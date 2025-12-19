using FlockCopilot.Api.Infrastructure;
using FlockCopilot.Api.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Api.Services.Repositories;

public sealed class CosmosAnomalyRepository : IAnomalyRepository
{
    private readonly Container? _container;
    private readonly ILogger<CosmosAnomalyRepository> _logger;

    public CosmosAnomalyRepository(CosmosContainerProvider provider, ILogger<CosmosAnomalyRepository> logger)
    {
        _container = provider.GetAnomaliesContainer();
        _logger = logger;
    }

    public async Task UpsertManyAsync(IReadOnlyList<AnomalyRecord> anomalies, CancellationToken cancellationToken = default)
    {
        if (_container == null)
        {
            _logger.LogWarning("Cosmos anomalies container is not configured; anomalies will not be persisted.");
            return;
        }

        foreach (var anomaly in anomalies)
        {
            if (string.IsNullOrWhiteSpace(anomaly.Id) || string.IsNullOrWhiteSpace(anomaly.TenantId))
            {
                continue;
            }

            await _container.UpsertItemAsync(anomaly, new PartitionKey(anomaly.TenantId), cancellationToken: cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AnomalyRecord>> GetRecentAsync(
        string tenantId,
        TimeSpan lookback,
        string? flockId,
        CancellationToken cancellationToken = default)
    {
        if (_container == null)
        {
            return Array.Empty<AnomalyRecord>();
        }

        var since = DateTimeOffset.UtcNow.Subtract(lookback);

        // Use LINQ provider to keep it simple for demo-scale volumes.
        var queryable = _container
            .GetItemLinqQueryable<AnomalyRecord>(
                allowSynchronousQueryExecution: false,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(tenantId)
                })
            .Where(a => a.CapturedAt >= since);

        if (!string.IsNullOrWhiteSpace(flockId))
        {
            queryable = queryable.Where(a => a.FlockId == flockId);
        }

        var iterator = queryable
            .OrderByDescending(a => a.CapturedAt)
            .ToFeedIterator();

        var results = new List<AnomalyRecord>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        return results;
    }
}

