using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Generic;

namespace Midianita.Core.Entities
{
    public class User
    {
        [DynamoDBHashKey("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>();
        public List<string> Roles { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
