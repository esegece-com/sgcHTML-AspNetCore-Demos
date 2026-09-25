# 06.Grid (ASP.NET Core)

Grid features demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\06.Grid` (which hosts `TsgcWebSocketHTTPServer`): the page-building
logic in `sgcGrid_Pages.cs` is copied **verbatim** from that demo, and only the
hosting layer changes.

It renders `TsgcHTMLComponent_Grid` with its feature toggles (basic, sort/filter,
export, inline edit, group-by, column reorder, pagination, virtual scroll). The
grid shell loads each feature fragment over htmx from its own route.

## How it is hosted

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets
  (`/bootstrap.min.css`, `/bootstrap.bundle.min.js`, `/htmx.min.js`), the PWA
  manifest / service worker and the `/ws` push channel. The grid pages reference
  only those three assets, all served by the adapter registry (self-hosted, no
  CDN), matching the 60.HTML resource lookup.
- Each `DispatchRequest` branch is a Minimal API `MapGet` endpoint, preserving the
  exact route path + query parameters the reused pages emit.

## Routes

| Method | Path | Renders |
|--------|------|---------|
| GET | `/` | Grid shell (tab nav) |
| GET | `/grid/basic` | Basic grid |
| GET | `/grid/sort-filter` | Sortable / filterable grid |
| GET | `/grid/export` | Export grid |
| GET | `/grid/edit` | Inline-edit grid |
| GET | `/grid/group` | Group-by grid |
| GET | `/grid/reorder` | Column-reorder grid |
| GET | `/grid/page?page=N` | Paginated grid (page N, default 1) |
| GET | `/grid/scroll` | Virtual-scroll grid |
| GET | `/grid/scroll-rows?offset=N&limit=N&rootId=X` | Next virtual-scroll rows |

## Run

```
dotnet run --project sgcGridWeb.csproj
```

Then open the shell (default port `8093`, see `appsettings.json`):

```
http://localhost:8093/
```
