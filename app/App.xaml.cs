using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace UiProto;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _showWindowEvent;
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationTask;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Одна копия приложения: если уже запущена — поднимаем её окно и выходим.
        // Имя мьютекса локальное (Local\), чтобы не конфликтовать с другими программами.
        _singleInstance = new Mutex(true, @"Local\v2crackN", out var firstInstance);
        if (!firstInstance)
        {
            SignalRunningInstance();
            Shutdown(0);
            return;
        }

        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\v2crackN.ShowWindow");
        _activationCancellation = new CancellationTokenSource();
        var activationToken = _activationCancellation.Token;
        var activationEvent = _showWindowEvent;
        _activationTask = Task.Run(() => ListenForActivation(activationEvent, activationToken));

        // Аппаратный D3D-путь WPF на этой машине не отдаёт кадр (белый экран при
        // полностью корректном внутреннем рендере — проверено на минимальном
        // Hello-World; WinForms/GDI при этом работают). По умолчанию — программная
        // отрисовка: для статичного прототипа её качества и плавности достаточно.
        // --hw: попробовать аппаратный режим (если драйвер GPU будет починен).
        if (!e.Args.Any(a => string.Equals(a, "--hw", StringComparison.OrdinalIgnoreCase)))
            System.Windows.Media.RenderOptions.ProcessRenderMode =
                System.Windows.Interop.RenderMode.SoftwareOnly;

        base.OnStartup(e);

        // Палитра и язык должны существовать до разбора MainWindow.xaml
        // (там StaticResource и локализация первого окна).
        try
        {
            PayloadPrep.Ensure();
            ThemeManager.Apply(PayloadPrep.ReadUiTheme());
            Loc.SetLang(PayloadPrep.ReadUiLanguage());
        }
        catch
        {
            ThemeManager.Apply(null);
            Loc.SetLang(null);
        }
    }

    private static void SignalRunningInstance()
    {
        // The first process may still be creating the named event.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(@"Local\v2crackN.ShowWindow");
                signal.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) { break; }
        }
        FocusRunningInstance();
    }

    private void ListenForActivation(EventWaitHandle signal, CancellationToken token)
    {
        while (WaitHandle.WaitAny([signal, token.WaitHandle]) == 0)
        {
            // StartupUri creates the window after OnStartup. Wait until startup
            // work has been dispatched before handling an immediate second launch.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                if (token.IsCancellationRequested || MainWindow is not Window window) return;
                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            });
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation?.Cancel();
        try { _activationTask?.GetAwaiter().GetResult(); }
        catch (Exception) { /* application is exiting */ }
        _showWindowEvent?.Dispose();
        _activationCancellation?.Dispose();
        _activationTask = null;
        _showWindowEvent = null;
        _activationCancellation = null;
        if (_singleInstance is not null)
        {
            try { _singleInstance.ReleaseMutex(); } catch { /* уже отпущена */ }
            _singleInstance.Dispose();
            _singleInstance = null;
        }

        base.OnExit(e);
    }

    /// <summary>Второй запуск: найти открытую копию и вывести её окно на передний план.</summary>
    private static void FocusRunningInstance()
    {
        try
        {
            var me = Process.GetCurrentProcess().ProcessName;
            foreach (var p in Process.GetProcessesByName(me))
            {
                try
                {
                    if (p.Id == Environment.ProcessId || p.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(p.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(p.MainWindowHandle);
                    break;
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // фокус — пожелание, а не обязанность
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
