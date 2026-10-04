namespace ServiceLib.Manager;

/// <summary>
///     Состояние обхода DPI.
/// </summary>
public enum EDpiBypassState
{
    /// <summary>Тумблер выключен.</summary>
    Disabled,

    /// <summary>ciadpi.exe работает, ядро выходит наружу через него.</summary>
    Running,

    /// <summary>bin\dpi\ciadpi.exe не найден.</summary>
    Missing,

    /// <summary>Не запустился (порт занят, файл не исполняется и т.п.).</summary>
    Failed,
}

/// <summary>
///     Авто-обход DPI: локальный SOCKS-прокси <c>bin\dpi\ciadpi.exe</c> (hufrea/byedpi, MIT).
///     Исходящие соединения ядра идут через него — DPI не может пересобрать SNI/Host
///     и заблокировать соединение. Драйверов и прав администратора не требуется,
///     системный прокси и реестр не трогаются, шифрование остаётся за ядром.
/// </summary>
public sealed class DpiBypassService
{
    private static readonly Lazy<DpiBypassService> _instance = new(() => new());
    public static DpiBypassService Instance => _instance.Value;

    private const string _tag = "DpiBypassService";

    /// <summary>Три попытки поднять ciadpi после аварийного падения за одну сессию работы ядра.</summary>
    private const int _maxRestarts = 3;

    /// <summary>Если процесс прожил дольше этого, счётчик перезапусков считается заново.</summary>
    private const long _stableRunMs = 15_000;

    /// <summary>Протоколы ядра Xray, которым локальный SOCKS не нужен.</summary>
    private static readonly HashSet<string> _skipXrayProtocols = new(StringComparer.OrdinalIgnoreCase)
    {
        "blackhole", "dns", "freedom", "loopback", "wireguard",
    };

    /// <summary>Типы outbounds sing-box, которым локальный SOCKS не нужен или он им вредит.</summary>
    private static readonly HashSet<string> _skipSingboxTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "direct", "block", "dns", "selector", "urltest", "hysteria2", "tuic", "wireguard",
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private long _startedAt;
    private int _restarts;
    private bool _stopping;

    /// <summary>
    ///     Обход был отключён на сессию после исчерпания перезапусков: без этого
    ///     каждая перезагрузка ядра поднимала бы ciadpi заново и снова падала.
    /// </summary>
    private bool _sessionBlocked;

    /// <summary>Перезагрузка уже запрошена (защита от цикла «упал → перезагрузка → упал»).</summary>
    private bool _reloadNotified;

    private DpiBypassService()
    {
    }

    public int Port { get; private set; }

    public EDpiBypassState State { get; private set; } = EDpiBypassState.Disabled;

    /// <summary>Подробность последнего перехода состояния (для лога).</summary>
    public string Detail { get; private set; } = string.Empty;

    /// <summary>Путь к ciadpi.exe, то есть bin\dpi\ciadpi.exe рядом с приложением.</summary>
    public static string ExePath => Utils.GetBinPath(Path.Combine("dpi", "ciadpi.exe"));

    public bool BinaryExists => File.Exists(ExePath);

    /// <summary>True, когда сгенерированный конфиг ядра нужно пустить через byedpi.</summary>
    public bool ShouldBypass => State == EDpiBypassState.Running && _process is { HasExited: false };

    /// <summary>Дескриптор процесса, чтобы CoreManager мог прикрепить его к своему job object.</summary>
    public nint ProcessHandle
    {
        get
        {
            try
            {
                return _process is { HasExited: false } ? _process.Handle : nint.Zero;
            }
            catch
            {
                return nint.Zero;
            }
        }
    }

    /// <summary>Короткая строка состояния для окна настроек.</summary>
    public string StatusText => State switch
    {
        EDpiBypassState.Running => string.Format(ResUI.DpiBypassStatusRunning, Port),
        EDpiBypassState.Missing => ResUI.DpiBypassStatusMissing,
        EDpiBypassState.Failed => ResUI.DpiBypassStatusFailed,
        _ => ResUI.DpiBypassStatusDisabled,
    };

