using FlockCopilot.Api.Models;
using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/flocks/{flockId}")]
public class FlockPerformanceController : ControllerBase
{
    private readonly INormalizedFlockRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FlockPerformanceController> _logger;

    public FlockPerformanceController(
        INormalizedFlockRepository repository,
        ITenantContext tenantContext,
        ILogger<FlockPerformanceController> logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the latest normalized performance snapshot for a specific flock.
    /// </summary>
    /// <param name="flockId">The unique identifier for the flock</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>The most recent normalized flock performance data</returns>
    /// <response code="200">Returns the latest performance data</response>
    /// <response code="404">No performance data found for the specified flock</response>
    [HttpGet("performance")]
    [ProducesResponseType(typeof(NormalizedFlockPerformance), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPerformance(string flockId, CancellationToken ct)
    {
        _logger.LogInformation("Retrieving latest performance for flock {FlockId}", flockId);

        var result = await _repository.GetLatestAsync(_tenantContext.TenantId, flockId, ct);

        if (result == null)
        {
            return NotFound(new { error = $"No normalized data found for flock '{flockId}'." });
        }

        return Ok(result);
    }
}
