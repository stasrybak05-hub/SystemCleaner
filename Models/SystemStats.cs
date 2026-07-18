namespace SystemCleaner.Models
{
    /// <summary>
    /// Поточний стан системи (оновлюється щосекунди).
    /// </summary>
    public class SystemStats
    {
        // CPU
        public float CpuUsage { get; set; }
        public float CpuTemp { get; set; }
        public string CpuName { get; set; } = "—";
        public int CpuCores { get; set; }

        // RAM
        public long RamUsed { get; set; }
        public long RamTotal { get; set; }
        public float RamPercent => RamTotal > 0 ? (float)RamUsed / RamTotal * 100 : 0;

        // GPU
        public float GpuTemp { get; set; }
        public string GpuName { get; set; } = "—";

        // Диски
        public float DiskReadMBps { get; set; }
        public float DiskWriteMBps { get; set; }

        // Мережа
        public float NetworkDownMBps { get; set; }
        public float NetworkUpMBps { get; set; }

        // Система
        public string OsName { get; set; } = "—";
        public TimeSpan Uptime { get; set; }
        public float BatteryPercent { get; set; } = -1; // -1 = немає батареї

        // Форматування
        public string RamUsedText => $"{FormatBytes(RamUsed)} / {FormatBytes(RamTotal)}";
        public string UptimeText => Uptime.TotalDays >= 1
            ? $"{(int)Uptime.TotalDays}д {Uptime.Hours}г {Uptime.Minutes}хв"
            : $"{Uptime.Hours}г {Uptime.Minutes}хв {Uptime.Seconds}с";
        public string DiskActivityText => $"↓{DiskReadMBps:0.0} МБ/с  ↑{DiskWriteMBps:0.0} МБ/с";
        public string NetworkText => $"↓{NetworkDownMBps:0.00} МБ/с  ↑{NetworkUpMBps:0.00} МБ/с";
        public string BatteryText => BatteryPercent < 0 ? "—" : $"{BatteryPercent:0}%";

        private static string FormatBytes(long bytes)
        {
            double s = bytes;
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            int i = 0;
            while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
            return $"{s:0.#} {units[i]}";
        }
    }
}