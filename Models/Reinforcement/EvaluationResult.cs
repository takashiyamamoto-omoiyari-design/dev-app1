using System;
using System.Collections.Generic;

namespace AzureRag.Models.Reinforcement
{
    public class EvaluationResult
    {
        public string JsonlId { get; set; }
        public string ResponseId { get; set; }
        public double SimilarityScore { get; set; }
        public double AccuracyScore { get; set; }
        public string CorrectText { get; set; }
        public string AIResponse { get; set; }
        public Dictionary<string, double> AnalysisDetails { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}