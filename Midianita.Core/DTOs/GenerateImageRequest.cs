using System.ComponentModel.DataAnnotations;

namespace Midianita.Core.DTOs
{
    /// <summary>
    /// Core Layer: DTO for image generation requests
    /// </summary>
    public class GenerateImageRequest
    {
        public string BackgroundPrompt { get; set; } = string.Empty;
        
        [Required]
        public string MainText { get; set; } = string.Empty;
        
        public string SubText { get; set; } = string.Empty;
        
        public string Format { get; set; } = "Square";
    }
}