    /// <summary>
    ///     Поднимает ciadpi, если тумблер включён, и гасит его, если выключен.
    ///     Вызывается до генерации конфига, чтобы <see cref="ShouldBypass" /> уже был известен.
    /// </summary>
    public async Task PrepareAsync(Config? config)
    {
        if (config?.CoreBasicItem?.EnableDpiBypass != true)
        {
            Stop();
            SetState(EDpiBypassState.Disabled, string.Empty);
            return;
        }

        if (ShouldBypass)
        {
            return;
        }

        if (_sessionBlocked)
        {
            SetState(EDpiBypassState.Failed, "session blocked");
            return;
        }

        _restarts = 0;
        await StartAsync();
    }

    /// <summary>
    ///     Внедряет в сгенерированный конфиг SOCKS-outbound на 127.0.0.1:&lt;port&gt; и
    ///     направляет через него все соединения с публичным адресом. Если обход не работает,
    ///     конфиг возвращается без изменений — ядро выходит напрямую.
    /// </summary>
    public string PatchConfig(ECoreType coreType, string content)
    {
        if (!ShouldBypass || content.IsNullOrEmpty())
        {
            return content;
        }

        try
        {
            if (JsonUtils.ParseJson(content) is not JsonObject configNode
                || configNode["outbounds"] is not JsonArray outbounds)
            {
                return content;
            }

            var patched = coreType == ECoreType.sing_box
                ? PatchSingbox(outbounds)
                : PatchXray(outbounds);

            return patched ? JsonUtils.Serialize(configNode) : content;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return content;
        }
    }

    /// <summary>Гасит ciadpi (если он был запущен нами).</summary>
    public void Stop()
    {
        var process = _process;
        if (process is null)
        {
            Port = 0;
            return;
        }

        _stopping = true;
        _process = null;
        Port = 0;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>Снимает ограничение сессии — обход можно попробовать запустить заново.</summary>
    public void ResetSession()
    {
        _sessionBlocked = false;
        _restarts = 0;
        _reloadNotified = false;
    }

    #region Запуск

    private async Task StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (ShouldBypass)
            {
                return;
            }

            var exe = ExePath;
            if (!File.Exists(exe))
            {
                SetState(EDpiBypassState.Missing, exe);
                return;
            }

            // Windows-рекомендация из README byedpi
            var port = Utils.GetFreePort(Global.DpiBypassPort);
            var arguments = $"-i {Global.Loopback} -p {port} --split 1+s --disorder 3+s";
            Process? process = null;
            try
            {
                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = arguments,
                        WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                    EnableRaisingEvents = true,
                };
                process.OutputDataReceived += OnOutputData;
                process.ErrorDataReceived += OnOutputData;
                process.Exited += OnExited;

                _stopping = false;
                if (!process.Start())
                {
                    SetState(EDpiBypassState.Failed, "start returned false");
                    return;
                }

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;
                _startedAt = Environment.TickCount64;

                // при занятом порте или нерабочем файле процесс уходит сразу
                await Task.Delay(300);
                if (process.HasExited)
                {
                    SetState(EDpiBypassState.Failed, $"exit code {process.ExitCode}");
                    return;
                }

                Port = port;
                _reloadNotified = false;
                SetState(EDpiBypassState.Running, string.Empty);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
                SetState(EDpiBypassState.Failed, ex.Message);
            }
            finally
            {
                if (State != EDpiBypassState.Running)
                {
                    if (ReferenceEquals(_process, process))
                    {
                        _process = null;
                    }
                    Port = 0;
                    process?.Dispose();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnOutputData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data.IsNullOrEmpty())
        {
            return;
        }
        Logging.SaveLog($"ciadpi: {e.Data}");
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (_stopping)
        {
            SetState(EDpiBypassState.Disabled, string.Empty);
            return;
        }

        _ = Task.Run(async () =>
        {
            if (Environment.TickCount64 - _startedAt > _stableRunMs)
            {
                _restarts = 0;
            }
            _restarts++;

            if (_restarts <= _maxRestarts)
            {
                Logging.SaveLog($"{_tag}: ciadpi.exe упал, перезапуск {_restarts}/{_maxRestarts}");
                await Task.Delay(500);
                await StartAsync();
            }

            if (ShouldBypass || _sessionBlocked)
            {
                return;
            }

            // Поднять не вышло: конфиг, уже отданный ядру, указывает на мёртвый SOCKS.
            // Отключаем обход на сессию и просим пересобрать конфиг — иначе весь трафик в никуда.
            _sessionBlocked = true;
            SetState(EDpiBypassState.Failed, "restarts exhausted");
            Logging.SaveLog($"{_tag}: обход DPI отключён на сессию, ядро перейдёт на прямое соединение");
            if (!_reloadNotified)
            {
                _reloadNotified = true;
                NoticeManager.Instance.Enqueue(ResUI.DpiBypassStatusFailed);
                AppEvents.ReloadRequested.Publish();
            }
        });
    }

