using System;

namespace AzureRag.Models.Reinforcement
{
    public class PromptMetadata
    {
        public string FileName { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public string FilePath { get; set; }
        public string SystemPrompt { get; set; }
        public string JsonlId { get; set; }
    }
}