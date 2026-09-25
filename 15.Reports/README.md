# sgcReports on ASP.NET Core

Reporting and BI portal, mirror of `demos/60.HTML/15.Reports` (which hosts a
`TsgcWebSocketHTTPServer`) on Kestrel.

Both apps render **byte-identical HTML** and expose the **same routes**: the whole
view layer, data layer, auth and i18n are the same files. Only the hosting layer
differs.

## What is reused verbatim

These files are copied from `demos/60.HTML/15.Reports/Server` and are **not**
changed here:

| File | What it is |
|---|---|
| `sgcReports_Types.cs` | domain records + `TReportsServerConfig` |
| `sgcReports_Config.cs` | `sgcReportsServer.conf.json` loader |
| `sgcReports_Bcrypt.cs` | bcrypt hash / verify (pure managed) |
| `sgcReports_Sessions.cs` | in-memory cookie session store |
| `sgcReports_Passkeys.cs` | WebAuthn passkey engine bridged to the DB |
| `sgcReports_I18n.cs` | en / es / de UI string table |
| `sgcReports_DB.cs` | SQLite schema, seed, report engine, dashboards |
| `sgcReports_Pages.cs` | every page and htmx fragment, built from sgcHTML components |

The two files that were **not** copied are `sgcReports_Server.cs` (the
`TsgcWebSocketHTTPServer` host) and the console `Program.cs`.

## What is new here

| File | What it does |
|---|---|
| `Program.cs` | `AddSgcHtml(o => o.ServeRootPage = false)`, `UseWebSockets()`, `UseSgcHtml()`, one Minimal API endpoint per 60.HTML `DispatchRequest` branch, the `OnHTMXMessage` wiring and the background push service |
| `sgcReportsWebHost.cs` | one `IResult`-returning method per route, cookies through the native ASP.NET Core cookie API, per-origin passkey factory |

## Hosting notes

- **ASP0016**: a Minimal API lambda whose only parameter is `HttpContext` and
  which returns `Task<IResult>` is treated as a raw `RequestDelegate`, so its
  `IResult` is discarded: the cookie side effects run but the redirect, status
  and body are lost. Every endpoint here returns plain `Task` and the local
  `Run` / `RunForm` helpers execute the handler's `IResult` explicitly.
- **Realtime**: the 60.HTML demo runs a push thread that broadcasts through
  `TsgcHTMX_Engine_Server`. Here a `BackgroundService` advances
  `TReportsPages.LiveTick()` every 900 ms and broadcasts through
  `ISgcHtmlHub`, and the `job:cancel` message that arrives on the adapter's
  `/ws` channel is broadcast the same way.
- **Passkeys**: the 60.HTML host hard-codes RP id `localhost` and origin
  `http://localhost:5708`. Here both are derived from the live request
  (`Request.Host` / `Request.Scheme`) and a per-origin `TReportsPasskeys` is
  cached so the begin/finish challenge state survives the two requests of one
  ceremony.
- **Assets**: the adapter serves Bootstrap, Chart.js, htmx, htmx-ext-ws and the
  sgcHTMX bridge by file name. `/sgcWebSockets.js`, `/favicon.svg`,
  `/pdf.min.js` and `/pdf.worker.min.js` are served by this app (the last two
  from `assets/` when present, else redirected to the public pdf.js build).

## Run it

```
dotnet run --project sgcReportsWeb.csproj
```

Then open <http://localhost:8104>. Sign in as `admin`/`admin`,
`analyst`/`demo1234` or `viewer`/`demo1234`.

The first start seeds ~27,000 order lines across ~6,000 orders into
`data/reports.db` in a single transaction, so the first launch takes a moment.

## Routes

| Route | Method | Notes |
|---|---|---|
| `/healthz` | GET | public |
| `/login`, `/logout` | GET / POST | public |
| `/theme`, `/lang` | POST | cookie switchers |
| `/webauthn/...` | any | passkey ceremonies |
| `/` | GET | dashboard home |
| `/dashboards/{slug}` | GET | `sales`, `margin`, `pipeline`, `ops` |
| `/reports` | GET | catalogue |
| `/reports/new`, `/reports/save`, `/reports/delete` | GET / POST | admin only |
| `/reports/{id}` | GET | parameter form |
| `/reports/{id}/run` | POST | htmx results fragment |
| `/reports/{id}/preview` | GET | inline PDF viewer |
| `/reports/{id}/run.pdf` / `.xlsx` / `.csv` | GET | raw-row exports are analyst / admin only |
| `/pivot`, `/pivot/build` | GET / POST | ad-hoc cross-tab |
| `/pivot/export.pdf`, `/pivot/export.xlsx` | GET | analyst / admin only |
| `/explore`, `/explore/rows` | GET | paged + virtual-scroll order lines |
| `/explore/export.pdf`, `/explore/export.xlsx` | GET | analyst / admin only |
| `/drilldown/{dim}/{id}` | GET | `region`, `category`, `salesperson`, `customer` |
| `/views`, `/views/save`, `/views/delete` | GET / POST | scoped to the session user |
| `/schedules`, `/schedules/save`, `/schedules/run-now` | GET / POST | not the viewer role |
| `/jobs` | GET | JobProgress + LogViewer, pushed over the WebSocket |
| `/sql` | GET | the flagship "no REST layer" page |
| `/users`, `/users/save` | GET / POST | admin only |
