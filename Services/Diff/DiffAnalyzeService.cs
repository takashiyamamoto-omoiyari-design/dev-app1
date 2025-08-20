using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AzureRag.Services.PDF;
using Microsoft.Extensions.Logging;

namespace AzureRag.Services.Diff
{
    public class DiffAnalyzeService : IDiffAnalyzeService
    {
        private readonly ILogger<DiffAnalyzeService> _logger;
        private readonly IPdfTextExtractionService _pdfTextExtraction;
        private readonly IWorkIdManagementService _workIdService;
        private readonly IAutoStructureService _autoService;

        public DiffAnalyzeService(
            ILogger<DiffAnalyzeService> logger,
            IPdfTextExtractionService pdfTextExtraction,
            IWorkIdManagementService workIdService,
            IAutoStructureService autoService)
        {
            _logger = logger;
            _pdfTextExtraction = pdfTextExtraction;
            _workIdService = workIdService;
            _autoService = autoService;
        }

        public async Task<DiffAnalyzeResult> AnalyzeAsync(string workId, string username)
        {
            if (string.IsNullOrWhiteSpace(workId)) throw new ArgumentException("workId is required");

            // 1) 原文PDFをローカル保存から取得
            var info = await _workIdService.GetWorkIdInfoAsync(workId);
            if (info == null || string.IsNullOrEmpty(info.SavedRelativePath) || !File.Exists(info.SavedRelativePath))
            {
                throw new FileNotFoundException("保存された原本PDFが見つかりません");
            }

            // 2) PDFページテキスト抽出
            Dictionary<int, string> originalByPage;
            using (var fs = File.OpenRead(info.SavedRelativePath))
            {
                originalByPage = await _pdfTextExtraction.ExtractTextByPageAsync(fs);
            }

            // 3) 構造化データ取得（page_text_list優先）
            var structured = await _autoService.GetStructuredDataAsync(workId);
            var extractedByPage = new Dictionary<int, string>(); // key: 1-based
            if (structured?.PageTextList != null && structured.PageTextList.Count > 0)
            {
                foreach (var p in structured.PageTextList)
                {
                    extractedByPage[p.PageNo + 1] = p.Text ?? string.Empty; // APIは0-based想定なので+1
                }
            }
            else if (structured?.TextList != null && structured.TextList.Count > 0)
            {
                // TextListを結合し1ページ扱い
                var text = string.Join("\n\n", structured.TextList.Select(t => t.Text ?? string.Empty));
                extractedByPage[1] = text;
            }

            // 4) 正規化
            string Normalize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var t = s.Replace("\r\n", "\n").Replace("\r", "\n");
                // ```mermaid ... ``` ブロック除去
                t = Regex.Replace(t, "```mermaid[\s\S]*?```", string.Empty, RegexOptions.IgnoreCase);
                // 余計な空白を畳み込み
                t = Regex.Replace(t, "[\t\u00A0]+", " ");
                return t.Trim();
            }

            var pageMax = Math.Max(originalByPage.Keys.DefaultIfEmpty(1).Max(), extractedByPage.Keys.DefaultIfEmpty(1).Max());
            var result = new DiffAnalyzeResult();

            for (int page = 1; page <= pageMax; page++)
            {
                var orig = Normalize(originalByPage.ContainsKey(page) ? originalByPage[page] : string.Empty);
                var ext = Normalize(extractedByPage.ContainsKey(page) ? extractedByPage[page] : string.Empty);

                // シンプル差分（行単位）：行集合の差を表示
                var origLines = (orig.Split('\n')).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                var extLines = (ext.Split('\n')).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

                var missing = origLines.Except(extLines).Take(50).ToList(); // 抜け
                var added = extLines.Except(origLines).Take(50).ToList();   // 付加

                var sb = new StringBuilder();
                sb.AppendLine($"[p.{page}] 差分");
                if (missing.Count == 0 && added.Count == 0)
                {
                    sb.AppendLine("差分なし");
                }
                else
                {
                    if (missing.Count > 0)
                    {
                        sb.AppendLine("- 欠落（原文にのみ存在）:");
                        foreach (var m in missing) sb.AppendLine("  • " + m);
                    }
                    if (added.Count > 0)
                    {
                        sb.AppendLine("- 追加（抽出結果にのみ存在）:");
                        foreach (var a in added) sb.AppendLine("  • " + a);
                    }
                }

                result.PageDiffs.Add(new PageDiff
                {
                    PageNo = page - 1,
                    DiffText = sb.ToString(),
                    Details = sb.ToString()
                });
            }

            return result;
        }
    }
}


