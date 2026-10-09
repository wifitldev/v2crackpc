using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace UiProto;

/// <summary>
/// Иконка в системном лотке через WinAPI (Shell_NotifyIcon).
/// Клик — показать/скрыть окно, ПКМ — меню.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_USER = 0x0400;
    private const uint TRAY_MESSAGE = WM_USER + 1;
    private const uint ID_TRAY_ICON = 1001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private bool _disposed;

    public TrayIcon(Window window)
    {
        _window = window;
        _hwnd = new WindowInteropHelper(window).Handle;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = ID_TRAY_ICON,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = TRAY_MESSAGE,
            hIcon = LoadIcon(IntPtr.Zero, new IntPtr(32516)), // IDI_APPLICATION
            szTip = "v2crackN"
        };

        Shell_NotifyIcon(NIM_ADD, ref nid);

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        _window.StateChanged += (s, e) =>
        {
            if (window.WindowState == WindowState.Minimized)
                window.Hide();
        };
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == TRAY_MESSAGE)
        {
            switch (lParam.ToInt64())
            {
                case WM_LBUTTONUP:
                    ShowWindow();
                    handled = true;
                    break;
                case WM_RBUTTONUP:
                    ShowContextMenu();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ShowContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var connect = new System.Windows.Controls.MenuItem { Header = "Подключить" };
        connect.Click += (s, e) => _ = ProtoApp.Instance.ConnectAsync();

        var disconnect = new System.Windows.Controls.MenuItem { Header = "Отключить" };
        disconnect.Click += (s, e) => _ = ProtoApp.Instance.DisconnectAsync();

        var show = new System.Windows.Controls.MenuItem { Header = "Показать" };
        show.Click += (s, e) => ShowWindow();

        var exit = new System.Windows.Controls.MenuItem { Header = "Выйти" };
        exit.Click += (s, e) =>
        {
            if (_window is MainWindow mw) mw.ForceExit();
            else _window.Close();
        };

        menu.Items.Add(connect);
        menu.Items.Add(disconnect);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(show);
        menu.Items.Add(exit);

        menu.PlacementTarget = null;
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = ID_TRAY_ICON
        };
        Shell_NotifyIcon(NIM_DELETE, ref nid);
    }
}
