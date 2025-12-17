namespace FlockCopilot.Api.Models;

/// <summary>
/// DTO emitted by the Extraction Agent after processing a manual report file.
/// </summary>
public class ManualReport
{
    public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset ReportedAt { get; set; } = DateTimeOffset.UtcNow;
    public ManualReporter Reporter { get; set; } = new();
    public ManualMetrics Metrics { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public ManualDataCompleteness DataCompleteness { get; set; } = new();
}

public class ManualReporter
{
    public string Name { get; set; } = "Unknown";
    public string? Role { get; set; }
    public string? Contact { get; set; }
}

public class ManualMetrics
{
    public double? MortalityPercent { get; set; }
    public double? FeedConversionRatio { get; set; }
    public double? AverageWeightLbs { get; set; }
    public double? WaterIntakeLiters { get; set; }
    public double? FeedIntakeKg { get; set; }
    public double? TemperatureAvgF { get; set; }
    public double? HumidityPercent { get; set; }
}

public class ManualDataCompleteness
{
    public bool GapsDetected { get; set; }
    public string CoverageDescription { get; set; } = "unknown";
}
