using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AzureRag.Services.PDF
{
    public class PdfTextExtractionService : IPdfTextExtractionService
    {
        private readonly ILogger<PdfTextExtractionService> _logger;
        
        public PdfTextExtractionService(ILogger<PdfTextExtractionService> logger = null)
        {
            _logger = logger;
        }
        
        public async Task<string> ExtractTextAsync(Stream pdfStream)
        {
            _logger?.LogInformation("PDFからテキストを抽出を開始");
            
            try
            {
                // PDFからのテキスト抽出処理
                // UglyPDF.NETのようなライブラリを使用するとよいでしょう
                
                // この実装は単純化されたもので、実際にはPDFパーサーライブラリを使用すべきです
                using var reader = new StreamReader(pdfStream);
                var text = await reader.ReadToEndAsync();
                
                // PDFヘッダーやバイナリデータを含む場合は適切にフィルタリング
                string filteredText = FilterPdfBinaryData(text);
                
                // 余分な空白や不要な文字を削除
                filteredText = CleanupExtractedText(filteredText);
                
                _logger?.LogInformation($"テキスト抽出完了: {filteredText.Length}文字");
                return filteredText;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PDFテキスト抽出中にエラーが発生しました");
                throw;
            }
        }
        
        public async Task<Dictionary<int, string>> ExtractTextByPageAsync(Stream pdfStream)
        {
            _logger?.LogInformation("PDFからページごとのテキスト抽出を開始");
            
            try
            {
                var result = new Dictionary<int, string>();
                
                // ここでは簡易的な実装として、ページ区切りを検出してテキストを分割
                string fullText = await ExtractTextAsync(pdfStream);
                
                // ページ区切りを識別してページごとに分割
                // この実装は単純化されたもので、実際にはPDFパーサーライブラリを使用すべきです
                var pageTexts = SplitIntoPages(fullText);
                
                for (int i = 0; i < pageTexts.Count; i++)
                {
                    result.Add(i + 1, pageTexts[i]); // ページ番号は1から開始
                }
                
                _logger?.LogInformation($"ページごとのテキスト抽出完了: {result.Count}ページ");
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ページごとのPDFテキスト抽出中にエラーが発生しました");
                throw;
            }
        }
        
        #region Private Helper Methods
        
        private string FilterPdfBinaryData(string text)
        {
            // PDFのバイナリデータや制御文字を除去
            // より高度な実装では専用のPDFパーサーライブラリを使用すべきです
            var sb = new StringBuilder();
            
            foreach (char c in text)
            {
                // 印刷可能なASCII文字、タブ、改行、日本語文字などを保持
                if (c >= 32 && c <= 126 || c == '\t' || c == '\n' || c == '\r' || 
                    (c >= 0x3000 && c <= 0x9FFF) || // CJK統合漢字、平仮名、カタカナなど
                    (c >= 0xFF00 && c <= 0xFFEF))   // 全角英数字など
                {
                    sb.Append(c);
                }
            }
            
            return sb.ToString();
        }
        
        private string CleanupExtractedText(string text)
        {
            // 連続する空白や改行を単一の空白や改行に置換
            string result = Regex.Replace(text, @"\s+", " ");
            
            // 特殊な制御文字を除去
            result = Regex.Replace(result, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");
            
            return result.Trim();
        }
        
        private List<string> SplitIntoPages(string fullText)
        {
            // この実装はテキストの特徴に基づいてページ分割を試みる簡易版です
            // 実際のPDF処理ではページ区切りの情報はPDFパーサーから取得するべきです
            
            var pages = new List<string>();
            
            // ページ番号などのパターンに基づいて分割
            // 例: "Page 1", "1/10" などのパターン
            var pageMatches = Regex.Matches(fullText, @"(?i)(page\s+\d+|\d+\s*/\s*\d+|^\s*\d+\s*$)");
            
            if (pageMatches.Count > 1)
            {
                int startIndex = 0;
                
                for (int i = 0; i < pageMatches.Count; i++)
                {
                    if (i > 0)
                    {
                        int endIndex = pageMatches[i].Index;
                        if (endIndex > startIndex)
                        {
                            pages.Add(fullText.Substring(startIndex, endIndex - startIndex).Trim());
                        }
                    }
                    
                    startIndex = pageMatches[i].Index + pageMatches[i].Length;
                }
                
                // 最後のページを追加
                if (startIndex < fullText.Length)
                {
                    pages.Add(fullText.Substring(startIndex).Trim());
                }
            }
            else
            {
                // ページ区切りが見つからない場合は、一定の文字数で分割
                const int charsPerPage = 3000; // 1ページあたりの文字数を概算
                
                for (int i = 0; i < fullText.Length; i += charsPerPage)
                {
                    int length = Math.Min(charsPerPage, fullText.Length - i);
                    pages.Add(fullText.Substring(i, length).Trim());
                }
            }
            
            return pages.Count > 0 ? pages : new List<string> { fullText };
        }
        
        #endregion
    }
}