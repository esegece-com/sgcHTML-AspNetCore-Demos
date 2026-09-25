# 13.Warehouse (ASP.NET Core)

Warehouse-management (WMS) web-app demo hosted on ASP.NET Core / Kestrel. It is a
mirror of `demos\60.HTML\13.Warehouse` (which hosts `TsgcWebSocketHTTPServer`):
the reusable logic is copied **verbatim** from that demo, and only the hosting
layer changes.

A Bootstrap warehouse app built entirely from sgcHTML components: bcrypt password
login, WebAuthn passkeys, in-memory cookie sessions, a SQLite database (schema +
a deterministic seed of ~180 products, 92 locations, 130 orders and 1200
movements), a dashboard with Chart.js and a bin-utilisation heatmap, a product
catalogue with server-side search / sort / paging, a location tree, inbound
(purchase orders, receiving, put-away, damage photos, labels), outbound (sales
orders, picking, packing, proof-of-delivery signature, packing slips), the stock
grid and the movement ledger, cycle counts, XLSX / PDF reports, a mobile handheld
scanner UI under `/hh`, user management and an audit log.

There is **no REST tier** on purpose: every page binds a live query straight into
a `TsgcHTMLComponent_*` through `LoadFromDataSet`, and `/sql` prints the exact
statement next to the rendered component.

## Roles

`admin` and `supervisor` reach the back office; `operator` is handheld-only (the
dispatcher answers 403 for every back-office route it reaches for, while `/hh/*`
stays open to all three roles).

Sign in with `admin` / `admin`, `supervisor` / `demo1234` or
`operator` / `demo1234`.

## Copied verbatim (reused, not changed)

- `sgcWMS_Bcrypt.cs` - bcrypt hash / verify (`Bcrypt.BcryptHash` / `BcryptVerify`).
- `sgcWMS_Config.cs` - `System.Text.Json` config loader (`TWMSConfigLoader`).
- `sgcWMS_DB.cs` - `Microsoft.Data.Sqlite` schema, queries and demo seed
  (`TWMSDBPool` / `TWMSDataSet`).
- `sgcWMS_Handheld.cs` - the handheld scanner screens (node layer, `TWMSHandheld`).
- `sgcWMS_Multipart.cs` - upload validation + disk storage (`TWMSAttachStore`).
- `sgcWMS_Pages.cs` - every back-office page + htmx fragment (node layer,
  `TWMSPages`).
- `sgcWMS_Passkeys.cs` - the WebAuthn passkey engine bridged to the DB
  (`TWMSPasskeys`).
- `sgcWMS_Reports.cs` - the XLSX / PDF exports, packing slip and labels
  (`TWMSReports`).
- `sgcWMS_Sessions.cs` - the thread-safe in-memory cookie session store
  (`TWMSSessionStore`).
- `sgcWMS_Types.cs` - domain records + `TWMSServerConfig` + `WMSConst`.

`sgcWMS_Server.cs` (the `TsgcWebSocketHTTPServer` host) and the console
`Program.cs` are intentionally **not** copied. The rewritten host is `Program.cs`
(endpoint mapping) + `sgcWMSWebHost.cs` (`WMSWebHost`, one IResult-returning
method per DispatchRequest branch).

## How it is hosted (the 01.ERP pattern)

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (the pages
  link `/bootstrap.min.css`, `/bootstrap.bundle.min.js`, `/chart.umd.min.js` and
  `/htmx.min.js`, all served by file name from the adapter asset registry), the
  PWA manifest / service worker and the `/ws` channel. `/favicon.svg` is the one
  static asset this app serves itself, from the same inline SVG constant the
  60.HTML host used.
- `WMSWebHost` is a single DI singleton that owns the reused objects: the DB
  pool, the session store, the page / handheld / report builders, the photo
  store and the per-origin passkey factory. Its constructor runs the DB init
  exactly like the console demo: `EnsureSchema()` ->
  `SeedAdmin(user, BcryptHash(password))` -> `SeedDemoData()` -> the two extra
  demo accounts (`supervisor`, `operator`).
- **Auth + session + cookie**: `POST /login` validates username + password with
  the reused bcrypt against the DB, creates a session in `TWMSSessionStore` and
  sets the **same** `wms_session` cookie (`Path=/; HttpOnly; SameSite=Lax`)
  through `ctx.Response.Cookies`. Endpoints run through one of three gates,
  matching the 60.HTML dispatcher: `host.Signed(...)` (any signed-in role - the
  handheld and `/security`), `host.Guarded(...)` (admin or supervisor - the back
  office; an operator gets the 403 forbidden page) and `host.GuardedAdmin(...)`
  (`/users*`, `/audit`). A signed-out request is redirected to `/login`, or - for
  an htmx request - answered with `401` + `HX-Redirect`, exactly as the 60.HTML
  host did. The `wms_theme` cookie is read + written the same way
  (`Max-Age=31536000; SameSite=Lax`, default `system`).
