namespace CoreSyncServer.Data
{
    /// <summary>
    /// Default tenant provider for single-tenant (on-premise) deployments.
    /// Always returns null, meaning no tenant scoping is applied.
    /// </summary>
    public class NullTenantProvider : ITenantProvider
    {
        public string? TenantId => null;
    }
}
