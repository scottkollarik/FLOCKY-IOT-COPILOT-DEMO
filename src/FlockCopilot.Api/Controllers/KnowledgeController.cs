using FlockCopilot.Api.Services;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.AspNetCore.Mvc;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/knowledge")]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeSearchService _knowledgeSearchService;
    private readonly IConfiguration _configuration;

    public KnowledgeController(IKnowledgeSearchService knowledgeSearchService, IConfiguration configuration)
    {
        _knowledgeSearchService = knowledgeSearchService;
        _configuration = configuration;
    }

    [HttpPost("search")]
    public async Task<IActionResult> SearchKnowledgeAsync([FromBody] KnowledgeSearchRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { message = "Query is required." });
        }

        var documents = await _knowledgeSearchService.SearchAsync(request.Query, cancellationToken);
        var results = documents
            .Select(d =>
            {
                var blobName = ExtractBlobName(d.Source);
                var downloadUrl = string.IsNullOrWhiteSpace(blobName)
                    ? null
                    : Url.RouteUrl(nameof(DownloadKnowledgeDocumentAsync), new { blobName });

                return new KnowledgeDocumentResult(
                    Title: d.Title,
                    Content: d.Content,
                    Source: blobName ?? d.Source,
                    DownloadUrl: downloadUrl);
            })
            .ToList();

        return Ok(new { documents = results });
    }

    [HttpGet("documents/{*blobName}", Name = nameof(DownloadKnowledgeDocumentAsync))]
    public async Task<IActionResult> DownloadKnowledgeDocumentAsync([FromRoute] string blobName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return BadRequest(new { message = "blobName is required." });
        }

        var storageAccount = _configuration["INGEST_STORAGE_ACCOUNT"];
        var containerName = _configuration["KNOWLEDGE_STORAGE_CONTAINER"];
        if (string.IsNullOrWhiteSpace(storageAccount) || string.IsNullOrWhiteSpace(containerName))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Knowledge storage is not configured." });
        }

        var serviceClient = new BlobServiceClient(new Uri($"https://{storageAccount}.blob.core.windows.net"), new DefaultAzureCredential());
        var container = serviceClient.GetBlobContainerClient(containerName);
        var blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            return NotFound(new { message = "Document not found." });
        }

        var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        var contentType = download.Value.Details.ContentType ?? "application/octet-stream";
        var fileName = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? blobName;

        return File(download.Value.Content, contentType, fileName);
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
                var builder = new BlobUriBuilder(uri);
                return string.IsNullOrWhiteSpace(builder.BlobName) ? null : builder.BlobName;
            }
            catch
            {
                return uri.Segments.LastOrDefault()?.Trim('/') ?? source;
            }
        }

        return source;
    }
}

public class KnowledgeSearchRequest
{
    public string? Query { get; set; }
}

public record KnowledgeDocumentResult(string Title, string Content, string? Source, string? DownloadUrl);
