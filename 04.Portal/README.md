# 04.Portal (ASP.NET Core)

Customer Portal web-app demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\04.Portal` (which hosts `TsgcWebSocketHTTPServer`): the reusable
logic is copied **verbatim** from that demo, and only the hosting layer changes.

A Bootstrap **role-gated, dual-area** app: bcrypt password login, WebAuthn
passkeys, in-memory cookie sessions, a SQLite database (schema + seed),
12-language i18n, and TWO areas selected by the signed-in user's role:

- **Customer area** (role `customer`): a self-service account dashboard, the
  customer's own orders (with per-order detail), an editable profile and a support
  page. Every customer route scopes its data to the session's `CustomerId`
  server-side, so a customer can only ever see / edit **their own** account.
- **Back-office area** (role `user` / `admin`): a dashboard with Chart.js, CRUD for
  customers / providers / products / invoices (multi-line) and reports. Role
  `admin` additionally owns user management plus a settings + IP-firewall +
  audit-log admin section.

This demo reuses the **host pattern** established by `01.ERP` / `02.AdminCRUD`: the
auth / session / cookie / passkey-origin / i18n / DB-seed wiring is identical. The
Portal-specific part is the **role model** described under *Role gating* below.

## Copied verbatim (reused, not changed)

- `sgcPortal_Bcrypt.cs` - bcrypt hash / verify (`Bcrypt.BcryptHash` / `BcryptVerify`).
- `sgcPortal_Config.cs` - `System.Text.Json` config loader.
- `sgcPortal_DB.cs` - `Microsoft.Data.Sqlite` schema, CRUD and demo seed (`TERPDBPool`),
  including the customer-scoped queries (`ListInvoicesByCustomer`,
  `SumInvoiceTotalByCustomer`) and `SeedCustomerUserIfMissing`.
- `sgcPortal_I18n.cs` - the 12-language string table (`I18n.T`).
- `sgcPortal_Pages.cs` - every page + fragment (node layer, `TERPPages`), including
  the customer pages (`BuildAccountDashboardPage`, `BuildMyOrdersPage`,
  `BuildOrderDetailPage`, `BuildMyProfilePage`, `BuildSupportPage`).
- `sgcPortal_Passkeys.cs` - the WebAuthn passkey engine bridged to the DB (`TERPPasskeys`).
- `sgcPortal_Sessions.cs` - the thread-safe in-memory cookie session store
  (`TERPSessionStore`); the session carries `CustomerId`.
- `sgcPortal_Types.cs` - domain records + `TERPServerConfig`.

`sgcPortal_Server.cs` (the `TsgcWebSocketHTTPServer` host) and the console
`Program.cs` are intentionally **not** copied. The rewritten host is `Program.cs`
(endpoint mapping) + `sgcPortalWebHost.cs` (`PortalWebHost`, one IResult-returning
method per DispatchRequest branch).

## How it is hosted (the 01.ERP / 02.AdminCRUD pattern)

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (Bootstrap /
  Chart.js / htmx, all served by file name from the adapter asset registry), the
  PWA manifest / service worker and the `/ws` channel.
- `PortalWebHost` is a single DI singleton that owns the reused objects: the DB
  pool, the session store, the page builder and the per-origin passkey factory. Its
  constructor runs the DB init exactly like the console demo: `EnsureSchema()` ->
  `SeedAdmin(user, BcryptHash(password))` -> `SeedDemoDataIfEmpty()` ->
  **`SeedCustomerUserIfMissing("customer", BcryptHash("customer"))`** -> seed
  default settings. That seed creates the demo `customer` / `customer` login (role
  `customer`) tied to the first seeded customer, so both areas can be exercised.
- **Auth + session + cookie**: `POST /login` validates username + password with the
  reused bcrypt against the DB, creates a session in `TERPSessionStore` and sets the
  **same** `merp_session` cookie (`Path=/; HttpOnly; SameSite=Lax`) through
  `ctx.Response.Cookies`. `Logout` destroys the session and deletes the cookie. All
  cookies (session / `merp_theme` / `merp_lang`) are read + written through the
  native ASP.NET Core cookie API.
- **Passkeys (WebAuthn), the Kestrel fix**: the 60.HTML host hard-coded RP id
  `localhost` and origin `http://localhost:<port>`. Here the RP id (the request host
  name) and origin (`ctx.Request.Scheme + "://" + ctx.Request.Host`) are **derived
  from the live request** and threaded into the reused `TERPPasskeys`. Because
  `TERPPasskeys` sets rpId/origin at construction (and holds the WebAuthn challenge
  state between the begin and finish calls of a ceremony), the host keeps a small
  **per-origin cache** (`PortalWebHost.GetPasskeys`, keyed by `rpId|origin`) so both
  requests of a ceremony hit the same instance. The challenge-start endpoints
  therefore return the request's real rpId/origin (e.g. `127.0.0.1`), never `localhost`.
