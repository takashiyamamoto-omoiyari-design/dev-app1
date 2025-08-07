using System;

namespace AzureRag.Models.Reinforcement
{
    public class SaveJsonlRequest
    {
        public string Content { get; set; }
        public string Description { get; set; }
        public string CustomId { get; set; }
    }
}