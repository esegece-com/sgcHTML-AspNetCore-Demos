# 14.POS (ASP.NET Core)

Retail point-of-sale web-app demo hosted on ASP.NET Core / Kestrel. It is a
mirror of `demos\60.HTML\14.POS` (which hosts `TsgcWebSocketHTTPServer`): the
reusable logic is copied **verbatim** from that demo, and only the hosting layer
changes.

A Bootstrap till: bcrypt password login, WebAuthn passkey sign-in, in-memory
cookie sessions, a SQLite database (schema + demo seed), the cashier till with
htmx product tiles and a live cart, barcode scanning, parked sales, split
payments with change, an 80mm till-roll receipt PDF, manager-PIN authorised
discounts and refunds, shift open / close with X and Z reports, and a
back-office of products, categories, promotions, customers, a Chart.js
dashboard, reports with XLSX and PDF export, a SQL page, user management and an
audit log.

This demo reuses the **host pattern** established by `01.ERP`: the auth /
session / cookie / passkey-origin / DB-seed wiring is identical. The
POS-specific deviations are the three-tier authorisation (cashier, back office,
admin) instead of the ERP's two, and the generated binary responses (receipt and
shift PDFs, catalogue and sales exports) that the ERP demo does not have.

## Copied verbatim (reused, not changed)

- `sgcPOS_Bcrypt.cs` - bcrypt hash / verify (`Bcrypt.BcryptHash` / `BcryptVerify`).
- `sgcPOS_Config.cs` - `System.Text.Json` config loader (`TPOSConfigLoader`).
- `sgcPOS_DB.cs` - `Microsoft.Data.Sqlite` schema, CRUD, analytics and demo seed
  (`TPOSDBPool`, `TPOSQuery`, `POSDB`).
- `sgcPOS_Pages.cs` - every page + htmx fragment (node layer, `TPOSPages`).
- `sgcPOS_Passkeys.cs` - the WebAuthn passkey engine bridged to the DB (`TPOSPasskeys`).
- `sgcPOS_Sessions.cs` - the thread-safe in-memory cookie session store (`TPOSSessionStore`).
- `sgcPOS_Types.cs` - domain records, `POSConst` and `TPOSServerConfig`.

`sgcPOS_Server.cs` (the `TsgcWebSocketHTTPServer` host) and the console
`Program.cs` are intentionally **not** copied. The rewritten host is `Program.cs`
(endpoint mapping) + `sgcPOSWebHost.cs` (`POSWebHost`, one IResult-returning
method per DispatchRequest branch).

## How it is hosted (the 01.ERP pattern)

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (the pages
  link `/bootstrap.min.css`, `/bootstrap.bundle.min.js`, `/chart.umd.min.js`,
  `/htmx.min.js`, all served by file name from the adapter asset registry), the
  PWA manifest / service worker and the `/ws` channel. The adapter claims `*.js`
  and `*.css` only, so the till's own generated SVG routes (`/favicon.svg` and
  `/promo/<id>.svg`) still reach the endpoints below.
- `POSWebHost` is a single DI singleton that owns the reused objects: the DB
  pool, the session store, the page builder and the per-origin passkey factory.
  Its constructor runs the DB init exactly like the console demo:
  `EnsureSchema()` -> `SeedAdmin(user, BcryptHash(password))` -> `SeedDemoData()`.
  It is resolved eagerly at startup, so the seed runs before the first request,
  not on it.
- **Auth + session + cookie**: `POST /login` validates the user name and
  password with the reused bcrypt against the DB, creates a session in
  `TPOSSessionStore` and sets the **same** `pos_session` cookie
  (`Path=/; HttpOnly; SameSite=Lax`) through `ctx.Response.Cookies`. The
  `pos_theme` cookie keeps its `Path=/; Max-Age=31536000; SameSite=Lax`
  attributes and, like the 60.HTML host, carries no `HttpOnly`: a theme is a
  display preference, not a credential.
- **Three authorisation tiers**, as host methods used by the endpoint map:
  `Guarded` (a valid session, otherwise `302 /login`), `GuardedBackOffice` (a
  session AND the manager or admin role, otherwise `302 /?err=denied`) and
  `GuardedAdmin` (a session AND the admin role, same denial). The Delphi takes
  the same decision with one prefix test per group before the route table.
- **Passkeys (WebAuthn), the Kestrel fix**: the 60.HTML host hard-coded RP id
  `localhost` and origin `http://localhost:<port>`. Here the RP id (the request
  host name) and origin (`ctx.Request.Scheme + "://" + ctx.Request.Host`) are
  **derived from the live request** and threaded into the reused `TPOSPasskeys`
  with RP name `sgcPOS Till`. Because `TPOSPasskeys` sets rpId/origin at
  construction (and holds the WebAuthn challenge state between the begin and
  finish calls of a ceremony), the host keeps a small **per-origin cache**
  (`POSWebHost.GetPasskeys`, keyed by `rpId|origin`) so both requests of a
  ceremony hit the same instance. The challenge-start endpoint therefore returns
  the request's real rpId/origin (e.g. `127.0.0.1`), never `localhost`.
