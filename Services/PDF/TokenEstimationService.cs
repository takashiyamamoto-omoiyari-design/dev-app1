using System;
using Microsoft.Extensions.Logging;

namespace AzureRag.Services.PDF
{
    public class TokenEstimationService : ITokenEstimationService
    {
        private readonly ILogger<TokenEstimationService> _logger;
        
        public TokenEstimationService(ILogger<TokenEstimationService> logger = null)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// トークン数を概算します（簡易版）
        /// 英語は単語をベースに、日本語は文字をベースに推定します
        /// </summary>
        /// <param name="text">対象テキスト</param>
        /// <returns>概算トークン数</returns>
        public int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            
            try
            {
                // 英単語数をカウント（スペースで区切られた単語）
                int wordCount = 0;
                bool hasJapanese = false;
                
                // 日本語文字（漢字、ひらがな、カタカナ）の数をカウント
                int japaneseCharCount = 0;
                
                // 文字を走査
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    
                    // 日本語文字の検出（漢字、ひらがな、カタカナの範囲）
                    if ((c >= 0x3040 && c <= 0x309F) ||    // ひらがな
                        (c >= 0x30A0 && c <= 0x30FF) ||    // カタカナ
                        (c >= 0x4E00 && c <= 0x9FFF))      // 漢字
                    {
                        hasJapanese = true;
                        japaneseCharCount++;
                    }
                    // 英単語をカウント（スペースを検出）
                    else if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        wordCount++;
                    }
                }
                
                // 最後の単語をカウント（テキストの最後にスペースがない場合）
                if (text.Length > 0 && !char.IsWhiteSpace(text[text.Length - 1]))
                {
                    wordCount++;
                }
                
                // 日本語文字がある場合は日本語トークン推定を優先
                if (hasJapanese)
                {
                    // 日本語は大体1文字 = 1.5トークン程度と概算
                    return (int)Math.Ceiling(japaneseCharCount * 1.5) + wordCount;
                }
                else
                {
                    // 英語は1単語 = 1.3トークン程度と概算
                    return (int)Math.Ceiling(wordCount * 1.3);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "トークン数推定中にエラーが発生しました");
                
                // エラーが発生した場合は文字数に基づいて大まかに推定
                // 平均的に1文字 = 0.5トークン程度と仮定
                return (int)Math.Ceiling(text.Length * 0.5);
            }
        }
    }
}