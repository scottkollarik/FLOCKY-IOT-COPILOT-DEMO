using System.Net;
using FlockCopilot.Functions.Infrastructure;
using FlockCopilot.Functions.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Functions.Services.Repositories;

public class CosmosNormalizedFlockRepository : INormalizedFlockRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosNormalizedFlockRepository> _logger;

    public CosmosNormalizedFlockRepository(CosmosContainerProvider provider, ILogger<CosmosNormalizedFlockRepository> logger)
    {
        _container = provider.GetContainer() ?? throw new InvalidOperationException("Cosmos container is not configured.");
        _logger = logger;
    }

    public async Task UpsertAsync(NormalizedFlockPerformance entity, CancellationToken cancellationToken = default)
    {
        await _container.UpsertItemAsync(entity, new PartitionKey(entity.TenantId), cancellationToken: cancellationToken);
    }

    public async Task<NormalizedFlockPerformance?> GetLatestAsync(
        string tenantId,
        string flockId,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
            @"SELECT TOP 1 * FROM c 
              WHERE c.tenantId = @tenantId AND c.flockId = @flockId 
              ORDER BY c.timestamp DESC")
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@flockId", flockId);

        var iterator = _container.GetItemQueryIterator<NormalizedFlockPerformance>(query);
        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            return response.FirstOrDefault();
        }

        return null;
    }

    public async Task<IReadOnlyList<NormalizedFlockPerformance>> GetHistoryAsync(
        string tenantId,
        string flockId,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.Subtract(window);
        var query = new QueryDefinition(
            @"SELECT * FROM c 
              WHERE c.tenantId = @tenantId 
                AND c.flockId = @flockId 
                AND c.timestamp >= @since
              ORDER BY c.timestamp DESC")
            .WithParameter("@tenantId", tenantId)
            .WithParameter("@flockId", flockId)
            .WithParameter("@since", since);

        var results = new List<NormalizedFlockPerformance>();
        var iterator = _container.GetItemQueryIterator<NormalizedFlockPerformance>(query);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(response);
        }

        return results;
    }
}
