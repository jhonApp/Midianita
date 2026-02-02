using Amazon.S3;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Midianita.Worker.Interfaces;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Midianita.Worker.Workers
{
    public class CleanupWorker : IQueueWorker
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly IAmazonS3 _s3Client;
        private readonly IConfiguration _configuration;

        public CleanupWorker(IAmazonSQS sqsClient, IAmazonS3 s3Client, IConfiguration configuration)
        {
            _sqsClient = sqsClient;
            _s3Client = s3Client;
            _configuration = configuration;
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queueUrl = _configuration["AWS:CleanupQueueUrl"] 
                           ?? "https://sqs.us-east-1.amazonaws.com/633574826164/image-cleanup-queue";
            var bucketName = Environment.GetEnvironmentVariable("AWS__BucketName") ?? "midianita-dev-assets";

            Console.WriteLine($"Started Cleanup Polling Loop on {queueUrl}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var request = new ReceiveMessageRequest
                    {
                        QueueUrl = queueUrl,
                        WaitTimeSeconds = 20,
                        MaxNumberOfMessages = 1
                    };

                    var response = await _sqsClient.ReceiveMessageAsync(request, stoppingToken);

                    if (response.Messages == null || response.Messages.Count == 0) continue;

                    foreach (var msg in response.Messages)
                    {
                        Console.WriteLine($"[Cleanup] Processing cleanup request: {msg.MessageId}");

                        try
                        {
                            var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(msg.Body);
                            if (payload != null && payload.TryGetValue("s3Key", out var s3Key))
                            {
                                Console.WriteLine($"[Cleanup] Deleting object: {bucketName}/{s3Key}");
                                await _s3Client.DeleteObjectAsync(bucketName, s3Key, stoppingToken);
                                Console.WriteLine($"[Cleanup] Object deleted.");
                            }
                            else
                            {
                                Console.WriteLine("[Cleanup] Invalid payload, skipping.");
                            }

                            await _sqsClient.DeleteMessageAsync(queueUrl, msg.ReceiptHandle, stoppingToken);
                        }
                        catch (JsonException)
                        {
                            Console.WriteLine("[Cleanup] JSON Error, deleting invalid message.");
                            await _sqsClient.DeleteMessageAsync(queueUrl, msg.ReceiptHandle, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Cleanup] Error: {ex.Message}");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
    }
}
