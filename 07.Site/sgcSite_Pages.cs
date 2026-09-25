// ***************************************************************************
//  sgcSite - Site Layouts web demo (managed port)
//  Port of delphi\Demos\60.HTML\07.Site\sgcSiteDemo_Pages.pas
//
//  Renders TsgcHTMLComponent_Site (6 layouts, 5 color presets, 3 modes) with a
//  navigation menu that routes to five DIFFERENT page bodies:
//    dashboard  - stat cards + a recent-orders table
//    customers  - a customers table with status badges
//    orders     - a full orders table with status badges
//    reports    - KPI stat cards + a dependency-free mini bar chart
//    settings   - a preferences form (fields, selects, switches)
//  A switcher card lets you change layout / theme / mode while staying on the
//  current page (each switch URL carries the current page + the other two
//  dimensions). Every menu item routes to its page while preserving the
//  current layout/theme/mode; the active page's item is highlighted.
//
//  ALL markup is built with the sgcHTML node / component layer (containers,
//  cards, tables, badges, buttons, forms, the mini bar chart and the stat-card
//  component). No hand-written HTML-tag string literals appear in the content
//  builders; the only exception is the Bootstrap-icon "<i class="bi ...">"
//  markup, which is passed exclusively to AddMenu / StatCard.Icon. Mirrors the
//  Delphi sgcSiteDemo_Pages BuildDemo 1:1.
//
//  SPA-feel navigation: the menu and switcher links load with htmx (Boost) and
//  the body is morphed, with a top loading bar, view transitions and an error
//  toast when a request fails. The page template of the Site gets the options
//  through OnPrepareTemplate.
// ***************************************************************************

using System;
// sgc
using esegece.sgcWebSockets;

namespace Site
{
    public static class TsgcSiteDemoNavigation
    {
        public static void DoPrepareTemplate(object Sender, TsgcHTMLTemplate_Bootstrap aTemplate)
        {
            aTemplate.Boost = true;
            aTemplate.DefaultSwap = "morph:innerHTML";
            aTemplate.LoadingIndicator = true;
            aTemplate.ViewTransitions = true;
            aTemplate.ErrorToast = true;
        }
    }

    public static class TsgcSiteDemoPages
    {
        private static readonly string[] CS_PAGE_VALUES = new string[]
        {
            "dashboard", "customers", "orders", "reports", "settings"
        };

        private static readonly string[] CS_LAYOUT_VALUES = new string[]
        {
            "sidebar-left", "sidebar-right", "topnav", "topnav-sidebar",
            "iconrail", "offcanvas"
        };

        private static readonly string[] CS_LAYOUT_LABELS = new string[]
        {
            "Sidebar Left", "Sidebar Right", "Top Nav", "Top Nav + Sidebar",
            "Icon Rail", "Offcanvas"
        };

        private static readonly string[] CS_THEME_VALUES = new string[]
        {
            "blue", "violet", "emerald", "slate", "dark"
        };

        private static readonly string[] CS_THEME_LABELS = new string[]
        {
            "Blue", "Violet", "Emerald", "Slate", "Dark"
        };

        private static readonly string[] CS_MODE_VALUES = new string[]
        {
            "light", "dark", "system"
        };

        private static readonly string[] CS_MODE_LABELS = new string[]
        {
            "Light", "Dark", "System"
        };

