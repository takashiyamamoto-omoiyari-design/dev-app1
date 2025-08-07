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
    [Route("api/reinforcement/jsonl")]
    public class ReinforcementJsonlController : BaseReinforcementController
    {
        public ReinforcementJsonlController(ILogger<ReinforcementJsonlController> logger = null) 
            : base(logger)
        {
        }
        
        [HttpPost("save")]
        public async Task<IActionResult> SaveJsonl([FromBody] SaveJsonlRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Content))
                {
                    return BadRequest(new { success = false, error = "JSONLコンテンツが空です" });
                }
                
                // 日本時間（JST）の年月日-時分秒形式でファイル名を生成
                var jstNow = TimeZoneHelper.GetCurrentJapanTime();
                var timestamp = jstNow.ToString("yyyyMMdd-HHmmss");
                var fileNameBase = timestamp;
                
                // カスタムIDがある場合はファイル名に追加
                if (!string.IsNullOrEmpty(request.CustomId))
                {
                    // ファイル名に使用できない文字を除去
                    var safeCustomId = new string(request.CustomId.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                    fileNameBase = $"{timestamp}-{safeCustomId}";
                }
                
                var fileName = $"{fileNameBase}.jsonl";
                var filePath = Path.Combine(_rlStorageDirectory, "jsonl", fileName);
                
                // ファイルに保存
                await System.IO.File.WriteAllTextAsync(filePath, request.Content);
                
                // メタデータを作成（作成日時も日本時間）
                var metaData = new JsonlMetadata
                {
                    FileName = fileName,
                    // 説明がない場合は空文字列を使用
                    Description = string.IsNullOrEmpty(request.Description) ? "" : request.Description,
                    CreatedAt = jstNow,
                    FilePath = filePath,
                    // カスタムIDも保存
                    CustomId = string.IsNullOrEmpty(request.CustomId) ? "" : request.CustomId
                };
                
                var metaFilePath = Path.Combine(_rlStorageDirectory, "jsonl", $"{fileNameBase}.meta.json");
                await System.IO.File.WriteAllTextAsync(
                    metaFilePath, 
                    JsonSerializer.Serialize(metaData, new JsonSerializerOptions { WriteIndented = true })
                );
                
                return Ok(new { 
                    success = true, 
                    fileName = fileName,
                    customId = string.IsNullOrEmpty(request.CustomId) ? "" : request.CustomId 
                });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpGet("list")]
        public IActionResult ListJsonl()
        {
            try
            {
                var jsonlFiles = new List<JsonlMetadata>();
                
                // アップロードディレクトリからメタデータを取得
                var uploadDir = Path.Combine(_rlStorageDirectory, "jsonl", "upload");
                if (Directory.Exists(uploadDir))
                {
                    var uploadMetaFiles = Directory.GetFiles(uploadDir, "*.meta.json");
                    foreach (var metaFile in uploadMetaFiles)
                    {
                        try
                        {
                            var json = System.IO.File.ReadAllText(metaFile);
                            var metadata = JsonSerializer.Deserialize<JsonlMetadata>(json);
                            
                            if (metadata != null)
                            {
                                jsonlFiles.Add(metadata);
                            }
                        }
                        catch
                        {
                            // メタデータの読み込みに失敗した場合はスキップ
                            continue;
                        }
                    }
                }
                
                // 通常のjsonlディレクトリからもメタデータを取得
                var jsonlDir = Path.Combine(_rlStorageDirectory, "jsonl");
                var metaFiles = Directory.GetFiles(jsonlDir, "*.meta.json");
                
                foreach (var metaFile in metaFiles)
                {
                    try
                    {
                        var json = System.IO.File.ReadAllText(metaFile);
                        var metadata = JsonSerializer.Deserialize<JsonlMetadata>(json);
                        
                        if (metadata != null)
                        {
                            jsonlFiles.Add(metadata);
                        }
                    }
                    catch
                    {
                        // メタデータの読み込みに失敗した場合はスキップ
                        continue;
                    }
                }
                
                // 作成日時の降順でソート
                jsonlFiles.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
                
                return Ok(new { success = true, files = jsonlFiles });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpGet("get/{id}")]
        public IActionResult GetJsonl(string id)
        {
            try
            {
                // まずアップロードフォルダで検索
                var uploadDir = Path.Combine(_rlStorageDirectory, "jsonl", "upload");
                var uploadFilePath = Path.Combine(uploadDir, id);
                
                if (System.IO.File.Exists(uploadFilePath))
                {
                    // アップロードフォルダにファイルがある場合
                    var uploadFileContent = System.IO.File.ReadAllText(uploadFilePath);
                    return Ok(new { success = true, content = uploadFileContent });
                }
                
                // アップロードフォルダになければ通常のjsonlフォルダで検索
                var filePath = Path.Combine(_rlStorageDirectory, "jsonl", id);
                
                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound(new { success = false, error = "ファイルが見つかりません" });
                }
                
                var fileContent = System.IO.File.ReadAllText(filePath);
                return Ok(new { success = true, content = fileContent });
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpGet("download/{id}")]
        public IActionResult DownloadJsonl(string id)
        {
            try
            {
                // まずアップロードフォルダで検索
                var uploadDir = Path.Combine(_rlStorageDirectory, "jsonl", "upload");
                var uploadFilePath = Path.Combine(uploadDir, id);
                
                string filePath;
                string metaFilePath;
                
                if (System.IO.File.Exists(uploadFilePath))
                {
                    // アップロードフォルダにファイルがある場合
                    filePath = uploadFilePath;
                    
                    // メタデータファイルの存在を確認
                    var jsonlFileName = Path.GetFileName(filePath);
                    var metaFileName = jsonlFileName.Replace(".jsonl", ".meta.json");
                    metaFilePath = Path.Combine(uploadDir, metaFileName);
                }
                else
                {
                    // 通常のjsonlフォルダで検索
                    filePath = Path.Combine(_rlStorageDirectory, "jsonl", id);
                    
                    if (!System.IO.File.Exists(filePath))
                    {
                        return NotFound(new { success = false, error = "ファイルが見つかりません" });
                    }
                    
                    // メタデータファイルの存在を確認
                    var jsonlFileName = Path.GetFileName(filePath);
                    var metaFileName = jsonlFileName.Replace(".jsonl", ".meta.json");
                    metaFilePath = Path.Combine(_rlStorageDirectory, "jsonl", metaFileName);
                }
                
                // デバッグ用ログ
                var currentJsonlFileName = Path.GetFileName(filePath);
                _logger?.LogInformation($"JSONLファイル: {currentJsonlFileName}, メタデータファイル: {Path.GetFileName(metaFilePath)}");
                _logger?.LogInformation($"メタデータファイルパス: {metaFilePath}");
                
                string customId = "";
                
                // メタデータからCustomIdを取得（存在する場合）
                if (System.IO.File.Exists(metaFilePath))
                {
                    try 
                    {
                        _logger?.LogInformation($"メタデータファイルが見つかりました: {metaFilePath}");
                        var metaContent = System.IO.File.ReadAllText(metaFilePath);
                        var metadata = JsonSerializer.Deserialize<JsonlMetadata>(metaContent);
                        if (metadata != null && !string.IsNullOrEmpty(metadata.CustomId))
                        {
                            customId = metadata.CustomId;
                            _logger?.LogInformation($"カスタムID '{customId}' をメタデータから取得しました");
                        }
                        else
                        {
                            _logger?.LogWarning("メタデータにカスタムIDが含まれていないか、メタデータが無効です");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"メタデータの読み込みエラー: {ex.Message}");
                    }
                }
                else
                {
                    _logger?.LogWarning($"メタデータファイルが見つかりません: {metaFilePath}");
                }
                
                var downloadContent = System.IO.File.ReadAllText(filePath);
                var bytes = System.Text.Encoding.UTF8.GetBytes(downloadContent);
                
                // カスタムIDがある場合はダウンロードファイル名に反映
                string downloadFileName = id;
                if (!string.IsNullOrEmpty(customId))
                {
                    _logger?.LogInformation($"ダウンロードファイル名にカスタムID '{customId}' を使用します");
                    
                    // ファイル名を解析してタイムスタンプ部分を抽出
                    // "20250409-123456-2.jsonl" → "20250409-123456-999.jsonl"
                    
                    // 正規表現で「yyyyMMdd-HHmmss」部分を抽出
                    var match = Regex.Match(id, @"^(\d{8}-\d{6})");
                    if (match.Success)
                    {
                        // タイムスタンプを抽出できた場合
                        string timestamp = match.Groups[1].Value;
                        downloadFileName = $"{timestamp}-{customId}.jsonl";
                        _logger?.LogInformation($"タイムスタンプ '{timestamp}' とカスタムID '{customId}' を使用して新しいファイル名を作成: {downloadFileName}");
                    }
                    else
                    {
                        // タイムスタンプを抽出できなかった場合は単純に連結
                        string fileName = Path.GetFileNameWithoutExtension(id);
                        if (fileName.Contains("-"))
                        {
                            // 最後のハイフンより前の部分を取得
                            int lastDashIndex = fileName.LastIndexOf("-");
                            if (lastDashIndex > 0)
                            {
                                string baseFileName = fileName.Substring(0, lastDashIndex);
                                downloadFileName = $"{baseFileName}-{customId}.jsonl";
                                _logger?.LogInformation($"ベース部分 '{baseFileName}' とカスタムID '{customId}' を組み合わせて新しいファイル名を作成: {downloadFileName}");
                            }
                            else
                            {
                                downloadFileName = $"{fileName}-{customId}.jsonl";
                                _logger?.LogInformation($"元のファイル名 '{fileName}' とカスタムID '{customId}' を連結: {downloadFileName}");
                            }
                        }
                        else
                        {
                            downloadFileName = $"{fileName}-{customId}.jsonl";
                            _logger?.LogInformation($"元のファイル名 '{fileName}' とカスタムID '{customId}' を連結: {downloadFileName}");
                        }
                    }
                }
                
                return File(bytes, "text/plain", downloadFileName);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
        
        [HttpPost("upload")]
        public async Task<IActionResult> UploadJsonl([FromForm] IFormFile file, [FromForm] string description = "", [FromForm] string customId = "")
        {
            try
            {
                if (file == null || file.Length == 0)
                {
                    return BadRequest(new { success = false, error = "ファイルが選択されていないか、空のファイルです" });
                }
                
                // ファイル拡張子の確認
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (extension != ".jsonl")
                {
                    return BadRequest(new { success = false, error = "JSONLファイル（.jsonl）のみアップロードできます" });
                }
                
                // ファイルの内容を読み込み
                string content;
                using (var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8))
                {
                    content = await reader.ReadToEndAsync();
                }
                
                // JSONLの形式チェック（各行が有効なJSONオブジェクトであることを確認）
                // 非常に緩い検証に変更 - 少なくとも1行以上あるかだけ確認
                try
                {
                    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    bool hasValidJson = false;

                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line) && line.Trim().StartsWith("{") && line.Trim().EndsWith("}"))
                        {
                            try
                            {
                                // 各行をJSONとしてパースしてみる
                                JsonDocument.Parse(line);
                                hasValidJson = true;
                                break; // 1つでも有効なJSONがあれば十分
                            }
                            catch 
                            {
                                // パースエラーは無視
                                _logger?.LogWarning($"無効なJSON行をスキップ: {line}");
                            }
                        }
                    }

                    if (!hasValidJson)
                    {
                        _logger?.LogWarning("有効なJSON行が見つかりません");
                        // エラーを返さず、警告だけ残す
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"JSONL検証中のエラー: {ex.Message}");
                    // エラーを返さず、警告だけ残す
                }
                
                // 日本時間（JST）の年月日-時分秒形式でファイル名を生成
                var jstNow = TimeZoneHelper.GetCurrentJapanTime();
                var timestamp = jstNow.ToString("yyyyMMdd-HHmmss");
                var fileNameBase = timestamp;
                
                // カスタムIDがある場合はファイル名に追加
                if (!string.IsNullOrEmpty(customId))
                {
                    // ファイル名に使用できない文字を除去
                    var safeCustomId = new string(customId.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                    fileNameBase = $"{timestamp}-{safeCustomId}";
                }
                
                var fileName = $"{fileNameBase}.jsonl";
                
                // アップロードディレクトリを使用
                var uploadDir = Path.Combine(_rlStorageDirectory, "jsonl", "upload");
                
                // アップロードディレクトリが存在しない場合は作成
                if (!Directory.Exists(uploadDir))
                {
                    Directory.CreateDirectory(uploadDir);
                }
                
                var filePath = Path.Combine(uploadDir, fileName);
                
                // ファイルに保存
                await System.IO.File.WriteAllTextAsync(filePath, content);
                
                // メタデータを作成（作成日時も日本時間）
                var metaData = new JsonlMetadata
                {
                    FileName = fileName,
                    Description = string.IsNullOrEmpty(description) ? file.FileName : description,
                    CreatedAt = jstNow,
                    FilePath = filePath,
                    CustomId = string.IsNullOrEmpty(customId) ? "" : customId
                };
                
                var metaFilePath = Path.Combine(uploadDir, $"{fileNameBase}.meta.json");
                await System.IO.File.WriteAllTextAsync(
                    metaFilePath, 
                    JsonSerializer.Serialize(metaData, new JsonSerializerOptions { WriteIndented = true })
                );
                
                _logger?.LogInformation($"JSONLファイルをアップロードしました: {fileName}, サイズ: {content.Length}バイト");
                
                return Ok(new { 
                    success = true, 
                    fileName = fileName,
                    customId = string.IsNullOrEmpty(customId) ? "" : customId,
                    originalFileName = file.FileName
                });
            }
            catch (System.Exception ex)
            {
                _logger?.LogError(ex, "JSONLファイルのアップロード中にエラーが発生しました");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }
}