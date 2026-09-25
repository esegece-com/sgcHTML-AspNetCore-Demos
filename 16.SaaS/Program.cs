// ***************************************************************************
//  sgcSaaSWeb - multi-tenant SaaS control plane demo on ASP.NET Core
//  Mirror of demos\60.HTML\16.SaaS (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcSaaS_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcSaaS_Config.cs    (JSON config loader)
//    - sgcSaaS_DB.cs        (Microsoft.Data.Sqlite schema + CRUD + seed)
//    - sgcSaaS_Pages.cs     (component view layer: every page of both axes)
//    - sgcSaaS_Passkeys.cs  (WebAuthn passkey engine bridged to the DB)
//    - sgcSaaS_Sessions.cs  (in-memory cookie session store + impersonation)
//    - sgcSaaS_Types.cs     (domain records + TSaaSServerConfig)
//
//  Only the hosting layer changes (following the 01.ERP host pattern):
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap / Chart.js /
//      htmx assets (by file name, from the adapter's resource registry) and the
//      htmx WebSocket channel, keeping the hosting pattern uniform across the
//      61.HTML.AspNetCore demos. The adapter claims *.js and *.css only, so the
//      app's own /favicon.svg route below still runs.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim TSaaSPages builder for every byte of HTML. The gate
//      is per-endpoint and mirrors the dispatcher's three steps: Guarded (step
//      5, any signed-in session), GuardedTenant (step 6, the /app workspace)
//      and GuardedVendor (step 7, the /admin console), plus GuardedJson for the
//      passkey registration pair that answers JSON rather than HTML.
//    - Cookies (saas_session / saas_theme) are read + written through the
//      native ASP.NET Core cookie API, with the SAME names + attributes.
//    - PASSKEYS: the RP id (request host name) + origin (scheme://host) are
//      DERIVED from the live request and threaded into the reused passkey
//      engine instead of the 60.HTML hard-coded localhost (SaaSWebHost.
//      GetPasskeys). The absolute verify / reset / invitation links come from
//      the same live request instead of the hard-coded ListenPort.
//
//  The two host files that were NOT copied are sgcSaaS_Server.cs (the
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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using SaaS;

var builder = WebApplication.CreateBuilder(args);

// Load the SaaS config (vendor staff credentials + DB path) from
// sgcSaaSServer.conf.json, tolerant of a missing file (defaults => root/root).
// The listen section is ignored here; Kestrel owns the port (appsettings.json).
var oConfig = new TSaaSServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory,
    "sgcSaaSServer.conf.json");
if (File.Exists(vConfigPath))
    new TSaaSConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is launched
// from. conf.json's "data\\saas.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "saas.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

// The host owns the reused singletons (DB pool, session store, page builder,
// per-origin passkey factory). Constructed once at startup below; DI disposes it
// (and the DB pool) on shutdown.
builder.Services.AddSingleton(sp => new SaaSWebHost(oConfig, vDbPath));

var app = builder.Build();

// Force construction now so the DB schema + demo/vendor seed run at startup
// (mirrors TSaaSServer.InitRuntime), not lazily on the first request.
var host = app.Services.GetRequiredService<SaaSWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
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

// ----- public: health + the one asset the adapter does not own ----- //
app.MapGet("/healthz", (HttpContext ctx) => Run(ctx, () => host.Healthz()));
app.MapGet("/favicon.svg", (HttpContext ctx) => Run(ctx, () => host.Favicon(ctx)));
app.MapGet("/favicon.ico", (HttpContext ctx) => Run(ctx, () => host.Favicon(ctx)));

