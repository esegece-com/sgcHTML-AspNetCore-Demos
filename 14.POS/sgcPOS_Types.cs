// ***************************************************************************
//  sgcPOS - retail point of sale web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\14.POS\sgcPOS_Types.pas
// ***************************************************************************
using System;

namespace POS
{
    /// <summary>POS domain error. Mirrors Delphi EPOSError.</summary>
    public class EPOSError : Exception
    {
        public EPOSError(string message) : base(message) { }
    }

    // Compile-time constants of the till, mirroring the Delphi const block.
    public static class POSConst
    {
        // Default listen port for the standalone server. 5700-5706 are taken by
        // the sibling 60.HTML demos, so the till takes 5707.
        public const int CS_POS_DEFAULT_PORT = 5707;

        // Sale lifecycle. The spec names three persisted states; "open" is the
        // fourth, in-progress one: the row a cashier is building right now.
        // Parking it moves it to "parked", recalling it moves it back to "open",
        // taking payment moves it to "completed" and a manager-authorised refund
        // moves it to "refunded".
        public const string CS_SALE_OPEN = "open";
        public const string CS_SALE_PARKED = "parked";
        public const string CS_SALE_COMPLETED = "completed";
        public const string CS_SALE_REFUNDED = "refunded";

        public const string CS_SHIFT_OPEN = "open";
        public const string CS_SHIFT_CLOSED = "closed";

        public const string CS_ROLE_ADMIN = "admin";
        public const string CS_ROLE_MANAGER = "manager";
        public const string CS_ROLE_CASHIER = "cashier";

        public const string CS_PAY_CASH = "cash";
        public const string CS_PAY_CARD = "card";
        public const string CS_PAY_VOUCHER = "voucher";
        public const string CS_PAY_LOYALTY = "loyalty";

        // Brand accent of the till (rose).
        public const string CS_POS_ACCENT = "#E11D48";
        public const string CS_POS_ACCENT_DARK = "#BE123C";
    }

    // Delphi records are ported as mutable reference classes (the demo passes
    // them around and mutates fields), with public fields matching 1:1.
    // Delphi Currency maps to decimal, Double to double, Int64 to long.

    // Role: "admin" | "manager" | "cashier"
    public class TPOSUser
    {
        public long Id;
        public string Username = "";
        public string PasswordHash = "";
        public string Role = "";
        public string DisplayName = "";
        public string PinHash = "";
        public DateTime CreatedAt;
    }

    public class TPOSPasskey
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

    public class TPOSCategory
    {
        public long Id;
        public string Name = "";
        public string Color = "";
        public int SortOrder;
    }

    public class TPOSProduct
    {
        public long Id;
        public string Sku = "";
        public string Barcode = "";
        public string Name = "";
        public long CategoryId;
        public string CategoryName = "";
        public decimal Price;
        public decimal Cost;
        public double TaxRate; // percent, e.g. 21
        public int Stock;
        public string ImageSVG = "";
        public bool Active;
    }

    public class TPOSCustomer
    {
        public long Id;
        public string Name = "";
        public string Email = "";
        public string Phone = "";
        public int LoyaltyPoints;
        public DateTime CreatedAt;
    }

    // Kind: "percent" | "amount" | "bundle"
    public class TPOSPromotion
    {
        public long Id;
        public string Name = "";
        public string Kind = "";
        public double Value;
        public long ProductId;
        public long CategoryId;
        public DateTime StartsAt;
        public DateTime EndsAt;
        public bool Active;
    }

    public class TPOSSaleLine
    {
        public long Id;
        public long SaleId;
        public long ProductId;
        public string ProductName = "";
        public string Sku = "";
        public double Qty;
        public decimal UnitPrice;
        public decimal Discount; // line-level discount, already money
        public double TaxRate;
        public decimal LineTotal; // Qty * UnitPrice - Discount, net of tax
    }

    public class TPOSSale
    {
        public long Id;
        public string Reference = "";
        public long UserId;
        public string UserName = "";
        public long CustomerId;
        public string CustomerName = "";
        public long ShiftId;
        public decimal Subtotal;
        public decimal Discount;
        public decimal Tax;
        public decimal Total;
        public string Status = "";
        public DateTime CreatedAt;
        public int LineCount;
    }

    public class TPOSPayment
    {
        public long Id;
        public long SaleId;
        public string Method = "";
        public decimal Amount;
        public decimal ChangeGiven;
        public DateTime CreatedAt;
    }

    public class TPOSShift
    {
        public long Id;
        public long UserId;
        public string UserName = "";
        public DateTime OpenedAt;
        public DateTime ClosedAt;
        public decimal OpeningFloat;
        public decimal CountedCash;
        public decimal ExpectedCash;
        public decimal Variance;
        public string Status = "";
    }

    public class TPOSAuditEntry
    {
        public long Id;
        public long UserId;
        public string UserName = "";
        public string Action = "";
        public string Entity = "";
        public long EntityId;
        public string Detail = "";
        public string IP = "";
        public DateTime CreatedAt;
    }

    // One bucket of a dashboard time series (hour of day, weekday, month).
    public class TPOSSeriesPoint
    {
        public string BucketLabel = "";
        public int BucketKey;
        public decimal Amount;
        public int Count;
    }

    // One (weekday, hour) bucket of the sales heatmap.
    public class TPOSHeatPoint
    {
        public int Weekday; // 0 = Monday
        public int Hour;
        public decimal Amount;
    }

    // Rolled-up totals used by the dashboard KPI row and the X/Z reports.
    public class TPOSTotals
    {
        public int SaleCount;
        public decimal Gross;
        public decimal NetSubtotal;
        public decimal Discount;
        public decimal Tax;
        public decimal Refunded;
        public int RefundCount;
        public decimal CashTaken;
        public decimal CardTaken;
        public decimal VoucherTaken;
        public decimal LoyaltyTaken;
        public decimal ChangeGiven;
    }

    // Server configuration. Defaults are applied in the constructor so the
    // server runs out-of-the-box even when the JSON config file is absent.
    public class TPOSServerConfig
    {
        public string ListenAddress;
        public int ListenPort;
        public string DatabaseFile;
        public string AdminUser;
        public string AdminPassword;
        // Money value above which a discount needs a manager PIN. A refund always
        // needs one, whatever its amount.
        public decimal DiscountPinThreshold;
        // Cash target of a shift, drives the shift Gauge on /shift.
        public decimal ShiftTarget;
        // Currency symbol shown on the till and on the printed receipt.
        public string CurrencySymbol;
        public string StoreName;
        public string StoreAddress;
        public string StoreTaxId;

        public TPOSServerConfig()
        {
            ListenAddress = "0.0.0.0";
            ListenPort = POSConst.CS_POS_DEFAULT_PORT;
            DatabaseFile = "data\\pos.db";
            AdminUser = "admin";
            AdminPassword = "admin";
            DiscountPinThreshold = 10.00m;
            ShiftTarget = 1200.00m;
            CurrencySymbol = "$";
            StoreName = "eSeGeCe Convenience";
            StoreAddress = "14 Market Street, Springfield";
            StoreTaxId = "VAT B-12345678";
        }
    }
}
