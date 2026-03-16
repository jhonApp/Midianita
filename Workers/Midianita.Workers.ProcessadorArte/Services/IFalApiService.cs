using Amazon.Lambda.Core;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Service to call the Fal.ai API endpoints.
/// </summary>
public interface IFalApiService
{
    /// <summary>
    /// Calls the fal-ai/flux-pro/v1.1 endpoint (queue).
    /// </summary>
    Task<byte[]> GenerateImageAsync(List<string> imageUrls, string masterPrompt, ILambdaLogger logger);
}
