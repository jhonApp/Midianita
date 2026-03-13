using Amazon.Lambda.Core;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf.WellKnownTypes;
using System.Text;
using System.Text.Json;
using ProtobufValue = Google.Protobuf.WellKnownTypes.Value;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Orchestrates calls to:
///  - Google Cloud Vertex AI Imagen 3 (background generation)
///  - RMBG-1.4 via remove.bg (background removal)
/// </summary>
public sealed class AiOrchestratorService : IAiOrchestratorService
{
    // ── Vertex AI (Imagen 3) ──────────────────────────────────────────────────
    private const string ImagenModel    = "imagen-4.0-ultra-generate-001";
    private const string ProjectIdEnv   = "GOOGLE_PROJECT_ID";
    private const string LocationEnv    = "GOOGLE_LOCATION";

    // ── RMBG-1.4 (remove.bg) ─────────────────────────────────────────────────
    private const string RmbgApiKeyEnv  = "RMBG_API_KEY";
    private const string RmbgApiUrl     = "https://api.remove.bg/v1.0/removebg";

    private readonly HttpClient _httpClient;

    public AiOrchestratorService(HttpClient httpClient) => _httpClient = httpClient;

    // ─────────────────────────────────────────────────────────────────────────
    //  Imagen 3 — Background Generation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls Google Vertex AI Imagen 3 to generate a background image from the
    /// banner master prompt + user text. Returns the generated image as PNG bytes.
    /// </summary>
    public async Task<byte[]> GenerateBackgroundAsync(
        string masterPrompt, string userText, ILambdaLogger logger)
    {
        var projectId = Environment.GetEnvironmentVariable(ProjectIdEnv)
            ?? throw new InvalidOperationException($"Environment variable '{ProjectIdEnv}' is not set.");
        var location  = Environment.GetEnvironmentVariable(LocationEnv) ?? "us-central1";

        logger.LogInformation(
            $"[AiOrchestratorService] 🎨 Calling Imagen 3 on project={projectId}, location={location}");

        // Regional endpoint required for Vertex AI prediction
        var clientBuilder = new PredictionServiceClientBuilder
        {
            Endpoint = $"{location}-aiplatform.googleapis.com"
        };
        var client = await clientBuilder.BuildAsync();

        var endpointResource =
            $"projects/{projectId}/locations/{location}/publishers/google/models/{ImagenModel}";

        var fullPrompt = $"{masterPrompt} User context: {userText}";

        // Build the instance proto value
        var instance = ProtobufValue.ForStruct(new Struct
        {
            Fields =
            {
                ["prompt"] = ProtobufValue.ForString(fullPrompt)
            }
        });

        // Build the parameters proto value
        var parameters = ProtobufValue.ForStruct(new Struct
        {
            Fields =
            {
                ["sampleCount"]  = ProtobufValue.ForNumber(1),
                ["aspectRatio"]  = ProtobufValue.ForString("1:1"),
                ["safetyFilterLevel"] = ProtobufValue.ForString("block_few")
            }
        });

        var response = await client.PredictAsync(
            endpointResource,
            new[] { instance },
            parameters);

        // Imagen 3 returns base64-encoded PNG bytes in the prediction
        var prediction = response.Predictions.FirstOrDefault()
            ?? throw new InvalidOperationException("Imagen 3 returned no predictions.");

        var b64 = prediction.StructValue.Fields["bytesBase64Encoded"].StringValue
            ?? throw new InvalidOperationException("Imagen 3 prediction missing 'bytesBase64Encoded'.");

        // Strip data:image/...;base64, prefix if present
        if (b64.StartsWith("data:"))
        {
            var commaIndex = b64.IndexOf(',');
            if (commaIndex >= 0)
                b64 = b64.Substring(commaIndex + 1);
        }

        var imageBytes = Convert.FromBase64String(b64);

        logger.LogInformation(
            $"[AiOrchestratorService] ✅ Imagen 3 responded — {imageBytes.Length} bytes");

        return imageBytes;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RMBG-1.4 — Background Removal
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls remove.bg (RMBG-1.4) to strip the background from a user photo.
    /// Returns a transparent PNG as a byte array.
    /// </summary>
    public async Task<byte[]> RemoveBackgroundAsync(string userPhotoUrl, ILambdaLogger logger)
    {
        logger.LogInformation(
            $"[AiOrchestratorService] ✂️  Calling RMBG-1.4 for: {userPhotoUrl}");

        var apiKey = Environment.GetEnvironmentVariable(RmbgApiKeyEnv)
            ?? throw new InvalidOperationException($"Environment variable '{RmbgApiKeyEnv}' is not set.");

        using var formData = new MultipartFormDataContent();
        formData.Add(new StringContent(userPhotoUrl), "image_url");
        formData.Add(new StringContent("auto"),       "size");
        formData.Add(new StringContent("png"),        "format");

        using var request = new HttpRequestMessage(HttpMethod.Post, RmbgApiUrl);
        request.Headers.Add("X-Api-Key", apiKey);
        
        // Ensure accept header asks for image (though base client might add application/json)
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("image/*"));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json", 0.8));

        request.Content = formData;

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            logger.LogError(
                $"[AiOrchestratorService] RMBG-1.4 error {(int)response.StatusCode}: {err}");
            throw new HttpRequestException($"RMBG-1.4 returned {(int)response.StatusCode}: {err}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        byte[] finalBytes;

        if (contentType != null && contentType.Contains("application/json"))
        {
            logger.LogInformation("[AiOrchestratorService] ✂️ RMBG returned JSON. Extracting base64...");
            var jsonStr = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonStr);
            var resultB64 = doc.RootElement.GetProperty("data").GetProperty("result_b64").GetString()!;
            finalBytes = Convert.FromBase64String(resultB64);
        }
        else
        {
            logger.LogInformation("[AiOrchestratorService] ✂️ RMBG returned image binary directly.");
            finalBytes = await response.Content.ReadAsByteArrayAsync();
        }

        logger.LogInformation(
            $"[AiOrchestratorService] ✅ Background removed — {finalBytes.Length} bytes PNG");
        return finalBytes;
    }
}
