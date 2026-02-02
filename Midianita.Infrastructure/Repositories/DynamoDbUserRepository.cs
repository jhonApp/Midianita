using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Midianita.Infrastructure.Repositories
{
    public class DynamoDbUserRepository : IUserRepository
    {
        private readonly IDynamoDBContext _context;
        private readonly DynamoDBOperationConfig _config;

        public DynamoDbUserRepository(IDynamoDBContext context, Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _context = context;
            var tableName = configuration["DynamoDb:User"] ?? "User_Midianita";
            _config = new DynamoDBOperationConfig { OverrideTableName = tableName };
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            // Scan with config
            var conditions = new List<ScanCondition>
            {
                new ScanCondition("Email", ScanOperator.Equal, email)
            };

            var search = _context.ScanAsync<User>(conditions, _config);
            var results = await search.GetRemainingAsync();
            return results.Count > 0 ? results[0] : null;
        }

        public async Task<User?> GetByIdAsync(string id)
        {
            return await _context.LoadAsync<User>(id, _config);
        }

        public async Task SaveAsync(User user)
        {
            await _context.SaveAsync(user, _config);
        }
    }
}
