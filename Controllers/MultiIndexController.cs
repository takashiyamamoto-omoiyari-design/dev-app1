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
    public class MultiIndexController : ControllerBase
    {
        private readonly ILogger<MultiIndexController> _logger;
        private readonly IFileStorageService _fileStorageService;
        private readonly Services.IAuthorizationService _authorizationService;

        public MultiIndexController(
            ILogger<MultiIndexController> logger,
            IFileStorageService fileStorageService,
            Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _fileStorageService = fileStorageService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// マルチインデックス用にPDFファイルをアップロードします
        /// </summary>
        [HttpPost("upload")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UploadFiles(IFormFileCollection files)
        {
            _logger.LogInformation($"マルチインデックスファイルアップロードAPI呼び出し: {files?.Count ?? 0} ファイル");
            
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
                            $"ユーザー {currentUser} がマルチインデックスにアップロードしたファイル: {fileInfo.FileName}"
                        );
                        
                        if (workIdRegistered)
                        {
                            _logger.LogInformation("マルチインデックスworkId登録成功: ユーザー={Username}, workId={WorkId}, ファイル={FileName}", 
                                currentUser, fileInfo.Id, fileInfo.FileName);
                        }
                        else
                        {
                            _logger.LogWarning("マルチインデックスworkId登録失敗: ユーザー={Username}, workId={WorkId}, ファイル={FileName}", 
                                currentUser, fileInfo.Id, fileInfo.FileName);
                        }
                    }
                }
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "マルチインデックスファイルアップロード中にエラーが発生しました");
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
            _logger.LogInformation("マルチインデックスファイル一覧API呼び出し");
            
            try
            {
                var files = await _fileStorageService.GetFileListAsync();
                return Ok(files);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "マルチインデックスファイル一覧取得中にエラーが発生しました");
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
            _logger.LogInformation($"マルチインデックスファイル詳細API呼び出し: {id}");
            
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
                _logger.LogError(ex, $"マルチインデックスファイル詳細取得中にエラーが発生: {id}");
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
            _logger.LogInformation($"マルチインデックスファイル削除API呼び出し: {id}");
            
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
                _logger.LogError(ex, $"マルチインデックスファイル削除中にエラーが発生: {id}");
                return StatusCode(StatusCodes.Status500InternalServerError, "ファイル削除中にエラーが発生しました");
            }
        }
    }
}