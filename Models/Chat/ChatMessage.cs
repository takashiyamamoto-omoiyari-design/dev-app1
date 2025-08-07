using System;

namespace AzureRag.Models.Chat
{
    /// <summary>
    /// チャットメッセージを表すモデルクラス
    /// </summary>
    public class ChatMessage
    {
        /// <summary>
        /// メッセージID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// チャットセッションID
        /// </summary>
        public string SessionId { get; set; }
        
        /// <summary>
        /// メッセージ送信者のロール（user または assistant）
        /// </summary>
        public string Role { get; set; }
        
        /// <summary>
        /// メッセージ内容
        /// </summary>
        public string Content { get; set; }
        
        /// <summary>
        /// メッセージ作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}