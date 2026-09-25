// ***************************************************************************
//  sgcComponentsWeb - sgcHTML components showcase on ASP.NET Core
//  Mirror of demos\60.HTML\08.Components (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME page builder (sgcComponents_Pages.cs, copied verbatim) on
//  Kestrel. The 60.HTML version drove the realtime part with a background push
//  thread + OnHTMXMessage on an engine bound to TsgcWebSocketHTTPServer. Here:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in client assets, the PWA
//      manifest / service worker, and OWN the /ws channel: for every inbound
//      text frame the adapter calls engine.DispatchMessage(null, text) and pushes
//      the RETURNED reply back to that sender.
//    - A BackgroundService replaces the push thread: every 1200 ms it advances
//      the live state (TsgcComponentsDemoPages.LiveTick) and broadcasts the OOB
//      fragments through ISgcHtmlHub.BroadcastAsync (the engine's own
//      BroadcastFragment is a no-op with no Server bound).
//    - engine.OnHTMXMessage reproduces the 60.HTML inbound behavior:
//        * autoCompleteSearch -> per-sender reply via ref aResponse (the adapter
//          pushes the DispatchMessage return value to the calling socket).
//        * job:cancel         -> broadcast-to-all via hub.BroadcastAsync.
//    - Binary routes /data/export.xlsx and /docs/sample.pdf, plus the component
//      page routes, are mapped with MapGet reusing the verbatim page builder.
//    - /sgcWebSockets.js: the reused page links it but the adapter registry does
//      NOT serve it, so it is served here from the esegece.sgcHTML manifest.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;
using Components;

// A minimal, self-contained one-page PDF so /docs works offline (verbatim from
// the 60.HTML demo's TsgcComponentsDemoServer.CS_SAMPLE_PDF).
const string CS_SAMPLE_PDF =
    "%PDF-1.4\n" +
    "1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
    "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
    "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 420 240]/Contents 4 0 R" +
    "/Resources<</Font<</F1 5 0 R>>>>>>endobj\n" +
    "4 0 obj<</Length 120>>\n" +
    "stream\n" +
    "BT /F1 20 Tf 40 170 Td (sgcHTML PDFViewer) Tj ET\n" +
    "BT /F1 12 Tf 40 140 Td (Served by the sgcHTML components demo.) Tj ET\n" +
    "BT /F1 12 Tf 40 120 Td (Route /docs/sample.pdf) Tj ET\n" +
    "endstream\n" +
    "endobj\n" +
    "5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\n" +
    "trailer<</Root 1 0 R/Size 6>>\n" +
    "%%EOF";

var builder = WebApplication.CreateBuilder(args);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

// The live push loop (mirror of the 60.HTML background push thread).
builder.Services.AddHostedService<ComponentsPushService>();

var app = builder.Build();

// The live components must exist before the first request AND before the push
// service touches them (mirror of TsgcComponentsDemoServer.Start's LiveInit).
TsgcComponentsDemoPages.LiveInit();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves assets + manifest + sw + /ws (not the page)

// ----- inbound WebSocket messages ----- //
// The adapter accepts /ws and, for each inbound text frame, calls
// engine.DispatchMessage(null, text) and pushes the RETURNED reply back to that
// sender via the hub. So OnHTMXMessage's ref aResponse is the per-sender reply
// channel; a broadcast-to-all goes through the hub instead.
var oEngine = app.Services.GetRequiredService<TsgcHTMX_Engine_Server>();
var oHub = app.Services.GetRequiredService<ISgcHtmlHub>();

