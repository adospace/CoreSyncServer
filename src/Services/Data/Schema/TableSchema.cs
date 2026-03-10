namespace CoreSyncServer.Data.Schema;

public class TableSchema
{
    public required string Name { get; set; }

    public string? Schema { get; set; }

    public IList<ColumnSchema> Columns { get; set; } = [];
}

public class ColumnSchema
{
    public required string Name { get; set; }

    public required string DataType { get; set; }

    public bool IsPrimaryKey { get; set; }

    public bool IsNullable { get; set; }

    public string? ReferencedTableName { get; set; }

    public string? ReferencedTableSchema { get; set; }

    public string? ReferencedColumnName { get; set; }
}
