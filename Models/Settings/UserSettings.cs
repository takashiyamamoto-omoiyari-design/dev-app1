namespace AzureRag.Models.Settings
{
    public class UserSettings
    {
        public string Id { get; set; }               // 設定ID
        public string UserPrompt { get; set; }       // ユーザープロンプト
        public int MaxTokens { get; set; } = 1000;   // 最大トークン数
        public float Temperature { get; set; } = 0.7f; // 温度設定
    }
}