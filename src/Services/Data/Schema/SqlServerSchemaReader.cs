using Microsoft.Data.SqlClient;

namespace CoreSyncServer.Data.Schema;

public class SqlServerSchemaReader : ISchemaReader
{
    public DataStoreType StoreType => DataStoreType.SqlServer;

    public async Task<IReadOnlyList<TableSchema>> GetTablesAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = new Dictionary<(string schema, string name), TableSchema>();

        // Get all user tables with columns, primary keys, and foreign keys in one go
        const string query = """
            SELECT
                t.TABLE_SCHEMA,
                t.TABLE_NAME,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.IS_NULLABLE,
                CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IS_PRIMARY_KEY,
                fk_ref.REFERENCED_TABLE_SCHEMA,
                fk_ref.REFERENCED_TABLE_NAME,
                fk_ref.REFERENCED_COLUMN_NAME
            FROM INFORMATION_SCHEMA.TABLES t
            INNER JOIN INFORMATION_SCHEMA.COLUMNS c
                ON c.TABLE_SCHEMA = t.TABLE_SCHEMA AND c.TABLE_NAME = t.TABLE_NAME
            LEFT JOIN (
                SELECT ku.TABLE_SCHEMA, ku.TABLE_NAME, ku.COLUMN_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                    AND tc.TABLE_SCHEMA = ku.TABLE_SCHEMA
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.TABLE_SCHEMA = c.TABLE_SCHEMA AND pk.TABLE_NAME = c.TABLE_NAME AND pk.COLUMN_NAME = c.COLUMN_NAME
            LEFT JOIN (
                SELECT
                    cu.TABLE_SCHEMA,
                    cu.TABLE_NAME,
                    cu.COLUMN_NAME,
                    ku2.TABLE_SCHEMA AS REFERENCED_TABLE_SCHEMA,
                    ku2.TABLE_NAME AS REFERENCED_TABLE_NAME,
                    ku2.COLUMN_NAME AS REFERENCED_COLUMN_NAME
                FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS rc
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE cu
                    ON rc.CONSTRAINT_NAME = cu.CONSTRAINT_NAME
                    AND rc.CONSTRAINT_SCHEMA = cu.CONSTRAINT_SCHEMA
                INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku2
                    ON rc.UNIQUE_CONSTRAINT_NAME = ku2.CONSTRAINT_NAME
                    AND rc.UNIQUE_CONSTRAINT_SCHEMA = ku2.CONSTRAINT_SCHEMA
                    AND cu.ORDINAL_POSITION = ku2.ORDINAL_POSITION
            ) fk_ref ON fk_ref.TABLE_SCHEMA = c.TABLE_SCHEMA AND fk_ref.TABLE_NAME = c.TABLE_NAME AND fk_ref.COLUMN_NAME = c.COLUMN_NAME
            WHERE t.TABLE_TYPE = 'BASE TABLE'
            ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION
            """;

        await using var cmd = new SqlCommand(query, connection);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var tableName = reader.GetString(1);
            var key = (schema, tableName);

            if (!tables.TryGetValue(key, out var table))
            {
                table = new TableSchema { Name = tableName, Schema = schema };
                tables[key] = table;
            }

            var column = new ColumnSchema
            {
                Name = reader.GetString(2),
                DataType = reader.GetString(3),
                IsNullable = reader.GetString(4) == "YES",
                IsPrimaryKey = reader.GetInt32(5) == 1,
                ReferencedTableSchema = reader.IsDBNull(6) ? null : reader.GetString(6),
                ReferencedTableName = reader.IsDBNull(7) ? null : reader.GetString(7),
                ReferencedColumnName = reader.IsDBNull(8) ? null : reader.GetString(8)
            };

            table.Columns.Add(column);
        }

        return tables.Values.ToList();
    }
}
