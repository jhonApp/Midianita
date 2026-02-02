using Amazon.DynamoDBv2.DataModel;
using System;

namespace Midianita.Core.Entities
{
    public class RefreshToken
    {
        // Token is the Hash Key in DynamoDB (Column Name: "Token")
        [DynamoDBHashKey("Token")]
        public string Token { get; set; } = string.Empty;
        
        public string UserId { get; set; } = string.Empty;
        
        public bool IsUsed { get; set; } = false;
        
        public bool IsInvalidated { get; set; } = false;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // TTL Attribute for DynamoDB (Epoch Timestamp)
        public long ExpiresAtEpoch { get; set; }
    }
}
