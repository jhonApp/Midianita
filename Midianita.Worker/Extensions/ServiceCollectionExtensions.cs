using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using Midianita.Core.Interfaces;
using Midianita.Infrastructure.Services;
using System.Net.Http;

namespace Midianita.Worker.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddWorkerServices(this IServiceCollection services, IConfiguration configuration)
        {
            // Register Configuration
            services.AddSingleton(configuration);
            services.AddLogging();
            services.AddHttpClient();

            // Register AWS Services
            // Note: In a real scenario, we might use AddAWSService<T> from generic host, 
            // but for this refactor we stick to the manual client registration pattern established in Program.cs
            // to support the specific region/local dev setup passed from Program.cs if needed.
            // However, Program.cs is refactoring to pass pre-configured clients or let DI handle it.
            // Let's assume standard DI best practice: Clients are registered as singletons.
            
            // We'll rely on the caller to register the specific AWS Client instances 
            // if they need custom config (like localstack vs real AWS), 
            // OR we can register them here if we trust the default config.
            // To respect the requirement "Move all services.AddSingleton... to this class",
            // we will accept the clients or build them here. 
            // Given the local debug setup, it's safer to register the services that rely on them.
            
            services.AddScoped<ITokenProvider, GoogleTokenProvider>();
            services.AddScoped<IVertexAiService>(sp =>
            {
                var http = sp.GetRequiredService<HttpClient>();
                var token = sp.GetRequiredService<ITokenProvider>();
                var config = sp.GetRequiredService<IConfiguration>();
                return new VertexAiService(http, token, config);
            });
            services.AddScoped<IStorageService, S3StorageService>();
            
            // Register Worker Function Logic
            services.AddSingleton<Function>(); // The Lambda Handler logic

            return services;
        }
    }
}
