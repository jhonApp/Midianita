using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Models;

/// <summary>
/// Design metadata produced by the AnalisadorBanner worker and stored in DynamoDB.
/// </summary>
public record BannerAnalysisResult(
    [property: JsonPropertyName("masterPrompt")]    string MasterPrompt,
    [property: JsonPropertyName("colors")]          List<string> Colors,
    [property: JsonPropertyName("typography")]      string Typography,
    [property: JsonPropertyName("layoutRules")]     LayoutRules LayoutRules,
    [property: JsonPropertyName("hasCutoutImages")] bool HasCutoutImages,
    [property: JsonPropertyName("cutoutPlacement")] string? CutoutPlacement
);

public record LayoutRules(
    [property: JsonPropertyName("cutoutPlacement")]       string CutoutPlacement,
    [property: JsonPropertyName("cutoutScalePercentage")] int CutoutScalePercentage,
    [property: JsonPropertyName("textPlacement")]         string TextPlacement,
    [property: JsonPropertyName("textAlign")]             string TextAlign
);
