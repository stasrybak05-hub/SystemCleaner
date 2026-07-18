using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using SystemCleaner.Models;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Шукає великі файли (>100 МБ) та дублікати на вибраних дисках.
    /// </summary>
    public class DiskAnalyzer
    {
        private const long LargeFileThreshold = 100L * 1024 * 1024; // 100 MB

        /// <summary>
        /// Знайти великі файли на обраних дисках.
        /// </summary>
        public async Task<List<LargeFile>> FindLargeFilesAsync(
            IEnumerable<string> drives,
            Action<string>? statusReport = null,
            CancellationToken ct = default)
        {
            var results = new ConcurrentBag<LargeFile>();

            var tasks = drives.Select(drive => Task.Run(() =>
            {
                try
                {
                    foreach (var file in SafeEnumerateAllFiles(drive, ct))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length >= LargeFileThreshold && !IsSystemPath(file))
                            {
                                results.Add(new LargeFile
                                {
                                    Path = file,
                                    Size = fi.Length
                                });
                                statusReport?.Invoke(file);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }, ct));

            await Task.WhenAll(tasks);
            return results.OrderByDescending(x => x.Size).ToList();
        }

        /// <summary>
        /// Знайти дублікати (за розміром + хеш) на обраних дисках.
        /// Обмежено файлами до 500 МБ для швидкодії.
        /// </summary>
        public async Task<List<DuplicateGroup>> FindDuplicatesAsync(
            IEnumerable<string> drives,
            Action<string>? statusReport = null,
            CancellationToken ct = default)
        {
            // 1. Збираємо файли, групуємо за розміром
            var sizeGroups = new ConcurrentDictionary<long, ConcurrentBag<string>>();

            var enumTasks = drives.Select(drive => Task.Run(() =>
            {
                foreach (var file in SafeEnumerateAllFiles(drive, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length > 1024 && fi.Length < 500L * 1024 * 1024 && !IsSystemPath(file))
                        {
                            var bag = sizeGroups.GetOrAdd(fi.Length, _ => new ConcurrentBag<string>());
                            bag.Add(file);
                        }
                    }
                    catch { }
                }
            }, ct));
            await Task.WhenAll(enumTasks);

            // 2. Групи з ≥2 файлів одного розміру - порівнюємо хеш
            var duplicates = new ConcurrentBag<DuplicateGroup>();
            var candidateGroups = sizeGroups.Where(g => g.Value.Count >= 2).ToList();

            var hashTasks = candidateGroups.Select(group => Task.Run(() =>
            {
                var hashBuckets = new ConcurrentDictionary<string, ConcurrentBag<string>>();
                foreach (var file in group.Value)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        statusReport?.Invoke(file);
                        var hash = ComputeFileHash(file);
                        if (hash != null)
                        {
                            var bucket = hashBuckets.GetOrAdd(hash, _ => new ConcurrentBag<string>());
                            bucket.Add(file);
                        }
                    }
                    catch { }
                }

                foreach (var kvp in hashBuckets)
                {
                    if (kvp.Value.Count >= 2)
                    {
                        duplicates.Add(new DuplicateGroup
                        {
                            Hash = kvp.Key,
                            Size = group.Key,
                            Paths = kvp.Value.ToList()
                        });
                    }
                }
            }, ct));
            await Task.WhenAll(hashTasks);

            return duplicates.OrderByDescending(d => d.Size).ToList();
        }

        private string? ComputeFileHash(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var sha = SHA256.Create();
                var hash = sha.ComputeHash(fs);
                return Convert.ToHexString(hash);
            }
            catch { return null; }
        }

        private IEnumerable<string> SafeEnumerateAllFiles(string root, CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                IEnumerable<string> files = Array.Empty<string>();
                IEnumerable<string> subdirs = Array.Empty<string>();
                try
                {
                    files = Directory.EnumerateFiles(dir);
                    subdirs = Directory.EnumerateDirectories(dir);
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                foreach (var f in files) yield return f;
                foreach (var d in subdirs)
                {
                    if (IsSystemPath(d)) continue;
                    stack.Push(d);
                }
            }
        }

        private bool IsSystemPath(string path)
        {
            var lower = path.ToLowerInvariant();
            return lower.Contains(@"\windows\") ||
                   lower.Contains(@"\program files") ||
                   lower.Contains(@"\$recycle.bin") ||
                   lower.Contains(@"\system volume information");
        }
    }
}