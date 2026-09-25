// ***************************************************************************
//  sgcFieldWeb - field service management demo on ASP.NET Core
//  Mirror of demos\60.HTML\17.FieldService (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcField_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcField_Config.cs    (JSON config loader)
//    - sgcField_DB.cs        (Microsoft.Data.Sqlite schema + queries + seed)
//    - sgcField_Multipart.cs (upload validation + disk photo store)
//    - sgcField_Pages.cs     (component view layer: every page + fragment)
//    - sgcField_Passkeys.cs  (WebAuthn passkey engine bridged to the DB)
//    - sgcField_Sessions.cs  (in-memory cookie session store)
//    - sgcField_Types.cs     (domain records + the job state machine + config)
//
//  Only the hosting layer changes (following the 01.ERP / 13.Warehouse /
//  15.Reports host pattern):
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / Chart.js /
//      htmx assets (by file name, from the adapter's resource registry) and the
//      htmx WebSocket channel at /ws, which is the channel every hx-swap-oob
//      fragment arrives on.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim TFieldPages builder for every byte of HTML. The gate
//      is per-endpoint and mirrors the dispatcher's steps: public routes before
//      the gate (assets, /healthz, /theme, /login, /logout, the passkey login
//      pair and the whole customer portal under /track), then Guarded (step 3,
//      any signed-in session), GuardedDispatch (the Delphi RequireDispatch local
//      function: dispatcher or manager, everyone else gets the denied page) and
//      GuardedJson for the passkey registration pair that answers JSON.
//    - Cookies (field_session / field_theme) are read + written through the
//      native ASP.NET Core cookie API, with the SAME names + attributes.
//    - PASSKEYS: the RP id (request host name) + origin (scheme://host) are
//      DERIVED from the live request and threaded into the reused passkey engine
//      instead of the 60.HTML hard-coded localhost (FieldWebHost.GetPasskeys).
//    - REALTIME: the FieldPushService BackgroundService below replaces the
//      60.HTML TFieldPushThread. It calls the SAME SimulateTick on the same
//      3000 ms beat and broadcasts the fragments through the adapter's
//      ISgcHtmlHub, because under Kestrel the engine has no Server bound and its
//      own BroadcastFragment is a no-op.
//
//  The two host files that were NOT copied are sgcField_Server.cs (the
//  TsgcWebSocketHTTPServer host) and the console Program.cs.
//
//  IMPORTANT hosting note (the reusable pattern): a Minimal API lambda whose ONLY
//  parameter is HttpContext and which returns Task<IResult> is treated as a raw
//  RequestDelegate, so its IResult is DISCARDED (ASP0016) - the handler's cookie
//  side effects run but the redirect / status / body are lost. To avoid that trap
//  uniformly, every endpoint lambda below returns plain Task and the Run / RunForm
//  helpers execute the handler's IResult onto the response explicitly.
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
using FieldService;

var builder = WebApplication.CreateBuilder(args);

// Load the field-service config (dispatcher credentials + DB path) from
// sgcFieldServer.conf.json, tolerant of a missing file (defaults =>
// dispatch/dispatch). The listen section is ignored here; Kestrel owns the port
// (appsettings.json).
var oConfig = new TFieldServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory,
    "sgcFieldServer.conf.json");
if (File.Exists(vConfigPath))
    new TFieldConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is launched
// from. conf.json's "data\\field.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "field.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// The reused TFieldPhotoStore resolves data\photos\ against the CURRENT
// directory, so anchor that to the exe directory as the 60.HTML console host does
// (Directory.SetCurrentDirectory in its Main). The uploaded job photos then land
// next to the database whatever directory the app was launched from.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

// The host owns the reused singletons (DB pool, session store, page builder,
// photo store, per-origin passkey factory) and the world simulator. Constructed
// once at startup below; DI disposes it (and the DB pool) on shutdown.
builder.Services.AddSingleton(sp => new FieldWebHost(oConfig, vDbPath,
    sp.GetRequiredService<ISgcHtmlHub>()));

// The live push loop (mirror of the 60.HTML TFieldPushThread).
builder.Services.AddHostedService<FieldPushService>();

var app = builder.Build();

// Force construction now so the DB schema + dispatcher/demo seed run at startup
// (mirrors TFieldServer.InitRuntime), not lazily on the first request. The first
// run seeds ~900 jobs, so it takes a moment.
var host = app.Services.GetRequiredService<FieldWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves Bootstrap/Chart.js/htmx assets + manifest + sw + /ws

// ----- endpoint helpers (see the IMPORTANT hosting note above) ----- //

// Runs a synchronous IResult handler and writes its result to the response.
Task Run(HttpContext ctx, Func<IResult> handler)
{
    return handler().ExecuteAsync(ctx);
}

