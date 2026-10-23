using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using Radha.Core.Services;
using Windows.Devices.Power;

namespace Radha.App.Services.Win;

/// <summary>CPU/GPU/RAM/storage/network/battery telemetry: LibreHardwareMonitor + Win32 + WinRT.</summary>
public sealed class SystemMonitorService : ISystemMonitorService, IDisposable
{
    private readonly Computer _computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true,
        IsMemoryEnabled = true,
        IsStorageEnabled = true
    };

    private readonly object _gate = new();
    private Timer? _timer;
    private long _lastBytes;
    private DateTime _lastSample = DateTime.UtcNow;
    private readonly TimeSpan _bootTime = DateTime.UtcNow.AddMilliseconds(-Environment.TickCount64);

    public SystemSample? Latest { get; private set; }
    public event EventHandler<SystemSample>? Sampled;

    public void Start(TimeSpan interval)
    {
        if (_timer is not null) return;
        try { _computer.Open(); } catch { /* sensors unavailable: still report RAM/disk/net */ }
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Sample()
    {
        double cpu = 0, gpu = 0;
        double? cpuTemp = null, gpuTemp = null;

        lock (_gate)
        {
            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    foreach (var sub in hardware.SubHardware) sub.Update();

                    foreach (var sensor in hardware.Sensors.Concat(hardware.SubHardware.SelectMany(s => s.Sensors)))
                    {
                        switch (sensor.SensorType)
                        {
                            case SensorType.Load when sensor.Name.Contains("CPU Total", StringComparison.OrdinalIgnoreCase):
                                cpu = sensor.Value ?? cpu;
                                break;
                            case SensorType.Load when hardware.HardwareType == HardwareType.GpuNvidia ||
                                                       hardware.HardwareType == HardwareType.GpuAmd ||
                                                       hardware.HardwareType == HardwareType.GpuIntel:
                                if (sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                    sensor.Name.Contains("D3D", StringComparison.OrdinalIgnoreCase))
                                    gpu = Math.Max(gpu, sensor.Value ?? 0);
                                break;
                            case SensorType.Temperature when sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase):
                                cpuTemp = sensor.Value;
                                break;
                            case SensorType.Temperature when hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd:
                                gpuTemp = sensor.Value ?? gpuTemp;
                                break;
                        }
                    }
                }
            }
            catch { /* a driver can disappear mid-enumeration */ }
        }

        var memory = ReadMemory();
        var storage = ReadStorage();
        var (downKbps, upKbps) = ReadNetwork();
        var (batteryPercent, charging) = ReadBattery();

        var sample = new SystemSample(
            Math.Clamp(cpu, 0, 100),
            Math.Clamp(gpu, 0, 100),
            memory.UsedGb,
            memory.TotalGb,
            storage,
            downKbps,
            upKbps,
            cpuTemp,
            gpuTemp,
            batteryPercent,
            charging,
            DateTime.UtcNow - _bootTime,
            DateTimeOffset.Now);

        Latest = sample;
        Ui.Post(() => Sampled?.Invoke(this, sample));
    }

    private static (double UsedGb, double TotalGb) ReadMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return (0, 0);
        var total = status.ullTotalPhys / 1024.0 / 1024 / 1024;
        var available = status.ullAvailPhys / 1024.0 / 1024 / 1024;
        return (total - available, total);
    }

    private static double ReadStorage()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return drive.TotalSize == 0 ? 0 : (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100;
        }
        catch { return 0; }
    }

    private (double DownKbps, double UpKbps) ReadNetwork()
    {
        try
        {
            long total = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPStatistics();
                total += stats.BytesReceived + stats.BytesSent;
            }

            var now = DateTime.UtcNow;
            var seconds = Math.Max(0.2, (now - _lastSample).TotalSeconds);
            var deltaBytes = Math.Max(0, total - _lastBytes);
            _lastBytes = total;
            _lastSample = now;

            // Split evenly: the bar shows total throughput; per-direction needs ETW.
            var kbps = deltaBytes * 8.0 / 1000.0 / seconds;
            return (kbps / 2, kbps / 2);
        }
        catch { return (0, 0); }
    }

    private static (int? Percent, bool Charging) ReadBattery()
    {
        try
        {
            var report = Battery.AggregateBattery.GetReport();
            if (report.FullChargeCapacityInMilliwattHours is not { } full || full == 0) return (null, false);
            var remaining = report.RemainingCapacityInMilliwattHours ?? 0;
            var percent = (int)Math.Clamp(remaining * 100.0 / full, 0, 100);
            return (percent, report.Status is BatteryStatus.Charging or BatteryStatus.Idle);
        }
        catch { return (null, false); }
    }

    public void Dispose()
    {
        Stop();
        try { _computer.Close(); } catch { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
            ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
}