        // Bootstrap-icon markup (the accepted icon exception, exactly as the menu
        // already uses). Only ever passed to StatCard.Icon / AddMenu.
        private const string CS_BI_USERS = "<i class=\"bi bi-people\"></i>";
        private const string CS_BI_REVENUE = "<i class=\"bi bi-cash-stack\"></i>";
        private const string CS_BI_ORDERS = "<i class=\"bi bi-bag\"></i>";
        private const string CS_BI_UPTIME = "<i class=\"bi bi-activity\"></i>";
        private const string CS_BI_MRR = "<i class=\"bi bi-graph-up-arrow\"></i>";
        private const string CS_BI_NEWCUST = "<i class=\"bi bi-person-plus\"></i>";
        private const string CS_BI_CHURN = "<i class=\"bi bi-arrow-down-right\"></i>";
        private const string CS_BI_AVGORDER = "<i class=\"bi bi-receipt\"></i>";
        private const string CS_BI_MENU_DASH = "<i class=\"bi bi-speedometer2\"></i>";
        private const string CS_BI_MENU_CUST = "<i class=\"bi bi-people\"></i>";
        private const string CS_BI_MENU_ORDERS = "<i class=\"bi bi-bag\"></i>";
        private const string CS_BI_MENU_REPORTS = "<i class=\"bi bi-graph-up\"></i>";
        private const string CS_BI_MENU_SETTINGS = "<i class=\"bi bi-gear\"></i>";

        // --- node-composition helpers (no custom HTML markup) --- //

        // One KPI stat card wrapped in a responsive grid column. The StatCard is a
        // TsgcHTMLComponent: build it and capture its HTML into the column node
        // (mirrors the Delphi StatCardCol; .NET is GC-managed, so nothing is freed).
        private static string StatCardCol(string aTitle, string aValue, string aIcon,
            TsgcHTMLStatColor aColor, TsgcHTMLStatTrend aTrend, string aTrendValue,
            string aColClass)
        {
            TsgcHTMLContainer oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = aColClass;
            TsgcHTMLComponent_StatCard oStat = new TsgcHTMLComponent_StatCard();
            oStat.Title = aTitle;
            oStat.Value = aValue;
            oStat.Icon = aIcon;
            oStat.Color = aColor;
            oStat.Trend = aTrend;
            oStat.TrendValue = aTrendValue;
            oStat.CSSClass = "h-100";
            oCol.AddRaw(oStat.HTML);
            return oCol.HTML;
        }

        // Append a status cell holding a coloured badge node to a table row.
        private static void AddBadgeCell(TsgcHTMLTableRow aRow, string aText,
            TsgcHTMLBadgeStyle aStyle)
        {
            TsgcHTMLTableCell oCell = aRow.AddCell();
            TsgcHTMLBadge oBadge = new TsgcHTMLBadge(aText);
            oBadge.Style = aStyle;
            oCell.Add(oBadge);
        }

        private static string NormPage(string aPage)
        {
            for (int vI = 0; vI < CS_PAGE_VALUES.Length; vI++)
                if (string.Equals(aPage, CS_PAGE_VALUES[vI],
                    StringComparison.OrdinalIgnoreCase))
                    return CS_PAGE_VALUES[vI];
            return "dashboard";
        }

        private static string NormLayout(string aLayout)
        {
            for (int vI = 0; vI < CS_LAYOUT_VALUES.Length; vI++)
                if (string.Equals(aLayout, CS_LAYOUT_VALUES[vI],
                    StringComparison.OrdinalIgnoreCase))
                    return CS_LAYOUT_VALUES[vI];
            return "topnav-sidebar";
        }

        private static string NormTheme(string aTheme)
        {
            for (int vI = 0; vI < CS_THEME_VALUES.Length; vI++)
                if (string.Equals(aTheme, CS_THEME_VALUES[vI],
                    StringComparison.OrdinalIgnoreCase))
                    return CS_THEME_VALUES[vI];
            return "blue";
        }

        private static string NormMode(string aMode)
        {
            for (int vI = 0; vI < CS_MODE_VALUES.Length; vI++)
                if (string.Equals(aMode, CS_MODE_VALUES[vI],
                    StringComparison.OrdinalIgnoreCase))
                    return CS_MODE_VALUES[vI];
            return "light";
        }

        // page first, then layout/theme/mode.
        private static string BuildURL(string aPage, string aLayout, string aTheme,
            string aMode)
        {
            return "/?page=" + aPage + "&layout=" + aLayout + "&theme=" + aTheme +
                "&mode=" + aMode;
        }

