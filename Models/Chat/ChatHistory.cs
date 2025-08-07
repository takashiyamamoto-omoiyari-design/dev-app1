using System;
using System.Collections.Generic;

namespace AzureRag.Models.Chat
{
    public class ChatHistory
    {
        public string Id { get; set; }               // 履歴ID
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();  // メッセージリスト
        public DateTime CreatedAt { get; set; }      // 作成日時
        public DateTime UpdatedAt { get; set; }      // 更新日時
    }
}