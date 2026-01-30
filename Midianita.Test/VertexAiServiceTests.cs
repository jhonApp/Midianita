using System.Net;
using FluentAssertions;
using Midianita.Infrastructure.Services;
using Moq;
using Moq.Protected;
using Xunit;

namespace Midianita.Test
{
    public class VertexAiServiceTests
    {
        private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
        private readonly HttpClient _httpClient;
        private readonly VertexAiService _service;

        public VertexAiServiceTests()
        {
            _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
            _service = new VertexAiService(_httpClient, "test-project", "us-central1");
        }

        [Fact]
        public async Task GenerateImageAsync_ShouldReturnResponse_WhenApiCallIsSuccessful()
        {
            // Arrange
            var expectedResponse = "{\"predictions\": [\"base64image\"]}";
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(expectedResponse)
                });

            // Act
            var result = await _service.GenerateImageAsync("a futuristic city");

            // Assert
            result.Should().Be(expectedResponse);
        }

        [Fact]
        public async Task GenerateImageAsync_ShouldThrowException_WhenApiCallFails()
        {
            // Arrange
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = new StringContent("Error message")
                });

            // Act
            Func<Task> act = async () => await _service.GenerateImageAsync("invalid prompt");

            // Assert
            await act.Should().ThrowAsync<HttpRequestException>()
                .WithMessage("*Erro Vertex AI*");
        }
    }
}