        private static string LayoutDescription(string aLayout)
        {
            if (aLayout == "sidebar-left")
                return "A fixed left sidebar holds the brand and navigation, " +
                    "content fills the rest of the width.";
            if (aLayout == "sidebar-right")
                return "The navigation sidebar sits on the right side, " +
                    "content flows on the left.";
            if (aLayout == "topnav")
                return "A classic horizontal top navigation bar, " +
                    "ideal for portals and landing pages.";
            if (aLayout == "iconrail")
                return "A narrow icon-only rail on the left maximizes " +
                    "the content area, great for dense dashboards.";
            if (aLayout == "offcanvas")
                return "The menu is hidden in a slide-in offcanvas panel, " +
                    "toggled from the top bar, ideal for mobile-first apps.";
            return "A top bar plus a left sidebar, the most common " +
                "admin layout combining branding, quick actions and navigation.";
        }

        private static string BuildSwitcher(string aPage, string aLayout, string aTheme,
            string aMode)
        {
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-4";
            oCard.Header.AddElement("strong", "Layout Switcher");

            // aWhich: 0 vary layout, 1 vary theme, 2 vary mode. Each button keeps
            // the current page (and the other two dimensions) so switching never
            // navigates away from the page you are on.
            void AddSwitchRow(string aLabel, string[] aValues, string[] aLabels,
                string aCurrent, int aWhich)
            {
                TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
                oWrap.CSSClass = "mb-3";

                TsgcHTMLContainer oLbl = new TsgcHTMLContainer("div");
                oLbl.CSSClass = "text-muted small text-uppercase fw-bold mb-1";
                oLbl.AddText(aLabel);
                oWrap.Add(oLbl);

                TsgcHTMLContainer oGroup = new TsgcHTMLContainer("div");
                oGroup.CSSClass = "d-flex flex-wrap gap-2";
                for (int vJ = 0; vJ < aValues.Length; vJ++)
                {
                    string vURL;
                    switch (aWhich)
                    {
                        case 0:
                            vURL = BuildURL(aPage, aValues[vJ], aTheme, aMode);
                            break;
                        case 1:
                            vURL = BuildURL(aPage, aLayout, aValues[vJ], aMode);
                            break;
                        default:
                            vURL = BuildURL(aPage, aLayout, aTheme, aValues[vJ]);
                            break;
                    }

                    TsgcHTMLButtonStyle vStyle;
                    if (string.Equals(aValues[vJ], aCurrent,
                        StringComparison.OrdinalIgnoreCase))
                        vStyle = TsgcHTMLButtonStyle.bsPrimary;
                    else
                        vStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;

                    TsgcHTMLButton oBtn = new TsgcHTMLButton(aLabels[vJ], vStyle);
                    oBtn.Href = vURL;
                    oBtn.CSSClass = "btn-sm";
                    oGroup.Add(oBtn);
                }

                oWrap.Add(oGroup);
                oCard.Body.Add(oWrap);
            }

            AddSwitchRow("Layout", CS_LAYOUT_VALUES, CS_LAYOUT_LABELS, aLayout, 0);
            AddSwitchRow("Theme", CS_THEME_VALUES, CS_THEME_LABELS, aTheme, 1);
            AddSwitchRow("Mode", CS_MODE_VALUES, CS_MODE_LABELS, aMode, 2);

            TsgcHTMLParagraph oPara = new TsgcHTMLParagraph(LayoutDescription(aLayout));
            oPara.CSSClass = "mb-0 text-muted";
            oCard.Body.Add(oPara);

            return oCard.HTML;
        }

        private static string BuildDashboardBody()
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.Add(new TsgcHTMLHeading("Dashboard", 1));

