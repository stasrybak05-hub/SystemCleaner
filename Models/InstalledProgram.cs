namespace SystemCleaner.Models
{
    /// <summary>
    /// Інстальована програма, знайдена в реєстрі Windows.
    /// </summary>
    public class InstalledProgram
    {
        /// <summary>Назва програми.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Видавець (компанія-розробник).</summary>
        public string Publisher { get; set; } = string.Empty;

        /// <summary>Версія програми.</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Дата встановлення (рядок у форматі YYYYMMDD з реєстру).</summary>
        public string InstallDate { get; set; } = string.Empty;

        /// <summary>Розмір у байтах (або 0, якщо не визначено).</summary>
        public long Size { get; set; }

        /// <summary>Шлях до папки встановлення.</summary>
        public string InstallLocation { get; set; } = string.Empty;

        /// <summary>Команда для запуску штатного деінсталятора.</summary>
        public string UninstallString { get; set; } = string.Empty;

        /// <summary>"Тиха" команда деінсталяції (якщо підтримується).</summary>
        public string QuietUninstallString { get; set; } = string.Empty;

        /// <summary>Іконка програми (шлях до .exe або .ico).</summary>
        public string DisplayIcon { get; set; } = string.Empty;

        /// <summary>Гілка реєстру, де знайдено програму (для примусового видалення).</summary>
        public string RegistryPath { get; set; } = string.Empty;

        /// <summary>Чи це MSI-пакет (MSI-програми потребують msiexec).</summary>
        public bool IsMsi { get; set; }

        /// <summary>Форматований розмір для відображення.</summary>
        public string SizeText
        {
            get
            {
                if (Size <= 0) return "—";
                double s = Size;
                string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
                int i = 0;
                while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
                return $"{s:0.##} {units[i]}";
            }
        }

        /// <summary>Форматована дата встановлення.</summary>
        public string FormattedDate
        {
            get
            {
                if (string.IsNullOrWhiteSpace(InstallDate) || InstallDate.Length != 8)
                    return "—";
                if (DateTime.TryParseExact(InstallDate, "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                    return dt.ToString("dd.MM.yyyy");
                return InstallDate;
            }
        }
    }
}