using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Midianita.Infrastructure.Repositories
{
    public class DynamoDbRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IDynamoDBContext _context;
        private readonly DynamoDBOperationConfig _config;

        public DynamoDbRefreshTokenRepository(IDynamoDBContext context, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _context = context;
            var tableName = configuration["DynamoDb:RefreshToken"] ?? "RefreshToken_Midianita";
            _config = new DynamoDBOperationConfig { OverrideTableName = tableName };
        }

        public async Task<RefreshToken?> GetByTokenAsync(string token)
        {
            return await _context.LoadAsync<RefreshToken>(token, _config);
        }

        public async Task<IEnumerable<RefreshToken>> GetByUserIdAsync(string userId)
        {
            // Assuming we have a GSI on UserId for efficient lookup
            // Or using Scan (inefficient but works for small scale)
            var conditions = new List<ScanCondition>
            {
                new ScanCondition("UserId", ScanOperator.Equal, userId)
            };
            var search = _context.ScanAsync<RefreshToken>(conditions, _config);
            return await search.GetRemainingAsync();
        }

        public async Task RevokeAllForUserAsync(string userId)
        {
            var tokens = await GetByUserIdAsync(userId);
            foreach (var token in tokens)
            {
                token.IsInvalidated = true;
                await _context.SaveAsync(token, _config);
            }
        }

        public async Task SaveAsync(RefreshToken token)
        {
            await _context.SaveAsync(token, _config);
        }
    }
}
