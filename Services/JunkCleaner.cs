using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using SystemCleaner.Models;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Видаляє знайдені сміттєві файли з урахуванням безпеки.
    /// </summary>
    public class JunkCleaner
    {
        /// <summary>
        /// Захисний список шляхів, які не можна видаляти ніколи.
        /// </summary>
        private static readonly HashSet<string> ProtectedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };

        private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".sys", ".msi", ".msu", ".cab", ".drv"
        };

        /// <summary>
        /// Очищає всі вибрані елементи у списку категорій.
        /// </summary>
        public async Task<int> CleanAsync(
            List<ScanCategoryResult> categories,
            Action<int, int, string>? progress = null,
            CancellationToken ct = default)
        {
            var items = categories
                .Where(c => c.Selected)
                .SelectMany(c => c.Items.Where(i => i.Selected))
                .ToList();

            if (items.Count == 0) return 0;

            int cleaned = 0;
            int total = items.Count;

            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[i];

                try
                {
                    if (IsProtected(item.Path))
                        continue;

                    // Для кошика - спеціальна процедура
                    if (item.Category == "Кошик")
                    {
                        EmptyRecycleBin();
                        cleaned++;
                        progress?.Invoke(i + 1, total, item.Path);
                        continue;
                    }

                    if (item.IsDirectory)
                    {
                        if (Directory.Exists(item.Path))
                        {
                            Directory.Delete(item.Path, recursive: true);
                            cleaned++;
                        }
                    }
                    else
                    {
                        if (File.Exists(item.Path))
                        {
                            File.SetAttributes(item.Path, FileAttributes.Normal);
                            File.Delete(item.Path);
                            cleaned++;
                        }
                    }
                }
                catch
                {
                    // Ігноруємо помилки доступу - файл може бути зайнятий
                }

                progress?.Invoke(i + 1, total, item.Path);
                await Task.Yield();
            }

            return cleaned;
        }

        /// <summary>
        /// Очистити тільки кошик Windows.
        /// </summary>
        public void EmptyRecycleBin()
        {
            try
            {
                const uint flags = NativeMethods.SHERB_NOCONFIRMATION
                                 | NativeMethods.SHERB_NOPROGRESSUI
                                 | NativeMethods.SHERB_NOSOUND;
                NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, flags);
            }
            catch { }
        }

        /// <summary>
        /// Перевірка чи шлях не входить до захищених системних/особистих директорій.
        /// </summary>
        private bool IsProtected(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            // Кошик — не захищений
            if (path == "[Кошик]") return false;

            try
            {
                var fullPath = Path.GetFullPath(path);
                foreach (var protectedPath in ProtectedFolders)
                {
                    if (string.IsNullOrEmpty(protectedPath)) continue;
                    var pFull = Path.GetFullPath(protectedPath);

                    // Шлях співпадає з самим системним каталогом (окрім підпапок Temp)
                    if (fullPath.Equals(pFull, StringComparison.OrdinalIgnoreCase))
                        return true;

                    // Якщо це всередині захищеної папки, перевіряємо, чи це дозволена підпапка
                    if (fullPath.StartsWith(pFull, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsAllowedInsideProtected(fullPath))
                            return true;
                    }
                }

                // Захист від видалення .exe/.dll, якщо це не в тимчасовій папці
                var ext = Path.GetExtension(fullPath);
                if (ProtectedExtensions.Contains(ext))
                {
                    if (!IsInsideTemp(fullPath)) return true;
                }
            }
            catch { return true; }

            return false;
        }

        /// <summary>
        /// Дозволені підпапки всередині Windows, які безпечно чистити.
        /// </summary>
        private bool IsAllowedInsideProtected(string fullPath)
        {
            var allowed = new[]
            {
                "Temp", "Prefetch", "SoftwareDistribution\\Download",
                "Minidump", "WER", "Explorer"
            };
            var lower = fullPath.ToLowerInvariant();
            return allowed.Any(a => lower.Contains(a.ToLowerInvariant()));
        }

        private bool IsInsideTemp(string fullPath)
        {
            var lower = fullPath.ToLowerInvariant();
            return lower.Contains("\\temp\\") || lower.Contains("\\tmp\\")
                || lower.Contains("\\cache\\") || lower.Contains("\\d3dscache")
                || lower.Contains("\\dx cache") || lower.Contains("\\glcache");
        }
    }
}