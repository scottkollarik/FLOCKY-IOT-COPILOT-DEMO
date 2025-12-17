using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/flocks/{flockId}/telemetry")]
public class RawTelemetryController : ControllerBase
{
    private readonly IRawTelemetryRepository _rawTelemetryRepository;
    private readonly ITenantContext _tenantContext;

    public RawTelemetryController(IRawTelemetryRepository rawTelemetryRepository, ITenantContext tenantContext)
    {
        _rawTelemetryRepository = rawTelemetryRepository;
        _tenantContext = tenantContext;
    }

    [HttpGet("latest")]
    public async Task<IActionResult> GetLatestAsync([FromRoute] string flockId, CancellationToken cancellationToken)
    {
        var latest = await _rawTelemetryRepository.GetLatestAsync(_tenantContext.TenantId, flockId, cancellationToken);
        if (latest == null)
        {
            return NotFound(new { error = $"No raw telemetry found for flock '{flockId}'." });
        }

        return Ok(latest);
    }
}

