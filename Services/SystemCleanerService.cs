using System.Diagnostics;
using System.IO; 
using Microsoft.Win32;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Очищує журнали Windows, DNS-кеш, історію запуску, буфер обміну тощо.
    /// </summary>
    public class SystemCleanerService
    {
        /// <summary>Очистити Event Logs через wevtutil.</summary>
        public async Task ClearEventLogsAsync(CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c for /F \"tokens=*\" %1 in ('wevtutil.exe el') do wevtutil.exe cl \"%1\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync(ct);
            }
            catch { }
        }

        /// <summary>Очистити DNS-кеш через ipconfig.</summary>
        public async Task FlushDnsAsync(CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ipconfig",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                if (proc != null) await proc.WaitForExitAsync(ct);
            }
            catch { }
        }

        /// <summary>Очистити історію запуску (Run MRU).</summary>
        public void ClearRunHistory()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU", true);
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        if (!name.Equals("MRUList", StringComparison.OrdinalIgnoreCase))
                            key.DeleteValue(name, false);
                    }
                }
            }
            catch { }
        }

        /// <summary>Очистити історію буфера обміну Windows 10/11.</summary>
        public void ClearClipboardHistory()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c echo off | clip",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);
            }
            catch { }

            // Також очищуємо кеш Windows Clipboard
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var clipPath = Path.Combine(localAppData, "Microsoft", "Clipboard");
                if (Directory.Exists(clipPath))
                {
                    foreach (var f in Directory.GetFiles(clipPath))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>Очистити Windows Search Cache.</summary>
        public void ClearSearchCache()
        {
            try
            {
                var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var searchDir = Path.Combine(programData, "Microsoft", "Search", "Data");
                if (Directory.Exists(searchDir))
                {
                    foreach (var dir in Directory.GetDirectories(searchDir))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}