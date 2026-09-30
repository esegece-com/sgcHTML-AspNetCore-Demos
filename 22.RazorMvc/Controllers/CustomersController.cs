using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// sgc
using RazorMvcDemo.Data;
using RazorMvcDemo.Models;

namespace RazorMvcDemo.Controllers
{
    public class CustomersController : Controller
    {
        private readonly ShopDb FDb;

        public CustomersController(ShopDb db)
        {
            FDb = db;
        }

        // 30 rows: small enough to hand the whole list to the grid (BindTo), which
        // renders every row and sorts in the browser, with no data endpoint.
        public IActionResult Index()
        {
            var oRows = FDb.Customers.AsNoTracking()
                .Select(c => new CustomerRow
                {
                    Id = c.Id,
                    Name = c.Name,
                    City = c.City,
                    Country = c.Country,
                    Orders = c.Orders.Count,
                    Revenue = c.Orders.Sum(o => o.Total)
                })
                .ToList();
            return View(oRows);
        }
    }
}
