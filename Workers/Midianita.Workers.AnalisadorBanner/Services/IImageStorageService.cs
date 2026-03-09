using Amazon.Lambda.Core;

namespace Midianita.Workers.AnalisadorBanner.Services;

/// <summary>
/// Downloads an image from object storage and returns its Base64 data-URI
/// along with the physical pixel dimensions extracted locally.
/// </summary>
public interface IImageStorageService
{
    Task<(string Base64, int Width, int Height)> DownloadAndProcessAsync(
        string bucketName,
        string objectKey,
        ILambdaLogger logger);
}
