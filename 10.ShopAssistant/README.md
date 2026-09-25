# 10.ShopAssistant (ASP.NET Core)

AI storefront demo (TechNest) hosted on ASP.NET Core / Kestrel. It is a mirror of
`demos\60.HTML\10.ShopAssistant` (which hosts `TsgcWebSocketHTTPServer`): the
reusable logic is copied **verbatim** from that demo, and only the hosting layer
changes.

A light-themed Bootstrap storefront built from `TsgcHTMLComponent_*` widgets
(NavBar, Carousel, Badge, Rating, Breadcrumb, Tabs, Accordion, ButtonGroup,
Toast, AutoComplete, AIChat), with an in-memory per-visitor chat session and a
grounded (RAG) shop assistant named Nova.

## Copied verbatim (reused, not changed)

- `sgcShop_Catalog.cs` - in-memory product catalog + keyword search / scoring.
- `sgcShop_AI.cs` - `TShopAIResponder`: grounded RAG answers. Asks a live LLM
  only when a provider + API key are configured; otherwise a deterministic local
  fallback that never invents a product or price.
- `sgcShop_Pages.cs` - the whole storefront view (node layer).
- `sgcShop_Types.cs` - value types + `TShopSessionStore` (thread-safe in-memory
  cookie-keyed session store) + config type.
- `sgcShop_Config.cs` - `System.Text.Json` config loader.

`sgcShop_Server.cs` (the `TsgcWebSocketHTTPServer` host) and the console
`Program.cs` are intentionally **not** copied; `Program.cs` here is the Kestrel
host. The tiny `TShopChatBridge` glue class (which wires the AIChat RAG events to
the responder) is reproduced verbatim at the bottom of `Program.cs`.

## How it is hosted

- `AddSgcHtml(o => o.ServeRootPage = false)` registers the sgcHTML services but
  tells the adapter NOT to serve a page at `/`, so this app owns page routing.
- `UseWebSockets()` + `UseSgcHtml()` serve the built-in client assets (the page
  template links `/bootstrap.min.css` and `/bootstrap.bundle.min.js`, both served
  by the adapter asset registry), the PWA manifest / service worker and the `/ws`
  channel. They keep the hosting pattern uniform across the `61.HTML.AspNetCore`
  demos.
- The reusable `TShopSessionStore`, `TShopPages` and `TShopAIResponder` are plain
  C# singletons registered in DI.
- The 60.HTML host scanned `RawHeaders` for the `Cookie:` line and wrote
  `Set-Cookie` through `CustomHeaders`. Here the **same** `shop_session` cookie
  (attributes `Path=/; HttpOnly; SameSite=Lax`) is read/written through
  `ctx.Request.Cookies` / `ctx.Response.Cookies`, so behavior matches.

## Routes

| Method | Path | Handler |
|---|---|---|
| GET  | `/`               | Home (hero carousel + featured products) |
| GET  | `/catalog`        | Catalog, filtered by the `category` query param |
| GET  | `/about`          | Store info + FAQ accordion |
| GET  | `/product/{id}`   | Product detail (200 / 404) - keeps the card links live |
| POST | `/api/chat`       | Chat turn: persist + `302` to `return_to?chatOpen=1` |
| GET  | `/favicon.svg`    | Inline brand favicon |
| any  | (unmapped)        | Reused 404 page |

`POST /api/chat` reproduces the 60.HTML synchronous flow: the message is run
through `TsgcHTMLComponent_AIChat` (its `OnRAGContext` + `OnChatSend` events
bridged to `TShopAIResponder`), both turns are appended to the session, and the
handler redirects to `return_to?chatOpen=1`. The next GET replays the whole
transcript into the chat widget, so the conversation survives a full page reload.

## AI configuration (keyless by default)

`sgcShopServer.conf.json` sets `ai.provider = "none"`, so Nova answers from the
local catalog only and the demo builds + runs with **zero** external
dependencies. To enable a live LLM, set `ai.provider` to `openai` or `anthropic`
(optionally `ai.model`) and set the `TECHNEST_AI_API_KEY` environment variable to
your API key. The `listen` section in that file is ignored here; Kestrel owns the
port (`appsettings.json`).

## Run

```
dotnet run --project sgcShopWeb.csproj
```

Then open (default port `8096`, see `appsettings.json`):

```
http://localhost:8096/
http://localhost:8096/catalog?category=Audio
http://localhost:8096/about
```

Click the chat bubble (bottom-right) and ask Nova about products, pricing, stock,
shipping or returns.
