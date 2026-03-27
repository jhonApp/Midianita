using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Midianita.Workers.ProcessadorArte.Models;
using System.Text.Json;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Reads banner metadata and updates job status records in DynamoDB.
/// </summary>
public sealed class DynamoDbJobRepository : IDynamoDbJobRepository
{
    private readonly string _bannerTable = Environment.GetEnvironmentVariable("DYNAMODB_BANNER_TABLE") ?? "Midianita_Dev_Banner";
    
    private readonly string _jobTable = Environment.GetEnvironmentVariable("DYNAMODB_JOB_TABLE") ?? "Midianita_Dev_Job";

    private readonly IAmazonDynamoDB _dynamo;

    public DynamoDbJobRepository(IAmazonDynamoDB dynamo) => _dynamo = dynamo;

    public async Task<BannerAnalysisResult> GetBannerMetadataAsync(string bannerId, ILambdaLogger logger)
    {
        logger.LogInformation($"[DynamoDbJobRepository] Fetching banner metadata for BannerId: {bannerId}");

        var response = await _dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = _bannerTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["BannerId"] = new AttributeValue { S = bannerId }
            }
        });

        if (response.Item is null || response.Item.Count == 0)
            throw new InvalidOperationException($"Banner '{bannerId}' not found in DynamoDB.");

        var item = response.Item;

        var lr = new LayoutRules(
            CutoutPlacement:       "",
            CutoutScalePercentage: 0,
            TextPlacement:         "",
            TextAlign:             ""
        );

        if (item.TryGetValue("LayoutRules", out var lrMap) && lrMap.M != null)
        {
            lr = new LayoutRules(
                CutoutPlacement:       lrMap.M.TryGetValue("CutoutPlacement",       out var cpVal) ? cpVal.S : "",
                CutoutScalePercentage: lrMap.M.TryGetValue("CutoutScalePercentage", out var spVal) && int.TryParse(spVal.N, out var scale) ? scale : 0,
                TextPlacement:         lrMap.M.TryGetValue("TextPlacement",         out var tpVal) ? tpVal.S : "",
                TextAlign:             lrMap.M.TryGetValue("TextAlign",             out var taVal) ? taVal.S : ""
            );
        }

        var result = new BannerAnalysisResult(
            MasterPrompt:    item.TryGetValue("MasterPrompt",    out var mp)  ? mp.S  : string.Empty,
            Colors:          item.TryGetValue("Colors",          out var col) ? col.SS : new List<string>(),
            Typography:      item.TryGetValue("Typography",      out var ty)  ? ty.S  : string.Empty,
            LayoutRules:     lr,
            HasCutoutImages: item.TryGetValue("HasCutoutImages", out var hc)  && hc.BOOL,
            CutoutPlacement: item.TryGetValue("CutoutPlacement", out var cp) && !string.IsNullOrEmpty(cp.S) ? cp.S : null
        );

        logger.LogInformation($"[DynamoDbJobRepository] ✅ Banner metadata fetched. HasCutout: {result.HasCutoutImages}");
        return result;
    }

    public async Task UpdateJobStatusAsync(string jobId, string status, ILambdaLogger logger, string? finalUrl = null)
    {
        logger.LogInformation($"[DynamoDbJobRepository] Updating job {jobId} → Status: {status}");

        var item = new Dictionary<string, AttributeValue>
        {
            ["JobId"]     = new AttributeValue { S = jobId },
            ["Status"]    = new AttributeValue { S = status },
            ["UpdatedAt"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") }
        };

        if (!string.IsNullOrEmpty(finalUrl))
            item["FinalS3Url"] = new AttributeValue { S = finalUrl };

        await _dynamo.PutItemAsync(new PutItemRequest
        {
            TableName = _jobTable,
            Item      = item
        });

        logger.LogInformation($"[DynamoDbJobRepository] ✅ Job {jobId} status updated to {status}");
    }

    public async Task<BannerFullRecord> GetBannerFullRecordAsync(string bannerId, ILambdaLogger logger)
    {
        logger.LogInformation($"[DynamoDbJobRepository] 📦 Fetching full banner record for BannerId: {bannerId}");

        var response = await _dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = _bannerTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["BannerId"] = new AttributeValue { S = bannerId }
            }
        });

        if (response.Item is null || response.Item.Count == 0)
            throw new InvalidOperationException($"Banner '{bannerId}' not found in DynamoDB.");

        var item = response.Item;

        // ── MasterPrompt ──────────────────────────────────────────────────
        var masterPrompt = item.TryGetValue("MasterPrompt", out var mpVal) ? mpVal.S : string.Empty;

        // ── OriginalImageKey ──────────────────────────────────────────────
        var originalImageKey = item.TryGetValue("OriginalImageKey", out var oikVal) ? oikVal.S : string.Empty;

        // ── HasCutoutImages ───────────────────────────────────────────────
        var hasCutout = item.TryGetValue("HasCutoutImages", out var hcVal) && hcVal.BOOL;

        // ── LayoutRulesV2 (DynamoDB Map → JSON → record) ─────────────────
        LayoutRulesV2? layoutV2 = null;
        if (item.TryGetValue("LayoutRulesV2", out var v2Attr))
        {
            // If stored as a JSON string (S), deserialize directly
            if (!string.IsNullOrEmpty(v2Attr.S))
            {
                layoutV2 = JsonSerializer.Deserialize<LayoutRulesV2>(v2Attr.S, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            // If stored as a DynamoDB Map (M), serialize the Document to JSON first
            else if (v2Attr.M is { Count: > 0 })
            {
                var doc = Amazon.DynamoDBv2.DocumentModel.Document.FromAttributeMap(v2Attr.M);
                layoutV2 = JsonSerializer.Deserialize<LayoutRulesV2>(doc.ToJson(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }

        // ── LayoutRules V1 (legacy fallback) ─────────────────────────────
        LayoutRules? lr = null;
        if (item.TryGetValue("LayoutRules", out var lrMap) && lrMap.M != null)
        {
            lr = new LayoutRules(
                CutoutPlacement:       lrMap.M.TryGetValue("CutoutPlacement",       out var cpVal) ? cpVal.S : "",
                CutoutScalePercentage: lrMap.M.TryGetValue("CutoutScalePercentage", out var spVal) && int.TryParse(spVal.N, out var scale) ? scale : 0,
                TextPlacement:         lrMap.M.TryGetValue("TextPlacement",         out var tpVal) ? tpVal.S : "",
                TextAlign:             lrMap.M.TryGetValue("TextAlign",             out var taVal) ? taVal.S : ""
            );
        }

        logger.LogInformation($"[DynamoDbJobRepository] ✅ Full record fetched. HasV2: {layoutV2 is not null}, HasCutout: {hasCutout}");

        return new BannerFullRecord(
            MasterPrompt:    masterPrompt,
            OriginalImageKey: originalImageKey,
            LayoutRulesV2:   layoutV2,
            LayoutRules:     lr,
            HasCutoutImages: hasCutout
        );
    }
}
