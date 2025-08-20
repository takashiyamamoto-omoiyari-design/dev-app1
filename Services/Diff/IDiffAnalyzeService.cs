using System.Collections.Generic;
using System.Threading.Tasks;

namespace AzureRag.Services.Diff
{
    public class DiffAnalyzeResult
    {
        public object Summary { get; set; }
        public List<PageDiff> PageDiffs { get; set; } = new List<PageDiff>();
    }

    public class PageDiff
    {
        public int PageNo { get; set; } // 0-based
        public string DiffText { get; set; } // 人が読むテキスト
        public string Details { get; set; } // 互換用
    }

    public interface IDiffAnalyzeService
    {
        Task<DiffAnalyzeResult> AnalyzeAsync(string workId, string username, int? targetPage0 = null);
    }
}


