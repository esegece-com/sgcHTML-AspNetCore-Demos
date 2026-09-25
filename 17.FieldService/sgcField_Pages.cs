// ***************************************************************************
//  sgcField - field service management web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\17.FieldService\sgcField_Pages.pas
//
//  Zero hand-written HTML strings: every page is composed from
//  TsgcHTMLComponent_* instances and, where a component does not exist for the
//  job, from the sgcHTML node layer. The only raw markup spliced in via AddRaw
//  is (a) the rendered .HTML of an sgcHTML component or node, (b) a value that
//  already went through the local HtmlEsc, or (c) a trusted constant such as
//  the '&copy;' entity.
//
//  The Delphi TDataSet parameters map to System.Data.DataTable, which is the
//  established TDataSet mapping for this migration and what the managed
//  components' LoadFromDataSet takes. .NET is GC-managed, so the Delphi .Free
//  calls are dropped.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
using esegece.sgcWebSockets;

namespace FieldService
{
    public static class FieldPagesConst
    {
        // Brand accent for this demo.
        public const string CS_FIELD_ACCENT = "#EA580C";
        public const string CS_FIELD_ACCENT_DARK = "#C2410C";
        // Element ids the realtime push targets with hx-swap-oob. Kept in one place
        // because the page builders and the push thread both have to agree on them.
        public const string CS_ID_BOARD_LANES = "fs-board-lanes";
        public const string CS_ID_BOARD_STALE = "fs-board-stale";
        public const string CS_ID_MAP_DATA = "fs-map-data";
        public const string CS_ID_STAT_STRIP = "fs-stat-strip";
        public const string CS_ID_PRESENCE = "fsPresence";
        public const string CS_ID_FEED = "fsFeed";
        public const string CS_ID_LOG = "fsLog";
        public const string CS_ID_JOBS = "fsJobsLive";
        public const string CS_ID_CLOCK = "fs-clock";
    }

    // One row of the dispatch board: a technician and the jobs they hold in the
    // displayed window. TechnicianId = 0 is the unassigned queue, which is a
    // real drop target (POST /board/unassign).
    public class TFieldLane
    {
        public long TechnicianId;
        public string DisplayName = "";
        public string Initials = "";
        public string Presence = "";
        public string Skills = "";
        public TFieldJob[] Jobs = new TFieldJob[0];
    }

    // Who is looking at the page. Threaded through every builder instead of six
    // separate parameters.
    public class TFieldPageCtx
    {
        public long UserId;
        public string DisplayName = "";
        public string Initials = "";
        public string Role = "";
        public string Theme = "";
        public int Unread;
    }

    // One "here is the SQL, here is the component it fed" pair on /sql.
    public class TFieldSqlBlock
    {
        public string Caption = "";
        public string Note = "";
        public string SQL = "";
        public string Kind = ""; // grid | datatable | treegrid | chart | calendar | select
        public DataTable Data;
    }

    // Filters carried by the job list.
    public class TFieldJobListFilter
    {
        public string Status = "";
        public long TechnicianId;
        public string Priority = "";
        public string Search = "";
        public string Sort = "";
        public string Dir = "";
        public int Page;
        public int PageSize;
        public int Total;
    }

    // One row of the "work in progress" panel on the live rail: a job that is
    // being carried out right now, with the checklist as its percentage.
    public class TFieldLiveJob
    {
        public long Id;
        public string Reference = "";
        public string Title = "";
        public string Technician = "";
        public int Percent;
        public string Status = "";
    }

    // Everything the board page needs that is not a lane.
    public class TFieldBoardStats
    {
        public int Unassigned;
        public int Scheduled;
        public int EnRoute;
        public int OnSite;
        public int CompletedToday;
        public int SlaAtRisk;
    }

    // Collects the CSS of every component rendered on a page, once per class,
    // so the page emits it in <head> instead of hand-writing the rules. Local
    // to a request: no shared state, no locking needed.
    //
    // The Delphi original needed a cracker class to reach the protected GetCSS;
    // in the managed library TsgcHTMLComponent.GetCSS() is already public, and
    // the Delphi metaclass (TsgcHTMLComponentClass) becomes System.Type.
    public class TFieldCSSBag
    {
        private readonly List<string> FSeen = new List<string>();
        private string FCSS = "";

        public string CSS
        {
            get { return FCSS; }
        }

        public void Add(TsgcHTMLComponent aComponent)
        {
            if (aComponent == null)
                return;
            string vName = aComponent.GetType().Name;
            if (FSeen.Contains(vName))
                return;
            FSeen.Add(vName);
            string vCSS = "";
            try
            {
                vCSS = aComponent.GetCSS();
            }
            catch (Exception)
            {
                vCSS = "";
            }
            if (!string.IsNullOrEmpty(vCSS))
                FCSS = FCSS + vCSS;
        }

        // For a component created inside a helper the page never sees: makes one
        // throwaway instance purely to read its class CSS, once.
        public void AddClass(Type aClass)
        {
            if (aClass == null)
                return;
            if (FSeen.Contains(aClass.Name))
                return;
            TsgcHTMLComponent oTemp = Activator.CreateInstance(aClass) as TsgcHTMLComponent;
            Add(oTemp);
        }
    }

    public class TFieldPages
    {
        // Base CSS shared by every theme.
        private const string CS_BASE_CSS = "body{background:#f6f7f9;}" +
            ".navbar-brand{font-weight:700;}" +
            ".fs-accent{color:" + FieldPagesConst.CS_FIELD_ACCENT + ";}" +
            ".fs-bg-accent{background:" + FieldPagesConst.CS_FIELD_ACCENT + ";color:#fff;}" +
            ".btn-fs{background:" + FieldPagesConst.CS_FIELD_ACCENT + ";border-color:" +
            FieldPagesConst.CS_FIELD_ACCENT + ";color:#fff;}" +
            ".btn-fs:hover{background:" + FieldPagesConst.CS_FIELD_ACCENT_DARK +
            ";border-color:" + FieldPagesConst.CS_FIELD_ACCENT_DARK + ";color:#fff;}" +
            ".navbar-brand .fs-dot{display:inline-block;width:10px;height:10px;" +
            "border-radius:50%;background:" + FieldPagesConst.CS_FIELD_ACCENT +
            ";margin-right:6px;}" +
            // ---- dispatch board lanes ----
            ".fs-lane{display:flex;align-items:stretch;gap:.5rem;border-bottom:1px solid var(--bs-border-color,#dee2e6);padding:.35rem 0;}" +
            ".fs-lane:last-child{border-bottom:0;}" +
            ".fs-lane-head{flex:0 0 176px;display:flex;align-items:center;gap:.5rem;padding:.25rem .5rem;}" +
            ".fs-lane-grid{flex:1 1 auto;min-width:0;overflow-x:auto;}" +
            // neutralise the per-lane chrome the Scheduler renders for a whole page
            ".fs-lane .card{border:0;background:transparent;}" +
            ".fs-lane .sgc-sched-header{display:none;}" +
            ".fs-lane table th:first-child,.fs-lane table td:first-child{display:none;}" +
            ".fs-lane table{margin-bottom:0;}" +
            ".fs-lane .sgc-sched-cell{min-height:56px;}" +
            ".fs-lane .sgc-sched-event{cursor:grab;user-select:none;}" +
            ".fs-lane .sgc-sched-event:active{cursor:grabbing;}" +
            ".fs-drop-hot{outline:2px dashed " + FieldPagesConst.CS_FIELD_ACCENT +
            ";outline-offset:-2px;}" + ".fs-dragging{opacity:.45;}" +
            ".fs-lane-unassigned .fs-lane-head{color:" + FieldPagesConst.CS_FIELD_ACCENT +
            ";font-weight:600;}" +
            // ---- misc ----
            ".fs-mono{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:.82rem;}" +
            ".fs-sql{background:#0f172a;color:#e2e8f0;border-radius:8px;padding:14px;" +
            "overflow-x:auto;white-space:pre;font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;font-size:.8rem;}" +
            ".table tbody tr[onclick]{cursor:pointer;}" +
            ".table tbody tr[onclick]:hover{background-color:#fff4ed;}" +
            ".fs-photo{width:104px;height:104px;object-fit:cover;border-radius:8px;border:1px solid #dee2e6;}" +
            ".fs-sig{max-width:320px;border:1px solid #dee2e6;border-radius:8px;background:#fff;}" +
            // ---- technician, mobile first ----
            ".fs-my{padding-bottom:96px;}" +
            ".fs-my .card{border-radius:14px;box-shadow:0 1px 3px rgba(0,0,0,.08);}" +
            ".fs-tap{min-height:56px;font-size:1.05rem;display:flex;align-items:center;}" +
            ".fs-actionbar{position:fixed;left:0;right:0;bottom:0;z-index:1030;" +
            "background:var(--bs-body-bg,#fff);border-top:1px solid var(--bs-border-color,#dee2e6);" +
            "padding:.6rem .75rem;box-shadow:0 -2px 10px rgba(0,0,0,.08);}" +
            ".fs-actionbar .btn{min-height:54px;font-size:1.05rem;font-weight:600;}" +
            ".fs-check-item{min-height:54px;}" +
            ".fs-check-item .form-check-input{width:1.6rem;height:1.6rem;margin-top:.15rem;}" +
            ".fs-check-item .form-check-label{padding-left:.5rem;font-size:1.02rem;}" +
            // Below lg the board and the live rail stack: a Splitter is a desktop
            // idea, and on a phone the two panes have to be one column.
            "@media (max-width:991.98px){" +
            ".sgc-splitter{flex-direction:column !important;height:auto !important;" +
            "overflow:visible !important;}" +
            ".sgc-splitter-pane{width:auto !important;flex:0 0 auto !important;" +
            "height:auto !important;}" +
            ".sgc-splitter-gutter{display:none !important;}" + "}" +
            "@media (max-width:575.98px){" + ".fs-lane-head{flex-basis:120px;}" +
            ".fs-lane .sgc-sched-event{font-size:.7rem;}" +
            ".container,.container-fluid{padding-left:.6rem;padding-right:.6rem;}" +
            ".fs-hide-xs{display:none !important;}" + "}";

        // Dark-mode overrides.
        private const string CS_DARK_CSS = "body{background:#15181d;color:#e4e6eb;}" +
            ".card{background:#22262d;border:1px solid #333842;color:#e4e6eb;}" +
            ".card-header{background:#2a2f37;border-bottom:1px solid #333842;}" +
            ".table{color:#e4e6eb;}" + ".text-muted{color:#8b90a0 !important;}" +
            ".form-control,.form-select{background:#22262d;border-color:#333842;color:#e4e6eb;}" +
            ".form-control:focus,.form-select:focus{background:#2a2f37;color:#e4e6eb;border-color:" +
            FieldPagesConst.CS_FIELD_ACCENT + ";}" +
            ".list-group-item{background:#22262d;border-color:#333842;color:#e4e6eb;}" +
            ".page-link{background:#22262d;border-color:#333842;color:#e4e6eb;}" +
            ".page-item.active .page-link{background:" + FieldPagesConst.CS_FIELD_ACCENT +
            ";border-color:" + FieldPagesConst.CS_FIELD_ACCENT + ";}" + "a{color:#fdba74;}" +
            ".navbar.bg-white{background:#1c1f25 !important;border-color:#333842 !important;}" +
            ".navbar.bg-white .navbar-brand,.navbar.bg-white .nav-link,.navbar.bg-white .text-muted{color:#e4e6eb !important;}" +
            ".dropdown-menu{background:#22262d;border-color:#333842;}" +
            ".dropdown-item{color:#e4e6eb;}" +
            ".dropdown-item:hover,.dropdown-item:focus{background:#333842;color:#fff;}" +
            ".offcanvas{background:#22262d;color:#e4e6eb;}" +
            ".modal-content{background:#22262d;color:#e4e6eb;}" +
            ".fs-lane{border-bottom-color:#333842;}" +
            ".table tbody tr[onclick]:hover{background-color:#2a2f37;}" +
            ".fs-actionbar{background:#1c1f25;border-top-color:#333842;}";

        // Self-contained brand mark for the sign-in card (no external image).
        private const string CS_LOGO_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 240 64\" width=\"196\" height=\"52\" role=\"img\" " +
            "aria-label=\"sgcField Service\">" + "<rect x=\"0\" y=\"4\" width=\"56\" " +
            "height=\"56\" rx=\"13\" fill=\"" + FieldPagesConst.CS_FIELD_ACCENT + "\"/>" +
            "<path d=\"M17 40 L28 17 L39 40 Z\" fill=\"none\" stroke=\"#FFFFFF\" " +
            "stroke-width=\"4\" stroke-linejoin=\"round\"/>" +
            "<circle cx=\"28\" cy=\"34\" r=\"4\" fill=\"#FFFFFF\"/>" +
            "<text x=\"70\" y=\"42\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"700\" font-size=\"28\" fill=\"#212529\">Field Service</text></svg>";

        // ----- small shared helpers ----- //

        // Minimal HTML escape for values spliced into raw markup.
        public static string HtmlEsc(string aValue)
        {
            string vResult = (aValue ?? "").Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            vResult = vResult.Replace("'", "&#39;");
            return vResult;
        }

        // Escape + turn line breaks into <br>.
        public static string HtmlEscNl(string aValue)
        {
            string vResult = HtmlEsc(aValue);
            vResult = vResult.Replace("\r\n", "<br>");
            vResult = vResult.Replace("\n", "<br>");
            return vResult;
        }

        // Percent-encode one query-string value.
        public static string UrlEnc(string aValue)
        {
            return Uri.EscapeDataString(aValue ?? "");
        }

