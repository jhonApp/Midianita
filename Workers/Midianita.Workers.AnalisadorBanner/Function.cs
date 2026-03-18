using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.S3Events;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Workers.AnalisadorBanner.Services;
using System.Net.Http.Headers;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Workers.AnalisadorBanner;

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

    public async Task FunctionHandler(S3Event s3Event, ILambdaContext context)
    {
        foreach (var record in s3Event.Records)
        {
            var jobId = record.S3.Object.Key;

            try
            {
                // Safety check on metadata or prompt if available (here we check the object key for signs of trouble)
                if (!_safety.IsContentSafe(jobId, context.Logger))
                {
                    context.Logger.LogWarning($"[Audit] Job {jobId} blocked by safety guardrails.");
                    return;
                }

                var (base64Image, width, height) = await _imageStorageService.DownloadAndProcessAsync(
                    record.S3.Bucket.Name, jobId, context.Logger);

                var result = await _visionApiService.AnalyzeImageAsync(base64Image, context.Logger, jobId);

                await _bannerRepository.SaveAsync(jobId, width, height, result, context.Logger);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"[AnalisadorBanner] Error processing {jobId}: {ex.Message}");
                throw; // Rethrow for SQS/Lambda retry mechanisms
            }
        }
    }
}
