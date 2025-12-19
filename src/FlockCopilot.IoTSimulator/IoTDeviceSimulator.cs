using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace FlockCopilot.IoTSimulator;

public class IoTDeviceSimulator
{
    private readonly string _apiUrl;
    private readonly string _tenantId;
    private readonly List<Building> _buildings = new();
    private readonly HttpClient _httpClient = new();
    private readonly Random _random = new();
    private volatile bool _suppressConsoleOutput;
    private const string HotkeyHint = "[grey]Controls: [green]A[/]=Inject anomaly | [green]S[/]=Status | [green]R[/]=Reset | [green]Q[/]=Quit[/][/]";

    public IoTDeviceSimulator(string apiUrl, string tenantId, List<string> buildingIds)
    {
        _apiUrl = apiUrl.TrimEnd('/');
        _tenantId = tenantId;

        // Initialize buildings with realistic zone structure
        foreach (var buildingId in buildingIds)
        {
            var building = new Building
            {
                BuildingId = buildingId,
                FlockSize = 20000,
                Zones = new List<Zone>()
            };

            // Create 6 zones per building (industry standard for tunnel-ventilated houses)
            for (int i = 1; i <= 6; i++)
            {
                var zone = new Zone
                {
                    ZoneId = $"{buildingId}-zone{i}",
                    ZoneNumber = i,
                    BuildingId = buildingId,
                    Description = i <= 2 ? "Inlet End" : i >= 5 ? "Exhaust End" : "Middle Section",
                    Sensors = new List<Sensor>()
                };

                // Each zone has 1 primary sensor cluster
                var sensor = new Sensor
                {
                    SensorId = $"sensor-{buildingId}-z{i}",
                    ZoneId = zone.ZoneId,
                    SensorType = "Environmental Cluster",
                    BaselineTemp = 78.0 + _random.NextDouble() * 4,  // 78-82°F
                    BaselineHumidity = 55.0 + _random.NextDouble() * 10,  // 55-65%
                    BaselineCO2 = 2500 + _random.NextDouble() * 500,  // 2500-3000 ppm
                    BaselineNH3 = 15 + _random.NextDouble() * 10  // 15-25 ppm
                };

                zone.Sensors.Add(sensor);
                building.Zones.Add(zone);
            }

            // Building-level aggregate metrics
            building.BaselineMortality = 0.5 + _random.NextDouble() * 0.5;  // 0.5-1.0%
            building.BaselineFCR = 1.7 + _random.NextDouble() * 0.3;  // 1.7-2.0
            building.BaselineWeight = 4.5 + _random.NextDouble() * 0.5;  // 4.5-5.0 lbs

            _buildings.Add(building);
        }
    }

    public void SetSuppressConsoleOutput(bool suppress) => _suppressConsoleOutput = suppress;

