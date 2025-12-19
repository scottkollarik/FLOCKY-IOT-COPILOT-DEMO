using FlockCopilot.Api.Models;
using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/anomalies")]
public class AnomaliesController : ControllerBase
{
    private readonly IAnomalyRepository _repository;
    private readonly ITenantContext _tenantContext;

    public AnomaliesController(IAnomalyRepository repository, ITenantContext tenantContext)
    {
        _repository = repository;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AnomaliesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentAsync(
        [FromQuery] string? window,
        [FromQuery] string? flockId,
        CancellationToken cancellationToken)
    {
        var lookback = ResolveWindow(window);
        var records = await _repository.GetRecentAsync(_tenantContext.TenantId, lookback, flockId, cancellationToken);

        return Ok(new AnomaliesResponse
        {
            TenantId = _tenantContext.TenantId,
            FlockId = flockId,
            Window = lookback.ToString(),
            Records = records.ToList()
        });
    }

    private static TimeSpan ResolveWindow(string? windowRaw)
    {
        if (string.IsNullOrWhiteSpace(windowRaw))
        {
            return TimeSpan.FromHours(48);
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

        return TimeSpan.FromHours(48);
    }
}

public sealed class AnomaliesResponse
{
    public string TenantId { get; set; } = string.Empty;
    public string? FlockId { get; set; }
    public string Window { get; set; } = string.Empty;
    public List<AnomalyRecord> Records { get; set; } = new();
}

