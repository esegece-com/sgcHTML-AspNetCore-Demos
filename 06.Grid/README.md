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
- Master / detail rows.
- Column reorder (drag columns).
- Pagination.
- Virtual scroll for large row sets, loading more rows on demand.
- Paging & Frozen: server paging with multi-column sort, filter and column
  resize, client paging, frozen columns.
- Selection: multiple selection that survives paging, with a bulk bar (mark
  shipped, mark delivered, delete with confirmation) applied on the server.
- Typed & Edit: typed columns with per-type editors, validation and a
  supplier lookup; valid edits travel over the sgcHTMX WebSocket and are kept
  in memory.
- Summaries: footer aggregates and conditional format rules.
- Keyboard & Live: keyboard navigation and live rows pushed by the server
  every 2 seconds (price updates, a new listing inserted on its own, a
  delisting deleted on its own).

The live channel is the sgcHTMX bridge WebSocket at the site root (`/`). A
hosted background service broadcasts the live row fragments through
`ISgcHtmlHub`, and inbound `gridEdit` messages are applied by the engine's
`OnHTMXMessage` handler.

## Configuration

No sign-in and no database: the orders, products and quotes live in memory
and reset on restart. The listen port comes from `appsettings.json`
(default 8093).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcGrid_Pages.cs` | The grid shell and the classic feature tabs |
| `sgcGrid_Features.cs` | In-memory data and the Paging, Selection, Typed, Summaries and Live tabs |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
