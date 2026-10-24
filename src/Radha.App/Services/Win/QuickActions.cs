using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Radha.App.Services.Win;

/// <summary>
/// The Windows calls behind the control-centre tiles that are not already covered by a service.
///
/// Everything here is the documented Win32 entry point for the thing it does, or a documented
/// settings page when Windows exposes no API at all. Nothing is simulated: a control that cannot
/// work on this machine says so through <see cref="Support"/>, and the tile disables itself rather
/// than pretending it toggled something.
/// </summary>
internal static class QuickActions
{
    // ---------------- brightness (Dxva2, internal displays) ----------------

    /// <summary>
    /// <c>PHYSICAL_MONITOR</c>: a handle followed by an inline <c>WCHAR[128]</c> description.
    /// MarshalAs(UnmanagedType.LPWStr) on the description declares a pointer, so the OS wrote 256
    /// bytes into an 8-byte field — heap corruption that killed the process before its first
    /// window. ByValTStr with the real size is the shape of the C struct.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count,
                                                              [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current,
                                                    out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    private const uint MonitorDefaultToPrimary = 1;

    /// <summary>
    /// Asks a child process what this machine can do with brightness.
    ///
    /// Dxva2 is a vendor DLL reached through a C ABI, and a mistake in that boundary is a native
    /// access violation — uncatchable on .NET, fatal to the shell. The same probe that verified the
    /// virtual-desktop bridge applies here: ask a process that is allowed to die. A crash costs one
    /// disabled slider instead of the whole shell.
    /// </summary>
    internal static (bool Supported, int Value) ProbeDisplay()
    {
        var reply = Interop.ChildProbe.Run("--probe-display", TimeSpan.FromSeconds(8));
        if (reply is null) return (false, -1);

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(reply);
            var root = document.RootElement;
            var supported = root.GetProperty("supported").GetBoolean();
            var value = root.GetProperty("brightness").GetInt32();
            return (supported, value);
        }
        catch
        {
            return (false, -1);
        }
    }

    /// <summary>Child side of <see cref="ProbeDisplay"/>: one JSON line, no XAML.</summary>
    internal static int RunDisplayProbe()
    {
        var supported = false;
        var value = -1;

        try
        {
            using var physical = PhysicalMonitorHandle.Open();
            if (physical is not null)
            {
                supported = GetMonitorBrightness(physical.Handle, out var min, out var current, out var max)
                            && max > min;
                if (supported) value = (int)Math.Round((current - min) * 100.0 / (max - min));
            }
        }
        catch
        {
            supported = false;
        }

        Console.Out.WriteLine($"{{\"supported\":{(supported ? "true" : "false")},\"brightness\":{value}}}");
        return 0;
    }

    /// <summary>Brightness 0–100, or -1 when nothing can report it.</summary>
    internal static int GetBrightness()
    {
        try
        {
            using var physical = PhysicalMonitorHandle.Open();
            if (physical is null) return -1;
            return GetMonitorBrightness(physical.Handle, out var min, out var current, out var max) && max > min
                ? (int)Math.Round((current - min) * 100.0 / (max - min))
                : -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>Sets brightness; false when the display refuses it (external panel, no DDC).</summary>
    internal static bool SetBrightness(int percent)
    {
        try
        {
            using var physical = PhysicalMonitorHandle.Open();
            if (physical is null) return false;
            if (!GetMonitorBrightness(physical.Handle, out var min, out _, out var max) || max <= min)
                return false;

            var target = (uint)Math.Round(min + Math.Clamp(percent, 0, 100) / 100.0 * (max - min));
            return SetMonitorBrightness(physical.Handle, target);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Owns the physical-monitor array so it cannot leak on an exception path.</summary>
    private sealed class PhysicalMonitorHandle : IDisposable
    {
        private readonly PhysicalMonitor[] _monitors;

        private PhysicalMonitorHandle(PhysicalMonitor[] monitors) => _monitors = monitors;

        /// <summary>The primary panel's handle; every call here targets one monitor.</summary>
        internal IntPtr Handle => _monitors[0].Handle;

        internal static PhysicalMonitorHandle? Open()
        {
            var hMonitor = MonitorFromWindow(IntPtr.Zero, MonitorDefaultToPrimary);
            if (hMonitor == IntPtr.Zero) return null;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0) return null;

            var monitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors)) return null;
            return new PhysicalMonitorHandle(monitors);
        }

        public void Dispose()
        {
            if (_monitors.Length > 0) DestroyPhysicalMonitors((uint)_monitors.Length, _monitors);
        }
    }

    // ---------------- night light (gamma ramp) ----------------

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr dc, [In] ushort[] ramp);

    private const int RampEntries = 256;

    /// <summary>
    /// Warm-shifts the display and drops its overall output, which is what Windows' night light
    /// does to the signal. Reversible by passing <c>false</c>, and reset automatically when the
    /// process exits through the usual path.
    /// </summary>
    internal static bool SetNightLight(bool on, double strength = 0.55)
    {
        if (!OperatingSystem.IsWindows()) return false;

        var dc = IntPtr.Zero;
        try
        {
            dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;

            var ramp = new ushort[RampEntries * 3];
            for (var i = 0; i < RampEntries; i++)
            {
                var value = (i * 65535) / (RampEntries - 1);

                // Warm: red untouched, green slightly down, blue well down. Neutral when off.
                var red = value;
                var green = on ? (int)(value * (1 - 0.18 * strength)) : value;
                var blue = on ? (int)(value * (1 - 0.55 * strength)) : value;

                ramp[i] = Clamp(red);
                ramp[RampEntries + i] = Clamp(green);
                ramp[RampEntries * 2 + i] = Clamp(blue);
            }

            return SetDeviceGammaRamp(dc, ramp);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
        }
    }

    private static ushort Clamp(int value) => (ushort)Math.Clamp(value, 0, 65535);

    // ---------------- rotation lock ----------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDisplayAutoRotationPreferences(int orientation);

    private const int OrientationLandscape = 1;

    /// <summary>
    /// Locks or unlocks automatic rotation for the session. Windows exposes this as a preference
    /// rather than a switch, so the tile reflects what was last asked for.
    /// </summary>
    internal static bool SetRotationLocked(bool locked) =>
        OperatingSystem.IsWindows() && SetDisplayAutoRotationPreferences(locked ? OrientationLandscape : 0);

    // ---------------- energy saver ----------------

    /// <summary>
    /// The Windows power-saver overlay (the same one the battery flyout toggles). Falls back to
    /// the balanced plan when switched off.
    /// </summary>
    internal static bool SetEnergySaver(bool on) => RunHidden("powercfg",
        on ? "/overlaysetactive OVERLAY_SCHEME_MIN" : "/overlaysetactive OVERLAY_SCHEME_MAX") == 0;

    // ---------------- shell surfaces Windows only exposes as a page ----------------

    /// <summary>Win+P's flyout is a program, not an API.</summary>
    internal static void OpenProject() => Start("DisplaySwitch.exe", "/invoke");

    internal static void OpenAccessibility() => Start("explorer.exe", "ms-settings:easeofaccess");

    internal static void OpenNearbySharing() => Start("explorer.exe", "ms-settings:nearbysharing");

    /// <summary>Snipping Tool, which is where screen recording lives on Windows 11.</summary>
    internal static void OpenScreenRecorder()
    {
        if (!Start("explorer.exe", "ms-screenclip:")) Start("SnippingTool.exe", "");
    }

    private static bool Start(string file, string arguments)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int RunHidden(string file, string arguments)
    {
        if (!OperatingSystem.IsWindows()) return -1;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return -1;
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                return -1;
            }

            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
