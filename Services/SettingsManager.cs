using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SystemCleaner.Services
{
    /// <summary>
    /// Уніфікований менеджер налаштувань. Зберігає ВСІ користувацькі
    /// налаштування в один файл settings.json у %APPDATA%\SystemCleaner\.
    /// </summary>
    public static class SettingsManager
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SystemCleaner", "settings.json");

        // ===== Усі налаштування в одному місці =====

        /// <summary>Поточна тема (Dark, Light, Sea, Black, Gray, Sunset).</summary>
        public static string Theme { get; set; } = "Dark";

        /// <summary>Поточна мова (Ukrainian, English, Russian).</summary>
        public static string Language { get; set; } = "Ukrainian";

        /// <summary>Чи створювати точку відновлення перед очищенням.</summary>
        public static bool CreateRestorePoint { get; set; } = true;

        /// <summary>Шлях до папки з логами.</summary>
        public static string LogsPath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SystemCleaner", "logs");

        /// <summary>Автоматичне очищення при старті Windows (на майбутнє).</summary>
        public static bool AutoCleanOnStartup { get; set; } = false;

        /// <summary>Завантажує налаштування з файлу. Викликається один раз при старті.</summary>
        public static void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    EnsureLogsDirectory();
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Theme", out var theme))
                    Theme = theme.GetString() ?? "Dark";

                if (root.TryGetProperty("Language", out var lang))
                    Language = lang.GetString() ?? "Ukrainian";

                if (root.TryGetProperty("CreateRestorePoint", out var rp))
                    CreateRestorePoint = rp.GetBoolean();

                if (root.TryGetProperty("LogsPath", out var logs))
                    LogsPath = logs.GetString() ?? LogsPath;

                if (root.TryGetProperty("AutoCleanOnStartup", out var auto))
                    AutoCleanOnStartup = auto.GetBoolean();

                EnsureLogsDirectory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsManager.Load: {ex.Message}");
            }
        }

        /// <summary>Зберігає всі налаштування у файл. Викликається при виході.</summary>
        public static void SaveSettings()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var settings = new Dictionary<string, object>
                {
                    ["Theme"] = Theme,
                    ["Language"] = Language,
                    ["CreateRestorePoint"] = CreateRestorePoint,
                    ["LogsPath"] = LogsPath,
                    ["AutoCleanOnStartup"] = AutoCleanOnStartup
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsManager.Save: {ex.Message}");
            }
        }

        private static void EnsureLogsDirectory()
        {
            try
            {
                if (!Directory.Exists(LogsPath))
                    Directory.CreateDirectory(LogsPath);
            }
            catch { }
        }
    }
}