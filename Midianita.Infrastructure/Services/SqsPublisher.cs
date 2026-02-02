using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Configuration;
using Midianita.Core.Interfaces;
using System.Text.Json;

namespace Midianita.Infrastructure.Services
{
    public class SqsPublisher : IQueuePublisher
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly IConfiguration _configuration;

        public SqsPublisher(IAmazonSQS sqsClient, IConfiguration configuration)
        {
            _sqsClient = sqsClient;
            _configuration = configuration;
        }

        public async Task PublishAsync<T>(T message, string queueNameConfigKey)
        {
            var queueUrl = _configuration[$"AWS:{queueNameConfigKey}"];

            if (string.IsNullOrEmpty(queueUrl))
                throw new Exception($"Fila '{queueNameConfigKey}' não configurada.");

            var request = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = JsonSerializer.Serialize(message)
            };

            await _sqsClient.SendMessageAsync(request);
        }
    }
}
