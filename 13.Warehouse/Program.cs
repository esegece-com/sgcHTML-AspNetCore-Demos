// ***************************************************************************
//  sgcWMSWeb - warehouse management demo on ASP.NET Core
//  Mirror of demos\60.HTML\13.Warehouse (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcWMS_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcWMS_Config.cs    (JSON config loader)
//    - sgcWMS_DB.cs        (Microsoft.Data.Sqlite schema + queries + seed)
//    - sgcWMS_Handheld.cs  (node-layer handheld scanner screens)
//    - sgcWMS_Multipart.cs (upload validation + disk storage)
//    - sgcWMS_Pages.cs     (node-layer view: every back-office page + fragment)
//    - sgcWMS_Passkeys.cs  (WebAuthn passkey engine bridged to the DB)
//    - sgcWMS_Reports.cs   (XLSX / PDF exports, packing slip, labels)
//    - sgcWMS_Sessions.cs  (in-memory cookie session store)
//    - sgcWMS_Types.cs     (domain records + TWMSServerConfig)
//
//  Only the hosting layer changes (following the 01.ERP host pattern):
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / Chart.js /
//      htmx assets (by file name, from the adapter's resource registry) and the
//      htmx WebSocket channel, keeping the hosting pattern uniform across the
//      61.HTML.AspNetCore demos.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim TWMSPages / TWMSHandheld / TWMSReports builders for
//      every byte of output. The auth gate is per-endpoint: Signed (any role,
//      the handheld + /security), Guarded (admin or supervisor, the back
//      office) and GuardedAdmin (/users*, /audit), matching the 60.HTML
//      dispatcher's step 3 / step 5 / step 6 gates.
//    - Cookies (session / theme) are read + written through the native
//      ASP.NET Core cookie API, with the SAME names + attributes.
//    - PASSKEYS: the RP id (request host name) + origin (scheme://host) are
//      DERIVED from the live request and threaded into the reused passkey engine
//      instead of the 60.HTML hard-coded localhost (see WMSWebHost.GetPasskeys).
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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using WMS;

var builder = WebApplication.CreateBuilder(args);

// Load the WMS config (admin credentials + DB path + company name) from
// sgcWMSServer.conf.json, tolerant of a missing file (defaults => admin/admin).
// The listen section is ignored here; Kestrel owns the port (appsettings.json).
var oConfig = new TWMSServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory, "sgcWMSServer.conf.json");
if (File.Exists(vConfigPath))
    new TWMSConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is launched
// from. conf.json's "data\\wms.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "wms.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// Kestrel owns the listen port (appsettings.json / --Kestrel:... cmdline). Parse
// it from configuration purely for display.
int vPort = 8102;
string vKestrelUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
if (!string.IsNullOrEmpty(vKestrelUrl))
{
    try { vPort = new Uri(vKestrelUrl).Port; } catch { }
}

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

// The host owns the reused singletons (DB pool, session store, page / handheld /
// report builders, photo store, per-origin passkey factory). Constructed once at
// startup below; DI disposes it (and the DB pool) on shutdown.
builder.Services.AddSingleton(sp => new WMSWebHost(oConfig, vDbPath, vPort));

var app = builder.Build();

// Force construction now so the DB schema + admin/demo seed run at startup
// (mirrors TWMSServer.InitRuntime), not lazily on the first request.
var host = app.Services.GetRequiredService<WMSWebHost>();

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

// ----- static asset the adapter does not own ----- //
app.MapGet("/favicon.svg", (HttpContext ctx) => Run(ctx, () => host.Favicon(ctx)));

// Health check (public).
app.MapGet("/healthz", () => Results.Text("ok", "text/plain; charset=utf-8"));

