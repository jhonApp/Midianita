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

    public async Task<string?> GetOriginalImageKeyAsync(string bannerId, ILambdaLogger logger)
    {
        var response = await _dynamoClient.GetItemAsync(TableName, new Dictionary<string, AttributeValue>
        {
            { "BannerId", new AttributeValue { S = bannerId } }
        });

        if (response.Item != null && response.Item.TryGetValue("OriginalImageKey", out var keyAttr))
        {
            return keyAttr.S;
        }
        return null;
    }

    public async Task SaveAsync(
        string bannerId,
        string originalImageKey,
        int width,
        int height,
        BannerAnalysisResult result,
        ILambdaLogger logger)
    {
        logger.LogInformation(
            $"[DynamoDbBannerRepository] 💾 Updating item {bannerId} in {TableName}");

        var item = new Dictionary<string, AttributeValue>
        {
            ["BannerId"]         = new AttributeValue { S    = bannerId },
            ["OriginalImageKey"] = new AttributeValue { S    = originalImageKey },
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
            ["Status"]           = new AttributeValue { S    = "ANALYZED" } // Changed from COMPLETED to ANALYZED as per original 
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
            $"[DynamoDbBannerRepository] ✅ Item updated. BannerId: {bannerId}");
    }
}
