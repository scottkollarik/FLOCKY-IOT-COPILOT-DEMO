using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

namespace FlockCopilot.Api.Services;

public interface ITenantContext
{
    string TenantId { get; }
}

public class TenantContext : ITenantContext
{
    public const string TenantIdHeaderName = "X-Tenant-Id-Claim";
    private const string DefaultTenant = "tenant-demo-123";

    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TenantContext(IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public string TenantId
    {
        get
        {
            var fromHeader = _httpContextAccessor.HttpContext?.Request?.Headers[TenantIdHeaderName].ToString();
            if (!string.IsNullOrWhiteSpace(fromHeader))
            {
                return fromHeader;
            }

            return _configuration["DEFAULT_TENANT_ID"] ?? DefaultTenant;
        }
    }
}
