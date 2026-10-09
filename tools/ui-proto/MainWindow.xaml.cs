using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ServiceLib;
using ServiceLib.Common;
using ServiceLib.Enums;
using ServiceLib.Manager;
using ServiceLib.Models.Dto;
using ServiceLib.Models.Entities;

namespace UiProto;

public partial class MainWindow : Window
{
    /// <summary>Цвет ошибки — следует за темой (тёмная/светлая палитра).</summary>
    private Brush ErrorBrush => (Brush)FindResource(ThemeManager.UsesLight ? "ErrorLight" : "ErrorDark");

    /// <summary>Idle-точка из палитры — следует за темой.</summary>
    private Brush IdleDot => (Brush)FindResource("5B6478");

    private static readonly string[] RouteIcons =
    [
        "\U0001F30D", "\U0001F1F7\U0001F1FA", "\U0001F4F1", "\U0001F3AC",
        "\U0001F3AE", "\U0001F916", "\U0001F6D1", "\U0001F310",
    ];

    private readonly DispatcherTimer _timer;

    private bool _initDone;
    private bool _connected;
    private bool _busy;
    private bool _syncing;
    private bool _probeBusy;
    private bool _exitDone;
    private bool _forceExit;
    private TrayIcon? _tray;
    private bool _pendingConnect;
    private bool _startTunnel;
    private bool _autotest;
    private bool _testPing;
    private bool _testUpdateSubs;
    private bool _testPingOne;
    private bool _openPicker;
    private string? _openImport;

    /// <summary>Скриншот-режим (--groupactions): hover-кнопки групп показаны без наведения.</summary>
    public bool ForceActions { get; private set; }

    /// <summary>--groupactions=demo|clean: временная группа для проверки групповых действий.</summary>
    private string? _groupActionsDemo;

    /// <summary>--rename: открыть popup переименования (скриншот).</summary>
    private bool _openRename;
    private bool _openPreset;

    /// <summary>--toast: показать mode-toast (скриншот).</summary>
    private bool _showToast;

    private const string DemoGroupName = "demo group";

    /// <summary>Тестовая vmess-ссылка для шага "import server" в автотесте.</summary>
    private const string TestVmessLink =
        "vmess://eyJ2IjoiMiIsInBzIjoiaW1wb3J0LXRlc3QiLCJhZGQiOiIxMjcuMC4wLjEiLCJwb3J0IjoiODAiLCJpZCI6ImFiY2RlZjAxLTIzNDUtNjc4OS1hYmNkLWVmMDEyMzQ1Njc4OSIsImFpZCI6IjAiLCJzY3kiOiJhdXRvIiwibmV0Ijoid3MiLCJ0eXBlIjoibm9uZSIsImhvc3QiOiIiLCJwYXRoIjoiLyIsInRscyI6IiIsInNuaSI6IiIsImFscG4iOiIifQ==";
    private string? _previewTheme;
    private string? _previewLang;
    private string? _setTheme;
    private string? _setLang;
    private string? _setPing;
    private string? _openMenu;
    private bool _tunnelUi;
    private string? _busyText;
    private int _seconds;
    private int _probeFails;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();

        SourceInitialized += (_, _) => ApplyDarkTitleBar();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;

        // язык выбран в App.OnStartup до разбора XAML — здесь доводим дерево окна
        Loc.Apply(this);

        ApplyStartupArgs(Environment.GetCommandLineArgs());
        SetMode(_startTunnel);

