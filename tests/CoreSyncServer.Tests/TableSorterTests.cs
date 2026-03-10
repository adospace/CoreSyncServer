using CoreSyncServer.Data.Schema;
using FluentAssertions;

namespace CoreSyncServer.Tests;

public class TableSorterTests
{
    private readonly TableSorter _sorter = new();

    [Fact]
    public void Sort_WithNoDependencies_ReturnsAllTables()
    {
        var tables = new List<TableSchema>
        {
            CreateTable("Users"),
            CreateTable("Products"),
            CreateTable("Categories")
        };

        var result = _sorter.Sort(tables);

        result.HasCycles.Should().BeFalse();
        result.Cycles.Should().BeEmpty();
        result.SortedTables.Should().HaveCount(3);
    }

    [Fact]
    public void Sort_WithLinearDependencyChain_ReturnsCorrectOrder()
    {
        // Orders -> Customers -> Countries
        var countries = CreateTable("Countries");
        var customers = CreateTable("Customers", ("CountryId", "Countries", "Id"));
        var orders = CreateTable("Orders", ("CustomerId", "Customers", "Id"));

        var result = _sorter.Sort([orders, customers, countries]);

        result.HasCycles.Should().BeFalse();
        var names = result.SortedTables.Select(t => t.Name).ToList();
        names.IndexOf("Countries").Should().BeLessThan(names.IndexOf("Customers"));
        names.IndexOf("Customers").Should().BeLessThan(names.IndexOf("Orders"));
    }

    [Fact]
    public void Sort_WithMultipleDependenciesOnSameTable_ReturnsCorrectOrder()
    {
        // OrderItems -> Orders AND OrderItems -> Products
        var products = CreateTable("Products");
        var orders = CreateTable("Orders");
        var orderItems = CreateTable("OrderItems",
            ("OrderId", "Orders", "Id"),
            ("ProductId", "Products", "Id"));

        var result = _sorter.Sort([orderItems, products, orders]);

        result.HasCycles.Should().BeFalse();
        var names = result.SortedTables.Select(t => t.Name).ToList();
        names.IndexOf("Orders").Should().BeLessThan(names.IndexOf("OrderItems"));
        names.IndexOf("Products").Should().BeLessThan(names.IndexOf("OrderItems"));
    }

    [Fact]
    public void Sort_WithSelfReference_IgnoresIt()
    {
        var employees = CreateTable("Employees", ("ManagerId", "Employees", "Id"));

        var result = _sorter.Sort([employees]);

        result.HasCycles.Should().BeFalse();
        result.SortedTables.Should().HaveCount(1);
        result.SortedTables[0].Name.Should().Be("Employees");
    }

    [Fact]
    public void Sort_WithReferenceToTableNotInList_IgnoresIt()
    {
        var orders = CreateTable("Orders", ("CustomerId", "Customers", "Id"));

        var result = _sorter.Sort([orders]);

        result.HasCycles.Should().BeFalse();
        result.SortedTables.Should().HaveCount(1);
    }

    [Fact]
    public void Sort_WithCircularReference_DetectsCycle()
    {
        // A -> B -> A
        var tableA = CreateTable("TableA", ("BId", "TableB", "Id"));
        var tableB = CreateTable("TableB", ("AId", "TableA", "Id"));

        var result = _sorter.Sort([tableA, tableB]);

        result.HasCycles.Should().BeTrue();
        result.Cycles.Should().NotBeEmpty();
        result.SortedTables.Should().HaveCount(2);
    }

    [Fact]
    public void Sort_WithMixOfCyclicAndNonCyclic_SortsNonCyclicFirstThenAppendsCyclic()
    {
        // Independent: Countries
        // Cyclic: A -> B -> A
        // Depends on A: Orders -> A
        var countries = CreateTable("Countries");
        var tableA = CreateTable("TableA", ("BId", "TableB", "Id"));
        var tableB = CreateTable("TableB", ("AId", "TableA", "Id"));
        var orders = CreateTable("Orders", ("AId", "TableA", "Id"));

        var result = _sorter.Sort([orders, tableA, countries, tableB]);

        result.HasCycles.Should().BeTrue();
        var names = result.SortedTables.Select(t => t.Name).ToList();
        // Countries has no dependencies, should be first
        names.IndexOf("Countries").Should().Be(0);
    }

    [Fact]
    public void Sort_WithSchemaQualifiedTables_ReturnsCorrectOrder()
    {
        var customers = new TableSchema { Name = "Customers", Schema = "sales" };
        var orders = new TableSchema
        {
            Name = "Orders",
            Schema = "sales",
            Columns =
            [
                new ColumnSchema
                {
                    Name = "CustomerId",
                    DataType = "int",
                    ReferencedTableName = "Customers",
                    ReferencedTableSchema = "sales",
                    ReferencedColumnName = "Id"
                }
            ]
        };

        var result = _sorter.Sort([orders, customers]);

        result.HasCycles.Should().BeFalse();
        var names = result.SortedTables.Select(t => t.Name).ToList();
        names.IndexOf("Customers").Should().BeLessThan(names.IndexOf("Orders"));
    }

    [Fact]
    public void Sort_WithDiamondDependency_ReturnsCorrectOrder()
    {
        // D -> B, D -> C, B -> A, C -> A
        var tableA = CreateTable("A");
        var tableB = CreateTable("B", ("AId", "A", "Id"));
        var tableC = CreateTable("C", ("AId", "A", "Id"));
        var tableD = CreateTable("D", ("BId", "B", "Id"), ("CId", "C", "Id"));

        var result = _sorter.Sort([tableD, tableC, tableB, tableA]);

        result.HasCycles.Should().BeFalse();
        var names = result.SortedTables.Select(t => t.Name).ToList();
        names.IndexOf("A").Should().BeLessThan(names.IndexOf("B"));
        names.IndexOf("A").Should().BeLessThan(names.IndexOf("C"));
        names.IndexOf("B").Should().BeLessThan(names.IndexOf("D"));
        names.IndexOf("C").Should().BeLessThan(names.IndexOf("D"));
    }

    [Fact]
    public void Sort_EmptyList_ReturnsEmpty()
    {
        var result = _sorter.Sort([]);

        result.HasCycles.Should().BeFalse();
        result.SortedTables.Should().BeEmpty();
    }

    private static TableSchema CreateTable(string name, params (string columnName, string refTable, string refColumn)[] foreignKeys)
    {
        var columns = new List<ColumnSchema>
        {
            new() { Name = "Id", DataType = "int", IsPrimaryKey = true }
        };

        foreach (var (columnName, refTable, refColumn) in foreignKeys)
        {
            columns.Add(new ColumnSchema
            {
                Name = columnName,
                DataType = "int",
                ReferencedTableName = refTable,
                ReferencedColumnName = refColumn
            });
        }

        return new TableSchema { Name = name, Columns = columns };
    }
}