// Reads the urlencoded / multipart form (null when the request has no form body,
// so GetParam falls back to the query string only - matching the 60.HTML merge),
// runs the IResult handler and writes its result to the response.
async Task RunForm(HttpContext ctx, Func<IFormCollection, IResult> handler)
{
    IFormCollection form = ctx.Request.HasFormContentType
        ? await ctx.Request.ReadFormAsync()
        : null;
    await handler(form).ExecuteAsync(ctx);
}

// ----- static assets the adapter does not own ----- //
// The adapter claims *.js and *.css from its resource registry, which carries
// htmx.min.js, sgcHTMX.min.js, bootstrap and chart.js. It does NOT carry
// sgcWebSockets.js, which the realtime pages link, so that one is served here
// straight from the esegece.sgcHTML assembly manifest by its logical name.
app.MapGet("/favicon.svg", (HttpContext ctx) => Run(ctx, () => host.Favicon(ctx)));
app.MapGet("/sgcWebSockets.js", () =>
{
    string vBody = GetEmbeddedAsset("sgcWebSockets.js");
    return vBody == ""
        ? Results.NotFound()
        : Results.Content(vBody, "application/javascript; charset=utf-8");
});

// ----- public routes (before the 60.HTML auth gate) ----- //
// The dispatcher answers /healthz and /logout for ANY method.
app.MapMethods("/healthz", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.Healthz(ctx)));
app.MapPost("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));
app.MapGet("/login", (HttpContext ctx) => Run(ctx, () => host.LoginGet(ctx)));
app.MapPost("/login", (HttpContext ctx) => RunForm(ctx, f => host.LoginPost(ctx, f)));
app.MapMethods("/logout", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));

// ----- passkeys (WebAuthn, JSON in / JSON out) ----- //
// Sign-in is public; registration is behind the JSON auth gate (the dispatcher
// answers those two paths with a JSON 401 rather than the login redirect).
app.MapPost("/passkey/login/options",
    (HttpContext ctx) => Run(ctx, () => host.PasskeyLoginOptions(ctx)));
// The two verify endpoints call async host methods (Task<IResult>). Use a
// block-bodied async lambda that returns plain Task (assign the awaited IResult
// to a local, then ExecuteAsync) so nothing is discarded (ASP0016).
app.MapPost("/passkey/login/verify", async (HttpContext ctx) =>
{
    IResult vResult = await host.PasskeyLoginVerify(ctx);
    await vResult.ExecuteAsync(ctx);
});
app.MapPost("/passkey/register/options", (HttpContext ctx) => Run(ctx,
    () => host.GuardedJson(ctx, s => host.PasskeyRegisterOptions(ctx, s))));
app.MapPost("/passkey/register/verify", async (HttpContext ctx) =>
{
    IResult vResult = await host.PasskeyRegisterVerify(ctx);
    await vResult.ExecuteAsync(ctx);
});

// ----- customer portal (public: the unguessable job reference IS the
// credential, and it opens exactly one job and nothing around it) ----- //
app.MapGet("/track", (HttpContext ctx) => Run(ctx, () => host.TrackLookup(ctx, null,
    false)));
app.MapPost("/track", (HttpContext ctx) => RunForm(ctx,
    f => host.TrackLookup(ctx, f, true)));
// The literal /approve tail is matched before the bare reference, exactly as the
// dispatcher's EndsText check does.
app.MapPost("/track/{reference}/approve", (HttpContext ctx, string reference) =>
    RunForm(ctx, f => host.TrackApprove(ctx, f, reference ?? "")));
app.MapGet("/track/{reference}", (HttpContext ctx, string reference) =>
    Run(ctx, () => host.TrackGet(ctx, reference ?? "")));

// ----- technician (mobile) ----- //
app.MapGet("/my", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.MyJobsGet(ctx, s))));
app.MapGet("/my/chat/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.Guarded(ctx, s => host.ChatGet(ctx, s, id))));
app.MapPost("/my/chat/{id:long}", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.ChatPost(ctx, f, s, id))));
// {id:long} so a non-numeric segment falls through to the 404 tail, exactly as
// the dispatcher's TryStrToInt64 miss does.
app.MapGet("/my/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.Guarded(ctx, s => host.MyJobGet(ctx, s, id, ""))));
app.MapPost("/my/{id:long}/enroute", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.MyStatusPost(ctx, f, s, id,
        FieldConst.CS_JOB_ENROUTE))));
app.MapPost("/my/{id:long}/onsite", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.MyStatusPost(ctx, f, s, id,
        FieldConst.CS_JOB_ONSITE))));
app.MapPost("/my/{id:long}/complete", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.MyStatusPost(ctx, f, s, id,
        FieldConst.CS_JOB_COMPLETE))));
app.MapPost("/my/{id:long}/check", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.MyCheckPost(ctx, f, s, id))));
app.MapPost("/my/{id:long}/part", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.MyPartPost(ctx, f, s, id))));
app.MapPost("/my/{id:long}/photo", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.MyPhotoPost(ctx, f, s, id))));
app.MapPost("/my/{id:long}/sign", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.Guarded(ctx, s => host.MySignPost(ctx, f, s, id))));