// ----- theme (public, POST only - the dispatcher answers 405 otherwise) ----- //
app.MapPost("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));

// ----- public marketing + auth ----- //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.Root(ctx)));
app.MapGet("/pricing", (HttpContext ctx) => Run(ctx, () => host.Pricing(ctx)));
app.MapGet("/signup", (HttpContext ctx) => Run(ctx, () => host.SignupGet(ctx)));
app.MapPost("/signup", (HttpContext ctx) => RunForm(ctx, f => host.SignupPost(ctx, f)));
// The 60.HTML dispatcher answers /verify and /logout for ANY method; the links
// on the pages are plain GETs.
app.MapMethods("/verify", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.Verify(ctx)));
app.MapGet("/login", (HttpContext ctx) => Run(ctx, () => host.LoginGet(ctx)));
app.MapPost("/login", (HttpContext ctx) => RunForm(ctx, f => host.LoginPost(ctx, f)));
app.MapMethods("/logout", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapGet("/forgot", (HttpContext ctx) => Run(ctx, () => host.ForgotGet(ctx)));
app.MapPost("/forgot", (HttpContext ctx) => RunForm(ctx, f => host.ForgotPost(ctx, f)));
app.MapPost("/reset", (HttpContext ctx) => RunForm(ctx, f => host.ResetPost(ctx, f)));
// A non-POST /reset is sent back to the request form, exactly as the dispatcher's
// else-branch does (the token alone is not a password change).
app.MapGet("/reset", () => Results.Redirect("/forgot"));

// ----- social sign-in (nothing is configured: both ends are local) ----- //
app.MapMethods("/auth/social/{**provider}", new[] { "GET", "POST" },
    (HttpContext ctx, string provider) => Run(ctx, () => host.SocialStart(provider)));
app.MapMethods("/auth/callback", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.OAuthCallback(ctx)));

// ----- invitations (public: the opaque token IS the credential) ----- //
app.MapPost("/invite/{token}/accept", (HttpContext ctx, string token) =>
    RunForm(ctx, f => host.InviteAccept(ctx, f, token ?? "")));
app.MapMethods("/invite/{token}", new[] { "GET", "POST" },
    (HttpContext ctx, string token) => Run(ctx, () => host.InviteGet(ctx, token ?? "")));

// ----- passkeys (WebAuthn, JSON in / JSON out) ----- //
// Sign-in is public; registration is behind the JSON auth gate (the dispatcher
// answers those two paths with a JSON 401 rather than the HTML gate).
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

// ----- both axes (matched before the axis gate) ----- //
// Stopping an impersonation must stay reachable while the session carries a
// tenant identity, and the ImpersonateBanner renders its stop control as a link,
// so GET is accepted here as well as POST.
app.MapMethods("/admin/impersonate/stop", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
        s => host.ImpersonateStop(ctx, s))));
app.MapMethods("/sql", new[] { "GET", "POST" },
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
        s => host.SQLPage(ctx, s))));

// ----- tenant workspace (/app: signed in AND tenant-scoped) ----- //
app.MapGet("/app", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.AppDashboard(ctx, s))));
app.MapGet("/app/onboarding", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.OnboardingGet(ctx, s))));
app.MapPost("/app/onboarding/step", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.OnboardingPost(ctx, f, s))));

// ----- projects ----- //
app.MapGet("/app/projects", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.ProjectsGet(ctx, s))));
app.MapPost("/app/projects/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.ProjectSave(ctx, f, s))));
app.MapPost("/app/projects/delete", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.ProjectDelete(ctx, f, s))));
// {id:long} so a non-numeric segment falls through to the 404 tail, exactly as
// the dispatcher's TryStrToInt64 miss does.
app.MapGet("/app/projects/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedTenant(ctx, s => host.ProjectDetail(ctx, s, id))));

// ----- tasks ----- //
app.MapGet("/app/tasks", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.TasksGet(ctx, s))));
app.MapPost("/app/tasks/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.TaskSave(ctx, f, s))));
app.MapPost("/app/tasks/move", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.TaskMove(ctx, f, s))));

