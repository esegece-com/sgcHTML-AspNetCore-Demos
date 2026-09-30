// ***************************************************************************
//  sgcRazorMvcWeb - sgcHTML Razor layer demo (ASP.NET Core MVC)
//
//  Controllers + Razor views over an EF Core SQLite database, using the
//  sgcHTML Tag Helpers and fluent helpers:
//    - Orders:    <sgc-grid> with server paging, sort, filters, search,
//                 export and push-url; edit in an <sgc-dialog>.
//    - New order: the same form built with the fluent field helpers.
//    - Customers: the fluent Html.Sgc().Grid<T>() bound to an in memory list.
//    - Dashboard: static, polling and live (WebSocket push) charts.
// ***************************************************************************
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
// sgc
using esegece.sgcWebSockets.AspNetCore;
using esegece.sgcWebSockets.AspNetCore.Razor;
using RazorMvcDemo.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
// The MVC app owns "/", sgcHTML serves its assets and the /ws push channel.
builder.Services.AddSgcHtml(o => o.ServeRootPage = false);
// SgcDataRequest model binder, grid state protection, the /sgcHTML.css bundle.
builder.Services.AddSgcHtmlRazor();

builder.Services.AddDbContext<ShopDb>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Shop")));
// Pushes a new point to every open live chart once a second.
builder.Services.AddHostedService<LiveSalesFeed>();

var app = builder.Build();

// The SQLite file is created and seeded on the first run; delete orders.db to reset it.
using (var scope = app.Services.CreateScope())
{
    ShopSeeder.Seed(scope.ServiceProvider.GetRequiredService<ShopDb>());
}

// One fixed culture, so the numbers and dates the forms post parse the same on any machine.
app.UseRequestLocalization("en-US");
app.UseWebSockets();   // REQUIRED before UseSgcHtml (live charts)
app.UseSgcHtml();      // htmx, bootstrap, chart.js, /sgcHTML.css, /ws
app.MapControllerRoute("default", "{controller=Orders}/{action=Index}/{id?}");

app.Run();
