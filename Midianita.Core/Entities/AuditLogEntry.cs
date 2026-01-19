using System;

namespace Midianita.Core.Entities
{
    public class AuditLogEntry
    {
        public string LogId { get; set; } = Guid.NewGuid().ToString();
        public string Action { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
