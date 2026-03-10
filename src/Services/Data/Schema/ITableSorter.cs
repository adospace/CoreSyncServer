namespace CoreSyncServer.Data.Schema;

/// <summary>
/// Sorts tables based on foreign key relationships so that referenced tables come before
/// the tables that reference them. When circular references exist, the sorter reports
/// the cycles so the user can decide the order manually.
/// </summary>
public interface ITableSorter
{
    TableSortResult Sort(IReadOnlyList<TableSchema> tables);
}

public class TableSortResult
{
    /// <summary>
    /// Tables sorted so that a table always appears after all tables it references.
    /// When cycles exist, the cyclic tables are appended at the end in their original order.
    /// </summary>
    public required IReadOnlyList<TableSchema> SortedTables { get; init; }

    /// <summary>
    /// True when at least one circular reference was detected.
    /// </summary>
    public bool HasCycles { get; init; }

    /// <summary>
    /// Groups of tables involved in circular references.
    /// Empty when <see cref="HasCycles"/> is false.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<TableSchema>> Cycles { get; init; } = [];
}
