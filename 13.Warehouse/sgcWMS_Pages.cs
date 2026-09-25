// ***************************************************************************
//  sgcWMS - warehouse management web-app demo
//  Port of delphi\Demos\60.HTML\01.RunTime\13.Warehouse\sgcWMS_Pages.pas
//
//  written by eSeGeCe
//  copyright © 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
//
//  Back-office view layer.
//
//  Component layer first: every block on every page is a TsgcHTMLComponent_*
//  (Grid, DataTable, TreeView, TreeGrid, Chart, Heatmap, TreeMap, Gauge,
//  StatCard, Stepper, Toolbar, Panel, Pagination, EmptyState, CommandPalette,
//  SignaturePad, Barcode, QRCode, ...). The sgcHTML node layer is used only as
//  glue - rows, columns and cards around those components. There is not one
//  hand-written HTML string: the only AddRaw inputs are a component's rendered
//  .HTML, a value already escaped through HtmlEsc, or a trusted entity.
//
//  The pages talk to the database directly through TWMSDBPool: a query is
//  opened and handed to LoadFromDataSet. No REST tier, no DTOs.
//
//  The managed port keeps the Delphi class + method names; .NET is GC-managed
//  so the Delphi .Free calls are dropped and the try/finally pairs that only
//  existed to free an object collapse into plain statements. The TWMSDataSet
//  instances still own a pooled connection, so those keep a using block.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
// sgc
using esegece.sgcWebSockets;

namespace WMS
{
    /// <summary>
    /// Filters carried by the list pages, so the server can hand one value
    /// around instead of eight loose strings.
    /// </summary>
    public class TWMSListFilter
    {
        public string Search = "";
        public string Category = "";
        public string Zone = "";
        public string Status = "";
        public string Kind = "";
        public string Sort = "";
        public string Dir = "";
        // Inclusive day bounds of the movement ledger, 'yyyy-mm-dd' or blank.
        public string DateFrom = "";
        public string DateTo = "";
        public int Page;
        public bool BelowMin;
    }

    /// <summary>
    /// The Delphi unit-level functions of sgcWMS_Pages, exposed so the Server
    /// and Handheld units can call them.
    /// </summary>
    public static class WMSPagesHelpers
    {
        /// <summary>Minimal HTML escape for values spliced into raw markup.</summary>
        public static string HtmlEsc(string aValue)
        {
            string vResult = (aValue ?? string.Empty).Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            vResult = vResult.Replace("'", "&#39;");
            return vResult;
        }

        /// <summary>Percent-encode a query-string value.</summary>
        public static string UrlEnc(string aValue)
        {
            if (string.IsNullOrEmpty(aValue))
                return string.Empty;
            return Uri.EscapeDataString(aValue);
        }
    }

    public class TWMSPages
    {
        // ------------------------------------------------------------ assets --

        // Self-contained inline logo (no external asset, the demos run offline).
        private const string CS_LOGO_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 240 64\" width=\"200\" height=\"54\" role=\"img\" " +
            "aria-label=\"sgcWMS\"><rect x=\"0\" y=\"6\" width=\"52\" height=\"52\" rx=\"12\" " +
            "fill=\"#F59E0B\"/><path d=\"M12 32 L26 22 L40 32 L40 46 L12 46 Z\" " +
            "fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"4\" " +
            "stroke-linejoin=\"round\"/><rect x=\"21\" y=\"34\" width=\"10\" height=\"12\" " +
            "fill=\"#FFFFFF\"/><text x=\"66\" y=\"44\" " +
            "font-family=\"Arial,Helvetica,sans-serif\" font-weight=\"800\" " +
            "font-size=\"30\" fill=\"#7C4A03\">sgcWMS</text></svg>";

        private const string CS_MARK_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 52 52\" " +
            "width=\"24\" height=\"24\" role=\"img\" aria-label=\"sgcWMS\"><rect width=\"52\" " +
            "height=\"52\" rx=\"12\" fill=\"#F59E0B\"/><path d=\"M11 28 L26 17 L41 28 L41 " +
            "43 L11 43 Z\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"4\" " +
            "stroke-linejoin=\"round\"/></svg>";

        // Base CSS. Amber accent, plus the two rules that keep the wide grids
        // scrolling inside their own container instead of widening the page.
        private const string CS_BASE_CSS = ":root{--wms-accent:#F59E0B;--wms-accent-dark:#B45309;}" +
            "body{background:#f6f7f9;}" + ".wms-main{min-width:0;}" +
            ".wms-shell{min-height:100vh;}" +
            ".table-responsive{-webkit-overflow-scrolling:touch;}" +
            ".wms-accent{color:var(--wms-accent-dark);}" +
            ".btn-wms{background:var(--wms-accent);border-color:var(--wms-accent);" +
            "color:#3b2600;font-weight:600;}" +
            ".btn-wms:hover{background:var(--wms-accent-dark);" +
            "border-color:var(--wms-accent-dark);color:#fff;}" +
            ".wms-kpi .card{border-left:4px solid var(--wms-accent);}" +
            ".wms-sql pre{background:#0f172a;color:#e2e8f0;padding:1rem;" +
            "border-radius:.5rem;font-size:.8rem;overflow-x:auto;margin:0;}" +
            ".wms-side .nav-link.active{background:var(--wms-accent) !important;" +
            "color:#3b2600 !important;font-weight:600;}" +
            ".wms-tight td,.wms-tight th{padding:.4rem .5rem;font-size:.875rem;}" +
            "table tbody tr[onclick]{cursor:pointer;}" +
            // Charts, gauges, sparklines, barcodes and the signature pad all carry
            // their natural pixel size. Every one of them has a viewBox (or, for the
            // canvas, re-measures itself from clientWidth), so clamping the rendered
            // width is enough to keep a 390px viewport from scrolling sideways.
            ".wms-main svg{max-width:100%;height:auto;}" +
            ".wms-main canvas{max-width:100%;}" +
            ".wms-main img{max-width:100%;height:auto;}" +
            "@media (max-width:575.98px){.wms-kpi .card-body{padding:.75rem;}" +
            ".wms-hide-xs{display:none !important;}}";

        private const string CS_DARK_CSS = "body{background:#12151b;color:#e4e6eb;}" +
            ".card{background:#1e222b;border:1px solid #2c313c;color:#e4e6eb;}" +
            ".card-header{background:#252a34;border-bottom:1px solid #2c313c;}" +
            ".table{color:#e4e6eb;}" + ".text-muted{color:#8b93a7 !important;}" +
            ".form-control,.form-select{background:#1e222b;border-color:#2c313c;" +
            "color:#e4e6eb;}" + ".form-control:focus,.form-select:focus{" +
            "background:#252a34;color:#e4e6eb;border-color:#F59E0B;}" +
            ".list-group-item{background:#1e222b;border-color:#2c313c;" +
            "color:#e4e6eb;}" + ".page-link{background:#1e222b;border-color:#2c313c;" +
            "color:#e4e6eb;}" + ".page-item.active .page-link{background:#F59E0B;" +
            "border-color:#F59E0B;color:#3b2600;}" + "a{color:#f0b429;}" +
            ".dropdown-menu{background:#1e222b;border-color:#2c313c;}" +
            ".dropdown-item{color:#e4e6eb;}" + ".dropdown-item:hover," +
            ".dropdown-item:focus{background:#2c313c;color:#fff;}" +
            ".wms-accent{color:#f0b429;}" + ".bg-light{background:#1e222b !important;}";

        // ------------------------------------------------------------ fields --

        private readonly TWMSDBPool FDB;

        // Index of the product grid's action column. The grid renders every cell
        // escaped, which is right for data; this one column is the exception and
        // is filled by GridActionCell below.
        private int FActionCol;

        public TWMSPages(TWMSDBPool aDB)
        {
            FDB = aDB;
            FActionCol = -1;
        }

        // ----------------------------------------------------- unit helpers ---

        /// <summary>Minimal HTML escape for values spliced into raw markup.</summary>
        internal static string HtmlEsc(string aValue)
        {
            return WMSPagesHelpers.HtmlEsc(aValue);
        }

        /// <summary>Percent-encode a query-string value.</summary>
        internal static string UrlEnc(string aValue)
        {
            return WMSPagesHelpers.UrlEnc(aValue);
        }

        // TsgcHTMLComponent_InputGroup writes InputValue into the value attribute
        // verbatim, unlike Edit / Form / Select which attribute-encode it. Anything
        // that came from the query string therefore has to be escaped here before it
        // is handed over, or a crafted search term would break out of the attribute.
        private static string SafeInputValue(string aValue)
        {
            return HtmlEsc(aValue);
        }

