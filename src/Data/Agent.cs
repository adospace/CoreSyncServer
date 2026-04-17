namespace CoreSyncServer.Data;

public class Agent
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public required string ApiKey { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedDate { get; set; }

    public DateTime? LastSeen { get; set; }

    public IList<DataStore> DataStores { get; set; } = [];
}
