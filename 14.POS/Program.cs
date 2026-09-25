// ***************************************************************************
//  sgcPOSWeb - retail point-of-sale web-app demo on ASP.NET Core
//  Mirror of demos\60.HTML\14.POS (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcPOS_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcPOS_Config.cs    (JSON config loader)
//    - sgcPOS_DB.cs        (Microsoft.Data.Sqlite schema + CRUD + seed)
//    - sgcPOS_Pages.cs     (node-layer view: every page + htmx fragment)
//    - sgcPOS_Passkeys.cs  (WebAuthn passkey engine bridged to the DB)
//    - sgcPOS_Sessions.cs  (in-memory cookie session store)
//    - sgcPOS_Types.cs     (domain records + TPOSServerConfig)
//
//  Only the hosting layer changes (following the 01.ERP host pattern):
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / Chart.js /
//      htmx assets (by file name, from the adapter's resource registry) and the
//      htmx WebSocket channel, keeping the hosting pattern uniform across the
//      61.HTML.AspNetCore demos. The adapter claims *.js and *.css only, so the
//      till's own /favicon.svg and /promo/<id>.svg routes below still run.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim TPOSPages builder for every byte of HTML. The auth
//      gate is per-endpoint (host.Guarded / GuardedBackOffice / GuardedAdmin).
//    - Cookies (pos_session / pos_theme) are read and written through the
//      native ASP.NET Core cookie API, with the SAME names and attributes.
//    - PASSKEYS: the RP id (request host name) and origin (scheme://host) are
//      DERIVED from the live request and threaded into the reused passkey
//      engine instead of the 60.HTML hard-coded localhost (POSWebHost.GetPasskeys).
//
//  IMPORTANT hosting note (the reusable pattern): a Minimal API lambda whose
//  ONLY parameter is HttpContext and which returns Task<IResult> is treated as
//  a raw RequestDelegate, so its IResult is DISCARDED (ASP0016) - the handler's
//  cookie side effects run but the redirect / status / body are lost. To avoid
//  that trap uniformly, every endpoint lambda below returns plain Task and the
//  Run / RunForm helpers execute the handler's IResult onto the response.
// ***************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using POS;

var builder = WebApplication.CreateBuilder(args);

// Load the POS config (admin credentials + DB path + store details + the
// discount PIN threshold) from sgcPOSServer.conf.json, tolerant of a missing
// file (defaults => admin/admin). The listen section is ignored here; Kestrel
// owns the port (appsettings.json).
var oConfig = new TPOSServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory, "sgcPOSServer.conf.json");
if (File.Exists(vConfigPath))
    new TPOSConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is
// launched from. conf.json's "data\\pos.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "pos.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// Kestrel owns the listen port (appsettings.json / --Kestrel:... cmdline).
// Parse it from configuration purely to print the startup banner.
int vPort = 8103;
string vKestrelUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
if (!string.IsNullOrEmpty(vKestrelUrl))
{
    try { vPort = new Uri(vKestrelUrl).Port; } catch { }
}

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

// The host owns the reused singletons (DB pool, session store, page builder,
// per-origin passkey factory). Constructed once at startup below; DI disposes
// it (and the DB pool) on shutdown.
builder.Services.AddSingleton(sp => new POSWebHost(oConfig, vDbPath, vPort));

var app = builder.Build();

// Force construction now so the DB schema + admin/demo seed run at startup
// (mirrors TPOSServer.Start), not lazily on the first request.
var host = app.Services.GetRequiredService<POSWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves Bootstrap/Chart.js/htmx assets + manifest + sw + /ws

// ----- endpoint helpers (see the IMPORTANT hosting note above) ----- //

// Runs a synchronous IResult handler and writes its result to the response.
Task Run(HttpContext ctx, Func<IResult> handler)
{
    return handler().ExecuteAsync(ctx);
}

// Reads the urlencoded form (null when the request has no form body, so
// GetParam falls back to the query string only, matching the 60.HTML merge),
// runs the IResult handler and writes its result to the response. Also used for
// the GET side of the any-verb routes (/theme, /till/scan), where the form is
// simply absent.
async Task RunForm(HttpContext ctx, Func<IFormCollection, IResult> handler)
{
    IFormCollection form = ctx.Request.HasFormContentType
        ? await ctx.Request.ReadFormAsync()
        : null;
    await handler(form).ExecuteAsync(ctx);
}

// ----- public: health + generated assets ----- //
app.MapGet("/healthz", (HttpContext ctx) => Run(ctx, () => host.Healthz()));
app.MapGet("/favicon.svg", (HttpContext ctx) => Run(ctx, () => host.FaviconSVG(ctx)));
// Promotion artwork, generated on the fly so the till needs no CDN.
app.MapGet("/promo/{id}.svg", (HttpContext ctx, string id) =>
    Run(ctx, () => host.PromoSVG(ctx, id)));

