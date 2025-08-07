using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AzureRag.Services;

namespace AzureRag.Pages
{
    public class DataStructuringModel : PageModel
    {
        private readonly ILogger<DataStructuringModel> _logger;
        private readonly IFileStorageService _fileStorageService;
        private readonly IConfiguration _configuration;
        private readonly IDocumentChatService _documentChatService;

        public DataStructuringModel(
            ILogger<DataStructuringModel> logger,
            IConfiguration configuration,
            IDocumentChatService documentChatService,
            IFileStorageService fileStorageService = null)
        {
            _logger = logger;
            _fileStorageService = fileStorageService;
            _configuration = configuration;
            _documentChatService = documentChatService;
        }

        public void OnGet()
        {
            // ページロード時の処理
            _logger.LogInformation("データ構造化ページがロードされました");
        }
    }
}