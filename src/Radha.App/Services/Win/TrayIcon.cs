using System.Runtime.InteropServices;

namespace Radha.App.Services.Win;

/// <summary>
/// System tray icon.
///
/// Windows has no WinUI tray control, so this is the documented <c>Shell_NotifyIcon</c> path: a
/// message-only window owned by this process receives the callback, and the icon lives in the
/// notification area. Left click raises <see cref="Activated"/>, right click raises
/// <see cref="ContextRequested"/> — the host decides what the menu contains.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WmApp = 0x8000;
    private const int WmTray = WmApp + 1;
    private const int WmCommand = 0x0111;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonDblClk = 0x0203;

    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private const uint NotifyIconVersion4 = 4;

    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfGrayed = 0x00000001;

    private const int CwUseDefault = unchecked((int)0x80000000);
    private const int IdiApplication = 32512;
    private const int WsOverlapped = 0x00000000;

    private static WndProcDelegate? _wndProc;   // must outlive the window: the OS holds the pointer
    private static readonly object StaticGate = new();
    private static readonly Dictionary<IntPtr, TrayIcon> Windows = new();

    private IntPtr _hwnd;
    private IntPtr _icon;
    private bool _added;
    private bool _disposed;
    private string _className = "RadhaTraySink";

    public event Action? Activated;
    public event Action? DoubleClicked;
    public event Action? ContextRequested;

    public string Tooltip { get; private set; } = "RADHA SHELL";

    public IntPtr Handle => _hwnd;
    public bool IsVisible => _added;

    /// <summary>Menu items rendered on right click: (id, label). A null label draws a separator.</summary>
    public Func<IReadOnlyList<(int Id, string? Label)>>? MenuBuilder { get; set; }

    /// <summary>Raised with the chosen menu id.</summary>
    public event Action<int>? MenuSelected;

    public bool Create(string tooltip)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (_hwnd != IntPtr.Zero) return _added;

        Tooltip = string.IsNullOrWhiteSpace(tooltip) ? "RADHA SHELL" : tooltip.Trim();

        try
        {
            lock (StaticGate)
            {
                _wndProc ??= WindowProc;

                var wc = new WndClassEx
                {
                    cbSize = Marshal.SizeOf<WndClassEx>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = GetModuleHandle(null),
                    lpszClassName = _className
                };

                // Re-registering the same class returns 0 with ERROR_CLASS_ALREADY_EXISTS; harmless.
                RegisterClassEx(ref wc);

                _hwnd = CreateWindowEx(0, _className, "RadhaTray", WsOverlapped,
                    0, 0, 0, 0, HwndMessage, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

                if (_hwnd == IntPtr.Zero) return false;
                Windows[_hwnd] = this;
            }

            _icon = LoadIcon(IntPtr.Zero, (IntPtr)IdiApplication);

            var data = NewData();
            _added = ShellNotifyIcon(NimAdd, ref data);

            // Version 4 enables the modern balloon behaviour and low-DPI-safe icon handling.
            var version = NewData();
            version.uVersion = NotifyIconVersion4;
            ShellNotifyIcon(NimSetVersion, ref version);

            return _added;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public void SetTooltip(string tooltip)
    {
        if (_hwnd == IntPtr.Zero || !_added) return;
        Tooltip = tooltip ?? string.Empty;
        var data = NewData();
        ShellNotifyIcon(0x00000001 /* NIM_MODIFY */, ref data);
    }

    private NotifyIconData NewData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NifMessage | NifIcon | NifTip,
        uCallbackMessage = WmTray,
        hIcon = _icon,
        szTip = Truncate(Tooltip, 127)
    };

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmTray:
                switch ((int)lParam)
                {
                    case WmLButtonUp:
                        Activated?.Invoke();
                        return IntPtr.Zero;
                    case WmLButtonDblClk:
                        DoubleClicked?.Invoke();
                        return IntPtr.Zero;
                    case WmRButtonUp:
                        ContextRequested?.Invoke();
                        ShowMenu();
                        return IntPtr.Zero;
                }
                break;

            case WmCommand:
                var id = (int)(wParam.ToInt64() & 0xFFFF);
                if (id > 0) MenuSelected?.Invoke(id);
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = MenuBuilder?.Invoke() ?? Array.Empty<(int Id, string? Label)>();
        if (items.Count == 0) return;

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;

        try
        {
            foreach (var (id, label) in items)
            {
                if (label is null)
                    AppendMenu(menu, MfSeparator, 0, null);
                else
                    AppendMenu(menu, MfString, (UIntPtr)id, label);
            }

            // Required before TrackPopupMenu, or the menu does not dismiss on an outside click.
            SetForegroundWindow(_hwnd);

            var chosen = TrackPopupMenuEx(menu, TpmReturnCmd | TpmRightButton,
                CursorX(), CursorY(), _hwnd, IntPtr.Zero);

            if (chosen > 0) MenuSelected?.Invoke(chosen);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            if (_added)
            {
                var data = NewData();
                ShellNotifyIcon(NimDelete, ref data);
                _added = false;
            }

            lock (StaticGate) Windows.Remove(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    // ---------------- interop ----------------

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private static readonly IntPtr HwndMessage = new(-3);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetCursorPos(out Point point);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    private static bool ShellNotifyIcon(uint message, ref NotifyIconData data) => Shell_NotifyIcon(message, ref data);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? item);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr paramsPtr);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    private static int CursorX()
    {
        GetCursorPos(out var point);
        return point.X;
    }

    private static int CursorY()
    {
        GetCursorPos(out var point);
        return point.Y;
    }
}
