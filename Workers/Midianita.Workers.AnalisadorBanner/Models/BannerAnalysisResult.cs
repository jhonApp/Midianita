using System.Text.Json.Serialization;

namespace Midianita.Workers.AnalisadorBanner.Models;

/// <summary>Result deserialized from OpenAI's JSON response.</summary>
public record BannerAnalysisResult(
    [property: JsonPropertyName("masterPrompt")]    string MasterPrompt,
    [property: JsonPropertyName("colors")]          List<string> Colors,
    [property: JsonPropertyName("typography")]      string Typography,
    [property: JsonPropertyName("layoutRules")]     string LayoutRules,
    [property: JsonPropertyName("hasCutoutImages")] bool HasCutoutImages,
    [property: JsonPropertyName("cutoutPlacement")] string? CutoutPlacement
);
