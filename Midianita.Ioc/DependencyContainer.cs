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
        public static IServiceCollection AddInfrastructureDependencies(this IServiceCollection services, string awsRegion)
        {
            services.AddHttpContextAccessor();
            services.AddHttpClient();

            services.AddScoped<IDesignRepository, DynamoDbDesignRepository>();
            services.AddScoped<IDesignsService, DesignsService>();

            services.AddScoped<IVertexAiService>(sp =>
            {
                var httpClient = sp.GetRequiredService<HttpClient>();
                return new VertexAiService(httpClient);
            });

            services.AddScoped<IAuditPublisher>(sp =>
            {
                var sqsClient = sp.GetRequiredService<IAmazonSQS>();
                return new SqsAuditPublisher(sqsClient);
            });

            services.AddSingleton<IAmazonDynamoDB>(sp =>
               new AmazonDynamoDBClient(Amazon.RegionEndpoint.GetBySystemName(awsRegion)));

            services.AddSingleton<IAmazonSQS>(sp =>
               new AmazonSQSClient(Amazon.RegionEndpoint.GetBySystemName(awsRegion)));

            return services;
        }
    }
}
