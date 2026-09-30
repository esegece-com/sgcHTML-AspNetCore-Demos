// ***************************************************************************
//  sgcReportsWeb - reporting and BI portal web-app demo on ASP.NET Core
//  Mirror of demos\60.HTML\15.Reports (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcReports_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcReports_Config.cs    (JSON config loader)
//    - sgcReports_DB.cs        (Microsoft.Data.Sqlite schema + CRUD + seed)
//    - sgcReports_Analytics.cs (Analytics: chart gallery, candles, pivot lab)
//    - sgcReports_I18n.cs      (en / es / de string table)
//    - sgcReports_Pages.cs     (component view layer: every page + fragment)
//    - sgcReports_Passkeys.cs  (WebAuthn passkey engine bridged to the DB)
//    - sgcReports_Sessions.cs  (in-memory cookie session store)
//    - sgcReports_Types.cs     (domain records + TReportsServerConfig)
//
//  Only the hosting layer changes (following the 01.ERP host pattern):
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / Chart.js /
//      htmx assets (by file name, from the adapter's resource registry) and the
//      htmx WebSocket channel.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim TReportsPages builder for every byte of HTML. The
//      auth gate is per-endpoint (host.Guarded / GuardedRole / GuardedNotViewer).
//    - Cookies (session / theme / language) are read + written through the
//      native ASP.NET Core cookie API, with the SAME names + attributes.
//    - A BackgroundService replaces the 60.HTML push thread: every 900 ms it
//      advances the export jobs (TReportsPages.LiveTick) and, every second
//      loop (about 2 s), one Analytics live chart point + one candle tick, and
//      broadcasts the out-of-band fragments through ISgcHtmlHub.
//    - The shared page shell opens the sgcHTMX bridge on ws://host<pathname>
//      (/jobs, /analytics, /analytics/market ...), so the adapter accepts the
//      channel on any path (AcceptWebSocketOnAnyPath) and bounds every push
//      with SendTimeout (2 s): a browser that stopped reading is dropped.
//    - PASSKEYS: the RP id (request host name) + origin (scheme://host) are
//      DERIVED from the live request and threaded into the reused passkey engine
//      instead of the 60.HTML hard-coded localhost (see ReportsWebHost).
//
//  IMPORTANT hosting note (the reusable pattern): a Minimal API lambda whose ONLY
//  parameter is HttpContext and which returns Task<IResult> is treated as a raw
//  RequestDelegate, so its IResult is DISCARDED (ASP0016) - the handler's cookie
//  side effects run but the redirect / status / body are lost. To avoid that trap
//  uniformly, every endpoint lambda below returns plain Task and the Run / RunForm
//  helpers execute the handler's IResult onto the response explicitly.
// ***************************************************************************

using System;
using System.Collections.Generic;
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
using Reports;

var builder = WebApplication.CreateBuilder(args);

// Load the Reports config (admin credentials + DB path) from
// sgcReportsServer.conf.json, tolerant of a missing file (defaults => admin/admin).
// The listen section is ignored here; Kestrel owns the port (appsettings.json).
var oConfig = new TReportsServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory,
    "sgcReportsServer.conf.json");
if (File.Exists(vConfigPath))
    new TReportsConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is launched
// from. conf.json's "data\\reports.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "reports.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
// The page shell connects the bridge to the page's own path, so upgrades are
// accepted on any path; a push a client does not read within 2 s drops it.
builder.Services.AddSgcHtml(o =>
{
    o.ServeRootPage = false;
    o.AcceptWebSocketOnAnyPath = true;
    o.SendTimeout = TimeSpan.FromSeconds(2);
});

// The host owns the reused singletons (DB pool, session store, page builder,
// per-origin passkey factory). Constructed once at startup below; DI disposes it
// (and the DB pool) on shutdown.
builder.Services.AddSingleton(sp => new ReportsWebHost(oConfig, vDbPath,
    sp.GetRequiredService<ISgcHtmlHub>()));

// The live push loop (mirror of the 60.HTML background push thread).
builder.Services.AddHostedService<ReportsPushService>();

