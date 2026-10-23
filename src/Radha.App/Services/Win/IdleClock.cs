using System.Runtime.InteropServices;

namespace Radha.App.Services.Win;

/// <summary>
/// Idle time and display-inhibit plumbing.
///
/// <c>GetLastInputInfo</c> is the documented way to read time-since-last-input without a hook, and
/// <c>SetThreadExecutionState</c> is the documented way to keep the display awake. Neither locks,
/// suspends or touches credentials — the policy decision lives in <c>Radha.Core.Power.IdlePolicy</c>.
/// </summary>
internal static class IdleClock
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;
    private const uint EsDisplayRequired = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint flags);

    /// <summary>Time since the last keyboard or mouse input.</summary>
    public static TimeSpan IdleFor()
    {
        if (!OperatingSystem.IsWindows()) return TimeSpan.Zero;

        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;

        var elapsed = (uint)Environment.TickCount - info.dwTime;
        return TimeSpan.FromMilliseconds(elapsed);
    }

    /// <summary>Adds or clears a continuous display+system request for the calling thread.</summary>
    public static bool SetInhibit(bool inhibit)
    {
        if (!OperatingSystem.IsWindows()) return false;

        var flags = EsContinuous | (inhibit ? EsSystemRequired | EsDisplayRequired : 0);
        return SetThreadExecutionState(flags) != 0;
    }
}