        // после layout половина строки может отличаться от дефолта — ползунок подгоняем
        ModeGrid.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width <= 0) return;
            var half = e.NewSize.Width / 2;
            if (double.IsNaN(ModeThumb.Width) || Math.Abs(ModeThumb.Width - half) > 0.5)
                SetMode(_tunnelUi);
        };
    }

    // CLI: --tab=connect|routing|settings, --connected, --mode=tunnel, --autotest
    //      --picker, --import=server|sub, --theme=, --menu=theme|lang, --groupactions
    private void ApplyStartupArgs(string[] args)
    {
        foreach (var raw in args)
        {
            var a = raw.Trim().ToLowerInvariant();
            if (a.StartsWith("--tab="))
            {
                var eq = raw.IndexOf('=');
                var tab = eq > 0 ? raw[(eq + 1)..] : "";
                if (tab == "routing") NavRouting.IsChecked = true;
                else if (tab == "settings") NavSettings.IsChecked = true;
                else if (tab == "logs") NavLogs.IsChecked = true;
                else NavConnect.IsChecked = true;
            }
            else if (a == "--groupactions")
            {
                // скриншот-режим: hover-кнопки групп видны без наведения
                ForceActions = true;
            }
            else if (a.StartsWith("--groupactions="))
            {
                // демо-группа для проверки rename/pin/delete: demo | clean
                ForceActions = true;
                var eq = raw.IndexOf('=');
                _groupActionsDemo = eq > 0 ? raw[(eq + 1)..] : null;
            }
            else if (a == "--rename")
            {
                // открыть popup переименования для скриншота
                ForceActions = true;
                _openRename = true;
            }
            else if (a == "--preset")
            {
                _openPreset = true;
            }
            else if (a == "--toast")
            {
                // показать mode-toast для скриншота
                _showToast = true;
            }
            else if (a == "--connected")
            {
                _pendingConnect = true;
            }
            else if (a == "--mode=tunnel")
            {
                _startTunnel = true;
            }
            else if (a == "--autotest")
            {
                _autotest = true;
            }
            else if (a == "--ping")
            {
                _testPing = true;
            }
            else if (a == "--testping")
            {
                // «Тест пинга» у карточки выбранного сервера
                _testPingOne = true;
            }
            else if (a == "--updatesubs")
            {
                _testUpdateSubs = true;
            }
            else if (a == "--picker")
            {
                _openPicker = true;
            }
            else if (a.StartsWith("--import="))
            {
                var eq = raw.IndexOf('=');
                _openImport = eq > 0 ? raw[(eq + 1)..] : null;
            }
            else if (a.StartsWith("--theme="))
            {
                var eq = raw.IndexOf('=');
                if (eq > 0) _previewTheme = raw[(eq + 1)..];
            }
            else if (a.StartsWith("--lang="))
            {
                var eq = raw.IndexOf('=');
                if (eq > 0) _previewLang = raw[(eq + 1)..];
            }
            else if (a.StartsWith("--set-theme="))
            {
                _setTheme = raw[(raw.IndexOf('=') + 1)..];
            }
            else if (a.StartsWith("--set-lang="))
            {
                _setLang = raw[(raw.IndexOf('=') + 1)..];
            }
            else if (a.StartsWith("--set-ping="))
            {
                _setPing = raw[(raw.IndexOf('=') + 1)..];
            }
            else if (a.StartsWith("--menu="))
            {
                var eq = raw.IndexOf('=');
                _openMenu = eq > 0 ? raw[(eq + 1)..] : null;
            }
        }
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            var on = ThemeManager.UsesLight ? 0 : 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Win10 2004+), 19 = fallback
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch
        {
            // ignore - title bar stays light
        }
    }

    // ================================================================= init

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var app = ProtoApp.Instance;
        app.Log += OnAppLog;
        app.Speed += OnAppSpeed;
        app.PingUpdate += OnPingUpdate;

        _tray = new TrayIcon(this);

        BarLog.Text = Loc.T("Loading ServiceLib…");

        bool ok;
        try
        {
            ok = await Task.Run(() => app.InitAsync());
        }
        catch (Exception ex)
        {
            ok = false;
            OnAppLog("init error: " + ex.Message);
        }

        if (!ok)
        {
            PaintError(Loc.T("ServiceLib init failed — see guiLogs"));
            if (_autotest)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(PayloadPrep.BaseDir, "autotest.log"),
                        "[FAIL] init\r\nRESULT: FAIL (init)");
                }
                catch { /* nothing else we can do */ }
                _exitDone = true;
                Application.Current.Shutdown(1);
            }
            return;
        }

        _initDone = true;
        SyncFromConfig();

        await RefreshNodeCardAsync();
        await RefreshServersTreeAsync();
        await LoadRoutingsAsync();
        PaintIdle();

        BarLog.Text = Loc.TF("ready · socks 127.0.0.1:{0}", app.SocksPort);

        // иконка информации о программе в левом нижнем углу
        if (InfoLink is not null)
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine(Loc.T("v2crackN 1.1.2"));
            info.AppendLine(Loc.T("ServiceLib 1.1.2 · logic ON"));
            info.AppendLine(Loc.TF("SOCKS: 127.0.0.1:{0}", app.SocksPort));
            info.AppendLine(Loc.TF("HTTP: 127.0.0.1:{0}", app.HttpPort));
            InfoLink.ToolTip = info.ToString();
            InfoLink.MouseLeftButtonDown += InfoLink_Click;
        }

        // --theme=Light / --menu=theme: предпросмотр для скриншотов, в конфиг не пишется
        if (_previewTheme is not null)
        {
            try
            {
                ThemeManager.Apply(_previewTheme);
                UpdateThemeLabels(_previewTheme);
                RepaintForTheme();
            }
            catch
            {
                // предпросмотр не должен ронять приложение
            }
        }

        // --lang=xx[,yy]: предпросмотр языка (в конфиг не пишется, но идёт тем же путём
        // Apply/RefreshUiTextsAsync, что и обычное переключение в меню); несколько
        // значений через запятую — серия «переключений», как руками в меню
        if (_previewLang is not null)
        {
            try
            {
                foreach (var code in _previewLang.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    Loc.SetLang(code);
                    await RefreshUiTextsAsync();
                }
            }
            catch
            {
                // предпросмотр не должен ронять приложение
            }
        }

        // --set-theme= / --set-lang=: реальная смена настройки (тем же путём, что и
        // меню) с записью в конфиг — для проверки персистентности после перезапуска
        if (_setTheme is not null)
        {
            try
            {
                await ProtoApp.Instance.SetThemeAsync(_setTheme);
                UpdateThemeLabels(_setTheme);
                RepaintForTheme();
            }
            catch
            {
                // тестовый флаг не должен ронять приложение
            }
        }

        if (_setLang is not null)
        {
            // язык UI применяем в любом случае — даже если конфиг не сохранился
            Loc.SetLang(_setLang);
            try
            {
                await ProtoApp.Instance.SetLanguageAsync(_setLang);
            }
            catch
            {
                // тестовый флаг не должен ронять приложение
            }

            try
            {
                await RefreshUiTextsAsync();
            }
            catch
            {
                // тестовый флаг не должен ронять приложение
            }

            try
            {
                UpdateLangLabels(_setLang);
            }
            catch
            {
                // меню ещё могло не построиться — не страшно
            }
        }

        // --set-ping=: реальная смена способа пинга (тем же путём, что и выбор в
        // настройках) с записью в proto-settings.json — для проверки персистентности
        if (_setPing is not null)
        {
            try
            {
                ProtoSettings.SetPingMethod(_setPing);
                _syncing = true;
                try
                {
                    SyncPingMethodRadios();
                }
                finally
                {
                    _syncing = false;
                }
            }
            catch
            {
                // тестовый флаг не должен ронять приложение
            }
        }

        if (_openMenu is not null)
        {
            NavSettings.IsChecked = true;
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                var current = ProtoApp.Instance.Config.UiItem.CurrentTheme;
                var lang = ProtoApp.Instance.Config.UiItem.CurrentLanguage;

                if (_openMenu == "lang")
                {
                    BuildMenu(LangItems, LangOptions(), lang ?? "", v => _ = PickLangAsync(v));
                    LangPop.IsOpen = true;
                }
                else
                {
                    BuildMenu(ThemeItems, ThemeOptions,
                              string.IsNullOrWhiteSpace(current) ? "Dark" : current!,
                              v => _ = PickThemeAsync(v));
                    ThemePop.IsOpen = true;
                }
            }), DispatcherPriority.Background);
        }

        // --updatesubs / --ping / --testping: проверка кнопок без мыши
        if (_testUpdateSubs) await UpdateServersAsync();
        if (_testPing) await PingAllAsync();
        if (_testPingOne) await TestPingAsync();

        if (_autotest)
        {
            await RunAutoTestAsync();
            return;
        }

        if (_startTunnel)
            await ApplyModeAsync(true);

        if (_openPicker)
            await OpenPickerAsync();

        if (_openImport is not null)
        {
            await OpenPickerAsync();
            ShowImport(_openImport is "sub" or "group" ? _openImport : "server");
        }

        if (_groupActionsDemo is not null)
            await GroupActionsDemoAsync(_groupActionsDemo);

        if (_openRename)
            await OpenRenamePreviewAsync();

        if (_openPreset)
            OpenPresetPreviewAsync();

        if (_showToast)
            ShowModeToast("Reconnect to apply the mode",
                          "Tunnel and Proxy switch only after reconnecting", sticky: true);

        if (_pendingConnect)
            await ConnectUiAsync();
    }

    /// <summary>Предпросмотр popup переименования (--rename): позиция фиксирована, без движения мыши.</summary>
    private async Task OpenRenamePreviewAsync()
    {
        var groups = await ProtoApp.Instance.GroupsAsync() ?? [];
        var target = groups.FirstOrDefault(t => t.Memo == ProtoApp.PinnedMemo)
                     ?? groups.FirstOrDefault(t => t.Id != Global.PermanentSubId);
        if (target is null) return;

        _renameGroupId = target.Id;
        RenameBox.Text = target.Remarks ?? string.Empty;
        RenamePop.Placement = PlacementMode.AbsolutePoint;
        RenamePop.HorizontalOffset = Left + 240;
        RenamePop.VerticalOffset = Top + 240;
        RenamePop.IsOpen = true;
    }

    /// <summary>Предпросмотр popup «новый пресет» (--preset): позиция фиксирована.</summary>
    private void OpenPresetPreviewAsync()
    {
        NewPresetBox.Text = string.Empty;
        NewPresetPop.Placement = PlacementMode.AbsolutePoint;
        NewPresetPop.HorizontalOffset = Left + 760;
        NewPresetPop.VerticalOffset = Top + 210;
        NewPresetPop.IsOpen = true;
    }

    /// <summary>Демо-группа для скриншотов: demo — создать и закрепить, clean — удалить.</summary>
    private async Task GroupActionsDemoAsync(string mode)
    {
        var app = ProtoApp.Instance;
        var groups = await app.GroupsAsync() ?? [];
        var demo = groups.FirstOrDefault(t => t.Remarks == DemoGroupName);

        if (mode == "clean")
        {
            if (demo is not null)
                await app.DeleteGroupAsync(demo.Id);
        }
        else
        {
            if (demo is null)
            {
                await app.CreateGroupAsync(DemoGroupName);
                groups = await app.GroupsAsync() ?? [];
                demo = groups.FirstOrDefault(t => t.Remarks == DemoGroupName);
            }

            if (demo is not null && demo.Memo != ProtoApp.PinnedMemo)
                await app.TogglePinGroupAsync(demo.Id);
        }

        await RefreshServersTreeAsync();
    }

    private void SyncFromConfig()
    {
        var app = ProtoApp.Instance;
        var cfg = app.Config;

        _syncing = true;
        try
        {
            TgTunnel.IsChecked = app.TunnelMode;
            TgSysProxy.IsChecked = app.TunnelMode;
            TgAutoRun.IsChecked = cfg.GuiItem.AutoRun;
            TgDpi.IsChecked = cfg.CoreBasicItem.EnableDpiBypass;
            TgGvisor.IsChecked = string.Equals(cfg.TunModeItem.Stack, "gvisor",
                                                StringComparison.OrdinalIgnoreCase);
            PortBox.Text = app.SocksPort.ToString();
            UpdateThemeLabels(cfg.UiItem.CurrentTheme);
            UpdateLangLabels(cfg.UiItem.CurrentLanguage);
            UpdatePortLabels();
            SyncPingMethodRadios();
        }
        finally
        {
            _syncing = false;
        }

        BarPort.Text = $"127.0.0.1:{app.SocksPort}";
        SetMode(app.TunnelMode);

        // Окно должно показывать ровно то, что записано в конфиге: палитра и язык
        // читаются до старта ServiceLib, а конфиг в этот момент мог ещё дописаться
        // (патч порта, восстановление) — тогда без этой сверки перезапуск выглядел бы
        // «откатом» настройки.
        var savedTheme = string.IsNullOrWhiteSpace(cfg.UiItem.CurrentTheme)
            ? "Dark"
            : cfg.UiItem.CurrentTheme!;
        if (!string.Equals(ThemeManager.Current, savedTheme, StringComparison.OrdinalIgnoreCase))
        {
            ThemeManager.Apply(savedTheme);
            UpdateThemeLabels(savedTheme);
            RepaintForTheme();
        }

        var savedLang = cfg.UiItem.CurrentLanguage;
        if (!string.IsNullOrWhiteSpace(savedLang)
            && !string.Equals(Loc.Lang, savedLang, StringComparison.OrdinalIgnoreCase))
        {
            Loc.SetLang(savedLang);
            UpdateLangLabels(savedLang);
            _ = RefreshUiTextsAsync();
        }
    }

    private void OnAppLog(string line)
    {
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                BarLog.Text = line.Length > 110 ? line[..110] + "…" : line;
                AppendLog(line);
            }));
        }
        catch
        {
            // window may already be closing
        }
    }

    // ================================================================= logs

    private readonly List<string> _logLines = [];
    private const int MaxLogLines = 2000;

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var text = $"[{DateTime.Now:HH:mm:ss}] {line}";
        _logLines.Add(text);

        var over = _logLines.Count - MaxLogLines;
        if (over > 0)
        {
            _logLines.RemoveRange(0, over);
            LogBox.Text = string.Join("\r\n", _logLines) + "\r\n";
        }
        else
        {
            LogBox.AppendText(text + "\r\n");
        }

        if (LogBox.IsVisible)
            LogBox.ScrollToEnd();
    }

    private void LogsImport_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var dir = Path.Combine(PayloadPrep.BaseDir, "guiLogs");
            var file = Directory.Exists(dir)
                ? new DirectoryInfo(dir).GetFiles("*.txt")
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;

            if (file is null)
            {
                AppendLog(Loc.T("guiLogs: no log file found yet"));
                return;
            }

            var all = File.ReadAllLines(file.FullName);
            var tail = all.Length > 300 ? all[^300..] : all;

            var block = new List<string>(tail.Length + 1)
            {
                $"---- {file.Name} · {all.Length} lines total, showing last {tail.Length} ----",
            };
            block.AddRange(tail);

            foreach (var l in block)
                _logLines.Add(l);

            while (_logLines.Count > MaxLogLines)
                _logLines.RemoveAt(0);

            LogBox.Text = string.Join("\r\n", _logLines) + "\r\n";
            LogBox.ScrollToEnd();
            AppendLog(Loc.TF("imported {0} lines from {1}", tail.Length, file.Name));
        }
        catch (Exception ex)
        {
            AppendLog(Loc.T("import failed: ") + ex.Message);
        }
    }

    private void LogsClear_Click(object sender, MouseButtonEventArgs e)
    {
        _logLines.Clear();
        LogBox.Clear();
        AppendLog(Loc.T("log view cleared (files in guiLogs are untouched)"));
    }

    private void LogsOpenFolder_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var dir = Path.Combine(PayloadPrep.BaseDir, "guiLogs");
            Directory.CreateDirectory(dir);

            var file = new DirectoryInfo(dir).GetFiles("*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();

            var arg = file is null
                ? $"\"{dir}\""
                : $"/select,\"{file.FullName}\"";

            Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppendLog(Loc.T("open folder failed: ") + ex.Message);
        }
    }

    private void OnAppSpeed(ServerSpeedItem speed)
    {
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DownText.Text = FormatSpeed(speed.ProxyDown + speed.DirectDown);
                UpText.Text = FormatSpeed(speed.ProxyUp + speed.DirectUp);

                var (up, down) = ProtoApp.Instance.SessionTotals;
                SessionText.Text = Utils.HumanFy(up + down);
            }));
        }
        catch
        {
            // window may already be closing
        }
    }

    private static string FormatSpeed(long bytesPerSec)
        => bytesPerSec <= 0 ? "0 B/s" : Utils.HumanFy(bytesPerSec) + "/s";

    // ================================================================= mode

    private static readonly TimeSpan ModeAnim = TimeSpan.FromMilliseconds(0.24);

    private void SetMode(bool tunnel)
    {
        if (ModeThumb is null || ModeGrid is null) return;
        _tunnelUi = tunnel;

        // ширина ползунка = половина строки переключателя (реальный размер — после layout)
        var w = ModeGrid.ActualWidth / 2;
        if (double.IsNaN(w) || w <= 0) w = 133;
        if (double.IsNaN(ModeThumb.Width) || Math.Abs(ModeThumb.Width - w) > 0.5)
            ModeThumb.Width = w;

        if (ModeThumb.RenderTransform is TranslateTransform tt)
        {
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(tunnel ? w : 0, ModeAnim)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
        }

        // цвет ползунка и подписей перетекает, а не перепрыгивает
        CrossFade(ModeThumb, Border.BackgroundProperty, (Brush)FindResource(tunnel ? "Ok" : "Accent"), ModeAnim);
        CrossFade(ModeProxyText, TextBlock.ForegroundProperty, tunnel ? OnTrackLabel : OnAccentLabel, ModeAnim);
        CrossFade(ModeTunnelText, TextBlock.ForegroundProperty, tunnel ? OnAccentLabel : OnTrackLabel, ModeAnim);
        UpdateModeHint();
    }

    /// <summary>
    /// Плавная замена кисти: значение копится в новую SolidColorBrush
    /// и добегает до целевого цвета анимацией (замороженные кисти не анимируются).
    /// </summary>
    private static void CrossFade(FrameworkElement el, DependencyProperty prop, Brush to, TimeSpan dur)
    {
        if (to is not SolidColorBrush solid)
        {
            el.SetValue(prop, to);
            return;
        }

        var toColor = solid.Color;
        var current = el.GetValue(prop) as SolidColorBrush;
        if (current is not null && current.Color == toColor) return;   // уже нужного цвета

        var fromColor = current?.Color ?? toColor;
        var br = new SolidColorBrush(fromColor);
        el.SetValue(prop, br);

        if (fromColor == toColor) return;
        br.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(fromColor, toColor, dur)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void UpdateModeHint()
    {
        if (ModeHint is null) return;
        var port = _initDone ? ProtoApp.Instance.SocksPort : PayloadPrep.ProtoPort;
        var key = _tunnelUi
            ? "Tunnel - the system proxy sends apps through 127.0.0.1:{port}"
            : "Apps read the local port 127.0.0.1:{port} - only proxy-aware traffic goes out";
        ModeHint.Text = Loc.T(key).Replace("{port}", port.ToString());
    }

    private async void ModeProxy_Click(object sender, MouseButtonEventArgs e) => await ApplyModeAsync(false);

    private async void ModeTunnel_Click(object sender, MouseButtonEventArgs e) => await ApplyModeAsync(true);

    private async Task ApplyModeAsync(bool tunnel)
    {
        var changed = _tunnelUi != tunnel;
        SetMode(tunnel);
        if (!_initDone) return;

        try
        {
            await ProtoApp.Instance.SetTunnelModeAsync(tunnel);
            SyncModeToggles(tunnel);
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }

        if (changed)
            ShowModeToast("Reconnect to apply the mode",
                          "Tunnel and Proxy switch only after reconnecting");
    }

    private void SyncModeToggles(bool tunnel)
    {
        _syncing = true;
        try
        {
            TgTunnel.IsChecked = tunnel;
            TgSysProxy.IsChecked = tunnel;
        }
        finally
        {
            _syncing = false;
        }
    }

    // =============================================================== toast

    private int _toastSeq;

    /// <summary>
    /// Тост по центру сверху: плавно выезжает сверху, держится ~4 c и уезжает обратно.
    /// Предупреждает, что режим Tunnel/Proxy меняется только при переподключении.
    /// </summary>
    private async void ShowModeToast(string title, string text, bool sticky = false)
    {
        if (ModeToast is null || ModeToastShift is null) return;

        var seq = ++_toastSeq;
        ModeToastTitle.Text = Loc.T(title);
        ModeToastText.Text = Loc.T(text);
        ModeToast.Visibility = Visibility.Visible;

        var show = new DoubleAnimation(-90, 0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        ModeToastShift.BeginAnimation(TranslateTransform.YProperty, show);

        if (sticky) return;

        await Task.Delay(4200);
        if (seq != _toastSeq) return;

        var hide = new DoubleAnimation(0, -90, TimeSpan.FromMilliseconds(340))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
        };
        hide.Completed += (_, _) =>
        {
            if (seq != _toastSeq) return;
            ModeToast.Visibility = Visibility.Collapsed;
            ModeToastShift.BeginAnimation(TranslateTransform.YProperty, null);
            ModeToastShift.Y = -90;
        };
        ModeToastShift.BeginAnimation(TranslateTransform.YProperty, hide);
    }

    // ========================================================== popups close

    /// <summary>
    /// Клик по окну закрывает открытые дропдауны (StaysOpen=True — закрытие вручную,
    /// иначе отпускание ЛКМ после клика по кнопке тут же гасило список).
    /// </summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var src = e.OriginalSource as DependencyObject;
        if (src is null) return;

        if (ThemePop is { IsOpen: true } && !IsInside(src, ThemeBtn))
            ThemePop.IsOpen = false;
        if (LangPop is { IsOpen: true } && !IsInside(src, LangBtn))
            LangPop.IsOpen = false;
        if (MovePop is { IsOpen: true } && !IsInsideAny(src, MovePop.Child))
            MovePop.IsOpen = false;
        if (RenamePop is { IsOpen: true } && !IsInsideAny(src, RenamePop.Child))
            RenamePop.IsOpen = false;
        if (NewPresetPop is { IsOpen: true } && !IsInsideAny(src, NewPresetPop.Child))
            NewPresetPop.IsOpen = false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseAllPopups();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        // popup с фокусом (например, поле переименования) не трогаем —
        // он всё равно закроется по клику вне окна или по Escape
        if (RenamePop is { IsOpen: true, IsKeyboardFocusWithin: true }) return;
        if (NewPresetPop is { IsOpen: true, IsKeyboardFocusWithin: true }) return;
        CloseAllPopups();
    }

    private void CloseAllPopups()
    {
        if (ThemePop is not null) ThemePop.IsOpen = false;
        if (LangPop is not null) LangPop.IsOpen = false;
        if (MovePop is not null) MovePop.IsOpen = false;
        if (RenamePop is not null) RenamePop.IsOpen = false;
        if (NewPresetPop is not null) NewPresetPop.IsOpen = false;
        if (AboutPop is not null) AboutPop.IsOpen = false;
    }

    private static bool IsInside(DependencyObject? src, DependencyObject? target)
    {
        if (src is null || target is null) return false;
        while (src is not null)
        {
            if (ReferenceEquals(src, target)) return true;
            src = src is Visual ? VisualTreeHelper.GetParent(src)
                 : LogicalTreeHelper.GetParent(src);
        }
        return false;
    }

    private static bool IsInsideAny(DependencyObject? src, DependencyObject? target)
        => IsInside(src, target);

    private Brush OnAccentLabel => (Brush)FindResource("0B1220");
    private Brush OnTrackLabel => (Brush)FindResource("B7C5DC");

    // ================================================================= nav

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || PageConnect is null || PageLogs is null)
            return;

        var tag = (rb.Tag as string) ?? "";
        var isConnect = tag.Contains("\u23FB");
        var isRouting = tag.Contains("\u21C4");
        var isLogs = tag.Contains("\u2630");
        var isSettings = !isConnect && !isRouting && !isLogs;

        PageConnect.Visibility = isConnect ? Visibility.Visible : Visibility.Collapsed;
        PageRouting.Visibility = isRouting ? Visibility.Visible : Visibility.Collapsed;
        PageLogs.Visibility = isLogs ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;

        _navPage = isConnect ? NavPage.Connect : isRouting ? NavPage.Routing : isLogs ? NavPage.Logs : NavPage.Settings;
        UpdateNavTitle();

        if (isLogs && LogBox is not null)
            LogBox.ScrollToEnd();
    }

    private enum NavPage { Connect, Routing, Settings, Logs }

    private NavPage _navPage = NavPage.Connect;

    /// <summary>Заголовок и подзаголовок страницы — пересчитываются при смене языка.</summary>
    private void UpdateNavTitle()
    {
        if (PageTitle is null) return;

        var (nav, sub) = _navPage switch
        {
            NavPage.Routing => (NavRouting, "Choose what goes through the tunnel"),
            NavPage.Settings => (NavSettings, "Tune the client to your taste"),
            NavPage.Logs => (NavLogs, "Raw client and core output"),
            _ => (NavConnect, "One tap to protect your traffic"),
        };

        PageTitle.Text = nav?.Content?.ToString() ?? Loc.T("Connect");
        PageSub.Text = Loc.T(sub);
    }

    // ============================================================= connect

    private async void Power_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_connected)
                await DisconnectUiAsync();
            else
                await ConnectUiAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async Task ConnectUiAsync()
    {
        var app = ProtoApp.Instance;
        if (!app.Initialized)
        {
            _pendingConnect = true;
            return;
        }

        if (_busy) return;
        _busy = true;
        PowerBtn.IsEnabled = false;
        PaintBusy("Connecting…");
        try
        {
            var r = await app.ConnectAsync();
            if (r.Success)
            {
                _probeFails = 0;
                PaintConnected();
            }
            else
            {
                PaintError(r.Message);
            }
        }
        catch (Exception ex)
        {
            PaintError(ex.Message);
        }
        finally
        {
            _busy = false;
            PowerBtn.IsEnabled = true;
        }
    }

    private async Task DisconnectUiAsync()
    {
        var app = ProtoApp.Instance;
        if (!app.Initialized || _busy) return;

        _busy = true;
        PowerBtn.IsEnabled = false;
        PaintBusy("Disconnecting…");
        try
        {
            var r = await app.DisconnectAsync();
            if (r.Success) PaintIdle();
            else PaintError(r.Message);
        }
        catch (Exception ex)
        {
            PaintError(ex.Message);
        }
        finally
        {
            _busy = false;
            PowerBtn.IsEnabled = true;
        }
    }

    /// <summary>Выполняет операцию ServiceLib, показывая Reconnecting при активном соединении. true = успех.</summary>
    private async Task<bool> RunOpAsync(Func<Task<OpResult>> op)
    {
        if (_busy) return false;
        _busy = true;
        PowerBtn.IsEnabled = false;
        try
        {
            if (_connected)
                PaintBusy("Reconnecting…");

            var r = await op();
            if (!r.Success)
            {
                PaintError(r.Message);
                return false;
            }

            if (_connected)
            {
                _probeFails = 0;
                PaintConnected();
            }
            else if (r.Message.Length > 0)
            {
                BarLog.Text = r.Message;
            }
            return true;
        }
        catch (Exception ex)
        {
            PaintError(ex.Message);
            return false;
        }
        finally
        {
            _busy = false;
            PowerBtn.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ paint UI

    private void PaintConnected()
    {
        _connected = true;
        _busyText = null;
        var accent = (Brush)FindResource("Ok");
        var text3 = (Brush)FindResource("Text3");

        StatusText.Text = Loc.T("Connected");
        StatusText.Foreground = accent;
        StatusText.FontSize = 20;
        HeaderText.Text = Loc.T("Connected");
        HeaderDot.Fill = accent;
        BarText.Text = Loc.T("Connected");
        BarDot.Fill = accent;
        PowerRing.BorderBrush = accent;
        if (PowerIcon is not null) PowerIcon.Stroke = accent;

        _seconds = 0;
        TimerText.Text = "00:00:00";
        TimerText.Foreground = text3;
        _timer.Start();
    }

    private void PaintIdle()
    {
        _connected = false;
        _busyText = null;
        _timer.Stop();

        var text2 = (Brush)FindResource("Text2");

        StatusText.Text = Loc.T("Not Connected");
        StatusText.Foreground = text2;
        StatusText.FontSize = 20;
        HeaderText.Text = Loc.T("Not connected");
        HeaderDot.Fill = IdleDot;
        BarText.Text = Loc.T("Not connected");
        BarDot.Fill = IdleDot;
        PowerRing.BorderBrush = (Brush)FindResource("CardStroke");
        if (PowerIcon is not null) PowerIcon.Stroke = text2;

        TimerText.Text = "00:00:00";
        TimerText.Foreground = (Brush)FindResource("Text3");
    }

    private void PaintBusy(string text)
    {
        var accent = (Brush)FindResource("Accent");
        var label = Loc.T(text);
        _busyText = text;

        StatusText.Text = label;
        StatusText.Foreground = accent;
        HeaderText.Text = label;
        HeaderDot.Fill = accent;
        BarText.Text = label;
        BarDot.Fill = accent;

        if (!_connected)
            TimerText.Text = "—";
    }

    private void PaintError(string message)
    {
        _connected = false;
        _busyText = null;
        _timer.Stop();

        var text2 = (Brush)FindResource("Text2");

        StatusText.Text = Loc.T("Error");
        StatusText.Foreground = ErrorBrush;
        StatusText.FontSize = 20;
        TimerText.Text = string.IsNullOrWhiteSpace(message) ? Loc.T("unknown error") : message;
        TimerText.Foreground = ErrorBrush;

        HeaderText.Text = Loc.T("Error");
        HeaderDot.Fill = ErrorBrush;
        BarText.Text = Loc.T("Error");
        BarDot.Fill = ErrorBrush;
        PowerRing.BorderBrush = (Brush)FindResource("CardStroke");
        if (PowerIcon is not null) PowerIcon.Stroke = text2;

        var shortMsg = (TimerText.Text.Length > 110 ? TimerText.Text[..110] + "…" : TimerText.Text);
        BarLog.Text = shortMsg;
    }

    // ------------------------------------------------------------- liveness

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_connected) return;

        _seconds++;
        TimerText.Text = TimeSpan.FromSeconds(_seconds).ToString(@"hh\:mm\:ss");

        if (_probeBusy || _seconds % 2 != 0) return;

        _probeBusy = true;
        try
        {
            if (await ProtoApp.Instance.IsPortAliveAsync())
            {
                _probeFails = 0;
            }
            else if (++_probeFails >= 3)
            {
                _probeFails = 0;
                await HandleCoreDeathAsync();
            }
        }
        catch
        {
            // probe machinery failure is not a core failure
        }
        finally
        {
            _probeBusy = false;
        }
    }

    private async Task HandleCoreDeathAsync()
    {
        try
        {
            await ProtoApp.Instance.DisconnectAsync();
        }
        catch
        {
            // best effort - at least reflect the state in UI
        }

        PaintIdle();
        TimerText.Text = Loc.T("core stopped");
        TimerText.Foreground = ErrorBrush;
        BarLog.Text = Loc.T("core stopped unexpectedly");
    }

    // ============================================================ settings

    /// <summary>Выбор способа пинга (Settings) — сохраняется немедленно, как в Happ.</summary>
    private void PingMethod_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || !_initDone || sender is not RadioButton rb || rb.Tag is not string tag)
            return;

        ProtoSettings.SetPingMethod(tag);
        PingTrace($"ping method -> {tag}");
    }

    /// <summary>Поставить галочку по сохранённому способу пинга (зовём под _syncing).</summary>
    private void SyncPingMethodRadios()
    {
        var method = ProtoSettings.PingMethod;
        PingMixed.IsChecked = method == "mixed";
        PingGet.IsChecked = method == "get";
        PingHead.IsChecked = method == "head";
        PingTcp.IsChecked = method == "tcp";
        PingIcmp.IsChecked = method == "icmp";
    }

    private async void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || !_initDone || sender is not CheckBox cb || cb.Tag is not string tag)
            return;

        var app = ProtoApp.Instance;
        var on = cb.IsChecked == true;

        try
        {
            switch (tag)
            {
                case "tunnel":
                case "sysproxy":
                    var wasOn = app.TunnelMode;
                    await app.SetTunnelModeAsync(on);
                    SyncModeToggles(app.TunnelMode);
                    SetMode(app.TunnelMode);
                    if (wasOn != app.TunnelMode)
                        ShowModeToast("Reconnect to apply the mode",
                                      "Tunnel and Proxy switch only after reconnecting");
                    break;

                case "autorun":
                    await app.SetAutoRunAsync(on);
                    break;

                case "dpi":
                    await app.SetDpiBypassAsync(on);
                    break;

                case "gvisor":
                    await app.SetTunStackAsync(on);
                    break;
            }
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void PortBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        try
        {
            await ApplyPortAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void PortBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_exitDone) return;
        try
        {
            await ApplyPortAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async Task ApplyPortAsync()
    {
        if (!_initDone) return;

        var app = ProtoApp.Instance;
        if (!int.TryParse(PortBox.Text?.Trim(), out var port) || port is < 1024 or > 65535)
        {
            PortBox.Text = app.SocksPort.ToString();
            BarLog.Text = Loc.T("port must be 1024-65535");
            return;
        }

        if (port == app.SocksPort) return;

        await RunOpAsync(() => app.SetSocksPortAsync(port));

        PortBox.Text = app.SocksPort.ToString();
        BarPort.Text = $"127.0.0.1:{app.SocksPort}";
        UpdatePortLabels();
        UpdateModeHint();
    }

    /// <summary>Порты в настройках: SOCKS задаётся вручную, HTTP всегда SOCKS+1.</summary>
    private void UpdatePortLabels()
    {
        if (HttpPortText is null || !_initDone) return;
        HttpPortText.Text = ProtoApp.Instance.HttpPort.ToString();
    }

    // ========================================================== appearance

    private static readonly (string Value, string Display)[] ThemeOptions =
    [
        ("FollowSystem", "Follow system"),
        ("Dark", "Dark"),
        ("Light", "Light"),
    ];

    private void ThemeBtn_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_initDone || ThemePop is null) return;

        var current = ProtoApp.Instance.Config.UiItem.CurrentTheme;
        BuildMenu(ThemeItems, ThemeOptions, string.IsNullOrWhiteSpace(current) ? "Dark" : current!,
                  v => _ = PickThemeAsync(v));
        ThemePop.IsOpen = !ThemePop.IsOpen;
    }

    private async Task PickThemeAsync(string value)
    {
        ThemePop.IsOpen = false;
        try
        {
            await ProtoApp.Instance.SetThemeAsync(value);
            UpdateThemeLabels(value);
            RepaintForTheme();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private void LangBtn_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_initDone || LangPop is null) return;

        BuildMenu(LangItems, LangOptions(), ProtoApp.Instance.Config.UiItem.CurrentLanguage ?? "",
                  v => _ = PickLangAsync(v));
        LangPop.IsOpen = !LangPop.IsOpen;
    }

    private async Task PickLangAsync(string value)
    {
        LangPop.IsOpen = false;
        try
        {
            await ProtoApp.Instance.SetLanguageAsync(value);
            Loc.SetLang(value);
            UpdateLangLabels(value);
            await RefreshUiTextsAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    /// <summary>
    /// Перевести всё окно после смены языка: статичноe дерево (Loc.Apply) плюс
    /// строки, которые код назначает на лету — статус, подзаголовки, подсказки,
    /// описания пресетов и карточка сервера.
    /// </summary>
    private async Task RefreshUiTextsAsync()
    {
        Loc.Apply(this);

        // дерево тоже перечитываем: его группы — кодовые строки («Server list», «sub»)
        await RefreshServersTreeAsync();

        UpdateNavTitle();
        UpdateModeHint();
        RefreshStatusTexts();

        // строка слева внизу (её задаёт код, не XAML)

        try
        {
            var cfg = ProtoApp.Instance.Config;
            UpdateThemeLabels(cfg.UiItem.CurrentTheme);
            UpdatePortLabels();

            if (ImportPanel is { Visibility: Visibility.Visible })
                ShowImport(_importMode);

            await RefreshNodeCardAsync();
            await LoadRoutingsAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    /// <summary>Статус подключения на новом языке — без перезапуска таймера сессии.</summary>
    private void RefreshStatusTexts()
    {
        if (StatusText is null) return;

        if (_busyText is not null)
        {
            var label = Loc.T(_busyText);
            StatusText.Text = label;
            HeaderText.Text = label;
            BarText.Text = label;
        }
        else if (_connected)
        {
            StatusText.Text = Loc.T("Connected");
            HeaderText.Text = Loc.T("Connected");
            BarText.Text = Loc.T("Connected");
        }
        else
        {
            StatusText.Text = Loc.T("Not Connected");
            HeaderText.Text = Loc.T("Not connected");
            BarText.Text = Loc.T("Not connected");
        }
    }

    private static (string Value, string Display)[] LangOptions()
        => [.. Global.LanguageOptions.Select(o => (o.Value, o.Display))];

    /// <summary>Меню дропдауна: активный пункт — Accent + галочка, остальные с hover-подсветкой.</summary>
    private void BuildMenu(Panel panel, (string Value, string Display)[] options,
                           string current, Action<string> pick)
    {
        panel.Children.Clear();

        foreach (var (value, display) in options)
        {
            var active = string.Equals(value, current, StringComparison.OrdinalIgnoreCase);
            var label = Loc.T(display);

            var row = new Border
            {
                Tag = value,
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Background = active ? null : Brushes.Transparent,
            };
            if (active)
                row.SetResourceReference(Border.BackgroundProperty, "AccentSoft");

            // кисти берём в момент события: при смене темы палитра подменяет
            // замороженные кисти, «захваченная» ссылка осталась бы по-старому
            row.MouseEnter += (_, _) =>
            {
                if (!active) row.Background = (Brush)FindResource("CardBg2");
            };
            row.MouseLeave += (_, _) =>
            {
                if (active) row.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                else row.Background = Brushes.Transparent;
            };

            var rowText = new TextBlock
            {
                Text = active ? label + " ✓" : label,
                FontSize = 13,
            };
            rowText.SetResourceReference(TextBlock.ForegroundProperty, active ? "Accent" : "Text1");
            row.Child = rowText;

            var picked = value;
            row.MouseLeftButtonDown += (_, args) =>
            {
                args.Handled = true;
                pick(picked);
            };

            panel.Children.Add(row);
        }
    }

    private void UpdateThemeLabels(string? theme)
    {
        if (ThemeBtnText is null) return;

        var key = (theme ?? "").Trim().ToLowerInvariant();
        var display = Loc.T(key switch
        {
            "followsystem" => "Follow system",
            "light" => "Light",
            _ => "Dark",
        });

        ThemeBtnText.Text = display + " \u25BE";
        ThemeSub.Text = display;
        ApplyDarkTitleBar();
    }

    private void UpdateLangLabels(string? code)
    {
        if (LangBtnText is null) return;
        LangBtnText.Text = FindLangDisplay(code) + " \u25BE";
    }

    private static string FindLangDisplay(string? code)
    {
        if (!string.IsNullOrEmpty(code))
            foreach (var option in Global.LanguageOptions)
                if (string.Equals(option.Value, code, StringComparison.OrdinalIgnoreCase))
                    return option.Display;

        return "English";
    }

    /// <summary>
    /// Кисти, назначенные из кода (статус, таймер, точки, режим), после смены темы
    /// переназначаются — палитра могла создать новые взамен замороженных.
    /// </summary>
    private void RepaintForTheme()
    {
        SetMode(_tunnelUi);
        if (_connected)
            PaintConnected();
        else if (!_busy)
            PaintIdle();

        // чипы роутинга и подсветка выбора — тоже перекрасить на новую палитру
        PaintRuleChips();
    }

    // ======================================================= server picker

    private async void Change_Click(object sender, MouseButtonEventArgs e) => await OpenPickerAsync();

    private async Task OpenPickerAsync()
    {
        if (!_initDone) return;

        try
        {
            var app = ProtoApp.Instance;
            var list = await app.ProfilesAsync() ?? [];
            var current = await app.CurrentProfileAsync();

            var vms = list
                .Select(p => new ProfileVm(
                    p.IndexId,
                    string.IsNullOrEmpty(p.Remarks) ? p.GetSummary() : p.Remarks,
                    $"{p.Address}:{p.Port} · {p.ConfigType}",
                    p.IndexId == current?.IndexId ? Visibility.Visible : Visibility.Collapsed,
                    ProtocolIcon(p.ConfigType.ToString())))
                .ToList();

            ProfileList.ItemsSource = vms;
            PickerOverlay.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void Profile_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string indexId }) return;

        try
        {
            PickerOverlay.Visibility = Visibility.Collapsed;
            await RunOpAsync(() => ProtoApp.Instance.SelectProfileAsync(indexId));
            await RefreshNodeCardAsync();
            await RefreshServersTreeAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private void PickerBg_Click(object sender, MouseButtonEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender)) return;
        PickerOverlay.Visibility = Visibility.Collapsed;
    }

    // ============================================================ server tree

    private readonly HashSet<string> _collapsedGroups = [];

    /// <summary>Последний пинг по серверам: текст + цвет (null = служебное состояние).</summary>
    private readonly Dictionary<string, (string Text, bool? Ok)> _pingResults = new();

    /// <summary>Серверы текущего прогона, чей ответ ещё не пришёл.</summary>
    private readonly HashSet<string> _pingPending = [];

    /// <summary>
    /// Виды фаз пинга прототипа: три фазы ServiceLib (TCP → реальный → UDP) плюс
    /// свои HEAD и ICMP — набор зависит от выбора в настройках (как в Happ).
    /// </summary>
    private enum PingPhase { Tcping, Realping, UdpTest, HttpHead, Icmp }

    /// <summary>Фазы прогона и порядок показа в строке по выбранному способу пинга.</summary>
    private static PingPhase[] PhasesFor(string method) => method switch
    {
        "get" => [PingPhase.Realping],
        "head" => [PingPhase.HttpHead],
        "tcp" => [PingPhase.Tcping],
        "icmp" => [PingPhase.Icmp],
        _ => [PingPhase.Tcping, PingPhase.Realping, PingPhase.UdpTest],
    };

    /// <summary>Порядок показа в строке: настоящий пинг через сервер важнее прямого TCP.</summary>
    private static PingPhase[] DisplayFor(string method) => method switch
    {
        "get" => [PingPhase.Realping],
        "head" => [PingPhase.HttpHead],
        "tcp" => [PingPhase.Tcping],
        "icmp" => [PingPhase.Icmp],
        _ => [PingPhase.Realping, PingPhase.Tcping, PingPhase.UdpTest],
    };

    /// <summary>Снимок выбора на момент прогона — настройку можно не бояться менять в пути.</summary>
    private PingPhase[] _runPhases = PhasesFor("mixed");
    private PingPhase[] _runDisplay = DisplayFor("mixed");

    /// <summary>Фаза, которая идёт прямо сейчас, и сколько фаз уже завершилось.</summary>
    private PingPhase _pingPhase = PingPhase.Tcping;
    private int _pingPhasesDone;

    /// <summary>Замеры каждой фазы по каждому серверу — итог в строке выбираем по приоритету.</summary>
    private readonly Dictionary<string, Dictionary<PingPhase, int>> _pingPhaseValues = new();

    private bool _pinging;
    private bool _updatingServers;
    private string? _moveIndexId;

    /// <summary>Индекс сервера, чей результат ждём от «Тест пинга» (кнопка у карточки).</summary>
    private string? _testPingIndexId;

    /// <summary>Дерево на Connect: «Server list» + группы (подписки и пользовательские).</summary>
    private async Task RefreshServersTreeAsync()
    {
        if (!_initDone) return;
        PingTrace("tree refresh start");

        try
        {
            var app = ProtoApp.Instance;
            var profiles = await app.ProfilesAsync() ?? [];
            var groups = await app.GroupsAsync() ?? [];
            var current = await app.CurrentProfileAsync();

            // прошлые замеры пинга (ProfileExItem.Delay, 0 = не пинговался)
            var delays = new Dictionary<string, int>();
            foreach (var px in await ProfileExManager.Instance.GetProfileExs())
                if (!string.IsNullOrEmpty(px.IndexId) && px.Delay != 0)
                    delays.TryAdd(px.IndexId, px.Delay);

            ProfileVm ToVm(ProfileItem p)
            {
                var vm = new ProfileVm(
                    p.IndexId,
                    string.IsNullOrEmpty(p.Remarks) ? p.GetSummary() : p.Remarks,
                    $"{p.Address}:{p.Port} · {p.ConfigType}",
                    p.IndexId == current?.IndexId ? Visibility.Visible : Visibility.Collapsed,
                    ProtocolIcon(p.ConfigType.ToString()));

                // последний замер: из этой сессии либо из прошлого прогона
                var id = p.IndexId ?? string.Empty;
                if (_pingResults.TryGetValue(id, out var ping))
                {
                    vm.PingText = ping.Text;
                    vm.PingBrush = PingBrushFor(ping.Ok);
                }
                else if (delays.TryGetValue(id, out var ms))
                {
                    vm.PingText = ms > 0 ? ms + "ms" : "n/a";
                    vm.PingBrush = PingBrushFor(ms > 0);
                }

                return vm;
            }

            var known = groups.Select(g => g.Id).ToHashSet();
            var singles = profiles
                .Where(p => string.IsNullOrEmpty(p.Subid) || !known.Contains(p.Subid))
                .ToList();

            var vms = new List<GroupVm>
            {
                new("", Loc.T("Server list"), "", singles.Count,
                    singles.Select(ToVm).ToList(), _collapsedGroups.Contains(""), false),
            };

            // закреплённые группы (SubItem.Memo == "pinned") идут первыми
            foreach (var g in groups.OrderBy(t => t.Memo == ProtoApp.PinnedMemo ? 0 : 1))
            {
                var items = profiles.Where(p => p.Subid == g.Id).ToList();
                vms.Add(new GroupVm(
                    g.Id,
                    string.IsNullOrEmpty(g.Remarks) ? "group" : g.Remarks,
                    string.IsNullOrEmpty(g.Url) ? Loc.T("group") : Loc.T("sub"),
                    items.Count,
                    items.Select(ToVm).ToList(),
                    _collapsedGroups.Contains(g.Id),
                    g.Memo == ProtoApp.PinnedMemo));
            }

            ServerTree.ItemsSource = vms;
            PingTrace($"tree refresh done groups={vms.Count} firstPing='{vms.SelectMany(g => g.Items).FirstOrDefault()?.PingText}'");

            // строки шаблона («No servers yet…», «Active») создаются после первого
            // Loc.Apply — прогоняем перевод ещё раз, когда контейнеры уже построены
            _ = Dispatcher.BeginInvoke(new Action(() => Loc.Apply(this)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    // ------------------------------------------------- refresh & ping

    /// <summary>«⟳ Update» — обновить все подписки и перечитать дерево.</summary>
    private async void TreeRefresh_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await UpdateServersAsync();
    }

    private async Task UpdateServersAsync()
    {
        if (!_initDone || _updatingServers) return;

        try
        {
            _updatingServers = true;
            TreeRefreshText.Opacity = 0.5;
            TreePingStatus.Text = Loc.T("Updating…");

            var rc = await ProtoApp.Instance.UpdateSubscriptionsAsync();
            TreePingStatus.Text = rc.Success ? rc.Message : Loc.T("nothing to update");
            if (!rc.Success) BarLog.Text = rc.Message;

            await RefreshServersTreeAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
        finally
        {
            _updatingServers = false;
            TreeRefreshText.Opacity = 1;
        }
    }

    /// <summary>«⚡ Ping all» — tcping всех серверов из дерева.</summary>
    private async void TreePingAll_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await PingAllAsync();
    }

    private async Task PingAllAsync()
    {
        if (!_initDone || _pinging) return;

        try
        {
            var profiles = await ProtoApp.Instance.ProfilesAsync() ?? [];
            if (profiles.Count == 0)
            {
                TreePingStatus.Text = Loc.T("No servers yet — use + Server");
                return;
            }

            await PingAsync(profiles);
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    /// <summary>Пинг одного сервера — текст пинга в его строке.</summary>
    private async void ServerPing_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: string indexId }) return;
        if (!_initDone || _pinging || string.IsNullOrEmpty(indexId)) return;

        try
        {
            var item = (await ProtoApp.Instance.ProfilesAsync() ?? [])
                .FirstOrDefault(t => t.IndexId == indexId);
            if (item is null) return;

            await PingAsync([item]);
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    /// <summary>«Тест пинга» — кнопка под карточкой выбранного сервера (как в Happ).</summary>
    private async void TestPing_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        await TestPingAsync();
    }

    private async Task TestPingAsync()
    {
        if (!_initDone || _pinging) return;

        try
        {
            var p = await ProtoApp.Instance.CurrentProfileAsync();
            if (p is null || string.IsNullOrEmpty(p.IndexId))
            {
                TestPingResult.Text = Loc.T("No server selected");
                return;
            }

            _testPingIndexId = p.IndexId;
            await PingAsync([p]);
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async Task PingAsync(List<ProfileItem> items)
    {
        _pinging = true;
        _pingPending.Clear();
        _pingPhasesDone = 0;
        // набор фаз фиксируем на старте: смена выбора в настройках не должна
        // перекроить уже идущий прогон (счётчик завершений считает по снимку)
        _runPhases = PhasesFor(ProtoSettings.PingMethod);
        _runDisplay = DisplayFor(ProtoSettings.PingMethod);
        foreach (var it in items)
        {
            if (string.IsNullOrEmpty(it.IndexId)) continue;
            _pingPending.Add(it.IndexId);
            _pingPhaseValues.Remove(it.IndexId);
            ApplyPingUi(it.IndexId, "…", null);
        }

        TreePingText.Opacity = 0.5;
        var total = _pingPending.Count;

        try
        {
            // как в Happ: не только TCP, а все виды выбранного теста подряд; каждая
            // следующая фаза уточняет значение, финал выбирается по приоритету
            for (var i = 0; i < _runPhases.Length; i++)
            {
                var phase = _pingPhase = _runPhases[i];
                PingTrace($"phase start {phase} method={ProtoSettings.PingMethod}");
                TreePingStatus.Text = Loc.TF("pinging {0} server(s)…", total)
                                      + " · " + PhaseLabel(phase);

                await Task.Run(() => RunPingPhaseAsync(items, phase));
            }
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }

        // финальное сообщение ServiceLib могло не дойти (например, список пуст) —
        // не оставляем кнопки в «busy»
        if (_pinging) FinishPing("");
    }

    /// <summary>
    /// Одна фаза: ServiceLib-тесты идут в SpeedtestService, свои (HEAD / ICMP) —
    /// в ProtoApp; обе стороны поднимают PingUpdate тем же протоколом.
    /// </summary>
    private static Task RunPingPhaseAsync(List<ProfileItem> items, PingPhase phase) => phase switch
    {
        PingPhase.HttpHead => ProtoApp.Instance.PingHeadAsync(items),
        PingPhase.Icmp => ProtoApp.Instance.PingIcmpAsync(items),
        _ => ProtoApp.Instance.PingAsync(items, ToAction(phase)),
    };

    private static ESpeedActionType ToAction(PingPhase phase) => phase switch
    {
        PingPhase.Realping => ESpeedActionType.Realping,
        PingPhase.UdpTest => ESpeedActionType.UdpTest,
        _ => ESpeedActionType.Tcping,
    };

    /// <summary>Подпись фазы для строки статуса.</summary>
    private static string PhaseLabel(PingPhase phase) => phase switch
    {
        PingPhase.Realping => Loc.T("real ping"),
        PingPhase.UdpTest => Loc.T("udp test"),
        PingPhase.HttpHead => Loc.T("head ping"),
        PingPhase.Icmp => Loc.T("icmp ping"),
        _ => Loc.T("tcp ping"),
    };

    /// <summary>Запомнить замер одной фазы — итог считаем по всем фазам сразу.</summary>
    private void RecordPhase(string indexId, PingPhase phase, int ms)
    {
        if (!_pingPhaseValues.TryGetValue(indexId, out var map))
            _pingPhaseValues[indexId] = map = new Dictionary<PingPhase, int>();

        map[phase] = ms;
        PingTrace($"{phase} id={indexId} ms={ms}");
    }

    /// <summary>Диагностика фаз пинга: UIPROTO_PING=1 → %TEMP%\opencode\ping.log.</summary>
    private static void PingTrace(string line)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("UIPROTO_PING") != "1") return;
            var path = Path.Combine(Path.GetTempPath(), "opencode", "ping.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {line}\r\n");
        }
        catch
        {
            // диагностика не должна ломать пинг
        }
    }

    /// <summary>
    /// Показать итог по приоритету как в Happ: настоящий пинг через сервер,
    /// иначе TCP, иначе UDP; пока есть неизвестные фазы — оставляем «…».
    /// </summary>
    private void ApplyPhaseUi(string indexId, bool final = false)
    {
        if (!_pingPhaseValues.TryGetValue(indexId, out var map) || map.Count == 0)
            return;

        var found = false;
        var ms = -1;
        foreach (var ph in _runDisplay)
        {
            if (map.TryGetValue(ph, out var v) && v > 0)
            {
                ms = v;
                found = true;
                break;
            }
        }

        if (!found)
        {
            // отказы приходят от каждой фазы: до конца всех фаз значение ещё может
            // появиться; на финале (часть фаз могла не ответить) — честное «n/a»
            if (!final && map.Count < _runPhases.Length) return;
            ApplyPingUi(indexId, "n/a", false);
            return;
        }

        ProtoApp.Instance.SetPingDelay(indexId, ms);
        ApplyPingUi(indexId, ms + "ms", true);
    }

    /// <summary>Результат от ServiceLib: индекс + текст («123», «-1» = таймаут).</summary>
    private void OnPingUpdate(string indexId, string delay)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (indexId.Length == 0)
                {
                    // конец очередной фазы (ServiceLib шлёт завершение после каждого
                    // прогона). Закрываем только когда завершились все фазы: событие
                    // может прийти уже после старта следующей фазы — гонка на 2мс
                    var done = ++_pingPhasesDone;
                    PingTrace($"phase end {done}/{_runPhases.Length} msg='{delay}'");
                    if (done >= _runPhases.Length)
                        FinishPing(delay);
                    return;
                }

                _pingPending.Remove(indexId);
                if (int.TryParse(delay, out var ms))
                {
                    RecordPhase(indexId, _pingPhase, ms);
                    ApplyPhaseUi(indexId);
                }
                // прочие служебные строки ServiceLib («Тестирование…») ячейку
                // не растягивают — там остаётся «…» до самого ответа
            }
            catch (Exception ex)
            {
                BarLog.Text = "error: " + ex.Message;
            }
        }));
    }

    private void FinishPing(string message)
    {
        PingTrace($"finish pending={_pingPending.Count} msg='{message}'");
        foreach (var id in _pingPending)
            ApplyPingUi(id, "n/a", false);
        _pingPending.Clear();

        // финальный пересчёт: у кого фаза не ответила (например, UDP) — «n/a»,
        // у остальных — приоритетный замер; иначе ячейка осталась бы «…»
        foreach (var id in _pingPhaseValues.Keys.ToList())
            ApplyPhaseUi(id, final: true);

        if (ServerTree.ItemsSource is List<GroupVm> gs)
            foreach (var vm in gs.SelectMany(g => g.Items).Take(3))
                PingTrace($"final vm id={vm.IndexId} vm='{vm.PingText}' res='{(_pingResults.TryGetValue(vm.IndexId, out var r) ? r.Text : "-")}'");

        // ServiceLib сохранила замер последней фазы — пересохраняем наш приоритетный
        ProtoApp.Instance.CommitPingDelays();

        _pinging = false;
        TreePingText.Opacity = 1;
        // текст ServiceLib («Тест завершён») формируется в фоновом потоке с языком
        // системы — финальный статус берём свой, локализованный
        if (message.Length > 0) TreePingStatus.Text = Loc.T("Test finished");
    }

    private void ApplyPingUi(string indexId, string text, bool? ok)
    {
        _pingResults[indexId] = (text, ok);

        var vm = FindVm(indexId);
        PingTrace($"ui id={indexId} text='{text}' vm={(vm is null ? "null" : "ok")}");
        if (vm is not null)
        {
            vm.PingText = text;
            vm.PingBrush = PingBrushFor(ok);
        }

        // результат «Тест пинга» у карточки выбранного сервера
        if (indexId == _testPingIndexId)
        {
            TestPingResult.Text = text;
            TestPingResult.SetResourceReference(TextBlock.ForegroundProperty,
                ok == false ? "Text3" : "C3D0E5");
        }
    }

    private ProfileVm? FindVm(string indexId)
    {
        if (ServerTree.ItemsSource is not List<GroupVm> groups) return null;

        foreach (var g in groups)
        {
            var hit = g.Items.Find(v => v.IndexId == indexId);
            if (hit is not null) return hit;
        }

        return null;
    }

    /// <summary>Как в Happ: значение — светлое, «n/a» и ожидание — приглушённые.</summary>
    private Brush PingBrushFor(bool? ok) => ok switch
    {
        true => (Brush)FindResource("C3D0E5"),
        _ => (Brush)FindResource("Text3"),
    };

    private async void GroupHeader_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string id }) return;
        if (!_collapsedGroups.Remove(id))
            _collapsedGroups.Add(id);
        try
        {
            await RefreshServersTreeAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    // ------------------------------------------------- group hover actions

    private string? _renameGroupId;
    private string? _renameServerId;

    private async void GroupRename_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string id }) return;
        if (!_initDone || RenamePop is null || string.IsNullOrEmpty(id)) return;

        try
        {
            _renameGroupId = id;
            var groups = await ProtoApp.Instance.GroupsAsync() ?? [];
            RenameBox.Text = groups.FirstOrDefault(t => t.Id == id)?.Remarks ?? string.Empty;
            RenamePop.IsOpen = true;
            RenameBox.Focus();
            RenameBox.SelectAll();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void GroupPin_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string id }) return;
        if (!_initDone || string.IsNullOrEmpty(id)) return;

        try
        {
            await RunOpAsync(() => ProtoApp.Instance.TogglePinGroupAsync(id));
            await RefreshServersTreeAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void GroupDelete_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string id }) return;
        if (!_initDone || string.IsNullOrEmpty(id)) return;

        try
        {
            _collapsedGroups.Remove(id);
            await RunOpAsync(() => ProtoApp.Instance.DeleteGroupAsync(id));
            await RefreshServersTreeAsync();
            await RefreshNodeCardAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void RenameSave_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            await CommitRenameAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private void RenameCancel_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _renameGroupId = null;
        RenamePop.IsOpen = false;
    }

    private async void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            try
            {
                await CommitRenameAsync();
            }
            catch (Exception ex)
            {
                BarLog.Text = "error: " + ex.Message;
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            _renameGroupId = null;
            RenamePop.IsOpen = false;
        }
    }

    private async Task CommitRenameAsync()
    {
        var groupId = _renameGroupId;
        var serverId = _renameServerId;
        var name = RenameBox.Text ?? string.Empty;
        _renameGroupId = null;
        _renameServerId = null;
        RenamePop.IsOpen = false;

        if (!string.IsNullOrEmpty(groupId))
        {
            await RunOpAsync(() => ProtoApp.Instance.RenameGroupAsync(groupId, name));
            await RefreshServersTreeAsync();
        }
        else if (!string.IsNullOrEmpty(serverId))
        {
            await RunOpAsync(() => ProtoApp.Instance.RenameServerAsync(serverId, name));
            await RefreshServersTreeAsync();
            await RefreshNodeCardAsync();
        }
    }

    private static string ProtocolIcon(string configType) => configType switch
    {
        "vmess" => "\U0001F517",
        "vless" => "\U0001F517",
        "trojan" => "\U0001F6E1",
        "shadowsocks" => "\U0001F512",
        "socks" => "\U0001F517",
        "http" => "\U0001F310",
        "hysteria2" => "\U0001F680",
        "wireguard" => "\U0001F512",
        _ => "\U0001F4E1",
    };

    private async void ServerRename_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string indexId }) return;
        if (!_initDone || string.IsNullOrEmpty(indexId)) return;

        try
        {
            var app = ProtoApp.Instance;
            var profiles = await app.ProfilesAsync() ?? [];
            var profile = profiles.FirstOrDefault(t => t.IndexId == indexId);
            if (profile is not null)
            {
                _renameServerId = indexId;
                RenameBox.Text = profile.Remarks;
                RenamePop.IsOpen = true;
                RenameBox.Focus();
                RenameBox.SelectAll();
            }
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void ServerRow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string indexId }) return;

        // двойной клик — переименовать сервер
        if (e.ClickCount == 2)
        {
            try
            {
                var app = ProtoApp.Instance;
                var profiles = await app.ProfilesAsync() ?? [];
                var profile = profiles.FirstOrDefault(t => t.IndexId == indexId);
                if (profile is not null)
                {
                    _renameServerId = indexId;
                    RenameBox.Text = profile.Remarks;
                    RenamePop.IsOpen = true;
                    RenameBox.Focus();
                    RenameBox.SelectAll();
                }
            }
            catch (Exception ex)
            {
                BarLog.Text = "error: " + ex.Message;
            }
            return;
        }

        try
        {
            await RunOpAsync(() => ProtoApp.Instance.SelectProfileAsync(indexId));
            await RefreshNodeCardAsync();
            await RefreshServersTreeAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void MoveBtn_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string indexId }) return;
        if (!_initDone || MovePop is null) return;

        try
        {
            _moveIndexId = indexId;
            var app = ProtoApp.Instance;
            var profiles = await app.ProfilesAsync() ?? [];
            var groups = await app.GroupsAsync() ?? [];
            var profile = profiles.FirstOrDefault(t => t.IndexId == indexId);

            var opts = new List<(string Value, string Display)> { ("", Loc.T("Server list")) };
            opts.AddRange(groups.Select(g => (g.Id, g.Remarks ?? g.Id)));
            BuildMenu(MoveItems, opts.ToArray(), profile?.Subid ?? "", v => _ = PickMoveGroupAsync(v));
            MovePop.IsOpen = true;
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async Task PickMoveGroupAsync(string groupId)
    {
        try
        {
            MovePop.IsOpen = false;
            if (_moveIndexId is null) return;

            var app = ProtoApp.Instance;
            var profiles = await app.ProfilesAsync() ?? [];
            var profile = profiles.FirstOrDefault(t => t.IndexId == _moveIndexId);
            _moveIndexId = null;
            if (profile is null) return;

            await RunOpAsync(() => app.MoveServerAsync(profile, groupId));
            await RefreshServersTreeAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void TreeAdd_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string mode }) return;
        if (!_initDone) return;

        try
        {
            await OpenPickerAsync();
            ShowImport(mode);
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    // =============================================================== import

    private string _importMode = "server";

    private void AddServerBtn_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowImport("server");
    }

    private void AddSubBtn_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowImport("sub");
    }

    private void ShowImport(string mode)
    {
        _importMode = mode;
        ImportHint.Text = Loc.T(mode switch
        {
            "sub" => "Subscription URL — Add will fetch its servers right away",
            "group" => "Group name — move servers into it with the ⇄ button",
            _ => "Server link (ss:// vmess:// vless:// trojan:// hysteria2:// …) or a base64 list",
        });
        ImportBox.Text = string.Empty;
        ImportPanel.Visibility = Visibility.Visible;
        _ = ImportBox.Focus();
    }

    private void ImportCancel_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ImportPanel.Visibility = Visibility.Collapsed;
    }

    private void ImportPaste_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            ImportBox.Text = System.Windows.Clipboard.GetText();
        }
        catch
        {
            BarLog.Text = Loc.T("clipboard unavailable");
        }
    }

    private async void ImportAdd_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var text = ImportBox.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            ImportPanel.Visibility = Visibility.Collapsed;
            return;
        }

        Func<Task<OpResult>> op = _importMode switch
        {
            "sub" => () => ProtoApp.Instance.AddSubscriptionAsync(text),
            "group" => () => ProtoApp.Instance.CreateGroupAsync(text),
            _ => () => ProtoApp.Instance.AddServerAsync(text),
        };

        try
        {
            var ok = await RunOpAsync(op);
            if (ok)
            {
                ImportPanel.Visibility = Visibility.Collapsed;
                await OpenPickerAsync();
                await RefreshNodeCardAsync();
                await RefreshServersTreeAsync();
            }
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async Task RefreshNodeCardAsync()
    {
        try
        {
            // карточка сменилась — прежний результат «Тест пинга» больше неактуален
            if (!_pinging)
            {
                _testPingIndexId = null;
                TestPingResult.Text = "";
            }

            var p = await ProtoApp.Instance.CurrentProfileAsync();
            if (p is null)
            {
                NodeName.Text = Loc.T("No server selected");
                NodeAddr.Text = Loc.T("Press Change to pick a server");
                NodeProtoText.Text = "—";
                NodeSecText.Text = "—";
                return;
            }

            NodeName.Text = string.IsNullOrEmpty(p.Remarks) ? p.GetSummary() : p.Remarks;
            NodeProtoText.Text = p.ConfigType.ToString();
            NodeSecText.Text = string.IsNullOrEmpty(p.StreamSecurity) ? p.GetNetwork() : p.StreamSecurity;
            NodeAddr.Text = p.Address + ":" + p.Port + Loc.T(" · Selected configuration");
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    // ============================================================== routing

    private string? _selectedRoutingId;
    private RoutingItem? _editRouting;
    private readonly List<RulesItem> _editRules = [];

    private string _ruleType = "domain";
    private string _ruleAction = "proxy";

    private static readonly Dictionary<string, string> RuleIcons = new()
    {
        ["domain"] = "\U0001F310",
        ["ip"] = "\U0001F4E1",
        ["process"] = "\U0001F527",
        ["port"] = "\U0001F50C",
    };

    private static readonly Dictionary<string, string> KindTitles = new()
    {
        ["domain"] = "Domain",
        ["ip"] = "IP",
        ["process"] = "Process",
        ["port"] = "Port",
    };

    /// <summary>Список пресетов + правила выбранного (двухколоночный экран).</summary>
    private async Task LoadRoutingsAsync()
    {
        try
        {
            var items = await ProtoApp.Instance.RoutingsAsync() ?? [];
            var ordered = items.Where(r => r.Enabled).OrderBy(r => r.Sort).ToList();

            if (_selectedRoutingId is null || ordered.All(r => r.Id != _selectedRoutingId))
                _selectedRoutingId = ordered.FirstOrDefault(r => r.IsActive)?.Id
                                     ?? ordered.FirstOrDefault()?.Id;

            var vms = ordered
                .Select((r, i) => new RoutingVm(
                    r.Id,
                    RouteIcons[i % RouteIcons.Length],
                    string.IsNullOrEmpty(r.Remarks) ? Loc.TF("Routing {0}", i + 1) : r.Remarks,
                    DescribeRouting(r),
                    r.IsActive,
                    r.Id == _selectedRoutingId))
                .ToList();

            PresetList.ItemsSource = vms;
            await LoadSelectedRoutingAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private static string DescribeRouting(RoutingItem r) => r.RuleNum > 0
        ? Loc.TF("{0} rules", r.RuleNum)
        : string.IsNullOrEmpty(r.Url) ? Loc.T("custom rules") : Loc.T("ruleset by url");

    private async Task LoadSelectedRoutingAsync()
    {
        _editRouting = null;
        _editRules.Clear();

        if (_selectedRoutingId is not null)
        {
            var items = await ProtoApp.Instance.RoutingsAsync() ?? [];
            _editRouting = items.FirstOrDefault(t => t.Id == _selectedRoutingId);
            if (_editRouting is not null)
                _editRules.AddRange(ProtoApp.Instance.GetRoutingRules(_editRouting));
        }

        RenderRuleEditor();
    }

    /// <summary>Правый столбец: заголовок, счётчик, пустое состояние и строки правил.</summary>
    private void RenderRuleEditor()
    {
        if (RuleList is null) return;

        var hasPreset = _editRouting is not null;
        RuleEditorTitle.Text = hasPreset && !string.IsNullOrEmpty(_editRouting!.Remarks)
            ? _editRouting.Remarks
            : Loc.T("Rules");
        RuleCountText.Text = _editRules.Count.ToString();
        RuleEditorHint.Text = hasPreset
            ? Loc.T(_editRouting!.IsActive ? "Active" : "Inactive")
            : Loc.T("Select a preset on the left");
        RuleEmpty.Visibility = _editRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RuleList.ItemsSource = _editRules.Select(ToRuleVm).ToList();
        PaintRuleChips();
    }

    private static RuleVm ToRuleVm(RulesItem r, int index)
    {
        _ = index;

        var kind = r.Domain is { Count: > 0 } ? "domain"
            : r.Ip is { Count: > 0 } ? "ip"
            : r.Process is { Count: > 0 } ? "process"
            : !string.IsNullOrEmpty(r.Port) || !string.IsNullOrEmpty(r.Network) ? "port"
            : "process";

        var parts = new List<string>();
        if (r.Domain is { Count: > 0 }) parts.AddRange(r.Domain);
        if (r.Ip is { Count: > 0 }) parts.AddRange(r.Ip);
        if (r.Process is { Count: > 0 }) parts.AddRange(r.Process);
        if (!string.IsNullOrEmpty(r.Port)) parts.Add(Loc.T("Port") + ": " + r.Port);
        if (!string.IsNullOrEmpty(r.Network)) parts.Add("net: " + r.Network);
        if (r.Protocol is { Count: > 0 }) parts.Add(string.Join(", ", r.Protocol));
        if (r.InboundTag is { Count: > 0 }) parts.Add("in: " + string.Join(", ", r.InboundTag));

        var action = string.IsNullOrEmpty(r.OutboundTag) ? "proxy" : r.OutboundTag!;
        var actionLabel = action is "proxy" or "direct" or "block" ? Loc.T(action) : action;

        return new RuleVm(
            r.Id ?? string.Empty,
            RuleIcons.GetValueOrDefault(kind, "\U0001F310"),
            parts.Count > 0 ? string.Join(", ", parts) : (r.Remarks ?? string.Empty),
            Loc.T(KindTitles.GetValueOrDefault(kind, "Domain")),
            action,
            actionLabel,
            r.Enabled);
    }

    private async void RoutingToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || !_initDone || sender is not CheckBox cb || cb.Tag is not string id)
            return;

        try
        {
            if (cb.IsChecked != true)
            {
                // снятие активного тумблера — вернуть единственный активный роутинг
                _ = Dispatcher.BeginInvoke(new Action(ReloadRoutingsUi));
                return;
            }

            await RunOpAsync(() => ProtoApp.Instance.SelectRoutingAsync(id));
            _ = Dispatcher.BeginInvoke(new Action(ReloadRoutingsUi));
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private void ReloadRoutingsUi() => _ = LoadRoutingsAsync();

    /// <summary>Клик по строке пресета — редактируем его правила в правом столбце.</summary>
    private async void PresetRow_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string id } || id == _selectedRoutingId) return;

        try
        {
            _selectedRoutingId = id;
            await LoadRoutingsAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    // ------------------------------------------------------------- правила

    private async Task SaveRulesAsync()
    {
        if (_editRouting is null) return;

        var rc = await ProtoApp.Instance.SaveRoutingRulesAsync(_editRouting, _editRules);
        if (!rc.Success)
            BarLog.Text = rc.Message;
    }

    private void RuleType_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string type }) return;
        _ruleType = type;
        PaintRuleChips();
    }

    private void RuleAction_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string action }) return;
        _ruleAction = action;
        PaintRuleChips();
    }

    /// <summary>Подсветка выбранных чипов «тип» и «действие».</summary>
    private void PaintRuleChips()
    {
        PaintChip(RuleTypeDomain, _ruleType == "domain");
        PaintChip(RuleTypeIp, _ruleType == "ip");
        PaintChip(RuleTypeProcess, _ruleType == "process");
        PaintChip(RuleTypePort, _ruleType == "port");

        PaintChip(RuleActionProxy, _ruleAction == "proxy");
        PaintChip(RuleActionDirect, _ruleAction == "direct");
        PaintChip(RuleActionBlock, _ruleAction == "block");
    }

    private void PaintChip(Border chip, bool on)
    {
        if (chip is null) return;
        // именно ссылка на ресурс, а не объект кисти: при смене темы замороженная
        // кисть в палитре заменяется новой, и локальная ссылка «остынет» по-старому
        chip.SetResourceReference(Border.BackgroundProperty, on ? "AccentSoft" : "1A2942");
        chip.SetResourceReference(Border.BorderBrushProperty, on ? "Accent" : "2E4466");
        if (chip.Child is TextBlock label)
            label.SetResourceReference(TextBlock.ForegroundProperty, on ? "Accent" : "C3D0E5");
    }

    private async void RuleAdd_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            await CommitRuleAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void RuleValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        try
        {
            await CommitRuleAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    /// <summary>Новое правило: значение из поля + выбранный тип и действие.</summary>
    private async Task CommitRuleAsync()
    {
        if (_editRouting is null)
        {
            BarLog.Text = Loc.T("select a preset first");
            return;
        }

        var raw = RuleValueBox.Text?.Trim() ?? string.Empty;
        if (raw.Length == 0)
            return;

        var rule = new RulesItem
        {
            Id = Utils.GetGuid(false),
            Enabled = true,
            OutboundTag = _ruleAction,
            Remarks = string.Empty,
        };

        switch (_ruleType)
        {
            case "ip":
                rule.Ip = SplitValues(raw);
                break;
            case "process":
                rule.Process = SplitValues(raw);
                break;
            case "port":
                rule.Port = raw;
                break;
            default:
                rule.Domain = SplitValues(raw);
                break;
        }

        _editRules.Insert(0, rule);
        RuleValueBox.Text = string.Empty;

        await SaveRulesAsync();
        await LoadSelectedRoutingAsync();
    }

    private static List<string> SplitValues(string raw)
        => [.. raw.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private void InfoLink_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (AboutPop is null) return;

        var app = ProtoApp.Instance;
        if (AboutSocks is not null)
            AboutSocks.Text = Loc.TF("SOCKS: 127.0.0.1:{0}", app.SocksPort);
        if (AboutHttp is not null)
            AboutHttp.Text = Loc.TF("HTTP: 127.0.0.1:{0}", app.HttpPort);

        AboutPop.IsOpen = true;
    }

    private void AboutClose_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (AboutPop is not null) AboutPop.IsOpen = false;
    }

    private void AboutRepo_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/wifitldev/v2crackpc",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void AboutTelegram_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://t.me/v2crackNG",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void AboutPc_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/wifitldev/v2crackpc/releases/tag/1.1.2",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void AboutMobile_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/wifitldev/v2crackpc",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private async void RuleToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not CheckBox cb || cb.Tag is not string id) return;

        try
        {
            var rule = _editRules.FirstOrDefault(t => t.Id == id);
            if (rule is null) return;

            rule.Enabled = cb.IsChecked == true;
            await SaveRulesAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void RuleDelete_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not Border { Tag: string id }) return;

        try
        {
            var rule = _editRules.FirstOrDefault(t => t.Id == id);
            if (rule is null) return;

            _editRules.Remove(rule);
            await SaveRulesAsync();
            RenderRuleEditor();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    // ------------------------------------------------------- новый пресет

    private void NewPreset_Click(object sender, RoutedEventArgs e)
    {
        if (!_initDone || NewPresetPop is null) return;

        NewPresetBox.Text = string.Empty;
        NewPresetPop.IsOpen = true;
        NewPresetBox.Focus();
    }

    private async void NewPresetCreate_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        try
        {
            await CommitNewPresetAsync();
        }
        catch (Exception ex)
        {
            BarLog.Text = "error: " + ex.Message;
        }
    }

    private async void NewPresetBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            try
            {
                await CommitNewPresetAsync();
            }
            catch (Exception ex)
            {
                BarLog.Text = "error: " + ex.Message;
            }
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            NewPresetPop.IsOpen = false;
        }
    }

    private void NewPresetCancel_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        NewPresetPop.IsOpen = false;
    }

    private async Task CommitNewPresetAsync()
    {
        var name = NewPresetBox.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            NewPresetPop.IsOpen = false;
            return;
        }

        var rc = await ProtoApp.Instance.CreateRoutingAsync(name);
        NewPresetPop.IsOpen = false;

        if (!rc.Success)
        {
            BarLog.Text = rc.Message;
            return;
        }

        var items = await ProtoApp.Instance.RoutingsAsync() ?? [];
        _selectedRoutingId = items.FirstOrDefault(t => t.Remarks == name)?.Id ?? _selectedRoutingId;
        await LoadRoutingsAsync();
    }

    // ============================================================== closing

    /// <summary>Реальный выход (меню трея «Выйти»): минуя сворачивание в трей.</summary>
    public void ForceExit()
    {
        _forceExit = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_exitDone) return;

        // с активным прокси/туннелем крестик прячет окно в трей,
        // реальный выход — только «Выйти» в меню трея
        if (!_forceExit && (ProtoApp.Instance.TunnelMode || ProtoApp.Instance.CoreUp))
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            Hide();
            return;
        }

        e.Cancel = true;
        _exitDone = true;
        _tray?.Dispose();
        _ = CloseAfterExitAsync();
    }

    private async Task CloseAfterExitAsync()
    {
        _timer.Stop();
        try
        {
            await ProtoApp.Instance.ExitAsync();
        }
        catch
        {
            // still close the window
        }

        Close();
    }

    // ============================================================== autotest

    private async Task RunAutoTestAsync()
    {
        var app = ProtoApp.Instance;
        var sb = new StringBuilder();
        sb.AppendLine("v2crackN-UiProto autotest · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("exe: " + Environment.ProcessPath);
        sb.AppendLine("base: " + PayloadPrep.BaseDir);
        var failed = 0;

        void Step(string name, bool pass, string detail = "")
        {
            sb.AppendLine($"[{(pass ? "PASS" : "FAIL")}] {name}" + (detail.Length > 0 ? " — " + detail : ""));
            if (!pass) failed++;
            BarLog.Text = $"autotest · {name}: {(pass ? "ok" : "FAIL")}";
        }

        try
        {
            Step("init", app.Initialized, "socks " + app.SocksPort);

            var profiles = await app.ProfilesAsync() ?? [];
            var routings = await app.RoutingsAsync() ?? [];
            Step("profiles", profiles.Count > 0, profiles.Count + " in db");
            Step("routings", routings.Count > 0, routings.Count + " in db");

            // импорт тестового сервера и его удаление (функция "+ Server")
            var importRc = await app.AddServerAsync(TestVmessLink);
            var imported = (await app.ProfilesAsync() ?? []).FirstOrDefault(t => t.Remarks == "import-test");
            var importOk = importRc.Success && imported is not null;
            if (imported is not null)
                await app.RemoveServerAsync(imported);
            Step("import server", importOk, importOk ? "added then removed" : importRc.Message);

            // групповые действия: create → rename → pin → unpin → delete
            var groupOk = false;
            string groupDetail;
            var gCreate = await app.CreateGroupAsync("autotest-group");
            var gItem = (await app.GroupsAsync() ?? []).FirstOrDefault(t => t.Remarks == "autotest-group");
            if (gCreate.Success && gItem is not null)
            {
                var gRename = await app.RenameGroupAsync(gItem.Id, "autotest-renamed");
                var renamed = (await app.GroupsAsync() ?? [])
                    .Any(t => t.Id == gItem.Id && t.Remarks == "autotest-renamed");

                var gPin = await app.TogglePinGroupAsync(gItem.Id);
                var pinned = (await app.GroupsAsync() ?? [])
                    .Any(t => t.Id == gItem.Id && t.Memo == ProtoApp.PinnedMemo);
                await app.TogglePinGroupAsync(gItem.Id);
                var unpinned = (await app.GroupsAsync() ?? [])
                    .Any(t => t.Id == gItem.Id && t.Memo != ProtoApp.PinnedMemo);

                var gDel = await app.DeleteGroupAsync(gItem.Id);
                var gone = (await app.GroupsAsync() ?? []).All(t => t.Id != gItem.Id);

                groupOk = gRename.Success && renamed && gPin.Success && pinned
                          && unpinned && gDel.Success && gone;
                groupDetail = groupOk
                    ? "created, renamed, pinned, deleted"
                    : $"rename={gRename.Success && renamed} pin={gPin.Success && pinned} " +
                      $"unpin={unpinned} del={gDel.Success && gone}";
            }
            else
            {
                groupDetail = gCreate.Message;
            }
            Step("group actions", groupOk, groupDetail);

            // пресет роутинга: create → правила → сохранение → удаление
            var presetOk = false;
            string presetDetail;
            var pCreate = await app.CreateRoutingAsync("autotest-preset");
            var pItem = (await app.RoutingsAsync() ?? []).FirstOrDefault(t => t.Remarks == "autotest-preset");
            if (pCreate.Success && pItem is not null)
            {
                var rules = new List<RulesItem>
                {
                    new()
                    {
                        Id = Utils.GetGuid(false),
                        Domain = ["example.com"],
                        OutboundTag = Global.ProxyTag,
                        Enabled = true,
                    },
                    new()
                    {
                        Id = Utils.GetGuid(false),
                        Port = "80",
                        OutboundTag = Global.DirectTag,
                        Enabled = false,
                    },
                };

                var pSave = await app.SaveRoutingRulesAsync(pItem, rules);
                var loaded = app.GetRoutingRules(pItem);
                var savedOk = pSave.Success && loaded.Count == 2
                              && loaded[0].Domain?.Contains("example.com") == true
                              && loaded[1].Enabled == false;

                var pDel = await app.DeleteRoutingAsync(pItem.Id);
                var gone = (await app.RoutingsAsync() ?? []).All(t => t.Id != pItem.Id);

                presetOk = savedOk && pDel.Success && gone;
                presetDetail = presetOk
                    ? "created, 2 rules saved, deleted"
                    : $"save={savedOk} rules={loaded.Count} del={pDel.Success} gone={gone}";
            }
            else
            {
                presetDetail = pCreate.Message;
            }
            Step("routing preset", presetOk, presetDetail);

            var rc = await app.ConnectAsync();
            Step("connect", rc.Success, rc.Success ? "port " + app.SocksPort : rc.Message);

            if (rc.Success)
            {
                PaintConnected();
                Step("port alive", await app.IsPortAliveAsync());

                try
                {
                    var probe = await SocksHttpProbeAsync(app.SocksPort, 20000);
                    Step("traffic via socks", probe == "ok", probe);
                }
                catch (Exception ex)
                {
                    Step("traffic via socks", false, ex.Message);
                }

                await app.SetTunnelModeAsync(true);
                SyncModeToggles(true);
                var onValue = ReadProxyEnable();
                Step("system proxy on", onValue == 1, "ProxyEnable=" + (onValue?.ToString() ?? "null"));

                await app.SetTunnelModeAsync(false);
                SyncModeToggles(false);
                var offValue = ReadProxyEnable();
                Step("system proxy off", offValue == 0, "ProxyEnable=" + (offValue?.ToString() ?? "null"));
            }

            await app.SetAutoRunAsync(true);
            var runOn = RunKeyHasOurExe();
            await app.SetAutoRunAsync(false);
            Step("autostart registry", runOn && !RunKeyHasOurExe(),
                runOn ? "added then removed" : "value not found");

            if (rc.Success)
            {
                var rd = await app.DisconnectAsync();
                PaintIdle();
                Step("disconnect", rd.Success, rd.Message);
                Step("port closed", !await app.IsPortAliveAsync());
            }
        }
        catch (Exception ex)
        {
            Step("exception", false, ex.GetType().Name + ": " + ex.Message);
        }

        var pass = failed == 0;
        sb.AppendLine(pass ? "RESULT: PASS" : $"RESULT: FAIL ({failed} step(s))");
        try
        {
            File.WriteAllText(Path.Combine(PayloadPrep.BaseDir, "autotest.log"), sb.ToString());
        }
        catch
        {
            // log file is a bonus, result is also in the status bar
        }

        BarLog.Text = pass
            ? Loc.T("AUTOTEST: PASS — autotest.log")
            : Loc.TF("AUTOTEST: FAIL ({0}) — autotest.log", failed);

        _exitDone = true;
        _timer.Stop();
        await app.ExitAsync();
        Application.Current.Shutdown(pass ? 0 : 1);
    }

    private static int? ReadProxyEnable()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            return key?.GetValue("ProxyEnable") as int?;
        }
        catch
        {
            return null;
        }
    }

    private static bool RunKeyHasOurExe()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key is null) return false;

            var exe = Path.GetFileName(Environment.ProcessPath ?? "v2crackN-UiProto.exe");
            foreach (var name in key.GetValueNames())
            {
                var data = key.GetValue(name)?.ToString();
                if (!string.IsNullOrEmpty(data) &&
                    data.Contains(exe, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Минимальный SOCKS5-клиент: CONNECT к msftconnecttest + HTTP GET.</summary>
    private static async Task<string> SocksHttpProbeAsync(int port, int timeoutMs)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(timeoutMs);

        await client.ConnectAsync("127.0.0.1", port, cts.Token);
        var stream = client.GetStream();

        // greeting: ver5, 1 method, no-auth
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cts.Token);
        var resp = new byte[2];
        await ReadExactAsync(stream, resp, 2, cts.Token);
        if (resp[0] != 5 || resp[1] == 0xFF)
            return "socks greeting rejected (method=" + resp[1] + ")";

        // connect request to www.msftconnecttest.com:80
        var host = Encoding.ASCII.GetBytes("www.msftconnecttest.com");
        var req = new byte[7 + host.Length];
        req[0] = 0x05;              // ver
        req[1] = 0x01;              // CONNECT
        req[2] = 0x00;              // rsv
        req[3] = 0x03;              // DOMAINNAME
        req[4] = (byte)host.Length;
        Buffer.BlockCopy(host, 0, req, 5, host.Length);
        req[^2] = 0x00;             // port hi
        req[^1] = 0x50;             // port 80
        await stream.WriteAsync(req, cts.Token);

        var reply = new byte[4];
        await ReadExactAsync(stream, reply, 4, cts.Token);
        if (reply[1] != 0x00)
            return "socks connect refused (rep=" + reply[1] + ")";

        // ответ CONNECT продолжается адресом и портом - дочитываем, иначе
        // остаток попадёт в HTTP-ответ
        int tail;
        switch (reply[3])
        {
            case 0x01: // IPv4
                tail = 4 + 2;
                break;
            case 0x04: // IPv6
                tail = 16 + 2;
                break;
            case 0x03: // domain: 1 byte length + name + port
            {
                var lenBuf = new byte[1];
                await ReadExactAsync(stream, lenBuf, 1, cts.Token);
                tail = lenBuf[0] + 2;
                break;
            }
            default:
                return "socks: unknown atyp " + reply[3];
        }

        var rest = new byte[tail];
        await ReadExactAsync(stream, rest, tail, cts.Token);

        var http = "GET /connecttest.txt HTTP/1.1\r\n" +
                   "Host: www.msftconnecttest.com\r\n" +
                   "Connection: close\r\n" +
                   "User-Agent: v2crackN-UiProto\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(http), cts.Token);

        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (text.Length < 8192)
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            if (read <= 0) break;
            text.Append(Encoding.ASCII.GetString(buffer, 0, read));
            if (text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal) &&
                text.Length > 64)
                break;
        }

        var head = text.ToString();
        if (head.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) ||
            head.StartsWith("HTTP/1.0 200", StringComparison.Ordinal))
            return "ok";

        var firstLine = head.Split('\n')[0].Trim();
        return "unexpected response: " + (firstLine.Length > 0 ? firstLine : "empty");
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct)
    {
        var got = 0;
        while (got < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(got, count - got), ct);
            if (read <= 0)
                throw new IOException("connection closed by peer");
            got += read;
        }
    }
}

