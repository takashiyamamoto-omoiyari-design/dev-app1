namespace AzureRag.Models.Settings
{
    public class UserDirectorySettings
    {
        public string S3Bucket { get; set; } = string.Empty;
        public string S3Key { get; set; } = string.Empty;
        public int CacheTtlSeconds { get; set; } = 600;
    }
}


