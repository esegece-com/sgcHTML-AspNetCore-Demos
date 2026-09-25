# 03.LiveMonitor (ASP.NET Core)

Live Monitor demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\03.LiveMonitor` (which hosts `TsgcWebSocketHTTPServer`): the reusable
logic is copied **verbatim** from that demo, and only the hosting layer changes.

It combines the **heavy admin stack** (bcrypt password login, WebAuthn passkeys,
in-memory cookie sessions, a SQLite database with schema + seed, 12-language i18n,
a settings + IP-firewall + audit-log admin section, and user management) **with
live WebSocket push**: an authenticated real-time dashboard (KPI cards, a live
Chart.js line chart, a streaming events table) whose values are pushed from the
server every ~1.5 s over the htmx WebSocket channel.

This is the last of the ten `61.HTML.AspNetCore` demos. It reuses the admin host
pattern from `02.AdminCRUD` / `01.ERP` **and** the WS-push pattern from
`08.Components` (a `BackgroundService` that broadcasts through `ISgcHtmlHub`).

## Copied verbatim (reused, not changed)

- `sgcLiveMonitor_Bcrypt.cs` - bcrypt hash / verify (`Bcrypt.BcryptHash` / `BcryptVerify`).
- `sgcLiveMonitor_Config.cs` - `System.Text.Json` config loader.
- `sgcLiveMonitor_DB.cs` - `Microsoft.Data.Sqlite` schema, queries and demo seed (`TERPDBPool`).
- `sgcLiveMonitor_I18n.cs` - the 12-language string table (`I18n.T`).
- `sgcLiveMonitor_Pages.cs` - every page (node layer, `TERPPages`), including the live dashboard.
- `sgcLiveMonitor_Passkeys.cs` - the WebAuthn passkey engine bridged to the DB (`TERPPasskeys`).
- `sgcLiveMonitor_Sessions.cs` - the thread-safe in-memory cookie session store (`TERPSessionStore`).
- `sgcLiveMonitor_Types.cs` - domain records + `TERPServerConfig`.

`sgcLiveMonitor_Server.cs` (the `TsgcWebSocketHTTPServer` host, including its
`System.Threading.Timer` push loop) and the console `Program.cs` are intentionally
**not** copied. The rewritten host is `Program.cs` (endpoint mapping + the push
`BackgroundService`) + `sgcLiveMonitorWebHost.cs` (`LiveMonitorWebHost`, one
IResult-returning method per DispatchRequest branch, plus the ported
`BuildMetricsFragment`).

## How it is hosted (the reusable admin pattern)

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (the pages
  link `/bootstrap.min.css`, `/bootstrap.bundle.min.js`, `/chart.umd.min.js`,
  `/htmx.min.js`, `/htmx-ext-ws.min.js`, all served by file name from the adapter
  asset registry), the PWA manifest / service worker and the `/ws` channel.
- `LiveMonitorWebHost` is a single DI singleton that owns the reused objects (DB
  pool, session store, page builder, per-origin passkey factory) plus the
  live-metric state. Its constructor runs the DB init exactly like the console
  demo: `EnsureSchema()` -> `SeedAdmin(user, BcryptHash(password))` ->
  `SeedDemoDataIfEmpty()` -> seed default settings.
- **Auth + session + cookie**: `POST /login` validates username + password with
  the reused bcrypt against the DB, creates a session in `TERPSessionStore` and
  sets the **same** `merp_session` cookie (`Path=/; HttpOnly; SameSite=Lax`)
  through `ctx.Response.Cookies`. Protected endpoints run through
  `host.Guarded(...)` (redirect to `/login` when signed out); admin endpoints
  through `host.GuardedAdmin(...)` (403 forbidden page for non-admins). `Logout`
  destroys the session and deletes the cookie.
- **Passkeys (WebAuthn), the Kestrel fix**: the 60.HTML host hard-coded RP id
  `localhost` and origin `http://localhost:<port>`. Here the RP id (the request
  host name) and origin (`ctx.Request.Scheme + "://" + ctx.Request.Host`) are
  **derived from the live request** and threaded into the reused `TERPPasskeys`,
  cached per origin (`LiveMonitorWebHost.GetPasskeys`, keyed by `rpId|origin`) so
  both requests of a ceremony hit the same instance. The challenge-start endpoints
  therefore return the request's real rpId/origin (e.g. `127.0.0.1`), never `localhost`.
- **i18n / theme**: the `merp_lang` and `merp_theme` cookies are preserved (names
  + attributes); `POST /lang` and `POST /theme` set them via `ctx.Response.Cookies`
  and every page is rendered in the selected language + theme by `I18n` / `TERPPages`.
- **Firewall gate**: a small middleware reproduces the DispatchRequest firewall
  check (a blocked IP gets a 403 forbidden page unless allow-listed; loopback is
  never blocked). The `/admin/firewall` CRUD writes the same SQLite tables.

## Live push (the point of this demo)

- The 60.HTML host bound the HTMX engine to its `TsgcWebSocketHTTPServer` and a
  `System.Threading.Timer` (dueTime 1500, period 1500) called
  `BroadcastFragment(BuildMetricsFragment())`.
