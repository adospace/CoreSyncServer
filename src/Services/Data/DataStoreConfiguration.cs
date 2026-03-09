using CoreSync;

namespace CoreSyncServer.Data;

public class DataStoreConfiguration
{
    public int Id { get; set; }

    public int DataStoreId { get; set; }

    public DataStore? DataStore { get; set; }

    public IList<DataStoreTableConfiguration> TableConfigurations { get; set; } = [];
}

public class DataStoreTableConfiguration
{
    public int Id { get; set; }

    public int DataStoreConfigurationId { get; set; }

    public DataStoreConfiguration? DataStoreConfiguration { get; set; }

    public required string Name { get; set; }

    public string? Schema { get; set; }

    public SyncDirection SyncDirection { get; set; }
}