/// <summary>Пресет роутинга в левой колонке; Selected — подсветка редактируемого.</summary>
public sealed record RoutingVm(string Id, string Icon, string Name, string Description, bool IsOn, bool Selected);

/// <summary>Строка правила в редакторе: значение, тип, действие и тумблер Enabled.</summary>
public sealed class RuleVm
{
    public string Id { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string KindLabel { get; init; } = string.Empty;
    public string Action { get; init; } = "proxy";
    public string ActionLabel { get; init; } = "proxy";
    public bool Enabled { get; set; } = true;

    public RuleVm() { }

    public RuleVm(string id, string icon, string value, string kindLabel,
                  string action, string actionLabel, bool enabled)
    {
        Id = id;
        Icon = icon;
        Value = value;
        KindLabel = kindLabel;
        Action = action;
        ActionLabel = actionLabel;
        Enabled = enabled;
    }
}

public sealed record ProfileVm(string IndexId, string Name, string Summary, Visibility ActiveVisibility, string ProtocolIcon)
    : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _pingText = "—";
    private Brush? _pingBrush;

    /// <summary>Пинг сервера: «123 ms», «…» пока идёт, «—» до первого замера.</summary>
    public string PingText
    {
        get => _pingText;
        set
        {
            _pingText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PingText)));
        }
    }

    /// <summary>Цвет строки пинга (зелёный — живой, красный — таймаут, серый — нет замера).</summary>
    public Brush? PingBrush
    {
        get => _pingBrush;
        set
        {
            _pingBrush = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PingBrush)));
        }
    }
}

/// <summary>Группа в дереве Connect: «Server list» (Id="") или подписка/пользовательская группа.</summary>
public sealed record GroupVm(string Id, string Name, string Kind, int Count,
                             List<ProfileVm> Items, bool Collapsed, bool Pinned)
{
    private readonly bool _canManage =
        !string.IsNullOrEmpty(Id) && Id != ServiceLib.Global.PermanentSubId;

    public string CountText => Count == 0 ? "" : Count.ToString();
    public Visibility KindVisibility => string.IsNullOrEmpty(Kind) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ListVisibility => Collapsed ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EmptyVisibility => Count > 0 || Collapsed ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Hover-панель показывается только у настоящих групп (не у «Server list»).</summary>
    public bool CanPin => !string.IsNullOrEmpty(Id);

    /// <summary>Rename/доступно только пользовательским группам и подпискам (permanent — нельзя).</summary>
    public Visibility ManageVisibility => _canManage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PinBtnVisibility => CanPin ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PinMarkVisibility => Pinned ? Visibility.Visible : Visibility.Collapsed;
}