var app = builder.Build();

// Force construction now so the DB schema + admin/demo seed run at startup
// (mirrors TReportsServer.InitRuntime), not lazily on the first request.
var host = app.Services.GetRequiredService<ReportsWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves Bootstrap/Chart.js/htmx assets + manifest + sw + the live channel

// ----- inbound WebSocket messages ----- //
// The adapter accepts /ws and, for each inbound text frame, calls
// engine.DispatchMessage(null, text) and pushes the RETURNED reply back to that
// sender via the hub. The JobProgress Cancel button must be seen by everyone
// watching, so it is broadcast through the hub rather than answered to the sender.
var oEngine = app.Services.GetRequiredService<TsgcHTMX_Engine_Server>();

oEngine.OnHTMXMessage += (object sender, TsgcWSConnection connection,
    string message, ref string response) =>
{
    response = "";

    List<string> vParams = new List<string>();
    string vPath;
    string vMethod;
    sgcHTMXHelpers.sgcHTMXParseMessage(message, out vPath, out vMethod, vParams);

    string vAction = GetParamValue(vParams, "action");
    if (vAction == "")
        return;

    if (string.Equals(vAction, "job:cancel", StringComparison.OrdinalIgnoreCase))
    {
        host.PushFragment(TReportsPages.LiveCancelJob(
            GetParamValue(vParams, "job")));
        return;
    }
};

// ----- endpoint helpers (see the IMPORTANT hosting note above) ----- //

// Runs a synchronous IResult handler and writes its result to the response.
Task Run(HttpContext ctx, Func<IResult> handler)
{
    return handler().ExecuteAsync(ctx);
}

// Reads the urlencoded form (null when the request has no form body, so GetParam
// falls back to the query string only - matching the 60.HTML merge), runs the
// IResult handler and writes its result to the response.
async Task RunForm(HttpContext ctx, Func<IFormCollection, IResult> handler)
{
    IFormCollection form = ctx.Request.HasFormContentType
        ? await ctx.Request.ReadFormAsync()
        : null;
    await handler(form).ExecuteAsync(ctx);
}

// ----- public routes ----- //
app.MapGet("/healthz", (HttpContext ctx) => Run(ctx, () => host.Health()));
app.MapGet("/favicon.svg", (HttpContext ctx) => Run(ctx, () => host.Favicon()));
app.MapGet("/pdf.min.js", (HttpContext ctx) => Run(ctx, () => host.PdfJs("pdf.min.js")));
app.MapGet("/pdf.worker.min.js",
    (HttpContext ctx) => Run(ctx, () => host.PdfJs("pdf.worker.min.js")));

// The reused pages link /sgcWebSockets.js, which the adapter registry does NOT
// serve (it is embedded in the esegece.sgcHTML assembly), so it is served here
// straight from the manifest by its logical name.
app.MapGet("/sgcWebSockets.js", () =>
{
    string vBody = GetEmbeddedAsset("sgcWebSockets.js");
    return vBody == ""
        ? Results.NotFound()
        : Results.Content(vBody, "application/javascript; charset=utf-8");
});

// ----- auth / theme / language ----- //
app.MapGet("/login", (HttpContext ctx) => Run(ctx, () => host.LoginGet(ctx)));
app.MapPost("/login", (HttpContext ctx) => RunForm(ctx, f => host.LoginPost(ctx, f)));
app.MapGet("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapPost("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapPost("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));
app.MapPost("/lang", (HttpContext ctx) => RunForm(ctx, f => host.SetLanguage(ctx, f)));

// ----- passkeys (WebAuthn) ----- //
// The 60.HTML host routed the WHOLE /webauthn/ prefix to one handler, so the
// same catch-all reaches the same action branches here (any verb, like Delphi).
app.Map("/webauthn/{**rest}", async (HttpContext ctx, string rest) =>
{
    IResult vResult = await host.WebAuthn(ctx, rest ?? "");
    await vResult.ExecuteAsync(ctx);
});

// ----- dashboards ----- //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Home(ctx, s))));
app.MapGet("/dashboards/{slug}",
    (HttpContext ctx, string slug) => Run(ctx, () => host.Guarded(ctx, s => host.Dashboard(ctx, s, slug))));
app.MapGet("/sql", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.SQLPage(ctx, s))));
app.MapGet("/jobs", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.JobsPage(ctx, s))));

