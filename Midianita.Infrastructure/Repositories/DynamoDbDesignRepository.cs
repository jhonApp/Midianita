using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;

namespace Midianita.Infrastructure.Repositories
{
    public class DynamoDbDesignRepository : IDesignRepository
    {
        private readonly DynamoDBContext _context;
        private readonly string _tableName;

        public DynamoDbDesignRepository(IAmazonDynamoDB dynamoDbClient, string tableName = "Midianita_Dev_Designs")
        {
            _context = new DynamoDBContext(dynamoDbClient);
            _tableName = tableName;
        }

        public async Task CreateAsync(Design design)
        {
            await _context.SaveAsync(design, new DynamoDBOperationConfig { OverrideTableName = _tableName });
        }

        public async Task<Design?> GetByIdAsync(string id)
        {
            return await _context.LoadAsync<Design>(id, new DynamoDBOperationConfig { OverrideTableName = _tableName });
        }

        public async Task<IEnumerable<Design>> GetAllAsync()
        {
            // Note: Scan is not efficient for large tables, but acceptable for this scope/initial implementation.
            return await _context.ScanAsync<Design>(new List<ScanCondition>(), new DynamoDBOperationConfig { OverrideTableName = _tableName }).GetRemainingAsync();
        }

        public async Task UpdateAsync(Design design)
        {
            await _context.SaveAsync(design, new DynamoDBOperationConfig { OverrideTableName = _tableName });
        }

        public async Task DeleteAsync(string id)
        {
            await _context.DeleteAsync<Design>(id, new DynamoDBOperationConfig { OverrideTableName = _tableName });
        }
    }
}
