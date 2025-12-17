using FlockCopilot.Api.Models;
using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/flocks/{flockId}")]
public class FlockHistoryController : ControllerBase
{
    private readonly INormalizedFlockRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<FlockHistoryController> _logger;

    public FlockHistoryController(
        INormalizedFlockRepository repository,
        ITenantContext tenantContext,
        ILogger<FlockHistoryController> logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves time-series historical performance data for a specific flock within a lookback window.
    /// </summary>
    /// <param name="flockId">The unique identifier for the flock</param>
    /// <param name="window">Time window in format: '7d' (days) or '72h' (hours). Defaults to '7d'.</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Historical performance records ordered by timestamp descending</returns>
    /// <response code="200">Returns the historical performance data</response>
    [HttpGet("history")]
    [ProducesResponseType(typeof(FlockHistoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(string flockId, [FromQuery] string? window, CancellationToken ct)
    {
        var timeWindow = ResolveWindow(window);
        _logger.LogInformation("Retrieving {Window} worth of history for flock {FlockId}", timeWindow, flockId);

        var history = await _repository.GetHistoryAsync(_tenantContext.TenantId, flockId, timeWindow, ct);

        var response = new FlockHistoryResponse
        {
            TenantId = _tenantContext.TenantId,
            FlockId = flockId,
            Window = timeWindow.ToString(),
            Records = history.ToList()
        };

        return Ok(response);
    }

    private static TimeSpan ResolveWindow(string? windowRaw)
    {
        if (string.IsNullOrWhiteSpace(windowRaw))
        {
            return TimeSpan.FromDays(7);
        }

        if (windowRaw.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(windowRaw.TrimEnd('d', 'D'), out var days))
        {
            return TimeSpan.FromDays(days);
        }

        if (windowRaw.EndsWith("h", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(windowRaw.TrimEnd('h', 'H'), out var hours))
        {
            return TimeSpan.FromHours(hours);
        }

        return TimeSpan.FromDays(7);
    }
}

public class FlockHistoryResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string FlockId { get; set; } = string.Empty;
    public string Window { get; set; } = string.Empty;
    public List<NormalizedFlockPerformance> Records { get; set; } = new();
}
