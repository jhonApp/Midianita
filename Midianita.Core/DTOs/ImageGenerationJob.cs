namespace Midianita.Core.DTOs
{
    public class ImageGenerationJob
    {
        public Guid JobId { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
