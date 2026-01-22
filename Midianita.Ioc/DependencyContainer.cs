using Amazon.DynamoDBv2;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Core.Interfaces;
using Midianita.Infrastructure.Repositories;
using Midianita.Infrastructure.Services;
using System.Net.Http;

namespace Midianita.Ioc
{
    public static class DependencyContainer
    {
        public static IServiceCollection AddInfrastructureDependencies(this IServiceCollection services, IConfiguration configuration)
        {
            // AWS Options (loads from appsettings.json "AWS" section)
            services.AddDefaultAWSOptions(configuration.GetAWSOptions());
            services.AddAWSService<IAmazonDynamoDB>();
            services.AddAWSService<IAmazonSQS>();

            services.AddHttpClient();

            services.AddScoped<IDesignRepository, DynamoDbDesignRepository>();
            services.AddScoped<IVertexAiService>(sp =>
            {
                var httpClient = sp.GetRequiredService<HttpClient>();
                // Ideally from config
                var projectId = configuration["Google:ProjectId"] ?? "default-project";
                return new VertexAiService(httpClient, projectId);
            });
            services.AddSingleton<IAuditPublisher, SqsAuditPublisher>();

            return services;
        }
    }
}
