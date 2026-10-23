using System.Runtime.InteropServices;
using Radha.Core.Services;

namespace Radha.App.Services.Win;

/// <summary>
/// Per-monitor desktop wallpapers through <c>IDesktopWallpaper</c>.
///
/// <c>SystemParametersInfo(SPI_SETDESKWALLPAPER)</c> can only paint every monitor with one image —
/// it has no notion of a monitor at all. The COM interface below is the documented one that does,
/// and it is what Windows' own "Personalisation → Background" page uses when you right-click a
/// single display. It is a stable public interface (since Windows 8), unlike the virtual-desktop
/// internals, so no version dispatch is needed here.
///
/// Everything is guarded: on a session without a shell (CI runs in session 0) activation fails and
/// callers transparently fall back to the global path.
/// </summary>
internal static class DesktopWallpaper
{
    private static readonly Guid ClsidDesktopWallpaper = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");

    /// <summary>MONITORINFOF_PRIMARY.</summary>
    private const uint MonitorInfoPrimary = 0x1;

    /// <summary>Every monitor Windows will accept a background for, primary first.</summary>
    public static IReadOnlyList<DisplayTarget> Monitors()
    {
        var wallpaper = TryCreate();
        if (wallpaper is null) return Array.Empty<DisplayTarget>();

        try
        {
            wallpaper.GetMonitorDevicePathCount(out var count);
            var result = new List<DisplayTarget>((int)Math.Min(count, 16));
            for (uint i = 0; i < count && i < 16; i++)
            {
                try
                {
                    wallpaper.GetMonitorDevicePathAt(i, out var id);
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    var primary = IsPrimary(wallpaper, id);
                    result.Add(new DisplayTarget(id, FriendlyName(id, i), primary));
                }
                catch { /* one bad entry must not hide the others */ }
            }

            // Primary first, then declaration order — a stable list for the picker.
            result.Sort((a, b) => a.IsPrimary == b.IsPrimary ? 0 : a.IsPrimary ? -1 : 1);
            return result;
        }
        catch
        {
            return Array.Empty<DisplayTarget>();
        }
        finally
        {
            Release(wallpaper);
        }
    }

    /// <summary>Paints one monitor. Returns false when the call was rejected.</summary>
    public static bool SetForMonitor(string monitorId, string path)
    {
        var wallpaper = TryCreate();
        if (wallpaper is null) return false;
        try
        {
            wallpaper.SetWallpaper(monitorId, path);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(wallpaper);
        }
    }

    /// <summary>Paints every monitor (<c>null</c> monitor id means "all").</summary>
    public static bool SetAll(string path)
    {
        var wallpaper = TryCreate();
        if (wallpaper is null) return false;
        try
        {
            wallpaper.SetWallpaper(null, path);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(wallpaper);
        }
    }

    public static string? GetForMonitor(string monitorId)
    {
        var wallpaper = TryCreate();
        if (wallpaper is null) return null;
        try
        {
            wallpaper.GetWallpaper(monitorId, out var path);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch
        {
            return null;
        }
        finally
        {
            Release(wallpaper);
        }
    }

    /// <summary>Maps our fit enum onto <c>DESKTOP_WALLPAPER_POSITION</c>.</summary>
    public static bool SetPosition(Core.Wallpaper.WallpaperFit fit)
    {
        var wallpaper = TryCreate();
        if (wallpaper is null) return false;
        try
        {
            wallpaper.SetPosition(fit switch
            {
                Core.Wallpaper.WallpaperFit.Fit => DesktopWallpaperPosition.Fit,
                Core.Wallpaper.WallpaperFit.Stretch => DesktopWallpaperPosition.Stretch,
                Core.Wallpaper.WallpaperFit.Center => DesktopWallpaperPosition.Center,
                Core.Wallpaper.WallpaperFit.Span => DesktopWallpaperPosition.Span,
                _ => DesktopWallpaperPosition.Fill
            });
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(wallpaper);
        }
    }

    public static Core.Wallpaper.WallpaperFit GetPosition()
    {
        var wallpaper = TryCreate();
        if (wallpaper is null) return Core.Wallpaper.WallpaperFit.Fill;
        try
        {
            return wallpaper.GetPosition() switch
            {
                DesktopWallpaperPosition.Fit => Core.Wallpaper.WallpaperFit.Fit,
                DesktopWallpaperPosition.Stretch => Core.Wallpaper.WallpaperFit.Stretch,
                DesktopWallpaperPosition.Center => Core.Wallpaper.WallpaperFit.Center,
                DesktopWallpaperPosition.Span => Core.Wallpaper.WallpaperFit.Span,
                _ => Core.Wallpaper.WallpaperFit.Fill
            };
        }
        catch
        {
            return Core.Wallpaper.WallpaperFit.Fill;
        }
        finally
        {
            Release(wallpaper);
        }
    }

    private static IDesktopWallpaper? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return (IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidDesktopWallpaper, throwOnError: false))!;
        }
        catch
        {
            // No shell, no COM activation (session 0), or the class is blocked by policy.
            return null;
        }
    }

    private static void Release(IDesktopWallpaper wallpaper)
    {
        if (Marshal.IsComObject(wallpaper)) Marshal.ReleaseComObject(wallpaper);
    }

    private static bool IsPrimary(IDesktopWallpaper wallpaper, string id)
    {
        try
        {
            wallpaper.GetMonitorRECT(id, out var rect);
            var handle = MonitorFromRect(ref rect, 0);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(handle, ref info)) return false;
            return (info.Flags & MonitorInfoPrimary) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static string FriendlyName(string devicePath, uint index)
    {
        // "\\?\DISPLAY#DELA04A5#5&2f1f8e3&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"
        // The vendor/model token is the only part a person recognises; keep it, and fall back to
        // the ordinal so two identical monitors are still distinguishable.
        var parts = devicePath.Split('#', StringSplitOptions.RemoveEmptyEntries);
        var name = parts.Length > 1 ? parts[1].Replace('_', ' ') : $"Display {index + 1}";
        return parts.Length > 2 ? $"{name} ({parts[2]})" : name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private enum DesktopWallpaperPosition
    {
        Center = 0,
        Tile = 1,
        Stretch = 2,
        Fit = 3,
        Fill = 4,
        Span = 5
    }

    /// <summary>
    /// <c>IDesktopWallpaper</c> (shobjidl_core.h).
    ///
    /// The order of these members *is* the vtable — nothing may be reordered or omitted from the
    /// front. Only the members we call are typed precisely; the rest are declared at their exact
    /// slot positions so the later calls do not run off into the wrong function.
    /// </summary>
    [ComImport]
    [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);

        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);

        void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);

        void GetMonitorDevicePathCount(out uint count);

        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, out Rect displayRect);

        void SetBackgroundColor(uint color);

        uint GetBackgroundColor();

        void SetPosition(DesktopWallpaperPosition position);

        DesktopWallpaperPosition GetPosition();
    }
}
