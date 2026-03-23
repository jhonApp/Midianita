namespace Midianita.Core.DTOs
{
    public class ImageGenerationJob
    {
        public Guid JobId { get; set; }

        public Guid BannerId { get; set; }

        public string MainText { get; set; }
        public string SubText { get; set; }
        public string Format { get; set; }
        public string ReferenceImageUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
