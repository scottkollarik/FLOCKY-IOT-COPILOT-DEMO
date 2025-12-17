namespace FlockCopilot.Api.Models;

/// <summary>
/// Unified DTO that feeds the Diagnostic Agent and downstream analytics.
/// </summary>
public class NormalizedFlockPerformance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TenantId { get; set; } = string.Empty;
    public string FlockId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Source { get; set; } = "iot";
    public PerformanceMetrics Metrics { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> MissingFields { get; set; } = new();
    public double Confidence { get; set; }
    public Dictionary<string, double>? SensorAlerts { get; set; }
}

public class PerformanceMetrics
{
    public double? MortalityPercent { get; set; }
    public double? FeedConversionRatio { get; set; }
    public double? AverageWeightLbs { get; set; }
    public double? WaterIntakeLiters { get; set; }
    public double? FeedIntakeKg { get; set; }
    public double? HumidityPercent { get; set; }
    public double? TemperatureAvgF { get; set; }
    public int? TemperatureSpikeEvents { get; set; }
    public double? Co2AvgPpm { get; set; }
    public double? Nh3AvgPpm { get; set; }
    public int? AffectedZoneCount { get; set; }
    public double? SoundDbAvg { get; set; }
    public double? BagokStressScoreAvg { get; set; }
    public int? BagokStressHotZoneCount { get; set; }
}
