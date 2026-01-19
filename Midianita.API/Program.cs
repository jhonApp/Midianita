using Midianita.Core.Interfaces;
using Midianita.Infrastructure.Repositories;
using Midianita.Infrastructure.Services;
using Midianita.API.Filters;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using Amazon.Extensions.NETCore.Setup;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// AWS Options (loads from appsettings.json "AWS" section)
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddAWSService<IAmazonSQS>();

builder.Services.AddHttpClient();

builder.Services.AddScoped<IDesignRepository, DynamoDbDesignRepository>();
builder.Services.AddScoped<IVertexAiService>(sp => 
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    // Ideally from config
    var projectId = builder.Configuration["Google:ProjectId"] ?? "default-project"; 
    return new VertexAiService(httpClient, projectId);
});
builder.Services.AddSingleton<IAuditPublisher, SqsAuditPublisher>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuditActionFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
