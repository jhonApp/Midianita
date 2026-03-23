using System.ComponentModel.DataAnnotations;

namespace Midianita.Core.DTOs
{
    public class GenerateImageRequest
    {
        [Required]
        public Guid BannerId { get; set; }

        [Required]
        [MaxLength(100)]
        public string MainText { get; set; }

        [MaxLength(200)]
        public string SubText { get; set; }

        public string Format { get; set; } = "Square";
        public string ReferenceImageUrl { get; set; }
    }
}
