using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlockCopilot.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Functions.Services;

public interface IManualReportExtractor
{
    Task<ManualReport> ExtractAsync(Stream blobStream, string contentType, CancellationToken cancellationToken);
}

public class ManualReportExtractor : IManualReportExtractor
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ManualReportExtractor> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ManualReportExtractor(HttpClient httpClient, IConfiguration configuration, ILogger<ManualReportExtractor> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ManualReport> ExtractAsync(Stream blobStream, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await blobStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var manual = await TryCallExtractionAgentAsync(buffer.ToArray(), contentType, cancellationToken);
        if (manual != null)
        {
            return manual;
        }

        buffer.Position = 0;
        try
        {
            return await JsonSerializer.DeserializeAsync<ManualReport>(buffer, JsonOptions, cancellationToken)
                   ?? new ManualReport();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Manual blob was not JSON – returning a minimal placeholder report.");
            return new ManualReport
            {
                Notes = new List<string> { "Placeholder manual report generated due to unknown file format." }
            };
        }
    }

    private async Task<ManualReport?> TryCallExtractionAgentAsync(byte[] payload, string contentType, CancellationToken cancellationToken)
    {
        var endpoint = _configuration["AI_SERVICE_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        try
        {
            using var content = new ByteArrayContent(payload);
            content.Headers.ContentType = new MediaTypeHeaderValue(string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

            // The actual Extraction Agent endpoint is project-specific. Here we assume the Foundry project
            // exposes a POST /extraction/manual endpoint that returns the structured DTO described in ManualReport.cs.
            var requestUri = new Uri(new Uri(endpoint.TrimEnd('/') + "/"), "extraction/manual");
            var response = await _httpClient.PostAsync(requestUri, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Extraction agent returned {StatusCode}. Falling back to raw JSON parsing.", response.StatusCode);
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<ManualReport>(responseStream, JsonOptions, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to call extraction agent; falling back to manual parsing.");
            return null;
        }
    }
}
