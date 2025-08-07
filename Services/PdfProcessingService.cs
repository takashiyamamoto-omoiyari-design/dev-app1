using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using AzureRag.Models.Index;
using AzureRag.Services.PDF;
using AzureRag.Utils;

namespace AzureRag.Services
{
    public class PdfProcessingService : IPdfProcessingService
    {
        private readonly IPdfTextExtractionService _textExtractionService;
        private readonly ITextChunkingService _textChunkingService;
        private readonly ITokenEstimationService _tokenEstimationService;
        private readonly ILogger<PdfProcessingService> _logger;
        private readonly string _storageRoot = Path.Combine("storage");
        
        public PdfProcessingService(
            IPdfTextExtractionService textExtractionService,
            ITextChunkingService textChunkingService,
            ITokenEstimationService tokenEstimationService,
            ILogger<PdfProcessingService> logger = null)
        {
            _textExtractionService = textExtractionService;
            _textChunkingService = textChunkingService;
            _tokenEstimationService = tokenEstimationService;
            _logger = logger;
            
            // ディレクトリを作成
            DirectoryHelper.EnsureDirectory(Path.Combine(_storageRoot, "tmp"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_storageRoot, "chunks"));
        }
        
        public async Task<string> ExtractTextAsync(Stream pdfStream)
        {
            _logger?.LogInformation("PDFからテキスト抽出を開始");
            return await _textExtractionService.ExtractTextAsync(pdfStream);
        }
        
        public async Task<List<TextChunk>> ChunkTextAsync(string text, string fileId, int maxTokens = 800)
        {
            _logger?.LogInformation($"テキストチャンキングを開始: ファイルID {fileId}");
            return await _textChunkingService.ChunkTextAsync(text, fileId, maxTokens);
        }
        
        public async Task<Dictionary<int, string>> ExtractTextByPageAsync(Stream pdfStream)
        {
            _logger?.LogInformation("PDFからページごとのテキスト抽出を開始");
            return await _textExtractionService.ExtractTextByPageAsync(pdfStream);
        }
        
        public async Task<List<string>> StructureAndSavePageTextsAsync(Dictionary<int, string> pageTexts, string fileId)
        {
            _logger?.LogInformation($"ページテキストの構造化を開始: ファイルID {fileId}");
            return await _textChunkingService.StructureAndSavePageTextsAsync(pageTexts, fileId);
        }
        
        public async Task<List<TextChunk>> CreateChunksFromStructuredTextsAsync(List<string> structuredTextPaths, string fileId, int maxTokens = 800)
        {
            _logger?.LogInformation($"構造化テキストからチャンク作成を開始: ファイルID {fileId}");
            return await _textChunkingService.CreateChunksFromStructuredTextsAsync(structuredTextPaths, fileId, maxTokens);
        }
        
        public async Task<string> SaveChunksToFileAsync(List<TextChunk> chunks, string fileId)
        {
            _logger?.LogInformation($"チャンクをファイルに保存: ファイルID {fileId}");
            return await _textChunkingService.SaveChunksToFileAsync(chunks, fileId);
        }
        
        public int EstimateTokenCount(string text)
        {
            return _tokenEstimationService.EstimateTokenCount(text);
        }
        
        /// <summary>
        /// Pythonで抽出されたテキストファイルからチャンクを作成します
        /// </summary>
        /// <param name="fileId">ファイルID</param>
        /// <param name="maxTokens">最大トークン数（デフォルト800）</param>
        /// <returns>テキストチャンクのリスト</returns>
        public async Task<List<TextChunk>> CreateChunksFromPythonExtractedTextsAsync(string fileId, int maxTokens = 800)
        {
            _logger?.LogInformation($"Pythonで抽出されたテキストからチャンク作成を開始: ファイルID {fileId}");
            
            try
            {
                var allChunks = new List<TextChunk>();
                string tmpDir = Path.Combine(_storageRoot, "tmp");
                
                // 指定されたファイルIDに対応するテキストファイルを検索
                var textFiles = Directory.GetFiles(tmpDir, $"{fileId}-page-*.txt")
                    .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Split('-').Last()))
                    .ToList();
                
                if (textFiles.Count == 0)
                {
                    _logger?.LogWarning($"テキストファイルが見つかりません: ファイルID {fileId}");
                    return allChunks;
                }
                
                _logger?.LogInformation($"{textFiles.Count}ページのテキストファイルを処理します");
                
                foreach (var textFile in textFiles)
                {
                    // ページ番号を取得
                    string fileName = Path.GetFileNameWithoutExtension(textFile);
                    int pageNumber = int.Parse(fileName.Split('-').Last());
                    
                    // テキストファイルを読み込み
                    string pageText = await File.ReadAllTextAsync(textFile);
                    
                    if (string.IsNullOrWhiteSpace(pageText))
                    {
                        _logger?.LogWarning($"ページ {pageNumber} のテキストが空です");
                        continue;
                    }
                    
                    // テキストをチャンクに分割
                    var pageChunks = await _textChunkingService.ChunkTextAsync(pageText, fileId, maxTokens);
                    
                    // ページ番号を設定し、IDをページ固有のものに修正
                    foreach (var chunk in pageChunks)
                    {
                        chunk.PageNumber = pageNumber;
                        // IDを各ページごとに一意になるように修正
                        chunk.Id = $"{fileId}-page-{pageNumber}_chunk-{chunk.ChunkNumber}";
                    }
                    
                    allChunks.AddRange(pageChunks);
                    _logger?.LogInformation($"ページ {pageNumber} から {pageChunks.Count} チャンクを作成しました");
                }
                
                _logger?.LogInformation($"合計 {allChunks.Count} チャンクを作成しました");
                return allChunks;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"テキストからのチャンク作成中にエラーが発生: {fileId}");
                throw;
            }
        }
    }
}