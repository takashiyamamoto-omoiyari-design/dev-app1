using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AzureRag.Models.Index;
using AzureRag.Services;
using AzureRag.Utils;

namespace AzureRag.Services.PDF
{
    public class TextChunkingService : ITextChunkingService
    {
        private readonly ILogger<TextChunkingService> _logger;
        private readonly ITokenEstimationService _tokenEstimationService;
        private readonly IAnthropicService _anthropicService;
        
        private readonly string _storageRoot = Path.Combine("storage");
        
        public TextChunkingService(
            ITokenEstimationService tokenEstimationService,
            IAnthropicService anthropicService,
            ILogger<TextChunkingService> logger = null)
        {
            _tokenEstimationService = tokenEstimationService;
            _anthropicService = anthropicService;
            _logger = logger;
            
            // ディレクトリを作成
            DirectoryHelper.EnsureDirectory(Path.Combine(_storageRoot, "tmp"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_storageRoot, "chunks"));
        }
        
        public async Task<List<TextChunk>> ChunkTextAsync(string text, string fileId, int maxTokens = 800)
        {
            _logger?.LogInformation($"テキストチャンキングを開始: ファイルID {fileId}");
            
            try
            {
                List<TextChunk> chunks = new List<TextChunk>();
                
                // 段落に分割
                string[] paragraphs = text.Split(
                    new[] { "\r\n\r\n", "\n\n" }, 
                    StringSplitOptions.RemoveEmptyEntries
                );
                
                StringBuilder currentChunk = new StringBuilder();
                int currentTokens = 0;
                int chunkIndex = 0;
                
                foreach (var paragraph in paragraphs)
                {
                    if (string.IsNullOrWhiteSpace(paragraph)) continue;
                    
                    string cleanParagraph = paragraph.Trim();
                    int paragraphTokens = _tokenEstimationService.EstimateTokenCount(cleanParagraph);
                    
                    // 段落が最大トークン数を超える場合、分割が必要
                    if (paragraphTokens > maxTokens)
                    {
                        _logger?.LogWarning($"大きな段落を検出（{paragraphTokens}トークン）：分割します");
                        
                        // 文に分割
                        var sentences = SplitIntoSentences(cleanParagraph);
                        
                        foreach (var sentence in sentences)
                        {
                            if (string.IsNullOrWhiteSpace(sentence)) continue;
                            
                            string cleanSentence = sentence.Trim();
                            int sentenceTokens = _tokenEstimationService.EstimateTokenCount(cleanSentence);
                            
                            // 文が最大トークン数を超える珍しいケース
                            if (sentenceTokens > maxTokens)
                            {
                                _logger?.LogWarning($"巨大な文を検出（{sentenceTokens}トークン）：強制分割します");
                                
                                // 強制的に分割
                                var forcedChunks = ForceSplitText(cleanSentence, maxTokens);
                                
                                foreach (var forcedChunk in forcedChunks)
                                {
                                    chunks.Add(new TextChunk
                                    {
                                        Content = forcedChunk,
                                        TokenCount = _tokenEstimationService.EstimateTokenCount(forcedChunk),
                                        Id = $"{fileId}-{chunkIndex++}",
                                        FileId = fileId,
                                        ChunkNumber = chunkIndex,
                                        CreatedAt = DateTime.UtcNow
                                    });
                                }
                                
                                continue;
                            }
                            
                            // 現在のチャンクに文を追加すると最大トークン数を超える場合
                            if (currentTokens + sentenceTokens > maxTokens)
                            {
                                // 現在のチャンクを保存
                                if (currentTokens > 0)
                                {
                                    chunks.Add(new TextChunk
                                    {
                                        Content = currentChunk.ToString().Trim(),
                                        TokenCount = currentTokens,
                                        Id = $"{fileId}-{chunkIndex++}",
                                        ChunkNumber = chunkIndex,
                                        CreatedAt = DateTime.UtcNow,
                                        FileId = fileId
                                    });
                                    
                                    currentChunk.Clear();
                                    currentTokens = 0;
                                }
                            }
                            
                            // 文を追加
                            currentChunk.AppendLine(cleanSentence);
                            currentTokens += sentenceTokens;
                        }
                    }
                    else
                    {
                        // 現在のチャンクに段落を追加すると最大トークン数を超える場合
                        if (currentTokens + paragraphTokens > maxTokens)
                        {
                            // 現在のチャンクを保存
                            if (currentTokens > 0)
                            {
                                chunks.Add(new TextChunk
                                {
                                    Content = currentChunk.ToString().Trim(),
                                    TokenCount = currentTokens,
                                    Id = $"{fileId}-page-{chunkIndex}_chunk-{chunkIndex}",
                                    ChunkNumber = chunkIndex,
                                    CreatedAt = DateTime.UtcNow,
                                    FileId = fileId
                                });
                                
                                currentChunk.Clear();
                                currentTokens = 0;
                            }
                        }
                        
                        // 段落を追加
                        currentChunk.AppendLine(cleanParagraph);
                        currentTokens += paragraphTokens;
                    }
                }
                
                // 残りのテキストがあれば最後のチャンクとして保存
                if (currentTokens > 0)
                {
                    chunks.Add(new TextChunk
                    {
                        Content = currentChunk.ToString().Trim(),
                        TokenCount = currentTokens,
                        Id = $"{fileId}-page-{chunkIndex}_chunk-{chunkIndex}",
                        ChunkNumber = chunkIndex,
                        CreatedAt = DateTime.UtcNow,
                        FileId = fileId
                    });
                }
                
                _logger?.LogInformation($"テキストチャンキング完了: {chunks.Count}チャンク作成");
                return chunks;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "テキストチャンキング中にエラーが発生しました");
                throw;
            }
        }
        