    public async Task StartTelemetrySenderAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[green]📡 Multi-sensor telemetry system started[/]");
        AnsiConsole.MarkupLine($"[grey]Monitoring {_buildings.Count} buildings with {_buildings.Sum(b => b.Zones.Count)} zones ({_buildings.Sum(b => b.Zones.Sum(z => z.Sensors.Count))} sensors)[/]");
        AnsiConsole.MarkupLine("");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendTelemetryForAllBuildingsAsync();
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error sending telemetry: {ex.Message}[/]");
            }
        }

        AnsiConsole.MarkupLine("[yellow]📡 Telemetry system stopped[/]");
    }

    private async Task SendTelemetryForAllBuildingsAsync()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var shouldRender = !_suppressConsoleOutput;

        Table? table = null;
        if (shouldRender)
        {
            table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("[yellow]Sensor[/]")
                .AddColumn("[grey]Zone[/]")
                .AddColumn("[cyan]Temp (°F)[/]")
                .AddColumn("[cyan]Humidity (%)[/]")
                .AddColumn("[magenta]CO₂ (ppm)[/]")
                .AddColumn("[yellow]NH₃ (ppm)[/]")
                .AddColumn("[grey]Status[/]");
        }

        foreach (var building in _buildings)
        {
            // Update building-level anomalies
            UpdateBuildingStateForAnomalies(building, timestamp);

            foreach (var zone in building.Zones)
            {
                foreach (var sensor in zone.Sensors)
                {
                    // Generate sensor reading
                    var reading = GenerateSensorReading(building, zone, sensor, timestamp);

                    // Send individual sensor telemetry
                    await SendSensorTelemetryAsync(building, reading);

                    // Determine status icon
                    var zoneAnomalies = building.ActiveAnomalies
                        .Where(a => a.AffectedZones.Contains(zone.ZoneId))
                        .ToList();

                    var statusIcon = zoneAnomalies.Any() ? "[red]⚠️[/]" : "[green]✓[/]";
                    var anomalyText = zoneAnomalies.Any()
                        ? string.Join(", ", zoneAnomalies.Select(a => a.Type))
                        : "Normal";

                    // Color code temperature warnings
                    var tempColor = reading.TemperatureF > 88 ? "red" :
                                   reading.TemperatureF < 70 ? "blue" : "cyan";

                    table?.AddRow(
                        sensor.SensorId,
                        $"{zone.ZoneNumber} ({zone.Description})",
                        $"[{tempColor}]{reading.TemperatureF:F1}[/]",
                        $"{reading.HumidityPercent:F1}",
                        $"{reading.CO2Ppm:F0}",
                        $"{reading.NH3Ppm:F1}",
                        $"{statusIcon} {anomalyText}"
                    );
                }
            }

            // Send aggregated building telemetry (for compatibility with existing API)
            await SendAggregatedBuildingTelemetryAsync(building, timestamp);
        }

        if (table != null)
        {
            AnsiConsole.Write(new Rule($"[yellow]Telemetry Snapshot at {timestamp:HH:mm:ss}[/]").RuleStyle("grey").LeftJustified());
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine(HotkeyHint);
            AnsiConsole.MarkupLine("");
        }
    }

    private SensorReading GenerateSensorReading(Building building, Zone zone, Sensor sensor, DateTimeOffset timestamp)
    {
        // Start with baseline
        var temp = sensor.BaselineTemp;
        var humidity = sensor.BaselineHumidity;
        var co2 = sensor.BaselineCO2;
        var nh3 = sensor.BaselineNH3;

        // Apply anomaly effects for this specific zone
        foreach (var anomaly in building.ActiveAnomalies.Where(a => a.AffectedZones.Contains(zone.ZoneId)))
        {
            var progress = (timestamp - anomaly.StartTime).TotalSeconds / (anomaly.EndTime - anomaly.StartTime).TotalSeconds;
            progress = Math.Clamp(progress, 0, 1);

            switch (anomaly.Type)
            {
                case "HeatStress":
                    // Exhaust-end zones affected most
                    var zoneMultiplier = zone.ZoneNumber / 6.0;
                    temp += 12 * progress * zoneMultiplier;
                    humidity -= 10 * progress * zoneMultiplier;
                    break;

                case "HvacMalfunction":
                    // All zones affected, but exhaust end shows it first
                    var hvacMultiplier = zone.ZoneNumber >= 4 ? 1.5 : 1.0;
                    var spike = Math.Sin(progress * Math.PI * 4) * 15 * hvacMultiplier;
                    temp += spike;
                    humidity += spike * -0.5;
                    break;

                case "DiseaseOutbreak":
                    // Affects adjacent zones - simulate spread pattern
                    if (anomaly.OriginZone == zone.ZoneId)
                    {
                        nh3 += 20 * progress;  // High ammonia from increased waste
                        co2 += 500 * progress;
                    }
                    else if (Math.Abs(zone.ZoneNumber - anomaly.OriginZoneNumber) == 1)
                    {
                        // Adjacent zone shows partial effect
                        nh3 += 10 * progress;
                        co2 += 250 * progress;
                    }
                    break;

                case "VentilationFailure":
                    // CO2 and NH3 spike, especially in middle zones
                    var ventMultiplier = zone.ZoneNumber >= 3 && zone.ZoneNumber <= 4 ? 1.5 : 1.0;
                    co2 += 2000 * progress * ventMultiplier;
                    nh3 += 40 * progress * ventMultiplier;
                    temp += 8 * progress;
                    break;

                case "EquipmentFailure":
                    // Erratic readings with dropouts
                    if (_random.NextDouble() < 0.3)
                    {
                        temp = 0;
                        humidity = 0;
                    }
                    break;

                case "ZoneSensorOutage":
                    // Entire zone loses telemetry (simulate comms/power loss)
                    temp = 0;
                    humidity = 0;
                    co2 = 0;
                    nh3 = 0;
                    break;
            }
        }

        // Add realistic sensor noise
        temp += (_random.NextDouble() - 0.5) * 1.5;
        humidity += (_random.NextDouble() - 0.5) * 3;
        co2 += (_random.NextDouble() - 0.5) * 200;
        nh3 += (_random.NextDouble() - 0.5) * 3;

        return new SensorReading
        {
            SensorId = sensor.SensorId,
            ZoneId = zone.ZoneId,
            Timestamp = timestamp,
            TemperatureF = Math.Max(0, temp),
            HumidityPercent = Math.Clamp(humidity, 0, 100),
            CO2Ppm = Math.Max(400, co2),
            NH3Ppm = Math.Max(0, nh3)
        };
    }

    private void UpdateBuildingStateForAnomalies(Building building, DateTimeOffset now)
    {
        // Remove expired anomalies
        building.ActiveAnomalies.RemoveAll(a => a.EndTime < now);

        // Update current metrics based on active anomalies
        building.CurrentMortality = building.BaselineMortality;
        building.CurrentFCR = building.BaselineFCR;
        building.CurrentWeight = building.BaselineWeight;

        foreach (var anomaly in building.ActiveAnomalies)
        {
            var progress = (now - anomaly.StartTime).TotalSeconds / (anomaly.EndTime - anomaly.StartTime).TotalSeconds;
            progress = Math.Clamp(progress, 0, 1);

            switch (anomaly.Type)
            {
                case "HeatStress":
                    building.CurrentFCR = building.BaselineFCR * (1 + 0.2 * progress);
                    building.CurrentWeight = building.BaselineWeight * (1 - 0.1 * progress);
                    building.CurrentMortality = building.BaselineMortality + (1.0 * progress);
                    break;

                case "DiseaseOutbreak":
                    building.CurrentMortality = building.BaselineMortality + (3.0 * progress);
                    building.CurrentFCR = building.BaselineFCR * (1 + 0.4 * progress);
                    building.CurrentWeight = building.BaselineWeight * (1 - 0.15 * progress);
                    break;

                case "VentilationFailure":
                    building.CurrentMortality = building.BaselineMortality + (2.0 * progress);
                    building.CurrentFCR = building.BaselineFCR * (1 + 0.3 * progress);
                    break;
            }
        }
    }

    private async Task SendSensorTelemetryAsync(Building building, SensorReading reading)
    {
        // Future: Send individual sensor readings to time-series database or Event Hub
        // For now, we aggregate at building level for the demo
        await Task.CompletedTask;
    }

    private async Task SendAggregatedBuildingTelemetryAsync(Building building, DateTimeOffset timestamp)
    {
        try
        {
            // Aggregate all zone sensor readings for building-level metrics
            var allReadings = building.Zones.SelectMany(z => z.Sensors.Select(s =>
                GenerateSensorReading(building, z, s, timestamp)
            )).ToList();

            // Generate one microphone-derived audio sample per zone (feature aggregates, not raw audio)
            var audioSamples = building.Zones.Select(z => GenerateZoneAudioSample(building, z, timestamp)).ToList();

            var avgTemp = allReadings.Average(r => r.TemperatureF);
            var avgHumidity = allReadings.Average(r => r.HumidityPercent);
            var avgCO2 = allReadings.Average(r => r.CO2Ppm);
            var avgNH3 = allReadings.Average(r => r.NH3Ppm);

            // Calculate feed/water based on building performance
            var waterIntake = 8.0 + (_random.NextDouble() * 2);
            var feedIntake = 4.2 + (_random.NextDouble() * 0.8);

            // Adjust for anomalies
            if (building.ActiveAnomalies.Any(a => a.Type == "HeatStress"))
            {
                waterIntake *= 1.5;  // Increased water consumption
                feedIntake *= 0.7;    // Reduced feed consumption
            }

            var telemetry = new TelemetrySnapshot
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                CapturedAt = timestamp,
                TenantId = _tenantId,
                FlockId = building.BuildingId,
                Source = "iot-gateway",
                IngestedAt = timestamp,
                Metrics = new PerformanceMetrics
                {
                    TemperatureAvgF = avgTemp,
                    HumidityPercent = avgHumidity,
                    MortalityPercent = building.CurrentMortality,
                    FeedConversionRatio = building.CurrentFCR,
                    AverageWeightLbs = building.CurrentWeight,
                    WaterIntakeLiters = waterIntake,
                    FeedIntakeKg = feedIntake,
                    TemperatureSpikeEvents = building.ActiveAnomalies.Any(a =>
                        a.Type.Contains("Hvac") || a.Type.Contains("Heat")) ? _random.Next(3, 10) : 0,
                    CO2AvgPpm = avgCO2,
                    NH3AvgPpm = avgNH3,
                    AffectedZoneCount = building.ActiveAnomalies.SelectMany(a => a.AffectedZones).Distinct().Count()
                },
                Sensors = allReadings
                    .Select(r => new SensorSampleDto
                    {
                        SensorId = r.SensorId,
                        Zone = r.ZoneId,
                        SensorType = "environment",
                        TemperatureAvgF = r.TemperatureF <= 0.01 ? null : r.TemperatureF,
                        HumidityPercent = r.HumidityPercent <= 0.01 ? null : r.HumidityPercent,
                        Co2Ppm = r.CO2Ppm <= 0.01 ? null : r.CO2Ppm,
                        Nh3Ppm = r.NH3Ppm <= 0.01 ? null : r.NH3Ppm
                    })
                    .Concat(audioSamples)
                    .ToList(),
                Events = building.ActiveAnomalies
                    .Select(a => new TelemetryEventDto
                    {
                        Type = a.Type,
                        Severity = a.Type.Contains("Failure", StringComparison.OrdinalIgnoreCase) ? "error" : "warning",
                        Description = a.Description,
                        OccurredAt = a.StartTime
                    })
                    .ToList()
            };

            var json = JsonSerializer.Serialize(telemetry, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_apiUrl}/api/telemetry/ingest", content);
        }
        catch
        {
            // Silent fail for demo
        }
    }

    private SensorSampleDto GenerateZoneAudioSample(Building building, Zone zone, DateTimeOffset timestamp)
    {
        // Baseline: normal barn soundscape (fans, movement, vocalization)
        var soundDb = 55.0 + (_random.NextDouble() * 4 - 2); // ~53-57 dB
        var vocalRate = 18.0 + (_random.NextDouble() * 6 - 3); // ~15-21 calls/min
        var stress = 0.12 + (_random.NextDouble() * 0.08); // ~0.12-0.20

        var zoneAnomalies = building.ActiveAnomalies.Where(a => a.AffectedZones.Contains(zone.ZoneId)).ToList();
        foreach (var anomaly in zoneAnomalies)
        {
            var progress = (timestamp - anomaly.StartTime).TotalSeconds / (anomaly.EndTime - anomaly.StartTime).TotalSeconds;
            progress = Math.Clamp(progress, 0, 1);

            switch (anomaly.Type)
            {
                case "HeatStress":
                    // Heat stress tends to elevate agitation/panting; model as higher sound + higher stress score.
                    soundDb += 10 * progress;
                    vocalRate += 8 * progress;
                    stress += 0.65 * progress;
                    break;

                case "VentilationFailure":
                    // Poor air quality correlates with distress and restlessness.
                    soundDb += 7 * progress;
                    vocalRate += 5 * progress;
                    stress += 0.5 * progress;
                    break;

                case "DiseaseOutbreak":
                    // Mixed signal: lower activity over time but some spikes; keep it moderate.
                    soundDb += 3 * progress;
                    vocalRate += 2 * progress;
                    stress += 0.25 * progress;
                    break;

                case "HvacMalfunction":
                    // Oscillation can be disruptive; intermittent spikes.
                    var spike = Math.Abs(Math.Sin(progress * Math.PI * 4));
                    soundDb += 6 * spike;
                    vocalRate += 4 * spike;
                    stress += 0.35 * spike;
                    break;

                case "ZoneSensorOutage":
                    // Mic offline / gateway can't read this zone: represent missing audio features.
                    return new SensorSampleDto
                    {
                        SensorId = $"{zone.ZoneId}-mic",
                        Zone = zone.ZoneId,
                        SensorType = "audio",
                        SoundDbAvg = null,
                        VocalizationRatePerMin = null,
                        BagokStressScore = null
                    };
            }
        }

        soundDb = Math.Clamp(soundDb, 35, 95);
        vocalRate = Math.Max(0, vocalRate);
        stress = Math.Clamp(stress, 0, 1);

        return new SensorSampleDto
        {
            SensorId = $"{zone.ZoneId}-mic",
            Zone = zone.ZoneId,
            SensorType = "audio",
            SoundDbAvg = Math.Round(soundDb, 1),
            VocalizationRatePerMin = Math.Round(vocalRate, 1),
            BagokStressScore = Math.Round(stress, 2)
        };
    }

    public void InjectHeatStress(string buildingId, TimeSpan duration)
    {
        var building = _buildings.First(b => b.BuildingId == buildingId);

        // Heat stress starts at exhaust end (zones 5-6) and spreads backward
        var affectedZones = building.Zones
            .Where(z => z.ZoneNumber >= 5)
            .Select(z => z.ZoneId)
            .ToList();

        building.ActiveAnomalies.Add(new BuildingAnomaly
        {
            Type = "HeatStress",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow + duration,
            AffectedZones = affectedZones,
            Description = "Elevated temperature in exhaust-end zones, likely HVAC capacity issue"
        });

        AnsiConsole.MarkupLine($"[red]🔥 Heat Stress injected in {buildingId}[/] - Zones 5-6 affected");
    }

    public void InjectDiseaseOutbreak(string buildingId, int originZoneNumber, TimeSpan duration)
    {
        var building = _buildings.First(b => b.BuildingId == buildingId);
        var originZone = building.Zones.First(z => z.ZoneNumber == originZoneNumber);

        // Disease starts in one zone, spreads to adjacent over time
        var affectedZones = building.Zones
            .Where(z => Math.Abs(z.ZoneNumber - originZoneNumber) <= 1)
            .Select(z => z.ZoneId)
            .ToList();

        building.ActiveAnomalies.Add(new BuildingAnomaly
        {
            Type = "DiseaseOutbreak",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow + duration,
            AffectedZones = affectedZones,
            OriginZone = originZone.ZoneId,
            OriginZoneNumber = originZoneNumber,
            Description = $"Disease outbreak originating in zone {originZoneNumber}, spreading to adjacent zones"
        });

        AnsiConsole.MarkupLine($"[red]🦠 Disease Outbreak injected in {buildingId}[/] - Origin: Zone {originZoneNumber}");
    }

    public void InjectVentilationFailure(string buildingId, TimeSpan duration)
    {
        var building = _buildings.First(b => b.BuildingId == buildingId);

        // Ventilation failure affects all zones
        var affectedZones = building.Zones.Select(z => z.ZoneId).ToList();

        building.ActiveAnomalies.Add(new BuildingAnomaly
        {
            Type = "VentilationFailure",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow + duration,
            AffectedZones = affectedZones,
            Description = "Ventilation system failure causing elevated CO₂ and NH₃ levels building-wide"
        });

        AnsiConsole.MarkupLine($"[red]💨 Ventilation Failure injected in {buildingId}[/] - All zones affected");
    }

    public void InjectHvacMalfunction(string buildingId, TimeSpan duration)
    {
        var building = _buildings.First(b => b.BuildingId == buildingId);

        // HVAC malfunction causes erratic readings in exhaust-end zones first
        var affectedZones = building.Zones
            .Where(z => z.ZoneNumber >= 4)
            .Select(z => z.ZoneId)
            .ToList();

        building.ActiveAnomalies.Add(new BuildingAnomaly
        {
            Type = "HvacMalfunction",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow + duration,
            AffectedZones = affectedZones,
            Description = "HVAC control malfunction causing temperature oscillations"
        });

        AnsiConsole.MarkupLine($"[red]⚙️ HVAC Malfunction injected in {buildingId}[/] - Zones 4-6 affected");
    }

    public void InjectEquipmentFailure(string buildingId, int zoneNumber)
    {
        var building = _buildings.First(b => b.BuildingId == buildingId);
        var zone = building.Zones.First(z => z.ZoneNumber == zoneNumber);

        building.ActiveAnomalies.Add(new BuildingAnomaly
        {
            Type = "EquipmentFailure",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow.AddHours(2),
            AffectedZones = new List<string> { zone.ZoneId },
            Description = $"Sensor malfunction in zone {zoneNumber} - intermittent readings"
        });

        AnsiConsole.MarkupLine($"[red]⚠️ Equipment Failure injected in {buildingId}[/] - Zone {zoneNumber}");
    }

    public void InjectZoneSensorOutage(string buildingId, int zoneNumber, TimeSpan duration)
    {
        var building = _buildings.First(b => b.BuildingId == buildingId);
        var zone = building.Zones.First(z => z.ZoneNumber == zoneNumber);

        building.ActiveAnomalies.Add(new BuildingAnomaly
        {
            Type = "ZoneSensorOutage",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow + duration,
            AffectedZones = new List<string> { zone.ZoneId },
            Description = $"Zone {zoneNumber} telemetry outage (gateway cannot read sensors in this zone)"
        });

        AnsiConsole.MarkupLine($"[red]📴 Zone Sensor Outage injected in {buildingId}[/] - Zone {zoneNumber}");
    }

    public void ResetAllAnomalies()
    {
        foreach (var building in _buildings)
        {
            building.ActiveAnomalies.Clear();
        }
        AnsiConsole.MarkupLine("[green]✓ All anomalies reset - systems returning to normal[/]");
    }

    public void DisplayStatus()
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[yellow]Building[/]")
            .AddColumn("[cyan]Active Anomalies[/]")
            .AddColumn("[grey]Affected Zones[/]")
            .AddColumn("[grey]Remaining Time[/]");

        foreach (var building in _buildings)
        {
            if (!building.ActiveAnomalies.Any())
            {
                table.AddRow(building.BuildingId, "[green]None (Normal Operation)[/]", "-", "-");
            }
            else
            {
                foreach (var anomaly in building.ActiveAnomalies)
                {
                    var remaining = anomaly.EndTime - DateTimeOffset.UtcNow;
                    var zoneList = string.Join(", ", anomaly.AffectedZones.Select(z => z.Split('-').Last()));

                    table.AddRow(
                        building.BuildingId,
                        $"[red]{anomaly.Type}[/]",
                        zoneList,
                        $"{remaining.TotalMinutes:F0} min"
                    );
                }
            }
        }

        AnsiConsole.Write(table);
    }

    public List<string> GetBuildingIds() => _buildings.Select(b => b.BuildingId).ToList();
}

