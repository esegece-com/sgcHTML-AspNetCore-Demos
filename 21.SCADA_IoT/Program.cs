// ***************************************************************************
//  sgcSCADAWeb - SCADA + IoT demo on ASP.NET Core
//  Mirror of demos\60.HTML\21.SCADA_IoT (TsgcWebSocketHTTPServer host).
//
//  A SCADA mimic, gauge, thermometer, strip chart and annunciator fed by a
//  simulated PLC and pushed live to every browser over the adapter's /ws
//  channel (sgcHTMX bridge).
//
//  Hosting:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns "/".
//    - UseWebSockets() + UseSgcHtml() serve the built-in client assets
//      (/htmx.min.js, /sgcWebSockets.js, /sgcHTMX.min.js) and OWN the WebSocket
//      channel at /ws.
//    - "/" renders the dashboard, POST /cmd toggles a valve / the pump / the
//      fan, POST /ack acknowledges an annunciator tile (both answer 204; the
//      visible change arrives as a pushed fragment).
//    - The PLC thread (1 s scan) starts after the app starts and stops on
//      ApplicationStopping.
//
//  SIMULATE MODE ONLY: the 60.HTML demo can also run against an MQTT broker
//  (test.mosquitto.org) through TsgcHTMLInstrumentsMQTTBinder. The MQTT binder
//  and the MQTT client are not part of the self-contained
//  esegece.sgcHTML.AspNetCore assembly, so this mirror drives the same
//  components through the transport-free TsgcHTMLInstrumentsBinder (see
//  sgcSCADA_Plant.cs and README.md).
// ***************************************************************************

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;
using SCADADemo;

var builder = WebApplication.CreateBuilder(args);

// conf.json next to the exe (same layout as the 60.HTML demo; the http port is
// ignored, Kestrel owns it through appsettings.json).
TsgcSCADAConfig oConfig = SCADAWebHost.LoadSCADAConfig(
    Path.Combine(AppContext.BaseDirectory, "sgcSCADAServer.conf.json"));

// The app renders its own page, so the adapter serves assets + /ws only.
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);
builder.Services.AddSingleton(sp => new SCADAWebHost(oConfig,
    sp.GetRequiredService<ISgcHtmlHub>()));

var app = builder.Build();

var host = app.Services.GetRequiredService<SCADAWebHost>();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves the htmx / sgcHTMX assets + /ws

// Runs a synchronous IResult handler and writes its result to the response
// (plain Task lambdas: a HttpContext-only lambda returning Task<IResult> would
// be treated as a RequestDelegate and its IResult discarded, ASP0016).
async Task RunForm(HttpContext ctx, Func<IFormCollection, IResult> handler)
{
    IFormCollection form = ctx.Request.HasFormContentType
        ? await ctx.Request.ReadFormAsync()
        : null;
    await handler(form).ExecuteAsync(ctx);
}

// The page links /sgcWebSockets.js (like the 60.HTML page), which the adapter
// registry does NOT serve: serve the embedded body here (same as 06.Grid).
app.MapGet("/sgcWebSockets.js", () =>
{
    string vBody = GetEmbeddedAsset("sgcWebSockets.js");
    return vBody == ""
        ? Results.NotFound()
        : Results.Content(vBody, "application/javascript; charset=utf-8");
});

// eSeGeCe favicon (inline SVG), so the browser gets no 404
const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
    "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"eSeGeCe\">" +
    "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"#0057B8\"/>" +
    "<text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica,sans-serif\" " +
    "font-weight=\"900\" font-size=\"44\" text-anchor=\"middle\" " +
    "fill=\"#FFFFFF\">e</text></svg>";
app.MapGet("/favicon.ico", () => Results.Content(CS_FAVICON_SVG, "image/svg+xml"));
app.MapGet("/favicon.svg", () => Results.Content(CS_FAVICON_SVG, "image/svg+xml"));

app.MapGet("/", () => Results.Content(host.BuildPage(), "text/html; charset=utf-8"));
app.MapPost("/cmd", (HttpContext ctx) => RunForm(ctx, f => host.CommandPost(ctx, f)));
app.MapPost("/ack", (HttpContext ctx) => RunForm(ctx, f => host.AckPost(ctx, f)));

app.Lifetime.ApplicationStarted.Register(() =>
{
    host.Start();
    Console.WriteLine("sgcHTML SCADA + IoT demo (ASP.NET Core)");
    Console.WriteLine("Mode: simulate (no broker)");
    Console.WriteLine("Topic root: " + host.Plant.Prefix + "/");
});
// the PLC thread pushes through the hub: stop it first
app.Lifetime.ApplicationStopping.Register(() => host.Stop());

app.Run();

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
