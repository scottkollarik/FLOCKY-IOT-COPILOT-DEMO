using FlockCopilot.Api.Models;

namespace FlockCopilot.Api.Services;

public interface IAnomalyDetector
{
    IReadOnlyList<AnomalyRecord> Detect(RawTelemetrySnapshot snapshot);
}

public sealed class AnomalyDetector : IAnomalyDetector
{
    // Keep thresholds aligned with Normalizer + ChatController to avoid conflicting behavior.
    private const double TemperatureHighF = 92;
    private const double TemperatureLowF = 70;
    private const double HumidityHighPercent = 75;
    private const double HumidityLowPercent = 40;
    private const double Co2HighPpm = 3000;
    private const double Nh3HighPpm = 25;
    private const double BagokStressHigh = 0.7;

    public IReadOnlyList<AnomalyRecord> Detect(RawTelemetrySnapshot snapshot)
    {
        var byType = new Dictionary<string, AnomalyRecord>(StringComparer.OrdinalIgnoreCase);

        void AddEvidence(string anomalyType, string severity, string summary, SensorSample sensor, string reason)
        {
            if (!byType.TryGetValue(anomalyType, out var record))
            {
                record = new AnomalyRecord
                {
                    Id = BuildId(snapshot, anomalyType),
                    TenantId = snapshot.TenantId,
                    FlockId = snapshot.FlockId,
                    CapturedAt = snapshot.CapturedAt,
                    IngestedAt = snapshot.IngestedAt,
                    Source = snapshot.Source,
                    RawSnapshotId = snapshot.Id,
                    AnomalyType = anomalyType,
                    Severity = severity,
                    Summary = summary
                };
                byType[anomalyType] = record;
            }

            record.Evidence.Add(new AnomalyEvidence
            {
                SensorId = sensor.SensorId,
                SensorType = sensor.SensorType,
                Zone = sensor.Zone,
                TemperatureAvgF = sensor.TemperatureAvgF,
                HumidityPercent = sensor.HumidityPercent,
                Co2Ppm = sensor.Co2Ppm,
                Nh3Ppm = sensor.Nh3Ppm,
                BagokStressScore = sensor.BagokStressScore,
                SoundDbAvg = sensor.SoundDbAvg,
                VocalizationRatePerMin = sensor.VocalizationRatePerMin,
                Reason = reason
            });
        }

        foreach (var sensor in snapshot.Sensors)
        {
            if (sensor.TemperatureAvgF is > TemperatureHighF)
            {
                AddEvidence(
                    anomalyType: "temperature_high",
                    severity: "high",
                    summary: $"Temperature above {TemperatureHighF}F",
                    sensor,
                    reason: $"temperatureAvgF={sensor.TemperatureAvgF:0.0} > {TemperatureHighF:0.0}");
            }

            if (sensor.TemperatureAvgF is < TemperatureLowF)
            {
                AddEvidence(
                    anomalyType: "temperature_low",
                    severity: "high",
                    summary: $"Temperature below {TemperatureLowF}F",
                    sensor,
                    reason: $"temperatureAvgF={sensor.TemperatureAvgF:0.0} < {TemperatureLowF:0.0}");
            }

            if (sensor.HumidityPercent is > HumidityHighPercent)
            {
                AddEvidence(
                    anomalyType: "humidity_high",
                    severity: "medium",
                    summary: $"Humidity above {HumidityHighPercent}%",
                    sensor,
                    reason: $"humidityPercent={sensor.HumidityPercent:0.0} > {HumidityHighPercent:0.0}");
            }

            if (sensor.HumidityPercent is < HumidityLowPercent)
            {
                AddEvidence(
                    anomalyType: "humidity_low",
                    severity: "medium",
                    summary: $"Humidity below {HumidityLowPercent}%",
                    sensor,
                    reason: $"humidityPercent={sensor.HumidityPercent:0.0} < {HumidityLowPercent:0.0}");
            }

            if (sensor.Co2Ppm is > Co2HighPpm)
            {
                AddEvidence(
                    anomalyType: "co2_high",
                    severity: "high",
                    summary: $"CO₂ above {Co2HighPpm}ppm",
                    sensor,
                    reason: $"co2Ppm={sensor.Co2Ppm:0} > {Co2HighPpm:0}");
            }

            if (sensor.Nh3Ppm is > Nh3HighPpm)
            {
                AddEvidence(
                    anomalyType: "nh3_high",
                    severity: "high",
                    summary: $"NH₃ above {Nh3HighPpm}ppm",
                    sensor,
                    reason: $"nh3Ppm={sensor.Nh3Ppm:0.0} > {Nh3HighPpm:0.0}");
            }

            if (sensor.BagokStressScore is >= BagokStressHigh)
            {
                AddEvidence(
                    anomalyType: "bagok_stress_high",
                    severity: "medium",
                    summary: $"Bagok stress score >= {BagokStressHigh:0.0}",
                    sensor,
                    reason: $"bagokStressScore={sensor.BagokStressScore:0.00} >= {BagokStressHigh:0.00}");
            }
        }

        return byType.Values
            .Select(r =>
            {
                // Ensure stable ordering in evidence for consistent downstream rendering.
                r.Evidence = r.Evidence
                    .OrderBy(e => e.Zone, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.SensorId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return r;
            })
            .OrderByDescending(r => r.Severity, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.AnomalyType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildId(RawTelemetrySnapshot snapshot, string anomalyType) =>
        $"{snapshot.TenantId}:{snapshot.FlockId}:{snapshot.Id}:{anomalyType}";
}

