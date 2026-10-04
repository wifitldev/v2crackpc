using System.IO.Compression;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace v2rayN.Manager;

/// <summary>
///     Sends a crash report to the support endpoint over HTTPS.
///     Nothing secret is baked into the binary: /crash accepts anonymous
///     reports, the read token never leaves the server side.
///
///     Rules for this class:
///       * never throw — a failure to report a crash must not cause a second crash;
///       * never hang — a fixed 10 s network budget for the whole attempt, retry included;
///       * never mail, never upload the config file (it holds subscription URLs);
///       * the same crash is not re-sent more often than <see cref="Cooldown"/>.
/// </summary>
public static class CrashReportService
{
    /// <summary>The only transport: HTTPS. No SMTP fallback, no other host.</summary>
    private const string Endpoint = "https://bug.teodortech.ru/crash";

    private const string AppId = "v2crackN";

    /// <summary>Fallback device id, matches ConstItem.Hwid.</summary>
    private const string FallbackHwid = "8f42b9a1c3d7e056";

    /// <summary>Network budget for the whole report, retry included.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>First try plus one retry; the second one only gets the time that is left.</summary>
    private const int MaxAttempts = 2;

    /// <summary>Same crash again — not sooner than this.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    /// <summary>Field limits mirror the server's <c>fields</c> section.</summary>
    private const int MaxMsg = 8192;
    private const int MaxStack = 64 * 1024;
    private const int MaxExtra = 64 * 1024;
    private const int MaxOs = 256;
    private const int MaxHw = 1024;
    private const int MaxCfg = 64 * 1024;
    private const int MaxAttachment = 512 * 1024;

    private static int _sending;
    private static HttpClient? _client;