// ----- team + permission matrix ----- //
app.MapGet("/app/team", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.TeamGet(ctx, s))));
app.MapPost("/app/team/invite", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.TeamInvite(ctx, f, s))));
app.MapPost("/app/team/role", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.TeamRole(ctx, f, s))));
app.MapPost("/app/team/remove", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.TeamRemove(ctx, f, s))));
app.MapGet("/app/roles", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.RolesGet(ctx, s))));
app.MapPost("/app/roles/grant", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.RolesGrant(ctx, f, s))));

// ----- billing ----- //
app.MapGet("/app/billing", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.BillingGet(ctx, s))));
app.MapPost("/app/billing/change-plan", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.BillingChangePlan(ctx, f, s))));
// The invoice segment carries the '.pdf' suffix the page links, so it is matched
// as a string and parsed in the host (the dispatcher strips it the same way).
app.MapMethods("/app/billing/invoice/{id}", new[] { "GET", "POST" },
    (HttpContext ctx, string id) => Run(ctx, () => host.GuardedTenant(ctx,
        s => host.InvoicePDF(ctx, s, id))));

// ----- settings, notifications, audit, isolation ----- //
app.MapGet("/app/settings", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.SettingsGet(ctx, s))));
app.MapPost("/app/settings/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.SettingsSave(ctx, f, s))));
app.MapGet("/app/notifications", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.NotificationsGet(ctx, s))));
app.MapPost("/app/notifications/read", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedTenant(ctx, s => host.NotificationRead(ctx, f, s))));
app.MapGet("/app/audit", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.AppAudit(ctx, s))));
app.MapGet("/app/isolation", (HttpContext ctx) => Run(ctx, () => host.GuardedTenant(ctx,
    s => host.Isolation(ctx, s))));

// ----- vendor console (/admin: signed in AND a vendor role) ----- //
app.MapGet("/admin", (HttpContext ctx) => Run(ctx, () => host.GuardedVendor(ctx,
    s => host.AdminDashboard(ctx, s))));
app.MapGet("/admin/plans", (HttpContext ctx) => Run(ctx, () => host.GuardedVendor(ctx,
    s => host.AdminPlansGet(ctx, s))));
app.MapPost("/admin/plans/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedVendor(ctx, s => host.AdminPlansSave(ctx, f, s))));
app.MapGet("/admin/flags", (HttpContext ctx) => Run(ctx, () => host.GuardedVendor(ctx,
    s => host.AdminFlagsGet(ctx, s))));
app.MapPost("/admin/flags/save", (HttpContext ctx) => RunForm(ctx,
    f => host.GuardedVendor(ctx, s => host.AdminFlagsSave(ctx, f, s))));
app.MapGet("/admin/audit", (HttpContext ctx) => Run(ctx, () => host.GuardedVendor(ctx,
    s => host.AdminAudit(ctx, s))));
// The literal /admin/impersonate/stop above wins over this parameter segment,
// and the :long constraint would refuse it anyway.
app.MapPost("/admin/impersonate/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedVendor(ctx, s => host.ImpersonateStart(ctx, s, id))));
app.MapGet("/admin/tenants/{id:long}", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedVendor(ctx, s => host.AdminTenant(ctx, s, id))));
app.MapPost("/admin/tenants/{id:long}/suspend", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedVendor(ctx, s => host.AdminTenantStatus(ctx, s, id,
        SaaSTypes.CS_TENANT_SUSPENDED))));
app.MapPost("/admin/tenants/{id:long}/activate", (HttpContext ctx, long id) => Run(ctx,
    () => host.GuardedVendor(ctx, s => host.AdminTenantStatus(ctx, s, id,
        SaaSTypes.CS_TENANT_ACTIVE))));

// ----- unknown protected path (mirrors the 60.HTML DispatchRequest tail) ----- //
// The auth gate runs first, then NotFound re-applies the axis gate for an
// unknown /app or /admin path before rendering the styled 404 page.
app.MapFallback((HttpContext ctx) => Run(ctx, () => host.Guarded(ctx,
    s => host.NotFound(ctx, s))));

app.Run();