- **i18n**: the `merp_lang` cookie is preserved (name + attributes). `POST /lang`
  sets it; every page is rendered in the selected language by `I18n` / `TERPPages`.
- **Query + form merge**: `GetParam` / `GetParamArray` merge the query string and the
  urlencoded form body (query first), matching the Delphi `ARequestInfo.Params`.
- **Firewall gate**: a small middleware reproduces the DispatchRequest firewall check
  (a blocked IP gets a 403 forbidden page unless allow-listed; loopback is never
  blocked). The `/admin/firewall` CRUD writes the same SQLite tables.

### Role gating (the Portal-specific part)

The 60.HTML `DispatchRequest` enforced the dual-area model with a role branch: a
`customer` session was confined to its self-service routes (any other in-app path
returned `403`), while the back-office routes further required role `admin` for
`/users` + `/admin`. This mirror reproduces the exact same model through four
per-endpoint guards in `PortalWebHost`:

| Guard | Rule | Area |
|---|---|---|
| `Guarded`         | any signed-in role (customers included) | `/security` + passkey management |
| `GuardedCustomer` | signed-in **and** role `customer`, else `302 /` | customer self-service |
| `GuardedStaff`    | signed-in **and** role **not** `customer`, else `403` | back-office CRUD |
| `GuardedAdmin`    | signed-in **and** role `admin`, else `403` | users + admin section |

The root `/` is **role-branched** (`RootGet`): a `customer` sees the account
dashboard, staff see the back-office dashboard. A `customer` reaching any staff /
admin path is refused with the `403` forbidden page; a staff user reaching a
customer-only path is redirected to `/` (its own dashboard), matching the 60.HTML
fallthrough.

### The ASP0016 trap (why every endpoint returns plain `Task`)

A Minimal API lambda whose ONLY parameter is `HttpContext` and that returns
`Task<IResult>` is bound as a raw `RequestDelegate`, so its `IResult` is silently
**discarded** (analyzer ASP0016): the cookie side effects run but the redirect /
status / body are lost (you see `200` where a `302` was intended). Every endpoint
lambda here therefore returns plain `Task`, and the `Run` / `RunForm` helpers
execute the handler's `IResult` explicitly (`await result.ExecuteAsync(ctx)`).

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
| GET  | `/favicon.svg`            | Inline emerald eSeGeCe favicon |
| GET  | `/healthz`                | `ok` |

Any signed-in role (customers included):

| Method | Path | Handler |
|---|---|---|
| POST | `/passkey/register/options` | WebAuthn attestation options (JSON) |
| POST | `/passkey/register/verify`  | WebAuthn attestation verify (JSON) |
| GET  | `/security`                 | Passkeys management page |
| POST | `/security/passkey/delete`  | Delete a passkey |
| GET  | `/`                         | **Role-branched**: customer account dashboard / back-office dashboard |

Customer area (role `customer` only):

| Method | Path | Handler |
|---|---|---|
| GET  | `/orders`                   | The customer's own orders |
| GET  | `/orders/view`              | One order detail (`id`, ownership-checked) |
| GET  | `/profile`                  | Editable profile form |
| POST | `/profile/save`             | Save profile -> `302 /profile?flash=saved` |
| GET  | `/support`                  | Support / contact page |

Back-office (role `user` / `admin`):

| Method | Path | Handler |
|---|---|---|
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

`sgcPortalServer.conf.json` carries the admin credentials (`admin` / `admin`) and
the SQLite path (`data\portal.db`). The DB file is resolved next to the executable
(`AppContext.BaseDirectory`). The `listen` section is ignored here; Kestrel owns
the port (`appsettings.json`, default **8099**).

## Run

```
dotnet run --project sgcPortalWeb.csproj
```

Then open `http://localhost:8099/` and sign in:

- `admin` / `admin` - the back-office + admin area.
- `customer` / `customer` - the customer self-service area.

Register a passkey from **Security** (needs a browser + a real authenticator).
