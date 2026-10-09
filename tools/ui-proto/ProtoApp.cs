using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Handler.Builder;
using ServiceLib.Handler.SysProxy;
using ServiceLib.Helper;
using ServiceLib.Manager;
using ServiceLib.Models.Configs;
using ServiceLib.Models.Dto;
using ServiceLib.Models.Entities;
using ServiceLib.Services;

namespace UiProto;

public sealed record OpResult(bool Success, string Message)
{
    public static OpResult Good(string message = "") => new(true, message);
    public static OpResult Bad(string message) => new(false, message);
}

/// <summary>
/// Слой логики прототипа. Всё берётся из ServiceLib 1.1.2 (v2crackN),
/// здесь только склейка: инициализация, connect/disconnect, режим прокси,
/// настройки, список серверов и роутингов, статистика.
/// </summary>
public sealed class ProtoApp
{
    public static ProtoApp Instance { get; } = new();

    /// <summary>Служебные строки: вывод ядра, ход подключения, ошибки.</summary>
    public event Action<string>? Log;

    /// <summary>Дельта трафика за секунду (байты).</summary>
    public event Action<ServerSpeedItem>? Speed;

    public bool Initialized { get; private set; }
    public bool CoreUp { get; private set; }
    public bool TunnelMode { get; private set; }

    private long _sessionUp;
    private long _sessionDown;

    public Config Config => AppManager.Instance.Config;

    public int SocksPort => AppManager.Instance.GetLocalPort(EInboundProtocol.socks);

    public (long Up, long Down) SessionTotals => (_sessionUp, _sessionDown);

    private void Say(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            Log?.Invoke(message.Trim());
    }

    // ---------------------------------------------------------------- init

    public async Task<bool> InitAsync()
    {
        try
        {
            PayloadPrep.Ensure();

            AppManager.Instance.WindowDialog = new NoDialog();
            if (!AppManager.Instance.InitApp())
            {
                Say("InitApp failed");
                return false;
            }

            AppManager.Instance.InitComponents();

            var config = Config;

            // язык конфига — сразу для всех потоков, чтобы строки ServiceLib
            // (статусы теста) не шли на языке системы
            try
            {
                ApplyCulture(new System.Globalization.CultureInfo(config.UiItem.CurrentLanguage));
            }
            catch (System.Globalization.CultureNotFoundException)
            {
            }

            await ConfigHandler.InitBuiltinDNS(config);
            await ConfigHandler.InitBuiltinFullConfigTemplate(config);
            await ProfileExManager.Instance.Init();
            await CoreManager.Instance.Init(config, CoreOutput);
            await CertPemManager.Instance.Init(config);

            if (config.GuiItem.EnableStatistics || config.GuiItem.DisplayRealTimeSpeed)
            {
                await StatisticsManager.Instance.Init(config, StatsOutput);
            }

            TunnelMode = config.SystemProxyItem.SysProxyType == ESysProxyType.ForcedChange;
            Initialized = true;

            Say($"ready · socks {SocksPort} · dpi {(config.CoreBasicItem.EnableDpiBypass ? "on" : "off")}");

            await HealDefaultServerAsync(config);
            return true;
        }
        catch (Exception ex)
        {
            Say("init error: " + ex.Message);
            return false;
        }
    }

