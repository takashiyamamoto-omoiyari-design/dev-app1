using System;

namespace AzureRag.Models
{
    /// <summary>
    /// 検索結果ドキュメントモデル
    /// </summary>
    public class DocumentSearchResult
    {
        /// <summary>
        /// ドキュメントのID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// ドキュメントのタイトル
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// ドキュメントのコンテンツ
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 検索スコア
        /// </summary>
        public double Score { get; set; }
    }
}