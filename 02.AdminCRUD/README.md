# 02.AdminCRUD: Admin Console demo (ASP.NET Core)

A dark-themed analytics and admin console built with sgcHTML .NET: a
Chart.js dashboard, CRUD screens for customers, providers, products and
multi-line invoices, reports and a settings/firewall/audit admin section,
running on Kestrel. It shows off password + passkey sign-in, a 12-language
interface and htmx live search, all server-rendered from sgcHTML components.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8097/ and sign in with `admin` / `admin`.

## Sign in

- Username/password: `admin` / `admin`.
- WebAuthn passkeys: once signed in, register a passkey from **Security**
  (needs a browser and a real authenticator, such as Windows Hello or a
  security key). After that, the login page also accepts a passkey.

## Features

- Dashboard with KPI cards and Chart.js charts.
- CRUD for customers, providers and multi-line invoices.
- Products list with live htmx search as you type, and an inline add/edit
  form fragment.
- Reports page with statistics by granularity.
- 12-language interface, switchable from the menu without a page reload.
- Admin section: user management, application settings, an IP firewall
  (block, unblock, allow-list) and an audit log.

## Configuration

`sgcAdminServer.conf.json` sets the seeded admin username/password and the
SQLite database file location (`data\admin.db`, created next to the
executable on first run). Its `listen` section is ignored; Kestrel owns the
port, set in `appsettings.json` (default 8097).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcAdminWebHost.cs` | Request handlers, sessions, cookies |
| `sgcAdmin_Pages.cs` | Page and htmx fragment rendering |
| `sgcAdmin_DB.cs` | SQLite schema, queries and demo data seed |
| `sgcAdmin_Sessions.cs` | In-memory cookie session store |
| `sgcAdmin_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcAdmin_I18n.cs` | 12-language string table |
| `sgcAdmin_Bcrypt.cs` | Password hashing |
| `sgcAdmin_Config.cs` | Configuration loader |
| `sgcAdmin_Types.cs` | Domain types |
| `sgcAdminServer.conf.json` | Admin credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