// ----- public auth / theme ----- //
app.MapGet("/login", (HttpContext ctx) => Run(ctx, () => host.LoginGet(ctx)));
app.MapPost("/login", (HttpContext ctx) => RunForm(ctx, f => host.LoginPost(ctx, f)));
app.MapGet("/register", (HttpContext ctx) => Run(ctx, () => host.RegisterGet(ctx)));
app.MapPost("/register", (HttpContext ctx) => RunForm(ctx, f => host.RegisterPost(ctx, f)));
// The 60.HTML dispatcher answers /logout for ANY method.
app.MapMethods("/logout", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapPost("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));

// ----- passkey (WebAuthn) ----- //
// The 60.HTML dispatcher answers these for ANY method; the browser uses POST.
app.MapMethods("/passkey/login/options", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.PasskeyLoginOptions(ctx)));
// The two verify endpoints call async host methods (Task<IResult>). Use a
// block-bodied async lambda that returns plain Task (assign the awaited IResult
// to a local, then ExecuteAsync) so nothing is discarded (ASP0016).
app.MapMethods("/passkey/login/verify", new[] { "GET", "POST" }, async (HttpContext ctx) =>
{
    IResult vResult = await host.PasskeyLoginVerify(ctx);
    await vResult.ExecuteAsync(ctx);
});
app.MapMethods("/passkey/register/options", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.GuardedJson(ctx,
        s => host.PasskeyRegisterOptions(ctx, s))));
app.MapMethods("/passkey/register/verify", new[] { "GET", "POST" }, async (HttpContext ctx) =>
{
    IResult vResult = await host.PasskeyRegisterVerifyGuarded(ctx);
    await vResult.ExecuteAsync(ctx);
});

// ----- security / passkeys management (any signed-in role) ----- //
app.MapGet("/security", (HttpContext ctx) => Run(ctx, () => host.Signed(ctx,
    s => host.SecurityGet(ctx, s))));

// ----- handheld, open to all three roles ----- //
// The 60.HTML dispatcher strips a trailing slash before routing, so it answered
// both '/hh' and '/hh/'. ASP.NET Core routing normalizes the trailing slash to
// the SAME template, so mapping both would be an AmbiguousMatchException - the
// single '/hh' mapping already serves '/hh/' as well.
app.MapGet("/hh", (HttpContext ctx) => Run(ctx, () => host.Signed(ctx,
    s => host.HandheldHome(ctx, s))));
app.MapGet("/hh/receive", (HttpContext ctx) => Run(ctx, () => host.Signed(ctx,
    s => host.HandheldReceiveGet(ctx, s))));
app.MapPost("/hh/receive", (HttpContext ctx) => RunForm(ctx, f => host.Signed(ctx,
    s => host.HandheldReceivePost(ctx, f, s))));
app.MapGet("/hh/pick", (HttpContext ctx) => Run(ctx, () => host.Signed(ctx,
    s => host.HandheldPickOrders(ctx, s))));
app.MapPost("/hh/pick/confirm", (HttpContext ctx) => RunForm(ctx, f => host.Signed(ctx,
    s => host.HandheldPickConfirm(ctx, f, s))));
app.MapGet("/hh/pick/{id:long}", (HttpContext ctx, long id) => Run(ctx, () => host.Signed(ctx,
    s => host.HandheldPickOrder(ctx, s, id))));
app.MapGet("/hh/count", (HttpContext ctx) => Run(ctx, () => host.Signed(ctx,
    s => host.HandheldCountGet(ctx, s))));
app.MapPost("/hh/count", (HttpContext ctx) => RunForm(ctx, f => host.Signed(ctx,
    s => host.HandheldCountPost(ctx, f, s))));
app.MapMethods("/hh/lookup", new[] { "GET", "POST" },
    (HttpContext ctx) => RunForm(ctx, f => host.Signed(ctx,
        s => host.HandheldLookup(ctx, f, s))));

// ----- dashboard (root) ----- //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.DashboardGet(ctx, s))));
app.MapGet("/dashboard/heatmap", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.HeatmapFragment(ctx))));

// ----- products ----- //
app.MapGet("/products", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.ProductsGet(ctx, s))));
app.MapGet("/products/form", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.ProductFormFragment(ctx))));
app.MapGet("/products/export.xlsx", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.ProductsExportXLSX(ctx))));
app.MapGet("/products/export.pdf", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.ProductsExportPDF(ctx))));
app.MapPost("/products/save", (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx,
    s => host.ProductSavePost(ctx, f, s))));
app.MapPost("/products/delete", (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx,
    s => host.ProductDeletePost(ctx, f, s))));
app.MapGet("/products/{id:long}", (HttpContext ctx, long id) => Run(ctx, () => host.Guarded(ctx,
    s => host.ProductDetailGet(ctx, s, id))));

