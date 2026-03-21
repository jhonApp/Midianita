using Amazon.Lambda.Core;
using Midianita.Workers.AnalisadorBanner.Models;
using Polly;
using Polly.Retry;
using System.Diagnostics;
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
    private const string AnthropicModel = "claude-3-5-sonnet-20241022"; 
    private const string AnthropicApiKeyEnv = "ANTHROPIC_KEY";
    private const string AnthropicVersion = "2023-06-01";

    private const string SystemPrompt =
        "Act as an Elite Art Director and UI/UX Layout Engine. Analyze this reference banner and extract its exact structural blueprint. You MUST break down your analysis into these rigid categories: " +
        "1. LAYERS (Z-Index): Z-0 (Base background color/gradient), Z-1 (Massive geometric anchors like giant circles, ovals, or grids), Z-2 (Midground elements), Z-3 (Foreground subjects). " +
        "2. SUBJECT ANONYMIZATION (CRITICAL): DO NOT describe the specific identity of the people. " +
        "Return ONLY a valid JSON object matching this schema: " +
        "{ \"masterPrompt\": \"string\", \"colors\": [\"string\"], " +
        "\"typography\": \"string\", \"layoutRules\": { \"cutoutPlacement\": \"string\", \"cutoutScalePercentage\": 0, \"textPlacement\": \"string\", \"textAlign\": \"string\" }, " +
        "\"hasCutoutImages\": boolean, \"cutoutPlacement\": \"string\" }.";

    private readonly HttpClient _httpClient;
    private readonly ITelemetryService _telemetry;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public AnthropicVisionService(HttpClient httpClient, ITelemetryService telemetry)
    {
        _httpClient = httpClient;
        _telemetry = telemetry;

        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => (int)r.StatusCode == 429 || (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<BannerAnalysisResult> AnalyzeImageAsync(string base64Image, ILambdaLogger logger, string requestId)
    {
        logger.LogInformation($"[AnthropicVisionService] 🤖 Sending image to Claude (RequestID: {requestId})...");

        var apiKey = Environment.GetEnvironmentVariable(AnthropicApiKeyEnv)
            ?? throw new InvalidOperationException($"Environment variable '{AnthropicApiKeyEnv}' is not set.");

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
            messages = new[] {
                new { role = "user", content = new object[] {
                    new { type = "image", source = new { type = "base64", media_type = mediaType, data = base64Data } },
                    new { type = "text", text = "Analyze this worship banner. Return raw JSON." }
                } }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("anthropic-version", AnthropicVersion);

        var sw = Stopwatch.StartNew();
        var response = await _retryPolicy.ExecuteAsync(() => _httpClient.PostAsync(AnthropicApiUrl, httpContent));
        sw.Stop();

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Anthropic returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(responseJson);
        
        int inputTokens = 0, outputTokens = 0;
        if (document.RootElement.TryGetProperty("usage", out var usage))
        {
            inputTokens = usage.GetProperty("input_tokens").GetInt32();
            outputTokens = usage.GetProperty("output_tokens").GetInt32();
        }

        _telemetry.LogUsage(requestId, AnthropicModel, inputTokens, outputTokens, sw.ElapsedMilliseconds);

        var content = document.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Anthropic returned empty content.");

        return JsonSerializer.Deserialize<BannerAnalysisResult>(SanitizeJsonResponse(content), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize JSON.");
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
