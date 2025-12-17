using System.Net;
using System.Text.Json;
using System.Web;
using FlockCopilot.Functions.Services;
using FlockCopilot.Functions.Services.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Functions.Functions;

public class GetFlockHistory
{
    private readonly INormalizedFlockRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<GetFlockHistory> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GetFlockHistory(
        INormalizedFlockRepository repository,
        ITenantContext tenantContext,
        ILogger<GetFlockHistory> logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [Function("GetFlockHistory")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "flocks/{flockId}/history")]
        HttpRequestData req,
        string flockId)
    {
        var window = ResolveWindow(req.Url);
        _logger.LogInformation("Retrieving {Window} worth of history for flock {FlockId}", window, flockId);

        var history = await _repository.GetHistoryAsync(_tenantContext.TenantId, flockId, window, req.FunctionContext.CancellationToken);
        var payload = new
        {
            tenantId = _tenantContext.TenantId,
            flockId,
            window = window.ToString(),
            records = history
        };

        return await WriteJsonAsync(req, HttpStatusCode.OK, payload);
    }

    private static TimeSpan ResolveWindow(Uri url)
    {
        var query = HttpUtility.ParseQueryString(url.Query);
        var windowRaw = query["window"];
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

    private static async Task<HttpResponseData> WriteJsonAsync(HttpRequestData req, HttpStatusCode status, object payload)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOptions));
        return response;
    }
}
