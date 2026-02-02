using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Core.Interfaces;
using Midianita.Infrastructure.Services;
using System.Text.Json;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using Midianita.Core.DTOs;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Midianita.Worker
{
    public class Function
    {
        private readonly IServiceProvider _serviceProvider;

        public Function()
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            _serviceProvider = serviceCollection.BuildServiceProvider();
        }

        public Function(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddHttpClient();
            services.AddLogging();
            // services.AddHttpContextAccessor(); // Not needed for Worker

            // Configuration Values
            var awsRegion = configuration["AWS_REGION"] ?? "us-east-1";
            var projectId = configuration["GOOGLE_PROJECT_ID"];
            var location = configuration["GOOGLE_LOCATION"] ?? "us-central1";
            var serviceUrl = configuration["AWS_SERVICE_URL"]; // For LocalStack

            // Register Core Services
            services.AddScoped<ITokenProvider, GoogleTokenProvider>();

            // Register Vertex AI Service
            services.AddScoped<IVertexAiService>(sp =>
            {
                var httpClient = sp.GetRequiredService<HttpClient>();
                var tokenProvider = sp.GetRequiredService<ITokenProvider>();
                return new VertexAiService(httpClient, tokenProvider, projectId, location);
            });

            // Register AWS Services
            services.AddSingleton<IAmazonS3>(sp =>
            {
                if (!string.IsNullOrEmpty(serviceUrl))
                {
                    var clientConfig = new AmazonS3Config
                    {
                        ServiceURL = serviceUrl,
                        ForcePathStyle = true
                    };
                    return new AmazonS3Client(new Amazon.Runtime.BasicAWSCredentials("test", "test"), clientConfig);
                }
                return new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(awsRegion));
            });

            services.AddScoped<IStorageService, S3StorageService>();
        }

        public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var vertexAiService = scope.ServiceProvider.GetRequiredService<IVertexAiService>();
                var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

                foreach (var record in sqsEvent.Records)
                {
                    context.Logger.LogLine($"Processing message: {record.MessageId}");

                    try
                    {
                        ImageGenerationJob? job = null;
                        try 
                        {
                            job = JsonSerializer.Deserialize<ImageGenerationJob>(record.Body);
                        }
                        catch (JsonException)
                        {
                            context.Logger.LogLine("JSON Deserialization failed. Message body might be invalid. Skipping.");
                            continue; // Valid strategy: consume and ignore bad messages
                        }

                        if (job == null || string.IsNullOrWhiteSpace(job.Prompt))
                        {
                            context.Logger.LogLine("Invalid job payload (missing Prompt). Skipping.");
                            continue;
                        }

                        var userId = !string.IsNullOrWhiteSpace(job.UserId) ? job.UserId : "anonymous";
                        context.Logger.LogLine($"Generating image for User: {userId}, Prompt: {job.Prompt}");

                        // Generate Image (Vertex AI returns JSON with Base64)
                        var responseJson = await vertexAiService.GenerateImageAsync(job.Prompt);
                        
                        // Parse Vertex AI Response to get Base64 string
                        // Assuming response format: { "predictions": [ "base64..." ] }
                        // Need a robust parsing here. For now, simple extraction.
                        // In a real scenario, use a class/record for response.
                        using var doc = JsonDocument.Parse(responseJson);
                        
                        if (!doc.RootElement.TryGetProperty("predictions", out var predictions))
                        {
                            context.Logger.LogLine($"Error: 'predictions' missing in Vertex AI response: {responseJson}");
                            continue;
                        }

                        if (predictions.GetArrayLength() == 0)
                        {
                            context.Logger.LogLine("Error: 'predictions' array is empty.");
                            continue;
                        }

                        var firstPrediction = predictions[0];
                        string? base64Image = null;

                        // Check if prediction is a string (older models) or object (Imagen)
                        if (firstPrediction.ValueKind == JsonValueKind.String)
                        {
                            base64Image = firstPrediction.GetString();
                        }
                        else if (firstPrediction.ValueKind == JsonValueKind.Object)
                        {
                            if (firstPrediction.TryGetProperty("bytesBase64Encoded", out var bytesProperty))
                            {
                                base64Image = bytesProperty.GetString();
                            }
                            else
                            {
                                context.Logger.LogLine($"Error: 'bytesBase64Encoded' missing in prediction object: {firstPrediction.GetRawText()}");
                                continue;
                            }
                        }

                        if (string.IsNullOrEmpty(base64Image))
                        {
                            throw new Exception("No image data received from Vertex AI.");
                        }

                        var imageBytes = Convert.FromBase64String(base64Image);
                        using var memoryStream = new MemoryStream(imageBytes);

                        // Upload to S3
                        var fileName = $"temp/{job.JobId}.png";
                        // Note: S3StorageService.PromoteAssetAsync uses userId to move this later. 
                        // For now we upload to temp.
                        var url = await storageService.UploadFileAsync(memoryStream, fileName, "image/png");

                        context.Logger.LogLine($"Image generated and uploaded to: {url}");

                        // Note: In a real event-driven architecture, we might want to:
                        // 1. Update DynamoDB status to 'Completed'.
                        // 2. Send a notification (SNS/WebSocket).
                        // However, the prompt only asks to process and upload.
                    }
                    catch (Exception ex)
                    {
                        context.Logger.LogLine($"Error processing message {record.MessageId}: {ex.Message}");
                        context.Logger.LogLine(ex.StackTrace);
                        // Re-throw to ensure SQS retries the message if valid exception (network, service down)
                        // Bad logic errors caught above (deserialization) shouldn't reach here.
                        throw; 
                    }
                }
            }
        }
    }
}
