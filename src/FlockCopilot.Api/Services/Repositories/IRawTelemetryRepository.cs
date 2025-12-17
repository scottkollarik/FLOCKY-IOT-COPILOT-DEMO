using FlockCopilot.Api.Models;

namespace FlockCopilot.Api.Services.Repositories;

public interface IRawTelemetryRepository
{
    Task UpsertAsync(RawTelemetrySnapshot snapshot, CancellationToken cancellationToken = default);
    Task<RawTelemetrySnapshot?> GetLatestAsync(string tenantId, string flockId, CancellationToken cancellationToken = default);
}
