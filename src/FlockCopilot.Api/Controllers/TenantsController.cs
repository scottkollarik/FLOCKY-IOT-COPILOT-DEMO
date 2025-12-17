using FlockCopilot.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace FlockCopilot.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantsController : ControllerBase
{
    private readonly CosmosContainerProvider _provider;

    public TenantsController(CosmosContainerProvider provider)
    {
        _provider = provider;
    }

    [HttpGet]
    public async Task<IActionResult> GetTenantsAsync(CancellationToken cancellationToken)
    {
        var container = _provider.GetContainer();
        if (container == null)
        {
            return Ok(new { tenants = Array.Empty<string>() });
        }

        var query = new QueryDefinition("SELECT DISTINCT VALUE c.tenantId FROM c WHERE IS_DEFINED(c.tenantId)");
        var iterator = container.GetItemQueryIterator<string>(query);

        var tenants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            foreach (var tenant in page)
            {
                if (!string.IsNullOrWhiteSpace(tenant))
                {
                    tenants.Add(tenant);
                }
            }
        }

        return Ok(new { tenants = tenants.OrderBy(t => t).ToArray() });
    }
}

