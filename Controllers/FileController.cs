using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AzureRag.Models.File;
using AzureRag.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using FileInfo = AzureRag.Models.File.FileInfo;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FileController : ControllerBase
    {
        private readonly ILogger<FileController> _logger;
        private readonly IFileStorageService _fileStorageService;
        private readonly IIndexManagementService _indexManagementService;
        private readonly Services.IAuthorizationService _authorizationService;

        public FileController(
            ILogger<FileController> logger,
            IFileStorageService fileStorageService,
            IIndexManagementService indexManagementService,
            Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _fileStorageService = fileStorageService;
            _indexManagementService = indexManagementService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// PDFファイルをアップロードします
        /// </summary>
        [HttpPost("upload")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UploadFiles(IFormFileCollection files)
        {
            _logger.LogInformation($"ファイルアップロードAPI呼び出し: {files?.Count ?? 0} ファイル");
            
            if (files == null || files.Count == 0)
            {
                return BadRequest("アップロードするファイルがありません");
            }
            
            try
            {
                var result = await _fileStorageService.UploadAndSaveFilesAsync(files);
                
                // 【追加】アップロード後にworkIdをユーザーに登録
                var currentUser = User.Identity?.Name;
                if (!string.IsNullOrEmpty(currentUser) && result != null)
                {
                    foreach (var fileInfo in result)
                    {
                        var workIdRegistered = await _authorizationService.AddWorkIdToUserAsync(
                            currentUser,
                            fileInfo.Id, // FileInfoのIdがworkId
                            fileInfo.FileName,
                            $"ユーザー {currentUser} がアップロードしたファイル: {fileInfo.FileName}"
                        );
                        
                        if (workIdRegistered)
                        {
                            _logger.LogInformation("ファイルworkId登録成功: ユーザー={Username}, workId={WorkId}, ファイル={FileName}", 
                                currentUser, fileInfo.Id, fileInfo.FileName);
                        }
                        else
                        {
                            _logger.LogWarning("ファイルworkId登録失敗: ユーザー={Username}, workId={WorkId}, ファイル={FileName}", 
                                currentUser, fileInfo.Id, fileInfo.FileName);
                        }
                    }
                }
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ファイルアップロード中にエラーが発生しました");
                return StatusCode(StatusCodes.Status500InternalServerError, "ファイルアップロード中にエラーが発生しました");
            }
        }

        /// <summary>
        /// アップロード済みファイルの一覧を取得します
        /// </summary>
        [HttpGet("list")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<List<FileInfo>>> GetFileList()
        {
            _logger.LogInformation("ファイル一覧API呼び出し");
            
            try
            {
                var files = await _fileStorageService.GetFileListAsync();
                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ファイル一覧取得中にエラーが発生しました");
                return StatusCode(StatusCodes.Status500InternalServerError, "ファイル一覧取得中にエラーが発生しました");
            }
        }

        /// <summary>
        /// 特定のファイルの詳細を取得します
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<FileInfo>> GetFileInfo(string id)
        {
            _logger.LogInformation($"ファイル詳細API呼び出し: {id}");
            
            try
            {
                var fileInfo = await _fileStorageService.GetFileInfoAsync(id);
                
                if (fileInfo == null)
                {
                    return NotFound($"ファイルが見つかりません: {id}");
                }
                
                return Ok(fileInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ファイル詳細取得中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "ファイル詳細取得中にエラーが発生しました");
            }
        }

        /// <summary>
        /// ファイルを削除します
        /// </summary>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteFile(string id)
        {
            _logger.LogInformation($"ファイル削除API呼び出し: {id}");
            
            try
            {
                var result = await _fileStorageService.DeleteFileAsync(id);
                
                if (!result)
                {
                    return NotFound($"ファイルが見つかりません: {id}");
                }
                
                return Ok(new { success = true, message = $"ファイルを削除しました: {id}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"ファイル削除中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "ファイル削除中にエラーが発生しました");
            }
        }
        
        /// <summary>
        /// Pythonスクリプトを使用してPDFからテキストを抽出します
        /// </summary>
        [HttpPost("process-python/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ProcessPdfWithPython(string id)
        {
            _logger.LogInformation($"Python PDFテキスト抽出API呼び出し: {id}");
            
            try
            {
                // ファイルが存在するか確認
                var fileInfo = await _fileStorageService.GetFileInfoAsync(id);
                if (fileInfo == null)
                {
                    return NotFound($"ファイルが見つかりません: {id}");
                }
                
                // ファイルパスを取得
                string filePath = Path.Combine("storage", "files", $"{id}.pdf");
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound($"ファイルが見つかりません: {filePath}");
                }
                
                _logger.LogInformation($"Pythonスクリプトを実行してPDFテキストを抽出します: {filePath}");
                
                // Pythonスクリプトを実行して処理
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"extract_pdf_text.py \"{filePath}\" \"{id}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(processStartInfo))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"Pythonスクリプト実行中にエラーが発生しました: {error}");
                        return StatusCode(StatusCodes.Status500InternalServerError, $"テキスト抽出中にエラーが発生しました: {error}");
                    }
                    
                    _logger.LogInformation($"テキスト抽出が完了しました: {output}");
                }
                
                return Ok(new { success = true, message = $"ファイル {id} のテキスト抽出が完了しました" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PDFテキスト抽出中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "PDFテキスト抽出中にエラーが発生しました");
            }
        }
        
        /// <summary>
        /// PDFを画像に変換します
        /// </summary>
        [HttpPost("pdf-to-images/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ConvertPdfToImages(string id, [FromQuery] int dpi = 200)
        {
            _logger.LogInformation($"PDF画像変換API呼び出し: {id}, DPI: {dpi}");
            
            try
            {
                // ファイルが存在するか確認
                var fileInfo = await _fileStorageService.GetFileInfoAsync(id);
                if (fileInfo == null)
                {
                    return NotFound($"ファイルが見つかりません: {id}");
                }
                
                // ファイルパスを取得
                string filePath = Path.Combine("storage", "files", $"{id}.pdf");
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound($"ファイルが見つかりません: {filePath}");
                }
                
                // 出力ディレクトリを作成
                string outputDir = Path.Combine("storage", "images");
                Directory.CreateDirectory(outputDir);
                
                _logger.LogInformation($"Pythonスクリプトを実行してPDFを画像に変換します: {filePath}");
                
                // Pythonスクリプトを実行して処理
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"pdf_to_images.py \"{filePath}\" \"{id}\" --dpi {dpi}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using (var process = Process.Start(processStartInfo))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"画像変換中にエラーが発生しました: {error}");
                        return StatusCode(StatusCodes.Status500InternalServerError, $"画像変換中にエラーが発生しました: {error}");
                    }
                    
                    _logger.LogInformation($"画像変換が完了しました: {output}");
                }
                
                return Ok(new { success = true, message = $"ファイル {id} の画像変換が完了しました" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"PDF画像変換中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "PDF画像変換中にエラーが発生しました");
            }
        }
        
        /// <summary>
        /// 画像からテキストを抽出します（Claude AI使用）
        /// </summary>
        [HttpPost("images-to-text/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ExtractTextFromImages(string id, [FromQuery] int maxWorkers = 3)
        {
            _logger.LogInformation($"画像テキスト抽出API呼び出し: {id}, 並列処理数: {maxWorkers}");
            
            try
            {
                // ファイルが存在するか確認
                var fileInfo = await _fileStorageService.GetFileInfoAsync(id);
                if (fileInfo == null)
                {
                    return NotFound($"ファイルが見つかりません: {id}");
                }
                
                // 画像ディレクトリ
                string imageDir = Path.Combine("storage", "images");
                
                // このファイルIDに対応する画像ファイルが存在するか確認
                bool imageExists = false;
                foreach (var file in Directory.GetFiles(imageDir))
                {
                    if (Path.GetFileName(file).StartsWith($"{id}-page-"))
                    {
                        imageExists = true;
                        break;
                    }
                }
                
                if (!imageExists)
                {
                    return NotFound($"ファイルID {id} に対応する画像が見つかりません。先にPDF画像変換を実行してください。");
                }
                
                _logger.LogInformation($"Pythonスクリプトを実行して画像からテキストを抽出します: {id}");
                
                // Pythonスクリプトを実行して処理
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"claude_image_to_text.py \"{id}\" --max_workers {maxWorkers}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // 環境変数ANTHROPIC_API_KEYを設定
                    Environment = 
                    {
                        {"ANTHROPIC_API_KEY", Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")}
                    }
                };
                
                using (var process = Process.Start(processStartInfo))
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode != 0)
                    {
                        _logger.LogError($"テキスト抽出中にエラーが発生しました: {error}");
                        return StatusCode(StatusCodes.Status500InternalServerError, $"テキスト抽出中にエラーが発生しました: {error}");
                    }
                    
                    _logger.LogInformation($"テキスト抽出が完了しました: {output}");
                }
                
                return Ok(new { success = true, message = $"ファイル {id} の画像からのテキスト抽出が完了しました" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"画像テキスト抽出中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "画像テキスト抽出中にエラーが発生しました");
            }
        }
        
        /// <summary>
        /// PDFをClaudeを使った画像処理パイプラインで処理します（PDF→画像→テキスト）
        /// </summary>
        [HttpPost("process-claude-pipeline/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ProcessPdfWithClaudePipeline(string id, [FromQuery] int dpi = 200, [FromQuery] int maxWorkers = 3)
        {
            _logger.LogInformation($"Claude画像処理パイプラインAPI呼び出し: {id}, DPI: {dpi}, 並列処理数: {maxWorkers}");
            
            try
            {
                // ステップ1: PDFを画像に変換
                var imageResult = await ConvertPdfToImages(id, dpi);
                if (imageResult is not OkObjectResult)
                {
                    return imageResult; // エラーがあれば即時返す
                }
                
                // ステップ2: 画像からテキストを抽出
                var textResult = await ExtractTextFromImages(id, maxWorkers);
                if (textResult is not OkObjectResult)
                {
                    return textResult; // エラーがあれば即時返す
                }
                
                return Ok(new { 
                    success = true, 
                    message = $"ファイル {id} のClaudeを使った画像処理パイプラインが完了しました"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Claude画像処理パイプライン中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "Claude画像処理パイプライン中にエラーが発生しました");
            }
        }
        
        /// <summary>
        /// Pythonで抽出したテキストからインデックスを作成します
        /// </summary>
        [HttpPost("index-python/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateIndexFromPythonExtractedText(string id)
        {
            _logger.LogInformation($"Python抽出テキストからインデックス作成API呼び出し: {id}");
            
            try
            {
                // ファイルが存在するか確認
                var fileInfo = await _fileStorageService.GetFileInfoAsync(id);
                if (fileInfo == null)
                {
                    return NotFound($"ファイルが見つかりません: {id}");
                }
                
                // Pythonで抽出されたテキストからインデックスを作成
                var indexInfo = await _indexManagementService.CreateIndexFromPythonExtractedTextsAsync(id);
                
                return Ok(new 
                { 
                    success = true,
                    message = $"ファイル {id} のインデックスが作成されました",
                    indexInfo = new
                    {
                        id = indexInfo.IndexId,
                        fileId = indexInfo.FileId,
                        chunkCount = indexInfo.ChunkCount,
                        createdAt = indexInfo.CreatedAt
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"インデックス作成中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "インデックス作成中にエラーが発生しました");
            }
        }
    }
}