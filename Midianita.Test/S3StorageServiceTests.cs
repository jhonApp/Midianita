using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Midianita.Infrastructure.Services;
using Moq;
using Xunit;

namespace Midianita.Test
{
    public class S3StorageServiceTests
    {
        private readonly Mock<IAmazonS3> _s3ClientMock;
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly S3StorageService _service;
        private readonly string _bucketName = "test-bucket";

        public S3StorageServiceTests()
        {
            _s3ClientMock = new Mock<IAmazonS3>();
            _configurationMock = new Mock<IConfiguration>();
            _configurationMock.Setup(c => c["AWS:BucketName"]).Returns(_bucketName);

            _service = new S3StorageService(_s3ClientMock.Object, _configurationMock.Object);
        }

        [Fact]
        public async Task UploadFileAsync_ShouldUploadFileAndReturnUrl()
        {
            // Arrange
            var content = "file content";
            var fileName = "test-image.png";
            var contentType = "image/png";
            using var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(content));

            // Act
            var result = await _service.UploadFileAsync(fileStream, fileName, contentType);

            // Assert
            _s3ClientMock.Verify(x => x.PutObjectAsync(
                It.Is<PutObjectRequest>(r => 
                    r.BucketName == _bucketName && 
                    r.Key == fileName && 
                    r.ContentType == contentType),
                default
            ), Times.Once);

            result.Should().Be($"https://{_bucketName}.s3.amazonaws.com/{fileName}");
        }
    }
}
