using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Workers.AnalisadorBanner.Services;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Workers.AnalisadorBanner;

public record SqsMessageBody(string BannerId);

public class Function
{
    private readonly IImageStorageService _imageStorageService;
    private readonly IVisionApiService    _visionApiService;
    private readonly IBannerRepository    _bannerRepository;
    private readonly ISafetyService       _safety;

    public Function()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAmazonS3, AmazonS3Client>();
        services.AddSingleton<IAmazonDynamoDB, AmazonDynamoDBClient>();
        services.AddSingleton<ITelemetryService, CloudWatchTelemetryService>();
        services.AddSingleton<ISafetyService, LocalSafetyService>();

        services.AddHttpClient("Anthropic", client => {
            client.Timeout = TimeSpan.FromSeconds(60); 
        });

        services.AddTransient<IImageStorageService, S3ImageService>();
        services.AddTransient<IBannerRepository, DynamoDbBannerRepository>();
        services.AddTransient<IVisionApiService>(provider =>
            new AnthropicVisionService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("Anthropic"),
                provider.GetRequiredService<ITelemetryService>()));

        var provider = services.BuildServiceProvider();
        _imageStorageService = provider.GetRequiredService<IImageStorageService>();
        _visionApiService = provider.GetRequiredService<IVisionApiService>();
        _bannerRepository = provider.GetRequiredService<IBannerRepository>();
        _safety = provider.GetRequiredService<ISafetyService>();
    }

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        foreach (var record in sqsEvent.Records)
        {
            string bannerId = string.Empty;

            try
            {
                // Parse BannerId from SQS Message Body securely and case-insensitively
                var bodyOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var parsedBody = JsonSerializer.Deserialize<SqsMessageBody>(record.Body, bodyOptions);
                bannerId = parsedBody?.BannerId 
                           ?? throw new ArgumentException("BannerId not found in message body.");

                context.Logger.LogInformation($"[AnalisadorBanner] Starting analysis for BannerId: {bannerId}");

                // Get original image key from DynamoDB
                var originalKey = await _bannerRepository.GetOriginalImageKeyAsync(bannerId, context.Logger)
                                  ?? throw new InvalidOperationException($"OriginalImageKey not found for BannerId: {bannerId}");

                // Safety check on metadata or prompt if available (here we check the object key for signs of trouble)
                if (!_safety.IsContentSafe(originalKey, context.Logger))
                {
                    context.Logger.LogWarning($"[Audit] Job {bannerId} blocked by safety guardrails.");
                    return;
                }

                var bucketName = Environment.GetEnvironmentVariable("ASSETS_BUCKET") 
                                 ?? throw new InvalidOperationException("ASSETS_BUCKET env var is not set.");

                var (base64Image, width, height) = await _imageStorageService.DownloadAndProcessAsync(
                    bucketName, originalKey, context.Logger);

                var result = await _visionApiService.AnalyzeImageAsync(base64Image, context.Logger, bannerId);

                await _bannerRepository.SaveAsync(bannerId, originalKey, width, height, result, context.Logger);
            }
            catch (Exception ex)
            {
                var idToLog = string.IsNullOrEmpty(bannerId) ? record.MessageId : bannerId;
                context.Logger.LogError($"[AnalisadorBanner] Error processing {idToLog}: {ex.Message}");
                throw; // Rethrow for SQS/Lambda retry mechanisms
            }
        }
    }
}
