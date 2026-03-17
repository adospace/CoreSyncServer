namespace CoreSyncServer.Data;

public class Project
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public DateTime CreatedDate { get; set; }

    public string? Description {get;set;}

    public bool IsEnabled {get;set;}

    public string? Tags {get;set;}

    public IList<DataStore> DataStores { get; set; } = [];

    public IList<DiagnosticItem> DiagnosticItems { get; set; } = [];
}