using FlockCopilot.Functions.Infrastructure;
using FlockCopilot.Functions.Services;
using FlockCopilot.Functions.Services.Repositories;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddSingleton<ITenantContext, TenantContext>();
        services.AddSingleton<INormalizer, Normalizer>();
        services.AddHttpClient<IManualReportExtractor, ManualReportExtractor>();
        services.AddSingleton<CosmosContainerProvider>();

        services.AddSingleton<INormalizedFlockRepository>(sp =>
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
    })
    .Build();

host.Run();
