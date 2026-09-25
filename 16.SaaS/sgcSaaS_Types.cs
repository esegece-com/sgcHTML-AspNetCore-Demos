// ***************************************************************************
//  sgcSaaS - multi-tenant SaaS control plane demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\16.SaaS\sgcSaaS_Types.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
//  The Delphi unit-level constants and the two timestamp helpers live in the
//  static SaaSTypes class; the other files pull them in with
//  'using static SaaS.SaaSTypes;' so the call sites read exactly like the
//  Delphi ones.

using System;
using System.Globalization;

namespace SaaS
{
    /// <summary>SaaS demo domain error. Mirrors Delphi ESaaSError.</summary>
    public class ESaaSError : Exception
    {
        public ESaaSError(string message) : base(message) { }
    }

    // Unit-level constants + helper functions of sgcSaaS_Types.pas.
    public static class SaaSTypes
    {
        public const int CS_SAAS_DEFAULT_PORT = 5709;
        public const string CS_SAAS_ACCENT = "#0891B2";

        // Tenant lifecycle states.
        public const string CS_TENANT_TRIAL = "trial";
        public const string CS_TENANT_ACTIVE = "active";
        public const string CS_TENANT_PAST_DUE = "past_due";
        public const string CS_TENANT_SUSPENDED = "suspended";

        // Vendor-side roles (users.tenant_id IS NULL).
        public const string CS_ROLE_SUPERADMIN = "superadmin";
        public const string CS_ROLE_SUPPORT = "support";
        // Tenant-side roles (users.tenant_id = the tenant).
        public const string CS_ROLE_OWNER = "owner";
        public const string CS_ROLE_ADMIN = "admin";
        public const string CS_ROLE_MEMBER = "member";
        public const string CS_ROLE_READONLY = "readonly";

        // Fixed-position parser for the TEXT timestamps this demo stores
        // ('yyyy-mm-dd"T"hh:nn:ss'). Never use DateTime.Parse on them: it honours
        // the current culture and fails silently outside US date formats.
        public static DateTime ParseSaaSTimestamp(string aValue)
        {
            DateTime vResult = DateTime.MinValue;
            if (string.IsNullOrEmpty(aValue))
                return vResult;
            if (aValue.Length < 10)
                return vResult;

            int vY, vM, vD, vH, vN, vS;
            if (!TryPart(aValue, 0, 4, out vY))
                return vResult;
            if (!TryPart(aValue, 5, 2, out vM))
                return vResult;
            if (!TryPart(aValue, 8, 2, out vD))
                return vResult;
            if (!TryEncodeDate(vY, vM, vD, out vResult))
                return DateTime.MinValue;
            if (aValue.Length < 19)
                return vResult;
            if (!TryPart(aValue, 11, 2, out vH))
                return vResult;
            if (!TryPart(aValue, 14, 2, out vN))
                return vResult;
            if (!TryPart(aValue, 17, 2, out vS))
                return vResult;
            if ((vH < 0) || (vH > 23) || (vN < 0) || (vN > 59) || (vS < 0) || (vS > 59))
                return vResult;
            return vResult.AddHours(vH).AddMinutes(vN).AddSeconds(vS);
        }

        // Inverse of ParseSaaSTimestamp. Returns "" for a zero date.
        public static string FormatSaaSTimestamp(DateTime aValue)
        {
            if (aValue == DateTime.MinValue)
                return "";
            return aValue.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static bool TryPart(string aValue, int aStart, int aLen, out int aResult)
        {
            aResult = 0;
            if (aStart + aLen > aValue.Length)
                return false;
            return int.TryParse(aValue.Substring(aStart, aLen), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out aResult);
        }

        private static bool TryEncodeDate(int aYear, int aMonth, int aDay, out DateTime aResult)
        {
            aResult = DateTime.MinValue;
            if ((aYear < 1) || (aYear > 9999) || (aMonth < 1) || (aMonth > 12) || (aDay < 1))
                return false;
            if (aDay > DateTime.DaysInMonth(aYear, aMonth))
                return false;
            aResult = new DateTime(aYear, aMonth, aDay);
            return true;
        }
    }

    // Delphi records are ported as mutable reference classes (the demo passes
    // them around and mutates fields), with public fields matching 1:1.

    // A user account. TenantId = 0 means vendor staff (superadmin / support);
    // any other value scopes the account to exactly one tenant.
    public class TSaaSUser
    {
        public long Id;
        public long TenantId;
        public string Username = "";
        public string Email = "";
        public string PasswordHash = "";
        public string Role = "";
        public string DisplayName = "";
        public string Status = "";
        public DateTime LastLoginAt;
        public DateTime CreatedAt;
        public DateTime VerifiedAt;
        public string VerifyToken = "";
        public DateTime VerifyExpiresAt;
    }

    // A registered WebAuthn / passkey credential (base64url encoded).
    public class TSaaSPasskey
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

