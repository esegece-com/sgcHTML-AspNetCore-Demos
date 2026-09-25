// ***************************************************************************
//  sgcShopWeb - AI storefront demo (TechNest) on ASP.NET Core
//  Mirror of demos\60.HTML\10.ShopAssistant (TsgcWebSocketHTTPServer host).
//
//  Hosts the SAME reusable logic on Kestrel. These files are copied VERBATIM
//  from the 60.HTML demo and are NOT changed here:
//    - sgcShop_Catalog.cs  (in-memory product catalog + search/scoring)
//    - sgcShop_AI.cs        (grounded RAG responder; keyless local fallback)
//    - sgcShop_Pages.cs     (node-layer view: the whole storefront)
//    - sgcShop_Types.cs     (value types + TShopSessionStore in-memory sessions)
//    - sgcShop_Config.cs    (JSON config loader)
//
//  Only the hosting layer changes:
//    - AddSgcHtml(o => o.ServeRootPage = false) so THIS app owns page routing.
//    - UseWebSockets() + UseSgcHtml() serve the built-in client assets (the page
//      template links /bootstrap.min.css + /bootstrap.bundle.min.js, both served
//      by the adapter asset registry) and keep the hosting pattern uniform across
//      the 61.HTML.AspNetCore demos.
//    - The 60.HTML host scanned RawHeaders for the Cookie line and wrote
//      Set-Cookie through CustomHeaders. Here the same "shop_session" cookie is
//      read/written through ctx.Request.Cookies / ctx.Response.Cookies.
//    - Routes are Minimal API endpoints (MapGet/MapPost) that reuse the verbatim
//      TShopPages builder for every byte of HTML, matching the 60.HTML
//      DispatchRequest branches.
//    - POST /api/chat reproduces the 60.HTML synchronous chat flow: run the
//      message through TsgcHTMLComponent_AIChat (RAG events bridged to the reused
//      TShopAIResponder), persist both turns in the session, then 302-redirect to
//      return_to?chatOpen=1 so the reply renders on the next GET.
//    - AI is keyless by default (provider "none" => grounded local fallback), so
//      the demo builds + runs + smoke-tests with zero external dependencies.
// ***************************************************************************

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;
using Shop;

// Same cookie name the 60.HTML demo uses, so behavior matches exactly.
const string CS_SHOP_SESSION_COOKIE = "shop_session";

// Self-contained favicon served at /favicon.svg (the page template links it in
// its head). Verbatim from the 60.HTML TShopServer.CS_SHOP_FAVICON_SVG; kept
// here because that host unit (sgcShop_Server.cs) is intentionally NOT copied.
const string CS_SHOP_FAVICON_SVG =
    "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\" " +
    "role=\"img\" aria-label=\"TechNest\">" +
    "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"#4F46E5\"/>" +
    "<text x=\"32\" y=\"43\" font-family=\"Arial,Helvetica,sans-serif\" " +
    "font-weight=\"900\" font-size=\"26\" text-anchor=\"middle\" " +
    "fill=\"#FFFFFF\">TN</text></svg>";

var builder = WebApplication.CreateBuilder(args);

// Load the shop config (AI provider / model / api-key env var) from
// sgcShopServer.conf.json, tolerant of a missing file (defaults => AI disabled).
// The listen section is ignored here; Kestrel owns the port (appsettings.json).
var oConfig = new TShopServerConfig();
string vConfigPath = Path.Combine(AppContext.BaseDirectory, "sgcShopServer.conf.json");
if (File.Exists(vConfigPath))
    new TShopConfigLoader(oConfig).LoadFromFile(vConfigPath);

// The app renders its own pages, so the adapter serves assets + the WebSocket
// channel only (ServeRootPage = false hands page routing back to the pipeline).
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);

// Plain-C# singletons reused verbatim from the 60.HTML demo.
builder.Services.AddSingleton(oConfig);
builder.Services.AddSingleton(new TShopSessionStore(120));
builder.Services.AddSingleton<TShopPages>();
builder.Services.AddSingleton<TShopAIResponder>();

var app = builder.Build();

app.UseWebSockets();   // REQUIRED before UseSgcHtml
app.UseSgcHtml();      // serves assets + manifest + sw + /ws (not the page)