    private void SetState(EDpiBypassState state, string detail)
    {
        var changed = State != state || Detail != detail;
        State = state;
        Detail = detail;
        if (changed)
        {
            Logging.SaveLog($"{_tag}: {state}{(detail.IsNullOrEmpty() ? string.Empty : $" ({detail})")}");
        }
    }

    #endregion Запуск

    #region Правка конфига

    private static bool PatchXray(JsonArray outbounds)
    {
        var patched = false;
        foreach (var outbound in outbounds.OfType<JsonObject>())
        {
            var protocol = outbound["protocol"]?.ToString() ?? string.Empty;
            if (_skipXrayProtocols.Contains(protocol))
            {
                continue;
            }

            // outbound уже чей-то, его цепочку не трогаем
            if ((outbound["streamSettings"] as JsonObject)?["sockopt"]?["dialerProxy"] is not null)
            {
                continue;
            }

            // адрес лежит либо плоско в settings.address (так Xray пишет vless/vmess/trojan/ss),
            // либо в settings.vnext / settings.servers (классический формат, socks/http)
            var settings = outbound["settings"] as JsonObject;
            var address = settings?["address"]?.ToString()
                ?? FirstAddress(settings?["vnext"] as JsonArray)
                ?? FirstAddress(settings?["servers"] as JsonArray)
                ?? string.Empty;
            if (address.IsNullOrEmpty() || Utils.IsPrivateNetwork(address))
            {
                continue;
            }

            var streamSettings = outbound["streamSettings"] as JsonObject ?? new JsonObject();
            var sockopt = streamSettings["sockopt"] as JsonObject ?? new JsonObject();
            sockopt["dialerProxy"] = Global.DpiBypassTag;
            streamSettings["sockopt"] = sockopt;
            outbound["streamSettings"] = streamSettings;

            if (streamSettings["xhttpSettings"]?["extra"]?["downloadSettings"] is JsonObject downloadSettings)
            {
                var downloadSockopt = downloadSettings["sockopt"] as JsonObject ?? new JsonObject();
                downloadSockopt["dialerProxy"] = Global.DpiBypassTag;
                downloadSettings["sockopt"] = downloadSockopt;
            }

            patched = true;
        }

        if (!patched)
        {
            return false;
        }

        if (outbounds.All(o => o?["tag"]?.ToString() != Global.DpiBypassTag))
        {
            outbounds.Add(new JsonObject
            {
                ["protocol"] = "socks",
                ["tag"] = Global.DpiBypassTag,
                ["settings"] = new JsonObject
                {
                    ["servers"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["address"] = Global.Loopback,
                            ["port"] = DpiBypassService.Instance.Port,
                        },
                    },
                },
                ["streamSettings"] = new JsonObject(),
            });
        }

        return true;
    }

    private static bool PatchSingbox(JsonArray outbounds)
    {
        var patched = false;
        foreach (var outbound in outbounds.OfType<JsonObject>())
        {
            var type = outbound["type"]?.ToString() ?? string.Empty;
            if (_skipSingboxTypes.Contains(type) || outbound["detour"] is not null)
            {
                continue;
            }

            var server = outbound["server"]?.ToString() ?? string.Empty;
            if (server.IsNullOrEmpty() || Utils.IsPrivateNetwork(server))
            {
                continue;
            }

            outbound["detour"] = Global.DpiBypassTag;
            patched = true;
        }

        if (!patched)
        {
            return false;
        }

        if (outbounds.All(o => o?["tag"]?.ToString() != Global.DpiBypassTag))
        {
            outbounds.Add(new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = Global.DpiBypassTag,
                ["server"] = Global.Loopback,
                ["server_port"] = DpiBypassService.Instance.Port,
            });
        }

        return true;
    }

    private static string? FirstAddress(JsonArray? array)
    {
        if (array is not { Count: > 0 } || array[0] is not JsonObject first)
        {
            return null;
        }

        var address = first["address"]?.ToString();
        return address.IsNullOrEmpty() ? null : address;
    }

    #endregion Правка конфига
}
