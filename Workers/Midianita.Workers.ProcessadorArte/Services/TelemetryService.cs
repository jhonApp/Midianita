using Amazon.Lambda.Core;
using System.Text.Json;

namespace Midianita.Workers.ProcessadorArte.Services;

public interface ITelemetryService
{
    void LogGenerationResult(string jobId, string provider, long latencyMs, bool success, string? errorMessage = null);
}

public class CloudWatchTelemetryService : ITelemetryService
{
    public void LogGenerationResult(string jobId, string provider, long latencyMs, bool success, string? errorMessage = null)
    {
        // Example cost calculation for Fal.ai (Nano Banana Edit is approx $0.05 per gen)
        double estimatedCost = success ? 0.05 : 0.00;

        var telemetry = new
        {
            Type = "IMAGE_GEN",
            JobId = jobId,
            Provider = provider,
            LatencyMs = latencyMs,
            Success = success,
            ErrorMessage = errorMessage,
            EstimatedCostUsd = estimatedCost,
            Timestamp = DateTime.UtcNow.ToString("o")
        };

        Console.WriteLine(JsonSerializer.Serialize(telemetry));
    }
}
