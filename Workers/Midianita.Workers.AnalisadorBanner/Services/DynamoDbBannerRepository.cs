using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Midianita.Workers.AnalisadorBanner.Models;
using System.Text.Json;

namespace Midianita.Workers.AnalisadorBanner.Services;

/// <summary>
/// Persists banner analysis results to the <c>Midianita_Dev_Banner</c> DynamoDB table.
/// </summary>
public sealed class DynamoDbBannerRepository : IBannerRepository
{
    private const string TableName = "Midianita_Dev_Banner";

    private readonly IAmazonDynamoDB _dynamoClient;

    public DynamoDbBannerRepository(IAmazonDynamoDB dynamoClient)
    {
        _dynamoClient = dynamoClient;
    }

    public async Task SaveAsync(
        string objectKey, int width, int height, BannerAnalysisResult result, ILambdaLogger logger)
    {
        var itemId = Guid.NewGuid().ToString();

        logger.LogInformation(
            $"[DynamoDbBannerRepository] 💾 Saving item {itemId} to {TableName}");

        var item = new Dictionary<string, AttributeValue>
        {
            ["BannerId"]         = new AttributeValue { S    = itemId },
            ["OriginalImageKey"] = new AttributeValue { S    = objectKey },
            ["MasterPrompt"]     = new AttributeValue { S    = result.MasterPrompt },
            ["Colors"]           = new AttributeValue { SS   = result.Colors },
            ["Typography"]       = new AttributeValue { S    = result.Typography },
            ["LayoutRules"]      = new AttributeValue { 
                M = new Dictionary<string, AttributeValue>
                {
                    ["CutoutPlacement"]       = new AttributeValue { S = result.LayoutRules.CutoutPlacement },
                    ["CutoutScalePercentage"] = new AttributeValue { N = result.LayoutRules.CutoutScalePercentage.ToString() },
                    ["TextPlacement"]         = new AttributeValue { S = result.LayoutRules.TextPlacement },
                    ["TextAlign"]             = new AttributeValue { S = result.LayoutRules.TextAlign }
                }
            },
            ["Width"]            = new AttributeValue { N    = width.ToString() },
            ["Height"]           = new AttributeValue { N    = height.ToString() },
            ["HasCutoutImages"]  = new AttributeValue { BOOL = result.HasCutoutImages },
            ["CreatedAt"]        = new AttributeValue { S    = DateTime.UtcNow.ToString("O") },
            ["Status"]           = new AttributeValue { S    = "ANALYZED" }
        };

        // Omit CutoutPlacement entirely when null/empty to avoid DynamoDB empty-string errors
        if (!string.IsNullOrEmpty(result.CutoutPlacement))
            item["CutoutPlacement"] = new AttributeValue { S = result.CutoutPlacement };

        // ── V2 layout: persisted as a JSON string for easy forward-compatibility ──
        // ProcessadorArte reads this field independently; null means Claude used V1 schema.
        if (result.LayoutRulesV2 is not null)
        {
            item["LayoutRulesV2"] = new AttributeValue
            {
                S = JsonSerializer.Serialize(result.LayoutRulesV2)
            };
        }

        await _dynamoClient.PutItemAsync(new PutItemRequest
        {
            TableName = TableName,
            Item      = item
        });

        logger.LogInformation(
            $"[DynamoDbBannerRepository] ✅ Item saved. BannerId: {itemId}");
    }
}
