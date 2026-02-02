using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Core.Interfaces;
using Midianita.Infrastructure.Repositories;
using Midianita.Infrastructure.Services;

namespace Midianita.Ioc
{
    public static class DependencyContainer
    {
        public static IServiceCollection AddInfrastructureDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();
            services.AddHttpClient();

            // --- 1. Configurações ---
            var awsRegion = configuration["AWS:Region"] ?? "us-east-1";

            // --- 2. AWS Clients (Mantive sua lógica de LocalStack) ---
            services.AddSingleton<IAmazonDynamoDB>(sp => CreateDynamoDbClient(configuration));
            services.AddSingleton<IAmazonSQS>(sp => CreateSqsClient(configuration));
            services.AddSingleton<IAmazonS3>(sp => CreateS3Client(configuration));

            services.AddScoped<ITokenProvider, GoogleTokenProvider>();
            services.AddScoped<IVertexAiService, VertexAiService>();

            // --- 3. Repositories & Services ---
            services.AddScoped<IDesignRepository, DynamoDbDesignRepository>(sp =>
            {
                var client = sp.GetRequiredService<IAmazonDynamoDB>();
                var tableName = configuration["DynamoDb:TableName"] ?? "Designs";
                return new DynamoDbDesignRepository(client, tableName);
            });

            services.AddScoped<IQueuePublisher, SqsPublisher>();

            services.AddScoped<IAuditPublisher>(sp =>
            {
                var sqsClient = sp.GetRequiredService<IAmazonSQS>();
                var auditUrl = configuration["AWS:AuditQueueUrl"];
                return new SqsAuditPublisher(sqsClient, auditUrl);
            });

            services.AddScoped<IStorageService, S3StorageService>();

            return services;
        }

        private static IAmazonDynamoDB CreateDynamoDbClient(IConfiguration configuration)
        {
            var serviceUrl = configuration["AWS:ServiceUrl"];
            if (!string.IsNullOrEmpty(serviceUrl))
            {
                return new AmazonDynamoDBClient(new Amazon.Runtime.BasicAWSCredentials("test", "test"),
                    new AmazonDynamoDBConfig { ServiceURL = serviceUrl });
            }
            return new AmazonDynamoDBClient(Amazon.RegionEndpoint.USEast1);
        }

        private static IAmazonSQS CreateSqsClient(IConfiguration configuration)
        {
            var serviceUrl = configuration["AWS:ServiceUrl"];
            if (!string.IsNullOrEmpty(serviceUrl))
            {
                return new AmazonSQSClient(new Amazon.Runtime.BasicAWSCredentials("test", "test"),
                    new AmazonSQSConfig { ServiceURL = serviceUrl });
            }
            return new AmazonSQSClient(Amazon.RegionEndpoint.USEast1);
        }

        private static IAmazonS3 CreateS3Client(IConfiguration configuration)
        {
            var serviceUrl = configuration["AWS:ServiceUrl"];
            if (!string.IsNullOrEmpty(serviceUrl))
            {
                return new AmazonS3Client(new Amazon.Runtime.BasicAWSCredentials("test", "test"),
                    new AmazonS3Config { ServiceURL = serviceUrl, ForcePathStyle = true });
            }
            return new AmazonS3Client(Amazon.RegionEndpoint.USEast1);
        }
    }
}