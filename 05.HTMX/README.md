# 05.HTMX (ASP.NET Core)

htmx features demo hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\05.HTMX` (which hosts `TsgcWebSocketHTTPServer`): the page-building
logic in `sgcHTMX_Pages.cs` is copied **verbatim** from that demo, and only the
hosting layer changes.

It demonstrates the common htmx interaction patterns (dashboard with out-of-band
swaps, active search, step wizard, activity feed, inline edit, live validation,
delete-row, infinite scroll, cart, cascading selects). Each interaction is a
server-rendered fragment loaded over htmx from its own route.

## How it is hosted

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets
  (`/bootstrap.min.css`, `/bootstrap.bundle.min.js`, `/htmx.min.js`), the PWA
  manifest / service worker and the `/ws` push channel. The htmx pages reference
  only those three assets, all served by the adapter registry (self-hosted, no
  CDN), matching the 60.HTML resource lookup.
- Each `DispatchRequest` branch is a Minimal API endpoint, preserving the exact
  route path + query parameters + verb the reused pages emit. Every fragment is
  `hx-get` except the single `hx-delete` row removal (`MapDelete`).

## Routes

| Method | Path | Renders |
|--------|------|---------|
| GET | `/` | htmx demo shell |
| GET | `/demo/dashboard` | Dashboard |
| GET | `/demo/dashboard-tick` | Dashboard OOB tick |
| GET | `/demo/search` | Smart search page |
| GET | `/demo/search-results?q=&cat=` | Search results |
| GET | `/demo/wizard` | Step wizard |
| GET | `/demo/wizard-step?step=&wname=&wemail=&wrole=&wteam=` | Wizard step |
| GET | `/demo/feed` | Activity feed |
| GET | `/demo/feed-tick` | Feed tick |
| GET | `/demo/edit` | Inline edit |
| GET | `/demo/edit-form?id=&v=` | Inline edit form |
| GET | `/demo/edit-save?id=&v=` | Inline edit view (save) |
| GET | `/demo/edit-cancel?id=&v=` | Inline edit view (cancel) |
| GET | `/demo/validate` | Live validation page |
| GET | `/demo/validate-email?email=` | Email validation fragment |
| GET | `/demo/validate-username?username=` | Username validation fragment |
| GET | `/demo/delete` | Delete-row page |
| DELETE | `/demo/delete-row` | Removes the row (empty fragment) |
| GET | `/demo/scroll` | Infinite scroll |
| GET | `/demo/scroll-rows?offset=&rootId=` | Next scroll rows |
| GET | `/demo/cart` | Cart |
| GET | `/demo/cart-add?id=` | Cart after add |
| GET | `/demo/cart-remove?id=` | Cart after remove |
| GET | `/demo/cascade` | Cascade selects |
| GET | `/demo/cascade-cities?country=` | Cities for country |

## Run

```
dotnet run --project sgcHTMXWeb.csproj
```

Then open the shell (default port `8094`, see `appsettings.json`):

```
http://localhost:8094/
```
