using System.Collections.Generic;

namespace AzureRag.Models.File
{
    public class UploadResult
    {
        public int TotalFiles { get; set; }
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public List<string> SuccessfulFiles { get; set; } = new List<string>();
        public List<string> FailedFiles { get; set; } = new List<string>();
        public List<string> ErrorMessages { get; set; } = new List<string>();
    }
}