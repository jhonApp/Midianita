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

// 1. Setup Configuration (Environment Variables for Prod/Dev parity)
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

// 2. Instantiate AWS Clients Manually
var s3Client = new AmazonS3Client(region);
var dynamoClient = new AmazonDynamoDBClient(region);
var sqsClient = new AmazonSQSClient(region);

// 3. Setup Dependency Injection for Function
var services = new ServiceCollection();

// Register Configuration
services.AddSingleton<IConfiguration>(configuration);
services.AddLogging();
services.AddHttpClient();

// Register Dependencies Manually
services.AddSingleton<IAmazonS3>(s3Client);
services.AddSingleton<IAmazonDynamoDB>(dynamoClient); // Even if not used directly in Handler, good practice
services.AddScoped<ITokenProvider, GoogleTokenProvider>();

// Register Services
// Note: We need to register VertexAiService. 
// It requires (HttpClient, ITokenProvider, projectId, location)
// We get projectId and location from config or hardcode for local debug if needed.
var projectId = configuration["GOOGLE_PROJECT_ID"] ?? "mythic-inn-144217"; // Fallback/Dummy
var location = configuration["GOOGLE_LOCATION"] ?? "us-central1";

services.AddScoped<IVertexAiService>(sp =>
{
    var http = sp.GetRequiredService<HttpClient>();
    var token = sp.GetRequiredService<ITokenProvider>();
    return new VertexAiService(http, token, projectId, location);
});

services.AddScoped<IStorageService, S3StorageService>();

var serviceProvider = services.BuildServiceProvider();

// 4. Instantiate Worker Function with Manual Provider
var worker = new Function(serviceProvider);
var context = new ConsoleLambdaContext(); // Mock Context

// 5. Polling Loop
while (true)
{
    try
    {
        var request = new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            WaitTimeSeconds = 20, // Long Polling
            MaxNumberOfMessages = 1
        };

        var response = await sqsClient.ReceiveMessageAsync(request);

        if (response.Messages == null || response.Messages.Count == 0)
        {
            // No messages, continue polling
            continue;
        }

        foreach (var msg in response.Messages)
        {
            Console.WriteLine($"Received message: {msg.MessageId}");

            // Map standard SQS Message to Lambda SQSEvent
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

            // Invoke the Worker Function
            await worker.FunctionHandler(lambdaEvent, context);

            // Delete message on success
            await sqsClient.DeleteMessageAsync(queueUrl, msg.ReceiptHandle);
            Console.WriteLine("Message processed and deleted.");
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error in polling loop: {ex.Message}");
        // Wait a bit before retrying to avoid spamming logs if SQS is down
        await Task.Delay(5000); 
    }
}
