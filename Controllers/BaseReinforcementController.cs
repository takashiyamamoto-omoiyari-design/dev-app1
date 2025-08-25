using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using AzureRag.Utils;

namespace AzureRag.Controllers
{
    [ApiController]
    public abstract class BaseReinforcementController : ControllerBase
    {
        protected readonly string _rlStorageDirectory;
        protected readonly ILogger _logger;
        
        public BaseReinforcementController(ILogger logger = null)
        {
            _logger = logger;
            // 保存ベースディレクトリ（Linux本番は /var/lib/demo-app2、無ければローカルstorage）
            var baseDir = "/var/lib/demo-app2";
            if (!Directory.Exists(baseDir)) baseDir = Path.Combine("storage");
            _rlStorageDirectory = Path.Combine(baseDir, "reinforcement");
            if (!Directory.Exists(_rlStorageDirectory)) Directory.CreateDirectory(_rlStorageDirectory);
            
            // サブディレクトリの作成
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "jsonl"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "prompts"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "responses"));
            DirectoryHelper.EnsureDirectory(Path.Combine(_rlStorageDirectory, "evaluations"));
        }
    }
}