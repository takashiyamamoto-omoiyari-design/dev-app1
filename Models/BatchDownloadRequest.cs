using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AzureRag.Models
{
    public class BatchDownloadRequest
    {
        [JsonPropertyName("filepaths")]
        public List<string> FilePaths { get; set; }
    }
}