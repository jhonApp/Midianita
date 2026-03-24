using System.ComponentModel.DataAnnotations;

namespace Midianita.Core.DTOs
{
    /// <summary>
    /// Request DTO for the banner analysis endpoint.
    /// The front-end sends the URL of the reference image to be analyzed by Claude Sonnet.
    /// </summary>
    public class AnalyzeBannerRequest
    {
        [Required(ErrorMessage = "ReferenceImageUrl is required.")]
        public string ReferenceImageUrl { get; set; } = string.Empty;
    }
}
