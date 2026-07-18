using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using SystemCleaner.Models;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Моніторинг системи в реальному часі.
    /// Оновлює SystemStats щосекунди через DispatcherTimer.
    /// </summary>
    public class SystemMonitor : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Computer _hwMonitor;
        
        // Performance counters
        private PerformanceCounter? _cpuCounter;
        private PerformanceCounter? _diskReadCounter;
        private PerformanceCounter? _diskWriteCounter;
        private PerformanceCounter? _netDownCounter;
        private PerformanceCounter? _netUpCounter;
        
        private long _lastNetDown, _lastNetUp;
        private DateTime _lastNetTime;
        private bool _initialized;

        public SystemStats CurrentStats { get; } = new();
        public event Action<SystemStats>? Updated;

        public SystemMonitor()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Update();
            
            _hwMonitor = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true
            };
        }

        public void Start()
        {
            if (_initialized) return;
            InitializeCounters();
            InitializeHardwareInfo();
            _timer.Start();
            _initialized = true;
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void InitializeCounters()
        {
            try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); } catch { }
            try { _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total"); } catch { }
            try { _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total"); } catch { }
            
            // Мережа — беремо перший активний інтерфейс
            try
            {
                var cat = new PerformanceCounterCategory("Network Interface");
                var instance = cat.GetInstanceNames().FirstOrDefault(n => 
                    !n.Contains("Loopback") && !n.Contains("isatap"));
                if (instance != null)
                {
                    _netDownCounter = new PerformanceCounter("Network Interface", "Bytes Received/sec", instance);
                    _netUpCounter = new PerformanceCounter("Network Interface", "Bytes Sent/sec", instance);
                }
            }
            catch { }
        }

        private void InitializeHardwareInfo()
        {
            try
            {
                _hwMonitor.Open();
                
                // CPU info
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (var obj in searcher.Get())
                {
                    CurrentStats.CpuName = obj["Name"]?.ToString()?.Trim() ?? "—";
                    CurrentStats.CpuCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    break;
                }

                // GPU info
                using var gpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                foreach (var obj in gpuSearcher.Get())
                {
                    CurrentStats.GpuName = obj["Name"]?.ToString()?.Trim() ?? "—";
                    break;
                }

                // OS info
                using var osSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
                foreach (var obj in osSearcher.Get())
                {
                    CurrentStats.OsName = obj["Caption"]?.ToString() ?? "—";
                    CurrentStats.RamTotal = Convert.ToInt64(obj["TotalVisibleMemorySize"] ?? 0) * 1024;
                    break;
                }
            }
            catch { }
        }

        private void Update()
        {
            try
            {
                // CPU usage
                if (_cpuCounter != null)
                {
                    try { CurrentStats.CpuUsage = _cpuCounter.NextValue(); } catch { }
                }

                // RAM
                try
                {
                    var memStatus = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(memStatus))
                    {
                        CurrentStats.RamTotal = (long)memStatus.ullTotalPhys;
                        CurrentStats.RamUsed = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys);
                    }
                }
                catch { }

                // Диски
                try { CurrentStats.DiskReadMBps = _diskReadCounter?.NextValue() / (1024f * 1024f) ?? 0; } catch { }
                try { CurrentStats.DiskWriteMBps = _diskWriteCounter?.NextValue() / (1024f * 1024f) ?? 0; } catch { }

                // Мережа
                try
                {
                    if (_netDownCounter != null && _netUpCounter != null)
                    {
                        var down = (long)_netDownCounter.NextValue();
                        var up = (long)_netUpCounter.NextValue();
                        var now = DateTime.UtcNow;
                        if (_lastNetTime != default)
                        {
                            var dt = (now - _lastNetTime).TotalSeconds;
                            if (dt > 0)
                            {
                                CurrentStats.NetworkDownMBps = (float)Math.Max(0, (down - _lastNetDown) / dt / (1024 * 1024));
                                CurrentStats.NetworkUpMBps = (float)Math.Max(0, (up - _lastNetUp) / dt / (1024 * 1024));
                            }
                        }
                        _lastNetDown = down;
                        _lastNetUp = up;
                        _lastNetTime = now;
                    }
                }
                catch { }

                // Uptime
                try
                {
                    CurrentStats.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                }
                catch { }

                // Батарея
                try
                {
                    var ps = System.Windows.SystemParameters.PowerLineStatus;
                    var batt = GetBatteryStatus();
                    CurrentStats.BatteryPercent = batt;
                }
                catch { CurrentStats.BatteryPercent = -1; }

                // Температури (LibreHardwareMonitor)
                try
                {
                    CurrentStats.CpuTemp = GetTemperature("CPU");
                    CurrentStats.GpuTemp = GetTemperature("GPU");
                }
                catch { }

                Updated?.Invoke(CurrentStats);
            }
            catch { }
        }

        private float GetTemperature(string type)
        {
            try
            {
                foreach (var hw in _hwMonitor.Hardware)
                {
                    hw.Update();
                    var name = hw.Name?.ToUpperInvariant() ?? "";
                    if (!name.Contains(type.ToUpperInvariant())) continue;

                    foreach (var sensor in hw.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                            return sensor.Value.Value;
                    }
                }
            }
            catch { }
            return 0;
        }

        private float GetBatteryStatus()
        {
            try
            {
                var ps = new PowerStatus();
                return ps.BatteryLifePercent * 100;
            }
            catch { return -1; }
        }

        // P/Invoke для RAM
        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
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
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private bool GlobalMemoryStatusEx(MEMORYSTATUSEX status)
        {
            status.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            return GlobalMemoryStatusEx(ref status);
        }

        public void Dispose()
        {
            _timer.Stop();
            _cpuCounter?.Dispose();
            _diskReadCounter?.Dispose();
            _diskWriteCounter?.Dispose();
            _netDownCounter?.Dispose();
            _netUpCounter?.Dispose();
            try { _hwMonitor.Close(); } catch { }
        }

        // Допоміжний клас для батареї
        private class PowerStatus
        {
            public float BatteryLifePercent
            {
                get
                {
                    try
                    {
                        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                        foreach (var obj in searcher.Get())
                        {
                            return Convert.ToSingle(obj["EstimatedChargeRemaining"] ?? 0) / 100f;
                        }
                    }
                    catch { }
                    return -1;
                }
            }
        }
    }
}