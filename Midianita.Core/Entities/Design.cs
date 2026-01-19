using System;

namespace Midianita.Core.Entities
{
    public class Design
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string? CanvasData { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Category { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
