using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models.Index;

namespace AzureRag.Services
{
    public interface IIndexManagementService
    {
        /// <summary>
        /// PDFファイルからインデックスを作成します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>インデックス情報</returns>
        Task<IndexInfo> CreateIndexFromPdfAsync(string fileId);
        
        /// <summary>
        /// インデックスを削除します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteIndexAsync(string fileId);
        
        /// <summary>
        /// インデックスの内容を取得します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>インデックス情報</returns>
        Task<IndexInfo> GetIndexContentAsync(string fileId);
        
        /// <summary>
        /// 特定のファイルのインデックスが存在するかを確認します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>インデックスが存在するかどうか</returns>
        Task<bool> IndexExistsAsync(string fileId);
        
        /// <summary>
        /// 全てのインデックス情報を取得します
        /// </summary>
        /// <returns>インデックス情報のリスト</returns>
        Task<List<IndexInfo>> GetAllIndexesAsync();
        
        /// <summary>
        /// Pythonで抽出されたテキストからインデックスを作成します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>インデックス情報</returns>
        Task<IndexInfo> CreateIndexFromPythonExtractedTextsAsync(string fileId);
    }
}