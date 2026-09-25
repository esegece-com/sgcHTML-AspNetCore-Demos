# sgcField on ASP.NET Core

Field service management, mirror of `demos/60.HTML/17.FieldService` (which hosts a
`TsgcWebSocketHTTPServer`) on Kestrel.

Both apps render **byte-identical HTML** and expose the **same routes**: the whole
view layer, data layer and auth are the same files. Only the hosting layer differs.

Three audiences over one database: the **dispatch office** (a drag-and-drop
scheduling board with per-technician lanes, a multi-day Gantt, a live map, a month
calendar, the job list and detail with the state machine, customers, sites, an
asset hierarchy, a parts catalogue, SLA and utilisation reporting with PDF / XLSX
exports, users and the audit trail), the **technician on a phone** under `/my`
(today's visits, the three big status buttons, the checklist, part scanning, photo
upload, a signature pad and a chat back to dispatch) and the **customer portal**
under `/track`, which needs no account at all: the unguessable job reference is the
credential and it opens exactly one job.

Every status write goes through the same state machine (`FieldCanTransition`), so
an illegal move is refused with a 409 and the row is never touched, wherever the
request came from.

There is **no REST tier** on purpose: every page binds a live query straight into a
`TsgcHTMLComponent_*`, and `/sql` prints the exact statement next to the rendered
component.

## What is reused verbatim

These files are copied from `demos/60.HTML/17.FieldService/Server` and are **not**
changed here:

| File | What it is |
|---|---|
| `sgcField_Types.cs` | domain records, the job state machine, role / status / priority constants, `TFieldServerConfig` |
| `sgcField_Config.cs` | `sgcFieldServer.conf.json` loader |
| `sgcField_Bcrypt.cs` | bcrypt hash / verify (pure managed) |
| `sgcField_Sessions.cs` | in-memory cookie session store |
| `sgcField_Passkeys.cs` | WebAuthn passkey engine bridged to the DB |
| `sgcField_Multipart.cs` | upload validation + the `data\photos\<job id>\` disk store |
| `sgcField_DB.cs` | SQLite schema, seed (~900 jobs), all queries and CRUD |
| `sgcField_Pages.cs` | every page and OOB fragment, built from sgcHTML components |

The two files that were **not** copied are `sgcField_Server.cs` (the
`TsgcWebSocketHTTPServer` host) and the console `Program.cs`.

## What is new here

| File | What it does |
|---|---|
| `Program.cs` | `AddSgcHtml(o => o.ServeRootPage = false)`, `UseWebSockets()`, `UseSgcHtml()`, one Minimal API endpoint per 60.HTML `DispatchRequest` branch, and `FieldPushService` (the world simulator) |
| `sgcFieldWebHost.cs` | `FieldWebHost`: one `IResult`-returning method per route, the three gates, cookies through the native ASP.NET Core cookie API, per-origin passkey factory, the simulator itself |

## Hosting notes

- **The gates**: the 60.HTML dispatcher answers the public routes first (assets,
  `/healthz`, `/theme`, `/login`, `/logout`, the passkey login pair and the whole
  customer portal), then runs the auth check once (step 3) and applies its
  `RequireDispatch` local function to the office routes. Here that is three
  per-endpoint helpers: `Guarded` (any signed-in session, `/my*` and the two
  job-scoped downloads), `GuardedDispatch` (dispatcher or manager, everything
  else, and a technician gets the same "your work is on the My jobs screen"
  denied page the Delphi builds) and `GuardedJson` (the passkey registration
  pair, which answers a JSON 401 rather than a redirect).
- **Job-scoped, not office-scoped**: `/jobs/{id}/report.pdf` and
  `/jobs/{id}/photos/{photo}` deliberately skip the office gate, exactly as the
  dispatcher does for `report.pdf` and the `photos/` prefix. `CanActOnJob` inside
  the handler is the real check, so the technician who holds the job reaches
  them and nobody else does.
- **404 tail**: `MapFallback` reproduces the dispatcher's last steps: signed out
  redirects to `/login`, signed in gets the styled 404 page.
- **405**: the Delphi dispatcher answers 405 when a known path is reached with
  the wrong verb. Here the verb is part of the mapping, so a wrong-verb request
  falls through to `MapFallback` and is answered by the 404 tail instead. No page
  or component in the demo produces such a request.
- **ASP0016**: a Minimal API lambda whose only parameter is `HttpContext` and
  which returns `Task<IResult>` is treated as a raw `RequestDelegate`, so its
  `IResult` is discarded: the cookie side effects run but the redirect, status
  and body are lost. Every endpoint here returns plain `Task` and the local
  `Run` / `RunForm` helpers execute the handler's `IResult` explicitly.
- **Realtime**: the 60.HTML host attached a `TsgcHTMX_Engine_Server` to its own
  HTTP server and broadcast through it. Under Kestrel the engine has no `Server`
  bound, so its `BroadcastFragment` is a no-op and the adapter's `ISgcHtmlHub` is
  the push channel. `FieldPushService` is a `BackgroundService` that calls the
  same `SimulateTick` on the same 3000 ms beat: it really walks the en-route vans
  18% closer to their site (a `job_events` row with coordinates) and really
  advances one job along the state machine, then broadcasts the resulting
  `hx-swap-oob` fragments. The board itself is never broadcast, because every
  dispatcher watches their own day or week: the push only signals "it moved" and
  each browser re-fetches `/board/fragment` for the view it is showing.
- **Uploads**: the 60.HTML host parsed `multipart/form-data` by hand
  (`TFieldMultipart`). Here the framework hands the files over as `IFormFile` and
  `CollectUploads` converts them into the same `TFieldMultipartField` values, so
  the reused `TFieldPhotoStore` does the validating and the storing and the
  semantics (`data\photos\<job id>\`, the extension block-list, the size and
  count caps) are unchanged.
- **Passkeys**: the 60.HTML host hard-codes RP id `localhost` and origin
  `http://localhost:5710`. Here both are derived from the live request
  (`Request.Host` / `Request.Scheme`) and a per-origin `TFieldPasskeys` is cached
  so the begin/finish challenge state survives the two requests of one ceremony.
