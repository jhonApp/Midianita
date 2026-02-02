using FluentAssertions;
using Microsoft.AspNetCore.Http;
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
        private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
        private readonly DesignsService _service;

        public DesignServiceTests()
        {
            _designRepositoryMock = new Mock<IDesignRepository>();
            _auditPublisherMock = new Mock<IAuditPublisher>();
            _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

            _service = new DesignsService(
                _designRepositoryMock.Object,
                _auditPublisherMock.Object,
                _httpContextAccessorMock.Object
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