        public async Task<List<string>> StructureAndSavePageTextsAsync(Dictionary<int, string> pageTexts, string fileId)
        {
            _logger?.LogInformation($"ページテキストの構造化を開始: ファイルID {fileId}");
            
            try
            {
                var structuredTextPaths = new List<string>();
                
                foreach (var entry in pageTexts)
                {
                    int pageNumber = entry.Key;
                    string pageText = entry.Value;
                    
                    if (string.IsNullOrWhiteSpace(pageText)) continue;
                    
                    // Anthropicを使用してテキスト構造化
                    string structuredText = await StructureTextWithAnthropic(pageText, pageNumber);
                    
                    // 構造化されたテキストを一時ファイルに保存
                    string tmpPath = Path.Combine(_storageRoot, "tmp", $"{fileId}-page-{pageNumber}.txt");
                    await File.WriteAllTextAsync(tmpPath, structuredText);
                    
                    structuredTextPaths.Add(tmpPath);
                    _logger?.LogInformation($"ページ {pageNumber} の構造化テキストを保存: {tmpPath}");
                }
                
                _logger?.LogInformation($"ページテキストの構造化完了: {structuredTextPaths.Count}ページ処理");
                return structuredTextPaths;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ページテキスト構造化中にエラーが発生しました");
                throw;
            }
        }
        