- **Params**: `GetParam` is the exact counterpart of the Delphi `Param` and
  **trims**, because the Delphi one does; `GetRawParam` is the untrimmed read the
  password and signature fields use. `GetParamIntList` reads the repeated `tech`
  values the lane MultiSelect posts, which a `Values` lookup would collapse to
  the first one.
- **Shared-host mounting**: the Delphi unit can be mounted under a URL prefix on
  a shared server (`AttachAndStart` / `FBasePath` / `PrefixAppURLs`). This app
  owns the whole Kestrel pipeline, so the prefix is always empty and that URL
  rewriting is not ported; the cookie path is `/`.
- **Assets**: the adapter serves htmx, sgcHTMX, Bootstrap and Chart.js by file
  name from its resource registry (it claims `*.js` and `*.css` only).
  `/favicon.svg` is served by this app from the same inline SVG constant the
  60.HTML host used, and `/sgcWebSockets.js` from the `esegece.sgcHTML` assembly
  manifest, because the registry does not carry that one.
- **Working directory**: the reused `TFieldPhotoStore` resolves `data\photos\`
  against the current directory, so `Program.cs` anchors that to the executable
  directory exactly as the 60.HTML console host does. The uploaded job photos
  then land next to the database whatever directory the app was launched from.

## Run it

```
dotnet run --project sgcFieldWeb.csproj
```

Then open <http://localhost:8106>. Sign in as `dispatch`/`dispatch` (the
dispatcher, pre-filled on the login page), `manager`/`demo1234` (adds the
reporting pages) or `amolina`/`demo1234` (a technician: you land on `/my`). The
customer portal at `/track` needs no account, paste any job reference from the
job list.

The listen URL comes from `appsettings.json` (`Kestrel:Endpoints:Http:Url`); the
dispatcher credentials and database path come from `sgcFieldServer.conf.json` (its
`listen` section is ignored here, Kestrel owns the port). The SQLite file and the
photo store are created next to the executable on first run and about 900 jobs are
seeded then, so the first launch takes a moment.

## Routes

| Route | Method | Gate |
|---|---|---|
| `/healthz` | GET / POST | public |
| `/favicon.svg`, `/sgcWebSockets.js` | GET | public |
| `/login` | GET / POST | public |
| `/logout` | GET / POST | public |
| `/theme` | POST | cookie switcher |
| `/passkey/login/options`, `/passkey/login/verify` | POST | public |
| `/passkey/register/options`, `/passkey/register/verify` | POST | signed in (JSON 401) |
| `/track` | GET / POST | public, the reference IS the credential |
| `/track/{reference}` | GET | public |
| `/track/{reference}/approve` | POST | public, approve or rate |
| `/` | GET | signed in; technician to `/my`, customer to `/track`, office to the board |
| `/my` | GET | signed in, needs a technician record |
| `/my/{id}` | GET | signed in, `CanActOnJob` |
| `/my/{id}/enroute`, `/onsite`, `/complete` | POST | signed in, `CanActOnJob` + the state machine |
| `/my/{id}/check`, `/part`, `/photo`, `/sign` | POST | signed in, `CanActOnJob` |
| `/my/chat/{id}` | GET / POST | signed in, `CanActOnJob` |
| `/board/fragment` | GET | dispatcher / manager |
| `/board/assign`, `/board/unassign` | POST | dispatcher / manager |
| `/gantt` | GET | dispatcher / manager |
| `/gantt/move` | POST | dispatcher / manager |
| `/map`, `/map/fragment` | GET | dispatcher / manager |
| `/calendar` | GET | dispatcher / manager |
| `/jobs`, `/jobs/new`, `/jobs/{id}` | GET | dispatcher / manager |
| `/jobs/save`, `/jobs/cancel` | POST | dispatcher / manager |
| `/jobs/{id}/status` | POST | dispatcher / manager + the state machine |
| `/jobs/{id}/report.pdf` | GET | signed in, `CanActOnJob` |
| `/jobs/{id}/photos/{photo}` | GET | signed in, `CanActOnJob` |
| `/customers`, `/customers/{id}` | GET | dispatcher / manager |
| `/customers/save` | POST | dispatcher / manager |
| `/sites/{id}` | GET | dispatcher / manager |
| `/assets`, `/assets/{id}` | GET | dispatcher / manager |
| `/parts` | GET | dispatcher / manager |
| `/parts/save` | POST | dispatcher / manager |
| `/reports`, `/reports/sla.pdf`, `/reports/utilisation.xlsx` | GET | dispatcher / manager |
| `/sql` | GET | dispatcher / manager |
| `/users` | GET | dispatcher / manager |
| `/users/save` | POST | dispatcher / manager |
| `/audit` | GET | dispatcher / manager |
