using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Midianita.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Midianita.Infrastructure.Services;
using Midianita.Core.Interfaces;
using System.Text.Json;
using Amazon.Lambda.Core;

// LOCAL DEBUGGING ENTRY POINT
// This allows running the worker as a Console App to poll SQS real-time.

var queueUrl = "https://sqs.us-east-1.amazonaws.com/633574826164/image-generation-queue";
var region = Amazon.RegionEndpoint.USEast1;

// FORCE DEV CONFIGURATION (Local Console App Only)
// Maps to configuration["AWS:BucketName"] and configuration["AWS:Region"]
Environment.SetEnvironmentVariable("AWS__BucketName", "midianita-dev-assets");
Environment.SetEnvironmentVariable("AWS__Region", "us-east-1");

Console.WriteLine("Starting Worker...");
Console.WriteLine($"Polling Queue: {queueUrl}");

// 1. Setup Configuration with Manual Overrides
var inMemorySettings = new Dictionary<string, string> {
    {"GCP:ProjectId", "mythic-inn-144217"},
    {"GCP:Location", "us-central1"}
};

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(inMemorySettings!)
    .AddEnvironmentVariables()
    .Build();

// 2. Instantiate AWS Clients Manually
var s3Client = new AmazonS3Client(region);
var dynamoClient = new AmazonDynamoDBClient(region);
var sqsClient = new AmazonSQSClient(region);

// 3. Setup Dependency Injection for Function
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging();
services.AddHttpClient();
services.AddSingleton<IAmazonS3>(s3Client);
services.AddSingleton<IAmazonDynamoDB>(dynamoClient);
services.AddScoped<ITokenProvider, GoogleTokenProvider>();
services.AddScoped<IVertexAiService>(sp =>
{
    var http = sp.GetRequiredService<HttpClient>();
    var token = sp.GetRequiredService<ITokenProvider>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new VertexAiService(http, token, config);
});
services.AddScoped<IStorageService, S3StorageService>();

var serviceProvider = services.BuildServiceProvider();

// 4. Instantiate Worker Function
var worker = new Function(serviceProvider);
var context = new ConsoleLambdaContext();

// 5. Queues
var generationQueueUrl = "https://sqs.us-east-1.amazonaws.com/633574826164/image-generation-queue";
var cleanupQueueUrl = "https://sqs.us-east-1.amazonaws.com/633574826164/image-cleanup-queue";
var bucketName = Environment.GetEnvironmentVariable("AWS__BucketName") ?? "midianita-dev-assets";

Console.WriteLine("Starting Worker with Parallel Polling...");
Console.WriteLine($"Generation Queue: {generationQueueUrl}");
Console.WriteLine($"Cleanup Queue: {cleanupQueueUrl}");

// 6. Run Parallel Loops
var generationTask = PollGenerationQueue(sqsClient, worker, context, generationQueueUrl, region);
var cleanupTask = PollCleanupQueue(sqsClient, s3Client, cleanupQueueUrl, bucketName);

await Task.WhenAll(generationTask, cleanupTask);

static async Task PollGenerationQueue(IAmazonSQS sqsClient, Function worker, ILambdaContext context, string queueUrl, Amazon.RegionEndpoint region)
{
    Console.WriteLine("Started Generation Polling Loop.");
    while (true)
    {
        try
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                WaitTimeSeconds = 20,
                MaxNumberOfMessages = 1
            };

            var response = await sqsClient.ReceiveMessageAsync(request);

            if (response.Messages == null || response.Messages.Count == 0) continue;

            foreach (var msg in response.Messages)
            {
                Console.WriteLine($"[Generation] Processing message: {msg.MessageId}");

                var lambdaEvent = new SQSEvent
                {
                    Records = new List<SQSEvent.SQSMessage>
                    {
                        new SQSEvent.SQSMessage
                        {
                            Body = msg.Body,
                            MessageId = msg.MessageId,
                            ReceiptHandle = msg.ReceiptHandle,
                            EventSource = "aws:sqs",
                            AwsRegion = region.SystemName
                        }
                    }
                };

                await worker.FunctionHandler(lambdaEvent, context);
                await sqsClient.DeleteMessageAsync(queueUrl, msg.ReceiptHandle);
                Console.WriteLine("[Generation] Message processed and deleted.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Generation] Error: {ex.Message}");
            await Task.Delay(5000);
        }
    }
}

static async Task PollCleanupQueue(IAmazonSQS sqsClient, IAmazonS3 s3Client, string queueUrl, string bucketName)
{
    Console.WriteLine("Started Cleanup Polling Loop.");
    while (true)
    {
        try
        {
            var request = new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                WaitTimeSeconds = 20,
                MaxNumberOfMessages = 1
            };

            var response = await sqsClient.ReceiveMessageAsync(request);

            if (response.Messages == null || response.Messages.Count == 0) continue;

            foreach (var msg in response.Messages)
            {
                Console.WriteLine($"[Cleanup] Processing cleanup request: {msg.MessageId}");

                try 
                {
                    // Body: { "s3Key": "..." }
                    var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(msg.Body);
                    if (payload != null && payload.TryGetValue("s3Key", out var s3Key))
                    {
                        Console.WriteLine($"[Cleanup] Deleting object: {bucketName}/{s3Key}");
                        await s3Client.DeleteObjectAsync(bucketName, s3Key);
                        Console.WriteLine($"[Cleanup] Object deleted.");
                    }
                    else
                    {
                        Console.WriteLine("[Cleanup] Invalid payload, skipping.");
                    }

                    await sqsClient.DeleteMessageAsync(queueUrl, msg.ReceiptHandle);
                }
                catch (JsonException)
                {
                     Console.WriteLine("[Cleanup] JSON Error, deleting invalid message.");
                     await sqsClient.DeleteMessageAsync(queueUrl, msg.ReceiptHandle);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Cleanup] Error: {ex.Message}");
            await Task.Delay(5000);
        }
    }
}