            TsgcHTMLContainer oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCardCol("Users", "12,480", CS_BI_USERS,
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stUp, "+8.2%",
                "col-6 col-lg-3"));
            oRow.AddRaw(StatCardCol("Revenue", "$48.9k", CS_BI_REVENUE,
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stUp, "+3.1%",
                "col-6 col-lg-3"));
            oRow.AddRaw(StatCardCol("Orders", "1,204", CS_BI_ORDERS,
                TsgcHTMLStatColor.scWarning, TsgcHTMLStatTrend.stDown, "-1.4%",
                "col-6 col-lg-3"));
            oRow.AddRaw(StatCardCol("Uptime", "99.98%", CS_BI_UPTIME,
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, "",
                "col-6 col-lg-3"));
            oRoot.Add(oRow);

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-4";
            oCard.Title = "Recent Orders";

            TsgcHTMLTable oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-striped table-hover align-middle mb-0";
            oTable.AddColumn("#");
            oTable.AddColumn("Customer");
            oTable.AddColumn("Product");
            oTable.AddColumn("Total", "text-end");
            oTable.AddColumn("Status");

            TsgcHTMLTableRow oTR = oTable.AddRow();
            oTR.AddCellText("1001");
            oTR.AddCellText("Alice Johnson");
            oTR.AddCellText("Wireless Headphones");
            oTR.AddCellText("$89.99", "text-end");
            AddBadgeCell(oTR, "Paid", TsgcHTMLBadgeStyle.bgSuccess);

            oTR = oTable.AddRow();
            oTR.AddCellText("1002");
            oTR.AddCellText("Bob Smith");
            oTR.AddCellText("Mechanical Keyboard");
            oTR.AddCellText("$129.99", "text-end");
            AddBadgeCell(oTR, "Pending", TsgcHTMLBadgeStyle.bgWarning);

            oTR = oTable.AddRow();
            oTR.AddCellText("1003");
            oTR.AddCellText("Carol White");
            oTR.AddCellText("LED Desk Lamp");
            oTR.AddCellText("$39.99", "text-end");
            AddBadgeCell(oTR, "Paid", TsgcHTMLBadgeStyle.bgSuccess);

            oTR = oTable.AddRow();
            oTR.AddCellText("1004");
            oTR.AddCellText("Dave Brown");
            oTR.AddCellText("USB Hub 7-Port");
            oTR.AddCellText("$24.99", "text-end");
            AddBadgeCell(oTR, "Refunded", TsgcHTMLBadgeStyle.bgSecondary);