var oSessions = app.Services.GetRequiredService<TShopSessionStore>();
var oPages = app.Services.GetRequiredService<TShopPages>();
var oAI = app.Services.GetRequiredService<TShopAIResponder>();

// ----- request helpers ----- //

// Returns the caller's session, creating one (and cookie-ing it via
// Response.Cookies) when the request has no valid session cookie yet. Mirrors
// the 60.HTML EnsureSession, using the same cookie name + attributes
// (Path=/; HttpOnly; SameSite=Lax) but through the ASP.NET Core cookie API.
TShopSession EnsureSession(HttpContext aCtx)
{
    string vToken = aCtx.Request.Cookies[CS_SHOP_SESSION_COOKIE];
    TShopSession vSession;
    if (string.IsNullOrEmpty(vToken) || !oSessions.TryGet(vToken, out vSession))
    {
        vToken = oSessions.CreateSession();
        oSessions.TryGet(vToken, out vSession);
        aCtx.Response.Cookies.Append(CS_SHOP_SESSION_COOKIE, vToken,
            new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
    }
    return vSession;
}

// True when the request carries '?chatOpen=1' (set by the POST /api/chat
// redirect so the just-answered chat panel starts open).
bool ChatOpenFlag(HttpContext aCtx)
{
    return aCtx.Request.Query["chatOpen"].ToString() == "1";
}

// The current path (+ query) used as the chat form's return_to and for nav
// highlighting. Mirrors the 60.HTML CurrentURI (Document + "?" + QueryParams);
// QueryString.Value already includes the leading '?'.
string CurrentURI(HttpContext aCtx)
{
    string vDoc = aCtx.Request.Path.HasValue ? aCtx.Request.Path.Value : "/";
    if (string.IsNullOrEmpty(vDoc))
        vDoc = "/";
    if (aCtx.Request.QueryString.HasValue)
        vDoc = vDoc + aCtx.Request.QueryString.Value;
    return vDoc;
}

// ----- static asset the page template links but the adapter registry lacks --//
// The adapter serves the built-in bootstrap CSS/JS, but not the custom favicon.
app.MapGet("/favicon.svg", (HttpContext ctx) =>
{
    ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
    return Results.Content(CS_SHOP_FAVICON_SVG, "image/svg+xml");
});

// ----- page routes (mirror of the 60.HTML DispatchRequest) ----- //

app.MapGet("/", (HttpContext ctx) =>
{
    TShopSession oSession = EnsureSession(ctx);
    string vHtml = oPages.BuildHomePage(oSession, CurrentURI(ctx), ChatOpenFlag(ctx));
    return Results.Content(vHtml, "text/html; charset=utf-8");
});

app.MapGet("/catalog", (HttpContext ctx) =>
{
    TShopSession oSession = EnsureSession(ctx);
    string vCategory = ctx.Request.Query["category"].ToString();
    string vHtml = oPages.BuildCatalogPage(vCategory, oSession, CurrentURI(ctx),
        ChatOpenFlag(ctx));
    return Results.Content(vHtml, "text/html; charset=utf-8");
});

app.MapGet("/about", (HttpContext ctx) =>
{
    TShopSession oSession = EnsureSession(ctx);
    string vHtml = oPages.BuildAboutPage(oSession, CurrentURI(ctx), ChatOpenFlag(ctx));
    return Results.Content(vHtml, "text/html; charset=utf-8");
});

// Product detail page. Not in the task's minimal route list, but the reused
// TShopPages product cards + navbar search link to /product/{id}, so it is
// mapped here to keep the mirror's links live (matches the 60.HTML route).
app.MapGet("/product/{id:int}", (HttpContext ctx, int id) =>
{
    TShopSession oSession = EnsureSession(ctx);
    TShopProduct oProduct;
    bool vFound = ShopCatalog.sgcShopFindProductById(id, out oProduct);
    if (oProduct == null)
        oProduct = new TShopProduct();
    string vHtml = oPages.BuildProductPage(oProduct, vFound, oSession,
        CurrentURI(ctx), ChatOpenFlag(ctx));
    return Results.Content(vHtml, "text/html; charset=utf-8", null,
        vFound ? 200 : 404);
});

// POST /api/chat: run the message through the AIChat RAG pipeline (bridged to
// the reused TShopAIResponder), persist both turns in the session, then redirect
// to return_to?chatOpen=1 so the reply renders on the next GET.
app.MapPost("/api/chat", async (HttpContext ctx) =>
{
    TShopSession oSession = EnsureSession(ctx);

    string vMessage = "";
    string vReturnTo = "";
    if (ctx.Request.HasFormContentType)
    {
        IFormCollection vForm = await ctx.Request.ReadFormAsync();
        vMessage = (vForm["message"].ToString() ?? "").Trim();
        vReturnTo = vForm["return_to"].ToString() ?? "";
    }

    // Only same-origin, absolute-path returns; anything else falls back to "/".
    if (string.IsNullOrEmpty(vReturnTo) || vReturnTo[0] != '/' ||
        (vReturnTo.Length >= 2 && vReturnTo.Substring(0, 2) == "//"))
        vReturnTo = "/";

    if (vMessage != "")
    {
        // Cap a single message so an abusive POST cannot blow up the grounding
        // context / prompt.
        if (vMessage.Length > 500)
            vMessage = vMessage.Substring(0, 500);

        var oBridge = new TShopChatBridge(oAI, oPages);
        var oChat = new TsgcHTMLComponent_AIChat();
        oChat.RAGEnabled = true;
        oChat.OnRAGContext += oBridge.HandleRAGContext;
        oChat.OnChatSend += oBridge.HandleChatSend;
        // Fires OnRAGContext then OnChatSend synchronously; the bridge builds the
        // grounding context, gets the reply (AI or local fallback) and calls
        // AddAIMessageWithSources on oChat.
        oChat.ProcessUserMessage(vMessage);

        oSessions.AppendMessage(oSession.Token, TShopChatRole.scrUser, vMessage, "");
        oSessions.AppendMessage(oSession.Token, TShopChatRole.scrAssistant,
            oBridge.LastReply, oBridge.LastSourcesHTML);
    }

    string vSep = vReturnTo.IndexOf('?') >= 0 ? "&" : "?";
    return Results.Redirect(vReturnTo + vSep + "chatOpen=1");
});

// Unknown path -> the reused standalone 404 page.
app.MapFallback((HttpContext ctx) =>
    Results.Content(oPages.BuildNotFoundPage(), "text/html; charset=utf-8", null, 404));

app.Run();

namespace Shop
{
    // Per-request bridge between TsgcHTMLComponent_AIChat's RAG events (fired
    // synchronously from ProcessUserMessage) and TShopAIResponder. A fresh
    // instance is created for each POST /api/chat call so concurrent HTTP
    // threads never share mutable state. HandleRAGContext computes and stashes
    // the grounding context + matched products; HandleChatSend (fired right
    // after by the same ProcessUserMessage call) reads them back to get the
    // actual reply and appends it via AddAIMessageWithSources.
    // Reproduced verbatim from the 60.HTML sgcShop_Server.cs host glue (that unit
    // is intentionally not copied, being the TsgcWebSocketHTTPServer host).
    internal class TShopChatBridge
    {
        private readonly TShopAIResponder FAI;
        private readonly TShopPages FPages;
        private TShopProduct[] FMatched = Array.Empty<TShopProduct>();
        private string FQuery = "";
        private string FContext = "";
        private string FLastReply = "";
        private string FLastSourcesHTML = "";

        public TShopChatBridge(TShopAIResponder aAI, TShopPages aPages)
        {
            FAI = aAI;
            FPages = aPages;
        }

        public string LastReply { get { return FLastReply; } }
        public string LastSourcesHTML { get { return FLastSourcesHTML; } }

        public void HandleRAGContext(object Sender, string aQuery,
            ref string aContext, ref string aSourceRefs)
        {
            FQuery = aQuery;
            FContext = FAI.BuildContext(aQuery, out FMatched);
            aSourceRefs = FPages.BuildChatSourcesHTML(FMatched);
            aContext = FContext;
        }

        public void HandleChatSend(object Sender, string aUserMessage,
            string aConversationHistory)
        {
            FLastReply = FAI.Reply(FQuery, FContext, FMatched);
            FLastSourcesHTML = FPages.BuildChatSourcesHTML(FMatched);
            var oChat = Sender as TsgcHTMLComponent_AIChat;
            if (oChat != null)
                oChat.AddAIMessageWithSources(FLastReply, FLastSourcesHTML);
        }
    }
}
