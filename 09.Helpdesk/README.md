# 09.Helpdesk: Support ticket helpdesk demo (ASP.NET Core)

A Bootstrap support-ticket helpdesk built with sgcHTML .NET, running on
Kestrel. It shows off self-registration, ticket threads with file
attachments, and an admin dashboard, all server-rendered from sgcHTML
components.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8100/ and sign in, or register a new account.

## Sign in

- `admin` / `admin`, the admin account (pre-filled on the login page until a
  non-admin account exists).
- Seeded demo users `alice`, `bob` or `carol`, password `demo1234`.
- Or register a new account from the login page.

## Features

- Ticket threads with replies, close and reopen.
- File attachments: upload up to 5 files per message (10 MB total, with a
  block-list of dangerous extensions) and download them from the ticket.
- A per-owner ticket list with filters; admins see every ticket.
- Admin dashboard: an opened-vs-closed activity chart with a date-range
  filter.
- CSV export of tickets (admin only).
- Every ticket route is ownership-checked: a user can only see or act on
  their own tickets and attachments, admins can see all.

## Configuration

`sgcHelpdeskServer.conf.json` sets the seeded admin username/password and
the SQLite database file location (`data\helpdesk.db`, created next to the
executable on first run). Attachments are stored under `data\attachments\`.
The `listen` section is ignored; Kestrel owns the port, set in
`appsettings.json` (default 8100).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `HelpdeskWebHost.cs` | Request handlers, sessions, cookies, uploads |
| `sgcHelpdesk_Pages.cs` | Page rendering |
| `sgcHelpdesk_DB.cs` | SQLite schema, ticket store and demo data seed |
| `sgcHelpdesk_Attachments.cs` | Attachment storage on disk |
| `sgcHelpdesk_Sessions.cs` | In-memory cookie session store |
| `sgcHelpdesk_Bcrypt.cs` | Password hashing |
| `sgcHelpdesk_Config.cs` | Configuration loader |
| `sgcHelpdesk_Types.cs` | Domain types |
| `sgcHelpdeskServer.conf.json` | Admin credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
