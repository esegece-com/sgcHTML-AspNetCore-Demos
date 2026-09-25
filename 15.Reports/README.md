# 15.Reports: Reporting and BI portal demo (ASP.NET Core)

A reporting and business-intelligence portal built with sgcHTML .NET,
running on Kestrel. It shows off dashboards, a report catalogue with
parameter forms and exports, an ad-hoc pivot builder, drill-down navigation,
and a background job feed pushed live over WebSocket, all with a "no REST
tier" design where every page binds a live database query straight into a
component.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8104/ and sign in. The first start seeds about
27,000 order lines across roughly 6,000 orders into the database in a single
transaction, so the first launch takes a moment.

## Sign in

- `admin` / `admin`, `analyst` / `demo1234` or `viewer` / `demo1234`.
- WebAuthn passkeys: once signed in, register a passkey from **Security**
  (needs a browser and a real authenticator, such as Windows Hello or a
  security key). After that, the login page also accepts a passkey.

## Features

- Four ready-made dashboards: sales, margin, pipeline, ops.
- Report catalogue with parameter forms; run a report as an htmx result
  fragment, an inline PDF preview, or export to PDF, XLSX or CSV (raw-row
  exports are restricted to the analyst and admin roles).
- Ad-hoc pivot / cross-tab builder with PDF and XLSX export.
- Explore: paged and virtual-scroll browsing of the underlying order lines,
  with PDF and XLSX export.
- Drill-down by region, category, salesperson or customer.
- Saved views, scoped to the signed-in user.
- Report schedules, runnable on demand.
- Jobs page: job progress and a log viewer, pushed live over WebSocket.
- `/sql` prints the exact SQL statement next to the component it feeds.
- English, Spanish and German interface.

## Configuration

`sgcReportsServer.conf.json` sets the seeded admin username/password and the
SQLite database file location (`data\reports.db`, created next to the
executable on first run). Its `listen` section is ignored; Kestrel owns the
port, set in `appsettings.json` (default 8104).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, the background push service |
| `sgcReportsWebHost.cs` | Request handlers, sessions, cookies |
| `sgcReports_Pages.cs` | Every page and htmx fragment |
| `sgcReports_DB.cs` | SQLite schema, seed, report engine, dashboards |
| `sgcReports_Sessions.cs` | In-memory cookie session store |
| `sgcReports_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcReports_I18n.cs` | English/Spanish/German string table |
| `sgcReports_Bcrypt.cs` | Password hashing |
| `sgcReports_Config.cs` | Configuration loader |
| `sgcReports_Types.cs` | Domain types |
| `sgcReportsServer.conf.json` | Admin credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
