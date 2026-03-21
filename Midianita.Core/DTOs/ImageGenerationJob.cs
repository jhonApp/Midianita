using System;

namespace Midianita.Core.DTOs
{
    /// <summary>
    /// Core Layer: DTO representing a job for image generation
    /// </summary>
    public class ImageGenerationJob
    {
        public Guid JobId { get; set; }
        public string FullPrompt { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
