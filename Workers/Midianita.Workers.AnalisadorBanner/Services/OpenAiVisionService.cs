using Amazon.Lambda.Core;
using Midianita.Workers.AnalisadorBanner.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midianita.Workers.AnalisadorBanner.Services;

/// <summary>
/// Sends a Base64-encoded image to OpenAI GPT-4o Vision and returns the
/// structured <see cref="BannerAnalysisResult"/> deserialized from the response.
/// </summary>
public sealed class OpenAiVisionService : IVisionApiService
{
    private const string OpenAiApiUrl    = "https://api.openai.com/v1/chat/completions";
    private const string OpenAiModel     = "gpt-4o";
    private const string OpenAiApiKeyEnv = "OPENAI_API_KEY";

    private const string SystemPrompt =
        "You are an expert UI/UX and Graphic Design analyzer. " +
        "Analyze this church worship banner. Extract the visual hierarchy, color palette (HEX), " +
        "typography style, and write a 'Master Prompt' that could be used in an AI image generator " +
        "to recreate this vibe. " +
        "Detect if the banner uses isolated/cutout images of people or objects (images with backgrounds removed). " +
        "If yes, briefly describe their position and scale in the layout " +
        "(e.g., 'bottom-right, large scale', 'center, overlapping text'). " +
        "If no, return null for placement. " +
        "Return ONLY a valid JSON object matching this schema: " +
        "{ \"masterPrompt\": \"string\", \"colors\": [\"string\"], " +
        "\"typography\": \"string\", \"layoutRules\": \"string\", " +
        "\"hasCutoutImages\": boolean, \"cutoutPlacement\": \"string\" }.";

    private readonly HttpClient _httpClient;

    public OpenAiVisionService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BannerAnalysisResult> AnalyzeImageAsync(
        string base64Image, ILambdaLogger logger)
    {
        logger.LogInformation("[OpenAiVisionService] 🤖 Sending image to GPT-4o Vision...");

        var apiKey = Environment.GetEnvironmentVariable(OpenAiApiKeyEnv)
            ?? throw new InvalidOperationException(
                $"Environment variable '{OpenAiApiKeyEnv}' is not set.");

        var requestBody = new OpenAiRequest(
            Model: OpenAiModel,
            Messages: new List<OpenAiMessage>
            {
                new("system", SystemPrompt),
                new("user", new List<OpenAiContentPart>
                {
                    new(Type: "text",      Text: "Analyze this church worship banner."),
                    new(Type: "image_url", ImageUrl: new OpenAiImageUrl(Url: base64Image))
                })
            }
        );

        var jsonOptions  = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var json         = JsonSerializer.Serialize(requestBody, jsonOptions);
        var httpContent  = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        var httpResponse = await _httpClient.PostAsync(OpenAiApiUrl, httpContent);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync();
            logger.LogError(
                $"[OpenAiVisionService] OpenAI API error {(int)httpResponse.StatusCode}: {errorBody}");
            throw new HttpRequestException(
                $"OpenAI returned {(int)httpResponse.StatusCode}: {errorBody}");
        }

        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        logger.LogInformation("[OpenAiVisionService] ✅ OpenAI response received. Parsing...");

        var openAiResponse = JsonSerializer.Deserialize<OpenAiResponse>(responseJson)
            ?? throw new InvalidOperationException("Failed to deserialize OpenAI response.");

        var rawContent = openAiResponse.Choices[0].Message.Content?.ToString()
            ?? throw new InvalidOperationException("OpenAI returned an empty content field.");

        rawContent = SanitizeJsonResponse(rawContent);
        logger.LogInformation($"[OpenAiVisionService] Raw AI JSON: {rawContent}");

        var result = JsonSerializer.Deserialize<BannerAnalysisResult>(rawContent)
            ?? throw new InvalidOperationException("Failed to deserialize BannerAnalysisResult.");

        logger.LogInformation(
            $"[OpenAiVisionService] ✅ Deserialized. MasterPrompt length: {result.MasterPrompt.Length} chars.");

        return result;
    }

    /// <summary>
    /// Strips optional Markdown code fences that GPT sometimes wraps around JSON.
    /// </summary>
    private static string SanitizeJsonResponse(string raw)
    {
        raw = raw.Trim();

        if (raw.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            raw = raw[7..];
        else if (raw.StartsWith("```"))
            raw = raw[3..];

        if (raw.EndsWith("```"))
            raw = raw[..^3];

        return raw.Trim();
    }
}
