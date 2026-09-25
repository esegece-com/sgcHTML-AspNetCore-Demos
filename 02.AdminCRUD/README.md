# 02.AdminCRUD (ASP.NET Core)

Admin Console mini-ERP demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\02.AdminCRUD` (which hosts `TsgcWebSocketHTTPServer`): the reusable
logic is copied **verbatim** from that demo, and only the hosting layer changes.

A dark analytics Bootstrap app: bcrypt password login, WebAuthn passkeys, in-memory
cookie sessions, a SQLite database (schema + seed), 12-language i18n, a dashboard
with Chart.js, CRUD for customers / providers / products (htmx live search) /
invoices (multi-line), reports, a settings + IP-firewall + audit-log admin
section, and user management.

This is the FIRST heavy demo of the `61.HTML.AspNetCore` set. The auth / session /
cookie / passkey-origin / i18n / DB-seed wiring here is the **reusable host
pattern** that the later heavy demos (01.ERP, 04.Portal, 03.LiveMonitor) replicate.

## Copied verbatim (reused, not changed)

- `sgcAdmin_Bcrypt.cs` - bcrypt hash / verify (`Bcrypt.BcryptHash` / `BcryptVerify`).
- `sgcAdmin_Config.cs` - `System.Text.Json` config loader.
- `sgcAdmin_DB.cs` - `Microsoft.Data.Sqlite` schema, CRUD and demo seed (`TERPDBPool`).
- `sgcAdmin_I18n.cs` - the 12-language string table (`I18n.T`).
- `sgcAdmin_Pages.cs` - every page + htmx fragment (node layer, `TERPPages`).
- `sgcAdmin_Passkeys.cs` - the WebAuthn passkey engine bridged to the DB (`TERPPasskeys`).
- `sgcAdmin_Sessions.cs` - the thread-safe in-memory cookie session store (`TERPSessionStore`).
- `sgcAdmin_Types.cs` - domain records + `TERPServerConfig`.

`sgcAdmin_Server.cs` (the `TsgcWebSocketHTTPServer` host) and the console
`Program.cs` are intentionally **not** copied. The rewritten host is `Program.cs`
(endpoint mapping) + `sgcAdminWebHost.cs` (`AdminWebHost`, one IResult-returning
method per DispatchRequest branch).

## How it is hosted (the reusable pattern)

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (the pages
  link `/bootstrap.min.css`, `/bootstrap.bundle.min.js`, `/chart.umd.min.js`,
  `/htmx.min.js`, `/htmx-ext-ws.min.js`, all served by file name from the adapter
  asset registry), the PWA manifest / service worker and the `/ws` channel.
- `AdminWebHost` is a single DI singleton that owns the reused objects: the DB
  pool, the session store, the page builder and the per-origin passkey factory.
  Its constructor runs the DB init exactly like the console demo:
  `EnsureSchema()` -> `SeedAdmin(user, BcryptHash(password))` ->
  `SeedDemoDataIfEmpty()` -> seed default settings.
- **Auth + session + cookie**: `POST /login` validates username + password with
  the reused bcrypt against the DB, creates a session in `TERPSessionStore` and
  sets the **same** `merp_session` cookie (`Path=/; HttpOnly; SameSite=Lax`)
  through `ctx.Response.Cookies`. Protected endpoints run through
  `host.Guarded(...)` (redirect to `/login` when signed out); admin endpoints
  through `host.GuardedAdmin(...)` (403 forbidden page for non-admins). `Logout`
  destroys the session and deletes the cookie. All cookies (session / `merp_theme`
  / `merp_lang`) are read + written through the native ASP.NET Core cookie API.
- **Passkeys (WebAuthn), the Kestrel fix**: the 60.HTML host hard-coded RP id
  `localhost` and origin `http://localhost:<port>`. Here the RP id (the request
  host name) and origin (`ctx.Request.Scheme + "://" + ctx.Request.Host`) are
  **derived from the live request** and threaded into the reused `TERPPasskeys`.
  Because `TERPPasskeys` sets rpId/origin at construction (and holds the WebAuthn
  challenge state between the begin and finish calls of a ceremony), the host
  keeps a small **per-origin cache** (`AdminWebHost.GetPasskeys`, keyed by
  `rpId|origin`) so both requests of a ceremony hit the same instance. The
  challenge-start endpoints therefore return the request's real rpId/origin (e.g.
  `127.0.0.1`), never `localhost`.
- **i18n**: the `merp_lang` cookie is preserved (name + attributes). `POST /lang`
  sets it via `ctx.Response.Cookies`; every page is rendered in the selected
  language by the reused `I18n` / `TERPPages`.
- **Query + form merge**: `GetParam` / `GetParamArray` merge the query string and
  the urlencoded form body (query first), matching the Delphi `ARequestInfo.Params`.
- **Firewall gate**: a small middleware reproduces the DispatchRequest firewall
  check (a blocked IP gets a 403 forbidden page unless allow-listed; loopback is
  never blocked). The `/admin/firewall` CRUD writes the same SQLite tables.

## Routes

Public (no session):

| Method | Path | Handler |
|---|---|---|
| GET  | `/login`                  | Login page |
| POST | `/login`                  | bcrypt login -> session cookie -> `302 /` |
| GET/POST | `/logout`             | Destroy session + clear cookie -> `302 /login` |
| POST | `/theme`                  | Set `merp_theme` cookie -> `302` back |
| POST | `/lang`                   | Set `merp_lang` cookie -> `302` back |
| POST | `/passkey/login/options`  | WebAuthn assertion options (JSON) |
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
| GET  | `/products`                 | Products list page (htmx) |
| GET  | `/products/search`          | htmx table fragment (live search) |
| GET  | `/products/form`            | htmx add/edit form fragment |
| POST | `/products/save`            | Upsert product -> refreshed fragment |
| POST | `/products/delete`          | Delete product -> refreshed fragment |
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

Any unmapped path returns `404`; a wrong method on a mapped path returns `405`.

## Configuration

`sgcAdminServer.conf.json` carries the admin credentials (`admin` / `admin`) and
the SQLite path (`data\admin.db`). The DB file is resolved next to the executable
(`AppContext.BaseDirectory`). The `listen` section is ignored here; Kestrel owns
the port (`appsettings.json`, default **8097**).

## Run

```
dotnet run --project sgcAdminWeb.csproj
```

Then open `http://localhost:8097/` and sign in with `admin` / `admin`.
Register a passkey from **Security** (needs a browser + a real authenticator).
