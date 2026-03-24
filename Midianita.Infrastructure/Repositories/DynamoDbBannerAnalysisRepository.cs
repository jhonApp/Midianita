using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Midianita.Core.Interfaces;

namespace Midianita.Infrastructure.Repositories
{
    public class DynamoDbBannerAnalysisRepository : IBannerRepository
    {
        private readonly IAmazonDynamoDB _dynamoDb;
        private readonly string _tableName;

        public DynamoDbBannerAnalysisRepository(IAmazonDynamoDB dynamoDb, string tableName)
        {
            _dynamoDb = dynamoDb;
            _tableName = tableName;
        }

        public async Task SaveAsync(Guid bannerId, string originalImageKey)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["BannerId"] = new AttributeValue { S = bannerId.ToString() },
                ["OriginalImageKey"] = new AttributeValue { S = originalImageKey },
                ["Status"] = new AttributeValue { S = "ANALYZING" },
                ["CreatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") }
            };

            var request = new PutItemRequest
            {
                TableName = _tableName,
                Item = item
            };

            await _dynamoDb.PutItemAsync(request);
        }

        public async Task<BannerAnalysisRecord?> GetByIdAsync(Guid bannerId)
        {
            var request = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["BannerId"] = new AttributeValue { S = bannerId.ToString() }
                }
            };

            var response = await _dynamoDb.GetItemAsync(request);

            if (response.Item == null || response.Item.Count == 0)
                return null;

            response.Item.TryGetValue("Status", out var statusValue);
            response.Item.TryGetValue("OriginalImageKey", out var originalImageKeyValue);
            response.Item.TryGetValue("LayoutRulesV2", out var layoutRulesV2Value);
            response.Item.TryGetValue("CreatedAt", out var createdAtValue);

            return new BannerAnalysisRecord
            {
                BannerId = bannerId,
                Status = statusValue?.S ?? "UNKNOWN",
                OriginalImageKey = originalImageKeyValue?.S,
                LayoutRulesV2Json = layoutRulesV2Value?.S,
                CreatedAt = createdAtValue?.S != null ? DateTime.Parse(createdAtValue.S) : default
            };
        }
    }
}
