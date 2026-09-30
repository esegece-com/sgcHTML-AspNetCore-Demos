// ***************************************************************************
//  sgcGridWeb - Grid features demo on ASP.NET Core
//  Mirror of demos\60.HTML\06.Grid (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME page builders on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcGrid_Pages.cs     (grid shell + the classic feature tabs)
//    - sgcGrid_Features.cs  (in-memory orders / products / quotes and the
//                            Paging & Frozen, Selection, Typed & Edit,
//                            Summaries and Keyboard & Live tabs)
//
//  Only the hosting layer changes:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns "/".
//    - UseWebSockets() + UseSgcHtml() serve the built-in client assets
//      (bootstrap.min.css, bootstrap.bundle.min.js, htmx.min.js,
//      sgcHTMX.min.js), the PWA manifest / service worker and the WebSocket
//      channel. /sgcWebSockets.js is not in the adapter registry, so it is
//      served below from the esegece.sgcHTML assembly.
//    - LIVE CHANNEL: the shell's sgcHTMX bridge connects to "ws(s)://host/"
//      (the site root, exactly like the 60.HTML demo), so the adapter accepts
//      the channel on any path (AcceptWebSocketOnAnyPath). A plain GET "/" is
//      not a WebSocket upgrade and still reaches the MapGet("/") below.
//    - Inbound gridEdit messages (Typed and Live tabs) reach the adapter's
//      engine; its OnHTMXMessage applies them to the in-memory data
//      (TsgcGridDemoFeatures.ApplyGridEdit), as the 60.HTML engine handler does.
//    - A BackgroundService replaces the 60.HTML live thread: every 2 seconds it
//      runs TsgcGridDemoFeatures.LiveTick and broadcasts each fragment through
//      ISgcHtmlHub, ONE push per fragment (an insert is never mixed with
//      update / delete pushes). Under Kestrel the engine has no Server bound,
//      so its BroadcastFragment would be a no-op; pushes go through the hub.
//    - Each branch of the 60.HTML DispatchRequest is mapped to an endpoint
//      below, preserving the exact route path + query / form params so the
//      htmx attributes in the reused pages still resolve.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
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
using GridDemo;

var builder = WebApplication.CreateBuilder(args);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands "/" back to the pipeline below).
// The bridge of the shell connects to the site root, so upgrades are accepted on
// any path.
builder.Services.AddSgcHtml(o =>
{
    o.ServeRootPage = false;
    o.AcceptWebSocketOnAnyPath = true;
});

// The live row pushes of the Keyboard & Live tab (mirror of the 60.HTML thread).
builder.Services.AddHostedService<GridLivePushService>();

var app = builder.Build();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves assets + manifest + sw + the "/" WebSocket channel

// ----- inbound WebSocket messages ----- //
// Inline edits of the Typed and Live tabs are kept in memory.
var oEngine = app.Services.GetRequiredService<TsgcHTMX_Engine_Server>();
oEngine.OnHTMXMessage += (object sender, TsgcWSConnection connection,
    string message, ref string response) =>
{
    response = "";
    TsgcGridDemoFeatures.ApplyGridEdit(message);
};

// Small culture-invariant int parser matching the demo's StrToIntDef.
static int ParseIntDef(HttpContext ctx, string name, int def)
{
    string vRaw = ctx.Request.Query[name];
    int vValue;
    if (int.TryParse((vRaw ?? "").Trim(), NumberStyles.Integer,
        CultureInfo.InvariantCulture, out vValue))
        return vValue;
    return def;
}

static IResult Html(string html)
{
    return Results.Content(html, "text/html; charset=utf-8");
}

