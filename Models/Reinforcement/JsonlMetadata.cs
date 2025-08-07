using System;

namespace AzureRag.Models.Reinforcement
{
    public class JsonlMetadata
    {
        public string FileName { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string FilePath { get; set; }
        public string CustomId { get; set; }
    }
}