using Microsoft.Data.Sqlite;

namespace CoreSyncServer.Data.Schema;

public class SqliteSchemaReader : ISchemaReader
{
    public DataStoreType StoreType => DataStoreType.SQLite;

    public async Task<IReadOnlyList<TableSchema>> GetTablesAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = new List<TableSchema>();

        // Get all user tables
        await using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";

        await using var tableReader = await tableCmd.ExecuteReaderAsync(cancellationToken);
        var tableNames = new List<string>();
        while (await tableReader.ReadAsync(cancellationToken))
        {
            tableNames.Add(tableReader.GetString(0));
        }

        foreach (var tableName in tableNames)
        {
            var table = new TableSchema { Name = tableName };

            // Get columns via PRAGMA
            var columns = new List<ColumnSchema>();
            await using var colCmd = connection.CreateCommand();
            colCmd.CommandText = $"PRAGMA table_info('{tableName}')";

            await using var colReader = await colCmd.ExecuteReaderAsync(cancellationToken);
            while (await colReader.ReadAsync(cancellationToken))
            {
                columns.Add(new ColumnSchema
                {
                    Name = colReader.GetString(1),          // name
                    DataType = colReader.GetString(2),       // type
                    IsNullable = colReader.GetInt32(3) == 0, // notnull (0 = nullable)
                    IsPrimaryKey = colReader.GetInt32(5) > 0 // pk
                });
            }

            // Get foreign keys via PRAGMA
            await using var fkCmd = connection.CreateCommand();
            fkCmd.CommandText = $"PRAGMA foreign_key_list('{tableName}')";

            await using var fkReader = await fkCmd.ExecuteReaderAsync(cancellationToken);
            while (await fkReader.ReadAsync(cancellationToken))
            {
                var fromColumn = fkReader.GetString(3); // from
                var toTable = fkReader.GetString(2);     // table
                var toColumn = fkReader.GetString(4);    // to

                var column = columns.FirstOrDefault(c => c.Name == fromColumn);
                if (column != null)
                {
                    column.ReferencedTableName = toTable;
                    column.ReferencedColumnName = toColumn;
                }
            }

            table.Columns = columns;
            tables.Add(table);
        }

        return tables;
    }
}