        private static string FmtDate(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static string FmtDay(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string FmtMoney(double aValue)
        {
            return aValue.ToString("#,##0.00", CultureInfo.InvariantCulture);
        }

        private static string FmtInt(long aValue)
        {
            return aValue.ToString("#,##0", CultureInfo.InvariantCulture);
        }

        // Single-quote a literal for an inline SQL fragment (Delphi QuotedStr).
        private static string QuotedStr(string aValue)
        {
            return "'" + (aValue ?? string.Empty).Replace("'", "''") + "'";
        }

        // Give the grid columns readable headers. aPairs is field, label, field,
        // label... Anything not named keeps the raw field name.
        private static void SetLabels(TWMSDataSet aData, string[] aPairs)
        {
            if ((aData == null) || (aData.DataSet == null))
                return;
            int vI = 0;
            while (vI < aPairs.Length - 1)
            {
                if (aData.DataSet.Columns.Contains(aPairs[vI]))
                    aData.DataSet.Columns[aPairs[vI]].Caption = aPairs[vI + 1];
                vI += 2;
            }
        }

        // Status pill, shared by the PO / SO / count lists.
        private static string StatusBadge(string aStatus)
        {
            string vS = (aStatus ?? string.Empty).Trim().ToLowerInvariant();
            if ((vS == "closed") || (vS == "shipped"))
                return TsgcHTMLComponent_Badge.Build(vS, TsgcHTMLBadgeStyle.bgSuccess, true);
            if ((vS == "receiving") || (vS == "picking"))
                return TsgcHTMLComponent_Badge.Build(vS, TsgcHTMLBadgeStyle.bgWarning, true);
            if ((vS == "packed") || (vS == "sent"))
                return TsgcHTMLComponent_Badge.Build(vS, TsgcHTMLBadgeStyle.bgInfo, true);
            if ((vS == "open") || (vS == "new"))
                return TsgcHTMLComponent_Badge.Build(vS, TsgcHTMLBadgeStyle.bgPrimary, true);
            return TsgcHTMLComponent_Badge.Build(vS, TsgcHTMLBadgeStyle.bgSecondary, true);
        }

        private static string PriorityBadge(string aPriority)
        {
            string vS = (aPriority ?? string.Empty).Trim().ToLowerInvariant();
            if (vS == "urgent")
                return TsgcHTMLComponent_Badge.Build("urgent", TsgcHTMLBadgeStyle.bgDanger, true);
            if (vS == "high")
                return TsgcHTMLComponent_Badge.Build("high", TsgcHTMLBadgeStyle.bgWarning, true);
            return TsgcHTMLComponent_Badge.Build("normal", TsgcHTMLBadgeStyle.bgLight, true);
        }

        // Wrap already-rendered HTML in a div, through the node layer rather than a
        // hand-written wrapper string. Used for the one-off layout boxes (scroll
        // containers, centred blocks) that no component covers.
        private static string Wrap(string aCSSClass, string aHTML, string aStyle = "")
        {
            var oBox = new TsgcHTMLContainer("div");
            oBox.CSSClass = aCSSClass;
            oBox.Style = aStyle;
            oBox.AddRaw(aHTML);
            return oBox.HTML;
        }

        // Card wrapper built from the node layer: title + already-rendered body.
        private static string Card(string aTitle, string aSubtitle, string aBodyHTML,
            string aHeaderExtra = "")
        {
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4";
            if (aHeaderExtra != "")
            {
                var oHead = new TsgcHTMLContainer("div");
                oHead.CSSClass =
                    "d-flex flex-wrap gap-2 justify-content-between align-items-center";
                var oH = new TsgcHTMLHeading(aTitle, 5);
                oH.CSSClass = "mb-0";
                oHead.Add(oH);
                oHead.AddRaw(aHeaderExtra);
                oCard.Header.Add(oHead);
            }
            else
                oCard.Title = aTitle;
            if (aSubtitle != "")
            {
                var oSub = new TsgcHTMLParagraph(aSubtitle);
                oSub.CSSClass = "text-muted small mb-3";
                oCard.Body.Add(oSub);
            }
            oCard.Body.AddRaw(aBodyHTML);
            return oCard.HTML;
        }

        // One bootstrap row of already-rendered blocks, aWidth columns wide each.
        private static string Cols(string[] aParts, TsgcHTMLColWidth aWidth)
        {
            var oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            for (int vI = 0; vI < aParts.Length; vI++)
                oRow.Col(aWidth).AddRaw(aParts[vI]);
            return oRow.HTML;
        }

        // A responsive KPI strip: 2 across on phones, 3 on tablets, 6 on desktop.
        private static string KPIStrip(string[] aCards)
        {
            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4 wms-kpi";
            for (int vI = 0; vI < aCards.Length; vI++)
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-6 col-md-4 col-xl-2";
                oCol.AddRaw(aCards[vI]);
                oRow.Add(oCol);
            }
            return oRow.HTML;
        }

        // ----------------------------------------------------- grid actions ---

        // Row actions of the product grid: an htmx Edit that swaps the form card
        // in, and an htmx Delete that swaps the refreshed table back.
        private void GridActionCell(object Sender, int aRowIndex, int aColIndex,
            string aValue, ref string aHTML)
        {
            if (aColIndex != FActionCol)
                return;
            // Never trust the cell text: it must be the row id and nothing else.
            long vId;
            if (!long.TryParse((aValue ?? string.Empty).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out vId))
                vId = 0;
            if (vId <= 0)
            {
                aHTML = "&nbsp;";
                return;
            }

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "d-flex gap-1 justify-content-end";

            var oBtn = new TsgcHTMLButton("Edit", TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBtn.CSSClass = "btn-sm";
            oBtn.Attributes = "hx-get=\"/products/form?id=" +
                vId.ToString(CultureInfo.InvariantCulture) +
                "\" hx-target=\"#wmsProductForm\" hx-swap=\"innerHTML\"";
            oWrap.Add(oBtn);

            oBtn = new TsgcHTMLButton("Delete", TsgcHTMLButtonStyle.bsOutlineDanger);
            oBtn.CSSClass = "btn-sm";
            oBtn.Attributes = "hx-post=\"/products/delete\" hx-vals='{\"id\": " +
                vId.ToString(CultureInfo.InvariantCulture) +
                "}' hx-target=\"#wmsProductsTable\" " +
                "hx-swap=\"innerHTML\" hx-confirm=\"Delete this product and its stock " +
                "rows?\"";
            oWrap.Add(oBtn);

            aHTML = oWrap.HTML;
        }

        // ---------------------------------------------------------------- shell ----

        private string SystemThemeScript()
        {
            return "<script>(function(){if(window.matchMedia&&window.matchMedia(" +
                "'(prefers-color-scheme: dark)').matches){document.documentElement." +
                "setAttribute('data-bs-theme','dark');}})();</script>";
        }

        private string WrapTemplate(string aTitle, string aBody, string aTheme)
        {
            var oTpl = new TsgcHTMLTemplate_Bootstrap();
            oTpl.Title = aTitle;
            oTpl.HtmlLang = "en";
            oTpl.Viewport = "width=device-width, initial-scale=1";
            oTpl.HeadNodes.AddRaw("<link rel=\"icon\" type=\"image/svg+xml\" " +
                "href=\"/favicon.svg\">");
            if (string.Equals(aTheme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "dark";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_BASE_CSS + CS_DARK_CSS;
            }
            else if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "light";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_BASE_CSS;
            }
            else
            {
                oTpl.HtmlTheme = "";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_BASE_CSS + "@media (prefers-color-scheme: dark){" +
                    CS_DARK_CSS + "}";
                oTpl.HeadNodes.AddRaw(SystemThemeScript());
            }
            oTpl.BodyContent = aBody;
            oTpl.BodyEndNodes.AddRaw("<script src=\"/htmx.min.js\"></script>");
            return oTpl.GetHTML();
        }

        private string BuildSidebar(TWMSPageCtx aCtx)
        {
            var oSide = new TsgcHTMLComponent_Sidebar();
            oSide.SidebarID = "wmsSide";
            oSide.Brand = "sgcWMS";
            oSide.BrandHref = "/";
            oSide.CSSWidth = "250px";
            oSide.Dark = true;
            oSide.Fixed = false;
            oSide.Responsive = true;
            oSide.CSSClass = "wms-side";
            oSide.FooterText = WMSConst.WMSRoleCaption(aCtx.Role);

            Action<string, string, string> addItem = (aText, aHref, aKey) =>
            {
                TsgcHTMLSidebarItem oItem = oSide.Items.Add();
                oItem.Text = aText;
                oItem.Href = aHref;
                oItem.Active = string.Equals(aCtx.Menu, aKey,
                    StringComparison.OrdinalIgnoreCase);
            };

            Action<string> addHeader = (aText) =>
            {
                TsgcHTMLSidebarItem oItem = oSide.Items.Add();
                oItem.Text = aText;
                oItem.Header = true;
            };

            addItem("Dashboard", "/", "dashboard");
            addHeader("Master data");
            addItem("Products", "/products", "products");
            addItem("Locations", "/locations", "locations");
            addHeader("Operations");
            addItem("Inbound", "/inbound", "inbound");
            addItem("Outbound", "/outbound", "outbound");
            addItem("Stock on hand", "/stock", "stock");
            addItem("Movements", "/stock/movements", "movements");
            addItem("Cycle counts", "/counts", "counts");
            addHeader("Insight");
            addItem("Reports", "/reports", "reports");
            addItem("SQL, no REST", "/sql", "sql");
            addHeader("Devices");
            addItem("Handheld", "/hh", "hh");
            if (WMSConst.WMSRoleIsAdmin(aCtx.Role))
            {
                addHeader("Administration");
                addItem("Users", "/users", "users");
                addItem("Audit log", "/audit", "audit");
            }
            addItem("Security", "/security", "security");

            return oSide.HTML;
        }

        private string BuildUserMenu(TWMSPageCtx aCtx)
        {
            string vName = aCtx.DisplayName;
            if (vName == "")
                vName = aCtx.Username;

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "d-flex align-items-center gap-2";
            string vUpper = (vName ?? string.Empty).ToUpperInvariant();
            if (vUpper.Length > 2)
                vUpper = vUpper.Substring(0, 2);
            oWrap.AddRaw(TsgcHTMLComponent_Avatar.Build(vUpper,
                TsgcHTMLAvatarSize.asSmall, WMSConst.CS_WMS_ACCENT,
                TsgcHTMLAvatarStatus.atOnline));

            var oDrop = new TsgcHTMLComponent_Dropdown();
            oDrop.DropdownID = "wmsUserMenu";
            oDrop.ButtonText = vName;
            oDrop.ButtonStyleEnum = TsgcHTMLButtonStyle.bsOutlineSecondary;
            TsgcHTMLDropdownItem oItem = oDrop.Items.Add();
            oItem.Text = WMSConst.WMSRoleCaption(aCtx.Role);
            oItem.Header = true;
            oItem = oDrop.Items.Add();
            oItem.Text = "Security and passkeys";
            oItem.Href = "/security";
            oItem = oDrop.Items.Add();
            oItem.Divider = true;
            oWrap.AddRaw(oDrop.HTML);

            // Theme switch + logout are POST forms, so they cannot live inside the
            // Dropdown item collection (which only renders links).
            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/theme";
            oForm.CSSClass = "m-0";
            if (string.Equals(aCtx.Theme, "dark", StringComparison.OrdinalIgnoreCase))
                oForm.AddHidden("theme", "light");
            else
                oForm.AddHidden("theme", "dark");
            var oBtn = new TsgcHTMLButton("Theme", TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "btn-sm";
            oForm.Add(oBtn);
            oWrap.Add(oForm);

            oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/logout";
            oForm.CSSClass = "m-0";
            oBtn = new TsgcHTMLButton("Sign out", TsgcHTMLButtonStyle.bsOutlineDanger);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "btn-sm";
            oForm.Add(oBtn);
            oWrap.Add(oForm);

            return oWrap.HTML;
        }

        private string BuildTopbar(TWMSPageCtx aCtx, string aTitle, string[] aCrumbs)
        {
            var oBar = new TsgcHTMLContainer("div");
            oBar.CSSClass = "d-flex flex-wrap gap-2 justify-content-between " +
                "align-items-center mb-3";

            var oLeft = new TsgcHTMLContainer("div");
            oLeft.CSSClass = "d-flex align-items-center gap-2";

            // Hamburger that opens the sidebar as a drawer below 992px.
            var oSide = new TsgcHTMLComponent_Sidebar();
            oSide.SidebarID = "wmsSide";
            oLeft.AddRaw(oSide.GetToggleButtonHTML());

            var oH = new TsgcHTMLHeading(aTitle, 1);
            oH.CSSClass = "h4 mb-0";
            oLeft.Add(oH);
            oBar.Add(oLeft);
            oBar.AddRaw(BuildUserMenu(aCtx));
            string vResult = oBar.HTML;

            if (aCrumbs.Length > 0)
            {
                var oCrumb = new TsgcHTMLComponent_Breadcrumb();
                oCrumb.BreadcrumbID = "wmsCrumb";
                int vI = 0;
                while (vI < aCrumbs.Length - 1)
                {
                    TsgcHTMLBreadcrumbItem oItem = oCrumb.Items.Add();
                    oItem.Text = aCrumbs[vI];
                    oItem.Href = aCrumbs[vI + 1];
                    oItem.Active = aCrumbs[vI + 1] == "";
                    vI += 2;
                }
                vResult = vResult + oCrumb.HTML;
            }
            return vResult;
        }

        private string BuildCommandPalette()
        {
            var oPal = new TsgcHTMLComponent_CommandPalette();
            oPal.PaletteID = "wmsPalette";
            oPal.Placeholder = "Jump to an order, or type a page name...";
            oPal.EmptyText = "Nothing matches that.";
            oPal.HotKey = "k";
            oPal.HotKeyCtrl = true;
            oPal.HotKeyMeta = true;
            oPal.MaxResults = 12;
            oPal.ShowCategories = true;
            oPal.ShowShortcuts = true;

            oPal.AddItem("Dashboard", "/", "", "Pages", "G D");
            oPal.AddItem("Products", "/products", "", "Pages", "G P");
            oPal.AddItem("Stock on hand", "/stock", "", "Pages", "G S");
            oPal.AddItem("Movements", "/stock/movements", "", "Pages", "");
            oPal.AddItem("Locations", "/locations", "", "Pages", "");
            oPal.AddItem("Cycle counts", "/counts", "", "Pages", "");
            oPal.AddItem("Reports", "/reports", "", "Pages", "");
            oPal.AddItem("Handheld terminal", "/hh", "", "Pages", "G H");

            // The open orders come straight out of SQL into the palette.
            using (TWMSDataSet oData = FDB.OpenCommands())
            {
                oPal.LoadFromDataSet(oData.DataSet, "caption", "description", "href",
                    "", "category");
            }
            return oPal.HTML;
        }

        private string BuildFlash(TWMSPageCtx aCtx)
        {
            string vResult = "";
            if (aCtx.Error != "")
            {
                var oAlert = new TsgcHTMLAlert(aCtx.Error);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oAlert.Dismissible = true;
                vResult = oAlert.HTML;
            }
            if (aCtx.Flash == "")
                return vResult;

            string vMsg;
            if (string.Equals(aCtx.Flash, "saved", StringComparison.OrdinalIgnoreCase))
                vMsg = "Saved.";
            else if (string.Equals(aCtx.Flash, "deleted", StringComparison.OrdinalIgnoreCase))
                vMsg = "Deleted.";
            else if (string.Equals(aCtx.Flash, "received", StringComparison.OrdinalIgnoreCase))
                vMsg = "Receipt recorded and put away.";
            else if (string.Equals(aCtx.Flash, "picked", StringComparison.OrdinalIgnoreCase))
                vMsg = "Pick confirmed.";
            else if (string.Equals(aCtx.Flash, "packed", StringComparison.OrdinalIgnoreCase))
                vMsg = "Order packed.";
            else if (string.Equals(aCtx.Flash, "shipped", StringComparison.OrdinalIgnoreCase))
                vMsg = "Order shipped.";
            else if (string.Equals(aCtx.Flash, "adjusted", StringComparison.OrdinalIgnoreCase))
                vMsg = "Stock adjusted.";
            else if (string.Equals(aCtx.Flash, "counted", StringComparison.OrdinalIgnoreCase))
                vMsg = "Count recorded.";
            else if (string.Equals(aCtx.Flash, "closed", StringComparison.OrdinalIgnoreCase))
                vMsg = "Count sheet closed and posted.";
            else
                vMsg = aCtx.Flash;
            return vResult + TsgcHTMLComponent_Snackbar.Build(vMsg,
                TsgcHTMLColor.hcSuccess, "Dismiss",
                TsgcHTMLSnackbarPosition.sbBottomRight);
        }

        private string BuildFooter()
        {
            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "border-top mt-4 pt-3 text-center text-muted small";
            var oP = new TsgcHTMLContainer("p");
            oP.CSSClass = "mb-1";
            oP.AddText("Built with ");
            var oLink = new TsgcHTMLLink("https://www.esegece.com/products/sgchtml/",
                "sgcHTML");
            oLink.Target = "_blank";
            oLink.Rel = "noopener";
            oLink.CSSClass = "fw-semibold text-decoration-none";
            oP.Add(oLink);
            oP.AddText(" for Delphi and C++Builder. No REST tier, no JavaScript " +
                "framework, one Delphi executable.");
            oFooter.Add(oP);

            var oCopy = new TsgcHTMLContainer("p");
            oCopy.CSSClass = "mb-0";
            oCopy.AddRaw("&copy; 2026 ");
            var oEse = new TsgcHTMLLink("https://www.esegece.com", "eSeGeCe");
            oEse.Target = "_blank";
            oEse.Rel = "noopener";
            oEse.CSSClass = "text-muted text-decoration-none";
            oCopy.Add(oEse);
            oFooter.Add(oCopy);
            return oFooter.HTML;
        }

        private string BuildShell(TWMSPageCtx aCtx, string aTitle, string aBodyHTML,
            string[] aCrumbs)
        {
            var oBody = new TsgcHTMLNodeList();

            var oShell = new TsgcHTMLContainer("div");
            oShell.CSSClass = "d-flex wms-shell";
            oShell.AddRaw(BuildSidebar(aCtx));

            var oMain = new TsgcHTMLContainer("main");
            oMain.CSSClass = "flex-grow-1 wms-main p-3 p-lg-4";
            oMain.AddRaw(BuildTopbar(aCtx, aTitle, aCrumbs));
            oMain.AddRaw(BuildFlash(aCtx));
            oMain.AddRaw(aBodyHTML);
            oMain.AddRaw(BuildFooter());
            oShell.Add(oMain);

            oBody.Add(oShell);
            oBody.AddRaw(BuildCommandPalette());
            string vBody = oBody.HTML;

            return WrapTemplate("sgcWMS - " + aTitle, vBody, aCtx.Theme);
        }

        // ------------------------------------------------------------ auth pages ---

        public string BuildLoginPage(string aTheme, string aError, string aDefaultUser,
            string aDefaultPassword)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass =
                "container py-5 d-flex justify-content-center align-items-center";
            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "w-100";
            oCol.Style = "max-width:26rem;";

            var oLogo = new TsgcHTMLContainer("div");
            oLogo.CSSClass = "text-center mb-3";
            oLogo.AddRaw(CS_LOGO_SVG);
            oCol.Add(oLogo);

            var oLogin = new TsgcHTMLComponent_Login();
            oLogin.FormAction = "/login";
            oLogin.FormMethod = "POST";
            oLogin.FormID = "wmsLogin";
            oLogin.Title = "Warehouse sign in";
            oLogin.Subtitle = "Back office and handheld, one application.";
            oLogin.UserLabel = "User name";
            oLogin.UserPlaceholder = "admin";
            oLogin.PasswordLabel = "Password";
            oLogin.ButtonText = "Sign in";
            oLogin.ButtonStyleEnum = TsgcHTMLButtonStyle.bsWarning;
            oLogin.ButtonBlock = true;
            oLogin.LoginStyle = TsgcHTMLLoginStyle.lsCard;
            oLogin.ShowRememberMe = false;
            oLogin.UserValue = aDefaultUser;
            oLogin.PasswordValue = aDefaultPassword;
            oLogin.ErrorMessage = aError;
            oCol.AddRaw(oLogin.HTML);

            var oAuthn = new TsgcHTMLComponent_WebAuthnLogin();
            oAuthn.WebAuthnID = "wmsPasskey";
            oAuthn.Mode = TsgcHTMLWebAuthnMode.wamAuthenticate;
            oAuthn.Title = "Passkey";
            oAuthn.Description = "Sign in with the passkey stored on this device.";
            oAuthn.AuthenticateURL = "/passkey/login";
            oAuthn.CallbackURL = "/";
            oAuthn.AuthenticateButtonText = "Sign in with a passkey";
            oAuthn.AuthenticateButtonStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oAuthn.UsernameSelector = "#wmsLogin input[name=\"username\"]";
            oCol.AddRaw(oAuthn.HTML);

            var oHint = new TsgcHTMLParagraph(
                "Demo accounts: admin / admin (everything), " +
                "supervisor / demo1234 (operations), operator / demo1234 (handheld).");
            oHint.CSSClass = "text-muted small text-center mt-3";
            oCol.Add(oHint);

            oWrap.Add(oCol);
            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;

            return WrapTemplate("sgcWMS - Sign in", vBody, aTheme);
        }

        public string BuildRegisterPage(string aTheme, string aError)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container py-5 d-flex justify-content-center";
            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "w-100";
            oCol.Style = "max-width:26rem;";

            var oLogo = new TsgcHTMLContainer("div");
            oLogo.CSSClass = "text-center mb-3";
            oLogo.AddRaw(CS_LOGO_SVG);
            oCol.Add(oLogo);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.Title = "Request a warehouse account";
            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oCard.Body.Add(oAlert);
            }

            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "wmsRegister";
            oForm.Action = "/register";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Create account";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsWarning;

            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "username";
            oField.Label_ = "User name";
            oField.Required = true;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "display_name";
            oField.Label_ = "Full name";

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "password";
            oField.Label_ = "Password";
            oField.HelpText = "At least 8 characters.";
            oField.Required = true;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "confirm";
            oField.Label_ = "Repeat password";
            oField.Required = true;

            oCard.Body.AddRaw(oForm.HTML);

            oCol.Add(oCard);
            var oBack = new TsgcHTMLParagraph("New accounts are created with the " +
                "operator role: handheld only until an administrator promotes them.");
            oBack.CSSClass = "text-muted small text-center mt-3";
            oCol.Add(oBack);
            oWrap.Add(oCol);
            oRoot.Add(oWrap);
            string vBody = oRoot.HTML;

            return WrapTemplate("sgcWMS - Register", vBody, aTheme);
        }

