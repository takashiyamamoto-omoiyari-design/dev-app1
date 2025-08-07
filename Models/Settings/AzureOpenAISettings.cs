using System.Text.Json.Serialization;

namespace AzureRag.Models.Settings
{
    /// <summary>
    /// Azure OpenAI設定
    /// </summary>
    public class AzureOpenAISettings
    {
        /// <summary>
        /// Azure OpenAIエンドポイント
        /// </summary>
        [JsonPropertyName("Endpoint")]
        public string Endpoint { get; set; }

        /// <summary>
        /// Azure OpenAI APIキー
        /// </summary>
        [JsonPropertyName("ApiKey")]
        public string ApiKey { get; set; }

        /// <summary>
        /// Azure OpenAI APIバージョン
        /// </summary>
        [JsonPropertyName("ApiVersion")]
        public string ApiVersion { get; set; }

        /// <summary>
        /// デプロイメント設定
        /// </summary>
        [JsonPropertyName("Deployments")]
        public DeploymentSettings Deployments { get; set; }
    }

    /// <summary>
    /// デプロイメント設定
    /// </summary>
    public class DeploymentSettings
    {
        /// <summary>
        /// チャットモデルのデプロイメント名
        /// </summary>
        [JsonPropertyName("Chat")]
        public string Chat { get; set; }

        /// <summary>
        /// エンベディングモデルのデプロイメント名
        /// </summary>
        [JsonPropertyName("Embedding")]
        public string Embedding { get; set; }
    }
}