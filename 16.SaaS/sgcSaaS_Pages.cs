// ***************************************************************************
//  sgcSaaS - multi-tenant SaaS control plane demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\16.SaaS\sgcSaaS_Pages.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
//  Node/component view layer. Zero hand-written HTML: every page is built from
//  TsgcHTMLComponent_* objects with the node layer used only as glue. The
//  Delphi records become mutable classes with public fields, the Delphi
//  TDataSet parameters become System.Data.DataTable (a nil DataSet stays null),
//  and .NET being GC-managed the Delphi .Free calls are dropped.

using System;
using System.Data;
using System.Globalization;
using esegece.sgcWebSockets;
using static SaaS.SaaSTypes;

namespace SaaS
{
    // Everything the shared shell needs to render the chrome for one request.
    // TenantId comes from the session, never from the request.
    public class TSaaSPageContext
    {
        public string Username = "";
        public string DisplayName = "";
        public string Role = "";
        public long TenantId;
        public string TenantName = "";
        public string TenantSlug = "";
        public bool Impersonating;
        public string ImpersonatorName = "";
        public int Unread;
        public string ActiveMenu = "";
        public string Theme = "";
        public string Flash = "";
        public string ErrorMsg = "";
    }

    public class TSaaSDashboardVM
    {
        public TSaaSTenant Tenant = new TSaaSTenant();
        public TSaaSPlanUsage Usage = new TSaaSPlanUsage();
        public int TotalTasks;
        public int OpenTasks;
        public int DoneTasks;
        public TSaaSProject[] Projects = new TSaaSProject[0];
        public TSaaSAuditRow[] Audit = new TSaaSAuditRow[0];
        public TSaaSUser[] Team = new TSaaSUser[0];
        public double[] ApiSeries = new double[0];
        public TSaaSMonthPoint[] UsageMonths = new TSaaSMonthPoint[0];
        public int TrialDaysLeft;
        public int PendingInvites;
    }

    public class TSaaSBillingVM
    {
        public TSaaSTenant Tenant = new TSaaSTenant();
        public TSaaSPlan Plan = new TSaaSPlan();
        public TSaaSPlan[] Plans = new TSaaSPlan[0];
        public TSaaSPlanUsage Usage = new TSaaSPlanUsage();
        public TSaaSInvoice[] Invoices = new TSaaSInvoice[0];
        public TSaaSSubscription Sub = new TSaaSSubscription();
        public TSaaSMonthPoint[] Spend = new TSaaSMonthPoint[0];
    }

    public class TSaaSTeamVM
    {
        public TSaaSUser[] Members = new TSaaSUser[0];
        public TSaaSInvitation[] Invites = new TSaaSInvitation[0];
        public TSaaSPlanUsage Usage = new TSaaSPlanUsage();
        public string InviteURL = "";
        public string InviteEmail = "";
        public bool CanManage;
    }

    // The evidence shown on /app/isolation. Everything here was produced by the
    // same code paths the workspace uses.
    public class TSaaSIsolationVM
    {
        public long SessionTenantId;
        public string TenantName = "";
        public string SQLText = "";
        public string CountSQLText = "";
        public int VisibleProjects;
        public int TotalProjects;
        public int VisibleTasks;
        public int TotalTasks;
        public long ForeignProjectId;
        public long ForeignOwnerId;
        public string ForeignOwnerName = "";
        public bool ForeignFetchRefused;
        public string RefusalReason = "";
        public string GuardReason = "";
        public TSaaSProject[] Rows = new TSaaSProject[0];
    }

    public class TSaaSAdminVM
    {
        public TSaaSPlatformKPI KPI = new TSaaSPlatformKPI();
        public TSaaSMonthPoint[] MRR = new TSaaSMonthPoint[0];
        public TSaaSMonthPoint[] Signups = new TSaaSMonthPoint[0];
        public TSaaSMonthPoint[] Churn = new TSaaSMonthPoint[0];
        public TSaaSTenant[] Tenants = new TSaaSTenant[0];
        public string Search = "";
        public string StatusFilter = "";
    }

    public class TSaaSTenantDetailVM
    {
        public TSaaSTenant Tenant = new TSaaSTenant();
        public TSaaSPlan Plan = new TSaaSPlan();
        public TSaaSUser[] Users = new TSaaSUser[0];
        public TSaaSInvoice[] Invoices = new TSaaSInvoice[0];
        public TSaaSMonthPoint[] UsageMonths = new TSaaSMonthPoint[0];
        public TSaaSSubscription Sub = new TSaaSSubscription();
        public bool CanImpersonate;
    }

    // Node/component view layer. Zero hand-written HTML: every page is built
    // from TsgcHTMLComponent_* objects with the node layer used only as glue.
    public class TSaaSPages
    {
        // -------------------------------------------------------------------
        // unit constants (copied verbatim from the Delphi implementation)
        // -------------------------------------------------------------------

        // The two asset URLs TsgcHTMLComponent_Site hard-codes. The demo must run
        // offline, so the rendered page is rewritten to the locally served copies
        // that come out of the linked sgcHTMLResources.RES.
        private const string CS_CDN_CSS =
            "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css";

        private const string CS_CDN_JS =
            "https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/js/bootstrap.bundle.min.js";

        private const string CS_LOCAL_CSS = "/bootstrap.min.css";
        private const string CS_LOCAL_JS = "/bootstrap.bundle.min.js";

        // Inline SVG icon marks (trusted constants). No icon font, no CDN.
        private const string CS_ICO_HOME = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M8 1 1 7h2v7h4V10h2v4h4V7h2z\"/></svg>";

        private const string CS_ICO_GRID = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M1 1h6v6H1zm8 0h6v6H9zM1 9h6v6H1zm8 0h6v6H9z\"/></svg>";

        private const string CS_ICO_TASK = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M2 3h12v2H2zm0 4h12v2H2zm0 4h8v2H2z\"/></svg>";

        private const string CS_ICO_TEAM = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M5.5 8a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM11 8a2 2 0 1 0 0-4 2 2 0 0 0 0 4zM1 14c0-2.2 2-4 4.5-4S10 11.8 10 14zm10.5 0c0-1.3-.5-2.4-1.3-3.2.3-.1.7-.1 1.1-.1 2 0 3.7 1.4 3.7 3.3z\"/></svg>";

        private const string CS_ICO_SHIELD = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M8 0 2 2v6c0 4 3.2 7.2 6 8 2.8-.8 6-4 6-8V2z\"/></svg>";

        private const string CS_ICO_CARD = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M1 3h14v3H1zm0 5h14v5H1zm2 2h4v2H3z\"/></svg>";

        private const string CS_ICO_GEAR = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M8 5a3 3 0 1 0 0 6 3 3 0 0 0 0-6zm7 3-1.6-.6a5.6 5.6 0 0 0-.5-1.3l.7-1.6-1.1-1.1-1.6.7a5.6 5.6 0 0 0-1.3-.5L9 1H7l-.6 1.6a5.6 5.6 0 0 0-1.3.5l-1.6-.7-1.1 " +
            "1.1.7 1.6a5.6 5.6 0 0 0-.5 1.3L1 7v2l1.6.6c.1.5.3.9.5 1.3l-.7 1.6 1.1 1.1 1.6-.7c.4.2.8.4 1.3.5L7 15h2l.6-1.6c.5-.1.9-.3 1.3-.5l1.6.7 1.1-1.1-.7-1.6c.2-.4.4-.8.5-1.3L15 9z\"/></svg>";

        private const string CS_ICO_BELL = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M8 16a2 2 0 0 0 2-2H6a2 2 0 0 0 2 2zm5-5V7a5 5 0 0 0-4-4.9V1.5a1 1 0 1 0-2 0v.6A5 5 0 0 0 3 7v4l-2 2h14z\"/></svg>";

        private const string CS_ICO_LIST = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M1 2h2v2H1zm4 0h10v2H5zM1 7h2v2H1zm4 0h10v2H5zM1 12h2v2H1zm4 0h10v2H5z\"/></svg>";

        private const string CS_ICO_LOCK = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M4 7V5a4 4 0 1 1 8 0v2h1v8H3V7zm2 0h4V5a2 2 0 1 0-4 0z\"/></svg>";

        private const string CS_ICO_DB = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><ellipse cx=\"8\" cy=\"3\" rx=\"6\" ry=\"2\"/><path d=\"M2 5v3c0 1.1 2.7 2 6 2s6-.9 6-2V5c0 1.1-2.7 2-6 2S2 6.1 2 5zm0 5v3c0 1.1 2.7 2 6 2s6-.9 6-2v-3c0 1.1-2.7 2-6 2s-6-.9-6-2z\"/></svg>";

        private const string CS_ICO_BUILDING = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M2 15V1h7v4h5v10zM4 3v2h3V3zm0 4v2h3V7zm0 4v2h3v-2zm6 0v2h3v-2zm0-4v2h3V7z\"/></svg>";

        private const string CS_ICO_FLAG = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M3 1v14h2V9h8L11 5l2-4z\"/></svg>";

        private const string CS_ICO_ROCKET = "<svg width=\"16\" height=\"16\" viewBox=\"0 0 16 16\" " +
            "fill=\"currentColor\"><path d=\"M14 1c-4 0-6.6 1.8-8.4 4.6L3 5 1 7l2.6 1.3.5 2.6L5.4 12l2.6.5L9.3 15l2-2-.6-2.6C13.2 8.6 15 6 15 2z\"/></svg>";

        // A small placeholder poster/clip used by the onboarding Video component and
        // the tenant logo Image. Both are inline data URIs so nothing leaves the box.
        private const string CS_POSTER_SVG = "data:image/svg+xml;utf8," +
            "%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 640 360%22%3E" +
            "%3Crect width=%22640%22 height=%22360%22 fill=%22%230891B2%22/%3E" +
            "%3Ctext x=%22320%22 y=%22190%22 font-family=%22Arial%22 font-size=%2234%22 " +
            "fill=%22%23ffffff%22 text-anchor=%22middle%22%3EProduct tour%3C/text%3E%3C/svg%3E";

        private const string CS_LOGO_SVG = "data:image/svg+xml;utf8," +
            "%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 96 96%22%3E" +
            "%3Crect width=%2296%22 height=%2296%22 rx=%2216%22 fill=%22%230891B2%22/%3E" +
            "%3Ctext x=%2248%22 y=%2264%22 font-family=%22Arial%22 font-weight=%22900%22 " +
            "font-size=%2246%22 fill=%22%23ffffff%22 text-anchor=%22middle%22%3ES%3C/text%3E%3C/svg%3E";

        private static readonly string[] CS_PERMISSIONS = { "project.view", "project.edit",
            "task.edit", "team.manage", "billing.manage", "settings.manage" };

        private static readonly string[] CS_PERMISSION_NAMES = { "View projects",
            "Create and edit projects", "Work on tasks", "Manage the team",
            "Manage billing", "Change workspace settings" };

        private static readonly string[] CS_PERMISSION_CATS = { "Work", "Work", "Work",
            "Administration", "Administration", "Administration" };

        private static readonly string[] CS_TENANT_ROLES = { CS_ROLE_OWNER, CS_ROLE_ADMIN,
            CS_ROLE_MEMBER, CS_ROLE_READONLY };

        private static readonly string[] CS_TENANT_ROLE_NAMES = { "Owner", "Admin",
            "Member", "Read only" };

        // The Delphi GFmt is an en-US TFormatSettings with '.' decimal and ','
        // thousand separators; the managed equivalent is the invariant culture.
        private static readonly CultureInfo GFmt = CultureInfo.InvariantCulture;

        // -------------------------------------------------------------------
        // small local helpers (mirror the Delphi unit-level free functions)
        // -------------------------------------------------------------------

        // Delphi Length() on a TArray<T>: a nil array reads as zero length.
        private static int Len<T>(T[] aValue)
        {
            return aValue == null ? 0 : aValue.Length;
        }

        // Delphi SameText: case-insensitive comparison.
        private static bool SameText(string aLeft, string aRight)
        {
            return string.Equals(aLeft ?? "", aRight ?? "",
                StringComparison.OrdinalIgnoreCase);
        }

        // Delphi Pos: 1-based index of the substring, 0 when absent.
        private static int PosStr(string aSubStr, string aValue)
        {
            if (string.IsNullOrEmpty(aValue) || string.IsNullOrEmpty(aSubStr))
                return 0;
            return aValue.IndexOf(aSubStr, StringComparison.Ordinal) + 1;
        }

        // Delphi Copy: 1-based, clamped, never raises.
        private static string CopyStr(string aValue, int aIndex, int aCount)
        {
            if (string.IsNullOrEmpty(aValue) || (aCount <= 0))
                return "";
            int vStart = aIndex - 1;
            if (vStart < 0)
                vStart = 0;
            if (vStart >= aValue.Length)
                return "";
            int vLen = aCount;
            if (vLen > aValue.Length - vStart)
                vLen = aValue.Length - vStart;
            return aValue.Substring(vStart, vLen);
        }

        // Delphi StringReplace with [rfReplaceAll, rfIgnoreCase].
        private static string ReplaceTextAll(string aValue, string aFrom, string aTo)
        {
            if (string.IsNullOrEmpty(aValue) || string.IsNullOrEmpty(aFrom))
                return aValue ?? "";
            var oResult = new System.Text.StringBuilder();
            int vPos = 0;
            while (vPos < aValue.Length)
            {
                int vFound = aValue.IndexOf(aFrom, vPos, StringComparison.OrdinalIgnoreCase);
                if (vFound < 0)
                {
                    oResult.Append(aValue, vPos, aValue.Length - vPos);
                    break;
                }
                oResult.Append(aValue, vPos, vFound - vPos);
                oResult.Append(aTo);
                vPos = vFound + aFrom.Length;
            }
            return oResult.ToString();
        }

        private static string Esc(string aValue)
        {
            string vResult = (aValue ?? "").Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            vResult = vResult.Replace("'", "&#39;");
            return vResult;
        }

        private static string Money(double aValue)
        {
            return aValue.ToString("#,##0.00", GFmt);
        }

        private static string Num(double aValue)
        {
            return aValue.ToString("#,##0", GFmt);
        }

