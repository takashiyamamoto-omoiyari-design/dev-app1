using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureRag.Models.Index
{
    /// <summary>
    /// インデックス情報を表すモデルクラス
    /// </summary>
    public class IndexInfo
    {
        /// <summary>
        /// ファイルID
        /// </summary>
        [JsonPropertyName("fileId")]
        public string FileId { get; set; }
        
        /// <summary>
        /// インデックスID
        /// </summary>
        [JsonPropertyName("indexId")]
        public string IndexId { get; set; }
        
        /// <summary>
        /// チャンク数
        /// </summary>
        [JsonPropertyName("chunkCount")]
        public int ChunkCount { get; set; }
        
        /// <summary>
        /// テキスト内容（分割されたチャンク）
        /// </summary>
        [JsonPropertyName("textContent")]
        public List<string> TextContent { get; set; }
        
        /// <summary>
        /// 作成日時
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public IndexInfo()
        {
            TextContent = new List<string>();
        }
    }
}