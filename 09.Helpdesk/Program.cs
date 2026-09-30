// ***************************************************************************
//  sgcHelpdeskWeb - Helpdesk support-ticket demo on ASP.NET Core
//  Mirror of demos\60.HTML\09.Helpdesk (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcHelpdesk_Bcrypt.cs    (bcrypt hash / verify)
//    - sgcHelpdesk_Config.cs    (JSON config loader)
//    - sgcHelpdesk_DB.cs        (Microsoft.Data.Sqlite schema + tickets + seed)
//    - sgcHelpdesk_Pages.cs     (node-layer view: every page)
//    - sgcHelpdesk_Sessions.cs  (in-memory cookie session store)
//    - sgcHelpdesk_Types.cs     (domain records + THelpdeskServerConfig)
//
//  Only the hosting layer changes:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in Bootstrap assets (by file
//      name, from the adapter's resource registry) and the htmx WebSocket channel,
//      keeping the hosting pattern uniform across the 61.HTML.AspNetCore demos.
//    - Every 60.HTML DispatchRequest branch becomes a Minimal API endpoint that
//      reuses the verbatim THelpdeskPages builder for every byte of HTML. The auth
//      gate is per-endpoint (host.Guarded); the admin / ticket-ownership (IDOR)
//      checks live inside each handler exactly as in 60.HTML.
//    - Cookies (session / theme) are read + written through the native ASP.NET
//      Core cookie API, with the SAME names + attributes.
//    - MULTIPART: the ticket create / reply endpoints read uploaded files from
//      ASP.NET Core IFormFile (ctx.Request.Form.Files) instead of the 60.HTML
//      hand-rolled multipart parser; the files are persisted by the reused
//      THelpdeskAttachStore and the attachment download streams via Results.File.
//    - KANBAN (admin ticket list, tickets #1154 / #1155): /tickets/kanban-move,
//      /tickets/kanban-add and /tickets/kanban-edit (GET + POST) are Minimal API
//      endpoints over the same handlers as the 60.HTML host. The board's live sync
//      bridge (sgcHTMX.min.js) opens a plain WebSocket on the page URL '/'; the
//      middleware below accepts that upgrade itself and passes the HttpContext to
//      the host, which reads the session cookie from the upgrade request and pushes
//      card fragments to admin boards only (the 60.HTML host captured the cookie in
//      OnHandshake for the same purpose). /sgcWebSockets.js is served from the
//      esegece.sgcHTML assembly manifest (the adapter registry serves htmx.min.js
//      and sgcHTMX.min.js by file name).
//
//  IMPORTANT hosting note (the reusable pattern): a Minimal API lambda whose ONLY
//  parameter is HttpContext and which returns Task<IResult> is treated as a raw
//  RequestDelegate, so its IResult is DISCARDED (ASP0016) - the handler's cookie
//  side effects run but the redirect / status / body are lost. To avoid that trap
//  uniformly, every endpoint lambda below returns plain Task and the Run / RunForm
//  helpers execute the handler's IResult onto the response explicitly. (Verified:
//  POST /login returns 302, not 200.)
// ***************************************************************************

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;
using Helpdesk;

var builder = WebApplication.CreateBuilder(args);

// Load the helpdesk config (admin credentials + DB path) from
// sgcHelpdeskServer.conf.json, tolerant of a missing file (defaults => admin/admin).
// The listen section is ignored here; Kestrel owns the port (appsettings.json).
var oConfig = new THelpdeskServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory, "sgcHelpdeskServer.conf.json");
if (File.Exists(vConfigPath))
    new THelpdeskConfigLoader(oConfig).LoadFromFile(vConfigPath);

// Resolve the SQLite file next to the exe (AppContext.BaseDirectory) so the DB
// lands in a stable place regardless of the working directory the app is launched
// from. conf.json's "data\\helpdesk.db" is relative.
string vDbPath = oConfig.DatabaseFile;
if (string.IsNullOrEmpty(vDbPath))
    vDbPath = Path.Combine("data", "helpdesk.db");
if (!Path.IsPathRooted(vDbPath))
    vDbPath = Path.Combine(AppContext.BaseDirectory, vDbPath);

// Attachments are stored under <exe dir>\data\attachments\<ticket_id>\, anchored
// to the exe dir so they land next to the DB (same as the 60.HTML relative layout).
string vStorageRoot = AppContext.BaseDirectory;

// Kestrel owns the listen port (appsettings.json / --Kestrel:... cmdline). Parse
// it from configuration purely for display.
int vPort = 8100;
string vKestrelUrl = builder.Configuration["Kestrel:Endpoints:Http:Url"];
if (!string.IsNullOrEmpty(vKestrelUrl))
{
    try { vPort = new Uri(vKestrelUrl).Port; } catch { }
}

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
// Kanban live sync: the board's sgcHTMX bridge connects to ws(s)://host/ (the page
// URL), so the adapter accepts the channel on any path; the hub keeps the session
// cookie of each upgrade and the host pushes card fragments only to admin browsers
// (a filtered ISgcHtmlHub.BroadcastAsync).
builder.Services.AddSgcHtml(o =>
{
    o.ServeRootPage = false;
    o.AcceptWebSocketOnAnyPath = true;
});

