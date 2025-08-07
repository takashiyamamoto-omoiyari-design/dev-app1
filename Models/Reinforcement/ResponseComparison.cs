using System;

namespace AzureRag.Models.Reinforcement
{
    public class ResponseComparison
    {
        public string Query { get; set; }
        public string StandardResponse { get; set; }
        public string FewshotResponse { get; set; }
        public string PromptId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}