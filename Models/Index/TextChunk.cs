using System;
using System.Text.Json.Serialization;

namespace AzureRag.Models.Index
{
    /// <summary>
    /// テキストチャンク（分割されたPDFのテキスト部分）を表すモデルクラス
    /// </summary>
    public class TextChunk
    {
        /// <summary>
        /// チャンクの一意のID
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        /// <summary>
        /// 元のファイルID
        /// </summary>
        [JsonPropertyName("fileId")]
        public string FileId { get; set; }
        
        /// <summary>
        /// チャンク番号（順序）
        /// </summary>
        [JsonPropertyName("chunkNumber")]
        public int ChunkNumber { get; set; }
        
        /// <summary>
        /// チャンクの内容（テキスト）
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; }
        
        /// <summary>
        /// チャンクのトークン数（概算）
        /// </summary>
        [JsonPropertyName("tokenCount")]
        public int TokenCount { get; set; }
        
        /// <summary>
        /// チャンク作成日時
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// ページ番号（存在する場合）
        /// </summary>
        [JsonPropertyName("pageNumber")]
        public int? PageNumber { get; set; }
    }
}