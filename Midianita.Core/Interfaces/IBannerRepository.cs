namespace Midianita.Core.Interfaces
{
    /// <summary>
    /// Repository contract for Banner analysis records in DynamoDB.
    /// Used by the API to create initial records and check processing status.
    /// </summary>
    public interface IBannerRepository
    {
        /// <summary>
        /// Saves a new banner analysis record with initial metadata (Status = ANALYZING).
        /// </summary>
        Task SaveAsync(Guid bannerId, string originalImageKey);

        /// <summary>
        /// Retrieves the banner analysis record by ID.
        /// Returns null if not found.
        /// </summary>
        Task<BannerAnalysisRecord?> GetByIdAsync(Guid bannerId);
    }

    /// <summary>
    /// Read model returned by IBannerRepository.GetByIdAsync.
    /// Maps to DynamoDB attributes without depending on the Worker's internal models.
    /// </summary>
    public class BannerAnalysisRecord
    {
        public Guid BannerId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? OriginalImageKey { get; set; }
        public string? LayoutRulesV2Json { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
