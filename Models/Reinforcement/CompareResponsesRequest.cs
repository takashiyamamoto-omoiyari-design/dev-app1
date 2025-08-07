using System;

namespace AzureRag.Models.Reinforcement
{
    public class CompareResponsesRequest
    {
        public string Query { get; set; }
        public string PromptId { get; set; }
    }
}