    private Task CoreOutput(bool notify, string message)
    {
        Say(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Если сохранённый IndexId отсутствует в нашей базе (конфиг сбрасывался),
    /// молча выбираем первый доступный сервер, чтобы коннект не падал.
    /// </summary>
    private async Task HealDefaultServerAsync(Config config)
    {
        try
        {
            if (await ConfigHandler.GetDefaultServer(config) is not null)
                return;

            var profiles = await AppManager.Instance.ProfileItems(string.Empty);
            var first = profiles?.FirstOrDefault();
            if (first is null)
                return;

            if (await ConfigHandler.SetDefaultServerIndex(config, first.IndexId) == 0)
                Say("selected server was missing → picked " + first.GetSummary());
        }
        catch (Exception ex)
        {
            Say("heal server error: " + ex.Message);
        }
    }

    private Task StatsOutput(ServerSpeedItem speed)
    {
        _sessionUp += speed.ProxyUp + speed.DirectUp;
        _sessionDown += speed.ProxyDown + speed.DirectDown;
        Speed?.Invoke(speed);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------- connect

    public async Task<OpResult> ConnectAsync()
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        var config = Config;
        var profile = await ConfigHandler.GetDefaultServer(config);
        if (profile is null)
            return OpResult.Bad("сервер не выбран");

        Say("build config · " + profile.GetSummary());

        var all = await CoreConfigContextBuilder.BuildAll(config, profile);
        var validator = all.CombinedValidatorResult;
        foreach (var warning in validator.Warnings)
            Say("warn: " + warning);

        if (!all.Success)
            return OpResult.Bad(string.Join("; ", validator.Errors));

        config.SystemProxyItem.SysProxyType = TunnelMode
            ? ESysProxyType.ForcedChange
            : ESysProxyType.ForcedClear;

        await Task.Run(async () =>
        {
            await CoreManager.Instance.LoadCore(all.MainResult.Context, all.PreSocksResult?.Context);
            await SysProxyHandler.UpdateSysProxy(config, false);
        });

        if (!await WaitForPortAsync(SocksPort, 8000))
        {
            CoreUp = false;
            Say("порт " + SocksPort + " не поднялся");
            return OpResult.Bad("ядро не подняло порт " + SocksPort);
        }

        CoreUp = true;
        _sessionUp = 0;
        _sessionDown = 0;
        Say($"connected · socks {SocksPort}");
        return OpResult.Good();
    }

    public async Task<OpResult> DisconnectAsync()
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        var config = Config;
        config.SystemProxyItem.SysProxyType = ESysProxyType.ForcedClear;
        await SysProxyHandler.UpdateSysProxy(config, false);
        await CoreManager.Instance.CoreStop();
        await ConfigHandler.SaveConfig(config);

        CoreUp = false;
        Say("disconnected");
        return OpResult.Good();
    }

    public async Task<bool> IsPortAliveAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(600);
            await client.ConnectAsync("127.0.0.1", SocksPort, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForPortAsync(int port, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(500);
                await client.ConnectAsync("127.0.0.1", port, cts.Token);
                if (client.Connected)
                    return true;
            }
            catch
            {
                // ещё не слушает
            }

            await Task.Delay(300);
        }

        return false;
    }

    // --------------------------------------------------------------- mode

    /// <summary>
    /// Сохранить настройки немедленно, с повторами. SaveConfig глотает исключения
    /// и возвращает -1 — без повтора изменение остаётся только в памяти, и после
    /// перезапуска настройка откатывается к прежнему значению из файла.
    /// </summary>
    private async Task<OpResult> SaveSettingsAsync(Config config)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (await ConfigHandler.SaveConfig(config) == 0)
                return OpResult.Good();

