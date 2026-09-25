# 03.LiveMonitor: Live Monitor demo (ASP.NET Core)

An authenticated real-time monitoring dashboard built with sgcHTML .NET:
KPI cards, a live Chart.js line chart and a streaming events table, all
pushed from the server about every 1.5 seconds over a WebSocket, running on
Kestrel. It shows off sgcHTML's server push (htmx over WebSocket) on top of
the same password + passkey admin stack as the other demos.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8101/ and sign in with `admin` / `admin`. The
dashboard starts streaming live metrics within about 1.5 seconds.

## Sign in

- Username/password: `admin` / `admin`.
- WebAuthn passkeys: once signed in, register a passkey from **Security**
  (needs a browser and a real authenticator, such as Windows Hello or a
  security key). After that, the login page also accepts a passkey.

## Features

- Live dashboard: four KPI cards, a Chart.js line chart and a streaming
  events table, all updated by the server without any polling, over the
  htmx WebSocket channel.
- 12-language interface and light/dark theme, switchable from the menu.
- Admin section: user management, application settings, an IP firewall
  (block, unblock, allow-list) and an audit log.

## Configuration

`sgcLiveMonitorServer.conf.json` sets the seeded admin username/password and
the SQLite database file location (`data\monitor.db`, created next to the
executable on first run). Its `listen` section is ignored; Kestrel owns the
port, set in `appsettings.json` (default 8101).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, the background push service |
| `sgcLiveMonitorWebHost.cs` | Request handlers, sessions, cookies, the pushed metrics fragment |
| `sgcLiveMonitor_Pages.cs` | Page rendering, including the live dashboard |
| `sgcLiveMonitor_DB.cs` | SQLite schema, queries and demo data seed |
| `sgcLiveMonitor_Sessions.cs` | In-memory cookie session store |
| `sgcLiveMonitor_Passkeys.cs` | WebAuthn passkey registration and sign-in |
| `sgcLiveMonitor_I18n.cs` | 12-language string table |
| `sgcLiveMonitor_Bcrypt.cs` | Password hashing |
| `sgcLiveMonitor_Config.cs` | Configuration loader |
| `sgcLiveMonitor_Types.cs` | Domain types |
| `sgcLiveMonitorServer.conf.json` | Admin credentials, database path |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
