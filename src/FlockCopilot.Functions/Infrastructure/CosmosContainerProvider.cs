using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlockCopilot.Functions.Infrastructure;

public class CosmosContainerProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<CosmosContainerProvider> _logger;
    private Container? _container;

    public CosmosContainerProvider(IConfiguration configuration, ILogger<CosmosContainerProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Container? GetContainer()
    {
        if (_container != null)
        {
            return _container;
        }

        var accountEndpoint = _configuration["COSMOS_DB_ACCOUNT"];
        var databaseName = _configuration["COSMOS_DB_DATABASE"];
        var containerName = _configuration["COSMOS_DB_CONTAINER"];

        if (string.IsNullOrWhiteSpace(accountEndpoint) ||
            string.IsNullOrWhiteSpace(databaseName) ||
            string.IsNullOrWhiteSpace(containerName))
        {
            _logger.LogWarning("Cosmos DB settings are missing. Repository will fall back to in-memory storage.");
            return null;
        }

        try
        {
            var credential = new DefaultAzureCredential();
            var client = new CosmosClient(accountEndpoint, credential);
            _container = client.GetContainer(databaseName, containerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Cosmos container client.");
            _container = null;
        }

        return _container;
    }
}
