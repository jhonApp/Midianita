using Amazon.DynamoDBv2;
using Amazon.S3;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Midianita.Worker.Extensions;
using Midianita.Worker.Interfaces;
using Midianita.Worker.Workers;

namespace Midianita.Worker
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var region = Amazon.RegionEndpoint.USEast1;

            // 1. Setup Configuration
            // FORCE DEV CONFIGURATION (Local Console App Only)
            Environment.SetEnvironmentVariable("AWS__BucketName", "midianita-dev-assets");
            Environment.SetEnvironmentVariable("AWS__Region", "us-east-1");

            var inMemorySettings = new Dictionary<string, string> {
                {"GCP:ProjectId", "mythic-inn-144217"},
                {"GCP:Location", "us-central1"}
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings!)
                .AddEnvironmentVariables()
                .Build();

            Console.WriteLine("Starting Worker with Parallel Polling (Refactored)...");

            // 2. Setup Dependency Injection
            var services = new ServiceCollection();

            // Use Extension Method for Core Services
            services.AddWorkerServices(configuration);

            // Register AWS Clients Manually (for specific region control)
            services.AddSingleton<IAmazonS3>(new AmazonS3Client(region));
            services.AddSingleton<IAmazonDynamoDB>(new AmazonDynamoDBClient(region));
            services.AddSingleton<IAmazonSQS>(new AmazonSQSClient(region));

            // Register Workers
            services.AddSingleton<IQueueWorker, GenerationWorker>();
            services.AddSingleton<IQueueWorker, CleanupWorker>();

            var serviceProvider = services.BuildServiceProvider();

            // 3. Resolve and Run Workers
            var workers = serviceProvider.GetServices<IQueueWorker>();
            var cts = new CancellationTokenSource();

            var tasks = new List<Task>();
            foreach (var worker in workers)
            {
                tasks.Add(worker.ExecuteAsync(cts.Token));
            }

            Console.WriteLine($"Running {tasks.Count} workers in parallel. Press Ctrl+C to stop.");
            
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Stopping workers...");
            };

            await Task.WhenAll(tasks);
        }
    }
}
