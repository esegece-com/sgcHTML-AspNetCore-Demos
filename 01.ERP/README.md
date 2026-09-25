# 01.ERP (ASP.NET Core)

ERP web-app demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\01.ERP` (which hosts `TsgcWebSocketHTTPServer`): the reusable logic
is copied **verbatim** from that demo, and only the hosting layer changes.

A Bootstrap business app: bcrypt password login, WebAuthn passkeys, in-memory
cookie sessions, a SQLite database (schema + seed), 12-language i18n, a dashboard
with Chart.js, CRUD for customers / providers / products / invoices (multi-line),
reports, a settings + IP-firewall + audit-log admin section, and user management.

This demo reuses the **host pattern** established by `02.AdminCRUD` (the first heavy
demo of the `61.HTML.AspNetCore` set): the auth / session / cookie / passkey-origin
/ i18n / DB-seed wiring is identical. The only ERP-specific deviation is that
products use **page-based** add/edit forms (`/products/new`, `/products/edit`),
matching the 60.HTML ERP demo, rather than the AdminCRUD htmx table fragments.

## Copied verbatim (reused, not changed)

- `sgcERP_Bcrypt.cs` - bcrypt hash / verify (`Bcrypt.BcryptHash` / `BcryptVerify`).
- `sgcERP_Config.cs` - `System.Text.Json` config loader.
- `sgcERP_DB.cs` - `Microsoft.Data.Sqlite` schema, CRUD and demo seed (`TERPDBPool`).
- `sgcERP_I18n.cs` - the 12-language string table (`I18n.T`).
- `sgcERP_Pages.cs` - every page + fragment (node layer, `TERPPages`).
- `sgcERP_Passkeys.cs` - the WebAuthn passkey engine bridged to the DB (`TERPPasskeys`).
- `sgcERP_Sessions.cs` - the thread-safe in-memory cookie session store (`TERPSessionStore`).
- `sgcERP_Types.cs` - domain records + `TERPServerConfig`.

`sgcERP_Server.cs` (the `TsgcWebSocketHTTPServer` host) and the console
`Program.cs` are intentionally **not** copied. The rewritten host is `Program.cs`
(endpoint mapping) + `sgcERPWebHost.cs` (`ERPWebHost`, one IResult-returning method
per DispatchRequest branch).

## How it is hosted (the 02.AdminCRUD pattern)

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (the pages
  link `/bootstrap.min.css`, `/bootstrap.bundle.min.js`, `/chart.umd.min.js`,
  `/htmx.min.js`, `/htmx-ext-ws.min.js`, `/idiomorph-ext.min.js`,
  `/htmx-ext-head-support.min.js`, `/sgcHTMX.min.js`, all served by file name from
  the adapter asset registry), the PWA manifest / service worker and the `/ws`
  channel.
- `ERPWebHost` is a single DI singleton that owns the reused objects: the DB pool,
  the session store, the page builder and the per-origin passkey factory. Its
  constructor runs the DB init exactly like the console demo: `EnsureSchema()` ->
  `SeedAdmin(user, BcryptHash(password))` -> `SeedDemoDataIfEmpty()` -> seed
  default settings.
- **Auth + session + cookie**: `POST /login` validates username + password with
  the reused bcrypt against the DB, creates a session in `TERPSessionStore` and
  sets the **same** `erp_session` cookie (`Path=/; HttpOnly; SameSite=Lax`)
  through `ctx.Response.Cookies`. Protected endpoints run through
  `host.Guarded(...)` (redirect to `/login` when signed out); admin endpoints
  through `host.GuardedAdmin(...)` (403 forbidden page for non-admins). `Logout`
  destroys the session and deletes the cookie. All cookies (session / `erp_theme`
  / `erp_lang`) are read + written through the native ASP.NET Core cookie API.
- **Passkeys (WebAuthn), the Kestrel fix**: the 60.HTML host hard-coded RP id
  `localhost` and origin `http://localhost:<port>`. Here the RP id (the request
  host name) and origin (`ctx.Request.Scheme + "://" + ctx.Request.Host`) are
  **derived from the live request** and threaded into the reused `TERPPasskeys`.
  Because `TERPPasskeys` sets rpId/origin at construction (and holds the WebAuthn
  challenge state between the begin and finish calls of a ceremony), the host
  keeps a small **per-origin cache** (`ERPWebHost.GetPasskeys`, keyed by
  `rpId|origin`) so both requests of a ceremony hit the same instance. The
  challenge-start endpoints therefore return the request's real rpId/origin (e.g.
  `127.0.0.1`), never `localhost`.
- **i18n**: the `erp_lang` cookie is preserved (name + attributes). `POST /lang`
  sets it via `ctx.Response.Cookies`; every page is rendered in the selected
  language by the reused `I18n` / `TERPPages`.
- **Query + form merge**: `GetParam` / `GetParamArray` merge the query string and
  the urlencoded form body (query first), matching the Delphi `ARequestInfo.Params`.
