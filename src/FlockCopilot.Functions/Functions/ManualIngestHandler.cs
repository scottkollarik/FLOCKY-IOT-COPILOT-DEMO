using Azure.Identity;
using Azure.Storage.Blobs;
using FlockCopilot.Functions.Models;
using FlockCopilot.Functions.Services;
using FlockCopilot.Functions.Services.Repositories;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FlockCopilot.Functions.Functions;

public class ManualIngestHandler
{
    private readonly ILogger<ManualIngestHandler> _logger;
    private readonly IManualReportExtractor _extractor;
    private readonly INormalizer _normalizer;
    private readonly INormalizedFlockRepository _repository;
    private readonly ITenantContext _tenantContext;

    public ManualIngestHandler(
        ILogger<ManualIngestHandler> logger,
        IManualReportExtractor extractor,
        INormalizer normalizer,
        INormalizedFlockRepository repository,
        ITenantContext tenantContext)
    {
        _logger = logger;
        _extractor = extractor;
        _normalizer = normalizer;
        _repository = repository;
        _tenantContext = tenantContext;
    }

    [Function("ManualIngestHandler")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ingestion/manual")]
        HttpRequestData req)
    {
        var body = await JsonSerializer.DeserializeAsync<ManualIngestRequest>(req.Body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new ManualIngestRequest();

        if (string.IsNullOrWhiteSpace(body.BlobUrl))
        {
            var error = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
            await error.WriteStringAsync("blobUrl is required in the payload.");
            return error;
        }

        var blobUri = new Uri(body.BlobUrl);
        var blobClient = new BlobClient(blobUri, new DefaultAzureCredential());

        await using var stream = new MemoryStream();
        await blobClient.DownloadToAsync(stream);
        stream.Position = 0;

        var report = await _extractor.ExtractAsync(stream, body.ContentType ?? "application/octet-stream", req.FunctionContext.CancellationToken);
        var flockId = string.IsNullOrWhiteSpace(body.FlockId) ? ResolveFlockId(blobUri) : body.FlockId;

        var raw = new FlockRaw
        {
            TenantId = _tenantContext.TenantId,
            FlockId = flockId,
            Source = "manual",
            IngestedAt = DateTimeOffset.UtcNow,
            ManualReport = report
        };

        var normalized = _normalizer.Normalize(raw);
        await _repository.UpsertAsync(normalized, req.FunctionContext.CancellationToken);

        _logger.LogInformation("Manual report for flock {FlockId} ingested with confidence {Confidence}.", flockId, normalized.Confidence);

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        await response.WriteStringAsync(JsonSerializer.Serialize(new
        {
            message = "Manual report normalized.",
            normalized.Id,
            normalized.Confidence
        }));
        return response;
    }

    private static string ResolveFlockId(Uri blobUri)
    {
        var blobName = new BlobUriBuilder(blobUri).BlobName;
        var segments = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2)
        {
            return segments[1];
        }

        return "unknown-flock";
    }
}

public class ManualIngestRequest
{
    public string? BlobUrl { get; set; }
    public string? ContentType { get; set; }
    public string? FlockId { get; set; }
}
