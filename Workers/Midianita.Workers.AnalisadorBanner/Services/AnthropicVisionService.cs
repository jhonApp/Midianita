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
    private const string AnthropicModel = "claude-sonnet-4-6";
    private const string AnthropicApiKeyEnv = "ANTHROPIC_KEY";
    private const string AnthropicVersion = "2023-06-01";

    private const string SystemPrompt =
        "You are a precise design forensics engine. Your sole task is to analyze the provided banner image using computer vision and extract its visual layout as a perfectly structured JSON object.\n" +
        "CRITICAL RULES:\n" +
        "- Return ONLY the raw JSON object. No Markdown, no code fences, no explanations, no preamble.\n" +
        "- Every field is REQUIRED. If a value cannot be determined, use a sensible default.\n" +
        "- All YPosition and FontSize values must be calibrated to a reference canvas height of 1080 pixels.\n" +
        "- Color values must be hexadecimal strings, e.g. '#FF5733'.\n" +
        "- Scale is a float between 0.1 and 1.0, representing the cutout height relative to the canvas height.\n" +
        "- 'anchor' must be one of: bottom-center, bottom-right, bottom-left, center-right, center-left.\n" +
        "- 'tipo' must be one of: titulo, info, data, background.\n" +
        "- 'fontWeight' must be one of: regular, medium, semibold, bold, extrabold.\n" +
        "- 'alignment' must be one of: left, center, right.\n" +
        "ANALYSIS STEPS:\n" +
        "1. Identify background colors and visual elements (ONLY non-typographic elements: gradients, textures, particles, geometric shapes like lines, circles, globes).\n" +
        "2. Identify the main cutout person (if any) and calculate scale/anchor.\n" +
        "3. Extract ALL typography, including large decorative background text. If the original image has giant typographic elements in the background (e.g., a large 'DOMINGO' or 'WORSHIP' behind the person), extract them as a text element with tipo='background'. These will be rendered by our engine, NOT by the image generator.\n" +
        "4. Generate a 'masterPrompt' in English. This must be a highly detailed, descriptive prompt for an AI image generator to recreate ONLY the background atmosphere. Include dominant colors, lighting direction, textures, gradients, and abstract visual elements (particles, bokeh, lens flares, geometric shapes). ABSOLUTE PROHIBITION: Under NO circumstances should the masterPrompt include requests for typography, letters, text shapes, words, characters, glyphs, or any form of written content. If the original background has large typographic elements, IGNORE them completely in the masterPrompt — they will be composited separately by our SkiaSharp rendering engine. End the prompt with: 'Negative: no text, no letters, no typography, no words, no watermarks, no people, no faces.'\n" +
        "OUTPUT SCHEMA (return exactly this structure, no extra fields):\n" +
        "{ \"masterPrompt\": \"string\", \"background\": { \"coresDominantes\": [\"#hex\"], \"elementosVisuais\": [\"string\"] }, " +
        "\"pessoa\": { \"anchor\": \"bottom-center\", \"scale\": 0.75, \"offsetY\": 0, \"filters\": [] }, " +
        "\"textos\": [ { \"tipo\": \"titulo\", \"yPosition\": 120, \"fontSize\": 96, \"color\": \"#FFFFFF\", \"fontWeight\": \"extrabold\", \"alignment\": \"center\" } ] }";

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
            max_tokens = 2048,
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

        var svgJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // ── V1 deserialization: populates MasterPrompt, Colors, LayoutRules, etc. ──
        // The V2 schema is a strict subset that omits V1 fields, so we deserialize
        // twice from the same raw JSON string to populate both generations.
        var v1Result = JsonSerializer.Deserialize<BannerAnalysisResult>(SanitizeJsonResponse(content), svgJsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize V1 JSON from Claude response.");

        // ── V2 deserialization: populates Background, Pessoa, Textos[] ──
        LayoutRulesV2? v2Layout = null;
        try
        {
            v2Layout = JsonSerializer.Deserialize<LayoutRulesV2>(SanitizeJsonResponse(content), svgJsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning($"[AnthropicVisionService] LayoutRulesV2 deserialization failed (V1 still saved). Reason: {ex.Message}");
        }

        // Merge: return V1 record with V2 injected into the nullable field
        // Also copy the MasterPrompt to the top-level entity property so DynamoDB indexes it properly.
        return v1Result with 
        { 
            LayoutRulesV2 = v2Layout,
            MasterPrompt = !string.IsNullOrWhiteSpace(v2Layout?.MasterPrompt) ? v2Layout.MasterPrompt : v1Result.MasterPrompt
        };
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