        private static string FmtDate(DateTime aValue)
        {
            if (TFieldDBPool.IsZeroDate(aValue))
                return "-";
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string FmtDateTime(DateTime aValue)
        {
            if (TFieldDBPool.IsZeroDate(aValue))
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static string FmtTime(DateTime aValue)
        {
            if (TFieldDBPool.IsZeroDate(aValue))
                return "--:--";
            return aValue.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string FmtISODate(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // Day and month NAMES through the invariant settings. A locale-bound
        // formatter renders "sabado, 22 agosto" inside an English page.
        private static string FmtDateEN(string aMask, DateTime aValue)
        {
            return aValue.ToString(aMask, CultureInfo.InvariantCulture);
        }

        private static string FmtMoney(double aValue)
        {
            return aValue.ToString("#,##0.00", CultureInfo.InvariantCulture);
        }

        private static string FmtNum(double aValue, int aDecimals)
        {
            string vMask;
            if (aDecimals <= 0)
                vMask = "0";
            else
                vMask = "0." + new string('0', aDecimals);
            return aValue.ToString(vMask, CultureInfo.InvariantCulture);
        }

        // A JS number literal always needs '.' whatever the machine locale is.
        private static string JSNum(double aValue, int aDecimals)
        {
            return FmtNum(aValue, aDecimals);
        }

        private static string JSStr(string aValue)
        {
            string vResult = (aValue ?? "").Replace("\\", "\\\\");
            vResult = vResult.Replace("'", "\\'");
            vResult = vResult.Replace("\"", "\\\"");
            vResult = vResult.Replace("\r", " ");
            vResult = vResult.Replace("\n", " ");
            return vResult;
        }

        // Delphi QuotedStr: single-quote the value, doubling embedded quotes.
        private static string QuotedStr(string aValue)
        {
            return "'" + (aValue ?? "").Replace("'", "''") + "'";
        }

        private static TsgcHTMLBadgeStyle StatusBadgeStyle(string aStatus)
        {
            string vS = (aStatus ?? "").Trim().ToLowerInvariant();
            if (vS == FieldConst.CS_JOB_SCHEDULED)
                return TsgcHTMLBadgeStyle.bgPrimary;
            if (vS == FieldConst.CS_JOB_ENROUTE)
                return TsgcHTMLBadgeStyle.bgInfo;
            if (vS == FieldConst.CS_JOB_ONSITE)
                return TsgcHTMLBadgeStyle.bgWarning;
            if (vS == FieldConst.CS_JOB_COMPLETE)
                return TsgcHTMLBadgeStyle.bgSuccess;
            if (vS == FieldConst.CS_JOB_CANCELLED)
                return TsgcHTMLBadgeStyle.bgSecondary;
            return TsgcHTMLBadgeStyle.bgDark;
        }

        private static TsgcHTMLColor StatusColor(string aStatus)
        {
            string vS = (aStatus ?? "").Trim().ToLowerInvariant();
            if (vS == FieldConst.CS_JOB_SCHEDULED)
                return TsgcHTMLColor.hcPrimary;
            if (vS == FieldConst.CS_JOB_ENROUTE)
                return TsgcHTMLColor.hcInfo;
            if (vS == FieldConst.CS_JOB_ONSITE)
                return TsgcHTMLColor.hcWarning;
            if (vS == FieldConst.CS_JOB_COMPLETE)
                return TsgcHTMLColor.hcSuccess;
            if (vS == FieldConst.CS_JOB_CANCELLED)
                return TsgcHTMLColor.hcSecondary;
            return TsgcHTMLColor.hcDark;
        }

        private static TsgcHTMLBadgeStyle PriorityBadgeStyle(string aValue)
        {
            string vS = (aValue ?? "").Trim().ToLowerInvariant();
            if (vS == FieldConst.CS_PRIORITY_LOW)
                return TsgcHTMLBadgeStyle.bgSecondary;
            if (vS == FieldConst.CS_PRIORITY_HIGH)
                return TsgcHTMLBadgeStyle.bgWarning;
            if (vS == FieldConst.CS_PRIORITY_URGENT)
                return TsgcHTMLBadgeStyle.bgDanger;
            return TsgcHTMLBadgeStyle.bgInfo;
        }

        private static string StatusBadge(string aStatus)
        {
            return TsgcHTMLComponent_Badge.Build(FieldConst.FieldStatusLabel(aStatus),
                StatusBadgeStyle(aStatus), true);
        }

        private static string PriorityBadge(string aValue)
        {
            return TsgcHTMLComponent_Badge.Build(FieldConst.FieldPriorityLabel(aValue),
                PriorityBadgeStyle(aValue), true);
        }

        private static string AvatarHTML(string aInitials, TsgcHTMLAvatarSize aSize,
            string aColor = FieldPagesConst.CS_FIELD_ACCENT)
        {
            return TsgcHTMLComponent_Avatar.Build(aInitials, aSize, aColor,
                TsgcHTMLAvatarStatus.atNone);
        }

        private static string PresenceLabel(string aPresence)
        {
            if (string.Equals(aPresence, "onsite", StringComparison.OrdinalIgnoreCase))
                return "On site";
            if (string.Equals(aPresence, "enroute", StringComparison.OrdinalIgnoreCase))
                return "En route";
            if (string.Equals(aPresence, "off", StringComparison.OrdinalIgnoreCase))
                return "Off duty";
            return "Available";
        }

        private static TsgcHTMLPresenceStatus PresenceStatus(string aPresence)
        {
            if (string.Equals(aPresence, "onsite", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLPresenceStatus.psBusy;
            if (string.Equals(aPresence, "enroute", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLPresenceStatus.psAway;
            if (string.Equals(aPresence, "off", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLPresenceStatus.psOffline;
            return TsgcHTMLPresenceStatus.psOnline;
        }

        private static string PresenceBadge(string aPresence)
        {
            TsgcHTMLBadgeStyle vStyle;
            if (string.Equals(aPresence, "onsite", StringComparison.OrdinalIgnoreCase))
                vStyle = TsgcHTMLBadgeStyle.bgWarning;
            else if (string.Equals(aPresence, "enroute", StringComparison.OrdinalIgnoreCase))
                vStyle = TsgcHTMLBadgeStyle.bgInfo;
            else if (string.Equals(aPresence, "off", StringComparison.OrdinalIgnoreCase))
                vStyle = TsgcHTMLBadgeStyle.bgSecondary;
            else
                vStyle = TsgcHTMLBadgeStyle.bgSuccess;
            return TsgcHTMLComponent_Badge.Build(PresenceLabel(aPresence), vStyle, true);
        }

        // The flash codes carried on a redirect as ?flash=..
        public static string FlashMessage(string aCode)
        {
            string vS = aCode ?? "";
            if (string.Equals(vS, "assigned", StringComparison.OrdinalIgnoreCase))
                return "Job assigned.";
            if (string.Equals(vS, "unassigned", StringComparison.OrdinalIgnoreCase))
                return "Job moved back to the unassigned queue.";
            if (string.Equals(vS, "rescheduled", StringComparison.OrdinalIgnoreCase))
                return "Job rescheduled.";
            if (string.Equals(vS, "created", StringComparison.OrdinalIgnoreCase))
                return "Job created.";
            if (string.Equals(vS, "saved", StringComparison.OrdinalIgnoreCase))
                return "Saved.";
            if (string.Equals(vS, "cancelled", StringComparison.OrdinalIgnoreCase))
                return "Job cancelled.";
            if (string.Equals(vS, "status", StringComparison.OrdinalIgnoreCase))
                return "Job status updated.";
            if (string.Equals(vS, "checked", StringComparison.OrdinalIgnoreCase))
                return "Checklist updated.";
            if (string.Equals(vS, "part", StringComparison.OrdinalIgnoreCase))
                return "Part added to the job.";
            if (string.Equals(vS, "photo", StringComparison.OrdinalIgnoreCase))
                return "Photo uploaded.";
            if (string.Equals(vS, "signed", StringComparison.OrdinalIgnoreCase))
                return "Signature captured.";
            if (string.Equals(vS, "approved", StringComparison.OrdinalIgnoreCase))
                return "Thank you, the quote is approved.";
            if (string.Equals(vS, "rated", StringComparison.OrdinalIgnoreCase))
                return "Thank you for rating this visit.";
            return "";
        }

        // A grid column carrying EXACTLY the Bootstrap classes given.
        // TsgcHTMLRow.Col prepends its own col-md-N, and a page that then adds
        // col-md-4 ends up with two rules of equal specificity inside the same media
        // query, where the one Bootstrap declares LAST wins: col-md-12 beats col-md-4
        // and every column goes full width. A plain container has no such argument.
        private static TsgcHTMLContainer AddCol(TsgcHTMLContainer aRow, string aClasses)
        {
            TsgcHTMLContainer oResult = new TsgcHTMLContainer("div");
            oResult.CSSClass = aClasses;
            aRow.Add(oResult);
            return oResult;
        }

        // A dismissible alert node, used for every flash / error banner.
        private static string AlertHTML(string aText, TsgcHTMLAlertStyle aStyle)
        {
            if (string.IsNullOrEmpty(aText))
                return "";
            TsgcHTMLAlert oAlert = new TsgcHTMLAlert();
            oAlert.Style = aStyle;
            oAlert.Dismissible = true;
            oAlert.CSSClass = "mb-3";
            oAlert.AddText(aText);
            return oAlert.HTML;
        }

        // ----- shell ----- //

        private string SystemThemeAutodetectScript()
        {
            return "<script>(function(){if(window.matchMedia&&window.matchMedia(" +
                "'(prefers-color-scheme: dark)').matches){document.documentElement." +
                "setAttribute('data-bs-theme','dark');}})();</script>";
        }

        // Wrap an already-built body in the Bootstrap template. aRealtime adds the
        // htmx + sgcWebSockets + sgcHTMX bridge trio (all served by this server out of
        // the linked resource, never a CDN) and opens the socket on /ws, which is the
        // channel every hx-swap-oob fragment arrives on.
        private string WrapTemplate(string aTitle, string aBody, string aTheme,
            string aExtraCSS, bool aRealtime)
        {
            string vBody = aBody;
            if (aRealtime)
            {
                TsgcHTMLScript oScript = new TsgcHTMLScript("/htmx.min.js", true);
                vBody = vBody + oScript.HTML;
                oScript = new TsgcHTMLScript("/sgcWebSockets.js", true);
                vBody = vBody + oScript.HTML;
                oScript = new TsgcHTMLScript("/sgcHTMX.min.js", true);
                vBody = vBody + oScript.HTML;
                oScript = new TsgcHTMLScript("", false);
                oScript.Code = "document.addEventListener(\"DOMContentLoaded\",function(){" +
                    "if(window.sgcHTMX&&sgcHTMX.init){sgcHTMX.init({host:" +
                    "(location.protocol==='https:'?'wss:':'ws:')+'//'+location.host+'/ws'});}" +
                    "var c=document.getElementById(\"" + FieldPagesConst.CS_ID_CLOCK + "\");" +
                    "if(c){setInterval(function(){c.textContent=new Date()" +
                    ".toLocaleTimeString();},1000);}" + "});";
                vBody = vBody + oScript.HTML;
            }

            TsgcHTMLTemplate_Bootstrap oTpl = new TsgcHTMLTemplate_Bootstrap();
            oTpl.Title = aTitle;
            oTpl.HtmlLang = "en";
            oTpl.Viewport = "width=device-width, initial-scale=1";
            oTpl.HeadNodes.AddRaw("<link rel=\"icon\" type=\"image/svg+xml\" " +
                "href=\"/favicon.svg\">");
            if (string.Equals(aTheme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "dark";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_BASE_CSS + aExtraCSS + CS_DARK_CSS;
            }
            else if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "light";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_BASE_CSS + aExtraCSS;
            }
            else
            {
                oTpl.HtmlTheme = "";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_BASE_CSS + aExtraCSS +
                    "@media (prefers-color-scheme: dark){" + CS_DARK_CSS + "}";
                oTpl.HeadNodes.AddRaw(SystemThemeAutodetectScript());
            }
            oTpl.BodyContent = vBody;
            return oTpl.GetHTML();
        }

        private void AddThemeItem(TsgcHTMLContainer aUl, string aTheme, string aValue,
            string aLabel)
        {
            TsgcHTMLContainer oLi = new TsgcHTMLContainer("li");
            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/theme";
            oForm.CSSClass = "m-0";
            oForm.AddHidden("theme", aValue);
            TsgcHTMLContainer oBtn = new TsgcHTMLContainer("button");
            oBtn.Attributes = "type=\"submit\" class=\"dropdown-item\"";
            TsgcHTMLContainer oSpan = new TsgcHTMLContainer("span");
            oSpan.AddText(aLabel);
            oBtn.Add(oSpan);
            if (string.Equals(aTheme, aValue, StringComparison.OrdinalIgnoreCase))
                oBtn.AddRaw(" <span class=\"ms-2\">&#10004;</span>");
            oForm.Add(oBtn);
            oLi.Add(oForm);
            aUl.Add(oLi);
        }

        private string BuildThemeDropdown(string aTheme)
        {
            string vCurLabel;
            if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
                vCurLabel = "Light";
            else if (string.Equals(aTheme, "dark", StringComparison.OrdinalIgnoreCase))
                vCurLabel = "Dark";
            else
                vCurLabel = "System";

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oLi = new TsgcHTMLContainer("li");
            oLi.CSSClass = "nav-item dropdown ms-2";
            TsgcHTMLContainer oToggle = new TsgcHTMLContainer("a");
            oToggle.Attributes = "class=\"nav-link dropdown-toggle\" href=\"#\" " +
                "id=\"fsThemeDropdown\" role=\"button\" data-bs-toggle=\"dropdown\" " +
                "aria-expanded=\"false\" aria-label=\"Theme: " + HtmlEsc(vCurLabel) + "\"";
            oToggle.AddText(vCurLabel);
            oLi.Add(oToggle);
            TsgcHTMLContainer oUl = new TsgcHTMLContainer("ul");
            oUl.Attributes = "class=\"dropdown-menu dropdown-menu-end\" " +
                "aria-labelledby=\"fsThemeDropdown\"";
            AddThemeItem(oUl, aTheme, "light", "Light");
            AddThemeItem(oUl, aTheme, "dark", "Dark");
            AddThemeItem(oUl, aTheme, "system", "System");
            oLi.Add(oUl);
            oRoot.Add(oLi);
            return oRoot.HTML;
        }

        // Notification bell in the navbar: unread dispatcher/technician messages.
        private string BuildNotificationBell(TFieldPageCtx aCtx)
        {
            TsgcHTMLComponent_Notification oNotify = new TsgcHTMLComponent_Notification();
            oNotify.NotificationID = "fsBell";
            oNotify.Title = "Notifications";
            oNotify.EmptyText = "Nothing new";
            oNotify.MaxVisible = 5;
            oNotify.ShowBadge = true;
            if (aCtx.Unread > 0)
                oNotify.AddNotification("unread", "Unread messages",
                    aCtx.Unread.ToString(CultureInfo.InvariantCulture) +
                    " message(s) waiting on your jobs", TsgcHTMLColor.hcWarning,
                    DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
            oNotify.AddNotification("welcome", "Signed in as " + aCtx.Role,
                "Realtime board updates are pushed over the WebSocket.",
                TsgcHTMLColor.hcPrimary,
                DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
            TsgcHTMLContainer oLi = new TsgcHTMLContainer("li");
            oLi.CSSClass = "nav-item d-flex align-items-center ms-2";
            oLi.AddRaw(oNotify.HTML);
            return oLi.HTML;
        }

        // Ctrl+K launcher. Every entry is a plain link, so it works with the keyboard
        // and with a click, and it needs no server round trip.
        private string BuildCommandPalette(TFieldPageCtx aCtx)
        {
            TsgcHTMLComponent_CommandPalette oPal = new TsgcHTMLComponent_CommandPalette();
            oPal.PaletteID = "fsPalette";
            oPal.Placeholder = "Jump to a board, a report or a job list...";
            oPal.EmptyText = "Nothing matches that.";
            oPal.HotKey = "k";
            oPal.MaxResults = 12;
            oPal.ShowCategories = true;
            oPal.ShowShortcuts = true;
            if (string.Equals(aCtx.Role, FieldConst.CS_ROLE_TECHNICIAN,
                StringComparison.OrdinalIgnoreCase))
            {
                oPal.AddItem("Today's jobs", "/my", "", "Technician", "G T");
                oPal.AddItem("Switch theme", "#", "theme", "Preferences", "");
            }
            else
            {
                oPal.AddItem("Dispatch board", "/", "", "Dispatch", "G B");
                oPal.AddItem("Gantt plan", "/gantt", "", "Dispatch", "G G");
                oPal.AddItem("Live map", "/map", "", "Dispatch", "G M");
                oPal.AddItem("Calendar", "/calendar", "", "Dispatch", "G C");
                oPal.AddItem("All jobs", "/jobs", "", "Work", "G J");
                oPal.AddItem("New job", "/jobs/new", "", "Work", "N J");
                oPal.AddItem("Unassigned queue", "/jobs?status=new&technician=0", "",
                    "Work", "");
                oPal.AddItem("Urgent jobs", "/jobs?priority=urgent", "", "Work", "");
                oPal.AddItem("Customers", "/customers", "", "Data", "G U");
                oPal.AddItem("Assets", "/assets", "", "Data", "G A");
                oPal.AddItem("Parts", "/parts", "", "Data", "G P");
                oPal.AddItem("Reports", "/reports", "", "Manager", "G R");
                oPal.AddItem("The SQL behind the pages", "/sql", "", "Manager", "");
                oPal.AddItem("Users", "/users", "", "Manager", "");
                oPal.AddItem("Audit log", "/audit", "", "Manager", "");
            }
            return oPal.HTML;
        }

        // Off-canvas navigation for phones. Same links as the navbar, big tap targets.
        private string BuildMobileMenu(TFieldPageCtx aCtx)
        {
            TsgcHTMLListGroupNode oList = new TsgcHTMLListGroupNode();
            oList.CSSClass = "list-group-flush";
            if (string.Equals(aCtx.Role, FieldConst.CS_ROLE_TECHNICIAN,
                StringComparison.OrdinalIgnoreCase))
            {
                oList.AddItem("Today's jobs").Href = "/my";
            }
            else if (string.Equals(aCtx.Role, FieldConst.CS_ROLE_CUSTOMER,
                StringComparison.OrdinalIgnoreCase))
            {
                oList.AddItem("Track a job").Href = "/track";
            }
            else
            {
                oList.AddItem("Dispatch board").Href = "/";
                oList.AddItem("Gantt plan").Href = "/gantt";
                oList.AddItem("Live map").Href = "/map";
                oList.AddItem("Calendar").Href = "/calendar";
                oList.AddItem("Jobs").Href = "/jobs";
                oList.AddItem("Customers").Href = "/customers";
                oList.AddItem("Assets").Href = "/assets";
                oList.AddItem("Parts").Href = "/parts";
                oList.AddItem("Reports").Href = "/reports";
                oList.AddItem("The SQL behind it").Href = "/sql";
                oList.AddItem("Users").Href = "/users";
                oList.AddItem("Audit log").Href = "/audit";
            }
            string vBody = oList.HTML;

            TsgcHTMLOffcanvas oCanvas = new TsgcHTMLOffcanvas("fsMobileMenu", "Field Service");
            oCanvas.Placement = "start";
            oCanvas.Dismissible = true;
            oCanvas.Body.AddRaw(vBody);
            oCanvas.Body.AddRaw("<div class=\"p-3 text-muted small\">Signed in as " +
                HtmlEsc(aCtx.DisplayName) + " (" + HtmlEsc(aCtx.Role) + ")</div>");
            return oCanvas.HTML;
        }

        private void AddNavLink(TsgcHTMLContainer aList, string aActiveMenu,
            string aHref, string aText, string aMenu)
        {
            TsgcHTMLContainer oLi = new TsgcHTMLContainer("li");
            oLi.CSSClass = "nav-item";
            string vClass = "nav-link";
            if (string.Equals(aActiveMenu, aMenu, StringComparison.OrdinalIgnoreCase))
                vClass = vClass + " active fw-semibold";
            TsgcHTMLLink oLink = new TsgcHTMLLink(aHref, aText);
            oLink.CSSClass = vClass;
            oLi.Add(oLink);
            aList.Add(oLi);
        }

        private string BuildNavbar(TFieldPageCtx aCtx, string aActiveMenu)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oNav = new TsgcHTMLContainer("nav");
            oNav.CSSClass = "navbar navbar-expand-lg navbar-light bg-white shadow-sm mb-3";

            TsgcHTMLContainer oInner = new TsgcHTMLContainer("div");
            oInner.CSSClass = "container-fluid px-3";

            TsgcHTMLContainer oBurger = new TsgcHTMLContainer("button");
            oBurger.Attributes = "class=\"btn btn-sm btn-outline-secondary me-2 " +
                "d-lg-none\" type=\"button\" data-bs-toggle=\"offcanvas\" " +
                "data-bs-target=\"#fsMobileMenu\" aria-controls=\"fsMobileMenu\" " +
                "aria-label=\"Menu\"";
            oBurger.AddRaw("&#9776;");
            oInner.Add(oBurger);

            // Brand: the accent dot is a styled <span>, so the anchor is built from a
            // container (a TsgcHTMLLink only carries escaped text).
            TsgcHTMLContainer oBrand = new TsgcHTMLContainer("a");
            oBrand.CSSClass = "navbar-brand d-flex align-items-center";
            oBrand.Attributes = "href=\"/\" aria-label=\"Field Service\"";
            TsgcHTMLContainer oDot = new TsgcHTMLContainer("span");
            oDot.CSSClass = "fs-dot";
            oBrand.Add(oDot);
            oBrand.AddText("sgcField");
            oInner.Add(oBrand);

            TsgcHTMLContainer oList = new TsgcHTMLContainer("ul");
            oList.CSSClass = "navbar-nav me-auto d-none d-lg-flex";
            if (string.Equals(aCtx.Role, FieldConst.CS_ROLE_TECHNICIAN,
                StringComparison.OrdinalIgnoreCase))
                AddNavLink(oList, aActiveMenu, "/my", "My jobs", "my");
            else if (string.Equals(aCtx.Role, FieldConst.CS_ROLE_CUSTOMER,
                StringComparison.OrdinalIgnoreCase))
                AddNavLink(oList, aActiveMenu, "/track", "Track a job", "track");
            else
            {
                AddNavLink(oList, aActiveMenu, "/", "Board", "board");
                AddNavLink(oList, aActiveMenu, "/gantt", "Gantt", "gantt");
                AddNavLink(oList, aActiveMenu, "/map", "Map", "map");
                AddNavLink(oList, aActiveMenu, "/calendar", "Calendar", "calendar");
                AddNavLink(oList, aActiveMenu, "/jobs", "Jobs", "jobs");
                AddNavLink(oList, aActiveMenu, "/customers", "Customers", "customers");
                AddNavLink(oList, aActiveMenu, "/assets", "Assets", "assets");
                AddNavLink(oList, aActiveMenu, "/parts", "Parts", "parts");
                AddNavLink(oList, aActiveMenu, "/reports", "Reports", "reports");
                AddNavLink(oList, aActiveMenu, "/sql", "SQL", "sql");
            }
            oInner.Add(oList);

            TsgcHTMLContainer oRight = new TsgcHTMLContainer("ul");
            // flex-row on purpose: below the lg breakpoint Bootstrap stacks a
            // navbar-nav vertically, which turns the bell / theme / sign-out group
            // into three rows on a phone.
            oRight.CSSClass = "navbar-nav ms-auto align-items-center flex-row gap-1";
            oRight.AddRaw(BuildNotificationBell(aCtx));
            oRight.AddRaw(BuildThemeDropdown(aCtx.Theme));

            TsgcHTMLContainer oUserLi = new TsgcHTMLContainer("li");
            oUserLi.CSSClass = "nav-item d-flex align-items-center ms-2 fs-hide-xs";
            oUserLi.AddRaw(AvatarHTML(aCtx.Initials, TsgcHTMLAvatarSize.asSmall));
            TsgcHTMLContainer oUserSpan = new TsgcHTMLContainer("span");
            oUserSpan.CSSClass = "text-muted small ms-2";
            oUserSpan.AddText(aCtx.DisplayName);
            oUserLi.Add(oUserSpan);
            oRight.Add(oUserLi);

            TsgcHTMLContainer oLogoutLi = new TsgcHTMLContainer("li");
            oLogoutLi.CSSClass = "nav-item ms-2";
            TsgcHTMLForm oLogoutForm = new TsgcHTMLForm();
            oLogoutForm.Method = "POST";
            oLogoutForm.Action = "/logout";
            oLogoutForm.CSSClass = "m-0";
            TsgcHTMLButton oLogoutBtn = new TsgcHTMLButton("Sign out",
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oLogoutBtn.ButtonType = "submit";
            oLogoutBtn.CSSClass = "btn-sm";
            oLogoutForm.Add(oLogoutBtn);
            oLogoutLi.Add(oLogoutForm);
            oRight.Add(oLogoutLi);

            oInner.Add(oRight);
            oNav.Add(oInner);
            oRoot.Add(oNav);
            return oRoot.HTML;
        }

        private string BuildFooter()
        {
            TsgcHTMLContainer oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "py-4 mt-4 border-top";
            TsgcHTMLContainer oInner = new TsgcHTMLContainer("div");
            oInner.CSSClass = "container-fluid px-3 text-center text-muted small";
            oInner.AddText("Built with sgcHTML - no REST tier, the components read the " +
                "database directly. ");
            oInner.AddRaw("&copy; 2026 eSeGeCe");
            oFooter.Add(oInner);
            return oFooter.HTML;
        }

        private string BuildBreadcrumb(string[] aItems, string[] aHrefs)
        {
            TsgcHTMLComponent_Breadcrumb oBc = new TsgcHTMLComponent_Breadcrumb();
            oBc.BreadcrumbID = "fsCrumb";
            for (int vI = 0; vI < aItems.Length; vI++)
            {
                TsgcHTMLBreadcrumbItem oItem = oBc.Items.Add();
                oItem.Text = aItems[vI];
                if ((vI < aHrefs.Length) && (aHrefs[vI] != ""))
                    oItem.Href = aHrefs[vI];
                else
                    oItem.Active = true;
            }
            return oBc.HTML;
        }

        public string BuildPageShell(TFieldPageCtx aCtx, string aTitle, string aBodyHTML,
            string aActiveMenu, string aExtraCSS, bool aRealtime = false)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildNavbar(aCtx, aActiveMenu));
            oRoot.AddRaw(BuildMobileMenu(aCtx));
            TsgcHTMLContainer oMain = new TsgcHTMLContainer("main");
            oMain.CSSClass = "container-fluid px-3";
            oMain.AddRaw(aBodyHTML);
            oRoot.Add(oMain);
            oRoot.AddRaw(BuildFooter());
            oRoot.AddRaw(BuildCommandPalette(aCtx));
            string vBody = oRoot.HTML;
            return WrapTemplate(aTitle + " - sgcField", vBody, aCtx.Theme, aExtraCSS,
                aRealtime);
        }

        // ----- auth / error pages ----- //

        public string BuildLoginPage(string aTheme, string aError = "",
            string aDefaultUser = "", string aDefaultPassword = "")
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container d-flex align-items-center justify-content-center";
            oWrap.Style = "min-height:100vh;";

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "p-4";
            oCard.Body.AddRaw("<div class=\"text-center mb-3\">" + CS_LOGO_SVG + "</div>");

            TsgcHTMLComponent_Login oLogin = new TsgcHTMLComponent_Login();
            oLogin.LoginStyle = TsgcHTMLLoginStyle.lsCard;
            oLogin.FormAction = "/login";
            oLogin.FormMethod = "POST";
            oLogin.FormID = "fsLoginForm";
            oLogin.Title = "Sign in";
            oLogin.Subtitle = "Dispatch board, technician app and manager reports";
            oLogin.UserLabel = "Username";
            oLogin.UserPlaceholder = "dispatch";
            oLogin.PasswordLabel = "Password";
            oLogin.PasswordPlaceholder = "Your password";
            oLogin.ButtonText = "Sign in";
            oLogin.ButtonStyleEnum = TsgcHTMLButtonStyle.bsPrimary;
            oLogin.ButtonClass = "btn btn-fs w-100";
            oLogin.MaxWidth = "100%";
            oLogin.CSSClass = "border-0 shadow-none p-0";
            oLogin.UserValue = aDefaultUser;
            oLogin.PasswordValue = aDefaultPassword;
            oLogin.UserAutocomplete = "username";
            oLogin.PasswordAutocomplete = "current-password";
            oLogin.ErrorMessage = aError;
            oBag.Add(oLogin);
            oCard.Body.AddRaw(oLogin.HTML);

            oCard.Body.AddRaw("<hr class=\"my-4\">");

            TsgcHTMLComponent_WebAuthnLogin oWA = new TsgcHTMLComponent_WebAuthnLogin();
            oWA.WebAuthnID = "fsWebAuthn";
            oWA.Mode = TsgcHTMLWebAuthnMode.wamAuthenticate;
            oWA.Title = "Or use a passkey";
            oWA.Description = "Face ID, Touch ID, Windows Hello or a security key.";
            oWA.AuthenticateButtonText = "Sign in with a passkey";
            oWA.AuthenticateButtonStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oWA.ShowPasskeyIcon = false;
            oWA.RegisterURL = "/passkey/register";
            oWA.AuthenticateURL = "/passkey/login";
            oWA.CallbackURL = "/";
            // Self-contained WebAuthn: the component's stock script expects the
            // SimpleWebAuthnBrowser bundle from a CDN, and these demos must run
            // offline, so the two functions it calls are supplied here on top of
            // the platform navigator.credentials API.
            oWA.CustomScript = "function fsB64uToBuf(s){" +
                "if(!s)return new ArrayBuffer(0);" +
                "s=s.replace(/-/g,\"+\").replace(/_/g,\"/\");" +
                "while(s.length%4)s+=\"=\";var bin=atob(s);" +
                "var arr=new Uint8Array(bin.length);" +
                "for(var i=0;i<bin.length;i++)arr[i]=bin.charCodeAt(i);" +
                "return arr.buffer;}" + "function fsBufToB64u(b){" +
                "var by=new Uint8Array(b);var s=\"\";" +
                "for(var i=0;i<by.byteLength;i++)s+=String.fromCharCode(by[i]);" +
                "return btoa(s).replace(/\\+/g,\"-\").replace(/\\//g,\"_\")" +
                ".replace(/=+$/,\"\");}" + "async function sgcWebAuthnAuthenticate(){" +
                "var s=document.getElementById(\"fsWebAuthn_status\");" + "try{" +
                "s.innerHTML='<div class=\"text-muted small\">Requesting passkey...</div>';" +
                "var r=await fetch(\"/passkey/login/options\",{method:\"POST\"," +
                "headers:{\"Content-Type\":\"application/json\"},body:\"{}\"," +
                "credentials:\"same-origin\"});" + "var o=await r.json();" +
                "if(!r.ok||o.error)throw new Error(o.error||(\"options \"+r.status));" +
                "o.challenge=fsB64uToBuf(o.challenge);" +
                "if(o.allowCredentials){o.allowCredentials.forEach(function(c){" +
                "c.id=fsB64uToBuf(c.id);});}" +
                "var a=await navigator.credentials.get({publicKey:o,mediation:\"optional\"});" +
                "var p={id:a.id,rawId:fsBufToB64u(a.rawId),type:a.type,response:{" +
                "clientDataJSON:fsBufToB64u(a.response.clientDataJSON)," +
                "authenticatorData:fsBufToB64u(a.response.authenticatorData)," +
                "signature:fsBufToB64u(a.response.signature)," +
                "userHandle:a.response.userHandle?fsBufToB64u(a.response.userHandle):null}};" +
                "var v=await fetch(\"/passkey/login/verify\",{method:\"POST\"," +
                "headers:{\"Content-Type\":\"application/json\"}," +
                "body:JSON.stringify(p),credentials:\"same-origin\"});" +
                "var vj=await v.json();" +
                "if(v.ok&&vj.ok){window.location=vj.redirect||\"/\";}" +
                "else throw new Error(vj.error||(\"verify \"+v.status));" +
                "}catch(e){s.innerHTML='<div class=\"alert alert-danger py-2 my-2\">'+" +
                "(e&&e.message?e.message:String(e))+'</div>';}}" +
                "async function sgcWebAuthnRegister(){" +
                "var s=document.getElementById(\"fsWebAuthn_status\");" + "try{" +
                "var n=document.getElementById(\"fsPasskeyName\");" +
                "var q=n&&n.value?(\"?name=\"+encodeURIComponent(n.value)):\"\";" +
                "s.innerHTML='<div class=\"text-muted small\">Requesting passkey...</div>';" +
                "var r=await fetch(\"/passkey/register/options\",{method:\"POST\"," +
                "headers:{\"Content-Type\":\"application/json\"},body:\"{}\"," +
                "credentials:\"same-origin\"});" + "var o=await r.json();" +
                "if(!r.ok||o.error)throw new Error(o.error||(\"options \"+r.status));" +
                "o.challenge=fsB64uToBuf(o.challenge);" +
                "o.user.id=fsB64uToBuf(o.user.id);" +
                "if(o.excludeCredentials){o.excludeCredentials.forEach(function(c){" +
                "c.id=fsB64uToBuf(c.id);});}" +
                "var c=await navigator.credentials.create({publicKey:o});" +
                "var at={id:c.id,rawId:fsBufToB64u(c.rawId),type:c.type,response:{" +
                "clientDataJSON:fsBufToB64u(c.response.clientDataJSON)," +
                "attestationObject:fsBufToB64u(c.response.attestationObject)}};" +
                "var v=await fetch(\"/passkey/register/verify\"+q,{method:\"POST\"," +
                "headers:{\"Content-Type\":\"application/json\"}," +
                "body:JSON.stringify(at),credentials:\"same-origin\"});" +
                "var vj=await v.json();" +
                "if(v.ok&&vj.ok){s.innerHTML='<div class=\"alert alert-success py-2 my-2\">Passkey registered.</div>';}" +
                "else throw new Error(vj.error||(\"verify \"+v.status));" +
                "}catch(e){s.innerHTML='<div class=\"alert alert-danger py-2 my-2\">'+" +
                "(e&&e.message?e.message:String(e))+'</div>';}}";
            oBag.Add(oWA);
            oCard.Body.AddRaw(oWA.HTML);

            oCard.Body.AddRaw("<div class=\"text-muted small mt-4\">" +
                "<div class=\"fw-semibold mb-1\">Demo accounts</div>" +
                "<div>dispatcher <b>dispatch</b> / dispatch</div>" +
                "<div>manager <b>manager</b> / demo1234</div>" +
                "<div>technician <b>amolina</b> / demo1234 (or dramos, enavarro, " +
                "jlopez, lvidal, mgarcia, pserrano, rortiz, sherrera)</div></div>");

            oWrap.Add(oCard);
            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;
            return WrapTemplate("Sign in - sgcField", vBody, aTheme, oBag.CSS, false);
        }

        public string BuildNotFoundPage(string aTheme)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
            oEmpty.Title = "That page does not exist";
            oEmpty.Description =
                "The link may be stale, or the job may have been reassigned.";
            oEmpty.Icon = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"56\" " +
                "height=\"56\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
                "stroke-width=\"1.5\"><circle cx=\"12\" cy=\"12\" r=\"9\"/>" +
                "<path d=\"M9 9l6 6M15 9l-6 6\"/></svg>";
            oEmpty.ActionCaption = "Back to the board";
            oEmpty.ActionHref = "/";
            oEmpty.Bordered = true;
            oBag.Add(oEmpty);
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container py-5";
            oWrap.AddRaw(oEmpty.HTML);
            return WrapTemplate("Not found - sgcField", oWrap.HTML, aTheme, oBag.CSS,
                false);
        }

        public string BuildDeniedPage(TFieldPageCtx aCtx, string aMessage)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
            oEmpty.Title = "Not available for your role";
            oEmpty.Description = aMessage;
            oEmpty.ActionCaption = "Go back";
            oEmpty.ActionHref = "/";
            oEmpty.Bordered = true;
            oBag.Add(oEmpty);
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "py-4";
            oWrap.AddRaw(oEmpty.HTML);
            return BuildPageShell(aCtx, "Not allowed", oWrap.HTML, "", oBag.CSS, false);
        }

        // ---------------------------------------------------------------------
        // Dispatch board
        // ---------------------------------------------------------------------

        // The Sunday the Scheduler's week view starts on for aAnchor: the Sunday of
        // the week that CONTAINS the anchor. Delphi DayOfWeek is 1 on a Sunday, so
        // the offset is DayOfWeek - 1, which is exactly .NET's DayOfWeek ordinal.
        //
        // Not StartOfTheWeek(aAnchor) - 1, which is what the component computes from
        // its own CurrentDate: StartOfTheWeek is the ISO Monday, so on a SUNDAY anchor
        // it lands on the Monday BEFORE it and the strip silently drops that Sunday's
        // work. The component is fed CurrentDate = weekStart + 3 (a Wednesday), so it
        // derives exactly this same Sunday whatever day the anchor was, and the lane's
        // data-weekstart matches the columns it drew.
        private static DateTime BoardWeekStart(DateTime aAnchor)
        {
            return aAnchor.Date.AddDays(-(int)aAnchor.DayOfWeek);
        }

        // A stable colour per technician so the same person keeps the same avatar all
        // over the app.
        private static string TechColor(long aId)
        {
            string[] CS_PALETTE = { "#EA580C", "#0D6EFD", "#198754", "#6F42C1",
                "#D63384", "#0DCAF0", "#FD7E14", "#20C997" };
            if (aId <= 0)
                return "#6C757D";
            return CS_PALETTE[aId % CS_PALETTE.Length];
        }

        // One board row. The Scheduler is configured as a single-row week grid, which
        // turns it into a resource lane: the lane IS the technician, the seven columns
        // are the days. StartHour = EndHour collapses the hour axis to one band; the
        // job chips carry their own start/end time in the label, and the tooltip
        // carries the job id the drag/drop script reads back.
        private string BuildLane(TFieldLane aLane, DateTime aWeekStart, bool aDayOnly,
            DateTime aDay)
        {
            TsgcHTMLComponent_Scheduler oSched = new TsgcHTMLComponent_Scheduler();
            oSched.SchedulerID = "fsSched" +
                aLane.TechnicianId.ToString(CultureInfo.InvariantCulture);
            oSched.View = TsgcHTMLSchedulerView.svWeek;
            // any day inside the target week; the component derives the week from it
            oSched.CurrentDate = aWeekStart.AddDays(3);
            oSched.StartHour = 8;
            oSched.EndHour = 8;
            int vCount = 0;
            for (int vI = 0; vI < aLane.Jobs.Length; vI++)
            {
                if (TFieldDBPool.IsZeroDate(aLane.Jobs[vI].ScheduledStart))
                    continue;
                if (aDayOnly && (aLane.Jobs[vI].ScheduledStart.Date != aDay.Date))
                    continue;
                vCount++;
                DateTime vEnd = aLane.Jobs[vI].ScheduledEnd;
                if (TFieldDBPool.IsZeroDate(vEnd))
                    vEnd = aLane.Jobs[vI].ScheduledStart;
                TsgcHTMLSchedulerEvent oEvt = oSched.Events.Add();
                oEvt.Title = FmtTime(aLane.Jobs[vI].ScheduledStart) + "  " +
                    aLane.Jobs[vI].Title;
                oEvt.StartDate = aLane.Jobs[vI].ScheduledStart.Date;
                oEvt.EndDate = vEnd.Date;
                oEvt.Color = StatusColor(aLane.Jobs[vI].Status);
                // The tooltip is also the drag payload: it opens with #<job id>.
                oEvt.Description = "#" +
                    aLane.Jobs[vI].Id.ToString(CultureInfo.InvariantCulture) + "  " +
                    aLane.Jobs[vI].Reference + " | " + aLane.Jobs[vI].CustomerName + " | " +
                    FmtTime(aLane.Jobs[vI].ScheduledStart) + "-" + FmtTime(vEnd) + " | " +
                    FieldConst.FieldPriorityLabel(aLane.Jobs[vI].Priority) + " | " +
                    FieldConst.FieldStatusLabel(aLane.Jobs[vI].Status);
            }

            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "fs-lane";
            if (aLane.TechnicianId == 0)
                oWrap.CSSClass = "fs-lane fs-lane-unassigned";
            oWrap.Attributes = "data-tech=\"" +
                aLane.TechnicianId.ToString(CultureInfo.InvariantCulture) +
                "\" data-weekstart=\"" + FmtISODate(aWeekStart) + "\"";

            TsgcHTMLContainer oHead = new TsgcHTMLContainer("div");
            oHead.CSSClass = "fs-lane-head";
            oHead.AddRaw(AvatarHTML(aLane.Initials, TsgcHTMLAvatarSize.asSmall,
                TechColor(aLane.TechnicianId)));
            TsgcHTMLContainer oName = new TsgcHTMLContainer("div");
            oName.CSSClass = "small";
            TsgcHTMLContainer oMeta = new TsgcHTMLContainer("div");
            oMeta.CSSClass = "fw-semibold text-truncate";
            oMeta.AddText(aLane.DisplayName);
            oName.Add(oMeta);
            string vLabel;
            if (aLane.TechnicianId == 0)
                vLabel = vCount.ToString(CultureInfo.InvariantCulture) + " waiting";
            else
                vLabel = vCount.ToString(CultureInfo.InvariantCulture) + " job(s)";
            oName.AddRaw("<div class=\"text-muted\" style=\"font-size:.72rem;\">" +
                HtmlEsc(vLabel) + "</div>");
            if (aLane.TechnicianId > 0)
                oName.AddRaw("<div class=\"mt-1\">" + PresenceBadge(aLane.Presence) +
                    "</div>");
            oHead.Add(oName);
            oWrap.Add(oHead);

            TsgcHTMLContainer oGrid = new TsgcHTMLContainer("div");
            oGrid.CSSClass = "fs-lane-grid";
            oGrid.AddRaw(oSched.HTML);
            oWrap.Add(oGrid);

            return oWrap.HTML;
        }

        public string BuildBoardLanes(TFieldLane[] aLanes, DateTime aAnchor, string aView)
        {
            DateTime vWeekStart = BoardWeekStart(aAnchor);
            bool vDayOnly = string.Equals(aView, "day", StringComparison.OrdinalIgnoreCase);
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            if (aLanes.Length == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No technicians on duty";
                oEmpty.Description =
                    "Activate a technician on the Users page to start dispatching.";
                oEmpty.ActionCaption = "Users";
                oEmpty.ActionHref = "/users";
                oEmpty.Compact = true;
                oRoot.AddRaw(oEmpty.HTML);
            }
            else
                for (int vI = 0; vI < aLanes.Length; vI++)
                    oRoot.AddRaw(BuildLane(aLanes[vI], vWeekStart, vDayOnly, aAnchor));
            return oRoot.HTML;
        }

        private void AddStatTile(TsgcHTMLContainer aRow, string aTitle, string aValue,
            TsgcHTMLStatColor aColor, string aCardID)
        {
            TsgcHTMLContainer oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-6 col-md-4 col-xl-2";
            oCol.AddRaw(TsgcHTMLComponent_StatCard.Build(aTitle, aValue, aColor,
                TsgcHTMLStatTrend.stNone, "", aCardID));
            aRow.Add(oCol);
        }

        public string BuildStatStrip(TFieldBoardStats aStats, bool aOOB)
        {
            TsgcHTMLContainer oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-2";
            AddStatTile(oRow, "Unassigned",
                aStats.Unassigned.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scDanger, "fsStatUnass");
            AddStatTile(oRow, "Scheduled",
                aStats.Scheduled.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scPrimary, "fsStatSched");
            AddStatTile(oRow, "En route",
                aStats.EnRoute.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scInfo, "fsStatEnroute");
            AddStatTile(oRow, "On site",
                aStats.OnSite.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scWarning, "fsStatOnsite");
            AddStatTile(oRow, "Done today",
                aStats.CompletedToday.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scSuccess, "fsStatDone");
            AddStatTile(oRow, "SLA at risk",
                aStats.SlaAtRisk.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scDark, "fsStatSla");
            string vInner = oRow.HTML;

            TsgcHTMLContainer oOuter = new TsgcHTMLContainer("div");
            oOuter.ID = FieldPagesConst.CS_ID_STAT_STRIP;
            if (aOOB)
                oOuter.Attributes = "hx-swap-oob=\"innerHTML\"";
            else
                oOuter.CSSClass = "mb-3";
            oOuter.AddRaw(vInner);
            return oOuter.HTML;
        }

        // The push signal. It carries no board of its own: it only tells every
        // open board that something moved, and each browser then re-fetches
        // /board/fragment for the day or week IT is showing.
        public string BuildBoardStaleSignal()
        {
            TsgcHTMLContainer oDiv = new TsgcHTMLContainer("div");
            oDiv.ID = FieldPagesConst.CS_ID_BOARD_STALE;
            oDiv.Attributes = "hx-swap-oob=\"innerHTML\"";
            oDiv.AddText(DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture));
            return oDiv.HTML;
        }

        // The bundle the push thread broadcasts: the lanes and the stat tiles, both as
        // out-of-band swaps so the dispatcher's board reflects a technician's status
        // change without a reload.
        public string BuildBoardOOB(TFieldLane[] aLanes, DateTime aAnchor, string aView,
            TFieldBoardStats aStats)
        {
            TsgcHTMLNodeList oList = new TsgcHTMLNodeList();
            TsgcHTMLContainer oDiv = new TsgcHTMLContainer("div");
            oDiv.ID = FieldPagesConst.CS_ID_BOARD_LANES;
            oDiv.Attributes = "hx-swap-oob=\"innerHTML\"";
            oDiv.AddRaw(BuildBoardLanes(aLanes, aAnchor, aView));
            oList.Add(oDiv);
            oList.AddRaw(BuildStatStrip(aStats, true));
            return oList.HTML;
        }

        // The full dispatch board: toolbar, technician filter, stat strip, the
        // lane strip inside a Splitter with the live rail, the right-click menu,
        // the reschedule modal and the drag/drop script.
        public string BuildBoardPage(TFieldPageCtx aCtx, TFieldLane[] aLanes,
            TFieldBoardStats aStats, TFieldTechnician[] aAllTechs, long[] aShownTechs,
            DateTime aAnchor, string aView, string aPresenceHTML, string aFeedHTML,
            string aLogHTML, string aJobsHTML)
        {
            bool vIsDay = string.Equals(aView, "day", StringComparison.OrdinalIgnoreCase);
            DateTime vPrev;
            DateTime vNext;
            string vTitle;
            if (vIsDay)
            {
                vPrev = aAnchor.AddDays(-1);
                vNext = aAnchor.AddDays(1);
                vTitle = FmtDateEN("dddd, d MMMM yyyy", aAnchor);
            }
            else
            {
                vPrev = aAnchor.AddDays(-7);
                vNext = aAnchor.AddDays(7);
                vTitle = "Week of " + FmtDateEN("d MMM", BoardWeekStart(aAnchor)) + " - " +
                    FmtDateEN("d MMM yyyy", BoardWeekStart(aAnchor).AddDays(6));
            }

            TFieldCSSBag oBag = new TFieldCSSBag();
            oBag.AddClass(typeof(TsgcHTMLComponent_Scheduler));
            oBag.AddClass(typeof(TsgcHTMLComponent_Presence));
            oBag.AddClass(typeof(TsgcHTMLComponent_ActivityFeed));
            oBag.AddClass(typeof(TsgcHTMLComponent_LogViewer));
            oBag.AddClass(typeof(TsgcHTMLComponent_JobProgress));
            oBag.AddClass(typeof(TsgcHTMLComponent_EmptyState));
            oBag.AddClass(typeof(TsgcHTMLComponent_Splitter));

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Dispatch", "Board" },
                new string[] { "/", "" }));

            // --- toolbar --- //
            TsgcHTMLContainer oToolbar = new TsgcHTMLContainer("div");
            oToolbar.CSSClass = "d-flex flex-wrap align-items-center gap-2 mb-3";
            oToolbar.AddRaw("<h5 class=\"mb-0 me-2\">" + HtmlEsc(vTitle) + "</h5>");
            oToolbar.AddRaw("<span class=\"text-muted small me-auto\" id=\"" +
                FieldPagesConst.CS_ID_CLOCK + "\"></span>");
            oToolbar.AddRaw("<div class=\"btn-group btn-group-sm\" role=\"group\">" +
                "<a class=\"btn btn-outline-secondary\" href=\"/?view=" + HtmlEsc(aView) +
                "&anchor=" + FmtISODate(vPrev) + "\">&laquo;</a>" +
                "<a class=\"btn btn-outline-secondary\" href=\"/?view=" + HtmlEsc(aView) +
                "\">Today</a>" + "<a class=\"btn btn-outline-secondary\" href=\"/?view=" +
                HtmlEsc(aView) + "&anchor=" + FmtISODate(vNext) + "\">&raquo;</a></div>");
            if (vIsDay)
                oToolbar.AddRaw("<div class=\"btn-group btn-group-sm\" role=\"group\">" +
                    "<a class=\"btn btn-fs\" href=\"/?view=day&anchor=" +
                    FmtISODate(aAnchor) + "\">Day</a><a class=\"btn btn-outline-secondary\" " +
                    "href=\"/?view=week&anchor=" + FmtISODate(aAnchor) +
                    "\">Week</a></div>");
            else
                oToolbar.AddRaw("<div class=\"btn-group btn-group-sm\" role=\"group\">" +
                    "<a class=\"btn btn-outline-secondary\" href=\"/?view=day&anchor=" +
                    FmtISODate(aAnchor) + "\">Day</a><a class=\"btn btn-fs\" " +
                    "href=\"/?view=week&anchor=" + FmtISODate(aAnchor) +
                    "\">Week</a></div>");
            oToolbar.AddRaw("<a class=\"btn btn-sm btn-fs\" href=\"/jobs/new\">" +
                "New job</a>");
            oToolbar.AddRaw("<a class=\"btn btn-sm btn-outline-secondary\" " +
                "href=\"/gantt\">Gantt</a>");
            oRoot.Add(oToolbar);

            // Which lanes to draw. A GET form, so the choice is in the URL and the
            // board can be bookmarked or shared with a colleague as it stands.
            TsgcHTMLForm oFilterForm = new TsgcHTMLForm();
            oFilterForm.Method = "GET";
            oFilterForm.Action = "/";
            oFilterForm.CSSClass = "d-flex flex-wrap align-items-end gap-2 mb-3";
            oFilterForm.AddHidden("view", aView);
            oFilterForm.AddHidden("anchor", FmtISODate(aAnchor));
            TsgcHTMLComponent_MultiSelect oTechFilter =
                new TsgcHTMLComponent_MultiSelect();
            oTechFilter.MultiSelectID = "fsTechFilter";
            oTechFilter.FieldName = "tech";
            oTechFilter.Placeholder = "All technicians";
            oTechFilter.SearchPlaceholder = "Find a technician";
            oTechFilter.NoResultsText = "No technician by that name";
            oTechFilter.ShowSearch = true;
            for (int vI = 0; vI < aAllTechs.Length; vI++)
            {
                bool vOn = false;
                for (int vJ = 0; vJ < aShownTechs.Length; vJ++)
                    if (aShownTechs[vJ] == aAllTechs[vI].Id)
                    {
                        vOn = true;
                        break;
                    }
                oTechFilter.AddOption(
                    aAllTechs[vI].Id.ToString(CultureInfo.InvariantCulture),
                    aAllTechs[vI].DisplayName + " - " + aAllTechs[vI].Skills, vOn);
            }
            oBag.Add(oTechFilter);
            oFilterForm.AddRaw("<div style=\"min-width:260px;max-width:460px;\">" +
                "<label class=\"form-label small text-muted mb-1\">Lanes on the " +
                "board</label>" + oTechFilter.HTML + "</div>");
            oFilterForm.AddRaw("<button type=\"submit\" class=\"btn btn-sm " +
                "btn-outline-secondary\">Apply</button>");
            oFilterForm.AddRaw("<a class=\"btn btn-sm btn-link\" href=\"/?view=" +
                HtmlEsc(aView) + "&anchor=" + FmtISODate(aAnchor) +
                "\">Show everyone</a>");
            oRoot.AddRaw(oFilterForm.HTML);

            oRoot.AddRaw(BuildStatStrip(aStats, false));

            // --- board (pane A) --- //
            TsgcHTMLCard oBoardCard = new TsgcHTMLCard();
            oBoardCard.CSSClass = "h-100";
            oBoardCard.BodyClass = "p-2";
            oBoardCard.Header.AddRaw("<div class=\"d-flex flex-wrap gap-2 " +
                "align-items-center justify-content-between\">" +
                "<strong>Dispatch board</strong>" +
                "<span class=\"text-muted small d-none d-md-inline\">Drag a job onto " +
                "another technician or another day. Right-click a job for more." +
                "</span></div>");
            TsgcHTMLContainer oBoardOuter = new TsgcHTMLContainer("div");
            oBoardOuter.ID = "fs-board";
            TsgcHTMLContainer oLanesDiv = new TsgcHTMLContainer("div");
            oLanesDiv.ID = FieldPagesConst.CS_ID_BOARD_LANES;
            oLanesDiv.AddRaw(BuildBoardLanes(aLanes, aAnchor, aView));
            oBoardOuter.Add(oLanesDiv);
            oBoardCard.Body.Add(oBoardOuter);
            string vBoard = oBoardCard.HTML;

            // --- live rail (pane B) --- //
            TsgcHTMLNodeList oRail = new TsgcHTMLNodeList();
            oRail.AddRaw(aPresenceHTML);
            oRail.AddRaw("<div class=\"mt-3\"></div>");
            oRail.AddRaw(aJobsHTML);
            oRail.AddRaw("<div class=\"mt-3\"></div>");
            oRail.AddRaw(aFeedHTML);
            oRail.AddRaw("<div class=\"mt-3\"></div>");
            oRail.AddRaw(aLogHTML);
            string vRail = oRail.HTML;

            TsgcHTMLComponent_Splitter oSplitter = new TsgcHTMLComponent_Splitter();
            oSplitter.SplitterID = "fsBoardSplit";
            oSplitter.Orientation = TsgcHTMLSplitterOrientation.soHorizontal;
            oSplitter.InitialSplit = 68;
            oSplitter.MinSizeA = 380;
            oSplitter.MinSizeB = 260;
            oSplitter.CSSHeight = "620px";
            oSplitter.PersistKey = "fsBoardSplit";
            oSplitter.AddPaneA(vBoard);
            oSplitter.AddPaneB(vRail);
            oBag.Add(oSplitter);
            oRoot.AddRaw(oSplitter.HTML);

            // --- right-click menu on a job chip --- //
            TsgcHTMLComponent_ContextMenu oMenu = new TsgcHTMLComponent_ContextMenu();
            oMenu.MenuID = "fsJobMenu";
            oMenu.TargetSelector = ".sgc-sched-event";
            TsgcHTMLContextMenuItem oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Job actions";
            oMenuItem.Header = true;
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Open job";
            oMenuItem.DataAction = "job:open";
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Change time...";
            oMenuItem.DataAction = "job:time";
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Divider = true;
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Send back to the queue";
            oMenuItem.DataAction = "job:unassign";
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Cancel job";
            oMenuItem.DataAction = "job:cancel";
            oBag.Add(oMenu);
            oRoot.AddRaw(oMenu.HTML);

            // --- "change time" modal --- //
            string vModalBody = "";
            TsgcHTMLComponent_DatePicker oDate = new TsgcHTMLComponent_DatePicker();
            oDate.DatePickerID = "fsTimeDate";
            oDate.ElementName = "date";
            oDate.Label_ = "Date";
            oDate.Value = FmtISODate(aAnchor);
            oBag.Add(oDate);
            vModalBody = vModalBody + oDate.HTML;
            TsgcHTMLComponent_TimePicker oTime = new TsgcHTMLComponent_TimePicker();
            oTime.FieldName = "time";
            oTime.LabelText = "Start time";
            oTime.Value = "09:00";
            oBag.Add(oTime);
            vModalBody = vModalBody + oTime.HTML;
            vModalBody = "<form id=\"fsTimeForm\"><input type=\"hidden\" " +
                "id=\"fsTimeJob\" name=\"job\" value=\"\">" + vModalBody +
                "<button type=\"submit\" class=\"btn btn-fs w-100\">Reschedule</button>" +
                "</form>";

            TsgcHTMLComponent_Modal oModal = new TsgcHTMLComponent_Modal();
            oModal.ModalID = "fsTimeModal";
            oModal.Title = "Reschedule job";
            oModal.Body = vModalBody;
            oModal.Centered = true;
            oBag.Add(oModal);
            oRoot.AddRaw(oModal.HTML);

            // --- toast host + board script --- //
            TsgcHTMLContainer oToasts = new TsgcHTMLContainer("div");
            oToasts.ID = "fs-toasts";
            oToasts.CSSClass = "position-fixed bottom-0 end-0 p-3";
            oToasts.Style = "z-index:1080;max-width:360px;";
            oRoot.Add(oToasts);

            // Hidden carriers the push thread writes into: the marker payload and
            // the "the board moved" signal.
            oRoot.AddRaw("<div id=\"" + FieldPagesConst.CS_ID_MAP_DATA +
                "\" style=\"display:none;\">" + "</div>");
            oRoot.AddRaw("<div id=\"" + FieldPagesConst.CS_ID_BOARD_STALE +
                "\" style=\"display:none;\"></div>");

            TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
            oScript.Code = "(function(){" + "var FSVIEW=\"" + JSStr(aView) +
                "\";var FSANCHOR=\"" + FmtISODate(aAnchor) + "\";" +
                "var board=document.getElementById(\"fs-board\");if(!board)return;" +
                "var lastJob=\"\";" + "function pad(n){return (n<10?\"0\":\"\")+n;}" +
                "function arm(){var e=board.querySelectorAll(\".sgc-sched-event\");" +
                "for(var i=0;i<e.length;i++){e[i].setAttribute(\"draggable\",\"true\");}}" +
                "arm();" +
                "if(window.MutationObserver){new MutationObserver(function(){arm();})" +
                ".observe(board,{childList:true,subtree:true});}" +
                "function jobIdOf(el){var t=el.getAttribute(\"title\")||\"\";" +
                "var m=t.match(/#(\\d+)/);return m?m[1]:\"\";}" +
                "function fsToast(msg,kind){var h=document.getElementById(\"fs-toasts\");" +
                "if(!h){return;}var d=document.createElement(\"div\");" +
                "d.className=\"alert alert-\"+kind+\" py-2 px-3 shadow-sm mb-2\";" +
                "d.setAttribute(\"role\",\"alert\");d.textContent=msg;h.appendChild(d);" +
                "setTimeout(function(){if(d.parentNode)d.parentNode.removeChild(d);},5000);}" +
                "window.fsToast=fsToast;" + "function fsPost(url,body){" +
                "fetch(url,{method:\"POST\",headers:{\"Content-Type\":" +
                "\"application/x-www-form-urlencoded\"},body:body," +
                "credentials:\"same-origin\"})" +
                ".then(function(r){return r.text().then(function(t){" +
                "return {ok:r.ok,status:r.status,text:t};});})" +
                ".then(function(res){if(res.ok){" +
                "var el=document.getElementById(\"" + FieldPagesConst.CS_ID_BOARD_LANES +
                "\");" +
                "if(el)el.innerHTML=res.text;fsToast(\"Board updated.\",\"success\");}" +
                "else{fsToast(res.text||(\"Refused (\"+res.status+\")\"),\"danger\");}})" +
                ".catch(function(){fsToast(\"Network error - the change was NOT saved." +
                " Try again.\",\"danger\");});}" + "window.fsPost=fsPost;" +
                "board.addEventListener(\"dragstart\",function(e){var t=e.target;" +
                "if(!t.classList||!t.classList.contains(\"sgc-sched-event\"))return;" +
                "var id=jobIdOf(t);if(!id){e.preventDefault();return;}" +
                "e.dataTransfer.setData(\"text/plain\",id);" +
                "e.dataTransfer.effectAllowed=\"move\";t.classList.add(\"fs-dragging\");});" +
                "board.addEventListener(\"dragend\",function(e){" +
                "if(e.target.classList)e.target.classList.remove(\"fs-dragging\");});" +
                "board.addEventListener(\"dragover\",function(e){" +
                "var td=e.target.closest?e.target.closest(\"td.sgc-sched-cell\"):null;" +
                "if(!td)return;e.preventDefault();e.dataTransfer.dropEffect=\"move\";" +
                "td.classList.add(\"fs-drop-hot\");});" +
                "board.addEventListener(\"dragleave\",function(e){" +
                "var td=e.target.closest?e.target.closest(\"td.sgc-sched-cell\"):null;" +
                "if(td)td.classList.remove(\"fs-drop-hot\");});" +
                "board.addEventListener(\"drop\",function(e){" +
                "var td=e.target.closest?e.target.closest(\"td.sgc-sched-cell\"):null;" +
                "if(!td)return;e.preventDefault();td.classList.remove(\"fs-drop-hot\");" +
                "var job=e.dataTransfer.getData(\"text/plain\");if(!job)return;" +
                "var lane=td.closest(\".fs-lane\");if(!lane)return;" +
                "var tr=td.parentNode;" +
                "var idx=Array.prototype.indexOf.call(tr.children,td)-1;" +
                "if(idx<0)idx=0;" +
                "var ws=lane.getAttribute(\"data-weekstart\")||\"\";" +
                "var tech=lane.getAttribute(\"data-tech\")||\"0\";" +
                "var p=ws.split(\"-\");" +
                "var d=new Date(parseInt(p[0],10),parseInt(p[1],10)-1," +
                "parseInt(p[2],10));d.setDate(d.getDate()+idx);" +
                "var iso=d.getFullYear()+\"-\"+pad(d.getMonth()+1)+\"-\"+pad(d.getDate());" +
                "var u=(tech===\"0\")?\"/board/unassign\":\"/board/assign\";" +
                "fsPost(u,\"job=\"+encodeURIComponent(job)+\"&technician=\"+" +
                "encodeURIComponent(tech)+\"&date=\"+iso+\"&view=\"+FSVIEW+" +
                "\"&anchor=\"+FSANCHOR);});" +
                "board.addEventListener(\"contextmenu\",function(e){" +
                "var el=e.target.closest?e.target.closest(\".sgc-sched-event\"):null;" +
                "lastJob=el?jobIdOf(el):\"\";});" +
                "document.addEventListener(\"sgcContextMenu:action\",function(e){" +
                "var a=(e.detail&&e.detail.action)?e.detail.action:\"\";" +
                "if(!lastJob)return;" +
                "if(a===\"job:open\"){window.location=\"/jobs/\"+lastJob;}" +
                "else if(a===\"job:unassign\"){fsPost(\"/board/unassign\",\"job=\"+lastJob+" +
                "\"&view=\"+FSVIEW+\"&anchor=\"+FSANCHOR);}" +
                "else if(a===\"job:cancel\"){fsPost(\"/jobs/cancel\",\"job=\"+lastJob+" +
                "\"&view=\"+FSVIEW+\"&anchor=\"+FSANCHOR);}" +
                "else if(a===\"job:time\"){var f=document.getElementById(\"fsTimeJob\");" +
                "if(f)f.value=lastJob;var m=document.getElementById(\"fsTimeModal\");" +
                "if(m&&window.bootstrap){new bootstrap.Modal(m).show();}}});" +
                "var tf=document.getElementById(\"fsTimeForm\");" +
                "if(tf){tf.addEventListener(\"submit\",function(e){e.preventDefault();" +
                "var j=document.getElementById(\"fsTimeJob\").value;" +
                "var dt=document.getElementById(\"fsTimeDate\").value;" +
                "var tm=document.getElementById(\"tp_time\").value;" +
                "fsPost(\"/board/assign\",\"job=\"+encodeURIComponent(j)+\"&date=\"+" +
                "encodeURIComponent(dt)+\"&time=\"+encodeURIComponent(tm)+" +
                "\"&view=\"+FSVIEW+\"&anchor=\"+FSANCHOR);" +
                "var m=document.getElementById(\"fsTimeModal\");" +
                "if(m&&window.bootstrap){var i=bootstrap.Modal.getInstance(m);" +
                "if(i)i.hide();}});}" +
                "var st=document.getElementById(\"" + FieldPagesConst.CS_ID_BOARD_STALE +
                "\");" +
                "if(st&&window.MutationObserver){new MutationObserver(function(){" +
                "fetch(\"/board/fragment?view=\"+FSVIEW+\"&anchor=\"+FSANCHOR," +
                "{credentials:\"same-origin\"})" +
                ".then(function(r){return r.ok?r.text():null;})" +
                ".then(function(h){if(h===null)return;" +
                "var el=document.getElementById(\"" + FieldPagesConst.CS_ID_BOARD_LANES +
                "\");" +
                "if(el)el.innerHTML=h;}).catch(function(){});})" +
                ".observe(st,{childList:true,characterData:true,subtree:true});}" +
                "})();";
            oRoot.AddRaw(oScript.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Dispatch board", vBody, "board", vExtra, true);
        }

        // ---------------------------------------------------------------------
        // DataTable cell readers. The Delphi reads a TDataSet through
        // FieldByName('x').AsInteger / .AsString; the managed port carries the
        // same rows in a System.Data.DataTable, so these three are the exact
        // equivalent and nothing else in the file touches a DataRow directly.
        // ---------------------------------------------------------------------

        private static string CellStr(DataRow aRow, string aColumn)
        {
            if (aRow == null)
                return "";
            if (!aRow.Table.Columns.Contains(aColumn))
                return "";
            object vValue = aRow[aColumn];
            if ((vValue == null) || (vValue == DBNull.Value))
                return "";
            return Convert.ToString(vValue, CultureInfo.InvariantCulture);
        }

        private static int CellInt(DataRow aRow, string aColumn)
        {
            string vText = CellStr(aRow, aColumn);
            int vResult;
            if (int.TryParse(vText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out vResult))
                return vResult;
            double vDouble;
            if (double.TryParse(vText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out vDouble))
                return (int)vDouble;
            return 0;
        }

        private static double CellFloat(DataRow aRow, string aColumn)
        {
            string vText = CellStr(aRow, aColumn);
            double vResult;
            if (double.TryParse(vText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out vResult))
                return vResult;
            return 0;
        }

        // ---------------------------------------------------------------------
        // Gantt
        // ---------------------------------------------------------------------

        public string BuildGanttPage(TFieldPageCtx aCtx, TFieldJob[] aJobs, DateTime aFrom,
            DateTime aTo, string aFlash)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Dispatch", "Gantt plan" },
                new string[] { "/", "" }));
            if (aFlash != "")
                oRoot.AddRaw(AlertHTML(FlashMessage(aFlash), TsgcHTMLAlertStyle.asSuccess));

            oRoot.AddRaw("<div class=\"d-flex flex-wrap align-items-center gap-2 " +
                "mb-3\"><h5 class=\"mb-0 me-auto\">Multi-day plan, " +
                HtmlEsc(FmtDate(aFrom)) + " to " + HtmlEsc(FmtDate(aTo)) +
                "</h5><a class=\"btn btn-sm btn-outline-secondary\" href=\"/gantt?from=" +
                FmtISODate(aFrom.AddDays(-14)) + "\">&laquo; Earlier</a>" +
                "<a class=\"btn btn-sm btn-outline-secondary\" href=\"/gantt\">Now</a>" +
                "<a class=\"btn btn-sm btn-outline-secondary\" href=\"/gantt?from=" +
                FmtISODate(aFrom.AddDays(14)) + "\">Later &raquo;</a></div>");

            if (aJobs.Length == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No multi-day work in this window";
                oEmpty.Description = "The Gantt shows jobs that span more than a " +
                    "single day. Move the window, or schedule a longer job.";
                oEmpty.ActionCaption = "Back to the board";
                oEmpty.ActionHref = "/";
                oEmpty.Bordered = true;
                oBag.Add(oEmpty);
                oRoot.AddRaw(oEmpty.HTML);
            }
            else
            {
                TsgcHTMLComponent_Gantt oGantt = new TsgcHTMLComponent_Gantt();
                oGantt.GanttID = "fsGantt";
                oGantt.Title = "Scheduled work by technician";
                oGantt.Zoom = TsgcHTMLGanttZoom.gzWeek;
                oGantt.RowHeight = 34;
                oGantt.Draggable = true;
                oGantt.Resizable = true;
                oGantt.ShowDependencies = true;
                oGantt.ShowProgress = true;
                oGantt.ShowToday = true;
                for (int vI = 0; vI < aJobs.Length; vI++)
                {
                    DateTime vEnd = aJobs[vI].ScheduledEnd;
                    if (vEnd <= aJobs[vI].ScheduledStart)
                        vEnd = aJobs[vI].ScheduledStart.AddDays(1);
                    // The job that has to finish before this one can start: the
                    // closest earlier job at the same SITE, or failing that for the
                    // same CUSTOMER. That is a real dependency on a service plan and
                    // it needs no extra column in the schema.
                    string vDepends = "";
                    DateTime vBest = DateTime.MinValue;
                    for (int vJ = 0; vJ < aJobs.Length; vJ++)
                    {
                        if (vJ == vI)
                            continue;
                        if (TFieldDBPool.IsZeroDate(aJobs[vJ].ScheduledEnd) ||
                            (aJobs[vJ].ScheduledEnd > aJobs[vI].ScheduledStart))
                            continue;
                        if (aJobs[vJ].ScheduledEnd < aJobs[vI].ScheduledStart.AddDays(-21))
                            continue;
                        if ((aJobs[vI].SiteId > 0) &&
                            (aJobs[vJ].SiteId == aJobs[vI].SiteId))
                        {
                            // a same-site predecessor always wins
                            if (TFieldDBPool.IsZeroDate(vBest) ||
                                (aJobs[vJ].ScheduledEnd > vBest))
                            {
                                vBest = aJobs[vJ].ScheduledEnd;
                                vDepends = "j" +
                                    aJobs[vJ].Id.ToString(CultureInfo.InvariantCulture);
                            }
                        }
                        else if ((vDepends == "") && (aJobs[vI].CustomerId > 0) &&
                            (aJobs[vJ].CustomerId == aJobs[vI].CustomerId))
                        {
                            vBest = aJobs[vJ].ScheduledEnd;
                            vDepends = "j" +
                                aJobs[vJ].Id.ToString(CultureInfo.InvariantCulture);
                        }
                    }
                    int vProgress;
                    if (string.Equals(aJobs[vI].Status, FieldConst.CS_JOB_COMPLETE,
                        StringComparison.OrdinalIgnoreCase))
                        vProgress = 100;
                    else if (string.Equals(aJobs[vI].Status, FieldConst.CS_JOB_ONSITE,
                        StringComparison.OrdinalIgnoreCase))
                        vProgress = 60;
                    else if (string.Equals(aJobs[vI].Status, FieldConst.CS_JOB_ENROUTE,
                        StringComparison.OrdinalIgnoreCase))
                        vProgress = 25;
                    else
                        vProgress = 0;
                    oGantt.AddTaskEx("j" +
                        aJobs[vI].Id.ToString(CultureInfo.InvariantCulture),
                        aJobs[vI].Reference + "  " + aJobs[vI].Title,
                        aJobs[vI].ScheduledStart, vEnd, vDepends, vProgress,
                        StatusColor(aJobs[vI].Status), aJobs[vI].TechnicianName);
                }
                oBag.Add(oGantt);
                TsgcHTMLCard oCard = new TsgcHTMLCard();
                oCard.BodyClass = "p-2";
                oCard.Body.AddRaw(oGantt.HTML);
                oCard.Footer.AddRaw("<span class=\"text-muted small\">Drag a bar to " +
                    "move the job, drag an edge to change its duration. Both are " +
                    "written straight back to the jobs table.</span>");
                oRoot.AddRaw(oCard.HTML);

                // The component sends the move over the WebSocket AND raises a DOM
                // event. The demo persists through the event so the change survives
                // with or without a live socket, and so it is curl-verifiable.
                TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
                oScript.Code = "(function(){" +
                    "document.addEventListener(\"sgcGantt:taskMoved\",function(e){" +
                    "var d=e.detail||{};if(!d.taskid)return;" +
                    "fetch(\"/gantt/move\",{method:\"POST\",headers:{\"Content-Type\":" +
                    "\"application/x-www-form-urlencoded\"},credentials:\"same-origin\"," +
                    "body:\"task=\"+encodeURIComponent(d.taskid)+\"&start=\"+" +
                    "encodeURIComponent(d.start)+\"&end=\"+encodeURIComponent(d.end)})" +
                    ".then(function(r){return r.text().then(function(t){" +
                    "return{ok:r.ok,text:t};});})" + ".then(function(res){" +
                    "var s=document.getElementById(\"fs-gantt-status\");" +
                    "if(!s)return;" + "if(res.ok){s.className=\"text-success small\";" +
                    "s.textContent=res.text;}else{s.className=\"text-danger small\";" +
                    "s.textContent=res.text||\"The move was refused.\";}})" +
                    ".catch(function(){var s=document.getElementById(" +
                    "\"fs-gantt-status\");if(s){s.className=\"text-danger small\";" +
                    "s.textContent=\"Network error - the move was NOT saved.\";}});" +
                    "});})();";
                oRoot.AddRaw(oScript.HTML);
                oRoot.AddRaw("<div class=\"mt-2\" id=\"fs-gantt-status\"></div>");
            }

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Gantt plan", vBody, "gantt", vExtra, false);
        }

        // ---------------------------------------------------------------------
        // Map
        // ---------------------------------------------------------------------

        // The live marker payload. One record per line so the page script can apply it
        // without a JSON parser and without any markup being generated client-side:
        //   T|id|lat|lng|name|presence
        public string BuildMapPayload(TFieldTechnician[] aTechs, TFieldJob[] aJobs,
            bool aOOB)
        {
            string vText = "";
            for (int vI = 0; vI < aTechs.Length; vI++)
            {
                if ((aTechs[vI].CurrentLat == 0) && (aTechs[vI].CurrentLng == 0))
                    continue;
                vText = vText + "T|" +
                    aTechs[vI].Id.ToString(CultureInfo.InvariantCulture) + "|" +
                    JSNum(aTechs[vI].CurrentLat, 6) + "|" +
                    JSNum(aTechs[vI].CurrentLng, 6) + "|" +
                    aTechs[vI].DisplayName.Replace("|", " ") + "|" +
                    aTechs[vI].Presence + "\n";
            }
            TsgcHTMLContainer oDiv = new TsgcHTMLContainer("div");
            oDiv.ID = FieldPagesConst.CS_ID_MAP_DATA;
            if (aOOB)
                oDiv.Attributes = "hx-swap-oob=\"innerHTML\"";
            else
                oDiv.Style = "display:none;";
            // AddText escapes, which is exactly right: the payload carries technician
            // names typed by a human.
            oDiv.AddText(vText);
            return oDiv.HTML;
        }

        public string BuildMapPage(TFieldPageCtx aCtx, TFieldTechnician[] aTechs,
            TFieldJob[] aJobs)
        {
            // Centre on the mean of the jobs actually being shown so the demo opens on
            // the cluster rather than on a default city.
            double vCLat = 0;
            double vCLng = 0;
            int vN = 0;
            for (int vI = 0; vI < aJobs.Length; vI++)
                if ((aJobs[vI].SiteLat != 0) || (aJobs[vI].SiteLng != 0))
                {
                    vCLat = vCLat + aJobs[vI].SiteLat;
                    vCLng = vCLng + aJobs[vI].SiteLng;
                    vN++;
                }
            if (vN > 0)
            {
                vCLat = vCLat / vN;
                vCLng = vCLng / vN;
            }
            else
            {
                vCLat = 40.4168;
                vCLng = -3.7038;
            }

            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Dispatch", "Live map" },
                new string[] { "/", "" }));
            oRoot.AddRaw("<div class=\"d-flex flex-wrap align-items-center gap-2 " +
                "mb-3\"><h5 class=\"mb-0 me-auto\">Technicians and today's work</h5>" +
                "<span class=\"text-muted small\" id=\"" + FieldPagesConst.CS_ID_CLOCK +
                "\"></span></div>");

            TsgcHTMLComponent_Map oMap = new TsgcHTMLComponent_Map();
            oMap.MapID = "fsMap";
            oMap.CSSHeight = "520px";
            oMap.CenterLatitude = vCLat;
            oMap.CenterLongitude = vCLng;
            oMap.Zoom = 6;
            if (string.Equals(aCtx.Theme, "dark", StringComparison.OrdinalIgnoreCase))
                oMap.TileProvider = TsgcHTMLMapTileProvider.mtCartoDBDark;
            else
                oMap.TileProvider = TsgcHTMLMapTileProvider.mtCartoDB;
            // Job markers are fixed for the life of the page, so they are the
            // component's own Markers collection, popups and all. The technicians
            // move, so they are drawn by the live layer below.
            for (int vI = 0; vI < aJobs.Length; vI++)
            {
                if ((aJobs[vI].SiteLat == 0) && (aJobs[vI].SiteLng == 0))
                    continue;
                TsgcHTMLMapMarker oMarker = oMap.Markers.Add();
                oMarker.Latitude = aJobs[vI].SiteLat;
                oMarker.Longitude = aJobs[vI].SiteLng;
                oMarker.Color = StatusColor(aJobs[vI].Status);
                // popup and tooltip are plain text, encoded by the component
                oMarker.PopupText = aJobs[vI].Reference + " - " +
                    aJobs[vI].Title + " | " +
                    aJobs[vI].CustomerName + " | " +
                    aJobs[vI].SiteName + " | " +
                    FieldConst.FieldStatusLabel(aJobs[vI].Status) + " - " +
                    FmtDateTime(aJobs[vI].ScheduledStart);
                oMarker.TooltipText = aJobs[vI].Reference;
            }
            oBag.Add(oMap);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.BodyClass = "p-2";
            // A skeleton while Leaflet boots, hidden by the map script once the
            // tiles are up. Without it the card is a blank 520px hole.
            TsgcHTMLComponent_Placeholder oSkeleton =
                new TsgcHTMLComponent_Placeholder();
            oSkeleton.PlaceholderID = "fsMapSkeleton";
            oSkeleton.LineCount = 6;
            oSkeleton.Animation = TsgcHTMLPlaceholderAnimation.paWave;
            oSkeleton.ShowTitle = true;
            oSkeleton.ShowButtons = false;
            oBag.Add(oSkeleton);
            oCard.Body.AddRaw("<div id=\"fs-map-skeleton\" class=\"p-3\">" +
                oSkeleton.HTML + "</div>");
            oCard.Body.AddRaw(oMap.HTML);
            oRoot.AddRaw(oCard.HTML);

            oRoot.AddRaw(BuildMapPayload(aTechs, aJobs, false));

            // The Map component leaves its Leaflet map instance in the global `m`.
            // The technician layer is rebuilt from the hidden payload every time
            // the server pushes a new one, so the vans move without a reload.
            TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
            oScript.Code = "(function(){" + "var layer=null,markers={};" +
                "function icon(p){var c=(p===\"onsite\")?\"#FFC107\":" +
                "((p===\"enroute\")?\"#0DCAF0\":((p===\"off\")?\"#6C757D\":\"#198754\"));" +
                "return L.divIcon({className:\"\",iconSize:[18,18]," +
                "html:'<div style=\"width:18px;height:18px;border-radius:50%;" +
                "border:3px solid #fff;box-shadow:0 0 3px rgba(0,0,0,.5);" +
                "background:'+c+'\"></div>'});}" + "function apply(){" +
                "if(typeof L===\"undefined\"||typeof m===\"undefined\"||!m)return;" +
                "var el=document.getElementById(\"" + FieldPagesConst.CS_ID_MAP_DATA +
                "\");if(!el)return;" + "if(!layer){layer=L.layerGroup().addTo(m);}" +
                "var lines=(el.textContent||\"\").split(\"\\n\");var seen={};" +
                "for(var i=0;i<lines.length;i++){" +
                "var p=lines[i].split(\"|\");if(p.length<6||p[0]!==\"T\")continue;" +
                "var id=p[1],la=parseFloat(p[2]),ln=parseFloat(p[3]);" +
                "if(isNaN(la)||isNaN(ln))continue;seen[id]=true;" +
                "var txt=p[4]+\" - \"+p[5];" +
                "if(markers[id]){markers[id].setLatLng([la,ln]);" +
                "markers[id].setIcon(icon(p[5]));" +
                "markers[id].setPopupContent(txt);}" +
                "else{markers[id]=L.marker([la,ln],{icon:icon(p[5])})" +
                ".addTo(layer).bindPopup(txt).bindTooltip(p[4]);}}" +
                "for(var k in markers){if(!seen[k]){layer.removeLayer(markers[k]);" +
                "delete markers[k];}}}" + "var tries=0;" +
                "var t=setInterval(function(){tries++;" +
                "if(typeof L!==\"undefined\"&&typeof m!==\"undefined\"&&m){" +
                "clearInterval(t);apply();" +
                "var sk=document.getElementById(\"fs-map-skeleton\");" +
                "if(sk)sk.style.display=\"none\";" +
                "var el=document.getElementById(\"" + FieldPagesConst.CS_ID_MAP_DATA +
                "\");" +
                "if(el&&window.MutationObserver){new MutationObserver(function(){" +
                "apply();}).observe(el,{childList:true,characterData:true," +
                "subtree:true});}" + "}else if(tries>60){clearInterval(t);" +
                "var w=document.getElementById(\"fs-map-warn\");" +
                "if(w)w.style.display=\"\";}},250);" + "})();";
            oRoot.AddRaw(oScript.HTML);

            oRoot.AddRaw("<div id=\"fs-map-warn\" class=\"alert alert-warning mt-3\" " +
                "style=\"display:none;\">The map library could not be reached, so the " +
                "tiles are not drawn. The technician and job list below carries the " +
                "same data, straight from the database.</div>");

            // Server-rendered fallback list: the map needs the internet for tiles,
            // this table never does.
            TsgcHTMLTable oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.TheadClass = "table-light";
            oTable.Responsive = true;
            oTable.AddColumn("Technician");
            oTable.AddColumn("Status");
            oTable.AddColumn("Latitude", "text-end");
            oTable.AddColumn("Longitude", "text-end");
            oTable.AddColumn("Open jobs", "text-end");
            for (int vI = 0; vI < aTechs.Length; vI++)
            {
                TsgcHTMLTableRow oRow = oTable.AddRow();
                oRow.AddCellRaw(AvatarHTML(aTechs[vI].AvatarInitials,
                    TsgcHTMLAvatarSize.asSmall, TechColor(aTechs[vI].Id)) +
                    " <span class=\"ms-2\">" + HtmlEsc(aTechs[vI].DisplayName) +
                    "</span>");
                oRow.AddCellRaw(PresenceBadge(aTechs[vI].Presence));
                oRow.AddCellText(JSNum(aTechs[vI].CurrentLat, 5), "text-end fs-mono");
                oRow.AddCellText(JSNum(aTechs[vI].CurrentLng, 5), "text-end fs-mono");
                oRow.AddCellText(
                    aTechs[vI].OpenJobs.ToString(CultureInfo.InvariantCulture),
                    "text-end");
            }
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.BodyClass = "p-0";
            oCard.Header.AddRaw("<strong>Technician positions</strong>");
            oCard.Body.AddRaw(oTable.HTML);
            oRoot.AddRaw(oCard.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Live map", vBody, "map", vExtra, true);
        }

        // ---------------------------------------------------------------------
        // Calendar
        // ---------------------------------------------------------------------

        public string BuildCalendarPage(TFieldPageCtx aCtx, DataTable aDataSet, int aYear,
            int aMonth, string aSQL)
        {
            int vPrevY = aYear;
            int vPrevM = aMonth - 1;
            if (vPrevM < 1)
            {
                vPrevM = 12;
                vPrevY--;
            }
            int vNextY = aYear;
            int vNextM = aMonth + 1;
            if (vNextM > 12)
            {
                vNextM = 1;
                vNextY++;
            }

            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Dispatch", "Calendar" },
                new string[] { "/", "" }));

