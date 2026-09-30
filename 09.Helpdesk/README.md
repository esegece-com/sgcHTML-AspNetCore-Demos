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

- Four-status ticket pipeline: New, Pending Resolution, Pending Feedback and
  Closed, plus a priority (low, medium, high, critical) and a category
  (general, account, billing, technical) on every ticket.
- Ticket threads with replies, close and reopen (a reopened ticket goes back
  to New).
- File attachments: upload up to 5 files per message (10 MB total, with a
  block-list of dangerous extensions) and download them from the ticket.
- A per-owner ticket list with filters; admins see every ticket.
- Admin dashboard: an opened-vs-closed activity chart with a date-range
  filter.
- CSV export of tickets (admin only).
- Every ticket route is ownership-checked: a user can only see or act on
  their own tickets and attachments, admins can see all.

## Kanban board

Admins get a **Kanban** tab next to the ticket list (All Tickets page):

- One column per status and one swimlane per priority. Drag a card to
  another column to change its status, or to another lane to change its
  priority.
- Workflow rules: a ticket is closed only from Pending Feedback, and a closed
  ticket can only be reopened into Pending Resolution. The board blocks other
  moves in the browser and the server validates every drop again
  (`POST /tickets/kanban-move` answers 409 for a move that is not allowed).
- Pending Resolution has a WIP limit of 2 tickets per lane, and every card
  shows its SLA due date (from the priority), its category and its owner.
- Search box, a quick add form (`+`) at the bottom of every cell
  (`POST /tickets/kanban-add`, never into Closed), and an edit dialog opened by
  clicking a card (`GET` / `POST /tickets/kanban-edit`: subject, priority and
  category).
- Live sync: every change is pushed as a card fragment to every other open
  board over a WebSocket, so several admins see the same board without
  reloading. The board's sgcHTMX bridge connects to the page URL (`ws://host/`);
  the adapter accepts it (`AcceptWebSocketOnAnyPath`), keeps the session
  cookie of the upgrade request, and the app only pushes to browsers signed in
  as admin (`ISgcHtmlHub.BroadcastAsync` with a session filter).
- The Kanban endpoints are admin only (403 for other users).

Databases created by an older build are upgraded on startup: the `priority`
and `category` columns are added and old `open` tickets move to New.

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
