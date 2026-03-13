using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Models;

/// <summary>
/// Design metadata produced by the AnalisadorBanner worker and stored in DynamoDB.
/// </summary>
public record BannerAnalysisResult(
    [property: JsonPropertyName("masterPrompt")]    string MasterPrompt,
    [property: JsonPropertyName("colors")]          List<string> Colors,
    [property: JsonPropertyName("typography")]      string Typography,
    [property: JsonPropertyName("layoutRules")]     string LayoutRules,
    [property: JsonPropertyName("hasCutoutImages")] bool HasCutoutImages,
    [property: JsonPropertyName("cutoutPlacement")] string? CutoutPlacement
);
