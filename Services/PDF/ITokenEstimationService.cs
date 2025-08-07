namespace AzureRag.Services.PDF
{
    public interface ITokenEstimationService
    {
        /// <summary>
        /// トークン数を概算します（簡易版）
        /// </summary>
        /// <param name="text">対象テキスト</param>
        /// <returns>概算トークン数</returns>
        int EstimateTokenCount(string text);
    }
}