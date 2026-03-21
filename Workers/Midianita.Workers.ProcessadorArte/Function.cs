using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Workers.ProcessadorArte.Models;
using Midianita.Workers.ProcessadorArte.Services;
using System.Net.Http.Headers;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Workers.ProcessadorArte;

public class Function
{
    private readonly IDynamoDbJobRepository   _jobRepository;
    private readonly IImageCompositionService _imageComposer;
    private readonly IS3StorageService        _s3Storage;
    private readonly IFalApiService           _falApi;

    public Function()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IAmazonS3,       AmazonS3Client>();
        services.AddSingleton<IAmazonDynamoDB,  AmazonDynamoDBClient>();
        services.AddSingleton<ITelemetryService, CloudWatchTelemetryService>();

        services.AddHttpClient("AI", client => {
            client.Timeout = TimeSpan.FromSeconds(120); 
        });

        services.AddTransient<IDynamoDbJobRepository,   DynamoDbJobRepository>();
        services.AddTransient<IImageCompositionService>(provider =>
            new ImageCompositionService(provider.GetRequiredService<ISmartTypographyService>()));
        services.AddTransient<IS3StorageService,        S3StorageService>();
        services.AddSingleton<ISmartTypographyService,  SmartTypographyService>();
        services.AddTransient<IFalApiService>(provider =>
            new FalApiService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("AI"),
                provider.GetRequiredService<ITelemetryService>()));

        var provider    = services.BuildServiceProvider();
        _jobRepository  = provider.GetRequiredService<IDynamoDbJobRepository>();
        _imageComposer  = provider.GetRequiredService<IImageCompositionService>();
        _s3Storage      = provider.GetRequiredService<IS3StorageService>();
        _falApi         = provider.GetRequiredService<IFalApiService>();
    }

    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        foreach (var record in sqsEvent.Records)
        {
            try
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<SqsJobPayload>(record.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("Invalid SQS payload");

                await _jobRepository.UpdateJobStatusAsync(payload.JobId, "PROCESSANDO", context.Logger);

                var banner = await _jobRepository.GetBannerMetadataAsync(payload.BannerId, context.Logger);

                // Pass JobId to Fal
                var aiGeneratedBytes = await _falApi.GenerateImageAsync(
                    payload.ImageUrls ?? new List<string>(), banner.MasterPrompt, context.Logger, payload.JobId);

                var finalImageBytes = await _imageComposer.ApplyTypographyAsync(
                    aiGeneratedBytes, banner, payload.UserText, context.Logger);

                var finalUrl = await _s3Storage.UploadFinalImageAsync(payload.JobId, finalImageBytes, context.Logger);
                await _jobRepository.UpdateJobStatusAsync(payload.JobId, "CONCLUIDO", context.Logger, finalUrl);
            }
            catch (Exception ex)
            {
                context.Logger.LogError($"[ProcessadorArte] Fatal Error: {ex.Message}");
                throw; // Retry SQS
            }
        }
    }
}