// ----- root: the axis split (technician -> /my, customer -> /track, office ->
// the dispatch board) ----- //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.Root(ctx, s))));

// ----- dispatch board ----- //
app.MapGet("/board/fragment", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(
    ctx, s => host.BoardFragment(ctx))));
app.MapPost("/board/assign", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.BoardAssign(ctx, f, s, false))));
app.MapPost("/board/unassign", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.BoardAssign(ctx, f, s, true))));

// ----- gantt, map, calendar ----- //
app.MapGet("/gantt", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.GanttGet(ctx, s))));
app.MapPost("/gantt/move", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.GanttMove(ctx, f, s))));
app.MapGet("/map", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.MapGet(ctx, s))));
app.MapGet("/map/fragment", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(
    ctx, s => host.MapFragment(ctx))));
app.MapGet("/calendar", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.CalendarGet(ctx, s))));

// ----- jobs ----- //
app.MapGet("/jobs", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.JobsGet(ctx, s))));
app.MapGet("/jobs/new", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.JobNewGet(ctx, s, ""))));
app.MapPost("/jobs/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.JobSave(ctx, f, s))));
app.MapPost("/jobs/cancel", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.JobCancel(ctx, f, s))));
app.MapGet("/jobs/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedDispatch(ctx, s => host.JobDetailGet(ctx, s, id))));
app.MapPost("/jobs/{id:long}/status", (HttpContext ctx, long id) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.JobStatusPost(ctx, f, s, id))));
// The service report and the job photos belong to the JOB, not to the office: the
// technician who holds it reaches them too, so these two skip the office gate and
// CanActOnJob inside the handler is the real check (the dispatcher makes the same
// exception for 'report.pdf' and the 'photos/' prefix).
app.MapGet("/jobs/{id:long}/report.pdf", (HttpContext ctx, long id) => Run(ctx,
    () => host.Guarded(ctx, s => host.JobReportPDF(ctx, s, id))));
app.MapGet("/jobs/{id:long}/photos/{photo:long}",
    (HttpContext ctx, long id, long photo) => Run(ctx, () => host.Guarded(ctx,
        s => host.JobPhotoGet(ctx, s, id, photo))));

// ----- customers, sites, assets, parts ----- //
app.MapGet("/customers", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.CustomersGet(ctx, s))));
app.MapPost("/customers/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.CustomerSave(ctx, f, s))));
app.MapGet("/customers/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedDispatch(ctx, s => host.CustomerDetailGet(ctx, s, id))));
app.MapGet("/sites/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedDispatch(ctx, s => host.SiteDetailGet(ctx, s, id))));
app.MapGet("/assets", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.AssetsGet(ctx, s))));
app.MapGet("/assets/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedDispatch(ctx, s => host.AssetDetailGet(ctx, s, id))));
app.MapGet("/parts", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.PartsGet(ctx, s))));
app.MapPost("/parts/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.PartsSave(ctx, f, s))));

// ----- manager: reports, the no-REST page, users, audit ----- //
app.MapGet("/reports", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.ReportsGet(ctx, s))));
app.MapGet("/reports/sla.pdf", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(
    ctx, s => host.ReportsSlaPDF(ctx))));
app.MapGet("/reports/utilisation.xlsx", (HttpContext ctx) => Run(ctx,
    () => host.GuardedDispatch(ctx, s => host.ReportsUtilisationXLSX(ctx))));
app.MapGet("/sql", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.SqlGet(ctx, s))));
app.MapGet("/users", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.UsersGet(ctx, s, ""))));
app.MapPost("/users/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedDispatch(ctx, s => host.UsersSave(ctx, f, s))));
app.MapGet("/audit", (HttpContext ctx) => Run(ctx, () => host.GuardedDispatch(ctx,
    s => host.AuditGet(ctx, s))));

// ----- unknown protected path (mirrors the 60.HTML DispatchRequest tail) ----- //
// The auth gate runs first (signed out -> /login), then the styled 404 page.
app.MapFallback((HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.NotFound(ctx))));

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

namespace FieldService
{
    // The live push loop: every CS_PUSH_INTERVAL_MS advance the simulated world
    // and broadcast the resulting htmx OOB fragments to every connected browser.
    // Mirror of the 60.HTML TFieldPushThread, driven by the hosted service
    // lifetime instead of a background thread.
    public sealed class FieldPushService : BackgroundService
    {
        private readonly FieldWebHost FHost;

        public FieldPushService(FieldWebHost aHost)
        {
            FHost = aHost;
        }

        protected override async Task ExecuteAsync(CancellationToken aStoppingToken)
        {
            while (!aStoppingToken.IsCancellationRequested)
            {
                try
                {
                    string vFragment = FHost.SimulateTick();
                    if (vFragment != "")
                        FHost.PushFragment(vFragment);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception E)
                {
                    Console.WriteLine("[push] error: " + E.Message);
                }

                try
                {
                    await Task.Delay(FieldWebHost.CS_PUSH_INTERVAL_MS, aStoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
