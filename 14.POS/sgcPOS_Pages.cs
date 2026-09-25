// ***************************************************************************
//  sgcPOS - retail point of sale web-app demo (node-based view layer)
//  Port of delphi\Demos\60.HTML\01.RunTime\14.POS\sgcPOS_Pages.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
// View layer of the till and its back office.
//
// Built COMPONENT FIRST: every screen is composed of TsgcHTMLComponent_*
// objects (NumPad, CameraScanner, CommandPalette, EmptyState, Grid,
// DataTable, Chart, Heatmap, PivotTable, Gauge, StatCard, Carousel, Splitter,
// Modal, Toast, Snackbar, ...), with the node layer used only as glue: rows,
// columns, cards and the spacing between them. There is no hand-written HTML
// string anywhere in this unit; every AddRaw takes either the rendered .HTML
// of a component/node or an escaped value.
//
// One instance is shared by every HTTP thread, so it holds no per-request
// state: the aggregate component CSS is computed once in the constructor and
// read-only afterwards, and anything a component event needs is carried by a
// short-lived provider object created inside the Build* call.
//
// Managed port notes:
// * .NET is GC-managed, so the Delphi manual .Free / try-finally-Free
//   scaffolding is dropped.
// * The Delphi TsgcHTMLComponentCSS protected-access shim (sgcPOS_Pages.pas:187)
//   is dropped too: TsgcHTMLComponent.GetCSS() is already public in the managed
//   library (src\sgcHTML\HTML\sgcHTML_Component.cs:198).
// * Managed component constructors are parameterless, so the Delphi Create(nil)
//   argument goes away.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text;
// sgc
using esegece.sgcWebSockets;

namespace POS
{
    // Everything the shell needs to draw itself, so no Build* has to carry six
    // string parameters that only the navbar reads. The Delphi record
    // (sgcPOS_Pages.pas:26) maps to a mutable class here.
    public class TPOSPageCtx
    {
        public long UserId;
        public string DisplayName = "";
        public string Role = "";
        public string Theme = ""; // 'light' | 'dark' | 'system'
        public string Menu = "";  // active nav key
        public string Flash = ""; // one-shot success message
        public string Error = ""; // one-shot error message
        public bool ShiftOpen;
        public long ShiftId;
        public int ParkedCount;
    }

    // Module-level routines and trusted constants of sgcPOS_Pages.pas
    // (lines 202-318 and 1685). Pascal unit-level routines have no C# home, so
    // they are hosted on this static class.
    internal static class POSPagesUtils
    {
        // Trusted inline SVG constants. They are markup, never user data, and are
        // the icons the Toolbar / EmptyState / Timeline components take.
        public const string CS_ICON_CART = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" " +
            "height=\"48\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
            "stroke-width=\"1.5\"><circle cx=\"9\" cy=\"20\" r=\"1.5\"/>" +
            "<circle cx=\"18\" cy=\"20\" r=\"1.5\"/>" +
            "<path d=\"M2 3h3l2.6 12h11.2l1.7-8H6\"/></svg>";

        public const string CS_ICON_PARK = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" " +
            "height=\"48\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
            "stroke-width=\"1.5\"><rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/>" +
            "<path d=\"M8 9h8M8 13h5\"/></svg>";

        public const string CS_ICON_SEARCH = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" " +
            "height=\"48\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
            "stroke-width=\"1.5\"><circle cx=\"11\" cy=\"11\" r=\"7\"/>" +
            "<path d=\"M20 20l-4-4\"/></svg>";

        public const string CS_FAVICON_HINT = "<link rel=\"icon\" type=\"image/svg+xml\" " +
            "href=\"/favicon.svg\">";

        // Page CSS: the rose accent, the till layout, and the one override the
        // Splitter needs to stop being a two-column row on a phone. All of it is
        // CSS, none of it is markup.
        public const string CS_POS_CSS = ":root{--pos-accent:" + POSConst.CS_POS_ACCENT +
            ";--pos-accent-dark:" + POSConst.CS_POS_ACCENT_DARK + ";}" +
            ".pos-accent{color:var(--pos-accent);}" +
            ".pos-bg-accent{background:var(--pos-accent);color:#fff;}" +
            ".btn-pos{background:var(--pos-accent);border-color:var(--pos-accent);" +
            "color:#fff;}" +
            ".btn-pos:hover,.btn-pos:focus{background:var(--pos-accent-dark);" +
            "border-color:var(--pos-accent-dark);color:#fff;}" +
            ".pos-topbar{background:linear-gradient(90deg,var(--pos-accent) 0%," +
            "var(--pos-accent-dark) 100%);color:#fff;}" +
            ".pos-topbar a{color:#fff;}" +
            ".pos-tile{display:flex;flex-direction:column;align-items:stretch;" +
            "justify-content:space-between;width:100%;min-height:104px;" +
            "padding:.5rem;border-radius:.75rem;text-align:left;}" +
            ".pos-tile .pos-tile-name{font-size:.82rem;line-height:1.15;" +
            "font-weight:600;overflow:hidden;overflow-wrap:anywhere;" +
            "display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;}" +
            ".pos-tile .pos-tile-price{font-size:1.05rem;font-weight:700;}" +
            ".pos-tiles{display:grid;gap:.5rem;" +
            "grid-template-columns:repeat(auto-fill,minmax(120px,1fr));}" +
            ".pos-rail{display:flex;flex-direction:column;gap:.35rem;}" +
            ".pos-cart-lines{max-height:44vh;overflow:auto;}" +
            ".pos-total{font-size:1.6rem;font-weight:800;}" +
            ".pos-sticky{position:sticky;top:0;z-index:5;}" +
            // Everything on the till must be reachable with a thumb: no hover-only
            // affordance, and a 44px minimum touch target.
            ".pos-touch .btn{min-height:44px;}" +
            "@media (max-width:991.98px){" +
            ".sgc-splitter{flex-direction:column !important;height:auto !important;}" +
            ".sgc-splitter-pane{flex:1 1 auto !important;overflow:visible !important;}" +
            ".sgc-splitter-gutter{display:none !important;}" +
            ".pos-rail{flex-direction:row;overflow-x:auto;padding-bottom:.25rem;}" +
            ".pos-rail .btn{flex:0 0 auto;}" +
            ".pos-cart-lines{max-height:none;}" +
            ".pos-tiles{grid-template-columns:repeat(auto-fill,minmax(104px,1fr));}" +
            "}";

        // Mirrors Delphi sgcHTMLFloatToStr (sgcHTML_Helpers.pas:1005). The managed
        // library has no public counterpart: the only copy is private inside
        // TsgcHTMLComponent_NumPad (sgcHTML_Component_NumPad.cs:685), so the demo
        // carries its own with the identical contract.
        private const int CS_SGC_HTML_FLOAT_DECIMALS = 6;
        private const int CS_SGC_HTML_FLOAT_MAX_DECIMALS = 15;

        // HTML-escape a value that goes into markup. Everything user-supplied on
        // these pages passes through here or through a component that encodes.
        public static string HtmlEsc(string aValue)
        {
            string vResult = (aValue ?? string.Empty).Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            vResult = vResult.Replace("'", "&#39;");
            return vResult;
        }

        // Short date/time for a table cell. The localized shape is the correct one
        // here: this is text a human reads, so no InvariantCulture.
        public static string WhenStr(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm");
        }

        public static string TimeStr(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return "-";
            return aValue.ToString("HH:mm");
        }

        // Qty as the till shows it: '2' rather than '2.00', and never with a comma.
        public static string QtyStr(double aValue)
        {
            return sgcHTMLFloatToStr(aValue, 3);
        }

        // A '0.##...#' picture (trailing zeros dropped, no thousands separator, no
        // exponent) rendered with '.' as the decimal separator on every locale.
        // InvariantCulture already writes '.', so the Delphi character-rewrite loop
        // (which only undoes the thread locale) is not needed here.
        public static string sgcHTMLFloatToStr(double aValue, int aDecimals)
        {
            int vDecimals = aDecimals;
            if (vDecimals < 0)
                vDecimals = CS_SGC_HTML_FLOAT_DECIMALS;
            if (vDecimals > CS_SGC_HTML_FLOAT_MAX_DECIMALS)
                vDecimals = CS_SGC_HTML_FLOAT_MAX_DECIMALS;

            string vFormat = "0";
            if (vDecimals > 0)
                vFormat = vFormat + "." + new string('#', vDecimals);
            return aValue.ToString(vFormat, CultureInfo.InvariantCulture);
        }

        public static string Initials(string aName)
        {
            string vName = (aName ?? string.Empty).Trim();
            if (vName == "")
                return "?";

            // Delphi Pos() is 1-based, IndexOf() is 0-based.
            int vSpace = vName.IndexOf(' ') + 1;
            if (vSpace > 1)
                return (vName.Substring(0, 1) + vName.Substring(vSpace, 1)).ToUpperInvariant();
            // Delphi Copy() clamps to the string length, Substring() does not.
            if (vName.Length >= 2)
                return vName.Substring(0, 2).ToUpperInvariant();
            return vName.ToUpperInvariant();
        }

        // Renders the read-only line table shared by the payment screen and the
        // on-screen receipt. sgcPOS_Pages.pas:1685.
        public static TsgcHTMLTable BuildLinesTable(TPOSPages aPages, TPOSSaleLine[] aLines)
        {
            var oResult = new TsgcHTMLTable();
            oResult.CSSClass = "table table-sm align-middle mb-0";
            oResult.TheadClass = "table-light";
            oResult.Responsive = true;
            oResult.AddColumn("Item");
            oResult.AddColumn("Qty", "text-center");
            oResult.AddColumn("Unit", "text-end");
            oResult.AddColumn("Tax", "text-end");
            oResult.AddColumn("Total", "text-end");
            if ((aLines == null) || (aLines.Length == 0))
            {
                oResult.AddEmptyRow("No lines", 5);
            }
            else
            {
                for (int vI = 0; vI < aLines.Length; vI++)
                {
                    TsgcHTMLTableRow oRow = oResult.AddRow();
                    oRow.AddCellText(aLines[vI].ProductName);
                    oRow.AddCellText(QtyStr(aLines[vI].Qty), "text-center");
                    oRow.AddCellText(aPages.Money(aLines[vI].UnitPrice), "text-end");
                    oRow.AddCellText(sgcHTMLFloatToStr(aLines[vI].TaxRate, 2) + "%", "text-end");
                    oRow.AddCellText(aPages.Money(aLines[vI].LineTotal), "text-end");
                }
            }
            return oResult;
        }
    }

    // Supplies the per-row Sparkline and the row action buttons of the products
    // grid. The Grid event is a delegate, so it needs a real instance; one is
    // created inside BuildProductsPage, which keeps TPOSPages itself free of
    // per-request state. sgcPOS_Pages.pas:193.
    public class TPOSProductCellProvider
    {
        public long[] Ids;
        public double[][] Trend;

        // Matches TsgcGridCellHTMLEvent (sgcHTML_Component_Grid.cs:177).
        public void GetCellHTML(object Sender, int aRowIndex, int aColIndex,
            string aValue, ref string aHTML)
        {
            aHTML = "";
            // Delphi High() of an empty array is -1, so an unset Ids renders nothing.
            if ((Ids == null) || (aRowIndex < 0) || (aRowIndex > Ids.Length - 1))
                return;
            long vId = Ids[aRowIndex];

            // Column 5 is the 12-week unit trend, column 6 the row actions. Every
            // other column keeps the grid's default escaping.
            if (aColIndex == 5)
            {
                if ((Trend == null) || (aRowIndex > Trend.Length - 1))
                    return;
                var oSpark = new TsgcHTMLComponent_Sparkline();
                oSpark.ChartType = TsgcHTMLSparklineType.slArea;
                oSpark.Width = 90;
                oSpark.Height = 26;
                oSpark.LineColor = POSConst.CS_POS_ACCENT;
                oSpark.FillColor = "rgba(225,29,72,0.18)";
                oSpark.AutoMin = false;
                oSpark.Min = 0;
                oSpark.ShowLastPoint = true;
                if (Trend[aRowIndex] != null)
                {
                    for (int vI = 0; vI < Trend[aRowIndex].Length; vI++)
                        oSpark.AddValue(Trend[aRowIndex][vI]);
                }
                aHTML = oSpark.HTML;
                return;
            }

            if (aColIndex == 6)
            {
                var oRoot = new TsgcHTMLNodeList();
                var oEdit = new TsgcHTMLButton("Edit", TsgcHTMLButtonStyle.bsOutlineSecondary);
                oEdit.ButtonType = "button";
                oEdit.CSSClass = "btn-sm me-1";
                oEdit.Attributes = "hx-get=\"/products/form?id=" +
                    vId.ToString(CultureInfo.InvariantCulture) +
                    "\" hx-target=\"#product-form-panel\" hx-swap=\"innerHTML\"";
                oRoot.Add(oEdit);
                var oDel = new TsgcHTMLButton("Deactivate", TsgcHTMLButtonStyle.bsOutlineDanger);
                oDel.ButtonType = "button";
                oDel.CSSClass = "btn-sm";
                oDel.Attributes = "hx-post=\"/products/delete\" hx-vals='{\"id\": " +
                    vId.ToString(CultureInfo.InvariantCulture) +
                    "}' hx-target=\"#products-grid\" hx-swap=\"innerHTML\" " +
                    "hx-confirm=\"Deactivate this product?\"";
                oRoot.Add(oDel);
                aHTML = oRoot.HTML;
            }
        }
    }

    // Declared partial: the back-office / analytics / admin half of the Delphi
    // unit lives in its own managed file and is merged into this same type.
    public class TPOSPages
    {
        private string FComponentCSS;
        private string FCurrencySymbol;
        private string FStoreName;
        private string FStoreAddress;
        private string FStoreTaxId;
        private decimal FShiftTarget;
        // The Delphi keeps this configured threshold on the page object although no
        // screen reads it: the over-threshold discount decision is taken server-side
        // (sgcPOS_Pages.pas:1245). Mirrored here for parity, so CS0414 is expected.
#pragma warning disable CS0414
        private decimal FDiscountThreshold;
#pragma warning restore CS0414