// Domain Models
public class Building
{
    public string BuildingId { get; set; } = string.Empty;
    public int FlockSize { get; set; }
    public List<Zone> Zones { get; set; } = new();
    public List<BuildingAnomaly> ActiveAnomalies { get; set; } = new();

    public double BaselineMortality { get; set; }
    public double BaselineFCR { get; set; }
    public double BaselineWeight { get; set; }

    public double CurrentMortality { get; set; }
    public double CurrentFCR { get; set; }
    public double CurrentWeight { get; set; }
}

public class Zone
{
    public string ZoneId { get; set; } = string.Empty;
    public int ZoneNumber { get; set; }
    public string BuildingId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Sensor> Sensors { get; set; } = new();
}

public class Sensor
{
    public string SensorId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string SensorType { get; set; } = string.Empty;

    public double BaselineTemp { get; set; }
    public double BaselineHumidity { get; set; }
    public double BaselineCO2 { get; set; }
    public double BaselineNH3 { get; set; }
}

public class SensorReading
{
    public string SensorId { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public double TemperatureF { get; set; }
    public double HumidityPercent { get; set; }
    public double CO2Ppm { get; set; }
    public double NH3Ppm { get; set; }
}

public class BuildingAnomaly
{
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public List<string> AffectedZones { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string OriginZone { get; set; } = string.Empty;
    public int OriginZoneNumber { get; set; }
}

// DTOs for API compatibility
public class TelemetrySnapshot
{
    public string SnapshotId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string FlockId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset IngestedAt { get; set; }
    public PerformanceMetrics Metrics { get; set; } = new();
    public List<SensorSampleDto>? Sensors { get; set; }
    public List<TelemetryEventDto>? Events { get; set; }
}

public class SensorSampleDto
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

public class TelemetryEventDto
{
    public string Type { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}

public class PerformanceMetrics
{
    public double? TemperatureAvgF { get; set; }
    public double? HumidityPercent { get; set; }
    public double? MortalityPercent { get; set; }
    public double? FeedConversionRatio { get; set; }
    public double? AverageWeightLbs { get; set; }
    public double? WaterIntakeLiters { get; set; }
    public double? FeedIntakeKg { get; set; }
    public int? TemperatureSpikeEvents { get; set; }
    public double? CO2AvgPpm { get; set; }
    public double? NH3AvgPpm { get; set; }
    public int? AffectedZoneCount { get; set; }
}
