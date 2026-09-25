# 08.Components: sgcHTML components showcase (ASP.NET Core)

A showcase of 31 sgcHTML components across eight pages, rendered
server-side with sgcHTML .NET and running on Kestrel. The **Live** page is
pushed from the server over WebSocket: jobs tick, log lines append, the
activity feed grows and users flip online/away, all without a page reload.
The **Inputs** page adds a server-side AutoComplete that searches over the
same socket.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open the showcase (default port 8095):

```
http://localhost:8095/          Overview
http://localhost:8095/inputs    MultiSelect, Transfer, pickers, sliders, AutoComplete (live search)
http://localhost:8095/display   ProgressBar, Badge, Chip, Splitter, Sparkline, ContextMenu
http://localhost:8095/codes     QRCode, Barcode, SignaturePad
http://localhost:8095/data      PivotTable, TreeGrid, Grid (tree view, XLSX export, master/detail)
http://localhost:8095/charts    Heatmap, TreeMap, CandlestickChart
http://localhost:8095/live      Presence, ActivityFeed, JobProgress, LogViewer, AuditTrail (live push)
http://localhost:8095/admin     UserManagement, ImpersonateBanner, RolesPermissions (sample data, no real sign-in)
http://localhost:8095/docs      PDFViewer
```

## Features

- 31 components across 8 pages, no client-side framework.
- Live server push on the `/live` page: job progress, a log viewer, an
  activity feed and user presence, all updated over WebSocket.
- A server-side AutoComplete on `/inputs` that searches over the same
  WebSocket channel as you type.
- Real generated files: an XLSX workbook export on `/data` and a PDF on
  `/docs`.

## Configuration

No sign-in and no database; the data behind each component is sample data
generated in code. The listen port comes from `appsettings.json` (default
8095).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, the WebSocket push service |
| `sgcComponents_Pages.cs` | Every component page and the live-tick / search logic |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
