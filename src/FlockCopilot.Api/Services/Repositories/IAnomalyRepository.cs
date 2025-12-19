using FlockCopilot.Api.Models;

namespace FlockCopilot.Api.Services.Repositories;

public interface IAnomalyRepository
{
    Task UpsertManyAsync(IReadOnlyList<AnomalyRecord> anomalies, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnomalyRecord>> GetRecentAsync(
        string tenantId,
        TimeSpan lookback,
        string? flockId,
        CancellationToken cancellationToken = default);
}

