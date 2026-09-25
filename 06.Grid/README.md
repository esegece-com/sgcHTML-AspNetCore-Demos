# 06.Grid: Grid features demo (ASP.NET Core)

A feature tour of the sgcHTML `Grid` component, rendered server-side with
sgcHTML .NET and running on Kestrel. Each tab loads a variant of the grid
over htmx from its own route, with no client-side framework.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8093/.

## Features

- Basic grid.
- Sortable and filterable columns.
- Export.
- Inline cell editing.
- Group-by.
- Column reorder (drag columns).
- Pagination.
- Virtual scroll for large row sets, loading more rows on demand.

## Configuration

No sign-in and no database. The listen port comes from `appsettings.json`
(default 8093).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcGrid_Pages.cs` | The grid shell and every feature variant |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