- **Firewall gate**: a small middleware reproduces the DispatchRequest firewall
  check (a blocked IP gets a 403 forbidden page unless allow-listed; loopback is
  never blocked). The `/admin/firewall` CRUD writes the same SQLite tables.

### The ASP0016 trap (why every endpoint returns plain `Task`)

A Minimal API lambda whose ONLY parameter is `HttpContext` and that returns
`Task<IResult>` is bound as a raw `RequestDelegate`, so its `IResult` is silently
**discarded** (analyzer ASP0016): the cookie side effects run but the redirect /
status / body are lost (you see `200` where a `302` was intended). Every endpoint
lambda here therefore returns plain `Task`, and the `Run` / `RunForm` helpers
execute the handler's `IResult` explicitly (`await result.ExecuteAsync(ctx)`).

## SPA-feel navigation

`WrapTemplate` (in `sgcERP_Pages.cs`) turns on the navigation options of the page
template: `Boost`, `DefaultSwap` set to `morph:innerHTML`, `LoadingIndicator`,
`ViewTransitions` and `ErrorToast`. Links and forms load with htmx, a thin bar at
the top shows a running request, and every server answer stays a full page.

- The redirects after a sign in, a sign out or a save are followed by htmx, and
  the browser URL follows them.
- A 400 or 401 page (a validation error, a wrong password) is shown like a page
  load. A 500 keeps the current page and shows the error toast.
- The theme and language switchers apply without a reload: the `lang` and
  `data-bs-theme` of the answer are copied to the page, and the system theme
  detection script carries `hx-head="re-eval"`, so it runs again after every
  navigation.
- Pages with their own script (the invoice form, the sign in and security pages)
  are swapped instead of morphed, so the script runs on fresh elements.
- The delete forms ask for a confirmation in `onsubmit`, which htmx does not
  honour, so they carry `hx-boost="false"` and post as before.
- The scripts are served by `UseSgcHtml()`. No WebSocket is opened.

## Routes

Public (no session):

| Method | Path | Handler |
|---|---|---|
| GET  | `/login`                  | Login page |
| POST | `/login`                  | bcrypt login -> session cookie -> `302 /` |
| GET/POST | `/logout`             | Destroy session + clear cookie -> `302 /login` |
| POST | `/theme`                  | Set `erp_theme` cookie -> `302` back |
| POST | `/lang`                   | Set `erp_lang` cookie -> `302` back |
| POST | `/passkey/login/options`  | WebAuthn assertion options (JSON) |
| POST | `/passkey/login/verify`   | WebAuthn assertion verify -> session (JSON) |
| GET  | `/healthz`                | `ok` |

Logged-in:

| Method | Path | Handler |
|---|---|---|
| POST | `/passkey/register/options` | WebAuthn attestation options (JSON) |
| POST | `/passkey/register/verify`  | WebAuthn attestation verify (JSON) |
| GET  | `/security`                 | Passkeys management page |
| POST | `/security/passkey/delete`  | Delete a passkey |
| GET  | `/`                         | Dashboard (KPIs + Chart.js) |
| GET  | `/customers`                | Customers list (search via `q`) |
| GET  | `/customers/new`            | New customer form |
| GET  | `/customers/edit`           | Edit customer form (`id`) |
| POST | `/customers/save`           | Upsert customer -> `302 ?flash=saved` |
| POST | `/customers/delete`         | Delete customer -> `302 ?flash=deleted` |
| GET  | `/providers`                | Providers list |
| GET  | `/providers/new`            | New provider form |
| GET  | `/providers/edit`           | Edit provider form |
| POST | `/providers/save`           | Upsert provider |
| POST | `/providers/delete`         | Delete provider |
| GET  | `/products`                 | Products list (search via `q`) |
| GET  | `/products/new`             | New product form (page) |
| GET  | `/products/edit`            | Edit product form (page) |
| POST | `/products/save`            | Upsert product -> `302 ?flash=saved` |
| POST | `/products/delete`          | Delete product -> `302 ?flash=deleted` |
| GET  | `/invoices`                 | Invoices list (search + status) |
| GET  | `/invoices/new`             | New invoice form |
| GET  | `/invoices/edit`            | Edit invoice form |
| POST | `/invoices/save`            | Upsert invoice (multi-line) |
| POST | `/invoices/delete`          | Delete invoice |
| GET  | `/reports`                  | Statistics (granularity `g`) |

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

Any unmapped path returns `404`.

## Configuration

`sgcERPServer.conf.json` carries the admin credentials (`admin` / `admin`) and the
SQLite path (`data\erp.db`). The DB file is resolved next to the executable
(`AppContext.BaseDirectory`). The `listen` section is ignored here; Kestrel owns
the port (`appsettings.json`, default **8098**).

## Run

```
dotnet run --project sgcERPWeb.csproj
```

Then open `http://localhost:8098/` and sign in with `admin` / `admin`.
Register a passkey from **Security** (needs a browser + a real authenticator).