        // Every sgcHTML component renders its markup from GetHTML and its stylesheet
        // from GetCSS, which a TsgcHTMLPageBuilder would normally collect. These pages
        // compose components by hand, so the whole stylesheet set is gathered once at
        // startup and shipped in the shell. That also means an htmx FRAGMENT can carry
        // any component: its CSS is already on the page that swapped it in.
        private void BuildComponentCSS()
        {
            var oSeen = new List<string>();
            var oCSS = new StringBuilder();

            Action<TsgcHTMLComponent> vTake = delegate(TsgcHTMLComponent aComp)
            {
                string vCSS = aComp.GetCSS();
                string vName = aComp.GetType().Name;
                if ((vCSS != "") && (oSeen.IndexOf(vName) < 0))
                {
                    oSeen.Add(vName);
                    oCSS.Append(vCSS);
                }
            };

            vTake(new TsgcHTMLComponent_NumPad());
            vTake(new TsgcHTMLComponent_CameraScanner());
            vTake(new TsgcHTMLComponent_CommandPalette());
            vTake(new TsgcHTMLComponent_EmptyState());
            vTake(new TsgcHTMLComponent_Modal());
            vTake(new TsgcHTMLComponent_Toast());
            vTake(new TsgcHTMLComponent_Snackbar());
            vTake(new TsgcHTMLComponent_ButtonGroup());
            vTake(new TsgcHTMLComponent_InputGroup());
            vTake(new TsgcHTMLComponent_Badge());
            vTake(new TsgcHTMLComponent_Chip());
            vTake(new TsgcHTMLComponent_Avatar());
            vTake(new TsgcHTMLComponent_Popover());
            vTake(new TsgcHTMLComponent_ContextMenu());
            vTake(new TsgcHTMLComponent_Carousel());
            vTake(new TsgcHTMLComponent_Image());
            vTake(new TsgcHTMLComponent_Panel());
            vTake(new TsgcHTMLComponent_Splitter());
            vTake(new TsgcHTMLComponent_Toolbar());
            vTake(new TsgcHTMLComponent_NavBar());
            vTake(new TsgcHTMLComponent_Dropdown());
            vTake(new TsgcHTMLComponent_Grid());
            vTake(new TsgcHTMLComponent_DataTable());
            vTake(new TsgcHTMLComponent_Pagination());
            vTake(new TsgcHTMLComponent_Tabs());
            vTake(new TsgcHTMLComponent_StatCard());
            vTake(new TsgcHTMLComponent_Chart());
            vTake(new TsgcHTMLComponent_Sparkline());
            vTake(new TsgcHTMLComponent_Gauge());
            vTake(new TsgcHTMLComponent_Heatmap());
            vTake(new TsgcHTMLComponent_PivotTable());
            vTake(new TsgcHTMLComponent_Rating());
            vTake(new TsgcHTMLComponent_Timeline());
            vTake(new TsgcHTMLComponent_AuditTrail());
            vTake(new TsgcHTMLComponent_UserManagement());
            vTake(new TsgcHTMLComponent_RolesPermissions());
            vTake(new TsgcHTMLComponent_Form());
            vTake(new TsgcHTMLComponent_Select());
            vTake(new TsgcHTMLComponent_DateRangePicker());
            vTake(new TsgcHTMLComponent_Login());
            vTake(new TsgcHTMLComponent_WebAuthnLogin());

            FComponentCSS = oCSS.ToString();
        }

        private string WrapTemplate(string aTitle, string aBody, string aTheme)
        {
            var oTpl = new TsgcHTMLTemplate_Bootstrap();
            oTpl.Title = aTitle;
            oTpl.HtmlLang = "en";
            oTpl.Viewport = "width=device-width, initial-scale=1";
            oTpl.HeadNodes.AddRaw(POSPagesUtils.CS_FAVICON_HINT);
            // Every asset is served by this process out of the linked resource, so the
            // till still works with the shop's internet connection down.
            oTpl.HeadNodes.AddScript("/htmx.min.js", true);
            oTpl.HeadNodes.AddScript("/chart.umd.min.js", true);
            if (string.Equals(aTheme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "dark";
                oTpl.DarkMode = true;
            }
            else if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "light";
            }
            else
            {
                // System theme: no data-bs-theme attribute, the script flips it
                // client-side from prefers-color-scheme.
                oTpl.HeadNodes.AddScript(
                    "(function(){if(window.matchMedia&&window.matchMedia(" +
                    "'(prefers-color-scheme: dark)').matches){document.documentElement." +
                    "setAttribute('data-bs-theme','dark');}})();", false);
            }
            oTpl.CustomCSS = FComponentCSS + POSPagesUtils.CS_POS_CSS;
            oTpl.BodyContent = aBody;
            return oTpl.GetHTML();
        }

        private string BuildTopBar(TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLContainer("div");
            oRoot.CSSClass = "pos-topbar d-flex flex-wrap align-items-center " +
                "justify-content-between gap-2 px-3 py-2";

            var oLeft = new TsgcHTMLContainer("div");
            oLeft.CSSClass = "d-flex align-items-center gap-2";
            oRoot.Add(oLeft);
            var oBrand = new TsgcHTMLLink("/", "sgcPOS");
            oBrand.CSSClass = "fs-5 fw-bold text-decoration-none";
            oLeft.Add(oBrand);
            var oShift = new TsgcHTMLComponent_Badge();
            if (aCtx.ShiftOpen)
            {
                oShift.Text = "Shift open";
                oShift.Color = TsgcHTMLBadgeStyle.bgSuccess;
            }
            else
            {
                oShift.Text = "No shift";
                oShift.Color = TsgcHTMLBadgeStyle.bgDark;
            }
            oShift.Pill = true;
            oLeft.AddRaw(oShift.HTML);

            var oRight = new TsgcHTMLContainer("div");
            oRight.CSSClass = "d-flex align-items-center gap-2";
            oRoot.Add(oRight);

            var oAvatar = new TsgcHTMLComponent_Avatar();
            oAvatar.Initials = POSPagesUtils.Initials(aCtx.DisplayName);
            oAvatar.Size = TsgcHTMLAvatarSize.asSmall;
            oAvatar.Color = "#ffffff";
            oAvatar.Status = TsgcHTMLAvatarStatus.atOnline;
            oAvatar.AltText = aCtx.DisplayName;
            oRight.AddRaw(oAvatar.HTML);

            var oName = new TsgcHTMLContainer("span");
            oName.CSSClass = "small d-none d-sm-inline";
            oName.AddText(aCtx.DisplayName);
            oRight.Add(oName);

            var oRole = new TsgcHTMLComponent_Badge();
            oRole.Text = aCtx.Role;
            oRole.Color = TsgcHTMLBadgeStyle.bgDark;
            oRole.Pill = true;
            oRight.AddRaw(oRole.HTML);

            var oTheme = new TsgcHTMLComponent_Dropdown();
            oTheme.ButtonText = "Theme";
            oTheme.ButtonClass = "btn btn-sm btn-light";
            oTheme.DropdownID = "pos-theme";
            TsgcHTMLDropdownItem oItem = oTheme.Items.Add();
            oItem.Text = "Light";
            oItem.Href = "/theme?set=light";
            oItem = oTheme.Items.Add();
            oItem.Text = "Dark";
            oItem.Href = "/theme?set=dark";
            oItem = oTheme.Items.Add();
            oItem.Text = "System";
            oItem.Href = "/theme?set=system";
            oRight.AddRaw(oTheme.HTML);

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/logout";
            oForm.CSSClass = "d-inline";
            var oOut = new TsgcHTMLButton("Sign out", TsgcHTMLButtonStyle.bsLight);
            oOut.ButtonType = "submit";
            oOut.CSSClass = "btn-sm";
            oForm.Add(oOut);
            oRight.Add(oForm);

            return oRoot.HTML;
        }

        private string BuildNav(TPOSPageCtx aCtx)
        {
            // A cashier gets the till and the shift only. Everything else is a back
            // office screen and the dispatcher refuses it as well, this just stops the
            // link being offered.
            bool vBackOffice =
                string.Equals(aCtx.Role, POSConst.CS_ROLE_ADMIN, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aCtx.Role, POSConst.CS_ROLE_MANAGER, StringComparison.OrdinalIgnoreCase);
            var oNav = new TsgcHTMLComponent_NavBar();

            Action<string, string, string> vAddItem =
                delegate(string aText, string aHref, string aKey)
            {
                TsgcHTMLNavItem oItem = oNav.Items.Add();
                oItem.Text = aText;
                oItem.Href = aHref;
                oItem.Active = string.Equals(aCtx.Menu, aKey, StringComparison.OrdinalIgnoreCase);
            };

            oNav.Brand = "";
            oNav.BrandHref = "/";
            oNav.Theme = TsgcHTMLNavBarTheme.ntLight;
            oNav.Expand = TsgcHTMLNavBarExpand.neLarge;
            oNav.NavBarID = "pos-nav";
            oNav.CSSClass = "border-bottom";
            vAddItem("Till", "/", "till");
            vAddItem("Parked", "/till/parked", "parked");
            vAddItem("Shift", "/shift", "shift");
            if (vBackOffice)
            {
                vAddItem("Products", "/products", "products");
                vAddItem("Categories", "/categories", "categories");
                vAddItem("Promotions", "/promotions", "promotions");
                vAddItem("Customers", "/customers", "customers");
                vAddItem("Dashboard", "/dashboard", "dashboard");
                vAddItem("Reports", "/reports", "reports");
                vAddItem("SQL", "/sql", "sql");
            }
            if (string.Equals(aCtx.Role, POSConst.CS_ROLE_ADMIN, StringComparison.OrdinalIgnoreCase))
            {
                vAddItem("Users", "/users", "users");
                vAddItem("Audit", "/audit", "audit");
            }
            return oNav.HTML;
        }

        // Ctrl+K quick actions. Only the entries the signed-in role may actually use
        // are emitted, so the palette never offers a route that answers 403.
        private string BuildCommandPalette(TPOSPageCtx aCtx)
        {
            bool vBackOffice =
                string.Equals(aCtx.Role, POSConst.CS_ROLE_ADMIN, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aCtx.Role, POSConst.CS_ROLE_MANAGER, StringComparison.OrdinalIgnoreCase);
            var oPalette = new TsgcHTMLComponent_CommandPalette();
            oPalette.PaletteID = "pos-palette";
            oPalette.Placeholder = "Type a command, Ctrl+K";
            oPalette.EmptyText = "Nothing matches";
            oPalette.MaxResults = 12;
            oPalette.AddItem("New sale", "/", "", "Till", "Ctrl+K");
            oPalette.AddItem("Parked sales", "/till/parked", "", "Till");
            oPalette.AddItem("Take payment", "/till/pay", "", "Till");
            oPalette.AddItem("Shift, open or close", "/shift", "", "Till");
            if (vBackOffice)
            {
                oPalette.AddItem("Products", "/products", "", "Back office");
                oPalette.AddItem("Categories", "/categories", "", "Back office");
                oPalette.AddItem("Promotions", "/promotions", "", "Back office");
                oPalette.AddItem("Customers", "/customers", "", "Back office");
                oPalette.AddItem("Dashboard", "/dashboard", "", "Analytics");
                oPalette.AddItem("Reports", "/reports", "", "Analytics");
                oPalette.AddItem("The SQL behind the page", "/sql", "", "Analytics");
                oPalette.AddItem("Sales report PDF", "/reports/sales.pdf", "", "Analytics");
            }
            if (string.Equals(aCtx.Role, POSConst.CS_ROLE_ADMIN, StringComparison.OrdinalIgnoreCase))
            {
                oPalette.AddItem("Users and roles", "/users", "", "Admin");
                oPalette.AddItem("Audit trail", "/audit", "", "Admin");
            }
            return oPalette.HTML;
        }

        // The manager gate, drawn as a NumPad inside a Modal. It only collects the
        // PIN: the decision is taken server-side by TPOSDBPool.VerifyManagerPin, so a
        // browser that hides the modal, edits the DOM or posts the form directly gets
        // exactly the same answer.
        private string BuildManagerPinModal(string aModalId, string aTitle,
            string aAction, string aExtraField, string aExtraValue)
        {
            var oBody = new TsgcHTMLNodeList();

            var oInfo = new TsgcHTMLParagraph(
                "A manager or admin PIN is required. The PIN is verified against its " +
                "bcrypt hash on the server.");
            oInfo.CSSClass = "text-muted small";
            oBody.Add(oInfo);

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = aAction;
            oForm.CSSClass = "d-flex flex-column align-items-center gap-2";
            if (aExtraField != "")
                oForm.AddHidden(aExtraField, aExtraValue);

            var oPad = new TsgcHTMLComponent_NumPad();
            oPad.PadID = aModalId + "-pad";
            oPad.FieldName = "pin";
            oPad.Mode = TsgcHTMLNumPadMode.npInteger;
            oPad.ShowDecimalPoint = false;
            oPad.ShowEnter = false;
            oPad.ButtonSize = 56;
            oPad.ColorStyle = TsgcHTMLButtonStyle.bsOutlineDark;
            oForm.AddRaw(oPad.HTML);

            var oSubmit = new TsgcHTMLButton("Authorise", TsgcHTMLButtonStyle.bsDanger);
            oSubmit.ButtonType = "submit";
            oSubmit.CSSClass = "w-100";
            oForm.Add(oSubmit);
            oBody.Add(oForm);

            var oModal = new TsgcHTMLComponent_Modal();
            oModal.ModalID = aModalId;
            oModal.Title = aTitle;
            oModal.Body = oBody.HTML;
            oModal.Centered = true;
            oModal.StaticBackdrop = true;
            return oModal.HTML;
        }

        public TPOSPages(TPOSServerConfig aConfig)
        {
            if (aConfig == null)
                throw new EPOSError("TPOSPages: config is nil");
            FCurrencySymbol = aConfig.CurrencySymbol;
            FStoreName = aConfig.StoreName;
            FStoreAddress = aConfig.StoreAddress;
            FStoreTaxId = aConfig.StoreTaxId;
            FShiftTarget = aConfig.ShiftTarget;
            FDiscountThreshold = aConfig.DiscountPinThreshold;
            BuildComponentCSS();
        }

        // Money as the cashier reads it: symbol + a locale-independent amount.
        public string Money(decimal aValue)
        {
            return FCurrencySymbol + POSDB.POSMoneyStr(aValue);
        }

        // Shared shell: navbar + nav + flash + body.
        public string BuildPageShell(string aTitle, string aBodyHTML, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildTopBar(aCtx));
            oRoot.AddRaw(BuildNav(aCtx));

            var oMain = new TsgcHTMLContainer("main");
            oMain.CSSClass = "container-fluid px-3 py-3 pos-touch";
            oRoot.Add(oMain);

            TsgcHTMLAlert oAlert;
            if (aCtx.Error != "")
            {
                oAlert = new TsgcHTMLAlert(aCtx.Error);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oAlert.Dismissible = true;
                oAlert.CSSClass = "mb-3";
                oMain.Add(oAlert);
            }
            if (aCtx.Flash != "")
            {
                oAlert = new TsgcHTMLAlert(aCtx.Flash);
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                oAlert.Dismissible = true;
                oAlert.CSSClass = "mb-3";
                oMain.Add(oAlert);
            }

            oMain.AddRaw(aBodyHTML);

            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "text-center text-muted small py-3 border-top";
            oFooter.AddRaw("Built with sgcHTML &middot; " + POSPagesUtils.HtmlEsc(FStoreName) +
                " &middot; copyright &copy; 2026 eSeGeCe");
            oRoot.Add(oFooter);

            oRoot.AddRaw(BuildCommandPalette(aCtx));

            return WrapTemplate(aTitle + " - sgcPOS", oRoot.HTML, aCtx.Theme);
        }

        // ----- auth ----- //

        public string BuildLoginPage(string aTheme, string aError, string aUser, string aPassword)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "d-flex align-items-center justify-content-center " +
                "min-vh-100 px-3";
            oRoot.Add(oWrap);

            var oCard = new TsgcHTMLContainer("div");
            oCard.CSSClass = "w-100";
            oCard.Style = "max-width:420px;";
            oWrap.Add(oCard);

