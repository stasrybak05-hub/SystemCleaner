using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using Microsoft.Win32;
using SystemCleaner.Models;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Менеджер інстальованих програм: читає список з реєстру,
    /// видаляє програми (звичайним або примусовим шляхом).
    /// </summary>
    public class UninstallManager
    {
        // Три основні гілки реєстру, де Windows зберігає інформацію про програми
        private static readonly string[] UninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", // 32-бітні програми на x64
        };

        /// <summary>
        /// Отримує список усіх інстальованих програм з реєстру.
        /// </summary>
        public List<InstalledProgram> GetInstalledPrograms()
        {
            var programs = new List<InstalledProgram>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Читаємо HKLM (всі користувачі)
            foreach (var path in UninstallPaths)
            {
                ReadFromRegistry(Registry.LocalMachine, path, programs, seenNames);
            }

            // Читаємо HKCU (тільки поточний користувач)
            ReadFromRegistry(Registry.CurrentUser, UninstallPaths[0], programs, seenNames);

            // Сортуємо за назвою
            return programs.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void ReadFromRegistry(RegistryKey root, string subKey,
            List<InstalledProgram> programs, HashSet<string> seenNames)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, false);
                if (key == null) return;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var appKey = key.OpenSubKey(subKeyName, false);
                        if (appKey == null) continue;

                        // Системні компоненти пропускаємо
                        var sysComp = appKey.GetValue("SystemComponent");
                        if (sysComp is int sc && sc == 1) continue;

                        var name = appKey.GetValue("DisplayName")?.ToString();
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        // Унікальність за назвою (щоб не було дублікатів)
                        if (!seenNames.Add(name)) continue;

                        var uninstall = appKey.GetValue("UninstallString")?.ToString() ?? "";
                        var quietUninstall = appKey.GetValue("QuietUninstallString")?.ToString() ?? "";
                        var isMsi = uninstall.StartsWith("msiexec", StringComparison.OrdinalIgnoreCase)
                                    || subKeyName.StartsWith("{") && subKeyName.EndsWith("}");

                        programs.Add(new InstalledProgram
                        {
                            Name = name,
                            Publisher = appKey.GetValue("Publisher")?.ToString() ?? "",
                            Version = appKey.GetValue("DisplayVersion")?.ToString() ?? "",
                            InstallDate = appKey.GetValue("InstallDate")?.ToString() ?? "",
                            InstallLocation = appKey.GetValue("InstallLocation")?.ToString() ?? "",
                            UninstallString = uninstall,
                            QuietUninstallString = quietUninstall,
                            DisplayIcon = appKey.GetValue("DisplayIcon")?.ToString() ?? "",
                            RegistryPath = $"{root.Name}\\{subKey}\\{subKeyName}",
                            IsMsi = isMsi,
                            Size = GetEstimatedSize(appKey)
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Бере розмір з реєстру (EstimatedSize в КБ) або повертає 0.
        /// </summary>
        private long GetEstimatedSize(RegistryKey appKey)
        {
            var size = appKey.GetValue("EstimatedSize");
            if (size is int kb) return kb * 1024L;
            return 0;
        }

        /// <summary>
        /// Асинхронно обчислює реальний розмір програми, скануючи InstallLocation.
        /// </summary>
        public async Task CalculateProgramSizesAsync(
            List<InstalledProgram> programs,
            Action<InstalledProgram>? onUpdated = null,
            CancellationToken ct = default)
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(programs, new ParallelOptions { CancellationToken = ct }, program =>
                {
                    if (program.Size > 0) return; // вже відомий з реєстру
                    if (string.IsNullOrWhiteSpace(program.InstallLocation)) return;
                    if (!Directory.Exists(program.InstallLocation)) return;

                    try
                    {
                        long total = 0;
                        foreach (var file in Directory.EnumerateFiles(
                            program.InstallLocation, "*", SearchOption.AllDirectories))
                        {
                            try { total += new FileInfo(file).Length; } catch { }
                        }
                        program.Size = total;
                        onUpdated?.Invoke(program);
                    }
                    catch { }
                });
            }, ct);
        }

        /// <summary>
        /// Запускає штатний деінсталятор програми.
        /// </summary>
        public bool Uninstall(InstalledProgram program)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(program.UninstallString))
                    return false;

                var psi = ParseUninstallString(program);
                if (psi == null) return false;

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000); // чекаємо до 5 сек, далі користувач сам
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Примусове видалення: запускає деінсталятор, потім видаляє папку та ключі реєстру.
        /// </summary>
        public async Task<bool> ForceUninstallAsync(InstalledProgram program, CancellationToken ct = default)
        {
            // 1. Спробувати штатний деінсталятор
            try
            {
                if (!string.IsNullOrWhiteSpace(program.UninstallString))
                {
                    var psi = ParseUninstallString(program, silent: true);
                    if (psi != null)
                    {
                        using var proc = Process.Start(psi);
                        if (proc != null) await proc.WaitForExitAsync(ct);
                    }
                }
            }
            catch { }

            await Task.Delay(1500, ct); // даємо час на звільнення файлів

            // 2. Видалити папку встановлення
            if (!string.IsNullOrWhiteSpace(program.InstallLocation) &&
                Directory.Exists(program.InstallLocation))
            {
                try
                {
                    Directory.Delete(program.InstallLocation, recursive: true);
                }
                catch
                {
                    // Якщо не вдається — пробуємо видалити файли по одному
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(
                            program.InstallLocation, "*", SearchOption.AllDirectories))
                        {
                            try { File.Delete(file); } catch { }
                        }
                        foreach (var dir in Directory.EnumerateDirectories(
                            program.InstallLocation).OrderByDescending(d => d.Length))
                        {
                            try { Directory.Delete(dir, true); } catch { }
                        }
                    }
                    catch { }
                }
            }

            // 3. Видалити ключ реєстру
            try
            {
                DeleteRegistryKey(program.RegistryPath);
            }
            catch { }

            return true;
        }

        /// <summary>
        /// Парсить UninstallString у ProcessStartInfo.
        /// Обробляє випадки: "C:\path\uninstall.exe" /args, msiexec /x {...}, тощо.
        /// </summary>
        private ProcessStartInfo? ParseUninstallString(InstalledProgram program, bool silent = false)
        {
            var cmd = silent && !string.IsNullOrWhiteSpace(program.QuietUninstallString)
                ? program.QuietUninstallString
                : program.UninstallString;

            if (string.IsNullOrWhiteSpace(cmd)) return null;

            // MSI-пакети
            if (program.IsMsi && cmd.Contains("{"))
            {
                var guid = ExtractGuid(cmd);
                if (guid != null)
                {
                    var args = $"/x {guid}";
                    if (silent) args += " /qn /norestart";
                    return new ProcessStartInfo("msiexec.exe", args)
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                }
            }

            // Звичайні .exe
            string exePath;
            string arguments = "";

            if (cmd.StartsWith("\""))
            {
                var end = cmd.IndexOf('"', 1);
                exePath = cmd.Substring(1, end - 1);
                arguments = end + 1 < cmd.Length ? cmd.Substring(end + 1).Trim() : "";
            }
            else
            {
                var parts = cmd.Split(' ', 2);
                exePath = parts[0];
                arguments = parts.Length > 1 ? parts[1] : "";
            }

            // Додаємо "тихий" аргумент, якщо потрібно
            if (silent && !string.IsNullOrWhiteSpace(arguments))
            {
                if (!arguments.Contains("/S", StringComparison.OrdinalIgnoreCase) &&
                    !arguments.Contains("/silent", StringComparison.OrdinalIgnoreCase) &&
                    !arguments.Contains("/quiet", StringComparison.OrdinalIgnoreCase))
                {
                    arguments += " /S";
                }
            }
            else if (silent && string.IsNullOrWhiteSpace(arguments))
            {
                arguments = "/S";
            }

            return new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas"
            };
        }

        private string? ExtractGuid(string input)
        {
            var start = input.IndexOf('{');
            var end = input.IndexOf('}');
            if (start >= 0 && end > start)
                return input.Substring(start, end - start + 1);
            return null;
        }

        /// <summary>
        /// Видаляє ключ реєстру за повним шляхом.
        /// </summary>
        private void DeleteRegistryKey(string fullPath)
        {
            var parts = fullPath.Split('\\', 2);
            if (parts.Length < 2) return;

            var rootName = parts[0];
            var subPath = parts[1];

            RegistryKey? root = rootName switch
            {
                "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKEY_CURRENT_USER" => Registry.CurrentUser,
                _ => null
            };

            if (root == null) return;

            try
            {
                root.DeleteSubKeyTree(subPath, throwOnMissingSubKey: false);
            }
            catch { }
        }
    }
}