namespace SystemCleaner.Models
{
    /// <summary>
    /// Один елемент, знайдений під час сканування (файл або папка).
    /// </summary>
    public class CleanItem
    {
        public string Path { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsDirectory { get; set; }
        public bool Selected { get; set; } = true;

        public string SizeText
        {
            get
            {
                double s = Size;
                string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
                int i = 0;
                while (s >= 1024 && i < units.Length - 1)
                {
                    s /= 1024;
                    i++;
                }
                return $"{s:0.00} {units[i]}";
            }
        }
    }
}