    /// <summary>
    ///     Builds and sends the report. Never throws, never blocks the UI
    ///     for longer than <see cref="RequestTimeout"/>.
    /// </summary>
    /// <param name="source">Which handler caught it (dispatcher / appdomain / task)</param>
    /// <param name="ex">Caught exception, may be null for non-exception fatal events</param>
    public static void Report(string source, Exception? ex)
    {
        try
        {
            if (Interlocked.Exchange(ref _sending, 1) == 1)
            {
                return; // already sending in another thread
            }

            try
            {
                var basis = BuildBasis(source, ex);
                if (!IsCooldownExpired(basis))
                {
                    return;
                }

                // Mark the attempt BEFORE sending, so a crash during send does not loop.
                WriteCooldown(basis);

                var payload = BuildPayload(source, ex);
                Send(payload);
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }
        catch
        {
            // Never let crash reporting itself throw.
        }
    }

    #region payload

    private static CrashPayload BuildPayload(string source, Exception? ex)
    {
        var stack = ex?.ToString() ?? string.Empty;

        return new CrashPayload
        {
            Ver = Utils.GetVersionInfo(),
            Kind = MapKind(source),
            App = AppId,
            Platform = "windows",
            Msg = BuildMessage(ex),
            Stack = stack,
            Hwid = ReadHwid(),
            Os = BuildOs(),
            Hw = BuildHardware(),
            Cfg = BuildConfig(),
            Extra = BuildExtra(),
            Files = BuildFiles(),
        };
    }

    /// <summary>
    ///     Maps the handler that fired to the report type shown in the panel.
    ///     The server keeps whatever string it gets (≤64 chars), so the mapping
    ///     is ours to own.
    /// </summary>
    private static string MapKind(string source)
    {
        return source switch
        {
            // the dispatcher caught it and the app keeps running
            "DispatcherUnhandledException" => "handled",
            // observed too late, the app keeps running
            "UnobservedTaskException" => "task",
            // process is about to die
            "AppDomainUnhandledException" => "unhandled",
            _ => "unhandled",
        };
    }

    private static string BuildMessage(Exception? ex)
    {
        if (ex == null)
        {
            return "Fatal crash without an exception object";
        }

        var msg = ex.GetType().Name;
        if (!string.IsNullOrEmpty(ex.Message))
        {
            msg += ": " + ex.Message.Split('\r', '\n')[0];
        }
        return msg;
    }

    /// <summary>Windows version + build + runtime + arch, cut to the server's limit.</summary>
    private static string BuildOs()
    {
        try
        {
            const string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
            var product = ReadRegValue(key, "ProductName")
                          ?? Environment.OSVersion.VersionString;
            var display = ReadRegValue(key, "DisplayVersion") ?? string.Empty;
            var build = ReadRegValue(key, "CurrentBuildNumber")
                        ?? Environment.OSVersion.Version.Build.ToString();

            var os = $"{product} {display} build {build}".Trim();
            os = string.Join(' ', os.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            os += " | " + RuntimeInformation.FrameworkDescription;
            os += " | " + RuntimeInformation.ProcessArchitecture;
            return Cut(os, MaxOs);
        }
        catch
        {
            return Cut(Environment.OSVersion.ToString(), MaxOs);
        }
    }

    /// <summary>CPU model, core count and total RAM.</summary>
    private static string BuildHardware()
    {
        try
        {
            const string cpuKey = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
            var cpu = ReadRegValue(cpuKey, "ProcessorNameString");
            cpu = string.IsNullOrWhiteSpace(cpu) ? "unknown CPU" : cpu.Trim();

            var cores = Environment.ProcessorCount;
            var ram = ReadTotalMemoryGb();
            var ramText = ram == "?" ? "RAM unknown" : $"{ram} GB RAM";

            return Cut($"{cpu} | {cores} logical cores | {ramText}", MaxHw);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadTotalMemoryGb()
    {
        try
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(ref status) && status.ullTotalPhys > 0)
            {
                var gb = status.ullTotalPhys / (1024d * 1024d * 1024d);
                return gb.ToString("0.#", CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // fall through
        }
        return "?";
    }

    /// <summary>
    ///     The user's configuration as the support needs to see it: which core
    ///     is running, which local ports, is TUN on, is the system proxy forced,
    ///     is fragmentation enabled. Never the config file itself - it carries
    ///     subscription URLs.
    /// </summary>
    private static string BuildConfig()
    {
        try
        {
            var app = AppManager.Instance;
            var cfg = app?.Config;
            if (cfg == null)
            {
                return "config=<not loaded>";
            }

            var sb = new StringBuilder();
            sb.Append("core=").Append(app.RunningCoreType);

            // The core serves SOCKS and HTTP (mixed) on ONE port: that port is
            // what GetLocalPort(socks) returns. Adding the enum offset to
            // EInboundProtocol.mixed would invent a port nobody listens on.
            var first = cfg.Inbound?.FirstOrDefault();
            sb.Append(" inbound=127.0.0.1:").Append(SafePort(() => app.GetLocalPort(EInboundProtocol.socks)));
            if (first?.SecondLocalPortEnabled == true)
            {
                sb.Append(",127.0.0.1:").Append(SafePort(() => app.GetLocalPort(EInboundProtocol.socks2)));
            }
            if (first?.AllowLANConn == true)
            {
                var lan = first.NewPort4LAN ? EInboundProtocol.socks3 : EInboundProtocol.socks;
                sb.Append(" lan=127.0.0.1:").Append(SafePort(() => app.GetLocalPort(lan)));
            }

            sb.Append(" sysProxy=").Append(cfg.SystemProxyItem?.SysProxyType ?? ESysProxyType.ForcedClear);
            sb.Append(" tun=").Append(cfg.TunModeItem?.EnableTun == true ? "on" : "off");
            sb.Append(" fragment=").Append(cfg.CoreBasicItem?.EnableFragment == true ? "on" : "off");
            sb.Append(" routing=").Append(cfg.RoutingBasicItem?.DomainStrategy ?? "-");
            sb.Append(" log=").Append(cfg.GuiItem?.EnableLog == true ? "on" : "off");
            sb.Append(" lang=").Append(cfg.UiItem?.CurrentLanguage ?? "-");
            sb.Append(" exe=").Append(Utils.GetExePath());

            return Cut(sb.ToString(), MaxCfg);
        }
        catch
        {
            return "config=<unavailable>";
        }
    }

    private static string SafePort(Func<int> getter)
    {
        try
        {
            return getter().ToString();
        }
        catch
        {
            return "-";
        }
    }

    /// <summary>Report header plus the tail of today's log.</summary>
    private static string BuildExtra()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"v2crackN crash report | {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"version: {Utils.GetVersionInfo()} | runtime: {Utils.GetVersion()}");
        sb.AppendLine($"exe:     {Utils.GetExePath()}");
        sb.AppendLine($"time:    {DateTime.Now:o}");
        sb.AppendLine();

        var tail = ReadLogTail(Utils.GetLogPath($"{DateTime.Now:yyyy-MM-dd}.txt"));
        if (tail.IsNotEmpty())
        {
            sb.AppendLine("=== log tail ===");
            sb.AppendLine(tail);
        }

        return Cut(sb.ToString(), MaxExtra);
    }

    /// <summary>
    ///     Today's log as an attachment when it is small enough.
    ///     The config file is deliberately never attached: it carries
    ///     subscription URLs and the user's server list.
    /// </summary>
    private static List<CrashFile>? BuildFiles()
    {
        try
        {
            var path = Utils.GetLogPath($"{DateTime.Now:yyyy-MM-dd}.txt");
            if (!File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length is 0 or > MaxAttachment)
            {
                return null;
            }

            return
            [
                new CrashFile
                {
                    Name = info.Name,
                    Data = Convert.ToBase64String(File.ReadAllBytes(path)),
                },
            ];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Last ~64 KB of today's log, so the report stays within sane limits.
    /// </summary>
    private static string ReadLogTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            const long max = 64 * 1024;
            var start = Math.Max(0, fs.Length - max);
            fs.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var text = reader.ReadToEnd();
            if (start > 0)
            {
                // avoid a half-cut first line
                var nl = text.IndexOfAny(['\r', '\n']);
                if (nl >= 0 && nl + 1 < text.Length)
                {
                    text = text[(nl + 1)..];
                }
            }
            return text;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string Cut(string value, int max)
    {
        return value.Length <= max ? value : value[..max];
    }

    private static string ReadHwid()
    {
        try
        {
            var hwid = AppManager.Instance?.Config?.ConstItem?.Hwid?.Trim();
            return !string.IsNullOrWhiteSpace(hwid) ? Cut(hwid!, 64) : FallbackHwid;
        }
        catch
        {
            return FallbackHwid;
        }
    }

    /// <summary>Reads one string value from HKLM; 64-bit view on a 64-bit OS.</summary>
    private static string? ReadRegValue(string keyPath, string name)
    {
        using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
            Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var key = baseKey.OpenSubKey(keyPath);
        return key?.GetValue(name) as string;
    }

    #endregion payload

    #region cooldown

    /// <summary>
    ///     Fingerprint of "what crashed" - kind + message + first 4000 chars of
    ///     the stack, exactly what the server hashes for deduplication. Two
    ///     different crashes can therefore go out inside the same minute.
    /// </summary>
    private static string BuildBasis(string source, Exception? ex)
    {
        var basis = MapKind(source) + "\n" + BuildMessage(ex) + "\n" + (ex?.ToString() ?? string.Empty);
        return Utils.GetMd5(basis);
    }

    private static string CooldownFile(string basis)
    {
        return Path.Combine(Path.GetTempPath(), "v2crackN.crashreport." + basis + ".lock");
    }

    private static bool IsCooldownExpired(string basis)
    {
        try
        {
            var file = CooldownFile(basis);
            if (!File.Exists(file))
            {
                return true;
            }

            var last = File.ReadAllText(file).Trim();
            if (!DateTime.TryParse(last, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
            {
                return true;
            }

            return DateTime.Now - when >= Cooldown;
        }
        catch
        {
            return true;
        }
    }

    private static void WriteCooldown(string basis)
    {
        try
        {
            File.WriteAllText(CooldownFile(basis), DateTime.Now.ToString("O"));
        }
        catch
        {
            // a leftover lock file is harmless
        }
    }

    #endregion cooldown

    #region transport

    /// <summary>
    ///     One gzip'd JSON body, up to two attempts inside a fixed time budget.
    ///     413/429/400 are the server saying "enough" - that is a normal outcome,
    ///     not an error, and it stops the retries.
    /// </summary>
    private static void Send(CrashPayload payload)
    {
        byte[] body;
        try
        {
            body = Compress(JsonSerializer.Serialize(payload, SerializerOptions));
        }
        catch
        {
            return;
        }

        var client = GetClient();
        if (client == null)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var remaining = RequestTimeout - watch.Elapsed;
            if (remaining < TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
                {
                    Content = new ByteArrayContent(body),
                };
                request.Content.Headers.ContentType = new("application/json");
                request.Content.Headers.ContentEncoding.Add("gzip");

                using var cts = new CancellationTokenSource(remaining);
                using var response = client.Send(request, cts.Token);
                var code = (int)response.StatusCode;

                if (code == 202)
                {
                    Log($"crash report accepted ({body.Length} bytes gzipped)");
                    return;
                }

                if (code is 400 or 413 or 429 or 431)
                {
                    // rejected / too large / rate limited - nothing to retry
                    return;
                }
            }
            catch (Exception ex)
            {
                Log($"crash report attempt {attempt} failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Logging must never influence the report itself: a failure here would
    ///     either swallow the "accepted" answer and re-send, or throw upward.
    /// </summary>
    private static void Log(string message)
    {
        try
        {
            Logging.SaveLog(message);
        }
        catch
        {
            // the report outcome does not depend on the log
        }
    }

    private static HttpClient? GetClient()
    {
        if (_client != null)
        {
            return _client;
        }

        try
        {
            // Direct connection on purpose: the report must not depend on the
            // system proxy the app may have set (or on a dead local core).
            var handler = new HttpClientHandler
            {
                UseProxy = false,
                UseDefaultCredentials = false,
            };

            var client = new HttpClient(handler)
            {
                Timeout = RequestTimeout,
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppId}/{Utils.GetVersionInfo()}");
            _client = client;
            return client;
        }
        catch
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static byte[] Compress(string json)
    {
        var raw = Encoding.UTF8.GetBytes(json);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            gz.Write(raw, 0, raw.Length);
        }
        return ms.ToArray();
    }

    #endregion transport

    #region wire format

    private sealed class CrashPayload
    {
        [JsonPropertyName("ver")] public string Ver { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "unhandled";
        [JsonPropertyName("app")] public string App { get; set; } = AppId;
        [JsonPropertyName("platform")] public string Platform { get; set; } = "windows";
        [JsonPropertyName("msg")] public string Msg { get; set; } = "";
        [JsonPropertyName("stack")] public string Stack { get; set; } = "";
        [JsonPropertyName("hwid")] public string Hwid { get; set; } = FallbackHwid;
        [JsonPropertyName("os")] public string Os { get; set; } = "";
        [JsonPropertyName("hw")] public string Hw { get; set; } = "";
        [JsonPropertyName("cfg")] public string Cfg { get; set; } = "";
        [JsonPropertyName("extra")] public string Extra { get; set; } = "";
        [JsonPropertyName("files")] public List<CrashFile>? Files { get; set; }
    }

    private sealed class CrashFile
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("data")] public string Data { get; set; } = "";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MemoryStatusEx()
        {
            dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    #endregion wire format
}