// The host owns the reused singletons (DB pool, session store, page builder,
// attachment store). Constructed once at startup below; DI disposes it (and the
// DB pool) on shutdown.
builder.Services.AddSingleton(sp => new HelpdeskWebHost(oConfig, vDbPath, vStorageRoot, vPort,
    sp.GetRequiredService<ISgcHtmlHub>()));

var app = builder.Build();

// Force construction now so the DB schema + admin/demo seed run at startup
// (mirrors THelpdeskServer.Start), not lazily on the first request.
var host = app.Services.GetRequiredService<HelpdeskWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml

app.UseSgcHtml();      // serves Bootstrap / htmx assets + manifest + sw + /ws

// ----- endpoint helpers (see the IMPORTANT hosting note above) ----- //

// Runs a synchronous IResult handler and writes its result to the response.
Task Run(HttpContext ctx, Func<IResult> handler)
{
    return handler().ExecuteAsync(ctx);
}

// Reads the form (urlencoded OR multipart; null when the request has no form body,
// so GetParam falls back to the query string only - matching the 60.HTML merge),
// runs the IResult handler and writes its result to the response.
async Task RunForm(HttpContext ctx, Func<IFormCollection, IResult> handler)
{
    IFormCollection form = ctx.Request.HasFormContentType
        ? await ctx.Request.ReadFormAsync()
        : null;
    await handler(form).ExecuteAsync(ctx);
}

// ----- static asset the page template links but the adapter lacks ----- //
// The adapter serves the built-in bootstrap *.js / *.css by file name; the custom
// favicon is served here (matches the 60.HTML TryServeStaticAsset favicon branch).
app.MapGet("/favicon.svg", (HttpContext ctx) => Run(ctx, () =>
{
    ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
    return Results.Content(HelpdeskWebHost.CS_FAVICON_SVG, "image/svg+xml");
}));

// The Kanban board links /sgcWebSockets.js, which the adapter registry does NOT
// serve (it is embedded in the esegece.sgcHTML assembly), so it is served here
// straight from the manifest by its logical name.
app.MapGet("/sgcWebSockets.js", () =>
{
    string vBody = GetEmbeddedAsset("sgcWebSockets.js");
    return vBody == ""
        ? Results.NotFound()
        : Results.Content(vBody, "application/javascript; charset=utf-8");
});

// Health check (public).
app.MapGet("/healthz", () => Results.Text("ok", "text/plain; charset=utf-8"));

// ----- public auth / theme ----- //
app.MapPost("/theme", (HttpContext ctx) => RunForm(ctx, f => host.SetTheme(ctx, f)));
app.MapGet("/login", (HttpContext ctx) => Run(ctx, () => host.LoginGet(ctx)));
app.MapPost("/login", (HttpContext ctx) => RunForm(ctx, f => host.LoginPost(ctx, f)));
app.MapGet("/register", (HttpContext ctx) => Run(ctx, () => host.RegisterGet(ctx)));
app.MapPost("/register", (HttpContext ctx) => RunForm(ctx, f => host.RegisterPost(ctx, f)));
app.MapGet("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));
app.MapPost("/logout", (HttpContext ctx) => Run(ctx, () => host.Logout(ctx)));

// ----- ticket list (root, logged-in) ----- //
app.MapGet("/", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketListGet(ctx, s))));

// ----- admin-only stats dashboard ----- //
app.MapGet("/dashboard", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.DashboardGet(ctx, s))));

// ----- create-ticket form (user role only) ----- //
app.MapGet("/tickets/new", (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketNewGet(ctx, s))));
app.MapPost("/tickets/new",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TicketNewPost(ctx, f, s))));

// ----- admin-only CSV export (literal route beats /tickets/{id}) ----- //
app.MapGet("/tickets/export.csv",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketsExportCsvGet(ctx, s))));

// ----- admin Kanban board (literal routes beat /tickets/{id}) ----- //
app.MapPost("/tickets/kanban-move",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TicketKanbanMovePost(ctx, f, s))));
app.MapPost("/tickets/kanban-add",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TicketKanbanAddPost(ctx, f, s))));
app.MapGet("/tickets/kanban-edit",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketKanbanEditGet(ctx, s))));
app.MapPost("/tickets/kanban-edit",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TicketKanbanEditPost(ctx, f, s))));

// ----- /tickets/{id}[/reply|/close|/reopen|/files/{attId}] ----- //
app.MapGet("/tickets/{id}",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketDetailGet(ctx, s))));
app.MapPost("/tickets/{id}/reply",
    (HttpContext ctx) => RunForm(ctx, f => host.Guarded(ctx, s => host.TicketReplyPost(ctx, f, s))));
app.MapPost("/tickets/{id}/close",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketStatusPost(ctx, s, "closed", "closed"))));
app.MapPost("/tickets/{id}/reopen",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketStatusPost(ctx, s, "new", "reopened"))));
app.MapGet("/tickets/{id}/files/{attId}",
    (HttpContext ctx) => Run(ctx, () => host.Guarded(ctx, s => host.TicketFileGet(ctx, s))));

app.Run();

// Reads an asset embedded in the esegece.sgcHTML assembly by its logical name.
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
