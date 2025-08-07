using System.Text.Json.Serialization;

namespace AzureRag.Models.Settings
{
    /// <summary>
    /// Azure Search設定
    /// </summary>
    public class AzureSearchSettings
    {
        /// <summary>
        /// Azure Searchエンドポイント
        /// </summary>
        [JsonPropertyName("Endpoint")]
        public string Endpoint { get; set; }

        /// <summary>
        /// Azure Search APIキー
        /// </summary>
        [JsonPropertyName("ApiKey")]
        public string ApiKey { get; set; }

        /// <summary>
        /// Azure Search APIバージョン
        /// </summary>
        [JsonPropertyName("ApiVersion")]
        public string ApiVersion { get; set; }

        /// <summary>
        /// インデックス設定
        /// </summary>
        [JsonPropertyName("Indexes")]
        public IndexSettings Indexes { get; set; }
    }

    /// <summary>
    /// インデックス設定
    /// </summary>
    public class IndexSettings
    {
        /// <summary>
        /// メインインデックス名（文書全体用）
        /// </summary>
        [JsonPropertyName("Main")]
        public string Main { get; set; }

        /// <summary>
        /// 文章インデックス名（センテンス単位用）
        /// </summary>
        [JsonPropertyName("Sentence")]
        public string Sentence { get; set; }
    }
}