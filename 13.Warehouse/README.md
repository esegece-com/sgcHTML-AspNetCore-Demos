# 13.Warehouse: Warehouse management (WMS) demo (ASP.NET Core)

A Bootstrap warehouse-management app built entirely from sgcHTML components,
running on Kestrel, with a deterministic seed of about 180 products, 92
locations, 130 orders and 1,200 stock movements. It shows off three roles
(back office, supervisor and handheld operator), password + passkey sign-in,
and a "no REST tier" design where every page binds a live database query
straight into a component.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8102/ for the back office and
http://localhost:8102/hh for the handheld scanner UI.

## Sign in

- `admin` / `admin`, back office and admin.
- `supervisor` / `demo1234`, back office.
- `operator` / `demo1234`, handheld only.
- WebAuthn passkeys: once signed in, register a passkey from **Security**
  (needs a browser and a real authenticator, such as Windows Hello or a
  security key). After that, the login page also accepts a passkey.

## Features

- Dashboard with Chart.js charts and a bin-utilisation heatmap.
- Product catalogue with server-side search, sort and paging, and a location
  tree.
- Inbound: purchase orders, receiving, put-away, damage photos, generated
  labels.
- Outbound: sales orders, picking, packing, a proof-of-delivery signature
  pad, generated packing slips.
- Stock grid and the full movement ledger, plus cycle counts.
- XLSX and PDF reports.
- A mobile handheld scanner UI under `/hh` for receiving, picking and
  counting, restricted to signed-in users.
- User management and an audit log (admin only).
- `/sql` prints the exact SQL statement next to the component it feeds, so
  you can see the query behind every page.

## Configuration

`sgcWMSServer.conf.json` sets the seeded admin username/password, the SQLite
database file location (`data\wms.db`, created next to the executable on
first run) and the company name shown in the app. Its `listen` section is
ignored; Kestrel owns the port, set in `appsettings.json` (default 8102).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcWMSWebHost.cs` | Request handlers, roles, sessions, cookies |
| `sgcWMS_Pages.cs` | Back-office pages and fragments |
| `sgcWMS_Handheld.cs` | Handheld scanner screens |
| `sgcWMS_DB.cs` | SQLite schema, queries and demo data seed |
| `sgcWMS_Reports.cs` | XLSX/PDF exports, packing slips and labels |
| `sgcWMS_Multipart.cs` | Upload validation and photo storage |
| `sgcWMS_Sessions.cs` | In-memory cookie session store |
| `sgcWMS_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcWMS_Bcrypt.cs` | Password hashing |
| `sgcWMS_Config.cs` | Configuration loader |
| `sgcWMS_Types.cs` | Domain types |
| `sgcWMSServer.conf.json` | Admin credentials, database path, company name |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
