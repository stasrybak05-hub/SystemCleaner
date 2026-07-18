namespace SystemCleaner.Models
{
    public class LargeFile
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }

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