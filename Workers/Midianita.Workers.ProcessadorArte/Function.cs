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
    private readonly IAiOrchestratorService   _aiOrchestrator;
    private readonly IImageCompositionService _imageComposer;
    private readonly IS3StorageService        _s3Storage;

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
        services.AddTransient<IS3StorageService,         S3StorageService>();
        services.AddTransient<IAiOrchestratorService>(provider =>
            new AiOrchestratorService(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("AI")));

        var provider    = services.BuildServiceProvider();
        _jobRepository  = provider.GetRequiredService<IDynamoDbJobRepository>();
        _aiOrchestrator = provider.GetRequiredService<IAiOrchestratorService>();
        _imageComposer  = provider.GetRequiredService<IImageCompositionService>();
        _s3Storage      = provider.GetRequiredService<IS3StorageService>();
    }

    // ── Test constructor ──────────────────────────────────────────────────────
    internal Function(
        IDynamoDbJobRepository   jobRepository,
        IAiOrchestratorService   aiOrchestrator,
        IImageCompositionService imageComposer,
        IS3StorageService        s3Storage)
    {
        _jobRepository  = jobRepository;
        _aiOrchestrator = aiOrchestrator;
        _imageComposer  = imageComposer;
        _s3Storage      = s3Storage;
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

                // ── 4. Parallel AI calls (Task.WhenAll) ───────────────────────
                context.Logger.LogInformation(
                    $"[ProcessadorArte] 🚀 Starting parallel AI calls. " +
                    $"RemoveBg: {banner.HasCutoutImages}");

                var backgroundTask = _aiOrchestrator.GenerateBackgroundAsync(
                    banner.MasterPrompt, payload.UserText, context.Logger);

                var cutoutTask = banner.HasCutoutImages
                    ? _aiOrchestrator.RemoveBackgroundAsync(payload.UserPhotoUrl, context.Logger)
                    : Task.FromResult<byte[]?>(null);

                await Task.WhenAll(backgroundTask, cutoutTask);

                var backgroundBytes = await backgroundTask;
                var cutoutBytes     = await cutoutTask;

                context.Logger.LogInformation(
                    $"[ProcessadorArte] ✅ Both AI tasks completed.");

                // ── 5. Compose final image ────────────────────────────────────
                var finalImageBytes = await _imageComposer.ComposeFinalArtefactAsync(
                    backgroundBytes,
                    cutoutBytes,
                    banner.CutoutPlacement,
                    payload.UserText,
                    context.Logger);

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
