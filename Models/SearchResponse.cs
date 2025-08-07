using System.Collections.Generic;

namespace AzureRag.Models
{
    /// <summary>
    /// 検索レスポンスモデル
    /// </summary>
    public class SearchResponse
    {
        /// <summary>
        /// 検索結果リスト
        /// </summary>
        public List<DocumentSearchResult> Results { get; set; } = new List<DocumentSearchResult>();
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; set; }
    }
}