        private static string DateStr(DateTime aValue)
        {
            if (aValue == DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd", GFmt);
        }

        private static string StampStr(DateTime aValue)
        {
            if (aValue == DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm", GFmt);
        }

        private static TsgcHTMLBadgeStyle StatusBadge(string aStatus)
        {
            if (SameText(aStatus, CS_TENANT_ACTIVE) || SameText(aStatus, "paid") ||
                SameText(aStatus, "done"))
                return TsgcHTMLBadgeStyle.bgSuccess;
            else if (SameText(aStatus, CS_TENANT_TRIAL) || SameText(aStatus, "open") ||
                SameText(aStatus, "doing"))
                return TsgcHTMLBadgeStyle.bgInfo;
            else if (SameText(aStatus, CS_TENANT_PAST_DUE) ||
                SameText(aStatus, "overdue"))
                return TsgcHTMLBadgeStyle.bgWarning;
            else if (SameText(aStatus, CS_TENANT_SUSPENDED))
                return TsgcHTMLBadgeStyle.bgDanger;
            else
                return TsgcHTMLBadgeStyle.bgSecondary;
        }

        // Percentage of a limit, clamped to 0..100. A zero/absent limit reads as 0.
        private static int PercentOf(double aValue, int aLimit)
        {
            if (aLimit <= 0)
                return 0;
            else
                return Math.Max(0, Math.Min(100,
                    (int)Math.Round((aValue / aLimit) * 100, MidpointRounding.ToEven)));
        }

        // One KPI card wrapped in a responsive grid column.
        private static string StatCol(string aTitle, string aValue, string aIcon,
            TsgcHTMLStatColor aColor, TsgcHTMLStatTrend aTrend,
            string aTrendValue, string aColClass, string aFooter)
        {
            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = aColClass;
            var oStat = new TsgcHTMLComponent_StatCard();
            oStat.Title = aTitle;
            oStat.Value = aValue;
            oStat.Icon = aIcon;
            oStat.Color = aColor;
            oStat.Trend = aTrend;
            oStat.TrendValue = aTrendValue;
            oStat.FooterText = aFooter;
            oStat.CSSClass = "h-100";
            oCol.AddRaw(oStat.HTML);
            return oCol.HTML;
        }

        // A labelled progress bar for one plan limit.
        private static string LimitBar(string aLabel, double aValue, int aLimit,
            string aUnit)
        {
            int vPct = PercentOf(aValue, aLimit);
            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "mb-3";
            var oHead = new TsgcHTMLContainer("div");
            oHead.CSSClass = "d-flex justify-content-between small mb-1";
            oHead.Children.AddElement("span", aLabel, "fw-semibold");
            if (aLimit > 0)
                oHead.Children.AddElement("span", Num(aValue) + " / " + Num(aLimit) + " " +
                    aUnit, "text-muted");
            else
                oHead.Children.AddElement("span", Num(aValue) + " " + aUnit, "text-muted");
            oWrap.Add(oHead);

            var oBar = new TsgcHTMLComponent_ProgressBar();
            oBar.Value = vPct;
            oBar.Max = 100;
            oBar.ShowLabel = true;
            oBar.CSSHeight = "14px";
            if (vPct >= 90)
                oBar.ColorStyle = TsgcHTMLColor.hcDanger;
            else if (vPct >= 70)
                oBar.ColorStyle = TsgcHTMLColor.hcWarning;
            else
                oBar.ColorStyle = TsgcHTMLColor.hcInfo;
            oWrap.AddRaw(oBar.HTML);
            return oWrap.HTML;
        }

        // A read-only code block. The text goes through the node layer, so anything
        // inside it is escaped.
        private static string CodeBlock(string aText)
        {
            var oPre = new TsgcHTMLContainer("pre");
            oPre.CSSClass = "bg-body-secondary border rounded p-3 small mb-0";
            oPre.Style = "white-space:pre-wrap;word-break:break-word;";
            oPre.AddText(aText);
            return oPre.HTML;
        }

        // Break a single-line SQL statement onto readable lines at the major clauses.
        private static string PrettySQL(string aSQL)
        {
            string[] vKeywords = { " FROM ", " WHERE ", " ORDER BY ",
                " GROUP BY ", " LEFT JOIN ", " INNER JOIN ", " JOIN ", " LIMIT ",
                " VALUES " };
            string vResult = aSQL ?? "";
            for (int vI = 0; vI <= vKeywords.Length - 1; vI++)
                vResult = ReplaceTextAll(vResult, vKeywords[vI],
                    Environment.NewLine + vKeywords[vI].Trim() + " ");
            return vResult;
        }

        private static string AvatarFor(string aName, TsgcHTMLAvatarSize aSize)
        {
            string vInitials = "?";
            string[] vParts = (aName ?? "").Trim().Split(' ');
            if (vParts.Length >= 2)
                vInitials = CopyStr(vParts[0], 1, 1) + CopyStr(vParts[1], 1, 1);
            else if ((vParts.Length == 1) && (vParts[0] != ""))
                vInitials = CopyStr(vParts[0], 1, 2);
            var oAvatar = new TsgcHTMLComponent_Avatar();
            oAvatar.Initials = vInitials.ToUpperInvariant();
            oAvatar.Size = aSize;
            oAvatar.Shape = TsgcHTMLAvatarShape.apCircle;
            oAvatar.Color = CS_SAAS_ACCENT;
            oAvatar.AltText = aName;
            return oAvatar.HTML;
        }

        private static string BadgeHTML(string aText, TsgcHTMLBadgeStyle aStyle,
            bool aPill)
        {
            var oBadge = new TsgcHTMLComponent_Badge();
            oBadge.Text = aText;
            oBadge.Color = aStyle;
            oBadge.Pill = aPill;
            return oBadge.HTML;
        }

        private static string ChipHTML(string aText, TsgcHTMLBadgeStyle aStyle)
        {
            var oChip = new TsgcHTMLComponent_Chip();
            oChip.Text = aText;
            oChip.Color = aStyle;
            oChip.Outline = true;
            return oChip.HTML;
        }

        private static string EmptyStateHTML(string aTitle, string aDescription,
            string aIcon, string aActionCaption, string aActionHref)
        {
            var oEmpty = new TsgcHTMLComponent_EmptyState();
            oEmpty.Title = aTitle;
            oEmpty.Description = aDescription;
            oEmpty.Icon = aIcon;
            oEmpty.ActionCaption = aActionCaption;
            oEmpty.ActionHref = aActionHref;
            oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsPrimary;
            oEmpty.Bordered = true;
            return oEmpty.HTML;
        }

        private static string BreadcrumbHTML(string[] aItems, string[] aHrefs)
        {
            var oCrumb = new TsgcHTMLComponent_Breadcrumb();
            for (int vI = 0; vI <= Len(aItems) - 1; vI++)
            {
                TsgcHTMLBreadcrumbItem oItem = oCrumb.Items.Add();
                oItem.Text = aItems[vI];
                if (vI <= Len(aHrefs) - 1)
                    oItem.Href = aHrefs[vI];
                oItem.Active = vI == Len(aItems) - 1;
            }
            return oCrumb.HTML;
        }

        // A card wrapper that keeps the page tree flat and readable.
        private static string CardWrap(string aTitle, string aBodyHTML,
            string aHeaderExtraHTML = "")
        {
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-4 shadow-sm";
            if (aTitle != "")
            {
                var oHead = new TsgcHTMLContainer("div");
                oHead.CSSClass =
                    "d-flex flex-wrap gap-2 justify-content-between align-items-center";
                oHead.Children.AddElement("strong", aTitle);
                if (aHeaderExtraHTML != "")
                    oHead.AddRaw(aHeaderExtraHTML);
                oCard.Header.Add(oHead);
            }
            oCard.Body.AddRaw(aBodyHTML);
            return oCard.HTML;
        }

        private static string ButtonHTML(string aText, string aHref,
            TsgcHTMLButtonStyle aStyle, string aCSSClass = "")
        {
            var oBtn = new TsgcHTMLButton(aText, aStyle);
            oBtn.Href = aHref;
            oBtn.CSSClass = aCSSClass;
            return oBtn.HTML;
        }

        // Small POST form holding one submit button (and optional hidden fields).
        private static string PostButton(string aAction, string aText,
            TsgcHTMLButtonStyle aStyle, string[] aNames, string[] aValues,
            string aConfirm = "", string aCSSClass = "btn-sm")
        {
            var oForm = new TsgcHTMLForm();
            oForm.Action = aAction;
            oForm.Method = "POST";
            oForm.CSSClass = "d-inline";
            for (int vI = 0; vI <= Len(aNames) - 1; vI++)
                oForm.AddHidden(aNames[vI], aValues[vI]);
            var oBtn = new TsgcHTMLButton(aText, aStyle);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = aCSSClass;
            if (aConfirm != "")
                oBtn.Attributes = "onclick=\"return confirm('" +
                    aConfirm.Replace("'", "") + "');\"";
            oForm.Add(oBtn);
            return oForm.HTML;
        }

        // -------------------------------------------------------------------
        // shells
        // -------------------------------------------------------------------

        private string FlashHTML(string aFlash, string aError)
        {
            if ((aFlash == "") && (aError == ""))
                return "";

            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLAlert oAlert;
            if (aError != "")
            {
                oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oAlert.Dismissible = true;
                oAlert.CSSClass = "mb-3";
                oRoot.Add(oAlert);
            }
            if (aFlash != "")
            {
                var oSnack = new TsgcHTMLComponent_Snackbar();
                oSnack.Message = aFlash;
                oSnack.Color = TsgcHTMLColor.hcSuccess;
                oSnack.Position = TsgcHTMLSnackbarPosition.sbBottomRight;
                oSnack.AutoHide = true;
                oSnack.Delay = 4500;
                oRoot.AddRaw(oSnack.HTML);

                oAlert = new TsgcHTMLAlert(aFlash);
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                oAlert.Dismissible = true;
                oAlert.CSSClass = "mb-3";
                oRoot.Add(oAlert);
            }
            return oRoot.HTML;
        }

        private string SiteShell(string aTitle, string aActive, string aTheme,
            bool aVendor, bool aPublic, TSaaSPageContext aCtx, string aBody)
        {
            var oSite = new TsgcHTMLComponent_Site();

            void Menu(string aText, string aHref, string aIcon, string aKey)
            {
                oSite.AddMenu(aText, aHref, aIcon).Active = SameText(aActive, aKey);
            }

            oSite.Title = aTitle + " - sgcSaaS";
            oSite.Lang = "en";
            oSite.Responsive = true;
            oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpCustom;
            oSite.Theme.Primary = CS_SAAS_ACCENT;
            oSite.Theme.SidebarDark = true;
            if (SameText(aTheme, "dark"))
                oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmDark;
            else if (SameText(aTheme, "system"))
                oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmSystem;
            else
                oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmLight;

            oSite.Brand.Text = "sgcSaaS";
            oSite.Brand.Href = "/";
            oSite.Footer.Text =
                "sgcSaaS demo - built with sgcHTML components, no REST tier. " +
                "Copyright 2026 eSeGeCe.";

            if (aPublic)
            {
                oSite.Layout = TsgcHTMLSiteLayout.slTopNav;
                oSite.Header.ShowUser = false;
                oSite.Header.ShowLogout = false;
                Menu("Product", "/", CS_ICO_HOME, "home");
                Menu("Pricing", "/pricing", CS_ICO_CARD, "pricing");
                Menu("Sign in", "/login", CS_ICO_LOCK, "login");
                Menu("Start free", "/signup", CS_ICO_ROCKET, "signup");
            }
            else
            {
                oSite.Layout = TsgcHTMLSiteLayout.slSidebarLeft;
                oSite.Header.ShowUser = true;
                oSite.Header.UserName = aCtx.DisplayName;
                oSite.Header.ShowLogout = true;
                oSite.Header.LogoutHref = "/logout";
                oSite.Header.ShowThemeSwitcher = false;

                if (aVendor)
                {
                    oSite.Brand.Text = "sgcSaaS vendor";
                    oSite.Brand.Href = "/admin";
                    Menu("Platform", "/admin", CS_ICO_HOME, "admin");
                    Menu("Plans", "/admin/plans", CS_ICO_CARD, "plans");
                    Menu("Feature flags", "/admin/flags", CS_ICO_FLAG, "flags");
                    Menu("Audit", "/admin/audit", CS_ICO_LIST, "audit");
                    Menu("Data layer", "/sql", CS_ICO_DB, "sql");
                }
                else
                {
                    oSite.Brand.Text = aCtx.TenantName;
                    oSite.Brand.Href = "/app";
                    Menu("Dashboard", "/app", CS_ICO_HOME, "dashboard");
                    Menu("Onboarding", "/app/onboarding", CS_ICO_ROCKET, "onboarding");
                    Menu("Projects", "/app/projects", CS_ICO_GRID, "projects");
                    Menu("Tasks", "/app/tasks", CS_ICO_TASK, "tasks");
                    Menu("Team", "/app/team", CS_ICO_TEAM, "team");
                    Menu("Roles", "/app/roles", CS_ICO_SHIELD, "roles");
                    Menu("Billing", "/app/billing", CS_ICO_CARD, "billing");
                    Menu("Notifications", "/app/notifications", CS_ICO_BELL,
                        "notifications");
                    Menu("Audit", "/app/audit", CS_ICO_LIST, "audit");
                    Menu("Isolation proof", "/app/isolation", CS_ICO_LOCK, "isolation");
                    Menu("Settings", "/app/settings", CS_ICO_GEAR, "settings");
                    Menu("Data layer", "/sql", CS_ICO_DB, "sql");
                }

                // The bell lives in the header slot, rendered by the Notification
                // component so the unread badge is the component's own.
                if (!aVendor)
                {
                    var oNotify = new TsgcHTMLComponent_Notification();
                    oNotify.Title = "Notifications";
                    oNotify.EmptyText = "Nothing new.";
                    oNotify.BellIcon = CS_ICO_BELL;
                    oNotify.ShowBadge = true;
                    oNotify.MaxVisible = 5;
                    if (aCtx.Unread > 0)
                        oNotify.AddNotification("n1", "You have unread items",
                            aCtx.Unread.ToString(GFmt) + " notification(s) waiting.",
                            TsgcHTMLColor.hcInfo, "");
                    else
                        oNotify.AddNotification("n1", "All caught up",
                            "No unread notifications.", TsgcHTMLColor.hcSecondary, "");
                    if (aCtx.Unread == 0)
                        oNotify.MarkAsRead("n1");
                    oSite.Header.ExtraHTML = oNotify.HTML;
                }
            }

            // Impersonation is always visible while it is active.
            if (aCtx.Impersonating)
            {
                var oBanner = new TsgcHTMLComponent_ImpersonateBanner();
                oBanner.UserName = aCtx.DisplayName;
                oBanner.Message = aCtx.ImpersonatorName +
                    " is signed in as this user. Every action is written to the audit " +
                    "log under both identities.";
                oBanner.StopCaption = "Stop impersonating";
                oBanner.StopURL = "/admin/impersonate/stop";
                oBanner.Color = TsgcHTMLColor.hcWarning;
                oBanner.Sticky = true;
                oSite.AddContent(oBanner.HTML);
            }

            oSite.AddContent(aBody);

            // Body-end widgets: the Ctrl+K palette and a quick-actions offcanvas.
            string vEnd = "";
            if (!aPublic)
            {
                var oPalette = new TsgcHTMLComponent_CommandPalette();
                oPalette.Placeholder = "Jump to a page (Ctrl+K)";
                oPalette.EmptyText = "Nothing matches.";
                oPalette.HotKey = "k";
                oPalette.HotKeyCtrl = true;
                oPalette.HotKeyMeta = true;
                oPalette.MaxResults = 12;
                oPalette.ShowCategories = true;
                oPalette.ShowShortcuts = true;
                if (aVendor)
                {
                    oPalette.AddItem("Platform overview", "/admin", "", "Vendor", "g p");
                    oPalette.AddItem("Plans", "/admin/plans", "", "Vendor", "g l");
                    oPalette.AddItem("Feature flags", "/admin/flags", "", "Vendor", "");
                    oPalette.AddItem("Vendor audit", "/admin/audit", "", "Vendor", "");
                    oPalette.AddItem("Data layer", "/sql", "", "Vendor", "");
                }
                else
                {
                    oPalette.AddItem("Dashboard", "/app", "", "Workspace", "g d");
                    oPalette.AddItem("Projects", "/app/projects", "", "Workspace",
                        "g p");
                    oPalette.AddItem("Tasks", "/app/tasks", "", "Workspace", "g t");
                    oPalette.AddItem("Team", "/app/team", "", "Workspace", "g m");
                    oPalette.AddItem("Roles and permissions", "/app/roles", "",
                        "Workspace", "");
                    oPalette.AddItem("Billing", "/app/billing", "", "Workspace", "g b");
                    oPalette.AddItem("Notifications", "/app/notifications", "",
                        "Workspace", "");
                    oPalette.AddItem("Audit log", "/app/audit", "", "Workspace", "");
                    oPalette.AddItem("Tenant isolation proof", "/app/isolation", "",
                        "Evidence", "g i");
                    oPalette.AddItem("Data layer (SQL)", "/sql", "", "Evidence", "");
                    oPalette.AddItem("Workspace settings", "/app/settings", "",
                        "Workspace", "");
                    oPalette.AddItem("Sign out", "/logout", "", "Session", "");
                }
                vEnd = vEnd + oPalette.HTML;

                var oOff = new TsgcHTMLComponent_Offcanvas();
                oOff.OffcanvasID = "sgcQuickPanel";
                oOff.Title = "Quick actions";
                oOff.Placement = TsgcHTMLOffcanvasPlacement.opEnd;
                var oOffBody = new TsgcHTMLNodeList();
                oOffBody.AddElement("p",
                    "Shortcuts for this session. Ctrl+K opens the command palette.",
                    "text-muted small");
                if (aVendor)
                {
                    oOffBody.AddRaw(ButtonHTML("Tenant list", "/admin",
                        TsgcHTMLButtonStyle.bsPrimary, "w-100 mb-2"));
                    oOffBody.AddRaw(ButtonHTML("Plans", "/admin/plans",
                        TsgcHTMLButtonStyle.bsOutlineSecondary, "w-100 mb-2"));
                    oOffBody.AddRaw(ButtonHTML("Vendor audit", "/admin/audit",
                        TsgcHTMLButtonStyle.bsOutlineSecondary, "w-100 mb-2"));
                }
                else
                {
                    oOffBody.AddRaw(ButtonHTML("New project", "/app/projects",
                        TsgcHTMLButtonStyle.bsPrimary, "w-100 mb-2"));
                    oOffBody.AddRaw(ButtonHTML("Invite a teammate", "/app/team",
                        TsgcHTMLButtonStyle.bsOutlineSecondary, "w-100 mb-2"));
                    oOffBody.AddRaw(ButtonHTML("Isolation proof", "/app/isolation",
                        TsgcHTMLButtonStyle.bsOutlineSecondary, "w-100 mb-2"));
                    oOffBody.AddRaw(ButtonHTML("Billing", "/app/billing",
                        TsgcHTMLButtonStyle.bsOutlineSecondary, "w-100 mb-2"));
                }
                oOff.Body = oOffBody.HTML;
                vEnd = vEnd + oOff.HTML;
            }
            oSite.BodyEndHTML = vEnd;

            string vResult = oSite.HTML;

            // Serve Bootstrap from the linked resource, never a CDN: these demos must
            // run with no network at all.
            vResult = vResult.Replace(CS_CDN_CSS, CS_LOCAL_CSS);
            vResult = vResult.Replace(CS_CDN_JS, CS_LOCAL_JS);
            return vResult;
        }

        public string BuildPublicShell(string aTitle, string aBody, string aActive,
            string aTheme)
        {
            var vCtx = new TSaaSPageContext();
            vCtx.Username = "";
            vCtx.DisplayName = "";
            vCtx.Role = "";
            vCtx.TenantId = 0;
            vCtx.TenantName = "";
            vCtx.TenantSlug = "";
            vCtx.Impersonating = false;
            vCtx.ImpersonatorName = "";
            vCtx.Unread = 0;
            vCtx.ActiveMenu = aActive;
            vCtx.Theme = aTheme;
            vCtx.Flash = "";
            vCtx.ErrorMsg = "";
            return SiteShell(aTitle, aActive, aTheme, false, true, vCtx, aBody);
        }

        public string BuildAppShell(string aTitle, string aBody, TSaaSPageContext aCtx)
        {
            return SiteShell(aTitle, aCtx.ActiveMenu, aCtx.Theme, false, false,
                aCtx, aBody);
        }

        public string BuildAdminShell(string aTitle, string aBody, TSaaSPageContext aCtx)
        {
            return SiteShell(aTitle, aCtx.ActiveMenu, aCtx.Theme, true, false,
                aCtx, aBody);
        }

        // -------------------------------------------------------------------
        // public pages
        // -------------------------------------------------------------------

        public string BuildMarketingPage(TSaaSPlan[] aPlans, TSaaSPlatformKPI aKPI,
            string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oRow;
            TsgcHTMLContainer oCol;
            TsgcHTMLAccordionItem oAccItem;
            TsgcHTMLTabItem oTab;
            int vI;
            string vBody;

            // Hero.
            var oHero = new TsgcHTMLContainer("div");
            oHero.CSSClass = "p-4 p-md-5 mb-4 rounded-3 text-white";
            oHero.Style = "background:linear-gradient(120deg,#0891B2,#155E75);";
            oHero.Children.AddElement("h1", "Ship multi-tenant SaaS from Delphi",
                "display-5 fw-bold mb-3");
            oHero.Children.AddElement("p",
                "One binary. One database. Strict per-tenant isolation, plans and " +
                "billing, a vendor console with impersonation, and a public site. " +
                "No REST tier, no JavaScript build step.", "lead mb-4");
            oHero.AddRaw(ButtonHTML("Start a free workspace", "/signup",
                TsgcHTMLButtonStyle.bsLight, "btn-lg me-2"));
            oHero.AddRaw(ButtonHTML("See the isolation proof", "/login",
                TsgcHTMLButtonStyle.bsOutlineLight, "btn-lg"));
            oRoot.Add(oHero);

            // KPI strip, real numbers out of the demo database.
            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCol("Tenants", Num(aKPI.Tenants), CS_ICO_BUILDING,
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stUp, "live",
                "col-6 col-lg-3", "in this database"));
            oRow.AddRaw(StatCol("Users", Num(aKPI.Users), CS_ICO_TEAM,
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-lg-3", "across all tenants"));
            oRow.AddRaw(StatCol("Projects", Num(aKPI.Projects), CS_ICO_GRID,
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-lg-3", "tenant workloads"));
            oRow.AddRaw(StatCol("MRR", Money(aKPI.MRR), CS_ICO_CARD,
                TsgcHTMLStatColor.scWarning, TsgcHTMLStatTrend.stUp, "simulated",
                "col-6 col-lg-3", "no payment provider"));
            oRoot.Add(oRow);

            // Feature tabs.
            var oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.Style = TsgcHTMLTabStyle.tsPill;

            var oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("Every workspace query carries a tenant_id bind", "",
                "enforced", TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("A query without that bind refuses to run", "",
                "guarded", TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Tenant ids are never read from the request", "",
                "session only", TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Cross-axis routes answer 403 with a reason", "",
                "no redirect", TsgcHTMLBadgeStyle.bgSuccess);
            oTab = oTabs.Items.Add();
            oTab.Title = "Isolation";
            oTab.Active = true;
            oTab.Content = oList.HTML;

            oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("Self-service signup creates tenant and owner", "",
                "", TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Resumable onboarding wizard", "", "",
                TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Invitations with single-use expiring tokens", "",
                "", TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Plan limits enforced server-side", "", "",
                TsgcHTMLBadgeStyle.bgSecondary);
            oTab = oTabs.Items.Add();
            oTab.Title = "Lifecycle";
            oTab.Content = oList.HTML;

            oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("Tenant list, suspend and activate", "", "",
                TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Impersonation, superadmin only, fully audited", "",
                "", TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Plan editor and feature flags", "", "",
                TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Platform-wide audit trail", "", "",
                TsgcHTMLBadgeStyle.bgSecondary);
            oTab = oTabs.Items.Add();
            oTab.Title = "Vendor console";
            oTab.Content = oList.HTML;

            oRoot.AddRaw(CardWrap("What this demo proves", oTabs.HTML));

            // Product tour placeholder + screenshot placeholder.
            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4 mb-4";

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-lg-7";
            var oVideo = new TsgcHTMLComponent_Video();
            oVideo.Src = "";
            oVideo.Poster = CS_POSTER_SVG;
            oVideo.Controls = true;
            oVideo.Responsive = true;
            oVideo.CSSWidth = "100%";
            oCol.AddRaw(CardWrap("Product tour", oVideo.HTML));
            oRow.Add(oCol);

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-lg-5";
            var oImage = new TsgcHTMLComponent_Image();
            oImage.Src = CS_LOGO_SVG;
            oImage.Alt = "sgcSaaS";
            oImage.Shape = TsgcHTMLImageShape.isRounded;
            oImage.CSSWidth = "96px";
            oImage.Caption = "Everything on this page is a sgcHTML component.";
            oCol.AddRaw(CardWrap("Built from components", oImage.HTML));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            // Plans teaser.
            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            for (vI = 0; vI <= Len(aPlans) - 1; vI++)
            {
                oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-12 col-sm-6 col-lg-3";
                if (aPlans[vI].PriceMonthly == 0)
                    vBody = "Free forever";
                else
                    vBody = Money(aPlans[vI].PriceMonthly) + " / month";
                oCol.AddRaw(StatCol(aPlans[vI].Name, vBody, CS_ICO_CARD,
                    TsgcHTMLStatColor.scSecondary, TsgcHTMLStatTrend.stNone, "", "",
                    Num(aPlans[vI].MaxUsers) + " users, " +
                    Num(aPlans[vI].MaxProjects) + " projects"));
                oRow.Add(oCol);
            }
            oRoot.Add(oRow);
            oRoot.AddRaw(ButtonHTML("Compare all plans", "/pricing",
                TsgcHTMLButtonStyle.bsPrimary, "btn-lg mb-4"));

            // FAQ accordion + a popover on the honesty note.
            var oAcc = new TsgcHTMLComponent_Accordion();
            oAccItem = oAcc.Items.Add();
            oAccItem.Title = "Is there a REST tier behind this?";
            oAccItem.Expanded = true;
            oAccItem.Content =
                "No. Every page binds a TFDQuery straight into a sgcHTML component. " +
                "The /sql page shows the statement next to the component it fed.";

            oAccItem = oAcc.Items.Add();
            oAccItem.Title = "How is tenant data kept apart?";
            oAccItem.Content =
                "Each workspace statement carries a tenant_id bind taken from the " +
                "session. A helper refuses to execute a workspace query that has no " +
                "such bind. Sign in and open the isolation page to watch a " +
                "cross-tenant read being refused.";

            oAccItem = oAcc.Items.Add();
            oAccItem.Title = "Does this send email or charge a card?";
            oAccItem.Content =
                "No. There is no SMTP server and no payment provider in this demo. " +
                "Verification and invitation links are printed on screen, and plan " +
                "changes are recorded and invoiced locally.";
            oRoot.AddRaw(CardWrap("Questions", oAcc.HTML));

            var oPopover = new TsgcHTMLComponent_Popover();
            oPopover.Content = "What is simulated here?";
            oPopover.Title = "Simulated parts";
            oPopover.Body = "Email delivery and payments. Everything else, " +
                "including tenant isolation, plan limits and the audit log, is real " +
                "code running against the demo database.";
            oPopover.Trigger = TsgcHTMLPopoverTrigger.ptClick;
            oPopover.ContentStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oRoot.AddRaw(oPopover.HTML);

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Multi-tenant SaaS control plane", vResult,
                "home", aTheme);
            return vResult;
        }

        public string BuildPricingPage(DataTable aPlansDS, TSaaSPlan[] aPlans,
            string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oCol;
            TsgcHTMLCard oCard;
            int vI;
            string vPrice;

            oRoot.Add(new TsgcHTMLHeading("Plans", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "Limits below are enforced by the server, not by the interface. " +
                "Adding a user past the plan ceiling is refused with an upgrade prompt."));

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            for (vI = 0; vI <= Len(aPlans) - 1; vI++)
            {
                oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-12 col-md-6 col-xl-3";
                oCard = new TsgcHTMLCard();
                oCard.CSSClass = "h-100 shadow-sm";
                if (SameText(aPlans[vI].Code, "pro"))
                    oCard.CSSClass = "h-100 shadow border-primary border-2";
                oCard.Header.AddElement("strong", aPlans[vI].Name);
                if (aPlans[vI].PriceMonthly == 0)
                    vPrice = "Free";
                else
                    vPrice = Money(aPlans[vI].PriceMonthly);
                oCard.Body.AddElement("div", vPrice, "display-6 fw-bold mb-1");
                oCard.Body.AddElement("div", "per month", "text-muted small mb-3");

                var oList = new TsgcHTMLComponent_ListGroup();
                oList.Flush = true;
                oList.AddItem("Users", "", Num(aPlans[vI].MaxUsers),
                    TsgcHTMLBadgeStyle.bgSecondary);
                oList.AddItem("Projects", "", Num(aPlans[vI].MaxProjects),
                    TsgcHTMLBadgeStyle.bgSecondary);
                oList.AddItem("Storage", "", Num(aPlans[vI].MaxStorageMB) + " MB",
                    TsgcHTMLBadgeStyle.bgSecondary);
                oCard.Body.AddRaw(oList.HTML);

                oCard.Footer.AddRaw(ButtonHTML("Choose " + aPlans[vI].Name,
                    "/signup?plan=" + aPlans[vI].Code, TsgcHTMLButtonStyle.bsPrimary,
                    "w-100"));
                oCol.Add(oCard);
                oRow.Add(oCol);
            }
            oRoot.Add(oRow);

            // The same plan table, this time straight out of the dataset.
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.Responsive = true;
            oGrid.ShowSort = true;
            oGrid.EmptyText = "No plans configured.";
            if (aPlansDS != null)
                oGrid.LoadFromDataSet(aPlansDS);
            oRoot.AddRaw(CardWrap("Plan matrix, bound straight from TFDQuery",
                oGrid.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Pricing", vResult, "pricing", aTheme);
            return vResult;
        }

        public string BuildSignupPage(string aCompany, string aName, string aEmail,
            string aUsername, string aPlanCode, string aError, string aTheme,
            TSaaSPlan[] aPlans)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLFormField oField;
            int vI;

            oRoot.Add(new TsgcHTMLHeading("Create your workspace", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "This creates a tenant row plus its owner account. No email is sent: " +
                "the verification link is printed on the next screen."));

            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oRoot.Add(oAlert);
            }

            var oForm = new TsgcHTMLComponent_Form();
            oForm.Action = "/signup";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Create workspace";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "company";
            oField.Label_ = "Company or team name";
            oField.Placeholder = "Northwind Traders";
            oField.Value = aCompany;
            oField.Required = true;
            oField.ColSpan = 12;
            if ((aError != "") && ((aCompany ?? "").Trim() == ""))
            {
                oField.Validation = TsgcHTMLFieldValidation.fvInvalid;
                oField.Feedback = "A workspace needs a name.";
            }

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "display_name";
            oField.Label_ = "Your name";
            oField.Placeholder = "Alice Johnson";
            oField.Value = aName;
            oField.Required = true;
            oField.ColSpan = 6;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftEmail;
            oField.Name = "email";
            oField.Label_ = "Work email";
            oField.Placeholder = "alice@northwind.example";
            oField.Value = aEmail;
            oField.Required = true;
            oField.ColSpan = 6;
            if ((aError != "") && (PosStr("email", (aError ?? "").ToLowerInvariant()) > 0))
            {
                oField.Validation = TsgcHTMLFieldValidation.fvInvalid;
                oField.Feedback = aError;
            }

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "username";
            oField.Label_ = "Sign-in name";
            oField.Placeholder = "alice";
            oField.Value = aUsername;
            oField.Required = true;
            oField.ColSpan = 6;
            if ((aError != "") &&
                (PosStr("sign-in name", (aError ?? "").ToLowerInvariant()) > 0))
            {
                oField.Validation = TsgcHTMLFieldValidation.fvInvalid;
                oField.Feedback = aError;
            }

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "password";
            oField.Label_ = "Password";
            oField.HelpText = "At least 6 characters. Stored bcrypt-hashed.";
            oField.Required = true;
            oField.ColSpan = 6;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "plan";
            oField.Label_ = "Starting plan";
            oField.ColSpan = 12;
            for (vI = 0; vI <= Len(aPlans) - 1; vI++)
                oField.Options.Add(aPlans[vI].Code + "=" + aPlans[vI].Name);
            if (aPlanCode != "")
                oField.Value = aPlanCode;
            else
                oField.Value = "free";

            oRoot.AddRaw(CardWrap("Sign up", oForm.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Sign up", vResult, "signup", aTheme);
            return vResult;
        }

        public string BuildSignupDonePage(string aTenantName, string aVerifyURL,
            string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.Add(new TsgcHTMLHeading("Workspace created", 1));

            var oAlert = new TsgcHTMLAlert("");
            oAlert.Style = TsgcHTMLAlertStyle.asWarning;
            oAlert.AddText("This demo has no SMTP server, so nothing was emailed. " +
                "In a real deployment the link below would be sent to the address you " +
                "entered. Here it is printed on screen.");
            oRoot.Add(oAlert);

            var oBody = new TsgcHTMLNodeList();
            oBody.AddElement("p", "Workspace: " + aTenantName, "mb-2");
            oBody.AddElement("p", "Verification link (single use, expires in 24 " +
                "hours):", "mb-2 text-muted small");
            oBody.AddRaw(CodeBlock(aVerifyURL));
            oBody.AddRaw(ButtonHTML("Verify now", aVerifyURL,
                TsgcHTMLButtonStyle.bsPrimary, "mt-3 btn-lg"));
            oRoot.AddRaw(CardWrap("Next step", oBody.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Workspace created", vResult, "signup", aTheme);
            return vResult;
        }

        public string BuildVerifyPage(bool aOK, string aMessage, string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.Add(new TsgcHTMLHeading("Email verification", 1));
            var oAlert = new TsgcHTMLAlert(aMessage);
            if (aOK)
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
            else
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
            oRoot.Add(oAlert);
            if (aOK)
                oRoot.AddRaw(ButtonHTML("Sign in", "/login",
                    TsgcHTMLButtonStyle.bsPrimary, "btn-lg"));
            else
                oRoot.AddRaw(ButtonHTML("Back to sign up", "/signup",
                    TsgcHTMLButtonStyle.bsSecondary, ""));

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Verification", vResult, "", aTheme);
            return vResult;
        }

        public string BuildLoginPage(string aError, string aInfo, string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLSocialProviderItem oProvider;

            if (aInfo != "")
            {
                var oAlert = new TsgcHTMLAlert(aInfo);
                oAlert.Style = TsgcHTMLAlertStyle.asInfo;
                oRoot.Add(oAlert);
            }

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4 justify-content-center";

            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-lg-5";
            var oLogin = new TsgcHTMLComponent_Login();
            oLogin.Title = "Sign in";
            oLogin.Subtitle = "Workspace and vendor accounts use the same form.";
            oLogin.FormAction = "/login";
            oLogin.FormMethod = "POST";
            oLogin.UserLabel = "Sign-in name";
            oLogin.PasswordLabel = "Password";
            oLogin.ButtonText = "Sign in";
            oLogin.ButtonStyleEnum = TsgcHTMLButtonStyle.bsPrimary;
            oLogin.ButtonBlock = true;
            oLogin.ShowRememberMe = true;
            oLogin.LoginStyle = TsgcHTMLLoginStyle.lsCard;
            oLogin.ErrorMessage = aError;
            oLogin.FooterLinkText = "Create a workspace";
            oLogin.FooterLinkURL = "/signup";
            oCol.AddRaw(oLogin.HTML);

            var oWebAuthn = new TsgcHTMLComponent_WebAuthnLogin();
            oWebAuthn.Mode = TsgcHTMLWebAuthnMode.wamAuthenticate;
            oWebAuthn.Title = "Passkey";
            oWebAuthn.Description =
                "Sign in with a device passkey instead of a password.";
            oWebAuthn.AuthenticateURL = "/passkey/login/options";
            oWebAuthn.CallbackURL = "/passkey/login/verify";
            oWebAuthn.AuthenticateButtonText = "Use a passkey";
            oWebAuthn.AuthenticateButtonStyle = TsgcHTMLButtonStyle.bsOutlinePrimary;
            oCol.AddRaw(CardWrap("", oWebAuthn.HTML));
            oRow.Add(oCol);

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-lg-4";
            var oSocial = new TsgcHTMLComponent_SocialLogin();
            oSocial.Title = "Or continue with";
            oSocial.Subtitle =
                "These start the OAuth dance and land on the callback page.";
            // Both this unit and the Stepper unit declare slVertical in Delphi, so
            // the enum is qualified rather than left to uses-clause order.
            oSocial.Layout = TsgcHTMLSocialLoginLayout.slVertical;
            oSocial.ShowDivider = false;
            oProvider = oSocial.Providers.Add();
            oProvider.Provider = TsgcHTMLSocialProvider.spGoogle;
            oProvider.AuthURL = "/auth/social/google";
            oProvider = oSocial.Providers.Add();
            oProvider.Provider = TsgcHTMLSocialProvider.spGitHub;
            oProvider.AuthURL = "/auth/social/github";
            oProvider = oSocial.Providers.Add();
            oProvider.Provider = TsgcHTMLSocialProvider.spMicrosoft;
            oProvider.AuthURL = "/auth/social/microsoft";
            oCol.AddRaw(oSocial.HTML);

            var oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("root / root", "", "superadmin",
                TsgcHTMLBadgeStyle.bgDanger);
            oList.AddItem("support / root", "", "read only",
                TsgcHTMLBadgeStyle.bgWarning);
            oList.AddItem("Any tenant owner: see the vendor console", "",
                "impersonate", TsgcHTMLBadgeStyle.bgInfo);
            oCol.AddRaw(CardWrap("Demo accounts", oList.HTML));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Sign in", vResult, "login", aTheme);
            return vResult;
        }

        public string BuildForgotPage(string aInfo, string aResetURL, string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLAlert oAlert;

            oRoot.Add(new TsgcHTMLHeading("Reset your password", 1));

            oAlert = new TsgcHTMLAlert(
                "No email is sent by this demo. Enter the account address and the " +
                "reset link appears on this page.");
            oAlert.Style = TsgcHTMLAlertStyle.asWarning;
            oRoot.Add(oAlert);

            if (aInfo != "")
            {
                oAlert = new TsgcHTMLAlert(aInfo);
                oAlert.Style = TsgcHTMLAlertStyle.asInfo;
                oRoot.Add(oAlert);
            }

            var oForm = new TsgcHTMLComponent_Form();
            oForm.Action = "/forgot";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.SubmitText = "Show the reset link";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftEmail;
            oField.Name = "email";
            oField.Label_ = "Account email";
            oField.Required = true;
            oField.ColSpan = 12;
            oRoot.AddRaw(CardWrap("Step 1", oForm.HTML));

            if (aResetURL != "")
            {
                var oBody = new TsgcHTMLNodeList();
                oBody.AddElement("p", "Single-use reset token, valid for one hour:",
                    "text-muted small");
                oBody.AddRaw(CodeBlock(aResetURL));

                var oReset = new TsgcHTMLForm();
                oReset.Action = "/reset";
                oReset.Method = "POST";
                oReset.CSSClass = "mt-3";
                oReset.AddHidden("token",
                    CopyStr(aResetURL, PosStr("token=", aResetURL) + 6, int.MaxValue));
                var oPwd = new TsgcHTMLField(TsgcHTMLInputType.itPassword, "password");
                oPwd.Label_ = "New password";
                oPwd.Required = true;
                oPwd.ColClass = "mb-3";
                oReset.Add(oPwd);
                var oBtn = new TsgcHTMLButton("Set the new password",
                    TsgcHTMLButtonStyle.bsPrimary);
                oBtn.ButtonType = "submit";
                oReset.Add(oBtn);
                oBody.AddRaw(oReset.HTML);
                oRoot.AddRaw(CardWrap("Step 2", oBody.HTML));
            }

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Password reset", vResult, "login", aTheme);
            return vResult;
        }

        public string BuildOAuthCallbackPage(string aProvider, string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oCallback = new TsgcHTMLComponent_OAuthCallback();
            oCallback.Status = TsgcHTMLOAuthCallbackStatus.csError;
            oCallback.ProviderName = aProvider;
            oCallback.ErrorMessage =
                "No OAuth client is configured in this demo, so the exchange " +
                "stopped here. The page you are looking at is the real callback " +
                "component: point it at a live provider and it completes.";
            oCallback.RedirectURL = "/login";
            oCallback.RedirectMethod = TsgcHTMLOAuthRedirectMethod.rmButtonOnly;
            oCallback.ShowUserInfo = false;
            oRoot.AddRaw(oCallback.HTML);

            var oAlert = new TsgcHTMLAlert(
                "Social sign-in is wired up to the point of the redirect only. " +
                "Nothing was sent to " + aProvider + ".");
            oAlert.Style = TsgcHTMLAlertStyle.asInfo;
            oRoot.Add(oAlert);

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("OAuth callback", vResult, "login", aTheme);
            return vResult;
        }

        public string BuildInvitePage(TSaaSInvitation aInvite, string aTenantName,
            string aError, string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLFormField oField;
            string vResult;

            oRoot.Add(new TsgcHTMLHeading("Join " + aTenantName, 1));

            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oRoot.Add(oAlert);
                oRoot.AddRaw(ButtonHTML("Back to sign in", "/login",
                    TsgcHTMLButtonStyle.bsSecondary, ""));
                vResult = oRoot.HTML;
                vResult = BuildPublicShell("Invitation", vResult, "", aTheme);
                return vResult;
            }

            oRoot.Add(new TsgcHTMLParagraph("You were invited as " +
                aInvite.Role + ". The invitation is single use and expires on " +
                StampStr(aInvite.ExpiresAt) + "."));

            var oForm = new TsgcHTMLComponent_Form();
            oForm.Action = "/invite/" + aInvite.Token + "/accept";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.SubmitText = "Accept and create my account";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftEmail;
            oField.Name = "email";
            oField.Label_ = "Email";
            oField.Value = aInvite.Email;
            oField.ReadOnly = true;
            oField.ColSpan = 6;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "display_name";
            oField.Label_ = "Your name";
            oField.Required = true;
            oField.ColSpan = 6;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "username";
            oField.Label_ = "Sign-in name";
            oField.Required = true;
            oField.ColSpan = 6;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "password";
            oField.Label_ = "Password";
            oField.Required = true;
            oField.ColSpan = 6;

            oRoot.AddRaw(CardWrap("Accept invitation", oForm.HTML));

            vResult = oRoot.HTML;
            vResult = BuildPublicShell("Invitation", vResult, "", aTheme);
            return vResult;
        }

        public string BuildForbiddenPage(string aReason, string aTheme,
            TSaaSPageContext aCtx, bool aInApp)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oEmpty = new TsgcHTMLComponent_EmptyState();
            oEmpty.Title = "403 - refused";
            oEmpty.Description = aReason;
            oEmpty.Icon = CS_ICO_LOCK;
            oEmpty.Bordered = true;
            if (aInApp)
            {
                oEmpty.ActionCaption = "Back to the workspace";
                oEmpty.ActionHref = "/app";
            }
            else
            {
                oEmpty.ActionCaption = "Sign in";
                oEmpty.ActionHref = "/login";
            }
            oEmpty.SecondaryActionCaption = "Why this happened";
            oEmpty.SecondaryActionHref = "/app/isolation";
            oRoot.AddRaw(oEmpty.HTML);

            string vResult = oRoot.HTML;
            if (aInApp && (aCtx.TenantId > 0))
                vResult = BuildAppShell("Refused", vResult, aCtx);
            else if (aInApp)
                vResult = BuildAdminShell("Refused", vResult, aCtx);
            else
                vResult = BuildPublicShell("Refused", vResult, "", aTheme);
            return vResult;
        }

        public string BuildNotFoundPage(string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(EmptyStateHTML("404 - no such page",
                "The address does not match any route in this demo.", CS_ICO_HOME,
                "Go to the product page", "/"));

            string vResult = oRoot.HTML;
            vResult = BuildPublicShell("Not found", vResult, "", aTheme);
            return vResult;
        }

        public string BuildSQLPage(DataTable aProjectsDS, string aSQL,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            oRoot.Add(new TsgcHTMLHeading("The whole data layer", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "There is no REST endpoint between the database and the page. The " +
                "statement on the left is executed by a pooled FireDAC connection and " +
                "the resulting TFDQuery is handed to the component on the right with " +
                "LoadFromDataSet. That is the entire pipeline."));

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4";

            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-5";
            var oBody = new TsgcHTMLNodeList();
            oBody.AddRaw(CodeBlock(PrettySQL(aSQL)));
            oBody.AddElement("p", "Bound values for this request:",
                "text-muted small mt-3 mb-1");
            oBody.AddRaw(CodeBlock(":tenant_id = " + aCtx.TenantId.ToString(GFmt)));
            oCol.AddRaw(CardWrap("The statement", oBody.HTML));

            var oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("TSaaSQuery.CreateScoped(pool, SQL, session.TenantId)",
                "", "step 1", TsgcHTMLBadgeStyle.bgPrimary);
            oList.AddItem("The helper refuses SQL without a :tenant_id bind", "",
                "step 2", TsgcHTMLBadgeStyle.bgPrimary);
            oList.AddItem("Query.Open on a pooled FireDAC connection", "",
                "step 3", TsgcHTMLBadgeStyle.bgPrimary);
            oList.AddItem("Grid.LoadFromDataSet(Query.DataSet)", "", "step 4",
                TsgcHTMLBadgeStyle.bgPrimary);
            oList.AddItem("Grid.HTML spliced into the page", "", "step 5",
                TsgcHTMLBadgeStyle.bgPrimary);
            oCol.AddRaw(CardWrap("Five lines, no API tier", oList.HTML));
            oRow.Add(oCol);

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-7";
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.Responsive = true;
            oGrid.ShowSort = true;
            oGrid.ShowFilter = true;
            oGrid.EmptyText = "This tenant has no rows.";
            if (aProjectsDS != null)
                oGrid.LoadFromDataSet(aProjectsDS);
            oCol.AddRaw(CardWrap("The component that consumed it", oGrid.HTML));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            string vResult = oRoot.HTML;
            if (SameText(aCtx.Role, CS_ROLE_SUPERADMIN) ||
                SameText(aCtx.Role, CS_ROLE_SUPPORT))
                vResult = BuildAdminShell("Data layer", vResult, aCtx);
            else
                vResult = BuildAppShell("Data layer", vResult, aCtx);
            return vResult;
        }

        // -------------------------------------------------------------------
        // tenant workspace
        // -------------------------------------------------------------------

        public string BuildDashboardPage(TSaaSDashboardVM aVM, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oRow;
            TsgcHTMLContainer oCol;
            TsgcHTMLNodeList oBody;
            TsgcHTMLAlert oAlert;
            TsgcHTMLDropdownItem oDDItem;
            TsgcHTMLTimelineItem oTimelineItem;
            int vI;
            double[] vValues;

            var oToolbar = new TsgcHTMLContainer("div");
            oToolbar.CSSClass =
                "d-flex flex-wrap gap-2 justify-content-between align-items-center mb-3";
            oToolbar.Children.AddElement("h1", aVM.Tenant.Name, "h3 mb-0");

            var oDropdown = new TsgcHTMLComponent_Dropdown();
            oDropdown.ButtonText = "Create";
            oDropdown.ButtonStyleEnum = TsgcHTMLButtonStyle.bsPrimary;
            oDDItem = oDropdown.Items.Add();
            oDDItem.Text = "New project";
            oDDItem.Href = "/app/projects";
            oDDItem = oDropdown.Items.Add();
            oDDItem.Text = "New task";
            oDDItem.Href = "/app/tasks";
            oDDItem = oDropdown.Items.Add();
            oDDItem.Divider = true;
            oDDItem = oDropdown.Items.Add();
            oDDItem.Text = "Invite a teammate";
            oDDItem.Href = "/app/team";
            oToolbar.AddRaw(oDropdown.HTML);

            var oBtn = new TsgcHTMLButton("Quick actions",
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBtn.ButtonType = "button";
            oBtn.Attributes =
                "data-bs-toggle=\"offcanvas\" data-bs-target=\"#sgcQuickPanel\"";
            oToolbar.Add(oBtn);
            oRoot.Add(oToolbar);

            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            // Lifecycle warnings, if any.
            if (SameText(aVM.Tenant.Status, CS_TENANT_TRIAL))
            {
                oAlert = new TsgcHTMLAlert("Trial ends in " +
                    aVM.TrialDaysLeft.ToString(GFmt) + " day(s), on " +
                    DateStr(aVM.Tenant.TrialEndsAt) +
                    ". Billing here is simulated, so upgrading records an invoice " +
                    "locally and charges nothing.");
                oAlert.Style = TsgcHTMLAlertStyle.asInfo;
                oAlert.AddRaw(ButtonHTML("Choose a plan", "/app/billing",
                    TsgcHTMLButtonStyle.bsPrimary, "btn-sm ms-2"));
                oRoot.Add(oAlert);
            }
            else if (SameText(aVM.Tenant.Status, CS_TENANT_PAST_DUE))
            {
                oAlert = new TsgcHTMLAlert("This workspace has an overdue invoice.");
                oAlert.Style = TsgcHTMLAlertStyle.asWarning;
                oRoot.Add(oAlert);
            }

            // KPI row.
            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCol("Projects", Num(aVM.Usage.Projects), CS_ICO_GRID,
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "limit " + Num(aVM.Usage.MaxProjects)));
            oRow.AddRaw(StatCol("Open tasks", Num(aVM.OpenTasks), CS_ICO_TASK,
                TsgcHTMLStatColor.scWarning, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", Num(aVM.DoneTasks) + " done"));
            oRow.AddRaw(StatCol("Members", Num(aVM.Usage.Users), CS_ICO_TEAM,
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "limit " + Num(aVM.Usage.MaxUsers)));
            oRow.AddRaw(StatCol("Plan", aVM.Tenant.PlanName, CS_ICO_CARD,
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", aVM.Tenant.Status));
            oRoot.Add(oRow);

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4 mb-4";

            // Usage chart.
            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-8";
            var oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartType = TsgcHTMLChartType.ctLine;
            oChart.Title = "API calls per month";
            oChart.CSSHeight = "260px";
            oChart.ShowLegend = false;
            vValues = new double[Len(aVM.UsageMonths)];
            for (vI = 0; vI <= Len(aVM.UsageMonths) - 1; vI++)
            {
                oChart.AddLabel(aVM.UsageMonths[vI].MonthLabel);
                vValues[vI] = aVM.UsageMonths[vI].Value;
            }
            oChart.AddDataset("API calls", vValues, CS_SAAS_ACCENT);
            oCol.AddRaw(CardWrap("Usage trend", oChart.HTML));
            oRow.Add(oCol);

            // Gauges + sparkline.
            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-4";
            oBody = new TsgcHTMLNodeList();

            var oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.Title = "Seats used";
            oGauge.Value = aVM.Usage.Users;
            oGauge.MinValue = 0;
            if (aVM.Usage.MaxUsers > 0)
                oGauge.MaxValue = aVM.Usage.MaxUsers;
            else
                oGauge.MaxValue = Math.Max(1, aVM.Usage.Users);
            oGauge.Unit_ = "seats";
            oGauge.Width = 190;
            oBody.AddRaw(oGauge.HTML);

            oBody.AddRaw(LimitBar("Storage", aVM.Usage.StorageMB,
                aVM.Usage.MaxStorageMB, "MB"));
            oBody.AddRaw(LimitBar("Projects", aVM.Usage.Projects,
                aVM.Usage.MaxProjects, ""));

            var oSpark = new TsgcHTMLComponent_Sparkline();
            oSpark.ChartType = TsgcHTMLSparklineType.slArea;
            oSpark.Width = 240;
            oSpark.Height = 46;
            oSpark.LineColor = CS_SAAS_ACCENT;
            oSpark.ShowLastPoint = true;
            for (vI = 0; vI <= Len(aVM.ApiSeries) - 1; vI++)
                oSpark.AddValue(aVM.ApiSeries[vI]);
            oBody.AddElement("div", "Activity, last 12 months",
                "text-muted small mt-3");
            oBody.AddRaw(oSpark.HTML);

            oCol.AddRaw(CardWrap("Plan limits", oBody.HTML));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4 mb-4";

            // Activity feed.
            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-lg-6";
            if (Len(aVM.Audit) == 0)
                oCol.AddRaw(CardWrap("Recent activity",
                    EmptyStateHTML("Nothing yet", "Activity in this workspace shows here.",
                    CS_ICO_LIST, "", "")));
            else
            {
                var oFeed = new TsgcHTMLComponent_ActivityFeed();
                oFeed.MaxItems = 8;
                oFeed.ShowRelativeTime = true;
                oFeed.EmptyText = "No activity yet.";
                for (vI = 0; vI <= Math.Min(Len(aVM.Audit) - 1, 7); vI++)
                    oFeed.AddActivity(aVM.Audit[vI].UserName, aVM.Audit[vI].Action,
                        aVM.Audit[vI].Entity + " #" +
                        aVM.Audit[vI].EntityId.ToString(GFmt),
                        TsgcHTMLColor.hcInfo).Timestamp = aVM.Audit[vI].CreatedAt;
                oCol.AddRaw(CardWrap("Recent activity", oFeed.HTML));
            }
            oRow.Add(oCol);

            // Presence + lifecycle timeline.
            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-lg-6";
            var oPresence = new TsgcHTMLComponent_Presence();
            oPresence.Layout = TsgcHTMLPresenceLayout.plAvatars;
            oPresence.MaxVisible = 8;
            oPresence.ShowCount = true;
            oPresence.Title = "Team";
            oPresence.EmptyText = "No members.";
            for (vI = 0; vI <= Len(aVM.Team) - 1; vI++)
                if (vI % 3 == 0)
                    oPresence.AddUser(aVM.Team[vI].Id.ToString(GFmt),
                        aVM.Team[vI].DisplayName, TsgcHTMLPresenceStatus.psOnline,
                        "", "Online");
                else if (vI % 3 == 1)
                    oPresence.AddUser(aVM.Team[vI].Id.ToString(GFmt),
                        aVM.Team[vI].DisplayName, TsgcHTMLPresenceStatus.psAway,
                        "", "Away");
                else
                    oPresence.AddUser(aVM.Team[vI].Id.ToString(GFmt),
                        aVM.Team[vI].DisplayName, TsgcHTMLPresenceStatus.psOffline,
                        "", "Offline");
            oCol.AddRaw(CardWrap("Who is around", oPresence.HTML));

            var oTimeline = new TsgcHTMLComponent_Timeline();
            oTimelineItem = oTimeline.Items.Add();
            oTimelineItem.Title = "Workspace created";
            oTimelineItem.Content = aVM.Tenant.Name + " signed up.";
            oTimelineItem.Timestamp = DateStr(aVM.Tenant.CreatedAt);
            oTimelineItem.ColorStyle = TsgcHTMLColor.hcSuccess;

            if (aVM.Tenant.TrialEndsAt != DateTime.MinValue)
            {
                oTimelineItem = oTimeline.Items.Add();
                oTimelineItem.Title = "Trial ends";
                oTimelineItem.Content = "Then the workspace moves onto its plan.";
                oTimelineItem.Timestamp = DateStr(aVM.Tenant.TrialEndsAt);
                oTimelineItem.ColorStyle = TsgcHTMLColor.hcWarning;
            }

            oTimelineItem = oTimeline.Items.Add();
            oTimelineItem.Title = "Plan: " + aVM.Tenant.PlanName;
            oTimelineItem.Content = "Current status is " + aVM.Tenant.Status + ".";
            oTimelineItem.Timestamp = DateStr(DateTime.Now);
            oTimelineItem.ColorStyle = TsgcHTMLColor.hcPrimary;
            oCol.AddRaw(CardWrap("Lifecycle", oTimeline.HTML));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            // Onboarding nudge, rendered as a skeleton when incomplete.
            if (aVM.Tenant.OnboardingStep < 4)
            {
                var oPlaceholder = new TsgcHTMLComponent_Placeholder();
                oPlaceholder.LineCount = 3;
                oPlaceholder.ShowTitle = true;
                oPlaceholder.ShowButtons = true;
                oPlaceholder.Animation = TsgcHTMLPlaceholderAnimation.paWave;
                oBody = new TsgcHTMLNodeList();
                oBody.AddElement("p",
                    "Onboarding is not finished, so parts of this workspace are " +
                    "still empty. The wizard remembers where you stopped.",
                    "text-muted");
                oBody.AddRaw(oPlaceholder.HTML);
                oBody.AddRaw(ButtonHTML("Resume onboarding", "/app/onboarding",
                    TsgcHTMLButtonStyle.bsPrimary, "mt-3"));
                oRoot.AddRaw(CardWrap("Finish setting up", oBody.HTML));
            }

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Dashboard", vResult, aCtx);
            return vResult;
        }

        public string BuildOnboardingPage(int aStep, TSaaSTenant aTenant,
            string aNote, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLComponent_Form oForm;
            TsgcHTMLFormField oField;
            TsgcHTMLNodeList oBody;
            TsgcHTMLForm oPost;
            TsgcHTMLButton oBtn;
            TsgcHTMLStepItem oItem;
            int vI;
            int vStep;
            var vTitles = new string[4];
            var vDescs = new string[4];

            vTitles[0] = "Workspace";
            vTitles[1] = "Branding";
            vTitles[2] = "Invite the team";
            vTitles[3] = "First project";
            vDescs[0] = "Name and timezone";
            vDescs[1] = "Logo and a welcome note";
            vDescs[2] = "Who else needs access";
            vDescs[3] = "Something to work on";

            vStep = aStep;
            if (vStep < 0)
                vStep = 0;
            if (vStep > 3)
                vStep = 3;

            oRoot.Add(new TsgcHTMLHeading("Set up " + aTenant.Name, 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "Every step is saved as you go, so you can leave and come back. " +
                "The wizard resumes at step " + (vStep + 1).ToString(GFmt) + "."));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            var oStepper = new TsgcHTMLComponent_Stepper();
            oStepper.Layout = TsgcHTMLStepperLayout.slHorizontal;
            oStepper.ShowContent = false;
            oStepper.CurrentColor = CS_SAAS_ACCENT;
            oStepper.CompletedColor = CS_SAAS_ACCENT;
            for (vI = 0; vI <= 3; vI++)
            {
                oItem = oStepper.Items.Add();
                oItem.Title = vTitles[vI];
                oItem.Description = vDescs[vI];
                if (vI < vStep)
                    oItem.State = TsgcHTMLStepState.ssCompleted;
                else if (vI == vStep)
                    oItem.State = TsgcHTMLStepState.ssCurrent;
                else
                    oItem.State = TsgcHTMLStepState.ssUpcoming;
            }
            oRoot.AddRaw(CardWrap("", oStepper.HTML));

            switch (vStep)
            {
                case 0:
                    {
                        oForm = new TsgcHTMLComponent_Form();
                        oForm.Action = "/app/onboarding/step";
                        oForm.Method = TsgcHTMLFormMethod.fmPost;
                        oForm.SubmitText = "Save and continue";
                        oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftHidden;
                        oField.Name = "step";
                        oField.Value = "0";

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftText;
                        oField.Name = "workspace_name";
                        oField.Label_ = "Workspace name";
                        oField.Value = aTenant.Name;
                        oField.Required = true;
                        oField.ColSpan = 8;

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftSelect;
                        oField.Name = "timezone";
                        oField.Label_ = "Timezone";
                        oField.ColSpan = 4;
                        oField.Options.Add("UTC=UTC");
                        oField.Options.Add("Europe/Madrid=Europe/Madrid");
                        oField.Options.Add("Europe/London=Europe/London");
                        oField.Options.Add("America/New_York=America/New York");
                        oRoot.AddRaw(CardWrap("Step 1 of 4 - workspace", oForm.HTML));

                        var oVideo = new TsgcHTMLComponent_Video();
                        oVideo.Poster = CS_POSTER_SVG;
                        oVideo.Controls = true;
                        oVideo.Responsive = true;
                        oRoot.AddRaw(CardWrap("Two minute tour", oVideo.HTML));
                    }
                    break;
                case 1:
                    {
                        oBody = new TsgcHTMLNodeList();
                        oPost = new TsgcHTMLForm();
                        oPost.Action = "/app/onboarding/step";
                        oPost.Method = "POST";
                        oPost.Enctype = "multipart/form-data";
                        oPost.AddHidden("step", "1");

                        var oUpload = new TsgcHTMLComponent_FileUpload();
                        oUpload.Title = "Workspace logo";
                        oUpload.Subtitle = "PNG or SVG, up to 1 MB.";
                        oUpload.Accept = ".png,.svg,.jpg";
                        oUpload.InputName = "logo";
                        oUpload.MaxSize = "1MB";
                        oUpload.DragDropEnabled = true;
                        oPost.AddRaw(oUpload.HTML);

                        var oEditor = new TsgcHTMLTextArea("welcome_note");
                        oEditor.Label_ = "Welcome note";
                        oEditor.Value = aNote;
                        oEditor.Rows = 5;
                        oEditor.Placeholder = "A short welcome note for new members.";
                        oEditor.ColClass = "mt-3";
                        oPost.Add(oEditor);

                        oBtn = new TsgcHTMLButton("Save and continue",
                            TsgcHTMLButtonStyle.bsPrimary);
                        oBtn.ButtonType = "submit";
                        oBtn.CSSClass = "mt-3";
                        oPost.Add(oBtn);
                        oBody.AddRaw(oPost.HTML);
                        oRoot.AddRaw(CardWrap("Step 2 of 4 - branding", oBody.HTML));
                    }
                    break;
                case 2:
                    {
                        oForm = new TsgcHTMLComponent_Form();
                        oForm.Action = "/app/onboarding/step";
                        oForm.Method = TsgcHTMLFormMethod.fmPost;
                        oForm.SubmitText = "Save and continue";
                        oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftHidden;
                        oField.Name = "step";
                        oField.Value = "2";

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftEmail;
                        oField.Name = "invite_email";
                        oField.Label_ = "Invite a teammate (optional)";
                        oField.Placeholder = "colleague@example.com";
                        oField.HelpText =
                            "The invitation link is shown on the team page, not emailed.";
                        oField.ColSpan = 8;

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftSelect;
                        oField.Name = "invite_role";
                        oField.Label_ = "Role";
                        oField.ColSpan = 4;
                        for (vI = 1; vI <= CS_TENANT_ROLES.Length - 1; vI++)
                            oField.Options.Add(CS_TENANT_ROLES[vI] + "=" +
                                CS_TENANT_ROLE_NAMES[vI]);
                        oRoot.AddRaw(CardWrap("Step 3 of 4 - team", oForm.HTML));
                    }
                    break;
                default:
                    {
                        oForm = new TsgcHTMLComponent_Form();
                        oForm.Action = "/app/onboarding/step";
                        oForm.Method = TsgcHTMLFormMethod.fmPost;
                        oForm.SubmitText = "Finish setup";
                        oForm.SubmitStyle = TsgcHTMLButtonStyle.bsSuccess;

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftHidden;
                        oField.Name = "step";
                        oField.Value = "3";

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftText;
                        oField.Name = "project_name";
                        oField.Label_ = "First project";
                        oField.Placeholder = "Onboarding revamp";
                        oField.ColSpan = 12;

                        oField = oForm.Fields.Add();
                        oField.FieldType = TsgcHTMLFieldType.ftTextArea;
                        oField.Name = "project_desc";
                        oField.Label_ = "What is it about?";
                        oField.TextAreaRows = 3;
                        oField.ColSpan = 12;
                        oRoot.AddRaw(CardWrap("Step 4 of 4 - first project", oForm.HTML));
                    }
                    break;
            }

            if (vStep > 0)
                oRoot.AddRaw(PostButton("/app/onboarding/step", "Back",
                    TsgcHTMLButtonStyle.bsOutlineSecondary,
                    new string[] { "step", "back" },
                    new string[] { vStep.ToString(GFmt), "1" }, "", ""));

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Onboarding", vResult, aCtx);
            return vResult;
        }

        public string BuildProjectsPage(DataTable aDS, TSaaSProject[] aRows,
            TSaaSUser[] aOwners, int aPage, int aPageCount, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLTableRow oRow;
            TsgcHTMLFormField oField;
            int vI;
            bool vCanEdit;

            vCanEdit = !SameText(aCtx.Role, CS_ROLE_READONLY);

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Projects" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Projects", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            if (Len(aRows) == 0)
                oRoot.AddRaw(EmptyStateHTML("No projects yet",
                    "Everything in this list is filtered by the tenant id on your " +
                    "session. Create the first project to see it appear.", CS_ICO_GRID,
                    "Create a project", "#"));
            else
            {
                // The searchable/paged view, bound straight from the dataset.
                var oTable = new TsgcHTMLComponent_DataTable();
                oTable.Title = "All projects in this workspace";
                oTable.ShowSearch = true;
                oTable.SearchPlaceholder = "Filter projects";
                oTable.ShowRowCount = true;
                oTable.ShowPageSize = true;
                oTable.Grid.Striped = true;
                oTable.Grid.Hover = true;
                oTable.Grid.Responsive = true;
                oTable.Grid.ShowSort = true;
                oTable.Grid.EmptyText = "No projects match.";
                if (aDS != null)
                    oTable.LoadFromDataSet(aDS, 10);
                oRoot.AddRaw(oTable.HTML);

                // Row actions live in their own table so each stays a real POST form.
                var oGrid = new TsgcHTMLTable();
                oGrid.CSSClass = "table table-sm align-middle mb-0";
                oGrid.Responsive = true;
                oGrid.AddColumn("Project");
                oGrid.AddColumn("Owner");
                oGrid.AddColumn("Status");
                oGrid.AddColumn("Tasks", "text-end");
                oGrid.AddColumn("Actions", "text-end");
                for (vI = 0; vI <= Len(aRows) - 1; vI++)
                {
                    oRow = oGrid.AddRow();
                    oRow.AddCellRaw(ButtonHTML(aRows[vI].Name,
                        "/app/projects/" + aRows[vI].Id.ToString(GFmt),
                        TsgcHTMLButtonStyle.bsLink, "p-0"));
                    oRow.AddCellText(aRows[vI].OwnerName);
                    oRow.AddCellRaw(BadgeHTML(aRows[vI].Status,
                        StatusBadge(aRows[vI].Status), true));
                    oRow.AddCellText(aRows[vI].DoneCount.ToString(GFmt) + " / " +
                        aRows[vI].TaskCount.ToString(GFmt), "text-end");
                    if (vCanEdit)
                        oRow.AddCellRaw(PostButton("/app/projects/delete", "Delete",
                            TsgcHTMLButtonStyle.bsOutlineDanger,
                            new string[] { "id" },
                            new string[] { aRows[vI].Id.ToString(GFmt) },
                            "Delete this project and its tasks?"), "text-end");
                    else
                        oRow.AddCellText("read only", "text-end text-muted small");
                }
                oRoot.AddRaw(CardWrap("Manage", oGrid.HTML));
            }

            if (aPageCount > 1)
            {
                var oPager = new TsgcHTMLComponent_Pagination();
                oPager.CurrentPage = aPage;
                oPager.TotalPages = aPageCount;
                oPager.BaseURL = "/app/projects?page=";
                oPager.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                oPager.ShowFirstLast = true;
                oRoot.AddRaw(oPager.HTML);
            }

            if (vCanEdit)
            {
                var oForm = new TsgcHTMLComponent_Form();
                oForm.Action = "/app/projects/save";
                oForm.Method = TsgcHTMLFormMethod.fmPost;
                oForm.SubmitText = "Save project";
                oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftHidden;
                oField.Name = "id";
                oField.Value = "0";

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftText;
                oField.Name = "name";
                oField.Label_ = "Name";
                oField.Required = true;
                oField.ColSpan = 12;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftTextArea;
                oField.Name = "description";
                oField.Label_ = "Description";
                oField.TextAreaRows = 3;
                oField.ColSpan = 12;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "status";
                oField.Label_ = "Status";
                oField.ColSpan = 6;
                oField.Options.Add("active=Active");
                oField.Options.Add("on_hold=On hold");
                oField.Options.Add("archived=Archived");

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "owner_id";
                oField.Label_ = "Owner";
                oField.ColSpan = 6;
                for (vI = 0; vI <= Len(aOwners) - 1; vI++)
                    oField.Options.Add(aOwners[vI].Id.ToString(GFmt) + "=" +
                        aOwners[vI].DisplayName);

                var oModal = new TsgcHTMLComponent_Modal();
                oModal.ModalID = "sgcProjectModal";
                oModal.Title = "New project";
                oModal.Size = TsgcHTMLModalSize.msLarge;
                oModal.Body = oForm.HTML;
                oRoot.AddRaw(TsgcHTMLComponent_Modal.BuildTriggerButton
                    ("sgcProjectModal", "New project", TsgcHTMLButtonStyle.bsPrimary));
                oRoot.AddRaw(oModal.HTML);
            }

            if (aCtx.Flash != "")
            {
                var oToast = new TsgcHTMLComponent_Toast();
                oToast.Title = "Projects";
                oToast.Body = aCtx.Flash;
                oToast.ColorStyle = TsgcHTMLColor.hcSuccess;
                oToast.AutoHide = true;
                var oBody = new TsgcHTMLNodeList();
                oBody.AddRaw(TsgcHTMLComponent_Toast.BuildContainer(oToast.HTML,
                    TsgcHTMLToastPosition.tpBottomEnd));
                oRoot.AddRaw(oBody.HTML);
            }

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Projects", vResult, aCtx);
            return vResult;
        }

        public string BuildProjectDetailPage(TSaaSProject aProject, TSaaSTask[] aTasks,
            TSaaSUser[] aMembers, DataTable aMembersDS, string aNote,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLTabItem oTab;
            TsgcHTMLGridColumn oColumn;
            TsgcHTMLNodeList oBody;
            TsgcHTMLForm oForm;
            TsgcHTMLButton oBtn;
            int vI;

            oRoot.AddRaw(BreadcrumbHTML(
                new string[] { "Workspace", "Projects", aProject.Name },
                new string[] { "/app", "/app/projects", "" }));
            oRoot.Add(new TsgcHTMLHeading(aProject.Name, 1));
            oRoot.AddRaw(BadgeHTML(aProject.Status, StatusBadge(aProject.Status),
                true));
            oRoot.Add(new TsgcHTMLParagraph(aProject.Description));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            var oTabs = new TsgcHTMLComponent_Tabs();

            // Tasks.
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.Responsive = true;
            oGrid.EmptyText = "No tasks on this project.";
            oColumn = oGrid.Columns.Add();
            oColumn.Name = "title";
            oColumn.Title = "Task";
            oColumn = oGrid.Columns.Add();
            oColumn.Name = "status";
            oColumn.Title = "Status";
            oColumn = oGrid.Columns.Add();
            oColumn.Name = "assignee";
            oColumn.Title = "Assignee";
            oColumn = oGrid.Columns.Add();
            oColumn.Name = "due";
            oColumn.Title = "Due";
            for (vI = 0; vI <= Len(aTasks) - 1; vI++)
                oGrid.AddRow(aTasks[vI].Title, aTasks[vI].Status,
                    aTasks[vI].AssigneeName, DateStr(aTasks[vI].DueAt));
            oTab = oTabs.Items.Add();
            oTab.Title = "Tasks";
            oTab.Active = true;
            oTab.Content = oGrid.HTML;

            // Members: Transfer + MultiSelect, both fed from the tenant dataset.
            oBody = new TsgcHTMLNodeList();
            oForm = new TsgcHTMLForm();
            oForm.Action = "/app/projects/save";
            oForm.Method = "POST";
            oForm.AddHidden("id", aProject.Id.ToString(GFmt));
            oForm.AddHidden("name", aProject.Name);
            oForm.AddHidden("description", aProject.Description);
            oForm.AddHidden("status", aProject.Status);

            var oTransfer = new TsgcHTMLComponent_Transfer();
            oTransfer.TitleSource = "Workspace members";
            oTransfer.TitleTarget = "On this project";
            oTransfer.FieldName = "assigned_members";
            oTransfer.ShowFilter = true;
            oTransfer.CSSHeight = "240px";
            for (vI = 0; vI <= Len(aMembers) - 1; vI++)
                oTransfer.AddItem(aMembers[vI].Id.ToString(GFmt),
                    aMembers[vI].DisplayName,
                    aMembers[vI].Id == aProject.OwnerId);
            oForm.AddRaw(oTransfer.HTML);

            var oMulti = new TsgcHTMLComponent_MultiSelect();
            oMulti.FieldName = "watchers";
            oMulti.Placeholder = "Watchers";
            oMulti.ShowSearch = true;
            if (aMembersDS != null)
                oMulti.LoadFromDataSet(aMembersDS, "id", "display_name");
            oForm.AddRaw(oMulti.HTML);

            oBtn = new TsgcHTMLButton("Save membership", TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "mt-3";
            oForm.Add(oBtn);
            oBody.AddRaw(oForm.HTML);
            oTab = oTabs.Items.Add();
            oTab.Title = "Members";
            oTab.Content = oBody.HTML;

            // Notes.
            oBody = new TsgcHTMLNodeList();
            oForm = new TsgcHTMLForm();
            oForm.Action = "/app/settings/save";
            oForm.Method = "POST";
            oForm.AddHidden("scope", "project_note");
            oForm.AddHidden("project_id", aProject.Id.ToString(GFmt));
            var oEditor = new TsgcHTMLTextArea("note");
            oEditor.Label_ = "Project notes";
            oEditor.Value = aNote;
            oEditor.Rows = 8;
            oEditor.Placeholder = "Notes about this project.";
            oForm.Add(oEditor);
            oBtn = new TsgcHTMLButton("Save note", TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "mt-3";
            oForm.Add(oBtn);
            oBody.AddRaw(oForm.HTML);
            oTab = oTabs.Items.Add();
            oTab.Title = "Knowledge base";
            oTab.Content = oBody.HTML;

            oRoot.AddRaw(CardWrap("", oTabs.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildAppShell(aProject.Name, vResult, aCtx);
            return vResult;
        }

        public string BuildTasksPage(DataTable aKanbanDS, TSaaSTask[] aTasks,
            TSaaSProject[] aProjects, TSaaSUser[] aMembers, int aYear, int aMonth,
            string aFrom, string aTo, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oCol;
            TsgcHTMLFormField oField;
            TsgcHTMLCalendarEvent oEvent;
            TsgcHTMLTableRow oMoveRow;
            TsgcHTMLForm oMove;
            TsgcHTMLSelect oMoveSelect;
            TsgcHTMLButton oMoveBtn;
            int vI;

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Tasks" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Tasks", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            // Date range filter, built from two DatePicker components.
            var oRange = new TsgcHTMLForm();
            oRange.Action = "/app/tasks";
            oRange.Method = "GET";
            oRange.CSSClass = "row g-2 align-items-end mb-3";
            var oFrom = new TsgcHTMLComponent_DatePicker();
            oFrom.ElementName = "from";
            oFrom.Label_ = "Due from";
            oFrom.Value = aFrom;
            oRange.AddRaw(oFrom.HTML);
            var oTo = new TsgcHTMLComponent_DatePicker();
            oTo.ElementName = "to";
            oTo.Label_ = "Due to";
            oTo.Value = aTo;
            oRange.AddRaw(oTo.HTML);
            var oBtn = new TsgcHTMLButton("Apply", TsgcHTMLButtonStyle.bsOutlinePrimary);
            oBtn.ButtonType = "submit";
            oRange.Add(oBtn);
            oRoot.AddRaw(CardWrap("Filter by due date", oRange.HTML));

            var oBoard = new TsgcHTMLComponent_KanbanBoard();
            oBoard.BoardID = "sgcTaskBoard";
            oBoard.CSSHeight = "460px";
            // Drag-and-drop would pull SortableJS from a CDN, and this demo runs
            // offline, so the board is read-only and the move controls below post
            // to the same tenant-scoped route.
            oBoard.DragEnabled = false;
            if (aKanbanDS != null)
                oBoard.LoadFromDataSet(aKanbanDS, "status", "title", "project_name",
                    "assignee_name");
            oRoot.AddRaw(CardWrap("Board", oBoard.HTML));

            // Move controls: one POST form per task, all scoped by the session tenant.
            if (!SameText(aCtx.Role, CS_ROLE_READONLY))
            {
                var oMoveTable = new TsgcHTMLTable();
                oMoveTable.CSSClass = "table table-sm align-middle mb-0";
                oMoveTable.Responsive = true;
                oMoveTable.AddColumn("Task");
                oMoveTable.AddColumn("Project");
                oMoveTable.AddColumn("Assignee");
                oMoveTable.AddColumn("Due");
                oMoveTable.AddColumn("Move to", "text-end");
                if (Len(aTasks) == 0)
                    oMoveTable.AddEmptyRow("No tasks in this workspace.", 5);
                else
                    for (vI = 0; vI <= Len(aTasks) - 1; vI++)
                    {
                        oMoveRow = oMoveTable.AddRow();
                        oMoveRow.AddCellText(aTasks[vI].Title);
                        oMoveRow.AddCellText(aTasks[vI].ProjectName);
                        oMoveRow.AddCellText(aTasks[vI].AssigneeName);
                        oMoveRow.AddCellText(DateStr(aTasks[vI].DueAt));
                        oMove = new TsgcHTMLForm();
                        oMove.Action = "/app/tasks/move";
                        oMove.Method = "POST";
                        oMove.CSSClass = "d-flex gap-2 justify-content-end";
                        oMove.AddHidden("id", aTasks[vI].Id.ToString(GFmt));
                        oMoveSelect = new TsgcHTMLSelect();
                        oMoveSelect.Name = "status";
                        oMoveSelect.CSSClass = "form-select form-select-sm";
                        oMoveSelect.AddOption("todo", "To do",
                            SameText(aTasks[vI].Status, "todo"));
                        oMoveSelect.AddOption("doing", "Doing",
                            SameText(aTasks[vI].Status, "doing"));
                        oMoveSelect.AddOption("review", "Review",
                            SameText(aTasks[vI].Status, "review"));
                        oMoveSelect.AddOption("done", "Done",
                            SameText(aTasks[vI].Status, "done"));
                        oMove.Add(oMoveSelect);
                        oMoveBtn = new TsgcHTMLButton("Move",
                            TsgcHTMLButtonStyle.bsOutlinePrimary);
                        oMoveBtn.ButtonType = "submit";
                        oMoveBtn.CSSClass = "btn-sm";
                        oMove.Add(oMoveBtn);
                        oMoveRow.AddCellRaw(oMove.HTML, "text-end");
                    }
                oRoot.AddRaw(CardWrap("Move a task", oMoveTable.HTML));
            }

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4";

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-7";
            var oCalendar = new TsgcHTMLComponent_Calendar();
            oCalendar.Year = aYear;
            oCalendar.Month = aMonth;
            oCalendar.HighlightToday = true;
            oCalendar.ShowNavigation = false;
            // Due dates live in TEXT columns, so they are parsed with the demo's
            // fixed-position parser and added as events, never read through a
            // field's AsDateTime (which honours the system locale and would fail
            // outside US date formats).
            for (vI = 0; vI <= Len(aTasks) - 1; vI++)
                if ((aTasks[vI].DueAt != DateTime.MinValue) &&
                    (aTasks[vI].DueAt.Year == aYear) &&
                    (aTasks[vI].DueAt.Month == aMonth))
                {
                    oEvent = oCalendar.Events.Add();
                    oEvent.Day = aTasks[vI].DueAt.Day;
                    oEvent.Title = aTasks[vI].Title;
                    oEvent.ColorStyle = TsgcHTMLColor.hcPrimary;
                }
            oCol.AddRaw(CardWrap("Due dates this month", oCalendar.HTML));
            oRow.Add(oCol);

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-5";
            if (SameText(aCtx.Role, CS_ROLE_READONLY))
                oCol.AddRaw(CardWrap("New task", EmptyStateHTML("Read only",
                    "Your role can view tasks but not change them.", CS_ICO_LOCK, "", "")));
            else
            {
                var oForm = new TsgcHTMLComponent_Form();
                oForm.Action = "/app/tasks/save";
                oForm.Method = TsgcHTMLFormMethod.fmPost;
                oForm.SubmitText = "Add task";
                oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftHidden;
                oField.Name = "id";
                oField.Value = "0";

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftText;
                oField.Name = "title";
                oField.Label_ = "Title";
                oField.Required = true;
                oField.ColSpan = 12;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "project_id";
                oField.Label_ = "Project";
                oField.ColSpan = 12;
                for (vI = 0; vI <= Len(aProjects) - 1; vI++)
                    oField.Options.Add(aProjects[vI].Id.ToString(GFmt) + "=" +
                        aProjects[vI].Name);

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "assignee_id";
                oField.Label_ = "Assignee";
                oField.ColSpan = 6;
                for (vI = 0; vI <= Len(aMembers) - 1; vI++)
                    oField.Options.Add(aMembers[vI].Id.ToString(GFmt) + "=" +
                        aMembers[vI].DisplayName);

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftDate;
                oField.Name = "due_at";
                oField.Label_ = "Due";
                oField.ColSpan = 6;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "status";
                oField.Label_ = "Status";
                oField.ColSpan = 12;
                oField.Options.Add("todo=To do");
                oField.Options.Add("doing=Doing");
                oField.Options.Add("review=Review");
                oField.Options.Add("done=Done");

                var oBody = new TsgcHTMLNodeList();
                oBody.AddRaw(oForm.HTML);
                oCol.AddRaw(CardWrap("New task", oBody.HTML));
            }
            oRow.Add(oCol);
            oRoot.Add(oRow);

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Tasks", vResult, aCtx);
            return vResult;
        }

        public string BuildTeamPage(DataTable aMembersDS, TSaaSTeamVM aVM,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLTable oTable;
            TsgcHTMLTableRow oRow;
            TsgcHTMLAlert oAlert;
            TsgcHTMLSelect oSelect;
            TsgcHTMLForm oPost;
            TsgcHTMLButton oBtn;
            int vI;
            int vJ;
            bool vAtLimit;

            vAtLimit = (aVM.Usage.MaxUsers > 0) &&
                (aVM.Usage.Users >= aVM.Usage.MaxUsers);

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Team" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Team", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            oRoot.AddRaw(LimitBar("Seats used", aVM.Usage.Users, aVM.Usage.MaxUsers,
                "seats"));

            if (vAtLimit)
            {
                oAlert = new TsgcHTMLAlert(
                    "This workspace is at the seat limit of its " + aVM.Usage.PlanName +
                    " plan. Adding another member is refused by the server until the " +
                    "plan changes.");
                oAlert.Style = TsgcHTMLAlertStyle.asWarning;
                oAlert.AddRaw(ButtonHTML("Upgrade the plan", "/app/billing",
                    TsgcHTMLButtonStyle.bsPrimary, "btn-sm ms-2"));
                oRoot.Add(oAlert);
            }

            // Member list straight from the tenant-scoped dataset.
            var oUsers = new TsgcHTMLComponent_UserManagement();
            oUsers.ShowSearch = true;
            oUsers.ShowAddButton = false;
            oUsers.ShowRoles = true;
            oUsers.ShowStatus = true;
            oUsers.ShowLastLogin = true;
            oUsers.ShowActions = false;
            oUsers.AllowImpersonate = false;
            oUsers.EmptyText = "No members.";
            if (aMembersDS != null)
                oUsers.LoadFromDataSet(aMembersDS, "id", "display_name", "email",
                    "role", "status");
            oRoot.AddRaw(CardWrap("Members", oUsers.HTML));

            // Role changes and removals, each its own POST form.
            if (aVM.CanManage)
            {
                oTable = new TsgcHTMLTable();
                oTable.CSSClass = "table table-sm align-middle mb-0";
                oTable.Responsive = true;
                oTable.AddColumn("Member");
                oTable.AddColumn("Role");
                oTable.AddColumn("", "text-end");
                for (vI = 0; vI <= Len(aVM.Members) - 1; vI++)
                {
                    oRow = oTable.AddRow();
                    oRow.AddCellRaw(AvatarFor(aVM.Members[vI].DisplayName,
                        TsgcHTMLAvatarSize.asSmall) + " " +
                        Esc(aVM.Members[vI].DisplayName));

                    oPost = new TsgcHTMLForm();
                    oPost.Action = "/app/team/role";
                    oPost.Method = "POST";
                    oPost.CSSClass = "d-flex gap-2";
                    oPost.AddHidden("user_id", aVM.Members[vI].Id.ToString(GFmt));
                    oSelect = new TsgcHTMLSelect();
                    oSelect.Name = "role";
                    oSelect.CSSClass = "form-select form-select-sm";
                    for (vJ = 0; vJ <= CS_TENANT_ROLES.Length - 1; vJ++)
                        oSelect.AddOption(CS_TENANT_ROLES[vJ], CS_TENANT_ROLE_NAMES[vJ],
                            SameText(CS_TENANT_ROLES[vJ], aVM.Members[vI].Role));
                    oPost.Add(oSelect);
                    oBtn = new TsgcHTMLButton("Set",
                        TsgcHTMLButtonStyle.bsOutlinePrimary);
                    oBtn.ButtonType = "submit";
                    oBtn.CSSClass = "btn-sm";
                    oPost.Add(oBtn);
                    oRow.AddCellRaw(oPost.HTML);

                    oRow.AddCellRaw(PostButton("/app/team/remove", "Remove",
                        TsgcHTMLButtonStyle.bsOutlineDanger,
                        new string[] { "user_id" },
                        new string[] { aVM.Members[vI].Id.ToString(GFmt) },
                        "Remove this member?"), "text-end");
                }
                oRoot.AddRaw(CardWrap("Roles", oTable.HTML));
            }

            // Pending invitations.
            oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.Responsive = true;
            oTable.AddColumn("Email");
            oTable.AddColumn("Role");
            oTable.AddColumn("Expires");
            oTable.AddColumn("State");
            oTable.AddColumn("", "text-end");
            if (Len(aVM.Invites) == 0)
                oTable.AddEmptyRow("No invitations.", 5);
            else
                for (vI = 0; vI <= Len(aVM.Invites) - 1; vI++)
                {
                    oRow = oTable.AddRow();
                    oRow.AddCellText(aVM.Invites[vI].Email);
                    oRow.AddCellRaw(ChipHTML(aVM.Invites[vI].Role,
                        TsgcHTMLBadgeStyle.bgSecondary));
                    oRow.AddCellText(StampStr(aVM.Invites[vI].ExpiresAt));
                    if (aVM.Invites[vI].AcceptedAt != DateTime.MinValue)
                        oRow.AddCellRaw(BadgeHTML("accepted",
                            TsgcHTMLBadgeStyle.bgSuccess, true));
                    else if (aVM.Invites[vI].ExpiresAt < DateTime.Now)
                        oRow.AddCellRaw(BadgeHTML("expired",
                            TsgcHTMLBadgeStyle.bgDanger, true));
                    else
                        oRow.AddCellRaw(BadgeHTML("pending",
                            TsgcHTMLBadgeStyle.bgInfo, true));
                    if (aVM.CanManage &&
                        (aVM.Invites[vI].AcceptedAt == DateTime.MinValue))
                        oRow.AddCellRaw(PostButton("/app/team/invite", "Revoke",
                            TsgcHTMLButtonStyle.bsOutlineDanger,
                            new string[] { "revoke_id" },
                            new string[] { aVM.Invites[vI].Id.ToString(GFmt) }, ""),
                            "text-end");
                    else
                        oRow.AddCellText("", "text-end");
                }
            oRoot.AddRaw(CardWrap("Invitations", oTable.HTML));

            if (aVM.InviteURL != "")
            {
                var oBody = new TsgcHTMLNodeList();
                oAlert = new TsgcHTMLAlert(
                    "No email was sent: this demo has no SMTP server. Copy the link " +
                    "below to " + aVM.InviteEmail + " yourself.");
                oAlert.Style = TsgcHTMLAlertStyle.asWarning;
                oBody.Add(oAlert);
                oBody.AddRaw(CodeBlock(aVM.InviteURL));
                oRoot.AddRaw(CardWrap("Invitation link", oBody.HTML));
            }

            if (aVM.CanManage)
            {
                var oForm = new TsgcHTMLComponent_Form();
                oForm.Action = "/app/team/invite";
                oForm.Method = TsgcHTMLFormMethod.fmPost;
                oForm.Layout = TsgcHTMLFormLayout.flVertical;
                oForm.SubmitText = "Create invitation";
                if (vAtLimit)
                    oForm.SubmitStyle = TsgcHTMLButtonStyle.bsSecondary;
                else
                    oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

                TsgcHTMLFormField oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftEmail;
                oField.Name = "email";
                oField.Label_ = "Email address";
                oField.Required = true;
                oField.ColSpan = 8;
                if (vAtLimit)
                {
                    oField.Validation = TsgcHTMLFieldValidation.fvInvalid;
                    oField.Feedback =
                        "Seat limit reached. The server refuses this until you upgrade.";
                }

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "role";
                oField.Label_ = "Role";
                oField.ColSpan = 4;
                for (vI = 1; vI <= CS_TENANT_ROLES.Length - 1; vI++)
                    oField.Options.Add(CS_TENANT_ROLES[vI] + "=" +
                        CS_TENANT_ROLE_NAMES[vI]);

                oRoot.AddRaw(CardWrap("Invite a teammate", oForm.HTML));
            }

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Team", vResult, aCtx);
            return vResult;
        }

        public string BuildRolesPage(DataTable aRolesDS, DataTable aPermsDS,
            DataTable aGrantsDS, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            int vI;

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Roles" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Roles and permissions", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "The grants below belong to this workspace only. They are stored with " +
                "the tenant id and loaded with a tenant-scoped query, so another " +
                "tenant editing its matrix cannot touch yours."));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            var oMatrix = new TsgcHTMLComponent_RolesPermissions();
            oMatrix.MatrixID = "sgcRoleMatrix";
            oMatrix.ShowDescriptions = true;
            oMatrix.ShowCategories = true;
            oMatrix.Striped = true;
            oMatrix.PermissionCaption = "Permission";
            // One shared field name for the whole matrix. Every cell then carries
            // it, valued <role>|<permission>, and the browser posts the checked
            // ones like any other checkbox group.
            oMatrix.FieldName = "sgc_grants";
            oMatrix.ReadOnly = !(SameText(aCtx.Role, CS_ROLE_OWNER) ||
                SameText(aCtx.Role, CS_ROLE_ADMIN));
            if (aRolesDS != null)
                oMatrix.LoadRolesFromDataSet(aRolesDS, "role_id", "role_name");
            else
                for (vI = 0; vI <= CS_TENANT_ROLES.Length - 1; vI++)
                    oMatrix.AddRole(CS_TENANT_ROLES[vI], CS_TENANT_ROLE_NAMES[vI]);
            if (aPermsDS != null)
                oMatrix.LoadPermissionsFromDataSet(aPermsDS, "perm_id", "perm_name",
                    "perm_desc", "perm_cat");
            else
                for (vI = 0; vI <= CS_PERMISSIONS.Length - 1; vI++)
                    oMatrix.AddPermission(CS_PERMISSIONS[vI],
                        CS_PERMISSION_NAMES[vI], CS_PERMISSION_CATS[vI]);
            if (aGrantsDS != null)
                oMatrix.LoadGrantsFromDataSet(aGrantsDS, "role", "permission");

            var oBody = new TsgcHTMLNodeList();
            var oForm = new TsgcHTMLForm();
            oForm.Action = "/app/roles/grant";
            oForm.Method = "POST";
            oForm.AddRaw(oMatrix.HTML);
            if (!oMatrix.ReadOnly)
            {
                var oBtn = new TsgcHTMLButton("Save the matrix",
                    TsgcHTMLButtonStyle.bsPrimary);
                oBtn.ButtonType = "submit";
                oBtn.CSSClass = "mt-3";
                oForm.Add(oBtn);
                // FieldName is all the matrix needs to submit as an ordinary
                // form POST. No JavaScript takes part in saving it.
            }
            oBody.AddRaw(oForm.HTML);
            oRoot.AddRaw(CardWrap("Permission matrix", oBody.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Roles", vResult, aCtx);
            return vResult;
        }

        public string BuildBillingPage(DataTable aInvoicesDS, TSaaSBillingVM aVM,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oRow;
            TsgcHTMLContainer oCol;
            TsgcHTMLCard oCard;
            TsgcHTMLAlert oAlert;
            TsgcHTMLTableRow oTR;
            TsgcHTMLNodeList oBody;
            int vI;
            double[] vValues;
            string vPrice;
            bool vCanManage;

            vCanManage = SameText(aCtx.Role, CS_ROLE_OWNER) ||
                SameText(aCtx.Role, CS_ROLE_ADMIN);

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Billing" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Billing", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            oAlert = new TsgcHTMLAlert(
                "Billing here is simulated. There is no payment provider connected: " +
                "changing a plan records a subscription row and issues a local " +
                "invoice, and no card is ever charged.");
            oAlert.Style = TsgcHTMLAlertStyle.asWarning;
            oRoot.Add(oAlert);

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCol("Current plan", aVM.Plan.Name, CS_ICO_CARD,
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", aVM.Tenant.Status));
            oRow.AddRaw(StatCol("Monthly", Money(aVM.Plan.PriceMonthly), CS_ICO_CARD,
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "simulated"));
            oRow.AddRaw(StatCol("Renews", DateStr(aVM.Sub.RenewsAt), CS_ICO_LIST,
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "next period"));
            oRow.AddRaw(StatCol("Invoices", Num(Len(aVM.Invoices)), CS_ICO_DB,
                TsgcHTMLStatColor.scSecondary, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "this tenant only"));
            oRoot.Add(oRow);

            // Usage against the plan.
            oBody = new TsgcHTMLNodeList();
            oBody.AddRaw(LimitBar("Users", aVM.Usage.Users, aVM.Usage.MaxUsers,
                "seats"));
            oBody.AddRaw(LimitBar("Projects", aVM.Usage.Projects,
                aVM.Usage.MaxProjects, ""));
            oBody.AddRaw(LimitBar("Storage", aVM.Usage.StorageMB,
                aVM.Usage.MaxStorageMB, "MB"));
            oRoot.AddRaw(CardWrap("Usage against your plan", oBody.HTML));

            // Plan chooser.
            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            for (vI = 0; vI <= Len(aVM.Plans) - 1; vI++)
            {
                oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-12 col-sm-6 col-xl-3";
                oCard = new TsgcHTMLCard();
                oCard.CSSClass = "h-100 shadow-sm";
                if (aVM.Plans[vI].Id == aVM.Plan.Id)
                    oCard.CSSClass = "h-100 shadow border-primary border-2";
                oCard.Header.AddElement("strong", aVM.Plans[vI].Name);
                if (aVM.Plans[vI].PriceMonthly == 0)
                    vPrice = "Free";
                else
                    vPrice = Money(aVM.Plans[vI].PriceMonthly) + " / month";
                oCard.Body.AddElement("div", vPrice, "h4 mb-2");
                oCard.Body.AddElement("div", Num(aVM.Plans[vI].MaxUsers) +
                    " users, " + Num(aVM.Plans[vI].MaxProjects) + " projects, " +
                    Num(aVM.Plans[vI].MaxStorageMB) + " MB", "small text-muted");
                if (aVM.Plans[vI].Id == aVM.Plan.Id)
                    oCard.Footer.AddRaw(BadgeHTML("Current plan",
                        TsgcHTMLBadgeStyle.bgPrimary, true));
                else if (vCanManage)
                    oCard.Footer.AddRaw(PostButton("/app/billing/change-plan",
                        "Switch to " + aVM.Plans[vI].Name,
                        TsgcHTMLButtonStyle.bsOutlinePrimary,
                        new string[] { "plan_id" },
                        new string[] { aVM.Plans[vI].Id.ToString(GFmt) }, "",
                        "btn-sm w-100"));
                else
                    oCard.Footer.AddElement("span", "Owner or admin only",
                        "text-muted small");
                oCol.Add(oCard);
                oRow.Add(oCol);
            }
            oRoot.Add(oRow);

            // Spend chart.
            var oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartType = TsgcHTMLChartType.ctBar;
            oChart.Title = "Invoiced per month";
            oChart.CSSHeight = "240px";
            oChart.ShowLegend = false;
            vValues = new double[Len(aVM.Spend)];
            for (vI = 0; vI <= Len(aVM.Spend) - 1; vI++)
            {
                oChart.AddLabel(aVM.Spend[vI].MonthLabel);
                vValues[vI] = aVM.Spend[vI].Value;
            }
            oChart.AddDataset("Invoiced", vValues, CS_SAAS_ACCENT);
            oRoot.AddRaw(CardWrap("Spend", oChart.HTML));

            // Invoice list from the dataset, plus a PDF link per row.
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.Responsive = true;
            oGrid.ShowSort = true;
            oGrid.EmptyText = "No invoices for this workspace.";
            if (aInvoicesDS != null)
                oGrid.LoadFromDataSet(aInvoicesDS);
            oRoot.AddRaw(CardWrap("Invoices, straight from TFDQuery", oGrid.HTML));

            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.Responsive = true;
            oTable.AddColumn("Number");
            oTable.AddColumn("Period");
            oTable.AddColumn("Total", "text-end");
            oTable.AddColumn("Status");
            oTable.AddColumn("PDF", "text-end");
            if (Len(aVM.Invoices) == 0)
                oTable.AddEmptyRow("No invoices.", 5);
            else
                for (vI = 0; vI <= Len(aVM.Invoices) - 1; vI++)
                {
                    oTR = oTable.AddRow();
                    oTR.AddCellText(aVM.Invoices[vI].Number);
                    oTR.AddCellText(DateStr(aVM.Invoices[vI].PeriodStart) + " - " +
                        DateStr(aVM.Invoices[vI].PeriodEnd));
                    oTR.AddCellText(Money(aVM.Invoices[vI].Total), "text-end");
                    oTR.AddCellRaw(BadgeHTML(aVM.Invoices[vI].Status,
                        StatusBadge(aVM.Invoices[vI].Status), true));
                    oTR.AddCellRaw(ButtonHTML("Download",
                        "/app/billing/invoice/" + aVM.Invoices[vI].Id.ToString(GFmt) +
                        ".pdf", TsgcHTMLButtonStyle.bsOutlineSecondary, "btn-sm"),
                        "text-end");
                }
            oRoot.AddRaw(CardWrap("Download invoices", oTable.HTML));

            var oPopover = new TsgcHTMLComponent_Popover();
            oPopover.Content = "How is the PDF produced?";
            oPopover.Title = "Server-side PDF";
            oPopover.Body = "TsgcHTMLExportPDF writes the bytes in-process and " +
                "the route streams them. The invoice is fetched with the session " +
                "tenant id in the WHERE clause, so another tenant cannot download it.";
            oPopover.Trigger = TsgcHTMLPopoverTrigger.ptClick;
            oPopover.ContentStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oRoot.AddRaw(oPopover.HTML);

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Billing", vResult, aCtx);
            return vResult;
        }

        public string BuildSettingsPage(TSaaSTenant aTenant, string aNote,
            string aLogoURL, string aTimezone, string aContact, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLField oField;
            TsgcHTMLNodeList oBody;

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Settings" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Workspace settings", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            oBody = new TsgcHTMLNodeList();
            var oForm = new TsgcHTMLForm();
            oForm.Action = "/app/settings/save";
            oForm.Method = "POST";
            oForm.Enctype = "multipart/form-data";
            oForm.AddHidden("scope", "workspace");

            oField = new TsgcHTMLField(TsgcHTMLInputType.itText, "workspace_name");
            oField.Label_ = "Workspace name";
            oField.Value = aTenant.Name;
            oField.ColClass = "mb-3";
            oForm.Add(oField);

            oField = new TsgcHTMLField(TsgcHTMLInputType.itText, "slug");
            oField.Label_ = "Slug";
            oField.Value = aTenant.Slug;
            oField.ReadOnly = true;
            oField.ColClass = "mb-3";
            oForm.Add(oField);

            oField = new TsgcHTMLField(TsgcHTMLInputType.itEmail, "contact");
            oField.Label_ = "Billing contact";
            oField.Value = aContact;
            oField.ColClass = "mb-3";
            oForm.Add(oField);

            var oSelect = new TsgcHTMLSelect();
            oSelect.Name = "timezone";
            oSelect.Label_ = "Timezone";
            oSelect.ColClass = "mb-3";
            oSelect.AddOption("UTC", "UTC", SameText(aTimezone, "UTC"));
            oSelect.AddOption("Europe/Madrid", "Europe/Madrid",
                SameText(aTimezone, "Europe/Madrid"));
            oSelect.AddOption("Europe/London", "Europe/London",
                SameText(aTimezone, "Europe/London"));
            oSelect.AddOption("America/New_York", "America/New York",
                SameText(aTimezone, "America/New_York"));
            oForm.Add(oSelect);

            var oUpload = new TsgcHTMLComponent_FileUpload();
            oUpload.Title = "Workspace logo";
            oUpload.Subtitle = "Stored per tenant. Nothing leaves this box.";
            oUpload.Accept = ".png,.svg,.jpg";
            oUpload.InputName = "logo";
            oUpload.MaxSize = "1MB";
            oForm.AddRaw(oUpload.HTML);

            var oEditor = new TsgcHTMLTextArea("welcome_note");
            oEditor.Label_ = "Welcome note";
            oEditor.Value = aNote;
            oEditor.Rows = 6;
            oEditor.Placeholder = "A note shown to new members.";
            oEditor.ColClass = "mb-3";
            oForm.Add(oEditor);

            var oBtn = new TsgcHTMLButton("Save settings", TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "mt-3";
            oForm.Add(oBtn);
            oBody.AddRaw(oForm.HTML);
            oRoot.AddRaw(CardWrap("General", oBody.HTML));

            oBody = new TsgcHTMLNodeList();
            var oImage = new TsgcHTMLComponent_Image();
            if (aLogoURL != "")
                oImage.Src = aLogoURL;
            else
                oImage.Src = CS_LOGO_SVG;
            oImage.Alt = aTenant.Name;
            oImage.CSSWidth = "96px";
            oImage.Shape = TsgcHTMLImageShape.isRounded;
            oBody.AddRaw(oImage.HTML);

            var oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("Tenant id (from your session)", "",
                aTenant.Id.ToString(GFmt), TsgcHTMLBadgeStyle.bgPrimary);
            oList.AddItem("Created", "", DateStr(aTenant.CreatedAt),
                TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Status", "", aTenant.Status,
                StatusBadge(aTenant.Status));
            oList.AddItem("Plan", "", aTenant.PlanName, TsgcHTMLBadgeStyle.bgInfo);
            oBody.AddRaw(oList.HTML);
            oRoot.AddRaw(CardWrap("Identity", oBody.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Settings", vResult, aCtx);
            return vResult;
        }

        public string BuildNotificationsPage(TSaaSNotificationRow[] aRows,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLTableRow oRow;
            int vI;
            TsgcHTMLColor vColor;

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Notifications" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Notifications", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            if (Len(aRows) == 0)
                oRoot.AddRaw(EmptyStateHTML("Nothing here",
                    "Notifications raised inside this workspace appear here.",
                    CS_ICO_BELL, "Back to the dashboard", "/app"));
            else
            {
                var oNotify = new TsgcHTMLComponent_Notification();
                oNotify.Title = "Inbox";
                oNotify.BellIcon = CS_ICO_BELL;
                oNotify.MaxVisible = 20;
                oNotify.EmptyText = "Nothing here.";
                for (vI = 0; vI <= Len(aRows) - 1; vI++)
                {
                    if (SameText(aRows[vI].Kind, "billing"))
                        vColor = TsgcHTMLColor.hcWarning;
                    else
                        vColor = TsgcHTMLColor.hcInfo;
                    oNotify.AddNotification(aRows[vI].Id.ToString(GFmt),
                        aRows[vI].Title, aRows[vI].Body, vColor,
                        StampStr(aRows[vI].CreatedAt));
                    if (aRows[vI].ReadAt != DateTime.MinValue)
                        oNotify.MarkAsRead(aRows[vI].Id.ToString(GFmt));
                }
                oRoot.AddRaw(CardWrap("Bell", oNotify.HTML));

                var oTable = new TsgcHTMLTable();
                oTable.CSSClass = "table table-sm align-middle mb-0";
                oTable.Responsive = true;
                oTable.AddColumn("When");
                oTable.AddColumn("Title");
                oTable.AddColumn("Kind");
                oTable.AddColumn("", "text-end");
                for (vI = 0; vI <= Len(aRows) - 1; vI++)
                {
                    oRow = oTable.AddRow();
                    oRow.AddCellText(StampStr(aRows[vI].CreatedAt));
                    oRow.AddCellText(aRows[vI].Title);
                    oRow.AddCellRaw(ChipHTML(aRows[vI].Kind,
                        TsgcHTMLBadgeStyle.bgSecondary));
                    if (aRows[vI].ReadAt == DateTime.MinValue)
                        oRow.AddCellRaw(PostButton("/app/notifications/read",
                            "Mark read", TsgcHTMLButtonStyle.bsOutlinePrimary,
                            new string[] { "id" },
                            new string[] { aRows[vI].Id.ToString(GFmt) }, ""),
                            "text-end");
                    else
                        oRow.AddCellRaw(BadgeHTML("read",
                            TsgcHTMLBadgeStyle.bgSecondary, true), "text-end");
                }
                oRoot.AddRaw(CardWrap("All notifications", oTable.HTML));
            }

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Notifications", vResult, aCtx);
            return vResult;
        }

        public string BuildAuditPage(TSaaSAuditRow[] aRows, int aPage, int aPageCount,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLAuditEntry oEntry;
            int vI;

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Workspace", "Audit" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Audit log", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "Only rows written under tenant id " + aCtx.TenantId.ToString(GFmt) +
                " are listed. The statement behind this table binds that id."));

            var oAudit = new TsgcHTMLComponent_AuditTrail();
            oAudit.Title = "Activity";
            oAudit.ShowFilter = true;
            oAudit.ShowExport = false;
            oAudit.PageSize = 25;
            oAudit.Striped = true;
            oAudit.EmptyText = "Nothing recorded yet.";
            for (vI = 0; vI <= Len(aRows) - 1; vI++)
            {
                oEntry = oAudit.AddEntry(aRows[vI].UserName, aRows[vI].Action,
                    aRows[vI].Entity + " #" + aRows[vI].EntityId.ToString(GFmt),
                    aRows[vI].IP, "ok", aRows[vI].Detail);
                oEntry.Timestamp = aRows[vI].CreatedAt;
            }
            oRoot.AddRaw(CardWrap("", oAudit.HTML));

            if (aPageCount > 1)
            {
                var oPager = new TsgcHTMLComponent_Pagination();
                oPager.CurrentPage = aPage;
                oPager.TotalPages = aPageCount;
                oPager.BaseURL = "/app/audit?page=";
                oPager.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                oRoot.AddRaw(oPager.HTML);
            }

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Audit", vResult, aCtx);
            return vResult;
        }

        public string BuildIsolationPage(TSaaSIsolationVM aVM, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oRow;
            TsgcHTMLContainer oCol;
            TsgcHTMLComponent_ListGroup oList;
            TsgcHTMLTableRow oTR;
            TsgcHTMLAlert oAlert;
            TsgcHTMLNodeList oBody;
            int vI;

            oRoot.AddRaw(BreadcrumbHTML(
                new string[] { "Workspace", "Isolation proof" },
                new string[] { "/app", "" }));
            oRoot.Add(new TsgcHTMLHeading("Tenant isolation, demonstrated", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "Everything on this page was produced by the same code the rest of " +
                "the workspace uses. Nothing here is a mock-up."));

            // 1. The session tenant id.
            oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("Tenant id on your session", "",
                aVM.SessionTenantId.ToString(GFmt), TsgcHTMLBadgeStyle.bgPrimary);
            oList.AddItem("Workspace", "", aVM.TenantName,
                TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Signed in as", "", aCtx.Username + " (" + aCtx.Role +
                ")", TsgcHTMLBadgeStyle.bgSecondary);
            oList.AddItem("Where the id comes from", "", "the session store only",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Can the request change it?", "", "no",
                TsgcHTMLBadgeStyle.bgSuccess);
            oRoot.AddRaw(CardWrap("1. The identity this page is scoped to",
                oList.HTML));

            // 2. The statement with the bind visible.
            oBody = new TsgcHTMLNodeList();
            oBody.AddElement("p",
                "The projects list on this workspace runs exactly this statement:",
                "text-muted small mb-2");
            oBody.AddRaw(CodeBlock(PrettySQL(aVM.SQLText)));
            oBody.AddElement("p", "with these bound values:",
                "text-muted small mt-3 mb-2");
            oBody.AddRaw(CodeBlock(":tenant_id = " +
                aVM.SessionTenantId.ToString(GFmt)));
            oBody.AddElement("p", "The count below the table uses the same bind:",
                "text-muted small mt-3 mb-2");
            oBody.AddRaw(CodeBlock(PrettySQL(aVM.CountSQLText)));
            oRoot.AddRaw(CardWrap("2. The parameterised SQL, with the bind visible",
                oBody.HTML));

            // 3. Visible rows versus table totals.
            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCol("Projects you can see", Num(aVM.VisibleProjects),
                CS_ICO_GRID, TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone,
                "", "col-6 col-xl-3",
                "WHERE tenant_id = " + aVM.SessionTenantId.ToString(GFmt)));
            oRow.AddRaw(StatCol("Projects in the table", Num(aVM.TotalProjects),
                CS_ICO_DB, TsgcHTMLStatColor.scSecondary, TsgcHTMLStatTrend.stNone,
                "", "col-6 col-xl-3", "every tenant, unscoped count"));
            oRow.AddRaw(StatCol("Tasks you can see", Num(aVM.VisibleTasks),
                CS_ICO_TASK, TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "scoped"));
            oRow.AddRaw(StatCol("Tasks in the table", Num(aVM.TotalTasks), CS_ICO_DB,
                TsgcHTMLStatColor.scSecondary, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "unscoped"));
            oRoot.Add(oRow);

            oBody = new TsgcHTMLNodeList();
            var oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.Title = "Share of the table you can reach";
            oGauge.Value = aVM.VisibleProjects;
            oGauge.MinValue = 0;
            oGauge.MaxValue = Math.Max(1, aVM.TotalProjects);
            oGauge.Unit_ = "rows";
            oGauge.Width = 210;
            oBody.AddRaw(oGauge.HTML);
            oBody.AddElement("p",
                "The two counts differ because the scoped count carries the bind " +
                "and the unscoped one does not. Sign in as a different tenant and " +
                "the left number changes while the right one does not.",
                "text-muted small mb-0");
            oRoot.AddRaw(CardWrap("3. Your slice against the whole table",
                oBody.HTML));

            // 4. A live cross-tenant fetch being refused.
            oBody = new TsgcHTMLNodeList();
            if (aVM.ForeignProjectId == 0)
                oBody.AddElement("p",
                    "There is no other tenant in this database to try against.",
                    "text-muted");
            else
            {
                oBody.AddElement("p", "Attempting, right now, to load project id " +
                    aVM.ForeignProjectId.ToString(GFmt) + " which belongs to \"" +
                    aVM.ForeignOwnerName + "\" (tenant id " +
                    aVM.ForeignOwnerId.ToString(GFmt) + "), through the same GetProject " +
                    "call the detail page uses:", "mb-2");
                oBody.AddRaw(CodeBlock("GetProject(session.TenantId = " +
                    aVM.SessionTenantId.ToString(GFmt) + ", id = " +
                    aVM.ForeignProjectId.ToString(GFmt) + ")"));

                if (aVM.ForeignFetchRefused)
                {
                    oAlert = new TsgcHTMLAlert("");
                    oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                    oAlert.AddText("Refused. " + aVM.RefusalReason);
                    oBody.Add(oAlert);
                }
                else
                {
                    oAlert = new TsgcHTMLAlert("");
                    oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                    oAlert.AddText("LEAK. The row came back. " + aVM.RefusalReason);
                    oBody.Add(oAlert);
                }

                oBody.AddElement("p",
                    "Requesting that id through the real route answers 403 with the " +
                    "same reason, not a redirect:", "text-muted small mt-3 mb-2");
                oBody.AddRaw(ButtonHTML("Try GET /app/projects/" +
                    aVM.ForeignProjectId.ToString(GFmt), "/app/projects/" +
                    aVM.ForeignProjectId.ToString(GFmt),
                    TsgcHTMLButtonStyle.bsOutlineDanger, ""));
            }

            oBody.AddElement("p",
                "And a workspace statement written without the bind never reaches " +
                "the database at all:", "text-muted small mt-4 mb-2");
            oBody.AddRaw(CodeBlock(aVM.GuardReason));
            oRoot.AddRaw(CardWrap("4. A real cross-tenant read, refused",
                oBody.HTML));

            // 5. The rows this tenant actually sees.
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.Responsive = true;
            oTable.AddColumn("Project id");
            oTable.AddColumn("Name");
            oTable.AddColumn("Tenant id on the row");
            if (Len(aVM.Rows) == 0)
                oTable.AddEmptyRow("No rows.", 3);
            else
                for (vI = 0; vI <= Len(aVM.Rows) - 1; vI++)
                {
                    oTR = oTable.AddRow();
                    oTR.AddCellText(aVM.Rows[vI].Id.ToString(GFmt));
                    oTR.AddCellText(aVM.Rows[vI].Name);
                    oTR.AddCellRaw(BadgeHTML(aVM.Rows[vI].TenantId.ToString(GFmt),
                        TsgcHTMLBadgeStyle.bgPrimary, true));
                }
            oRoot.AddRaw(CardWrap("5. Every row you can reach, with its tenant id",
                oTable.HTML));

            // 6. The path, drawn.
            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4";
            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-8";
            var oDiagram = new TsgcHTMLComponent_Diagram();
            oDiagram.DiagramWidth = 760;
            oDiagram.DiagramHeight = 340;
            oDiagram.AddNode("req", "HTTP request", 20, 30, TsgcHTMLColor.hcSecondary,
                TsgcHTMLDiagramNodeShape.nsRoundedRect);
            oDiagram.AddNode("cookie", "Session cookie", 220, 30,
                TsgcHTMLColor.hcSecondary, TsgcHTMLDiagramNodeShape.nsRoundedRect);
            oDiagram.AddNode("store", "Session store", 420, 30, TsgcHTMLColor.hcInfo,
                TsgcHTMLDiagramNodeShape.nsRoundedRect);
            oDiagram.AddNode("tenant", "tenant_id", 630, 30, TsgcHTMLColor.hcPrimary,
                TsgcHTMLDiagramNodeShape.nsCircle);
            oDiagram.AddNode("guard", "CreateScoped guard", 380, 140,
                TsgcHTMLColor.hcWarning, TsgcHTMLDiagramNodeShape.nsDiamond);
            oDiagram.AddNode("sql", "WHERE tenant_id = :tenant_id", 340, 250,
                TsgcHTMLColor.hcSuccess, TsgcHTMLDiagramNodeShape.nsRoundedRect);
            oDiagram.AddNode("db", "SQLite", 620, 250, TsgcHTMLColor.hcDark,
                TsgcHTMLDiagramNodeShape.nsRoundedRect);
            oDiagram.AddNode("drop", "Refused", 60, 250, TsgcHTMLColor.hcDanger,
                TsgcHTMLDiagramNodeShape.nsRoundedRect);

            oDiagram.Connect("req", "cookie", "carries");
            oDiagram.Connect("cookie", "store", "looks up");
            oDiagram.Connect("store", "tenant", "yields");
            oDiagram.Connect("tenant", "guard", "binds");
            oDiagram.Connect("guard", "sql", "bind present");
            oDiagram.Connect("guard", "drop", "bind missing");
            oDiagram.Connect("sql", "db", "executes");
            oCol.AddRaw(CardWrap("6. Request to row, end to end", oDiagram.HTML));
            oRow.Add(oCol);

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-4";
            oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("The request never supplies a tenant id", "", "rule 1",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Session store is the only source", "", "rule 2",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Workspace SQL must contain :tenant_id", "", "rule 3",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Counts, exports and PDFs use the same bind", "",
                "rule 4", TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Cross-axis routes answer 403 with a reason", "",
                "rule 5", TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Impersonation swaps the identity and audits both ends",
                "", "rule 6", TsgcHTMLBadgeStyle.bgSuccess);
            oCol.AddRaw(CardWrap("The six rules", oList.HTML));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            string vResult = oRoot.HTML;
            vResult = BuildAppShell("Isolation proof", vResult, aCtx);
            return vResult;
        }

        // -------------------------------------------------------------------
        // vendor console
        // -------------------------------------------------------------------

        public string BuildAdminDashboardPage(DataTable aTenantsDS, TSaaSAdminVM aVM,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oRow;
            TsgcHTMLContainer oCol;
            TsgcHTMLTableRow oTR;
            int vI;
            double[] vValues;

            oRoot.Add(new TsgcHTMLHeading("Platform", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCol("Tenants", Num(aVM.KPI.Tenants), CS_ICO_BUILDING,
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", Num(aVM.KPI.ActiveTenants) + " active"));
            oRow.AddRaw(StatCol("Trials", Num(aVM.KPI.TrialTenants), CS_ICO_ROCKET,
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "converting"));
            oRow.AddRaw(StatCol("Past due", Num(aVM.KPI.PastDueTenants), CS_ICO_CARD,
                TsgcHTMLStatColor.scWarning, TsgcHTMLStatTrend.stDown, "",
                "col-6 col-xl-3", Num(aVM.KPI.SuspendedTenants) + " suspended"));
            oRow.AddRaw(StatCol("MRR", Money(aVM.KPI.MRR), CS_ICO_CARD,
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stUp, "simulated",
                "col-6 col-xl-3", "no payment provider"));
            oRoot.Add(oRow);

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4 mb-4";

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-8";
            var oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartType = TsgcHTMLChartType.ctLine;
            oChart.Title = "Invoiced per month, signups and overdue invoices";
            oChart.CSSHeight = "280px";
            for (vI = 0; vI <= Len(aVM.MRR) - 1; vI++)
                oChart.AddLabel(aVM.MRR[vI].MonthLabel);

            vValues = new double[Len(aVM.MRR)];
            for (vI = 0; vI <= Len(aVM.MRR) - 1; vI++)
                vValues[vI] = aVM.MRR[vI].Value;
            oChart.AddDataset("Invoiced", vValues, CS_SAAS_ACCENT);

            vValues = new double[Len(aVM.Signups)];
            for (vI = 0; vI <= Len(aVM.Signups) - 1; vI++)
                vValues[vI] = aVM.Signups[vI].Value;
            oChart.AddDataset("Signups", vValues, "#7C3AED");

            vValues = new double[Len(aVM.Churn)];
            for (vI = 0; vI <= Len(aVM.Churn) - 1; vI++)
                vValues[vI] = aVM.Churn[vI].Value;
            oChart.AddDataset("Overdue", vValues, "#DC2626");
            oCol.AddRaw(CardWrap("Growth", oChart.HTML));
            oRow.Add(oCol);

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-4";
            var oFilter = new TsgcHTMLForm();
            oFilter.Action = "/admin";
            oFilter.Method = "GET";
            var oSearch = new TsgcHTMLField(TsgcHTMLInputType.itSearch, "q");
            oSearch.Label_ = "Search tenants";
            oSearch.Value = aVM.Search;
            oSearch.ColClass = "mb-3";
            oFilter.Add(oSearch);
            var oStatus = new TsgcHTMLSelect();
            oStatus.Name = "status";
            oStatus.Label_ = "Status";
            oStatus.ColClass = "mb-3";
            oStatus.AddOption("all", "All", SameText(aVM.StatusFilter, "all"));
            oStatus.AddOption(CS_TENANT_ACTIVE, "Active",
                SameText(aVM.StatusFilter, CS_TENANT_ACTIVE));
            oStatus.AddOption(CS_TENANT_TRIAL, "Trial",
                SameText(aVM.StatusFilter, CS_TENANT_TRIAL));
            oStatus.AddOption(CS_TENANT_PAST_DUE, "Past due",
                SameText(aVM.StatusFilter, CS_TENANT_PAST_DUE));
            oStatus.AddOption(CS_TENANT_SUSPENDED, "Suspended",
                SameText(aVM.StatusFilter, CS_TENANT_SUSPENDED));
            oFilter.Add(oStatus);
            var oBtn = new TsgcHTMLButton("Filter", TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oFilter.Add(oBtn);
            oCol.AddRaw(CardWrap("Find a tenant", oFilter.HTML));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            // Tenant list from the dataset.
            var oTable = new TsgcHTMLComponent_DataTable();
            oTable.Title = "Tenants";
            oTable.ShowSearch = true;
            oTable.ShowRowCount = true;
            oTable.Grid.Striped = true;
            oTable.Grid.Hover = true;
            oTable.Grid.Responsive = true;
            oTable.Grid.ShowSort = true;
            oTable.Grid.EmptyText = "No tenants match.";
            if (aTenantsDS != null)
                oTable.LoadFromDataSet(aTenantsDS, 15);
            oRoot.AddRaw(oTable.HTML);

            // Actions per tenant, each a POST form.
            var oGrid = new TsgcHTMLTable();
            oGrid.CSSClass = "table table-sm align-middle mb-0";
            oGrid.Responsive = true;
            oGrid.AddColumn("Tenant");
            oGrid.AddColumn("Plan");
            oGrid.AddColumn("Status");
            oGrid.AddColumn("Users", "text-end");
            oGrid.AddColumn("Activity");
            oGrid.AddColumn("", "text-end");
            if (Len(aVM.Tenants) == 0)
                oGrid.AddEmptyRow("No tenants.", 6);
            else
                for (vI = 0; vI <= Len(aVM.Tenants) - 1; vI++)
                {
                    oTR = oGrid.AddRow();
                    oTR.AddCellRaw(ButtonHTML(aVM.Tenants[vI].Name,
                        "/admin/tenants/" + aVM.Tenants[vI].Id.ToString(GFmt),
                        TsgcHTMLButtonStyle.bsLink, "p-0"));
                    oTR.AddCellRaw(ChipHTML(aVM.Tenants[vI].PlanName,
                        TsgcHTMLBadgeStyle.bgSecondary));
                    oTR.AddCellRaw(BadgeHTML(aVM.Tenants[vI].Status,
                        StatusBadge(aVM.Tenants[vI].Status), true));
                    oTR.AddCellText(aVM.Tenants[vI].UserCount.ToString(GFmt),
                        "text-end");
                    var oSpark = new TsgcHTMLComponent_Sparkline();
                    oSpark.ChartType = TsgcHTMLSparklineType.slBar;
                    oSpark.Width = 90;
                    oSpark.Height = 24;
                    oSpark.LineColor = CS_SAAS_ACCENT;
                    oSpark.AddValue(aVM.Tenants[vI].UserCount);
                    oSpark.AddValue(aVM.Tenants[vI].ProjectCount);
                    oSpark.AddValue(aVM.Tenants[vI].PlanPrice / 10);
                    oTR.AddCellRaw(oSpark.HTML);
                    if (SameText(aVM.Tenants[vI].Status, CS_TENANT_SUSPENDED))
                        oTR.AddCellRaw(PostButton("/admin/tenants/" +
                            aVM.Tenants[vI].Id.ToString(GFmt) + "/activate", "Activate",
                            TsgcHTMLButtonStyle.bsOutlineSuccess, new string[0],
                            new string[0], ""), "text-end");
                    else
                        oTR.AddCellRaw(PostButton("/admin/tenants/" +
                            aVM.Tenants[vI].Id.ToString(GFmt) + "/suspend", "Suspend",
                            TsgcHTMLButtonStyle.bsOutlineDanger, new string[0],
                            new string[0], "Suspend this tenant?"), "text-end");
                }
            oRoot.AddRaw(CardWrap("Tenant actions", oGrid.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildAdminShell("Platform", vResult, aCtx);
            return vResult;
        }

        public string BuildAdminTenantPage(DataTable aUsersDS, TSaaSTenantDetailVM aVM,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oRow;
            TsgcHTMLContainer oCol;
            TsgcHTMLTimelineItem oItem;
            TsgcHTMLTable oTable;
            TsgcHTMLTableRow oTR;
            TsgcHTMLAlert oAlert;
            int vI;
            double[] vValues;

            oRoot.AddRaw(BreadcrumbHTML(new string[] { "Platform", aVM.Tenant.Name },
                new string[] { "/admin", "" }));
            oRoot.Add(new TsgcHTMLHeading(aVM.Tenant.Name, 1));
            oRoot.AddRaw(BadgeHTML(aVM.Tenant.Status,
                StatusBadge(aVM.Tenant.Status), true));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCol("Plan", aVM.Plan.Name, CS_ICO_CARD,
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", Money(aVM.Plan.PriceMonthly) + " / month"));
            oRow.AddRaw(StatCol("Users", Num(aVM.Tenant.UserCount), CS_ICO_TEAM,
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "limit " + Num(aVM.Plan.MaxUsers)));
            oRow.AddRaw(StatCol("Projects", Num(aVM.Tenant.ProjectCount), CS_ICO_GRID,
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "limit " + Num(aVM.Plan.MaxProjects)));
            oRow.AddRaw(StatCol("Slug", aVM.Tenant.Slug, CS_ICO_BUILDING,
                TsgcHTMLStatColor.scSecondary, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-xl-3", "tenant id " + aVM.Tenant.Id.ToString(GFmt)));
            oRoot.Add(oRow);

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-4 mb-4";

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-7";
            var oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartType = TsgcHTMLChartType.ctBar;
            oChart.Title = "API calls per month";
            oChart.CSSHeight = "240px";
            oChart.ShowLegend = false;
            vValues = new double[Len(aVM.UsageMonths)];
            for (vI = 0; vI <= Len(aVM.UsageMonths) - 1; vI++)
            {
                oChart.AddLabel(aVM.UsageMonths[vI].MonthLabel);
                vValues[vI] = aVM.UsageMonths[vI].Value;
            }
            oChart.AddDataset("API calls", vValues, CS_SAAS_ACCENT);
            oCol.AddRaw(CardWrap("Usage", oChart.HTML));
            oRow.Add(oCol);

            oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-12 col-xl-5";
            var oTimeline = new TsgcHTMLComponent_Timeline();
            oItem = oTimeline.Items.Add();
            oItem.Title = "Signed up";
            oItem.Content = "Tenant row created.";
            oItem.Timestamp = DateStr(aVM.Tenant.CreatedAt);
            oItem.ColorStyle = TsgcHTMLColor.hcSuccess;

            if (aVM.Sub.StartedAt != DateTime.MinValue)
            {
                oItem = oTimeline.Items.Add();
                oItem.Title = "Subscription started";
                oItem.Content = aVM.Plan.Name + " plan.";
                oItem.Timestamp = DateStr(aVM.Sub.StartedAt);
                oItem.ColorStyle = TsgcHTMLColor.hcPrimary;
            }

            if (aVM.Tenant.TrialEndsAt != DateTime.MinValue)
            {
                oItem = oTimeline.Items.Add();
                oItem.Title = "Trial ends";
                oItem.Content = "Conversion decision point.";
                oItem.Timestamp = DateStr(aVM.Tenant.TrialEndsAt);
                oItem.ColorStyle = TsgcHTMLColor.hcWarning;
            }

            oItem = oTimeline.Items.Add();
            oItem.Title = "Renews";
            oItem.Content = "Next simulated invoice.";
            oItem.Timestamp = DateStr(aVM.Sub.RenewsAt);
            oItem.ColorStyle = TsgcHTMLColor.hcInfo;
            oCol.AddRaw(CardWrap("Lifecycle", oTimeline.HTML));

            var oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("Suspend or activate", "", "",
                TsgcHTMLBadgeStyle.bgSecondary);
            oCol.AddRaw(CardWrap("Actions", oList.HTML +
                PostButton("/admin/tenants/" + aVM.Tenant.Id.ToString(GFmt) + "/suspend",
                "Suspend", TsgcHTMLButtonStyle.bsOutlineDanger, new string[0],
                new string[0], "Suspend this tenant?", "btn-sm me-2") +
                PostButton("/admin/tenants/" + aVM.Tenant.Id.ToString(GFmt) + "/activate",
                "Activate", TsgcHTMLButtonStyle.bsOutlineSuccess, new string[0],
                new string[0], "", "btn-sm")));
            oRow.Add(oCol);
            oRoot.Add(oRow);

            // Users, with impersonation where allowed.
            var oUsers = new TsgcHTMLComponent_UserManagement();
            oUsers.ShowSearch = true;
            oUsers.ShowAddButton = false;
            oUsers.ShowRoles = true;
            oUsers.ShowStatus = true;
            oUsers.ShowActions = false;
            oUsers.EmptyText = "No users.";
            if (aUsersDS != null)
                oUsers.LoadFromDataSet(aUsersDS, "id", "display_name", "email",
                    "role", "status");
            oRoot.AddRaw(CardWrap("Users", oUsers.HTML));

            var oBody = new TsgcHTMLNodeList();
            if (!aVM.CanImpersonate)
            {
                oAlert = new TsgcHTMLAlert(
                    "Impersonation is restricted to superadmin accounts. Your role is " +
                    aCtx.Role + ".");
                oAlert.Style = TsgcHTMLAlertStyle.asSecondary;
                oBody.Add(oAlert);
            }
            else
            {
                oAlert = new TsgcHTMLAlert(
                    "Starting an impersonation writes an audit row now and another one " +
                    "when it stops, and a banner stays on screen throughout. A " +
                    "superadmin cannot be impersonated.");
                oAlert.Style = TsgcHTMLAlertStyle.asInfo;
                oBody.Add(oAlert);
            }

            oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.Responsive = true;
            oTable.AddColumn("User");
            oTable.AddColumn("Role");
            oTable.AddColumn("Last login");
            oTable.AddColumn("", "text-end");
            if (Len(aVM.Users) == 0)
                oTable.AddEmptyRow("No users.", 4);
            else
                for (vI = 0; vI <= Len(aVM.Users) - 1; vI++)
                {
                    oTR = oTable.AddRow();
                    oTR.AddCellRaw(AvatarFor(aVM.Users[vI].DisplayName,
                        TsgcHTMLAvatarSize.asSmall) + " " +
                        Esc(aVM.Users[vI].DisplayName));
                    oTR.AddCellRaw(ChipHTML(aVM.Users[vI].Role,
                        TsgcHTMLBadgeStyle.bgSecondary));
                    oTR.AddCellText(StampStr(aVM.Users[vI].LastLoginAt));
                    if (aVM.CanImpersonate)
                        oTR.AddCellRaw(PostButton("/admin/impersonate/" +
                            aVM.Users[vI].Id.ToString(GFmt), "Impersonate",
                            TsgcHTMLButtonStyle.bsOutlineWarning, new string[0],
                            new string[0], "Sign in as this user?"), "text-end");
                    else
                        oTR.AddCellText("superadmin only", "text-end text-muted small");
                }
            oBody.AddRaw(oTable.HTML);
            oRoot.AddRaw(CardWrap("Impersonation", oBody.HTML));

            // Invoices.
            oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.Responsive = true;
            oTable.AddColumn("Number");
            oTable.AddColumn("Issued");
            oTable.AddColumn("Total", "text-end");
            oTable.AddColumn("Status");
            if (Len(aVM.Invoices) == 0)
                oTable.AddEmptyRow("No invoices.", 4);
            else
                for (vI = 0; vI <= Len(aVM.Invoices) - 1; vI++)
                {
                    oTR = oTable.AddRow();
                    oTR.AddCellText(aVM.Invoices[vI].Number);
                    oTR.AddCellText(DateStr(aVM.Invoices[vI].IssuedAt));
                    oTR.AddCellText(Money(aVM.Invoices[vI].Total), "text-end");
                    oTR.AddCellRaw(BadgeHTML(aVM.Invoices[vI].Status,
                        StatusBadge(aVM.Invoices[vI].Status), true));
                }
            oRoot.AddRaw(CardWrap("Invoices", oTable.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildAdminShell(aVM.Tenant.Name, vResult, aCtx);
            return vResult;
        }

        public string BuildAdminPlansPage(DataTable aPlansDS, TSaaSPlan[] aPlans,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLFormField oField;
            int vI;

            oRoot.Add(new TsgcHTMLHeading("Plans", 1));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.Responsive = true;
            oGrid.ShowSort = true;
            oGrid.EmptyText = "No plans.";
            if (aPlansDS != null)
                oGrid.LoadFromDataSet(aPlansDS);
            oRoot.AddRaw(CardWrap("Current plans", oGrid.HTML));

            if (SameText(aCtx.Role, CS_ROLE_SUPERADMIN))
            {
                var oForm = new TsgcHTMLComponent_Form();
                oForm.Action = "/admin/plans/save";
                oForm.Method = TsgcHTMLFormMethod.fmPost;
                oForm.SubmitText = "Save plan";
                oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "id";
                oField.Label_ = "Plan";
                oField.ColSpan = 12;
                for (vI = 0; vI <= Len(aPlans) - 1; vI++)
                    oField.Options.Add(aPlans[vI].Id.ToString(GFmt) + "=" +
                        aPlans[vI].Name);

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftText;
                oField.Name = "name";
                oField.Label_ = "Display name";
                oField.ColSpan = 6;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftNumber;
                oField.Name = "price_monthly";
                oField.Label_ = "Price per month";
                oField.ColSpan = 6;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftNumber;
                oField.Name = "max_users";
                oField.Label_ = "Max users";
                oField.ColSpan = 4;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftNumber;
                oField.Name = "max_projects";
                oField.Label_ = "Max projects";
                oField.ColSpan = 4;

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftNumber;
                oField.Name = "max_storage_mb";
                oField.Label_ = "Max storage (MB)";
                oField.ColSpan = 4;
                oRoot.AddRaw(CardWrap("Edit a plan", oForm.HTML));
            }

            string vResult = oRoot.HTML;
            vResult = BuildAdminShell("Plans", vResult, aCtx);
            return vResult;
        }

        public string BuildAdminFlagsPage(TSaaSFeatureFlag[] aFlags,
            TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLCheckbox oCheck;
            int vI;

            oRoot.Add(new TsgcHTMLHeading("Feature flags", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "Global flags apply to every tenant unless a tenant-level row overrides " +
                "them."));
            oRoot.AddRaw(FlashHTML(aCtx.Flash, aCtx.ErrorMsg));

            var oForm = new TsgcHTMLForm();
            oForm.Action = "/admin/flags/save";
            oForm.Method = "POST";
            for (vI = 0; vI <= Len(aFlags) - 1; vI++)
            {
                oCheck = new TsgcHTMLCheckbox("flag_" + aFlags[vI].Flag);
                oCheck.FieldID = "flag_" + aFlags[vI].Flag;
                oCheck.Label_ = aFlags[vI].Flag + " (" + aFlags[vI].Scope + ")";
                oCheck.Switch = true;
                oCheck.Checked = aFlags[vI].Enabled;
                oCheck.Value = "1";
                oCheck.WrapperClass = "form-check form-switch mb-2";
                oForm.Add(oCheck);
            }
            if (Len(aFlags) == 0)
                oForm.AddRaw(EmptyStateHTML("No flags", "Nothing configured yet.",
                    CS_ICO_FLAG, "", ""));
            var oBtn = new TsgcHTMLButton("Save flags", TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "mt-3";
            oForm.Add(oBtn);
            oRoot.AddRaw(CardWrap("Flags", oForm.HTML));

            string vResult = oRoot.HTML;
            vResult = BuildAdminShell("Feature flags", vResult, aCtx);
            return vResult;
        }

        public string BuildAdminAuditPage(TSaaSAuditRow[] aRows, string aFilter,
            int aPage, int aPageCount, TSaaSPageContext aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLAuditEntry oEntry;
            int vI;

            oRoot.Add(new TsgcHTMLHeading("Platform audit", 1));
            oRoot.Add(new TsgcHTMLParagraph(
                "The vendor view is deliberately unscoped: it spans every tenant. " +
                "That is the one place in this demo where that is correct, and it is " +
                "reachable by vendor roles only."));

            var oForm = new TsgcHTMLForm();
            oForm.Action = "/admin/audit";
            oForm.Method = "GET";
            oForm.CSSClass = "row g-2 align-items-end mb-3";
            var oSelect = new TsgcHTMLSelect();
            oSelect.Name = "action";
            oSelect.Label_ = "Action";
            oSelect.ColClass = "col-auto";
            oSelect.AddOption("all", "All", SameText(aFilter, "all"));
            oSelect.AddOption("auth.login", "auth.login",
                SameText(aFilter, "auth.login"));
            oSelect.AddOption("impersonate.start", "impersonate.start",
                SameText(aFilter, "impersonate.start"));
            oSelect.AddOption("impersonate.stop", "impersonate.stop",
                SameText(aFilter, "impersonate.stop"));
            oSelect.AddOption("tenant.suspend", "tenant.suspend",
                SameText(aFilter, "tenant.suspend"));
            oSelect.AddOption("tenant.signup", "tenant.signup",
                SameText(aFilter, "tenant.signup"));
            oForm.Add(oSelect);
            var oBtn = new TsgcHTMLButton("Filter",
                TsgcHTMLButtonStyle.bsOutlinePrimary);
            oBtn.ButtonType = "submit";
            oForm.Add(oBtn);
            oRoot.AddRaw(oForm.HTML);

            var oAudit = new TsgcHTMLComponent_AuditTrail();
            oAudit.Title = "All tenants";
            oAudit.ShowFilter = true;
            oAudit.ShowExport = false;
            oAudit.PageSize = 25;
            oAudit.Striped = true;
            oAudit.EmptyText = "Nothing recorded.";
            for (vI = 0; vI <= Len(aRows) - 1; vI++)
            {
                oEntry = oAudit.AddEntry(aRows[vI].UserName, aRows[vI].Action,
                    aRows[vI].TenantName, aRows[vI].IP, "ok", aRows[vI].Detail);
                oEntry.Timestamp = aRows[vI].CreatedAt;
            }
            oRoot.AddRaw(CardWrap("", oAudit.HTML));

            if (aPageCount > 1)
            {
                var oPager = new TsgcHTMLComponent_Pagination();
                oPager.CurrentPage = aPage;
                oPager.TotalPages = aPageCount;
                oPager.BaseURL = "/admin/audit?action=" + aFilter + "&page=";
                oPager.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                oRoot.AddRaw(oPager.HTML);
            }

            string vResult = oRoot.HTML;
            vResult = BuildAdminShell("Platform audit", vResult, aCtx);
            return vResult;
        }
    }
}