// ----- reports ----- //
app.MapGet("/reports", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.ReportsGet(ctx, s))));
app.MapGet("/reports/new",
    (HttpContext ctx) => Run(ctx, () => host.GuardedRole(ctx, "admin",
        "Writing report SQL is an admin task.", s => host.ReportNew(ctx, s))));
app.MapPost("/reports/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedRole(ctx, "admin",
        "Writing report SQL is an admin task.", s => host.ReportSave(ctx, f, s))));
app.MapPost("/reports/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedRole(ctx, "admin",
        "Deleting a report is an admin task.", s => host.ReportDelete(ctx, f))));
app.MapGet("/reports/{id}",
    (HttpContext ctx, string id) => Run(ctx, () => host.Guarded(ctx, s => host.ReportForm(ctx, s, id))));
app.MapPost("/reports/{id}/run",
    (HttpContext ctx, string id) => RunForm(ctx, f => host.Guarded(ctx, s => host.ReportRun(ctx, f, s, id))));
app.MapGet("/reports/{id}/preview",
    (HttpContext ctx, string id) => Run(ctx, () => host.Guarded(ctx, s => host.ReportPreview(ctx, s, id))));
app.MapGet("/reports/{id}/run.pdf",
    (HttpContext ctx, string id) => Run(ctx, () => host.Guarded(ctx, s => host.ReportExport(ctx, s, id, "pdf"))));
app.MapGet("/reports/{id}/run.xlsx",
    (HttpContext ctx, string id) => Run(ctx, () => host.Guarded(ctx, s => host.ReportExport(ctx, s, id, "xlsx"))));
app.MapGet("/reports/{id}/run.csv",
    (HttpContext ctx, string id) => Run(ctx, () => host.Guarded(ctx, s => host.ReportExport(ctx, s, id, "csv"))));

// ----- pivot ----- //
app.MapGet("/pivot", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Pivot(ctx, s))));
app.MapPost("/pivot/build",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.PivotBuild(ctx, f))));
app.MapGet("/pivot/export.pdf",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.PivotExport(ctx, s, true))));
app.MapGet("/pivot/export.xlsx",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.PivotExport(ctx, s, false))));

// ----- analytics ----- //
// Chart gallery, price candles and the pivot lab (drill-through, field chooser,
// XLSX export; the viewer role gets a 403 on the export).
app.MapGet("/analytics",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Analytics(ctx, null, s, "/analytics"))));
// the chart ClickURL posts the clicked label (GET works too, as in 60.HTML)
app.MapMethods("/analytics/charts/region", new[] { "GET", "POST" },
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.Analytics(ctx, f, s, "/analytics/charts/region"))));
app.MapGet("/analytics/market",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Analytics(ctx, null, s, "/analytics/market"))));
app.MapGet("/analytics/pivot",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Analytics(ctx, null, s, "/analytics/pivot"))));
app.MapPost("/analytics/pivot/layout",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.Analytics(ctx, f, s, "/analytics/pivot/layout"))));
app.MapPost("/analytics/pivot/drill",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.Analytics(ctx, f, s, "/analytics/pivot/drill"))));
app.MapGet("/analytics/pivot/export.xlsx",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Analytics(ctx, null, s, "/analytics/pivot/export.xlsx"))));

// ----- explore ----- //
app.MapGet("/explore", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Explore(ctx, s))));
app.MapGet("/explore/rows",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.ExploreRows(ctx))));
app.MapGet("/explore/export.pdf",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.ExploreExport(ctx, s, true))));
app.MapGet("/explore/export.xlsx",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.ExploreExport(ctx, s, false))));

