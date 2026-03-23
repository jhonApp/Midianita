using Amazon.SQS;
using Amazon.SQS.Model;
using Midianita.Core.Interfaces;
using System.Text.Json;

namespace Midianita.Infrastructure.Services
{
    public class SqsPublisher : IQueuePublisher
    {
        private readonly IAmazonSQS _sqsClient;

        public SqsPublisher(IAmazonSQS sqsClient)
        {
            _sqsClient = sqsClient;
        }

        public async Task PublishAsync<T>(T message, string queueUrl)
        {
            var jsonMessage = JsonSerializer.Serialize(message);
            var request = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = jsonMessage
            };

            await _sqsClient.SendMessageAsync(request);
        }
    }
}