            TsgcHTMLComponent_Calendar oCal = new TsgcHTMLComponent_Calendar();
            oCal.CalendarID = "fsCalendar";
            oCal.Year = aYear;
            oCal.Month = aMonth;
            oCal.HighlightToday = true;
            oCal.ShowNavigation = true;
            oCal.PrevURL = "/calendar?year=" +
                vPrevY.ToString(CultureInfo.InvariantCulture) + "&month=" +
                vPrevM.ToString(CultureInfo.InvariantCulture);
            oCal.NextURL = "/calendar?year=" +
                vNextY.ToString(CultureInfo.InvariantCulture) + "&month=" +
                vNextM.ToString(CultureInfo.InvariantCulture);
            oCal.EventDotSize = 7;
            // Straight off the query. The day number is computed by SQLite
            // (strftime), so nothing here parses a date string and no locale can
            // break it; the SQL that did the work is printed underneath.
            if (aDataSet != null)
            {
                for (int vR = 0; vR < aDataSet.Rows.Count; vR++)
                {
                    int vDay = CellInt(aDataSet.Rows[vR], "day_of_month");
                    if ((vDay >= 1) && (vDay <= 31))
                    {
                        TsgcHTMLCalendarEvent oEvt = oCal.Events.Add();
                        oEvt.Day = vDay;
                        oEvt.Title = CellStr(aDataSet.Rows[vR], "title");
                        oEvt.ColorStyle = StatusColor(CellStr(aDataSet.Rows[vR],
                            "status"));
                    }
                }
            }
            oBag.Add(oCal);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Scheduled work, " +
                HtmlEsc(FmtDateEN("MMMM yyyy", new DateTime(aYear, aMonth, 1))) +
                "</strong>");
            oCard.Body.AddRaw(oCal.HTML);
            oRoot.AddRaw(oCard.HTML);

