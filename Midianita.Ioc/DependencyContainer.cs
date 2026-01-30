using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Aplication.Interface;
using Midianita.Aplication.Service;
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

            var awsRegion = configuration["AWS:Region"];
            var tableName = configuration["DynamoDb:TableName"] ?? "Midianita_Dev_Designs";
            var auditQueueUrl = configuration["AWS:AuditQueueUrl"] ?? "Midianita_Dev_AuditQueue";
            var projectId = configuration["GoogleCloud:ProjectId"];
            var location = configuration["GoogleCloud:Location"];

            services.AddScoped<IDesignRepository>(sp =>
            {
                var client = sp.GetRequiredService<IAmazonDynamoDB>();
                return new DynamoDbDesignRepository(client, tableName);
            });

            services.AddScoped<IDesignsService, DesignsService>();

            services.AddScoped<ITokenProvider, GoogleTokenProvider>();

            services.AddScoped<IVertexAiService>(sp =>
            {
                var httpClient = sp.GetRequiredService<HttpClient>();
                var tokenProvider = sp.GetRequiredService<ITokenProvider>();
                return new VertexAiService(httpClient, tokenProvider, projectId, location);
            });

            services.AddScoped<IAuditPublisher>(sp =>
            {
                var sqsClient = sp.GetRequiredService<IAmazonSQS>();
                return new SqsAuditPublisher(sqsClient, auditQueueUrl);
            });

            services.AddSingleton<IAmazonDynamoDB>(sp =>
               new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(awsRegion)));

            services.AddSingleton<IAmazonSQS>(sp =>
               new AmazonSQSClient(Amazon.RegionEndpoint.GetBySystemName(awsRegion)));

            return services;
        }
    }
}
