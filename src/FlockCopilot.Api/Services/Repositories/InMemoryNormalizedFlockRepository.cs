using System.Collections.Concurrent;
using FlockCopilot.Api.Models;

namespace FlockCopilot.Api.Services.Repositories;

public class InMemoryNormalizedFlockRepository : INormalizedFlockRepository
{
    private readonly ConcurrentDictionary<string, List<NormalizedFlockPerformance>> _store = new();

    public InMemoryNormalizedFlockRepository()
    {
        Seed();
    }

    public Task UpsertAsync(NormalizedFlockPerformance entity, CancellationToken cancellationToken = default)
    {
        var list = _store.GetOrAdd(Key(entity.TenantId, entity.FlockId), _ => new List<NormalizedFlockPerformance>());
        list.RemoveAll(x => x.Id == entity.Id);
        list.Add(entity);
        return Task.CompletedTask;
    }

    public Task<NormalizedFlockPerformance?> GetLatestAsync(string tenantId, string flockId, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(Key(tenantId, flockId), out var list);
        var result = list?.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<NormalizedFlockPerformance>> GetHistoryAsync(
        string tenantId,
        string flockId,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(Key(tenantId, flockId), out var list);
        var cutoff = DateTimeOffset.UtcNow.Subtract(window);
        var result = list?
            .Where(x => x.Timestamp >= cutoff)
            .OrderByDescending(x => x.Timestamp)
            .ToList() ?? new List<NormalizedFlockPerformance>();
        return Task.FromResult<IReadOnlyList<NormalizedFlockPerformance>>(result);
    }

    private static string Key(string tenantId, string flockId) => $"{tenantId}:{flockId}".ToLowerInvariant();

    private void Seed()
    {
        var tenant = "tenant-demo-123";
        var flockId = "flock-a";
        var history = new List<NormalizedFlockPerformance>();
        var start = DateTimeOffset.UtcNow.AddDays(-14);
        var random = new Random();

        for (var i = 0; i < 14; i++)
        {
            history.Add(new NormalizedFlockPerformance
            {
                TenantId = tenant,
                FlockId = flockId,
                Source = "iot",
                Timestamp = start.AddDays(i),
                Confidence = 0.85,
                Metrics = new PerformanceMetrics
                {
                    MortalityPercent = 6.5 + random.NextDouble(),
                    FeedConversionRatio = 1.9 + random.NextDouble() * 0.2,
                    AverageWeightLbs = 4.8 + random.NextDouble() * 0.3,
                    TemperatureAvgF = 88 + random.NextDouble() * 5,
                    HumidityPercent = 68 + random.NextDouble() * 4,
                    WaterIntakeLiters = 12 + random.NextDouble(),
                    FeedIntakeKg = 33 + random.NextDouble()
                }
            });
        }

        _store.TryAdd(Key(tenant, flockId), history);
    }
}
