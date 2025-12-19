using FlockCopilot.IoTSimulator;
using Spectre.Console;

AnsiConsole.Clear();

var headerGrid = new Grid()
    .AddColumn(new GridColumn().NoWrap())
    .AddColumn();

var chickenHead = new Markup("[yellow]  ,~.\n ('v')\n /   \\\n^^^ ^^^[/]");
var title = new Markup("[bold green]Flocky IoT Simulator[/]\n[grey]Multi-Sensor Zone-Based IoT Telemetry Generator[/]");

headerGrid.AddRow(chickenHead, title);

AnsiConsole.Write(headerGrid);
AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
AnsiConsole.MarkupLine("");

// Configuration
var apiUrl = AnsiConsole.Ask<string>("Enter Container App API URL:", "https://ca-flockfoundry.azurecontainerapps.io");
var tenantId = AnsiConsole.Ask<string>("Enter Tenant ID:", "tenant-demo-123");
var sendIntervalSeconds = AnsiConsole.Ask<int>("Send interval (seconds):", 15);

AnsiConsole.MarkupLine("");
AnsiConsole.Write(new Rule("[yellow]Building Configuration[/]").RuleStyle("grey").LeftJustified());

var buildings = new List<string> { "building-a", "building-b", "building-c" };
AnsiConsole.MarkupLine($"[green]Simulating {buildings.Count} buildings:[/] {string.Join(", ", buildings)}");
AnsiConsole.MarkupLine($"[grey]Each building: 6 zones | 20,000 birds | 6 environmental sensors[/]");
AnsiConsole.MarkupLine("");

// Initialize simulator
var simulator = new IoTDeviceSimulator(apiUrl, tenantId, buildings);

// Start background telemetry sender
var cts = new CancellationTokenSource();
var telemetryTask = simulator.StartTelemetrySenderAsync(TimeSpan.FromSeconds(sendIntervalSeconds), cts.Token);

AnsiConsole.MarkupLine("[grey]Controls: [green]A[/]=Inject anomaly | [green]S[/]=Status | [green]R[/]=Reset | [green]Q[/]=Quit[/]");
AnsiConsole.MarkupLine("");

while (true)
{
    var key = Console.ReadKey(intercept: true).Key;

    switch (key)
    {
        case ConsoleKey.A:
        {
            simulator.SetSuppressConsoleOutput(true);
            try
            {
                AnsiConsole.Write(new Rule("[yellow]Anomaly Injection[/]").RuleStyle("grey").LeftJustified());
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select an [green]anomaly scenario[/] to inject:")
                        .PageSize(12)
                        .AddChoices(new[]
                        {
                            "🔥 Heat Stress Event (exhaust-end zones)",
                            "🦠 Disease Outbreak (zone-based spread)",
                            "💨 Ventilation Failure (building-wide CO₂/NH₃)",
                            "🌡️ HVAC Malfunction (temp oscillations)",
                            "⚙️ Equipment Failure (single sensor)",
                            "📴 Zone Sensor Outage (entire zone missing)",
                            "↩️ Cancel"
                        }));

                AnsiConsole.MarkupLine("");

                switch (choice)
                {
                    case "🔥 Heat Stress Event (exhaust-end zones)":
                        var heatBuilding = PromptForBuilding(simulator.GetBuildingIds());
                        var heatDuration = AnsiConsole.Ask<int>("Duration (minutes):", 30);
                        simulator.InjectHeatStress(heatBuilding, TimeSpan.FromMinutes(heatDuration));
                        AnsiConsole.MarkupLine($"[grey]→ Effect: Zones 5-6 show elevated temperature, reduced feed intake[/]");
                        break;

                    case "🦠 Disease Outbreak (zone-based spread)":
                        var diseaseBuilding = PromptForBuilding(simulator.GetBuildingIds());
                        var originZone = AnsiConsole.Ask<int>("Origin zone (1-6):", 3);
                        var diseaseDuration = AnsiConsole.Ask<int>("Duration (hours):", 12);
                        simulator.InjectDiseaseOutbreak(diseaseBuilding, originZone, TimeSpan.FromHours(diseaseDuration));
                        AnsiConsole.MarkupLine($"[grey]→ Effect: Elevated NH₃ and CO₂ in zone {originZone} and adjacent zones[/]");
                        break;

                    case "💨 Ventilation Failure (building-wide CO₂/NH₃)":
                        var ventBuilding = PromptForBuilding(simulator.GetBuildingIds());
                        var ventDuration = AnsiConsole.Ask<int>("Duration (hours):", 2);
                        simulator.InjectVentilationFailure(ventBuilding, TimeSpan.FromHours(ventDuration));
                        AnsiConsole.MarkupLine($"[grey]→ Effect: All zones show elevated CO₂ and NH₃, middle zones worst[/]");
                        break;

                    case "🌡️ HVAC Malfunction (temp oscillations)":
                        var hvacBuilding = PromptForBuilding(simulator.GetBuildingIds());
                        var hvacDuration = AnsiConsole.Ask<int>("Duration (hours):", 4);
                        simulator.InjectHvacMalfunction(hvacBuilding, TimeSpan.FromHours(hvacDuration));
                        AnsiConsole.MarkupLine($"[grey]→ Effect: Zones 4-6 show temperature oscillations (±15°F)[/]");
                        break;

                    case "⚙️ Equipment Failure (single sensor)":
                        var equipBuilding = PromptForBuilding(simulator.GetBuildingIds());
                        var sensorZone = AnsiConsole.Ask<int>("Which zone (1-6):", 3);
                        simulator.InjectEquipmentFailure(equipBuilding, sensorZone);
                        AnsiConsole.MarkupLine($"[grey]→ Effect: Intermittent sensor dropouts in zone {sensorZone}[/]");
                        break;

                    case "📴 Zone Sensor Outage (entire zone missing)":
                        var outageBuilding = PromptForBuilding(simulator.GetBuildingIds());
                        var outageZone = AnsiConsole.Ask<int>("Which zone (1-6):", 3);
                        var outageDuration = AnsiConsole.Ask<int>("Duration (minutes):", 20);
                        simulator.InjectZoneSensorOutage(outageBuilding, outageZone, TimeSpan.FromMinutes(outageDuration));
                        AnsiConsole.MarkupLine($"[grey]→ Effect: All sensors in zone {outageZone} report missing readings[/]");
                        break;
                }

                AnsiConsole.MarkupLine("");
            }
            finally
            {
                simulator.SetSuppressConsoleOutput(false);
            }

            break;
        }
        case ConsoleKey.S:
            simulator.SetSuppressConsoleOutput(true);
            try
            {
                AnsiConsole.Write(new Rule("[yellow]Current Status[/]").RuleStyle("grey").LeftJustified());
                simulator.DisplayStatus();
                AnsiConsole.MarkupLine("");
            }
            finally
            {
                simulator.SetSuppressConsoleOutput(false);
            }
            break;

        case ConsoleKey.R:
            simulator.SetSuppressConsoleOutput(true);
            try
            {
                simulator.ResetAllAnomalies();
                AnsiConsole.MarkupLine("");
            }
            finally
            {
                simulator.SetSuppressConsoleOutput(false);
            }
            break;

        case ConsoleKey.Q:
            AnsiConsole.MarkupLine("[yellow]Stopping multi-sensor simulator...[/]");
            cts.Cancel();
            await telemetryTask;
            return;
    }
}

static string PromptForBuilding(List<string> buildings)
{
    return AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select building:")
            .AddChoices(buildings));
}