            var oLogin = new TsgcHTMLComponent_Login();
            oLogin.FormAction = "/login";
            oLogin.FormMethod = "POST";
            oLogin.LoginStyle = TsgcHTMLLoginStyle.lsCard;
            oLogin.Title = "sgcPOS";
            oLogin.Subtitle = FStoreName;
            oLogin.UserLabel = "User";
            oLogin.PasswordLabel = "Password";
            oLogin.ButtonText = "Sign in";
            oLogin.ButtonStyleEnum = TsgcHTMLButtonStyle.bsDanger;
            oLogin.UserValue = aUser;
            oLogin.PasswordValue = aPassword;
            oLogin.UserAutocomplete = "username";
            oLogin.PasswordAutocomplete = "current-password";
            oLogin.ErrorMessage = aError;
            oLogin.MaxWidth = "420px";
            oCard.AddRaw(oLogin.HTML);

            var oAuthn = new TsgcHTMLComponent_WebAuthnLogin();
            oAuthn.Mode = TsgcHTMLWebAuthnMode.wamAuthenticate;
            oAuthn.AuthenticateURL = "/passkey/login";
            oAuthn.CallbackURL = "/";
            oAuthn.Title = "";
            oAuthn.Description = "Sign in with the passkey registered on this till.";
            oAuthn.AuthenticateButtonText = "Sign in with a passkey";
            oAuthn.AuthenticateButtonStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oAuthn.WebAuthnID = "pos-webauthn";
            oCard.AddRaw(oAuthn.HTML);

            var oHint = new TsgcHTMLParagraph(
                "Demo accounts: admin/admin, manager/manager, cashier/cashier. " +
                "Manager PIN 1379, admin PIN 4242.");
            oHint.CSSClass = "text-muted small text-center mt-3";
            oCard.Add(oHint);

