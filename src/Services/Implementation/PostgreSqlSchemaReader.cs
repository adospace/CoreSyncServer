using CoreSyncServer.Data;
using CoreSyncServer.Services;
using Npgsql;

namespace CoreSyncServer.Services.Implementation;

public class PostgreSqlSchemaReader : ISchemaReader
{
    public DataStoreType StoreType => DataStoreType.PostgreSQL;

    public async Task<IReadOnlyList<TableSchema>> GetTablesAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = new Dictionary<(string schema, string name), TableSchema>();

        const string query = """
            SELECT
                t.table_schema,
                t.table_name,
                c.column_name,
                c.data_type,
                c.is_nullable,
                CASE WHEN pk.column_name IS NOT NULL THEN 1 ELSE 0 END AS is_primary_key,
                ccu.table_schema AS referenced_table_schema,
                ccu.table_name AS referenced_table_name,
                ccu.column_name AS referenced_column_name
            FROM information_schema.tables t
            INNER JOIN information_schema.columns c
                ON c.table_schema = t.table_schema AND c.table_name = t.table_name
            LEFT JOIN (
                SELECT ku.table_schema, ku.table_name, ku.column_name
                FROM information_schema.table_constraints tc
                INNER JOIN information_schema.key_column_usage ku
                    ON tc.constraint_name = ku.constraint_name
                    AND tc.table_schema = ku.table_schema
                WHERE tc.constraint_type = 'PRIMARY KEY'
            ) pk ON pk.table_schema = c.table_schema AND pk.table_name = c.table_name AND pk.column_name = c.column_name
            LEFT JOIN (
                SELECT
                    kcu.table_schema,
                    kcu.table_name,
                    kcu.column_name,
                    ccu.table_schema AS ref_table_schema,
                    ccu.table_name AS ref_table_name,
                    ccu.column_name AS ref_column_name,
                    rc.constraint_name
                FROM information_schema.referential_constraints rc
                INNER JOIN information_schema.key_column_usage kcu
                    ON rc.constraint_name = kcu.constraint_name
                    AND rc.constraint_schema = kcu.constraint_schema
                INNER JOIN information_schema.constraint_column_usage ccu
                    ON rc.unique_constraint_name = ccu.constraint_name
                    AND rc.unique_constraint_schema = ccu.constraint_schema
            ) fk ON fk.table_schema = c.table_schema AND fk.table_name = c.table_name AND fk.column_name = c.column_name
            WHERE t.table_type = 'BASE TABLE'
              AND t.table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY t.table_schema, t.table_name, c.ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(query, connection);
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
