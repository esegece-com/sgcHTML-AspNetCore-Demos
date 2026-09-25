// ***************************************************************************
//  sgcPortalWeb - Customer Portal web-app demo on ASP.NET Core
//  Mirror of demos\60.HTML\04.Portal (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcPortal_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcPortal_Config.cs    (JSON config loader)
//    - sgcPortal_DB.cs        (Microsoft.Data.Sqlite schema + CRUD + seed)
//    - sgcPortal_I18n.cs      (12-language string table)
//    - sgcPortal_Pages.cs     (node-layer view: every page + htmx fragment)
//    - sgcPortal_Passkeys.cs  (WebAuthn passkey engine bridged to the DB)
//    - sgcPortal_Sessions.cs  (in-memory cookie session store)
//    - sgcPortal_Types.cs     (domain records + TERPServerConfig)
//
//  Only the hosting layer changes (following the 01.ERP / 02.AdminCRUD pattern):
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / Chart.js /
//      htmx assets (by file name, from the adapter's resource registry) and the
//      htmx WebSocket channel, keeping the hosting pattern uniform across the
//      61.HTML.AspNetCore demos.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim TERPPages builder for every byte of HTML.
//    - Cookies (session / theme / language) are read + written through the
//      native ASP.NET Core cookie API, with the SAME names + attributes.
//    - PASSKEYS: the RP id (request host name) + origin (scheme://host) are
//      DERIVED from the live request and threaded into the reused passkey engine
//      instead of the 60.HTML hard-coded localhost (see PortalWebHost.GetPasskeys).
//
//  PORTAL-SPECIFIC (the dual-area role model). The auth gate is per-endpoint and
//  role-aware, reproducing the 60.HTML DispatchRequest role branch:
//    - Guarded         : any signed-in role (customers included) -> /security +
//                        passkey management.
//    - GuardedCustomer : signed-in AND role == 'customer' (else redirect '/') ->
//                        the self-service area (orders / profile / support).
//    - GuardedStaff    : signed-in AND role != 'customer' (else 403) -> the
//                        back-office CRUD (customers / providers / products /
//                        invoices / reports).
//    - GuardedAdmin    : signed-in AND role == 'admin' (else 403) -> users +
//                        admin settings / firewall / audit.
//  The root '/' is role-branched (host.RootGet): a customer sees its account
//  dashboard, staff see the back-office dashboard.
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
using Portal;

var builder = WebApplication.CreateBuilder(args);

// Load the Portal config (admin credentials + DB path + default language) from
// sgcPortalServer.conf.json, tolerant of a missing file (defaults => admin/admin).
// The listen section is ignored here; Kestrel owns the port (appsettings.json).
var oConfig = new TERPServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory, "sgcPortalServer.conf.json");
if (File.Exists(vConfigPath))
    new TERPConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is launched
// from. conf.json's "data\\portal.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "portal.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// Kestrel owns the listen port (appsettings.json / --Kestrel:... cmdline). Parse
// it from configuration purely for the admin "server info" panel display.
int vPort = 8099;
string vKestrelUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
if (!string.IsNullOrEmpty(vKestrelUrl))
{
    try { vPort = new Uri(vKestrelUrl).Port; } catch { }
}

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

// The host owns the reused singletons (DB pool, session store, page builder,
// per-origin passkey factory). Constructed once at startup below; DI disposes it
// (and the DB pool) on shutdown.
builder.Services.AddSingleton(sp => new PortalWebHost(oConfig, vDbPath, vPort));

var app = builder.Build();

// Force construction now so the DB schema + admin/demo/customer seed run at
// startup (mirrors TsgcPortalServer.Start), not lazily on the first request.
var host = app.Services.GetRequiredService<PortalWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml

// Firewall gate (mirrors the 60.HTML DispatchRequest firewall check). A blocked
// IP gets a 403 unless it is on the ignored allow-list; loopback is never
// blocked. Runs before assets + routing, exactly like the Delphi/managed host.
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
    return Results.Content(PortalWebHost.CS_FAVICON_SVG, "image/svg+xml");
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

// ----- security / passkeys management (any signed-in role, customers included) - //
app.MapGet("/security", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.SecurityGet(ctx, s))));
app.MapPost("/security/passkey/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.SecurityPasskeyDelete(ctx, f, s))));

// ----- root (role-branched: customer -> account dashboard, staff -> back-office) //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.RootGet(ctx)));

// ----- customer self-service area (role 'customer' only) ----- //
app.MapGet("/orders", (HttpContext ctx) => Run(ctx, () => host.GuardedCustomer(ctx, s => host.MyOrdersGet(ctx, s))));
app.MapGet("/orders/view", (HttpContext ctx) => Run(ctx, () => host.GuardedCustomer(ctx, s => host.OrderDetailGet(ctx, s))));
app.MapGet("/profile", (HttpContext ctx) => Run(ctx, () => host.GuardedCustomer(ctx, s => host.ProfileGet(ctx, s))));
app.MapPost("/profile/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedCustomer(ctx, s => host.ProfileSavePost(ctx, f, s))));
app.MapGet("/support", (HttpContext ctx) => Run(ctx, () => host.GuardedCustomer(ctx, s => host.SupportGet(ctx, s))));

// ----- customers CRUD (staff) ----- //
app.MapGet("/customers", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.CustomersGet(ctx, s))));
app.MapGet("/customers/new", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.CustomerNewGet(ctx, s))));
app.MapGet("/customers/edit", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.CustomerEditGet(ctx, s))));
app.MapPost("/customers/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.CustomerSavePost(ctx, f, s))));
app.MapPost("/customers/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.CustomerDeletePost(ctx, f))));

// ----- providers CRUD (staff) ----- //
app.MapGet("/providers", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.ProvidersGet(ctx, s))));
app.MapGet("/providers/new", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.ProviderNewGet(ctx, s))));
app.MapGet("/providers/edit", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.ProviderEditGet(ctx, s))));
app.MapPost("/providers/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.ProviderSavePost(ctx, f, s))));
app.MapPost("/providers/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.ProviderDeletePost(ctx, f))));

// ----- products CRUD (staff; page-based, as in the 60.HTML Portal demo) ----- //
app.MapGet("/products", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.ProductsGet(ctx, s))));
app.MapGet("/products/new", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.ProductNewGet(ctx, s))));
app.MapGet("/products/edit", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.ProductEditGet(ctx, s))));
app.MapPost("/products/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.ProductSavePost(ctx, f, s))));
app.MapPost("/products/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.ProductDeletePost(ctx, f))));

// ----- invoices CRUD (staff) ----- //
app.MapGet("/invoices", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.InvoicesGet(ctx, s))));
app.MapGet("/invoices/new", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.InvoiceNewGet(ctx, s))));
app.MapGet("/invoices/edit", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.InvoiceEditGet(ctx, s))));
app.MapPost("/invoices/save",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.InvoiceSavePost(ctx, f, s))));
app.MapPost("/invoices/delete",
    (HttpContext ctx) => RunForm(ctx, f => host.GuardedStaff(ctx, s => host.InvoiceDeletePost(ctx, f))));

// ----- reports / statistics (staff) ----- //
app.MapGet("/reports", (HttpContext ctx) => Run(ctx, () => host.GuardedStaff(ctx, s => host.ReportsGet(ctx, s))));

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