    // One customer organisation. PlanCode / PlanName / UserCount / ProjectCount
    // are joined/aggregated for the vendor console; they are not columns.
    public class TSaaSTenant
    {
        public long Id;
        public string Slug = "";
        public string Name = "";
        public long PlanId;
        public string Status = "";
        public DateTime TrialEndsAt;
        public DateTime CreatedAt;
        public int OnboardingStep;
        public string PlanCode = "";
        public string PlanName = "";
        public double PlanPrice;
        public int UserCount;
        public int ProjectCount;
    }

    public class TSaaSPlan
    {
        public long Id;
        public string Code = "";
        public string Name = "";
        public double PriceMonthly;
        public int MaxUsers;
        public int MaxProjects;
        public int MaxStorageMB;
        public string FeaturesJSON = "";
        public int SortOrder;
    }

    public class TSaaSSubscription
    {
        public long Id;
        public long TenantId;
        public long PlanId;
        public DateTime StartedAt;
        public DateTime RenewsAt;
        public DateTime CancelledAt;
        public string Status = "";
    }

    public class TSaaSInvoice
    {
        public long Id;
        public long TenantId;
        public string Number = "";
        public DateTime PeriodStart;
        public DateTime PeriodEnd;
        public double Subtotal;
        public double Tax;
        public double Total;
        public string Status = "";
        public DateTime IssuedAt;
        public DateTime PaidAt;
    }

    public class TSaaSInvoiceLine
    {
        public long Id;
        public long InvoiceId;
        public string Description = "";
        public double Qty;
        public double UnitPrice;
        public double LineTotal;
    }

    public class TSaaSProject
    {
        public long Id;
        public long TenantId;
        public string Name = "";
        public string Description = "";
        public string Status = "";
        public long OwnerId;
        public string OwnerName = "";
        public DateTime CreatedAt;
        public int TaskCount;
        public int DoneCount;
    }

    public class TSaaSTask
    {
        public long Id;
        public long TenantId;
        public long ProjectId;
        public string ProjectName = "";
        public string Title = "";
        public string Status = "";
        public long AssigneeId;
        public string AssigneeName = "";
        public DateTime DueAt;
        public DateTime CreatedAt;
    }

    // A pending or accepted team invitation. Token is an opaque 32-byte random
    // hex value; it is single use (AcceptedAt) and expiring (ExpiresAt).
    public class TSaaSInvitation
    {
        public long Id;
        public long TenantId;
        public string Email = "";
        public string Role = "";
        public string Token = "";
        public DateTime ExpiresAt;
        public DateTime AcceptedAt;
        public long InvitedBy;
        public string InvitedByName = "";
        public DateTime CreatedAt;
    }

    public class TSaaSNotificationRow
    {
        public long Id;
        public long TenantId;
        public long UserId;
        public string Title = "";
        public string Body = "";
        public string Kind = "";
        public DateTime ReadAt;
        public DateTime CreatedAt;
    }

    public class TSaaSAuditRow
    {
        public long Id;
        public long TenantId;
        public long UserId;
        public string Action = "";
        public string Entity = "";
        public long EntityId;
        public string Detail = "";
        public string IP = "";
        public DateTime CreatedAt;
        public string UserName = "";
        public string TenantName = "";
    }

    public class TSaaSUsageMetric
    {
        public long Id;
        public long TenantId;
        public string Metric = "";
        public double Value;
        public DateTime RecordedAt;
    }

    public class TSaaSFeatureFlag
    {
        public long Id;
        public long TenantId;
        public string Flag = "";
        public bool Enabled;
        public string Scope = "";
    }

    // One bucket of a monthly series (MRR, signups, usage). MonthLabel is 'yyyy-mm'.
    public class TSaaSMonthPoint
    {
        public string MonthLabel = "";
        public double Value;
        public int Cnt;
    }

    // Live plan-limit picture for one tenant. Everything here is counted with a
    // tenant_id bind; nothing is derived from the request.
    public class TSaaSPlanUsage
    {
        public string PlanCode = "";
        public string PlanName = "";
        public int Users;
        public int MaxUsers;
        public int Projects;
        public int MaxProjects;
        public double StorageMB;
        public int MaxStorageMB;
        public double ApiCalls;
    }

    // Aggregated numbers for the vendor console home page.
    public class TSaaSPlatformKPI
    {
        public int Tenants;
        public int ActiveTenants;
        public int TrialTenants;
        public int PastDueTenants;
        public int SuspendedTenants;
        public int Users;
        public int Projects;
        public double MRR;
    }

    // Server configuration. Defaults are applied in the constructor so the demo
    // runs out-of-the-box even when the JSON config file is absent.
    public class TSaaSServerConfig
    {
        public string ListenAddress = "0.0.0.0";
        public int ListenPort = SaaSTypes.CS_SAAS_DEFAULT_PORT;
        public string DatabaseFile = "data\\saas.db";
        public string AdminUser = "root";
        public string AdminPassword = "root";
    }
}
