using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using System.Text.Json;
using Midianita.Core.Entities;

namespace Midianita.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IAmazonSQS _sqsClient;
    private readonly DynamoDBContext _dynamoContext;
    private const string QueueUrl = "Midianita_AuditQueue"; // Ideally from config

    public Worker(ILogger<Worker> logger, IAmazonSQS sqsClient, IAmazonDynamoDB dynamoDbClient)
    {
        _logger = logger;
        _sqsClient = sqsClient;
        _dynamoContext = new DynamoDBContext(dynamoDbClient);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started connecting to queue {QueueUrl}", QueueUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = new ReceiveMessageRequest
                {
                    QueueUrl = QueueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20
                };

                var response = await _sqsClient.ReceiveMessageAsync(request, stoppingToken);

                foreach (var message in response.Messages)
                {
                    try
                    {
                        var auditEntry = JsonSerializer.Deserialize<AuditLogEntry>(message.Body);
                        if (auditEntry != null)
                        {
                            await _dynamoContext.SaveAsync(auditEntry, stoppingToken);
                            _logger.LogInformation("Processed audit log: {LogId}", auditEntry.LogId);
                        }

                        await _sqsClient.DeleteMessageAsync(QueueUrl, message.ReceiptHandle, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing message {MessageId}", message.MessageId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling SQS");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
