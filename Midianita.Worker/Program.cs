using Midianita.Worker;
using Amazon.SQS;
using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAWSService<IAmazonSQS>();
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
