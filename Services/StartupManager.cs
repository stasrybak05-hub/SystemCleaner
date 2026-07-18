using Microsoft.Win32;
using System.IO;
using SystemCleaner.Models;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Читає та керує автозапуском через реєстр (HKCU/HKLM Run/RunOnce).
    /// </summary>
    public class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

        public List<StartupItem> GetStartupItems()
        {
            var list = new List<StartupItem>();

            // HKCU Run
            ReadRegistryEntries(Registry.CurrentUser, RunKey, "HKCU\\Run", list);
            ReadRegistryEntries(Registry.CurrentUser, RunOnceKey, "HKCU\\RunOnce", list);

            // HKLM Run
            ReadRegistryEntries(Registry.LocalMachine, RunKey, "HKLM\\Run", list);
            ReadRegistryEntries(Registry.LocalMachine, RunOnceKey, "HKLM\\RunOnce", list);

            // Папка Startup
            var startupFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "Startup");
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    list.Add(new StartupItem
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Location = "Startup Folder",
                        Enabled = true
                    });
                }
            }

            return list;
        }

        private void ReadRegistryEntries(RegistryKey root, string subKey, string location, List<StartupItem> list)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, false);
                if (key == null) return;

                foreach (var name in key.GetValueNames())
                {
                    var value = key.GetValue(name)?.ToString() ?? "";
                    list.Add(new StartupItem
                    {
                        Name = name,
                        Publisher = "",
                        Command = value,
                        Location = location,
                        Enabled = true,
                        RegistryPath = subKey
                    });
                }
            }
            catch { }
        }

        /// <summary>
        /// Вимикає запис автозапуску (переміщує в "заблоковані").
        /// </summary>
        public bool DisableItem(StartupItem item)
        {
            try
            {
                if (item.Location.StartsWith("HKCU") || item.Location.StartsWith("HKLM"))
                {
                    var root = item.Location.StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
                    using var key = root.OpenSubKey(item.RegistryPath, true);
                    if (key != null)
                    {
                        var value = key.GetValue(item.Name);
                        key.DeleteValue(item.Name, false);

                        // Зберігаємо в спеціальній папці "Disabled" для можливості відновлення
                        using var disabledKey = root.CreateSubKey(item.RegistryPath + "\\_Disabled");
                        disabledKey?.SetValue(item.Name, value ?? "");
                        return true;
                    }
                }
                else if (item.Location == "Startup Folder")
                {
                    var dir = Path.GetDirectoryName(item.Command);
                    if (dir != null)
                    {
                        var disabledDir = Path.Combine(dir, "_Disabled");
                        Directory.CreateDirectory(disabledDir);
                        var dest = Path.Combine(disabledDir, Path.GetFileName(item.Command));
                        File.Move(item.Command, dest);
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Вмикає раніше вимкнену програму.
        /// </summary>
        public bool EnableItem(StartupItem item)
        {
            try
            {
                if (item.Location.StartsWith("HKCU") || item.Location.StartsWith("HKLM"))
                {
                    var root = item.Location.StartsWith("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
                    using var disabledKey = root.OpenSubKey(item.RegistryPath + "\\_Disabled", true);
                    if (disabledKey != null)
                    {
                        var value = disabledKey.GetValue(item.Name);
                        disabledKey.DeleteValue(item.Name, false);
                        using var key = root.CreateSubKey(item.RegistryPath);
                        key?.SetValue(item.Name, value ?? "");
                        return true;
                    }
                }
                else if (item.Location == "Startup Folder")
                {
                    var dir = Path.GetDirectoryName(item.Command);
                    if (dir != null)
                    {
                        var parent = Directory.GetParent(dir)?.FullName;
                        if (parent != null)
                        {
                            var src = item.Command;
                            var dest = Path.Combine(parent, Path.GetFileName(item.Command));
                            File.Move(src, dest);
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }
}