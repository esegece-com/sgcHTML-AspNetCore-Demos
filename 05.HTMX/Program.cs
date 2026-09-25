// ***************************************************************************
//  sgcHTMXWeb - htmx features demo on ASP.NET Core
//  Mirror of demos\60.HTML\05.HTMX (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME page builder (sgcHTMX_Pages.cs, copied verbatim) on Kestrel:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns "/".
//    - UseWebSockets() + UseSgcHtml() still serve the built-in client assets
//      (bootstrap.min.css, bootstrap.bundle.min.js, htmx.min.js), the PWA
//      manifest / service worker and the /ws push channel. The htmx pages only
//      reference the three assets above; all are served by the adapter registry.
//    - Each branch of the 60.HTML DispatchRequest is mapped to a Minimal API
//      endpoint below, preserving the exact route path + method + query params
//      so the htmx attributes in the reused pages still resolve. Every fragment
//      is hx-get except the single hx-delete row removal (MapDelete).
// ***************************************************************************

using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using HTMXDemo;

var builder = WebApplication.CreateBuilder(args);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands "/" back to the pipeline below).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

var app = builder.Build();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves assets + manifest + sw + /ws (not the page)

// Query helpers matching the demo's GetParam / StrToIntDef.
static string Q(HttpContext ctx, string name)
{
    string v = ctx.Request.Query[name];
    return v ?? "";
}

static int ParseIntDef(HttpContext ctx, string name, int def)
{
    int vValue;
    if (int.TryParse(Q(ctx, name).Trim(), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out vValue))
        return vValue;
    return def;
}

static IResult Html(string html)
{
    return Results.Content(html, "text/html; charset=utf-8");
}

// ---- routes: one per DispatchRequest branch, verbs matching the htmx attrs ---- //

// "/" -> the htmx demo shell (side nav; each demo loaded via hx-get)
app.MapGet("/", () => Html(TsgcHTMXDemoPages.BuildShell()));

// Dashboard + out-of-band tick
app.MapGet("/demo/dashboard", () => Html(TsgcHTMXDemoPages.BuildDemoDashboard()));
app.MapGet("/demo/dashboard-tick", () => Html(TsgcHTMXDemoPages.BuildDashboardTick()));

// Smart search (active search: ?q=&cat=)
app.MapGet("/demo/search", () => Html(TsgcHTMXDemoPages.BuildDemoSearch()));
app.MapGet("/demo/search-results", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildSearchResults(Q(ctx, "q"), Q(ctx, "cat"))));

// Step wizard (?step=&wname=&wemail=&wrole=&wteam=)
app.MapGet("/demo/wizard", () => Html(TsgcHTMXDemoPages.BuildDemoWizard()));
app.MapGet("/demo/wizard-step", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildWizardStep(ParseIntDef(ctx, "step", 1),
        Q(ctx, "wname"), Q(ctx, "wemail"), Q(ctx, "wrole"), Q(ctx, "wteam"))));

// Activity feed + tick
app.MapGet("/demo/feed", () => Html(TsgcHTMXDemoPages.BuildDemoFeed()));
app.MapGet("/demo/feed-tick", () => Html(TsgcHTMXDemoPages.BuildFeedTick()));

// Inline edit (view / form / save / cancel; ?id=&v=)
app.MapGet("/demo/edit", () => Html(TsgcHTMXDemoPages.BuildDemoInlineEdit()));
app.MapGet("/demo/edit-form", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildEditForm(Q(ctx, "id"), Q(ctx, "v"))));
app.MapGet("/demo/edit-save", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildEditView(Q(ctx, "id"), Q(ctx, "v"))));
app.MapGet("/demo/edit-cancel", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildEditView(Q(ctx, "id"), Q(ctx, "v"))));

// Live validation (?email= / ?username=)
app.MapGet("/demo/validate", () => Html(TsgcHTMXDemoPages.BuildDemoValidate()));
app.MapGet("/demo/validate-email", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildValidateEmail(Q(ctx, "email"))));
app.MapGet("/demo/validate-username", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildValidateUsername(Q(ctx, "username"))));

// Delete row (hx-delete): the view GET plus the DELETE that removes the row
app.MapGet("/demo/delete", () => Html(TsgcHTMXDemoPages.BuildDemoDeleteRow()));
app.MapDelete("/demo/delete-row", () => Html(" "));

// Infinite scroll (?offset=&rootId=)
app.MapGet("/demo/scroll", () => Html(TsgcHTMXDemoPages.BuildDemoInfiniteScroll()));
app.MapGet("/demo/scroll-rows", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildScrollRows(ParseIntDef(ctx, "offset", 0), Q(ctx, "rootId"))));

// Cart (add / remove; ?id=)
app.MapGet("/demo/cart", () => Html(TsgcHTMXDemoPages.BuildDemoCart()));
app.MapGet("/demo/cart-add", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildCartAdd(Q(ctx, "id"))));
app.MapGet("/demo/cart-remove", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildCartRemove(Q(ctx, "id"))));

// Cascade select (?country=)
app.MapGet("/demo/cascade", () => Html(TsgcHTMXDemoPages.BuildDemoCascade()));
app.MapGet("/demo/cascade-cities", (HttpContext ctx) =>
    Html(TsgcHTMXDemoPages.BuildCascadeCities(Q(ctx, "country"))));

app.Run();
