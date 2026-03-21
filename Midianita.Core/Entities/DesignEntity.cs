using System;

namespace Midianita.Core.Entities
{
    /// <summary>
    /// Domain Layer: Entity representing an image design
    /// </summary>
    public class DesignEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string UserId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = "Processing"; // e.g., "Processing", "Completed"
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
