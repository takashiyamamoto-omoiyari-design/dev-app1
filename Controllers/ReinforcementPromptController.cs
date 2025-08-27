using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using AzureRag.Models.Reinforcement;
using AzureRag.Utils;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/reinforcement/prompt")]
    public class ReinforcementPromptController : BaseReinforcementController
    {
        public ReinforcementPromptController(ILogger<ReinforcementPromptController> logger = null) 
            : base(logger)
        {
        }
        
        [HttpPost("save")]
        [Consumes("application/json")]
        public async Task<IActionResult> SavePrompt([FromBody] SavePromptRequest request)
        {
            try
            {
                _logger?.LogInformation($"SavePrompt called. Content length: {request?.Content?.Length ?? 0}, Description: {request?.Description ?? "None"}");
                
                if (request == null)
                {
                    _logger?.LogWarning("SavePrompt: Request is null");
                    return BadRequest(new { success = false, error = "リクエストが無効です" });
                }
                
                if (string.IsNullOrEmpty(request.Content))
                {
                    _logger?.LogWarning("SavePrompt: Content is null or empty");
                    return BadRequest(new { success = false, error = "プロンプトコンテンツが空です" });
                }
                
                // 日本時間（JST）の年月日-時分秒形式でファイル名を生成
                var jstNow = TimeZoneHelper.GetCurrentJapanTime();
                var timestamp = jstNow.ToString("yyyyMMdd-HHmmss");
                var fileNameBase = timestamp;
                
                // JSONLファイルIDがある場合は、説明から抽出
                string jsonlIdSuffix = "";
                if (!string.IsNullOrEmpty(request.Description) && 
                    (request.Description.Contains("prompt_") || request.Description.Contains("【")) && 
                    request.Description.Contains("-"))
                {
                    // 注意: プレフィックスタグが付いている場合、例:【AD】prompt_YYYYMMDD-HHMMSS-12345
                    // の場合も対応できるよう、正規表現を修正
                    var match = Regex.Match(request.Description, @"_\d{8}-\d{6}(-\d+)");
                    if (match.Success && match.Groups.Count > 1)
                    {
                        jsonlIdSuffix = match.Groups[1].Value;
                        _logger?.LogInformation($"Extracted JSONL ID suffix: {jsonlIdSuffix}");
                    }
                }
                
                // デバッグログ（メッセージはそのまま残す。デバッグ後も使用できる形にしておく）
                _logger?.LogInformation($"Description received: '{request.Description}'");
                
                // 【超重要】デバッグ：リクエストの全内容をログに出力
                _logger?.LogInformation("【超重要デバッグ】SavePrompt リクエスト全体の内容:");
                _logger?.LogInformation($"【超重要デバッグ】Content (先頭10文字): {(request.Content?.Length > 10 ? request.Content.Substring(0, 10) : request.Content)}...");
                _logger?.LogInformation($"【超重要デバッグ】Description: '{request.Description}'");
                _logger?.LogInformation($"【超重要デバッグ】SystemPrompt設定: {!string.IsNullOrEmpty(request.SystemPrompt)}");
                _logger?.LogInformation($"【超重要デバッグ】JsonlId設定: {!string.IsNullOrEmpty(request.JsonlId)}");
                
                // 【超重要】デバッグ：Descriptionにおける【】の検出
                if (!string.IsNullOrEmpty(request.Description))
                {
                    _logger?.LogInformation($"【超重要デバッグ】Description文字数: {request.Description.Length}");
                    _logger?.LogInformation($"【超重要デバッグ】Description先頭文字: '{request.Description.Substring(0, Math.Min(request.Description.Length, 10))}'");
                    _logger?.LogInformation($"【超重要デバッグ】Description先頭に【が含まれる: {request.Description.StartsWith("【")}");
                    _logger?.LogInformation($"【超重要デバッグ】Descriptionに【が含まれる: {request.Description.Contains("【")}");
                    _logger?.LogInformation($"【超重要デバッグ】Descriptionに】が含まれる: {request.Description.Contains("】")}");
                    
                    // 【】があるかをより詳細に検出
                    var tagMatch = Regex.Match(request.Description, @"【(.+?)】");
                    if (tagMatch.Success)
                    {
                        _logger?.LogInformation($"【超重要デバッグ】正規表現で【】タグを検出: '{tagMatch.Groups[1].Value}'");
                    }
                    else
                    {
                        _logger?.LogInformation("【超重要デバッグ】正規表現での【】タグ検出に失敗");
                    }
                }
                
                // ファイル名を生成
                var fileName = $"{fileNameBase}.txt";
                var filePath = Path.Combine(_rlStorageDirectory, "prompts", fileName);
                
                _logger?.LogInformation($"SavePrompt: Saving prompt to {filePath}");
                
                // プロンプトディレクトリが存在するか確認
                var promptsDir = Path.Combine(_rlStorageDirectory, "prompts");
                if (!Directory.Exists(promptsDir))
                {
                    _logger?.LogInformation($"Creating prompts directory: {promptsDir}");
                    Directory.CreateDirectory(promptsDir);
                }
                
                // ファイルに保存
                await System.IO.File.WriteAllTextAsync(filePath, request.Content);
                
                // メタデータを作成（作成日時も日本時間）
                var metaData = new PromptMetadata
                {
                    FileName = fileName,
                    // 説明がない場合は空文字列を使用
                    Description = string.IsNullOrEmpty(request.Description) ? "" : request.Description,
                    CreatedAt = jstNow,
                    FilePath = filePath,
                    SystemPrompt = string.IsNullOrEmpty(request.SystemPrompt) ? "" : request.SystemPrompt,
                    JsonlId = string.IsNullOrEmpty(request.JsonlId) ? "" : request.JsonlId
                };
                
                var metaFilePath = Path.Combine(_rlStorageDirectory, "prompts", $"{fileNameBase}.meta.json");
                await System.IO.File.WriteAllTextAsync(
                    metaFilePath, 
                    JsonSerializer.Serialize(metaData, new JsonSerializerOptions { WriteIndented = true })
                );
                
                _logger?.LogInformation($"SavePrompt: Successfully saved prompt. File: {fileName}, Size: {request.Content.Length} bytes");
                
                return Ok(new { success = true, fileName = fileName });
            }
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, $"Error saving prompt: {ex.Message}");
                _logger?.LogError($"Exception details: {ex.ToString()}");
                
                if (ex.InnerException != null)
                {
                    _logger?.LogError($"Inner exception: {ex.InnerException.Message}");
                }
                
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpGet("list")]
        public IActionResult ListPrompts()
        {
            try
            {
                _logger?.LogInformation("ListPrompts called");
                
                var promptsDir = Path.Combine(_rlStorageDirectory, "prompts");
                
                // プロンプトディレクトリが存在するか確認
                if (!Directory.Exists(promptsDir))
                {
                    _logger?.LogInformation($"Creating prompts directory: {promptsDir}");
                    Directory.CreateDirectory(promptsDir);
                    // 新しいディレクトリなので、ファイルはありません
                    return Ok(new { success = true, files = new List<PromptMetadata>() });
                }
                
                _logger?.LogInformation($"Reading meta files from {promptsDir}");
                var metaFiles = Directory.GetFiles(promptsDir, "*.meta.json");
                _logger?.LogInformation($"Found {metaFiles.Length} meta files");
                
                var promptFiles = new List<PromptMetadata>();
                
                foreach (var metaFile in metaFiles)
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(metaFile);
                        var metadata = JsonSerializer.Deserialize<PromptMetadata>(json);
                        
                        if (metadata != null)
                        {
                            promptFiles.Add(metadata);
                            _logger?.LogInformation($"Added prompt metadata: {metadata.FileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, $"Error reading meta file {metaFile}");
                        // メタデータの読み込みに失敗した場合はスキップ
                        continue;
                    }
                }
                
                // 作成日時の降順でソート
                promptFiles.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
                
                _logger?.LogInformation($"Returning {promptFiles.Count} prompt files");
                return Ok(new { success = true, files = promptFiles });
            }
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, $"Error listing prompts: {ex.Message}");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// 最新のプロンプト本文を返す（.meta.jsonが無くても*.txtの更新日時で判定）
        /// </summary>
        [HttpGet("latest")]
        public IActionResult GetLatestPrompt()
        {
            try
            {
                var promptsDir = Path.Combine(_rlStorageDirectory, "prompts");
                if (!Directory.Exists(promptsDir))
                {
                    return NotFound(new { success = false, error = "prompts ディレクトリが存在しません" });
                }

                var txtFiles = Directory.GetFiles(promptsDir, "*.txt");
                if (txtFiles.Length == 0)
                {
                    return NotFound(new { success = false, error = "プロンプトファイルが見つかりません" });
                }

                // 更新日時の降順で最新を選択
                var latest = txtFiles
                    .Select(p => new { Path = p, Time = System.IO.File.GetLastWriteTime(p) })
                    .OrderByDescending(x => x.Time)
                    .First();

                var content = System.IO.File.ReadAllText(latest.Path);
                return Ok(new { success = true, file = Path.GetFileName(latest.Path), content });
            }
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, "最新プロンプト取得に失敗");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpGet("get/{id}")]
        public IActionResult GetPrompt(string id)
        {
            try
            {
                var filePath = Path.Combine(_rlStorageDirectory, "prompts", id);
                
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound(new { success = false, error = "プロンプトファイルが見つかりません" });
                }
                
                var content = System.IO.File.ReadAllText(filePath);
                
                // メタデータファイルのパス
                var metaFileName = id.Replace(".txt", ".meta.json");
                var metaFilePath = Path.Combine(_rlStorageDirectory, "prompts", metaFileName);
                
                PromptMetadata metadata = null;
                if (System.IO.File.Exists(metaFilePath))
                {
                    try
                    {
                        var metaContent = System.IO.File.ReadAllText(metaFilePath);
                        metadata = JsonSerializer.Deserialize<PromptMetadata>(metaContent);
                    }
                    catch (Exception)
                    {
                        // メタデータの読み込みエラーは無視
                    }
                }
                
                return Ok(new { 
                    success = true, 
                    content = content,
                    metadata = metadata
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpGet("download/{id}")]
        public IActionResult DownloadPrompt(string id)
        {
            try
            {
                _logger?.LogInformation($"DownloadPrompt called with id: {id}");
                
                var filePath = Path.Combine(_rlStorageDirectory, "prompts", id);
                _logger?.LogInformation($"Looking for file at {filePath}");
                
                if (!System.IO.File.Exists(filePath))
                {
                    _logger?.LogWarning($"プロンプトファイルが見つかりません: {filePath}");
                    return NotFound(new { success = false, error = $"プロンプトファイルが見つかりません: {id}" });
                }
                
                var content = System.IO.File.ReadAllText(filePath);
                var contentBytes = Encoding.UTF8.GetBytes(content);
                _logger?.LogInformation($"ファイルのサイズ: {contentBytes.Length} バイト");
                
                // メタデータファイルから説明を取得（存在する場合）
                var metaFileName = id.Replace(".txt", ".meta.json");
                var metaFilePath = Path.Combine(_rlStorageDirectory, "prompts", metaFileName);
                _logger?.LogInformation($"メタデータファイルパス: {metaFilePath}");
                
                string downloadFileName = id;
                
                if (System.IO.File.Exists(metaFilePath))
                {
                    try
                    {
                        var metaContent = System.IO.File.ReadAllText(metaFilePath);
                        var metadata = JsonSerializer.Deserialize<PromptMetadata>(metaContent);
                        
                        if (metadata != null)
                        {
                            _logger?.LogInformation($"メタデータ: {metadata.FileName}, 説明: {metadata.Description}");
                            
                            // 追加デバッグ情報
                            _logger?.LogInformation($"【重要デバッグ】説明フィールドの内容: '{metadata.Description}'");
                            _logger?.LogInformation($"【重要デバッグ】説明フィールドの長さ: {metadata.Description?.Length ?? 0}");
                            
                            if (!string.IsNullOrEmpty(metadata.Description))
                            {
                                // 説明に【】が含まれているか確認
                                var hasPrefix = metadata.Description.Contains("【") && metadata.Description.Contains("】");
                                _logger?.LogInformation($"【重要デバッグ】説明に【】が含まれているか: {hasPrefix}");
                                
                                // 常に説明をファイル名として使用する
                                downloadFileName = $"{metadata.Description}.txt";
                                _logger?.LogInformation($"ダウンロードファイル名を変更: {downloadFileName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, $"メタデータの読み込みエラー: {metaFilePath}");
                        // メタデータの読み込みエラーは無視
                    }
                }
                else
                {
                    _logger?.LogInformation($"メタデータファイルが見つかりません: {metaFilePath}");
                }
                
                _logger?.LogInformation($"プロンプトファイルをダウンロード: {id} → {downloadFileName}");
                
                // Content-Type: text/plain を設定し、ファイル名を指定
                return File(contentBytes, "text/plain", downloadFileName);
            }
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, $"プロンプトファイルのダウンロード中にエラーが発生しました: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger?.LogError($"Inner exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpPost("upload")]
        public async Task<IActionResult> UploadPrompt([FromForm] IFormFile file, [FromForm] string description = "", 
                                                    [FromForm] string systemPrompt = "", [FromForm] string jsonlId = "")
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { success = false, error = "ファイルが選択されていないか、空のファイルです" });
                }
                
                // ファイルの内容を読み込み
                string content;
                using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
                {
                    content = await reader.ReadToEndAsync();
                }
                
                // 日本時間（JST）の年月日-時分秒形式でファイル名を生成
                var jstNow = TimeZoneHelper.GetCurrentJapanTime();
                var timestamp = jstNow.ToString("yyyyMMdd-HHmmss");
                var fileNameBase = timestamp;
                
                var fileName = $"{fileNameBase}.txt";
                var filePath = Path.Combine(_rlStorageDirectory, "prompts", fileName);
                
                // ファイルに保存
                await System.IO.File.WriteAllTextAsync(filePath, content);
                
                // メタデータを作成
                var metaData = new PromptMetadata
                {
                    FileName = fileName,
                    Description = string.IsNullOrEmpty(description) ? file.FileName : description,
                    CreatedAt = jstNow,
                    FilePath = filePath,
                    SystemPrompt = string.IsNullOrEmpty(systemPrompt) ? "" : systemPrompt,
                    JsonlId = string.IsNullOrEmpty(jsonlId) ? "" : jsonlId
                };
                
                var metaFilePath = Path.Combine(_rlStorageDirectory, "prompts", $"{fileNameBase}.meta.json");
                await System.IO.File.WriteAllTextAsync(
                    metaFilePath, 
                    JsonSerializer.Serialize(metaData, new JsonSerializerOptions { WriteIndented = true })
                );
                
                _logger?.LogInformation($"プロンプトファイルをアップロードしました: {fileName}, サイズ: {content.Length}バイト");
                
                return Ok(new { 
                    success = true, 
                    fileName = fileName,
                    originalFileName = file.FileName
                });
            }
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, "プロンプトファイルのアップロード中にエラーが発生しました");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}