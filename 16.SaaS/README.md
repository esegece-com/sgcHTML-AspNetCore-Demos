# sgcSaaS on ASP.NET Core

Multi-tenant SaaS control plane, mirror of `demos/60.HTML/16.SaaS` (which hosts a
`TsgcWebSocketHTTPServer`) on Kestrel.

Both apps render **byte-identical HTML** and expose the **same routes**: the whole
view layer, data layer and auth are the same files. Only the hosting layer differs.

Two axes over one database: the **tenant workspace** under `/app` (dashboard,
onboarding, projects, tasks, team, permission matrix, billing with simulated
invoices and a PDF export, settings, notifications, audit trail, and an
isolation page that tries to read another tenant's row and shows the refusal)
and the **vendor console** under `/admin` (platform KPIs, tenant list and
detail, plans, feature flags, cross-tenant audit and superadmin impersonation).

There is **no REST tier** on purpose: every page binds a live query straight into
a `TsgcHTMLComponent_*`, and `/sql` prints the exact statement next to the
rendered component. Every workspace statement goes through
`TSaaSQuery.CreateScoped`, which refuses to run without a `:tenant_id` bind, and
the tenant id comes from the session and from nowhere else.

## What is reused verbatim

These files are copied from `demos/60.HTML/16.SaaS` and are **not** changed here:

| File | What it is |
|---|---|
| `sgcSaaS_Types.cs` | domain records + `TSaaSServerConfig` + the role / status constants |
| `sgcSaaS_Config.cs` | `sgcSaaSServer.conf.json` loader |
| `sgcSaaS_Bcrypt.cs` | bcrypt hash / verify (pure managed) |
| `sgcSaaS_Sessions.cs` | in-memory cookie session store + impersonation bookkeeping |
| `sgcSaaS_Passkeys.cs` | WebAuthn passkey engine bridged to the DB |
| `sgcSaaS_DB.cs` | SQLite schema, seed, tenant-scoped query helper, all CRUD |
| `sgcSaaS_Pages.cs` | every page of both axes, built from sgcHTML components |

The two files that were **not** copied are `sgcSaaS_Server.cs` (the
`TsgcWebSocketHTTPServer` host) and the console `Program.cs`.

## What is new here

| File | What it does |
|---|---|
| `Program.cs` | `AddSgcHtml(o => o.ServeRootPage = false)`, `UseWebSockets()`, `UseSgcHtml()`, one Minimal API endpoint per 60.HTML `DispatchRequest` branch |
| `sgcSaaSWebHost.cs` | `SaaSWebHost`: one `IResult`-returning method per route, the four gates, cookies through the native ASP.NET Core cookie API, per-origin passkey factory |

## Hosting notes

- **The gates**: the 60.HTML dispatcher runs the auth check once (step 5) and
  then splits on the axis (step 6 `/app`, step 7 `/admin`). Here that is four
  per-endpoint helpers: `Guarded` (any signed-in session, `/sql` and
  `/admin/impersonate/stop`), `GuardedTenant` (`/app*`, a vendor account gets
  the 403 reason page), `GuardedVendor` (`/admin*`, a tenant account gets the
  403 reason page) and `GuardedJson` (the passkey registration pair, which
  answers a JSON 401 rather than an HTML redirect). A signed-out **POST** is
  answered with the 403 page that names the reason, not a redirect that would
  silently drop the body, exactly as the dispatcher does.
- **404 / 403 tail**: `MapFallback` reproduces the dispatcher's last three
  steps: signed out redirects to `/login`, an unknown `/app` path still meets
  the tenant gate, an unknown `/admin` path still meets the vendor gate, and
  everything else is the styled 404 page.
- **ASP0016**: a Minimal API lambda whose only parameter is `HttpContext` and
  which returns `Task<IResult>` is treated as a raw `RequestDelegate`, so its
  `IResult` is discarded: the cookie side effects run but the redirect, status
  and body are lost. Every endpoint here returns plain `Task` and the local
  `Run` / `RunForm` helpers execute the handler's `IResult` explicitly.
- **Passkeys**: the 60.HTML host hard-codes RP id `localhost` and origin
  `http://localhost:5709`. Here both are derived from the live request
  (`Request.Host` / `Request.Scheme`) and a per-origin `TSaaSPasskeys` is cached
  so the begin/finish challenge state survives the two requests of one ceremony.
  The same live request supplies `BaseURL`, so the absolute verification, reset
  and invitation links the demo prints point at whatever address the browser
  actually used.
- **Params**: `GetParam` is the exact counterpart of the Delphi `ParamOf` and is
  deliberately **not** trimmed, because `sgcSaaS_Server.pas` trims at each call
  site; the same `.Trim()` calls appear here in the same places. `GetParamArray`
  reads the repeated `sgc_grants` values the permission matrix posts, which are
  only ever used as a membership test against the fixed role / permission pairs.
- **Shared-host mounting**: the Delphi unit can be mounted under a URL prefix on
  a shared server (`AttachAndStart` / `FBasePath` / `PrefixAppURLs`). This app
  owns the whole Kestrel pipeline, so the prefix is always empty and that URL
  rewriting is not ported; the cookie path is `/`.
- **Assets**: the adapter serves Bootstrap, Chart.js, htmx and the sgcHTMX
  bridge by file name (it claims `*.js` and `*.css` only). `/favicon.svg` and
  `/favicon.ico` are served by this app from the same inline SVG constant the
  60.HTML host used.
- **405**: the Delphi dispatcher answers 405 when a known path is reached with
  the wrong verb. Here the verb is part of the mapping, so a wrong-verb request
  falls through to `MapFallback` and is answered by the 404 tail instead. No
  page or component in the demo produces such a request.

## Run it

```
dotnet run --project sgcSaaSWeb.csproj
```

Then open <http://localhost:8105>. Sign in as `root`/`root` (superadmin) or
`support`/`root` (read only). The seeded tenant accounts carry an unusable
password hash on purpose: reach a workspace from the vendor console by
impersonating a tenant owner, or create your own through `/signup`.

The listen URL comes from `appsettings.json` (`Kestrel:Endpoints:Http:Url`); the
vendor credentials and database path come from `sgcSaaSServer.conf.json` (its
`listen` section is ignored here, Kestrel owns the port). The SQLite file is
created next to the executable on first run and the demo data is seeded then, so
the first launch takes a moment.

## Routes

| Route | Method | Gate |
|---|---|---|
| `/healthz`, `/favicon.svg`, `/favicon.ico` | GET | public |
| `/` | GET | public marketing, or the axis home when signed in |
| `/pricing` | GET | public |
| `/signup` | GET / POST | public |
| `/verify` | GET | public, single-use token |
| `/login`, `/logout` | GET / POST | public |
| `/forgot`, `/reset` | GET / POST | public, single-use token |
| `/theme` | POST | cookie switcher |
| `/auth/social/{provider}`, `/auth/callback` | GET / POST | public, nothing is configured |
| `/invite/{token}`, `/invite/{token}/accept` | GET / POST | public, the token IS the credential |
| `/passkey/login/options`, `/passkey/login/verify` | POST | public |
| `/passkey/register/options`, `/passkey/register/verify` | POST | signed in (JSON 401) |
| `/sql` | GET | signed in, both axes |
| `/admin/impersonate/stop` | GET / POST | signed in, before the axis gate |
| `/app` | GET | tenant |
| `/app/onboarding`, `/app/onboarding/step` | GET / POST | tenant |
| `/app/projects`, `/app/projects/{id}` | GET | tenant |
| `/app/projects/save`, `/app/projects/delete` | POST | tenant, not the readonly role |
| `/app/tasks` | GET | tenant |
| `/app/tasks/save`, `/app/tasks/move` | POST | tenant, not the readonly role |
| `/app/team` | GET | tenant |
| `/app/team/invite`, `/app/team/role`, `/app/team/remove` | POST | tenant owner / admin |
| `/app/roles`, `/app/roles/grant` | GET / POST | tenant, grants are owner / admin |
| `/app/billing`, `/app/billing/change-plan` | GET / POST | tenant, plan change is owner / admin |
| `/app/billing/invoice/{id}.pdf` | GET | tenant, scoped by tenant id |
| `/app/settings`, `/app/settings/save` | GET / POST | tenant |
| `/app/notifications`, `/app/notifications/read` | GET / POST | tenant |
| `/app/audit`, `/app/isolation` | GET | tenant |
| `/admin` | GET | vendor |
| `/admin/plans`, `/admin/plans/save` | GET / POST | vendor, save is superadmin |
| `/admin/flags`, `/admin/flags/save` | GET / POST | vendor, save is superadmin |
| `/admin/audit` | GET | vendor |
| `/admin/tenants/{id}` | GET | vendor |
| `/admin/tenants/{id}/suspend`, `/activate` | POST | superadmin |
| `/admin/impersonate/{id}` | POST | superadmin |