        public string BuildForbiddenPage(TWMSPageCtx aCtx)
        {
            var oEmpty = new TsgcHTMLComponent_EmptyState();
            oEmpty.Title = "Not your side of the warehouse";
            oEmpty.Description = "The " + WMSConst.WMSRoleCaption(aCtx.Role) +
                " role works on the handheld terminal. Back-office pages are open to " +
                "supervisors and administrators.";
            oEmpty.Icon = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"48\" " +
                "height=\"48\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
                "stroke-width=\"1.5\"><rect x=\"4\" y=\"10\" width=\"16\" height=\"10\" rx=\"2\"/>" +
                "<path d=\"M8 10V7a4 4 0 0 1 8 0v3\"/></svg>";
            oEmpty.ActionCaption = "Open the handheld terminal";
            oEmpty.ActionHref = "/hh";
            oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsWarning;
            oEmpty.SecondaryActionCaption = "Sign out";
            oEmpty.SecondaryActionHref = "/logout";
            oEmpty.Bordered = true;
            string vBody = oEmpty.HTML;

            return WrapTemplate("sgcWMS - Forbidden", Wrap("container py-5", vBody),
                aCtx.Theme);
        }

        public string BuildNotFoundPage(string aTheme)
        {
            var oEmpty = new TsgcHTMLComponent_EmptyState();
            oEmpty.Title = "Nothing on that aisle";
            oEmpty.Description = "The page you asked for does not exist.";
            oEmpty.ActionCaption = "Back to the dashboard";
            oEmpty.ActionHref = "/";
            oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsWarning;
            oEmpty.Bordered = true;
            string vBody = oEmpty.HTML;

            return WrapTemplate("sgcWMS - Not found", Wrap("container py-5", vBody),
                aTheme);
        }

        // ------------------------------------------------------------- dashboard ---

        public string BuildHeatmapFragment(string aZone)
        {
            var oHeat = new TsgcHTMLComponent_Heatmap();
            var oRows = new List<string>();
            var oCounted = new List<string>();

            oHeat.ElementID = "wmsHeat";
            oHeat.MinColor = "#FEF3C7";
            oHeat.MaxColor = "#B45309";
            oHeat.EmptyColor = "#E5E7EB";
            oHeat.CellSize = 34;
            oHeat.CellGap = 3;
            oHeat.ShowValues = true;
            oHeat.ShowLegend = true;
            oHeat.Rounded = true;
            oHeat.Decimals = 0;
            oHeat.AutoMin = false;
            oHeat.AutoMax = false;
            oHeat.MinValue = 0;
            oHeat.MaxValue = 100;

            using (TWMSDataSet oData = FDB.OpenBinUtilisation(aZone))
            {
                while (!oData.Eof)
                {
                    string vRowKey = oData.AsStr("rack_label");
                    string vColKey = oData.AsStr("bin_label");
                    int vRow = oRows.IndexOf(vRowKey);
                    if (vRow < 0)
                    {
                        oRows.Add(vRowKey);
                        vRow = oRows.Count - 1;
                    }
                    int vCol = oCounted.IndexOf(vColKey);
                    if (vCol < 0)
                    {
                        oCounted.Add(vColKey);
                        vCol = oCounted.Count - 1;
                    }
                    oHeat.SetCell(vRow, vCol, oData.AsFloat("pct"));
                    oData.Next();
                }
            }

            oHeat.RowLabels.Clear();
            oHeat.RowLabels.AddRange(oRows);
            oHeat.ColumnLabels.Clear();
            oHeat.ColumnLabels.AddRange(oCounted);
            return oHeat.HTML;
        }

