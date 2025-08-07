using System.Collections.Generic;

namespace AzureRag.Models.Chat
{
    /// <summary>
    /// チャットリクエストを表すDTOクラス
    /// </summary>
    public class ChatRequest
    {
        /// <summary>
        /// チャットセッションID（新規セッションの場合はnull）
        /// </summary>
        public string? SessionId { get; set; } = string.Empty;
        
        /// <summary>
        /// ユーザーからのクエリ（最新のメッセージ）
        /// </summary>
        public string Query { get; set; } = string.Empty;
        
        /// <summary>
        /// 過去のメッセージ履歴（任意）
        /// </summary>
        public List<ChatMessage> History { get; set; } = new List<ChatMessage>();
        
        /// <summary>
        /// システムプロンプト（任意）
        /// </summary>
        public string SystemPrompt { get; set; } = string.Empty;
        
        /// <summary>
        /// ユーザー名（認証済みユーザーの識別用）
        /// </summary>
        public string? Username { get; set; }
    }
}