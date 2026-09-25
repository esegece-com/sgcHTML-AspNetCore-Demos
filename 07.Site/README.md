# 07.Site: Site Layouts demo (ASP.NET Core)

A tour of the sgcHTML `Site` component's layouts and theming, rendered
server-side with sgcHTML .NET and running on Kestrel. It shows off 6 page
layouts, 5 color presets and 3 theme modes, all driven by the query string,
plus SPA-like navigation over htmx with no client-side framework.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open one of the six layouts (default port 8092):

```
http://localhost:8092/?layout=sidebar-left
http://localhost:8092/?layout=sidebar-right
http://localhost:8092/?layout=topnav
http://localhost:8092/?layout=topnav-sidebar
http://localhost:8092/?layout=iconrail
http://localhost:8092/?layout=offcanvas
```

## Features

- 6 layouts: sidebar left, sidebar right, top nav, top nav with sidebar,
  icon rail, off-canvas.
- 5 color presets (blue, violet, emerald, slate, dark) and 3 theme modes
  (light, dark, system), switchable from the page without a reload.
- 5 sample pages (dashboard, customers, orders, reports, settings) selectable
  with the `page` query parameter, combined with `layout`.
- SPA-like navigation: the menu, switcher buttons and settings form load with
  htmx instead of a full page load, with view transitions between pages, a
  thin progress bar and a toast on failed requests. Back and forward restore
  pages from the htmx history.

Bootstrap is loaded from the CDN; the htmx scripts are served locally by the
app.

## Configuration

No sign-in and no database. The listen port comes from `appsettings.json`
(default 8092).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, sgcHTML services |
| `sgcSite_Pages.cs` | The site shell, built from `TsgcHTMLComponent_Site` |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
