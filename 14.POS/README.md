# 14.POS: Retail point-of-sale demo (ASP.NET Core)

A Bootstrap retail till built with sgcHTML .NET, running on Kestrel. It
shows off htmx product tiles with a live cart, barcode scanning, split
payments, manager-PIN authorised discounts and refunds, generated PDF
receipts and reports, and a full back office, all server-rendered from
sgcHTML components.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8103/ and sign in. Open a shift before ringing
anything up.

## Sign in

- `admin` / `admin`, `manager` / `manager` or `cashier` / `cashier`.
- Manager PIN `1379`, admin PIN `4242` (needed for discounts above a
  threshold and for every refund).
- The login page also offers sign in with a passkey, but the till only
  *consumes* passkeys, it does not enrol new ones here: a credential has to
  already exist for the account.

## Features

- Cashier till: category and search tiles, a live cart, barcode scanning,
  parking and recalling a sale.
- Split payments across several tenders, with change calculated by the
  server.
- An 80mm till-roll receipt as a generated PDF.
- Manager-PIN authorised discounts (above a configurable threshold) and
  refunds, verified server-side.
- Shift open/close with X and Z reports (expected cash and variance
  computed on the server, never trusted from the browser).
- Back office: products, categories, promotions, customers, a Chart.js
  dashboard, reports with XLSX and PDF export, a SQL page, user management
  and an audit log.
- Three-tier authorisation: cashier, back office (manager/admin), admin.

## Configuration

`sgcPOSServer.conf.json` sets the seeded admin username/password, the SQLite
database file location (`data\pos.db`, created next to the executable on
first run) and the store details (name, address, tax id, currency symbol,
discount PIN threshold, shift target). Its `listen` section is ignored;
Kestrel owns the port, set in `appsettings.json` (default 8103).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcPOSWebHost.cs` | Request handlers, roles, sessions, cookies |
| `sgcPOS_Pages.cs` | Till and back-office pages and fragments |
| `sgcPOS_DB.cs` | SQLite schema, CRUD, analytics and demo data seed |
| `sgcPOS_Sessions.cs` | In-memory cookie session store |
| `sgcPOS_Passkeys.cs` | WebAuthn passkey sign-in |
| `sgcPOS_Bcrypt.cs` | Password hashing |
| `sgcPOS_Config.cs` | Configuration loader |
| `sgcPOS_Types.cs` | Domain types |
| `sgcPOSServer.conf.json` | Admin credentials, database path, store details |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
