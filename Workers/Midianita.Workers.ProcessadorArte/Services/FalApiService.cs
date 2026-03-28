using Amazon.Lambda.Core;
using Polly;
using Polly.Retry;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midianita.Workers.ProcessadorArte.Services;

public sealed class FalApiService : IFalApiService
{
    private const string FalQueueUrl = "https://queue.fal.run/fal-ai/flux-1/dev";
    private const string FalRmbgQueueUrl = "https://queue.fal.run/fal-ai/birefnet";
    private const string FalApiKeyEnv = "FAL_KEY";

    private readonly HttpClient _httpClient;
    private readonly ITelemetryService _telemetry;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;

    public FalApiService(HttpClient httpClient, ITelemetryService telemetry)
    {
        _httpClient = httpClient;
        _telemetry = telemetry;

        // More aggressive retry for polling and intermediate failures
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => (int)r.StatusCode == 429 || (int)r.StatusCode >= 500)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(1.5, retryAttempt)));
    }

    public async Task<byte[]> GenerateImageAsync(string masterPrompt, ILambdaLogger logger, string jobId)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;
        string? error = null;

        try
        {
            var apiKey = Environment.GetEnvironmentVariable(FalApiKeyEnv)
                ?? throw new InvalidOperationException($"Environment variable '{FalApiKeyEnv}' is not set.");

            var requestBody = new 
            { 
                prompt = masterPrompt,
                image_size = "landscape_4_3", // Optimized for banners
                num_images = 1
            };
            // Serializamos o JSON uma única vez — reutilizá-lo é seguro pois é uma string imutável.
            var json = JsonSerializer.Serialize(requestBody);

            // 1. Queue initialization with Polly
            // CORREÇÃO: StringContent e HttpRequestMessage são criados DENTRO do delegate.
            // HttpClient faz dispose do Content após cada envio; se firassem fora, o Polly
            // tentaria re-enviar um objeto já descartado, causando ObjectDisposedException.
            var response = await _retryPolicy.ExecuteAsync(() => 
            {
                var freshContent = new StringContent(json, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, FalQueueUrl)
                {
                    Headers = { Authorization = new AuthenticationHeaderValue("Key", apiKey) },
                    Content = freshContent
                };
                return _httpClient.SendAsync(request);
            });

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Fal queue error: {response.StatusCode}");

            var queueJson = await response.Content.ReadAsStringAsync();
            using var queueDoc = JsonDocument.Parse(queueJson);
            var statusUrl = queueDoc.RootElement.GetProperty("status_url").GetString()!;
            var responseUrl = queueDoc.RootElement.GetProperty("response_url").GetString()!;

            // 2. Polling loop with local timeout
            var pollingCts = new CancellationTokenSource(TimeSpan.FromSeconds(110)); // Total Lambda limit nearby
            while (!pollingCts.Token.IsCancellationRequested)
            {
                await Task.Delay(2000, pollingCts.Token);

                var statusResp = await _retryPolicy.ExecuteAsync(() =>
                {
                    using var statusReq = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                    statusReq.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey);
                    return _httpClient.SendAsync(statusReq);
                });

                var statusStr = await statusResp.Content.ReadAsStringAsync();
                using var sDoc = JsonDocument.Parse(statusStr);
                var status = sDoc.RootElement.GetProperty("status").GetString();

                if (status == "COMPLETED") break;
                if (status != "IN_PROGRESS" && status != "IN_QUEUE")
                    throw new InvalidOperationException($"Fal job failed: {status}");
            }

            // 3. Download results
            var finalResp = await _httpClient.GetAsync(responseUrl);
            var finalJson = await finalResp.Content.ReadAsStringAsync();
            
            // Valida se foi sucesso e loga caso contrário
            if (!finalResp.IsSuccessStatusCode)
            {
                logger.LogError($"[FalApiService] Error downloading results from Fal.ai. HTTP Status: {finalResp.StatusCode}. Response: {finalJson}");
                throw new HttpRequestException($"Fal API error: {finalResp.StatusCode}");
            }

            using var fDoc = JsonDocument.Parse(finalJson);
            
            // Extração resiliente usando TryGetProperty
            if (!fDoc.RootElement.TryGetProperty("images", out var imagesElement) || 
                imagesElement.ValueKind != JsonValueKind.Array || 
                imagesElement.GetArrayLength() == 0 ||
                !imagesElement[0].TryGetProperty("url", out var urlElement))
            {
                logger.LogError($"[FalApiService] Invalid payload structure. Missing 'images[0].url'. Raw Payload: {finalJson}");
                throw new InvalidOperationException("Failed to extract final image URL from the API response.");
            }

            var finalImageUrl = urlElement.GetString()!;
            
            var bytes = await _httpClient.GetByteArrayAsync(finalImageUrl);
            success = true;
            return bytes;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();
            _telemetry.LogGenerationResult(jobId, "fal-ai/flux-1/dev", sw.ElapsedMilliseconds, success, error);
        }
    }

    public async Task<byte[]> RemoveBackgroundAsync(byte[] imageBytes, ILambdaLogger logger, string jobId)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;
        string? error = null;

        try
        {
            var apiKey = Environment.GetEnvironmentVariable(FalApiKeyEnv)
                ?? throw new InvalidOperationException($"Environment variable '{FalApiKeyEnv}' is not set.");

            var base64 = Convert.ToBase64String(imageBytes);
            var requestBody = new { image_url = $"data:image/png;base64,{base64}" };
            var json = JsonSerializer.Serialize(requestBody);

            var response = await _retryPolicy.ExecuteAsync(() => 
            {
                var freshContent = new StringContent(json, Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, FalRmbgQueueUrl)
                {
                    Headers = { Authorization = new AuthenticationHeaderValue("Key", apiKey) },
                    Content = freshContent
                };
                return _httpClient.SendAsync(request);
            });

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Fal RMBG queue error: {response.StatusCode}");

            var queueJson = await response.Content.ReadAsStringAsync();
            using var queueDoc = JsonDocument.Parse(queueJson);
            var statusUrl = queueDoc.RootElement.GetProperty("status_url").GetString()!;
            var responseUrl = queueDoc.RootElement.GetProperty("response_url").GetString()!;

            var pollingCts = new CancellationTokenSource(TimeSpan.FromSeconds(110));
            while (!pollingCts.Token.IsCancellationRequested)
            {
                await Task.Delay(2000, pollingCts.Token);

                var statusResp = await _retryPolicy.ExecuteAsync(() =>
                {
                    using var statusReq = new HttpRequestMessage(HttpMethod.Get, statusUrl);
                    statusReq.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey);
                    return _httpClient.SendAsync(statusReq);
                });

                var statusStr = await statusResp.Content.ReadAsStringAsync();
                using var sDoc = JsonDocument.Parse(statusStr);
                var status = sDoc.RootElement.GetProperty("status").GetString();

                if (status == "COMPLETED") break;
                if (status != "IN_PROGRESS" && status != "IN_QUEUE")
                    throw new InvalidOperationException($"Fal RMBG job failed: {status}");
            }

            var finalResp = await _httpClient.GetAsync(responseUrl);
            var finalJson = await finalResp.Content.ReadAsStringAsync();
            
            if (!finalResp.IsSuccessStatusCode)
            {
                logger.LogError($"[FalApiService] Error downl. RMBG. Status: {finalResp.StatusCode}. Payload: {finalJson}");
                throw new HttpRequestException($"Fal API error: {finalResp.StatusCode}");
            }

            using var fDoc = JsonDocument.Parse(finalJson);
            
            if (!fDoc.RootElement.TryGetProperty("image", out var imageElement) || 
                !imageElement.TryGetProperty("url", out var urlElement))
            {
                logger.LogError($"[FalApiService] Invalid RMBG payload structure. Raw Payload: {finalJson}");
                throw new InvalidOperationException("Failed to extract cutout image URL from response.");
            }

            var finalImageUrl = urlElement.GetString()!;
            var bytes = await _httpClient.GetByteArrayAsync(finalImageUrl);
            
            success = true;
            return bytes;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            throw;
        }
        finally
        {
            sw.Stop();
            _telemetry.LogGenerationResult(jobId, "fal-ai/birefnet", sw.ElapsedMilliseconds, success, error);
        }
    }
}
