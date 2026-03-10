# Project Rules

## Blazor Razor Pages

- **Directive ordering** in `.razor` files (top to bottom):
  1. `@page`
  2. `@attribute`
  3. `@inherits`
  4. `@inject`
  5. `@layout`
  6. `@rendermode`
  7. `@using` (sorted alphabetically)

- **Interactive pages** that need `HttpClient` must inherit from `InteractivePage` (in `CoreSyncServer.Client.Components`) and override `OnInitializedInteractiveAsync()` instead of using `OnAfterRenderAsync` directly. Do not add `@inject HttpClient Http` — it is provided by the base class.

## DataGrid Component

- Use `DataGrid` (in `CoreSyncServer.Client.Components`) to display tabular data. It wraps QuickGrid with filter bar, pagination, and loading/empty states styled to the app theme.
- Always set `Theme="custom"` on the inner `QuickGrid` to disable its default styles.
- **In-memory mode** (small datasets like Users, Projects): pass `Items` to QuickGrid and `ItemCount` to DataGrid. See `Pages/Users.razor` for a complete example.
- **Server-side mode** (large datasets like logs): pass `ItemsProvider` to QuickGrid, omit `ItemCount` on DataGrid (it reads `PaginationState.TotalItemCount` automatically). Hold a `@ref` on QuickGrid and call `RefreshDataAsync()` when the filter changes.

## C# Conventions

- Private fields use underscore prefix: `_myField`.