        public string BuildDashboardPage(TWMSPageCtx aCtx)
        {
            TWMSDashboardStats oStats = FDB.GetDashboardStats();
            TWMSActivityPoint[] vSeries = FDB.GetActivitySeries(12);

            var oRoot = new TsgcHTMLNodeList();

            // ---- KPI strip: six StatCards ---- //
            oRoot.AddRaw(KPIStrip(new string[] {
                TsgcHTMLComponent_StatCard.Build("Products",
                    FmtInt(oStats.Products), TsgcHTMLStatColor.scPrimary),
                TsgcHTMLComponent_StatCard.Build("Units on hand",
                    FmtInt(oStats.StockUnits), TsgcHTMLStatColor.scInfo),
                TsgcHTMLComponent_StatCard.Build("Stock value",
                    FmtMoney(oStats.StockValue), TsgcHTMLStatColor.scSuccess),
                TsgcHTMLComponent_StatCard.Build("Below minimum",
                    FmtInt(oStats.BelowMin), TsgcHTMLStatColor.scDanger,
                    TsgcHTMLStatTrend.stDown, "reorder"),
                TsgcHTMLComponent_StatCard.Build("Open purchase orders",
                    FmtInt(oStats.OpenPOs), TsgcHTMLStatColor.scWarning),
                TsgcHTMLComponent_StatCard.Build("Open sales orders",
                    FmtInt(oStats.OpenSOs), TsgcHTMLStatColor.scSecondary) }));

            string vLeft;
            string vRight;
            var vReceived = new double[vSeries.Length];
            var vPicked = new double[vSeries.Length];

            // ---- received vs picked, 12 months ---- //
            var oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "wmsFlow";
            oChart.ChartType = TsgcHTMLChartType.ctBar;
            oChart.Title = "";
            oChart.CSSHeight = "280px";
            oChart.ShowLegend = true;
            for (int vI = 0; vI < vSeries.Length; vI++)
            {
                oChart.AddLabel(vSeries[vI].BucketLabel);
                vReceived[vI] = vSeries[vI].Received;
                vPicked[vI] = vSeries[vI].Shipped;
            }
            oChart.AddDataset("Received", vReceived, "#F59E0B");
            oChart.AddDataset("Picked", vPicked, "#0EA5E9");
            vLeft = Card("Goods in and goods out",
                "Twelve months of receipt and pick movements, aggregated in SQL.",
                oChart.HTML);

            // ---- capacity gauge + picked sparkline ---- //
            var oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.GaugeID = "wmsCapacity";
            oGauge.Title = "Bins in use";
            oGauge.Unit_ = "%";
            oGauge.MinValue = 0;
            oGauge.MaxValue = 100;
            if (oStats.BinsTotal > 0)
                oGauge.Value = Math.Round((double)oStats.BinsUsed * 100 / oStats.BinsTotal);
            else
                oGauge.Value = 0;
            oGauge.ColorLow = "#16A34A";
            oGauge.ColorMid = "#F59E0B";
            oGauge.ColorHigh = "#DC2626";
            oGauge.ThresholdMid = 60;
            oGauge.ThresholdHigh = 85;
            vRight = oGauge.HTML;

            var oSpark = new TsgcHTMLComponent_Sparkline();
            oSpark.ChartType = TsgcHTMLSparklineType.slArea;
            oSpark.Width = 220;
            oSpark.Height = 48;
            oSpark.LineColor = "#B45309";
            oSpark.FillColor = "#FDE68A";
            oSpark.ShowLastPoint = true;
            oSpark.SetData(vPicked);
            vRight = vRight + Wrap("text-center mt-2",
                Wrap("text-muted small", "Units picked per month") + oSpark.HTML);

            vRight = Card("Warehouse capacity",
                oStats.BinsUsed.ToString(CultureInfo.InvariantCulture) + " of " +
                oStats.BinsTotal.ToString(CultureInfo.InvariantCulture) +
                " bins hold stock.", vRight);

            oRoot.AddRaw(Cols(new string[] { vLeft, vRight }, TsgcHTMLColWidth.cw6));

            // ---- stock value by category (TreeMap, LoadFromDataSet) ---- //
            var oTreeMap = new TsgcHTMLComponent_TreeMap();
            oTreeMap.ElementID = "wmsTreeMap";
            oTreeMap.Width = 640;
            oTreeMap.Height = 300;
            oTreeMap.ShowLabels = true;
            oTreeMap.ShowValues = true;
            oTreeMap.ColorScheme = TsgcHTMLTreeMapScheme.tmWarm;
            oTreeMap.Decimals = 0;
            using (TWMSDataSet oData = FDB.OpenValueByCategory())
            {
                oTreeMap.LoadFromDataSet(oData.DataSet, "category", "value");
            }
            vLeft = Card("Stock value by category",
                "One GROUP BY, straight into TreeMap.LoadFromDataSet.",
                Wrap("table-responsive", oTreeMap.HTML));

            // ---- bin utilisation, htmx-loaded behind a Placeholder skeleton ---- //
            var oPanel = new TsgcHTMLContainer("div");
            oPanel.ID = "wmsHeatPanel";
            oPanel.Attributes =
                "hx-get=\"/dashboard/heatmap?zone=A\" hx-trigger=\"load\" " +
                "hx-swap=\"innerHTML\"";
            oPanel.AddRaw(TsgcHTMLComponent_Placeholder.BuildCard(4,
                TsgcHTMLPlaceholderAnimation.paWave));
            vRight = Card("Bin utilisation, zone A",
                "Loaded after the page paints. The skeleton is the Placeholder " +
                "component; the swap is one htmx attribute.",
                Wrap("table-responsive", oPanel.HTML));

            oRoot.AddRaw(Cols(new string[] { vLeft, vRight }, TsgcHTMLColWidth.cw6));

            // ---- top picked products (Grid from SQL) + recent movements ---- //
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsTop";
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.Responsive = true;
            oGrid.CSSClass = "wms-tight";
            using (TWMSDataSet oData = FDB.OpenTopProducts(8))
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product", "units",
                    "Units picked" });
                oGrid.LoadFromDataSet(oData.DataSet);
            }
            vLeft = Card("Top movers", "The eight most-picked SKUs.", oGrid.HTML);

            var oList = new TsgcHTMLComponent_ListGroup();
            oList.ListGroupID = "wmsRecent";
            oList.Flush = true;
            using (TWMSDataSet oData = FDB.OpenMovements("", "", "", "", "created",
                "desc", 1, 10))
            {
                oList.LoadFromDataSet(oData.DataSet, "reference", "", "kind");
            }
            vRight = Card("Latest movements",
                "ListGroup.LoadFromDataSet over the movements ledger.", oList.HTML);

            oRoot.AddRaw(Cols(new string[] { vLeft, vRight }, TsgcHTMLColWidth.cw6));
            string vBody = oRoot.HTML;

            return BuildShell(aCtx, "Dashboard", vBody, new string[0]);
        }

        // -------------------------------------------------------------- products ---

        // The product filters as a query string, without the page number: 'q=..&cat=..'
        // or '' when nothing is filtered.
        private static string ProductFilterQS(TWMSListFilter aFilter)
        {
            string vResult = "";
            if (aFilter.Search != "")
                vResult = vResult + "&q=" + UrlEnc(aFilter.Search);
            if (aFilter.Category != "")
                vResult = vResult + "&cat=" + UrlEnc(aFilter.Category);
            if (aFilter.Sort != "")
                vResult = vResult + "&sort=" + UrlEnc(aFilter.Sort);
            if (aFilter.Dir != "")
                vResult = vResult + "&dir=" + UrlEnc(aFilter.Dir);
            if (vResult != "")
                vResult = vResult.Substring(1);
            return vResult;
        }

        // Full query string including the page. Used by the export links.
        private static string ProductQS(TWMSListFilter aFilter, int aPage)
        {
            string vResult = "?page=" + aPage.ToString(CultureInfo.InvariantCulture);
            if (ProductFilterQS(aFilter) != "")
                vResult = vResult + "&" + ProductFilterQS(aFilter);
            return vResult;
        }

        // Pagination.BaseURL: the component appends the page number, so the page
        // parameter has to come last and appear exactly once (the HTTP server reads
        // the FIRST occurrence of a repeated parameter, so a duplicate would pin
        // every link to the same page).
        private static string ProductBaseURL(TWMSListFilter aFilter)
        {
            string vResult = "/products?";
            if (ProductFilterQS(aFilter) != "")
                vResult = vResult + ProductFilterQS(aFilter) + "&";
            return vResult + "page=";
        }

        public string BuildProductTableFragment(TWMSListFilter aFilter)
        {
            int vTotal = FDB.CountProducts(aFilter.Search, aFilter.Category);
            int vPages = (int)Math.Ceiling((double)vTotal / WMSConst.CS_WMS_PAGE_SIZE);
            if (vPages < 1)
                vPages = 1;

            var oWrap = new TsgcHTMLContainer("div");
            if (vTotal == 0)
            {
                var oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No products match";
                oEmpty.Description = "Clear the search box or pick another category.";
                oEmpty.ActionCaption = "Show every product";
                oEmpty.ActionHref = "/products";
                oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
                oEmpty.Compact = true;
                oWrap.AddRaw(oEmpty.HTML);
                return oWrap.HTML;
            }

            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsProducts";
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.Responsive = true;
            oGrid.ShowSort = true;
            oGrid.CSSClass = "wms-tight";
            oGrid.EmptyText = "No products.";
            oGrid.ExportXLSX = true;
            oGrid.ExportURL = "/products/export.xlsx" + ProductQS(aFilter, aFilter.Page);
            oGrid.ExportXLSXText = "Export XLSX";
            oGrid.ExportPDF = true;
            oGrid.ExportPDFURL = "/products/export.pdf" + ProductQS(aFilter, aFilter.Page);
            oGrid.OnGetCellHTML += GridActionCell;

            using (TWMSDataSet oData = FDB.OpenProducts(aFilter.Search, aFilter.Category,
                aFilter.Sort, aFilter.Dir, aFilter.Page, WMSConst.CS_WMS_PAGE_SIZE))
            {
                SetLabels(oData, new string[] { "sku", "SKU", "barcode", "Barcode",
                    "name", "Product", "category", "Category", "uom", "UoM",
                    "unit_cost", "Unit cost", "min_stock", "Min", "onhand", "On hand" });
                // The row-id column is loaded so the actions cell can address the
                // right product; it is hidden by the columns menu, never rendered as
                // a value (OnGetCellHTML turns it into the Edit / Delete buttons).
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                    "category", "uom", "unit_cost", "min_stock", "onhand", "id" });
                oGrid.Columns[7].Title = "Actions";
                oGrid.Columns[7].Align = TsgcHTMLGridAlign.gaRight;
                FActionCol = 7;
            }
            oWrap.AddRaw(oGrid.HTML);

            if (vPages > 1)
            {
                var oPag = new TsgcHTMLComponent_Pagination();
                oPag.PaginationID = "wmsProdPag";
                oPag.CurrentPage = aFilter.Page;
                oPag.TotalPages = vPages;
                oPag.TotalItems = vTotal;
                oPag.PageSize = WMSConst.CS_WMS_PAGE_SIZE;
                oPag.BaseURL = ProductBaseURL(aFilter);
                oPag.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                oPag.MaxVisible = 7;
                oWrap.AddRaw(oPag.HTML);
            }
            return oWrap.HTML;
        }

        public string BuildProductFormFragment(long aId)
        {
            TWMSProduct oProduct = null;
            bool vHas = (aId > 0) && FDB.GetProduct(aId, out oProduct);
            if (!vHas)
            {
                oProduct = new TWMSProduct();
                oProduct.Id = 0;
                oProduct.SKU = "";
                oProduct.Barcode = "";
                oProduct.Name = "";
                oProduct.Description = "";
                oProduct.UOM = "EA";
                oProduct.UnitCost = 0;
                oProduct.MinStock = 10;
                oProduct.Category = "";
            }

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4 border-warning";
            if (vHas)
                oCard.Title = "Edit " + oProduct.SKU;
            else
                oCard.Title = "New product";

            // The node-layer form carries the htmx attributes; every field inside it
            // is an sgcHTML input component.
            var oForm = new TsgcHTMLForm();
            oForm.FormID = "wmsProductFormEl";
            oForm.Method = "POST";
            oForm.Action = "/products/save";
            oForm.Attributes = "hx-post=\"/products/save\" " +
                "hx-target=\"#wmsProductsTable\" hx-swap=\"innerHTML\"";
            oForm.AddHidden("id", oProduct.Id.ToString(CultureInfo.InvariantCulture));

            var oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";

            var oEdit = new TsgcHTMLComponent_Edit();
            oEdit.EditID = "wmsPfSku";
            oEdit.ElementName = "sku";
            oEdit.Label_ = "SKU";
            oEdit.Value = oProduct.SKU;
            oEdit.Required = true;
            oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(oEdit.HTML);

            oEdit = new TsgcHTMLComponent_Edit();
            oEdit.EditID = "wmsPfBarcode";
            oEdit.ElementName = "barcode";
            oEdit.Label_ = "Barcode (EAN-13)";
            oEdit.Value = oProduct.Barcode;
            oEdit.HelpText = "Scanned by the handheld lookup screen.";
            oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(oEdit.HTML);

            oEdit = new TsgcHTMLComponent_Edit();
            oEdit.EditID = "wmsPfName";
            oEdit.ElementName = "name";
            oEdit.Label_ = "Description";
            oEdit.Value = oProduct.Name;
            oEdit.Required = true;
            oRow.Col(TsgcHTMLColWidth.cw12).AddRaw(oEdit.HTML);

            var oMemo = new TsgcHTMLComponent_Memo();
            oMemo.MemoID = "wmsPfDesc";
            oMemo.ElementName = "description";
            oMemo.Label_ = "Long description";
            oMemo.Value = oProduct.Description;
            oMemo.Rows = 3;
            oRow.Col(TsgcHTMLColWidth.cw12).AddRaw(oMemo.HTML);

            var oSel = new TsgcHTMLComponent_Select();
            oSel.SelectID = "wmsPfCat";
            oSel.ElementName = "category";
            oSel.Label_ = "Category";
            using (TWMSDataSet oData = FDB.OpenCategories())
            {
                oSel.LoadFromDataSet(oData.DataSet, "category", "category");
            }
            if (oProduct.Category != "")
                oSel.AddOption(oProduct.Category, oProduct.Category, true);
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(oSel.HTML);

            oSel = new TsgcHTMLComponent_Select();
            oSel.SelectID = "wmsPfUom";
            oSel.ElementName = "uom";
            oSel.Label_ = "Unit of measure";
            oSel.AddOption("EA", "EA", oProduct.UOM == "EA");
            oSel.AddOption("BOX", "BOX", oProduct.UOM == "BOX");
            oSel.AddOption("PK", "PK", oProduct.UOM == "PK");
            oSel.AddOption("KG", "KG", oProduct.UOM == "KG");
            oSel.AddOption("M", "M", oProduct.UOM == "M");
            oSel.AddOption("L", "L", oProduct.UOM == "L");
            oRow.Col(TsgcHTMLColWidth.cw2).AddRaw(oSel.HTML);

            oEdit = new TsgcHTMLComponent_Edit();
            oEdit.EditID = "wmsPfCost";
            oEdit.ElementName = "unit_cost";
            oEdit.Label_ = "Unit cost";
            oEdit.InputType = TsgcHTMLInputType.itNumber;
            oEdit.Value = oProduct.UnitCost.ToString("0.00", CultureInfo.InvariantCulture);
            oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(oEdit.HTML);

            oEdit = new TsgcHTMLComponent_Edit();
            oEdit.EditID = "wmsPfMin";
            oEdit.ElementName = "min_stock";
            oEdit.Label_ = "Minimum stock";
            oEdit.InputType = TsgcHTMLInputType.itNumber;
            oEdit.Value = oProduct.MinStock.ToString(CultureInfo.InvariantCulture);
            oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(oEdit.HTML);

            oForm.Add(oRow);

            var oBtn = new TsgcHTMLButton("Save product", TsgcHTMLButtonStyle.bsWarning);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "mt-3 me-2";
            oForm.Add(oBtn);

            oBtn = new TsgcHTMLButton("Cancel", TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBtn.ButtonType = "button";
            oBtn.CSSClass = "mt-3";
            oBtn.Attributes = "hx-get=\"/products/form?cancel=1\" " +
                "hx-target=\"#wmsProductForm\" hx-swap=\"innerHTML\"";
            oForm.Add(oBtn);

            oCard.Body.Add(oForm);
            return oCard.HTML;
        }

        public string BuildProductListPage(TWMSPageCtx aCtx, TWMSListFilter aFilter)
        {
            var oRoot = new TsgcHTMLNodeList();

            // ---- toolbar ---- //
            var oBar = new TsgcHTMLComponent_Toolbar();
            oBar.ToolbarID = "wmsProdBar";
            oBar.AddButton("Export XLSX", TsgcHTMLButtonStyle.bsOutlineSecondary,
                "/products/export.xlsx" + ProductQS(aFilter, aFilter.Page));
            oBar.AddButton("Export PDF", TsgcHTMLButtonStyle.bsOutlineSecondary,
                "/products/export.pdf" + ProductQS(aFilter, aFilter.Page));
            oBar.AddSeparator();
            oBar.AddButton("Stock on hand", TsgcHTMLButtonStyle.bsOutlinePrimary, "/stock");
            oRoot.AddRaw(oBar.HTML);

            // The New button has to be an htmx trigger, so it is emitted as its own
            // node right after the toolbar rather than as a toolbar link.
            var oBtn = new TsgcHTMLButton("New product", TsgcHTMLButtonStyle.bsWarning);
            oBtn.CSSClass = "mb-3";
            oBtn.Attributes = "hx-get=\"/products/form\" " +
                "hx-target=\"#wmsProductForm\" hx-swap=\"innerHTML\"";
            oRoot.AddRaw(oBtn.HTML);

            // ---- filter form: InputGroup + Select ---- //
            string vFilterHTML;
            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/products";
            oForm.CSSClass = "mb-3";
            var oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-2 align-items-end";

            var oInput = new TsgcHTMLComponent_InputGroup();
            oInput.GroupID = "wmsProdSearch";
            oInput.PrependText = "Search";
            oInput.InputName = "q";
            oInput.InputValue = SafeInputValue(aFilter.Search);
            oInput.Placeholder = "SKU, description or barcode";
            oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(oInput.HTML);

            var oSel = new TsgcHTMLComponent_Select();
            oSel.SelectID = "wmsProdCat";
            oSel.ElementName = "cat";
            oSel.Placeholder = "Every category";
            using (TWMSDataSet oData = FDB.OpenCategories())
            {
                oSel.LoadFromDataSet(oData.DataSet, "category", "category");
            }
            if (aFilter.Category != "")
                oSel.AddOption(aFilter.Category, aFilter.Category, true);
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(oSel.HTML);

            oBtn = new TsgcHTMLButton("Filter", TsgcHTMLButtonStyle.bsOutlinePrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100";
            oRow.Col(TsgcHTMLColWidth.cw2).Add(oBtn);

            oForm.Add(oRow);
            vFilterHTML = oForm.HTML;

            // ---- form panel (htmx target) ---- //
            var oPanel = new TsgcHTMLContainer("div");
            oPanel.ID = "wmsProductForm";
            oRoot.AddRaw(oPanel.HTML);

            // ---- table panel (htmx target) ---- //
            var oTable = new TsgcHTMLContainer("div");
            oTable.ID = "wmsProductsTable";
            oTable.AddRaw(BuildProductTableFragment(aFilter));
            oRoot.AddRaw(Card("Product catalogue",
                "Search, sort and paging all happen in SQL. The table is a Grid; " +
                "the pager is the Pagination component.", vFilterHTML + oTable.HTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Products", vBody, new string[] { "Dashboard", "/",
                "Products", "" });
        }

        public string BuildProductDetailPage(TWMSPageCtx aCtx, long aId)
        {
            TWMSProduct oProduct;
            if (!FDB.GetProduct(aId, out oProduct))
                return BuildNotFoundPage(aCtx.Theme);

            var oRoot = new TsgcHTMLNodeList();

            // ---- facts ---- //
            string vFacts;
            var oDL = new TsgcHTMLDescriptionList();
            oDL.CSSClass = "row mb-0";
            oDL.TermClass = "col-5 text-muted";
            oDL.DescClass = "col-7";
            oDL.AddItem("SKU", oProduct.SKU);
            oDL.AddItem("Category", oProduct.Category);
            oDL.AddItem("Unit of measure", oProduct.UOM);
            oDL.AddItem("Unit cost", FmtMoney(oProduct.UnitCost));
            oDL.AddItem("Minimum stock",
                oProduct.MinStock.ToString(CultureInfo.InvariantCulture));
            oDL.AddItem("Barcode", oProduct.Barcode);
            vFacts = oDL.HTML;

            vFacts = vFacts + Wrap("mt-3",
                TsgcHTMLComponent_Popover.BuildButton("What is unit cost?",
                "Valuation basis", "Stock value on every report is on-hand quantity " +
                "times this unit cost. It is the moving average your ERP would feed " +
                "in.", TsgcHTMLButtonStyle.bsOutlineSecondary, TsgcHTMLPlacement.plTop));
            vFacts = Card("Product", oProduct.Name, vFacts);

            // ---- codes ---- //
            string vCodes;
            var oBarcode = new TsgcHTMLComponent_Barcode();
            oBarcode.Data = oProduct.SKU;
            oBarcode.Height = 60;
            oBarcode.ModuleWidth = 2;
            oBarcode.ShowText = true;
            vCodes = Wrap("text-center mb-3", oBarcode.HTML);

            var oQR = new TsgcHTMLComponent_QRCode();
            oQR.Data = oProduct.Barcode;
            oQR.ModuleSize = 4;
            oQR.QuietZone = 2;
            oQR.DarkColor = "#7C4A03";
            oQR.Caption = oProduct.Barcode;
            oQR.CaptionVisible = true;
            vCodes = vCodes + Wrap("text-center", oQR.HTML);

            vCodes = Card("Scan codes",
                "Code 128 of the SKU and a QR of the EAN-13, both rendered server side.",
                vCodes);

            oRoot.AddRaw(Cols(new string[] { vFacts, vCodes }, TsgcHTMLColWidth.cw6));

            // ---- stock by location ---- //
            string vStockHTML;
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsProdStock";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            oGrid.EmptyText = "This product is not stocked in any bin yet.";
            using (TWMSDataSet oData = FDB.OpenProductStock(aId))
            {
                SetLabels(oData, new string[] { "location", "Bin", "zone", "Zone",
                    "qty", "Quantity", "capacity", "Bin capacity", "value", "Value" });
                oGrid.LoadFromDataSet(oData.DataSet);
            }
            vStockHTML = oGrid.HTML;

            // ---- movement history ---- //
            string vMoveHTML;
            var oList = new TsgcHTMLComponent_ListGroup();
            oList.ListGroupID = "wmsProdMoves";
            oList.Flush = true;
            using (TWMSDataSet oData = FDB.OpenProductMovements(aId, 25))
            {
                oList.LoadFromDataSet(oData.DataSet, "reference", "", "kind");
            }
            vMoveHTML = oList.HTML;

            // ---- 12-month sparkline ---- //
            string vChartHTML;
            var oSpark = new TsgcHTMLComponent_Sparkline();
            oSpark.ChartType = TsgcHTMLSparklineType.slBar;
            oSpark.Width = 460;
            oSpark.Height = 90;
            oSpark.LineColor = "#B45309";
            oSpark.FillColor = "#F59E0B";
            oSpark.ShowMinMax = true;
            var vValues = new List<double>();
            using (TWMSDataSet oData = FDB.OpenProductMonthly(aId))
            {
                while (!oData.Eof)
                {
                    vValues.Add(oData.AsFloat("units"));
                    oData.Next();
                }
            }
            if (vValues.Count == 0)
                vValues.Add(0);
            oSpark.SetData(vValues.ToArray());
            vChartHTML = Wrap("text-center", oSpark.HTML);

            var oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.TabsID = "wmsProdTabs";
            oTabs.Style = TsgcHTMLTabStyle.tsTab;
            TsgcHTMLTabItem oTab = oTabs.Items.Add();
            oTab.Title = "Stock by location";
            oTab.Content = vStockHTML;
            oTab.Active = true;
            oTab = oTabs.Items.Add();
            oTab.Title = "Movement history";
            oTab.Content = vMoveHTML;
            oTab = oTabs.Items.Add();
            oTab.Title = "Monthly volume";
            oTab.Content = vChartHTML;
            oRoot.AddRaw(Card("Where it is and what it did", "", oTabs.HTML));

            // ---- adjust-stock modal ---- //
            var oModal = new TsgcHTMLComponent_Modal();
            oModal.ModalID = "wmsAdjust";
            oModal.Title = "Adjust stock";
            oModal.Centered = true;

            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "wmsAdjustForm";
            oForm.Action = "/stock/adjust";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Post adjustment";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsWarning;

            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftHidden;
            oField.Name = "product_id";
            oField.Value = aId.ToString(CultureInfo.InvariantCulture);

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "location_id";
            oField.Label_ = "Bin";
            using (TWMSDataSet oData = FDB.OpenProductStock(aId))
            {
                while (!oData.Eof)
                {
                    oField.Options.Add(oData.AsStr("location"));
                    oData.Next();
                }
            }

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "delta";
            oField.Label_ = "Adjustment (+/-)";
            oField.Value = "0";
            oField.HelpText = "Positive adds units, negative removes them.";

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "reference";
            oField.Label_ = "Reason";
            oField.Placeholder = "Damaged in transit";

            oModal.Body = oForm.HTML;

            oRoot.AddRaw(Wrap("mb-4",
                TsgcHTMLComponent_Modal.BuildTriggerButton("wmsAdjust",
                "Adjust stock", TsgcHTMLButtonStyle.bsOutlineWarning)) + oModal.HTML);

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, oProduct.SKU, vBody, new string[] { "Dashboard", "/",
                "Products", "/products", oProduct.SKU, "" });
        }

        // ------------------------------------------------------------- locations ---

        public string BuildLocationsPage(TWMSPageCtx aCtx, string aZone)
        {
            string vZone = aZone;
            if (vZone == "")
                vZone = "A";

            var oRoot = new TsgcHTMLNodeList();

            // ---- TreeView straight from the self-referencing table ---- //
            string vTreeHTML;
            var oTree = new TsgcHTMLComponent_TreeView();
            oTree.TreeID = "wmsLocTree";
            oTree.ShowLines = true;
            oTree.Selectable = true;
            oTree.IndentSize = 16;
            using (TWMSDataSet oData = FDB.OpenLocationTree())
            {
                oTree.LoadFromDataSet(oData.DataSet, "id", "parent_id", "code");
            }
            vTreeHTML = Wrap("", oTree.HTML, "max-height:520px;overflow:auto;");

            // ---- the same tree as a Grid in TreeMode, with occupancy ---- //
            string vGridHTML;
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsLocGrid";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            oGrid.TreeExpandAll = false;
            using (TWMSDataSet oData = FDB.OpenLocationTree())
            {
                SetLabels(oData, new string[] { "code", "Location", "kind", "Kind",
                    "zone", "Zone", "capacity", "Capacity", "units", "Units" });
                oGrid.LoadFromDataSetTree(oData.DataSet, "id", "parent_id");
            }
            vGridHTML = oGrid.HTML;

            var oSplit = new TsgcHTMLComponent_Splitter();
            oSplit.SplitterID = "wmsLocSplit";
            oSplit.Orientation = TsgcHTMLSplitterOrientation.soHorizontal;
            oSplit.InitialSplit = 32;
            oSplit.CSSHeight = "560px";
            oSplit.PersistKey = "wms.locations";
            oSplit.AddPaneA(vTreeHTML);
            oSplit.AddPaneB(vGridHTML);
            oRoot.AddRaw(Card("Location hierarchy",
                "Zone, aisle, rack and bin. Left: the TreeView. Right: the same " +
                "rows in a Grid with TreeMode on. Both from one self-join-free SELECT.",
                oSplit.HTML));

            // ---- per-zone occupancy gauges ---- //
            string vGaugeHTML = "";
            using (TWMSDataSet oZones = FDB.OpenZones())
            {
                while (!oZones.Eof)
                {
                    var oGauge = new TsgcHTMLComponent_Gauge();
                    oGauge.GaugeID = "wmsZone" + oZones.AsStr("zone");
                    oGauge.Title = "Zone " + oZones.AsStr("zone");
                    oGauge.Unit_ = "%";
                    oGauge.MinValue = 0;
                    oGauge.MaxValue = 100;
                    oGauge.Width = 150;
                    oGauge.Value = FDB.ScalarFloat(
                        "SELECT CASE WHEN SUM(l.capacity) > 0 THEN " +
                        "ROUND(COALESCE(SUM(s.qty), 0) * 100.0 / SUM(l.capacity), 0) " +
                        "ELSE 0 END FROM locations l LEFT JOIN stock s " +
                        "ON s.location_id = l.id WHERE l.kind = 'bin' AND l.zone = " +
                        QuotedStr(oZones.AsStr("zone")));
                    oGauge.ColorLow = "#16A34A";
                    oGauge.ColorMid = "#F59E0B";
                    oGauge.ColorHigh = "#DC2626";
                    oGauge.ThresholdMid = 60;
                    oGauge.ThresholdHigh = 85;
                    vGaugeHTML = vGaugeHTML + Wrap("col-6 col-lg-3 text-center",
                        oGauge.HTML);
                    oZones.Next();
                }
            }

            var oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.TabsID = "wmsLocTabs";
            oTabs.Style = TsgcHTMLTabStyle.tsPill;
            TsgcHTMLTabItem oTab = oTabs.Items.Add();
            oTab.Title = "Zone capacity";
            oTab.Content = Wrap("row g-3", vGaugeHTML);
            oTab.Active = true;
            oTab = oTabs.Items.Add();
            oTab.Title = "Bin heatmap, zone " + vZone;
            oTab.Content = Wrap("table-responsive", BuildHeatmapFragment(vZone));
            oRoot.AddRaw(Card("Occupancy", "", oTabs.HTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Locations", vBody, new string[] { "Dashboard", "/",
                "Locations", "" });
        }

        public string BuildLocationDetailPage(TWMSPageCtx aCtx, long aId)
        {
            TWMSLocation oLoc;
            if (!FDB.GetLocation(aId, out oLoc))
                return BuildNotFoundPage(aCtx.Theme);

            long vUnits = FDB.ScalarInt("SELECT COALESCE(SUM(qty), 0) FROM stock " +
                "WHERE location_id = " + aId.ToString(CultureInfo.InvariantCulture));

            var oRoot = new TsgcHTMLNodeList();

            string vCodes;
            var oQR = new TsgcHTMLComponent_QRCode();
            oQR.Data = oLoc.Code;
            oQR.ModuleSize = 5;
            oQR.DarkColor = "#7C4A03";
            oQR.Caption = oLoc.Code;
            oQR.CaptionVisible = true;
            vCodes = Wrap("text-center mb-3", oQR.HTML);

            var oBarcode = new TsgcHTMLComponent_Barcode();
            oBarcode.Data = oLoc.Code;
            oBarcode.Height = 50;
            vCodes = vCodes + Wrap("text-center", oBarcode.HTML);

            var oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.GaugeID = "wmsBinGauge";
            oGauge.Title = "Occupancy";
            oGauge.Unit_ = "%";
            oGauge.MinValue = 0;
            oGauge.MaxValue = 100;
            if (oLoc.Capacity > 0)
                oGauge.Value = Math.Round((double)vUnits * 100 / oLoc.Capacity);
            else
                oGauge.Value = 0;
            oGauge.ColorLow = "#16A34A";
            oGauge.ColorMid = "#F59E0B";
            oGauge.ColorHigh = "#DC2626";
            oGauge.ThresholdMid = 60;
            oGauge.ThresholdHigh = 85;
            oRoot.AddRaw(Cols(new string[] {
                Card(oLoc.Code, "Zone " + oLoc.Zone + ", kind " + oLoc.Kind +
                    ", capacity " + oLoc.Capacity.ToString(CultureInfo.InvariantCulture) +
                    ", holding " + FmtInt(vUnits) + " units.",
                    Wrap("text-center", oGauge.HTML)),
                Card("Labels", "Print these on the bin front.", vCodes) },
                TsgcHTMLColWidth.cw6));

            string vStock;
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsBinStock";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.ShowSort = true;
            oGrid.CSSClass = "wms-tight";
            oGrid.EmptyText = "Nothing stored here.";
            using (TWMSDataSet oData = FDB.OpenLocationStock(aId))
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "barcode", "Barcode", "qty", "Quantity", "value", "Value" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                    "barcode", "qty", "value" });
            }
            vStock = oGrid.HTML;
            oRoot.AddRaw(Card("Contents", "", vStock));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, oLoc.Code, vBody, new string[] { "Dashboard", "/",
                "Locations", "/locations", oLoc.Code, "" });
        }

        // --------------------------------------------------------------- inbound ---

        public string BuildInboundListPage(TWMSPageCtx aCtx, TWMSListFilter aFilter)
        {
            var oRoot = new TsgcHTMLNodeList();

            using (TWMSDataSet oData = FDB.OpenPurchaseOrders(aFilter.Status,
                aFilter.Search))
            {
                if (oData.IsEmpty())
                {
                    var oEmpty = new TsgcHTMLComponent_EmptyState();
                    oEmpty.Title = "No purchase orders";
                    oEmpty.Description = "Nothing matches that status filter.";
                    oEmpty.ActionCaption = "Show every order";
                    oEmpty.ActionHref = "/inbound?status=all";
                    oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
                    oEmpty.Bordered = true;
                    oRoot.AddRaw(oEmpty.HTML);
                }
                else
                {
                    var oTable = new TsgcHTMLComponent_DataTable();
                    oTable.TableID = "wmsInbound";
                    oTable.Title = "Purchase orders";
                    oTable.ShowSearch = true;
                    oTable.SearchPlaceholder = "Reference or supplier...";
                    oTable.ShowRowCount = true;
                    oTable.ShowPageSize = true;
                    oTable.PageSizes = "10,25,50";
                    oTable.Grid.Responsive = true;
                    oTable.Grid.CSSClass = "wms-tight";
                    oTable.Grid.ShowSort = true;
                    SetLabels(oData, new string[] { "reference", "Reference",
                        "supplier", "Supplier", "status", "Status", "lines", "Lines",
                        "qty_ordered", "Ordered", "qty_received", "Received",
                        "expected_at", "Expected", "created_at", "Raised" });
                    oTable.LoadFromDataSet(oData.DataSet, 25);
                    oRoot.AddRaw(Card("Inbound",
                        "DataTable.LoadFromDataSet with client-side search and paging " +
                        "on top of the SQL result.", oTable.HTML));
                }
            }

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Inbound", vBody, new string[] { "Dashboard", "/",
                "Inbound", "" });
        }

        public string BuildInboundDetailPage(TWMSPageCtx aCtx, long aId)
        {
            TWMSPurchaseOrder oPO;
            if (!FDB.GetPurchaseOrder(aId, out oPO))
                return BuildNotFoundPage(aCtx.Theme);

            var oRoot = new TsgcHTMLNodeList();

            var oBar = new TsgcHTMLComponent_Toolbar();
            oBar.ToolbarID = "wmsPOBar";
            oBar.AddButton("Print bin and product labels",
                TsgcHTMLButtonStyle.bsOutlineSecondary,
                "/inbound/" + aId.ToString(CultureInfo.InvariantCulture) + "/labels.pdf");
            oBar.AddButton("Back to inbound", TsgcHTMLButtonStyle.bsOutlineSecondary,
                "/inbound");
            oRoot.AddRaw(oBar.HTML);

            // ---- Stepper: receive -> put-away -> confirm ---- //
            var oStep = new TsgcHTMLComponent_Stepper();
            oStep.StepperID = "wmsPOStep";
            oStep.Layout = TsgcHTMLStepperLayout.slHorizontal;
            oStep.CurrentColor = WMSConst.CS_WMS_ACCENT;
            oStep.CompletedColor = "#16A34A";
            TsgcHTMLStepItem oItem = oStep.Items.Add();
            oItem.Title = "Receive";
            oItem.Description = "Count what arrived";
            oItem = oStep.Items.Add();
            oItem.Title = "Put away";
            oItem.Description = "Suggested bin";
            oItem = oStep.Items.Add();
            oItem.Title = "Confirm";
            oItem.Description = "Close the order";
            if (string.Equals(oPO.Status, "closed", StringComparison.OrdinalIgnoreCase))
            {
                oStep.Items[0].State = TsgcHTMLStepState.ssCompleted;
                oStep.Items[1].State = TsgcHTMLStepState.ssCompleted;
                oStep.Items[2].State = TsgcHTMLStepState.ssCompleted;
            }
            else if (string.Equals(oPO.Status, "receiving",
                StringComparison.OrdinalIgnoreCase))
            {
                oStep.Items[0].State = TsgcHTMLStepState.ssCompleted;
                oStep.Items[1].State = TsgcHTMLStepState.ssCurrent;
            }
            else
                oStep.Items[0].State = TsgcHTMLStepState.ssCurrent;
            oRoot.AddRaw(Card(oPO.Reference, oPO.SupplierName + " - expected " +
                FmtDay(oPO.ExpectedAt) + " - status " + oPO.Status,
                oStep.HTML + Wrap("mt-3", StatusBadge(oPO.Status))));

            // ---- lines ---- //
            string vLines;
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsPOLines";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            using (TWMSDataSet oData = FDB.OpenPurchaseOrderLines(aId))
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "uom", "UoM", "qty_ordered", "Ordered", "qty_received", "Received",
                    "outstanding", "Outstanding" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                    "uom", "qty_ordered", "qty_received", "outstanding" });
            }
            vLines = oGrid.HTML;

            // ---- receive form with the put-away suggestion ---- //
            string vReceiveHTML;
            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "wmsReceive";
            oForm.Action = "/inbound/" + aId.ToString(CultureInfo.InvariantCulture) +
                "/receive";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Record receipt";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsWarning;

            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "line_id";
            oField.Label_ = "Line";
            oField.ColSpan = 5;
            long vSuggested = 0;
            using (TWMSDataSet oData = FDB.OpenPurchaseOrderLines(aId))
            {
                while (!oData.Eof)
                {
                    if (oData.AsInt("outstanding") > 0)
                    {
                        oField.Options.Add(oData.AsStr("id") + "=" + oData.AsStr("sku") +
                            " (" + oData.AsStr("outstanding") + " outstanding)");
                        if (vSuggested == 0)
                            vSuggested = FDB.SuggestPutawayBin(oData.AsInt("product_id"),
                                (int)oData.AsInt("outstanding"));
                    }
                    oData.Next();
                }
            }

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftNumber;
            oField.Name = "qty";
            oField.Label_ = "Quantity received";
            oField.Value = "";
            oField.ColSpan = 3;
            oField.Required = true;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "location";
            oField.Label_ = "Put away to bin";
            oField.ColSpan = 4;
            TWMSLocation oLoc = null;
            if ((vSuggested > 0) && FDB.GetLocation(vSuggested, out oLoc))
            {
                oField.Value = oLoc.Code;
                oField.HelpText = "Suggested: lowest-coded bin in zone " + oLoc.Zone +
                    " with free capacity.";
            }
            else
                oField.HelpText = "Type a bin code, for example A-01-1-01.";

            vReceiveHTML = oForm.HTML;

            var oUpload = new TsgcHTMLComponent_FileUpload();
            oUpload.UploadID = "wmsDamage";
            oUpload.Action = "/inbound/" + aId.ToString(CultureInfo.InvariantCulture) +
                "/photo";
            oUpload.InputName = "photo";
            oUpload.Accept = ".jpg,.jpeg,.png";
            oUpload.Multiple = true;
            oUpload.MaxSize = "4 MB";
            oUpload.Title = "Damage photos";
            oUpload.Subtitle = "Drop a photo of any damaged pallet here.";
            oUpload.ButtonText = "Choose photos";
            oUpload.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
            oUpload.DragDropEnabled = true;
            vReceiveHTML = vReceiveHTML + Wrap("border-top my-4 pt-4", oUpload.HTML);

            oRoot.AddRaw(Cols(new string[] { Card("Order lines", "", vLines),
                Card("Receive", "Posting a receipt writes the stock row and the " +
                "movement in one transaction.", vReceiveHTML) },
                TsgcHTMLColWidth.cw6));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, oPO.Reference, vBody, new string[] { "Dashboard", "/",
                "Inbound", "/inbound", oPO.Reference, "" });
        }

        // -------------------------------------------------------------- outbound ---

        public string BuildOutboundListPage(TWMSPageCtx aCtx, TWMSListFilter aFilter)
        {
            var oRoot = new TsgcHTMLNodeList();

            using (TWMSDataSet oData = FDB.OpenSalesOrders(aFilter.Status,
                aFilter.Search))
            {
                if (oData.IsEmpty())
                {
                    var oEmpty = new TsgcHTMLComponent_EmptyState();
                    oEmpty.Title = "No sales orders";
                    oEmpty.Description = "Nothing matches that status filter.";
                    oEmpty.ActionCaption = "Show every order";
                    oEmpty.ActionHref = "/outbound?status=all";
                    oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
                    oEmpty.Bordered = true;
                    oRoot.AddRaw(oEmpty.HTML);
                }
                else
                {
                    var oTable = new TsgcHTMLComponent_DataTable();
                    oTable.TableID = "wmsOutbound";
                    oTable.Title = "Sales orders";
                    oTable.ShowSearch = true;
                    oTable.SearchPlaceholder = "Reference or customer...";
                    oTable.ShowRowCount = true;
                    oTable.ShowPageSize = true;
                    oTable.PageSizes = "10,25,50";
                    oTable.Grid.Responsive = true;
                    oTable.Grid.CSSClass = "wms-tight";
                    oTable.Grid.ShowSort = true;
                    SetLabels(oData, new string[] { "reference", "Reference",
                        "customer", "Customer", "city", "City", "status", "Status",
                        "priority", "Priority", "lines", "Lines", "qty_ordered",
                        "Ordered", "qty_picked", "Picked", "created_at", "Raised" });
                    oTable.LoadFromDataSet(oData.DataSet, 25);
                    oRoot.AddRaw(Card("Outbound",
                        "Urgent orders float to the top; the ORDER BY does that in SQL.",
                        oTable.HTML));
                }
            }

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Outbound", vBody, new string[] { "Dashboard", "/",
                "Outbound", "" });
        }

        public string BuildOutboundDetailPage(TWMSPageCtx aCtx, long aId)
        {
            TWMSSalesOrder oSO;
            if (!FDB.GetSalesOrder(aId, out oSO))
                return BuildNotFoundPage(aCtx.Theme);

            var oRoot = new TsgcHTMLNodeList();

            var oBar = new TsgcHTMLComponent_Toolbar();
            oBar.ToolbarID = "wmsSOBar";
            oBar.AddButton("Packing slip PDF", TsgcHTMLButtonStyle.bsOutlineSecondary,
                "/outbound/" + aId.ToString(CultureInfo.InvariantCulture) +
                "/packingslip.pdf");
            oBar.AddButton("Back to outbound", TsgcHTMLButtonStyle.bsOutlineSecondary,
                "/outbound");
            oRoot.AddRaw(oBar.HTML);

            var oStep = new TsgcHTMLComponent_Stepper();
            oStep.StepperID = "wmsSOStep";
            oStep.Layout = TsgcHTMLStepperLayout.slHorizontal;
            oStep.CurrentColor = WMSConst.CS_WMS_ACCENT;
            oStep.CompletedColor = "#16A34A";
            TsgcHTMLStepItem oItem = oStep.Items.Add();
            oItem.Title = "New";
            oItem = oStep.Items.Add();
            oItem.Title = "Picking";
            oItem = oStep.Items.Add();
            oItem.Title = "Packed";
            oItem = oStep.Items.Add();
            oItem.Title = "Shipped";
            // Everything before the current status is done, the status itself is
            // current, and 'shipped' completes the whole strip.
            int vStage = 0;
            if (string.Equals(oSO.Status, "picking", StringComparison.OrdinalIgnoreCase))
                vStage = 1;
            else if (string.Equals(oSO.Status, "packed",
                StringComparison.OrdinalIgnoreCase))
                vStage = 2;
            else if (string.Equals(oSO.Status, "shipped",
                StringComparison.OrdinalIgnoreCase))
                vStage = 3;
            for (int vI = 0; vI < oStep.Items.Count; vI++)
            {
                if (vI < vStage)
                    oStep.Items[vI].State = TsgcHTMLStepState.ssCompleted;
                else if (vI == vStage)
                {
                    if (string.Equals(oSO.Status, "shipped",
                        StringComparison.OrdinalIgnoreCase))
                        oStep.Items[vI].State = TsgcHTMLStepState.ssCompleted;
                    else
                        oStep.Items[vI].State = TsgcHTMLStepState.ssCurrent;
                }
                else
                    oStep.Items[vI].State = TsgcHTMLStepState.ssUpcoming;
            }
            oRoot.AddRaw(Card(oSO.Reference, oSO.CustomerName + " - " +
                oSO.CustomerCity + " - raised " + FmtDay(oSO.CreatedAt), oStep.HTML +
                Wrap("mt-3 d-flex gap-2", StatusBadge(oSO.Status) +
                PriorityBadge(oSO.Priority))));

            // ---- order lines ---- //
            string vLines;
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsSOLines";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            using (TWMSDataSet oData = FDB.OpenSalesOrderLines(aId))
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "uom", "UoM", "qty_ordered", "Ordered", "qty_picked", "Picked",
                    "outstanding", "Outstanding", "value", "Value" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                    "uom", "qty_ordered", "qty_picked", "outstanding", "value" });
            }
            vLines = oGrid.HTML;

            // ---- pick list grouped by zone, in a TreeGrid ---- //
            string vPick;
            var oTreeGrid = new TsgcHTMLComponent_TreeGrid();
            var oZones = new List<string>();
            oTreeGrid.TreeGridID = "wmsPickTree";
            oTreeGrid.ExpandedByDefault = true;
            oTreeGrid.Striped = true;
            oTreeGrid.IndentPixels = 18;
            TsgcHTMLTreeGridColumn oCol = oTreeGrid.Columns.Add();
            oCol.Caption = "Bin / product";
            oCol = oTreeGrid.Columns.Add();
            oCol.Caption = "In bin";
            oCol.Align = TsgcHTMLTreeGridAlign.tgaRight;
            oCol = oTreeGrid.Columns.Add();
            oCol.Caption = "To pick";
            oCol.Align = TsgcHTMLTreeGridAlign.tgaRight;

            using (TWMSDataSet oData = FDB.OpenPickList(aId))
            {
                while (!oData.Eof)
                {
                    string vZoneKey = "Z" + oData.AsStr("zone");
                    if (oZones.IndexOf(vZoneKey) < 0)
                    {
                        oZones.Add(vZoneKey);
                        oTreeGrid.AddNode(vZoneKey, "", "Zone " + oData.AsStr("zone"),
                            "", "");
                    }
                    string vNodeId = "L" + oData.AsStr("line_id") + "_" +
                        oData.AsStr("location_id");
                    oTreeGrid.AddNode(vNodeId, vZoneKey, oData.AsStr("location") +
                        "  " + oData.AsStr("sku"), oData.AsStr("bin_qty"),
                        oData.AsStr("outstanding"));
                    oData.Next();
                }
            }
            vPick = oTreeGrid.HTML;

            oRoot.AddRaw(Cols(new string[] { Card("Order lines", "", vLines),
                Card("Pick list", "Walk order by bin code, grouped by zone in a " +
                "TreeGrid.", vPick) }, TsgcHTMLColWidth.cw6));

            // ---- pick / pack / ship ---- //
            string vActions = "";
            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/outbound/" + aId.ToString(CultureInfo.InvariantCulture) +
                "/pick";
            oForm.CSSClass = "d-inline-block me-2";
            var oBtn = new TsgcHTMLButton("Pick everything outstanding",
                TsgcHTMLButtonStyle.bsWarning);
            oBtn.ButtonType = "submit";
            oForm.Add(oBtn);
            vActions = vActions + oForm.HTML;

            oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/outbound/" + aId.ToString(CultureInfo.InvariantCulture) +
                "/pack";
            oForm.CSSClass = "d-inline-block me-2";
            oBtn = new TsgcHTMLButton("Mark packed", TsgcHTMLButtonStyle.bsOutlinePrimary);
            oBtn.ButtonType = "submit";
            oForm.Add(oBtn);
            vActions = vActions + oForm.HTML;

            oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/outbound/" + aId.ToString(CultureInfo.InvariantCulture) +
                "/ship";
            oForm.CSSClass = "mt-3";
            var oPad = new TsgcHTMLComponent_SignaturePad();
            oPad.PadID = "wmsPOD";
            oPad.Width = 420;
            oPad.Height = 150;
            oPad.PenColor = "#7C4A03";
            oPad.FieldName = "signature";
            oPad.ShowClear = true;
            oPad.ShowUndo = true;
            oPad.ClearCaption = "Clear";
            oForm.AddRaw(oPad.HTML);
            oBtn = new TsgcHTMLButton("Ship with proof of delivery",
                TsgcHTMLButtonStyle.bsSuccess);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "mt-2";
            oForm.Add(oBtn);
            vActions = vActions + oForm.HTML;

            oRoot.AddRaw(Card("Fulfilment",
                "Pick moves the units out of the bins and writes one movement per " +
                "line. Ship captures the signature on the SignaturePad and stamps " +
                "shipped_at.", vActions));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, oSO.Reference, vBody, new string[] { "Dashboard", "/",
                "Outbound", "/outbound", oSO.Reference, "" });
        }

        // ----------------------------------------------------------------- stock ---

        // Same shape as the product helpers: filters without the page, then a base
        // URL that ends in 'page=' for the Pagination component to complete.
        private static string StockFilterQS(TWMSListFilter aFilter)
        {
            string vResult = "";
            if (aFilter.Search != "")
                vResult = vResult + "&q=" + UrlEnc(aFilter.Search);
            if (aFilter.Zone != "")
                vResult = vResult + "&zone=" + UrlEnc(aFilter.Zone);
            if (aFilter.Sort != "")
                vResult = vResult + "&sort=" + UrlEnc(aFilter.Sort);
            if (aFilter.Dir != "")
                vResult = vResult + "&dir=" + UrlEnc(aFilter.Dir);
            if (aFilter.BelowMin)
                vResult = vResult + "&below=1";
            if (vResult != "")
                vResult = vResult.Substring(1);
            return vResult;
        }

        private static string StockBaseURL(TWMSListFilter aFilter)
        {
            string vResult = "/stock?";
            if (StockFilterQS(aFilter) != "")
                vResult = vResult + StockFilterQS(aFilter) + "&";
            return vResult + "page=";
        }

        public string BuildStockPage(TWMSPageCtx aCtx, TWMSListFilter aFilter)
        {
            int vTotal = FDB.CountStock(aFilter.Search, aFilter.Zone, aFilter.BelowMin);
            int vPages = (int)Math.Ceiling((double)vTotal / WMSConst.CS_WMS_PAGE_SIZE);
            if (vPages < 1)
                vPages = 1;

            var oRoot = new TsgcHTMLNodeList();

            // ---- filter bar ---- //
            string vFilterHTML;
            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/stock";
            oForm.CSSClass = "mb-3";
            var oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-2 align-items-end";

            var oInput = new TsgcHTMLComponent_InputGroup();
            oInput.GroupID = "wmsStockSearch";
            oInput.PrependText = "Search";
            oInput.InputName = "q";
            oInput.InputValue = SafeInputValue(aFilter.Search);
            oInput.Placeholder = "SKU, product or bin code";
            oRow.Col(TsgcHTMLColWidth.cw5).AddRaw(oInput.HTML);

            var oMulti = new TsgcHTMLComponent_MultiSelect();
            oMulti.MultiSelectID = "wmsStockZone";
            oMulti.FieldName = "zone";
            oMulti.Placeholder = "Every zone";
            oMulti.ShowSearch = false;
            oMulti.MaxSelections = 1;
            using (TWMSDataSet oData = FDB.OpenZones())
            {
                oMulti.LoadFromDataSet(oData.DataSet, "zone", "zone");
            }
            oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(oMulti.HTML);

            var oCheck = new TsgcHTMLComponent_CheckBox();
            oCheck.CheckBoxID = "wmsBelowMin";
            oCheck.ElementName = "below";
            oCheck.Label_ = "Below minimum only";
            oCheck.Switch = true;
            oCheck.Checked = aFilter.BelowMin;
            oRow.Col(TsgcHTMLColWidth.cw2).AddRaw(oCheck.HTML);

            var oBtn = new TsgcHTMLButton("Apply", TsgcHTMLButtonStyle.bsOutlinePrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100";
            oRow.Col(TsgcHTMLColWidth.cw2).Add(oBtn);
            oForm.Add(oRow);
            vFilterHTML = oForm.HTML;

            string vTableHTML;
            if (vTotal == 0)
            {
                var oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No stock rows match";
                oEmpty.Description = "Try another zone, or clear the search.";
                oEmpty.ActionCaption = "Show all stock";
                oEmpty.ActionHref = "/stock";
                oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsOutlineSecondary;
                oEmpty.Compact = true;
                vTableHTML = oEmpty.HTML;
            }
            else
            {
                var oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "wmsStockGrid";
                oGrid.Responsive = true;
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.ShowSort = true;
                oGrid.CSSClass = "wms-tight";
                oGrid.SavedViews = true;
                oGrid.PersistKey = "wms.stock";
                oGrid.ShowColumnsMenu = true;
                using (TWMSDataSet oData = FDB.OpenStock(aFilter.Search, aFilter.Zone,
                    aFilter.Sort, aFilter.Dir, aFilter.BelowMin, aFilter.Page,
                    WMSConst.CS_WMS_PAGE_SIZE))
                {
                    SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                        "category", "Category", "location", "Bin", "zone", "Zone",
                        "qty", "Quantity", "min_stock", "Min", "value", "Value" });
                    oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                        "category", "location", "zone", "qty", "min_stock", "value" });
                }
                vTableHTML = oGrid.HTML;

                if (vPages > 1)
                {
                    var oPag = new TsgcHTMLComponent_Pagination();
                    oPag.PaginationID = "wmsStockPag";
                    oPag.CurrentPage = aFilter.Page;
                    oPag.TotalPages = vPages;
                    oPag.TotalItems = vTotal;
                    oPag.PageSize = WMSConst.CS_WMS_PAGE_SIZE;
                    oPag.BaseURL = StockBaseURL(aFilter);
                    oPag.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                    oPag.MaxVisible = 7;
                    vTableHTML = vTableHTML + oPag.HTML;
                }
            }

            oRoot.AddRaw(Card("Stock on hand",
                FmtInt(vTotal) + " product/bin rows. Page " +
                aFilter.Page.ToString(CultureInfo.InvariantCulture) + " of " +
                vPages.ToString(CultureInfo.InvariantCulture) +
                ". Search, sort and paging are LIMIT / " +
                "OFFSET / ORDER BY in SQLite, never an in-memory filter.",
                vFilterHTML + vTableHTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Stock on hand", vBody, new string[] { "Dashboard",
                "/", "Stock", "" });
        }

        public string BuildMovementsPage(TWMSPageCtx aCtx, TWMSListFilter aFilter)
        {
            int vTotal = FDB.CountMovements(aFilter.Search, aFilter.Kind,
                aFilter.DateFrom, aFilter.DateTo);
            int vPages = (int)Math.Ceiling((double)vTotal / WMSConst.CS_WMS_PAGE_SIZE);
            if (vPages < 1)
                vPages = 1;
            // The page parameter must come last and appear once (see ProductBaseURL).
            string vQS = "?";
            if (aFilter.Search != "")
                vQS = vQS + "q=" + UrlEnc(aFilter.Search) + "&";
            if (aFilter.Kind != "")
                vQS = vQS + "kind=" + UrlEnc(aFilter.Kind) + "&";
            if (aFilter.DateFrom != "")
                vQS = vQS + "from=" + UrlEnc(aFilter.DateFrom) + "&";
            if (aFilter.DateTo != "")
                vQS = vQS + "to=" + UrlEnc(aFilter.DateTo) + "&";

            var oRoot = new TsgcHTMLNodeList();

            string vFilterHTML;
            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/stock/movements";
            oForm.CSSClass = "mb-3";
            var oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-2 align-items-end";

            var oInput = new TsgcHTMLComponent_InputGroup();
            oInput.GroupID = "wmsMovSearch";
            oInput.PrependText = "Search";
            oInput.InputName = "q";
            oInput.InputValue = SafeInputValue(aFilter.Search);
            oInput.Placeholder = "SKU, product or reference";
            oRow.Col(TsgcHTMLColWidth.cw5).AddRaw(oInput.HTML);

            // The library has no DateRangePicker, so the range is two DatePickers
            // that post 'from' and 'to'.
            var oDate = new TsgcHTMLComponent_DatePicker();
            oDate.DatePickerID = "wmsMovFrom";
            oDate.ElementName = "from";
            oDate.Label_ = "From";
            oDate.Mode = TsgcHTMLDatePickerMode.dmDate;
            oDate.Value = aFilter.DateFrom;
            oRow.Col(TsgcHTMLColWidth.cw2).AddRaw(oDate.HTML);

            oDate = new TsgcHTMLComponent_DatePicker();
            oDate.DatePickerID = "wmsMovTo";
            oDate.ElementName = "to";
            oDate.Label_ = "To";
            oDate.Mode = TsgcHTMLDatePickerMode.dmDate;
            oDate.Value = aFilter.DateTo;
            oRow.Col(TsgcHTMLColWidth.cw2).AddRaw(oDate.HTML);

            var oRadio = new TsgcHTMLComponent_RadioGroup();
            oRadio.RadioGroupID = "wmsMovKind";
            oRadio.ElementName = "kind";
            oRadio.Label_ = "Movement kind";
            oRadio.InlineLayout = true;
            oRadio.Items.Add("all");
            oRadio.Items.Add("receipt");
            oRadio.Items.Add("putaway");
            oRadio.Items.Add("pick");
            oRadio.Items.Add("adjust");
            oRadio.Items.Add("count");
            oRadio.SelectedIndex = oRadio.Items.IndexOf(
                (aFilter.Kind ?? string.Empty).ToLowerInvariant());
            if (oRadio.SelectedIndex < 0)
                oRadio.SelectedIndex = 0;
            oRow.Col(TsgcHTMLColWidth.cw12).AddRaw(oRadio.HTML);

            var oBtn = new TsgcHTMLButton("Apply", TsgcHTMLButtonStyle.bsOutlinePrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100";
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oBtn);
            oForm.Add(oRow);
            vFilterHTML = oForm.HTML;

            string vTableHTML;
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsMovGrid";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.ShowSort = true;
            oGrid.CSSClass = "wms-tight";
            oGrid.EmptyText = "No movements match.";
            using (TWMSDataSet oData = FDB.OpenMovements(aFilter.Search, aFilter.Kind,
                aFilter.DateFrom, aFilter.DateTo, aFilter.Sort, aFilter.Dir,
                aFilter.Page, WMSConst.CS_WMS_PAGE_SIZE))
            {
                SetLabels(oData, new string[] { "created_at", "When", "kind", "Kind",
                    "sku", "SKU", "name", "Product", "qty", "Quantity", "from_code",
                    "From", "to_code", "To", "reference", "Reference", "user_name",
                    "User" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "created_at",
                    "kind", "sku", "name", "qty", "from_code", "to_code", "reference",
                    "user_name" });
            }
            vTableHTML = oGrid.HTML;

            if (vPages > 1)
            {
                var oPag = new TsgcHTMLComponent_Pagination();
                oPag.PaginationID = "wmsMovPag";
                oPag.CurrentPage = aFilter.Page;
                oPag.TotalPages = vPages;
                oPag.TotalItems = vTotal;
                oPag.PageSize = WMSConst.CS_WMS_PAGE_SIZE;
                oPag.BaseURL = "/stock/movements" + vQS + "page=";
                oPag.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                oPag.MaxVisible = 7;
                vTableHTML = vTableHTML + oPag.HTML;
            }

            oRoot.AddRaw(Card("Movement ledger", FmtInt(vTotal) +
                " movements recorded. Every receipt, put-away, pick, adjustment and " +
                "count writes one row here.", vFilterHTML + vTableHTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Movements", vBody, new string[] { "Dashboard", "/",
                "Stock", "/stock", "Movements", "" });
        }

        // ----------------------------------------------------------- cycle counts --

        public string BuildCountsPage(TWMSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/counts/new";
            oForm.CSSClass = "mb-3";
            var oBtn = new TsgcHTMLButton("Start a count sheet",
                TsgcHTMLButtonStyle.bsWarning);
            oBtn.ButtonType = "submit";
            oForm.Add(oBtn);
            oRoot.AddRaw(oForm.HTML);

            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsCounts";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.Hover = true;
            oGrid.CSSClass = "wms-tight";
            oGrid.EmptyText = "No cycle counts yet.";
            using (TWMSDataSet oData = FDB.OpenStockCounts())
            {
                SetLabels(oData, new string[] { "reference", "Reference", "status",
                    "Status", "lines", "Lines", "counted", "Counted", "variance",
                    "Variance", "created_at", "Opened", "closed_at", "Closed" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "reference",
                    "status", "lines", "counted", "variance", "created_at",
                    "closed_at" });
            }
            oRoot.AddRaw(Card("Cycle counts",
                "A sheet is seeded from the fullest bins. Closing it posts every " +
                "variance as a count movement.", oGrid.HTML));

            // Open a sheet: the same rows through ListGroup, whose Href field turns
            // each entry into a link. No JavaScript row handler needed.
            var oList = new TsgcHTMLComponent_ListGroup();
            oList.ListGroupID = "wmsCountList";
            using (TWMSDataSet oData = FDB.OpenStockCounts())
            {
                oData.First();
                while (!oData.Eof)
                {
                    oList.AddItem(oData.AsStr("reference") + "  (" +
                        oData.AsStr("status") + ")", "/counts/" + oData.AsStr("id"),
                        oData.AsStr("counted") + "/" + oData.AsStr("lines"));
                    oData.Next();
                }
            }
            oRoot.AddRaw(Card("Open a sheet", "", oList.HTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Cycle counts", vBody, new string[] { "Dashboard",
                "/", "Counts", "" });
        }

        public string BuildCountDetailPage(TWMSPageCtx aCtx, long aId)
        {
            TWMSStockCount oCount;
            if (!FDB.GetStockCount(aId, out oCount))
                return BuildNotFoundPage(aCtx.Theme);

            var oRoot = new TsgcHTMLNodeList();

            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsCountLines";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            using (TWMSDataSet oData = FDB.OpenStockCountLines(aId))
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "location", "Bin", "qty_expected", "Expected", "qty_counted",
                    "Counted", "variance", "Variance" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                    "location", "qty_expected", "qty_counted", "variance" });
            }
            oRoot.AddRaw(Card(oCount.Reference, "Opened " + FmtDate(oCount.CreatedAt) +
                " - status " + oCount.Status, oGrid.HTML));

            if (string.Equals(oCount.Status, "open", StringComparison.OrdinalIgnoreCase))
            {
                var oForm = new TsgcHTMLComponent_Form();
                oForm.FormID = "wmsCountLine";
                oForm.Action = "/counts/" + aId.ToString(CultureInfo.InvariantCulture) +
                    "/line";
                oForm.Method = TsgcHTMLFormMethod.fmPost;
                oForm.Layout = TsgcHTMLFormLayout.flVertical;
                oForm.SubmitText = "Record count";
                oForm.SubmitStyle = TsgcHTMLButtonStyle.bsWarning;

                TsgcHTMLFormField oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftSelect;
                oField.Name = "line_id";
                oField.Label_ = "Line";
                using (TWMSDataSet oData = FDB.OpenStockCountLines(aId))
                {
                    while (!oData.Eof)
                    {
                        oField.Options.Add(oData.AsStr("id") + "=" +
                            oData.AsStr("location") + " " + oData.AsStr("sku") +
                            " (expected " + oData.AsStr("qty_expected") + ")");
                        oData.Next();
                    }
                }

                oField = oForm.Fields.Add();
                oField.FieldType = TsgcHTMLFieldType.ftNumber;
                oField.Name = "counted";
                oField.Label_ = "Counted quantity";
                oField.Required = true;

                oRoot.AddRaw(Card("Record a line", "", oForm.HTML));

                var oClose = new TsgcHTMLForm();
                oClose.Method = "POST";
                oClose.Action = "/counts/" + aId.ToString(CultureInfo.InvariantCulture) +
                    "/close";
                var oBtn = new TsgcHTMLButton("Close sheet and post variances",
                    TsgcHTMLButtonStyle.bsDanger);
                oBtn.ButtonType = "submit";
                oClose.Add(oBtn);
                oRoot.AddRaw(Card("Close", "Posting writes one count movement per " +
                    "line whose counted quantity differs from the expected one.",
                    oClose.HTML));
            }

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, oCount.Reference, vBody, new string[] { "Dashboard",
                "/", "Counts", "/counts", oCount.Reference, "" });
        }

        // --------------------------------------------------------------- reports ---

        public string BuildReportsPage(TWMSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oBar = new TsgcHTMLComponent_Toolbar();
            oBar.ToolbarID = "wmsRepBar";
            oBar.AddButton("Valuation PDF", TsgcHTMLButtonStyle.bsOutlineDanger,
                "/reports/valuation.pdf");
            oBar.AddButton("Valuation XLSX", TsgcHTMLButtonStyle.bsOutlineSuccess,
                "/reports/valuation.xlsx");
            oRoot.AddRaw(oBar.HTML);

            string vVal;
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsValuation";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.ShowSort = true;
            oGrid.CSSClass = "wms-tight";
            using (TWMSDataSet oData = FDB.OpenValuation())
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "category", "Category", "uom", "UoM", "unit_cost", "Unit cost",
                    "onhand", "On hand", "value", "Value" });
                oGrid.LoadFromDataSet(oData.DataSet, 200);
            }
            vVal = oGrid.HTML;

            string vABC;
            oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsABC";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            using (TWMSDataSet oData = FDB.OpenABC())
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "category", "Category", "moves", "Units moved", "value",
                    "Movement value", "band", "Band" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "band", "sku",
                    "name", "category", "moves", "value" });
            }
            vABC = oGrid.HTML;

            string vSlow;
            oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsSlow";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            using (TWMSDataSet oData = FDB.OpenSlowMovers())
            {
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "category", "Category", "onhand", "On hand", "value", "Value",
                    "last_pick", "Last picked" });
                oGrid.LoadFromDataSet(oData.DataSet);
            }
            vSlow = oGrid.HTML;

            string vMap;
            var oTreeMap = new TsgcHTMLComponent_TreeMap();
            oTreeMap.ElementID = "wmsRepMap";
            oTreeMap.Width = 900;
            oTreeMap.Height = 340;
            oTreeMap.ColorScheme = TsgcHTMLTreeMapScheme.tmWarm;
            oTreeMap.ShowValues = true;
            oTreeMap.Decimals = 0;
            using (TWMSDataSet oData = FDB.OpenValueByCategory())
            {
                oTreeMap.LoadFromDataSet(oData.DataSet, "category", "value");
            }
            vMap = Wrap("table-responsive", oTreeMap.HTML);

            var oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.TabsID = "wmsRepTabs";
            oTabs.Style = TsgcHTMLTabStyle.tsTab;
            TsgcHTMLTabItem oTab = oTabs.Items.Add();
            oTab.Title = "Valuation";
            oTab.Content = vVal;
            oTab.Active = true;
            oTab = oTabs.Items.Add();
            oTab.Title = "ABC analysis";
            oTab.Content = vABC;
            oTab = oTabs.Items.Add();
            oTab.Title = "Slow movers";
            oTab.Content = vSlow;
            oTab = oTabs.Items.Add();
            oTab.Title = "Value by category";
            oTab.Content = vMap;
            oRoot.AddRaw(Card("Reports",
                "Every tab is one SELECT bound into a component. The PDF and XLSX " +
                "buttons render the same rows server side.", oTabs.HTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Reports", vBody, new string[] { "Dashboard", "/",
                "Reports", "" });
        }

        // ------------------------------------------------------------------- sql ---

        public string BuildSQLPage(TWMSPageCtx aCtx)
        {
            // One block: the SQL text on the left, the live component it feeds on the
            // right. The SQL is the string the query actually ran, taken from the
            // TWMSDataSet, not a copy pasted into the page.
            Func<string, string, string, string, string> SQLBlock =
                (aTitle, aSQL, aComponentHTML, aNote) =>
                {
                    var oRow = new TsgcHTMLRow();
                    oRow.CSSClass = "g-3";
                    var oWrap = new TsgcHTMLContainer("div");
                    oWrap.CSSClass = "wms-sql";
                    var oPre = new TsgcHTMLContainer("pre");
                    oPre.AddText(aSQL);
                    oWrap.Add(oPre);
                    oRow.Col(TsgcHTMLColWidth.cw5).Add(oWrap);
                    oRow.Col(TsgcHTMLColWidth.cw7).AddRaw(aComponentHTML);
                    return Card(aTitle, aNote, oRow.HTML);
                };

            var oRoot = new TsgcHTMLNodeList();

            var oAlert = new TsgcHTMLAlert("");
            oAlert.Style = TsgcHTMLAlertStyle.asWarning;
            oAlert.AddRaw("<strong>This is the whole data layer.</strong> " +
                "Each block below runs the SQL on the left through a plain " +
                "query and hands the open dataset to LoadFromDataSet on the " +
                "component on the right. There is no REST endpoint, no JSON, no " +
                "controller and no client-side data binding anywhere in this " +
                "application.");
            oRoot.AddRaw(oAlert.HTML);

            // ---- Grid ---- //
            using (TWMSDataSet oData = FDB.OpenProducts("", "", "sku", "asc", 1, 10))
            {
                var oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "wmsSqlGrid";
                oGrid.Responsive = true;
                oGrid.Striped = true;
                oGrid.CSSClass = "wms-tight";
                SetLabels(oData, new string[] { "sku", "SKU", "name", "Product",
                    "category", "Category", "onhand", "On hand" });
                oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                    "category", "onhand" });
                oRoot.AddRaw(SQLBlock("Grid", oData.SQL, oGrid.HTML,
                    "TsgcHTMLComponent_Grid.LoadFromDataSet(oQuery)"));
            }

            // ---- Chart ---- //
            using (TWMSDataSet oData = FDB.OpenValueByCategory())
            {
                var oChart = new TsgcHTMLComponent_Chart();
                oChart.ChartID = "wmsSqlChart";
                oChart.ChartType = TsgcHTMLChartType.ctDoughnut;
                oChart.CSSHeight = "300px";
                oChart.ShowLegend = true;
                oChart.LoadFromDataSet(oData.DataSet, "category", "value");
                oRoot.AddRaw(SQLBlock("Chart", oData.SQL, oChart.HTML,
                    "TsgcHTMLComponent_Chart.LoadFromDataSet(oQuery, 'category', " +
                    "['value'])"));
            }

            // ---- TreeView ---- //
            using (TWMSDataSet oData = FDB.OpenLocationTree())
            {
                var oTree = new TsgcHTMLComponent_TreeView();
                oTree.TreeID = "wmsSqlTree";
                oTree.ShowLines = true;
                oTree.LoadFromDataSet(oData.DataSet, "id", "parent_id", "code");
                oRoot.AddRaw(SQLBlock("TreeView", oData.SQL,
                    Wrap("", oTree.HTML, "max-height:320px;overflow:auto;"),
                    "TsgcHTMLComponent_TreeView.LoadFromDataSet(oQuery, " +
                    "'id', 'parent_id', 'code')"));
            }

            // ---- ListGroup ---- //
            using (TWMSDataSet oData = FDB.OpenMovements("", "", "", "", "created",
                "desc", 1, 8))
            {
                var oList = new TsgcHTMLComponent_ListGroup();
                oList.ListGroupID = "wmsSqlList";
                oList.LoadFromDataSet(oData.DataSet, "reference", "", "kind");
                oRoot.AddRaw(SQLBlock("ListGroup", oData.SQL, oList.HTML,
                    "TsgcHTMLComponent_ListGroup.LoadFromDataSet(oQuery, " +
                    "'reference', '', 'kind')"));
            }

            var oPanel = new TsgcHTMLComponent_Panel();
            oPanel.PanelID = "wmsSqlNote";
            oPanel.Title = "What this replaces";
            oPanel.Color = TsgcHTMLColor.hcLight;
            oPanel.Body = "In a JavaScript stack the four blocks above would need " +
                "four REST endpoints, four DTO classes, four serializers, a client " +
                "router, a state store and a build pipeline. Here they are four " +
                "SELECT statements and four LoadFromDataSet calls, compiled into one " +
                "executable with no runtime dependencies.";
            oRoot.AddRaw(oPanel.HTML);

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "SQL, no REST layer", vBody, new string[] {
                "Dashboard", "/", "SQL", "" });
        }

        // ------------------------------------------------------------------ admin --

        public string BuildUsersPage(TWMSPageCtx aCtx, long aEditId)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oUsers = new TsgcHTMLComponent_UserManagement();
            oUsers.TableID = "wmsUsers";
            oUsers.ShowSearch = true;
            oUsers.ShowAddButton = false;
            oUsers.ShowRoles = true;
            oUsers.ShowStatus = true;
            oUsers.ShowLastLogin = true;
            oUsers.ShowActions = false;
            oUsers.PageSize = 25;
            oUsers.Striped = true;
            oUsers.DisplayNameField = "display_name";
            oUsers.LastLoginField = "created_at";
            using (TWMSDataSet oData = FDB.OpenUsers())
            {
                // The component's "email" slot is the second line of the user cell;
                // this app has no e-mail column, so the login name goes there under
                // the display name. An admin has to be able to see both.
                oUsers.LoadFromDataSet(oData.DataSet, "id", "username", "username",
                    "role", "");
            }
            oRoot.AddRaw(Card("Warehouse users",
                "UserManagement.LoadFromDataSet over the users table.", oUsers.HTML));

            TWMSUser oUser = null;
            bool vHas = (aEditId > 0) && FDB.GetUserById(aEditId, out oUser);

            var oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "wmsUserForm";
            oForm.Action = "/users/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Save user";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsWarning;

            TsgcHTMLFormField oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftHidden;
            oField.Name = "id";
            if (vHas)
                oField.Value = oUser.Id.ToString(CultureInfo.InvariantCulture);
            else
                oField.Value = "0";

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "username";
            oField.Label_ = "User name";
            oField.ColSpan = 4;
            oField.Required = true;
            if (vHas)
                oField.Value = oUser.Username;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftText;
            oField.Name = "display_name";
            oField.Label_ = "Full name";
            oField.ColSpan = 4;
            if (vHas)
                oField.Value = oUser.DisplayName;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftSelect;
            oField.Name = "role";
            oField.Label_ = "Role";
            oField.ColSpan = 4;
            oField.Options.Add(WMSConst.CS_ROLE_ADMIN);
            oField.Options.Add(WMSConst.CS_ROLE_SUPERVISOR);
            oField.Options.Add(WMSConst.CS_ROLE_OPERATOR);
            if (vHas)
                oField.Value = oUser.Role;

            oField = oForm.Fields.Add();
            oField.FieldType = TsgcHTMLFieldType.ftPassword;
            oField.Name = "password";
            oField.Label_ = "Password";
            oField.HelpText = "Leave blank to keep the current password.";
            oField.ColSpan = 6;

            oRoot.AddRaw(Card("Add or edit a user", "", oForm.HTML));

            var oRoles = new TsgcHTMLComponent_RolesPermissions();
            oRoles.MatrixID = "wmsRoles";
            oRoles.ReadOnly = true;
            oRoles.ShowCategories = true;
            oRoles.ShowDescriptions = false;
            oRoles.StickyHeader = true;
            oRoles.AddRole(WMSConst.CS_ROLE_ADMIN, "Administrator");
            oRoles.AddRole(WMSConst.CS_ROLE_SUPERVISOR, "Supervisor");
            oRoles.AddRole(WMSConst.CS_ROLE_OPERATOR, "Operator");
            oRoles.AddPermission("master", "Edit master data", "Back office");
            oRoles.AddPermission("orders", "Work purchase and sales orders",
                "Back office");
            oRoles.AddPermission("reports", "Run reports and exports", "Back office");
            oRoles.AddPermission("users", "Manage users", "Administration");
            oRoles.AddPermission("audit", "Read the audit log", "Administration");
            oRoles.AddPermission("hh_receive", "Receive on the handheld", "Handheld");
            oRoles.AddPermission("hh_pick", "Pick on the handheld", "Handheld");
            oRoles.AddPermission("hh_count", "Count on the handheld", "Handheld");
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "master", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "orders", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "reports", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "users", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "audit", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "hh_receive", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "hh_pick", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_ADMIN, "hh_count", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_SUPERVISOR, "orders", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_SUPERVISOR, "reports", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_SUPERVISOR, "hh_receive", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_SUPERVISOR, "hh_pick", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_SUPERVISOR, "hh_count", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_OPERATOR, "hh_receive", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_OPERATOR, "hh_pick", true);
            oRoles.SetGrant(WMSConst.CS_ROLE_OPERATOR, "hh_count", true);
            oRoot.AddRaw(Card("What each role may do",
                "The dispatcher enforces exactly this matrix server side.",
                oRoles.HTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Users", vBody, new string[] { "Dashboard", "/",
                "Users", "" });
        }

        public string BuildAuditPage(TWMSPageCtx aCtx, string aSearch)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/audit";
            oForm.CSSClass = "mb-3";
            var oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-2 align-items-end";
            var oInput = new TsgcHTMLComponent_InputGroup();
            oInput.GroupID = "wmsAuditSearch";
            oInput.PrependText = "Search";
            oInput.InputName = "q";
            oInput.InputValue = SafeInputValue(aSearch);
            oInput.Placeholder = "action, entity or detail";
            oRow.Col(TsgcHTMLColWidth.cw8).AddRaw(oInput.HTML);

            var oBtn = new TsgcHTMLButton("Search", TsgcHTMLButtonStyle.bsOutlinePrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100";
            oRow.Col(TsgcHTMLColWidth.cw2).Add(oBtn);
            oForm.Add(oRow);
            oRoot.AddRaw(oForm.HTML);

            using (TWMSDataSet oData = FDB.OpenAudit(aSearch, 200))
            {
                var oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "wmsAudit";
                oGrid.Responsive = true;
                oGrid.Striped = true;
                oGrid.ShowSort = true;
                oGrid.CSSClass = "wms-tight";
                oGrid.EmptyText = "Nothing logged yet.";
                SetLabels(oData, new string[] { "created_at", "When", "user_name",
                    "User", "action", "Action", "entity", "Entity", "entity_id", "Id",
                    "detail", "Detail", "ip", "IP" });
                oGrid.LoadFromDataSet(oData.DataSet);
                oRoot.AddRaw(Card("Audit log", "Every mutating route writes here.",
                    oGrid.HTML));

                oData.First();
                var oLine = new TsgcHTMLComponent_Timeline();
                oLine.TimelineID = "wmsAuditLine";
                oLine.LoadFromDataSet(oData.DataSet, "action", "detail", "created_at");
                oRoot.AddRaw(Card("Recent activity",
                    "The same rows through Timeline.LoadFromDataSet.", oLine.HTML));
            }

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Audit log", vBody, new string[] { "Dashboard", "/",
                "Audit", "" });
        }

        public string BuildSecurityPage(TWMSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oAuthn = new TsgcHTMLComponent_WebAuthnLogin();
            oAuthn.WebAuthnID = "wmsRegKey";
            oAuthn.Mode = TsgcHTMLWebAuthnMode.wamRegister;
            oAuthn.Title = "Passkeys";
            oAuthn.Description = "Register the biometric or security key on this " +
                "device so the handheld can be unlocked without typing a password.";
            oAuthn.RegisterURL = "/passkey/register";
            oAuthn.CallbackURL = "/security";
            oAuthn.RegisterButtonText = "Register a passkey";
            oAuthn.RegisterButtonStyle = TsgcHTMLButtonStyle.bsWarning;
            oRoot.AddRaw(Card("Sign-in security", "", oAuthn.HTML));

            TWMSPasskey[] vKeys = FDB.GetPasskeysByUser(aCtx.UserId);
            var oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "wmsKeys";
            oGrid.Responsive = true;
            oGrid.Striped = true;
            oGrid.CSSClass = "wms-tight";
            oGrid.EmptyText = "No passkey registered on this account yet.";
            TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
            oCol.Name = "device";
            oCol.Title = "Device";
            oCol = oGrid.Columns.Add();
            oCol.Name = "created";
            oCol.Title = "Registered";
            oCol = oGrid.Columns.Add();
            oCol.Name = "used";
            oCol.Title = "Last used";
            for (int vI = 0; vI < vKeys.Length; vI++)
                oGrid.AddRow(vKeys[vI].DeviceName, FmtDate(vKeys[vI].CreatedAt),
                    FmtDate(vKeys[vI].LastUsedAt));
            oRoot.AddRaw(Card("Registered passkeys", "", oGrid.HTML));

            string vBody = oRoot.HTML;
            return BuildShell(aCtx, "Security", vBody, new string[] { "Dashboard", "/",
                "Security", "" });
        }
    }
}
