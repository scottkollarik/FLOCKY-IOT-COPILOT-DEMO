using System.Net;
using System.Text.Json;
using FlockCopilot.Functions.Services;
using FlockCopilot.Functions.Services.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Functions.Functions;

public class GetFlockPerformance
{
    private readonly INormalizedFlockRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<GetFlockPerformance> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public GetFlockPerformance(
        INormalizedFlockRepository repository,
        ITenantContext tenantContext,
        ILogger<GetFlockPerformance> logger)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [Function("GetFlockPerformance")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "flocks/{flockId}/performance")]
        HttpRequestData req,
        string flockId)
    {
        _logger.LogInformation("Retrieving latest performance for flock {FlockId}", flockId);

        var result = await _repository.GetLatestAsync(_tenantContext.TenantId, flockId, req.FunctionContext.CancellationToken);
        if (result == null)
        {
            return await WriteJsonAsync(req, HttpStatusCode.NotFound, new { error = $"No normalized data found for flock '{flockId}'." });
        }

        return await WriteJsonAsync(req, HttpStatusCode.OK, result);
    }

    private static async Task<HttpResponseData> WriteJsonAsync(HttpRequestData req, HttpStatusCode status, object payload)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOptions));
        return response;
    }
}
