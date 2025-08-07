using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AzureRag.Services.PDF
{
    public interface IPdfTextExtractionService
    {
        /// <summary>
        /// PDFからテキストを抽出します
        /// </summary>
        /// <param name="pdfStream">PDFのストリーム</param>
        /// <returns>抽出されたテキスト</returns>
        Task<string> ExtractTextAsync(Stream pdfStream);
        
        /// <summary>
        /// PDFからテキストを抽出し、ページごとに分割します
        /// </summary>
        /// <param name="pdfStream">PDFのストリーム</param>
        /// <returns>ページごとのテキストのディクショナリ (キー: ページ番号, 値: テキスト)</returns>
        Task<Dictionary<int, string>> ExtractTextByPageAsync(Stream pdfStream);
    }
}