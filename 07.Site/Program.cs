// ***************************************************************************
//  sgcSiteWeb - Site Layouts web demo on ASP.NET Core
//  Mirror of demos\60.HTML\07.Site (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME page builder (sgcSite_Pages.cs, copied verbatim) on Kestrel:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns "/".
//    - UseWebSockets() + UseSgcHtml() still serve the built-in client assets,
//      the PWA manifest / service worker and the /ws push channel. Bootstrap
//      comes from the CDN, but the navigation options (OnPrepareTemplate in
//      sgcSite_Pages.cs) load /htmx.min.js, /idiomorph-ext.min.js,
//      /htmx-ext-head-support.min.js and /sgcHTMX.min.js, which the adapter
//      serves.
//    - MapGet("/") reads the demo's query contract (page/layout/theme/mode) and
//      returns the rendered site shell as text/html.
// ***************************************************************************

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using Site;

var builder = WebApplication.CreateBuilder(args);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands "/" back to the pipeline below).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

var app = builder.Build();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves assets + manifest + sw + /ws (not the page)

// "/" (and any bare path) renders the Site shell for the requested
// page/layout/theme/mode, exactly like the 60.HTML DispatchRequest.
app.MapGet("/", (HttpContext ctx) =>
{
    string vPage = ctx.Request.Query["page"];
    string vLayout = ctx.Request.Query["layout"];
    string vTheme = ctx.Request.Query["theme"];
    string vMode = ctx.Request.Query["mode"];

    string vHtml = TsgcSiteDemoPages.BuildDemo(vPage, vLayout, vTheme, vMode);
    return Results.Content(vHtml, "text/html; charset=utf-8");
});

app.Run();