            TsgcHTMLContainer oPre = new TsgcHTMLContainer("div");
            oPre.CSSClass = "fs-sql";
            oPre.AddText(aSQL);
            string vSQLHTML = oPre.HTML;

            TsgcHTMLAccordion oAcc = new TsgcHTMLAccordion("fsCalSql");
            oAcc.AddItem("The query behind this calendar (no REST endpoint)",
                false).Body.AddRaw(vSQLHTML);
            oRoot.AddRaw("<div class=\"mt-3\">" + oAcc.HTML + "</div>");

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Calendar", vBody, "calendar", vExtra, false);
        }

        // ---------------------------------------------------------------------
        // Jobs
        // ---------------------------------------------------------------------

        // Supplies the row-action markup for a Grid column. The Grid escapes every
        // cell by default, which is right; a column of links is the exception, and
        // OnGetCellHTML is the hook the component provides for it.
        private class TFieldCellLinker
        {
            public string[] Hrefs = new string[0];
            public int LinkColumn;

            public void GetCellHTML(object Sender, int aRowIndex, int aColIndex,
                string aValue, ref string aHTML)
            {
                if (aColIndex != LinkColumn)
                    return;
                if ((aRowIndex < 0) || (aRowIndex >= Hrefs.Length))
                    return;
                aHTML = "<a class=\"fw-semibold\" href=\"" + HtmlEsc(Hrefs[aRowIndex]) +
                    "\">" + HtmlEsc(aValue) + "</a>";
            }
        }

        // Rebuild the job-list query string, dropping the blanks, so a sort link or a
        // page link carries the current filter forward.
        private static void JobQueryAdd(ref string aParts, string aName, string aValue)
        {
            if (aValue == "")
                return;
            if (aParts != "")
                aParts = aParts + "&";
            aParts = aParts + aName + "=" + UrlEnc(aValue);
        }

        private static string JobQueryString(TFieldJobListFilter aFilter, string aSort,
            string aDir, int aPage)
        {
            string vParts = "";
            if (!string.Equals(aFilter.Status, "all", StringComparison.OrdinalIgnoreCase))
                JobQueryAdd(ref vParts, "status", aFilter.Status);
            if (aFilter.TechnicianId >= 0)
                JobQueryAdd(ref vParts, "technician",
                    aFilter.TechnicianId.ToString(CultureInfo.InvariantCulture));
            if (!string.Equals(aFilter.Priority, "all",
                StringComparison.OrdinalIgnoreCase))
                JobQueryAdd(ref vParts, "priority", aFilter.Priority);
            JobQueryAdd(ref vParts, "q", aFilter.Search);
            JobQueryAdd(ref vParts, "sort", aSort);
            JobQueryAdd(ref vParts, "dir", aDir);
            if (aPage > 1)
                JobQueryAdd(ref vParts, "page",
                    aPage.ToString(CultureInfo.InvariantCulture));
            if (vParts == "")
                return "";
            return "?" + vParts;
        }

        public string BuildJobListPage(TFieldPageCtx aCtx, TFieldJob[] aJobs,
            TFieldTechnician[] aTechs, TFieldJobListFilter aFilter)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Work", "Jobs" },
                new string[] { "/", "" }));

            // --- filter bar --- //
            TsgcHTMLForm oFilterForm = new TsgcHTMLForm();
            oFilterForm.Method = "GET";
            oFilterForm.Action = "/jobs";
            oFilterForm.CSSClass = "row g-2 align-items-end mb-3";

            TsgcHTMLSelect oSelStatus = new TsgcHTMLSelect();
            oSelStatus.ColClass = "col-6 col-md-2";
            oSelStatus.Name = "status";
            oSelStatus.FieldID = "fltStatus";
            oSelStatus.Label_ = "Status";
            oSelStatus.CSSClass = "form-select form-select-sm";
            oSelStatus.AddOption("all", "All statuses",
                string.Equals(aFilter.Status, "all", StringComparison.OrdinalIgnoreCase) ||
                (aFilter.Status == ""));
            string[] vStatuses = FieldConst.FieldStatusList();
            for (int vI = 0; vI < vStatuses.Length; vI++)
                oSelStatus.AddOption(vStatuses[vI],
                    FieldConst.FieldStatusLabel(vStatuses[vI]),
                    string.Equals(aFilter.Status, vStatuses[vI],
                    StringComparison.OrdinalIgnoreCase));
            oFilterForm.Add(oSelStatus);

            TsgcHTMLSelect oSelTech = new TsgcHTMLSelect();
            oSelTech.ColClass = "col-6 col-md-3";
            oSelTech.Name = "technician";
            oSelTech.FieldID = "fltTech";
            oSelTech.Label_ = "Technician";
            oSelTech.CSSClass = "form-select form-select-sm";
            oSelTech.AddOption("-1", "Everyone", aFilter.TechnicianId < 0);
            oSelTech.AddOption("0", "Unassigned", aFilter.TechnicianId == 0);
            for (int vI = 0; vI < aTechs.Length; vI++)
                oSelTech.AddOption(aTechs[vI].Id.ToString(CultureInfo.InvariantCulture),
                    aTechs[vI].DisplayName, aFilter.TechnicianId == aTechs[vI].Id);
            oFilterForm.Add(oSelTech);

            TsgcHTMLSelect oSelPrio = new TsgcHTMLSelect();
            oSelPrio.ColClass = "col-6 col-md-2";
            oSelPrio.Name = "priority";
            oSelPrio.FieldID = "fltPrio";
            oSelPrio.Label_ = "Priority";
            oSelPrio.CSSClass = "form-select form-select-sm";
            oSelPrio.AddOption("all", "Any priority",
                string.Equals(aFilter.Priority, "all",
                StringComparison.OrdinalIgnoreCase) || (aFilter.Priority == ""));
            oSelPrio.AddOption(FieldConst.CS_PRIORITY_URGENT, "Urgent",
                string.Equals(aFilter.Priority, FieldConst.CS_PRIORITY_URGENT,
                StringComparison.OrdinalIgnoreCase));
            oSelPrio.AddOption(FieldConst.CS_PRIORITY_HIGH, "High",
                string.Equals(aFilter.Priority, FieldConst.CS_PRIORITY_HIGH,
                StringComparison.OrdinalIgnoreCase));
            oSelPrio.AddOption(FieldConst.CS_PRIORITY_NORMAL, "Normal",
                string.Equals(aFilter.Priority, FieldConst.CS_PRIORITY_NORMAL,
                StringComparison.OrdinalIgnoreCase));
            oSelPrio.AddOption(FieldConst.CS_PRIORITY_LOW, "Low",
                string.Equals(aFilter.Priority, FieldConst.CS_PRIORITY_LOW,
                StringComparison.OrdinalIgnoreCase));
            oFilterForm.Add(oSelPrio);

            TsgcHTMLField oSearch = new TsgcHTMLField(TsgcHTMLInputType.itSearch, "q");
            oSearch.ColClass = "col-12 col-md-3";
            oSearch.FieldID = "fltSearch";
            oSearch.Label_ = "Search";
            oSearch.Placeholder = "Reference, title or customer";
            oSearch.Value = aFilter.Search;
            oSearch.InputCSSClass = "form-control form-control-sm";
            oFilterForm.Add(oSearch);

            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "col-12 col-md-2 d-grid";
            TsgcHTMLButton oBtn = new TsgcHTMLButton("Filter",
                TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "btn-sm btn-fs";
            oWrap.Add(oBtn);
            oFilterForm.Add(oWrap);

            oRoot.AddRaw(oFilterForm.HTML);

            if (aJobs.Length == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No jobs match that filter";
                oEmpty.Description =
                    "Try widening the status or the technician, or clear the search.";
                oEmpty.ActionCaption = "Clear filters";
                oEmpty.ActionHref = "/jobs";
                oEmpty.SecondaryActionCaption = "New job";
                oEmpty.SecondaryActionHref = "/jobs/new";
                oEmpty.Bordered = true;
                oBag.Add(oEmpty);
                oRoot.AddRaw(oEmpty.HTML);
            }
            else
            {
                TFieldCellLinker oLinker = new TFieldCellLinker();
                oLinker.Hrefs = new string[aJobs.Length];
                oLinker.LinkColumn = 0;
                for (int vI = 0; vI < aJobs.Length; vI++)
                    oLinker.Hrefs[vI] = "/jobs/" +
                        aJobs[vI].Id.ToString(CultureInfo.InvariantCulture);

                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "fsJobsGrid";
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.HeaderClass = "table-light";
                oGrid.EmptyText = "No jobs.";
                oGrid.ShowSort = true;
                oGrid.ShowFilter = true;
                oGrid.OnGetCellHTML += oLinker.GetCellHTML;
                TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
                oCol.Name = "ref";
                oCol.Title = "Reference";
                oCol = oGrid.Columns.Add();
                oCol.Name = "title";
                oCol.Title = "Job";
                oCol = oGrid.Columns.Add();
                oCol.Name = "customer";
                oCol.Title = "Customer";
                oCol = oGrid.Columns.Add();
                oCol.Name = "tech";
                oCol.Title = "Technician";
                oCol = oGrid.Columns.Add();
                oCol.Name = "priority";
                oCol.Title = "Priority";
                oCol = oGrid.Columns.Add();
                oCol.Name = "status";
                oCol.Title = "Status";
                oCol = oGrid.Columns.Add();
                oCol.Name = "sched";
                oCol.Title = "Scheduled";
                for (int vI = 0; vI < aJobs.Length; vI++)
                {
                    if (aJobs[vI].TechnicianName == "")
                        oGrid.AddRow(aJobs[vI].Reference, aJobs[vI].Title,
                            aJobs[vI].CustomerName, "Unassigned",
                            FieldConst.FieldPriorityLabel(aJobs[vI].Priority),
                            FieldConst.FieldStatusLabel(aJobs[vI].Status),
                            FmtDateTime(aJobs[vI].ScheduledStart));
                    else
                        oGrid.AddRow(aJobs[vI].Reference, aJobs[vI].Title,
                            aJobs[vI].CustomerName, aJobs[vI].TechnicianName,
                            FieldConst.FieldPriorityLabel(aJobs[vI].Priority),
                            FieldConst.FieldStatusLabel(aJobs[vI].Status),
                            FmtDateTime(aJobs[vI].ScheduledStart));
                }
                oBag.Add(oGrid);

                TsgcHTMLCard oCard = new TsgcHTMLCard();
                oCard.BodyClass = "p-0";
                oCard.Header.AddRaw("<div class=\"d-flex align-items-center " +
                    "justify-content-between\"><strong>" +
                    aFilter.Total.ToString(CultureInfo.InvariantCulture) +
                    " job(s)</strong><a class=\"btn btn-sm btn-fs\" " +
                    "href=\"/jobs/new\">New job</a></div>");
                oCard.Body.AddRaw(oGrid.HTML);
                oRoot.AddRaw(oCard.HTML);

                int vPages;
                if (aFilter.PageSize > 0)
                    vPages = (aFilter.Total + aFilter.PageSize - 1) / aFilter.PageSize;
                else
                    vPages = 1;
                if (vPages > 1)
                {
                    TsgcHTMLComponent_Pagination oPager =
                        new TsgcHTMLComponent_Pagination();
                    oPager.PaginationID = "fsJobsPager";
                    oPager.CurrentPage = aFilter.Page;
                    oPager.TotalPages = vPages;
                    oPager.TotalItems = aFilter.Total;
                    oPager.PageSize = aFilter.PageSize;
                    oPager.MaxVisible = 7;
                    oPager.ShowFirstLast = true;
                    oPager.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                    oPager.BaseURL = "/jobs" + JobQueryString(aFilter, aFilter.Sort,
                        aFilter.Dir, 1);
                    if (oPager.BaseURL.IndexOf('?') >= 0)
                        oPager.BaseURL = oPager.BaseURL + "&page=";
                    else
                        oPager.BaseURL = oPager.BaseURL + "?page=";
                    oBag.Add(oPager);
                    oRoot.AddRaw("<div class=\"mt-3\">" + oPager.HTML + "</div>");
                }
            }

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Jobs", vBody, "jobs", vExtra, false);
        }

        // The state machine drawn with the Diagram component. The node for the job's
        // current status is filled with its own colour, the rest are muted, so the
        // picture reads as "you are here" rather than as a generic legend.
        private static TsgcHTMLColor DiagramNodeColor(string aId, string aStatus)
        {
            if (string.Equals(aId, aStatus, StringComparison.OrdinalIgnoreCase))
                return StatusColor(aId);
            return TsgcHTMLColor.hcLight;
        }

        private static string BuildStatusDiagram(string aStatus)
        {
            TsgcHTMLComponent_Diagram oDiag = new TsgcHTMLComponent_Diagram();
            oDiag.DiagramID = "fsStateMachine";
            oDiag.DiagramWidth = 880;
            oDiag.DiagramHeight = 250;
            // 110-wide boxes on a 175 pitch: 65px of clear space between them, which
            // is what the edge labels need in order not to sit on top of a box.
            oDiag.AddNode(FieldConst.CS_JOB_NEW, "new", 10, 20,
                DiagramNodeColor(FieldConst.CS_JOB_NEW, aStatus),
                TsgcHTMLDiagramNodeShape.nsRoundedRect).Width = 110;
            oDiag.AddNode(FieldConst.CS_JOB_SCHEDULED, "scheduled", 185, 20,
                DiagramNodeColor(FieldConst.CS_JOB_SCHEDULED, aStatus),
                TsgcHTMLDiagramNodeShape.nsRoundedRect).Width = 110;
            oDiag.AddNode(FieldConst.CS_JOB_ENROUTE, "enroute", 360, 20,
                DiagramNodeColor(FieldConst.CS_JOB_ENROUTE, aStatus),
                TsgcHTMLDiagramNodeShape.nsRoundedRect).Width = 110;
            oDiag.AddNode(FieldConst.CS_JOB_ONSITE, "onsite", 535, 20,
                DiagramNodeColor(FieldConst.CS_JOB_ONSITE, aStatus),
                TsgcHTMLDiagramNodeShape.nsRoundedRect).Width = 110;
            oDiag.AddNode(FieldConst.CS_JOB_COMPLETE, "complete", 710, 20,
                DiagramNodeColor(FieldConst.CS_JOB_COMPLETE, aStatus),
                TsgcHTMLDiagramNodeShape.nsRoundedRect).Width = 110;
            oDiag.AddNode(FieldConst.CS_JOB_CANCELLED, "cancelled", 360, 170,
                DiagramNodeColor(FieldConst.CS_JOB_CANCELLED, aStatus),
                TsgcHTMLDiagramNodeShape.nsDiamond).Width = 110;
            oDiag.Connect(FieldConst.CS_JOB_NEW, FieldConst.CS_JOB_SCHEDULED, "assign");
            oDiag.Connect(FieldConst.CS_JOB_SCHEDULED, FieldConst.CS_JOB_ENROUTE,
                "depart");
            oDiag.Connect(FieldConst.CS_JOB_ENROUTE, FieldConst.CS_JOB_ONSITE, "arrive");
            oDiag.Connect(FieldConst.CS_JOB_ONSITE, FieldConst.CS_JOB_COMPLETE, "close");
            oDiag.Connect(FieldConst.CS_JOB_SCHEDULED, FieldConst.CS_JOB_CANCELLED,
                "cancel");
            oDiag.Connect(FieldConst.CS_JOB_ONSITE, FieldConst.CS_JOB_CANCELLED,
                "cancel");
            return oDiag.HTML;
        }

        // The lifecycle Stepper: completed steps behind the job, the current one
        // highlighted, the rest upcoming. A cancelled job shows a single failed step.
        private static string BuildStatusStepper(string aStatus)
        {
            TsgcHTMLComponent_Stepper oStep = new TsgcHTMLComponent_Stepper();
            oStep.StepperID = "fsLifecycle";
            oStep.Layout = TsgcHTMLStepperLayout.slHorizontal;
            oStep.ShowContent = false;
            oStep.CircleSize = 34;
            int vIdx = FieldConst.FieldStatusIndex(aStatus);
            if (vIdx < 0)
            {
                TsgcHTMLStepItem oItem = oStep.Items.Add();
                oItem.Title = "Cancelled";
                oItem.Description = "This job will not be carried out";
                oItem.State = TsgcHTMLStepState.ssCurrent;
            }
            else
            {
                string[] vAll = new string[5];
                vAll[0] = FieldConst.CS_JOB_NEW;
                vAll[1] = FieldConst.CS_JOB_SCHEDULED;
                vAll[2] = FieldConst.CS_JOB_ENROUTE;
                vAll[3] = FieldConst.CS_JOB_ONSITE;
                vAll[4] = FieldConst.CS_JOB_COMPLETE;
                for (int vI = 0; vI < vAll.Length; vI++)
                {
                    TsgcHTMLStepItem oItem = oStep.Items.Add();
                    oItem.Title = FieldConst.FieldStatusLabel(vAll[vI]);
                    if (vI < vIdx)
                        oItem.State = TsgcHTMLStepState.ssCompleted;
                    else if (vI == vIdx)
                        oItem.State = TsgcHTMLStepState.ssCurrent;
                    else
                        oItem.State = TsgcHTMLStepState.ssUpcoming;
                }
            }
            return oStep.HTML;
        }

        // The status buttons the machine actually allows from here, and nothing else.
        private static string BuildStatusActions(long aJobId, string aStatus,
            string aReturn)
        {
            string[] vNext = FieldConst.FieldNextStatuses(aStatus);
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "d-flex flex-wrap gap-2";
            if (vNext.Length == 0)
                oWrap.AddRaw("<span class=\"text-muted small\">This job is " +
                    HtmlEsc(FieldConst.FieldStatusLabel(aStatus).ToLowerInvariant()) +
                    ". The state machine allows no further transition.</span>");
            for (int vI = 0; vI < vNext.Length; vI++)
            {
                TsgcHTMLForm oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/jobs/" +
                    aJobId.ToString(CultureInfo.InvariantCulture) + "/status";
                oForm.CSSClass = "m-0";
                oForm.AddHidden("status", vNext[vI]);
                if (aReturn != "")
                    oForm.AddHidden("return", aReturn);
                TsgcHTMLButtonStyle vStyle;
                if (string.Equals(vNext[vI], FieldConst.CS_JOB_CANCELLED,
                    StringComparison.OrdinalIgnoreCase))
                    vStyle = TsgcHTMLButtonStyle.bsOutlineDanger;
                else if (string.Equals(vNext[vI], FieldConst.CS_JOB_COMPLETE,
                    StringComparison.OrdinalIgnoreCase))
                    vStyle = TsgcHTMLButtonStyle.bsSuccess;
                else if (string.Equals(vNext[vI], FieldConst.CS_JOB_NEW,
                    StringComparison.OrdinalIgnoreCase))
                    vStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
                else
                    vStyle = TsgcHTMLButtonStyle.bsPrimary;
                TsgcHTMLButton oBtn = new TsgcHTMLButton("Mark " +
                    FieldConst.FieldStatusLabel(vNext[vI]), vStyle);
                oBtn.ButtonType = "submit";
                oForm.Add(oBtn);
                oWrap.AddRaw(oForm.HTML);
            }
            return oWrap.HTML;
        }

        private static string BuildChecklistBlock(TFieldChecklistItem[] aItems,
            long aJobId, bool aInteractive, string aPostURL)
        {
            int vDone = 0;
            for (int vI = 0; vI < aItems.Length; vI++)
                if (aItems[vI].Done)
                    vDone++;
            int vPct;
            if (aItems.Length > 0)
                vPct = (int)Math.Round((double)vDone * 100 / aItems.Length,
                    MidpointRounding.AwayFromZero);
            else
                vPct = 0;

            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            TsgcHTMLComponent_ProgressBar oProgress =
                new TsgcHTMLComponent_ProgressBar();
            oProgress.ProgressBarID = "fsChecklistBar";
            oProgress.Value = vPct;
            oProgress.Max = 100;
            oProgress.ShowLabel = true;
            oProgress.LabelFormat = vDone.ToString(CultureInfo.InvariantCulture) +
                " of " + aItems.Length.ToString(CultureInfo.InvariantCulture) + " done";
            oProgress.CSSHeight = "22px";
            if (vPct == 100)
                oProgress.ColorStyle = TsgcHTMLColor.hcSuccess;
            else
                oProgress.ColorStyle = TsgcHTMLColor.hcWarning;
            oWrap.AddRaw(oProgress.HTML);

            if (aItems.Length == 0)
                oWrap.AddRaw("<div class=\"text-muted small mt-3\">" +
                    "This job has no checklist.</div>");

            for (int vI = 0; vI < aItems.Length; vI++)
            {
                TsgcHTMLContainer oItem = new TsgcHTMLContainer("div");
                oItem.CSSClass = "fs-check-item d-flex align-items-center " +
                    "border-bottom py-2";
                if (aInteractive)
                {
                    TsgcHTMLForm oForm = new TsgcHTMLForm();
                    oForm.Method = "POST";
                    oForm.Action = aPostURL;
                    oForm.CSSClass = "m-0 w-100";
                    oForm.AddHidden("item",
                        aItems[vI].Id.ToString(CultureInfo.InvariantCulture));
                    if (aItems[vI].Done)
                        oForm.AddHidden("done", "0");
                    else
                        oForm.AddHidden("done", "1");
                    TsgcHTMLCheckbox oCheck = new TsgcHTMLCheckbox("tick");
                    oCheck.FieldID = "chk" +
                        aItems[vI].Id.ToString(CultureInfo.InvariantCulture);
                    oCheck.Label_ = aItems[vI].Text_;
                    oCheck.Checked = aItems[vI].Done;
                    oCheck.Value = "1";
                    oCheck.WrapperClass = "form-check w-100";
                    oCheck.InputCSSClass = "form-check-input";
                    oForm.Add(oCheck);
                    oForm.AddRaw("<button type=\"submit\" class=\"visually-hidden\">" +
                        "Save</button>");
                    oItem.AddRaw(oForm.HTML);
                }
                else
                {
                    TsgcHTMLCheckbox oCheck = new TsgcHTMLCheckbox("");
                    oCheck.FieldID = "ro" +
                        aItems[vI].Id.ToString(CultureInfo.InvariantCulture);
                    oCheck.Label_ = aItems[vI].Text_;
                    oCheck.Checked = aItems[vI].Done;
                    oCheck.Disabled = true;
                    oCheck.WrapperClass = "form-check w-100";
                    oItem.AddRaw(oCheck.HTML);
                }
                oWrap.Add(oItem);
            }

            if (aInteractive)
                oWrap.AddRaw("<script>(function(){" +
                    "var f=document.querySelectorAll('.fs-check-item form');" +
                    "for(var i=0;i<f.length;i++){" +
                    "(function(fm){var c=fm.querySelector('input[type=checkbox]');" +
                    "if(c)c.addEventListener(\"change\",function(){fm.submit();});})(f[i]);}" +
                    "})();</script>");
            return oWrap.HTML;
        }

        private static string BuildPartsBlock(TFieldJobPart[] aParts)
        {
            if (aParts.Length == 0)
                return TsgcHTMLComponent_EmptyState.Build("No parts used",
                    "Parts added from the van are listed here with their price.");
            double vTotal = 0;
            TsgcHTMLTable oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.TheadClass = "table-light";
            oTable.Responsive = true;
            oTable.AddColumn("SKU");
            oTable.AddColumn("Part");
            oTable.AddColumn("Qty", "text-end");
            oTable.AddColumn("Unit", "text-end");
            oTable.AddColumn("Total", "text-end");
            for (int vI = 0; vI < aParts.Length; vI++)
            {
                vTotal = vTotal + aParts[vI].Qty * aParts[vI].UnitPrice;
                TsgcHTMLTableRow oRow = oTable.AddRow();
                oRow.AddCellText(aParts[vI].Sku, "fs-mono");
                oRow.AddCellText(aParts[vI].Name);
                oRow.AddCellText(aParts[vI].Qty.ToString(CultureInfo.InvariantCulture),
                    "text-end");
                oRow.AddCellText(FmtMoney(aParts[vI].UnitPrice), "text-end");
                oRow.AddCellText(FmtMoney(aParts[vI].Qty * aParts[vI].UnitPrice),
                    "text-end");
            }
            TsgcHTMLTableRow oLast = oTable.AddRow();
            oLast.RowClass = "table-light fw-semibold";
            oLast.AddCellText("");
            oLast.AddCellText("Total");
            oLast.AddCellText("");
            oLast.AddCellText("");
            oLast.AddCellText(FmtMoney(vTotal), "text-end");
            return oTable.HTML;
        }

        private static string BuildPhotoStrip(long aJobId, TFieldJobPhoto[] aPhotos)
        {
            if (aPhotos.Length == 0)
                return TsgcHTMLComponent_EmptyState.Build("No photos yet",
                    "Photos taken on site are attached to the job and to the service " +
                    "report.");
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "d-flex flex-wrap gap-2";
            for (int vI = 0; vI < aPhotos.Length; vI++)
                oWrap.AddRaw("<a href=\"/jobs/" +
                    aJobId.ToString(CultureInfo.InvariantCulture) + "/photos/" +
                    aPhotos[vI].Id.ToString(CultureInfo.InvariantCulture) +
                    "\" target=\"_blank\" title=\"" + HtmlEsc(aPhotos[vI].Caption) +
                    "\"><img class=\"fs-photo\" src=\"/jobs/" +
                    aJobId.ToString(CultureInfo.InvariantCulture) + "/photos/" +
                    aPhotos[vI].Id.ToString(CultureInfo.InvariantCulture) + "\" alt=\"" +
                    HtmlEsc(aPhotos[vI].Caption) + "\"></a>");
            return oWrap.HTML;
        }

        private static string BuildTimelineBlock(TFieldJobEvent[] aEvents)
        {
            if (aEvents.Length == 0)
                return TsgcHTMLComponent_EmptyState.Build("Nothing has happened yet",
                    "Every assignment, arrival and sign-off lands here.");
            TsgcHTMLComponent_Timeline oTl = new TsgcHTMLComponent_Timeline();
            oTl.TimelineID = "fsJobTimeline";
            oTl.DotSize = 16;
            for (int vI = 0; vI < aEvents.Length; vI++)
            {
                string vKind = (aEvents[vI].Kind ?? "").ToLowerInvariant();
                // The position breadcrumbs the simulator drops are map data, not job
                // history; they would drown the timeline.
                if (vKind == "gps")
                    continue;
                TsgcHTMLColor vColor;
                if (vKind == "completed")
                    vColor = TsgcHTMLColor.hcSuccess;
                else if (vKind == "cancelled")
                    vColor = TsgcHTMLColor.hcSecondary;
                else if (vKind == "onsite")
                    vColor = TsgcHTMLColor.hcWarning;
                else if (vKind == "enroute")
                    vColor = TsgcHTMLColor.hcInfo;
                else if (vKind == "rating")
                    vColor = TsgcHTMLColor.hcWarning;
                else if (vKind == "assigned")
                    vColor = TsgcHTMLColor.hcPrimary;
                else
                    vColor = TsgcHTMLColor.hcDark;
                TsgcHTMLTimelineItem oItem = oTl.Items.Add();
                oItem.Title = aEvents[vI].Kind;
                oItem.Content = aEvents[vI].Detail;
                oItem.Timestamp = FmtDateTime(aEvents[vI].CreatedAt);
                oItem.ColorStyle = vColor;
            }
            return oTl.HTML;
        }

        // The dispatcher-side conversation. Chat is the rich component (avatars,
        // bubbles, date separators); the technician gets the compact ChatBox.
        private static string BuildChatBlock(TFieldPageCtx aCtx, TFieldJob aJob,
            TFieldMessage[] aMessages, bool aRich)
        {
            if (aRich)
            {
                TsgcHTMLComponent_Chat oChat = new TsgcHTMLComponent_Chat();
                oChat.ChatID = "fsChat";
                oChat.CSSHeight = "420px";
                oChat.Title = aJob.TechnicianName;
                if (oChat.Title == "")
                    oChat.Title = "No technician assigned yet";
                oChat.Subtitle = aJob.Reference + " - " + aJob.Title;
                oChat.HeaderAvatarInitials = aJob.TechnicianInitials;
                oChat.ShowInput = true;
                oChat.ShowAttachButton = false;
                oChat.InputPlaceholder = "Message the technician...";
                oChat.Options.ShowDateSeparators = true;
                oChat.Options.GroupConsecutive = true;
                for (int vI = 0; vI < aMessages.Length; vI++)
                {
                    bool vMine = aMessages[vI].FromUserId == aCtx.UserId;
                    TsgcHTMLChat_Message oMsg = oChat.Messages.Add();
                    oMsg.Sender = aMessages[vI].FromName;
                    oMsg.Text = aMessages[vI].Body;
                    oMsg.Timestamp = FmtDateTime(aMessages[vI].CreatedAt);
                    oMsg.AvatarInitials = aMessages[vI].FromInitials;
                    if (vMine)
                    {
                        oMsg.Align = TsgcHTMLChatMessageAlign.maRight;
                        oMsg.Color = TsgcHTMLColor.hcPrimary;
                        if (!TFieldDBPool.IsZeroDate(aMessages[vI].ReadAt))
                            oMsg.Status = TsgcHTMLChatMessageStatus.msRead;
                        else
                            oMsg.Status = TsgcHTMLChatMessageStatus.msSent;
                    }
                    else
                    {
                        oMsg.Align = TsgcHTMLChatMessageAlign.maLeft;
                        oMsg.Color = TsgcHTMLColor.hcSecondary;
                    }
                }
                return oChat.HTML;
            }
            else
            {
                TsgcHTMLComponent_ChatBox oBox = new TsgcHTMLComponent_ChatBox();
                oBox.ChatID = "fsChat";
                oBox.CSSHeight = "46vh";
                oBox.Title = "Dispatch";
                oBox.ShowInput = true;
                oBox.InputPlaceholder = "Message dispatch...";
                oBox.SendButtonText = "Send";
                oBox.SendButtonStyle = TsgcHTMLButtonStyle.bsPrimary;
                for (int vI = 0; vI < aMessages.Length; vI++)
                {
                    bool vMine = aMessages[vI].FromUserId == aCtx.UserId;
                    TsgcHTMLChatMessage oMsg2 = oBox.Messages.Add();
                    oMsg2.Sender = aMessages[vI].FromName;
                    oMsg2.Text = aMessages[vI].Body;
                    oMsg2.Timestamp = FmtDateTime(aMessages[vI].CreatedAt);
                    oMsg2.AvatarInitials = aMessages[vI].FromInitials;
                    if (vMine)
                    {
                        oMsg2.Align = TsgcHTMLChatAlign.caRight;
                        oMsg2.Color = TsgcHTMLColor.hcPrimary;
                    }
                    else
                    {
                        oMsg2.Align = TsgcHTMLChatAlign.caLeft;
                        oMsg2.Color = TsgcHTMLColor.hcSecondary;
                    }
                }
                return oBox.HTML;
            }
        }

        // The submit interceptor both chat components need. Their input form is a
        // data-sgc-ws-send form with onsubmit="return false", so the message would
        // only ever travel over the socket; this posts it as well, appends the
        // rendered bubble the server sends back, and says so plainly when the post
        // fails instead of losing what was typed.
        private static string ChatPostScript(string aPostURL, string aChatID)
        {
            return "(function(){" + "var url=\"" + JSStr(aPostURL) + "\";" +
                "var box=document.getElementById(\"" + JSStr(aChatID) + "_messages\");" +
                "function warn(txt,retry){" +
                "var h=document.getElementById(\"fs-chat-status\");if(!h)return;" +
                "h.innerHTML=\"\";var d=document.createElement(\"div\");" +
                "d.className=\"alert alert-warning d-flex align-items-center gap-2 py-2\";" +
                "var s=document.createElement(\"span\");s.textContent=txt;d.appendChild(s);" +
                "var b=document.createElement(\"button\");b.type=\"button\";" +
                "b.className=\"btn btn-sm btn-outline-dark ms-auto\";b.textContent=\"Retry\";" +
                "b.addEventListener(\"click\",function(){h.innerHTML=\"\";send(retry);});" +
                "d.appendChild(b);h.appendChild(d);}" + "function send(text){" +
                "fetch(url,{method:\"POST\",headers:{\"Content-Type\":" +
                "\"application/x-www-form-urlencoded\"},credentials:\"same-origin\"," +
                "body:\"body=\"+encodeURIComponent(text)})" +
                ".then(function(r){if(!r.ok)throw new Error(\"HTTP \"+r.status);" +
                "return r.text();})" + ".then(function(html){" +
                "var h=document.getElementById(\"fs-chat-status\");if(h)h.innerHTML=\"\";" +
                "if(box){box.insertAdjacentHTML(\"beforeend\",html);" +
                "box.scrollTop=box.scrollHeight;}})" +
                ".catch(function(){warn(\"That message did not reach dispatch. It is " +
                "still here, nothing was lost.\",text);});}" +
                "document.addEventListener(\"submit\",function(e){" +
                "var f=e.target;" +
                "if(!f||!f.hasAttribute||!f.hasAttribute(\"data-sgc-ws-send\"))return;" +
                "var i=f.querySelector('[name=\"message\"]');if(!i)return;" +
                "var t=(i.value||\"\").trim();if(!t)return;i.value=\"\";" + "send(t);},true);" +
                "})();";
        }

        // One rendered chat bubble, returned by the chat POST so the page can
        // append it without a reload.
        public string BuildChatBubble(TFieldPageCtx aCtx, TFieldMessage aMessage)
        {
            TsgcHTMLComponent_ChatBox oBox = new TsgcHTMLComponent_ChatBox();
            oBox.ChatID = "fsChat";
            TsgcHTMLChatMessage oMsg = oBox.Messages.Add();
            oMsg.Sender = aMessage.FromName;
            oMsg.Text = aMessage.Body;
            oMsg.Timestamp = FmtDateTime(aMessage.CreatedAt);
            oMsg.AvatarInitials = aMessage.FromInitials;
            if (aMessage.FromUserId == aCtx.UserId)
            {
                oMsg.Align = TsgcHTMLChatAlign.caRight;
                oMsg.Color = TsgcHTMLColor.hcPrimary;
            }
            else
            {
                oMsg.Align = TsgcHTMLChatAlign.caLeft;
                oMsg.Color = TsgcHTMLColor.hcSecondary;
            }
            return oBox.GetLastMessageHTML();
        }

        public string BuildJobDetailPage(TFieldPageCtx aCtx, TFieldJob aJob,
            TFieldJobEvent[] aEvents, TFieldChecklistItem[] aChecklist,
            TFieldJobPart[] aParts, TFieldJobPhoto[] aPhotos, TFieldSignature aSig,
            bool aHasSig, TFieldMessage[] aMessages, string aFlash, string aError)
        {
            int vRating = 0;
            for (int vI = 0; vI < aEvents.Length; vI++)
                if (string.Equals(aEvents[vI].Kind, "rating",
                    StringComparison.OrdinalIgnoreCase))
                {
                    int vParsed;
                    if (int.TryParse((aEvents[vI].Detail ?? "").Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out vParsed))
                        vRating = vParsed;
                    else
                        vRating = 0;
                }

            TFieldCSSBag oBag = new TFieldCSSBag();
            oBag.AddClass(typeof(TsgcHTMLComponent_Timeline));
            oBag.AddClass(typeof(TsgcHTMLComponent_Stepper));
            oBag.AddClass(typeof(TsgcHTMLComponent_Diagram));
            oBag.AddClass(typeof(TsgcHTMLComponent_Chat));
            oBag.AddClass(typeof(TsgcHTMLComponent_ChatBox));
            oBag.AddClass(typeof(TsgcHTMLComponent_EmptyState));
            oBag.AddClass(typeof(TsgcHTMLComponent_FileUpload));
            oBag.AddClass(typeof(TsgcHTMLComponent_SignaturePad));

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(
                new string[] { "Work", "Jobs", aJob.Reference },
                new string[] { "/", "/jobs", "" }));
            if (aFlash != "")
                oRoot.AddRaw(AlertHTML(FlashMessage(aFlash),
                    TsgcHTMLAlertStyle.asSuccess));
            if (aError != "")
                oRoot.AddRaw(AlertHTML(aError, TsgcHTMLAlertStyle.asDanger));

            // --- header --- //
            string vHead = "<div class=\"d-flex flex-wrap align-items-center gap-2 mb-3\">" +
                "<h4 class=\"mb-0 me-2\">" + HtmlEsc(aJob.Title) + "</h4>" +
                StatusBadge(aJob.Status) + " " + PriorityBadge(aJob.Priority) +
                " <span class=\"fs-mono text-muted\">" + HtmlEsc(aJob.Reference) +
                "</span>" + "<span class=\"ms-auto\"></span>" +
                "<a class=\"btn btn-sm btn-outline-secondary\" href=\"/jobs/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture) +
                "/report.pdf\">Service report (PDF)</a>" +
                "<a class=\"btn btn-sm btn-outline-secondary\" href=\"/track/" +
                HtmlEsc(aJob.Reference) + "\" target=\"_blank\">Customer view</a></div>";
            oRoot.AddRaw(vHead);

            oRoot.AddRaw("<div class=\"card mb-3\"><div class=\"card-body\">" +
                BuildStatusStepper(aJob.Status) + "</div></div>");

            // --- overview tab --- //
            TsgcHTMLDescriptionList oDL = new TsgcHTMLDescriptionList();
            oDL.TermClass = "col-sm-4 text-muted";
            oDL.DescClass = "col-sm-8";
            oDL.AddItem("Customer", aJob.CustomerName);
            oDL.AddItem("Site", aJob.SiteName + " - " + aJob.SiteAddress);
            if (aJob.AssetName != "")
                oDL.AddItem("Asset", aJob.AssetName);
            if (aJob.TechnicianName != "")
                oDL.AddItem("Technician", aJob.TechnicianName);
            else
                oDL.AddItem("Technician", "Unassigned");
            if (aJob.ScheduledEnd.Date == aJob.ScheduledStart.Date)
                oDL.AddItem("Scheduled", FmtDateTime(aJob.ScheduledStart) + " - " +
                    FmtTime(aJob.ScheduledEnd));
            else
                oDL.AddItem("Scheduled", FmtDateTime(aJob.ScheduledStart) + "  to  " +
                    FmtDateTime(aJob.ScheduledEnd));
            oDL.AddItem("SLA due", FmtDateTime(aJob.SlaDueAt));
            oDL.AddItem("Started", FmtDateTime(aJob.ActualStart));
            oDL.AddItem("Finished", FmtDateTime(aJob.ActualEnd));
            oDL.AddItem("Created", FmtDateTime(aJob.CreatedAt));
            oDL.AddItem("Description", aJob.Description);
            string vOverview = oDL.HTML;

            // SLA headroom as a gauge: 100 = the whole window is still ahead.
            double vSLAPct = 0;
            if (!TFieldDBPool.IsZeroDate(aJob.SlaDueAt))
            {
                if (!TFieldDBPool.IsZeroDate(aJob.ActualEnd))
                {
                    if (aJob.ActualEnd <= aJob.SlaDueAt)
                        vSLAPct = 100;
                    else
                        vSLAPct = 0;
                }
                else if (aJob.SlaDueAt > DateTime.Now)
                    vSLAPct = Math.Min(100.0,
                        (aJob.SlaDueAt - DateTime.Now).TotalHours * 10);
                else
                    vSLAPct = 0;
            }

            TsgcHTMLComponent_Gauge oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.GaugeID = "fsJobSla";
            oGauge.Title = "SLA headroom";
            oGauge.Value = vSLAPct;
            oGauge.MinValue = 0;
            oGauge.MaxValue = 100;
            oGauge.Unit_ = "%";
            oGauge.ThresholdMid = 35;
            oGauge.ThresholdHigh = 70;
            oGauge.ColorLowStyle = TsgcHTMLColor.hcDanger;
            oGauge.ColorMidStyle = TsgcHTMLColor.hcWarning;
            oGauge.ColorHighStyle = TsgcHTMLColor.hcSuccess;
            oBag.Add(oGauge);
            vOverview = vOverview + "<div class=\"row g-3 mt-1\">" +
                "<div class=\"col-12 col-md-4\">" + oGauge.HTML + "</div>";

            TsgcHTMLComponent_Rating oRating = new TsgcHTMLComponent_Rating();
            oRating.RatingID = "fsJobRating";
            oRating.Value = vRating;
            oRating.MaxValue = 5;
            oRating.ReadOnly = true;
            oRating.ShowValue = true;
            oRating.Size = "1.6rem";
            oBag.Add(oRating);
            vOverview = vOverview + "<div class=\"col-12 col-md-8\">" +
                "<div class=\"text-muted small\">Customer satisfaction</div>" +
                oRating.HTML;
            if (vRating == 0)
                vOverview = vOverview +
                    "<div class=\"text-muted small\">Not rated yet.</div>";
            vOverview = vOverview + "</div></div>";

            TsgcHTMLComponent_Popover oPopover = new TsgcHTMLComponent_Popover();
            oPopover.PopoverID = "fsSMPop";
            oPopover.Content = "Why is that button missing?";
            oPopover.Title = "Server-side state machine";
            oPopover.Body = "Only the transitions the machine allows from the " +
                "current status are rendered, and the server checks them again " +
                "before writing. An illegal transition is refused with 409.";
            oPopover.Placement = TsgcHTMLPlacement.plTop;
            oPopover.Trigger = TsgcHTMLPopoverTrigger.ptClick;
            oBag.Add(oPopover);
            vOverview = vOverview + "<hr class=\"my-3\">" +
                "<div class=\"d-flex align-items-center gap-2 mb-2\">" +
                "<strong>Allowed transitions</strong>" + oPopover.HTML + "</div>" +
                BuildStatusActions(aJob.Id, aJob.Status, "/jobs/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture)) +
                "<div class=\"mt-4\"><div class=\"text-muted " +
                "small mb-2\">The job status state machine, with this job's state " +
                "filled in. A cancel is allowed from any live state (two of the " +
                "four are drawn); scheduled can be unassigned back to new and " +
                "enroute recalled to scheduled; complete and cancelled are " +
                "terminal.</div><div class=\"overflow-auto\">" +
                BuildStatusDiagram(aJob.Status) + "</div></div>";

            // --- photo upload block --- //
            TsgcHTMLComponent_FileUpload oUpload = new TsgcHTMLComponent_FileUpload();
            oUpload.UploadID = "fsJobPhotoUp";
            oUpload.Action = "/my/" + aJob.Id.ToString(CultureInfo.InvariantCulture) +
                "/photo";
            oUpload.Accept = "image/png,image/jpeg";
            oUpload.InputName = "photo";
            oUpload.Multiple = false;
            oUpload.Title = "Add a photo";
            oUpload.Subtitle = "PNG or JPEG, up to 10 MB";
            oUpload.ButtonText = "Choose a photo";
            oUpload.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oBag.Add(oUpload);
            string vSigHTML = oUpload.HTML;

            // --- tabs --- //
            TsgcHTMLComponent_Tabs oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.TabsID = "fsJobTabs";
            oTabs.Style = TsgcHTMLTabStyle.tsTab;
            TsgcHTMLTabItem oTab = oTabs.Items.Add();
            oTab.Title = "Overview";
            oTab.Content = vOverview;
            oTab.Active = true;
            oTab.TabID = "ovw";
            oTab = oTabs.Items.Add();
            oTab.Title = "History";
            oTab.Content = BuildTimelineBlock(aEvents);
            oTab.TabID = "hist";
            oTab = oTabs.Items.Add();
            oTab.Title = "Checklist";
            oTab.Content = BuildChecklistBlock(aChecklist, aJob.Id, false, "");
            oTab.TabID = "chk";
            oTab = oTabs.Items.Add();
            oTab.Title = "Parts";
            oTab.Content = BuildPartsBlock(aParts);
            oTab.TabID = "parts";
            oTab = oTabs.Items.Add();
            oTab.Title = "Photos";
            oTab.Content = BuildPhotoStrip(aJob.Id, aPhotos) +
                "<div class=\"mt-3\">" + vSigHTML + "</div>";
            oTab.TabID = "photos";
            if (aHasSig)
            {
                oTab = oTabs.Items.Add();
                oTab.Title = "Signature";
                oTab.Content = "<div class=\"mb-2 text-muted small\">Signed by " +
                    HtmlEsc(aSig.SignerName) + " on " +
                    HtmlEsc(FmtDateTime(aSig.SignedAt)) + "</div>" +
                    "<img class=\"fs-sig\" alt=\"Customer signature\" src=\"" +
                    HtmlEsc(aSig.SignaturePng) + "\">";
                oTab.TabID = "sig";
            }
            else
            {
                oTab = oTabs.Items.Add();
                oTab.Title = "Signature";
                oTab.Content = TsgcHTMLComponent_EmptyState.Build("Not signed off yet",
                    "The technician captures the customer signature on the phone " +
                    "when the job is complete.");
                oTab.TabID = "sig";
            }
            oTab = oTabs.Items.Add();
            oTab.Title = "Chat";
            oTab.Content = "<div id=\"fs-chat-status\"></div>" +
                BuildChatBlock(aCtx, aJob, aMessages, true);
            oTab.TabID = "chat";
            oBag.Add(oTabs);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Body.AddRaw(oTabs.HTML);
            oRoot.AddRaw(oCard.HTML);

            TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
            oScript.Code = ChatPostScript("/my/chat/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture), "fsChat");
            oRoot.AddRaw(oScript.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, aJob.Reference, vBody, "jobs", vExtra, false);
        }

        public string BuildJobNewPage(TFieldPageCtx aCtx, TFieldCustomer[] aCustomers,
            TFieldSite[] aSites, TFieldTechnician[] aTechs, string aError)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Work", "Jobs", "New" },
                new string[] { "/", "/jobs", "" }));
            if (aError != "")
                oRoot.AddRaw(AlertHTML(aError, TsgcHTMLAlertStyle.asDanger));

            TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "fsNewJob";
            oForm.Action = "/jobs/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Create job";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;
            oForm.SubmitClass = "btn btn-fs";
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "title";
            oField.Label_ = "Job title";
            oField.Placeholder = "Annual boiler service";
            oField.Required = true;
            oField.ColSpan = 8;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "priority";
            oField.Label_ = "Priority";
            oField.ColSpan = 4;
            oField.Options.Add("normal=Normal");
            oField.Options.Add("low=Low");
            oField.Options.Add("high=High");
            oField.Options.Add("urgent=Urgent");
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "customer";
            oField.FieldID = "jobCustomer";
            oField.Label_ = "Customer";
            oField.Required = true;
            oField.ColSpan = 6;
            for (int vI = 0; vI < aCustomers.Length; vI++)
                oField.Options.Add(
                    aCustomers[vI].Id.ToString(CultureInfo.InvariantCulture) + "=" +
                    aCustomers[vI].Name);
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "site";
            oField.FieldID = "jobSite";
            oField.Label_ = "Site";
            oField.ColSpan = 6;
            for (int vI = 0; vI < aSites.Length; vI++)
                oField.Options.Add(
                    aSites[vI].Id.ToString(CultureInfo.InvariantCulture) + "=" +
                    aSites[vI].CustomerName + " - " + aSites[vI].Name);
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "technician";
            oField.Label_ = "Assign to";
            oField.ColSpan = 4;
            oField.Options.Add("0=Leave unassigned");
            for (int vI = 0; vI < aTechs.Length; vI++)
                oField.Options.Add(
                    aTechs[vI].Id.ToString(CultureInfo.InvariantCulture) + "=" +
                    aTechs[vI].DisplayName + " (" + aTechs[vI].Skills + ")");
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftDate;
            oField.Name = "date";
            oField.Label_ = "Date";
            oField.ColSpan = 4;
            oField.Value = FmtISODate(DateTime.Today.AddDays(1));
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftTime;
            oField.Name = "time";
            oField.Label_ = "Start time";
            oField.ColSpan = 4;
            oField.Value = "09:00";
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "hours";
            oField.Label_ = "Estimated hours";
            oField.ColSpan = 4;
            oField.Value = "2";
            oField.MinValue = "1";
            oField.MaxValue = "120";
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftTextArea;
            oField.Name = "description";
            oField.Label_ = "What needs doing";
            oField.TextAreaRows = 4;
            oField.ColSpan = 12;
            oBag.Add(oForm);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>New job</strong>");
            oCard.Body.AddRaw(oForm.HTML);
            oRoot.AddRaw(oCard.HTML);

            // Site list filtered by the chosen customer, entirely client-side: the
            // full customer -> site mapping is already on the page.
            string vSiteMap = "";
            for (int vI = 0; vI < aSites.Length; vI++)
                vSiteMap = vSiteMap +
                    aSites[vI].Id.ToString(CultureInfo.InvariantCulture) + ":" +
                    aSites[vI].CustomerId.ToString(CultureInfo.InvariantCulture) + ",";
            TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
            oScript.Code = "(function(){" + "var map=\"" + JSStr(vSiteMap) + "\";" +
                "var c=document.getElementById(\"jobCustomer\");" +
                "var s=document.getElementById(\"jobSite\");" + "if(!c||!s)return;" +
                "var pairs=map.split(\",\");var owner={};" +
                "for(var i=0;i<pairs.length;i++){var p=pairs[i].split(\":\");" +
                "if(p.length===2)owner[p[0]]=p[1];}" +
                "var all=[];for(var j=0;j<s.options.length;j++)all.push(s.options[j]);" +
                "function refresh(){var cur=c.value;" +
                "while(s.options.length)s.remove(0);" +
                "for(var k=0;k<all.length;k++){" +
                "if(!cur||owner[all[k].value]===cur)s.add(all[k].cloneNode(true));}" +
                "if(!s.options.length){for(var n=0;n<all.length;n++)" +
                "s.add(all[n].cloneNode(true));}}" +
                "c.addEventListener(\"change\",refresh);refresh();})();";
            oRoot.AddRaw(oScript.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "New job", vBody, "jobs", vExtra, false);
        }

        // ---------------------------------------------------------------------
        // Technician - mobile first. One decision per screen, 56px tap targets, a
        // sticky action bar at the bottom where a thumb reaches it.
        // ---------------------------------------------------------------------

        public string BuildMyJobsPage(TFieldPageCtx aCtx, TFieldTechnician aTech,
            TFieldJob[] aToday, TFieldJob[] aUpcoming, string aFlash)
        {
            int vDone = 0;
            for (int vI = 0; vI < aToday.Length; vI++)
                if (FieldConst.FieldStatusIsFinal(aToday[vI].Status))
                    vDone++;

            TFieldCSSBag oBag = new TFieldCSSBag();
            oBag.AddClass(typeof(TsgcHTMLComponent_EmptyState));
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "fs-my";

            if (aFlash != "")
                oWrap.AddRaw(AlertHTML(FlashMessage(aFlash),
                    TsgcHTMLAlertStyle.asSuccess));

            // --- who and where --- //
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-3";
            oCard.BodyClass = "d-flex align-items-center gap-3";
            oCard.Body.AddRaw(AvatarHTML(aTech.AvatarInitials,
                TsgcHTMLAvatarSize.asLarge, TechColor(aTech.Id)));
            oCard.Body.AddRaw("<div><div class=\"fw-semibold fs-5\">" +
                HtmlEsc(aTech.DisplayName) + "</div><div class=\"text-muted small\">" +
                HtmlEsc(aTech.Skills) + "</div><div class=\"mt-1\">" +
                PresenceBadge(aTech.Presence) + "</div></div>");
            oWrap.AddRaw(oCard.HTML);

            TsgcHTMLComponent_ProgressBar oProgress =
                new TsgcHTMLComponent_ProgressBar();
            oProgress.ProgressBarID = "fsMyProgress";
            if (aToday.Length > 0)
                oProgress.Value = (int)Math.Round((double)vDone * 100 / aToday.Length,
                    MidpointRounding.AwayFromZero);
            else
                oProgress.Value = 0;
            oProgress.Max = 100;
            oProgress.ShowLabel = true;
            oProgress.LabelFormat = vDone.ToString(CultureInfo.InvariantCulture) +
                " of " + aToday.Length.ToString(CultureInfo.InvariantCulture) +
                " done today";
            oProgress.CSSHeight = "26px";
            oProgress.ColorStyle = TsgcHTMLColor.hcSuccess;
            oWrap.AddRaw("<div class=\"mb-3\">" + oProgress.HTML + "</div>");

            oWrap.AddRaw("<h5 class=\"mb-2\">Today, " +
                HtmlEsc(FmtDateEN("dddd d MMMM", DateTime.Today)) + "</h5>");

            if (aToday.Length == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "Nothing booked for today";
                oEmpty.Description =
                    "Dispatch will push new work straight to this screen.";
                oEmpty.Bordered = true;
                oWrap.AddRaw(oEmpty.HTML);
            }
            else
                for (int vI = 0; vI < aToday.Length; vI++)
                    oWrap.AddRaw("<a class=\"text-decoration-none text-reset\" href=\"/my/" +
                        aToday[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\"><div class=\"card mb-2\">" +
                        "<div class=\"card-body py-3\">" +
                        "<div class=\"d-flex align-items-center gap-2 mb-1\">" +
                        "<span class=\"fw-bold fs-5\">" +
                        HtmlEsc(FmtTime(aToday[vI].ScheduledStart)) + "</span>" +
                        StatusBadge(aToday[vI].Status) +
                        PriorityBadge(aToday[vI].Priority) + "</div>" +
                        "<div class=\"fw-semibold\">" + HtmlEsc(aToday[vI].Title) +
                        "</div>" + "<div class=\"text-muted\">" +
                        HtmlEsc(aToday[vI].CustomerName) + "</div>" +
                        "<div class=\"text-muted small\">" +
                        HtmlEsc(aToday[vI].SiteName) + " - " +
                        HtmlEsc(aToday[vI].SiteAddress) + "</div>" +
                        "</div></div></a>");

            // --- the rest of the week, folded away --- //
            string vUpcoming = "";
            if (aUpcoming.Length == 0)
                vUpcoming = "<div class=\"text-muted small\">Nothing else booked.</div>";
            else
                for (int vI = 0; vI < aUpcoming.Length; vI++)
                    vUpcoming = vUpcoming +
                        "<a class=\"d-block text-decoration-none text-reset border-bottom " +
                        "py-2\" href=\"/my/" +
                        aUpcoming[vI].Id.ToString(CultureInfo.InvariantCulture) + "\">" +
                        "<div class=\"d-flex align-items-center gap-2\">" +
                        "<span class=\"fw-semibold\">" +
                        HtmlEsc(FmtDateEN("ddd d", aUpcoming[vI].ScheduledStart)) +
                        " " + HtmlEsc(FmtTime(aUpcoming[vI].ScheduledStart)) + "</span>" +
                        StatusBadge(aUpcoming[vI].Status) + "</div>" +
                        "<div>" + HtmlEsc(aUpcoming[vI].Title) + "</div>" +
                        "<div class=\"text-muted small\">" +
                        HtmlEsc(aUpcoming[vI].CustomerName) + "</div></a>";

            // The node-layer accordion, not TsgcHTMLComponent_Accordion: the
            // component HTML-encodes its Content (correct for text, fatal for a
            // pane of markup), and these panes carry rendered components.
            TsgcHTMLAccordion oAcc = new TsgcHTMLAccordion("fsMyUpcoming");
            oAcc.AddItem("Coming up (" +
                aUpcoming.Length.ToString(CultureInfo.InvariantCulture) + ")",
                false).Body.AddRaw(vUpcoming);
            oWrap.AddRaw("<div class=\"mt-3\">" + oAcc.HTML + "</div>");

            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "My jobs", vBody, "my", vExtra, true);
        }

        public string BuildMyJobPage(TFieldPageCtx aCtx, TFieldJob aJob,
            TFieldChecklistItem[] aChecklist, TFieldJobPart[] aParts,
            TFieldJobPhoto[] aPhotos, TFieldSignature aSig, bool aHasSig,
            string aFlash, string aError)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            oBag.AddClass(typeof(TsgcHTMLComponent_Stepper));
            oBag.AddClass(typeof(TsgcHTMLComponent_EmptyState));

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "fs-my";

            // A snackbar, not a banner: on a phone the confirmation should say its
            // piece at the top and get out of the way, without pushing the job card
            // down the screen.
            if (aFlash != "")
            {
                TsgcHTMLComponent_Snackbar oSnack = new TsgcHTMLComponent_Snackbar();
                oSnack.SnackbarID = "fsMySnack";
                oSnack.Message = FlashMessage(aFlash);
                oSnack.Color = TsgcHTMLColor.hcSuccess;
                oSnack.Position = TsgcHTMLSnackbarPosition.sbTop;
                oSnack.AutoHide = true;
                oSnack.Delay = 3500;
                oBag.Add(oSnack);
                oWrap.AddRaw(oSnack.HTML);
            }
            // An error is NOT a snackbar: it has to stay on screen until it is read.
            if (aError != "")
                oWrap.AddRaw(AlertHTML(aError, TsgcHTMLAlertStyle.asDanger));

            oWrap.AddRaw("<a class=\"btn btn-sm btn-outline-secondary mb-2\" " +
                "href=\"/my\">&laquo; Today</a>");

            // --- the one thing that matters: where, when, what --- //
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-3";
            oCard.Body.AddRaw("<div class=\"d-flex align-items-center gap-2 mb-2\">" +
                StatusBadge(aJob.Status) + PriorityBadge(aJob.Priority) +
                "<span class=\"fs-mono text-muted ms-auto\">" +
                HtmlEsc(aJob.Reference) + "</span></div>");
            oCard.Body.AddRaw("<div class=\"fs-4 fw-bold mb-1\">" +
                HtmlEsc(aJob.Title) + "</div>");
            oCard.Body.AddRaw("<div class=\"fs-6\">" +
                HtmlEsc(aJob.CustomerName) + "</div>");
            oCard.Body.AddRaw("<div class=\"text-muted\">" +
                HtmlEsc(aJob.SiteName) + "<br>" + HtmlEsc(aJob.SiteAddress) +
                "</div>");
            oCard.Body.AddRaw("<div class=\"mt-2 fs-5 fw-semibold\">" +
                HtmlEsc(FmtTime(aJob.ScheduledStart)) + " - " +
                HtmlEsc(FmtTime(aJob.ScheduledEnd)) + "</div>");
            if (aJob.AssetName != "")
                oCard.Body.AddRaw("<div class=\"mt-2\">" +
                    TsgcHTMLComponent_Chip.Build(aJob.AssetName,
                    TsgcHTMLBadgeStyle.bgSecondary, false) + "</div>");
            if (aJob.Description != "")
                oCard.Body.AddRaw("<div class=\"mt-2 small\">" +
                    HtmlEscNl(aJob.Description) + "</div>");
            oCard.Footer.AddRaw("<a class=\"btn btn-outline-secondary w-100 fs-tap " +
                "justify-content-center\" href=\"/my/chat/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture) +
                "\">Message dispatch</a>");
            oWrap.AddRaw(oCard.HTML);

            oWrap.AddRaw("<div class=\"card mb-3\"><div class=\"card-body py-3\">" +
                BuildStatusStepper(aJob.Status) + "</div></div>");

            // --- parts (scan or type) --- //
            string vPartsHTML = BuildPartsBlock(aParts);
            TsgcHTMLComponent_CameraScanner oScanner =
                new TsgcHTMLComponent_CameraScanner();
            oScanner.ScannerID = "fsScanner";
            oScanner.Mode = TsgcHTMLScannerMode.smBoth;
            oScanner.Formats = "qr_code,ean_13,code_128,code_39";
            oScanner.FacingMode = TsgcHTMLScannerFacing.sfEnvironment;
            oScanner.CSSHeight = "260px";
            oScanner.ContinuousScan = false;
            oScanner.ShowManualEntry = true;
            oScanner.ManualEntryPlaceholder = "Type the SKU, e.g. FLT-1042";
            oScanner.ShowCaptureButton = false;
            oScanner.ShowTorchButton = true;
            oScanner.StartCaption = "Scan a part";
            oScanner.StopCaption = "Stop the camera";
            oScanner.FieldName = "scan";
            oScanner.Action = "/my/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture) + "/part";
            oBag.Add(oScanner);
            vPartsHTML = vPartsHTML + "<div class=\"mt-3\">" + oScanner.HTML +
                "</div>";
            // Typed fallback: works with no camera, no permission and a torn label,
            // and it is the path curl exercises.
            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/my/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture) + "/part";
            oForm.CSSClass = "row g-2 align-items-end mt-2";
            TsgcHTMLField oField = new TsgcHTMLField(TsgcHTMLInputType.itText, "scan");
            oField.ColClass = "col-7";
            oField.FieldID = "fsPartSku";
            oField.Label_ = "Part SKU";
            oField.Placeholder = "FLT-1042";
            oForm.Add(oField);
            oField = new TsgcHTMLField(TsgcHTMLInputType.itNumber, "qty");
            oField.ColClass = "col-5";
            oField.FieldID = "fsPartQty";
            oField.Label_ = "Qty";
            oField.Value = "1";
            oForm.Add(oField);
            TsgcHTMLButton oBtn = new TsgcHTMLButton("Add part",
                TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100 fs-tap justify-content-center";
            oForm.AddRaw("<div class=\"col-12\">" + oBtn.HTML + "</div>");
            vPartsHTML = vPartsHTML + oForm.HTML;

            // --- photos --- //
            string vPhotoHTML = BuildPhotoStrip(aJob.Id, aPhotos);
            TsgcHTMLComponent_FileUpload oUpload = new TsgcHTMLComponent_FileUpload();
            oUpload.UploadID = "fsMyPhoto";
            oUpload.Action = "/my/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture) + "/photo";
            oUpload.Accept = "image/png,image/jpeg";
            oUpload.InputName = "photo";
            oUpload.Title = "Take or attach a photo";
            oUpload.Subtitle = "PNG or JPEG, up to 10 MB";
            oUpload.ButtonText = "Camera or gallery";
            oUpload.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oBag.Add(oUpload);
            vPhotoHTML = vPhotoHTML + "<div class=\"mt-3\">" + oUpload.HTML +
                "</div>";

            // --- signature --- //
            string vSigHTML;
            if (aHasSig)
                vSigHTML = "<div class=\"text-muted small mb-2\">Signed by " +
                    HtmlEsc(aSig.SignerName) + " on " +
                    HtmlEsc(FmtDateTime(aSig.SignedAt)) + "</div>" +
                    "<img class=\"fs-sig\" alt=\"Customer signature\" src=\"" +
                    HtmlEsc(aSig.SignaturePng) + "\">";
            else
            {
                oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/my/" +
                    aJob.Id.ToString(CultureInfo.InvariantCulture) + "/sign";
                oForm.CSSClass = "m-0";
                oField = new TsgcHTMLField(TsgcHTMLInputType.itText, "signer");
                oField.ColClass = "mb-2";
                oField.FieldID = "fsSigner";
                oField.Label_ = "Signed by";
                oField.Placeholder = "Customer name";
                oField.Required = true;
                oForm.Add(oField);
                TsgcHTMLComponent_SignaturePad oPad =
                    new TsgcHTMLComponent_SignaturePad();
                oPad.PadID = "fsSigPad";
                oPad.Width = 360;
                oPad.Height = 190;
                oPad.PenWidth = 3;
                oPad.FieldName = "signature";
                oPad.ShowClear = true;
                oPad.ShowUndo = true;
                // No UploadURL: the pad's hidden input lives inside this form, so
                // the signature and the signer name post together in one request.
                oBag.Add(oPad);
                oForm.AddRaw(oPad.HTML);
                oBtn = new TsgcHTMLButton("Save signature",
                    TsgcHTMLButtonStyle.bsPrimary);
                oBtn.ButtonType = "submit";
                oBtn.CSSClass = "w-100 fs-tap justify-content-center mt-2 btn-fs";
                oForm.Add(oBtn);
                vSigHTML = "<div class=\"text-muted small mb-2\">Ask the customer to " +
                    "sign with a finger.</div>" + oForm.HTML;
            }

            // --- everything else, folded --- //
            // Node-layer accordion: these panes are rendered components, and the
            // component-layer Accordion HTML-encodes its Content.
            TsgcHTMLAccordion oAcc = new TsgcHTMLAccordion("fsMyAcc");
            oAcc.AddItem("Checklist", true).Body.AddRaw(
                BuildChecklistBlock(aChecklist, aJob.Id, true,
                "/my/" + aJob.Id.ToString(CultureInfo.InvariantCulture) + "/check"));
            oAcc.AddItem("Parts used", false).Body.AddRaw(vPartsHTML);
            oAcc.AddItem("Photos", false).Body.AddRaw(vPhotoHTML);
            oAcc.AddItem("Customer sign-off", false).Body.AddRaw(vSigHTML);
            oWrap.AddRaw(oAcc.HTML);

            // --- sticky action bar: the single next decision --- //
            string[] vNext = FieldConst.FieldNextStatuses(aJob.Status);
            TsgcHTMLContainer oBar = new TsgcHTMLContainer("div");
            oBar.CSSClass = "fs-actionbar";
            if (vNext.Length == 0)
                oBar.AddRaw("<div class=\"text-center text-muted py-2\">This job is " +
                    HtmlEsc(FieldConst.FieldStatusLabel(aJob.Status).ToLowerInvariant()) +
                    ".</div>");
            else
            {
                for (int vI = 0; vI < vNext.Length; vI++)
                {
                    if (string.Equals(vNext[vI], FieldConst.CS_JOB_CANCELLED,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    oForm = new TsgcHTMLForm();
                    oForm.Method = "POST";
                    oForm.CSSClass = "d-grid mb-1";
                    string vCaption;
                    TsgcHTMLButtonStyle vStyle;
                    if (string.Equals(vNext[vI], FieldConst.CS_JOB_ENROUTE,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        oForm.Action = "/my/" +
                            aJob.Id.ToString(CultureInfo.InvariantCulture) + "/enroute";
                        vCaption = "I am on my way";
                        vStyle = TsgcHTMLButtonStyle.bsPrimary;
                    }
                    else if (string.Equals(vNext[vI], FieldConst.CS_JOB_ONSITE,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        oForm.Action = "/my/" +
                            aJob.Id.ToString(CultureInfo.InvariantCulture) + "/onsite";
                        vCaption = "I have arrived";
                        vStyle = TsgcHTMLButtonStyle.bsWarning;
                    }
                    else if (string.Equals(vNext[vI], FieldConst.CS_JOB_COMPLETE,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        oForm.Action = "/my/" +
                            aJob.Id.ToString(CultureInfo.InvariantCulture) + "/complete";
                        vCaption = "Job complete";
                        vStyle = TsgcHTMLButtonStyle.bsSuccess;
                    }
                    else
                        continue;
                    oBtn = new TsgcHTMLButton(vCaption, vStyle);
                    oBtn.ButtonType = "submit";
                    oBtn.CSSClass = "btn-lg";
                    oForm.Add(oBtn);
                    oBar.AddRaw(oForm.HTML);
                }
            }
            oWrap.Add(oBar);

            // The scanner posts on its own; reload so the parts list and the stock
            // figure both reflect it. If the post failed the page says so on the
            // way back in, and nothing typed is lost because the manual form is
            // still filled in.
            TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
            oScript.Code = "(function(){" +
                "document.addEventListener(\"sgcCameraScanner:scan\",function(){" +
                "setTimeout(function(){location.reload();},700);});})();";
            oWrap.AddRaw(oScript.HTML);

            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, aJob.Reference, vBody, "my", vExtra, true);
        }

        public string BuildMyChatPage(TFieldPageCtx aCtx, TFieldJob aJob,
            TFieldMessage[] aMessages)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            oBag.AddClass(typeof(TsgcHTMLComponent_ChatBox));
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "fs-my";
            oWrap.AddRaw("<a class=\"btn btn-sm btn-outline-secondary mb-2\" " +
                "href=\"/my/" + aJob.Id.ToString(CultureInfo.InvariantCulture) +
                "\">&laquo; Back to the job</a>");
            oWrap.AddRaw("<div class=\"mb-2\"><span class=\"fw-semibold\">" +
                HtmlEsc(aJob.Title) + "</span> <span class=\"text-muted fs-mono\">" +
                HtmlEsc(aJob.Reference) + "</span></div>");
            oWrap.AddRaw("<div id=\"fs-chat-status\"></div>");
            oWrap.AddRaw(BuildChatBlock(aCtx, aJob, aMessages, false));
            TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
            oScript.Code = ChatPostScript("/my/chat/" +
                aJob.Id.ToString(CultureInfo.InvariantCulture), "fsChat");
            oWrap.AddRaw(oScript.HTML);
            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Chat", vBody, "my", vExtra, true);
        }

        // ---------------------------------------------------------------------
        // Customer portal - reachable without a session, but ONLY through the job's
        // own unguessable reference, and it shows nothing beyond that job's progress.
        // ---------------------------------------------------------------------

        // The public portal has no index and no search: you arrive with a reference
        // somebody handed you, or you do not arrive at all.
        public string BuildTrackLookupPage(string aTheme, bool aNotFound)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container py-5";
            oWrap.Style = "max-width:560px;";
            oWrap.AddRaw("<div class=\"text-center mb-4\">" + CS_LOGO_SVG + "</div>");
            if (aNotFound)
                oWrap.AddRaw(AlertHTML("No job carries that reference. Check the " +
                    "letters and digits on your booking confirmation.",
                    TsgcHTMLAlertStyle.asWarning));

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Track your job</strong>");
            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/track";
            oForm.CSSClass = "m-0";
            TsgcHTMLField oField = new TsgcHTMLField(TsgcHTMLInputType.itText,
                "reference");
            oField.ColClass = "mb-3";
            oField.FieldID = "trackRef";
            oField.Label_ = "Job reference";
            oField.Placeholder = "FS-2026-0a1b2c3d4e";
            oField.Required = true;
            oField.Autocomplete = "off";
            oForm.Add(oField);
            TsgcHTMLButton oBtn = new TsgcHTMLButton("Show my job",
                TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100 btn-fs fs-tap justify-content-center";
            oForm.Add(oBtn);
            oCard.Body.AddRaw(oForm.HTML);
            oCard.Footer.AddRaw("<span class=\"text-muted small\">The reference is " +
                "the only key to this page. It opens one job and shows its status, " +
                "nothing else.</span>");
            oWrap.AddRaw(oCard.HTML);

            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return WrapTemplate("Track your job - sgcField", vBody, aTheme, vExtra,
                false);
        }

        public string BuildTrackPage(TFieldJob aJob, TFieldJobEvent[] aEvents,
            int aChecklistDone, int aChecklistTotal, bool aApproved, string aTheme,
            string aFlash)
        {
            // First name only. The tracking page is public-ish, so it carries the least
            // it can and still be useful.
            string vTech = (aJob.TechnicianName ?? "").Trim();
            int vP = vTech.IndexOf(' ') + 1;
            if (vP > 1)
                vTech = vTech.Substring(0, vP - 1);
            if (vTech == "")
                vTech = "Not assigned yet";

            TFieldCSSBag oBag = new TFieldCSSBag();
            oBag.AddClass(typeof(TsgcHTMLComponent_Stepper));
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container py-4";
            oWrap.Style = "max-width:720px;";
            oWrap.AddRaw("<div class=\"text-center mb-4\">" + CS_LOGO_SVG + "</div>");

            if (aFlash != "")
                oWrap.AddRaw(AlertHTML(FlashMessage(aFlash),
                    TsgcHTMLAlertStyle.asSuccess));

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<div class=\"d-flex align-items-center gap-2\">" +
                "<strong>" + HtmlEsc(aJob.Title) + "</strong>" +
                StatusBadge(aJob.Status) + "<span class=\"ms-auto fs-mono " +
                "text-muted small\">" + HtmlEsc(aJob.Reference) + "</span></div>");
            oCard.Body.AddRaw(BuildStatusStepper(aJob.Status));
            oCard.Body.AddRaw("<hr class=\"my-3\">");
            oCard.Body.AddRaw("<div class=\"row g-3\">" +
                "<div class=\"col-6\"><div class=\"text-muted small\">Site</div><div>" +
                HtmlEsc(aJob.SiteName) + "</div></div>" +
                "<div class=\"col-6\"><div class=\"text-muted small\">Engineer</div>" +
                "<div>" + HtmlEsc(vTech) + "</div></div>" +
                "<div class=\"col-6\"><div class=\"text-muted small\">Booked for</div>" +
                "<div>" + HtmlEsc(FmtDateTime(aJob.ScheduledStart)) + "</div></div>" +
                "<div class=\"col-6\"><div class=\"text-muted small\">Expected end" +
                "</div><div>" + HtmlEsc(FmtTime(aJob.ScheduledEnd)) +
                "</div></div></div>");

            TsgcHTMLComponent_ProgressBar oProgress =
                new TsgcHTMLComponent_ProgressBar();
            oProgress.ProgressBarID = "fsTrackProgress";
            if (aChecklistTotal > 0)
                oProgress.Value = (int)Math.Round(
                    (double)aChecklistDone * 100 / aChecklistTotal,
                    MidpointRounding.AwayFromZero);
            else
                oProgress.Value = 0;
            oProgress.Max = 100;
            oProgress.ShowLabel = true;
            oProgress.LabelFormat =
                aChecklistDone.ToString(CultureInfo.InvariantCulture) + " of " +
                aChecklistTotal.ToString(CultureInfo.InvariantCulture) +
                " work steps done";
            oProgress.CSSHeight = "24px";
            oProgress.ColorStyle = TsgcHTMLColor.hcSuccess;
            oCard.Body.AddRaw("<div class=\"mt-3\">" + oProgress.HTML + "</div>");
            oWrap.AddRaw(oCard.HTML);

            // Progress events only. Nothing internal: no prices, no notes, no
            // photos, no other job of this customer.
            TsgcHTMLComponent_Timeline oTl = new TsgcHTMLComponent_Timeline();
            oTl.TimelineID = "fsTrackTimeline";
            oTl.DotSize = 14;
            for (int vI = 0; vI < aEvents.Length; vI++)
            {
                string vKind = (aEvents[vI].Kind ?? "").ToLowerInvariant();
                if ((vKind != "created") && (vKind != "assigned") &&
                    (vKind != "enroute") && (vKind != "onsite") &&
                    (vKind != "completed") && (vKind != "cancelled") &&
                    (vKind != "approved"))
                    continue;
                TsgcHTMLTimelineItem oItem = oTl.Items.Add();
                oItem.Title = FieldConst.FieldStatusLabel(vKind);
                if (vKind == "created")
                    oItem.Title = "Booked";
                else if (vKind == "assigned")
                    oItem.Title = "Engineer assigned";
                else if (vKind == "approved")
                    oItem.Title = "Quote approved";
                oItem.Timestamp = FmtDateTime(aEvents[vI].CreatedAt);
                oItem.ColorStyle = TsgcHTMLColor.hcPrimary;
            }
            oBag.Add(oTl);
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Progress</strong>");
            oCard.Body.AddRaw(oTl.HTML);
            oWrap.AddRaw(oCard.HTML);

            // Approve the quote / rate the visit.
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Your say</strong>");
            if (aApproved)
                oCard.Body.AddRaw("<div class=\"text-success\">The quote for this " +
                    "visit is approved. Thank you.</div>");
            else
            {
                TsgcHTMLForm oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/track/" + HtmlEsc(aJob.Reference) + "/approve";
                oForm.CSSClass = "d-grid mb-3";
                TsgcHTMLButton oBtn = new TsgcHTMLButton("Approve the quote",
                    TsgcHTMLButtonStyle.bsPrimary);
                oBtn.ButtonType = "submit";
                oBtn.CSSClass = "btn-fs fs-tap justify-content-center";
                oForm.Add(oBtn);
                oCard.Body.AddRaw(oForm.HTML);
            }

            if (string.Equals(aJob.Status, FieldConst.CS_JOB_COMPLETE,
                StringComparison.OrdinalIgnoreCase))
            {
                TsgcHTMLForm oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/track/" + HtmlEsc(aJob.Reference) + "/approve";
                oForm.AddHidden("action", "rate");
                TsgcHTMLComponent_Rating oRating = new TsgcHTMLComponent_Rating();
                oRating.RatingID = "fsTrackRate";
                oRating.MaxValue = 5;
                oRating.Value = 5;
                oRating.ReadOnly = false;
                oRating.InputName = "rating";
                oRating.Size = "2rem";
                oBag.Add(oRating);
                oForm.AddRaw("<div class=\"text-muted small mb-1\">How did the " +
                    "visit go?</div>" + oRating.HTML);
                TsgcHTMLButton oBtn = new TsgcHTMLButton("Send my rating",
                    TsgcHTMLButtonStyle.bsOutlineSecondary);
                oBtn.ButtonType = "submit";
                oBtn.CSSClass = "mt-2";
                oForm.Add(oBtn);
                oCard.Body.AddRaw(oForm.HTML);
            }
            oWrap.AddRaw(oCard.HTML);

            oWrap.AddRaw("<div class=\"alert alert-secondary mt-4 small\">" +
                "This page is reached with the job reference only. It shows the " +
                "status of this one job and nothing else: no other job, no contact " +
                "details, no prices and no internal notes. There is no session and " +
                "no way to browse from here.</div>");

            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return WrapTemplate("Track " + aJob.Reference + " - sgcField", vBody,
                aTheme, vExtra, false);
        }

        // ---------------------------------------------------------------------
        // Customers, sites, assets, parts
        // ---------------------------------------------------------------------

        public string BuildCustomerListPage(TFieldPageCtx aCtx,
            TFieldCustomer[] aCustomers, string aSearch)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Data", "Customers" },
                new string[] { "/", "" }));

            if (aCustomers.Length == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No customers match that search";
                oEmpty.Description = "Clear the search to see the whole book.";
                oEmpty.ActionCaption = "Clear";
                oEmpty.ActionHref = "/customers";
                oEmpty.Bordered = true;
                oBag.Add(oEmpty);
                oRoot.AddRaw(oEmpty.HTML);
            }
            else
            {
                TFieldCellLinker oLinker = new TFieldCellLinker();
                oLinker.Hrefs = new string[aCustomers.Length];
                oLinker.LinkColumn = 0;
                for (int vI = 0; vI < aCustomers.Length; vI++)
                    oLinker.Hrefs[vI] = "/customers/" +
                        aCustomers[vI].Id.ToString(CultureInfo.InvariantCulture);

                TsgcHTMLComponent_DataTable oDT = new TsgcHTMLComponent_DataTable();
                oDT.TableID = "fsCustomers";
                oDT.Title = aCustomers.Length.ToString(CultureInfo.InvariantCulture) +
                    " customer(s)";
                oDT.ShowSearch = true;
                oDT.SearchAction = "/customers";
                oDT.SearchPlaceholder = "Name, city or contact";
                oDT.ShowRowCount = true;
                oDT.ShowPageSize = false;
                oDT.Grid.Striped = true;
                oDT.Grid.Hover = true;
                oDT.Grid.ShowSort = true;
                oDT.Grid.ShowFilter = true;
                oDT.Grid.HeaderClass = "table-light";
                oDT.Grid.OnGetCellHTML += oLinker.GetCellHTML;
                TsgcHTMLGridColumn oCol = oDT.Grid.Columns.Add();
                oCol.Name = "name";
                oCol.Title = "Customer";
                oCol = oDT.Grid.Columns.Add();
                oCol.Name = "contact";
                oCol.Title = "Contact";
                oCol = oDT.Grid.Columns.Add();
                oCol.Name = "city";
                oCol.Title = "City";
                oCol = oDT.Grid.Columns.Add();
                oCol.Name = "phone";
                oCol.Title = "Phone";
                oCol = oDT.Grid.Columns.Add();
                oCol.Name = "sites";
                oCol.Title = "Sites";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                oCol = oDT.Grid.Columns.Add();
                oCol.Name = "jobs";
                oCol.Title = "Jobs";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                for (int vI = 0; vI < aCustomers.Length; vI++)
                    oDT.Grid.AddRow(aCustomers[vI].Name, aCustomers[vI].Contact,
                        aCustomers[vI].City, aCustomers[vI].Phone,
                        aCustomers[vI].SiteCount.ToString(CultureInfo.InvariantCulture),
                        aCustomers[vI].JobCount.ToString(CultureInfo.InvariantCulture));
                oDT.Pagination.TotalItems = aCustomers.Length;
                oDT.Pagination.PageSize = aCustomers.Length;
                oDT.Pagination.TotalPages = 1;
                oBag.Add(oDT);
                oBag.Add(oDT.Grid);
                oRoot.AddRaw(oDT.HTML);
            }
            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Customers", vBody, "customers", vExtra,
                false);
        }

        public string BuildCustomerDetailPage(TFieldPageCtx aCtx,
            TFieldCustomer aCustomer, TFieldSite[] aSites, TFieldJob[] aJobs,
            string aFlash)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(
                new string[] { "Data", "Customers", aCustomer.Name },
                new string[] { "/", "/customers", "" }));
            if (aFlash != "")
                oRoot.AddRaw(AlertHTML(FlashMessage(aFlash),
                    TsgcHTMLAlertStyle.asSuccess));

            // --- left: identity + edit form --- //
            TsgcHTMLDescriptionList oDL = new TsgcHTMLDescriptionList();
            oDL.TermClass = "col-5 text-muted";
            oDL.DescClass = "col-7";
            oDL.AddItem("Contact", aCustomer.Contact);
            oDL.AddItem("Email", aCustomer.Email);
            oDL.AddItem("Phone", aCustomer.Phone);
            oDL.AddItem("Address", aCustomer.Address);
            oDL.AddItem("City", aCustomer.City);
            oDL.AddItem("Since", FmtDate(aCustomer.CreatedAt));
            string vLeft = oDL.HTML;

            TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "fsCustomerForm";
            oForm.Action = "/customers/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Save customer";
            oForm.SubmitClass = "btn btn-fs";
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftHidden;
            oField.Name = "id";
            oField.Value = aCustomer.Id.ToString(CultureInfo.InvariantCulture);
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "name";
            oField.Label_ = "Name";
            oField.Value = aCustomer.Name;
            oField.Required = true;
            oField.ColSpan = 12;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "contact";
            oField.Label_ = "Contact";
            oField.Value = aCustomer.Contact;
            oField.ColSpan = 6;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftEmail;
            oField.Name = "email";
            oField.Label_ = "Email";
            oField.Value = aCustomer.Email;
            oField.ColSpan = 6;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "phone";
            oField.Label_ = "Phone";
            oField.Value = aCustomer.Phone;
            oField.ColSpan = 6;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "city";
            oField.Label_ = "City";
            oField.Value = aCustomer.City;
            oField.ColSpan = 6;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "address";
            oField.Label_ = "Address";
            oField.Value = aCustomer.Address;
            oField.ColSpan = 12;
            oBag.Add(oForm);
            vLeft = vLeft + "<hr class=\"my-3\">" + oForm.HTML;

            // --- right: sites + recent jobs --- //
            TsgcHTMLListGroupNode oList = new TsgcHTMLListGroupNode();
            oList.CSSClass = "list-group-flush";
            for (int vI = 0; vI < aSites.Length; vI++)
            {
                TsgcHTMLListGroupNodeItem oItem = oList.AddItem(aSites[vI].Name +
                    " - " + aSites[vI].Address);
                oItem.Href = "/sites/" +
                    aSites[vI].Id.ToString(CultureInfo.InvariantCulture);
            }
            if (aSites.Length == 0)
                oList.AddItem("No sites on file");
            string vRight = oList.HTML;

            TsgcHTMLTable oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.TheadClass = "table-light";
            oTable.Responsive = true;
            oTable.AddColumn("Reference");
            oTable.AddColumn("Job");
            oTable.AddColumn("Status");
            oTable.AddColumn("Scheduled");
            for (int vI = 0; vI < aJobs.Length; vI++)
            {
                TsgcHTMLTableRow oTRow = oTable.AddRow();
                oTRow.Style = "cursor:pointer;";
                oTRow.OnClick = "window.location=" + QuotedStr("/jobs/" +
                    aJobs[vI].Id.ToString(CultureInfo.InvariantCulture));
                oTRow.AddCellText(aJobs[vI].Reference, "fs-mono");
                oTRow.AddCellText(aJobs[vI].Title);
                oTRow.AddCellRaw(StatusBadge(aJobs[vI].Status));
                oTRow.AddCellText(FmtDateTime(aJobs[vI].ScheduledStart));
            }
            if (aJobs.Length == 0)
                oTable.AddEmptyRow("No jobs yet.", 4);

            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            TsgcHTMLContainer oCol = AddCol(oRow, "col-lg-5");
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>" + HtmlEsc(aCustomer.Name) + "</strong>");
            oCard.Body.AddRaw(vLeft);
            oCol.AddRaw(oCard.HTML);

            oCol = AddCol(oRow, "col-lg-7");
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Sites</strong>");
            oCard.BodyClass = "p-0";
            oCard.Body.AddRaw(vRight);
            oCol.AddRaw(oCard.HTML);
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Recent jobs</strong>");
            oCard.BodyClass = "p-0";
            oCard.Body.AddRaw(oTable.HTML);
            oCol.AddRaw(oCard.HTML);
            oRoot.AddRaw(oRow.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, aCustomer.Name, vBody, "customers", vExtra,
                false);
        }

        public string BuildSiteDetailPage(TFieldPageCtx aCtx, TFieldSite aSite,
            TFieldAsset[] aAssets, TFieldJob[] aJobs)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Data", "Customers",
                aSite.CustomerName, aSite.Name },
                new string[] { "/", "/customers", "/customers/" +
                aSite.CustomerId.ToString(CultureInfo.InvariantCulture), "" }));

            TsgcHTMLDescriptionList oDL = new TsgcHTMLDescriptionList();
            oDL.TermClass = "col-4 text-muted";
            oDL.DescClass = "col-8";
            oDL.AddItem("Customer", aSite.CustomerName);
            oDL.AddItem("Address", aSite.Address);
            oDL.AddItem("Coordinates", JSNum(aSite.Lat, 5) + ", " +
                JSNum(aSite.Lng, 5));
            oDL.AddItem("Access notes", aSite.AccessNotes);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>" + HtmlEsc(aSite.Name) + "</strong>");
            oCard.Body.AddRaw(oDL.HTML);
            oRoot.AddRaw(oCard.HTML);

            // Asset hierarchy for this site, built from the parent_id column.
            TsgcHTMLComponent_TreeView oTree = new TsgcHTMLComponent_TreeView();
            oTree.TreeID = "fsSiteAssets";
            oTree.ShowLines = true;
            oTree.IndentSize = 22;
            for (int vI = 0; vI < aAssets.Length; vI++)
            {
                if (aAssets[vI].ParentId != 0)
                    continue;
                TsgcHTMLTreeNode oNode = oTree.Nodes.Add();
                oNode.Text = aAssets[vI].Name + "  (" + aAssets[vI].Model + ")";
                oNode.NodeID = aAssets[vI].Id.ToString(CultureInfo.InvariantCulture);
                oNode.Href = "/assets/" +
                    aAssets[vI].Id.ToString(CultureInfo.InvariantCulture);
                oNode.Expanded = true;
                for (int vJ = 0; vJ < aAssets.Length; vJ++)
                    if (aAssets[vJ].ParentId == aAssets[vI].Id)
                        oNode.Children.Add(aAssets[vJ].Name);
            }
            oBag.Add(oTree);
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Assets on site</strong>");
            if (aAssets.Length == 0)
                oCard.Body.AddRaw(TsgcHTMLComponent_EmptyState.Build(
                    "No assets recorded", "Add the plant this site runs to get a " +
                    "service history per machine."));
            else
                oCard.Body.AddRaw(oTree.HTML);
            oRoot.AddRaw(oCard.HTML);

            TsgcHTMLTable oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.TheadClass = "table-light";
            oTable.Responsive = true;
            oTable.AddColumn("Reference");
            oTable.AddColumn("Job");
            oTable.AddColumn("Technician");
            oTable.AddColumn("Status");
            oTable.AddColumn("Scheduled");
            for (int vI = 0; vI < aJobs.Length; vI++)
            {
                TsgcHTMLTableRow oTRow = oTable.AddRow();
                oTRow.Style = "cursor:pointer;";
                oTRow.OnClick = "window.location=" + QuotedStr("/jobs/" +
                    aJobs[vI].Id.ToString(CultureInfo.InvariantCulture));
                oTRow.AddCellText(aJobs[vI].Reference, "fs-mono");
                oTRow.AddCellText(aJobs[vI].Title);
                oTRow.AddCellText(aJobs[vI].TechnicianName);
                oTRow.AddCellRaw(StatusBadge(aJobs[vI].Status));
                oTRow.AddCellText(FmtDateTime(aJobs[vI].ScheduledStart));
            }
            if (aJobs.Length == 0)
                oTable.AddEmptyRow("No service history yet.", 5);
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Service history</strong>");
            oCard.BodyClass = "p-0";
            oCard.Body.AddRaw(oTable.HTML);
            oRoot.AddRaw(oCard.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, aSite.Name, vBody, "customers", vExtra, false);
        }

        public string BuildAssetListPage(TFieldPageCtx aCtx, DataTable aTreeData,
            string aSQL, string aSearch)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Data", "Assets" },
                new string[] { "/", "" }));

            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/assets";
            oForm.CSSClass = "row g-2 align-items-end mb-3";
            TsgcHTMLField oField = new TsgcHTMLField(TsgcHTMLInputType.itSearch, "q");
            oField.ColClass = "col-12 col-md-4";
            oField.FieldID = "assetSearch";
            oField.Label_ = "Search assets";
            oField.Placeholder = "Name, model or serial";
            oField.Value = aSearch;
            oField.InputCSSClass = "form-control form-control-sm";
            oForm.Add(oField);
            oForm.AddRaw("<div class=\"col-auto\"><button type=\"submit\" " +
                "class=\"btn btn-sm btn-fs\">Search</button></div>");
            oRoot.AddRaw(oForm.HTML);

            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";

            // TreeGrid: the whole asset table, nested by parent_id, straight from
            // the query via LoadFromDataSet.
            TsgcHTMLContainer oCol = AddCol(oRow, "col-xl-8");
            TsgcHTMLComponent_TreeGrid oTreeGrid = new TsgcHTMLComponent_TreeGrid();
            oTreeGrid.TreeGridID = "fsAssetTree";
            oTreeGrid.ExpandedByDefault = true;
            oTreeGrid.Striped = true;
            oTreeGrid.Hover = true;
            oTreeGrid.IndentPixels = 22;
            oTreeGrid.Sortable = true;
            if (aTreeData != null)
                oTreeGrid.LoadFromDataSet(aTreeData, "id", "parent_id");
            oBag.Add(oTreeGrid);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Asset hierarchy</strong>");
            oCard.BodyClass = "p-0";
            oCard.Body.AddRaw(oTreeGrid.HTML);
            oCol.AddRaw(oCard.HTML);

            // TreeView on the very same dataset, to show two components reading
            // one query with no API in between.
            oCol = AddCol(oRow, "col-xl-4");
            TsgcHTMLComponent_TreeView oTree = new TsgcHTMLComponent_TreeView();
            oTree.TreeID = "fsAssetTreeView";
            oTree.ShowLines = true;
            oTree.IndentSize = 18;
            if (aTreeData != null)
                oTree.LoadFromDataSet(aTreeData, "id", "parent_id", "name");
            oBag.Add(oTree);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Same query, TreeView</strong>");
            oCard.Body.AddRaw(oTree.HTML);
            oCol.AddRaw(oCard.HTML);

            oRoot.AddRaw(oRow.HTML);

            TsgcHTMLContainer oPre = new TsgcHTMLContainer("div");
            oPre.CSSClass = "fs-sql";
            oPre.AddText(aSQL);
            string vSQLHTML = oPre.HTML;
            TsgcHTMLAccordion oAcc = new TsgcHTMLAccordion("fsAssetSql");
            oAcc.AddItem("The one query both components were handed",
                false).Body.AddRaw(vSQLHTML);
            oRoot.AddRaw("<div class=\"mt-3\">" + oAcc.HTML + "</div>");

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Assets", vBody, "assets", vExtra, false);
        }

        public string BuildAssetDetailPage(TFieldPageCtx aCtx, TFieldAsset aAsset,
            TFieldAsset[] aChildren, TFieldJob[] aJobs)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(
                new string[] { "Data", "Assets", aAsset.Name },
                new string[] { "/", "/assets", "" }));

            TsgcHTMLDescriptionList oDL = new TsgcHTMLDescriptionList();
            oDL.TermClass = "col-4 text-muted";
            oDL.DescClass = "col-8";
            oDL.AddItem("Model", aAsset.Model);
            oDL.AddItem("Serial", aAsset.Serial);
            oDL.AddItem("Site", aAsset.SiteName);
            oDL.AddItem("Customer", aAsset.CustomerName);
            oDL.AddItem("Installed", FmtDate(aAsset.InstalledAt));
            oDL.AddItem("Warranty until", FmtDate(aAsset.WarrantyUntil));
            oDL.AddItem("Status", aAsset.Status);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<div class=\"d-flex align-items-center gap-2\">" +
                "<strong>" + HtmlEsc(aAsset.Name) + "</strong>" +
                TsgcHTMLComponent_Chip.Build(aAsset.Status,
                TsgcHTMLBadgeStyle.bgSecondary, false) + "</div>");
            oCard.Body.AddRaw(oDL.HTML);
            if (aAsset.SiteId > 0)
                oCard.Footer.AddRaw("<a class=\"btn btn-sm btn-outline-secondary\" " +
                    "href=\"/sites/" +
                    aAsset.SiteId.ToString(CultureInfo.InvariantCulture) +
                    "\">Open the site</a>");
            oRoot.AddRaw(oCard.HTML);

            if (aChildren.Length > 0)
            {
                TsgcHTMLListGroupNode oList = new TsgcHTMLListGroupNode();
                oList.CSSClass = "list-group-flush";
                for (int vI = 0; vI < aChildren.Length; vI++)
                {
                    TsgcHTMLListGroupNodeItem oItem = oList.AddItem(
                        aChildren[vI].Name + " - " + aChildren[vI].Model);
                    oItem.Href = "/assets/" +
                        aChildren[vI].Id.ToString(CultureInfo.InvariantCulture);
                }
                oCard = new TsgcHTMLCard();
                oCard.CSSClass = "mt-3";
                oCard.Header.AddRaw("<strong>Sub-assets</strong>");
                oCard.BodyClass = "p-0";
                oCard.Body.AddRaw(oList.HTML);
                oRoot.AddRaw(oCard.HTML);
            }

            TsgcHTMLComponent_Timeline oTl = new TsgcHTMLComponent_Timeline();
            oTl.TimelineID = "fsAssetHistory";
            for (int vI = 0; vI < aJobs.Length; vI++)
            {
                TsgcHTMLTimelineItem oItem = oTl.Items.Add();
                oItem.Title = aJobs[vI].Reference + " - " + aJobs[vI].Title;
                oItem.Content = FieldConst.FieldStatusLabel(aJobs[vI].Status) + " - " +
                    aJobs[vI].TechnicianName;
                oItem.Timestamp = FmtDateTime(aJobs[vI].ScheduledStart);
                oItem.ColorStyle = StatusColor(aJobs[vI].Status);
            }
            oBag.Add(oTl);
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Service history</strong>");
            if (aJobs.Length == 0)
                oCard.Body.AddRaw(TsgcHTMLComponent_EmptyState.Build(
                    "Never serviced", "No job has touched this asset yet."));
            else
                oCard.Body.AddRaw(oTl.HTML);
            oRoot.AddRaw(oCard.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, aAsset.Name, vBody, "assets", vExtra, false);
        }

        public string BuildPartsPage(TFieldPageCtx aCtx, TFieldPart[] aParts,
            string aSearch, string aFlash)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Data", "Parts" },
                new string[] { "/", "" }));
            if (aFlash != "")
                oRoot.AddRaw(AlertHTML(FlashMessage(aFlash),
                    TsgcHTMLAlertStyle.asSuccess));

            TsgcHTMLForm oSearchForm = new TsgcHTMLForm();
            oSearchForm.Method = "GET";
            oSearchForm.Action = "/parts";
            oSearchForm.CSSClass = "row g-2 align-items-end mb-3";
            TsgcHTMLField oField = new TsgcHTMLField(TsgcHTMLInputType.itSearch, "q");
            oField.ColClass = "col-12 col-md-4";
            oField.FieldID = "partSearch";
            oField.Label_ = "Search parts";
            oField.Placeholder = "SKU or name";
            oField.Value = aSearch;
            oField.InputCSSClass = "form-control form-control-sm";
            oSearchForm.Add(oField);
            oSearchForm.AddRaw("<div class=\"col-auto\"><button type=\"submit\" " +
                "class=\"btn btn-sm btn-fs\">Search</button></div>");
            oRoot.AddRaw(oSearchForm.HTML);

            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            TsgcHTMLContainer oCol = AddCol(oRow, "col-xl-8");

            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "fsPartsGrid";
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.ShowSort = true;
            oGrid.ShowFilter = true;
            oGrid.HeaderClass = "table-light";
            oGrid.EmptyText = "No parts.";
            TsgcHTMLGridColumn oGCol = oGrid.Columns.Add();
            oGCol.Name = "sku";
            oGCol.Title = "SKU";
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "name";
            oGCol.Title = "Part";
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "price";
            oGCol.Title = "Unit price";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "stock";
            oGCol.Title = "Stock";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            for (int vI = 0; vI < aParts.Length; vI++)
                oGrid.AddRow(aParts[vI].Sku, aParts[vI].Name,
                    FmtMoney(aParts[vI].UnitPrice),
                    aParts[vI].Stock.ToString(CultureInfo.InvariantCulture));
            oBag.Add(oGrid);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>" +
                aParts.Length.ToString(CultureInfo.InvariantCulture) +
                " part(s) in the catalogue</strong>");
            oCard.BodyClass = "p-0";
            oCard.Body.AddRaw(oGrid.HTML);
            oCol.AddRaw(oCard.HTML);

            oCol = AddCol(oRow, "col-xl-4");

            TsgcHTMLComponent_Slider oSlider = new TsgcHTMLComponent_Slider();
            oSlider.SliderID = "fsStockSlider";
            oSlider.FieldName = "threshold";
            oSlider.LabelText = "Highlight stock below";
            oSlider.Min = 0;
            oSlider.Max = 100;
            oSlider.Step = 5;
            oSlider.Value = 20;
            oSlider.ShowValue = true;
            oSlider.ShowMinMax = true;
            oSlider.ColorStyle = TsgcHTMLColor.hcWarning;
            oBag.Add(oSlider);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Low stock</strong>");
            oCard.Body.AddRaw(oSlider.HTML);
            oCard.Body.AddRaw("<div class=\"text-muted small mt-2\">Rows whose " +
                "stock is under the threshold turn red, live, with no " +
                "round trip.</div>");
            oCol.AddRaw(oCard.HTML);

            TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "fsPartForm";
            oForm.Action = "/parts/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Save part";
            oForm.SubmitClass = "btn btn-fs";
            TsgcHTMLFormField oFField = oForm.Fields.Add();
            oFField.FieldType = TsgcHTMLFieldType.ftHidden;
            oFField.Name = "id";
            oFField.Value = "0";
            oFField = oForm.Fields.Add();
            oFField.FieldType = TsgcHTMLFieldType.ftText;
            oFField.Name = "sku";
            oFField.Label_ = "SKU";
            oFField.Required = true;
            oFField.Placeholder = "FLT-2001";
            oFField.ColSpan = 12;
            oFField = oForm.Fields.Add();
            oFField.FieldType = TsgcHTMLFieldType.ftText;
            oFField.Name = "name";
            oFField.Label_ = "Name";
            oFField.Required = true;
            oFField.ColSpan = 12;
            oFField = oForm.Fields.Add();
            oFField.FieldType = TsgcHTMLFieldType.ftNumber;
            oFField.Name = "price";
            oFField.Label_ = "Unit price";
            oFField.Value = "0";
            oFField.ColSpan = 6;
            oFField = oForm.Fields.Add();
            oFField.FieldType = TsgcHTMLFieldType.ftNumber;
            oFField.Name = "stock";
            oFField.Label_ = "Stock";
            oFField.Value = "0";
            oFField.ColSpan = 6;
            oBag.Add(oForm);
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Add a part</strong>");
            oCard.Body.AddRaw(oForm.HTML);
            oCol.AddRaw(oCard.HTML);

            oRoot.AddRaw(oRow.HTML);

            TsgcHTMLScript oScript = new TsgcHTMLScript("", false);
            oScript.Code = "(function(){" +
                "var s=document.getElementById(\"fsStockSlider\");" +
                "var t=document.getElementById(\"fsPartsGrid\");" +
                "if(!s||!t)return;" + "function paint(){" +
                "var lim=parseInt(s.value,10)||0;" +
                "var rows=t.querySelectorAll(\"tbody tr\");" +
                "for(var i=0;i<rows.length;i++){" +
                "var c=rows[i].cells;if(!c||c.length<4)continue;" +
                "var n=parseInt((c[3].textContent||\"\").replace(/[^0-9-]/g,\"\"),10);" +
                "if(!isNaN(n)&&n<lim){rows[i].classList.add(\"table-danger\");}" +
                "else{rows[i].classList.remove(\"table-danger\");}}}" +
                "s.addEventListener(\"input\",paint);paint();})();";
            oRoot.AddRaw(oScript.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Parts", vBody, "parts", vExtra, false);
        }

        // ---------------------------------------------------------------------
        // Manager: reports, the SQL page, users, audit
        // ---------------------------------------------------------------------

        public string BuildReportsPage(TFieldPageCtx aCtx, TFieldReportStats aStats,
            TFieldTechStat[] aTechStats, TFieldPoint[] aSeries,
            TFieldPoint[] aStatusBreak, TFieldHeatCell[] aHeat,
            TFieldPoint[] aRevenue, DateTime aFrom, DateTime aTo)
        {
            double vSla;
            double vFtf;
            if ((aStats.SlaMet + aStats.SlaBreached) > 0)
                vSla = (double)aStats.SlaMet * 100 /
                    (aStats.SlaMet + aStats.SlaBreached);
            else
                vSla = 0;
            if ((aStats.FirstTimeFix + aStats.Revisits) > 0)
                vFtf = (double)aStats.FirstTimeFix * 100 /
                    (aStats.FirstTimeFix + aStats.Revisits);
            else
                vFtf = 0;

            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Manager", "Reports" },
                new string[] { "/", "" }));

            // --- window picker + exports --- //
            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/reports";
            oForm.CSSClass = "d-flex flex-wrap align-items-end gap-2 mb-3";
            TsgcHTMLComponent_DateRangePicker oRange =
                new TsgcHTMLComponent_DateRangePicker();
            oRange.FieldNameStart = "from";
            oRange.FieldNameEnd = "to";
            oRange.LabelStart = "From";
            oRange.LabelEnd = "To";
            oRange.StartValue = FmtISODate(aFrom);
            oRange.EndValue = FmtISODate(aTo);
            oRange.ShowPresets = true;
            oRange.Layout = TsgcHTMLDateRangeLayout.drlInline;
            oBag.Add(oRange);
            oForm.AddRaw(oRange.HTML);
            oForm.AddRaw("<button type=\"submit\" class=\"btn btn-sm btn-fs\">" +
                "Apply</button>");
            oForm.AddRaw("<a class=\"btn btn-sm btn-outline-secondary\" " +
                "href=\"/reports/sla.pdf?from=" + FmtISODate(aFrom) + "&to=" +
                FmtISODate(aTo) + "\">SLA report (PDF)</a>");
            oForm.AddRaw("<a class=\"btn btn-sm btn-outline-secondary\" " +
                "href=\"/reports/utilisation.xlsx?from=" + FmtISODate(aFrom) +
                "&to=" + FmtISODate(aTo) + "\">Utilisation (XLSX)</a>");
            oRoot.AddRaw(oForm.HTML);

            // --- headline tiles --- //
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-2 mb-3";
            TsgcHTMLContainer oCol = AddCol(oRow, "col-6 col-xl-2");
            oCol.AddRaw(TsgcHTMLComponent_StatCard.Build("Jobs in window",
                aStats.TotalJobs.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone, "",
                "fsRepTotal"));
            oCol = AddCol(oRow, "col-6 col-xl-2");
            oCol.AddRaw(TsgcHTMLComponent_StatCard.Build("Completed",
                aStats.CompletedJobs.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, "",
                "fsRepDone"));
            oCol = AddCol(oRow, "col-6 col-xl-2");
            oCol.AddRaw(TsgcHTMLComponent_StatCard.Build("Still open",
                aStats.OpenJobs.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scWarning, TsgcHTMLStatTrend.stNone, "",
                "fsRepOpen"));
            oCol = AddCol(oRow, "col-6 col-xl-2");
            oCol.AddRaw(TsgcHTMLComponent_StatCard.Build("Labour hours",
                FmtNum(aStats.LabourHours, 0), TsgcHTMLStatColor.scInfo,
                TsgcHTMLStatTrend.stNone, "", "fsRepHours"));
            oCol = AddCol(oRow, "col-6 col-xl-2");
            oCol.AddRaw(TsgcHTMLComponent_StatCard.Build("Parts revenue",
                FmtMoney(aStats.PartsRevenue), TsgcHTMLStatColor.scDark,
                TsgcHTMLStatTrend.stNone, "", "fsRepRev"));
            oCol = AddCol(oRow, "col-6 col-xl-2");
            oCol.AddRaw(TsgcHTMLComponent_StatCard.Build("Cancelled",
                aStats.CancelledJobs.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scSecondary, TsgcHTMLStatTrend.stNone, "",
                "fsRepCanc"));
            oRoot.AddRaw(oRow.HTML);

            // --- gauges + satisfaction --- //
            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";

            oCol = AddCol(oRow, "col-md-4");
            TsgcHTMLComponent_Gauge oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.GaugeID = "fsGaugeSla";
            oGauge.Title = "SLA attainment";
            oGauge.Value = vSla;
            oGauge.MinValue = 0;
            oGauge.MaxValue = 100;
            oGauge.Unit_ = "%";
            oGauge.ThresholdMid = 85;
            oGauge.ThresholdHigh = 95;
            oGauge.ColorLowStyle = TsgcHTMLColor.hcDanger;
            oGauge.ColorMidStyle = TsgcHTMLColor.hcWarning;
            oGauge.ColorHighStyle = TsgcHTMLColor.hcSuccess;
            oBag.Add(oGauge);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.BodyClass = "text-center";
            oCard.Body.AddRaw(oGauge.HTML);
            oCard.Body.AddRaw("<div class=\"text-muted small\">" +
                aStats.SlaMet.ToString(CultureInfo.InvariantCulture) + " met, " +
                aStats.SlaBreached.ToString(CultureInfo.InvariantCulture) +
                " breached</div>");
            oCol.AddRaw(oCard.HTML);

            oCol = AddCol(oRow, "col-md-4");
            oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.GaugeID = "fsGaugeFtf";
            oGauge.Title = "First-time fix";
            oGauge.Value = vFtf;
            oGauge.MinValue = 0;
            oGauge.MaxValue = 100;
            oGauge.Unit_ = "%";
            oGauge.ThresholdMid = 70;
            oGauge.ThresholdHigh = 85;
            oGauge.ColorLowStyle = TsgcHTMLColor.hcDanger;
            oGauge.ColorMidStyle = TsgcHTMLColor.hcWarning;
            oGauge.ColorHighStyle = TsgcHTMLColor.hcSuccess;
            oCard = new TsgcHTMLCard();
            oCard.BodyClass = "text-center";
            oCard.Body.AddRaw(oGauge.HTML);
            oCard.Body.AddRaw("<div class=\"text-muted small\">" +
                aStats.FirstTimeFix.ToString(CultureInfo.InvariantCulture) +
                " fixed first time, " +
                aStats.Revisits.ToString(CultureInfo.InvariantCulture) +
                " needed a revisit</div>");
            oCol.AddRaw(oCard.HTML);

            oCol = AddCol(oRow, "col-md-4");
            TsgcHTMLComponent_Rating oRating = new TsgcHTMLComponent_Rating();
            oRating.RatingID = "fsRepRating";
            oRating.Value = (int)Math.Round(aStats.AvgSatisfaction,
                MidpointRounding.AwayFromZero);
            oRating.MaxValue = 5;
            oRating.ReadOnly = true;
            oRating.ShowValue = false;
            oRating.Size = "2rem";
            oBag.Add(oRating);
            oCard = new TsgcHTMLCard();
            oCard.BodyClass = "text-center";
            oCard.Body.AddRaw("<div class=\"text-muted small mb-2\">" +
                "Customer satisfaction</div>");
            oCard.Body.AddRaw(oRating.HTML);
            oCard.Body.AddRaw("<div class=\"fs-4 fw-bold mt-2\">" +
                FmtNum(aStats.AvgSatisfaction, 2) + " / 5</div>");
            oCard.Body.AddRaw("<div class=\"text-muted small\">from " +
                aStats.RatedJobs.ToString(CultureInfo.InvariantCulture) +
                " rated visits</div>");
            oCol.AddRaw(oCard.HTML);
            oRoot.AddRaw(oRow.HTML);

            // --- created vs completed + status split --- //
            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";

            oCol = AddCol(oRow, "col-lg-8");
            double[] vCreated = new double[aSeries.Length];
            double[] vDone = new double[aSeries.Length];
            for (int vI = 0; vI < aSeries.Length; vI++)
            {
                vCreated[vI] = aSeries[vI].Value;
                vDone[vI] = aSeries[vI].Value2;
            }
            TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "fsChartFlow";
            oChart.ChartType = TsgcHTMLChartType.ctLine;
            oChart.CSSHeight = "280px";
            // AddLabel, not the Labels property: Labels is spliced into the
            // Chart.js config verbatim, so a bare 2026-08-17 arrives as
            // arithmetic and the axis reads -17. AddLabel JSON-quotes each one.
            for (int vI = 0; vI < aSeries.Length; vI++)
                oChart.AddLabel(aSeries[vI].Label_);
            oChart.ShowLegend = true;
            oChart.PointRadius = 0;
            oChart.AddDataset("Created", vCreated, FieldPagesConst.CS_FIELD_ACCENT,
                "rgba(234,88,12,.15)", true);
            oChart.AddDataset("Completed", vDone, "#198754",
                "rgba(25,135,84,.15)", true);
            oBag.Add(oChart);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Jobs created vs completed</strong>");
            oCard.Body.AddRaw(oChart.HTML);
            oCol.AddRaw(oCard.HTML);

            oCol = AddCol(oRow, "col-lg-4");
            double[] vVals = new double[aStatusBreak.Length];
            for (int vI = 0; vI < aStatusBreak.Length; vI++)
                vVals[vI] = aStatusBreak[vI].Value;
            oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "fsChartStatus";
            // A bar, not a doughnut: the component carries ONE background
            // colour per dataset, and a single-coloured doughnut is a solid
            // ring that says nothing. Bars read correctly with one colour.
            oChart.ChartType = TsgcHTMLChartType.ctBar;
            oChart.CSSHeight = "280px";
            oChart.ShowLegend = false;
            // Label_ already holds the display label; running it through
            // FieldStatusLabel again turned "En route" back into "New".
            for (int vI = 0; vI < aStatusBreak.Length; vI++)
                oChart.AddLabel(aStatusBreak[vI].Label_);
            oChart.AddDataset("Jobs", vVals, FieldPagesConst.CS_FIELD_ACCENT,
                FieldPagesConst.CS_FIELD_ACCENT, true);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Where the work sits</strong>");
            oCard.Body.AddRaw(oChart.HTML);
            oCol.AddRaw(oCard.HTML);
            oRoot.AddRaw(oRow.HTML);

            // --- heatmap + treemap --- //
            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";

            oCol = AddCol(oRow, "col-xl-7");
            TsgcHTMLComponent_Heatmap oHeat = new TsgcHTMLComponent_Heatmap();
            oHeat.RowLabels.Clear();
            string[] vHeatRows = new string[7];
            vHeatRows[0] = "Mon";
            vHeatRows[1] = "Tue";
            vHeatRows[2] = "Wed";
            vHeatRows[3] = "Thu";
            vHeatRows[4] = "Fri";
            vHeatRows[5] = "Sat";
            vHeatRows[6] = "Sun";
            for (int vI = 0; vI <= 6; vI++)
                oHeat.RowLabels.Add(vHeatRows[vI]);
            oHeat.ColumnLabels.Clear();
            for (int vI = 7; vI <= 19; vI++)
                oHeat.ColumnLabels.Add(vI.ToString("00", CultureInfo.InvariantCulture));
            oHeat.CellSize = 30;
            oHeat.CellGap = 2;
            oHeat.Rounded = true;
            oHeat.ShowLegend = true;
            oHeat.ShowValues = false;
            oHeat.AutoMin = false;
            oHeat.MinValue = 0;
            oHeat.MinColor = "#FFF7ED";
            oHeat.MaxColor = FieldPagesConst.CS_FIELD_ACCENT;
            for (int vI = 0; vI < aHeat.Length; vI++)
                if ((aHeat[vI].Hour >= 7) && (aHeat[vI].Hour <= 19) &&
                    (aHeat[vI].Weekday >= 0) && (aHeat[vI].Weekday <= 6))
                    oHeat.SetCell(aHeat[vI].Weekday, aHeat[vI].Hour - 7,
                        aHeat[vI].Count);
            oBag.Add(oHeat);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>When the work happens</strong>");
            oCard.BodyClass = "p-2 overflow-auto";
            oCard.Body.AddRaw(oHeat.HTML);
            oCol.AddRaw(oCard.HTML);

            oCol = AddCol(oRow, "col-xl-5");
            TsgcHTMLComponent_TreeMap oTreeMap = new TsgcHTMLComponent_TreeMap();
            oTreeMap.Width = 520;
            oTreeMap.Height = 330;
            oTreeMap.ShowLabels = true;
            oTreeMap.ShowValues = true;
            oTreeMap.Decimals = 0;
            oTreeMap.ColorScheme = TsgcHTMLTreeMapScheme.tmWarm;
            for (int vI = 0; vI < aRevenue.Length; vI++)
                oTreeMap.AddItem(aRevenue[vI].Label_, aRevenue[vI].Value);
            oBag.Add(oTreeMap);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Parts revenue by customer</strong>");
            oCard.BodyClass = "p-2 overflow-auto";
            if (aRevenue.Length == 0)
                oCard.Body.AddRaw(TsgcHTMLComponent_EmptyState.Build(
                    "No parts billed in this window",
                    "Widen the date range to see the split."));
            else
                oCard.Body.AddRaw(oTreeMap.HTML);
            oCol.AddRaw(oCard.HTML);
            oRoot.AddRaw(oRow.HTML);

            // --- technician performance, with a sparkline per row --- //
            string[] vSparks = new string[aTechStats.Length];
            for (int vI = 0; vI < aTechStats.Length; vI++)
            {
                TsgcHTMLComponent_Sparkline oSpark = new TsgcHTMLComponent_Sparkline();
                oSpark.ChartType = TsgcHTMLSparklineType.slBar;
                oSpark.Width = 110;
                oSpark.Height = 26;
                oSpark.LineColor = FieldPagesConst.CS_FIELD_ACCENT;
                oSpark.FillColor = FieldPagesConst.CS_FIELD_ACCENT;
                oSpark.AutoMin = false;
                oSpark.Min = 0;
                oSpark.ClearData();
                string vStr2 = aTechStats[vI].Trend ?? "";
                string[] vParts = vStr2.Split(',');
                for (int vJ = 0; vJ < vParts.Length; vJ++)
                {
                    double vNum;
                    if (!double.TryParse(vParts[vJ].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out vNum))
                        vNum = 0;
                    oSpark.AddValue(vNum);
                }
                if (vI == 0)
                    oBag.Add(oSpark);
                vSparks[vI] = oSpark.HTML;
            }

            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "fsTechGrid";
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.ShowSort = true;
            oGrid.HeaderClass = "table-light";
            oGrid.EmptyText = "No technician activity in this window.";
            oGrid.ExportXLSX = true;
            oGrid.ExportURL = "/reports/utilisation.xlsx?from=" +
                FmtISODate(aFrom) + "&to=" + FmtISODate(aTo);
            oGrid.ExportXLSXText = "Export XLSX";
            oGrid.ExportPDF = true;
            oGrid.ExportPDFURL = "/reports/sla.pdf?from=" + FmtISODate(aFrom) +
                "&to=" + FmtISODate(aTo);
            TsgcHTMLGridColumn oGCol = oGrid.Columns.Add();
            oGCol.Name = "tech";
            oGCol.Title = "Technician";
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "done";
            oGCol.Title = "Completed";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "sla";
            oGCol.Title = "SLA met";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "hours";
            oGCol.Title = "Hours";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "util";
            oGCol.Title = "Utilisation";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "sat";
            oGCol.Title = "Satisfaction";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            for (int vI = 0; vI < aTechStats.Length; vI++)
                oGrid.AddRow(aTechStats[vI].DisplayName,
                    aTechStats[vI].Completed.ToString(CultureInfo.InvariantCulture),
                    aTechStats[vI].SlaMet.ToString(CultureInfo.InvariantCulture),
                    FmtNum(aTechStats[vI].Hours, 1),
                    FmtNum(aTechStats[vI].Utilisation, 0) + " %",
                    FmtNum(aTechStats[vI].Satisfaction, 2));
            oBag.Add(oGrid);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Technician performance</strong>");
            oCard.BodyClass = "p-0";
            oCard.Body.AddRaw(oGrid.HTML);
            oRoot.AddRaw(oCard.HTML);

            // One Sparkline per technician, rendered from the same weekly series the
            // grid above counts.
            string vStr = "";
            for (int vI = 0; vI < aTechStats.Length; vI++)
                vStr = vStr + "<div class=\"d-flex align-items-center gap-2 py-1 " +
                    "border-bottom\"><span style=\"width:180px;\" class=\"text-truncate\">" +
                    HtmlEsc(aTechStats[vI].DisplayName) + "</span>" + vSparks[vI] +
                    "<span class=\"text-muted small ms-2\">" +
                    aTechStats[vI].Completed.ToString(CultureInfo.InvariantCulture) +
                    " completed</span></div>";
            if (vStr != "")
            {
                oCard = new TsgcHTMLCard();
                oCard.CSSClass = "mt-3";
                oCard.Header.AddRaw("<strong>Weekly completion trend</strong>");
                oCard.Body.AddRaw(vStr);
                oRoot.AddRaw(oCard.HTML);
            }

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Reports", vBody, "reports", vExtra, false);
        }

        public string BuildSqlPage(TFieldPageCtx aCtx, TFieldSqlBlock[] aBlocks)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(
                new string[] { "Manager", "The SQL behind it" },
                new string[] { "/", "" }));
            oRoot.AddRaw("<div class=\"alert alert-warning\">" +
                "<strong>There is no REST tier in this application.</strong> " +
                "Every block below is one query handed straight to an sgcHTML " +
                "component with LoadFromDataSet. No controller, no JSON contract, no " +
                "client-side model: the query on the left is the whole data layer for " +
                "the component on the right.</div>");

            // TsgcHTMLComponent_Accordion, used where it belongs: its Content is
            // HTML-encoded, so it is the right control for prose and the wrong one
            // for a pane of rendered markup (which is what the node-layer
            // TsgcHTMLAccordion is for, and what the other pages use).
            TsgcHTMLComponent_Accordion oNotes = new TsgcHTMLComponent_Accordion();
            oNotes.AccordionID = "fsSqlNotes";
            oNotes.Flush = true;
            TsgcHTMLAccordionItem oNote = oNotes.Items.Add();
            oNote.Title = "Where is the API layer?";
            oNote.Content = "There is not one. A query is opened on a pooled " +
                "SQLite connection and handed to LoadFromDataSet. The component " +
                "renders the rows on the server and the browser receives HTML. " +
                "No JSON contract to version, no DTOs to keep in step, no " +
                "client-side model to hydrate.";
            oNote = oNotes.Items.Add();
            oNote.Title = "What about the dates?";
            oNote.Content = "Timestamps are stored as yyyy-mm-ddThh:nn:ss text and " +
                "parsed by fixed position, never with a locale-bound parser, which " +
                "reads the machine locale and fails silently outside en-US. " +
                "Where a component wants a number instead, SQLite computes it " +
                "(see the calendar query, which returns strftime('%d')).";
            oNote = oNotes.Items.Add();
            oNote.Title = "Is any of this interpolated into SQL?";
            oNote.Content = "Only whitelisted ORDER BY column names and directions, " +
                "and integer LIMIT/OFFSET values built from an integer. Every " +
                "value from a request travels as a parameter, and LIKE terms " +
                "have their metacharacters escaped with an explicit ESCAPE " +
                "clause.";
            oBag.Add(oNotes);
            oRoot.AddRaw("<div class=\"mb-4\">" + oNotes.HTML + "</div>");

            for (int vI = 0; vI < aBlocks.Length; vI++)
            {
                string vKind = (aBlocks[vI].Kind ?? "").Trim().ToLowerInvariant();
                string vRendered = "";

                if (aBlocks[vI].Data == null)
                    vRendered = TsgcHTMLComponent_EmptyState.Build("Query returned " +
                        "nothing", "The window holds no rows for this one.");
                else if (vKind == "datatable")
                {
                    TsgcHTMLComponent_DataTable oDT = new TsgcHTMLComponent_DataTable();
                    oDT.TableID = "fsSql" + vI.ToString(CultureInfo.InvariantCulture);
                    oDT.ShowSearch = false;
                    oDT.ShowPageSize = false;
                    oDT.Grid.Striped = true;
                    oDT.Grid.HeaderClass = "table-light";
                    oDT.LoadFromDataSet(aBlocks[vI].Data, 25);
                    oBag.Add(oDT);
                    oBag.Add(oDT.Grid);
                    vRendered = oDT.HTML;
                }
                else if (vKind == "treegrid")
                {
                    TsgcHTMLComponent_TreeGrid oTG = new TsgcHTMLComponent_TreeGrid();
                    oTG.TreeGridID = "fsSqlTG" +
                        vI.ToString(CultureInfo.InvariantCulture);
                    oTG.Striped = true;
                    oTG.ExpandedByDefault = true;
                    oTG.LoadFromDataSet(aBlocks[vI].Data, "id", "parent_id");
                    oBag.Add(oTG);
                    vRendered = oTG.HTML;
                }
                else if (vKind == "chart")
                {
                    TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
                    oChart.ChartID = "fsSqlChart" +
                        vI.ToString(CultureInfo.InvariantCulture);
                    oChart.ChartType = TsgcHTMLChartType.ctBar;
                    oChart.CSSHeight = "260px";
                    oChart.ShowLegend = false;
                    oChart.LoadFromDataSet(aBlocks[vI].Data, "label", "value");
                    oBag.Add(oChart);
                    vRendered = oChart.HTML;
                }
                else if (vKind == "select")
                {
                    TsgcHTMLComponent_Select oSel = new TsgcHTMLComponent_Select();
                    oSel.SelectID = "fsSqlSel" +
                        vI.ToString(CultureInfo.InvariantCulture);
                    oSel.ElementName = "picked";
                    oSel.Label_ = "Straight out of the query";
                    oSel.LoadFromDataSet(aBlocks[vI].Data, "value", "text");
                    oBag.Add(oSel);
                    vRendered = oSel.HTML;
                }
                else if (vKind == "treemap")
                {
                    TsgcHTMLComponent_TreeMap oTM = new TsgcHTMLComponent_TreeMap();
                    oTM.Width = 520;
                    oTM.Height = 260;
                    oTM.ShowValues = true;
                    oTM.Decimals = 0;
                    oTM.ColorScheme = TsgcHTMLTreeMapScheme.tmCool;
                    oTM.LoadFromDataSet(aBlocks[vI].Data, "label", "value");
                    oBag.Add(oTM);
                    vRendered = oTM.HTML;
                }
                else if (vKind == "palette")
                {
                    TsgcHTMLComponent_CommandPalette oPal =
                        new TsgcHTMLComponent_CommandPalette();
                    oPal.PaletteID = "fsSqlPal" +
                        vI.ToString(CultureInfo.InvariantCulture);
                    oPal.HotKey = "j";
                    oPal.Placeholder = "Ctrl+J: jump to a job straight from the query";
                    oPal.MaxResults = 8;
                    oPal.LoadFromDataSet(aBlocks[vI].Data, "caption", "description",
                        "href");
                    oBag.Add(oPal);
                    vRendered = "<div class=\"text-muted small mb-2\">Press " +
                        "<kbd>Ctrl</kbd>+<kbd>J</kbd>: the palette entries are rows of " +
                        "this query.</div>" + oPal.HTML;
                }
                else
                {
                    TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                    oGrid.TableID = "fsSqlGrid" +
                        vI.ToString(CultureInfo.InvariantCulture);
                    oGrid.Striped = true;
                    oGrid.Hover = true;
                    oGrid.HeaderClass = "table-light";
                    oGrid.LoadFromDataSet(aBlocks[vI].Data, 25);
                    oBag.Add(oGrid);
                    vRendered = oGrid.HTML;
                }

                TsgcHTMLContainer oPre = new TsgcHTMLContainer("div");
                oPre.CSSClass = "fs-sql";
                oPre.AddText(aBlocks[vI].SQL);
                TsgcHTMLCard oCard = new TsgcHTMLCard();
                oCard.CSSClass = "mb-3";
                oCard.Header.AddRaw("<strong>" + HtmlEsc(aBlocks[vI].Caption) +
                    "</strong><div class=\"text-muted small\">" +
                    HtmlEsc(aBlocks[vI].Note) + "</div>");
                oCard.Body.AddRaw("<div class=\"row g-3\">" +
                    "<div class=\"col-lg-5\">" + oPre.HTML + "</div>" +
                    "<div class=\"col-lg-7\">" + vRendered + "</div></div>");
                oRoot.AddRaw(oCard.HTML);
            }

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "The SQL behind it", vBody, "sql", vExtra,
                false);
        }

        public string BuildUsersPage(TFieldPageCtx aCtx, TFieldUser[] aUsers,
            string aFlash, string aError)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Manager", "Users" },
                new string[] { "/", "" }));
            if (aFlash != "")
                oRoot.AddRaw(AlertHTML(FlashMessage(aFlash),
                    TsgcHTMLAlertStyle.asSuccess));
            if (aError != "")
                oRoot.AddRaw(AlertHTML(aError, TsgcHTMLAlertStyle.asDanger));

            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            TsgcHTMLContainer oCol = AddCol(oRow, "col-lg-8");
            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "fsUsersGrid";
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.ShowSort = true;
            oGrid.HeaderClass = "table-light";
            TsgcHTMLGridColumn oGCol = oGrid.Columns.Add();
            oGCol.Name = "user";
            oGCol.Title = "Username";
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "name";
            oGCol.Title = "Display name";
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "role";
            oGCol.Title = "Role";
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "phone";
            oGCol.Title = "Phone";
            oGCol = oGrid.Columns.Add();
            oGCol.Name = "since";
            oGCol.Title = "Since";
            for (int vI = 0; vI < aUsers.Length; vI++)
                oGrid.AddRow(aUsers[vI].Username, aUsers[vI].DisplayName,
                    aUsers[vI].Role, aUsers[vI].Phone, FmtDate(aUsers[vI].CreatedAt));
            oBag.Add(oGrid);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>" +
                aUsers.Length.ToString(CultureInfo.InvariantCulture) +
                " user(s)</strong>");
            oCard.BodyClass = "p-0";
            oCard.Body.AddRaw(oGrid.HTML);
            oCol.AddRaw(oCard.HTML);

            oCol = AddCol(oRow, "col-lg-4");
            TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "fsUserForm";
            oForm.Action = "/users/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Create user";
            oForm.SubmitClass = "btn btn-fs";
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftHidden;
            oField.Name = "id";
            oField.Value = "0";
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "username";
            oField.Label_ = "Username";
            oField.Required = true;
            oField.ColSpan = 12;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "display_name";
            oField.Label_ = "Display name";
            oField.Required = true;
            oField.ColSpan = 12;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "role";
            oField.Label_ = "Role";
            oField.ColSpan = 12;
            oField.Options.Add(FieldConst.CS_ROLE_TECHNICIAN + "=Technician");
            oField.Options.Add(FieldConst.CS_ROLE_DISPATCHER + "=Dispatcher");
            oField.Options.Add(FieldConst.CS_ROLE_MANAGER + "=Manager");
            oField.Options.Add(FieldConst.CS_ROLE_CUSTOMER + "=Customer portal");
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "phone";
            oField.Label_ = "Phone";
            oField.ColSpan = 12;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "password";
            oField.Label_ = "Password";
            oField.HelpText = "At least 6 characters. Stored bcrypt-hashed.";
            oField.ColSpan = 12;
            oBag.Add(oForm);
            oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Add a user</strong>");
            oCard.Body.AddRaw(oForm.HTML);
            oCol.AddRaw(oCard.HTML);

            // Passkey enrolment for the signed-in account.
            TsgcHTMLComponent_WebAuthnLogin oWA = new TsgcHTMLComponent_WebAuthnLogin();
            oWA.WebAuthnID = "fsWebAuthn";
            oWA.Mode = TsgcHTMLWebAuthnMode.wamRegister;
            oWA.Title = "Your passkey";
            oWA.Description = "Register this device so you can sign in " +
                "without a password.";
            oWA.RegisterButtonText = "Add a passkey to this device";
            oWA.RegisterButtonStyle = TsgcHTMLButtonStyle.bsOutlinePrimary;
            oWA.ShowPasskeyIcon = false;
            oWA.RegisterURL = "/passkey/register";
            oWA.AuthenticateURL = "/passkey/login";
            oWA.CustomScript = "function fsB64uToBuf(s){" +
                "if(!s)return new ArrayBuffer(0);" +
                "s=s.replace(/-/g,\"+\").replace(/_/g,\"/\");" +
                "while(s.length%4)s+=\"=\";var bin=atob(s);" +
                "var arr=new Uint8Array(bin.length);" +
                "for(var i=0;i<bin.length;i++)arr[i]=bin.charCodeAt(i);" +
                "return arr.buffer;}" + "function fsBufToB64u(b){" +
                "var by=new Uint8Array(b);var s=\"\";" +
                "for(var i=0;i<by.byteLength;i++)s+=String.fromCharCode(by[i]);" +
                "return btoa(s).replace(/\\+/g,\"-\").replace(/\\//g,\"_\")" +
                ".replace(/=+$/,\"\");}" + "async function sgcWebAuthnRegister(){" +
                "var s=document.getElementById(\"fsWebAuthn_status\");" + "try{" +
                "var n=document.getElementById(\"fsPasskeyName\");" +
                "var q=n&&n.value?(\"?name=\"+encodeURIComponent(n.value)):\"\";" +
                "s.innerHTML='<div class=\"text-muted small\">Requesting passkey...</div>';" +
                "var r=await fetch(\"/passkey/register/options\",{method:\"POST\"," +
                "headers:{\"Content-Type\":\"application/json\"},body:\"{}\"," +
                "credentials:\"same-origin\"});" + "var o=await r.json();" +
                "if(!r.ok||o.error)throw new Error(o.error||(\"options \"+r.status));" +
                "o.challenge=fsB64uToBuf(o.challenge);" +
                "o.user.id=fsB64uToBuf(o.user.id);" +
                "if(o.excludeCredentials){o.excludeCredentials.forEach(" +
                "function(c){c.id=fsB64uToBuf(c.id);});}" +
                "var c=await navigator.credentials.create({publicKey:o});" +
                "var at={id:c.id,rawId:fsBufToB64u(c.rawId),type:c.type,response:{" +
                "clientDataJSON:fsBufToB64u(c.response.clientDataJSON)," +
                "attestationObject:fsBufToB64u(c.response.attestationObject)}};" +
                "var v=await fetch(\"/passkey/register/verify\"+q,{method:\"POST\"," +
                "headers:{\"Content-Type\":\"application/json\"}," +
                "body:JSON.stringify(at),credentials:\"same-origin\"});" +
                "var vj=await v.json();" +
                "if(v.ok&&vj.ok){s.innerHTML='<div class=\"alert alert-success " +
                "py-2 my-2\">Passkey registered.</div>';}" +
                "else throw new Error(vj.error||(\"verify \"+v.status));" +
                "}catch(e){s.innerHTML='<div class=\"alert alert-danger py-2 " +
                "my-2\">'+(e&&e.message?e.message:String(e))+'</div>';}}" +
                "async function sgcWebAuthnAuthenticate(){}";
            oBag.Add(oWA);
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3";
            oCard.Header.AddRaw("<strong>Security</strong>");
            oCard.Body.AddRaw("<div class=\"mb-2\"><label class=\"form-label\" " +
                "for=\"fsPasskeyName\">Device name</label>" +
                "<input class=\"form-control\" id=\"fsPasskeyName\" " +
                "placeholder=\"Work phone\"></div>");
            oCard.Body.AddRaw(oWA.HTML);
            oCol.AddRaw(oCard.HTML);

            oRoot.AddRaw(oRow.HTML);

            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Users", vBody, "users", vExtra, false);
        }

        public string BuildAuditPage(TFieldPageCtx aCtx, TFieldAuditEntry[] aEntries)
        {
            TFieldCSSBag oBag = new TFieldCSSBag();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumb(new string[] { "Manager", "Audit log" },
                new string[] { "/", "" }));
            TsgcHTMLComponent_AuditTrail oAudit = new TsgcHTMLComponent_AuditTrail();
            oAudit.AuditID = "fsAudit";
            oAudit.ShowFilter = true;
            oAudit.PageSize = 25;
            oAudit.Striped = true;
            oAudit.EmptyText = "Nothing recorded yet.";
            oAudit.FilterPlaceholder = "Filter by user, action or entity";
            for (int vI = 0; vI < aEntries.Length; vI++)
            {
                TsgcHTMLAuditEntry oEntry = oAudit.AddEntry(aEntries[vI].UserName,
                    aEntries[vI].Action, aEntries[vI].Entity + " #" +
                    aEntries[vI].EntityId.ToString(CultureInfo.InvariantCulture),
                    aEntries[vI].IP, "ok", aEntries[vI].Detail);
                oEntry.Timestamp = aEntries[vI].CreatedAt;
            }
            oBag.Add(oAudit);
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw("<strong>Audit log</strong>");
            oCard.Body.AddRaw(oAudit.HTML);
            oRoot.AddRaw(oCard.HTML);
            string vBody = oRoot.HTML;
            string vExtra = oBag.CSS;
            return BuildPageShell(aCtx, "Audit log", vBody, "audit", vExtra, false);
        }

        // ---------------------------------------------------------------------
        // Realtime singleton. The three live components are created ONCE and owned
        // here; the server only ever exchanges strings with them, and every entry
        // point takes the lock, because the HTTP threads and the push thread both
        // arrive here.
        //
        // The Delphi TCriticalSection becomes a plain monitor lock on a private
        // object: Acquire/Release around a try..finally is exactly what C# lock
        // does, and .NET is GC-managed so the destructor's FreeAndNil calls go.
        // ---------------------------------------------------------------------
        private class TFieldLive
        {
            private readonly object FLock = new object();
            private readonly TsgcHTMLComponent_Presence FPresence;
            private readonly TsgcHTMLComponent_ActivityFeed FFeed;
            private readonly TsgcHTMLComponent_LogViewer FLog;
            private readonly TsgcHTMLComponent_JobProgress FJobs;

            public TFieldLive()
            {
                FPresence = new TsgcHTMLComponent_Presence();
                FPresence.PresenceID = FieldPagesConst.CS_ID_PRESENCE;
                FPresence.Title = "Technicians on duty";
                FPresence.Layout = TsgcHTMLPresenceLayout.plList;
                FPresence.MaxVisible = 0;
                FPresence.ShowStatusText = true;
                FPresence.ShowCount = true;
                FPresence.AvatarSize = 32;
                FPresence.EmptyText = "Nobody on duty";

                FFeed = new TsgcHTMLComponent_ActivityFeed();
                FFeed.FeedID = FieldPagesConst.CS_ID_FEED;
                FFeed.Title = "Live activity";
                FFeed.MaxItems = 40;
                FFeed.Compact = true;
                FFeed.ShowRelativeTime = true;
                FFeed.EmptyText = "Waiting for the first job event...";

                FLog = new TsgcHTMLComponent_LogViewer();
                FLog.LogID = FieldPagesConst.CS_ID_LOG;
                FLog.Title = "Dispatch log";
                FLog.MaxLines = 200;
                FLog.CSSHeight = "190px";
                FLog.AutoScroll = true;
                FLog.ShowAutoScrollToggle = true;
                FLog.ShowLevelFilter = true;
                FLog.ShowSearch = false;
                FLog.Theme = TsgcHTMLLogTheme.ltDark;
                FLog.EmptyText = "No dispatch activity yet.";

                FJobs = new TsgcHTMLComponent_JobProgress();
                FJobs.JobsID = FieldPagesConst.CS_ID_JOBS;
                FJobs.Title = "Work in progress";
                FJobs.EmptyText = "Nobody is on site right now.";
                FJobs.ShowCompleted = true;
                FJobs.AutoRemoveCompleted = false;
                // The cancel button belongs to a batch runner, not to a field job: a
                // job is cancelled from the board or from the job page, where the
                // state machine is.
                FJobs.ShowCancelButton = false;
            }

            private TsgcHTMLColor ColorOf(string aName)
            {
                if (string.Equals(aName, "success", StringComparison.OrdinalIgnoreCase))
                    return TsgcHTMLColor.hcSuccess;
                if (string.Equals(aName, "warning", StringComparison.OrdinalIgnoreCase))
                    return TsgcHTMLColor.hcWarning;
                if (string.Equals(aName, "danger", StringComparison.OrdinalIgnoreCase))
                    return TsgcHTMLColor.hcDanger;
                if (string.Equals(aName, "info", StringComparison.OrdinalIgnoreCase))
                    return TsgcHTMLColor.hcInfo;
                if (string.Equals(aName, "secondary", StringComparison.OrdinalIgnoreCase))
                    return TsgcHTMLColor.hcSecondary;
                if (string.Equals(aName, "dark", StringComparison.OrdinalIgnoreCase))
                    return TsgcHTMLColor.hcDark;
                return TsgcHTMLColor.hcPrimary;
            }

            public string RenderPresence()
            {
                lock (FLock)
                {
                    return FPresence.HTML;
                }
            }

            public string RenderFeed()
            {
                lock (FLock)
                {
                    return FFeed.HTML;
                }
            }

            public string RenderLog()
            {
                lock (FLock)
                {
                    return FLog.HTML;
                }
            }

            public string RenderJobs()
            {
                lock (FLock)
                {
                    return FJobs.HTML;
                }
            }

            // The roster of jobs being carried out right now. A row that is already on
            // the page is updated in place (out-of-band outerHTML); a row that is not
            // is appended (out-of-band beforeend), which is the only way a NEW row can
            // reach a browser that rendered the page before the job started. A job
            // that has left the set is shown as finished rather than removed, because
            // an out-of-band swap can only replace an element, never delete one.
            public string SyncJobs(TFieldLiveJob[] aJobs)
            {
                string vOut = "";
                lock (FLock)
                {
                    // 1) add or update everything in the new set
                    for (int vI = 0; vI < aJobs.Length; vI++)
                    {
                        string vId = aJobs[vI].Id.ToString(CultureInfo.InvariantCulture);
                        TsgcHTMLJobStatus vStatus;
                        if (string.Equals(aJobs[vI].Status, FieldConst.CS_JOB_ONSITE,
                            StringComparison.OrdinalIgnoreCase))
                            vStatus = TsgcHTMLJobStatus.jsRunning;
                        else if (string.Equals(aJobs[vI].Status,
                            FieldConst.CS_JOB_COMPLETE,
                            StringComparison.OrdinalIgnoreCase))
                            vStatus = TsgcHTMLJobStatus.jsCompleted;
                        else if (string.Equals(aJobs[vI].Status,
                            FieldConst.CS_JOB_CANCELLED,
                            StringComparison.OrdinalIgnoreCase))
                            vStatus = TsgcHTMLJobStatus.jsCancelled;
                        else
                            vStatus = TsgcHTMLJobStatus.jsQueued;

                        bool vFound = false;
                        for (int vJ = 0; vJ < FJobs.Jobs.Count; vJ++)
                            if (FJobs.Jobs[vJ].Id == vId)
                            {
                                vFound = true;
                                break;
                            }

                        if (!vFound)
                        {
                            TsgcHTMLJobItem oItem = FJobs.AddJob(vId,
                                aJobs[vI].Reference + "  " + aJobs[vI].Title,
                                aJobs[vI].Technician);
                            if (oItem != null)
                                oItem.StartedAt = DateTime.Now;
                            FJobs.UpdateJob(vId, aJobs[vI].Percent, vStatus,
                                aJobs[vI].Percent.ToString(CultureInfo.InvariantCulture) +
                                "% of the checklist done");
                            vOut = vOut + FJobs.GetJobAppendFragmentHTML(vId);
                        }
                        else
                        {
                            FJobs.UpdateJob(vId, aJobs[vI].Percent, vStatus,
                                aJobs[vI].Percent.ToString(CultureInfo.InvariantCulture) +
                                "% of the checklist done");
                            vOut = vOut + FJobs.GetJobFragmentHTML(vId);
                        }
                    }

                    // 2) anything that dropped out of the set has finished
                    for (int vJ = 0; vJ < FJobs.Jobs.Count; vJ++)
                    {
                        TsgcHTMLJobItem oItem = FJobs.Jobs[vJ];
                        if (oItem.Status == TsgcHTMLJobStatus.jsCompleted)
                            continue;
                        bool vStillThere = false;
                        for (int vI = 0; vI < aJobs.Length; vI++)
                            if (aJobs[vI].Id.ToString(CultureInfo.InvariantCulture) ==
                                oItem.Id)
                            {
                                vStillThere = true;
                                break;
                            }
                        if (!vStillThere)
                        {
                            FJobs.UpdateJob(oItem.Id, 100,
                                TsgcHTMLJobStatus.jsCompleted, "Left site");
                            vOut = vOut + FJobs.GetJobFragmentHTML(oItem.Id);
                        }
                    }

                    if (vOut != "")
                        vOut = vOut + FJobs.GetEmptyFragmentHTML();
                    return vOut;
                }
            }

            public string SyncTechnicians(TFieldTechnician[] aTechs)
            {
                string vOut = "";
                lock (FLock)
                {
                    for (int vI = 0; vI < aTechs.Length; vI++)
                    {
                        string vId = aTechs[vI].Id.ToString(CultureInfo.InvariantCulture);
                        TsgcHTMLPresenceStatus vNew = PresenceStatus(aTechs[vI].Presence);
                        string vText = PresenceLabel(aTechs[vI].Presence);
                        bool vFound = false;
                        for (int vJ = 0; vJ < FPresence.Users.Count; vJ++)
                        {
                            TsgcHTMLPresenceUser oUser = FPresence.Users[vJ];
                            if (oUser.Id == vId)
                            {
                                vFound = true;
                                if ((oUser.Status != vNew) || (oUser.StatusText != vText))
                                {
                                    FPresence.SetUserStatus(vId, vNew, vText);
                                    vOut = vOut + FPresence.GetUserFragmentHTML(vId);
                                }
                                break;
                            }
                        }
                        if (!vFound)
                        {
                            FPresence.AddUser(vId, aTechs[vI].DisplayName, vNew, "",
                                vText);
                            vOut = vOut + FPresence.GetUserFragmentHTML(vId);
                        }
                    }
                    if (vOut != "")
                        vOut = vOut + FPresence.GetCountFragmentHTML() +
                            FPresence.GetMoreFragmentHTML() +
                            FPresence.GetEmptyFragmentHTML();
                    return vOut;
                }
            }

            public string AddActivity(string aActor, string aAction, string aTarget,
                string aColorName)
            {
                lock (FLock)
                {
                    FFeed.AddActivity(aActor, aAction, aTarget, ColorOf(aColorName));
                    return FFeed.GetLastItemFragmentHTML() +
                        FFeed.GetEmptyFragmentHTML();
                }
            }

            public string AddLog(string aLevel, string aText, string aSource)
            {
                TsgcHTMLLogLevel vLevel;
                if (string.Equals(aLevel, "warn", StringComparison.OrdinalIgnoreCase))
                    vLevel = TsgcHTMLLogLevel.llWarning;
                else if (string.Equals(aLevel, "error",
                    StringComparison.OrdinalIgnoreCase))
                    vLevel = TsgcHTMLLogLevel.llError;
                else if (string.Equals(aLevel, "debug",
                    StringComparison.OrdinalIgnoreCase))
                    vLevel = TsgcHTMLLogLevel.llDebug;
                else
                    vLevel = TsgcHTMLLogLevel.llInfo;
                lock (FLock)
                {
                    FLog.AddLine(vLevel, aText, aSource);
                    return FLog.GetLastLineFragmentHTML() +
                        FLog.GetEmptyFragmentHTML();
                }
            }
        }

        // ----- TFieldPages - realtime facade ----- //

        // The Delphi unit-level gLive. Guarded by its own lock so LiveInit is safe
        // to call from more than one thread, which the Delphi got from being called
        // once at startup.
        private static readonly object GLiveLock = new object();
        private static TFieldLive GLive;

        public static void LiveInit()
        {
            lock (GLiveLock)
            {
                if (GLive == null)
                    GLive = new TFieldLive();
            }
        }

        public static void LiveDone()
        {
            lock (GLiveLock)
            {
                GLive = null;
            }
        }

        public static string LiveRenderPresence()
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.RenderPresence();
            return "";
        }

        public static string LiveRenderFeed()
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.RenderFeed();
            return "";
        }

        public static string LiveRenderLog()
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.RenderLog();
            return "";
        }

        public static string LiveRenderJobs()
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.RenderJobs();
            return "";
        }

        // Replaces the "work in progress" roster; returns the out-of-band
        // fragments for the rows that actually moved.
        public static string LiveSyncJobs(TFieldLiveJob[] aJobs)
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.SyncJobs(aJobs);
            return "";
        }

        // Replace the roster; returns the out-of-band fragments for every user
        // whose status actually changed plus the online-count badge.
        public static string LiveSyncTechnicians(TFieldTechnician[] aTechs)
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.SyncTechnicians(aTechs);
            return "";
        }

        // Append one activity item / one log line; returns its OOB fragment.
        public static string LiveAddActivity(string aActor, string aAction,
            string aTarget, string aColorName)
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.AddActivity(aActor, aAction, aTarget, aColorName);
            return "";
        }

        public static string LiveAddLog(string aLevel, string aText, string aSource)
        {
            TFieldLive oLive = GLive;
            if (oLive != null)
                return oLive.AddLog(aLevel, aText, aSource);
            return "";
        }
    }
}
