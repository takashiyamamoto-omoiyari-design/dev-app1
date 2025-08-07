using System.Collections.Generic;
using System.Threading.Tasks;

namespace AzureRag.Services
{
    /// <summary>
    /// 複数モデル対応AI処理サービスインターフェース
    /// </summary>
    public interface IMultiModelAIService
    {
        /// <summary>
        /// 検索結果と質問に基づいて回答を生成
        /// </summary>
        /// <param name="query">ユーザーからの質問</param>
        /// <param name="context">検索結果などのコンテキスト情報</param>
        /// <returns>生成された回答</returns>
        Task<string> GenerateAnswerAsync(string query, string context);
        
        /// <summary>
        /// テキストをエンベディングベクトルに変換
        /// </summary>
        /// <param name="text">変換するテキスト</param>
        /// <returns>エンベディングベクトル</returns>
        Task<IReadOnlyList<float>> GenerateEmbeddingAsync(string text);
        
        /// <summary>
        /// 画像からテキストを抽出（OCR）
        /// </summary>
        /// <param name="imagePath">画像ファイルのパス</param>
        /// <returns>抽出されたテキスト</returns>
        Task<string> ExtractTextFromImageAsync(string imagePath);
    }
}