using Amazon.Lambda.Core;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Orchestrates calls to external AI APIs (FLUX.dev and RMBG-1.4).
/// </summary>
public interface IAiOrchestratorService
{
    /// <summary>Calls FLUX.dev to generate a background image. Returns the result image URL/bytes.</summary>
    Task<byte[]> GenerateBackgroundAsync(string masterPrompt, string userText, ILambdaLogger logger);

    /// <summary>Calls RMBG-1.4 to remove the background from a user photo. Returns PNG bytes.</summary>
    Task<byte[]> RemoveBackgroundAsync(string userPhotoUrl, ILambdaLogger logger);
}
