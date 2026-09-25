# 10.ShopAssistant: AI storefront demo (ASP.NET Core)

TechNest, a light-themed Bootstrap storefront built with sgcHTML .NET,
running on Kestrel. It shows off a grounded (RAG) AI shop assistant named
Nova, built from sgcHTML widgets (NavBar, Carousel, Rating, Tabs, Accordion,
AutoComplete, AIChat) with an in-memory per-visitor chat session.

## Run

Requires the .NET 8 SDK.

```
dotnet run
```

Then open (default port 8096):

```
http://localhost:8096/
http://localhost:8096/catalog?category=Audio
http://localhost:8096/about
```

Click the chat bubble (bottom-right) and ask Nova about products, pricing,
stock, shipping or returns.

## Features

- Home page with a hero carousel and featured products.
- Catalog, filterable by category.
- Store info and FAQ accordion.
- Product detail pages.
- Nova, the AI shop assistant: answers are grounded in the in-demo catalog
  and store info, so she never invents a product or a price. The
  conversation is kept per visitor and survives a full page reload.

## AI configuration (works with no key)

By default `sgcShopServer.conf.json` sets the AI provider to `none`, so Nova
answers from the local catalog only and the demo runs with zero external
dependencies. To have Nova call a live LLM instead, set the provider to
`openai` or `anthropic` in that file and set the `TECHNEST_AI_API_KEY`
environment variable to your API key. The call is a plain HTTP request to
OpenAI's or Anthropic's chat API; if it fails or no key is set, Nova falls
back to the local, catalog-grounded answers automatically. The `listen`
section of that file is ignored; Kestrel owns the port, set in
`appsettings.json` (default 8096).

## Project layout

| File | Role |
|---|---|
| `Program.cs` | App startup, routing, the AIChat-to-Nova wiring |
| `sgcShop_Pages.cs` | The storefront view |
| `sgcShop_Catalog.cs` | In-memory product catalog and keyword search |
| `sgcShop_AI.cs` | Nova: grounded RAG answers, live LLM call, local fallback |
| `sgcShop_Types.cs` | Value types and the per-visitor session store |
| `sgcShop_Config.cs` | Configuration loader |
| `sgcShopServer.conf.json` | AI provider, env var name, model |
| `appsettings.json` | Kestrel listen port |

---

Built with sgcHTML .NET, https://www.esegece.com. The Community edition shows
a one-time startup notice and a "Built with sgcHTML .NET Community Edition"
badge on every page.
