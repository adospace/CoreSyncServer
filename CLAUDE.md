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

## C# Conventions

- Private fields use underscore prefix: `_myField`.
