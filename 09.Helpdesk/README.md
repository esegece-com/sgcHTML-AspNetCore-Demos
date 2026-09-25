# 09.Helpdesk (ASP.NET Core)

Support-ticket helpdesk demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\09.Helpdesk` (which hosts `TsgcWebSocketHTTPServer`): the reusable
logic is copied **verbatim** from that demo, and only the hosting layer changes.

A Bootstrap app: bcrypt password login, self-registration, in-memory cookie
sessions, a SQLite database (schema + seed), user + admin roles, ticket threads
with replies, **multipart file attachments** (upload + download), close / reopen,
an admin dashboard (opened-vs-closed activity chart + date-range filter), a
per-owner ticket list with filters, and a CSV export. No passkeys, no i18n.

It reuses the same auth / session / cookie / DB-seed **host pattern** as the
`61.HTML.AspNetCore` heavy demos (02.AdminCRUD, 01.ERP), adding the multipart
upload / download wiring.

## Copied verbatim (reused, not changed)

- `sgcHelpdesk_Bcrypt.cs` - bcrypt hash / verify (`Bcrypt.BcryptHash` / `BcryptVerify`).
- `sgcHelpdesk_Config.cs` - `System.Text.Json` config loader.
- `sgcHelpdesk_DB.cs` - `Microsoft.Data.Sqlite` schema, ticket store and demo seed (`THelpdeskDBPool`).
- `sgcHelpdesk_Pages.cs` - every page (node layer, `THelpdeskPages`).
- `sgcHelpdesk_Sessions.cs` - the thread-safe in-memory cookie session store (`THelpdeskSessionStore`).
- `sgcHelpdesk_Types.cs` - domain records + `THelpdeskServerConfig`.

**Not** copied: `sgcHelpdesk_Server.cs` (the `TsgcWebSocketHTTPServer` host), the
console `Program.cs`, and `sgcHelpdesk_Multipart.cs` (the hand-rolled
multipart/form-data parser, replaced by ASP.NET Core `IFormFile`). The rewritten
host is `Program.cs` (endpoint mapping) + `HelpdeskWebHost.cs` (`HelpdeskWebHost`,
one IResult-returning method per DispatchRequest branch). The **storage half** of
the old `_Multipart.cs` (`THelpdeskAttachStore` / `THelpdeskAttachSaved`) lives on
in `sgcHelpdesk_Attachments.cs`, ported with the same on-disk layout but fed from
`IFormFile` instead of the old parser.

## How it is hosted (the reusable pattern)

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (the pages
  link `/bootstrap.min.css` and `/bootstrap.bundle.min.js`, served by file name
  from the adapter asset registry), the PWA manifest / service worker and the
  `/ws` channel.
- `HelpdeskWebHost` is a single DI singleton that owns the reused objects: the DB
  pool, the session store, the page builder and the attachment store. Its
  constructor runs the DB init exactly like the console demo:
  `EnsureSchema()` -> `SeedAdmin(user, BcryptHash(password))` -> `SeedDemoData()`
  (idempotent: seeds demo users `alice` / `bob` / `carol` with password
  `demo1234` and their tickets the first time the DB is empty).
- **Auth + session + cookie**: `POST /login` validates username + password with
  the reused bcrypt against the DB (`AuthenticateUser`), creates a session in
  `THelpdeskSessionStore` and sets the **same** `helpdesk_session` cookie
  (`Path=/; HttpOnly; SameSite=Lax`) through `ctx.Response.Cookies`. `POST
  /register` creates a user (bcrypt) and signs them straight in. Protected
  endpoints run through `host.Guarded(...)` (redirect to `/login` when signed
  out). `Logout` destroys the session and deletes the cookie. The `helpdesk_theme`
  cookie is read + written the same way.
- **IDOR ticket-ownership check (preserved)**: every ticket route
  (`/tickets/{id}` and its `/reply`, `/close`, `/reopen`, `/files/{attId}`
  sub-routes) re-loads the ticket and, unless the caller is `admin` or the
  ticket's owner, redirects to `/` (attachment mismatches return `404`). A
  non-owner, non-staff user can never see or act on another user's ticket or
  download its attachments.
- **Multipart (the IFormFile change)**: the create-ticket and reply forms POST
  `multipart/form-data`. The endpoints read the text fields (`subject` /
  `message` / `body`) and the uploaded files (`ctx.Request.Form.Files`, field
  name `attachments`) through the framework. Each file is materialized into a
  `THelpdeskUploadFile` and persisted by the reused `THelpdeskAttachStore` under
  `<exe dir>\data\attachments\<ticket_id>\<random-stored-name>`; the attachment
  metadata row (original name, stored name, content type, size) is written by the
  reused DB layer exactly as before. The same validation applies (max 5 files,
  10 MB total, dangerous-extension block-list).
- **Attachment download**: `GET /tickets/{id}/files/{attId}` (after the ownership
  + attachment-belongs-to-ticket checks) streams the file with
  `Results.File(bytes, contentType, fileName)`, reproducing the original
  content-type and `Content-Disposition: attachment; filename="..."`.
- **Query + form merge**: `GetParam` reads the query string first, then the form
  body (urlencoded or multipart), matching the 60.HTML `GetParam` merge.

### The ASP0016 hosting note

A Minimal API lambda whose only parameter is `HttpContext` and which returns
`Task<IResult>` is treated as a raw `RequestDelegate`, so its `IResult` is
**discarded** (the cookie side effects run but the redirect / status / body are
lost). To avoid that trap, every endpoint lambda returns plain `Task` and the
`Run` / `RunForm` helpers execute the handler's `IResult` onto the response with
`await result.ExecuteAsync(ctx)`. This is why `POST /login` returns `302`, not
`200`.

## Routes

Public (no session):

| Method | Path | Handler |
|---|---|---|
| GET  | `/login`     | Login page (pre-fills `admin`/`admin` until a non-admin exists) |
| POST | `/login`     | bcrypt login -> session cookie -> `302 /` |
| GET  | `/register`  | Self-registration page |
| POST | `/register`  | Create user (bcrypt) -> session -> `302 /` |
| GET/POST | `/logout`| Destroy session + clear cookie -> `302 /login` |
| POST | `/theme`     | Set `helpdesk_theme` cookie -> `302` back |
| GET  | `/favicon.svg` | Inline brand favicon |
| GET  | `/healthz`   | `ok` |

Logged-in (IDOR-guarded on every `/tickets/*`):

| Method | Path | Handler |
|---|---|---|
| GET  | `/`                              | Ticket list (admin: all + filters; user: own) |
| GET  | `/dashboard`                     | Admin-only activity dashboard (`range`) |
| GET  | `/tickets/new`                   | New-ticket form (user role only) |
| POST | `/tickets/new`                   | Create ticket + attachments -> `302 /tickets/{id}?flash=created` |
| GET  | `/tickets/export.csv`            | Admin-only CSV export (`text/csv`) |
| GET  | `/tickets/{id}`                  | Ticket detail + message thread |
| POST | `/tickets/{id}/reply`            | Reply + attachments -> `302 ?flash=replied` |
| POST | `/tickets/{id}/close`            | Close ticket -> `302 ?flash=closed` |
| POST | `/tickets/{id}/reopen`           | Reopen ticket -> `302 ?flash=reopened` |
| GET  | `/tickets/{id}/files/{attId}`    | Download an attachment (owner / staff only) |

Any unmapped path returns `404`.

## Configuration

`sgcHelpdeskServer.conf.json` carries the admin credentials (`admin` / `admin`)
and the SQLite path (`data\helpdesk.db`). The DB file and the `data\attachments\`
tree are resolved next to the executable (`AppContext.BaseDirectory`). The
`listen` section is ignored here; Kestrel owns the port (`appsettings.json`,
default **8100**).

## Run

```
dotnet run --project sgcHelpdeskWeb.csproj
```

Then open `http://localhost:8100/` and sign in with `admin` / `admin`, or with a
seeded demo user (`alice` / `bob` / `carol`, password `demo1234`), or register a
new account.
