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
    private const string FalQueueUrl = "https://queue.fal.run/fal-ai/nano-banana/edit";
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

    public async Task<byte[]> GenerateImageAsync(List<string> imageUrls, string masterPrompt, ILambdaLogger logger, string jobId)
    {
        var sw = Stopwatch.StartNew();
        bool success = false;
        string? error = null;

        try
        {
            var apiKey = Environment.GetEnvironmentVariable(FalApiKeyEnv)
                ?? throw new InvalidOperationException($"Environment variable '{FalApiKeyEnv}' is not set.");

            var requestBody = new { image_urls = imageUrls.ToArray(), prompt = masterPrompt };
            var json = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            // 1. Queue initialization with Polly
            var response = await _retryPolicy.ExecuteAsync(() => 
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, FalQueueUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Key", apiKey);
                request.Content = httpContent;
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
            using var fDoc = JsonDocument.Parse(finalJson);
            var finalImageUrl = fDoc.RootElement.GetProperty("images")[0].GetProperty("url").GetString()!;
            
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
            _telemetry.LogGenerationResult(jobId, "fal-ai/nano-banana", sw.ElapsedMilliseconds, success, error);
        }
    }
}