- Under Kestrel the engine has **no** `TsgcWSHTTPServer` bound, so its own
  `BroadcastFragment` is a **no-op**. Server push therefore goes through
  `ISgcHtmlHub`, the adapter's connection hub. `LiveMonitorPushService`
  (a `BackgroundService`, registered with `AddHostedService`) replicates the
  timer: every **1500 ms** it calls `host.BuildMetricsFragment()` (the SAME
  fragment builder, ported verbatim from the 60.HTML host) and
  `hub.BroadcastAsync(fragment)`.
- The fragment is one bundle of htmx `hx-swap-oob` elements: the four KPI values
  (`#kpi-cpu`, `#kpi-mem`, `#kpi-conn`, `#kpi-rps`), the chart value carrier
  (`#metric-cpu-val`), and an `afterbegin` swap into `#events-body` (a new log row).
  `#kpi-conn` is the live WS connection count, read from `ISgcHtmlHub.Count`
  (the Kestrel counterpart of the 60.HTML `FHTTP.Count`).
- The authenticated dashboard page at `/` renders with
  `hx-ext="ws" ws-connect="/ws"`: the reused page builder emits
  `ws-connect="/"` (the self-hosted server accepted the upgrade at the root path),
  and the host retargets it to `/ws` (the adapter's `WebSocketPath`) so the page
  connects to the path the middleware actually accepts. The reused page already
  carries the matching OOB target ids, so each pushed fragment swaps into place.

## The ASP0016 trap (the reusable hosting note)

A Minimal API lambda whose only parameter is `HttpContext` and which returns
`Task<IResult>` is treated as a raw `RequestDelegate`, so its `IResult` is
**discarded** (analyzer ASP0016) - the handler's cookie side effects run but the
`302` / status / body are lost (you would see `200` instead of `302`). To avoid
that uniformly, every endpoint lambda returns plain `Task` and the `Run` / `RunForm`
helpers execute the handler's `IResult` onto the response explicitly; the two async
passkey-verify endpoints use a block-bodied async lambda that awaits the `IResult`
into a local and then `ExecuteAsync`es it.

## Routes

Public (no session):

| Method | Path | Handler |
|---|---|---|
| GET  | `/login`                  | Login page |
| POST | `/login`                  | bcrypt login -> session cookie -> `302 /` |
| GET/POST | `/logout`             | Destroy session + clear cookie -> `302 /login` |
| POST | `/theme`                  | Set `merp_theme` cookie -> `302` back |
| POST | `/lang`                   | Set `merp_lang` cookie -> `302` back |
| POST | `/passkey/login/options`  | WebAuthn assertion options (JSON, request-derived rpId) |
| POST | `/passkey/login/verify`   | WebAuthn assertion verify -> session (JSON) |
| GET  | `/favicon.svg`            | Inline brand favicon |
| GET  | `/healthz`                | `ok` |

Logged-in:

| Method | Path | Handler |
|---|---|---|
| POST | `/passkey/register/options` | WebAuthn attestation options (JSON) |
| POST | `/passkey/register/verify`  | WebAuthn attestation verify (JSON) |
| GET  | `/security`                 | Passkeys management page |
| POST | `/security/passkey/delete`  | Delete a passkey |
| GET  | `/`                         | **Live monitoring dashboard** (KPIs + Chart.js + events, `ws-connect="/ws"`) |

Admin role only:

| Method | Path | Handler |
|---|---|---|
| GET  | `/users`                    | Users list |
| GET  | `/users/new`                | New user form |
| GET  | `/users/edit`               | Edit user form |
| POST | `/users/save`               | Upsert user (bcrypt hash) |
| POST | `/users/delete`             | Delete user (self / last-admin guards) |
| GET  | `/admin`                    | Settings page + server info |
| POST | `/admin/settings`           | Save settings |
| GET  | `/admin/firewall`           | IP firewall page |
| POST | `/admin/firewall/block`     | Block an IP |
| POST | `/admin/firewall/unblock`   | Unblock an IP |
| POST | `/admin/firewall/ignore`    | Allow-list an IP |
| POST | `/admin/firewall/unignore`  | Remove from the allow-list |
| GET  | `/admin/audit`              | Audit log (paged, filtered) |

WebSocket: `GET /ws` (htmx `hx-ext="ws"` upgrade) - the live dashboard push channel.

Any unmapped path returns `404`.

## Configuration

`sgcLiveMonitorServer.conf.json` carries the admin credentials (`admin` / `admin`)
and the SQLite path (`data\monitor.db`). The DB file is resolved next to the
executable (`AppContext.BaseDirectory`). The `listen` section is ignored here;
Kestrel owns the port (`appsettings.json`, default **8101**).

## Run

```
dotnet run --project sgcLiveMonitorWeb.csproj
```

Then open `http://localhost:8101/` and sign in with `admin` / `admin`. The
dashboard starts streaming live metrics within ~1.5 s. Register a passkey from
**Security** (needs a browser + a real authenticator).
