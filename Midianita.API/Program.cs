using Midianita.API.Filters;
using Midianita.Ioc;
using Midianita.API.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Use the new Dynamic Authentication extension
builder.Services.AddDynamicAuthentication(builder.Configuration);

builder.Services.AddInfrastructureDependencies(builder.Configuration);

builder.Services.AddControllers(options =>
{
    options.Filters.Add<AuditActionFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Ensure Authentication is used before Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
