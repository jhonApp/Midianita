using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Midianita.Worker.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Midianita.Worker.Workers
{
    public class GenerationWorker : IQueueWorker
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly Function _functionHandler;
        private readonly IConfiguration _configuration;

        public GenerationWorker(IAmazonSQS sqsClient, Function functionHandler, IConfiguration configuration)
        {
            _sqsClient = sqsClient;
            _functionHandler = functionHandler;
            _configuration = configuration;
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var queueUrl = _configuration["AWS:GenerationQueueUrl"] 
                           ?? "https://sqs.us-east-1.amazonaws.com/633574826164/image-generation-queue";
            var region = Amazon.RegionEndpoint.USEast1; // Could extract to config

            Console.WriteLine($"Started Generation Polling Loop on {queueUrl}");

            var context = new ConsoleLambdaContext();

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

                        await _functionHandler.FunctionHandler(lambdaEvent, context);
                        await _sqsClient.DeleteMessageAsync(queueUrl, msg.ReceiptHandle, stoppingToken);
                        Console.WriteLine("[Generation] Message processed and deleted.");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[Generation] Error: {ex.Message}");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
    }
}
