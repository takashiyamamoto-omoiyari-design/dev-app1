namespace AzureRag.Models
{
    /// <summary>
    /// 単一の質問に対する回答を保持するレスポンスクラス
    /// </summary>
    public class ChatAnswerResponse
    {
        public string? Answer { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
