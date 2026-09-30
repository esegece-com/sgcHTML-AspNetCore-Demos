// ***************************************************************************
//  sgcLiveMonitorWeb - Live Monitor demo on ASP.NET Core
//  Mirror of demos\60.HTML\03.LiveMonitor (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcLiveMonitor_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcLiveMonitor_Config.cs    (JSON config loader)
//    - sgcLiveMonitor_DB.cs        (Microsoft.Data.Sqlite schema + seed)
//    - sgcLiveMonitor_I18n.cs      (12-language string table)
//    - sgcLiveMonitor_Pages.cs     (node-layer view: login, dashboard, admin...)
//    - sgcLiveMonitor_Passkeys.cs  (WebAuthn passkey engine bridged to the DB)
//    - sgcLiveMonitor_Sessions.cs  (in-memory cookie session store)
//    - sgcLiveMonitor_Types.cs     (domain records + TERPServerConfig)
//
//  Only the hosting layer changes:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / Chart.js /
//      htmx / htmx-ext-ws assets (by file name, from the adapter's resource
//      registry) and OWN the htmx WebSocket channel at /ws.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim TERPPages builder for every byte of HTML. The auth
//      gate is per-endpoint (host.Guarded / GuardedAdmin / GuardedJson).
//    - Cookies (session / theme / language) are read + written through the
//      native ASP.NET Core cookie API, with the SAME names + attributes.
//    - PASSKEYS: the RP id (request host name) + origin (scheme://host) are
//      DERIVED from the live request and threaded into the reused passkey engine
//      instead of the 60.HTML hard-coded localhost (see LiveMonitorWebHost.GetPasskeys).
//
//  LIVE PUSH (the point of this demo): the 60.HTML host bound the HTMX engine to
//  its TsgcWebSocketHTTPServer and a System.Threading.Timer called
//  BroadcastFragment(BuildMetricsFragment()) every ~1500 ms. Under Kestrel the
//  engine has NO Server bound, so its BroadcastFragment is a NO-OP; push MUST go
//  through ISgcHtmlHub. The LiveMonitorPushService (a BackgroundService below)
//  replicates the timer: every ~1500 ms it builds the SAME live metric OOB
//  fragment bundle (host.BuildMetricsFragment) and calls hub.BroadcastAsync. The
//  authenticated dashboard page at '/' renders with hx-ext="ws" ws-connect="/ws"
//  and the matching OOB target ids (kpi-cpu / kpi-mem / kpi-conn / kpi-rps /
//  metric-cpu-val / events-body) so each pushed fragment swaps into place.
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using LiveMonitor;

var builder = WebApplication.CreateBuilder(args);

// Load the config (admin credentials + DB path) from sgcLiveMonitorServer.conf.json,
// tolerant of a missing file (defaults => admin/admin). The listen section is
// ignored here; Kestrel owns the port (appsettings.json).
var oConfig = new TERPServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory, "sgcLiveMonitorServer.conf.json");
if (File.Exists(vConfigPath))
    new TERPConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is launched
// from. conf.json's "data\\monitor.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "monitor.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// Kestrel owns the listen port (appsettings.json / --Kestrel:... cmdline). Parse
// it from configuration purely for the admin "server info" panel display.
int vPort = 8101;
string vKestrelUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
if (!string.IsNullOrEmpty(vKestrelUrl))
{
    try { vPort = new Uri(vKestrelUrl).Port; } catch { }
}

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
// The reused dashboard page connects ws-connect="/" (the root path the 60.HTML
// server accepted), so the adapter accepts the channel on any path.
builder.Services.AddSgcHtml(o =>
{
    o.ServeRootPage = false;
    o.AcceptWebSocketOnAnyPath = true;
});

// The host owns the reused singletons (DB pool, session store, page builder,
// per-origin passkey factory) + the live-metric state. It needs the hub both to
// report the live connection count on the dashboard and to build the metric
// fragment. Constructed once at startup below; DI disposes it (and the DB pool)
// on shutdown.
builder.Services.AddSingleton(sp => new LiveMonitorWebHost(oConfig, vDbPath, vPort,
    sp.GetRequiredService<ISgcHtmlHub>()));

// The live push loop (mirror of the 60.HTML System.Threading.Timer / PushTick).
builder.Services.AddHostedService<LiveMonitorPushService>();

var app = builder.Build();

// Force construction now so the DB schema + admin/demo seed run at startup
// (mirrors TsgcLiveMonitorServer.Start), not lazily on the first request.
var host = app.Services.GetRequiredService<LiveMonitorWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml

// Firewall gate (mirrors the 60.HTML DispatchRequest firewall check). A blocked
// IP gets a 403 unless it is on the ignored allow-list; loopback is never
// blocked. Runs before assets + routing, exactly like the managed host.
app.Use(async (ctx, next) =>
{
    if (host.IsRequestBlocked(ctx))
    {
        await host.ForbiddenFirewall(ctx).ExecuteAsync(ctx);
        return;
    }
    await next();
});

app.UseSgcHtml();      // serves Bootstrap/Chart.js/htmx assets + manifest + sw + /ws

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