// ----- drilldown ----- //
app.MapGet("/drilldown/{dim}/{id}",
    (HttpContext ctx, string dim, string id) => Run(ctx, () => host.Guarded(ctx, s => host.Drilldown(ctx, s, dim, id))));

// ----- saved views ----- //
app.MapGet("/views", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Views(ctx, s))));
app.MapPost("/views/save",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.ViewsSave(ctx, f, s))));
app.MapPost("/views/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.ViewsDelete(ctx, f, s))));

// ----- schedules ----- //
app.MapGet("/schedules", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.Schedules(ctx, s))));
app.MapPost("/schedules/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedNotViewer(ctx,
        "The viewer role cannot change schedules.", s => host.ScheduleSave(ctx, f))));
app.MapPost("/schedules/run-now",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedNotViewer(ctx,
        "The viewer role cannot start an export.", s => host.ScheduleRunNow(ctx, f))));

// ----- users (admin) ----- //
app.MapGet("/users",
    (HttpContext ctx) => Run(ctx, () => host.GuardedRole(ctx, "admin",
        "Managing users is an admin task.", s => host.Users(ctx, s))));
app.MapPost("/users/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedRole(ctx, "admin",
        "Managing users is an admin task.", s => host.UsersSave(ctx, f, s))));

// ----- unknown protected path (mirrors the 60.HTML DispatchRequest tail) ----- //
app.MapFallback((HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.NotFound(ctx))));

app.Run();

// ----- local helpers (part of the top-level program) ----- //

// Read a value out of the "name=value" list sgcHTMXParseMessage fills.
static string GetParamValue(List<string> aParams, string aName)
{
    if (aParams == null)
        return "";
    for (int vI = 0; vI < aParams.Count; vI++)
    {
        string vLine = aParams[vI];
        if (vLine == null)
            continue;
        int vEq = vLine.IndexOf('=');
        if (vEq < 0)
            continue;
        if (string.Equals(vLine.Substring(0, vEq), aName,
            StringComparison.OrdinalIgnoreCase))
            return vLine.Substring(vEq + 1);
    }
    return "";
}

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

namespace Reports
{
    // The live push loop: every 900 ms advance the export jobs, every second
    // loop (about 2 s) add one Analytics chart point + one candle tick, and
    // broadcast the resulting htmx OOB fragments to every connected browser (the
    // bridge runs the scripts they carry). Mirror of TReportsServer.PushLoop,
    // driven by the hosted service lifetime instead of a background thread.
    public sealed class ReportsPushService : BackgroundService
    {
        private readonly ISgcHtmlHub FHub;
        private readonly ReportsWebHost FHost;

        public ReportsPushService(ISgcHtmlHub aHub, ReportsWebHost aHost)
        {
            FHub = aHub;
            FHost = aHost;
        }

        // The hub bounds every send (SgcHtmlOptions.SendTimeout) and drops a
        // browser that stopped reading (a page frozen in the back/forward cache,
        // a dead link), so a stuck client never blocks this loop.
        private Task BroadcastAsync(string aHtml, CancellationToken aStoppingToken)
        {
            return FHub.BroadcastAsync(aHtml, null, aStoppingToken);
        }

        protected override async Task ExecuteAsync(CancellationToken aStoppingToken)
        {
            int vTick = 0;
            while (!aStoppingToken.IsCancellationRequested)
            {
                vTick++;
                try
                {
                    if (vTick % 2 == 0)
                    {
                        string vLive = FHost.AnalyticsLiveFragment();
                        if (!string.IsNullOrEmpty(vLive))
                            await BroadcastAsync(vLive, aStoppingToken)
                                .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (aStoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception E)
                {
                    Console.WriteLine("[push] analytics error: " + E.Message);
                }

                try
                {
                    string vFragment = TReportsPages.LiveTick();
                    if (vFragment != "")
                        await BroadcastAsync(vFragment, aStoppingToken)
                            .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (aStoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception E)
                {
                    Console.WriteLine("[push] error: " + E.Message);
                }

                try
                {
                    await Task.Delay(900, aStoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
