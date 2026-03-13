using CoreSync;
using CoreSyncServer.Data;

namespace CoreSyncServer.Services;

public interface ISyncProviderFactory
{
    ISyncProvider CreateSyncProvider(DataStoreConfiguration configuration);
}
