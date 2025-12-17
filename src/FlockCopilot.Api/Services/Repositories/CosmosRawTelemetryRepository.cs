using FlockCopilot.Api.Infrastructure;
using FlockCopilot.Api.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Api.Services.Repositories;

public sealed class CosmosRawTelemetryRepository : IRawTelemetryRepository
{
    private readonly CosmosContainerProvider _provider;
    private readonly ILogger<CosmosRawTelemetryRepository> _logger;

    public CosmosRawTelemetryRepository(CosmosContainerProvider provider, ILogger<CosmosRawTelemetryRepository> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task UpsertAsync(RawTelemetrySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var container = _provider.GetTelemetryContainer();
        if (container == null)
        {
            _logger.LogDebug("Telemetry container not configured; skipping raw telemetry persistence.");
            return;
        }

        await container.UpsertItemAsync(snapshot, new PartitionKey(snapshot.TenantId), cancellationToken: cancellationToken);
    }

    public async Task<RawTelemetrySnapshot?> GetLatestAsync(string tenantId, string flockId, CancellationToken cancellationToken = default)
    {
        var container = _provider.GetTelemetryContainer();
        if (container == null)
        {
            return null;
        }

        var query = new QueryDefinition(
                @"SELECT TOP 1 * FROM c
                  WHERE c.tenantId = @tenantId AND c.flockId = @flockId
                  ORDER BY c.capturedAt DESC")
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@flockId", flockId);

        var iterator = container.GetItemQueryIterator<RawTelemetrySnapshot>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(tenantId)
        });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            return response.FirstOrDefault();
        }

        return null;
    }
}
