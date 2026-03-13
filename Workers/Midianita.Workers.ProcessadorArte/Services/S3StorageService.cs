using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Uploads the final composed image to an S3 bucket and returns the public URL.
/// </summary>
public sealed class S3StorageService : IS3StorageService
{
    private const string BucketEnv = "OUTPUT_S3_BUCKET";

    private readonly IAmazonS3 _s3;

    public S3StorageService(IAmazonS3 s3) => _s3 = s3;

    public async Task<string> UploadFinalImageAsync(string jobId, byte[] imageBytes, ILambdaLogger logger)
    {
        var bucket = Environment.GetEnvironmentVariable(BucketEnv)
            ?? throw new InvalidOperationException($"Environment variable '{BucketEnv}' is not set.");

        var key = $"artes-finais/{jobId}.png";

        logger.LogInformation($"[S3StorageService] ⬆️  Uploading final image to s3://{bucket}/{key} ({imageBytes.Length} bytes)");

        using var stream = new MemoryStream(imageBytes);

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = bucket,
            Key         = key,
            InputStream = stream,
            ContentType = "image/png"
        });

        // Return the S3 URL (assumes the bucket has appropriate read permissions / CloudFront)
        var url = $"https://{bucket}.s3.amazonaws.com/{key}";

        logger.LogInformation($"[S3StorageService] ✅ Image uploaded. URL: {url}");
        return url;
    }
}
