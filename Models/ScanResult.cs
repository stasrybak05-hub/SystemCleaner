using System.Collections.ObjectModel;

namespace SystemCleaner.Models
{
    /// <summary>
    /// Результат сканування однієї категорії.
    /// </summary>
    public class ScanCategoryResult
    {
        public string Name { get; set; } = string.Empty;
        public ObservableCollection<CleanItem> Items { get; } = new();
        public bool Selected { get; set; } = true;

        public long TotalSize => Items.Where(i => i.Selected).Sum(i => i.Size);
        public int TotalCount => Items.Where(i => i.Selected).Count();
    }
}