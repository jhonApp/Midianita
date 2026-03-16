using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Midianita.Aplication.Service;
using Midianita.Core.Entities;
using Midianita.Core.Interfaces;
using Moq;
using Xunit;

namespace Midianita.Test
{
    public class DesignServiceTests
    {
        private readonly Mock<IDesignRepository> _designRepositoryMock;
        private readonly Mock<IAuditPublisher> _auditPublisherMock;
        private readonly Mock<IQueuePublisher> _queuePublisherMock;
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
        private readonly Mock<ILogger<DesignsService>> _loggerMock;
        private readonly DesignsService _service;

        public DesignServiceTests()
        {
            _designRepositoryMock   = new Mock<IDesignRepository>();
            _auditPublisherMock     = new Mock<IAuditPublisher>();
            _queuePublisherMock     = new Mock<IQueuePublisher>();
            _configurationMock      = new Mock<IConfiguration>();
            _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
            _loggerMock             = new Mock<ILogger<DesignsService>>();

            _service = new DesignsService(
                _designRepositoryMock.Object,
                _auditPublisherMock.Object,
                _queuePublisherMock.Object,
                _configurationMock.Object,
                _httpContextAccessorMock.Object,
                _loggerMock.Object
            );
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnDesign_WhenDesignExists()
        {
            // Arrange
            var designId = Guid.NewGuid().ToString();
            var expectedDesign = new Design { Id = designId, Name = "Test Design" };

            _designRepositoryMock.Setup(x => x.GetByIdAsync(designId))
                .ReturnsAsync(expectedDesign);

            // Act
            var result = await _service.GetByIdAsync(designId);

            // Assert
            result.Should().NotBeNull();
            result!.Id.Should().Be(designId);
            result.Name.Should().Be("Test Design");
            _designRepositoryMock.Verify(x => x.GetByIdAsync(designId), Times.Once);
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnNull_WhenDesignDoesNotExist()
        {
            // Arrange
            var nonExistentId = Guid.NewGuid().ToString();
            _designRepositoryMock.Setup(x => x.GetByIdAsync(nonExistentId))
                .ReturnsAsync((Design?)null);

            // Act
            var result = await _service.GetByIdAsync(nonExistentId);

            // Assert
            result.Should().BeNull();
            _designRepositoryMock.Verify(x => x.GetByIdAsync(nonExistentId), Times.Once);
        }
    }
}
