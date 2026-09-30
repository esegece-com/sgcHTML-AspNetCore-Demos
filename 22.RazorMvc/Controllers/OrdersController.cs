using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
// sgc
using esegece.sgcWebSockets.AspNetCore.Razor;
using RazorMvcDemo.Data;
using RazorMvcDemo.Models;

namespace RazorMvcDemo.Controllers
{
    public class OrdersController : Controller
    {
        private readonly ShopDb FDb;

        public OrdersController(ShopDb db)
        {
            FDb = db;
        }

        private IQueryable<OrderRow> Rows()
        {
            return OrderRow.From(FDb.Orders.AsNoTracking());
        }

        // The view renders page 1 inline from this query (no loading flash).
        public IActionResult Index()
        {
            return View(Rows());
        }

        // Every sort, filter, search and page click of the grid lands here.
        // The Telerik twin is Read([DataSourceRequest] request) => Json(query.ToDataSourceResult(request)).
        public IActionResult Read(SgcDataRequest request)
        {
            return this.SgcGrid(request, Rows());
        }

        // The toolbar links send format=xlsx|pdf plus the current filters and sort.
        public IActionResult Export(SgcDataRequest request)
        {
            return this.SgcGridExport(request, Rows(), "orders");
        }

        // The autocomplete sends the typed text as "query". LIKE instead of Contains
        // because SQLite translates Contains to a case sensitive instr().
        public IActionResult Customers_Search(string query)
        {
            var oItems = FDb.Customers.AsNoTracking()
                .Where(c => EF.Functions.Like(c.Name, "%" + (query ?? "") + "%"))
                .OrderBy(c => c.Name)
                .Take(10)
                .AsEnumerable()
                .Select(c => new SgcItem(c.Id.ToString(), c.Name + " (" + c.City + ")"));
            return this.SgcAutoComplete(oItems);
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            Order oOrder = FDb.Orders.AsNoTracking().FirstOrDefault(o => o.Id == id);
            if (oOrder == null)
            {
                return NotFound();
            }

            return EditForm(OrderForm.From(oOrder));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, OrderForm form)
        {
            form.Id = id;
            Order oOrder = FDb.Orders.Find(id);
            if (oOrder == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                // htmx swaps the form in place, with the errors under each input.
                return EditForm(form);
            }

            form.CopyTo(oOrder);
            FDb.SaveChanges();
            // Closes the dialog and reloads the grid named "orders" (HX-Trigger).
            return this.SgcCloseDialog("edit-order", refresh: "orders");
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new OrderForm { OrderDate = DateTime.Today, Status = "New" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(OrderForm form)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.CustomerName = CustomerName(form.CustomerId);
                return PartialView("_CreateForm", form);
            }

            var oOrder = new Order();
            form.CopyTo(oOrder);
            FDb.Orders.Add(oOrder);
            FDb.SaveChanges();
            // HX-Redirect: htmx navigates the whole page, like a 302 after a classic POST.
            return this.SgcRedirect(Url.Action("Index", new { q = oOrder.Reference }));
        }

        private IActionResult EditForm(OrderForm form)
        {
            // The autocomplete shows the customer name while the form posts CustomerId.
            ViewBag.CustomerName = CustomerName(form.CustomerId);
            return PartialView("_EditForm", form);
        }

        private string CustomerName(int? id)
        {
            return id == null ? null : FDb.Customers.AsNoTracking().Where(c => c.Id == id).Select(c => c.Name).FirstOrDefault();
        }
    }
}