            oTR = oTable.AddRow();
            oTR.AddCellText("1005");
            oTR.AddCellText("Emma Davis");
            oTR.AddCellText("Webcam HD 1080p");
            oTR.AddCellText("$49.99", "text-end");
            AddBadgeCell(oTR, "Paid", TsgcHTMLBadgeStyle.bgSuccess);

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            return oRoot.HTML;
        }

        private static string BuildCustomersBody()
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.Add(new TsgcHTMLHeading("Customers", 1));

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-4";
            oCard.Title = "All Customers";

            TsgcHTMLTable oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-striped table-hover align-middle mb-0";
            oTable.AddColumn("Name");
            oTable.AddColumn("Email");
            oTable.AddColumn("Company");
            oTable.AddColumn("Status");

            TsgcHTMLTableRow oTR = oTable.AddRow();
            oTR.AddCellText("Alice Johnson");
            oTR.AddCellText("alice@northwind.com");
            oTR.AddCellText("Northwind Traders");
            AddBadgeCell(oTR, "Active", TsgcHTMLBadgeStyle.bgSuccess);

            oTR = oTable.AddRow();
            oTR.AddCellText("Bob Smith");
            oTR.AddCellText("bob@contoso.com");
            oTR.AddCellText("Contoso Ltd");
            AddBadgeCell(oTR, "Active", TsgcHTMLBadgeStyle.bgSuccess);

            oTR = oTable.AddRow();
            oTR.AddCellText("Carol White");
            oTR.AddCellText("carol@fabrikam.com");
            oTR.AddCellText("Fabrikam Inc");
            AddBadgeCell(oTR, "Lead", TsgcHTMLBadgeStyle.bgWarning);

            oTR = oTable.AddRow();
            oTR.AddCellText("Dave Brown");
            oTR.AddCellText("dave@adventure.com");
            oTR.AddCellText("Adventure Works");
            AddBadgeCell(oTR, "Active", TsgcHTMLBadgeStyle.bgSuccess);

            oTR = oTable.AddRow();
            oTR.AddCellText("Emma Davis");
            oTR.AddCellText("emma@wideworld.com");
            oTR.AddCellText("Wide World Importers");
            AddBadgeCell(oTR, "Lead", TsgcHTMLBadgeStyle.bgWarning);

            oTR = oTable.AddRow();
            oTR.AddCellText("Frank Miller");
            oTR.AddCellText("frank@tailspin.com");
            oTR.AddCellText("Tailspin Toys");
            AddBadgeCell(oTR, "Churned", TsgcHTMLBadgeStyle.bgSecondary);

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            return oRoot.HTML;
        }

        private static string BuildOrdersBody()
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.Add(new TsgcHTMLHeading("Orders", 1));

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-4";
            oCard.Title = "All Orders";

            TsgcHTMLTable oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-striped table-hover align-middle mb-0";
            oTable.AddColumn("#");
            oTable.AddColumn("Customer");
            oTable.AddColumn("Product");
            oTable.AddColumn("Date");
            oTable.AddColumn("Total", "text-end");
            oTable.AddColumn("Status");

            void AddOrder(string aNum, string aCustomer, string aProduct, string aDate,
                string aTotal, string aStatus, TsgcHTMLBadgeStyle aStyle)
            {
                TsgcHTMLTableRow oTR = oTable.AddRow();
                oTR.AddCellText(aNum);
                oTR.AddCellText(aCustomer);
                oTR.AddCellText(aProduct);
                oTR.AddCellText(aDate);
                oTR.AddCellText(aTotal, "text-end");
                AddBadgeCell(oTR, aStatus, aStyle);
            }

            AddOrder("1001", "Alice Johnson", "Wireless Headphones", "2026-06-21",
                "$89.99", "Paid", TsgcHTMLBadgeStyle.bgSuccess);
            AddOrder("1002", "Bob Smith", "Mechanical Keyboard", "2026-06-22",
                "$129.99", "Pending", TsgcHTMLBadgeStyle.bgWarning);
            AddOrder("1003", "Carol White", "LED Desk Lamp", "2026-06-22", "$39.99",
                "Paid", TsgcHTMLBadgeStyle.bgSuccess);
            AddOrder("1004", "Dave Brown", "USB Hub 7-Port", "2026-06-23", "$24.99",
                "Refunded", TsgcHTMLBadgeStyle.bgSecondary);
            AddOrder("1005", "Emma Davis", "Webcam HD 1080p", "2026-06-24", "$49.99",
                "Paid", TsgcHTMLBadgeStyle.bgSuccess);
            AddOrder("1006", "Frank Miller", "Laptop Stand", "2026-06-25", "$34.99",
                "Pending", TsgcHTMLBadgeStyle.bgWarning);
            AddOrder("1007", "Grace Lee", "Noise Cancelling Earbuds", "2026-06-26",
                "$119.00", "Paid", TsgcHTMLBadgeStyle.bgSuccess);
            AddOrder("1008", "Henry Ford", "Portable SSD 1TB", "2026-06-27", "$99.50",
                "Refunded", TsgcHTMLBadgeStyle.bgSecondary);

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            return oRoot.HTML;
        }

        private static string BuildReportsBody()
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.Add(new TsgcHTMLHeading("Reports", 1));

            TsgcHTMLContainer oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            oRow.AddRaw(StatCardCol("MRR", "$12.4k", CS_BI_MRR,
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stUp, "+5.4%",
                "col-6 col-lg-3"));
            oRow.AddRaw(StatCardCol("New Customers", "128", CS_BI_NEWCUST,
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stUp, "+12",
                "col-6 col-lg-3"));
            oRow.AddRaw(StatCardCol("Churn", "1.8%", CS_BI_CHURN,
                TsgcHTMLStatColor.scWarning, TsgcHTMLStatTrend.stDown, "-0.3%",
                "col-6 col-lg-3"));
            oRow.AddRaw(StatCardCol("Avg Order", "$57", CS_BI_AVGORDER,
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stUp, "+2.1%",
                "col-6 col-lg-3"));
            oRoot.Add(oRow);

            TsgcHTMLMiniBarChart oChart = new TsgcHTMLMiniBarChart();
            oChart.Title = "Revenue by Month";
            oChart.Height = "220px";
            oChart.BarClass = "bg-primary";
            oChart.AddBar("Jan", 8.2, "$8.2k");
            oChart.AddBar("Feb", 9.1, "$9.1k");
            oChart.AddBar("Mar", 10.4, "$10.4k");
            oChart.AddBar("Apr", 9.8, "$9.8k");
            oChart.AddBar("May", 11.6, "$11.6k");
            oChart.AddBar("Jun", 12.5, "$12.5k");
            oChart.AddBar("Jul", 13.9, "$13.9k");
            oChart.AddBar("Aug", 15.2, "$15.2k");
            oRoot.AddRaw(oChart.HTML);

            return oRoot.HTML;
        }

        private static string BuildSettingsBody()
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.Add(new TsgcHTMLHeading("Settings", 1));

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mb-4";
            oCard.Title = "Preferences";

            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/settings";

            TsgcHTMLContainer oGrid = new TsgcHTMLContainer("div");
            oGrid.CSSClass = "row";

            TsgcHTMLField oName = new TsgcHTMLField(TsgcHTMLInputType.itText,
                "display_name");
            oName.FieldID = "display_name";
            oName.Label_ = "Display Name";
            oName.Value = "Demo User";
            oName.Placeholder = "Your name";
            oName.ColClass = "col-md-6 mb-3";
            oGrid.Add(oName);

            TsgcHTMLField oEmail = new TsgcHTMLField(TsgcHTMLInputType.itEmail, "email");
            oEmail.FieldID = "email";
            oEmail.Label_ = "Email";
            oEmail.Value = "demo@esegece.com";
            oEmail.Placeholder = "you@example.com";
            oEmail.ColClass = "col-md-6 mb-3";
            oGrid.Add(oEmail);

            TsgcHTMLSelect oRole = new TsgcHTMLSelect();
            oRole.Name = "role";
            oRole.FieldID = "role";
            oRole.Label_ = "Role";
            oRole.ColClass = "col-md-6 mb-3";
            oRole.AddOption("admin", "Admin", true);
            oRole.AddOption("editor", "Editor");
            oRole.AddOption("viewer", "Viewer");
            oGrid.Add(oRole);

            TsgcHTMLSelect oTheme = new TsgcHTMLSelect();
            oTheme.Name = "theme_pref";
            oTheme.FieldID = "theme_pref";
            oTheme.Label_ = "Theme";
            oTheme.ColClass = "col-md-6 mb-3";
            oTheme.AddOption("blue", "Blue", true);
            oTheme.AddOption("violet", "Violet");
            oTheme.AddOption("emerald", "Emerald");
            oTheme.AddOption("slate", "Slate");
            oTheme.AddOption("dark", "Dark");
            oGrid.Add(oTheme);

            oForm.Add(oGrid);

            TsgcHTMLCheckbox oChk1 = new TsgcHTMLCheckbox("notifications");
            oChk1.FieldID = "notifications";
            oChk1.Label_ = "Email notifications";
            oChk1.Switch = true;
            oChk1.Checked = true;
            oChk1.WrapperClass = "form-check form-switch mb-2";
            oForm.Add(oChk1);

            TsgcHTMLCheckbox oChk2 = new TsgcHTMLCheckbox("compact");
            oChk2.FieldID = "compact";
            oChk2.Label_ = "Compact mode";
            oChk2.Switch = true;
            oChk2.WrapperClass = "form-check form-switch mb-3";
            oForm.Add(oChk2);

            TsgcHTMLButton oSubmit = new TsgcHTMLButton("Save changes",
                TsgcHTMLButtonStyle.bsPrimary);
            oSubmit.ButtonType = "submit";
            oForm.Add(oSubmit);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            return oRoot.HTML;
        }

        public static string BuildDemo(string aPage, string aLayout, string aTheme,
            string aMode)
        {
            string vPage = NormPage(aPage);
            string vLayout = NormLayout(aLayout);
            string vTheme = NormTheme(aTheme);
            string vMode = NormMode(aMode);

            TsgcHTMLComponent_Site oSite = new TsgcHTMLComponent_Site();
            oSite.Title = "sgcHTML Site Layouts";
            oSite.OnPrepareTemplate += TsgcSiteDemoNavigation.DoPrepareTemplate;

            // Layout
            if (vLayout == "sidebar-left")
                oSite.Layout = TsgcHTMLSiteLayout.slSidebarLeft;
            else if (vLayout == "sidebar-right")
                oSite.Layout = TsgcHTMLSiteLayout.slSidebarRight;
            else if (vLayout == "topnav")
                oSite.Layout = TsgcHTMLSiteLayout.slTopNav;
            else if (vLayout == "iconrail")
                oSite.Layout = TsgcHTMLSiteLayout.slIconRail;
            else if (vLayout == "offcanvas")
                oSite.Layout = TsgcHTMLSiteLayout.slOffcanvas;
            else
                oSite.Layout = TsgcHTMLSiteLayout.slTopNavSidebarLeft;

            // Theme preset
            if (vTheme == "violet")
                oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpViolet;
            else if (vTheme == "emerald")
                oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpEmerald;
            else if (vTheme == "slate")
                oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpSlate;
            else if (vTheme == "dark")
                oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpDark;
            else
                oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpBlue;

            // Theme mode
            if (vMode == "dark")
                oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmDark;
            else if (vMode == "system")
                oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmSystem;
            else
                oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmLight;

            oSite.Brand.Text = "eSeGeCe";
            oSite.Brand.Href = "/";
            oSite.Header.ShowThemeSwitcher = true;
            oSite.Header.ShowUser = true;
            oSite.Header.UserName = "Demo User";
            oSite.Footer.Text = "(c) 2026 eSeGeCe.com";
            oSite.CustomHead =
                "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css\">";

            // Menu: each item routes to a DIFFERENT page while preserving the
            // current layout/theme/mode. The item matching the active page is
            // highlighted.
            oSite.AddMenu("Dashboard", BuildURL("dashboard", vLayout, vTheme, vMode),
                CS_BI_MENU_DASH).Active = (vPage == "dashboard");
            oSite.AddMenu("Customers", BuildURL("customers", vLayout, vTheme, vMode),
                CS_BI_MENU_CUST).Active = (vPage == "customers");
            oSite.AddMenu("Orders", BuildURL("orders", vLayout, vTheme, vMode),
                CS_BI_MENU_ORDERS).Active = (vPage == "orders");
            oSite.AddMenu("Reports", BuildURL("reports", vLayout, vTheme, vMode),
                CS_BI_MENU_REPORTS).Active = (vPage == "reports");
            oSite.AddMenu("Settings", BuildURL("settings", vLayout, vTheme, vMode),
                CS_BI_MENU_SETTINGS).Active = (vPage == "settings");

            // Content: the switcher card (carries the current page) then the
            // page-specific body, both produced by node-tree builders.
            oSite.AddContent(BuildSwitcher(vPage, vLayout, vTheme, vMode));
            if (vPage == "customers")
                oSite.AddContent(BuildCustomersBody());
            else if (vPage == "orders")
                oSite.AddContent(BuildOrdersBody());
            else if (vPage == "reports")
                oSite.AddContent(BuildReportsBody());
            else if (vPage == "settings")
                oSite.AddContent(BuildSettingsBody());
            else
                oSite.AddContent(BuildDashboardBody());

            return oSite.HTML;
        }
    }
}