// Query string first, then the urlencoded form body (the bulk actions post
// action, keys, page, pageSize, sort and filter), matching the 60.HTML merge.
static async Task<Func<string, string>> ParamsOf(HttpContext ctx)
{
    IFormCollection oForm = ctx.Request.HasFormContentType
        ? await ctx.Request.ReadFormAsync()
        : null;
    return aName =>
    {
        if (ctx.Request.Query.ContainsKey(aName))
            return ctx.Request.Query[aName].ToString();
        if (oForm != null && oForm.ContainsKey(aName))
            return oForm[aName].ToString();
        return "";
    };
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

// The shell links /sgcWebSockets.js, which the adapter registry does NOT serve.
app.MapGet("/sgcWebSockets.js", () =>
{
    string vBody = GetEmbeddedAsset("sgcWebSockets.js");
    return vBody == ""
        ? Results.NotFound()
        : Results.Content(vBody, "application/javascript; charset=utf-8");
});

// ---- routes: one per DispatchRequest branch ---- //

// "/" -> the grid shell (tab navigation + first grid loaded via htmx)
app.MapGet("/", () => Html(TsgcGridDemoPages.BuildShell()));

app.MapGet("/grid/basic", () => Html(TsgcGridDemoPages.BuildGridBasic()));
app.MapGet("/grid/sort-filter", () => Html(TsgcGridDemoPages.BuildGridSortFilter()));
app.MapGet("/grid/export", () => Html(TsgcGridDemoPages.BuildGridExport()));
app.MapGet("/grid/edit", () => Html(TsgcGridDemoPages.BuildGridInlineEdit()));
app.MapGet("/grid/group", () => Html(TsgcGridDemoPages.BuildGridGroupBy()));
app.MapGet("/grid/master-detail", () => Html(TsgcGridDemoPages.BuildGridMasterDetail()));
app.MapGet("/grid/reorder", () => Html(TsgcGridDemoPages.BuildGridColumnReorder()));

// pagination: ?page=N (default 1)
app.MapGet("/grid/page", (HttpContext ctx) =>
{
    int vPage = ParseIntDef(ctx, "page", 1);
    return Html(TsgcGridDemoPages.BuildGridPagination(vPage));
});

app.MapGet("/grid/scroll", () => Html(TsgcGridDemoPages.BuildGridVirtualScroll()));

// virtual scroll rows: ?offset=N&limit=N&rootId=X (defaults 0 / 20 / "")
app.MapGet("/grid/scroll-rows", (HttpContext ctx) =>
{
    int vOffset = ParseIntDef(ctx, "offset", 0);
    int vLimit = ParseIntDef(ctx, "limit", 20);
    string vRootId = ctx.Request.Query["rootId"];
    return Html(TsgcGridDemoPages.BuildScrollRows(vOffset, vLimit, vRootId ?? ""));
});

// ---- 2026 grid features (any verb, like the 60.HTML DispatchRequest) ---- //
// Each handler returns plain Task and writes its IResult explicitly: a lambda
// whose only parameter is HttpContext and that returns Task<IResult> is treated
// as a raw RequestDelegate and its result would be discarded (ASP0016).
app.MapGet("/grid/paging", () => Html(TsgcGridDemoFeatures.BuildPaging()));
app.Map("/grid/orders-page", async (HttpContext ctx) =>
    await Html(TsgcGridDemoFeatures.BuildOrdersPage(await ParamsOf(ctx))).ExecuteAsync(ctx));
app.MapGet("/grid/selection", () => Html(TsgcGridDemoFeatures.BuildSelection()));
app.Map("/grid/selection-page", async (HttpContext ctx) =>
    await Html(TsgcGridDemoFeatures.BuildSelectionPage(await ParamsOf(ctx))).ExecuteAsync(ctx));
app.Map("/grid/selection-bulk", async (HttpContext ctx) =>
    await Html(TsgcGridDemoFeatures.ApplyBulkAction(await ParamsOf(ctx))).ExecuteAsync(ctx));
app.MapGet("/grid/typed", () => Html(TsgcGridDemoFeatures.BuildTyped()));
app.Map("/grid/lookup/supplier", async (HttpContext ctx) =>
    await Html(TsgcGridDemoFeatures.BuildSupplierLookup((await ParamsOf(ctx))("q"))).ExecuteAsync(ctx));
app.MapGet("/grid/summaries", () => Html(TsgcGridDemoFeatures.BuildSummaries()));
app.MapGet("/grid/live", () => Html(TsgcGridDemoFeatures.BuildLive()));

app.Run();

namespace GridDemo
{
    // Pushes the market updates of the Live tab every 2 seconds. Mirror of the
    // 60.HTML LiveLoop, driven by the hosted service lifetime instead of a thread.
    public sealed class GridLivePushService : BackgroundService
    {
        private readonly ISgcHtmlHub FHub;

        public GridLivePushService(ISgcHtmlHub aHub)
        {
            FHub = aHub;
        }

        protected override async Task ExecuteAsync(CancellationToken aStoppingToken)
        {
            List<string> oFragments = new List<string>();
            while (!aStoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, aStoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    oFragments.Clear();
                    TsgcGridDemoFeatures.LiveTick(oFragments);
                    // one push per fragment: insert pushes are never mixed with
                    // update / delete ones
                    for (int i = 0; i < oFragments.Count; i++)
                        if (!string.IsNullOrEmpty(oFragments[i]))
                            await FHub.BroadcastAsync(oFragments[i], null, aStoppingToken)
                                .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception E)
                {
                    // keep pushing even if one broadcast fails
                    Console.WriteLine("[push] error: " + E.Message);
                }
            }
        }
    }
}
