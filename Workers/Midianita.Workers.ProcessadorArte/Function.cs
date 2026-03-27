using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Workers.ProcessadorArte.Models;
using Midianita.Workers.ProcessadorArte.Services;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Workers.ProcessadorArte;

public class Function
{
    private readonly IDynamoDbJobRepository _jobRepository;
    private readonly ISkiaRendererService   _renderer;
    private readonly IS3StorageService      _s3Storage;
    private readonly IFalApiService         _falApi;
    private readonly IAmazonS3              _s3Client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Function()
    {
        var services = new ServiceCollection();

        // ── AWS SDK Clients ────────────────────────────────────────────────
        services.AddSingleton<IAmazonS3,       AmazonS3Client>();
        services.AddSingleton<IAmazonDynamoDB,  AmazonDynamoDBClient>();
        services.AddSingleton<ITelemetryService, CloudWatchTelemetryService>();

        // ── HTTP Client (for Fal.ai) ───────────────────────────────────────
        services.AddHttpClient("AI", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120);
        });

        // ── Application Services ───────────────────────────────────────────
        services.AddTransient<IDynamoDbJobRepository, DynamoDbJobRepository>();
        services.AddTransient<IS3StorageService,      S3StorageService>();
        services.AddSingleton<ISkiaRendererService,   SkiaRendererService>();
        services.AddTransient<IFalApiService>(provider =>
            new FalApiService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("AI"),
                provider.GetRequiredService<ITelemetryService>()));

        var provider   = services.BuildServiceProvider();
        _jobRepository = provider.GetRequiredService<IDynamoDbJobRepository>();
        _renderer      = provider.GetRequiredService<ISkiaRendererService>();
        _s3Storage     = provider.GetRequiredService<IS3StorageService>();
        _falApi        = provider.GetRequiredService<IFalApiService>();
        _s3Client      = provider.GetRequiredService<IAmazonS3>();
    }

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        foreach (var record in sqsEvent.Records)
        {
            try
            {
                // ═══════════════════════════════════════════════════════════
                //  STEP 1 — Deserialize SQS message
                // ═══════════════════════════════════════════════════════════
                var payload = JsonSerializer.Deserialize<SqsJobPayload>(record.Body, JsonOptions)
                    ?? throw new InvalidOperationException("Invalid SQS payload.");

                context.Logger.LogInformation(
                    $"[ProcessadorArte] 🚀 Processing JobId: {payload.JobId}, BannerId: {payload.BannerId}");

                await _jobRepository.UpdateJobStatusAsync(payload.JobId, "PROCESSANDO", context.Logger);

                // ═══════════════════════════════════════════════════════════
                //  STEP 2 — Fetch full banner record from DynamoDB
                // ═══════════════════════════════════════════════════════════
                var banner = await _jobRepository.GetBannerFullRecordAsync(payload.BannerId, context.Logger);

                if (string.IsNullOrWhiteSpace(banner.MasterPrompt))
                    throw new InvalidOperationException($"Banner '{payload.BannerId}' has no MasterPrompt.");

                if (banner.LayoutRulesV2 is null)
                    throw new InvalidOperationException($"Banner '{payload.BannerId}' has no LayoutRulesV2 data.");

                // ═══════════════════════════════════════════════════════════
                //  STEP 3 — Generate clean background via Fal.ai
                // ═══════════════════════════════════════════════════════════
                context.Logger.LogInformation("[ProcessadorArte] 🎨 Generating background via Fal.ai...");

                var backgroundBytes = await _falApi.GenerateImageAsync(
                    banner.MasterPrompt, context.Logger, payload.JobId);

                context.Logger.LogInformation(
                    $"[ProcessadorArte] ✅ Background generated: {backgroundBytes.Length} bytes");

                // ═══════════════════════════════════════════════════════════
                //  STEP 4 — Download person cutout from S3 (USER IMAGE)
                // ═══════════════════════════════════════════════════════════
                byte[] personBytes = Array.Empty<byte>();

                if (!string.IsNullOrWhiteSpace(payload.ReferenceImageUrl))
                {
                    context.Logger.LogInformation(
                        $"[ProcessadorArte] 👤 Downloading USER person cutout: {payload.ReferenceImageUrl}");

                    try
                    {
                        personBytes = await DownloadPersonFromS3Async(payload.ReferenceImageUrl, context.Logger);

                        context.Logger.LogInformation(
                            $"[ProcessadorArte] ✅ User person image downloaded: {personBytes.Length} bytes");

                        if (payload.RemoveBackground && personBytes.Length > 0)
                        {
                            context.Logger.LogInformation("[ProcessadorArte] ✂️ Removing background with Fal.ai...");
                            personBytes = await _falApi.RemoveBackgroundAsync(personBytes, context.Logger, payload.JobId);
                            context.Logger.LogInformation($"[ProcessadorArte] ✅ Background removed: {personBytes.Length} bytes");
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Logger.LogWarning(
                            $"[ProcessadorArte] ⚠️ Could not prepare user image: {ex.Message}. Proceeding without cutout.");
                        personBytes = Array.Empty<byte>();
                    }
                }
                else
                {
                    context.Logger.LogInformation("[ProcessadorArte] ℹ️ No ReferenceImageUrl in payload. Rendering without user cutout.");
                }

                // ═══════════════════════════════════════════════════════════
                //  STEP 5 — Compose final banner with SkiaSharp (4-layer)
                // ═══════════════════════════════════════════════════════════
                context.Logger.LogInformation("[ProcessadorArte] 🖼️ Composing final banner with SkiaRenderer...");

                var finalBannerBytes = _renderer.RenderFinalBanner(
                    banner.LayoutRulesV2,
                    backgroundBytes,
                    personBytes);

                context.Logger.LogInformation(
                    $"[ProcessadorArte] ✅ Final banner composed: {finalBannerBytes.Length} bytes");

                // ═══════════════════════════════════════════════════════════
                //  STEP 6 — Upload to S3
                // ═══════════════════════════════════════════════════════════
                var finalUrl = await _s3Storage.UploadFinalImageAsync(
                    payload.JobId, finalBannerBytes, context.Logger);

                // ═══════════════════════════════════════════════════════════
                //  STEP 7 — Update status to COMPLETED
                // ═══════════════════════════════════════════════════════════
                await _jobRepository.UpdateJobStatusAsync(
                    payload.JobId, "COMPLETED", context.Logger, finalUrl);

                context.Logger.LogInformation(
                    $"[ProcessadorArte] 🏁 Pipeline completed. FinalUrl: {finalUrl}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"[ProcessadorArte] ❌ Fatal Error: {ex.Message}");
                context.Logger.LogError($"[ProcessadorArte] StackTrace: {ex.StackTrace}");
                throw; // Let SQS retry
            }
        }
    }

    // ── S3 Download Helper ─────────────────────────────────────────────────

    /// <summary>
    /// Downloads the person cutout from the INPUT assets bucket.
    /// Handles both raw keys, s3:// URIs, and HTTPS absolute URLs.
    /// </summary>
    private async Task<byte[]> DownloadPersonFromS3Async(string objectKeyOrUrl, ILambdaLogger logger)
    {
        var bucket = Environment.GetEnvironmentVariable("INPUT_S3_BUCKET")
            ?? Environment.GetEnvironmentVariable("OUTPUT_S3_BUCKET")
            ?? throw new InvalidOperationException("Neither 'INPUT_S3_BUCKET' nor 'OUTPUT_S3_BUCKET' is configured.");

        var objectKey = ExtractS3Key(objectKeyOrUrl, bucket);

        logger.LogInformation($"[ProcessadorArte] ⬇️ S3 Download: bucket={bucket}, key={objectKey}");

        var response = await _s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = bucket,
            Key = objectKey
        });

        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Extracts the clean object key from any of these formats:
    /// - URL HTTPS: https://midianita-dev-assets.s3.amazonaws.com/anexos/imagem.jpg
    /// - S3 URI: s3://midianita-dev-assets/anexos/imagem.jpg
    /// - Raw Key: anexos/imagem.jpg
    /// </summary>
    public static string ExtractS3Key(string input, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        input = input.Trim();

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            // Cenário B: s3://midianita-dev-assets/anexos/imagem.jpg
            if (uri.Scheme.Equals("s3", StringComparison.OrdinalIgnoreCase))
            {
                return uri.AbsolutePath.TrimStart('/');
            }

            // Cenário A: https://midianita-dev-assets.s3.amazonaws.com/anexos/imagem.jpg
            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || 
                uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                if (uri.Host.Contains("amazonaws.com", StringComparison.OrdinalIgnoreCase) || 
                    uri.Host.Contains(bucketName, StringComparison.OrdinalIgnoreCase))
                {
                    return uri.AbsolutePath.TrimStart('/');
                }
            }
        }

        // Cenário C: Raw Key (anexos/imagem.jpg)
        return input.TrimStart('/');
    }
}
