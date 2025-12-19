using FlockCopilot.Api.Infrastructure;
using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Load optional local overrides (gitignored) so developers can keep secrets/config outside source control
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

// Add controllers
builder.Services.AddControllers();

// Add OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FlockCopilot API",
        Version = "v1",
        Description = "Flock performance analytics API for Azure AI Foundry agents. Provides endpoints for retrieving normalized flock performance data from IoT telemetry and manual reports.",
        Contact = new OpenApiContact
        {
            Name = "FlockCopilot Team"
        }
    });
});

// Register application services (migrated from Functions Program.cs)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddSingleton<INormalizer, Normalizer>();
builder.Services.AddSingleton<IAnomalyDetector, AnomalyDetector>();
builder.Services.AddHttpClient<IManualReportExtractor, ManualReportExtractor>();
builder.Services.AddHttpClient<IAzureOpenAiChatService, AzureOpenAiChatService>();
builder.Services.AddSingleton<CosmosContainerProvider>();
builder.Services.AddHttpClient<IKnowledgeSearchService, KnowledgeSearchService>();

// Repository with Cosmos DB fallback to in-memory
builder.Services.AddSingleton<INormalizedFlockRepository>(sp =>
{
    var provider = sp.GetRequiredService<CosmosContainerProvider>();
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var container = provider.GetContainer();

    if (container == null)
    {
        var fallbackLogger = loggerFactory.CreateLogger<InMemoryNormalizedFlockRepository>();
        fallbackLogger.LogWarning("Using in-memory repository because Cosmos DB is not configured.");
        return new InMemoryNormalizedFlockRepository();
    }

    var repoLogger = loggerFactory.CreateLogger<CosmosNormalizedFlockRepository>();
    return new CosmosNormalizedFlockRepository(provider, repoLogger);
});

builder.Services.AddSingleton<IRawTelemetryRepository>(sp =>
{
    var provider = sp.GetRequiredService<CosmosContainerProvider>();
    var logger = sp.GetRequiredService<ILogger<CosmosRawTelemetryRepository>>();
    return new CosmosRawTelemetryRepository(provider, logger);
});

builder.Services.AddSingleton<IAnomalyRepository>(sp =>
{
    var provider = sp.GetRequiredService<CosmosContainerProvider>();
    var logger = sp.GetRequiredService<ILogger<CosmosAnomalyRepository>>();
    return new CosmosAnomalyRepository(provider, logger);
});

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("API is running"));

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FlockCopilot API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
