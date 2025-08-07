using System;
using System.Collections.Generic;

namespace AzureRag.Models.Chat
{
    /// <summary>
    /// チャットセッションを表すモデルクラス
    /// </summary>
    public class ChatSession
    {
        /// <summary>
        /// セッションID
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// セッション名
        /// </summary>
        public string Name { get; set; } = $"新しいチャット {DateTime.Now:yyyy/MM/dd HH:mm}";
        
        /// <summary>
        /// セッション作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 最終更新日時
        /// </summary>
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// セッションに含まれるメッセージ一覧
        /// </summary>
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }
}