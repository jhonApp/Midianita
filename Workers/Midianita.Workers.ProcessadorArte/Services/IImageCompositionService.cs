using Amazon.Lambda.Core;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Composes the final artwork by overlaying a cutout on a background and rendering text.
/// </summary>
public interface IImageCompositionService
{
    Task<byte[]> ComposeFinalArtefactAsync(
        byte[] backgroundBytes,
        byte[]? cutoutBytes,
        string? cutoutPlacement,
        string userText,
        ILambdaLogger logger);
}
