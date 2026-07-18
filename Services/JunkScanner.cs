using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices; 
using System.Security.Principal;
using SystemCleaner.Models;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Сканує систему на наявність сміттєвих файлів по всім категоріям.
    /// Асинхронний, багатопотоковий.
    /// </summary>
    public class JunkScanner
    {
        private readonly string _userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private readonly string _windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        private readonly string _programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        private readonly string _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private readonly string _appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        private readonly string _temp = Path.GetTempPath();

        /// <summary>
        /// Скільки файлів в категорії показувати максимум (для продуктивності UI).
        /// </summary>
        private const int MaxItemsPerCategory = 5000;

        public async Task<List<ScanCategoryResult>> ScanAsync(Action<int, string>? progress = null, CancellationToken ct = default)
        {
            var categories = new List<ScanCategoryResult>();

            // Сканування виконується паралельно по категоріям
            var tasks = new List<Task<ScanCategoryResult>>
            {
                Task.Run(() => ScanWindowsTemp(), ct),
                Task.Run(() => ScanUserTemp(), ct),
                Task.Run(() => ScanPrefetch(), ct),
                Task.Run(() => ScanWindowsUpdateCache(), ct),
                Task.Run(() => ScanDeliveryOptimization(), ct),
                Task.Run(() => ScanWindowsErrorReporting(), ct),
                Task.Run(() => ScanCrashDumps(), ct),
                Task.Run(() => ScanDirectXShaderCache(), ct),
                Task.Run(() => ScanNvidiaCache(), ct),
                Task.Run(() => ScanAmdCache(), ct),
                Task.Run(() => ScanIntelCache(), ct),
                Task.Run(() => ScanBrowserCaches(), ct),
                Task.Run(() => ScanThumbnailCache(), ct),
                Task.Run(() => ScanRecentFiles(), ct),
                Task.Run(() => ScanRecycleBinInfo(), ct),
            };

            var results = await Task.WhenAll(tasks);
            foreach (var r in results) categories.Add(r);

            return categories;
        }

        private ScanCategoryResult ScanWindowsTemp()
        {
            var result = new ScanCategoryResult { Name = "Windows Temp" };
            AddFilesFromDirectory(result, Path.Combine(_windowsDir, "Temp"));
            return result;
        }

        private ScanCategoryResult ScanUserTemp()
        {
            var result = new ScanCategoryResult { Name = "Користувацький Temp" };
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                _temp,
                Environment.GetEnvironmentVariable("TEMP") ?? "",
                Environment.GetEnvironmentVariable("TMP") ?? "",
                Path.Combine(_localAppData, "Temp"),
                Path.Combine(_userProfile, "AppData", "Local", "Temp")
            };
            foreach (var p in paths.Where(x => !string.IsNullOrWhiteSpace(x)))
                AddFilesFromDirectory(result, p);
            return result;
        }

        private ScanCategoryResult ScanPrefetch()
        {
            var result = new ScanCategoryResult { Name = "Prefetch" };
            AddFilesFromDirectory(result, Path.Combine(_windowsDir, "Prefetch"));
            return result;
        }

        private ScanCategoryResult ScanWindowsUpdateCache()
        {
            var result = new ScanCategoryResult { Name = "Кеш Windows Update" };
            AddFilesFromDirectory(result, Path.Combine(_windowsDir, "SoftwareDistribution", "Download"));
            return result;
        }

        private ScanCategoryResult ScanDeliveryOptimization()
        {
            var result = new ScanCategoryResult { Name = "Delivery Optimization" };
            AddFilesFromDirectory(result, Path.Combine(_programData, "Microsoft", "Windows", "DeliveryOptimization", "Cache"));
            return result;
        }

        private ScanCategoryResult ScanWindowsErrorReporting()
        {
            var result = new ScanCategoryResult { Name = "Звіти про помилки Windows (WER)" };
            AddFilesFromDirectory(result, Path.Combine(_programData, "Microsoft", "Windows", "WER"));
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "Microsoft", "Windows", "WER"));
            return result;
        }

        private ScanCategoryResult ScanCrashDumps()
        {
            var result = new ScanCategoryResult { Name = "Crash Dumps" };
            AddFilesFromDirectory(result, Path.Combine(_windowsDir, "Minidump"));
            AddFileIfExists(result, Path.Combine(_windowsDir, "MEMORY.DMP"));
            return result;
        }

        private ScanCategoryResult ScanDirectXShaderCache()
        {
            var result = new ScanCategoryResult { Name = "DirectX Shader Cache" };
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "D3DSCache"));
            return result;
        }

        private ScanCategoryResult ScanNvidiaCache()
        {
            var result = new ScanCategoryResult { Name = "NVIDIA Cache" };
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "NVIDIA", "DXCache"));
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "NVIDIA", "GLCache"));
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "NVIDIA Corporation", "NV_Cache"));
            return result;
        }

        private ScanCategoryResult ScanAmdCache()
        {
            var result = new ScanCategoryResult { Name = "AMD Cache" };
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "AMD", "DxCache"));
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "AMD", "GLCache"));
            return result;
        }

        private ScanCategoryResult ScanIntelCache()
        {
            var result = new ScanCategoryResult { Name = "Intel Cache" };
            AddFilesFromDirectory(result, Path.Combine(_localAppData, "Intel", "ShaderCache"));
            return result;
        }

        private ScanCategoryResult ScanBrowserCaches()
        {
            var result = new ScanCategoryResult { Name = "Кеші браузерів" };

            // Chrome
            var chromeDefault = Path.Combine(_localAppData, "Google", "Chrome", "User Data", "Default");
            AddFilesFromDirectory(result, Path.Combine(chromeDefault, "Cache"));
            AddFilesFromDirectory(result, Path.Combine(chromeDefault, "Code Cache"));
            AddFilesFromDirectory(result, Path.Combine(chromeDefault, "GPUCache"));

            // Edge
            var edgeDefault = Path.Combine(_localAppData, "Microsoft", "Edge", "User Data", "Default");
            AddFilesFromDirectory(result, Path.Combine(edgeDefault, "Cache"));
            AddFilesFromDirectory(result, Path.Combine(edgeDefault, "Code Cache"));
            AddFilesFromDirectory(result, Path.Combine(edgeDefault, "GPUCache"));

            // Brave
            var braveDefault = Path.Combine(_localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default");
            AddFilesFromDirectory(result, Path.Combine(braveDefault, "Cache"));
            AddFilesFromDirectory(result, Path.Combine(braveDefault, "Code Cache"));
            AddFilesFromDirectory(result, Path.Combine(braveDefault, "GPUCache"));

            // Firefox - всі профілі
            var firefoxProfiles = Path.Combine(_appData, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(firefoxProfiles))
            {
                foreach (var profile in Directory.GetDirectories(firefoxProfiles))
                {
                    AddFilesFromDirectory(result, Path.Combine(profile, "cache2"));
                }
            }

            // Opera GX
            var operaGx = Path.Combine(_appData, "Opera Software", "Opera GX Stable");
            AddFilesFromDirectory(result, Path.Combine(operaGx, "Cache"));
            AddFilesFromDirectory(result, Path.Combine(operaGx, "Code Cache"));
            AddFilesFromDirectory(result, Path.Combine(operaGx, "GPUCache"));

            return result;
        }

        private ScanCategoryResult ScanThumbnailCache()
        {
            var result = new ScanCategoryResult { Name = "Кеш мініатюр" };
            var explorerDir = Path.Combine(_localAppData, "Microsoft", "Windows", "Explorer");
            if (Directory.Exists(explorerDir))
            {
                foreach (var file in SafeEnumerateFiles(explorerDir, "thumbcache_*"))
                {
                    AddFileIfExists(result, file);
                }
            }
            return result;
        }

        private ScanCategoryResult ScanRecentFiles()
        {
            var result = new ScanCategoryResult { Name = "Недавні файли" };
            AddFilesFromDirectory(result, Path.Combine(_appData, "Microsoft", "Windows", "Recent"));
            return result;
        }

        private ScanCategoryResult ScanRecycleBinInfo()
        {
            var result = new ScanCategoryResult { Name = "Кошик" };
            try
            {
                var info = new NativeMethods.SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.SHQUERYRBINFO));
                if (NativeMethods.SHQueryRecycleBin(null, ref info) == 0)
                {
                    result.Items.Add(new CleanItem
                    {
                        Path = "[Кошик]",
                        Category = "Кошик",
                        Size = info.i64Size,
                        IsDirectory = true,
                        Selected = true
                    });
                }
            }
            catch { /* ігноруємо */ }
            return result;
        }

        // ====== Хелпери сканування ======

        private void AddFilesFromDirectory(ScanCategoryResult result, string directory, bool includeDirs = true)
        {
            if (!Directory.Exists(directory)) return;
            try
            {
                if (includeDirs)
                {
                    foreach (var dir in SafeEnumerateDirectories(directory))
                    {
                        if (result.Items.Count >= MaxItemsPerCategory) break;
                        try
                        {
                            long size = GetDirectorySize(dir);
                            result.Items.Add(new CleanItem
                            {
                                Path = dir,
                                Category = result.Name,
                                Size = size,
                                IsDirectory = true,
                                Selected = true
                            });
                        }
                        catch { }
                    }
                }

                foreach (var file in SafeEnumerateFiles(directory))
                {
                    if (result.Items.Count >= MaxItemsPerCategory) break;
                    try
                    {
                        var fi = new FileInfo(file);
                        result.Items.Add(new CleanItem
                        {
                            Path = file,
                            Category = result.Name,
                            Size = fi.Length,
                            IsDirectory = false,
                            Selected = true
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void AddFileIfExists(ScanCategoryResult result, string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                var fi = new FileInfo(filePath);
                result.Items.Add(new CleanItem
                {
                    Path = filePath,
                    Category = result.Name,
                    Size = fi.Length,
                    IsDirectory = false,
                    Selected = true
                });
            }
            catch { }
        }

        private IEnumerable<string> SafeEnumerateFiles(string dir, string pattern = "*")
        {
            IEnumerable<string> result = Array.Empty<string>();
            try
            {
                result = Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            return result;
        }

        private IEnumerable<string> SafeEnumerateDirectories(string dir)
        {
            IEnumerable<string> result = Array.Empty<string>();
            try
            {
                result = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            return result;
        }

        private long GetDirectorySize(string path)
        {
            long total = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch { }
                }
            }
            catch { }
            return total;
        }
    }
}