            await Task.Delay(60 * attempt);
        }

        Say("не удалось сохранить настройки в конфиг");
        return OpResult.Bad("не удалось сохранить настройки");
    }

    /// <summary>Tunnel = системный прокси включён, Proxy = только локальный порт.</summary>
    public async Task<OpResult> SetTunnelModeAsync(bool tunnel)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        TunnelMode = tunnel;
        var config = Config;

        // Системный прокси включаем только когда ядро реально поднято,
        // иначе вся система уйдёт в мёртвый порт. Желаемый режим запоминается
        // и применяется при следующем подключении (см. ConnectAsync).
        config.SystemProxyItem.SysProxyType = tunnel && CoreUp
            ? ESysProxyType.ForcedChange
            : ESysProxyType.ForcedClear;

        await SysProxyHandler.UpdateSysProxy(config, false);
        var saved = await SaveSettingsAsync(config);

        Say(tunnel
            ? (CoreUp
                ? "system proxy → 127.0.0.1:" + SocksPort
                : "system proxy: будет включён при подключении")
            : "system proxy → off");
        return saved;
    }

    // ----------------------------------------------------------- settings

    public async Task<OpResult> SetAutoRunAsync(bool enabled)
    {
        var config = Config;
        config.GuiItem.AutoRun = enabled;
        await AutoStartupHandler.UpdateTask(config);
        var saved = await SaveSettingsAsync(config);
        Say(enabled ? "autostart on" : "autostart off");
        return saved;
    }

    public async Task<OpResult> SetDpiBypassAsync(bool enabled)
    {
        var config = Config;
        config.CoreBasicItem.EnableDpiBypass = enabled;
        if (enabled)
            DpiBypassService.Instance.ResetSession();

        var saved = await SaveSettingsAsync(config);
        Say("bypass dpi " + (enabled ? "on" : "off") + " · " + DpiBypassService.Instance.StatusText);
        return saved;
    }

    /// <summary>Стек TUN (gVisor / системный): пишется в TunModeItem.Stack.</summary>
    public async Task<OpResult> SetTunStackAsync(bool gvisor)
    {
        var config = Config;
        config.TunModeItem.Stack = gvisor ? "gvisor" : "system";
        var saved = await SaveSettingsAsync(config);
        Say("tun stack → " + config.TunModeItem.Stack);
        return saved;
    }

    public async Task<OpResult> SetSocksPortAsync(int port)
    {
        if (port is < 1024 or > 65535)
            return OpResult.Bad("порт должен быть 1024-65535");

        var config = Config;
        var inbound = config.Inbound?.FirstOrDefault(t => t.Protocol == "socks");
        if (inbound is null)
            return OpResult.Bad("socks inbound не найден");

        if (inbound.LocalPort == port)
            return OpResult.Good();

        inbound.LocalPort = port;
        var saved = await SaveSettingsAsync(config);
        if (!saved.Success)
            return saved;

        AppManager.Instance.Reset();
        PayloadPrep.SaveWantedPort(port);
        Say("local port → " + port);

        return CoreUp ? await ConnectAsync() : OpResult.Good();
    }

    /// <summary>Смена темы: пишет UiItem.CurrentTheme и сразу перекрашивает палитру.</summary>
    public async Task<OpResult> SetThemeAsync(string theme)
    {
        // канонический вид значения (Dark / Light / FollowSystem) — в конфиге
        // должно лежать ровно то, что сравнивает меню и читает старт приложения
        theme = theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light"
              : theme.Equals("FollowSystem", StringComparison.OrdinalIgnoreCase) ? "FollowSystem"
              : "Dark";

        var config = Config;

        // пишем всегда, даже если значение «совпадает» с памятью: файл и память
        // могли разойтись (пропущенная запись, чужая копия конфига) — только
        // безусловная запись гарантирует, что перезапуск покажет выбранную тему
        config.UiItem.CurrentTheme = theme;
        var saved = await SaveSettingsAsync(config);

        ThemeManager.Apply(theme);
        Say("theme → " + theme);
        return saved;
    }

    /// <summary>
    /// Смена языка: пишет UiItem.CurrentLanguage и меняет культуру потока —
    /// ровно то, что делает AppManager.InitApp при старте и ThemeSettingViewModel
    /// в основном приложении. Сообщения ServiceLib локализуются сразу.
    /// </summary>
    public async Task<OpResult> SetLanguageAsync(string code)
    {
        System.Globalization.CultureInfo culture;
        try
        {
            culture = new System.Globalization.CultureInfo(code);
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return OpResult.Bad("unknown language: " + code);
        }

        var config = Config;
        config.UiItem.CurrentLanguage = code;
        var saved = await SaveSettingsAsync(config);

        ApplyCulture(culture);

        Say("language → " + code);
        return saved;
    }

    /// <summary>
    /// Культура всех потоков: строки ResUI («Тест завершён», «Тестирование…»)
    /// формируются в фоновых потоках SpeedtestService — им нужен Default-культура,
    /// иначе они берут язык системы.
    /// </summary>
    private static void ApplyCulture(System.Globalization.CultureInfo culture)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Threading.Thread.CurrentThread.CurrentUICulture = culture;
    }

    // ------------------------------------------------- profiles / routing

    public Task<List<ProfileItem>?> ProfilesAsync() => AppManager.Instance.ProfileItems(string.Empty);

    public Task<ProfileItem?> CurrentProfileAsync() => ConfigHandler.GetDefaultServer(Config);

    public async Task<OpResult> SelectProfileAsync(string indexId)
    {
        if (await ConfigHandler.SetDefaultServerIndex(Config, indexId) != 0)
            return OpResult.Bad("не удалось выбрать сервер");

        var profile = await ConfigHandler.GetDefaultServer(Config);
        Say("server → " + (profile?.Remarks ?? indexId));

        return CoreUp ? await ConnectAsync() : OpResult.Good();
    }

    public Task<List<RoutingItem>?> RoutingsAsync() => AppManager.Instance.RoutingItems();

    // --------------------------------------------------------------- import

    /// <summary>
    /// Импорт подписки: добавляет SubItem (ConfigHandler.AddSubItem) и сразу
    /// подтягивает из неё серверы через SubscriptionHandler.UpdateProcess —
    /// ровно тот же путь, что и автообновление в TaskManager.
    /// </summary>
    public async Task<OpResult> AddSubscriptionAsync(string url)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        url = url?.Trim() ?? string.Empty;
        if (url.Length == 0)
            return OpResult.Bad("URL пустой");

        var config = Config;
        if (await ConfigHandler.AddSubItem(config, url) != 0)
            return OpResult.Bad("invalid subscription url");

        await ConfigHandler.SaveConfig(config);
        Say("subscription added · " + url);

        var sub = (await AppManager.Instance.SubItems())?.FirstOrDefault(t => t.Url == url);
        if (sub is null)
            return OpResult.Good("subscription saved");

        await SubscriptionHandler.UpdateProcess(config, sub.Id, CoreUp, (_, msg) =>
        {
            if (!string.IsNullOrWhiteSpace(msg))
                Say(msg.Trim());
            return Task.CompletedTask;
        });

        var count = (await AppManager.Instance.ProfileItems(sub.Id))?.Count ?? 0;
        return count > 0
            ? OpResult.Good($"loaded {count} servers")
            : OpResult.Bad("подписка сохранена, но серверы не загрузились");
    }

    /// <summary>
    /// Обновление всех подписок — тот же SubscriptionHandler.UpdateProcess,
    /// что и при автообновлении; после обновления список серверов перечитывается.
    /// </summary>
    public async Task<OpResult> UpdateSubscriptionsAsync()
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        var config = Config;
        var subs = await AppManager.Instance.SubItems() ?? [];
        if (subs.Count == 0)
            return OpResult.Bad("нет подписок");

        var ok = 0;
        foreach (var sub in subs)
        {
            try
            {
                await SubscriptionHandler.UpdateProcess(config, sub.Id, CoreUp, (_, msg) =>
                {
                    if (!string.IsNullOrWhiteSpace(msg))
                        Say(msg.Trim());
                    return Task.CompletedTask;
                });
                ok++;
            }
            catch (Exception ex)
            {
                Say("subscription update failed · " + ex.Message);
            }
        }

        var total = (await AppManager.Instance.ProfileItems(string.Empty))?.Count ?? 0;
        return ok > 0
            ? OpResult.Good($"обновлено {ok}/{subs.Count} · серверов: {total}")
            : OpResult.Bad("подписки не обновились");
    }

    // ------------------------------------------------------------- speedtest

    /// <summary>
    /// Результат пинга: indexId + текст (мс, ошибка или служебное сообщение).
    /// Пустой indexId = прогон завершён.
    /// </summary>
    public event Action<string, string>? PingUpdate;

    private SpeedtestService? _speedtest;

    private SpeedtestService Speedtest => _speedtest ??= new SpeedtestService(Config, r =>
    {
        var id = r.IndexId ?? string.Empty;
        var text = r.Delay ?? string.Empty;
        PingUpdate?.Invoke(id, text);
        if (id.Length == 0 && !string.IsNullOrWhiteSpace(text))
            Say(text.Trim());
        return Task.CompletedTask;
    });

    /// <summary>Прогон одного вида теста: Tcping / Realping / UdpTest.</summary>
    public Task PingAsync(List<ProfileItem> items, ESpeedActionType action = ESpeedActionType.Tcping)
    {
        if (!Initialized || items is null || items.Count == 0)
            return Task.CompletedTask;

        return Speedtest.RunLoop(action, items);
    }

    /// <summary>Отмена идущего прогона: все свои фазы (HEAD / ICMP) делят этот CTS.</summary>
    private CancellationTokenSource? _customPingCts;

    /// <summary>
    /// «via Proxy HEAD»: HTTP HEAD через локальный speedtest-прокси каждого сервера —
    /// тот же туннель, что у GET-пинга ServiceLib, но запрос HEAD (как в Happ).
    /// Протокол событий прежний: (indexId, мс) по серверам и ровно одно ("",
    /// завершение) в конце — MainWindow по нему закрывает фазу.
    /// </summary>
    public async Task PingHeadAsync(List<ProfileItem> items)
    {
        if (!Initialized || items is null || items.Count == 0)
            return;

        var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _customPingCts, cts);
        try { prev?.Cancel(); } catch { /* прошлая фаза уже отменена */ }

        ProcessService? ps = null;
        try
        {
            var selecteds = await BuildTestItemsAsync(items);
            if (selecteds.Count == 0)
                return;

            ps = await CoreManager.Instance.LoadCoreConfigSpeedtest(selecteds);
            if (ps is null)
            {
                // ядро не поднялось — каждому серверу честный -1
                foreach (var it in selecteds)
                    PingUpdate?.Invoke(it.IndexId ?? "", "-1");
                return;
            }

            // время на прогрев локальных входов — как в RunRealPingAsync ServiceLib
            await Task.Delay(1000, cts.Token);

            var url = Config.SpeedTestItem.SpeedPingTestUrl;
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, selecteds.Count),
                CancellationToken = cts.Token,
            };

            await Parallel.ForEachAsync(selecteds, options, async (it, innerCt) =>
            {
                if (!it.AllowTest)
                {
                    PingUpdate?.Invoke(it.IndexId ?? "", "-1");
                    return;
                }

                var ms = -1;
                try
                {
                    ms = await HeadPingOnceAsync(url, it.Port, innerCt);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // сервер не ответил → -1, как у GetRealPingTime
                }

                PingUpdate?.Invoke(it.IndexId ?? "", ms.ToString());
            });
        }
        catch (OperationCanceledException)
        {
            // отмена — фазу закрываем в finally
        }
        catch (Exception ex)
        {
            Say("head ping: " + ex.Message);
        }
        finally
        {
            if (ps is not null)
            {
                try { await ps.StopAsync(); } catch { /* ядро могло уже упасть */ }
            }

            if (ReferenceEquals(_customPingCts, cts)) _customPingCts = null;
            try { cts.Dispose(); } catch { }

            // ровно одно завершение фазы — счётчик фаз в MainWindow
            PingUpdate?.Invoke("", "head ping done");
        }
    }

    /// <summary>HTTP HEAD через socks-прокси сервера: 2 попытки, минимум положительных.</summary>
    private static async Task<int> HeadPingOnceAsync(string url, int port, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource();
        timeoutCts.CancelAfter(Global.LocalFetch);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var token = linkedCts.Token;

        using var client = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"),
            UseProxy = true,
            ConnectTimeout = Global.LocalFetch,
        });

        var times = new List<int>(2);
        for (var i = 0; i < 2; i++)
        {
            var timer = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, token);
            timer.Stop();
            times.Add((int)timer.Elapsed.TotalMilliseconds);
            await Task.Delay(100, token);
        }

        return times.Where(t => t > 0).OrderBy(t => t).DefaultIfEmpty(-1).First();
    }

    /// <summary>
    /// «ICMP»: прямой эхо-запрос к адресу сервера — через прокси ICMP невозможен.
    /// Протокол событий тот же, что у HEAD.
    /// </summary>
    public async Task PingIcmpAsync(List<ProfileItem> items)
    {
        if (!Initialized || items is null || items.Count == 0)
            return;

        var cts = new CancellationTokenSource();
        var prev = Interlocked.Exchange(ref _customPingCts, cts);
        try { prev?.Cancel(); } catch { /* прошлая фаза уже отменена */ }

        try
        {
            var targets = items
                .Where(t => !string.IsNullOrEmpty(t.IndexId) && !string.IsNullOrEmpty(t.Address))
                .ToList();

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, targets.Count),
                CancellationToken = cts.Token,
            };

            await Parallel.ForEachAsync(targets, options, async (it, innerCt) =>
            {
                innerCt.ThrowIfCancellationRequested();

                var ms = -1;
                try
                {
                    using var pinger = new System.Net.NetworkInformation.Ping();
                    var reply = await pinger.SendPingAsync(it.Address!, (int)Global.LocalFetch.TotalMilliseconds);
                    if (reply.Status == IPStatus.Success)
                        ms = (int)reply.RoundtripTime;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // узел не ответил или DNS не разрешился → -1
                }

                PingUpdate?.Invoke(it.IndexId!, ms.ToString());
            });
        }
        catch (OperationCanceledException)
        {
            // отмена — фазу закрываем в finally
        }
        catch (Exception ex)
        {
            Say("icmp ping: " + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_customPingCts, cts)) _customPingCts = null;
            try { cts.Dispose(); } catch { }

            // ровно одно завершение фазы — счётчик фаз в MainWindow
            PingUpdate?.Invoke("", "icmp ping done");
        }
    }

    /// <summary>Список ServerTestItem по образцу приватного GetClearItem из SpeedtestService.</summary>
    private async Task<List<ServerTestItem>> BuildTestItemsAsync(List<ProfileItem> items)
    {
        var list = new List<ServerTestItem>(items.Count);
        var ids = items
            .Where(t => !string.IsNullOrEmpty(t.IndexId)
                        && t.ConfigType != EConfigType.Custom
                        && (t.ConfigType.IsComplexType() || t.Port > 0))
            .Select(t => t.IndexId!)
            .ToList();
        var map = await AppManager.Instance.GetProfileItemsByIndexIdsAsMap(ids);

        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (string.IsNullOrEmpty(it.IndexId))
                continue;

            if (it.ConfigType == EConfigType.Custom)
                continue;

            if (!it.ConfigType.IsComplexType() && it.Port <= 0)
                continue;

            var profile = map.GetValueOrDefault(it.IndexId, it);
            list.Add(new ServerTestItem
            {
                IndexId = it.IndexId,
                Address = it.Address,
                Port = it.Port,
                ConfigType = it.ConfigType,
                QueueNum = i,
                Profile = profile,
                CoreType = AppManager.Instance.GetCoreType(profile, it.ConfigType),
            });
        }

        return list;
    }

    /// <summary>Переписать задержку в профиле (показываем приоритетный замер, а не последний).</summary>
    public void SetPingDelay(string indexId, int ms)
    {
        if (string.IsNullOrEmpty(indexId)) return;
        ProfileExManager.Instance.SetTestDelay(indexId, ms);
    }

    /// <summary>Сохранить задержки на диск — зовём один раз по завершении всех фаз.</summary>
    public void CommitPingDelays() => _ = ProfileExManager.Instance.SaveTo();

    /// <summary>Остановить текущий прогон пинга — все фазы: ServiceLib и свои (HEAD / ICMP).</summary>
    public void CancelPing()
    {
        _speedtest?.ExitLoop();
        try { _customPingCts?.Cancel(); } catch { /* уже отменён */ }
    }

    /// <summary>Импорт серверов из ссылок (ss:// vmess:// vless:// trojan:// …) или base64-списка.</summary>
    public async Task<OpResult> AddServerAsync(string data)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        data = data?.Trim() ?? string.Empty;
        if (data.Length == 0)
            return OpResult.Bad("нет данных для импорта");

        var added = await ConfigHandler.AddBatchServers(Config, data, string.Empty, false);
        if (added < 1)
            return OpResult.Bad("no valid servers found");

        await ConfigHandler.SaveConfig(Config);
        Say($"imported {added} server(s)");
        return OpResult.Good($"imported {added} server(s)");
    }

    /// <summary>Удаление сервера из списка (ConfigHandler.RemoveServers).</summary>
    public async Task<OpResult> RemoveServerAsync(ProfileItem item)
    {
        if (await ConfigHandler.RemoveServers(Config, [item]) != 0)
            return OpResult.Bad("remove failed");

        Say("server removed · " + (item.Remarks ?? item.Address));
        return OpResult.Good();
    }

    /// <summary>Все группы (подписки и пользовательские) в порядке сортировки.</summary>
    public Task<List<SubItem>?> GroupsAsync() => AppManager.Instance.SubItems();

    /// <summary>
    /// Создание пользовательской группы: SubItem без URL (ConfigHandler.AddSubItem) —
    /// тот же механизм, что и у подписок, только без адреса.
    /// </summary>
    public async Task<OpResult> CreateGroupAsync(string name)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return OpResult.Bad("group name is empty");

        var rc = await ConfigHandler.AddSubItem(Config, new SubItem
        {
            Remarks = name,
            Enabled = true,
        });
        if (rc != 0)
            return OpResult.Bad("group not created");

        await ConfigHandler.SaveConfig(Config);
        Say("group created · " + name);
        return OpResult.Good("group created");
    }

    /// <summary>Метка закреплённой группы в SubItem.Memo — закреплённые всплывают наверх списка.</summary>
    public const string PinnedMemo = "pinned";

    /// <summary>Переименование группы: SubItem.Remarks пишется напрямую (SQLiteHelper.ReplaceAsync).</summary>
    public async Task<OpResult> RenameGroupAsync(string id, string name)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return OpResult.Bad("group name is empty");
        if (id == Global.PermanentSubId)
            return OpResult.Bad("permanent group can't be renamed");

        var item = await AppManager.Instance.GetSubItem(id);
        if (item is null)
            return OpResult.Bad("group not found");

        item.Remarks = name;
        if (await SQLiteHelper.Instance.ReplaceAsync(item) <= 0)
            return OpResult.Bad("rename failed");

        Say("group renamed · " + name);
        return OpResult.Good("group renamed");
    }

    /// <summary>Переименование сервера: ProfileItem.Remarks пишется напрямую (SQLiteHelper.ReplaceAsync).</summary>
    public async Task<OpResult> RenameServerAsync(string indexId, string name)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return OpResult.Bad("server name is empty");

        var item = await AppManager.Instance.GetProfileItem(indexId);
        if (item is null)
            return OpResult.Bad("server not found");

        item.Remarks = name;
        if (await SQLiteHelper.Instance.ReplaceAsync(item) <= 0)
            return OpResult.Bad("rename failed");

        Say("server renamed · " + name);
        return OpResult.Good("server renamed");
    }

    /// <summary>
    /// Закрепить/открепить группу: метка в SubItem.Memo, в дереве
    /// закреплённые группы идут первыми (RefreshServersTreeAsync).
    /// </summary>
    public async Task<OpResult> TogglePinGroupAsync(string id)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        var item = await AppManager.Instance.GetSubItem(id);
        if (item is null)
            return OpResult.Bad("group not found");

        var pinned = item.Memo == PinnedMemo;
        item.Memo = pinned ? null : PinnedMemo;
        if (await SQLiteHelper.Instance.ReplaceAsync(item) <= 0)
            return OpResult.Bad("pin failed");

        Say((pinned ? "unpinned · " : "pinned · ") + item.Remarks);
        return OpResult.Good(pinned ? "group unpinned" : "group pinned");
    }

    /// <summary>
    /// Удаление группы (ConfigHandler.DeleteSubItem). Пользовательская группа
    /// (без URL) сначала отдаёт свои серверы в «Server list», чтобы они не пропали;
    /// серверы подписки удаляются вместе с ней — как задумано в ServiceLib.
    /// </summary>
    public async Task<OpResult> DeleteGroupAsync(string id)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");
        if (id == Global.PermanentSubId)
            return OpResult.Bad("permanent group can't be removed");

        var item = await AppManager.Instance.GetSubItem(id);
        if (item is null)
            return OpResult.Bad("group not found");

        if (string.IsNullOrEmpty(item.Url))
        {
            var profiles = await AppManager.Instance.ProfileItems(id) ?? [];
            if (profiles.Count > 0)
                await ConfigHandler.MoveToGroup(Config, profiles, string.Empty);
        }

        if (await ConfigHandler.DeleteSubItem(Config, id) != 0)
            return OpResult.Bad("delete failed");

        await ConfigHandler.SaveConfig(Config);
        Say("group removed · " + item.Remarks);
        return OpResult.Good("group removed");
    }

    /// <summary>Перемещение сервера в другую группу (ConfigHandler.MoveToGroup).</summary>
    public async Task<OpResult> MoveServerAsync(ProfileItem item, string groupId)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        if (await ConfigHandler.MoveToGroup(Config, [item], groupId) != 0)
            return OpResult.Bad("move failed");

        var group = (await AppManager.Instance.SubItems())?.FirstOrDefault(t => t.Id == groupId);
        Say($"moved to {group?.Remarks ?? "group"} · {item.Remarks ?? item.Address}");
        return OpResult.Good("server moved");
    }

    public async Task<OpResult> SelectRoutingAsync(string id)
    {
        var item = await AppManager.Instance.GetRoutingItem(id);
        if (item is null)
            return OpResult.Bad("роутинг не найден");

        if (await ConfigHandler.SetDefaultRouting(Config, item) != 0)
            return OpResult.Bad("не удалось сменить роутинг");

        Say("routing → " + item.Remarks);

        return CoreUp ? await ConnectAsync() : OpResult.Good();
    }

    /// <summary>HTTP-прокси — всегда SOCKS+1 (второй mixed-вход в конфиге).</summary>
    public int HttpPort => Math.Min(SocksPort + 1, 65535);

    /// <summary>Правила пресета: RuleSet (JSON) → список RulesItem.</summary>
    public List<RulesItem> GetRoutingRules(RoutingItem item)
        => JsonUtils.Deserialize<List<RulesItem>>(item?.RuleSet) ?? [];

    /// <summary>
    /// Новый пресет роутинга: пустой список правил, Sort — в конец списка.
    /// Создаётся через ConfigHandler.SaveRoutingItem — как в основном приложении.
    /// </summary>
    public async Task<OpResult> CreateRoutingAsync(string name)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return OpResult.Bad("preset name is empty");

        var items = await AppManager.Instance.RoutingItems() ?? [];
        var item = new RoutingItem
        {
            Id = Utils.GetGuid(false),
            Remarks = name,
            Enabled = true,
            Sort = items.Count > 0 ? items.Max(t => t.Sort) + 1 : 0,
            RuleSet = "[]",
            RuleNum = 0,
        };

        if (await ConfigHandler.SaveRoutingItem(Config, item) != 0)
            return OpResult.Bad("preset not created");

        Say("routing created · " + name);
        return OpResult.Good(item.Id);
    }

    /// <summary>Сохранение списка правил в пресет (RuleSet + RuleNum, как SaveRoutingAsync в ServiceLib).</summary>
    public async Task<OpResult> SaveRoutingRulesAsync(RoutingItem item, List<RulesItem> rules)
    {
        if (item is null)
            return OpResult.Bad("preset not selected");

        rules ??= [];
        foreach (var rule in rules)
            rule.Id = Utils.GetGuid(false);

        item.RuleNum = rules.Count;
        item.RuleSet = JsonUtils.Serialize(rules, false);

        if (await ConfigHandler.SaveRoutingItem(Config, item) != 0)
            return OpResult.Bad("rules not saved");

        Say($"routing rules · {item.Remarks}: {rules.Count}");
        return OpResult.Good();
    }

    /// <summary>Удаление пресета (ConfigHandler.RemoveRoutingItem).</summary>
    public async Task<OpResult> DeleteRoutingAsync(string id)
    {
        if (!Initialized)
            return OpResult.Bad("не инициализировано");

        var item = await AppManager.Instance.GetRoutingItem(id);
        if (item is null)
            return OpResult.Bad("preset not found");
        if (item.IsActive)
            return OpResult.Bad("active preset can't be removed");

        await ConfigHandler.RemoveRoutingItem(item);
        Say("routing removed · " + item.Remarks);
        return OpResult.Good("preset removed");
    }

    // --------------------------------------------------------------- exit

    public async Task ExitAsync()
    {
        if (!Initialized)
            return;

        try
        {
            await AppManager.Instance.AppExitAsync(false);
            Say("exit · core stopped, system proxy cleared");
        }
        catch (Exception ex)
        {
            Say("exit error: " + ex.Message);
        }
    }
}
