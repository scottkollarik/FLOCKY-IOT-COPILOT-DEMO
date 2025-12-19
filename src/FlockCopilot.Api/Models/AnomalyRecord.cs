namespace FlockCopilot.Api.Models;

public sealed class AnomalyRecord
{
    public string Id { get; set; } = string.Empty;

    public string TenantId { get; set; } = string.Empty;

    public string FlockId { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }

    public DateTimeOffset IngestedAt { get; set; }

    public string Source { get; set; } = "iot";

    public string AnomalyType { get; set; } = string.Empty;

    public string Severity { get; set; } = "info";

    public string Summary { get; set; } = string.Empty;

    public string? RawSnapshotId { get; set; }

    public List<AnomalyEvidence> Evidence { get; set; } = new();
}

public sealed class AnomalyEvidence
{
    public string SensorId { get; set; } = string.Empty;
    public string? SensorType { get; set; }
    public string? Zone { get; set; }
    public double? TemperatureAvgF { get; set; }
    public double? HumidityPercent { get; set; }
    public double? Co2Ppm { get; set; }
    public double? Nh3Ppm { get; set; }
    public double? BagokStressScore { get; set; }
    public double? SoundDbAvg { get; set; }
    public double? VocalizationRatePerMin { get; set; }

    public string Reason { get; set; } = string.Empty;
}

