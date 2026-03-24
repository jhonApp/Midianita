using System.Text.Json.Serialization;

namespace Midianita.Workers.AnalisadorBanner.Models;

/// <summary>
/// Result deserialized from the vision model JSON response.
/// Both V1 (LayoutRules) and V2 (LayoutRulesV2) fields coexist to allow a
/// gradual migration: AnalisadorBanner can start populating LayoutRulesV2
/// while ProcessadorArte still reads LayoutRules — zero breaking changes.
/// </summary>
public record BannerAnalysisResult(
    [property: JsonPropertyName("masterPrompt")]    string MasterPrompt,
    [property: JsonPropertyName("colors")]          List<string> Colors,
    [property: JsonPropertyName("typography")]      string Typography,

    // ── V1 layout (legacy) — kept alive for ProcessadorArte compatibility ──
    [property: JsonPropertyName("layoutRules")]     LayoutRules LayoutRules,
    [property: JsonPropertyName("hasCutoutImages")] bool HasCutoutImages,
    [property: JsonPropertyName("cutoutPlacement")] string? CutoutPlacement,

    // ── V2 layout (new) — null when AnalisadorBanner still uses V1 prompt ──
    [property: JsonPropertyName("layoutRulesV2")]   LayoutRulesV2? LayoutRulesV2 = null
);

public record LayoutRules(
    [property: JsonPropertyName("cutoutPlacement")]       string CutoutPlacement,
    [property: JsonPropertyName("cutoutScalePercentage")] int CutoutScalePercentage,
    [property: JsonPropertyName("textPlacement")]         string TextPlacement,
    [property: JsonPropertyName("textAlign")]             string TextAlign
);
