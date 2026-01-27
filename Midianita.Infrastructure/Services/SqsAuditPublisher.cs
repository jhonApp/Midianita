using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;

namespace Midianita.Infrastructure.Services
{
    public class SqsAuditPublisher : IAuditPublisher
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly string _queueUrl;

        public SqsAuditPublisher(IAmazonSQS sqsClient, string queueUrl = "Midianita_Dev_AuditQueue")
        {
            _sqsClient = sqsClient;
            _queueUrl = queueUrl;
        }

        public async Task PublishAsync(AuditLogEntry entry)
        {
            var messageBody = JsonSerializer.Serialize(entry);
            var request = new SendMessageRequest
            {
                QueueUrl = _queueUrl,
                MessageBody = messageBody
            };

            await _sqsClient.SendMessageAsync(request);
        }
    }
}
