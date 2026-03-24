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
            ["BannerId"]         = new AttributeValue { S = bannerId ?? Guid.NewGuid().ToString() },
            ["OriginalImageKey"] = new AttributeValue { S = originalImageKey ?? string.Empty },
            ["MasterPrompt"]     = new AttributeValue { S = result?.MasterPrompt ?? string.Empty },
            ["Typography"]       = new AttributeValue { S = result?.Typography ?? string.Empty },
            ["HasCutoutImages"]  = new AttributeValue { BOOL = result?.HasCutoutImages ?? false },
            ["CreatedAt"]        = new AttributeValue { S = DateTime.UtcNow.ToString("O") },
            ["Status"]           = new AttributeValue { S = "ANALYZED" }
        };

        // SS (String Set) cannot be empty or null in DynamoDB.
        if (result?.Colors != null && result.Colors.Any())
        {
            item["Colors"] = new AttributeValue { SS = result.Colors };
        }

        // V1 LayoutRules check
        if (result?.LayoutRules != null)
        {
            item["LayoutRules"] = new AttributeValue { 
                M = new Dictionary<string, AttributeValue>
                {
                    ["CutoutPlacement"]       = new AttributeValue { S = result.LayoutRules.CutoutPlacement ?? string.Empty },
                    ["CutoutScalePercentage"] = new AttributeValue { N = result.LayoutRules.CutoutScalePercentage.ToString() },
                    ["TextPlacement"]         = new AttributeValue { S = result.LayoutRules.TextPlacement ?? string.Empty },
                    ["TextAlign"]             = new AttributeValue { S = result.LayoutRules.TextAlign ?? string.Empty }
                }
            };
        }

        // Width and Height might be passed separately
        item["Width"]  = new AttributeValue { N = width.ToString() };
        item["Height"] = new AttributeValue { N = height.ToString() };

        // Omit CutoutPlacement entirely when null/empty to avoid DynamoDB empty-string errors
        if (!string.IsNullOrEmpty(result?.CutoutPlacement))
            item["CutoutPlacement"] = new AttributeValue { S = result.CutoutPlacement };

        // ── V2 layout: Map explicitly handling nested nulls ──
        if (result?.LayoutRulesV2 != null)
        {
            var backgroundMap = new Dictionary<string, AttributeValue>();
            
            var cores = result.LayoutRulesV2.Background?.CoresDominantes ?? new List<string>();
            if (cores.Any()) backgroundMap["coresDominantes"] = new AttributeValue { SS = cores };
            
            var elementos = result.LayoutRulesV2.Background?.ElementosVisuais ?? new List<string>();
            if (elementos.Any()) backgroundMap["elementosVisuais"] = new AttributeValue { SS = elementos };

            var pessoaMap = new Dictionary<string, AttributeValue>
            {
                ["anchor"]  = new AttributeValue { S = result.LayoutRulesV2.Pessoa?.Anchor ?? "bottom-center" },
                ["scale"]   = new AttributeValue { N = (result.LayoutRulesV2.Pessoa?.Scale ?? 0.75f).ToString(System.Globalization.CultureInfo.InvariantCulture) },
                ["offsetY"] = new AttributeValue { N = (result.LayoutRulesV2.Pessoa?.OffsetY ?? 0).ToString() }
            };

            var pessoaFilters = result.LayoutRulesV2.Pessoa?.Filters ?? new List<string>();
            if (pessoaFilters.Any()) pessoaMap["filters"] = new AttributeValue { SS = pessoaFilters };

            var textosList = new List<AttributeValue>();
            foreach (var texto in result.LayoutRulesV2.Textos ?? new List<TextElement>())
            {
                textosList.Add(new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["tipo"]       = new AttributeValue { S = texto?.Tipo ?? "info" },
                        ["yPosition"]  = new AttributeValue { N = (texto?.YPosition ?? 0).ToString() },
                        ["fontSize"]   = new AttributeValue { N = (texto?.FontSize ?? 24).ToString() },
                        ["color"]      = new AttributeValue { S = texto?.Color ?? "#FFFFFF" },
                        ["fontWeight"] = new AttributeValue { S = texto?.FontWeight ?? "regular" },
                        ["alignment"]  = new AttributeValue { S = texto?.Alignment ?? "center" }
                    }
                });
            }

            item["LayoutRulesV2"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["background"] = new AttributeValue { M = backgroundMap },
                    ["pessoa"]     = new AttributeValue { M = pessoaMap },
                    ["textos"]     = new AttributeValue { L = textosList }
                }
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
