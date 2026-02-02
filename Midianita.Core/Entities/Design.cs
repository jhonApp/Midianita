using System;

namespace Midianita.Core.Entities
{
    public class Design
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string GeneratedText { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string? CanvasData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
