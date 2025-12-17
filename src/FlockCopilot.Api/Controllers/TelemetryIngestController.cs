using FlockCopilot.Api.Models;
using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/telemetry")]
public class TelemetryIngestController : ControllerBase
{
    private readonly ILogger<TelemetryIngestController> _logger;
    private readonly INormalizer _normalizer;
    private readonly INormalizedFlockRepository _repository;
    private readonly IRawTelemetryRepository _rawTelemetryRepository;
    private readonly ITenantContext _tenantContext;

    public TelemetryIngestController(
        ILogger<TelemetryIngestController> logger,
        INormalizer normalizer,
        INormalizedFlockRepository repository,
        IRawTelemetryRepository rawTelemetryRepository,
        ITenantContext tenantContext)
    {
        _logger = logger;
        _normalizer = normalizer;
        _repository = repository;
        _rawTelemetryRepository = rawTelemetryRepository;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Ingests IoT telemetry snapshot from simulated or real flock devices.
    /// </summary>
    /// <param name="telemetry">Telemetry snapshot with sensor readings</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Ingestion result with normalized record ID and confidence score</returns>
    /// <response code="200">Telemetry successfully processed and normalized</response>
    /// <response code="400">Invalid telemetry data</response>
    [HttpPost("ingest")]
    [ProducesResponseType(typeof(TelemetryIngestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> IngestTelemetry([FromBody] TelemetryIngestRequest telemetry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(telemetry.FlockId))
        {
            return BadRequest(new { error = "flockId is required." });
        }

        var now = DateTimeOffset.UtcNow;
        var snapshotId = telemetry.SnapshotId ?? Guid.NewGuid().ToString("N");
        var capturedAt = telemetry.CapturedAt ?? now;

        // Create FlockRaw from IoT telemetry
        var raw = new FlockRaw
        {
            TenantId = telemetry.TenantId ?? _tenantContext.TenantId,
            FlockId = telemetry.FlockId,
            Source = telemetry.Source ?? "iot",
            IngestedAt = telemetry.IngestedAt ?? now,
            Telemetry = new TelemetrySnapshot
            {
                SnapshotId = snapshotId,
                CapturedAt = capturedAt,
                Sensors = telemetry.Sensors?
                    .Select(s => new SensorSample
                    {
                        SensorId = s.SensorId,
                        Zone = s.Zone,
                        SensorType = s.SensorType,
                        TemperatureAvgF = s.TemperatureAvgF,
                        HumidityPercent = s.HumidityPercent,
                        Co2Ppm = s.Co2Ppm,
                        Nh3Ppm = s.Nh3Ppm,
                        SoundDbAvg = s.SoundDbAvg,
                        VocalizationRatePerMin = s.VocalizationRatePerMin,
                        BagokStressScore = s.BagokStressScore,
                        WaterIntakeLiters = s.WaterIntakeLiters,
                        FeedIntakeKg = s.FeedIntakeKg,
                        MortalityPercent = s.MortalityPercent
                    })
                    .ToList() ?? new List<SensorSample>(),
                Aggregates = new AggregatedTelemetryMetrics
                {
                    MortalityPercent = telemetry.Metrics.MortalityPercent,
                    FeedConversionRatio = telemetry.Metrics.FeedConversionRatio,
                    AverageWeightLbs = telemetry.Metrics.AverageWeightLbs,
                    WaterIntakeLiters = telemetry.Metrics.WaterIntakeLiters,
                    FeedIntakeKg = telemetry.Metrics.FeedIntakeKg,
                    HumidityPercent = telemetry.Metrics.HumidityPercent,
                    TemperatureAvgF = telemetry.Metrics.TemperatureAvgF,
                    TemperatureSpikeEvents = telemetry.Metrics.TemperatureSpikeEvents,
                    Co2AvgPpm = telemetry.Metrics.Co2AvgPpm,
                    Nh3AvgPpm = telemetry.Metrics.Nh3AvgPpm,
                    AffectedZoneCount = telemetry.Metrics.AffectedZoneCount
                },
                Events = telemetry.Events?
                    .Select(e => new TelemetryEvent
                    {
                        Type = e.Type,
                        Severity = e.Severity ?? "info",
                        Description = e.Description,
                        OccurredAt = e.OccurredAt
                    })
                    .ToList() ?? new List<TelemetryEvent>()
            }
        };

        // Persist raw snapshot for analytics / traceability.
        var rawSnapshot = new RawTelemetrySnapshot
        {
            Id = snapshotId,
            TenantId = raw.TenantId,
            FlockId = raw.FlockId,
            Source = raw.Source,
            CapturedAt = capturedAt,
            IngestedAt = raw.IngestedAt,
            Sensors = raw.Telemetry.Sensors,
            Aggregates = raw.Telemetry.Aggregates,
            Events = raw.Telemetry.Events
        };

        var normalized = _normalizer.Normalize(raw);
        try
        {
            await _rawTelemetryRepository.UpsertAsync(rawSnapshot, ct);
            await _repository.UpsertAsync(normalized, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist telemetry for flock {FlockId}", telemetry.FlockId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "Failed to persist telemetry.",
                exception = ex.GetType().Name,
                message = ex.Message
            });
        }

        _logger.LogInformation(
            "IoT telemetry for flock {FlockId} ingested with confidence {Confidence}.",
            telemetry.FlockId,
            normalized.Confidence);

        return Ok(new TelemetryIngestResponse
        {
            Message = "Telemetry normalized.",
            Id = normalized.Id,
            Confidence = normalized.Confidence,
            Timestamp = normalized.Timestamp
        });
    }
}

public class TelemetryIngestRequest
{
    public string? SnapshotId { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public string? TenantId { get; set; }
    public string FlockId { get; set; } = string.Empty;
    public string? Source { get; set; }
    public DateTimeOffset? IngestedAt { get; set; }
    public TelemetryMetrics Metrics { get; set; } = new();
    public List<TelemetrySensorSample>? Sensors { get; set; }
    public List<TelemetryEventRequest>? Events { get; set; }
}

public class TelemetryMetrics
{
    public double? TemperatureAvgF { get; set; }
    public double? HumidityPercent { get; set; }
    public double? Co2AvgPpm { get; set; }
    public double? Nh3AvgPpm { get; set; }
    public int? AffectedZoneCount { get; set; }
    public double? MortalityPercent { get; set; }
    public double? FeedConversionRatio { get; set; }
    public double? AverageWeightLbs { get; set; }
    public double? WaterIntakeLiters { get; set; }
    public double? FeedIntakeKg { get; set; }
    public int? TemperatureSpikeEvents { get; set; }
}

public class TelemetrySensorSample
{
    public string SensorId { get; set; } = string.Empty;
    public string Zone { get; set; } = "unknown";
    public string? SensorType { get; set; }
    public double? TemperatureAvgF { get; set; }
    public double? HumidityPercent { get; set; }
    public double? Co2Ppm { get; set; }
    public double? Nh3Ppm { get; set; }
    public double? SoundDbAvg { get; set; }
    public double? VocalizationRatePerMin { get; set; }
    public double? BagokStressScore { get; set; }
    public double? WaterIntakeLiters { get; set; }
    public double? FeedIntakeKg { get; set; }
    public double? MortalityPercent { get; set; }
}

public class TelemetryEventRequest
{
    public string Type { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}

public class TelemetryIngestResponse
{
    public string Message { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
