using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Core.Interfaces;
using Midianita.Infrastructure.Services;
using System.Text.Json;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
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
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new VertexAiService(httpClient, tokenProvider, configuration);
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

                        // Execute tasks in parallel
                        // Note: If text generation fails, we might still want the image, or fail all? 
                        // Requirement implies simultaneous generation. We await both.
                        var imageTask = vertexAiService.GenerateImageAsync(job.Prompt);
                        var textTask = vertexAiService.GenerateTextAsync(job.Prompt);

                        await Task.WhenAll(imageTask, textTask);

                        var responseJson = await imageTask;
                        var generatedText = await textTask;

                        context.Logger.LogLine($"Generated Copy: {generatedText}");

                        // Parse Vertex AI Response...
                        using var doc = JsonDocument.Parse(responseJson);
                        
                        if (!doc.RootElement.TryGetProperty("predictions", out var predictions))
                        {
                             context.Logger.LogLine($"Error: 'predictions' missing: {responseJson}");
                             continue;
                        }

                        if (predictions.GetArrayLength() == 0)
                        {
                             context.Logger.LogLine("Error: No predictions found.");
                             continue;
                        }

                        var base64Image = "";
                        var firstPrediction = predictions[0];

                         if (firstPrediction.ValueKind == JsonValueKind.String) 
                         {
                             base64Image = firstPrediction.GetString();
                         }
                         else if (firstPrediction.ValueKind == JsonValueKind.Object && firstPrediction.TryGetProperty("bytesBase64Encoded", out var bytesProperty)) 
                         {
                             base64Image = bytesProperty.GetString();
                         }
                         
                         if (string.IsNullOrEmpty(base64Image)) 
                         {
                             throw new Exception("No image data found in response.");
                         }

                        var imageBytes = Convert.FromBase64String(base64Image);
                        using var memoryStream = new MemoryStream(imageBytes);

                        // Upload to S3
                        var fileName = $"temp/{job.JobId}.png";
                        var url = await storageService.UploadFileAsync(memoryStream, fileName, "image/png");

                        context.Logger.LogLine($"Image generated and uploaded to: {url}");

                        // Update DynamoDB
                        // PK: JOB#{jobId}
                        // SK: METADATA
                        var dynamoClient = scope.ServiceProvider.GetRequiredService<IAmazonDynamoDB>();
                        var tableName = "Midianita_Dev_Designs"; // Ideally from config

                        var updateRequest = new UpdateItemRequest
                        {
                            TableName = tableName,
                            Key = new Dictionary<string, AttributeValue>
                            {
                                { "Id", new AttributeValue { S = job.JobId.ToString() } }
                            },
                            UpdateExpression = "SET #status = :status, #imageUrl = :imageUrl, #generatedText = :generatedText, #updatedAt = :updatedAt",
                            ExpressionAttributeNames = new Dictionary<string, string>
                            {
                                { "#status", "Status" },
                                { "#imageUrl", "ImageUrl" },
                                { "#generatedText", "GeneratedText" },
                                { "#updatedAt", "UpdatedAt" }
                            },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                { ":status", new AttributeValue { S = "COMPLETED" } },
                                { ":imageUrl", new AttributeValue { S = url } },
                                { ":generatedText", new AttributeValue { S = generatedText } },
                                { ":updatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("O") } }
                            }
                        };

                        await dynamoClient.UpdateItemAsync(updateRequest);
                        context.Logger.LogLine("DynamoDB updated with status COMPLETED.");
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
