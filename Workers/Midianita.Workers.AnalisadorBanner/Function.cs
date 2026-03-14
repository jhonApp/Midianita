using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Workers.AnalisadorBanner.Services;
using System.Net.Http.Headers;

// Registers the Lambda JSON serializer (System.Text.Json-based)
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Workers.AnalisadorBanner;

/// <summary>
/// Lambda entry point. Acts purely as an orchestrator: wires up DI, then
/// delegates each step to a focused service. No business logic lives here.
/// </summary>
public class Function
{
    private readonly IImageStorageService _imageStorageService;
    private readonly IVisionApiService    _visionApiService;
    private readonly IBannerRepository    _bannerRepository;

    // ── Production constructor (used by the Lambda runtime) ───────────────────
    public Function()
    {
        var services = new ServiceCollection();

        // AWS clients
        services.AddSingleton<IAmazonS3,       AmazonS3Client>();
        services.AddSingleton<IAmazonDynamoDB,  AmazonDynamoDBClient>();

        // Named HttpClient for Anthropic
        services.AddHttpClient("Anthropic", client =>
        {
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Services
        services.AddTransient<IImageStorageService, S3ImageService>();
        services.AddTransient<IBannerRepository,    DynamoDbBannerRepository>();
        services.AddTransient<IVisionApiService>(provider =>
            new AnthropicVisionService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("Anthropic")));

        var provider         = services.BuildServiceProvider();
        _imageStorageService = provider.GetRequiredService<IImageStorageService>();
        _visionApiService    = provider.GetRequiredService<IVisionApiService>();
        _bannerRepository    = provider.GetRequiredService<IBannerRepository>();
    }

    // ── Test constructor (allows injecting mocks) ─────────────────────────────
    internal Function(
        IImageStorageService imageStorageService,
        IVisionApiService    visionApiService,
        IBannerRepository    bannerRepository)
    {
        _imageStorageService = imageStorageService;
        _visionApiService    = visionApiService;
        _bannerRepository    = bannerRepository;
    }

    // ── Entry Point ───────────────────────────────────────────────────────────

    /// <summary>Lambda handler — triggered by an S3 PUT event.</summary>
    public async Task FunctionHandler(S3Event s3Event, ILambdaContext context)
    {
        context.Logger.LogInformation(
            $"[AnalisadorBanner] Lambda invoked. Records received: {s3Event.Records.Count}");

        foreach (var record in s3Event.Records)
        {
            var bucketName = record.S3.Bucket.Name;
            var objectKey  = Uri.UnescapeDataString(record.S3.Object.Key.Replace("+", " "));

            context.Logger.LogInformation(
                $"[AnalisadorBanner] Processing s3://{bucketName}/{objectKey}");

            try
            {
                // Step 1 — Download image and extract dimensions
                var (base64Image, width, height) =
                    await _imageStorageService.DownloadAndProcessAsync(
                        bucketName, objectKey, context.Logger);

                // Step 2 — Analyse with GPT-4o Vision
                var result = await _visionApiService.AnalyzeImageAsync(
                    base64Image, context.Logger);

                // Step 3 — Persist to DynamoDB
                await _bannerRepository.SaveAsync(
                    objectKey, width, height, result, context.Logger);

                context.Logger.LogInformation(
                    $"[AnalisadorBanner] ✅ Successfully processed: {objectKey}");
            }
            catch (Exception ex)
            {
                context.Logger.LogError(
                    $"[AnalisadorBanner] ❌ Error processing {objectKey}: {ex.Message}");
                throw;
            }
        }
    }
}