// ----- static asset the page template links but the adapter lacks ----- //
// The adapter serves the built-in *.js / *.css by file name; the custom favicon
// is served here (matches the 60.HTML TryServeStaticAsset favicon branch).
app.MapGet("/favicon.svg", (HttpContext ctx) => Run(ctx, () =>
{
    ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
    return Results.Content(LiveMonitorWebHost.CS_FAVICON_SVG, "image/svg+xml");
}));

// Health check (public).
app.MapGet("/healthz", () => Results.Text("ok", "text/plain; charset=utf-8"));

// ----- public auth / theme / language ----- //
app.MapGet("/login", (HttpContext ctx) => Run(ctx, () => host.LoginGet(ctx)));
app.MapPost("/login", (HttpContext ctx) => RunForm(ctx, f => host.LoginPost(ctx, f)));
app.MapGet("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapPost("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapPost("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));
app.MapPost("/lang", (HttpContext ctx) => RunForm(ctx, f => host.SetLanguage(ctx, f)));

// ----- passkey (WebAuthn) ----- //
app.MapPost("/passkey/login/options", (HttpContext ctx) => Run(ctx, () => host.PasskeyLoginOptions(ctx)));
// The two verify endpoints call async host methods (Task<IResult>). Use a
// block-bodied async lambda that returns plain Task (assign the awaited IResult
// to a local, then ExecuteAsync) so nothing is discarded (ASP0016).
app.MapPost("/passkey/login/verify", async (HttpContext ctx) =>
{
    IResult vResult = await host.PasskeyLoginVerify(ctx);
    await vResult.ExecuteAsync(ctx);
});
app.MapPost("/passkey/register/options",
    (HttpContext ctx) => Run(ctx, () => host.GuardedJson(ctx, s => host.PasskeyRegisterOptions(ctx, s))));
app.MapPost("/passkey/register/verify", async (HttpContext ctx) =>
{
    IResult vResult = await host.PasskeyRegisterVerifyGuarded(ctx);
    await vResult.ExecuteAsync(ctx);
});

// ----- security / passkeys management (logged-in) ----- //
app.MapGet("/security", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.SecurityGet(ctx, s))));
app.MapPost("/security/passkey/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.SecurityPasskeyDelete(ctx, f, s))));

// ----- dashboard (root) - the live monitoring page ----- //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.DashboardGet(ctx, s))));

// ----- users management (admin only) ----- //
app.MapGet("/users", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.UsersGet(ctx, s))));
app.MapGet("/users/new", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.UserNewGet(ctx, s))));
app.MapGet("/users/edit", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.UserEditGet(ctx, s))));
app.MapPost("/users/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.UserSavePost(ctx, f, s))));
app.MapPost("/users/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.UserDeletePost(ctx, f, s))));

// ----- admin section (admin only) ----- //
app.MapGet("/admin", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.AdminSettingsGet(ctx, s))));
app.MapPost("/admin/settings",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.AdminSettingsPost(ctx, f, s))));
app.MapGet("/admin/firewall", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.AdminFirewallGet(ctx, s))));
app.MapPost("/admin/firewall/block",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.AdminFirewallAdd(ctx, f, true))));
app.MapPost("/admin/firewall/unblock",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.AdminFirewallRemove(ctx, f, true))));
app.MapPost("/admin/firewall/ignore",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.AdminFirewallAdd(ctx, f, false))));
app.MapPost("/admin/firewall/unignore",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.AdminFirewallRemove(ctx, f, false))));
app.MapGet("/admin/audit", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.AdminAuditGet(ctx, s))));

app.Run();

namespace LiveMonitor
{
    // The live push loop: every ~1500 ms advance the simulated metrics and
    // broadcast the resulting htmx OOB fragment bundle to every connected browser.
    // Mirror of TsgcLiveMonitorServer.PushTick (System.Threading.Timer with
    // dueTime 1500 + period 1500), driven by the hosted service lifetime instead
    // of a background timer. host.BuildMetricsFragment builds the SAME bundle the
    // 60.HTML host broadcast; the hub is the Kestrel push channel (the engine's
    // Server-bound BroadcastFragment is a no-op here).
    public sealed class LiveMonitorPushService : Microsoft.Extensions.Hosting.BackgroundService
    {
        private const int CS_TICK_MS = 1500;

        private readonly LiveMonitorWebHost FHost;
        private readonly ISgcHtmlHub FHub;

        public LiveMonitorPushService(LiveMonitorWebHost aHost, ISgcHtmlHub aHub)
        {
            FHost = aHost;
            FHub = aHub;
        }

        protected override async Task ExecuteAsync(CancellationToken aStoppingToken)
        {
            // Match the 60.HTML timer's dueTime: first tick after ~1500 ms, then
            // every ~1500 ms.
            while (!aStoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(CS_TICK_MS, aStoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    string vFragment = FHost.BuildMetricsFragment();
                    if (vFragment != "")
                        await FHub.BroadcastAsync(vFragment, null, aStoppingToken)
                            .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception E)
                {
                    Console.WriteLine("[push] error: " + E.Message);
                }
            }
        }
    }
}
