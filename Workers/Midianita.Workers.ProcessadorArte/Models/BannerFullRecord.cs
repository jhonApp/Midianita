using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Models;

/// <summary>
/// Aggregated record returned by the repository containing everything
/// the ProcessadorArte pipeline needs to compose the final banner.
/// </summary>
public record BannerFullRecord(
    [property: JsonPropertyName("masterPrompt")]    string          MasterPrompt,
    [property: JsonPropertyName("originalImageKey")] string         OriginalImageKey,
    [property: JsonPropertyName("layoutRulesV2")]   LayoutRulesV2?  LayoutRulesV2,
    [property: JsonPropertyName("layoutRules")]     LayoutRules?    LayoutRules,
    [property: JsonPropertyName("hasCutoutImages")] bool            HasCutoutImages
);
