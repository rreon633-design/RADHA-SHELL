using System.Runtime.InteropServices;

namespace Radha.App.Interop;

/// <summary>
/// Attaches a window behind the desktop icons, the way live-wallpaper tools do.
///
/// A normal full-screen window would sit *above* the icons and swallow every click on the desktop.
/// The correct place to draw is the <c>WorkerW</c> window that Explorer creates behind
/// <c>SHELLDLL_DefView</c>. We ask Progman to spawn it with the long-standing 0x052C message, find it,
/// and reparent ourselves into it.
///
/// This is genuinely undocumented behaviour, so every step is checked and the caller is told when it
/// did not work. A failure means "do not show the layer", never "show it in the wrong place".
/// </summary>
internal static class DesktopWallpaperLayer
{
    private const uint WM_SPAWN_WORKER = 0x052C;
    private const uint SMTO_NORMAL = 0x0000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    /// <summary>
    /// Parents <paramref name="hwnd"/> behind the desktop icons.
    /// Returns false when Explorer is not running or the layer could not be attached.
    /// </summary>
    public static bool Attach(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindows()) return false;

        try
        {
            var progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero) return false;

            // Ask Progman to create (or reuse) the WorkerW that hosts the wallpaper. Older builds
            // spawn it only on request and the return value tells us nothing useful, hence the
            // search below rather than a call-and-check.
            SendMessageTimeout(progman, WM_SPAWN_WORKER, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

            var host = FindWallpaperWorker() ?? progman;
            return SetParent(hwnd, host) != IntPtr.Zero && GetParent(hwnd) == host;
        }
        catch
        {
            return false;
        }
    }

    public static void Detach(IntPtr hwnd)
    {
        try
        {
            if (hwnd != IntPtr.Zero && GetParent(hwnd) != IntPtr.Zero) SetParent(hwnd, IntPtr.Zero);
        }
        catch { }
    }

    /// <summary>
    /// The WorkerW that follows the window containing SHELLDLL_DefView — that sibling, not the one
    /// before it, is the wallpaper host on every build since Windows 10.
    /// </summary>
    private static IntPtr? FindWallpaperWorker()
    {
        IntPtr found = IntPtr.Zero;
        var stop = false;

        EnumWindows((hwnd, _) =>
        {
            if (stop) return false;

            if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                var worker = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
                if (worker != IntPtr.Zero)
                {
                    found = worker;
                    stop = true;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        return found == IntPtr.Zero ? null : found;
    }
}
