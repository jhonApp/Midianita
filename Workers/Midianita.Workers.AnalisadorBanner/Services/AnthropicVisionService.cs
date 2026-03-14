using Amazon.Lambda.Core;
using Midianita.Workers.AnalisadorBanner.Models;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midianita.Workers.AnalisadorBanner.Services;

/// <summary>
/// Sends an image to Anthropic Claude 3.5 Sonnet and returns the
/// structured <see cref="BannerAnalysisResult"/> deserialized from the response.
/// </summary>
public sealed class AnthropicVisionService : IVisionApiService
{
    private const string AnthropicApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicModel = "claude-sonnet-4-6";
    private const string AnthropicApiKeyEnv = "ANTHROPIC_KEY";
    private const string AnthropicVersion = "2023-06-01";

    private const string SystemPrompt =
        "Act as an Elite Art Director and UI/UX Layout Engine. Analyze this reference banner and extract its exact structural blueprint. You MUST break down your analysis into these rigid categories: " +
        "1. LAYERS (Z-Index): Z-0 (Base background color/gradient), Z-1 (Massive geometric anchors like giant circles, ovals, or grids), Z-2 (Midground elements), Z-3 (Foreground subjects). " +
        "2. SUBJECT ANONYMIZATION (CRITICAL): DO NOT describe the specific identity, gender, clothing, hair, or accessories of the people in the reference image. Treat them as generic mannequins. You MUST ONLY describe their spatial properties: Pose (e.g., facing left, looking at camera, arms raised), Crop (e.g., waist-up, full body, headshot), and Color Grading (e.g., high-contrast black and white). Example: 'Foreground features a generic subject facing right, waist-up crop, holding a microphone, in black and white'. " +
        "3. TEXTURE & VIBE: Explicitly define the surface texture. Is it 'Heavy Film Grain/Noise', 'Grunge/Vintage', or 'Double Exposure'? Use 'crisp graphic vector art style' instead of 'clean and flat'. " +
        "4. SHAPES & GRAPHIC OVERLAYS: Explicitly mention background patterns (e.g., 'intersecting black grid lines', 'massive white wireframe globe', 'giant solid black ovals'). " +
        "5. TYPOGRAPHY ARCHITECTURE: Describe how the texts are positioned, rotated, stacked, and if they sit BEHIND the subjects or IN FRONT of them. " +
        "CRITICAL RULE FOR `masterPrompt`: This string goes to an AI image generator. IT MUST NOT CONTAIN TEXT OR WORDS. DO NOT say 'leave space for images'. DO NOT mention the typography. " +
        "TRANSLATION RULE: The `masterPrompt` must be written in natural, descriptive language optimized for an AI image generator. DO NOT use technical tags like 'Z-0', 'Z-1', or 'midground' in the final string. Use spatial relationships like 'In the deep background...', 'Directly behind the subjects...', 'In the foreground...'. " +
        "ATTENTION WEIGHTING: You MUST describe the complex background elements at the very BEGINNING of the prompt which is the most powerful attention anchor for the AI. Describe the human subjects after the background. " +
        "BACKGROUND DOMINANCE: The primary goal is to recreate the background architecture identically. Spend 80% of your descriptive effort detailing the exact colors, geometric anchors (circles, wireframes, grids), and textures of the elements in the environment. " +
        "NEGATIVE PROMPT INSTRUCTION: The end of the `masterPrompt` MUST include: 'DO NOT add text, letters, watermarks, starry night skies, cheap light flares, or random borders. Keep the composition strictly graphic and architectural.' " +
        "LAYOUT ANALYSIS: Output a JSON object matching this exact structure: { \"cutoutPlacement\": \"BottomCenter|BottomRight|BottomLeft|Center|TopCenter\", \"cutoutScalePercentage\": <integer 50-100>, \"textPlacement\": \"TopCenter|BottomCenter|LeftCenter|RightCenter|Split\", \"textAlign\": \"Left|Center|Right\" }. Base decisions strictly on the visual layout. " +
        "Return ONLY a valid JSON object matching this schema: " +
        "{ \"masterPrompt\": \"string\", \"colors\": [\"string\"], " +
        "\"typography\": \"string\", \"layoutRules\": { \"cutoutPlacement\": \"string\", \"cutoutScalePercentage\": 0, \"textPlacement\": \"string\", \"textAlign\": \"string\" }, " +
        "\"hasCutoutImages\": boolean, \"cutoutPlacement\": \"string\" }.";

    private readonly HttpClient _httpClient;

    public AnthropicVisionService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<BannerAnalysisResult> AnalyzeImageAsync(string base64Image, ILambdaLogger logger)
    {
        logger.LogInformation("[AnthropicVisionService] 🤖 Sending image to Claude 3.5 Sonnet...");

        var apiKey = Environment.GetEnvironmentVariable(AnthropicApiKeyEnv)
            ?? throw new InvalidOperationException($"Environment variable '{AnthropicApiKeyEnv}' is not set.");

        // Anthropic expects base64 without the data:image/jpeg;base64, prefix if present
        var base64Data = base64Image;
        var mediaType = "image/jpeg";
        if (base64Image.Contains(","))
        {
            var parts = base64Image.Split(',');
            base64Data = parts[1];
            mediaType = parts[0].Split(';')[0].Split(':')[1];
        }

        var requestBody = new
        {
            model = AnthropicModel,
            max_tokens = 1024,
            system = SystemPrompt,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = mediaType,
                                data = base64Data
                            }
                        },
                        new
                        {
                            type = "text",
                            text = "Analyze this church worship banner. Return ONLY the raw JSON object."
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);

        var response = await _httpClient.PostAsync(AnthropicApiUrl, httpContent);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogError($"[AnthropicVisionService] Anthropic API error {(int)response.StatusCode}: {errorBody}");
            throw new HttpRequestException($"Anthropic returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseJson);
        var content = document.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Anthropic returned empty content.");

        var rawJson = SanitizeJsonResponse(content);
        logger.LogInformation($"[AnthropicVisionService] Raw AI JSON: {rawJson}");

        return JsonSerializer.Deserialize<BannerAnalysisResult>(rawJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize BannerAnalysisResult.");
    }

    private static string SanitizeJsonResponse(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("```json", StringComparison.OrdinalIgnoreCase)) raw = raw[7..];
        else if (raw.StartsWith("```")) raw = raw[3..];
        if (raw.EndsWith("```")) raw = raw[..^3];
        return raw.Trim();
    }
}
