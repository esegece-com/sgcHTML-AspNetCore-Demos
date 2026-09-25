# 05.HTMX: htmx patterns demo (ASP.NET Core)

A single-page gallery of common htmx interaction patterns, rendered
server-side with sgcHTML .NET and running on Kestrel. Each interaction is a
server-rendered fragment loaded over htmx from its own route, with no
client-side framework or hand-written JavaScript.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open http://localhost:8094/.

## Features

Pick any tile from the shell to try a pattern:

- Dashboard with out-of-band (OOB) swaps that refresh several parts of the
  page from one response.
- Active/smart search that queries as you type.
- Step wizard, moving forward and back through server-rendered steps.
- Activity feed that ticks with new entries.
- Inline edit, save and cancel.
- Live field validation (email, username) without a page reload.
- Delete-row with `hx-delete`, removing the row without a full refresh.
- Infinite scroll, loading further rows on demand.
- A shopping cart, adding and removing items.
- Cascading selects (country to city).

## Configuration

No sign-in and no database. The listen port comes from `appsettings.json`
(default 8094).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcHTMX_Pages.cs` | Every page and fragment for the ten patterns |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
