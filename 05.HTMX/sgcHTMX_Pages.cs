// ***************************************************************************
//  sgcHTMX - htmx features demo (managed port)
//  Port of delphi\Demos\60.HTML\05.HTMX\sgcHTMXDemo_Pages.pas
//
//  Faithful 1:1 port of the ten htmx feature pages built with the sgcHTML node /
//  component layer (TsgcHTMLComponent_Sidebar / _StatCard / _Spinner / _Select,
//  TsgcHTMLCard / Table / Form / Field, TsgcHTMX_Fragment for out-of-band swaps).
//  The Delphi module-global tick counters + cart TStringList become private
//  static fields (single server instance, as in the Delphi console demo).
// ***************************************************************************

using System;
using System.Globalization;
using System.Text;
// sgc
using esegece.sgcWebSockets;

namespace HTMXDemo
{
    public static class TsgcHTMXDemoPages
    {
        // ----- module state (mirrors the Delphi global vars) ----- //
        private static int GHTMXDashTick = 0;
        private static int GHTMXFeedTick = 0;
        // product id ("1".."6") -> quantity in cart
        private static readonly int[] GHTMXCartData = new int[6];

        // ----- sample data ----- //
        private static readonly string[] CS_PROD_NAMES = {
            "Wireless Headphones", "USB Hub 7-Port", "LED Desk Lamp",
            "Mechanical Keyboard", "Smart Watch Band", "Webcam HD 1080p",
            "Python Cookbook", "Clean Code", "Design Patterns", "SQL Mastery",
            "Coffee Mug XL", "Tea Sampler Box" };
        private static readonly string[] CS_PROD_CATS = {
            "Electronics", "Electronics", "Electronics", "Electronics",
            "Electronics", "Electronics", "Books", "Books", "Books", "Books",
            "Kitchen", "Kitchen" };
        private static readonly int[] CS_PROD_PRICES = {
            8999, 2499, 3999, 12999, 1999, 4999, 4499, 2999, 3499, 3199, 1499, 2499 };
        private static readonly string[] CS_FEED_USERS = {
            "Alice", "Bob", "Carol", "Dave", "Emma", "Frank" };
        private static readonly string[] CS_FEED_ACTIONS = {
            "logged in", "uploaded a file", "created a report", "deleted a record",
            "invited a team member", "exported data", "updated settings",
            "commented on a task" };
        private static readonly string[] CS_FEED_BADGES = {
            "bg-success", "bg-primary", "bg-info", "bg-danger",
            "bg-warning text-dark", "bg-secondary", "bg-dark", "bg-info" };
        private static readonly string[] CS_FEED_CODES = {
            "IN", "UP", "CR", "DL", "IV", "EX", "ST", "CM" };
        private static readonly string[] CS_EDIT_IDS = { "1", "2", "3", "4", "5" };
        private static readonly string[] CS_EDIT_LABELS = {
            "Server Time", "Poll Count", "Response", "Status", "Uptime" };
        private static readonly string[] CS_EDIT_VALS = {
            "12:00:00", "0", "200 OK", "Running", "0s" };
        private static readonly string[] CS_DEL_NAMES = {
            "Alice Johnson", "Bob Smith", "Carol Williams", "David Brown", "Emma Davis" };
        private static readonly string[] CS_DEL_DEPTS = {
            "Engineering", "Marketing", "HR", "Finance", "Engineering" };
        private static readonly string[] CS_DEL_ROLES = {
            "Developer", "Designer", "Manager", "Analyst", "Tech Lead" };
        private static readonly string[] CS_SCR_FN = {
            "Alice", "Bob", "Carol", "David", "Emma", "Frank", "Grace", "Henry",
            "Isabella", "James" };
        private static readonly string[] CS_SCR_LN = {
            "Johnson", "Smith", "Williams", "Brown", "Davis", "Miller", "Wilson",
            "Moore", "Taylor", "Anderson" };
        private static readonly string[] CS_SCR_DEPT = {
            "Engineering", "Marketing", "HR", "Finance", "Operations" };
        private static readonly string[] CS_SCR_CITY = {
            "New York", "London", "Berlin", "Paris", "Tokyo", "Chicago",
            "Amsterdam", "Sydney", "Toronto", "Singapore" };
        private static readonly string[] CS_SCR_POS = {
            "Developer", "Senior Dev", "Manager", "Analyst", "Tech Lead" };

        private const string CS_PAGE_HEAD =
            @"<!DOCTYPE html><html lang=""en""><head>" +
            @"<meta charset=""UTF-8"">" +
            @"<meta name=""viewport"" content=""width=device-width, initial-scale=1"">" +
            @"<title>htmx Features - sgcHTML</title>" +
            @"<link rel=""stylesheet"" href=""/bootstrap.min.css"">" + "<style>" +
            "body{margin:0;background:#f8f9fa;}" +
            "#demo-content{margin-left:220px;padding:1.5rem;}" +
            ".htmx-indicator{opacity:0;transition:opacity 200ms ease-in;}" +
            ".htmx-request .htmx-indicator{opacity:1;}" +
            ".htmx-request.htmx-indicator{opacity:1;}" +
            ".feed-scroll{max-height:380px;overflow-y:auto;}" + "</style>" +
            "</head><body>";

        private const string CS_PAGE_FOOT =
            @"<script src=""/bootstrap.bundle.min.js""></script>" +
            @"<script src=""/htmx.min.js""></script>" + "<script>" +
            @"document.querySelectorAll(""#sgcSidebar .nav-link"")" +
            @".forEach(function(a){" + @"var url=a.getAttribute(""href"");" +
            @"a.setAttribute(""hx-get"",url);" +
            @"a.setAttribute(""hx-target"",""#demo-content"");" +
            @"a.setAttribute(""hx-swap"",""innerHTML"");" + @"a.setAttribute(""href"",""#"");});" +
            @"htmx.process(document.getElementById(""sgcSidebar""));" +
            @"document.body.addEventListener(""htmx:beforeSwap"",function(e){" +
            @"document.querySelectorAll(""#sgcSidebar .nav-link"")" +
            @".forEach(function(a){a.classList.remove(""active"");});" +
            @"var url=e.detail.requestConfig.path;" +
            @"document.querySelectorAll(""#sgcSidebar .nav-link"")" +
            @".forEach(function(a){" +
            @"if(a.getAttribute(""hx-get"")===url)a.classList.add(""active"");});});" +
            "</script></body></html>";

        // ----- helpers ----- //

