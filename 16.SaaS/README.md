# 16.SaaS: Multi-tenant SaaS control plane demo (ASP.NET Core)

A multi-tenant SaaS control plane built with sgcHTML .NET, running on
Kestrel, over a single shared database. It shows off two axes of one app: a
tenant workspace and a vendor console, with strict tenant data isolation and
a "no REST tier" design where every page binds a live, tenant-scoped
database query straight into a component.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8105/. The SQLite file and demo data are created
on first run, so the first launch takes a moment.

## Sign in

- `root` / `root`, vendor superadmin.
- `support` / `root`, vendor read-only support account.
- The seeded tenant accounts carry no usable password on purpose: reach a
  tenant workspace from the vendor console by impersonating a tenant owner,
  or create your own account through **Sign up**.

## Features

- **Tenant workspace** (`/app`): dashboard, onboarding flow, projects,
  tasks, team management, a permission matrix, billing with simulated
  invoices and a PDF export, settings, notifications, an audit trail, and an
  isolation page that deliberately tries to read another tenant's row and
  shows the refusal.
- **Vendor console** (`/admin`): platform-wide KPIs, tenant list and detail,
  plans, feature flags, cross-tenant audit log, and superadmin impersonation
  of a tenant owner.
- Self-service sign-up, email verification and password reset flows (single
  use tokens). Social sign-in buttons are present but not wired to a real
  identity provider.
- Team invitations by link, where the invitation token itself is the
  credential.
- WebAuthn passkey sign-in and registration.
- `/sql` prints the exact SQL statement next to the component it feeds, so
  you can see the tenant-scoped query behind every page.

## Configuration

`sgcSaaSServer.conf.json` sets the seeded vendor username/password and the
SQLite database file location (`data\saas.db`, created next to the
executable on first run). Its `listen` section is ignored; Kestrel owns the
port, set in `appsettings.json` (default 8105).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcSaaSWebHost.cs` | Request handlers, the tenant/vendor gates, sessions, cookies |
| `sgcSaaS_Pages.cs` | Every page of both axes |
| `sgcSaaS_DB.cs` | SQLite schema, seed, tenant-scoped query helper, all CRUD |
| `sgcSaaS_Sessions.cs` | In-memory cookie session store, including impersonation |
| `sgcSaaS_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcSaaS_Bcrypt.cs` | Password hashing |
| `sgcSaaS_Config.cs` | Configuration loader |
| `sgcSaaS_Types.cs` | Domain types, role and status constants |
| `sgcSaaSServer.conf.json` | Vendor credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
