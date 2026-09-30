using System;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// sgc
using esegece.sgcWebSockets.AspNetCore.Razor;
using RazorMvcDemo.Data;
using RazorMvcDemo.Models;

namespace RazorMvcDemo.Controllers
{
    public class DashboardController : Controller
    {
        private readonly ShopDb FDb;

        public DashboardController(ShopDb db)
        {
            FDb = db;
        }

        public IActionResult Index()
        {
            // Sales by month, grouped in memory (SQLite has no portable month function in EF Core).
            var oMonths = FDb.Orders.AsNoTracking()
                .Where(o => o.Status != "Cancelled")
                .Select(o => new { o.OrderDate, o.Total })
                .AsEnumerable()
                .GroupBy(o => new DateTime(o.OrderDate.Year, o.OrderDate.Month, 1))
                .OrderBy(g => g.Key)
                .ToList();
            var oModel = new DashboardModel
            {
                Months = oMonths.Select(g => g.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture)).ToArray(),
                Sales = oMonths.Select(g => Math.Round(g.Sum(o => o.Total), 2)).ToArray(),
                Orders = oMonths.Select(g => (double)g.Count()).ToArray(),
                ByStatus = CountByStatus()
            };
            return View(oModel);
        }

        // Polled by <sgc-chart refresh-action="ByStatus" refresh-every="5s">: edit an
        // order status on the Orders page and the bars follow within 5 seconds.
        public IActionResult ByStatus()
        {
            return this.SgcChart(new SgcChartBuilder("by-status").Type("bar").Height(260)
                .Labels(ShopSeeder.Statuses)
                .Series("Orders", CountByStatus(), "#198754"));
        }

        private double[] CountByStatus()
        {
            var oCounts = FDb.Orders.AsNoTracking()
                .GroupBy(o => o.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToList();
            return ShopSeeder.Statuses
                .Select(s => (double)(oCounts.FirstOrDefault(c => c.Status == s)?.Count ?? 0))
                .ToArray();
        }
    }
}
