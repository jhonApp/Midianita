using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using SixLabors.ImageSharp;

namespace Midianita.Workers.AnalisadorBanner.Services;

/// <summary>
/// Downloads an image from a private S3 bucket, extracts its pixel dimensions
/// locally using ImageSharp, and returns a Base64 data-URI for the Vision API.
/// </summary>
public sealed class S3ImageService : IImageStorageService
{
    private readonly IAmazonS3 _s3Client;

    public S3ImageService(IAmazonS3 s3Client)
    {
        _s3Client = s3Client;
    }

    public async Task<(string Base64, int Width, int Height)> DownloadAndProcessAsync(
        string bucketName, string objectKey, ILambdaLogger logger)
    {
        var cleanKey = SanitizeObjectKey(objectKey);

        logger.LogInformation(
            $"[S3ImageService] ⬇️  Downloading image from S3: {bucketName}/{cleanKey}");

        var request = new GetObjectRequest
        {
            BucketName = bucketName,
            Key        = cleanKey
        };

        using var response     = await _s3Client.GetObjectAsync(request);
        using var memoryStream = new MemoryStream();

        await response.ResponseStream.CopyToAsync(memoryStream);

        // ── Extract dimensions (ImageSharp reads sequentially, then reset) ────
        memoryStream.Position = 0;
        var imageInfo = await Image.IdentifyAsync(memoryStream);
        int width  = imageInfo?.Width  ?? 0;
        int height = imageInfo?.Height ?? 0;

        logger.LogInformation(
            $"[S3ImageService] 📐 Image dimensions: {width}x{height}px");

        // ── Reset stream, then encode to Base64 ───────────────────────────────
        memoryStream.Position = 0;
        var imageBytes   = memoryStream.ToArray();
        var base64String = Convert.ToBase64String(imageBytes);

        var contentType = response.Headers.ContentType ?? "image/jpeg";
        var dataUri     = $"data:{contentType};base64,{base64String}";

        logger.LogInformation(
            $"[S3ImageService] ✅ Image ready. Size: {imageBytes.Length} bytes, MIME: {contentType}");

        return (dataUri, width, height);
    }

    /// <summary>
    /// Checks if the provided key is an absolute URL and extracts just the path portion.
    /// Otherwise, assumes it is already a valid object key.
    /// </summary>
    private static string SanitizeObjectKey(string objectKey)
    {
        if (Uri.TryCreate(objectKey, UriKind.Absolute, out var uri))
        {
            // AbsolutePath includes a leading slash (e.g. "/banners-admin/imagem.png")
            // We want everything after the slash for the S3 object key.
            return uri.AbsolutePath.TrimStart('/');
        }
        
        return objectKey;
    }
}
