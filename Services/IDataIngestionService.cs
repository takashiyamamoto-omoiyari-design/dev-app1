using System.Collections.Generic;
using System.Threading.Tasks;
using static AzureRag.Services.AutoStructureService;

namespace AzureRag.Services
{
    public interface IDataIngestionService
    {
        /// <summary>
        /// 外部APIからチャンクデータを取得（認証付き）
        /// </summary>
        Task<List<ChunkItem>> GetChunksFromExternalApiAsync(string username, string workId);
        
        /// <summary>
        /// チャンクデータをAzure Searchインデックスに登録（認証付き）
        /// </summary>
        Task<bool> IndexChunksToAzureSearchAsync(string username, string workId, List<ChunkItem> chunks);
        
        /// <summary>
        /// 外部APIからチャンク取得→Azure Search登録の完全なパイプライン実行（認証付き）
        /// </summary>
        Task<(bool success, int processedChunks, string errorMessage)> ProcessWorkIdAsync(string username, string workId);
        
        /// <summary>
        /// ユーザーが利用可能なworkIdリストを取得（認証付き）
        /// </summary>
        Task<List<string>> GetAvailableWorkIdsAsync(string username);
        
        /// <summary>
        /// ユーザーが利用可能なworkIdメタデータリストを取得（認証付き）
        /// </summary>
        Task<List<WorkIdMetadata>> GetAvailableWorkIdMetadataAsync(string username);
    }
} 