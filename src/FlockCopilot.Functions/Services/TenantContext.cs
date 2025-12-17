using Microsoft.Extensions.Configuration;

namespace FlockCopilot.Functions.Services;

public interface ITenantContext
{
    string TenantId { get; }
}

public class TenantContext : ITenantContext
{
    private const string DefaultTenant = "tenant-demo-123";

    public TenantContext(IConfiguration configuration)
    {
        TenantId = configuration["DEFAULT_TENANT_ID"] ?? DefaultTenant;
    }

    public string TenantId { get; }
}
