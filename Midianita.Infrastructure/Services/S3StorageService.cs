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

        public async Task<string> PromoteAssetAsync(string tempKey, string userId)
        {
            if (string.IsNullOrEmpty(tempKey))
            {
                throw new ArgumentNullException(nameof(tempKey), "Temporary key cannot be null or empty.");
            }

            try
            {
                // Extract filename from the tempKey (assuming it might be a full URL or just a key)
                // If it's a full URL, we need to extract the key part. 
                // However, based on the requirement "tempKey", we assume it is the object key.
                // Standardizing: if it comes as a URL, we try to extract the last part.
                var fileName = Path.GetFileName(tempKey);
                
                // Construct destination key: users/{userId}/assets/{fileName}
                var destinationKey = $"users/{userId}/assets/{fileName}";

                var copyRequest = new CopyObjectRequest
                {
                    SourceBucket = _bucketName,
                    SourceKey = tempKey,
                    DestinationBucket = _bucketName,
                    DestinationKey = destinationKey
                };

                await _s3Client.CopyObjectAsync(copyRequest);

                var deleteRequest = new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = tempKey
                };

                await _s3Client.DeleteObjectAsync(deleteRequest);

                return $"https://{_bucketName}.s3.amazonaws.com/{destinationKey}";
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
            {
                throw new FileNotFoundException($"The temporary asset '{tempKey}' was not found.", tempKey, ex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to promote asset '{tempKey}' for user '{userId}'.", ex);
            }
        }
    }
}
