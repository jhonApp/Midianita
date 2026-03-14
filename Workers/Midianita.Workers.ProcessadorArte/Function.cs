using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Workers.ProcessadorArte.Models;
using Midianita.Workers.ProcessadorArte.Services;
using System.Net.Http.Headers;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Workers.ProcessadorArte;

/// <summary>
/// Lambda entry point — triggered by an SQS message.
/// Acts purely as an orchestrator; all business logic lives in the services.
/// </summary>
public class Function
{
    private readonly IDynamoDbJobRepository   _jobRepository;
    private readonly IImageCompositionService _imageComposer;
    private readonly IS3StorageService        _s3Storage;
    private readonly IFalApiService           _falApi;

    // ── Production constructor ────────────────────────────────────────────────
    public Function()
    {
        var services = new ServiceCollection();

        // AWS clients
        services.AddSingleton<IAmazonS3,       AmazonS3Client>();
        services.AddSingleton<IAmazonDynamoDB,  AmazonDynamoDBClient>();

        // Named HttpClient for AI APIs
        services.AddHttpClient("AI", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(120); // AI generation can be slow
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Services
        services.AddTransient<IDynamoDbJobRepository,   DynamoDbJobRepository>();
        services.AddTransient<IImageCompositionService, ImageCompositionService>();
        services.AddTransient<IS3StorageService,        S3StorageService>();
        services.AddTransient<IFalApiService>(provider =>
            new FalApiService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("AI")));

        var provider    = services.BuildServiceProvider();
        _jobRepository  = provider.GetRequiredService<IDynamoDbJobRepository>();
        _imageComposer  = provider.GetRequiredService<IImageCompositionService>();
        _s3Storage      = provider.GetRequiredService<IS3StorageService>();
        _falApi         = provider.GetRequiredService<IFalApiService>();
    }

    internal Function(
        IDynamoDbJobRepository   jobRepository,
        IImageCompositionService imageComposer,
        IS3StorageService        s3Storage,
        IFalApiService           falApi)
    {
        _jobRepository  = jobRepository;
        _imageComposer  = imageComposer;
        _s3Storage      = s3Storage;
        _falApi         = falApi;
    }

    // ── Entry Point ───────────────────────────────────────────────────────────

    /// <summary>Lambda handler — triggered by an SQS event.</summary>
    public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
    {
        context.Logger.LogInformation(
            $"[ProcessadorArte] Lambda invoked. Records: {sqsEvent.Records.Count}");

        foreach (var record in sqsEvent.Records)
        {
            SqsJobPayload? payload = null;

            try
            {
                // ── 1. Deserialize SQS payload ────────────────────────────────
                payload = JsonSerializer.Deserialize<SqsJobPayload>(record.Body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidOperationException("SQS message body could not be deserialized.");

                context.Logger.LogInformation(
                    $"[ProcessadorArte] Processing JobId: {payload.JobId}, BannerId: {payload.BannerId}");

                // ── 2. Mark job as in-progress ────────────────────────────────
                await _jobRepository.UpdateJobStatusAsync(
                    payload.JobId, "PROCESSANDO", context.Logger);

                // ── 3. Fetch banner metadata ──────────────────────────────────
                var banner = await _jobRepository.GetBannerMetadataAsync(
                    payload.BannerId, context.Logger);

                // ── 4. MasterPrompt Dynamic Context Orchestration ─────────────────
                string modifiedMasterPrompt = banner.MasterPrompt;
                
                // Safety: If ImageUrls is null or empty, try to fallback/initialize
                var effectiveImageUrls = payload.ImageUrls ?? new List<string>();
                if (effectiveImageUrls.Count == 0 && !string.IsNullOrEmpty(record.Body))
                {
                    // Attempt to extract legacy UserPhotoUrl if ImageUrls is missing
                    using var doc = JsonDocument.Parse(record.Body);
                    if (doc.RootElement.TryGetProperty("UserPhotoUrl", out var prop))
                    {
                         effectiveImageUrls.Add(prop.GetString()!);
                    }
                }

                if (effectiveImageUrls.Count == 1)
                {
                    context.Logger.LogInformation("[ProcessadorArte] ✂️ 1 Image detected. Manipulating MasterPrompt to remove midground Z-1 elements.");
                    
                    modifiedMasterPrompt = System.Text.RegularExpressions.Regex.Replace(
                        modifiedMasterPrompt, 
                        @"(?i)(Z-1|midground).*?(?=Z-2|Z-3|Foreground|foreground|$)", 
                        "");
                        
                    modifiedMasterPrompt += " CRITICAL: This composition must feature ONLY the single main subject provided in foreground (Z-2). DO NOT generate any other people, background photos, or extra faces in the midground.";
                }

                // ── 5. Fal.ai Pure Generative AI Composition ──────────────────
                context.Logger.LogInformation(
                    $"[ProcessadorArte] 🎨 Initiating Pure Generative AI via Fal.ai with {effectiveImageUrls.Count} images...");

                var aiGeneratedBytes = await _falApi.GenerateImageAsync(
                    effectiveImageUrls, modifiedMasterPrompt, context.Logger);

                // ── 5. Apply Final Typography (ImageSharp) ────────────────────
                context.Logger.LogInformation(
                    $"[ProcessadorArte] ✍️ Applying Final Typography...");
                var finalImageBytes = await _imageComposer.ApplyTypographyAsync(
                    aiGeneratedBytes, banner, payload.UserText, context.Logger);

                // ── 6. Upload to S3 ───────────────────────────────────────────
                var finalUrl = await _s3Storage.UploadFinalImageAsync(
                    payload.JobId, finalImageBytes, context.Logger);

                // ── 7. Mark job as complete ───────────────────────────────────
                await _jobRepository.UpdateJobStatusAsync(
                    payload.JobId, "CONCLUIDO", context.Logger, finalUrl);

                context.Logger.LogInformation(
                    $"[ProcessadorArte] ✅ Job {payload.JobId} completed. URL: {finalUrl}");
            }
            catch (Exception ex)
            {
                var jobId = payload?.JobId ?? "UNKNOWN";
                context.Logger.LogError(
                    $"[ProcessadorArte] ❌ Error on JobId {jobId}: {ex.Message}");

                if (payload is not null)
                {
                    try
                    {
                        await _jobRepository.UpdateJobStatusAsync(
                            payload.JobId, "ERRO", context.Logger);
                    }
                    catch (Exception updateEx)
                    {
                        context.Logger.LogError(
                            $"[ProcessadorArte] ⚠️  Could not mark job as ERRO: {updateEx.Message}");
                    }
                }

                // Re-throw so SQS applies visibility timeout / DLQ policy
                throw;
            }
        }
    }
}
