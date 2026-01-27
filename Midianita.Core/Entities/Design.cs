using Amazon.DynamoDBv2.DataModel;
using System;

namespace Midianita.Core.Entities
{
    [DynamoDBTable("Midianita_Dev_Designs")]
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
