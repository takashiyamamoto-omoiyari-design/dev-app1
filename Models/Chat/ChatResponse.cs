using System.Collections.Generic;
using AzureRag.Models;

namespace AzureRag.Models.Chat
{
    /// <summary>
    /// チャットレスポンスを表すDTOクラス
    /// </summary>
    public class ChatResponse
    {
        /// <summary>
        /// チャットセッションID
        /// </summary>
        public string SessionId { get; set; }
        
        /// <summary>
        /// AIアシスタントからの回答
        /// </summary>
        public string Answer { get; set; }
        
        /// <summary>
        /// 参照ドキュメント一覧
        /// </summary>
        public List<DocumentSearchResult> Sources { get; set; } = new List<DocumentSearchResult>();
        
        /// <summary>
        /// 更新されたメッセージ履歴
        /// </summary>
        public List<ChatMessage> History { get; set; } = new List<ChatMessage>();
    }
}