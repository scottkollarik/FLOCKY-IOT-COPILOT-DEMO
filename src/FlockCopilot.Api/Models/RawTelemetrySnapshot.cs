namespace FlockCopilot.Api.Models;

/// <summary>
/// Raw gateway snapshot containing all per-sensor datapoints for a flock at a given time.
/// Intended for analytics (e.g., Fabric mirroring) and traceability.
/// </summary>
public class RawTelemetrySnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string FlockId { get; set; } = string.Empty;
    public string Source { get; set; } = "iot-gateway";
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SensorSample> Sensors { get; set; } = new();
    public AggregatedTelemetryMetrics? Aggregates { get; set; }
    public List<TelemetryEvent> Events { get; set; } = new();
}

