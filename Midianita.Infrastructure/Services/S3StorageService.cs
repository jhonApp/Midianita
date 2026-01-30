using Amazon.S3;
using Amazon.S3.Model;
using Midianita.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Midianita.Infrastructure.Services
{
    public class S3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public S3StorageService(IAmazonS3 s3Client, IConfiguration configuration)
        {
            _s3Client = s3Client;
            _bucketName = configuration["AWS:BucketName"] ?? "midianita-designs";
        }

        public async Task<string> UploadFileAsync(Stream fileStream, string fileName, string contentType)
        {
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = fileName,
                InputStream = fileStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(request);

            // Assuming standard S3 URL format. For production, consider using CloudFront or specific region endpoints.
            // Also depends on bucket policy/public access settings.
            return $"https://{_bucketName}.s3.amazonaws.com/{fileName}";
        }
    }
}
