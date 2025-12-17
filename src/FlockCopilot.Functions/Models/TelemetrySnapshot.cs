namespace FlockCopilot.Functions.Models;

/// <summary>
/// Raw IoT snapshot captured from the grow-out houses.
/// </summary>
public class TelemetrySnapshot
{
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<SensorSample> Sensors { get; set; } = new();
    public AggregatedTelemetryMetrics? Aggregates { get; set; }
    public List<TelemetryEvent> Events { get; set; } = new();
}

public class SensorSample
{
    public string SensorId { get; set; } = string.Empty;
    public double? TemperatureAvgF { get; set; }
    public double? HumidityPercent { get; set; }
    public double? WaterIntakeLiters { get; set; }
    public double? FeedIntakeKg { get; set; }
    public double? MortalityPercent { get; set; }
    public string Zone { get; set; } = "unknown";
}

public class AggregatedTelemetryMetrics
{
    public double? MortalityPercent { get; set; }
    public double? FeedConversionRatio { get; set; }
    public double? AverageWeightLbs { get; set; }
    public double? WaterIntakeLiters { get; set; }
    public double? FeedIntakeKg { get; set; }
    public double? TemperatureAvgF { get; set; }
    public double? HumidityPercent { get; set; }
    public int? TemperatureSpikeEvents { get; set; }
}

public class TelemetryEvent
{
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = "info";
    public string? Description { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}