- **Passkeys (WebAuthn), the Kestrel fix**: the 60.HTML host hard-coded RP id
  `localhost` and origin `http://localhost:5706`. Here the RP id (the request
  host name) and origin (`ctx.Request.Scheme + "://" + ctx.Request.Host`) are
  **derived from the live request** and threaded into the reused `TWMSPasskeys`.
  Because `TWMSPasskeys` sets rpId/origin at construction (and holds the WebAuthn
  challenge state between the begin and finish calls of a ceremony), the host
  caches one instance per `"rpId|origin"` key so both requests of a ceremony hit
  the same object.
- **File uploads**: the 60.HTML demo parsed `multipart/form-data` by hand in
  `sgcWMS_Multipart.cs`. Here the framework's `IFormFile` replaces the parser,
  but the uploaded files are converted into the SAME `TWMSMultipartField` values
  and handed to the SAME reused `TWMSAttachStore`, so the storage semantics
  (`data\photos\<po_id>\`, the random 16-hex stored-name suffix, the
  dangerous-extension block-list and the size / count caps) are unchanged.
- **404 / 403**: `MapFallback` reproduces the dispatcher's final steps - signed
  out redirects to `/login`, an unknown `/hh/*` path renders the styled 404 page,
  an operator anywhere else gets the 403 page, and everything else gets the 404
  page.

## The ASP0016 trap (why every lambda returns plain `Task`)

A Minimal API lambda whose ONLY parameter is `HttpContext` and which returns
`Task<IResult>` is treated as a raw `RequestDelegate`, so its `IResult` is
silently **discarded**: the handler's cookie side effects run but the redirect /
status / body are lost. Every endpoint below therefore returns plain `Task`, and
the `Run` / `RunForm` helpers execute the handler's `IResult` onto the response
explicitly.

## Routes

Identical to the 60.HTML demo - both trees expose the same paths, methods, query
parameters and cookie names; only the hosting layer differs.

| Method | Path | Gate |
|---|---|---|
| GET | `/healthz`, `/favicon.svg` | public |
| GET POST | `/login`, `/register` | public |
| GET POST | `/logout` | public |
| POST | `/theme` | public |
| GET POST | `/passkey/login/options`, `/passkey/login/verify` | public |
| GET POST | `/passkey/register/options`, `/passkey/register/verify` | signed in |
| GET | `/security` | signed in |
| GET | `/hh`, `/hh/pick`, `/hh/pick/{id}` | signed in |
| GET POST | `/hh/receive`, `/hh/count`, `/hh/lookup` | signed in |
| POST | `/hh/pick/confirm` | signed in |
| GET | `/` (dashboard), `/dashboard/heatmap` | back office |
| GET | `/products`, `/products/form`, `/products/{id}` | back office |
| GET | `/products/export.xlsx`, `/products/export.pdf` | back office |
| POST | `/products/save`, `/products/delete` | back office |
| GET | `/locations`, `/locations/{id}` | back office |
| GET | `/inbound`, `/inbound/{id}`, `/inbound/{id}/labels.pdf` | back office |
| POST | `/inbound/{id}/receive`, `/inbound/{id}/photo` | back office |
| GET | `/outbound`, `/outbound/{id}`, `/outbound/{id}/packingslip.pdf` | back office |
| POST | `/outbound/{id}/pick`, `/outbound/{id}/pack`, `/outbound/{id}/ship` | back office |
| GET | `/stock`, `/stock/movements` | back office |
| POST | `/stock/adjust` | back office |
| GET | `/counts`, `/counts/{id}` | back office |
| POST | `/counts/new`, `/counts/{id}/line`, `/counts/{id}/close` | back office |
| GET | `/reports`, `/reports/valuation.pdf`, `/reports/valuation.xlsx` | back office |
| GET | `/sql` | back office |
| GET | `/users`, `/audit` | admin |
| POST | `/users/save`, `/users/delete` | admin |

## Run

```
dotnet run --project demos\61.HTML.AspNetCore\13.Warehouse\sgcWMSWeb.csproj
```

Then open <http://localhost:8102/> for the back office and
<http://localhost:8102/hh> for the handheld. The listen URL comes from
`appsettings.json` (`Kestrel:Endpoints:Http:Url`); the admin credentials,
database path and company name come from `sgcWMSServer.conf.json` (its `listen`
section is ignored here - Kestrel owns the port). The SQLite file is created next
to the executable on first run.
