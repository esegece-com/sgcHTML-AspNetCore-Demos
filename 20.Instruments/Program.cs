// ***************************************************************************
//  sgcInstrumentsWeb - control room demo of the sgcHTML instrumentation
//  components on ASP.NET Core
//  Mirror of demos\60.HTML\20.Instruments (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME view on Kestrel. This file is copied VERBATIM from the
//  60.HTML demo and is NOT changed here:
//    - sgcInstrumentsDemo_Pages.cs (every instrument, the 4 pages and the
//      live fragments / push scripts)
//
//  Only the hosting layer changes:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / htmx /
//      sgcHTMX assets by file name from the adapter's resource registry.
//      /sgcWebSockets.js (which the adapter does not carry) and the favicon are
//      served here.
//    - The 4 pages (/, /gauges, /panels, /controls, /trends), the control
//      endpoints POST /ctl/<name> and POST /ack become Minimal API endpoints
//      that run the SAME logic as the 60.HTML DispatchRequest (InstrumentsWebHost).
//    - PER-PAGE LIVE CHANNEL: the page's sgcHTMX bridge opens its WebSocket on
//      the page URL (location.pathname), not on /ws. A middleware placed BEFORE
//      UseSgcHtml accepts those upgrades and registers each socket under its
//      page, so every live fragment only reaches the browsers showing that page
//      (see InstrumentsWebHost). The adapter's /ws hub is not used: it has no
//      notion of which page a connection came from.
//    - The 60.HTML simulation thread becomes a BackgroundService with the SAME
//      100 ms beat: an oscilloscope frame every 200 ms and a process step plus
//      a push of every instrument every 500 ms.
//
//  IMPORTANT hosting note (the reusable pattern): a Minimal API lambda whose ONLY
//  parameter is HttpContext and which returns Task<IResult> is treated as a raw
//  RequestDelegate, so its IResult is DISCARDED (ASP0016). Every endpoint lambda
//  below returns plain Task and executes the handler's IResult explicitly.
// ***************************************************************************

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;
using InstrumentsDemo;

var builder = WebApplication.CreateBuilder(args);

// The app renders its own pages, so the adapter serves assets + the live channel
// only (ServeRootPage = false hands page routing back to the pipeline). The
// sgcHTMX bridge of every page connects to the page URL itself, so upgrades are
// accepted on any path; the host pushes per page with a filtered broadcast.
builder.Services.AddSgcHtml(o =>
{
    o.ServeRootPage = false;
    o.AcceptWebSocketOnAnyPath = true;
});

// The host owns the process state and the view; it pushes through ISgcHtmlHub.
builder.Services.AddSingleton<InstrumentsWebHost>();

// The simulation loop (mirror of the 60.HTML TsgcInstrumentsServer.Execute thread).
builder.Services.AddHostedService<InstrumentsSimulationService>();

var app = builder.Build();

var host = app.Services.GetRequiredService<InstrumentsWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml

app.UseSgcHtml();      // assets + manifest + sw + the per-page live channel

// ----- endpoint helpers (see the IMPORTANT hosting note above) ----- //

// Reads the urlencoded form (null when the request has no form body, so the
// parameters come from the query string only - matching the 60.HTML merge),
// runs the async IResult handler and writes its result to the response.
async Task RunForm(HttpContext ctx,
    Func<HttpContext, IFormCollection, Task<IResult>> handler)
{
    IFormCollection form = ctx.Request.HasFormContentType
        ? await ctx.Request.ReadFormAsync()
        : null;
    IResult vResult = await handler(ctx, form);
    await vResult.ExecuteAsync(ctx);
}

// ----- static assets the adapter does not own ----- //
app.MapMethods("/favicon.svg", new[] { "GET", "HEAD" }, (HttpContext ctx) =>
{
    ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
    return Results.Content(InstrumentsWebHost.CS_FAVICON_SVG, "image/svg+xml");
});
app.MapGet("/favicon.ico", (HttpContext ctx) =>
{
    ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
    return Results.Content(InstrumentsWebHost.CS_FAVICON_SVG, "image/svg+xml");
});
app.MapGet("/sgcWebSockets.js", () =>
{
    string vBody = GetEmbeddedAsset("sgcWebSockets.js");
    return vBody == ""
        ? Results.NotFound()
        : Results.Content(vBody, "application/javascript; charset=utf-8");
});

// ----- the 4 pages ----- //
foreach (string vRoute in new[] { "/", "/gauges", "/panels", "/controls", "/trends" })
    app.MapGet(vRoute, (HttpContext ctx) => host.PageGet(ctx).ExecuteAsync(ctx));

// ----- control endpoints (hx-post from the Controls and Panels pages) ----- //
app.MapPost("/ctl/{name}", (HttpContext ctx, string name) =>
    RunForm(ctx, (c, f) => host.ControlPost(c, name, f)));
app.MapPost(TInstrumentsConst.CS_ACK_URL, (HttpContext ctx) =>
    RunForm(ctx, host.AckPost));

Console.WriteLine(string.Format("{0} v{1}", "sgcInstrumentsWeb",
    InstrumentsWebHost.CS_INSTRUMENTS_SERVER_VERSION));

app.Run();

// ----- local helpers (part of the top-level program) ----- //

// Read an asset embedded in the esegece.sgcHTML assembly by its logical name.
static string GetEmbeddedAsset(string aFileName)
{
    try
    {
        Assembly oAsm = typeof(TsgcHTMX_Engine_Server).Assembly;
        using Stream oStream = oAsm.GetManifestResourceStream(
            "esegece.sgcWebSockets.html." + aFileName);
        if (oStream == null)
            return "";
        using StreamReader oReader = new StreamReader(oStream);
        return oReader.ReadToEnd();
    }
    catch
    {
        return "";
    }
}

namespace InstrumentsDemo
{
    // The simulated process: every 100 ms it wakes up, pushes an oscilloscope
    // frame every 200 ms (~5 Hz) and advances the process + pushes every
    // instrument every 500 ms. Mirror of TsgcInstrumentsServer.Execute, driven by
    // the hosted service lifetime instead of a background thread.
    public sealed class InstrumentsSimulationService : BackgroundService
    {
        private readonly InstrumentsWebHost FHost;

        public InstrumentsSimulationService(InstrumentsWebHost aHost)
        {
            FHost = aHost;
        }

        protected override async Task ExecuteAsync(CancellationToken aStoppingToken)
        {
            int vCount = 0;
            while (!aStoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(100, aStoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                vCount++;
                try
                {
                    if (vCount % 2 == 0)
                        await FHost.DoScopeAsync().ConfigureAwait(false);
                    if (vCount % 5 == 0)
                        await FHost.DoTickAsync().ConfigureAwait(false);
                }
                catch (Exception E)
                {
                    Console.WriteLine("[push] error: " + E.Message);
                }
            }
        }
    }
}
