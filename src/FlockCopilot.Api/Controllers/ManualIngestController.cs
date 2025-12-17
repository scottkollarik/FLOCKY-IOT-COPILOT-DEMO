using Azure.Identity;
using Azure.Storage.Blobs;
using FlockCopilot.Api.Models;
using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/ingestion")]
public class ManualIngestController : ControllerBase
{
    private readonly ILogger<ManualIngestController> _logger;
    private readonly IManualReportExtractor _extractor;
    private readonly INormalizer _normalizer;
    private readonly INormalizedFlockRepository _repository;
    private readonly ITenantContext _tenantContext;

    public ManualIngestController(
        ILogger<ManualIngestController> logger,
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

    /// <summary>
    /// Ingests a manual flock report from Azure Blob Storage, extracts structured data via AI, normalizes it, and stores it in Cosmos DB.
    /// </summary>
    /// <param name="request">Manual ingest request containing blob URL, optional content type, and optional flock ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Ingestion result with normalized record ID and confidence score</returns>
    /// <response code="200">Manual report successfully processed and normalized</response>
    /// <response code="400">Invalid request (missing blobUrl)</response>
    [HttpPost("manual")]
    [ProducesResponseType(typeof(ManualIngestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestManual([FromBody] ManualIngestRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BlobUrl))
        {
            return BadRequest(new { error = "blobUrl is required in the payload." });
        }

        var blobUri = new Uri(request.BlobUrl);
        var blobClient = new BlobClient(blobUri, new DefaultAzureCredential());

        await using var stream = new MemoryStream();
        await blobClient.DownloadToAsync(stream, ct);
        stream.Position = 0;

        var report = await _extractor.ExtractAsync(stream, request.ContentType ?? "application/octet-stream", ct);
        var flockId = string.IsNullOrWhiteSpace(request.FlockId) ? ResolveFlockId(blobUri) : request.FlockId;

        var raw = new FlockRaw
        {
            TenantId = _tenantContext.TenantId,
            FlockId = flockId,
            Source = "manual",
            IngestedAt = DateTimeOffset.UtcNow,
            ManualReport = report
        };

        var normalized = _normalizer.Normalize(raw);
        await _repository.UpsertAsync(normalized, ct);

        _logger.LogInformation("Manual report for flock {FlockId} ingested with confidence {Confidence}.", flockId, normalized.Confidence);

        return Ok(new ManualIngestResponse
        {
            Message = "Manual report normalized.",
            Id = normalized.Id,
            Confidence = normalized.Confidence
        });
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

public class ManualIngestResponse
{
    public string Message { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public double Confidence { get; set; }
}
