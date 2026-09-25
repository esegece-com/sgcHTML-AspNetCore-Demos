# 07.Site (ASP.NET Core)

Site Layouts demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\07.Site` (which hosts `TsgcWebSocketHTTPServer`): the page-building
logic in `sgcSite_Pages.cs` is copied **verbatim** from that demo, and only the
hosting layer changes.

It renders `TsgcHTMLComponent_Site` with 6 layouts, 5 color presets and 3 theme
modes, driven entirely by the query string. Bootstrap is linked from the CDN.

## SPA-feel navigation

The menu, the switcher buttons and the settings form load with htmx instead of a
full page load. `BuildDemo` subscribes `OnPrepareTemplate` on the Site, and the
handler turns on the navigation options of the page template: `Boost`,
`DefaultSwap` set to `morph:innerHTML`, `LoadingIndicator`, `ViewTransitions` and
`ErrorToast`.

- The server still answers every request with a full page. htmx morphs the body,
  the head support extension merges the CSS of the new theme preset, and the
  `data-bs-theme` of the answer is copied to the page, so the Mode buttons apply
  without a reload. Back and forward restore the pages from the htmx history.
- A thin bar at the top shows a running request, and a toast appears when a
  request fails.
- The page loads `/htmx.min.js`, `/idiomorph-ext.min.js`,
  `/htmx-ext-head-support.min.js` and `/sgcHTMX.min.js`, served by `UseSgcHtml()`.
  No WebSocket is opened.

## How it is hosted

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` still serve the built-in client assets (the
  htmx scripts the navigation options load), the PWA manifest / service worker
  and the `/ws` push channel.
- `MapGet("/")` reads the `page` / `layout` / `theme` / `mode` query parameters
  and returns the rendered site shell as `text/html`.

## Run

```
dotnet run --project sgcSiteWeb.csproj
```

Then open one of the six layouts (default port `8092`, see `appsettings.json`):

```
http://localhost:8092/?layout=sidebar-left
http://localhost:8092/?layout=sidebar-right
http://localhost:8092/?layout=topnav
http://localhost:8092/?layout=topnav-sidebar
http://localhost:8092/?layout=iconrail
http://localhost:8092/?layout=offcanvas
```

The `page` (dashboard / customers / orders / reports / settings), `theme`
(blue / violet / emerald / slate / dark) and `mode` (light / dark / system) query
parameters combine with `layout` to select the rendered output.
