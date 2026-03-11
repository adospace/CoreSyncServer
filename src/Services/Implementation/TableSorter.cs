using CoreSyncServer.Services;

namespace CoreSyncServer.Services.Implementation;

/// <summary>
/// Topologically sorts tables using Kahn's algorithm.
/// Tables that are referenced by others (via foreign keys) appear first in the result.
/// </summary>
public class TableSorter : ITableSorter
{
    public TableSortResult Sort(IReadOnlyList<TableSchema> tables)
    {
        // Build a lookup by fully-qualified name (schema.name)
        var tableByKey = new Dictionary<string, TableSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            tableByKey[GetKey(table)] = table;
        }

        // Build adjacency list: edge from A -> B means "A depends on B" (A references B)
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in tables)
        {
            var key = GetKey(table);
            dependencies.TryAdd(key, []);
            dependents.TryAdd(key, []);
        }

        foreach (var table in tables)
        {
            var key = GetKey(table);
            foreach (var column in table.Columns)
            {
                if (column.ReferencedTableName is null)
                    continue;

                var refKey = GetKey(column.ReferencedTableSchema, column.ReferencedTableName);

                // Only consider references to tables in the provided list; skip self-references
                if (string.Equals(refKey, key, StringComparison.OrdinalIgnoreCase) || !tableByKey.ContainsKey(refKey))
                    continue;

                if (dependencies[key].Add(refKey))
                {
                    dependents[refKey].Add(key);
                }
            }
        }

        // Kahn's algorithm
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            inDegree[GetKey(table)] = dependencies[GetKey(table)].Count;
        }

        var queue = new Queue<string>();
        foreach (var (key, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(key);
        }

        var sorted = new List<TableSchema>();
        while (queue.Count > 0)
        {
            var key = queue.Dequeue();
            sorted.Add(tableByKey[key]);

            foreach (var dependent in dependents[key])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        // Detect cycles: any table not yet sorted is part of a cycle
        var sortedSet = new HashSet<string>(sorted.Select(GetKey), StringComparer.OrdinalIgnoreCase);
        var cyclicTables = tables.Where(t => !sortedSet.Contains(GetKey(t))).ToList();

        var cycles = new List<IReadOnlyList<TableSchema>>();
        if (cyclicTables.Count > 0)
        {
            cycles.AddRange(FindCycles(cyclicTables, dependencies));
            // Append cyclic tables at the end in their original order
            sorted.AddRange(cyclicTables);
        }

        return new TableSortResult
        {
            SortedTables = sorted,
            HasCycles = cycles.Count > 0,
            Cycles = cycles
        };
    }

    private static List<IReadOnlyList<TableSchema>> FindCycles(
        List<TableSchema> cyclicTables,
        Dictionary<string, HashSet<string>> dependencies)
    {
        // Find strongly connected components among cyclic tables using iterative DFS
        var cyclicKeys = new HashSet<string>(cyclicTables.Select(GetKey), StringComparer.OrdinalIgnoreCase);
        var tableByKey = cyclicTables.ToDictionary(GetKey, t => t, StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cycles = new List<IReadOnlyList<TableSchema>>();
        var stack = new List<string>();

        foreach (var table in cyclicTables)
        {
            var key = GetKey(table);
            if (visited.Contains(key))
                continue;

            FindCyclesDfs(key, dependencies, cyclicKeys, tableByKey, visited, onStack, stack, cycles);
        }

        return cycles;
    }

    private static void FindCyclesDfs(
        string start,
        Dictionary<string, HashSet<string>> dependencies,
        HashSet<string> cyclicKeys,
        Dictionary<string, TableSchema> tableByKey,
        HashSet<string> visited,
        HashSet<string> onStack,
        List<string> stack,
        List<IReadOnlyList<TableSchema>> cycles)
    {
        visited.Add(start);
        onStack.Add(start);
        stack.Add(start);

        foreach (var dep in dependencies[start])
        {
            if (!cyclicKeys.Contains(dep))
                continue;

            if (!visited.Contains(dep))
            {
                FindCyclesDfs(dep, dependencies, cyclicKeys, tableByKey, visited, onStack, stack, cycles);
            }
            else if (onStack.Contains(dep))
            {
                // Found a cycle: extract from stack
                var cycleStart = stack.IndexOf(dep);
                var cycle = stack.Skip(cycleStart).Select(k => tableByKey[k]).ToList();
                cycles.Add(cycle);
            }
        }

        stack.RemoveAt(stack.Count - 1);
        onStack.Remove(start);
    }

    private static string GetKey(TableSchema table) => GetKey(table.Schema, table.Name);

    private static string GetKey(string? schema, string name) =>
        string.IsNullOrEmpty(schema) ? name : $"{schema}.{name}";
}
