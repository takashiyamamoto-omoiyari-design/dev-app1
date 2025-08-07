using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AzureRag.Utils;

namespace AzureRag.Controllers
{
    [ApiController]
    public abstract class BaseReinforcementController : ControllerBase
    {
        protected readonly string _rlStorageDirectory = Path.Combine("storage", "reinforcement");
        protected readonly ILogger _logger;
        
        public BaseReinforcementController(ILogger logger = null)
        {
            _logger = logger;
            // ロガーがnullの場合でも動作するよう設計
            // ディレクトリが存在しない場合は作成
            if (!Directory.Exists(_rlStorageDirectory))
            {
                Directory.CreateDirectory(_rlStorageDirectory);
            }
            
            // サブディレクトリの作成
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "jsonl"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "prompts"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "responses"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "evaluations"));
        }
    }
}