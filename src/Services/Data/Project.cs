namespace CoreSyncServer.Data;

public class Project
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public DateTime CreatedDate { get; set; }

    public IList<DataStore> DataStores { get; set; } = [];
}