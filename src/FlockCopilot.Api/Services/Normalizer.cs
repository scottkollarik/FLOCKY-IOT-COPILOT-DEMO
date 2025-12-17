using FlockCopilot.Api.Models;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Api.Services;

public interface INormalizer
{
    NormalizedFlockPerformance Normalize(FlockRaw raw);
}

public class Normalizer : INormalizer
{
    private readonly ILogger<Normalizer> _logger;

    public Normalizer(ILogger<Normalizer> logger)
    {
        _logger = logger;
    }

    public NormalizedFlockPerformance Normalize(FlockRaw raw)
    {
        var normalized = new NormalizedFlockPerformance
        {
            TenantId = raw.TenantId,
            FlockId = raw.FlockId,
            Source = raw.Source,
            Timestamp = raw.IsIoT && raw.Telemetry != null ? raw.Telemetry.CapturedAt : raw.IngestedAt
        };

        var metrics = normalized.Metrics;

        if (raw.IsIoT && raw.Telemetry != null)
        {
            PopulateFromTelemetry(raw.Telemetry, metrics, normalized);
        }
        else if (raw.IsManual && raw.ManualReport != null)
        {
            PopulateFromManual(raw.ManualReport, metrics, normalized);
        }
        else
        {
            normalized.Notes.Add("Payload did not specify a recognized source.");
            normalized.MissingFields.AddRange(new[]
            {
                nameof(PerformanceMetrics.MortalityPercent),
                nameof(PerformanceMetrics.FeedConversionRatio),
                nameof(PerformanceMetrics.AverageWeightLbs),
                nameof(PerformanceMetrics.WaterIntakeLiters),
                nameof(PerformanceMetrics.FeedIntakeKg),
                nameof(PerformanceMetrics.HumidityPercent),
                nameof(PerformanceMetrics.TemperatureAvgF)
            });
        }

        var populated = new[]
        {
            metrics.MortalityPercent,
            metrics.FeedConversionRatio,
            metrics.AverageWeightLbs,
            metrics.WaterIntakeLiters,
            metrics.FeedIntakeKg,
            metrics.HumidityPercent,
            metrics.TemperatureAvgF
        }.Count(v => v.HasValue);

        normalized.Confidence = populated == 0 ? 0 : Math.Round((double)populated / 7, 2);

        if (normalized.Confidence < 0.4)
        {
            normalized.Notes.Add("Low confidence – insufficient signal across required metrics.");
        }

        return normalized;
    }