- **Query + form merge**: `GetParam` merges the query string and the urlencoded
  form body (query first) and trims the value, matching the Delphi `Param()`
  helper over `ARequestInfo.Params`.
- **Client IP**: `X-Forwarded-For` (first hop) when present, else
  `ctx.Connection.RemoteIpAddress` with an IPv4-mapped IPv6 loopback normalised
  to `127.0.0.1`. Every audit row is written with it.

### Decisions taken on the server, not in the browser

The till is the demo where trusting the client is expensive, so the port keeps
every server-side check:

- The one-shot banner is decoded from a **whitelisted** `?flash=` or `?err=`
  code, never echoed from free text. A reflected message is a stored XSS waiting
  to happen, and a till has no reason to print the query string.
- Money posted by a form goes through `POSDB.POSParseMoney`, a hand-walked
  locale-independent parser, never `decimal.Parse`. `12.50` means twelve fifty
  on a Spanish machine too.
- A discount above `Config.DiscountPinThreshold` and **every** refund need a
  manager PIN that bcrypt-verifies through `FDB.VerifyManagerPin`. Hiding the
  modal or posting the form by hand changes nothing.
- A recalled sale must be parked AND owned by the caller, a cart line id is
  always scoped to the caller's own basket, and a cashier only sees their own
  receipts and shift reports.
- Closing a shift takes only the counted cash from the browser. The expected
  cash is recomputed from the recorded payments and the variance is the
  difference the server works out.

### The ASP0016 trap (why every endpoint returns plain `Task`)

A Minimal API lambda whose ONLY parameter is `HttpContext` and that returns
`Task<IResult>` is bound as a raw `RequestDelegate`, so its `IResult` is silently
**discarded** (analyzer ASP0016): the cookie side effects run but the redirect /
status / body are lost (you see `200` where a `302` was intended). Every endpoint
lambda here therefore returns plain `Task`, and the `Run` / `RunForm` helpers
execute the handler's `IResult` explicitly (`await result.ExecuteAsync(ctx)`).
The one asynchronous handler, `POST /passkey/login/verify`, uses a block-bodied
`async` lambda that awaits the `IResult` into a local and then executes it.

## Routes

Public (no session):

| Method | Path | Handler |
|---|---|---|
| GET  | `/healthz`                | `ok <version> uptime <n>s` |
| GET  | `/favicon.svg`            | The generated rose till mark |
| GET  | `/promo/{id}.svg`         | Generated promotion artwork (no CDN) |
| GET/POST | `/theme`              | Set `pos_theme` (`set` or `theme`) -> `302` to the Referer |
| GET  | `/login`                  | Login page |
| POST | `/login`                  | bcrypt login -> session cookie -> `302 /` |
| GET/POST | `/logout`             | Destroy session + clear cookie -> `302 /login` |
| POST | `/passkey/login/options`  | WebAuthn assertion options (JSON) |
| POST | `/passkey/login/verify`   | WebAuthn assertion verify -> session (JSON) |

Session required (`Guarded`):

| Method | Path | Handler |
|---|---|---|
| GET  | `/`                       | The till (categories, tiles, live cart) |
| GET  | `/till/category/{id}`     | htmx tiles fragment for a category |
| GET  | `/till/search`            | htmx tiles fragment for a search (`q`) |
| POST | `/till/add`               | Add a line (`product_id`) -> cart fragment |
| POST | `/till/qty`               | Set a line quantity (`line_id`, `qty`) |
| POST | `/till/remove`            | Remove a line (`line_id`) |
| POST | `/till/discount`          | Sale discount (`amount`, `pin`), manager PIN over the threshold |
| POST | `/till/customer`          | Attach a customer (`customer_id`) -> `302 /?flash=customer` |
| GET/POST | `/till/scan`          | Barcode scan (`scan`, else `q`) -> cart fragment |
| POST | `/till/park`              | Park the basket -> `302 /till/parked?flash=parked` |
| GET  | `/till/parked`            | Parked sales of this cashier |
| POST | `/till/recall`            | Recall a parked sale (`sale_id`), owner checked |
| GET  | `/till/pay`               | Payment page (paid / due) |
| POST | `/till/pay`               | Take one tender (`method`, `amount`), completes when covered |
| GET  | `/till/receipt/{id}`      | Receipt page |
| GET  | `/till/receipt/{id}.pdf`  | 80mm till-roll receipt PDF |
| POST | `/till/refund/{id}`       | Refund (`pin`), always manager authorised |
| GET  | `/shift`                  | Shift page (totals, expected cash, history) |
| POST | `/shift/open`             | Open a shift (`opening_float`) |
| POST | `/shift/close`            | Close a shift (`counted_cash`), variance computed here |
| GET  | `/shift/{id}/xreport.pdf` | X report PDF |
| GET  | `/shift/{id}/zreport.pdf` | Z report PDF (adds counted cash + variance) |

