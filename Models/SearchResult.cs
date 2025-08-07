using System;

namespace AzureRag.Models
{
    /// <summary>
    /// MSPSeimei用の検索結果を表すモデルクラス
    /// </summary>
    public class SearchResult
    {
        /// <summary>
        /// ドキュメントID
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// ファイルパス
        /// </summary>
        public string Filepath { get; set; }
        
        /// <summary>
        /// タイトル
        /// </summary>
        public string Title { get; set; }
        
        /// <summary>
        /// 内容
        /// </summary>
        public string Content { get; set; }
        
        /// <summary>
        /// 検索スコア
        /// </summary>
        public double Score { get; set; }
        
        // チャンク検索用の追加プロパティ
        public int PageNumber { get; set; }
        public int ChunkNumber { get; set; }
        public string MatchedKeywords { get; set; }
    }
    
    /// <summary>
    /// SearchResultからDocumentSearchResultへの変換拡張メソッド
    /// </summary>
    public static class SearchResultExtensions
    {
        /// <summary>
        /// SearchResultをDocumentSearchResultに変換します
        /// </summary>
        public static DocumentSearchResult ToDocumentSearchResult(this SearchResult result)
        {
            return new DocumentSearchResult
            {
                Id = result.Id,
                Title = result.Title,
                Content = result.Content,
                Score = result.Score
            };
        }
        
        /// <summary>
        /// SearchResultのリストをDocumentSearchResultのリストに変換します
        /// </summary>
        public static System.Collections.Generic.List<DocumentSearchResult> ToDocumentSearchResults(this System.Collections.Generic.List<SearchResult> results)
        {
            return results.ConvertAll(r => r.ToDocumentSearchResult());
        }
    }
}