using Amazon.Lambda.Core;
using Midianita.Workers.ProcessadorArte.Models;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Applies final typography overlays.
/// </summary>
public interface IImageCompositionService
{
    Task<byte[]> ApplyTypographyAsync(
        byte[] aiGeneratedImageBytes, BannerAnalysisResult bannerMetadata, string userText, ILambdaLogger logger);
}