oEngine.OnHTMXMessage += (object sender, TsgcWSConnection connection,
    string message, ref string response) =>
{
    response = "";

    List<string> vParams = new List<string>();
    string vPath;
    string vMethod;
    // The sgcHTMX client sends the named form fields as a flat JSON object.
    sgcHTMXHelpers.sgcHTMXParseMessage(message, out vPath, out vMethod, vParams);

    string vAction = GetParamValue(vParams, "action");
    if (vAction == "")
        return;

    // JobProgress Cancel: everyone watching should see the job flip, so broadcast
    // to all (no sender id needed) instead of replying only to the sender.
    if (string.Equals(vAction, "job:cancel", StringComparison.OrdinalIgnoreCase))
    {
        string vFragment = TsgcComponentsDemoPages.LiveCancelJob(
            GetParamValue(vParams, "job"));
        if (vFragment != "")
            oHub.BroadcastAsync(vFragment).GetAwaiter().GetResult();
        return;
    }

    // AutoComplete server search: the result list is specific to this user, so it
    // goes back to the calling connection only. Returning it via aResponse makes
    // DispatchMessage return it, and the adapter pushes it to the sender's socket.
    if (string.Equals(vAction, "autoCompleteSearch",
        StringComparison.OrdinalIgnoreCase))
    {
        response = TsgcComponentsDemoPages.LiveAutoCompleteSearch(
            GetParamValue(vParams, "query"));
        return;
    }
};

// ----- static asset the reused page links but the adapter registry lacks ----- //
// The reused page links /htmx.min.js, /sgcWebSockets.js and /sgcHTMX.min.js. The
// adapter registry serves htmx.min.js and sgcHTMX.min.js (plus bootstrap / chart
// / htmx-ext-ws), but NOT sgcWebSockets.js (the sgcWebSockets JS client the
// sgcHTMX bridge builds on). It is embedded in the esegece.sgcHTML assembly, so
// serve it here straight from the manifest by its logical name.
app.MapGet("/sgcWebSockets.js", () =>
{
    string vBody = GetEmbeddedAsset("sgcWebSockets.js");
    return vBody == ""
        ? Results.NotFound()
        : Results.Content(vBody, "application/javascript; charset=utf-8");
});

// ----- binary routes ----- //

// Grid "Export XLSX": a real workbook built server-side from the same rows the
// grid rendered (SaveToXLSXStream).
app.MapGet("/data/export.xlsx", () =>
{
    using MemoryStream oStream = new MemoryStream();
    TsgcComponentsDemoPages.BuildDataXLSX(oStream);
    byte[] vBytes = oStream.ToArray();
    return Results.File(vBytes,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "export.xlsx");
});

// PDFViewer document.
app.MapGet("/docs/sample.pdf", () =>
{
    byte[] vBytes = Encoding.ASCII.GetBytes(CS_SAMPLE_PDF);
    return Results.File(vBytes, "application/pdf");
});

// ----- component page routes (mirror of the 60.HTML DispatchRequest) ----- //
// Every BuildPage route renders the full Site shell; unknown routes 404.
string[] vRoutes = new string[]
{
    "/", "/inputs", "/display", "/codes", "/data", "/charts", "/live",
    "/admin", "/docs"
};
foreach (string vRoute in vRoutes)
{
    string vCapture = vRoute;
    app.MapGet(vRoute, () =>
    {
        string vHtml = TsgcComponentsDemoPages.BuildPage(vCapture);
        return vHtml == ""
            ? Results.NotFound()
            : Results.Content(vHtml, "text/html; charset=utf-8");
    });
}

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
        using Stream? oStream = oAsm.GetManifestResourceStream(
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

namespace Components
{
    // The live push loop: every 1200 ms advance the simulated live state and
    // broadcast the resulting htmx OOB fragments to every connected browser.
    // Mirror of TsgcComponentsDemoServer.PushLoop, driven by the hosted service
    // lifetime instead of a background thread.
    public sealed class ComponentsPushService : BackgroundService
    {
        private readonly ISgcHtmlHub FHub;

        public ComponentsPushService(ISgcHtmlHub aHub)
        {
            FHub = aHub;
        }

        protected override async Task ExecuteAsync(CancellationToken aStoppingToken)
        {
            while (!aStoppingToken.IsCancellationRequested)
            {
                try
                {
                    string vFragment = TsgcComponentsDemoPages.LiveTick();
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

                try
                {
                    await Task.Delay(1200, aStoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
