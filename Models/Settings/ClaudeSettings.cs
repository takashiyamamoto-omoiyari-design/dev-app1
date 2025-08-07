using System.Text.Json.Serialization;

namespace AzureRag.Models.Settings
{
    /// <summary>
    /// Anthropic Claude API設定
    /// </summary>
    public class ClaudeSettings
    {
        /// <summary>
        /// Claude API キー
        /// </summary>
        [JsonPropertyName("ApiKey")]
        public string ApiKey { get; set; }

        /// <summary>
        /// 使用するClaudeモデル
        /// </summary>
        [JsonPropertyName("Model")]
        public string Model { get; set; }
    }
}