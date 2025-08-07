using System;
using System.ComponentModel.DataAnnotations;

namespace AzureRag.Models.Reinforcement
{
    public class SavePromptRequest
    {
        [Required(ErrorMessage = "プロンプト内容は必須です")]
        public string Content { get; set; }
        
        // 以下は任意（オプション）のパラメータ
        public string Description { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public string JsonlId { get; set; } = "";
    }
}