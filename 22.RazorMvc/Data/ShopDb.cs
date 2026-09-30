using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace RazorMvcDemo.Data
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public List<Order> Orders { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public string Reference { get; set; }
        public int CustomerId { get; set; }
        public Customer Customer { get; set; }
        public DateTime OrderDate { get; set; }
        public string Status { get; set; }
        // double, not decimal: EF Core SQLite cannot ORDER BY a decimal column.
        public double Total { get; set; }
        public bool Paid { get; set; }
    }

    public class ShopDb : DbContext
    {
        public ShopDb(DbContextOptions<ShopDb> options) : base(options)
        {
        }

        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Order> Orders => Set<Order>();
    }

    /// <summary>Creates the SQLite file and fills it with 30 customers and 200 orders.</summary>
    public static class ShopSeeder
    {
        public static readonly string[] Statuses = { "New", "Paid", "Shipped", "Delivered", "Cancelled" };

        public static void Seed(ShopDb db)
        {
            db.Database.EnsureCreated();
            if (db.Customers.Any())
            {
                return;
            }

            string[] vPrefixes = { "Acme", "Globex", "Initech", "Umbrella", "Stark", "Wayne", "Hooli", "Vandelay", "Soylent", "Tyrell" };
            string[] vSuffixes = { "Corp", "Labs", "Trading" };
            string[] vCities = { "Madrid", "Barcelona", "Paris", "Berlin", "Rome", "Lisbon" };
            string[] vCountries = { "Spain", "Spain", "France", "Germany", "Italy", "Portugal" };
            var oRandom = new Random(2026);
            for (int i = 0; i < 30; i++)
            {
                int vCity = oRandom.Next(vCities.Length);
                db.Customers.Add(new Customer
                {
                    Id = i + 1,
                    Name = vPrefixes[i % vPrefixes.Length] + " " + vSuffixes[i / vPrefixes.Length],
                    City = vCities[vCity],
                    Country = vCountries[vCity]
                });
            }

            var vStart = new DateTime(2026, 1, 1);
            for (int i = 1; i <= 200; i++)
            {
                string vStatus = Statuses[oRandom.Next(Statuses.Length)];
                db.Orders.Add(new Order
                {
                    Id = i,
                    Reference = "PO-" + (10000 + i * 7).ToString(),
                    CustomerId = oRandom.Next(30) + 1,
                    OrderDate = vStart.AddDays(oRandom.Next(270)),
                    Status = vStatus,
                    Total = Math.Round(50 + oRandom.NextDouble() * 4950, 2),
                    Paid = vStatus != "New" && vStatus != "Cancelled"
                });
            }

            db.SaveChanges();
        }
    }
}
