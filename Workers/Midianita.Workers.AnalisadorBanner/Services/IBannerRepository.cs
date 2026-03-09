using Amazon.Lambda.Core;
using Midianita.Workers.AnalisadorBanner.Models;

namespace Midianita.Workers.AnalisadorBanner.Services;

/// <summary>
/// Persists banner analysis results to the backing store.
/// </summary>
public interface IBannerRepository
{
    Task SaveAsync(
        string objectKey,
        int width,
        int height,
        BannerAnalysisResult result,
        ILambdaLogger logger);
}
