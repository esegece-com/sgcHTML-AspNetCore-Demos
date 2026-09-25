// ***************************************************************************
//  sgcERP - ERP web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\50.ERP\Source\sgcERP_Types.pas
// ***************************************************************************
using System;

namespace ERP
{
    /// <summary>ERP Demo domain error. Mirrors Delphi EERPError.</summary>
    public class EERPError : Exception
    {
        public EERPError(string message) : base(message) { }
    }

    // Delphi records are ported as mutable reference classes (the demo passes
    // them around and mutates fields), with public fields matching 1:1.

    // Role = "admin" | "user"
    public class TERPUser
    {
        public long Id;
        public string Username = "";
        public string PasswordHash = "";
        public string Role = "";
        public string DisplayName = "";
        public string Email = "";
        public DateTime CreatedAt;
    }

    // A registered WebAuthn / passkey credential. CredentialId and PublicKey are
    // base64url-encoded as produced by the WebAuthn library.
    public class TERPPasskey
    {
        public long Id;
        public long UserId;
        public string CredentialId = "";
        public string PublicKey = "";
        public string DeviceName = "";
        public long SignCount;
        public DateTime CreatedAt;
        public DateTime LastUsedAt;
    }

    public class TERPCustomer
    {
        public long Id;
        public string Code = "";
        public string Name = "";
        public string TaxID = "";
        public string Email = "";
        public string Phone = "";
        public string Address = "";
        public string City = "";
        public string Country = "";
        public string Notes = "";
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
    }

    // Same shape as TERPCustomer.
    public class TERPProvider
    {
        public long Id;
        public string Code = "";
        public string Name = "";
        public string TaxID = "";
        public string Email = "";
        public string Phone = "";
        public string Address = "";
        public string City = "";
        public string Country = "";
        public string Notes = "";
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
    }

    public class TERPProduct
    {
        public long Id;
        public string Code = "";
        public string Name = "";
        public string Description = "";
        public string Unit_ = "";
        public double Price;
        public double TaxRate;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
    }

    public class TERPInvoice
    {
        public long Id;
        public string Number = "";
        public long CustomerId;
        public DateTime IssueDate;
        public DateTime DueDate;
        public string Status = "";
        public string Currency = "";
        public string Notes = "";
        public double Subtotal;
        public double TaxRate;
        public double TaxAmount;
        public double Total;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
    }

    public class TERPInvoiceLine
    {
        public long Id;
        public long InvoiceId;
        public long ProductId;
        public string Description = "";
        public double Quantity;
        public double UnitPrice;
        public double LineTotal;
    }

    // A firewall list entry (blocked_ips / ignored_ips).
    public class TERPFirewallEntry
    {
        public string IP = "";
        public string Reason = "";
        public DateTime CreatedAt;
    }

    // One row of the audit_log table.
    public class TERPAuditRow
    {
        public long Id;
        public DateTime Ts;
        public long UserId;
        public string Username = "";
        public string Action = "";
        public string EntityType = "";
        public long EntityId;
        public string Details = "";
        public string IP = "";
    }

    // One bucket of the dashboard revenue-by-month series.
    public class TERPRevenueMonth
    {
        public string MonthLabel = "";
        public double Total;
        public int Cnt;
    }

    // A flattened invoice row for the list view (built by ListInvoices).
    public class TERPInvoiceListRow
    {
        public long Id;
        public string Number = "";
        public long CustomerId;
        public string CustomerName = "";
        public DateTime IssueDate;
        public DateTime DueDate;
        public string Status = "";
        public string Currency = "";
        public double Total;
    }

    // Server configuration. Defaults applied in the constructor so the server
    // runs out-of-the-box even when the JSON config file is absent.
    public class TERPServerConfig
    {
        public string ListenAddress = "0.0.0.0";
        public int ListenPort = 5700;
        public string DatabaseFile = "data\\erp.db";
        public string AdminUser = "admin";
        public string AdminPassword = "admin";
    }
}
