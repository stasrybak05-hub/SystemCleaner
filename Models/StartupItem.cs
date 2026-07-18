namespace SystemCleaner.Models
{
    /// <summary>
    /// Запис про програму в автозапуску.
    /// </summary>
    public class StartupItem
    {
        public string Name { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty; // Run/RunOnce/Task/StartupFolder
        public bool Enabled { get; set; }
        public string RegistryPath { get; set; } = string.Empty;
    }
}