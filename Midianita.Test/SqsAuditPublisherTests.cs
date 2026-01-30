using Amazon.SQS;
using Amazon.SQS.Model;
using Midianita.Core.Entities;
using Midianita.Infrastructure.Services;
using Moq;
using Xunit;

namespace Midianita.Test
{
    public class SqsAuditPublisherTests
    {
        private readonly Mock<IAmazonSQS> _sqsClientMock;
        private readonly SqsAuditPublisher _publisher;
        private readonly string _queueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/test-queue";

        public SqsAuditPublisherTests()
        {
            _sqsClientMock = new Mock<IAmazonSQS>();
            _publisher = new SqsAuditPublisher(_sqsClientMock.Object, _queueUrl);
        }

        [Fact]
        public async Task PublishAsync_ShouldSendMessageToSqs()
        {
            // Arrange
            var entry = new AuditLogEntry(
                "Design",
                "Create",
                "User123",
                "Design created",
                DateTime.UtcNow
            );

            // Act
            await _publisher.PublishAsync(entry);

            // Assert
            _sqsClientMock.Verify(x => x.SendMessageAsync(
                It.Is<SendMessageRequest>(r => 
                    r.QueueUrl == _queueUrl && 
                    r.MessageBody.Contains("Design created") &&
                    r.MessageBody.Contains("User123")),
                default
            ), Times.Once);
        }
    }
}
