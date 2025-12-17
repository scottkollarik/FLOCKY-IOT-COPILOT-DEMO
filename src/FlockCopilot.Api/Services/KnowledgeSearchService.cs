using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Api.Services;

public record KnowledgeDocument(string Title, string Content, string? Source);

public interface IKnowledgeSearchService
{
    Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(string query, CancellationToken cancellationToken);
}

internal sealed class KnowledgeSearchService : IKnowledgeSearchService
{
    private const string ApiVersion = "2023-11-01";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<KnowledgeSearchService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public KnowledgeSearchService(HttpClient httpClient, IConfiguration configuration, ILogger<KnowledgeSearchService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var endpoint = _configuration["AZURE_SEARCH_ENDPOINT"];
        var indexName = _configuration["AZURE_SEARCH_INDEX"];
        var apiKey = _configuration["AZURE_SEARCH_API_KEY"];

        if (string.IsNullOrWhiteSpace(endpoint) ||
            string.IsNullOrWhiteSpace(indexName) ||
            string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Azure AI Search configuration is missing. Endpoint: {Endpoint}, Index: {Index}", endpoint, indexName);
            return Array.Empty<KnowledgeDocument>();
        }

        var requestUri = $"{endpoint.TrimEnd('/')}/indexes/{indexName}/docs/search?api-version={ApiVersion}";
        object requestPayload;
        if (string.IsNullOrWhiteSpace(_configuration["AZURE_SEARCH_SEMANTIC_CONFIG"]))
        {
            requestPayload = new
            {
                search = query,
                top = 3
            };
        }
        else
        {
            requestPayload = new
            {
                search = query,
                top = 3,
                queryLanguage = "en-us",
                semanticConfiguration = _configuration["AZURE_SEARCH_SEMANTIC_CONFIG"],
                queryType = "semantic"
            };
        }

        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(requestPayload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("api-key", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Azure AI Search returned status {StatusCode}", response.StatusCode);
                return Array.Empty<KnowledgeDocument>();
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<SearchResponse>(stream, JsonOptions, cancellationToken);

            if (payload?.Value == null || payload.Value.Count == 0)
            {
                return Array.Empty<KnowledgeDocument>();
            }

            return payload.Value
                .Select(doc =>
                {
                    var title = doc.Title ?? doc.MetadataTitle ?? doc.MetadataStorageName ?? doc.Id ?? "Untitled";
                    var content = doc.Content ?? doc.Chunk ?? doc.Summary ?? string.Empty;
                    var source = doc.Source ?? doc.MetadataStorageName ?? doc.MetadataStoragePath;
                    return new KnowledgeDocument(title, content, source);
                })
                .Where(d => !string.IsNullOrWhiteSpace(d.Content))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query Azure AI Search.");
            return Array.Empty<KnowledgeDocument>();
        }
    }

    private sealed class SearchResponse
    {
        public List<SearchDocument> Value { get; set; } = new();
    }

    private sealed class SearchDocument
    {
        public string? Title { get; set; }
        public string? Content { get; set; }
        public string? Chunk { get; set; }
        public string? Summary { get; set; }
        public string? Source { get; set; }
        [JsonPropertyName("metadata_title")]
        public string? MetadataTitle { get; set; }

        [JsonPropertyName("metadata_storage_name")]
        public string? MetadataStorageName { get; set; }

        [JsonPropertyName("metadata_storage_path")]
        public string? MetadataStoragePath { get; set; }
        public string? Id { get; set; }
    }
}