        // Encode &<>" (matches the Delphi HtmlEnc; ' is intentionally not encoded).
        private static string HtmlEnc(string s)
        {
            var oResult = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '&': oResult.Append("&amp;"); break;
                    case '<': oResult.Append("&lt;"); break;
                    case '>': oResult.Append("&gt;"); break;
                    case '"': oResult.Append("&quot;"); break;
                    default: oResult.Append(c); break;
                }
            }
            return oResult.ToString();
        }

        private static string FormatCents(int aCents)
        {
            int vC = aCents % 100;
            if (vC < 10)
                return "$" + (aCents / 100).ToString(CultureInfo.InvariantCulture) + ".0" +
                    vC.ToString(CultureInfo.InvariantCulture);
            return "$" + (aCents / 100).ToString(CultureInfo.InvariantCulture) + "." +
                vC.ToString(CultureInfo.InvariantCulture);
        }

        private static string ProdCatColor(string aCat)
        {
            if (aCat == "Electronics")
                return "primary";
            if (aCat == "Books")
                return "success";
            return "warning text-dark";
        }

        private static string SelAttr(string aVal, string aCurrent)
        {
            if (aVal == aCurrent)
                return " selected";
            return "";
        }

        private static int StrToIntDef(string aValue, int aDefault)
        {
            int vResult;
            if (int.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        private static int CartTotalQty()
        {
            int vResult = 0;
            for (int i = 1; i <= 6; i++)
                vResult += GHTMXCartData[i - 1];
            return vResult;
        }

        private static string GetCartContent()
        {
            int vTotalCents = 0;
            var oItems = new StringBuilder();
            for (int i = 1; i <= 6; i++)
            {
                int vQty = GHTMXCartData[i - 1];
                if (vQty > 0)
                {
                    int vItemCents = CS_PROD_PRICES[i - 1] * vQty;
                    vTotalCents += vItemCents;
                    oItems.Append(
                        @"<li class=""list-group-item d-flex justify-content-between" +
                        @" align-items-center px-0 py-1"">" + @"<div class=""small""><strong>" +
                        CS_PROD_NAMES[i - 1] + "</strong>" + @"<div class=""text-muted"">" +
                        vQty.ToString(CultureInfo.InvariantCulture) + " x " +
                        FormatCents(CS_PROD_PRICES[i - 1]) + "</div></div>" +
                        @"<div class=""d-flex align-items-center"">" +
                        @"<span class=""fw-bold me-2"">" + FormatCents(vItemCents) + "</span>" +
                        @"<button class=""btn btn-sm btn-outline-danger py-0""" +
                        @" hx-get=""/demo/cart-remove?id=" +
                        i.ToString(CultureInfo.InvariantCulture) + @"""" +
                        @" hx-target=""#cart-panel"" hx-swap=""innerHTML"">" +
                        "&times;</button></div></li>");
                }
            }
            if (oItems.Length == 0)
                return @"<p class=""text-muted text-center py-4"">Cart is empty</p>";
            return @"<ul class=""list-group list-group-flush mb-2"">" + oItems.ToString() +
                "</ul>" +
                @"<div class=""d-flex justify-content-between fw-bold border-top pt-2"">" +
                @"<span>Total</span><span class=""text-success"">" +
                FormatCents(vTotalCents) + "</span></div>";
        }

        private static string MakeFeedItem(int aUserIdx, int aActionIdx, string aTime)
        {
            return @"<div class=""d-flex align-items-start py-2 border-bottom"">" +
                @"<span class=""badge " + CS_FEED_BADGES[aActionIdx] +
                @" me-2"" style=""min-width:30px;text-align:center;"">" +
                CS_FEED_CODES[aActionIdx] + "</span>" +
                @"<div class=""flex-grow-1 small""><strong>" + CS_FEED_USERS[aUserIdx] +
                "</strong> " + CS_FEED_ACTIONS[aActionIdx] +
                @"<div class=""text-muted"">" + aTime + "</div></div></div>";
        }

        // -------------------------------------------------------------------
        // Shell
        // -------------------------------------------------------------------

        public static string BuildShell()
        {
            var oSidebar = new TsgcHTMLComponent_Sidebar();
            try
            {
                oSidebar.Brand = "htmx Demo";
                oSidebar.Dark = true;
                oSidebar.CSSWidth = "220px";
                oSidebar.Fixed = true;
                oSidebar.SidebarID = "sgcSidebar";
                oSidebar.FooterText = "sgcHTML component library";
                TsgcHTMLSidebarItem oItem = oSidebar.Items.Add();
                oItem.Text = "Live Dashboard";
                oItem.Href = "/demo/dashboard";
                oItem.Active = true;
                oSidebar.Items.Add().Divider = true;
                oItem = oSidebar.Items.Add();
                oItem.Text = "Smart Search";
                oItem.Href = "/demo/search";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Step Wizard";
                oItem.Href = "/demo/wizard";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Activity Feed";
                oItem.Href = "/demo/feed";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Inline Edit";
                oItem.Href = "/demo/edit";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Validation";
                oItem.Href = "/demo/validate";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Delete Row";
                oItem.Href = "/demo/delete";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Infinite Scroll";
                oItem.Href = "/demo/scroll";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Shopping Cart";
                oItem.Href = "/demo/cart";
                oItem = oSidebar.Items.Add();
                oItem.Text = "Cascade Select";
                oItem.Href = "/demo/cascade";

                var oMain = new TsgcHTMLContainer();
                oMain.ID = "demo-content";
                oMain.AddRaw(BuildDemoDashboard());
                return CS_PAGE_HEAD + oSidebar.HTML + oMain.HTML + BuildProductFooter() +
                    CS_PAGE_FOOT;
            }
            finally
            {
                oSidebar.Dispose();
            }
        }

        // -------------------------------------------------------------------
        // 1. Live Dashboard
        // -------------------------------------------------------------------

        public static string BuildDemoDashboard()
        {
            GHTMXDashTick = 0;
            string vCPU = TsgcHTMLComponent_StatCard.Build("CPU Usage", "42%",
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stUp, "+3%", "dash-cpu",
                TsgcHTMLStatGradient.sgBlueViolet);
            string vMem = TsgcHTMLComponent_StatCard.Build("Memory", "1456 MB",
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNeutral, "+48 MB", "dash-mem",
                TsgcHTMLStatGradient.sgBlueAqua);
            string vReq = TsgcHTMLComponent_StatCard.Build("Requests/s", "245",
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stUp, "+12", "dash-req",
                TsgcHTMLStatGradient.sgGreenTeal);
            string vUsers = TsgcHTMLComponent_StatCard.Build("Active Users", "217",
                TsgcHTMLStatColor.scWarning, TsgcHTMLStatTrend.stDown, "-5", "dash-users",
                TsgcHTMLStatGradient.sgPinkRed);
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Live Dashboard", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "Four metric cards update every 2 seconds via out-of-band htmx swaps. " +
                "A single hidden poller drives all four areas simultaneously."));
            oCard.Body.AddRaw(@"<div class=""row g-3 mb-3"">" + @"<div class=""col-md-6"">" +
                vCPU + "</div>" + @"<div class=""col-md-6"">" + vMem + "</div>" + "</div>" +
                @"<div class=""row g-3"">" + @"<div class=""col-md-6"">" + vReq + "</div>" +
                @"<div class=""col-md-6"">" + vUsers + "</div>" + "</div>");
            var oPollDiv = new TsgcHTMLContainer();
            oPollDiv.Attributes =
                @"hx-get=""/demo/dashboard-tick"" hx-trigger=""every 2s"" hx-swap=""none""";
            oCard.Body.Add(oPollDiv);
            return oCard.HTML;
        }

        public static string BuildDashboardTick()
        {
            GHTMXDashTick++;
            int vCPU = 30 + (GHTMXDashTick * 7 + 13) % 50;
            int vMem = 1100 + (GHTMXDashTick * 11 + 7) % 900;
            int vReq = 100 + (GHTMXDashTick * 17 + 3) % 320;
            int vUsers = 150 + (GHTMXDashTick * 5 + 11) % 150;
            string vA, vB, vC, vD;
            if ((GHTMXDashTick % 3) == 0)
                vA = TsgcHTMLComponent_StatCard.Build("CPU Usage",
                    vCPU.ToString(CultureInfo.InvariantCulture) + "%",
                    TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stDown,
                    (1 + GHTMXDashTick % 5).ToString(CultureInfo.InvariantCulture) + "%",
                    "dash-cpu", TsgcHTMLStatGradient.sgBlueViolet);
            else
                vA = TsgcHTMLComponent_StatCard.Build("CPU Usage",
                    vCPU.ToString(CultureInfo.InvariantCulture) + "%",
                    TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stUp,
                    (1 + GHTMXDashTick % 5).ToString(CultureInfo.InvariantCulture) + "%",
                    "dash-cpu", TsgcHTMLStatGradient.sgBlueViolet);
            if ((GHTMXDashTick % 4) == 0)
                vB = TsgcHTMLComponent_StatCard.Build("Memory",
                    vMem.ToString(CultureInfo.InvariantCulture) + " MB",
                    TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stDown,
                    (10 + GHTMXDashTick % 50).ToString(CultureInfo.InvariantCulture) + " MB",
                    "dash-mem", TsgcHTMLStatGradient.sgBlueAqua);
            else
                vB = TsgcHTMLComponent_StatCard.Build("Memory",
                    vMem.ToString(CultureInfo.InvariantCulture) + " MB",
                    TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stUp,
                    (10 + GHTMXDashTick % 50).ToString(CultureInfo.InvariantCulture) + " MB",
                    "dash-mem", TsgcHTMLStatGradient.sgBlueAqua);
            if ((GHTMXDashTick % 2) == 0)
                vC = TsgcHTMLComponent_StatCard.Build("Requests/s",
                    vReq.ToString(CultureInfo.InvariantCulture), TsgcHTMLStatColor.scSuccess,
                    TsgcHTMLStatTrend.stUp,
                    "+" + (1 + GHTMXDashTick % 20).ToString(CultureInfo.InvariantCulture),
                    "dash-req", TsgcHTMLStatGradient.sgGreenTeal);
            else
                vC = TsgcHTMLComponent_StatCard.Build("Requests/s",
                    vReq.ToString(CultureInfo.InvariantCulture), TsgcHTMLStatColor.scSuccess,
                    TsgcHTMLStatTrend.stDown,
                    "-" + (1 + GHTMXDashTick % 20).ToString(CultureInfo.InvariantCulture),
                    "dash-req", TsgcHTMLStatGradient.sgGreenTeal);
            if ((GHTMXDashTick % 5) == 0)
                vD = TsgcHTMLComponent_StatCard.Build("Active Users",
                    vUsers.ToString(CultureInfo.InvariantCulture), TsgcHTMLStatColor.scWarning,
                    TsgcHTMLStatTrend.stDown,
                    "-" + (1 + GHTMXDashTick % 10).ToString(CultureInfo.InvariantCulture),
                    "dash-users", TsgcHTMLStatGradient.sgPinkRed);
            else
                vD = TsgcHTMLComponent_StatCard.Build("Active Users",
                    vUsers.ToString(CultureInfo.InvariantCulture), TsgcHTMLStatColor.scWarning,
                    TsgcHTMLStatTrend.stUp,
                    "+" + (1 + GHTMXDashTick % 10).ToString(CultureInfo.InvariantCulture),
                    "dash-users", TsgcHTMLStatGradient.sgPinkRed);
            return TsgcHTMX_Fragment.Build("dash-cpu", vA, TsgcHTMXSwapMode.hxInnerHTML) +
                TsgcHTMX_Fragment.Build("dash-mem", vB, TsgcHTMXSwapMode.hxInnerHTML) +
                TsgcHTMX_Fragment.Build("dash-req", vC, TsgcHTMXSwapMode.hxInnerHTML) +
                TsgcHTMX_Fragment.Build("dash-users", vD, TsgcHTMXSwapMode.hxInnerHTML);
        }

        // -------------------------------------------------------------------
        // 2. Smart Search
        // -------------------------------------------------------------------

        public static string BuildDemoSearch()
        {
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Smart Search", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "Type to filter 12 products with a 300 ms debounce. " +
                "Combine text search with a category filter. No page reload."));
            string vCatHTML;
            var oCatSelect = new TsgcHTMLComponent_Select();
            try
            {
                oCatSelect.ElementName = "cat";
                oCatSelect.SelectID = "cat-select";
                oCatSelect.Label_ = "";
                oCatSelect.AddOption("", "All Categories", true);
                oCatSelect.AddOption("Electronics", "Electronics");
                oCatSelect.AddOption("Books", "Books");
                oCatSelect.AddOption("Kitchen", "Kitchen");
                vCatHTML = oCatSelect.HTML;
            }
            finally
            {
                oCatSelect.Dispose();
            }
            var oForm = new TsgcHTMLForm();
            oForm.Method = "get";
            oForm.Attributes =
                @"hx-get=""/demo/search-results"" hx-target=""#search-results"" " +
                @"hx-swap=""innerHTML"" hx-trigger=""keyup changed delay:300ms " +
                @"from:input[name=q], change from:select[name=cat]""";
            var oSearchField = new TsgcHTMLField(TsgcHTMLInputType.itSearch, "q");
            oSearchField.Placeholder = "Search products...";
            oSearchField.Label_ = "Search";
            oSearchField.FieldID = "search-q";
            oForm.Add(oSearchField);
            oForm.AddRaw(vCatHTML);
            oCard.Body.Add(oForm);
            var oResultsDiv = new TsgcHTMLContainer();
            oResultsDiv.ID = "search-results";
            oResultsDiv.CSSClass = "mt-3";
            oResultsDiv.AddRaw(BuildSearchResults("", ""));
            oCard.Body.Add(oResultsDiv);
            return oCard.HTML;
        }

        public static string BuildSearchResults(string aQ, string aCat)
        {
            string vQ = (aQ ?? "").Trim().ToLowerInvariant();
            string vC = (aCat ?? "").Trim().ToLowerInvariant();
            int vCount = 0;
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-hover table-sm";
            oTable.Responsive = true;
            oTable.TheadClass = "table-light";
            oTable.AddColumn("Product");
            oTable.AddColumn("Category");
            oTable.AddColumn("Price");
            for (int i = 0; i <= 11; i++)
            {
                if (vQ != "" && CS_PROD_NAMES[i].ToLowerInvariant().IndexOf(vQ,
                    StringComparison.Ordinal) < 0)
                    continue;
                if (vC != "" && CS_PROD_CATS[i].ToLowerInvariant() != vC)
                    continue;
                TsgcHTMLTableRow oRow = oTable.AddRow();
                oRow.AddCellText(CS_PROD_NAMES[i]);
                string vBadgeColor = ProdCatColor(CS_PROD_CATS[i]);
                var oBadge = new TsgcHTMLContainer("span");
                oBadge.CSSClass = "badge bg-" + vBadgeColor;
                oBadge.AddText(CS_PROD_CATS[i]);
                oRow.AddCell().Add(oBadge);
                oRow.AddCellText(FormatCents(CS_PROD_PRICES[i]));
                vCount++;
            }
            if (vCount > 0)
            {
                string vSuffix = vCount == 1 ? "" : "s";
                return @"<div class=""mb-2""><span class=""badge bg-secondary"">" +
                    vCount.ToString(CultureInfo.InvariantCulture) + " result" + vSuffix +
                    "</span></div>" + oTable.HTML;
            }
            var oAlert = new TsgcHTMLAlert("No products match your search criteria.");
            oAlert.Style = TsgcHTMLAlertStyle.asWarning;
            return oAlert.HTML;
        }

        // -------------------------------------------------------------------
        // 3. Step Wizard
        // -------------------------------------------------------------------

        public static string BuildDemoWizard()
        {
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Step Wizard", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "A three-step registration form. Each Next/Back button replaces only " +
                "the form area. Progress and state are managed server-side, zero JS."));
            var oContentDiv = new TsgcHTMLContainer();
            oContentDiv.ID = "wizard-content";
            oContentDiv.AddRaw(BuildWizardStep(1, "", "", "", ""));
            oCard.Body.Add(oContentDiv);
            return oCard.HTML;
        }

        public static string BuildWizardStep(int aStep, string aName, string aEmail,
            string aRole, string aTeam)
        {
            string vEnName = HtmlEnc(aName);
            string vEnEmail = HtmlEnc(aEmail);
            string vEnRole = HtmlEnc(aRole);
            string vEnTeam = HtmlEnc(aTeam);
            string vL1 = "text-muted small";
            string vL2 = "text-muted small";
            string vL3 = "text-muted small";
            int vPct;
            switch (aStep)
            {
                case 1:
                    vPct = 33;
                    vL1 = "fw-bold text-primary small";
                    break;
                case 2:
                    vPct = 66;
                    vL2 = "fw-bold text-primary small";
                    break;
                default:
                    vPct = 100;
                    vL3 = "fw-bold text-primary small";
                    break;
            }
            string vProgress = @"<div class=""progress mb-2"" style=""height:6px;"">" +
                @"<div class=""progress-bar bg-primary"" role=""progressbar""" +
                @" style=""width:" + vPct.ToString(CultureInfo.InvariantCulture) +
                @"%;transition:width .4s"">" + "</div></div>" +
                @"<div class=""d-flex justify-content-between small mb-3"">" +
                @"<span class=""" + vL1 + @""">1 Account</span>" + @"<span class=""" + vL2 +
                @""">2 Profile</span>" + @"<span class=""" + vL3 +
                @""">3 Confirm</span></div>";
            string vForm;
            switch (aStep)
            {
                case 1:
                    vForm = @"<div class=""mb-3""><label class=""form-label fw-semibold"">" +
                        @"Full Name</label><input type=""text"" name=""wname""" +
                        @" class=""form-control"" placeholder=""e.g. Jane Smith"" value=""" +
                        vEnName + @"""></div>" +
                        @"<div class=""mb-4""><label class=""form-label fw-semibold"">" +
                        @"Email Address</label><input type=""email"" name=""wemail""" +
                        @" class=""form-control"" placeholder=""you@example.com"" value=""" +
                        vEnEmail + @"""></div>" + @"<div class=""d-flex justify-content-end"">" +
                        @"<button class=""btn btn-primary""" +
                        @" hx-get=""/demo/wizard-step?step=2""" +
                        @" hx-target=""#wizard-content"" hx-swap=""innerHTML""" +
                        @" hx-include=""[name=wname],[name=wemail]"">Next &rarr;</button></div>";
                    break;
                case 2:
                    vForm = @"<input type=""hidden"" name=""wname"" value=""" + vEnName + @""">" +
                        @"<input type=""hidden"" name=""wemail"" value=""" + vEnEmail + @""">" +
                        @"<div class=""mb-3""><label class=""form-label fw-semibold"">Role" +
                        @"</label><select name=""wrole"" class=""form-select"">" +
                        @"<option value=""Developer""" + SelAttr("Developer", aRole) +
                        ">Developer</option>" + @"<option value=""Designer""" +
                        SelAttr("Designer", aRole) + ">Designer</option>" +
                        @"<option value=""Manager""" + SelAttr("Manager", aRole) +
                        ">Manager</option>" + @"<option value=""Analyst""" +
                        SelAttr("Analyst", aRole) + ">Analyst</option></select></div>" +
                        @"<div class=""mb-4""><label class=""form-label fw-semibold"">Team" +
                        @"</label><input type=""text"" name=""wteam"" class=""form-control""" +
                        @" placeholder=""e.g. Frontend, Backend"" value=""" + vEnTeam +
                        @"""></div>" + @"<div class=""d-flex justify-content-between"">" +
                        @"<button class=""btn btn-secondary""" +
                        @" hx-get=""/demo/wizard-step?step=1""" +
                        @" hx-target=""#wizard-content"" hx-swap=""innerHTML""" +
                        @" hx-include=""[name=wname],[name=wemail]"">" + "&larr; Back</button>" +
                        @"<button class=""btn btn-primary""" +
                        @" hx-get=""/demo/wizard-step?step=3""" +
                        @" hx-target=""#wizard-content"" hx-swap=""innerHTML""" +
                        @" hx-include=""[name=wname],[name=wemail],[name=wrole],[name=wteam]"">" +
                        "Next &rarr;</button></div>";
                    break;
                case 3:
                    {
                        string vTeamDisp;
                        if (aTeam == "")
                            vTeamDisp = "(none)";
                        else
                            vTeamDisp = vEnTeam;
                        vForm = @"<input type=""hidden"" name=""wname"" value=""" + vEnName +
                            @""">" + @"<input type=""hidden"" name=""wemail"" value=""" +
                            vEnEmail + @""">" + @"<input type=""hidden"" name=""wrole"" value=""" +
                            vEnRole + @""">" + @"<input type=""hidden"" name=""wteam"" value=""" +
                            vEnTeam + @""">" + @"<div class=""alert alert-info mb-3"">" +
                            "<strong>Review your details before submitting.</strong>" +
                            @"</div><table class=""table table-bordered mb-4""><tbody>" +
                            @"<tr><th class=""w-25"">Name</th><td>" + vEnName + "</td></tr>" +
                            "<tr><th>Email</th><td>" + vEnEmail + "</td></tr>" +
                            "<tr><th>Role</th><td>" + vEnRole + "</td></tr>" +
                            "<tr><th>Team</th><td>" + vTeamDisp + "</td></tr>" +
                            "</tbody></table>" +
                            @"<div class=""d-flex justify-content-between"">" +
                            @"<button class=""btn btn-secondary""" +
                            @" hx-get=""/demo/wizard-step?step=2""" +
                            @" hx-target=""#wizard-content"" hx-swap=""innerHTML""" +
                            @" hx-include=""[name=wname],[name=wemail],[name=wrole],[name=wteam]"">" +
                            "&larr; Back</button>" + @"<button class=""btn btn-success""" +
                            @" hx-get=""/demo/wizard-step?step=4""" +
                            @" hx-target=""#wizard-content"" hx-swap=""innerHTML""" +
                            @" hx-include=""[name=wname]"">Submit</button></div>";
                    }
                    break;
                case 4:
                    vForm = @"<div class=""text-center py-4"">" +
                        @"<div class=""display-4 text-success mb-3"">&#10004;</div>" +
                        "<h4>Registration complete!</h4>" +
                        @"<p class=""text-muted"">Welcome, " + vEnName +
                        ". Your account is ready.</p>" +
                        @"<button class=""btn btn-outline-primary mt-2""" +
                        @" hx-get=""/demo/wizard-step?step=1""" +
                        @" hx-target=""#wizard-content"" hx-swap=""innerHTML"">" +
                        "Start Over</button></div>";
                    break;
                default:
                    vForm = "";
                    break;
            }
            return vProgress + vForm;
        }

        // -------------------------------------------------------------------
        // 4. Activity Feed
        // -------------------------------------------------------------------

        public static string BuildDemoFeed()
        {
            GHTMXFeedTick = 0;
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Activity Feed", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "New activities prepend to the top of the feed every 3 seconds. " +
                "The container polls the server automatically using htmx."));
            var oPollDiv = new TsgcHTMLContainer();
            oPollDiv.Attributes = @"hx-get=""/demo/feed-tick"" hx-trigger=""every 3s""" +
                @" hx-target=""#feed-list"" hx-swap=""afterbegin""";
            var oFeedDiv = new TsgcHTMLContainer();
            oFeedDiv.ID = "feed-list";
            oFeedDiv.CSSClass = "feed-scroll";
            oFeedDiv.AddRaw(MakeFeedItem(4, 6, "1 min ago") + MakeFeedItem(3, 4, "3 min ago") +
                MakeFeedItem(2, 2, "5 min ago") + MakeFeedItem(1, 1, "7 min ago") +
                MakeFeedItem(0, 0, "9 min ago"));
            oCard.Body.Add(oPollDiv);
            oCard.Body.Add(oFeedDiv);
            return oCard.HTML;
        }

        public static string BuildFeedTick()
        {
            GHTMXFeedTick++;
            int vUserIdx = GHTMXFeedTick % 6;
            int vActionIdx = (GHTMXFeedTick * 3 + 1) % 8;
            return MakeFeedItem(vUserIdx, vActionIdx, DateTime.Now.ToString("HH:mm:ss",
                CultureInfo.InvariantCulture));
        }

        // -------------------------------------------------------------------
        // 5. Inline Edit
        // -------------------------------------------------------------------

        public static string BuildDemoInlineEdit()
        {
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Inline Edit", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "Click Edit on any row to replace cells with an input form in-place. " +
                "Save commits the new value; Cancel restores the original."));
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-striped table-hover";
            oTable.Responsive = true;
            oTable.TheadClass = "table-light";
            oTable.AddColumn("#");
            oTable.AddColumn("Label");
            oTable.AddColumn("Value");
            oTable.AddColumn("Action");
            for (int i = 0; i <= 4; i++)
            {
                TsgcHTMLTableRow oRow = oTable.AddRow();
                oRow.AddCellText(CS_EDIT_IDS[i]);
                oRow.AddCellText(CS_EDIT_LABELS[i]);
                oRow.AddCellText(CS_EDIT_VALS[i]);
                TsgcHTMLTableCell oCell = oRow.AddCell();
                var oBtn = new TsgcHTMLButton("Edit");
                oBtn.Style = TsgcHTMLButtonStyle.bsWarning;
                oBtn.ButtonType = "button";
                oBtn.CSSClass = "btn-sm";
                oBtn.Attributes = @"hx-get=""/demo/edit-form?id=" + CS_EDIT_IDS[i] + "&v=" +
                    HtmlEnc(CS_EDIT_VALS[i]) + @""" " +
                    @"hx-target=""closest tr"" hx-swap=""innerHTML""";
                oCell.Add(oBtn);
            }
            oCard.Body.AddRaw(oTable.HTML);
            return oCard.HTML;
        }

        public static string BuildEditForm(string aID, string aV)
        {
            int vIdx = StrToIntDef(aID, 1) - 1;
            if (vIdx < 0 || vIdx > 4)
                vIdx = 0;
            var oCells = new TsgcHTMLNodeList();
            var oCell = new TsgcHTMLTableCell();
            oCell.AddText(aID);
            oCells.Add(oCell);
            oCell = new TsgcHTMLTableCell();
            oCell.AddText(CS_EDIT_LABELS[vIdx]);
            oCells.Add(oCell);
            oCell = new TsgcHTMLTableCell();
            var oField = new TsgcHTMLField(TsgcHTMLInputType.itText, "v");
            oField.FieldID = "edit-val-" + aID;
            oField.Value = aV;
            oField.Label_ = "";
            oCell.Add(oField);
            oCells.Add(oCell);
            oCell = new TsgcHTMLTableCell();
            var oSaveBtn = new TsgcHTMLButton("Save");
            oSaveBtn.Style = TsgcHTMLButtonStyle.bsSuccess;
            oSaveBtn.ButtonType = "button";
            oSaveBtn.CSSClass = "btn-sm me-1";
            oSaveBtn.Attributes = @"hx-get=""/demo/edit-save?id=" + aID + @""" " +
                @"hx-include=""#edit-val-" + aID + @""" " +
                @"hx-target=""closest tr"" hx-swap=""innerHTML""";
            oCell.Add(oSaveBtn);
            var oCancelBtn = new TsgcHTMLButton("Cancel");
            oCancelBtn.Style = TsgcHTMLButtonStyle.bsSecondary;
            oCancelBtn.ButtonType = "button";
            oCancelBtn.CSSClass = "btn-sm";
            oCancelBtn.Attributes = @"hx-get=""/demo/edit-cancel?id=" + aID + "&v=" +
                HtmlEnc(aV) + @""" " + @"hx-target=""closest tr"" hx-swap=""innerHTML""";
            oCell.Add(oCancelBtn);
            oCells.Add(oCell);
            return oCells.HTML;
        }

        public static string BuildEditView(string aID, string aV)
        {
            int vIdx = StrToIntDef(aID, 1) - 1;
            if (vIdx < 0 || vIdx > 4)
                vIdx = 0;
            var oCells = new TsgcHTMLNodeList();
            var oCell = new TsgcHTMLTableCell();
            oCell.AddText(aID);
            oCells.Add(oCell);
            oCell = new TsgcHTMLTableCell();
            oCell.AddText(CS_EDIT_LABELS[vIdx]);
            oCells.Add(oCell);
            oCell = new TsgcHTMLTableCell();
            oCell.AddText(aV);
            oCells.Add(oCell);
            oCell = new TsgcHTMLTableCell();
            var oBtn = new TsgcHTMLButton("Edit");
            oBtn.Style = TsgcHTMLButtonStyle.bsWarning;
            oBtn.ButtonType = "button";
            oBtn.CSSClass = "btn-sm";
            oBtn.Attributes = @"hx-get=""/demo/edit-form?id=" + aID + "&v=" + HtmlEnc(aV) +
                @""" " + @"hx-target=""closest tr"" hx-swap=""innerHTML""";
            oCell.Add(oBtn);
            oCells.Add(oCell);
            return oCells.HTML;
        }

        // -------------------------------------------------------------------
        // 6. Live Validation
        // -------------------------------------------------------------------

        public static string BuildDemoValidate()
        {
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Real-time Validation", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "Each field validates on keyup (400 ms debounce). " +
                "The server returns instant feedback without a page reload."));
            var oEmailForm = new TsgcHTMLForm();
            oEmailForm.Method = "get";
            oEmailForm.Attributes =
                @"hx-get=""/demo/validate-email"" hx-target=""#email-feedback"" " +
                @"hx-swap=""innerHTML"" " +
                @"hx-trigger=""keyup changed delay:400ms from:input[name=email]""";
            var oEmailField = new TsgcHTMLField(TsgcHTMLInputType.itEmail, "email");
            oEmailField.Label_ = "Email address";
            oEmailField.Placeholder = "user@example.com";
            oEmailField.FieldID = "email-input";
            oEmailForm.Add(oEmailField);
            var oEmailFb = new TsgcHTMLContainer();
            oEmailFb.ID = "email-feedback";
            oEmailFb.CSSClass = "mt-1";
            oEmailForm.Add(oEmailFb);
            var oUsernameForm = new TsgcHTMLForm();
            oUsernameForm.Method = "get";
            oUsernameForm.CSSClass = "mt-3";
            oUsernameForm.Attributes =
                @"hx-get=""/demo/validate-username"" hx-target=""#username-feedback"" " +
                @"hx-swap=""innerHTML"" " +
                @"hx-trigger=""keyup changed delay:400ms from:input[name=username]""";
            var oUsernameField = new TsgcHTMLField(TsgcHTMLInputType.itText, "username");
            oUsernameField.Label_ = "Username";
            oUsernameField.Placeholder = "3-20 chars, letters/digits/_";
            oUsernameField.FieldID = "username-input";
            oUsernameForm.Add(oUsernameField);
            var oUserFb = new TsgcHTMLContainer();
            oUserFb.ID = "username-feedback";
            oUserFb.CSSClass = "mt-1";
            oUsernameForm.Add(oUserFb);
            var oWrap = new TsgcHTMLContainer();
            oWrap.Add(oEmailForm);
            oWrap.Add(oUsernameForm);
            oCard.Body.Add(oWrap);
            return oCard.HTML;
        }

        public static string BuildValidateEmail(string aEmail)
        {
            if ((aEmail ?? "").Trim() == "")
                return "";
            bool vOK = (aEmail.IndexOf('@') > 0) && (aEmail.IndexOf('.') > 1) &&
                (aEmail.Length > 5);
            var oAlert = new TsgcHTMLAlert("");
            if (vOK)
            {
                oAlert.AddText("Valid email address.");
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
            }
            else
            {
                oAlert.AddText("Enter a valid email (must contain @ and a domain).");
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
            }
            return oAlert.HTML;
        }

        public static string BuildValidateUsername(string aUsername)
        {
            if ((aUsername ?? "").Trim() == "")
                return "";
            int vLen = aUsername.Length;
            bool vOK = (vLen >= 3) && (vLen <= 20);
            if (vOK)
            {
                for (int i = 0; i < vLen; i++)
                {
                    char c = aUsername[i];
                    if (!(((c >= 'A') && (c <= 'Z')) || ((c >= 'a') && (c <= 'z')) ||
                        ((c >= '0') && (c <= '9')) || (c == '_')))
                    {
                        vOK = false;
                        break;
                    }
                }
            }
            var oAlert = new TsgcHTMLAlert("");
            if (vOK)
            {
                oAlert.AddText("Username is available!");
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
            }
            else
            {
                oAlert.AddText("Username must be 3-20 chars, letters/digits/_ only.");
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
            }
            return oAlert.HTML;
        }

        // -------------------------------------------------------------------
        // 7. Delete Row
        // -------------------------------------------------------------------

        public static string BuildDemoDeleteRow()
        {
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Delete Row", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "Click Delete on any row. htmx sends a DELETE request; on success the " +
                "row removes itself from the DOM with no page reload."));
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-striped table-hover";
            oTable.Responsive = true;
            oTable.TheadClass = "table-light";
            oTable.AddColumn("Name");
            oTable.AddColumn("Department");
            oTable.AddColumn("Role");
            oTable.AddColumn("Action");
            for (int i = 0; i <= 4; i++)
            {
                TsgcHTMLTableRow oRow = oTable.AddRow();
                oRow.AddCellText(CS_DEL_NAMES[i]);
                oRow.AddCellText(CS_DEL_DEPTS[i]);
                oRow.AddCellText(CS_DEL_ROLES[i]);
                TsgcHTMLTableCell oCell = oRow.AddCell();
                var oBtn = new TsgcHTMLButton("Delete");
                oBtn.Style = TsgcHTMLButtonStyle.bsDanger;
                oBtn.ButtonType = "button";
                oBtn.CSSClass = "btn-sm";
                oBtn.Attributes = @"hx-delete=""/demo/delete-row?id=" +
                    (i + 1).ToString(CultureInfo.InvariantCulture) + @""" " +
                    @"hx-confirm=""Delete " + HtmlEnc(CS_DEL_NAMES[i]) + @"?"" " +
                    @"hx-swap=""none"" " +
                    @"hx-on:htmx:after-request=""if(event.detail.successful)" +
                    "this.closest('tr').remove()\"";
                oCell.Add(oBtn);
            }
            oCard.Body.AddRaw(oTable.HTML);
            return oCard.HTML;
        }

        // -------------------------------------------------------------------
        // 8. Infinite Scroll
        // -------------------------------------------------------------------

        public static string BuildDemoInfiniteScroll()
        {
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Infinite Scroll", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "First 10 rows render immediately. Scrolling near the bottom triggers " +
                "htmx to load the next batch. 60 employees total."));
            string vGridHTML;
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.VirtualScroll = true;
                oGrid.VirtualScrollURL = "/demo/scroll-rows";
                oGrid.VisibleRows = 10;
                oGrid.TableID = "htmx-scroll-grid";
                TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
                oCol.Name = "name";
                oCol.Title = "Name";
                oCol.Width = "160";
                oCol = oGrid.Columns.Add();
                oCol.Name = "dept";
                oCol.Title = "Department";
                oCol.Width = "130";
                oCol = oGrid.Columns.Add();
                oCol.Name = "city";
                oCol.Title = "City";
                oCol.Width = "110";
                oCol = oGrid.Columns.Add();
                oCol.Name = "position";
                oCol.Title = "Position";
                oCol.Width = "130";
                oCol = oGrid.Columns.Add();
                oCol.Name = "salary";
                oCol.Title = "Salary";
                oCol.Width = "90";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                for (int i = 0; i <= 59; i++)
                {
                    string vN = CS_SCR_FN[i % 10] + " " + CS_SCR_LN[(i / 10) % 10];
                    string vD = CS_SCR_DEPT[i % 5];
                    string vC = CS_SCR_CITY[i % 10];
                    string vP = CS_SCR_POS[i % 5];
                    string vS = "$" + (60000 + (i % 5) * 5000 + (i / 10) * 1000)
                        .ToString(CultureInfo.InvariantCulture);
                    oGrid.AddRow(vN, vD, vC, vP, vS);
                }
                vGridHTML = oGrid.HTML;
            }
            finally
            {
                oGrid.Dispose();
            }
            oCard.Body.AddRaw(vGridHTML);
            return oCard.HTML;
        }

        public static string BuildScrollRows(int aOffset, string aRootId)
        {
            int vEnd = aOffset + 9;
            if (vEnd > 59)
                vEnd = 59;
            string vRoot = aRootId;
            if (vRoot == "")
                vRoot = "htmx-scroll-grid-vswrap";
            var oResult = new StringBuilder();
            for (int i = aOffset; i <= vEnd; i++)
            {
                string vN = CS_SCR_FN[i % 10] + " " + CS_SCR_LN[(i / 10) % 10];
                string vD = CS_SCR_DEPT[i % 5];
                string vC = CS_SCR_CITY[i % 10];
                string vP = CS_SCR_POS[i % 5];
                string vS = "$" + (60000 + (i % 5) * 5000 + (i / 10) * 1000)
                    .ToString(CultureInfo.InvariantCulture);
                var oRow = new TsgcHTMLTableRow();
                oRow.AddCellText(vN);
                oRow.AddCellText(vD);
                oRow.AddCellText(vC);
                oRow.AddCellText(vP);
                oRow.AddCellText(vS, "text-end");
                oResult.Append(oRow.HTML);
            }
            if (vEnd < 59)
            {
                var oSentinel = new TsgcHTMLContainer("tr");
                oSentinel.Attributes = @"hx-get=""/demo/scroll-rows?offset=" +
                    (vEnd + 1).ToString(CultureInfo.InvariantCulture) + "&rootId=" + vRoot +
                    @""" " + @"hx-trigger=""intersect root:#" + vRoot + @""" " +
                    @"hx-swap=""outerHTML""";
                var oTd = new TsgcHTMLContainer("td");
                oTd.Attributes = @"colspan=""5""";
                oTd.CSSClass = "text-center py-2 text-muted";
                oTd.AddRaw(TsgcHTMLComponent_Spinner.Build(TsgcHTMLSpinnerType.spBorder,
                    "secondary", TsgcHTMLSpinnerSize.ssNormal));
                oSentinel.Add(oTd);
                oResult.Append(oSentinel.HTML);
            }
            else
            {
                var oSentinel = new TsgcHTMLContainer("tr");
                var oTd = new TsgcHTMLContainer("td");
                oTd.Attributes = @"colspan=""5""";
                oTd.CSSClass = "text-center py-2 text-muted small";
                oTd.AddText("All 60 employees loaded.");
                oSentinel.Add(oTd);
                oResult.Append(oSentinel.HTML);
            }
            return oResult.ToString();
        }

        // -------------------------------------------------------------------
        // 9. Shopping Cart
        // -------------------------------------------------------------------

        public static string BuildDemoCart()
        {
            for (int i = 1; i <= 6; i++)
                GHTMXCartData[i - 1] = 0;
            var oProdGrid = new StringBuilder();
            oProdGrid.Append(@"<div class=""row g-2"">");
            for (int i = 1; i <= 6; i++)
            {
                string vBgColor = ProdCatColor(CS_PROD_CATS[i - 1]);
                oProdGrid.Append(@"<div class=""col-6""><div class=""card h-100 shadow-sm"">" +
                    @"<div class=""card-body p-2"">" +
                    @"<div class=""d-flex justify-content-between align-items-start"">" +
                    @"<h6 class=""card-title mb-1 small"">" + CS_PROD_NAMES[i - 1] + "</h6>" +
                    @"<span class=""badge bg-" + vBgColor + @" ms-1 small"">" +
                    CS_PROD_CATS[i - 1] + "</span></div>" +
                    @"<div class=""fw-bold text-success"">" +
                    FormatCents(CS_PROD_PRICES[i - 1]) + "</div></div>" +
                    @"<div class=""card-footer p-1"">" +
                    @"<button class=""btn btn-sm btn-primary w-100""" +
                    @" hx-get=""/demo/cart-add?id=" +
                    i.ToString(CultureInfo.InvariantCulture) + @"""" +
                    @" hx-target=""#cart-panel"" hx-swap=""innerHTML"">" +
                    "Add to Cart</button></div></div></div>");
            }
            oProdGrid.Append("</div>");
            var oCard = new TsgcHTMLCard();
            oCard.Header.AddRaw(
                @"<h5 class=""mb-0 d-flex align-items-center gap-2"">Shopping Cart" +
                @"<span id=""cart-badge"" class=""badge bg-primary rounded-pill"">0</span>" +
                "</h5>");
            oCard.Body.Add(new TsgcHTMLParagraph(
                "Click Add to Cart on any product. The cart panel and badge update " +
                "instantly via htmx. State is held server-side."));
            oCard.Body.AddRaw(@"<div class=""row"">" + @"<div class=""col-lg-8 mb-3"">" +
                oProdGrid.ToString() + "</div>" + @"<div class=""col-lg-4"">" +
                @"<div class=""card shadow-sm"">" +
                @"<div class=""card-header fw-bold small"">Your Cart</div>" +
                @"<div class=""card-body"" id=""cart-panel"">" + GetCartContent() +
                "</div></div></div></div>");
            return oCard.HTML;
        }

        public static string BuildCartAdd(string aProductId)
        {
            int vId = StrToIntDef(aProductId, 0);
            if (vId >= 1 && vId <= 6)
                GHTMXCartData[vId - 1] = GHTMXCartData[vId - 1] + 1;
            int vTotal = CartTotalQty();
            return GetCartContent() + TsgcHTMX_Fragment.Build("cart-badge",
                vTotal.ToString(CultureInfo.InvariantCulture), TsgcHTMXSwapMode.hxInnerHTML);
        }

        public static string BuildCartRemove(string aProductId)
        {
            int vId = StrToIntDef(aProductId, 0);
            if (vId >= 1 && vId <= 6)
            {
                if (GHTMXCartData[vId - 1] > 0)
                    GHTMXCartData[vId - 1] = GHTMXCartData[vId - 1] - 1;
                else
                    GHTMXCartData[vId - 1] = 0;
            }
            int vTotal = CartTotalQty();
            return GetCartContent() + TsgcHTMX_Fragment.Build("cart-badge",
                vTotal.ToString(CultureInfo.InvariantCulture), TsgcHTMXSwapMode.hxInnerHTML);
        }

        // -------------------------------------------------------------------
        // 10. Cascade Select
        // -------------------------------------------------------------------

        public static string BuildDemoCascade()
        {
            var oCard = new TsgcHTMLCard();
            oCard.Header.Add(new TsgcHTMLHeading("Cascade Select", 5));
            oCard.Body.Add(new TsgcHTMLParagraph(
                "Choosing a country fires an htmx request; the server returns " +
                "matching cities and the city dropdown updates immediately."));
            string vCountryHTML;
            var oCountry = new TsgcHTMLComponent_Select();
            try
            {
                oCountry.ElementName = "country";
                oCountry.SelectID = "country-select";
                oCountry.Label_ = "Country";
                oCountry.AddOption("", "-- Select country --", true);
                oCountry.AddOption("us", "United States");
                oCountry.AddOption("uk", "United Kingdom");
                oCountry.AddOption("de", "Germany");
                oCountry.AddOption("fr", "France");
                oCountry.AddOption("jp", "Japan");
                vCountryHTML = oCountry.HTML;
            }
            finally
            {
                oCountry.Dispose();
            }
            string vCityHTML;
            var oCity = new TsgcHTMLComponent_Select();
            try
            {
                oCity.ElementName = "city";
                oCity.SelectID = "city-select";
                oCity.Label_ = "City";
                oCity.Disabled = true;
                oCity.AddOption("", "-- Select country first --", true);
                vCityHTML = oCity.HTML;
            }
            finally
            {
                oCity.Dispose();
            }
            var oForm = new TsgcHTMLForm();
            oForm.Method = "get";
            oForm.Attributes =
                @"hx-get=""/demo/cascade-cities"" hx-target=""#city-wrapper"" " +
                @"hx-swap=""innerHTML"" " + @"hx-trigger=""change from:select#country-select"" " +
                @"hx-include=""[name=country]""";
            oForm.AddRaw(vCountryHTML);
            var oCityWrapper = new TsgcHTMLContainer();
            oCityWrapper.ID = "city-wrapper";
            oCityWrapper.CSSClass = "mt-2";
            oCityWrapper.AddRaw(vCityHTML);
            oForm.Add(oCityWrapper);
            oCard.Body.Add(oForm);
            return oCard.HTML;
        }

        public static string BuildCascadeCities(string aCountry)
        {
            string[] CS_CITIES_US = { "New York", "Los Angeles", "Chicago", "Houston" };
            string[] CS_CITIES_UK = { "London", "Manchester", "Birmingham", "Edinburgh" };
            string[] CS_CITIES_DE = { "Berlin", "Munich", "Hamburg", "Frankfurt" };
            string[] CS_CITIES_FR = { "Paris", "Lyon", "Marseille", "Toulouse" };
            string[] CS_CITIES_JP = { "Tokyo", "Osaka", "Kyoto", "Fukuoka" };
            string vC = (aCountry ?? "").Trim().ToLowerInvariant();
            bool vFound = false;
            var oCity = new TsgcHTMLComponent_Select();
            try
            {
                oCity.ElementName = "city";
                oCity.SelectID = "city-select";
                oCity.Label_ = "City";
                oCity.AddOption("", "-- Select city --", true);
                if (vC == "us")
                {
                    for (int i = 0; i <= 3; i++)
                        oCity.AddOption(CS_CITIES_US[i].ToLowerInvariant(), CS_CITIES_US[i]);
                    vFound = true;
                }
                else if (vC == "uk")
                {
                    for (int i = 0; i <= 3; i++)
                        oCity.AddOption(CS_CITIES_UK[i].ToLowerInvariant(), CS_CITIES_UK[i]);
                    vFound = true;
                }
                else if (vC == "de")
                {
                    for (int i = 0; i <= 3; i++)
                        oCity.AddOption(CS_CITIES_DE[i].ToLowerInvariant(), CS_CITIES_DE[i]);
                    vFound = true;
                }
                else if (vC == "fr")
                {
                    for (int i = 0; i <= 3; i++)
                        oCity.AddOption(CS_CITIES_FR[i].ToLowerInvariant(), CS_CITIES_FR[i]);
                    vFound = true;
                }
                else if (vC == "jp")
                {
                    for (int i = 0; i <= 3; i++)
                        oCity.AddOption(CS_CITIES_JP[i].ToLowerInvariant(), CS_CITIES_JP[i]);
                    vFound = true;
                }
                oCity.Disabled = !vFound;
                return oCity.HTML;
            }
            finally
            {
                oCity.Dispose();
            }
        }

        public static string BuildProductFooter()
        {
            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "border-top mt-4 py-3 text-center text-muted small";
            oFooter.Style = "margin-left:220px";
            var oP = new TsgcHTMLContainer("p");
            oP.CSSClass = "mb-0";
            oP.AddText("Built with ");
            var oLink = new TsgcHTMLLink("https://www.esegece.com/products/sgchtml/", "sgcHTML");
            oLink.Target = "_blank";
            oLink.Rel = "noopener";
            oLink.CSSClass = "fw-semibold text-decoration-none";
            oP.Add(oLink);
            oP.AddText(" components for Delphi and C++Builder.");
            oFooter.Add(oP);
            return oFooter.HTML;
        }
    }
}