Back office, manager or admin role (`GuardedBackOffice`):

| Method | Path | Handler |
|---|---|---|
| GET  | `/products`               | Catalogue (`q`, `cat`, `sort`, `dir`, `page`) |
| GET  | `/products/form`          | htmx product form fragment (`id`, `cancel`) |
| POST | `/products/save`          | Upsert product -> refreshed grid fragment |
| POST | `/products/delete`        | Deactivate product -> refreshed grid fragment |
| GET  | `/products/export.xlsx`   | Catalogue XLSX (server-side, from the Grid) |
| GET  | `/products/export.pdf`    | Catalogue PDF (server-side, from the Grid) |
| GET  | `/categories`             | Categories |
| POST | `/categories/save`        | Upsert category -> `302 ?flash=saved` |
| GET  | `/promotions`             | Promotions |
| POST | `/promotions/save`        | Upsert promotion -> `302 ?flash=saved` |
| GET  | `/customers`              | Customers (`q`) |
| POST | `/customers/save`         | Upsert customer -> `302 /customers/<id>?flash=saved` |
| GET  | `/customers/{id}`         | One customer + their last sales |
| GET  | `/dashboard`              | Chart.js analytics (`range` = today/week/month/year) |
| GET  | `/reports`                | Pivot + monthly series (`from`, `to`) |
| GET  | `/reports/sales.pdf`      | Sales report PDF (landscape A4) |
| GET  | `/reports/sales.xlsx`     | Sales report XLSX |
| GET  | `/sql`                    | The SQL next to the component |

Admin role only (`GuardedAdmin`):

| Method | Path | Handler |
|---|---|---|
| GET  | `/users`                  | Users |
| POST | `/users/save`             | Upsert user (bcrypt password + PIN) |
| GET  | `/audit`                  | Audit log (last 300 rows) |

Any unmapped path renders the till's own 404 page for a signed-in caller and
redirects a signed-out one to `/login`, exactly like the 60.HTML dispatch.

## What differs from the 60.HTML host

- **Hosting only.** Kestrel replaces `TsgcWebSocketHTTPServer`, Minimal API
  endpoints replace the `DispatchRequest` if-chain, and `IResult` replaces the
  Indy `TIdHTTPResponseInfo`. Every page, fragment, query and rule comes from the
  same reused files.
- **The port** comes from `appsettings.json` (`http://localhost:8103`), not from
  the `listen` section of `sgcPOSServer.conf.json`, which is ignored here. The
  rest of that file (admin credentials, DB path, store details, discount PIN
  threshold, shift target) is read exactly as in the console demo.
- **Passkey rpId and origin** are per-request instead of the hard-coded
  `localhost`, as described above.
- **The shared-host machinery is gone.** The Delphi host can be mounted under a
  URL prefix on a server it does not own, and rewrites root-relative links
  through `PrefixAppURLs`. This app always owns the root of its Kestrel endpoint,
  which is exactly the case where that rewrite is a no-op, so `FBasePath`,
  `CookiePath` and `PrefixAppURLs` have no counterpart here.
- **Generated files are returned with `Results.File`**, which sets
  `Content-Disposition: attachment`. The Delphi wrote `inline`, so a receipt PDF
  opens in a new tab there and downloads here.
- **`405` comes from routing.** The Delphi answered a wrong verb with an explicit
  `405`; ASP.NET Core produces the same status from the mapped verb set.
- **`sgcHTMLFloatToStr` is carried locally.** The managed sgcHTML library has no
  public counterpart yet (the only copy is private inside
  `TsgcHTMLComponent_NumPad`), so `POSWebHost` holds a private one with the
  identical contract for the receipt quantity column and the exported tax rate.

## Configuration

`sgcPOSServer.conf.json` carries the admin credentials (`admin` / `admin`), the
SQLite path (`data\pos.db`) and the store block (name, address, tax id, currency
symbol, discount PIN threshold, shift target). The DB file is resolved next to
the executable (`AppContext.BaseDirectory`). The `listen` section is ignored
here; Kestrel owns the port (`appsettings.json`, default **8103**).

## Run

```
dotnet run --project sgcPOSWeb.csproj
```

Then open `http://localhost:8103/` and sign in. The seed creates
`admin` / `admin`, `manager` / `manager` and `cashier` / `cashier`, with manager
PIN `1379` and admin PIN `4242`. Open a shift before ringing anything up.

The login page also offers **Sign in with a passkey**. Like the 60.HTML demo,
the till only *consumes* passkeys: the sign-in ceremony is served, enrolment is
not, so a credential has to already exist for the account (the passkey tables are
part of the reused `sgcPOS_DB.cs` schema). That is why the route table above has
`/passkey/login/options` and `/passkey/login/verify` and no register pair.
