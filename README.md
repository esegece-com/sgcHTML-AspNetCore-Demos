# sgcHTML .NET for ASP.NET Core, demos

Demo applications for **sgcHTML .NET**, the eSeGeCe library that builds complete
web user interfaces from C# code. Pages, forms, grids, dashboards and live
updates are composed server side with sgcHTML components, and the ASP.NET Core
adapter hosts them on Kestrel with Minimal API endpoints. No hand written HTML,
CSS or JavaScript is needed.

These demos match sgcHTML .NET **2026.10.0** and restore the free Community
edition, the `esegece.sgcHTML.AspNetCore.Community` package, from nuget.org.

More information, documentation and the commercial editions:
https://www.esegece.com

## Requirements

* .NET 8 SDK or later, https://dotnet.microsoft.com/download
* Windows, Linux or macOS. Every demo is a plain ASP.NET Core web app.

## Running a demo

Each demo is a standalone project. Open a terminal in its folder and run it:

```
cd 01.ERP
dotnet run
```

Then open the URL printed in the console, it is also listed in the table below.
The port of each demo is set in its `appsettings.json`.

To build all the demos at once, use the solution in the root folder:

```
dotnet build 61.HTML.AspNetCore.sln
```

Demos that keep data use SQLite and create their database under a `data`
folder on first run.

## Demos

| Folder | URL | Description |
|---|---|---|
| 01.ERP | http://localhost:8098 | ERP web app with SQLite storage |
| 02.AdminCRUD | http://localhost:8097 | Admin console mini ERP with CRUD screens |
| 03.LiveMonitor | http://localhost:8101 | Live monitoring dashboard with real time updates |
| 04.Portal | http://localhost:8099 | Customer portal web app |
| 05.HTMX | http://localhost:8094 | htmx features built with sgcHTML components |
| 06.Grid | http://localhost:8093 | Grid component features |
| 07.Site | http://localhost:8092 | Site layouts |
| 08.Components | http://localhost:8095 | Showcase of the sgcHTML components |
| 09.Helpdesk | http://localhost:8100 | Support ticket helpdesk |
| 10.ShopAssistant | http://localhost:8096 | TechNest storefront with an AI shopping assistant |
| 13.Warehouse | http://localhost:8102 | Warehouse management (WMS) web app |
| 14.POS | http://localhost:8103 | Retail point of sale |
| 15.Reports | http://localhost:8104 | Reporting and BI portal |
| 16.SaaS | http://localhost:8105 | Multi tenant SaaS control plane |
| 17.FieldService | http://localhost:8106 | Field service management |

Each folder has its own `README.md` with the details of that demo.

### 10.ShopAssistant and the AI provider

The shopping assistant runs keyless by default. `sgcShopServer.conf.json` sets
`ai.provider` to `none`, and the assistant answers from a local, catalogue
grounded fallback. To use a live LLM, set `ai.provider` to `openai` or
`anthropic` in that file and put your API key in the `TECHNEST_AI_API_KEY`
environment variable:

```
set TECHNEST_AI_API_KEY=your-key          (Windows cmd)
export TECHNEST_AI_API_KEY=your-key       (Linux, macOS)
```

## Community edition

The demos reference the free Community edition of sgcHTML .NET. It has the
complete feature set. When an application starts it shows a brief startup
notice, and every generated page carries a small
"Built with sgcHTML .NET Community Edition" badge. The Professional and
Enterprise editions remove both, see https://www.esegece.com

## Licence

The sgcHTML .NET library is covered by the Community Edition licence, which
ships inside the `esegece.sgcHTML.AspNetCore.Community` NuGet package as
`LICENSE-Community.txt`. It is free for individuals and organizations under
EUR 1,000,000 annual gross revenue with no more than five developers. The full
terms and the commercial licences are at https://www.esegece.com

copyright (c) 2026 eSeGeCe
