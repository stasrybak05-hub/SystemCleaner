using System.Collections.Generic;
using System.Linq;

namespace SystemCleaner.Models
{
    public class DuplicateGroup
    {
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public List<string> Paths { get; set; } = new();

        // Нова властивість: автоматично з'єднує шляхи в один рядок для відображення
        public string PathsText => string.Join(" | ", Paths);
    }
}