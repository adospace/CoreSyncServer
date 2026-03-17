namespace CoreSyncServer.Data
{
    /// <summary>
    /// Provides the current tenant context. In the OSS (single-tenant) build,
    /// <see cref="NullTenantProvider"/> is registered by default. SaaS layers
    /// replace this with a request-scoped implementation that resolves the tenant
    /// from the incoming request (e.g. subdomain).
    /// </summary>
    public interface ITenantProvider
    {
        string? TenantId { get; }
    }
}