// ----- public: theme, login, logout ----- //
// The navbar dropdown links to GET /theme?set=..., a form posts "theme". The
// 60.HTML host answers /theme on any verb, so both are mapped.
app.MapGet("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));
app.MapPost("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));
app.MapGet("/login", (HttpContext ctx) => Run(ctx, () => host.LoginGet(ctx)));
app.MapPost("/login", (HttpContext ctx) => RunForm(ctx, f => host.LoginPost(ctx, f)));
app.MapGet("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapPost("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));

// ----- public: passkey sign-in (WebAuthn, JSON in / JSON out) ----- //
app.MapPost("/passkey/login/options",
    (HttpContext ctx) => Run(ctx, () => host.PasskeyLoginOptions(ctx)));
// The verify endpoint calls an async host method (Task<IResult>). Use a
// block-bodied async lambda that returns plain Task (assign the awaited IResult
// to a local, then ExecuteAsync) so nothing is discarded (ASP0016).
app.MapPost("/passkey/login/verify", async (HttpContext ctx) =>
{
    IResult vResult = await host.PasskeyLoginVerify(ctx);
    await vResult.ExecuteAsync(ctx);
});

// ----- the till (session required) ----- //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TillGet(ctx, s))));
app.MapGet("/till/category/{id}", (HttpContext ctx, string id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.TillCategory(id))));
app.MapGet("/till/search",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TillSearch(ctx))));
app.MapPost("/till/add",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillAdd(ctx, f, s))));
app.MapPost("/till/qty",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillQty(ctx, f, s))));
app.MapPost("/till/remove",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillRemove(ctx, f, s))));
app.MapPost("/till/discount",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillDiscount(ctx, f, s))));
app.MapPost("/till/customer",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillCustomer(ctx, f, s))));
// The camera scanner posts, the manual-entry box can arrive as a GET. The
// 60.HTML host answers /till/scan on any verb, so both are mapped.
app.MapGet("/till/scan",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillScan(ctx, f, s))));
app.MapPost("/till/scan",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillScan(ctx, f, s))));
app.MapPost("/till/park",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TillPark(ctx, s))));
app.MapGet("/till/parked",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TillParked(ctx, s))));
app.MapPost("/till/recall",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillRecall(ctx, f, s))));
app.MapGet("/till/pay",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TillPayGet(ctx, s))));
app.MapPost("/till/pay",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TillPayPost(ctx, f, s))));
// The ".pdf" route is a complex segment, so it outranks the bare "{id}" one for
// "/till/receipt/12.pdf" and never competes for "/till/receipt/12".
app.MapGet("/till/receipt/{id}", (HttpContext ctx, string id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.Receipt(ctx, s, id))));
app.MapGet("/till/receipt/{id}.pdf", (HttpContext ctx, string id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.ReceiptPDF(s, id))));
app.MapPost("/till/refund/{id}", (HttpContext ctx, string id) =>
    RunForm(ctx, f => host.Guarded(ctx, s => host.Refund(ctx, f, s, id))));

// ----- shift (session required) ----- //
app.MapGet("/shift",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.ShiftGet(ctx, s))));
app.MapPost("/shift/open",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.ShiftOpen(ctx, f, s))));
app.MapPost("/shift/close",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.ShiftClose(ctx, f, s))));
app.MapGet("/shift/{id}/xreport.pdf", (HttpContext ctx, string id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.ShiftReportPDF(s, id, false))));
app.MapGet("/shift/{id}/zreport.pdf", (HttpContext ctx, string id) =>
    Run(ctx, () => host.Guarded(ctx, s => host.ShiftReportPDF(s, id, true))));

// ----- back office: manager and admin only ----- //
app.MapGet("/products",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.Products(ctx, s))));
app.MapGet("/products/form",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.ProductForm(ctx))));
app.MapPost("/products/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedBackOffice(ctx, s => host.ProductSave(ctx, f, s))));
app.MapPost("/products/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedBackOffice(ctx, s => host.ProductDelete(ctx, f, s))));
app.MapGet("/products/export.xlsx",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.ProductsExport(false))));
app.MapGet("/products/export.pdf",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.ProductsExport(true))));

app.MapGet("/categories",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.Categories(ctx, s))));
app.MapPost("/categories/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedBackOffice(ctx, s => host.CategorySave(ctx, f, s))));

app.MapGet("/promotions",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.Promotions(ctx, s))));
app.MapPost("/promotions/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedBackOffice(ctx, s => host.PromotionSave(ctx, f, s))));

app.MapGet("/customers",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.Customers(ctx, s))));
app.MapPost("/customers/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedBackOffice(ctx, s => host.CustomerSave(ctx, f, s))));
// Literal segments outrank the "{id}" parameter, so "/customers/save" above is
// never captured here.
app.MapGet("/customers/{id}", (HttpContext ctx, string id) =>
    Run(ctx, () => host.GuardedBackOffice(ctx, s => host.Customer(ctx, s, id))));

app.MapGet("/dashboard",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.Dashboard(ctx, s))));
app.MapGet("/reports",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.Reports(ctx, s))));
app.MapGet("/reports/sales.pdf",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.SalesReport(ctx, true))));
app.MapGet("/reports/sales.xlsx",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.SalesReport(ctx, false))));
app.MapGet("/sql",
    (HttpContext ctx) => Run(ctx, () => host.GuardedBackOffice(ctx, s => host.SQL(ctx, s))));

// ----- admin only ----- //
app.MapGet("/users",
    (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.Users(ctx, s))));
app.MapPost("/users/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedAdmin(ctx, s => host.UserSave(ctx, f, s))));
app.MapGet("/audit",
    (HttpContext ctx) => Run(ctx, () => host.GuardedAdmin(ctx, s => host.AuditGet(ctx, s))));

// Anything else: the till's own 404 page for a signed-in caller, a redirect to
// /login for a signed-out one, exactly like the 60.HTML dispatch.
app.MapFallback((HttpContext ctx) =>
    Run(ctx, () => host.Guarded(ctx, s => host.NotFoundPage(ctx, s))));

Console.WriteLine("sgcPOSWeb v" + POSWebHost.CS_POS_SERVER_VERSION +
    " - the till is on http://localhost:" +
    host.ListenPort.ToString(CultureInfo.InvariantCulture) + "/");
Console.WriteLine("Accounts: admin/admin, manager/manager, cashier/cashier.");
Console.WriteLine("Manager PIN 1379, admin PIN 4242.");

app.Run();
