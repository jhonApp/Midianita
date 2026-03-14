using Amazon.Lambda.Core;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Services;

public sealed class FalApiService : IFalApiService
{
    private const string FalQueueUrl = "https://queue.fal.run/fal-ai/nano-banana/edit";
    private const string FalApiKeyEnv = "FAL_KEY";

    private readonly HttpClient _httpClient;

    public FalApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<byte[]> GenerateImageAsync(List<string> imageUrls, string masterPrompt, ILambdaLogger logger)
    {
        logger.LogInformation($"[FalApiService] 🎨 Calling Nano Banana Edit via Queue...");

        var apiKey = Environment.GetEnvironmentVariable(FalApiKeyEnv)
            ?? throw new InvalidOperationException($"Environment variable '{FalApiKeyEnv}' is not set.");

        var instructionPrompt = $"Preserve the identity and exact features of the main subjects in this image perfectly. Replace the entire background and environment to match this description: {masterPrompt}";

        var requestBody = new
        {
            image_urls = imageUrls.ToArray(),
            prompt     = instructionPrompt
        };

        var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var json = JsonSerializer.Serialize(requestBody, jsonOptions);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, FalQueueUrl);
        // Fal expects "Key <FAL_KEY>" 
        request.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey);
        request.Content = httpContent;

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError($"[FalApiService] Fal.ai API Queue Error ({response.StatusCode}): {errorContent}");
            throw new HttpRequestException($"Fal.ai API Error ({response.StatusCode}): {errorContent}");
        }

        var queueJson = await response.Content.ReadAsStringAsync();
        using var queueDoc = JsonDocument.Parse(queueJson);
        var statusUrl = queueDoc.RootElement.GetProperty("status_url").GetString()!;
        var responseUrl = queueDoc.RootElement.GetProperty("response_url").GetString()!;

        logger.LogInformation($"[FalApiService] ⏳ Job queued. Polling status_url: {statusUrl}");

        // Polling loop
        while (true)
        {
            await Task.Delay(2000); // Poll every 2 seconds

            using var statusReq = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            statusReq.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey);
            
            var statusResp = await _httpClient.SendAsync(statusReq);
            if (!statusResp.IsSuccessStatusCode)
            {
                 var errorContent = await statusResp.Content.ReadAsStringAsync();
                 logger.LogError($"[FalApiService] Fal.ai Status Error ({statusResp.StatusCode}): {errorContent}");
                 throw new HttpRequestException($"Fal.ai API Error ({statusResp.StatusCode}): {errorContent}");
            }

            var statusStr = await statusResp.Content.ReadAsStringAsync();
            using var sDoc = JsonDocument.Parse(statusStr);
            var status = sDoc.RootElement.GetProperty("status").GetString();

            if (status == "IN_PROGRESS" || status == "IN_QUEUE")
            {
                logger.LogInformation($"[FalApiService] ⏳ Status: {status}...");
                continue;
            }
            if (status == "COMPLETED")
            {
                logger.LogInformation($"[FalApiService] ✅ Status: COMPLETED. Fetching payload...");
                break;
            }
            else
            {
                throw new InvalidOperationException($"Fal job failed or returned unhandled status: {status}");
            }
        }

        // Fetch actual response
        using var respReq = new HttpRequestMessage(HttpMethod.Get, responseUrl);
        respReq.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey);
        
        var finalResp = await _httpClient.SendAsync(respReq);
        if (!finalResp.IsSuccessStatusCode)
        {
            var errorContent = await finalResp.Content.ReadAsStringAsync();
            logger.LogError($"[FalApiService] Fal.ai Fetch Error ({finalResp.StatusCode}): {errorContent}");
            throw new HttpRequestException($"Fal.ai API Error ({finalResp.StatusCode}): {errorContent}");
        }

        var finalJson = await finalResp.Content.ReadAsStringAsync();
        using var fDoc = JsonDocument.Parse(finalJson);
        
        var imagesArray = fDoc.RootElement.GetProperty("images");
        var finalImageUrl = imagesArray[0].GetProperty("url").GetString()!;

        if (finalImageUrl.StartsWith("data:"))
        {
            var commaIndex = finalImageUrl.IndexOf(',');
            var b64        = finalImageUrl.Substring(commaIndex + 1);
            return Convert.FromBase64String(b64);
        }
        else
        {
            logger.LogInformation($"[FalApiService] 📥 Downloading generation from Fal URL: {finalImageUrl}");
            return await _httpClient.GetByteArrayAsync(finalImageUrl);
        }
    }
}
