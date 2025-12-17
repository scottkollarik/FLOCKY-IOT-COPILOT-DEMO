using FlockCopilot.Functions.Models;

namespace FlockCopilot.Functions.Services.Repositories;

public interface INormalizedFlockRepository
{
    Task UpsertAsync(NormalizedFlockPerformance entity, CancellationToken cancellationToken = default);

    Task<NormalizedFlockPerformance?> GetLatestAsync(
        string tenantId,
        string flockId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NormalizedFlockPerformance>> GetHistoryAsync(
        string tenantId,
        string flockId,
        TimeSpan window,
        CancellationToken cancellationToken = default);
}
