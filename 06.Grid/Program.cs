// ***************************************************************************
//  sgcGridWeb - Grid features demo on ASP.NET Core
//  Mirror of demos\60.HTML\06.Grid (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME page builder (sgcGrid_Pages.cs, copied verbatim) on Kestrel:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns "/".
//    - UseWebSockets() + UseSgcHtml() still serve the built-in client assets
//      (bootstrap.min.css, bootstrap.bundle.min.js, htmx.min.js), the PWA
//      manifest / service worker and the /ws push channel. The grid pages only
//      reference the three assets above; all are served by the adapter registry.
//    - Each branch of the 60.HTML DispatchRequest is mapped to a Minimal API
//      endpoint below, preserving the exact route path + method + query params
//      so the htmx attributes in the reused pages still resolve.
// ***************************************************************************

using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using GridDemo;

var builder = WebApplication.CreateBuilder(args);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands "/" back to the pipeline below).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

var app = builder.Build();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves assets + manifest + sw + /ws (not the page)

// Small culture-invariant int parser matching the demo's StrToIntDef.
static int ParseIntDef(HttpContext ctx, string name, int def)
{
    string vRaw = ctx.Request.Query[name];
    int vValue;
    if (int.TryParse((vRaw ?? "").Trim(), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out vValue))
        return vValue;
    return def;
}

static IResult Html(string html)
{
    return Results.Content(html, "text/html; charset=utf-8");
}

// ---- routes: one per DispatchRequest branch (all GET, matching the demo) ---- //

// "/" -> the grid shell (tab navigation + first grid loaded via htmx)
app.MapGet("/", () => Html(TsgcGridDemoPages.BuildShell()));

app.MapGet("/grid/basic", () => Html(TsgcGridDemoPages.BuildGridBasic()));
app.MapGet("/grid/sort-filter", () => Html(TsgcGridDemoPages.BuildGridSortFilter()));
app.MapGet("/grid/export", () => Html(TsgcGridDemoPages.BuildGridExport()));
app.MapGet("/grid/edit", () => Html(TsgcGridDemoPages.BuildGridInlineEdit()));
app.MapGet("/grid/group", () => Html(TsgcGridDemoPages.BuildGridGroupBy()));
app.MapGet("/grid/reorder", () => Html(TsgcGridDemoPages.BuildGridColumnReorder()));

// pagination: ?page=N (default 1)
app.MapGet("/grid/page", (HttpContext ctx) =>
{
    int vPage = ParseIntDef(ctx, "page", 1);
    return Html(TsgcGridDemoPages.BuildGridPagination(vPage));
});

app.MapGet("/grid/scroll", () => Html(TsgcGridDemoPages.BuildGridVirtualScroll()));

// virtual scroll rows: ?offset=N&limit=N&rootId=X (defaults 0 / 20 / "")
app.MapGet("/grid/scroll-rows", (HttpContext ctx) =>
{
    int vOffset = ParseIntDef(ctx, "offset", 0);
    int vLimit = ParseIntDef(ctx, "limit", 20);
    string vRootId = ctx.Request.Query["rootId"];
    return Html(TsgcGridDemoPages.BuildScrollRows(vOffset, vLimit, vRootId ?? ""));
});

app.Run();
