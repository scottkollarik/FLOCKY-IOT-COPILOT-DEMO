using System.Text.Json.Serialization;

namespace FlockCopilot.Api.Models;

/// <summary>
/// Represents the deterministic payload handed to the normalizer. It is either populated
/// from IoT telemetry snapshots or from the manual extraction agent output.
/// </summary>
public class FlockRaw
{
    public string TenantId { get; set; } = string.Empty;
    public string FlockId { get; set; } = string.Empty;

    /// <summary>
    /// Defines the ingestion source ("iot" or "manual").
    /// </summary>
    public string Source { get; set; } = "iot";

    public DateTimeOffset IngestedAt { get; set; } = DateTimeOffset.UtcNow;

    public TelemetrySnapshot? Telemetry { get; set; }

    public ManualReport? ManualReport { get; set; }

    [JsonIgnore]
    public bool IsManual => string.Equals(Source, "manual", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsIoT => !IsManual;
}