    private void PopulateFromTelemetry(
        TelemetrySnapshot telemetry,
        PerformanceMetrics metrics,
        NormalizedFlockPerformance normalized)
    {
        double? Average(IEnumerable<double?> values)
        {
            var list = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return list.Count == 0 ? null : list.Average();
        }

        metrics.TemperatureAvgF = Average(telemetry.Sensors.Select(s => s.TemperatureAvgF)) ?? telemetry.Aggregates?.TemperatureAvgF;
        metrics.HumidityPercent = Average(telemetry.Sensors.Select(s => s.HumidityPercent)) ?? telemetry.Aggregates?.HumidityPercent;
        metrics.Co2AvgPpm = Average(telemetry.Sensors.Select(s => s.Co2Ppm)) ?? telemetry.Aggregates?.Co2AvgPpm;
        metrics.Nh3AvgPpm = Average(telemetry.Sensors.Select(s => s.Nh3Ppm)) ?? telemetry.Aggregates?.Nh3AvgPpm;
        metrics.SoundDbAvg = Average(telemetry.Sensors.Where(s => IsAudio(s.SensorType)).Select(s => s.SoundDbAvg));
        metrics.BagokStressScoreAvg = Average(telemetry.Sensors.Where(s => IsAudio(s.SensorType)).Select(s => s.BagokStressScore));
        metrics.WaterIntakeLiters = Average(telemetry.Sensors.Select(s => s.WaterIntakeLiters)) ?? telemetry.Aggregates?.WaterIntakeLiters;
        metrics.FeedIntakeKg = Average(telemetry.Sensors.Select(s => s.FeedIntakeKg)) ?? telemetry.Aggregates?.FeedIntakeKg;
        metrics.MortalityPercent = Average(telemetry.Sensors.Select(s => s.MortalityPercent)) ?? telemetry.Aggregates?.MortalityPercent;
        metrics.FeedConversionRatio = telemetry.Aggregates?.FeedConversionRatio;
        metrics.AverageWeightLbs = telemetry.Aggregates?.AverageWeightLbs;
        metrics.TemperatureSpikeEvents = telemetry.Aggregates?.TemperatureSpikeEvents;

        var affectedZones = telemetry.Sensors
            .Where(s => s.TemperatureAvgF is > 88 or < 70 ||
                        s.HumidityPercent is > 75 or < 40 ||
                        s.Co2Ppm is > 3000 ||
                        s.Nh3Ppm is > 25 ||
                        s.BagokStressScore is >= 0.7)
            .Select(s => s.Zone)
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        metrics.AffectedZoneCount = affectedZones.Count > 0
            ? affectedZones.Count
            : telemetry.Aggregates?.AffectedZoneCount;

        var hotBagokZones = telemetry.Sensors
            .Where(s => IsAudio(s.SensorType) && s.BagokStressScore is >= 0.7)
            .Select(s => s.Zone)
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        metrics.BagokStressHotZoneCount = hotBagokZones.Count;

        foreach (var evt in telemetry.Events)
        {
            normalized.Notes.Add($"Telemetry event {evt.Type} ({evt.Severity}) {evt.Description}");
        }

        normalized.SensorAlerts = telemetry.Sensors
            .Where(s => s.TemperatureAvgF is > 92 or < 70 ||
                        s.HumidityPercent is > 75 or < 40 ||
                        s.Co2Ppm is > 3000 ||
                        s.Nh3Ppm is > 25 ||
                        s.BagokStressScore is >= 0.7)
            .ToDictionary(
                s => s.SensorId,
                s =>
                    s.TemperatureAvgF ??
                    s.Co2Ppm ??
                    s.Nh3Ppm ??
                    s.BagokStressScore ??
                    s.HumidityPercent ??
                    0);
    }

    private static bool IsAudio(string? sensorType) =>
        sensorType != null && sensorType.Equals("audio", StringComparison.OrdinalIgnoreCase);

    private void PopulateFromManual(
        ManualReport manual,
        PerformanceMetrics metrics,
        NormalizedFlockPerformance normalized)
    {
        metrics.MortalityPercent = manual.Metrics.MortalityPercent;
        metrics.FeedConversionRatio = manual.Metrics.FeedConversionRatio;
        metrics.AverageWeightLbs = manual.Metrics.AverageWeightLbs;
        metrics.FeedIntakeKg = manual.Metrics.FeedIntakeKg;
        metrics.WaterIntakeLiters = manual.Metrics.WaterIntakeLiters;
        metrics.TemperatureAvgF = manual.Metrics.TemperatureAvgF;
        metrics.HumidityPercent = manual.Metrics.HumidityPercent;

        if (manual.Notes.Any())
        {
            normalized.Notes.AddRange(manual.Notes);
        }

        if (manual.DataCompleteness.GapsDetected)
        {
            normalized.Notes.Add($"Manual reporter indicated gaps: {manual.DataCompleteness.CoverageDescription}");
        }

        AddMissingFieldIfNull(metrics.MortalityPercent, nameof(PerformanceMetrics.MortalityPercent), normalized);
        AddMissingFieldIfNull(metrics.FeedConversionRatio, nameof(PerformanceMetrics.FeedConversionRatio), normalized);
        AddMissingFieldIfNull(metrics.AverageWeightLbs, nameof(PerformanceMetrics.AverageWeightLbs), normalized);
        AddMissingFieldIfNull(metrics.TemperatureAvgF, nameof(PerformanceMetrics.TemperatureAvgF), normalized);
        AddMissingFieldIfNull(metrics.HumidityPercent, nameof(PerformanceMetrics.HumidityPercent), normalized);
        AddMissingFieldIfNull(metrics.FeedIntakeKg, nameof(PerformanceMetrics.FeedIntakeKg), normalized);
        AddMissingFieldIfNull(metrics.WaterIntakeLiters, nameof(PerformanceMetrics.WaterIntakeLiters), normalized);
    }

    private static void AddMissingFieldIfNull(
        double? value,
        string fieldName,
        NormalizedFlockPerformance normalized)
    {
        if (!value.HasValue)
        {
            normalized.MissingFields.Add(fieldName);
        }
    }
}
