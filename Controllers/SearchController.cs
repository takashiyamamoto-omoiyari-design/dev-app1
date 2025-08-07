using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AzureRag.Models;
using AzureRag.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace AzureRag.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class SearchController : ControllerBase
    {
        private readonly ILogger<SearchController> _logger;
        private readonly IAzureSearchService _azureSearchService;
        private readonly Services.IAuthorizationService _authorizationService;

        public SearchController(
            ILogger<SearchController> logger,
            IAzureSearchService azureSearchService,
            Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _azureSearchService = azureSearchService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// ドキュメントを検索します（ユーザー認証付き）
        /// </summary>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SearchResponse>> Search([FromQuery] string query)
        {
            _logger.LogInformation($"検索API呼び出し: クエリ={query}");
            
            // ユーザー認証チェック
            if (!User?.Identity?.IsAuthenticated ?? true)
            {
                _logger.LogWarning("未認証ユーザーによる検索試行");
                return Unauthorized(new SearchResponse
                {
                    ErrorMessage = "認証が必要です"
                });
            }
            
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new SearchResponse
                {
                    ErrorMessage = "検索クエリが空です"
                });
            }
            
            try
            {
                var currentUsername = User.Identity.Name;
                _logger.LogInformation($"認証済みユーザー '{currentUsername}' による検索実行");
                
                // ユーザーがアクセス可能なworkIdを取得
                var allowedWorkIds = await _authorizationService.GetAllowedWorkIdsAsync(currentUsername);
                
                if (allowedWorkIds == null || !allowedWorkIds.Any())
                {
                    _logger.LogWarning($"ユーザー '{currentUsername}' にアクセス可能なworkIdがありません");
                    return Ok(new SearchResponse
                    {
                        Results = new List<DocumentSearchResult>()
                    });
                }
                
                // ユーザー固有のインデックスを設定
                _azureSearchService.SetUserSpecificIndexes(currentUsername);
                
                // workID制限付きで検索実行
                var results = await _azureSearchService.SearchDocumentsAsync(query, allowedWorkIds);
                
                // 検索結果をDocumentSearchResultに変換
                var documentResults = results.Select(r => new DocumentSearchResult
                {
                    Id = r.Id,
                    Title = r.Title,
                    Content = r.Content,
                    Score = r.Score
                }).ToList();
                
                _logger.LogInformation($"ユーザー '{currentUsername}' の検索完了: {documentResults.Count}件取得");
                
                return Ok(new SearchResponse
                {
                    Results = documentResults
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"検索中にエラーが発生: {query}");
                return StatusCode(StatusCodes.Status500InternalServerError, new SearchResponse
                {
                    ErrorMessage = "検索中にエラーが発生しました"
                });
            }
        }
    }
}