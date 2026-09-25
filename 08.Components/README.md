# 08.Components (ASP.NET Core)

sgcHTML components showcase hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\08.Components` (which hosts `TsgcWebSocketHTTPServer`): the
page-building logic in `sgcComponents_Pages.cs` is copied **verbatim** from that
demo, and only the hosting layer changes.

This is the first `61.HTML.AspNetCore` demo that uses the WebSocket push channel.
It exercises 31 newer sgcHTML components across eight pages, and the `/live` page
is pushed from the server over the WebSocket: jobs tick, log lines append, the
activity feed grows and users flip online/away with no page reload. The
`/inputs` page adds a server-side AutoComplete that searches over the same
socket.

## How it is hosted

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets, the PWA
  manifest / service worker, and OWN the `/ws` channel. For every inbound text
  frame the adapter calls `engine.DispatchMessage(null, text)` and pushes the
  returned reply back to that sender.
- A `BackgroundService` (`ComponentsPushService`) replaces the 60.HTML push
  thread: every 1200 ms it calls `TsgcComponentsDemoPages.LiveTick()` and
  broadcasts the resulting out-of-band fragments through
  `ISgcHtmlHub.BroadcastAsync`.
- `engine.OnHTMXMessage` reproduces the inbound behavior:
  - `autoCompleteSearch` -> a per-sender reply through `ref aResponse` (the
    adapter pushes the `DispatchMessage` return value to the calling socket).
  - `job:cancel` -> a broadcast-to-all through `hub.BroadcastAsync`.
- Binary routes `/data/export.xlsx` (a real workbook via `SaveToXLSXStream`) and
  `/docs/sample.pdf`, plus the component page routes, are mapped with `MapGet`
  reusing the verbatim page builder.
- `/sgcWebSockets.js`: the reused page links it, but the adapter asset registry
  does not serve it (it serves htmx, htmx-ext-ws, sgcHTMX, bootstrap and chart).
  It is served here straight from the `esegece.sgcHTML` assembly manifest.

## Run

```
dotnet run --project sgcComponentsWeb.csproj
```

Then open the showcase (default port `8095`, see `appsettings.json`):

```
http://localhost:8095/          Overview
http://localhost:8095/inputs    MultiSelect, Transfer, pickers, sliders, AutoComplete (WS)
http://localhost:8095/display   ProgressBar, Badge, Chip, Splitter, Sparkline, ContextMenu
http://localhost:8095/codes     QRCode, Barcode, SignaturePad
http://localhost:8095/data      PivotTable, TreeGrid, Grid (tree + XLSX export + master/detail)
http://localhost:8095/charts    Heatmap, TreeMap, CandlestickChart
http://localhost:8095/live      Presence, ActivityFeed, JobProgress, LogViewer, AuditTrail (WS push)
http://localhost:8095/admin     UserManagement, ImpersonateBanner, RolesPermissions
http://localhost:8095/docs      PDFViewer
```
