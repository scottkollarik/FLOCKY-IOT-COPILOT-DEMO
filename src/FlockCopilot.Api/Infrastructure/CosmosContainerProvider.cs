using System.Text.Json;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Api.Infrastructure;

public class CosmosContainerProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CosmosContainerProvider> _logger;
    private CosmosClient? _client;
    private Container? _normalizedContainer;
    private Container? _telemetryContainer;
    private Container? _anomaliesContainer;

    public CosmosContainerProvider(IConfiguration configuration, ILogger<CosmosContainerProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Container? GetContainer()
    {
        if (_normalizedContainer != null)
        {
            return _normalizedContainer;
        }

        var containerName = _configuration["COSMOS_DB_CONTAINER"];

        if (string.IsNullOrWhiteSpace(containerName))
        {
            _logger.LogWarning("Cosmos DB settings are missing. Repository will fall back to in-memory storage.");
            return null;
        }

        try
        {
            var client = GetClient();
            if (client == null)
            {
                return null;
            }

            var databaseName = _configuration["COSMOS_DB_DATABASE"]!;
            _normalizedContainer = client.GetContainer(databaseName, containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Cosmos container client.");
            _normalizedContainer = null;
        }

        return _normalizedContainer;
    }

    public Container? GetTelemetryContainer()
    {
        if (_telemetryContainer != null)
        {
            return _telemetryContainer;
        }

        var containerName = _configuration["COSMOS_DB_TELEMETRY_CONTAINER"];
        if (string.IsNullOrWhiteSpace(containerName))
        {
            _logger.LogWarning("COSMOS_DB_TELEMETRY_CONTAINER is not configured; raw telemetry will not be persisted.");
            return null;
        }

        try
        {
            var client = GetClient();
            if (client == null)
            {
                return null;
            }

            var databaseName = _configuration["COSMOS_DB_DATABASE"]!;
            _telemetryContainer = client.GetContainer(databaseName, containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Cosmos telemetry container client.");
            _telemetryContainer = null;
        }

        return _telemetryContainer;
    }

    public Container? GetAnomaliesContainer()
    {
        if (_anomaliesContainer != null)
        {
            return _anomaliesContainer;
        }

        var containerName = _configuration["COSMOS_DB_ANOMALIES_CONTAINER"];
        if (string.IsNullOrWhiteSpace(containerName))
        {
            _logger.LogWarning("COSMOS_DB_ANOMALIES_CONTAINER is not configured; anomalies will not be persisted.");
            return null;
        }

        try
        {
            var client = GetClient();
            if (client == null)
            {
                return null;
            }

            var databaseName = _configuration["COSMOS_DB_DATABASE"]!;
            _anomaliesContainer = client.GetContainer(databaseName, containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Cosmos anomalies container client.");
            _anomaliesContainer = null;
        }

        return _anomaliesContainer;
    }

    private CosmosClient? GetClient()
    {
        if (_client != null)
        {
            return _client;
        }

        var accountEndpoint = _configuration["COSMOS_DB_ACCOUNT"];
        var databaseName = _configuration["COSMOS_DB_DATABASE"];

        if (string.IsNullOrWhiteSpace(accountEndpoint) || string.IsNullOrWhiteSpace(databaseName))
        {
            _logger.LogWarning("Cosmos DB settings are missing. Repository will fall back to in-memory storage.");
            return null;
        }

        try
        {
            var credential = new DefaultAzureCredential();
            var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            _client = new CosmosClient(
                accountEndpoint,
                credential,
                new CosmosClientOptions
                {
                    Serializer = new SystemTextJsonCosmosSerializer(serializerOptions)
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Cosmos client.");
            _client = null;
        }

        return _client;
    }
}