// ----- locations ----- //
app.MapGet("/locations", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.LocationsGet(ctx, s))));
app.MapGet("/locations/{id:long}", (HttpContext ctx, long id) => Run(ctx, () => host.Guarded(ctx,
    s => host.LocationDetailGet(ctx, s, id))));

// ----- inbound (purchase orders) ----- //
app.MapGet("/inbound", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.InboundListGet(ctx, s))));
app.MapGet("/inbound/{id:long}", (HttpContext ctx, long id) => Run(ctx, () => host.Guarded(ctx,
    s => host.InboundDetailGet(ctx, s, id))));
app.MapPost("/inbound/{id:long}/receive", (HttpContext ctx, long id) =>
    RunForm(ctx, f => host.Guarded(ctx, s => host.InboundReceivePost(ctx, f, s, id))));
app.MapPost("/inbound/{id:long}/photo", (HttpContext ctx, long id) =>
    RunForm(ctx, f => host.Guarded(ctx, s => host.InboundPhotoPost(ctx, f, s, id))));
app.MapGet("/inbound/{id:long}/labels.pdf", (HttpContext ctx, long id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.InboundLabelsPDF(ctx, id))));

// ----- outbound (sales orders) ----- //
app.MapGet("/outbound", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.OutboundListGet(ctx, s))));
app.MapGet("/outbound/{id:long}", (HttpContext ctx, long id) => Run(ctx, () => host.Guarded(ctx,
    s => host.OutboundDetailGet(ctx, s, id))));
app.MapPost("/outbound/{id:long}/pick", (HttpContext ctx, long id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.OutboundPickPost(ctx, s, id))));
app.MapPost("/outbound/{id:long}/pack", (HttpContext ctx, long id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.OutboundPackPost(ctx, s, id))));
app.MapPost("/outbound/{id:long}/ship", (HttpContext ctx, long id) =>
    RunForm(ctx, f => host.Guarded(ctx, s => host.OutboundShipPost(ctx, f, s, id))));
app.MapGet("/outbound/{id:long}/packingslip.pdf", (HttpContext ctx, long id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.OutboundPackingSlipPDF(ctx, id))));

// ----- stock ----- //
app.MapGet("/stock", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.StockGet(ctx, s))));
app.MapGet("/stock/movements", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.MovementsGet(ctx, s))));
app.MapPost("/stock/adjust", (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx,
    s => host.StockAdjustPost(ctx, f, s))));

// ----- cycle counts ----- //
app.MapGet("/counts", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.CountsGet(ctx, s))));
app.MapPost("/counts/new", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.CountNewPost(ctx, s))));
app.MapGet("/counts/{id:long}", (HttpContext ctx, long id) => Run(ctx, () => host.Guarded(ctx,
    s => host.CountDetailGet(ctx, s, id))));
app.MapPost("/counts/{id:long}/line", (HttpContext ctx, long id) =>
    RunForm(ctx, f => host.Guarded(ctx, s => host.CountLinePost(ctx, f, s, id))));
app.MapPost("/counts/{id:long}/close", (HttpContext ctx, long id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.CountClosePost(ctx, s, id))));

// ----- reports ----- //
app.MapGet("/reports", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.ReportsGet(ctx, s))));
app.MapGet("/reports/valuation.pdf", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.ValuationPDF(ctx))));
app.MapGet("/reports/valuation.xlsx", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.ValuationXLSX(ctx))));

// ----- the no-REST page ----- //
app.MapGet("/sql", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.SQLGet(ctx, s))));

// ----- users + audit (admin only) ----- //
app.MapGet("/users", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx,
    s => host.UsersGet(ctx, s))));
app.MapPost("/users/save", (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx,
    s => host.UserSavePost(ctx, f, s))));
app.MapPost("/users/delete", (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx,
    s => host.UserDeletePost(ctx, f, s))));
app.MapGet("/audit", (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx,
    s => host.AuditGet(ctx, s))));

// ----- 404 / 403 (mirrors the 60.HTML dispatcher's final steps) ----- //
app.MapFallback((HttpContext ctx) => Run(ctx, () => host.NotFound(ctx)));

app.Run();
