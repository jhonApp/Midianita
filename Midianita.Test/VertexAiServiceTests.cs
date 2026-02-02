using System.Net;
using FluentAssertions;
using Midianita.Core.Interfaces;
using Midianita.Infrastructure.Services;
using Moq;
using Moq.Protected;
using Xunit;

namespace Midianita.Test
{
    public class VertexAiServiceTests
    {
        private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
        private readonly Mock<ITokenProvider> _tokenProviderMock;
        private readonly HttpClient _httpClient;
        private readonly VertexAiService _service;

        public VertexAiServiceTests()
        {
            _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
            _tokenProviderMock = new Mock<ITokenProvider>();
            _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
            
            _tokenProviderMock.Setup(x => x.GetAccessTokenAsync())
                .ReturnsAsync("fake-access-token");

            var configurationMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
            configurationMock.Setup(c => c["GCP:ProjectId"]).Returns("test-project");
            configurationMock.Setup(c => c["GCP:Location"]).Returns("us-central1");

            _service = new VertexAiService(_httpClient, _tokenProviderMock.Object, configurationMock.Object);
        }

        [Fact]
        public async Task GenerateImageAsync_ShouldReturnResponse_WhenApiCallIsSuccessful()
        {
            // Arrange
            var expectedResponse = "{\"predictions\": [\"base64image\"]}";
            _httpMessageHandlerMock.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => 
                        req.Headers.Authorization.ToString() == "Bearer fake-access-token"),
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
