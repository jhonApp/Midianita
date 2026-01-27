using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;

namespace Midianita.Infrastructure.Repositories
{
    public class DynamoDbDesignRepository : IDesignRepository
    {
        private readonly DynamoDBContext _context;

        public DynamoDbDesignRepository(IAmazonDynamoDB dynamoDbClient)
        {
            _context = new DynamoDBContext(dynamoDbClient);
        }

        public async Task CreateAsync(Design design)
        {
            await _context.SaveAsync(design);
        }

        public async Task<Design?> GetByIdAsync(string id)
        {
            return await _context.LoadAsync<Design>(id);
        }

        public async Task<IEnumerable<Design>> GetAllAsync()
        {
            // Note: Scan is not efficient for large tables, but acceptable for this scope/initial implementation.
            return await _context.ScanAsync<Design>(new List<ScanCondition>()).GetRemainingAsync();
        }

        public async Task UpdateAsync(Design design)
        {
            await _context.SaveAsync(design);
        }

        public async Task DeleteAsync(string id)
        {
            await _context.DeleteAsync<Design>(id);
        }
    }
}
