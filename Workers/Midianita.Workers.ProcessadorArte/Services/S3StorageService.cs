using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;

namespace Midianita.Workers.ProcessadorArte.Services;

/// <summary>
/// Faz upload da imagem final composta para um bucket S3 e retorna a URL pública.
/// </summary>
public sealed class S3StorageService : IS3StorageService
{
    private const string BucketEnv  = "OUTPUT_S3_BUCKET";
    private const string ContentType = "image/png";

    private readonly IAmazonS3 _s3;

    public S3StorageService(IAmazonS3 s3) => _s3 = s3;

    /// <summary>
    /// Upload primário via <see cref="Stream"/> — sem alocação intermediária de byte[].
    /// Ideal para imagens retornadas pela API da OpenAI (~2–5 MB).
    /// </summary>
    public async Task<string> UploadFinalImageStreamAsync(
        string jobId, Stream imageStream, ILambdaLogger logger)
    {
        var bucket = ResolveBucket();
        var key    = BuildKey(jobId);

        logger.LogInformation(
            $"[S3StorageService] ⬆️  Stream upload → s3://{bucket}/{key}");

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName  = bucket,
            Key         = key,
            InputStream = imageStream,
            ContentType = ContentType,

            // Garante que o S3 não tente fazer checksum de streaming multipart
            // antes de conhecer o tamanho total (importante para MemoryStream de base64).
            DisablePayloadSigning = false
        });

        var url = BuildUrl(bucket, key);
        logger.LogInformation($"[S3StorageService] ✅ Upload concluído. URL: {url}");
        return url;
    }

    /// <summary>
    /// Sobrecarga legada — wraps byte[] em MemoryStream e delega ao método principal.
    /// </summary>
    public async Task<string> UploadFinalImageAsync(
        string jobId, byte[] imageBytes, ILambdaLogger logger)
    {
        logger.LogInformation(
            $"[S3StorageService] ⚠️  Usando sobrecarga legada (byte[]). " +
            $"Prefira UploadFinalImageStreamAsync. Tamanho: {imageBytes.Length} bytes.");

        await using var stream = new MemoryStream(imageBytes);
        return await UploadFinalImageStreamAsync(jobId, stream, logger);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ResolveBucket() =>
        Environment.GetEnvironmentVariable(BucketEnv)
        ?? throw new InvalidOperationException(
            $"Variável de ambiente '{BucketEnv}' não configurada.");

    private static string BuildKey(string jobId) =>
        $"artes-finais/{jobId}.png";

    private static string BuildUrl(string bucket, string key) =>
        $"https://{bucket}.s3.amazonaws.com/{key}";
}
