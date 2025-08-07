using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AzureRag.Models.File;
using Microsoft.AspNetCore.Http;

namespace AzureRag.Services
{
    public interface IFileStorageService
    {
        /// <summary>
        /// ファイルをアップロードして保存します
        /// </summary>
        /// <param name="files">アップロードされたファイルのコレクション</param>
        /// <returns>保存されたファイル情報のリスト</returns>
        Task<List<Models.File.FileInfo>> UploadAndSaveFilesAsync(IFormFileCollection files);
        
        /// <summary>
        /// ファイル情報を取得します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>ファイル情報</returns>
        Task<Models.File.FileInfo> GetFileInfoAsync(string fileId);
        
        /// <summary>
        /// ファイル一覧を取得します
        /// </summary>
        /// <returns>ファイル情報のリスト</returns>
        Task<List<Models.File.FileInfo>> GetFileListAsync();
        
        /// <summary>
        /// ファイルの内容を取得します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>ファイルのストリーム</returns>
        Task<Stream> GetFileContentAsync(string fileId);
        
        /// <summary>
        /// ファイルを削除します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteFileAsync(string fileId);
        
        /// <summary>
        /// ファイルのインデックス状態を更新します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <param name="isIndexed">インデックスの有無</param>
        /// <returns>更新が成功したかどうか</returns>
        Task<bool> UpdateIndexStatusAsync(string fileId, bool isIndexed);
    }
}