        public async Task<List<TextChunk>> CreateChunksFromStructuredTextsAsync(List<string> structuredTextPaths, string fileId, int maxTokens = 800)
        {
            _logger?.LogInformation($"構造化テキストからチャンク作成を開始: ファイルID {fileId}");
            
            try
            {
                var allChunks = new List<TextChunk>();
                
                foreach (var path in structuredTextPaths)
                {
                    // 構造化テキストファイルを読み込み
                    string structuredText = await File.ReadAllTextAsync(path);
                    
                    // テキストをチャンクに分割
                    var chunks = await ChunkTextAsync(structuredText, fileId, maxTokens);
                    allChunks.AddRange(chunks);
                }
                
                _logger?.LogInformation($"構造化テキストからのチャンク作成完了: {allChunks.Count}チャンク作成");
                return allChunks;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "構造化テキストからのチャンク作成中にエラーが発生しました");
                throw;
            }
        }
        
        public async Task<string> SaveChunksToFileAsync(List<TextChunk> chunks, string fileId)
        {
            _logger?.LogInformation($"チャンクをファイルに保存: ファイルID {fileId}");
            
            try
            {
                string chunksPath = Path.Combine(_storageRoot, "chunks", $"{fileId}.json");
                
                // チャンクをJSON形式で保存
                await File.WriteAllTextAsync(
                    chunksPath, 
                    JsonSerializer.Serialize(chunks, new JsonSerializerOptions { WriteIndented = true })
                );
                
                _logger?.LogInformation($"チャンク保存完了: {chunksPath}");
                return chunksPath;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "チャンク保存中にエラーが発生しました");
                throw;
            }
        }
        
        #region Private Helper Methods
        
        private async Task<string> StructureTextWithAnthropic(string text, int pageNumber)
        {
            try
            {
                // 長すぎるテキストの場合はトークン制限を考慮
                if (_tokenEstimationService.EstimateTokenCount(text) > 80000)
                {
                    // テキストを適当なサイズに分割
                    _logger?.LogWarning($"ページ {pageNumber} のテキストが長すぎます: 分割処理を実行");
                    text = text.Substring(0, Math.Min(text.Length, 80000));
                }
                
                string systemPrompt = @"
あなたは高度な文書構造化AIアシスタントです。
与えられたPDFから抽出された生のテキストを、以下のルールに従って構造化してください：

1. 段落や項目を適切に区切り、論理的な構造を維持する
2. 見出しや小見出しを識別して階層を明確にする
3. 箇条書きリストや番号付きリストを正しくフォーマットする
4. 表や図の説明文を適切に配置する
5. フッターやヘッダー、ページ番号などの不要な要素を削除する
6. テキストの内容や意味は変更せず、構造のみを整える

元のテキストの内容をすべて保持しながら、読みやすく構造化された形式で出力してください。
";
                
                string userPrompt = $"以下はPDFのページ{pageNumber}から抽出された生のテキストです。このテキストを適切に構造化してください：\n\n{text}";
                
                var response = await _anthropicService.StructureTextAsync(userPrompt, systemPrompt);
                return response;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Anthropicによるテキスト構造化中にエラーが発生しました: ページ{pageNumber}");
                // エラーが発生した場合は元のテキストをそのまま返す
                return text;
            }
        }
        
        private List<string> SplitIntoSentences(string text)
        {
            // 文末パターンに基づいてテキストを文に分割
            // 日本語と英語の両方に対応
            var result = new List<string>();
            
            // 英語の文末パターン: ピリオド、疑問符、感嘆符の後に空白か改行
            // 日本語の文末パターン: 句点「。」や疑問符、感嘆符の後
            var matches = Regex.Matches(text, @"(.*?[.!?。])([\s]|$)");
            
            int lastEnd = 0;
            foreach (Match match in matches)
            {
                result.Add(text.Substring(lastEnd, match.Index + match.Length - lastEnd).Trim());
                lastEnd = match.Index + match.Length;
            }
            
            // 残りのテキストがあれば追加
            if (lastEnd < text.Length)
            {
                result.Add(text.Substring(lastEnd).Trim());
            }
            
            // 結果が空の場合は元のテキストを一つの文として返す
            if (result.Count == 0)
            {
                result.Add(text);
            }
            
            return result;
        }
        
        private List<string> ForceSplitText(string text, int maxTokens)
        {
            var result = new List<string>();
            
            // テキストを単語または文字に分割
            char[] delimiters = { ' ', '\t', '\n', '\r' };
            string[] words = text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
            
            StringBuilder currentChunk = new StringBuilder();
            int currentTokens = 0;
            
            foreach (var word in words)
            {
                int wordTokens = _tokenEstimationService.EstimateTokenCount(word);
                
                // 単語が単体で最大トークン数を超える場合（非常にまれ）
                if (wordTokens > maxTokens)
                {
                    // 現在のチャンクが空でない場合は追加
                    if (currentTokens > 0)
                    {
                        result.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                        currentTokens = 0;
                    }
                    
                    // 単語を文字単位で分割
                    for (int i = 0; i < word.Length; i += 100)
                    {
                        int length = Math.Min(100, word.Length - i);
                        string part = word.Substring(i, length);
                        result.Add(part);
                    }
                    
                    continue;
                }
                
                // 現在のチャンクに単語を追加すると最大トークン数を超える場合
                if (currentTokens + wordTokens > maxTokens)
                {
                    // 現在のチャンクを結果に追加
                    result.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                    currentTokens = 0;
                }
                
                // 単語を追加
                if (currentChunk.Length > 0)
                {
                    currentChunk.Append(' ');
                }
                currentChunk.Append(word);
                currentTokens += wordTokens;
            }
            
            // 残りのテキストがあれば最後のチャンクとして追加
            if (currentTokens > 0)
            {
                result.Add(currentChunk.ToString().Trim());
            }
            
            return result;
        }
        
        #endregion
    }
}