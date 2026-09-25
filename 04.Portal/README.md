# 04.Portal: Customer Portal demo (ASP.NET Core)

A Bootstrap, role-gated customer portal built with sgcHTML .NET, running on
Kestrel. The same sign-in page leads to two different areas depending on the
signed-in user's role: a self-service customer area, or a back-office with
CRUD and reporting. It shows off role-based access control, per-customer data
scoping and password + passkey sign-in, all from sgcHTML components.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8099/ and sign in with one of the accounts below.

## Sign in

- `admin` / `admin`, the back-office and admin area.
- `customer` / `customer`, the customer self-service area.
- WebAuthn passkeys: once signed in, register a passkey from **Security**
  (needs a browser and a real authenticator, such as Windows Hello or a
  security key). After that, the login page also accepts a passkey.

## Features

- **Customer area** (role `customer`): an account dashboard, the customer's
  own orders with per-order detail, an editable profile and a support page.
  Every route scopes its data to the signed-in customer, so a customer can
  only ever see or edit their own account.
- **Back-office area** (role `user` / `admin`): a Chart.js dashboard, CRUD
  for customers, providers, products and multi-line invoices, and reports.
  The `admin` role additionally gets user management and a settings/IP
  firewall/audit-log admin section.
- 12-language interface, switchable from the menu without a page reload.

## Configuration

`sgcPortalServer.conf.json` sets the seeded admin username/password and the
SQLite database file location (`data\portal.db`, created next to the
executable on first run). Its `listen` section is ignored; Kestrel owns the
port, set in `appsettings.json` (default 8099).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcPortalWebHost.cs` | Request handlers, role gating, sessions, cookies |
| `sgcPortal_Pages.cs` | Page rendering for both the customer and back-office areas |
| `sgcPortal_DB.cs` | SQLite schema, queries and demo data seed, including customer-scoped queries |
| `sgcPortal_Sessions.cs` | In-memory cookie session store |
| `sgcPortal_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcPortal_I18n.cs` | 12-language string table |
| `sgcPortal_Bcrypt.cs` | Password hashing |
| `sgcPortal_Config.cs` | Configuration loader |
| `sgcPortal_Types.cs` | Domain types |
| `sgcPortalServer.conf.json` | Admin credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
