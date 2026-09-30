using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using RazorMvcDemo.Data;

namespace RazorMvcDemo.Models
{
    /// <summary>
    /// One row of the orders grid. The grid reads an IQueryable of it, so EF Core
    /// translates the sort, filters, search and paging of the projection to SQL.
    /// </summary>
    public class OrderRow
    {
        public int Id { get; set; }
        public string Reference { get; set; }
        public string Customer { get; set; }
        public string City { get; set; }
        [Display(Name = "Date")]
        public DateTime OrderDate { get; set; }
        public string Status { get; set; }
        public double Total { get; set; }
        public bool Paid { get; set; }

        // Rendered with Encode(false). Not part of the SQL projection, so it is
        // neither sortable nor filterable. The attributes are the ones that the
        // sgc-dialog="edit-order" attribute writes on a Razor element.
        public string Edit => "<a href=\"/Orders/Edit/" + Id + "\" hx-get=\"/Orders/Edit/" + Id +
            "\" hx-target=\"#edit-order-body\" hx-swap=\"innerHTML\" data-sgc-dialog=\"edit-order\"" +
            " class=\"btn btn-sm btn-outline-primary sgc-edit\">Edit</a>";

        public static IQueryable<OrderRow> From(IQueryable<Order> orders)
        {
            return orders.Select(o => new OrderRow
            {
                Id = o.Id,
                Reference = o.Reference,
                Customer = o.Customer.Name,
                City = o.Customer.City,
                OrderDate = o.OrderDate,
                Status = o.Status,
                Total = o.Total,
                Paid = o.Paid
            });
        }
    }

    /// <summary>The edit and create form. [Display], [Required] and [StringLength] drive the sgc inputs.</summary>
    public class OrderForm
    {
        public int Id { get; set; }

        [Required, StringLength(12)]
        public string Reference { get; set; }

        // The autocomplete posts the key here; the visible text is only for display.
        [Required(ErrorMessage = "Pick a customer from the list.")]
        [Display(Name = "Customer")]
        public int? CustomerId { get; set; }

        [Display(Name = "Order date")]
        public DateTime OrderDate { get; set; }

        [Required]
        public string Status { get; set; }

        [Range(1, 100000)]
        public double Total { get; set; }

        [Display(Name = "Paid")]
        public bool Paid { get; set; }

        public static IEnumerable<SelectListItem> StatusItems =>
            ShopSeeder.Statuses.Select(s => new SelectListItem(s, s));

        public static OrderForm From(Order order)
        {
            return new OrderForm
            {
                Id = order.Id,
                Reference = order.Reference,
                CustomerId = order.CustomerId,
                OrderDate = order.OrderDate,
                Status = order.Status,
                Total = order.Total,
                Paid = order.Paid
            };
        }

        public void CopyTo(Order order)
        {
            order.Reference = Reference;
            order.CustomerId = CustomerId.Value;
            order.OrderDate = OrderDate;
            order.Status = Status;
            order.Total = Total;
            order.Paid = Paid;
        }
    }

    public class CustomerRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public int Orders { get; set; }
        [Display(Name = "Revenue")]
        public double Revenue { get; set; }
    }

    public class DashboardModel
    {
        public string[] Months { get; set; }
        public double[] Sales { get; set; }
        public double[] Orders { get; set; }
        public double[] ByStatus { get; set; }
    }
}