            return WrapTemplate("Sign in - sgcPOS", oRoot.HTML, aTheme);
        }

        // ----- the till ----- //

        // htmx FRAGMENT: the product tile grid only.
        public string BuildTilesFragment(TPOSProduct[] aProds, string aSearch)
        {
            var oRoot = new TsgcHTMLNodeList();

            if ((aProds == null) || (aProds.Length == 0))
            {
                var oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Icon = POSPagesUtils.CS_ICON_SEARCH;
                oEmpty.Title = "No products here";
                if (aSearch != "")
                    oEmpty.Description = "Nothing matches \"" + aSearch +
                        "\". Try a different name, SKU or barcode.";
                else
                    oEmpty.Description = "This category has no active products yet.";
                oEmpty.Bordered = true;
                oRoot.AddRaw(oEmpty.HTML);
                return oRoot.HTML;
            }

            var oGrid = new TsgcHTMLContainer("div");
            oGrid.CSSClass = "pos-tiles";
            oRoot.Add(oGrid);

            for (int vI = 0; vI < aProds.Length; vI++)
            {
                var oTile = new TsgcHTMLContainer("div");
                oTile.CSSClass = "position-relative";
                oGrid.Add(oTile);

                // The tile caption is rich (name over price), and TsgcHTMLButton
                // escapes its Text, so the button itself is a node-layer element with
                // two children. One tap adds the line and swaps the cart only: no page
                // reload, no double-click, no hover needed.
                var oBtn = new TsgcHTMLContainer("button");
                oBtn.CSSClass = "btn btn-outline-dark pos-tile w-100";
                oBtn.Attributes = "type=\"button\" hx-post=\"/till/add\" " +
                    "hx-vals='{\"product_id\": " +
                    aProds[vI].Id.ToString(CultureInfo.InvariantCulture) +
                    "}' hx-target=\"#pos-cart\" hx-swap=\"innerHTML\" title=\"" +
                    POSPagesUtils.HtmlEsc(aProds[vI].Sku + " - " + aProds[vI].Name) + "\"";
                oTile.Add(oBtn);

                var oName = new TsgcHTMLContainer("span");
                oName.CSSClass = "pos-tile-name";
                oName.AddText(aProds[vI].Name);
                oBtn.Add(oName);
                var oPrice = new TsgcHTMLContainer("span");
                oPrice.CSSClass = "pos-tile-price";
                oPrice.AddText(Money(aProds[vI].Price));
                oBtn.Add(oPrice);

                var oCorner = new TsgcHTMLContainer("span");
                oCorner.CSSClass = "position-absolute top-0 end-0 m-1";
                oTile.Add(oCorner);
                var oStock = new TsgcHTMLComponent_Badge();
                oStock.Text = aProds[vI].Stock.ToString(CultureInfo.InvariantCulture);
                if (aProds[vI].Stock <= 5)
                    oStock.Color = TsgcHTMLBadgeStyle.bgDanger;
                else
                    oStock.Color = TsgcHTMLBadgeStyle.bgSecondary;
                oStock.Pill = true;
                var oInfo = new TsgcHTMLComponent_Badge();
                oInfo.Text = "i";
                oInfo.Color = TsgcHTMLBadgeStyle.bgLight;
                oInfo.Pill = true;
                // the popover body is plain text, encoded by the component
                string vInfo = "SKU " + aProds[vI].Sku + " | Barcode " +
                    aProds[vI].Barcode + " | Tax " +
                    POSPagesUtils.sgcHTMLFloatToStr(aProds[vI].TaxRate, 2) + "% | In stock " +
                    aProds[vI].Stock.ToString(CultureInfo.InvariantCulture);
                oCorner.AddRaw(TsgcHTMLComponent_Popover.Build(oInfo.HTML,
                    aProds[vI].Name, vInfo, TsgcHTMLPlacement.plLeft,
                    TsgcHTMLPopoverTrigger.ptFocus));
                oCorner.AddRaw(oStock.HTML);
            }

            return oRoot.HTML;
        }

        // htmx FRAGMENT: the cart panel only (lines + totals + actions).
        public string BuildCartFragment(TPOSSale aSale, TPOSSaleLine[] aLines,
            TPOSPageCtx aCtx, string aNotice, string aNoticeColor)
        {
            var oRoot = new TsgcHTMLNodeList();

            if (aNotice != "")
            {
                var oSnack = new TsgcHTMLComponent_Snackbar();
                oSnack.Message = aNotice;
                oSnack.SnackbarID = "pos-snack";
                oSnack.Position = TsgcHTMLSnackbarPosition.sbBottomRight;
                oSnack.Delay = 2500;
                if (string.Equals(aNoticeColor, "danger", StringComparison.OrdinalIgnoreCase))
                    oSnack.Color = TsgcHTMLColor.hcDanger;
                else
                    oSnack.Color = TsgcHTMLColor.hcSuccess;
                oRoot.AddRaw(oSnack.HTML);
            }

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body p-2";
            oCard.HeaderClass = "card-header d-flex justify-content-between " +
                "align-items-center";
            oCard.Header.AddText("Current sale");
            var oCust = new TsgcHTMLContainer("span");
            oCust.CSSClass = "small text-muted";
            if (aSale.CustomerName != "")
                oCust.AddText(aSale.CustomerName);
            else
                oCust.AddText("Walk-in");
            oCard.Header.Add(oCust);
            oRoot.Add(oCard);

            if ((aLines == null) || (aLines.Length == 0))
            {
                var oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Icon = POSPagesUtils.CS_ICON_CART;
                oEmpty.Title = "Cart is empty";
                oEmpty.Description = "Tap a product, scan a barcode, or recall a parked sale.";
                oEmpty.ActionCaption = "Parked sales";
                oEmpty.ActionHref = "/till/parked";
                oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
                oEmpty.Compact = true;
                oCard.Body.AddRaw(oEmpty.HTML);
                return oRoot.HTML;
            }

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "pos-cart-lines";
            oCard.Body.Add(oWrap);

            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.Responsive = false;
            oTable.AddColumn("Item");
            oTable.AddColumn("Qty", "text-center");
            oTable.AddColumn("Total", "text-end");
            oTable.AddColumn("", "text-end");
            oWrap.Add(oTable);

            for (int vI = 0; vI < aLines.Length; vI++)
            {
                TsgcHTMLTableRow oRow = oTable.AddRow();
                oRow.RowClass = "pos-line";
                TsgcHTMLTableCell oCell = oRow.AddCell();
                oCell.AddElement("div", aLines[vI].ProductName, "fw-semibold");
                oCell.AddRaw(TsgcHTMLComponent_Badge.Build(Money(aLines[vI].UnitPrice) +
                    " x " + POSPagesUtils.QtyStr(aLines[vI].Qty),
                    TsgcHTMLBadgeStyle.bgLight, true));
                if (aLines[vI].Discount > 0)
                    oCell.AddRaw(TsgcHTMLComponent_Badge.Build("-" +
                        Money(aLines[vI].Discount), TsgcHTMLBadgeStyle.bgWarning, true));

                oCell = oRow.AddCell();
                oCell.CellClass = "text-center";
                var oMinus = new TsgcHTMLButton("-", TsgcHTMLButtonStyle.bsOutlineSecondary);
                oMinus.ButtonType = "button";
                oMinus.CSSClass = "btn-sm";
                oMinus.Attributes = "hx-post=\"/till/qty\" hx-vals='{\"line_id\": " +
                    aLines[vI].Id.ToString(CultureInfo.InvariantCulture) + ", \"qty\": " +
                    POSPagesUtils.sgcHTMLFloatToStr(aLines[vI].Qty - 1, 3) +
                    "}' hx-target=\"#pos-cart\" hx-swap=\"innerHTML\"";
                oCell.Add(oMinus);
                oCell.AddRaw(TsgcHTMLComponent_Badge.Build(POSPagesUtils.QtyStr(aLines[vI].Qty),
                    TsgcHTMLBadgeStyle.bgDark, false));
                var oPlus = new TsgcHTMLButton("+", TsgcHTMLButtonStyle.bsOutlineSecondary);
                oPlus.ButtonType = "button";
                oPlus.CSSClass = "btn-sm";
                oPlus.Attributes = "hx-post=\"/till/qty\" hx-vals='{\"line_id\": " +
                    aLines[vI].Id.ToString(CultureInfo.InvariantCulture) + ", \"qty\": " +
                    POSPagesUtils.sgcHTMLFloatToStr(aLines[vI].Qty + 1, 3) +
                    "}' hx-target=\"#pos-cart\" hx-swap=\"innerHTML\"";
                oCell.Add(oPlus);

                oCell = oRow.AddCell();
                oCell.CellClass = "text-end fw-semibold";
                oCell.AddText(Money(aLines[vI].LineTotal));

                oCell = oRow.AddCell();
                oCell.CellClass = "text-end";
                var oDel = new TsgcHTMLButton("x", TsgcHTMLButtonStyle.bsOutlineDanger);
                oDel.ButtonType = "button";
                oDel.CSSClass = "btn-sm";
                oDel.Attributes = "hx-post=\"/till/remove\" hx-vals='{\"line_id\": " +
                    aLines[vI].Id.ToString(CultureInfo.InvariantCulture) +
                    "}' hx-target=\"#pos-cart\" hx-swap=\"innerHTML\"";
                oCell.Add(oDel);
            }

            // NumPad-driven quantity for the selected line. The pad writes a
            // locale-independent number into its hidden field, so a comma keyboard
            // never posts '2,5' into a number the server has to parse.
            var oPanelBody = new TsgcHTMLNodeList();
            var oQtyForm = new TsgcHTMLForm();
            oQtyForm.Method = "POST";
            oQtyForm.Action = "/till/qty";
            oQtyForm.Attributes = "hx-post=\"/till/qty\" hx-target=\"#pos-cart\" " +
                "hx-swap=\"innerHTML\"";
            var oSelect = new TsgcHTMLSelect();
            oSelect.Name = "line_id";
            oSelect.Label_ = "Line";
            oSelect.FieldID = "pos-qty-line";
            oSelect.CSSClass = "form-select form-select-sm mb-2";
            for (int vI = 0; vI < aLines.Length; vI++)
                oSelect.AddOption(aLines[vI].Id.ToString(CultureInfo.InvariantCulture),
                    aLines[vI].ProductName, vI == 0);
            oQtyForm.Add(oSelect);
            var oQtyPad = new TsgcHTMLComponent_NumPad();
            oQtyPad.PadID = "pos-qty-pad";
            oQtyPad.FieldName = "qty";
            oQtyPad.Mode = TsgcHTMLNumPadMode.npInteger;
            oQtyPad.ShowDecimalPoint = false;
            oQtyPad.ShowEnter = false;
            oQtyPad.ButtonSize = 46;
            oQtyPad.MaxValue = 999;
            oQtyPad.ColorStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oQtyForm.AddRaw(oQtyPad.HTML);
            var oSetQty = new TsgcHTMLButton("Set quantity", TsgcHTMLButtonStyle.bsSecondary);
            oSetQty.ButtonType = "submit";
            oSetQty.CSSClass = "w-100 mt-2 btn-sm";
            oQtyForm.Add(oSetQty);
            oPanelBody.Add(oQtyForm);

            var oPanel = new TsgcHTMLComponent_Panel();
            oPanel.Title = "Quantity keypad";
            oPanel.PanelID = "pos-qty-panel";
            oPanel.Collapsible = true;
            oPanel.Expanded = false;
            oPanel.UseColorClass = false;
            oPanel.CSSClass = "mt-2";
            oPanel.Body = oPanelBody.HTML;
            oCard.Body.AddRaw(oPanel.HTML);

            // Totals. lines + tax - discount = total, and the page prints all four so
            // the arithmetic is auditable at a glance.
            var oDesc = new TsgcHTMLDescriptionList();
            oDesc.CSSClass = "row mb-0 mt-2 small";
            oDesc.TermClass = "col-7 text-muted fw-normal";
            oDesc.DescClass = "col-5 text-end mb-0";
            oDesc.AddItem("Subtotal", Money(aSale.Subtotal));
            oDesc.AddItem("Discount", "-" + Money(aSale.Discount));
            oDesc.AddItem("Tax", Money(aSale.Tax));
            oCard.Body.Add(oDesc);

            var oTotal = new TsgcHTMLContainer("div");
            oTotal.CSSClass = "d-flex justify-content-between align-items-baseline " +
                "border-top pt-2 mt-2";
            oTotal.AddRaw(TsgcHTMLComponent_Badge.Build("TOTAL", TsgcHTMLBadgeStyle.bgDark, true));
            oCard.Body.Add(oTotal);
            var oTotalValue = new TsgcHTMLContainer("span");
            oTotalValue.CSSClass = "pos-total";
            oTotalValue.AddText(Money(aSale.Total));
            oTotal.Add(oTotalValue);

            // Whole-sale discount. Anything above the configured threshold is refused
            // unless a manager PIN comes with it, and that check lives on the server.
            var oDiscForm = new TsgcHTMLForm();
            oDiscForm.Method = "POST";
            oDiscForm.Action = "/till/discount";
            oDiscForm.CSSClass = "mt-2";
            oDiscForm.Attributes = "hx-post=\"/till/discount\" hx-target=\"#pos-cart\" " +
                "hx-swap=\"innerHTML\"";
            var oDiscInput = new TsgcHTMLComponent_InputGroup();
            oDiscInput.PrependText = FCurrencySymbol;
            oDiscInput.InputName = "amount";
            oDiscInput.InputTypeEnum = TsgcHTMLInputType.itNumber;
            oDiscInput.InputValue = POSDB.POSMoneyStr(aSale.Discount);
            oDiscInput.Placeholder = "Sale discount";
            oDiscInput.AppendText = "Apply";
            oDiscInput.Size = TsgcHTMLInputGroupSize.igsSmall;
            oDiscInput.GroupID = "pos-discount";
            oDiscForm.AddRaw(oDiscInput.HTML);
            var oApply = new TsgcHTMLButton("Apply discount", TsgcHTMLButtonStyle.bsOutlineWarning);
            oApply.ButtonType = "submit";
            oApply.CSSClass = "w-100 mt-1 btn-sm";
            oDiscForm.Add(oApply);
            oCard.Body.Add(oDiscForm);

            // Payment method shortcuts. The buttons are links into /till/pay, which
            // preselects the method there; the amount is entered on the NumPad.
            var oGroup = new TsgcHTMLComponent_ButtonGroup();
            oGroup.GroupID = "pos-methods";
            oGroup.AriaLabel = "Payment method";
            oGroup.Size = TsgcHTMLButtonGroupSize.bgsSmall;
            TsgcHTMLButtonItem oItem = oGroup.Items.Add();
            oItem.Text = "Cash";
            oItem.Href = "/till/pay?method=cash";
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineDark;
            oItem = oGroup.Items.Add();
            oItem.Text = "Card";
            oItem.Href = "/till/pay?method=card";
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineDark;
            oItem = oGroup.Items.Add();
            oItem.Text = "Voucher";
            oItem.Href = "/till/pay?method=voucher";
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineDark;
            oItem = oGroup.Items.Add();
            oItem.Text = "Loyalty";
            oItem.Href = "/till/pay?method=loyalty";
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineDark;
            var oMethods = new TsgcHTMLContainer("div");
            oMethods.CSSClass = "mt-2";
            oMethods.AddRaw(oGroup.HTML);
            oCard.Body.Add(oMethods);

            var oActions = new TsgcHTMLContainer("div");
            oActions.CSSClass = "d-grid gap-2 mt-2";
            oCard.Body.Add(oActions);
            var oPay = new TsgcHTMLButton("Pay " + Money(aSale.Total));
            oPay.CSSClass = "btn-pos btn-lg";
            oPay.UseButtonClass = true;
            oPay.Style = TsgcHTMLButtonStyle.bsDanger;
            oPay.Href = "/till/pay";
            oActions.Add(oPay);
            var oParkForm = new TsgcHTMLForm();
            oParkForm.Method = "POST";
            oParkForm.Action = "/till/park";
            oParkForm.CSSClass = "d-grid";
            var oPark = new TsgcHTMLButton("Park this sale", TsgcHTMLButtonStyle.bsOutlineSecondary);
            oPark.ButtonType = "submit";
            oParkForm.Add(oPark);
            oActions.Add(oParkForm);

            return oRoot.HTML;
        }

        public string BuildTillPage(TPOSCategory[] aCats, TPOSProduct[] aProds, long aCatId,
            TPOSSale aSale, TPOSSaleLine[] aLines, TPOSPromotion[] aPromos,
            TPOSQuery aCustomers, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            // ----- action bar ----- //
            var oBar = new TsgcHTMLContainer("div");
            oBar.CSSClass = "d-flex flex-wrap align-items-center gap-2 mb-2";
            oRoot.Add(oBar);

            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/till/search";
            oForm.CSSClass = "flex-grow-1";
            oForm.Attributes = "hx-get=\"/till/search\" hx-target=\"#pos-tiles\" " +
                "hx-swap=\"innerHTML\" hx-trigger=\"submit, keyup changed delay:300ms\"";
            var oSearch = new TsgcHTMLComponent_InputGroup();
            oSearch.PrependText = "Find";
            oSearch.InputName = "q";
            oSearch.InputTypeEnum = TsgcHTMLInputType.itSearch;
            oSearch.Placeholder = "Name, SKU or barcode";
            oSearch.GroupID = "pos-search";
            oForm.AddRaw(oSearch.HTML);
            oBar.Add(oForm);

            oBar.AddRaw(TsgcHTMLComponent_Modal.BuildTriggerButton("pos-scan-modal",
                "Scan", TsgcHTMLButtonStyle.bsOutlineDark));

            var oParked = new TsgcHTMLButton("Parked (" +
                aCtx.ParkedCount.ToString(CultureInfo.InvariantCulture) + ")",
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oParked.Href = "/till/parked";
            oBar.Add(oParked);

            // Attaching a customer is a plain form POST, and the option list comes
            // straight out of a query cursor through LoadFromDataSet. No REST endpoint,
            // no JSON, no DTO.
            var oCustForm = new TsgcHTMLForm();
            oCustForm.Method = "POST";
            oCustForm.Action = "/till/customer";
            oCustForm.CSSClass = "d-flex align-items-center gap-1";
            var oCustSelect = new TsgcHTMLComponent_Select();
            oCustSelect.ElementName = "customer_id";
            oCustSelect.SelectID = "pos-customer";
            oCustSelect.Placeholder = "Walk-in customer";
            oCustSelect.Size = TsgcHTMLSelectSize.ssSmall;
            if (aCustomers != null)
                oCustSelect.LoadFromDataSet(aCustomers.DataSet, "id", "name");
            oCustForm.AddRaw(oCustSelect.HTML);
            var oCustBtn = new TsgcHTMLButton("Attach", TsgcHTMLButtonStyle.bsOutlineSecondary);
            oCustBtn.ButtonType = "submit";
            oCustBtn.CSSClass = "btn-sm";
            oCustForm.Add(oCustBtn);
            oBar.Add(oCustForm);

            // ----- category rail + tiles (left) ----- //
            var oLeft = new TsgcHTMLNodeList();
            var oRail = new TsgcHTMLContainer("div");
            oRail.CSSClass = "pos-rail mb-2";
            oLeft.Add(oRail);
            var oRailBtn = new TsgcHTMLButton("All", TsgcHTMLButtonStyle.bsOutlineDark);
            oRailBtn.ButtonType = "button";
            oRailBtn.CSSClass = "btn-sm";
            if (aCatId <= 0)
                oRailBtn.CSSClass = "btn-sm active";
            oRailBtn.Attributes = "hx-get=\"/till/category/0\" " +
                "hx-target=\"#pos-tiles\" hx-swap=\"innerHTML\"";
            oRail.Add(oRailBtn);
            if (aCats != null)
            {
                for (int vI = 0; vI < aCats.Length; vI++)
                {
                    oRailBtn = new TsgcHTMLButton(aCats[vI].Name, TsgcHTMLButtonStyle.bsOutlineDark);
                    oRailBtn.ButtonType = "button";
                    if (aCatId == aCats[vI].Id)
                        oRailBtn.CSSClass = "btn-sm active";
                    else
                        oRailBtn.CSSClass = "btn-sm";
                    // The rail is a set of htmx triggers, one per category, and the
                    // component ButtonGroup cannot carry a per-item hx-get, so the rail
                    // is built from node buttons.
                    oRailBtn.Attributes = "hx-get=\"/till/category/" +
                        aCats[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\" hx-target=\"#pos-tiles\" " +
                        "hx-swap=\"innerHTML\" style=\"border-left:6px solid " +
                        POSPagesUtils.HtmlEsc(aCats[vI].Color) + ";\"";
                    oRail.Add(oRailBtn);
                }
            }

            var oTiles = new TsgcHTMLContainer("div");
            oTiles.ID = "pos-tiles";
            oTiles.AddRaw(BuildTilesFragment(aProds, ""));
            oLeft.Add(oTiles);

            // ----- cart (right) ----- //
            var oCartWrap = new TsgcHTMLNodeList();
            var oCartHost = new TsgcHTMLContainer("div");
            oCartHost.ID = "pos-cart";
            oCartHost.AddRaw(BuildCartFragment(aSale, aLines, aCtx, "", ""));
            oCartWrap.Add(oCartHost);

            var oSplit = new TsgcHTMLComponent_Splitter();
            oSplit.SplitterID = "pos-split";
            oSplit.Orientation = TsgcHTMLSplitterOrientation.soHorizontal;
            oSplit.InitialSplit = 62;
            oSplit.MinSizeA = 260;
            oSplit.MinSizeB = 280;
            oSplit.CSSHeight = "68vh";
            oSplit.PersistKey = "pos-till-split";
            // The Delphi pane content is a TStringList assigned through .Text; the
            // managed pane is a List<string>, so one raw line per pane says the same.
            oSplit.AddPaneA(oLeft.HTML);
            oSplit.AddPaneB(oCartWrap.HTML);
            oRoot.AddRaw(oSplit.HTML);

            // Right-click quick actions over the till.
            var oMenu = new TsgcHTMLComponent_ContextMenu();
            oMenu.MenuID = "pos-till-menu";
            oMenu.TargetSelector = "#pos-tiles";
            TsgcHTMLContextMenuItem oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Till";
            oMenuItem.Header = true;
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Take payment";
            oMenuItem.Href = "/till/pay";
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Parked sales";
            oMenuItem.Href = "/till/parked";
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Divider = true;
            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Shift";
            oMenuItem.Href = "/shift";
            oRoot.AddRaw(oMenu.HTML);

            // ----- customer-facing panel + scanner ----- //
            var oPanelHost = new TsgcHTMLNodeList();
            var oCarousel = new TsgcHTMLComponent_Carousel();
            oCarousel.CarouselID = "pos-promos";
            oCarousel.CSSHeight = "160px";
            oCarousel.Interval = 6000;
            if (aPromos != null)
            {
                for (int vI = 0; vI < aPromos.Length; vI++)
                    oCarousel.AddSlide("/promo/" +
                        aPromos[vI].Id.ToString(CultureInfo.InvariantCulture) + ".svg",
                        aPromos[vI].Name, "Valid " + POSPagesUtils.WhenStr(aPromos[vI].StartsAt) +
                        " to " + POSPagesUtils.WhenStr(aPromos[vI].EndsAt));
            }
            oPanelHost.AddRaw(oCarousel.HTML);

            var oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.TabsID = "pos-till-tabs";
            oTabs.Style = TsgcHTMLTabStyle.tsPill;
            TsgcHTMLTabItem oTab = oTabs.Items.Add();
            oTab.Title = "Customer display";
            oTab.TabID = "pos-tab-promos";
            oTab.Active = true;
            oTab.Content = oPanelHost.HTML;
            oTab = oTabs.Items.Add();
            oTab.Title = "How this page gets its data";
            oTab.TabID = "pos-tab-sql";
            oTab.Content = TsgcHTMLComponent_EmptyState.Build(
                "No REST tier involved",
                "Every list on this screen is a query cursor handed to an sgcHTML " +
                "component through LoadFromDataSet. Open the SQL page to see the " +
                "statement next to the component it feeds.");
            oRoot.AddRaw(oTabs.HTML);

            // ----- camera scanner, in a modal ----- //
            var oScanner = new TsgcHTMLComponent_CameraScanner();
            oScanner.ScannerID = "pos-scanner";
            oScanner.Mode = TsgcHTMLScannerMode.smBarcode;
            oScanner.Formats = "ean_13,ean_8,code_128,code_39,qr_code";
            oScanner.FieldName = "scan";
            oScanner.Action = "/till/scan";
            oScanner.ShowManualEntry = true;
            oScanner.ManualEntryPlaceholder = "Type the barcode or the SKU";
            oScanner.ShowCaptureButton = false;
            oScanner.CSSHeight = "280px";
            var oScanModal = new TsgcHTMLComponent_Modal();
            oScanModal.ModalID = "pos-scan-modal";
            oScanModal.Title = "Scan a barcode";
            oScanModal.Body = oScanner.HTML;
            oScanModal.Size = TsgcHTMLModalSize.msLarge;
            oRoot.AddRaw(oScanModal.HTML);

            if (aCtx.Flash != "")
            {
                var oToast = new TsgcHTMLComponent_Toast();
                oToast.Title = "Till";
                oToast.Body = aCtx.Flash;
                oToast.ToastID = "pos-toast";
                oToast.ColorStyle = TsgcHTMLColor.hcSuccess;
                oRoot.AddRaw(TsgcHTMLComponent_Toast.BuildContainer(oToast.HTML,
                    TsgcHTMLToastPosition.tpBottomEnd));
            }

            string vBody = oRoot.HTML;
            return BuildPageShell("Till", vBody, aCtx);
        }

        public string BuildParkedPage(TPOSSale[] aSales, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oHeading = new TsgcHTMLHeading("Parked sales", 1);
            oHeading.CSSClass = "h4 mb-3";
            oRoot.Add(oHeading);

            if ((aSales == null) || (aSales.Length == 0))
            {
                var oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Icon = POSPagesUtils.CS_ICON_PARK;
                oEmpty.Title = "Nothing parked";
                oEmpty.Description = "Park a sale from the till when a customer " +
                    "walks away to fetch one more thing.";
                oEmpty.ActionCaption = "Back to the till";
                oEmpty.ActionHref = "/";
                oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsDanger;
                oEmpty.Bordered = true;
                oRoot.AddRaw(oEmpty.HTML);
            }
            else
            {
                var oGrid = new TsgcHTMLContainer("div");
                oGrid.CSSClass = "row g-3";
                oRoot.Add(oGrid);
                for (int vI = 0; vI < aSales.Length; vI++)
                {
                    TsgcHTMLContainer oCol = oGrid.Children.AddElement("div", "",
                        "col-12 col-md-6 col-xl-4");
                    var oCard = new TsgcHTMLCard();
                    oCard.CSSClass = "h-100 shadow-sm";
                    oCard.BodyClass = "card-body";
                    oCard.Title = "Sale #" + aSales[vI].Id.ToString(CultureInfo.InvariantCulture);
                    var oDesc = new TsgcHTMLDescriptionList();
                    oDesc.CSSClass = "row mb-2 small";
                    oDesc.TermClass = "col-6 text-muted fw-normal";
                    oDesc.DescClass = "col-6 text-end mb-0";
                    oDesc.AddItem("Parked at", POSPagesUtils.WhenStr(aSales[vI].CreatedAt));
                    oDesc.AddItem("Lines",
                        aSales[vI].LineCount.ToString(CultureInfo.InvariantCulture));
                    oDesc.AddItem("Customer", aSales[vI].CustomerName);
                    oDesc.AddItem("Total", Money(aSales[vI].Total));
                    oCard.Body.Add(oDesc);
                    var oForm = new TsgcHTMLForm();
                    oForm.Method = "POST";
                    oForm.Action = "/till/recall";
                    oForm.CSSClass = "d-grid";
                    oForm.AddHidden("sale_id",
                        aSales[vI].Id.ToString(CultureInfo.InvariantCulture));
                    var oBtn = new TsgcHTMLButton("Recall to the till",
                        TsgcHTMLButtonStyle.bsDanger);
                    oBtn.ButtonType = "submit";
                    oForm.Add(oBtn);
                    oCard.Body.Add(oForm);
                    oCol.AddRaw(oCard.HTML);
                }
            }

            string vBody = oRoot.HTML;
            return BuildPageShell("Parked sales", vBody, aCtx);
        }

        public string BuildPayPage(TPOSSale aSale, TPOSSaleLine[] aLines,
            TPOSPayment[] aPayments, decimal aPaid, decimal aDue, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3";
            oRoot.Add(oRow);

            // ----- left: what is owed ----- //
            TsgcHTMLContainer oColA = oRow.Children.AddElement("div", "", "col-12 col-lg-6");
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-3";
            oCard.BodyClass = "card-body";
            oCard.Title = "Sale #" + aSale.Id.ToString(CultureInfo.InvariantCulture);
            oCard.Body.Add(POSPagesUtils.BuildLinesTable(this, aLines));
            var oDesc = new TsgcHTMLDescriptionList();
            oDesc.CSSClass = "row mb-0 mt-3";
            oDesc.TermClass = "col-7 text-muted fw-normal";
            oDesc.DescClass = "col-5 text-end mb-0";
            oDesc.AddItem("Lines (subtotal)", Money(aSale.Subtotal));
            oDesc.AddItem("Discount", "-" + Money(aSale.Discount));
            oDesc.AddItem("Tax", Money(aSale.Tax));
            oDesc.AddItem("Total", Money(aSale.Total));
            oDesc.AddItem("Paid so far", Money(aPaid));
            oDesc.AddItem("Still due", Money(aDue));
            oCard.Body.Add(oDesc);
            oColA.AddRaw(TsgcHTMLComponent_StatCard.Build("Still due", Money(aDue),
                TsgcHTMLStatColor.scDanger, TsgcHTMLStatTrend.stNone, "", "pos-due",
                TsgcHTMLStatGradient.sgPinkRed));
            oColA.Add(oCard);

            // ----- right: the keypad ----- //
            TsgcHTMLContainer oColB = oRow.Children.AddElement("div", "", "col-12 col-lg-6");
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";
            oCard.Title = "Take a payment";

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/till/pay";
            oForm.CSSClass = "d-flex flex-column align-items-center gap-2";

            var oSelect = new TsgcHTMLComponent_Select();
            oSelect.ElementName = "method";
            oSelect.SelectID = "pos-pay-method";
            oSelect.Label_ = "Method";
            oSelect.AddOption(POSConst.CS_PAY_CASH, "Cash", true);
            oSelect.AddOption(POSConst.CS_PAY_CARD, "Card");
            oSelect.AddOption(POSConst.CS_PAY_VOUCHER, "Voucher");
            oSelect.AddOption(POSConst.CS_PAY_LOYALTY, "Loyalty");
            oForm.AddRaw(oSelect.HTML);

            // The NumPad is the showcase of this screen: it drives the tender amount,
            // it accepts the physical keyboard, and its hidden field always carries a
            // locale-independent number, so a European keypad comma never posts
            // '20,00' into the amount the server has to add up.
            var oPad = new TsgcHTMLComponent_NumPad();
            oPad.PadID = "pos-pay-pad";
            oPad.FieldName = "amount";
            oPad.Mode = TsgcHTMLNumPadMode.npCurrency;
            oPad.CurrencySymbol = FCurrencySymbol;
            oPad.DecimalPlaces = 2;
            oPad.Value = (double)aDue;
            oPad.ButtonSize = 64;
            oPad.ShowEnter = false;
            oPad.ColorStyle = TsgcHTMLButtonStyle.bsOutlineDark;
            oPad.QuickAmounts.Add(POSDB.POSMoneyStr(aDue));
            oPad.QuickAmounts.Add("5");
            oPad.QuickAmounts.Add("10");
            oPad.QuickAmounts.Add("20");
            oPad.QuickAmounts.Add("50");
            oForm.AddRaw(oPad.HTML);

            var oBtn = new TsgcHTMLButton("Tender", TsgcHTMLButtonStyle.bsDanger);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100 btn-lg";
            oForm.Add(oBtn);
            oCard.Body.Add(oForm);

            if ((aPayments != null) && (aPayments.Length > 0))
            {
                var oTable = new TsgcHTMLTable();
                oTable.CSSClass = "table table-sm mt-3 mb-0";
                oTable.Responsive = false;
                oTable.AddColumn("Method");
                oTable.AddColumn("Amount", "text-end");
                oTable.AddColumn("Change", "text-end");
                for (int vI = 0; vI < aPayments.Length; vI++)
                {
                    TsgcHTMLTableRow oPayRow = oTable.AddRow();
                    oPayRow.AddCellText(aPayments[vI].Method);
                    oPayRow.AddCellText(Money(aPayments[vI].Amount), "text-end");
                    oPayRow.AddCellText(Money(aPayments[vI].ChangeGiven), "text-end");
                }
                oCard.Body.Add(oTable);
            }

            var oBack = new TsgcHTMLButton("Back to the till",
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBack.Href = "/";
            oBack.CSSClass = "mt-3";
            oCard.Body.Add(oBack);

            oColB.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell("Payment", vBody, aCtx);
        }

        public string BuildReceiptPage(TPOSSale aSale, TPOSSaleLine[] aLines,
            TPOSPayment[] aPayments, TPOSPageCtx aCtx)
        {
            decimal vPaid = 0m;
            decimal vChange = 0m;
            if (aPayments != null)
            {
                for (int vI = 0; vI < aPayments.Length; vI++)
                {
                    vPaid = vPaid + aPayments[vI].Amount;
                    vChange = vChange + aPayments[vI].ChangeGiven;
                }
            }

            var oRoot = new TsgcHTMLNodeList();

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "mx-auto";
            oWrap.Style = "max-width:420px;";
            oRoot.Add(oWrap);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";
            oCard.Body.AddElement("div", FStoreName, "fw-bold fs-5 text-center");
            oCard.Body.AddElement("div", FStoreAddress, "text-muted small text-center");
            oCard.Body.AddElement("div", FStoreTaxId, "text-muted small text-center");

            var oBadge = new TsgcHTMLComponent_Badge();
            oBadge.Text = aSale.Status;
            if (string.Equals(aSale.Status, POSConst.CS_SALE_REFUNDED,
                StringComparison.OrdinalIgnoreCase))
                oBadge.Color = TsgcHTMLBadgeStyle.bgDanger;
            else
                oBadge.Color = TsgcHTMLBadgeStyle.bgSuccess;
            oBadge.Pill = true;
            oCard.Body.AddElement("div", "", "text-center my-2").AddRaw(oBadge.HTML);

            var oDesc = new TsgcHTMLDescriptionList();
            oDesc.CSSClass = "row small mb-2";
            oDesc.TermClass = "col-5 text-muted fw-normal";
            oDesc.DescClass = "col-7 text-end mb-0";
            oDesc.AddItem("Receipt", aSale.Reference);
            oDesc.AddItem("Date", POSPagesUtils.WhenStr(aSale.CreatedAt));
            oDesc.AddItem("Cashier", aSale.UserName);
            if (aSale.CustomerName != "")
                oDesc.AddItem("Customer", aSale.CustomerName);
            oCard.Body.Add(oDesc);

            oCard.Body.Add(POSPagesUtils.BuildLinesTable(this, aLines));

            oDesc = new TsgcHTMLDescriptionList();
            oDesc.CSSClass = "row mt-3 mb-0";
            oDesc.TermClass = "col-7 text-muted fw-normal";
            oDesc.DescClass = "col-5 text-end mb-0";
            oDesc.AddItem("Subtotal", Money(aSale.Subtotal));
            oDesc.AddItem("Discount", "-" + Money(aSale.Discount));
            oDesc.AddItem("Tax", Money(aSale.Tax));
            oDesc.AddItem("TOTAL", Money(aSale.Total));
            oDesc.AddItem("Tendered", Money(vPaid));
            oDesc.AddItem("Change", Money(vChange));
            oCard.Body.Add(oDesc);

            var oBtns = new TsgcHTMLContainer("div");
            oBtns.CSSClass = "d-grid gap-2 mt-3";
            oCard.Body.Add(oBtns);
            var oBtn = new TsgcHTMLButton("Print receipt (80mm PDF)",
                TsgcHTMLButtonStyle.bsDark);
            oBtn.Href = "/till/receipt/" +
                aSale.Id.ToString(CultureInfo.InvariantCulture) + ".pdf";
            oBtns.Add(oBtn);
            oBtn = new TsgcHTMLButton("New sale", TsgcHTMLButtonStyle.bsDanger);
            oBtn.Href = "/";
            oBtns.Add(oBtn);
            if (string.Equals(aSale.Status, POSConst.CS_SALE_COMPLETED,
                StringComparison.OrdinalIgnoreCase))
                oBtns.AddRaw(TsgcHTMLComponent_Modal.BuildTriggerButton("pos-refund",
                    "Refund this sale", TsgcHTMLButtonStyle.bsOutlineDanger));

            oWrap.Add(oCard);

            if (string.Equals(aSale.Status, POSConst.CS_SALE_COMPLETED,
                StringComparison.OrdinalIgnoreCase))
                oRoot.AddRaw(BuildManagerPinModal("pos-refund",
                    "Manager authorisation", "/till/refund/" +
                    aSale.Id.ToString(CultureInfo.InvariantCulture),
                    "sale_id", aSale.Id.ToString(CultureInfo.InvariantCulture)));

            string vBody = oRoot.HTML;
            return BuildPageShell("Receipt", vBody, aCtx);
        }

        // Int64 / Integer to text, always locale independent: this is the
        // managed spelling of the Delphi IntToStr the ported code called.
        private static string Part2IntStr(long aValue)
        {
            return aValue.ToString(CultureInfo.InvariantCulture);
        }

        // ----- shift ----- //

        public string BuildShiftPage(TPOSShift aShift, TPOSTotals aTotals, decimal aExpected,
            TPOSShift[] aHistory, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3";
            oRoot.Add(oRow);

            TsgcHTMLContainer oCol = oRow.Children.AddElement("div", "", "col-12 col-lg-5");
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";

            if (!aCtx.ShiftOpen)
            {
                oCard.Title = "Open a shift";
                var oOpenForm = new TsgcHTMLForm();
                oOpenForm.Method = "POST";
                oOpenForm.Action = "/shift/open";
                oOpenForm.CSSClass = "d-flex flex-column align-items-center gap-2";
                var oPad = new TsgcHTMLComponent_NumPad();
                oPad.PadID = "pos-float-pad";
                oPad.FieldName = "opening_float";
                oPad.Mode = TsgcHTMLNumPadMode.npCurrency;
                oPad.CurrencySymbol = FCurrencySymbol;
                oPad.Value = 100;
                oPad.ButtonSize = 58;
                oPad.ShowEnter = false;
                oPad.QuickAmounts.Add("100");
                oPad.QuickAmounts.Add("150");
                oPad.QuickAmounts.Add("200");
                oOpenForm.AddRaw(oPad.HTML);
                var oOpenBtn = new TsgcHTMLButton("Open the drawer", TsgcHTMLButtonStyle.bsDanger);
                oOpenBtn.ButtonType = "submit";
                oOpenBtn.CSSClass = "w-100";
                oOpenForm.Add(oOpenBtn);
                oCard.Body.Add(oOpenForm);
            }
            else
            {
                oCard.Title = "Close the shift";
                oCard.Body.AddElement("div",
                    "Expected cash is computed from the payments recorded against this " +
                    "shift: opening float plus cash taken minus change given. The count " +
                    "below is compared against it on the server.", "text-muted small mb-2");
                var oCloseForm = new TsgcHTMLForm();
                oCloseForm.Method = "POST";
                oCloseForm.Action = "/shift/close";
                oCloseForm.CSSClass = "d-flex flex-column align-items-center gap-2";
                oCloseForm.AddHidden("shift_id", Part2IntStr(aShift.Id));
                var oPad = new TsgcHTMLComponent_NumPad();
                oPad.PadID = "pos-count-pad";
                oPad.FieldName = "counted_cash";
                oPad.Mode = TsgcHTMLNumPadMode.npCurrency;
                oPad.CurrencySymbol = FCurrencySymbol;
                oPad.Value = 0;
                oPad.ButtonSize = 58;
                oPad.ShowEnter = false;
                oCloseForm.AddRaw(oPad.HTML);
                var oCloseBtn = new TsgcHTMLButton("Count and close", TsgcHTMLButtonStyle.bsDanger);
                oCloseBtn.ButtonType = "submit";
                oCloseBtn.CSSClass = "w-100";
                oCloseForm.Add(oCloseBtn);
                oCard.Body.Add(oCloseForm);

                var oXBtn = new TsgcHTMLButton("X report (PDF)",
                    TsgcHTMLButtonStyle.bsOutlineSecondary);
                oXBtn.Href = "/shift/" + Part2IntStr(aShift.Id) + "/xreport.pdf";
                oXBtn.CSSClass = "mt-3 me-2";
                oCard.Body.Add(oXBtn);
                var oZBtn = new TsgcHTMLButton("Z report (PDF)",
                    TsgcHTMLButtonStyle.bsOutlineSecondary);
                oZBtn.Href = "/shift/" + Part2IntStr(aShift.Id) + "/zreport.pdf";
                oZBtn.CSSClass = "mt-3";
                oCard.Body.Add(oZBtn);
            }
            oCol.Add(oCard);

            oCol = oRow.Children.AddElement("div", "", "col-12 col-lg-7");
            if (aCtx.ShiftOpen)
            {
                var oGauge = new TsgcHTMLComponent_Gauge();
                oGauge.GaugeID = "pos-shift-gauge";
                oGauge.Title = "Shift takings vs target";
                oGauge.Unit_ = FCurrencySymbol;
                oGauge.MinValue = 0;
                oGauge.MaxValue = (double)FShiftTarget;
                oGauge.Value = (double)aTotals.Gross;
                oGauge.ThresholdMid = (double)FShiftTarget * 0.4;
                oGauge.ThresholdHigh = (double)FShiftTarget * 0.75;
                oGauge.Width = 240;
                oCol.AddRaw(oGauge.HTML);

                var oStats = new TsgcHTMLContainer("div");
                oStats.CSSClass = "row g-2 mt-2";
                oCol.Add(oStats);

                // One stat tile of the open-shift summary row.
                Action<string, string, TsgcHTMLStatColor> oAddStat =
                    (aTitle, aValue, aColor) =>
                    {
                        oStats.Children.AddElement("div", "", "col-6 col-lg-3").AddRaw(
                            TsgcHTMLComponent_StatCard.Build(aTitle, aValue, aColor));
                    };

                oAddStat("Sales", Part2IntStr(aTotals.SaleCount), TsgcHTMLStatColor.scPrimary);
                oAddStat("Cash in drawer", Money(aExpected), TsgcHTMLStatColor.scSuccess);
                oAddStat("Card", Money(aTotals.CardTaken), TsgcHTMLStatColor.scInfo);
                oAddStat("Refunds", Money(aTotals.Refunded), TsgcHTMLStatColor.scDanger);

                var oTimeline = new TsgcHTMLComponent_Timeline();
                oTimeline.TimelineID = "pos-shift-timeline";
                TsgcHTMLTimelineItem oItem = oTimeline.Items.Add();
                oItem.Title = "Shift opened";
                oItem.Timestamp = POSPagesUtils.WhenStr(aShift.OpenedAt);
                oItem.Content = "Opening float " + Money(aShift.OpeningFloat);
                oItem.ColorStyle = TsgcHTMLColor.hcSuccess;
                oItem = oTimeline.Items.Add();
                oItem.Title = "Takings so far";
                oItem.Timestamp = POSPagesUtils.TimeStr(DateTime.Now);
                oItem.Content = Part2IntStr(aTotals.SaleCount) + " sales, " +
                    Money(aTotals.Gross) + " gross, " + Money(aTotals.Tax) + " tax";
                oItem.ColorStyle = TsgcHTMLColor.hcPrimary;
                oItem = oTimeline.Items.Add();
                oItem.Title = "Expected cash";
                oItem.Timestamp = POSPagesUtils.TimeStr(DateTime.Now);
                oItem.Content = Money(aShift.OpeningFloat) + " float + " +
                    Money(aTotals.CashTaken) + " cash taken = " + Money(aExpected);
                oItem.ColorStyle = TsgcHTMLColor.hcWarning;
                oCol.AddRaw(oTimeline.HTML);
            }
            else
            {
                var oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Icon = POSPagesUtils.CS_ICON_PARK;
                oEmpty.Title = "No shift is open";
                oEmpty.Description = "Count the float into the drawer and open a " +
                    "shift before ringing anything up.";
                oEmpty.Bordered = true;
                oCol.AddRaw(oEmpty.HTML);
            }

            // Shift history, straight into a Grid.
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "pos-shift-history";
            oGrid.HeaderClass = "table-light";
            oGrid.EmptyText = "No shifts recorded yet";
            oGrid.ShowSort = true;
            TsgcHTMLGridColumn oCols = oGrid.Columns.Add();
            oCols.Title = "Shift";
            oCols = oGrid.Columns.Add();
            oCols.Title = "Cashier";
            oCols = oGrid.Columns.Add();
            oCols.Title = "Opened";
            oCols = oGrid.Columns.Add();
            oCols.Title = "Closed";
            oCols = oGrid.Columns.Add();
            oCols.Title = "Expected";
            oCols.Align = TsgcHTMLGridAlign.gaRight;
            oCols = oGrid.Columns.Add();
            oCols.Title = "Counted";
            oCols.Align = TsgcHTMLGridAlign.gaRight;
            oCols = oGrid.Columns.Add();
            oCols.Title = "Variance";
            oCols.Align = TsgcHTMLGridAlign.gaRight;
            for (int vI = 0; vI < aHistory.Length; vI++)
                oGrid.AddRow("#" + Part2IntStr(aHistory[vI].Id), aHistory[vI].UserName,
                    POSPagesUtils.WhenStr(aHistory[vI].OpenedAt),
                    POSPagesUtils.WhenStr(aHistory[vI].ClosedAt),
                    Money(aHistory[vI].ExpectedCash), Money(aHistory[vI].CountedCash),
                    Money(aHistory[vI].Variance));
            oRoot.AddElement("h2", "Shift history", "h5 mt-4");
            oRoot.AddRaw(oGrid.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell("Shift", vBody, aCtx);
        }

        // ----- back office ----- //

        public string BuildProductsGridFragment(TPOSProduct[] aProds, double[][] aTrend)
        {
            var oProvider = new TPOSProductCellProvider();
            oProvider.Ids = new long[aProds.Length];
            for (int vI = 0; vI < aProds.Length; vI++)
                oProvider.Ids[vI] = aProds[vI].Id;
            oProvider.Trend = aTrend;

            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "pos-products-grid";
            oGrid.HeaderClass = "table-light";
            oGrid.EmptyText = "No products match this filter";
            oGrid.ShowSort = true;
            oGrid.ShowFilter = true;
            oGrid.ShowColumnsMenu = true;
            oGrid.ColumnsText = "Columns";
            oGrid.ExportXLSX = true;
            oGrid.ExportURL = "/products/export.xlsx";
            oGrid.ExportPDF = true;
            oGrid.ExportPDFURL = "/products/export.pdf";
            oGrid.OnGetCellHTML += oProvider.GetCellHTML;

            TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
            oCol.Title = "SKU";
            oCol = oGrid.Columns.Add();
            oCol.Title = "Product";
            oCol = oGrid.Columns.Add();
            oCol.Title = "Category";
            oCol = oGrid.Columns.Add();
            oCol.Title = "Price";
            oCol.Align = TsgcHTMLGridAlign.gaRight;
            oCol = oGrid.Columns.Add();
            oCol.Title = "Stock";
            oCol.Align = TsgcHTMLGridAlign.gaRight;
            oCol = oGrid.Columns.Add();
            oCol.Title = "12 week trend";
            oCol = oGrid.Columns.Add();
            oCol.Title = "Actions";
            oCol.Align = TsgcHTMLGridAlign.gaRight;

            for (int vI = 0; vI < aProds.Length; vI++)
                oGrid.AddRow(aProds[vI].Sku, aProds[vI].Name,
                    aProds[vI].CategoryName, Money(aProds[vI].Price),
                    Part2IntStr(aProds[vI].Stock), "", "");

            return oGrid.HTML;
        }

        public string BuildProductsPage(TPOSProduct[] aProds, TPOSCategory[] aCats,
            double[][] aTrend, string aSearch, long aCatId, int aPage, int aPageCount,
            TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oToolbar = new TsgcHTMLComponent_Toolbar();
            oToolbar.ToolbarID = "pos-products-toolbar";
            TsgcHTMLToolbarItem oItem = oToolbar.Items.Add();
            oItem.Text = "New product";
            oItem.Href = "/products/form";
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsDanger;
            oItem = oToolbar.Items.Add();
            oItem.Separator = true;
            oItem = oToolbar.Items.Add();
            oItem.Text = "Export XLSX";
            oItem.Href = "/products/export.xlsx";
            oItem = oToolbar.Items.Add();
            oItem.Text = "Export PDF";
            oItem.Href = "/products/export.pdf";
            oItem = oToolbar.Items.Add();
            oItem.Text = "Categories";
            oItem.Href = "/categories";
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oRoot.AddRaw(oToolbar.HTML);

            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/products";
            oForm.CSSClass = "my-3";
            oRoot.Add(oForm);
            var oBar = new TsgcHTMLContainer("div");
            oBar.CSSClass = "d-flex flex-wrap gap-2 align-items-end";
            oForm.Add(oBar);
            var oSearch = new TsgcHTMLComponent_InputGroup();
            oSearch.PrependText = "Search";
            oSearch.InputName = "q";
            oSearch.InputTypeEnum = TsgcHTMLInputType.itSearch;
            oSearch.InputValue = aSearch;
            oSearch.Placeholder = "Name, SKU or barcode";
            oSearch.GroupID = "pos-prod-search";
            oBar.AddRaw(oSearch.HTML);
            var oSelect = new TsgcHTMLComponent_Select();
            oSelect.ElementName = "cat";
            oSelect.SelectID = "pos-prod-cat";
            oSelect.Placeholder = "All categories";
            for (int vI = 0; vI < aCats.Length; vI++)
                oSelect.AddOption(Part2IntStr(aCats[vI].Id), aCats[vI].Name,
                    aCats[vI].Id == aCatId);
            oBar.AddRaw(oSelect.HTML);
            var oGo = new TsgcHTMLButton("Filter", TsgcHTMLButtonStyle.bsOutlineDark);
            oGo.ButtonType = "submit";
            oBar.Add(oGo);

            // The htmx form panel: Edit / New swap the form in here, Save swaps the
            // refreshed grid back into #products-grid. No page reload either way.
            var oPanel = new TsgcHTMLContainer("div");
            oPanel.ID = "product-form-panel";
            oPanel.CSSClass = "mb-3";
            oRoot.Add(oPanel);

            var oGridHost = new TsgcHTMLContainer("div");
            oGridHost.ID = "products-grid";
            oGridHost.AddRaw(BuildProductsGridFragment(aProds, aTrend));
            oRoot.Add(oGridHost);

            if (aPageCount > 1)
            {
                var oPager = new TsgcHTMLComponent_Pagination();
                oPager.PaginationID = "pos-products-pager";
                oPager.CurrentPage = aPage;
                oPager.TotalPages = aPageCount;
                oPager.MaxVisible = 7;
                oPager.ShowFirstLast = true;
                // the pager sanitizes and attribute-encodes the URL itself
                oPager.BaseURL = "/products?q=" + aSearch + "&cat=" +
                    Part2IntStr(aCatId) + "&page=";
                oRoot.AddRaw(oPager.HTML);
            }

            string vBody = oRoot.HTML;
            return BuildPageShell("Products", vBody, aCtx);
        }

        public string BuildProductFormFragment(TPOSProduct aProd, TPOSCategory[] aCats,
            bool aIsNew, string aError)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";
            if (aIsNew)
                oCard.Title = "New product";
            else
                oCard.Title = "Edit " + aProd.Name;
            oRoot.Add(oCard);

            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oCard.Body.Add(oAlert);
            }

            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "pos-product-form";
            oForm.Action = "/products/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.SubmitText = "Save";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsDanger;
            oForm.Attributes = "hx-post=\"/products/save\" " +
                "hx-target=\"#products-grid\" hx-swap=\"innerHTML\"";

            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftHidden;
            oField.Name = "id";
            oField.Value = Part2IntStr(aProd.Id);

            oField = oForm.Fields.Add();
            oField.Name = "sku";
            oField.Label_ = "SKU";
            oField.Value = aProd.Sku;
            oField.ColSpan = 6;
            oField.Required = true;

            oField = oForm.Fields.Add();
            oField.Name = "barcode";
            oField.Label_ = "Barcode";
            oField.Value = aProd.Barcode;
            oField.ColSpan = 6;

            oField = oForm.Fields.Add();
            oField.Name = "name";
            oField.Label_ = "Name";
            oField.Value = aProd.Name;
            oField.ColSpan = 12;
            oField.Required = true;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "category_id";
            oField.Label_ = "Category";
            oField.ColSpan = 6;
            for (int vI = 0; vI < aCats.Length; vI++)
                oField.Options.Add(Part2IntStr(aCats[vI].Id) + "=" + aCats[vI].Name);
            oField.Value = Part2IntStr(aProd.CategoryId);

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "price";
            oField.Label_ = "Price";
            // POSMoneyStr, never a culture-sensitive conversion: a comma here would
            // break the number input on a Spanish or German browser.
            oField.Value = POSDB.POSMoneyStr(aProd.Price);
            oField.ColSpan = 3;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "cost";
            oField.Label_ = "Cost";
            oField.Value = POSDB.POSMoneyStr(aProd.Cost);
            oField.ColSpan = 3;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "tax_rate";
            oField.Label_ = "Tax %";
            oField.Value = POSPagesUtils.sgcHTMLFloatToStr(aProd.TaxRate, 2);
            oField.ColSpan = 4;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "stock";
            oField.Label_ = "Stock";
            oField.Value = Part2IntStr(aProd.Stock);
            oField.ColSpan = 4;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSwitch;
            oField.Name = "active";
            oField.Label_ = "Active";
            oField.ColSpan = 4;
            if (aProd.Active || aIsNew)
                oField.Value = "1";

            oCard.Body.AddRaw(oForm.HTML);

            return oRoot.HTML;
        }

        public string BuildCategoriesPage(TPOSCategory[] aCats, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3";
            oRoot.Add(oRow);

            TsgcHTMLContainer oCol = oRow.Children.AddElement("div", "", "col-12 col-lg-8");
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "pos-categories";
            oGrid.HeaderClass = "table-light";
            oGrid.EmptyText = "No categories yet";
            TsgcHTMLGridColumn oGCol = oGrid.Columns.Add();
            oGCol.Title = "Id";
            oGCol = oGrid.Columns.Add();
            oGCol.Title = "Name";
            oGCol = oGrid.Columns.Add();
            oGCol.Title = "Colour";
            oGCol = oGrid.Columns.Add();
            oGCol.Title = "Order";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            for (int vI = 0; vI < aCats.Length; vI++)
                oGrid.AddRow(Part2IntStr(aCats[vI].Id), aCats[vI].Name,
                    aCats[vI].Color, Part2IntStr(aCats[vI].SortOrder));
            oCol.AddRaw(oGrid.HTML);

            oCol.Children.AddElement("div", "", "d-flex flex-wrap gap-1 mt-3");
            for (int vI = 0; vI < aCats.Length; vI++)
            {
                var oChip = new TsgcHTMLComponent_Chip();
                oChip.Text = aCats[vI].Name;
                oChip.Color = TsgcHTMLBadgeStyle.bgSecondary;
                oChip.Outline = true;
                oCol.AddRaw(oChip.HTML);
            }

            oCol = oRow.Children.AddElement("div", "", "col-12 col-lg-4");
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";
            oCard.Title = "Add or edit a category";
            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "pos-cat-form";
            oForm.Action = "/categories/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.SubmitText = "Save category";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsDanger;
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "id";
            oField.Label_ = "Id (blank for a new one)";
            oField = oForm.Fields.Add();
            oField.Name = "name";
            oField.Label_ = "Name";
            oField.Required = true;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftColor;
            oField.Name = "color";
            oField.Label_ = "Colour";
            oField.Value = POSConst.CS_POS_ACCENT;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "sort_order";
            oField.Label_ = "Sort order";
            oField.Value = Part2IntStr(aCats.Length + 1);
            oCard.Body.AddRaw(oForm.HTML);
            oCol.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell("Categories", vBody, aCtx);
        }

        public string BuildPromotionsPage(TPOSPromotion[] aPromos, TPOSCategory[] aCats,
            TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3";
            oRoot.Add(oRow);

            TsgcHTMLContainer oCol;
            TsgcHTMLCard oCard;
            for (int vI = 0; vI < aPromos.Length; vI++)
            {
                oCol = oRow.Children.AddElement("div", "", "col-12 col-md-6 col-xl-4");
                oCard = new TsgcHTMLCard();
                oCard.CSSClass = "h-100 shadow-sm";
                oCard.BodyClass = "card-body";
                var oImage = new TsgcHTMLComponent_Image();
                oImage.Src = "/promo/" + Part2IntStr(aPromos[vI].Id) + ".svg";
                oImage.Alt = aPromos[vI].Name;
                oImage.Shape = TsgcHTMLImageShape.isRounded;
                oImage.CSSWidth = "100%";
                oImage.LazyLoad = true;
                oCard.Body.AddRaw(oImage.HTML);
                oCard.Body.AddElement("div", aPromos[vI].Name, "fw-bold mt-2");
                var oBadge = new TsgcHTMLComponent_Badge();
                oBadge.Text = aPromos[vI].Kind;
                oBadge.Color = TsgcHTMLBadgeStyle.bgInfo;
                oBadge.Pill = true;
                oCard.Body.AddRaw(oBadge.HTML);
                var oDesc = new TsgcHTMLDescriptionList();
                oDesc.CSSClass = "row small mt-2 mb-0";
                oDesc.TermClass = "col-5 text-muted fw-normal";
                oDesc.DescClass = "col-7 text-end mb-0";
                oDesc.AddItem("Value", POSPagesUtils.sgcHTMLFloatToStr(aPromos[vI].Value, 2));
                oDesc.AddItem("From", POSPagesUtils.WhenStr(aPromos[vI].StartsAt));
                oDesc.AddItem("To", POSPagesUtils.WhenStr(aPromos[vI].EndsAt));
                oCard.Body.Add(oDesc);
                oCol.AddRaw(oCard.HTML);
            }

            oCol = oRow.Children.AddElement("div", "", "col-12 col-xl-4");
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";
            oCard.Title = "New promotion";
            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "pos-promo-form";
            oForm.Action = "/promotions/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.SubmitText = "Save promotion";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsDanger;
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "id";
            oField.Label_ = "Id (blank for a new one)";
            oField = oForm.Fields.Add();
            oField.Name = "name";
            oField.Label_ = "Name";
            oField.Required = true;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "kind";
            oField.Label_ = "Kind";
            oField.Options.Add("percent=Percent off");
            oField.Options.Add("amount=Amount off");
            oField.Options.Add("bundle=Bundle");
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "value";
            oField.Label_ = "Value";
            oField.Value = "10";
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "category_id";
            oField.Label_ = "Category";
            oField.Options.Add("0=Any");
            for (int vI = 0; vI < aCats.Length; vI++)
                oField.Options.Add(Part2IntStr(aCats[vI].Id) + "=" + aCats[vI].Name);
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftDate;
            oField.Name = "starts_at";
            oField.Label_ = "Starts";
            oField.Value = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftDate;
            oField.Name = "ends_at";
            oField.Label_ = "Ends";
            oField.Value = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            oCard.Body.AddRaw(oForm.HTML);
            oCol.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell("Promotions", vBody, aCtx);
        }

        public string BuildCustomersPage(TPOSQuery aQuery, string aSearch, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddElement("h1", "Customers", "h4 mb-3");

            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/customers";
            oForm.CSSClass = "mb-3 d-flex gap-2 align-items-end flex-wrap";
            oRoot.Add(oForm);
            var oSearch = new TsgcHTMLComponent_InputGroup();
            oSearch.PrependText = "Search";
            oSearch.InputName = "q";
            oSearch.InputTypeEnum = TsgcHTMLInputType.itSearch;
            oSearch.InputValue = aSearch;
            oSearch.GroupID = "pos-cust-search";
            oForm.AddRaw(oSearch.HTML);
            var oGo = new TsgcHTMLButton("Search", TsgcHTMLButtonStyle.bsOutlineDark);
            oGo.ButtonType = "submit";
            oForm.Add(oGo);

            // The whole data layer of this page: one query, handed to the
            // component. No controller, no serializer, no REST endpoint.
            var oTable = new TsgcHTMLComponent_DataTable();
            oTable.TableID = "pos-customers";
            oTable.Title = "Loyalty members";
            oTable.ShowSearch = true;
            oTable.SearchPlaceholder = "Filter the loaded page";
            oTable.ShowPageSize = true;
            oTable.ShowRowCount = true;
            oTable.PageSizes = "10,25,50";
            if (aQuery != null)
                oTable.LoadFromDataSet(aQuery.DataSet, 10);
            oRoot.AddRaw(oTable.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell("Customers", vBody, aCtx);
        }

        public string BuildCustomerPage(TPOSCustomer aCust, TPOSSale[] aSales, TPOSPageCtx aCtx)
        {
            // Loyalty tier, 0..5 stars, one star per 200 points. The schema has no
            // product-feedback table, so the Rating shows the tier the points actually
            // earn instead of a made-up score.
            int vTier = aCust.LoyaltyPoints / 200;
            if (vTier > 5)
                vTier = 5;

            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3";
            oRoot.Add(oRow);

            TsgcHTMLContainer oCol = oRow.Children.AddElement("div", "", "col-12 col-lg-5");
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";
            var oAvatar = new TsgcHTMLComponent_Avatar();
            oAvatar.Initials = POSPagesUtils.Initials(aCust.Name);
            oAvatar.Size = TsgcHTMLAvatarSize.asLarge;
            oAvatar.Color = POSConst.CS_POS_ACCENT;
            oAvatar.AltText = aCust.Name;
            oCard.Body.AddRaw(oAvatar.HTML);
            oCard.Body.AddElement("div", aCust.Name, "fw-bold fs-5 mt-2");
            var oRating = new TsgcHTMLComponent_Rating();
            oRating.RatingID = "pos-cust-rating";
            oRating.Value = vTier;
            oRating.MaxValue = 5;
            oRating.ReadOnly = true;
            oRating.ShowValue = true;
            oCard.Body.AddRaw(oRating.HTML);
            var oDesc = new TsgcHTMLDescriptionList();
            oDesc.CSSClass = "row small mt-3 mb-0";
            oDesc.TermClass = "col-5 text-muted fw-normal";
            oDesc.DescClass = "col-7 text-end mb-0";
            oDesc.AddItem("Email", aCust.Email);
            oDesc.AddItem("Phone", aCust.Phone);
            oDesc.AddItem("Points", Part2IntStr(aCust.LoyaltyPoints));
            oDesc.AddItem("Member since", POSPagesUtils.WhenStr(aCust.CreatedAt));
            oCard.Body.Add(oDesc);
            oCol.Add(oCard);

            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mt-3";
            oCard.BodyClass = "card-body";
            oCard.Title = "Edit";
            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "pos-cust-form";
            oForm.Action = "/customers/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.SubmitText = "Save customer";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsDanger;
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftHidden;
            oField.Name = "id";
            oField.Value = Part2IntStr(aCust.Id);
            oField = oForm.Fields.Add();
            oField.Name = "name";
            oField.Label_ = "Name";
            oField.Value = aCust.Name;
            oField.Required = true;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftEmail;
            oField.Name = "email";
            oField.Label_ = "Email";
            oField.Value = aCust.Email;
            oField = oForm.Fields.Add();
            oField.Name = "phone";
            oField.Label_ = "Phone";
            oField.Value = aCust.Phone;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "loyalty_points";
            oField.Label_ = "Loyalty points";
            oField.Value = Part2IntStr(aCust.LoyaltyPoints);
            oCard.Body.AddRaw(oForm.HTML);
            oCol.Add(oCard);

            oCol = oRow.Children.AddElement("div", "", "col-12 col-lg-7");
            var oTimeline = new TsgcHTMLComponent_Timeline();
            oTimeline.TimelineID = "pos-cust-timeline";
            for (int vI = 0; vI < aSales.Length; vI++)
            {
                TsgcHTMLTimelineItem oItem = oTimeline.Items.Add();
                oItem.Title = aSales[vI].Reference + " - " + Money(aSales[vI].Total);
                oItem.Timestamp = POSPagesUtils.WhenStr(aSales[vI].CreatedAt);
                oItem.Content = Part2IntStr(aSales[vI].LineCount) + " lines, served by " +
                    aSales[vI].UserName;
                if (string.Equals(aSales[vI].Status, POSConst.CS_SALE_REFUNDED,
                    StringComparison.OrdinalIgnoreCase))
                    oItem.ColorStyle = TsgcHTMLColor.hcDanger;
                else
                    oItem.ColorStyle = TsgcHTMLColor.hcSuccess;
            }
            if (aSales.Length == 0)
                oCol.AddRaw(TsgcHTMLComponent_EmptyState.Build("No purchases yet",
                    "This member has not bought anything through the till."));
            else
                oCol.AddRaw(oTimeline.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell(aCust.Name, vBody, aCtx);
        }

        // ----- analytics ----- //

        public string BuildDashboardPage(TPOSTotals aToday, TPOSTotals aMonth,
            TPOSSeriesPoint[] aByHour, TPOSSeriesPoint[] aMix, TPOSHeatPoint[] aHeat,
            TPOSSeriesPoint[] aTop, string aRange, TPOSPageCtx aCtx)
        {
            // Row labels of the weekday x hour heatmap (the Delphi's local const).
            string[] CS_DAYS = new string[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "d-flex justify-content-between align-items-center mb-3";
            oRow.Children.AddElement("h1", "Dashboard", "h4 mb-0");
            oRoot.Add(oRow);
            var oRange = new TsgcHTMLComponent_Dropdown();
            oRange.DropdownID = "pos-range";
            oRange.ButtonText = "Range: " + aRange;
            oRange.ButtonStyleEnum = TsgcHTMLButtonStyle.bsOutlineDark;
            TsgcHTMLDropdownItem oDItem = oRange.Items.Add();
            oDItem.Text = "Today";
            oDItem.Href = "/dashboard?range=today";
            oDItem = oRange.Items.Add();
            oDItem.Text = "This week";
            oDItem.Href = "/dashboard?range=week";
            oDItem = oRange.Items.Add();
            oDItem.Text = "This month";
            oDItem.Href = "/dashboard?range=month";
            oDItem = oRange.Items.Add();
            oDItem.Text = "This year";
            oDItem.Href = "/dashboard?range=year";
            oRow.AddRaw(oRange.HTML);

            var oStats = new TsgcHTMLContainer("div");
            oStats.CSSClass = "row g-3 mb-3";
            oRoot.Add(oStats);

            // One gradient stat tile of the dashboard header row.
            Action<string, string, TsgcHTMLStatColor, TsgcHTMLStatGradient> oAddStat =
                (aTitle, aValue, aColor, aGradient) =>
                {
                    oStats.Children.AddElement("div", "", "col-6 col-xl-3").AddRaw(
                        TsgcHTMLComponent_StatCard.Build(aTitle, aValue, aColor,
                        TsgcHTMLStatTrend.stNone, "", "", aGradient));
                };

            oAddStat("Sales today", Part2IntStr(aToday.SaleCount),
                TsgcHTMLStatColor.scPrimary, TsgcHTMLStatGradient.sgBlueViolet);
            oAddStat("Takings today", Money(aToday.Gross),
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatGradient.sgGreenTeal);
            oAddStat("Tax collected", Money(aMonth.Tax),
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatGradient.sgBlueAqua);
            oAddStat("Refunded", Money(aMonth.Refunded),
                TsgcHTMLStatColor.scDanger, TsgcHTMLStatGradient.sgPinkRed);

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3";
            oRoot.Add(oRow);

            double[] vAmounts;

            // Sales by hour of day.
            TsgcHTMLContainer oCol = oRow.Children.AddElement("div", "", "col-12 col-xl-8");
            var oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "pos-hour-chart";
            oChart.ChartType = TsgcHTMLChartType.ctBar;
            oChart.Title = "Takings by hour of day";
            oChart.CSSHeight = "260px";
            vAmounts = new double[aByHour.Length];
            for (int vI = 0; vI < aByHour.Length; vI++)
            {
                oChart.AddLabel(aByHour[vI].BucketLabel);
                vAmounts[vI] = (double)aByHour[vI].Amount;
            }
            oChart.AddDataset("Takings", vAmounts, POSConst.CS_POS_ACCENT,
                "rgba(225,29,72,0.55)", true);
            oCol.AddRaw(oChart.HTML);

            // Payment mix.
            oCol = oRow.Children.AddElement("div", "", "col-12 col-xl-4");
            oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "pos-mix-chart";
            oChart.ChartType = TsgcHTMLChartType.ctDoughnut;
            oChart.Title = "Payment mix";
            oChart.CSSHeight = "260px";
            vAmounts = new double[aMix.Length];
            for (int vI = 0; vI < aMix.Length; vI++)
            {
                oChart.AddLabel(aMix[vI].BucketLabel);
                vAmounts[vI] = (double)aMix[vI].Amount;
            }
            // TsgcHTMLComponent_Chart carries a single backgroundColor string,
            // so a doughnut cannot colour its segments one by one. White arc
            // separators keep the slices apart, and the list underneath gives
            // the reader the number behind each one.
            oChart.AddDataset("Taken", vAmounts, "#FFFFFF", POSConst.CS_POS_ACCENT, true);
            oCol.AddRaw(oChart.HTML);

            var oMix = new TsgcHTMLDescriptionList();
            oMix.CSSClass = "row small mt-2 mb-0";
            oMix.TermClass = "col-6 text-muted fw-normal";
            oMix.DescClass = "col-6 text-end mb-0";
            for (int vI = 0; vI < aMix.Length; vI++)
                oMix.AddItem(aMix[vI].BucketLabel, Money(aMix[vI].Amount));
            oCol.Add(oMix);

            // Weekday x hour heatmap.
            oCol = oRow.Children.AddElement("div", "", "col-12 col-xl-8");
            var oHeat = new TsgcHTMLComponent_Heatmap();
            oHeat.ElementID = "pos-heatmap";
            oHeat.MinColor = "#FFE4E6";
            oHeat.MaxColor = POSConst.CS_POS_ACCENT;
            oHeat.CellSize = 30;
            oHeat.Rounded = true;
            oHeat.ShowLegend = true;
            oHeat.AutoMin = false;
            oHeat.MinValue = 0;
            for (int vI = 0; vI <= 6; vI++)
                oHeat.RowLabels.Add(CS_DAYS[vI]);
            for (int vI = 8; vI <= 21; vI++)
                oHeat.ColumnLabels.Add(vI.ToString("00", CultureInfo.InvariantCulture));
            for (int vI = 0; vI < aHeat.Length; vI++)
            {
                TsgcHTMLHeatmapCell oCell = oHeat.Cells.Add();
                oCell.Row = aHeat[vI].Weekday;
                oCell.Col = aHeat[vI].Hour - 8;
                oCell.Value = (double)aHeat[vI].Amount;
            }
            oCol.Children.AddElement("h2", "Busy hours, by weekday", "h6 mt-3");
            oCol.AddRaw(oHeat.HTML);

            // Target gauge + top sellers.
            oCol = oRow.Children.AddElement("div", "", "col-12 col-xl-4");
            var oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.GaugeID = "pos-today-gauge";
            oGauge.Title = "Today vs target";
            oGauge.Unit_ = FCurrencySymbol;
            oGauge.MinValue = 0;
            oGauge.MaxValue = (double)FShiftTarget;
            oGauge.Value = (double)aToday.Gross;
            oGauge.ThresholdMid = (double)FShiftTarget * 0.4;
            oGauge.ThresholdHigh = (double)FShiftTarget * 0.75;
            oGauge.Width = 220;
            oCol.AddRaw(oGauge.HTML);

            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "pos-top-products";
            oGrid.HeaderClass = "table-light";
            oGrid.EmptyText = "No sales in this range";
            TsgcHTMLGridColumn oGCol = oGrid.Columns.Add();
            oGCol.Title = "Product";
            oGCol = oGrid.Columns.Add();
            oGCol.Title = "Units";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            oGCol = oGrid.Columns.Add();
            oGCol.Title = "Value";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;
            for (int vI = 0; vI < aTop.Length; vI++)
                oGrid.AddRow(aTop[vI].BucketLabel, Part2IntStr(aTop[vI].Count),
                    Money(aTop[vI].Amount));
            oCol.Children.AddElement("h2", "Top sellers", "h6 mt-3");
            oCol.AddRaw(oGrid.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell("Dashboard", vBody, aCtx);
        }

        public string BuildReportsPage(TPOSQuery aPivot, TPOSSeriesPoint[] aByMonth,
            string aFrom, string aTo, TPOSTotals aTotals, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddElement("h1", "Reports", "h4 mb-3");

            var oToolbar = new TsgcHTMLComponent_Toolbar();
            oToolbar.ToolbarID = "pos-reports-toolbar";
            TsgcHTMLToolbarItem oItem = oToolbar.Items.Add();
            oItem.Text = "Sales report (PDF)";
            // the toolbar sanitizes and attribute-encodes the URL itself
            oItem.Href = "/reports/sales.pdf?from=" + aFrom + "&to=" + aTo;
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsDanger;
            oItem = oToolbar.Items.Add();
            oItem.Text = "Sales report (XLSX)";
            oItem.Href = "/reports/sales.xlsx?from=" + aFrom + "&to=" + aTo;
            oRoot.AddRaw(oToolbar.HTML);

            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/reports";
            oForm.CSSClass = "my-3";
            oRoot.Add(oForm);
            var oRange = new TsgcHTMLComponent_DateRangePicker();
            oRange.ElementID = "pos-report-range";
            oRange.FieldNameStart = "from";
            oRange.FieldNameEnd = "to";
            oRange.LabelStart = "From";
            oRange.LabelEnd = "To";
            oRange.StartValue = aFrom;
            oRange.EndValue = aTo;
            oRange.ShowPresets = true;
            oForm.AddRaw(oRange.HTML);
            var oBtn = new TsgcHTMLButton("Run", TsgcHTMLButtonStyle.bsOutlineDark);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "mt-2";
            oForm.Add(oBtn);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-3";
            oCard.BodyClass = "card-body";
            oCard.Title = "Range totals";
            var oDesc = new TsgcHTMLDescriptionList();
            oDesc.CSSClass = "row mb-0";
            oDesc.TermClass = "col-6 col-md-3 text-muted fw-normal";
            oDesc.DescClass = "col-6 col-md-3 text-end mb-0";
            oDesc.AddItem("Sales", Part2IntStr(aTotals.SaleCount));
            oDesc.AddItem("Net of tax", Money(aTotals.NetSubtotal));
            oDesc.AddItem("Discount", Money(aTotals.Discount));
            oDesc.AddItem("Tax", Money(aTotals.Tax));
            oDesc.AddItem("Gross", Money(aTotals.Gross));
            oDesc.AddItem("Refunded", Money(aTotals.Refunded));
            oCard.Body.Add(oDesc);
            oRoot.Add(oCard);

            var oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "pos-month-chart";
            oChart.ChartType = TsgcHTMLChartType.ctLine;
            oChart.Title = "Takings by month";
            oChart.CSSHeight = "260px";
            oChart.PointRadius = 4;
            double[] vAmounts = new double[aByMonth.Length];
            for (int vI = 0; vI < aByMonth.Length; vI++)
            {
                oChart.AddLabel(aByMonth[vI].BucketLabel);
                vAmounts[vI] = (double)aByMonth[vI].Amount;
            }
            oChart.AddDataset("Takings", vAmounts, POSConst.CS_POS_ACCENT,
                "rgba(225,29,72,0.20)", true);
            oRoot.AddRaw(oChart.HTML);

            // Sales by category and month, pivoted straight out of the query.
            var oPivot = new TsgcHTMLComponent_PivotTable();
            oPivot.TableID = "pos-pivot";
            oPivot.Caption = "Sales by category and month";
            oPivot.Compact = true;
            // The field set has to be declared BEFORE LoadFromDataSet: the
            // component builds its DataFields list from the row, column and
            // measure names and then reads exactly those columns out of the
            // cursor. Loading first leaves the column groups empty.
            TsgcHTMLPivotField oPField = oPivot.RowFields.Add();
            oPField.FieldName = "category";
            oPField.Caption = "Category";
            oPField = oPivot.ColumnFields.Add();
            oPField.FieldName = "month";
            oPField.Caption = "Month";
            TsgcHTMLPivotMeasure oMeasure = oPivot.Measures.Add();
            oMeasure.SourceField = "amount";
            oMeasure.Caption = "Net";
            oMeasure.Aggregation = TsgcHTMLPivotAggregation.paSum;
            oMeasure.Format = "0.00";
            if (aPivot != null)
                oPivot.LoadFromDataSet(aPivot.DataSet);
            oRoot.AddElement("h2", "Pivot", "h6 mt-4");
            oRoot.AddRaw(oPivot.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell("Reports", vBody, aCtx);
        }

        // The point of the whole demo, on one page: the exact statement on the left,
        // the live component it feeds on the right, and nothing in between.
        public string BuildSQLPage(TPOSQuery aQuery, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddElement("h1", "No REST layer", "h4 mb-2");
            var oLead = new TsgcHTMLParagraph(
                "This page runs one SQL statement against the till database and hands " +
                "the resulting cursor to a TsgcHTMLComponent_Grid through " +
                "LoadFromDataSet. There is no controller, no JSON, no DTO and no HTTP " +
                "endpoint between the two: a Delphi shop can put the database it " +
                "already has on the web without building an API tier first.");
            oLead.CSSClass = "text-muted";
            oRoot.Add(oLead);

            var oLeft = new TsgcHTMLNodeList();
            var oRight = new TsgcHTMLNodeList();

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "h-100";
            oCard.BodyClass = "card-body";
            oCard.Title = "The statement";
            var oPre = new TsgcHTMLContainer("pre");
            oPre.CSSClass = "bg-body-tertiary p-3 rounded small mb-0";
            oPre.Style = "white-space:pre-wrap;";
            var oCode = new TsgcHTMLContainer("code");
            if (aQuery != null)
                oCode.AddText(aQuery.SQL);
            else
                oCode.AddText("");
            oPre.Add(oCode);
            oCard.Body.Add(oPre);
            oLeft.AddRaw(oCard.HTML);

            var oSteps = new TsgcHTMLComponent_Timeline();
            oSteps.TimelineID = "pos-sql-steps";
            TsgcHTMLTimelineItem oItem = oSteps.Items.Add();
            oItem.Title = "TPOSDBPool.OpenQuery";
            oItem.Content = "Takes a pooled connection and opens the statement on it.";
            oItem.ColorStyle = TsgcHTMLColor.hcPrimary;
            oItem = oSteps.Items.Add();
            oItem.Title = "Grid.LoadFromDataSet(oQuery.DataSet)";
            oItem.Content = "The component walks the very cursor SQLite " +
                "returned. 27 sgcHTML components accept a data set this way.";
            oItem.ColorStyle = TsgcHTMLColor.hcSuccess;
            oItem = oSteps.Items.Add();
            oItem.Title = "Grid.HTML";
            oItem.Content = "Markup, rendered server-side, straight into the response.";
            oItem.ColorStyle = TsgcHTMLColor.hcWarning;
            oLeft.AddRaw(oSteps.HTML);

            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "pos-sql-grid";
            oGrid.HeaderClass = "table-light";
            oGrid.ShowSort = true;
            oGrid.ShowFilter = true;
            oGrid.EmptyText = "The query returned no rows";
            oGrid.ExportXLSX = true;
            oGrid.ExportURL = "/reports/sales.xlsx";
            if (aQuery != null)
                oGrid.LoadFromDataSet(aQuery.DataSet);
            oRight.AddRaw(oGrid.HTML);

            var oSplit = new TsgcHTMLComponent_Splitter();
            oSplit.SplitterID = "pos-sql-split";
            oSplit.InitialSplit = 42;
            oSplit.CSSHeight = "70vh";
            oSplit.PersistKey = "pos-sql-split";
            oSplit.AddPaneA(oLeft.HTML);
            oSplit.AddPaneB(oRight.HTML);
            oRoot.AddRaw(oSplit.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell("SQL", vBody, aCtx);
        }

        // ----- admin ----- //

        public string BuildUsersPage(TPOSQuery aQuery, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddElement("h1", "Users and roles", "h4 mb-3");

            var oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.TabsID = "pos-users-tabs";
            oTabs.Style = TsgcHTMLTabStyle.tsTab;

            var oPane = new TsgcHTMLNodeList();
            var oUsers = new TsgcHTMLComponent_UserManagement();
            oUsers.TableID = "pos-users";
            oUsers.ShowAddButton = false;
            oUsers.ShowLastLogin = false;
            oUsers.ShowActions = false;
            oUsers.EmptyText = "No accounts";
            if (aQuery != null)
                oUsers.LoadFromDataSet(aQuery.DataSet, "id", "username",
                    "display_name", "role");
            oPane.AddRaw(oUsers.HTML);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mt-3";
            oCard.BodyClass = "card-body";
            oCard.Title = "Add or update an account";
            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "pos-user-form";
            oForm.Action = "/users/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.SubmitText = "Save account";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsDanger;
            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "id";
            oField.Label_ = "Id (blank for a new account)";
            oField.ColSpan = 4;
            oField = oForm.Fields.Add();
            oField.Name = "username";
            oField.Label_ = "User name";
            oField.ColSpan = 4;
            oField.Required = true;
            oField = oForm.Fields.Add();
            oField.Name = "display_name";
            oField.Label_ = "Display name";
            oField.ColSpan = 4;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "role";
            oField.Label_ = "Role";
            oField.ColSpan = 4;
            oField.Options.Add(POSConst.CS_ROLE_CASHIER + "=Cashier");
            oField.Options.Add(POSConst.CS_ROLE_MANAGER + "=Manager");
            oField.Options.Add(POSConst.CS_ROLE_ADMIN + "=Admin");
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "password";
            oField.Label_ = "Password (blank keeps it)";
            oField.ColSpan = 4;
            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "pin";
            oField.Label_ = "Manager PIN (blank keeps it)";
            oField.ColSpan = 4;
            oField.HelpText = "Stored as a bcrypt hash, never in the clear.";
            oCard.Body.AddRaw(oForm.HTML);
            oPane.AddRaw(oCard.HTML);

            TsgcHTMLTabItem oTab = oTabs.Items.Add();
            oTab.Title = "Accounts";
            oTab.TabID = "pos-tab-users";
            oTab.Active = true;
            oTab.Content = oPane.HTML;

            var oRoles = new TsgcHTMLComponent_RolesPermissions();
            oRoles.MatrixID = "pos-roles";
            oRoles.ReadOnly = true;
            oRoles.AddRole(POSConst.CS_ROLE_CASHIER, "Cashier");
            oRoles.AddRole(POSConst.CS_ROLE_MANAGER, "Manager");
            oRoles.AddRole(POSConst.CS_ROLE_ADMIN, "Admin");
            oRoles.AddPermission("till.sell", "Ring up a sale", "Till");
            oRoles.AddPermission("till.park", "Park and recall", "Till");
            oRoles.AddPermission("till.refund", "Refund a sale", "Till");
            oRoles.AddPermission("till.discount", "Discount over the threshold", "Till");
            oRoles.AddPermission("shift.open", "Open a shift", "Shift");
            oRoles.AddPermission("shift.close", "Close and count", "Shift");
            oRoles.AddPermission("catalogue.edit", "Edit the catalogue", "Back office");
            oRoles.AddPermission("reports.view", "See the analytics", "Back office");
            oRoles.AddPermission("users.manage", "Manage accounts", "Admin");

            // Grants the permission to the role on the read-only matrix.
            Action<string, string> oGrant = (aRole, aPerm) =>
            {
                oRoles.ProcessGrantChanged(aRole, aPerm, true);
            };

            oGrant(POSConst.CS_ROLE_CASHIER, "till.sell");
            oGrant(POSConst.CS_ROLE_CASHIER, "till.park");
            oGrant(POSConst.CS_ROLE_CASHIER, "shift.open");
            oGrant(POSConst.CS_ROLE_CASHIER, "shift.close");
            oGrant(POSConst.CS_ROLE_MANAGER, "till.sell");
            oGrant(POSConst.CS_ROLE_MANAGER, "till.park");
            oGrant(POSConst.CS_ROLE_MANAGER, "till.refund");
            oGrant(POSConst.CS_ROLE_MANAGER, "till.discount");
            oGrant(POSConst.CS_ROLE_MANAGER, "shift.open");
            oGrant(POSConst.CS_ROLE_MANAGER, "shift.close");
            oGrant(POSConst.CS_ROLE_MANAGER, "catalogue.edit");
            oGrant(POSConst.CS_ROLE_MANAGER, "reports.view");
            oGrant(POSConst.CS_ROLE_ADMIN, "till.sell");
            oGrant(POSConst.CS_ROLE_ADMIN, "till.park");
            oGrant(POSConst.CS_ROLE_ADMIN, "till.refund");
            oGrant(POSConst.CS_ROLE_ADMIN, "till.discount");
            oGrant(POSConst.CS_ROLE_ADMIN, "shift.open");
            oGrant(POSConst.CS_ROLE_ADMIN, "shift.close");
            oGrant(POSConst.CS_ROLE_ADMIN, "catalogue.edit");
            oGrant(POSConst.CS_ROLE_ADMIN, "reports.view");
            oGrant(POSConst.CS_ROLE_ADMIN, "users.manage");

            oTab = oTabs.Items.Add();
            oTab.Title = "Roles and permissions";
            oTab.TabID = "pos-tab-roles";
            oTab.Content = oRoles.HTML;

            oRoot.AddRaw(oTabs.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell("Users", vBody, aCtx);
        }

        // The audit trail is filled row by row rather than through LoadFromDataSet.
        //
        // The component reads its timestamp column as a typed date, and this demo
        // stores every timestamp as ISO-8601 TEXT on purpose. A SQLite TEXT column
        // comes back as a string field, whose date conversion follows the machine
        // locale and mis-parses the ISO shape outside the US, so the dates are
        // parsed here with the unit's own fixed-position reader and handed to the
        // component already typed.
        public string BuildAuditPage(TPOSAuditEntry[] aEntries, TPOSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oAudit = new TsgcHTMLComponent_AuditTrail();
            oAudit.AuditID = "pos-audit";
            oAudit.Title = "Audit trail";
            oAudit.ShowFilter = true;
            oAudit.FilterPlaceholder = "Filter by user, action or entity";
            oAudit.PageSize = 25;
            oAudit.EmptyText = "Nothing logged yet";
            // .NET spelling of the Delphi 'yyyy-mm-dd hh:nn:ss' picture string.
            oAudit.TimeFormat = "yyyy-MM-dd HH:mm:ss";
            for (int vI = 0; vI < aEntries.Length; vI++)
            {
                TsgcHTMLAuditEntry oEntry = oAudit.AddEntry(aEntries[vI].UserName,
                    aEntries[vI].Action,
                    aEntries[vI].Entity + " #" + Part2IntStr(aEntries[vI].EntityId),
                    aEntries[vI].IP, "success", aEntries[vI].Detail);
                oEntry.Timestamp = aEntries[vI].CreatedAt;
            }
            oRoot.AddRaw(oAudit.HTML);

            string vBody = oRoot.HTML;
            return BuildPageShell("Audit", vBody, aCtx);
        }

        public string BuildNotFoundPage(TPOSPageCtx aCtx)
        {
            string vBody = TsgcHTMLComponent_EmptyState.Build("404, nothing here",
                "That route does not exist on this till.");
            return BuildPageShell("Not found", vBody, aCtx);
        }
}
}
