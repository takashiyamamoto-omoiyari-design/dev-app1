using System;
using System.Text.Json.Serialization;

namespace AzureRag.Models.File
{
    /// <summary>
    /// アップロードされたファイルの情報を保持するモデルクラス
    /// </summary>
    public class FileInfo
    {
        /// <summary>
        /// ファイルの一意のID
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }
        
        /// <summary>
        /// ファイルの元のファイル名
        /// </summary>
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }
        
        /// <summary>
        /// ファイルのMIMEタイプ
        /// </summary>
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }
        
        /// <summary>
        /// ファイルサイズ（バイト）
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }
        
        /// <summary>
        /// ファイルパス（サーバー上のローカルパス）
        /// </summary>
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; }
        
        /// <summary>
        /// アップロード日時
        /// </summary>
        [JsonPropertyName("uploadDate")]
        public DateTime UploadDate { get; set; }
        
        /// <summary>
        /// インデックスの有無
        /// </summary>
        [JsonPropertyName("isIndexed")]
        public bool IsIndexed { get; set; }
        
        /// <summary>
        /// インデックス作成日時
        /// </summary>
        [JsonPropertyName("indexedDate")]
        public DateTime? IndexedDate { get; set; }
    }
}