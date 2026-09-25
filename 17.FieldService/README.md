# 17.FieldService: Field service management demo (ASP.NET Core)

A field service management app built with sgcHTML .NET, running on Kestrel,
over a single shared database. It serves three audiences from one app: a
dispatch office, a technician on a phone, and a customer tracking portal
that needs no account. It shows off a live scheduling board, a job state
machine enforced on the server, and a "no REST tier" design where every page
binds a live database query straight into a component.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8106/. The SQLite database, the photo store and
about 900 seeded jobs are created on first run, so the first launch takes a
moment.

## Sign in

- `dispatch` / `dispatch`, the dispatcher (pre-filled on the login page).
- `manager` / `demo1234`, dispatcher plus the reporting pages.
- `amolina` / `demo1234`, a technician: you land on the mobile view at `/my`.
- The customer portal at `/track` needs no account: paste any job reference
  from the job list to open that job.

## Features

- **Dispatch office**: a drag-and-drop scheduling board with per-technician
  lanes, a multi-day Gantt chart, a live map, a month calendar, the job list
  and detail with the full status workflow, customers, sites, an asset
  hierarchy, a parts catalogue, SLA and utilisation reports with PDF and
  XLSX export, user management and an audit trail.
- **Technician view** (`/my`, on a phone): today's visits, three big status
  buttons (en route, on site, complete), a checklist, part scanning, photo
  upload, a signature pad, and a chat back to dispatch.
- **Customer portal** (`/track`): no login, the unguessable job reference is
  the credential and it opens exactly that one job, with an approve/rate
  action.
- Every status change goes through the same server-side state machine: an
  illegal move is refused (HTTP 409) and nothing is written, no matter which
  of the three views the request came from.
- Live simulation: every 3 seconds the server moves en-route vans closer to
  their destination and advances a job along its workflow, then pushes the
  update to whichever view is watching over WebSocket.
- `/sql` prints the exact SQL statement next to the component it feeds.

## Configuration

`sgcFieldServer.conf.json` sets the seeded dispatcher username/password and
the SQLite database file location (`data\field.db`, created next to the
executable on first run). Uploaded job photos are stored under
`data\photos\<job id>\`. Its `listen` section is ignored; Kestrel owns the
port, set in `appsettings.json` (default 8106).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, the live simulation background service |
| `sgcFieldWebHost.cs` | Request handlers, the three audience gates, sessions, cookies |
| `sgcField_Pages.cs` | Every page and live-update fragment |
| `sgcField_DB.cs` | SQLite schema, seed (about 900 jobs), all queries and CRUD |
| `sgcField_Types.cs` | Domain types, the job state machine, role/status/priority constants |
| `sgcField_Multipart.cs` | Upload validation and the job photo store |
| `sgcField_Sessions.cs` | In-memory cookie session store |
| `sgcField_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcField_Bcrypt.cs` | Password hashing |
| `sgcField_Config.cs` | Configuration loader |
| `sgcFieldServer.conf.json` | Dispatcher credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
