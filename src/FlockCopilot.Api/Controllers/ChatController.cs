using System.Text;
using FlockCopilot.Api.Models;
using FlockCopilot.Api.Services;
using FlockCopilot.Api.Services.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private static readonly string[] DefaultFlockIds = ["building-a", "building-b", "building-c"];
    private const double BagokStressThreshold = 0.7;

    private readonly INormalizedFlockRepository _normalizedRepository;
    private readonly IRawTelemetryRepository _rawTelemetryRepository;
    private readonly IKnowledgeSearchService _knowledgeSearchService;
    private readonly IAzureOpenAiChatService _chatService;
    private readonly ITenantContext _tenantContext;

    public ChatController(
        INormalizedFlockRepository normalizedRepository,
        IRawTelemetryRepository rawTelemetryRepository,
        IKnowledgeSearchService knowledgeSearchService,
        IAzureOpenAiChatService chatService,
        ITenantContext tenantContext)
    {
        _normalizedRepository = normalizedRepository;
        _rawTelemetryRepository = rawTelemetryRepository;
        _knowledgeSearchService = knowledgeSearchService;
        _chatService = chatService;
        _tenantContext = tenantContext;
    }

    [HttpPost]
    public async Task<IActionResult> ChatAsync([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { error = "message is required." });
        }

        var tenantId = _tenantContext.TenantId;
        var mode = (request.Mode ?? "auto").Trim().ToLowerInvariant();

        var requestedFlockId = ResolveFlockId(request.Message) ?? request.FlockId;
        var targetFlockIds = !string.IsNullOrWhiteSpace(requestedFlockId) ? [requestedFlockId!] : DefaultFlockIds;

        var timeIntent = ResolveTimeIntent(request.Message, request.ClientNowUtc, request.TzOffsetMinutes);
        var (shouldFetchTelemetry, shouldFetchHistory) = ResolveAutoPlan(mode, request.Message, requestedFlockId, timeIntent);

        var latestByFlockId = new Dictionary<string, NormalizedFlockPerformance?>(StringComparer.OrdinalIgnoreCase);
        if (shouldFetchTelemetry)
        {
            foreach (var flockId in targetFlockIds)
            {
                latestByFlockId[flockId] = await _normalizedRepository.GetLatestAsync(tenantId, flockId, cancellationToken);
            }
        }

        var latestRaw = (shouldFetchTelemetry && !string.IsNullOrWhiteSpace(requestedFlockId))
            ? await _rawTelemetryRepository.GetLatestAsync(tenantId, requestedFlockId!, cancellationToken)
            : null;

        var historyByFlockId = new Dictionary<string, IReadOnlyList<NormalizedFlockPerformance>>(StringComparer.OrdinalIgnoreCase);
        if (shouldFetchHistory)
        {
            foreach (var flockId in targetFlockIds)
            {
                var records = await _normalizedRepository.GetHistoryAsync(tenantId, flockId, timeIntent.Lookback, cancellationToken);
                historyByFlockId[flockId] = FilterHistory(records, timeIntent);
            }
        }

        var anomalies = new List<string>();
        var staleFlocks = new List<string>();
        var anyAnomalousPerf = default(NormalizedFlockPerformance?);

        foreach (var (flockId, perf) in latestByFlockId)
        {
            var (localAnomalies, stale) = DetectAnomalies(perf);
            if (stale)
            {
                staleFlocks.Add(flockId);
            }

            if (localAnomalies.Count > 0)
            {
                anyAnomalousPerf ??= perf;
                if (targetFlockIds.Length > 1)
                {
                    anomalies.AddRange(localAnomalies.Select(a => $"{flockId}: {a}"));
                }
                else
                {
                    anomalies.AddRange(localAnomalies);
                }
            }
        }

        // If the latest snapshot indicates anomalies, enrich them with raw sensor evidence when available.
        if (anomalies.Count > 0 && latestRaw != null && !string.IsNullOrWhiteSpace(requestedFlockId))
        {
            anomalies = EnrichAnomaliesWithRawEvidence(anomalies, latestRaw);
        }

        // If the user asked about a time window, prefer a windowed anomaly summary.
        if (timeIntent.IsWindowed && historyByFlockId.Count > 0)
        {
            anomalies = SummarizeAnomaliesOverWindow(historyByFlockId, timeIntent);
            if (latestRaw != null && !string.IsNullOrWhiteSpace(requestedFlockId))
            {
                var currentExamples = latestRaw.Sensors
                    .Where(s => s.TemperatureAvgF is > 92 or < 70 ||
                                s.HumidityPercent is > 75 or < 40 ||
                                s.Co2Ppm is > 3000 ||
                                s.Nh3Ppm is > 25 ||
                                s.BagokStressScore is >= BagokStressThreshold)
                    .OrderByDescending(s => s.Co2Ppm ?? 0)
                    .ThenByDescending(s => s.Nh3Ppm ?? 0)
                    .ThenByDescending(s => s.TemperatureAvgF ?? 0)
                    .ThenByDescending(s => s.BagokStressScore ?? 0)
                    .Take(5)
                    .Select(s => $"{s.SensorId} (type={s.SensorType ?? "unknown"} zone={s.Zone ?? "n/a"})")
                    .ToList();

                if (currentExamples.Count > 0)
                {
                    anomalies.Add($"{requestedFlockId}: current alerting sensors (latest snapshot {latestRaw.CapturedAt:O}): {string.Join("; ", currentExamples)}");
                }
            }
        }

        var shouldRetrieveBestPractices =
            mode == "bestpractices" ||
            (mode == "auto" && (anomalies.Count > 0 || LooksLikeHowTo(request.Message) || LooksLikeDocRequest(request.Message)));

        IReadOnlyList<KnowledgeDocument> docs = Array.Empty<KnowledgeDocument>();
        if (shouldRetrieveBestPractices)
        {
            var queryPerf =
                !string.IsNullOrWhiteSpace(requestedFlockId)
                    ? latestByFlockId.GetValueOrDefault(requestedFlockId!)
                    : anyAnomalousPerf;
            var query = BuildKnowledgeQuery(request.Message, queryPerf);
            docs = await _knowledgeSearchService.SearchAsync(query, cancellationToken);
        }

        // If the user explicitly asked for a document/link, keep the sources focused.
        if (mode != "diagnostics" && LooksLikeDocRequest(request.Message) && docs.Count > 1)
        {
            docs = docs.Take(1).ToList();
        }

        var systemPrompt = BuildSystemPrompt(
            mode,
            tenantId,
            requestedFlockId,
            latestByFlockId,
            latestRaw,
            timeIntent,
            historyByFlockId,
            anomalies,
            staleFlocks,
            docs);
        string answer;
        try
        {
            answer = await _chatService.ChatAsync(systemPrompt, request.Message, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new
            {
                error = "Intent orchestrator is not configured with Azure OpenAI settings.",
                requiredEnv = new[] { "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_API_KEY", "AZURE_OPENAI_DEPLOYMENT" }
            });
        }

        var bestPracticeDocuments = docs.Select(d =>
        {
            var blobName = ExtractBlobName(d.Source);
            var downloadUrl = string.IsNullOrWhiteSpace(blobName) ? null : $"/api/knowledge/documents/{blobName}";
            return new
            {
                title = d.Title,
                source = blobName ?? d.Source,
                downloadUrl
            };
        }).ToList();

        return Ok(new
        {
            tenantId,
            mode,
            flockId = requestedFlockId,
            analyzedFlocks = targetFlockIds,
            usedHistory = shouldFetchHistory,
            historyWindow = shouldFetchHistory ? timeIntent.Label : null,
            anomalies,
            staleFlocks,
            bestPracticesDocumentCount = docs.Count,
            bestPracticesDocuments = bestPracticeDocuments,
            answer
        });
    }

    private static (bool shouldFetchTelemetry, bool shouldFetchHistory) ResolveAutoPlan(
        string mode,
        string message,
        string? requestedFlockId,
        TimeIntent timeIntent)
    {
        if (mode == "diagnostics")
        {
            return (true, timeIntent.IsWindowed);
        }

        if (mode == "bestpractices")
        {
            return (false, false);
        }

        // mode == auto
        var hasExplicitTarget = !string.IsNullOrWhiteSpace(requestedFlockId);
        var looksHowTo = LooksLikeHowTo(message);
        var looksDocRequest = LooksLikeDocRequest(message);
        var looksDiagnostic = LooksLikeDiagnostics(message);

        if ((looksHowTo || looksDocRequest) && !looksDiagnostic && !hasExplicitTarget && !timeIntent.IsWindowed)
        {
            // Pure how-to: don't fetch telemetry unless the user asks about their buildings/status.
            return (false, false);
        }

        return (true, timeIntent.IsWindowed);
    }

    private static bool LooksLikeDiagnostics(string message)
    {
        var m = message.ToLowerInvariant();
        return m.Contains("anomal", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("issue", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("problem", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("what's happening", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("whats happening", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("status", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("current", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("in my buildings", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("any alerts", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<NormalizedFlockPerformance> FilterHistory(
        IReadOnlyList<NormalizedFlockPerformance> records,
        TimeIntent timeIntent)
    {
        if (!timeIntent.IsWindowed || timeIntent.FilterStartUtc == null || timeIntent.FilterEndUtc == null)
        {
            return records;
        }

        var start = timeIntent.FilterStartUtc.Value;
        var end = timeIntent.FilterEndUtc.Value;
        return records.Where(r => r.Timestamp >= start && r.Timestamp < end).ToList();
    }

    private static TimeIntent ResolveTimeIntent(string message, DateTimeOffset? clientNowUtc, int? tzOffsetMinutes)
    {
        var m = message.ToLowerInvariant();
        var nowUtc = clientNowUtc ?? DateTimeOffset.UtcNow;

        if (m.Contains("today", StringComparison.OrdinalIgnoreCase))
        {
            if (tzOffsetMinutes.HasValue)
            {
                var (start, end) = ResolveLocalDayWindowUtc(nowUtc, tzOffsetMinutes.Value, dayOffset: 0);
                return TimeIntent.Windowed("today (local day)", end - start, start, end);
            }

            return TimeIntent.Windowed("today (last 24h)", TimeSpan.FromHours(24), nowUtc - TimeSpan.FromHours(24), nowUtc);
        }

        if (m.Contains("yesterday", StringComparison.OrdinalIgnoreCase))
        {
            if (tzOffsetMinutes.HasValue)
            {
                var (start, end) = ResolveLocalDayWindowUtc(nowUtc, tzOffsetMinutes.Value, dayOffset: -1);
                return TimeIntent.Windowed("yesterday (local day)", nowUtc - start, start, end);
            }

            var endUtc = nowUtc - TimeSpan.FromHours(24);
            var startUtc = nowUtc - TimeSpan.FromHours(48);
            return TimeIntent.Windowed("yesterday (24h window)", TimeSpan.FromHours(48), startUtc, endUtc);
        }

        var minutes = MatchInt(m, "last ", " minutes") ?? MatchInt(m, "last ", " mins") ?? MatchInt(m, "last ", " min");
        if (minutes.HasValue)
        {
            var lookback = TimeSpan.FromMinutes(minutes.Value);
            return TimeIntent.Windowed($"last {minutes.Value} minutes", lookback, nowUtc - lookback, nowUtc);
        }

        var hours = MatchInt(m, "last ", " hours") ?? MatchInt(m, "last ", " hrs") ?? MatchInt(m, "last ", " hr");
        if (hours.HasValue)
        {
            var lookback = TimeSpan.FromHours(hours.Value);
            return TimeIntent.Windowed($"last {hours.Value} hours", lookback, nowUtc - lookback, nowUtc);
        }

        var days = MatchInt(m, "last ", " days") ?? MatchInt(m, "last ", " day");
        if (days.HasValue)
        {
            var lookback = TimeSpan.FromDays(days.Value);
            return TimeIntent.Windowed($"last {days.Value} days", lookback, nowUtc - lookback, nowUtc);
        }

        if (m.Contains("trend", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("history", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("over time", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("past", StringComparison.OrdinalIgnoreCase))
        {
            var lookback = TimeSpan.FromDays(7);
            return TimeIntent.Windowed("history (7d)", lookback, nowUtc - lookback, nowUtc);
        }

        return TimeIntent.None();
    }

    // tzOffsetMinutes uses JavaScript Date.getTimezoneOffset() semantics:
    // minutes to add to local time to get UTC (e.g., EST winter = 300, EDT = 240).
    private static (DateTimeOffset startUtc, DateTimeOffset endUtc) ResolveLocalDayWindowUtc(
        DateTimeOffset nowUtc,
        int tzOffsetMinutes,
        int dayOffset)
    {
        var offset = TimeSpan.FromMinutes(tzOffsetMinutes);
        var localNow = nowUtc - offset;
        var localMidnight = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, TimeSpan.Zero)
            .AddDays(dayOffset);

        var startUtc = localMidnight + offset;
        var endUtc = startUtc.AddDays(1);
        return (startUtc, endUtc);
    }

    private static int? MatchInt(string haystack, string prefix, string suffix)
    {
        var start = haystack.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var afterPrefix = start + prefix.Length;
        var end = haystack.IndexOf(suffix, afterPrefix, StringComparison.OrdinalIgnoreCase);
        if (end < 0) return null;
        var slice = haystack.Substring(afterPrefix, end - afterPrefix).Trim();
        return int.TryParse(slice, out var value) ? value : null;
    }

    private static string? ResolveFlockId(string message)
    {
        var m = message.ToLowerInvariant();
        if (m.Contains("building-a")) return "building-a";
        if (m.Contains("building-b")) return "building-b";
        if (m.Contains("building-c")) return "building-c";
        return null;
    }

    private static bool LooksLikeHowTo(string message)
    {
        var m = message.ToLowerInvariant();
        return m.Contains("how", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("mitigate", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("best practice", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("best practices", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("what should i do", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("recommend", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("steps", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("temperature control", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("ventilation", StringComparison.OrdinalIgnoreCase) ||
               m.Contains("ammonia", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDocRequest(string message)
    {
        var m = message.ToLowerInvariant();
        return (m.Contains("link", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("document", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("source", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("download", StringComparison.OrdinalIgnoreCase)) &&
               (m.Contains("best practice", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("best practices", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("guide", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("playbook", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildKnowledgeQuery(string message, NormalizedFlockPerformance? perf)
    {
        var sb = new StringBuilder(message);
        if (perf?.Metrics?.Co2AvgPpm is > 3000)
        {
            sb.Append(" high CO2 ventilation remediation");
        }
        if (perf?.Metrics?.Nh3AvgPpm is > 25)
        {
            sb.Append(" high ammonia NH3 remediation litter ventilation");
        }
        if (perf?.Metrics?.TemperatureAvgF is > 88)
        {
            sb.Append(" heat stress mitigation cooling ventilation water");
        }
        if (perf?.Metrics?.BagokStressHotZoneCount is > 0)
        {
            sb.Append(" stress vocalization welfare indicators");
        }

        return sb.ToString();
    }

    private static (List<string> anomalies, bool stale) DetectAnomalies(NormalizedFlockPerformance? perf)
    {
        var anomalies = new List<string>();
        if (perf?.Metrics == null)
        {
            return (anomalies, true);
        }

        var now = DateTimeOffset.UtcNow;
        var stale = (now - perf.Timestamp) > TimeSpan.FromMinutes(15);

        if (perf.Metrics.TemperatureAvgF is > 88)
        {
            anomalies.Add($"High temperature ({perf.Metrics.TemperatureAvgF:0.0}°F)");
        }
        if (perf.Metrics.Co2AvgPpm is > 3000)
        {
            anomalies.Add($"Elevated CO₂ ({perf.Metrics.Co2AvgPpm:0} ppm)");
        }
        if (perf.Metrics.Nh3AvgPpm is > 25)
        {
            anomalies.Add($"Elevated NH₃ ({perf.Metrics.Nh3AvgPpm:0.0} ppm)");
        }
        if (perf.Metrics.BagokStressHotZoneCount is > 0)
        {
            anomalies.Add($"Bagok stress hot zones ({perf.Metrics.BagokStressHotZoneCount})");
        }
        if (perf.SensorAlerts != null && perf.SensorAlerts.Count > 0)
        {
            anomalies.Add($"Sensor alerts ({perf.SensorAlerts.Count})");
        }

        return (anomalies, stale);
    }

    private static List<string> EnrichAnomaliesWithRawEvidence(List<string> anomalies, RawTelemetrySnapshot raw)
    {
        var enriched = new List<string>();

        foreach (var a in anomalies)
        {
            if (a.Contains("Bagok stress", StringComparison.OrdinalIgnoreCase))
            {
                var bagokExamples = raw.Sensors
                    .Where(s => s.SensorType != null &&
                                s.SensorType.Equals("audio", StringComparison.OrdinalIgnoreCase) &&
                                s.BagokStressScore is >= BagokStressThreshold)
                    .OrderByDescending(s => s.BagokStressScore)
                    .Take(5)
                    .Select(s => $"{s.SensorId} (zone={s.Zone ?? "n/a"} bagok={s.BagokStressScore:0.00} soundDb={s.SoundDbAvg?.ToString("0.0") ?? "n/a"})")
                    .ToList();

                enriched.Add(bagokExamples.Count > 0 ? $"{a} – examples: {string.Join("; ", bagokExamples)}" : a);
                continue;
            }

            if (!a.Contains("Sensor alerts", StringComparison.OrdinalIgnoreCase))
            {
                enriched.Add(a);
                continue;
            }

            var examples = raw.Sensors
                .Where(s => s.TemperatureAvgF is > 92 or < 70 ||
                            s.HumidityPercent is > 75 or < 40 ||
                            s.Co2Ppm is > 3000 ||
                            s.Nh3Ppm is > 25 ||
                            s.BagokStressScore is >= BagokStressThreshold)
                .OrderByDescending(s => s.Co2Ppm ?? 0)
                .ThenByDescending(s => s.Nh3Ppm ?? 0)
                .ThenByDescending(s => s.TemperatureAvgF ?? 0)
                .ThenByDescending(s => s.BagokStressScore ?? 0)
                .Take(5)
                .Select(s =>
                    $"{s.SensorId} (type={s.SensorType ?? "unknown"} zone={s.Zone ?? "n/a"} temp={s.TemperatureAvgF?.ToString("0.0") ?? "n/a"}F hum={s.HumidityPercent?.ToString("0.0") ?? "n/a"}% co2={s.Co2Ppm?.ToString("0") ?? "n/a"} nh3={s.Nh3Ppm?.ToString("0.0") ?? "n/a"} bagok={s.BagokStressScore?.ToString("0.00") ?? "n/a"})")
                .ToList();

            enriched.Add(examples.Count > 0
                ? $"{a} – examples: {string.Join("; ", examples)}"
                : a);
        }

        return enriched;
    }

    private static List<string> SummarizeAnomaliesOverWindow(
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedFlockPerformance>> historyByFlockId,
        TimeIntent timeIntent)
    {
        var result = new List<string>();
        foreach (var (flockId, records) in historyByFlockId.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            var hits = records
                .Select(r => (record: r, anomalies: DetectAnomalies(r).anomalies))
                .Where(x => x.anomalies.Count > 0)
                .OrderByDescending(x => x.record.Timestamp)
                .ToList();

            if (hits.Count == 0)
            {
                continue;
            }

            var latestHit = hits.First();
            var distinct = hits.SelectMany(x => x.anomalies).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            result.Add($"{flockId}: anomalies in {timeIntent.Label} ({hits.Count} records) – latest {latestHit.record.Timestamp:O}: {string.Join("; ", distinct)}");
        }

        return result;
    }

    private static string BuildSystemPrompt(
        string mode,
        string tenantId,
        string? requestedFlockId,
        IReadOnlyDictionary<string, NormalizedFlockPerformance?> latestByFlockId,
        RawTelemetrySnapshot? raw,
        TimeIntent timeIntent,
        IReadOnlyDictionary<string, IReadOnlyList<NormalizedFlockPerformance>> historyByFlockId,
        IReadOnlyList<string> anomalies,
        IReadOnlyList<string> staleFlocks,
        IReadOnlyList<KnowledgeDocument> docs)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are FlockCopilot's intent orchestrator agent.");
        sb.AppendLine("You must ground your answer in the provided data only.");
        sb.AppendLine("If there is insufficient or stale data, say so and recommend monitoring/verification.");
        sb.AppendLine();

        sb.AppendLine($"Tenant: {tenantId}");
        if (!string.IsNullOrWhiteSpace(requestedFlockId))
        {
            sb.AppendLine($"Flock: {requestedFlockId}");
        }
        sb.AppendLine();

        sb.AppendLine("Latest normalized rollups:");
        foreach (var (flockId, latest) in latestByFlockId.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (latest == null)
            {
                sb.AppendLine($"- {flockId}: no record found");
                continue;
            }

            var isStale = staleFlocks.Contains(flockId, StringComparer.OrdinalIgnoreCase);
            sb.AppendLine($"- {flockId}: timestamp={latest.Timestamp:O} stale={(isStale ? "yes" : "no")} confidence={latest.Confidence:0.00}");
            sb.AppendLine($"  metrics: tempAvgF={latest.Metrics.TemperatureAvgF} humidity={latest.Metrics.HumidityPercent} co2AvgPpm={latest.Metrics.Co2AvgPpm} nh3AvgPpm={latest.Metrics.Nh3AvgPpm} bagokAvg={latest.Metrics.BagokStressScoreAvg} bagokHotZones={latest.Metrics.BagokStressHotZoneCount}");

            if (latest.Notes.Any())
            {
                sb.AppendLine("  notes:");
                foreach (var n in latest.Notes.Take(5))
                {
                    sb.AppendLine($"  - {n}");
                }
            }
            if (latest.SensorAlerts?.Count > 0)
            {
                var examples = latest.SensorAlerts.Take(5).Select(kvp => $"{kvp.Key}={kvp.Value:0.##}").ToList();
                sb.AppendLine($"  sensorAlerts: {string.Join(", ", examples)}");
            }
        }
        sb.AppendLine();
        sb.AppendLine($"Detected anomalies: {(anomalies.Count > 0 ? string.Join("; ", anomalies) : "none")}");
        sb.AppendLine();

        if (raw != null && !string.IsNullOrWhiteSpace(requestedFlockId))
        {
            sb.AppendLine($"Latest raw telemetry ({requestedFlockId}) capturedAt: {raw.CapturedAt:O}");
            sb.AppendLine($"Sensors count: {raw.Sensors.Count}");
            sb.AppendLine("Sensor evidence for alerting sensors (top 8):");
            foreach (var s in raw.Sensors
                         .Where(s => s.TemperatureAvgF is > 92 or < 70 ||
                                     s.HumidityPercent is > 75 or < 40 ||
                                     s.Co2Ppm is > 3000 ||
                                     s.Nh3Ppm is > 25 ||
                                     s.BagokStressScore is >= BagokStressThreshold)
                         .OrderByDescending(s => s.Co2Ppm ?? 0)
                         .ThenByDescending(s => s.Nh3Ppm ?? 0)
                         .ThenByDescending(s => s.TemperatureAvgF ?? 0)
                         .ThenByDescending(s => s.BagokStressScore ?? 0)
                         .Take(8))
            {
                sb.AppendLine($"- {s.SensorId} ({s.SensorType ?? "unknown"}) zone={s.Zone} temp={s.TemperatureAvgF} hum={s.HumidityPercent} co2={s.Co2Ppm} nh3={s.Nh3Ppm} bagok={s.BagokStressScore} soundDb={s.SoundDbAvg}");
            }
            sb.AppendLine();
        }

        if (historyByFlockId.Count > 0)
        {
            sb.AppendLine($"History requested: {timeIntent.Label}");
            foreach (var (flockId, records) in historyByFlockId.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                var anomalyRecords = records.Count(r => DetectAnomalies(r).anomalies.Count > 0);
                sb.AppendLine($"- {flockId}: records={records.Count} recordsWithAnomalies={anomalyRecords}");
            }
            sb.AppendLine("Use history only if the user asked about time windows/trends, and prefer the most recent records.");
            sb.AppendLine();
        }

        if (docs.Count > 0)
        {
            sb.AppendLine("Best-practices documents (use for remediation guidance and cite as [Document X]):");
            for (var i = 0; i < docs.Count; i++)
            {
                var doc = docs[i];
                sb.AppendLine($"[Document {i + 1}] {doc.Title}");
                sb.AppendLine(doc.Content);
                sb.AppendLine();
            }
        }
        else if (mode == "bestpractices")
        {
            sb.AppendLine("No best-practices documents were retrieved. If asked for remediation guidance, say the knowledge base is empty/unavailable.");
            sb.AppendLine();
        }

        sb.AppendLine("Answer style:");
        sb.AppendLine("- If the user asked about a time window (today/yesterday/last N): summarize anomalies observed in that window.");
        sb.AppendLine("- If anomalies exist: list them explicitly, including sensorId + zone + sensorType and the metric(s) over threshold when possible.");
        sb.AppendLine("- If no anomalies: state that and suggest routine monitoring.");
        sb.AppendLine("- If data is stale or low-confidence: recommend verifying sensor health before escalating.");
        sb.AppendLine("- Do not invent or format hyperlinks; cite documents as [Document X] only. The UI renders downloadable source links separately.");

        return sb.ToString();
    }

    private static string? ExtractBlobName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
            (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.Length >= 2 ? string.Join('/', segments.Skip(1)) : segments.LastOrDefault();
            }
            catch
            {
                return uri.Segments.LastOrDefault()?.Trim('/') ?? source;
            }
        }

        return source;
    }
}

public sealed class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? Mode { get; set; } // auto | diagnostics | bestPractices
    public string? FlockId { get; set; }
    public DateTimeOffset? ClientNowUtc { get; set; }
    public int? TzOffsetMinutes { get; set; }
}

internal readonly record struct TimeIntent(
    bool IsWindowed,
    string Label,
    TimeSpan Lookback,
    DateTimeOffset? FilterStartUtc,
    DateTimeOffset? FilterEndUtc)
{
    public static TimeIntent None() => new(false, "none", TimeSpan.FromHours(0), null, null);
    public static TimeIntent Windowed(string label, TimeSpan lookback, DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        new(true, label, lookback, startUtc, endUtc);
}
