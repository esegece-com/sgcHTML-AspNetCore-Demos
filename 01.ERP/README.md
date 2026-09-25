# 01.ERP: ERP demo (ASP.NET Core)

A Bootstrap-based ERP web app built with sgcHTML .NET: customers, providers,
products and multi-line invoices, a Chart.js dashboard, reports, and an admin
section, all rendered server-side and running on Kestrel. It shows off
password + passkey sign-in, a 12-language interface and htmx-powered SPA-like
navigation, all built from sgcHTML components with no client-side framework.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8098/ and sign in with `admin` / `admin`.

## Sign in

- Username/password: `admin` / `admin`.
- WebAuthn passkeys: once signed in, register a passkey from **Security**
  (needs a browser and a real authenticator, such as Windows Hello or a
  security key). After that, the login page also accepts a passkey.

## Features

- Dashboard with KPI cards and Chart.js charts.
- CRUD for customers, providers and products, and multi-line invoices with
  their own line items.
- Reports page with statistics by granularity (day/week/month).
- 12-language interface, switchable from the menu without a page reload.
- Light/dark theme switch.
- Admin section: user management, application settings, an IP firewall
  (block, unblock, allow-list) and an audit log.
- SPA-like navigation powered by htmx: links and forms load in place, a thin
  progress bar shows a running request, and view transitions animate page
  changes while every response stays a real, full server-rendered page.

## Configuration

`sgcERPServer.conf.json` sets the seeded admin username/password and the
SQLite database file location (`data\erp.db`, created next to the executable
on first run). Its `listen` section is ignored; Kestrel owns the port, set in
`appsettings.json` (default 8098).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcERPWebHost.cs` | Request handlers, sessions, cookies |
| `sgcERP_Pages.cs` | Page and fragment rendering |
| `sgcERP_DB.cs` | SQLite schema, queries and demo data seed |
| `sgcERP_Sessions.cs` | In-memory cookie session store |
| `sgcERP_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcERP_I18n.cs` | 12-language string table |
| `sgcERP_Bcrypt.cs` | Password hashing |
| `sgcERP_Config.cs` | Configuration loader |
| `sgcERP_Types.cs` | Domain types |
| `sgcERPServer.conf.json` | Admin credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
