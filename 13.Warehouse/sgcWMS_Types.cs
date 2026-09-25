// ***************************************************************************
//  sgcWMS - warehouse management web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\13.Warehouse\sgcWMS_Types.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************

using System;

namespace WMS
{
    // Shared constants of the warehouse demo (Delphi unit-level consts).
    public static class WMSConst
    {
        // Default listen port for the standalone server. 5700-5705 are taken by
        // the sibling 60.HTML demos (ERP / AdminCRUD / LiveMonitor / Portal /
        // HTMX / Helpdesk), so the warehouse demo owns 5706.
        public const int CS_WMS_DEFAULT_PORT = 5706;

        // Brand accent. Amber, deliberately distinct from ERP blue (#0057B8),
        // AdminCRUD violet (#7C3AED) and Portal emerald (#10B981).
        public const string CS_WMS_ACCENT = "#F59E0B";
        public const string CS_WMS_ACCENT_DARK = "#B45309";

        // Roles. 'operator' is the handheld-only role: the dispatcher answers 403
        // for every back-office route it reaches for, while /hh/* stays open to
        // all three roles.
        public const string CS_ROLE_ADMIN = "admin";
        public const string CS_ROLE_SUPERVISOR = "supervisor";
        public const string CS_ROLE_OPERATOR = "operator";

        // Page size of the server-side paged lists (/products, /stock, ...).
        public const int CS_WMS_PAGE_SIZE = 25;

        // Coerce an arbitrary role string to one of the three supported values.
        public static string WMSNormalizeRole(string aRole)
        {
            string vRole = (aRole ?? "").Trim().ToLowerInvariant();
            if (vRole == CS_ROLE_ADMIN)
                return CS_ROLE_ADMIN;
            if (vRole == CS_ROLE_SUPERVISOR)
                return CS_ROLE_SUPERVISOR;
            return CS_ROLE_OPERATOR;
        }

        // True when aRole may reach the back-office routes (everything that is
        // not /hh/*, /login, /logout, /theme, /healthz or a static asset).
        public static bool WMSRoleIsBackOffice(string aRole)
        {
            string vRole = WMSNormalizeRole(aRole);
            return (vRole == CS_ROLE_ADMIN) || (vRole == CS_ROLE_SUPERVISOR);
        }

        // True when aRole may change master data / users (admin only).
        public static bool WMSRoleIsAdmin(string aRole)
        {
            return WMSNormalizeRole(aRole) == CS_ROLE_ADMIN;
        }

        // Display caption for a role.
        public static string WMSRoleCaption(string aRole)
        {
            string vRole = WMSNormalizeRole(aRole);
            if (vRole == CS_ROLE_ADMIN)
                return "Administrator";
            if (vRole == CS_ROLE_SUPERVISOR)
                return "Supervisor";
            return "Operator";
        }
    }

    /// <summary>WMS demo domain error. Mirrors Delphi EWMSError.</summary>
    public class EWMSError : Exception
    {
        public EWMSError(string message) : base(message) { }
    }

    // Delphi records are ported as mutable reference classes (the demo passes
    // them around and mutates fields), with public fields matching 1:1.

    // Role = 'admin' | 'supervisor' | 'operator'
    public class TWMSUser
    {
        public long Id;
        public string Username = "";
        public string PasswordHash = "";
        public string Role = "";
        public string DisplayName = "";
        public DateTime CreatedAt;
    }

    // WebAuthn credential, one row of the passkeys table.
    public class TWMSPasskey
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

    public class TWMSProduct
    {
        public long Id;
        public string SKU = "";
        public string Barcode = "";
        public string Name = "";
        public string Description = "";
        public string UOM = "";
        public double UnitCost;
        public int MinStock;
        public string Category = "";
    }

    // Kind = 'zone' | 'aisle' | 'rack' | 'bin'. Only 'bin' rows hold stock.
    public class TWMSLocation
    {
        public long Id;
        public string Code = "";
        public long ParentId;
        public string Zone = "";
        public string Aisle = "";
        public string Rack = "";
        public string Bin = "";
        public string Kind = "";
        public int Capacity;
    }

    // Status = 'draft' | 'sent' | 'receiving' | 'closed'
    public class TWMSPurchaseOrder
    {
        public long Id;
        public long SupplierId;
        public string SupplierName = "";
        public string Reference = "";
        public string Status = "";
        public DateTime ExpectedAt;
        public DateTime CreatedAt;
    }

    // Status = 'new' | 'picking' | 'packed' | 'shipped'
    public class TWMSSalesOrder
    {
        public long Id;
        public long CustomerId;
        public string CustomerName = "";
        public string CustomerCity = "";
        public string Reference = "";
        public string Status = "";
        public string Priority = "";
        public DateTime CreatedAt;
        public DateTime ShippedAt;
    }

    // Status = 'open' | 'closed'
    public class TWMSStockCount
    {
        public long Id;
        public string Reference = "";
        public string Status = "";
        public DateTime CreatedAt;
        public DateTime ClosedAt;
    }

    // One bucket of a dashboard time series. Received/Shipped are the two
    // movement counts the dashboard chart plots against BucketLabel.
    public class TWMSActivityPoint
    {
        public string BucketLabel = "";
        public DateTime BucketStart;
        public int Received;
        public int Shipped;
    }

    // Dashboard headline numbers, filled in a single DB round trip.
    public class TWMSDashboardStats
    {
        public int Products;
        public int StockUnits;
        public double StockValue;
        public int BelowMin;
        public int OpenPOs;
        public int OpenSOs;
        public int PickedToday;
        public int ReceivedToday;
        public int BinsUsed;
        public int BinsTotal;
    }

    // Everything the view layer needs about the caller and the current request.
    // Passed to every Build* so page signatures stay short.
    public class TWMSPageCtx
    {
        public long UserId;
        public string Username = "";
        public string DisplayName = "";
        public string Role = "";
        public string Theme = "";      // 'light' | 'dark' | 'system'
        public string Menu = "";       // active sidebar key
        public string Flash = "";      // short flash code rendered as a toast
        public string Error = "";      // inline error, rendered as a danger alert
        public string Company = "";
    }

    // Server configuration. Defaults are applied in the constructor so the
    // server runs out-of-the-box even when the JSON config file is absent.
    public class TWMSServerConfig
    {
        public string ListenAddress = "0.0.0.0";
        public int ListenPort = WMSConst.CS_WMS_DEFAULT_PORT;
        public string DatabaseFile = "data\\wms.db";
        public string AdminUser = "admin";
        public string AdminPassword = "admin";
        public string CompanyName = "Northwind Distribution";
    }
}
