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
                //  STEP 4 — Download person cutout from S3
                // ═══════════════════════════════════════════════════════════
                byte[] personBytes = Array.Empty<byte>();

                if (banner.HasCutoutImages && !string.IsNullOrWhiteSpace(banner.OriginalImageKey))
                {
                    context.Logger.LogInformation(
                        $"[ProcessadorArte] 👤 Downloading person cutout: {banner.OriginalImageKey}");

                    personBytes = await DownloadFromS3Async(banner.OriginalImageKey, context.Logger);

                    context.Logger.LogInformation(
                        $"[ProcessadorArte] ✅ Person downloaded: {personBytes.Length} bytes");
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
    /// Downloads an object from S3 using the provided key or full URL.
    /// Handles both raw keys ("banners-admin/img.png") and absolute URLs.
    /// </summary>
    private async Task<byte[]> DownloadFromS3Async(string objectKeyOrUrl, ILambdaLogger logger)
    {
        var bucket = Environment.GetEnvironmentVariable("OUTPUT_S3_BUCKET")
            ?? throw new InvalidOperationException("Environment variable 'OUTPUT_S3_BUCKET' is not set.");

        var objectKey = SanitizeObjectKey(objectKeyOrUrl);

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
    /// Strips a full S3 URL down to just the object key if needed.
    /// </summary>
    private static string SanitizeObjectKey(string keyOrUrl)
    {
        if (Uri.TryCreate(keyOrUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "https" || uri.Scheme == "http"))
        {
            return uri.AbsolutePath.TrimStart('/');
        }
        return keyOrUrl;
    }
}
