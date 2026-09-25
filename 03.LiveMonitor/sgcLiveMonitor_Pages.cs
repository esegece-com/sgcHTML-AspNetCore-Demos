// ***************************************************************************
//  sgcLiveMonitor - mini ERP web-app demo (node-based view layer)
//  Port of delphi\Demos\60.HTML\50.LiveMonitor\Source\sgcLiveMonitor_Pages.pas
//
//  written by eSeGeCe
//  copyright © 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
// Node-based view layer for the Live Monitor demo. Zero custom HTML strings: each
// page builds a node tree, renders it to HTML and returns it. Every page is
// themed (light/dark/system) and localized (en/es/de/fr) and the in-app pages
// render their body inside a shared shell (sidebar + navbar with theme and
// language switchers). The managed port keeps the Delphi class + method names;
// .NET is GC-managed so the Delphi .Free calls are dropped.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using esegece.sgcWebSockets;

namespace LiveMonitor
{
    public class TERPPages
    {
        // ---------------------------------------------------------------------
        // theme CSS constants (copied verbatim from the Delphi)
        // ---------------------------------------------------------------------

        // Dark CSS overrides shared between explicit dark mode and the 'system'
        // mode (where they're wrapped in a prefers-color-scheme media query).
        private const string CS_THEME_DARK_RULES = "body{background:#1a1d23;color:#e4e6eb;}" +
            ".card{background:#2d3038;border:1px solid #3d4048;color:#e4e6eb;}" +
            ".card-header{background:#353840;border-bottom:1px solid #3d4048;}" +
            ".table{--bs-table-bg:#2d3038;--bs-table-color:#e4e6eb;" +
            "--bs-table-border-color:#3d4048;--bs-table-striped-bg:#353840;" +
            "--bs-table-striped-color:#e4e6eb;--bs-table-hover-bg:#3d4048;" +
            "--bs-table-hover-color:#fff;color:#e4e6eb;border-color:#3d4048;}" +
            ".table>:not(caption)>*>*{background-color:var(--bs-table-bg);" +
            "color:var(--bs-table-color);border-bottom-color:#3d4048;}" +
            ".table-bordered>:not(caption)>*>*{border-color:#3d4048;}" +
            ".table-light{--bs-table-bg:#353840;--bs-table-color:#e4e6eb;" +
            "--bs-table-border-color:#3d4048;color:#e4e6eb;}" +
            "thead.table-light th{background:#353840;color:#e4e6eb;border-color:#3d4048;}" +
            ".text-muted{color:#8b8fa3 !important;}" +
            ".form-control,.form-select{background:#2d3038;border-color:#3d4048;color:#e4e6eb;}" +
            ".form-control:focus,.form-select:focus{background:#353840;color:#e4e6eb;border-color:#0d6efd;}" +
            ".list-group-item{background:#2d3038;border-color:#3d4048;color:#e4e6eb;}" +
            ".modal-content{background:#2d3038;color:#e4e6eb;}" +
            ".page-link{background:#2d3038;border-color:#3d4048;color:#e4e6eb;}" +
            ".page-item.active .page-link{background:#0d6efd;border-color:#0d6efd;}" +
            "a{color:#6ea8fe;}" +
            ".dropdown-menu{background:#2d3038;border-color:#3d4048;}" +
            ".dropdown-item{color:#e4e6eb;}" +
            ".dropdown-item:hover,.dropdown-item:focus{background:#3d4048;color:#fff;}" +
            ".nav-tabs .nav-link{color:#e4e6eb;}" +
            ".nav-tabs .nav-link.active{background:#2d3038;border-color:#3d4048 #3d4048 #2d3038;color:#fff;}" +
            ".bg-light{background-color:#1a1d23 !important;color:#e4e6eb !important;}";

        // Light-mode overrides matching the public www.esegece.com palette.
        private const string CS_THEME_LIGHT_RULES =
            "@import url(\"https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap\");" +
            "body{font-family:\"Inter\",-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,sans-serif;color:#1E293B;background:#FFFFFF;line-height:1.6;}" +
            ":root,html[data-bs-theme=\"light\"]{" +
            "--bs-primary:#0057B8;--bs-primary-rgb:0,87,184;" +
            "--bs-link-color:#0057B8;--bs-link-color-rgb:0,87,184;" +
            "--bs-link-hover-color:#003D82;--bs-link-hover-color-rgb:0,61,130;" +
            "--bs-body-color:#1E293B;--bs-body-color-rgb:30,41,59;" +
            "--bs-body-bg:#FFFFFF;--bs-body-bg-rgb:255,255,255;" +
            "--bs-secondary-color:#64748B;--bs-secondary-color-rgb:100,116,139;" +
            "--bs-tertiary-bg:#F8FAFC;--bs-tertiary-bg-rgb:248,250,252;" +
            "--bs-border-color:#E2E8F0;--bs-border-color-translucent:rgba(0,87,184,.12);" +
            "--bs-warning:#F0A400;--bs-warning-rgb:240,164,0;" +
            "--bs-warning-bg-subtle:#FFF8E5;--bs-warning-text-emphasis:#7A4F00;" +
            "--bs-info:#0057B8;--bs-info-rgb:0,87,184;" + "}" +
            ".btn-primary{--bs-btn-bg:#0057B8;--bs-btn-border-color:#0057B8;--bs-btn-hover-bg:#003D82;--bs-btn-hover-border-color:#003D82;--bs-btn-active-bg:#003D82;--bs-btn-active-border-color:#003D82;}" +
            ".btn-outline-primary{--bs-btn-color:#0057B8;--bs-btn-border-color:#0057B8;--bs-btn-hover-bg:#0057B8;--bs-btn-hover-border-color:#0057B8;}" +
            ".btn-warning{--bs-btn-bg:#F0A400;--bs-btn-border-color:#F0A400;--bs-btn-hover-bg:#D18F00;--bs-btn-hover-border-color:#D18F00;--bs-btn-color:#fff;--bs-btn-hover-color:#fff;}" +
            ".btn{border-radius:8px;font-weight:600;}" +
            ".card{border-radius:12px;border:1px solid #E2E8F0;background:#FFFFFF;box-shadow:0 1px 3px rgba(0,0,0,.06),0 1px 2px rgba(0,0,0,.04);}" +
            ".card-header{background:#F8FAFC;border-bottom:1px solid #E2E8F0;font-weight:600;}" +
            ".table{--bs-table-color:#1E293B;--bs-table-border-color:#E2E8F0;}" +
            "thead.table-light{--bs-table-bg:#F8FAFC;--bs-table-color:#1E293B;--bs-table-border-color:#E2E8F0;}" +
            "thead.table-dark{--bs-table-bg:#0057B8;--bs-table-color:#fff;--bs-table-border-color:#003D82;}" +
            "nav.navbar.bg-dark,nav.navbar.navbar-dark{background:rgba(255,255,255,.97) !important;border-bottom:1px solid #E2E8F0;backdrop-filter:blur(12px);}" +
            "nav.navbar.bg-dark .navbar-brand,nav.navbar.bg-dark .navbar-text,nav.navbar.bg-dark .nav-link{color:#1E293B !important;}" +
            "nav.navbar.bg-dark .navbar-brand{font-weight:800;}" +
            "nav.navbar.bg-dark .nav-link.fw-medium,nav.navbar.bg-dark .nav-link.text-light{color:#64748B !important;font-weight:500;}" +
            "nav.navbar.bg-dark .navbar-toggler{border-color:#E2E8F0;}" +
            "nav.navbar.bg-dark .navbar-toggler-icon{filter:invert(.45) brightness(.7);}" +
            "nav.navbar.bg-dark .btn-outline-light{color:#0057B8;border-color:#0057B8;background:transparent;}" +
            "nav.navbar.bg-dark .btn-outline-light:hover{background:#0057B8;color:#fff;}" +
            "nav.navbar.bg-dark .dropdown-menu{background:#fff;border:1px solid #E2E8F0;box-shadow:0 4px 6px rgba(0,0,0,.05),0 2px 4px rgba(0,0,0,.04);}" +
            "nav.navbar.bg-dark .dropdown-item{color:#1E293B;}" +
            "nav.navbar.bg-dark .dropdown-item:hover,nav.navbar.bg-dark .dropdown-item:focus{background:#E8F1FB;color:#0057B8;}" +
            ".offcanvas.bg-dark,.offcanvas-lg.bg-dark{background:#F8FAFC !important;border-right:1px solid #E2E8F0;}" +
            ".offcanvas.bg-dark .text-white,.offcanvas-lg.bg-dark .text-white{color:#1E293B !important;}" +
            ".offcanvas.bg-dark .nav-link,.offcanvas-lg.bg-dark .nav-link{color:#1E293B !important;font-weight:600;border-left:3px solid transparent;border-radius:6px;padding:10px 14px;}" +
            ".offcanvas.bg-dark .nav-link:hover,.offcanvas-lg.bg-dark .nav-link:hover{background:#E8F1FB;color:#0057B8 !important;border-left-color:#0057B8;}" +
            ".offcanvas.bg-dark .nav-link.active,.offcanvas-lg.bg-dark .nav-link.active{background:#E8F1FB;color:#0057B8 !important;border-left-color:#0057B8;font-weight:700;}" +
            ".offcanvas.bg-dark hr,.offcanvas-lg.bg-dark hr{border-color:#E2E8F0;}" +
            ".offcanvas.bg-dark .btn-close-white,.offcanvas-lg.bg-dark .btn-close-white{filter:none;}" +
            "#sgcWebsiteSidebar .offcanvas-body{overflow-y:auto;-webkit-overflow-scrolling:touch;}" +
            "@media (max-width:991.98px){#sgcWebsiteSidebar{height:100%;min-height:0 !important;}}" +
            "@media (min-width:992px){#sgcWebsiteSidebar{min-height:100vh;}}" +
            ".form-control:focus,.form-select:focus{border-color:#0057B8;box-shadow:0 0 0 .2rem rgba(0,87,184,.15);}" +
            ".alert-info{background:#E8F1FB;border-color:#0057B8;color:#003D82;}" +
            ".badge.bg-primary{background:#0057B8 !important;}" +
            ".badge.bg-info{background:#0057B8 !important;color:#fff;}" +
            ".badge.bg-warning{background:#F0A400 !important;color:#fff;}" +
            ".badge.bg-autologin{background:#E8F1FB !important;color:#003D82 !important;}" +
            "a{color:#0057B8;}a:hover{color:#003D82;}" +
            ".col-auto>.bg-dark{background:#F8FAFC !important;border-right:1px solid #E2E8F0;}" +
            ".col-auto>.bg-dark .text-white,.col-auto>.bg-dark strong{color:#1E293B !important;}" +
            ".col-auto>.bg-dark .nav-pills .nav-link{color:#1E293B !important;font-weight:600;border-left:3px solid transparent;border-radius:6px;padding:10px 14px;}" +
            ".col-auto>.bg-dark .nav-pills .nav-link:hover{background:#E8F1FB;color:#0057B8 !important;border-left-color:#0057B8;}" +
            ".col-auto>.bg-dark .nav-pills .nav-link.active{background:#E8F1FB !important;color:#0057B8 !important;border-left-color:#0057B8;font-weight:700;}" +
            ".col-auto>.bg-dark hr{border-color:#E2E8F0 !important;color:#E2E8F0 !important;}";

        // Navbar stacking context (lift navbar + open dropdowns above body cards).
        private const string CS_NAVBAR_STACKING_RULES = ".navbar{position:relative;z-index:1030;}" +
            ".navbar .dropdown-menu{z-index:1040;}" +
            "@media (min-width:992px){#sgcWebsiteSidebar{position:static !important;" +
            "transform:none !important;visibility:visible !important;" +
            "width:240px !important;height:auto !important;}}";

        // Inline Bootstrap-style SVG glyphs for the KPI cards (24x24, currentColor).
        private const string CS_ICON_CUSTOMERS =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"26\" height=\"26\" " +
            "fill=\"currentColor\" viewBox=\"0 0 16 16\"><path d=\"M15 14s1 0 1-1-1-4-5-4" +
            "-5 3-5 4 1 1 1 1zm-7.978-1A.261.261 0 0 1 7 13h4.99a.27.27 0 0 1 .026" +
            "-.004 4 4 0 0 0-8.04 0zM11 7a3 3 0 1 0 0-6 3 3 0 0 0 0 6m-9 6s-1 0-1-1" +
            " 1-4 6-4 6 3 6 4-1 1-1 1zm9-7a3 3 0 1 0 0-6 3 3 0 0 0 0 6\"/></svg>";
        private const string CS_ICON_PRODUCTS =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"26\" height=\"26\" " +
            "fill=\"currentColor\" viewBox=\"0 0 16 16\"><path d=\"M8.186 1.113a.5.5 0 0 0" +
            "-.372 0L1.846 3.5 8 5.961 14.154 3.5zM15 4.239l-6.5 2.6v7.922l6.224-2.49" +
            "A.5.5 0 0 0 15 11.5zm-7.5 10.522V6.84L1 4.239v7.261a.5.5 0 0 0 .276.447z" +
            "\"/></svg>";
        private const string CS_ICON_PROVIDERS =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"26\" height=\"26\" " +
            "fill=\"currentColor\" viewBox=\"0 0 16 16\"><path d=\"M0 3.5A1.5 1.5 0 0 1 " +
            "1.5 2h9A1.5 1.5 0 0 1 12 3.5V5h1.02a1.5 1.5 0 0 1 1.17.563l1.481 1.85a" +
            "1.5 1.5 0 0 1 .329.938V10.5a1.5 1.5 0 0 1-1.5 1.5H14a2 2 0 1 1-4 0H5a2 " +
            "2 0 1 1-3.998-.085A1.5 1.5 0 0 1 0 10.5zm1.294 7.456A2 2 0 0 1 4.732 " +
            "11h5.536a2 2 0 0 1 .732-.732V3.5a.5.5 0 0 0-.5-.5h-9a.5.5 0 0 0-.5.5z" +
            "M12 10a2 2 0 0 1 1.732 1h.768a.5.5 0 0 0 .5-.5V8.35a.5.5 0 0 0-.11-.312" +
            "l-1.48-1.85A.5.5 0 0 0 13.02 6H12zm-9 1a1 1 0 1 0 0 2 1 1 0 0 0 0-2m9 " +
            "0a1 1 0 1 0 0 2 1 1 0 0 0 0-2\"/></svg>";
        private const string CS_ICON_INVOICES =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"26\" height=\"26\" " +
            "fill=\"currentColor\" viewBox=\"0 0 16 16\"><path d=\"M5 1.5A1.5 1.5 0 0 1 " +
            "6.5 0h7A1.5 1.5 0 0 1 15 1.5v13a1.5 1.5 0 0 1-1.5 1.5H6.5A1.5 1.5 0 0 1 " +
            "5 14.5zM6.5 1a.5.5 0 0 0-.5.5v13a.5.5 0 0 0 .5.5h7a.5.5 0 0 0 .5-.5v-13a" +
            ".5.5 0 0 0-.5-.5zM3 4.5a.5.5 0 0 1 .5.5v9a.5.5 0 0 1-1 0V5a.5.5 0 0 1 " +
            ".5-.5m-2 2A.5.5 0 0 1 1.5 7v5a.5.5 0 0 1-1 0V7a.5.5 0 0 1 .5-.5\"/></svg>";
        private const string CS_ICON_REVENUE =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"26\" height=\"26\" " +
            "fill=\"currentColor\" viewBox=\"0 0 16 16\"><path d=\"M4 10.781c.148 1.667 " +
            "1.513 2.85 3.591 3.003V15h1.043v-1.216c2.27-.179 3.678-1.438 3.678-3.3" +
            "63 0-1.747-1.097-2.685-3.054-3.151l-.624-.156V3.713c1.06.151 1.738.721 " +
            "1.9 1.535H12.4c-.138-1.643-1.452-2.812-3.115-2.928V1H8.243v1.32c-1.943" +
            ".192-3.348 1.36-3.348 3.234 0 1.59 1.051 2.638 2.83 3.057l.535.136v3.07" +
            "4c-1.09-.16-1.81-.748-1.97-1.59zm4.978 2.119v-2.928l.367.092c1.077.27 " +
            "1.66.749 1.66 1.495 0 .898-.668 1.332-2.027 1.341m-1.04-7.137c-1.234" +
            "-.293-1.74-.752-1.74-1.494 0-.842.604-1.479 1.74-1.604z\"/></svg>";

        // Inline eSeGeCe brand mark (self-contained SVG, em-sized so it scales
        // with the surrounding font). Used in the sidebar, navbar, login and footer.
        private const string CS_ESEGECE_LOGO_SVG =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\" " +
            "width=\"1.6em\" height=\"1.6em\" role=\"img\" aria-label=\"eSeGeCe\" " +
            "class=\"me-2 flex-shrink-0\"><rect width=\"64\" height=\"64\" rx=\"12\" " +
            "fill=\"#0057B8\"/><text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica," +
            "sans-serif\" font-weight=\"900\" font-size=\"44\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">e</text></svg>";

        // ---------------------------------------------------------------------
        // fields + i18n shortcut
        // ---------------------------------------------------------------------

        // Brand text shown in the sidebar + navbar. When empty the localized
        // app.title is used. The server sets this from the company_name setting.
        private string FBrand = string.Empty;

        // Optional brand override (company name). When empty the localized
        // app.title is shown in the sidebar + navbar.
        public string Brand
        {
            get { return FBrand; }
            set { FBrand = value; }
        }

        // i18n lookup shortcut (mirrors the Delphi free function T).
        private static string T(string aKey, string aLang)
        {
            return I18n.T(aKey, aLang);
        }

        // ---------------------------------------------------------------------
        // formatting helpers (mirror the Delphi free functions)
        // ---------------------------------------------------------------------

        // Minimal HTML-escape for text spliced into raw markup (device names, dates).
        private static string HtmlEsc(string aValue)
        {
            string vResult = (aValue ?? string.Empty).Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            return vResult;
        }

        private static string FmtDate(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------------
        // private brand / theme helpers
        // ---------------------------------------------------------------------

        // The brand to display: FBrand when non-empty, else T('app.title', aLang).
        private string BrandText(string aLang)
        {
            if (FBrand != null && FBrand.Trim() != "")
                return FBrand;
            return T("app.title", aLang);
        }

        // The prefers-color-scheme autodetect <script> injected (in <head>) when
        // the active theme is 'system'.
        private string SystemThemeAutodetectScript()
        {
            return "<script>(function(){if(window.matchMedia&&window.matchMedia(" +
                "'(prefers-color-scheme: dark)').matches){document.documentElement." +
                "setAttribute('data-bs-theme','dark');}})();</script>";
        }

        // Wraps an already-built body in a Bootstrap template, applying the theme
        // (data-bs-theme for light/dark, system-autodetect script for 'system')
        // and the page language.
        private string WrapTemplate(string aTitle, string aBody, string aTheme, string aLang)
        {
            // Shared base CSS (theme controller) + navbar stacking, then the
            // eSeGeCe brand theme rules.
            string vSharedCSS = TsgcHTMLThemeController.GetSharedCSS() + CS_NAVBAR_STACKING_RULES;

            var oTpl = new TsgcHTMLTemplate_Bootstrap();
            oTpl.Title = aTitle;
            oTpl.HtmlLang = aLang;
            oTpl.Viewport = "width=device-width, initial-scale=1";
            oTpl.HeadNodes.AddRaw("<link rel=\"icon\" type=\"image/svg+xml\" " +
                "href=\"/favicon.svg\">");
            if (string.Equals(aTheme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "dark";
                oTpl.DarkMode = true;
                oTpl.CustomCSS = vSharedCSS + CS_THEME_DARK_RULES;
            }
            else if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "light";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = vSharedCSS + CS_THEME_LIGHT_RULES;
            }
            else
            {
                // System theme: no data-bs-theme attribute; the autodetect script
                // in <head> flips it client-side from prefers-color-scheme. Both
                // rule sets are wrapped in prefers-color-scheme media queries.
                oTpl.HtmlTheme = string.Empty;
                oTpl.DarkMode = false;
                oTpl.CustomCSS = vSharedCSS + "@media (prefers-color-scheme: light){" +
                    CS_THEME_LIGHT_RULES + "}" + "@media (prefers-color-scheme: dark){" +
                    CS_THEME_DARK_RULES + "}";
                oTpl.HeadNodes.AddRaw(SystemThemeAutodetectScript());
            }

            oTpl.BodyContent = aBody;
            return oTpl.GetHTML();
        }

        // ---------------------------------------------------------------------
        // sidebar / navbar / dropdowns
        // ---------------------------------------------------------------------

        // Sidebar nav (Container/Link). Active menu highlighted. Admin items only
        // when aRole='admin'. Labels via T('nav.*', aLang).
        private string BuildSidebar(string aActiveMenu, string aRole, string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oNav = new TsgcHTMLContainer("div");
            oNav.Attributes = "class=\"bg-dark p-3 d-flex flex-column\" " +
                "style=\"min-height:100%;min-width:220px;\"";

            var oBrand = new TsgcHTMLContainer("a");
            oBrand.Attributes = "href=\"/\" " +
                "class=\"d-flex align-items-center mb-3 text-white text-decoration-none\"";
            oBrand.AddRaw(CS_ESEGECE_LOGO_SVG);
            var oStrong = new TsgcHTMLContainer("strong");
            oStrong.AddText(BrandText(aLang));
            oBrand.Add(oStrong);
            oNav.Add(oBrand);
            oNav.AddRaw("<hr class=\"text-white\">");

            var oUl = new TsgcHTMLContainer("ul");
            oUl.CSSClass = "nav nav-pills flex-column mb-auto";

            Action<string, string, string> addNavLink = (aHref, aKey, aMenu) =>
            {
                string vClass = "nav-link text-white";
                if (string.Equals(aActiveMenu, aMenu, StringComparison.OrdinalIgnoreCase))
                    vClass = vClass + " active";
                var oLi = new TsgcHTMLContainer("li");
                oLi.CSSClass = "nav-item";
                var oLink = new TsgcHTMLLink(aHref, T(aKey, aLang));
                oLink.CSSClass = vClass;
                oLi.Add(oLink);
                oUl.Add(oLi);
            };

            addNavLink("/", "nav.dashboard", "dashboard");
            if (string.Equals(aRole, "admin", StringComparison.OrdinalIgnoreCase))
            {
                addNavLink("/admin", "nav.admin", "admin");
                // Users management is admin-only, shown right under the Admin item.
                addNavLink("/users", "nav.users", "users");
            }
            addNavLink("/security", "nav.security", "security");

            oNav.Add(oUl);
            oRoot.Add(oNav);
            return oRoot.HTML;
        }

        // Theme switcher dropdown (Light/Dark/System -> POST /theme).
        private string BuildThemeDropdown(string aTheme, string aLang)
        {
            string vCurKey;
            if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
                vCurKey = "theme.light";
            else if (string.Equals(aTheme, "dark", StringComparison.OrdinalIgnoreCase))
                vCurKey = "theme.dark";
            else
                vCurKey = "theme.system";

            var oRoot = new TsgcHTMLNodeList();

            var oLi = new TsgcHTMLContainer("li");
            oLi.CSSClass = "nav-item dropdown ms-2";

            var oToggle = new TsgcHTMLContainer("a");
            oToggle.Attributes = "class=\"nav-link dropdown-toggle\" href=\"#\" " +
                "id=\"merpThemeDropdown\" role=\"button\" data-bs-toggle=\"dropdown\" " +
                "aria-expanded=\"false\" aria-label=\"Theme: " + T(vCurKey, aLang) + "\"";
            oToggle.AddText(T(vCurKey, aLang));
            oLi.Add(oToggle);

            var oUl = new TsgcHTMLContainer("ul");
            oUl.Attributes = "class=\"dropdown-menu dropdown-menu-end\" " +
                "aria-labelledby=\"merpThemeDropdown\"";

            Action<string, string> addThemeItem = (aValue, aKey) =>
            {
                var oItemLi = new TsgcHTMLContainer("li");
                var oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/theme";
                oForm.CSSClass = "m-0";
                oForm.AddHidden("theme", aValue);
                var oBtn = new TsgcHTMLContainer("button");
                oBtn.Attributes = "type=\"submit\" class=\"dropdown-item\"";
                var oSpan = new TsgcHTMLContainer("span");
                oSpan.AddText(T(aKey, aLang));
                oBtn.Add(oSpan);
                if (string.Equals(aTheme, aValue, StringComparison.OrdinalIgnoreCase))
                    oBtn.AddRaw(" <span class=\"ms-2\">&#10004;</span>");
                oForm.Add(oBtn);
                oItemLi.Add(oForm);
                oUl.Add(oItemLi);
            };

            addThemeItem("light", "theme.light");
            addThemeItem("dark", "theme.dark");
            addThemeItem("system", "theme.system");
            oLi.Add(oUl);

            oRoot.Add(oLi);
            return oRoot.HTML;
        }

        // Language switcher dropdown (EN/ES/DE/FR/... -> POST /lang).
        private string BuildLanguageDropdown(string aLang)
        {
            string vCur;
            if (string.Equals(aLang, "es", StringComparison.OrdinalIgnoreCase))
                vCur = "ES";
            else if (string.Equals(aLang, "de", StringComparison.OrdinalIgnoreCase))
                vCur = "DE";
            else if (string.Equals(aLang, "fr", StringComparison.OrdinalIgnoreCase))
                vCur = "FR";
            else if (string.Equals(aLang, "it", StringComparison.OrdinalIgnoreCase))
                vCur = "IT";
            else if (string.Equals(aLang, "nl", StringComparison.OrdinalIgnoreCase))
                vCur = "NL";
            else if (string.Equals(aLang, "pl", StringComparison.OrdinalIgnoreCase))
                vCur = "PL";
            else if (string.Equals(aLang, "br", StringComparison.OrdinalIgnoreCase))
                vCur = "BR";
            else if (string.Equals(aLang, "tr", StringComparison.OrdinalIgnoreCase))
                vCur = "TR";
            else if (string.Equals(aLang, "zh", StringComparison.OrdinalIgnoreCase))
                vCur = "ZH";
            else if (string.Equals(aLang, "ja", StringComparison.OrdinalIgnoreCase))
                vCur = "JA";
            else if (string.Equals(aLang, "ko", StringComparison.OrdinalIgnoreCase))
                vCur = "KO";
            else
                vCur = "EN";

            var oRoot = new TsgcHTMLNodeList();

            var oLi = new TsgcHTMLContainer("li");
            oLi.CSSClass = "nav-item dropdown ms-2";

            var oToggle = new TsgcHTMLContainer("a");
            oToggle.Attributes = "class=\"nav-link dropdown-toggle\" href=\"#\" " +
                "id=\"merpLangDropdown\" role=\"button\" data-bs-toggle=\"dropdown\" " +
                "aria-expanded=\"false\" aria-label=\"" + T("lang.label", aLang) + "\"";
            oToggle.AddText(vCur);
            oLi.Add(oToggle);

            var oUl = new TsgcHTMLContainer("ul");
            oUl.Attributes = "class=\"dropdown-menu dropdown-menu-end\" " +
                "aria-labelledby=\"merpLangDropdown\"";

            Action<string, string> addLangItem = (aShort, aLabel) =>
            {
                var oItemLi = new TsgcHTMLContainer("li");
                var oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/lang";
                oForm.CSSClass = "m-0";
                oForm.AddHidden("lang", aShort);
                var oBtn = new TsgcHTMLContainer("button");
                oBtn.Attributes = "type=\"submit\" class=\"dropdown-item\"";
                var oSpan = new TsgcHTMLContainer("span");
                oSpan.AddText(aLabel);
                oBtn.Add(oSpan);
                if (string.Equals(aLang, aShort, StringComparison.OrdinalIgnoreCase))
                    oBtn.AddRaw(" <span class=\"ms-2\">&#10004;</span>");
                oForm.Add(oBtn);
                oItemLi.Add(oForm);
                oUl.Add(oItemLi);
            };

            addLangItem("en", "English");
            addLangItem("es", "Español");
            addLangItem("de", "Deutsch");
            addLangItem("fr", "Français");
            addLangItem("it", "Italiano");
            addLangItem("nl", "Nederlands");
            addLangItem("pl", "Polski");
            addLangItem("br", "Português");
            addLangItem("tr", "Türkçe");
            addLangItem("zh", "中文");
            addLangItem("ja", "日本語");
            addLangItem("ko", "한국어");
            oLi.Add(oUl);

            oRoot.Add(oLi);
            return oRoot.HTML;
        }

        // Top navbar: brand + theme switcher + language switcher + user name +
        // logout. Theme/Language menu items are POST forms (/theme, /lang).
        private string BuildNavBar(string aDisplayName, string aTheme, string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oNav = new TsgcHTMLContainer("nav");
            oNav.CSSClass = "navbar navbar-expand-lg navbar-dark bg-dark mb-0";
            var oContainer = new TsgcHTMLContainer("div");
            oContainer.CSSClass = "container-fluid";

            var oBrand = new TsgcHTMLContainer("a");
            oBrand.Attributes = "class=\"navbar-brand d-flex align-items-center\" href=\"/\"";
            oBrand.AddRaw(CS_ESEGECE_LOGO_SVG);
            var oBrandText = new TsgcHTMLContainer("span");
            oBrandText.AddText(BrandText(aLang));
            oBrand.Add(oBrandText);
            oContainer.Add(oBrand);

            var oRightUl = new TsgcHTMLContainer("ul");
            oRightUl.CSSClass = "navbar-nav ms-auto mb-2 mb-lg-0 align-items-center";

            // Signed-in user label.
            var oUserLi = new TsgcHTMLContainer("li");
            oUserLi.CSSClass = "nav-item";
            var oUserLink = new TsgcHTMLContainer("span");
            oUserLink.CSSClass = "nav-link text-light fw-medium";
            oUserLink.AddText(aDisplayName);
            oUserLi.Add(oUserLink);
            oRightUl.Add(oUserLi);

            oRightUl.AddRaw(BuildThemeDropdown(aTheme, aLang));
            oRightUl.AddRaw(BuildLanguageDropdown(aLang));

            // Logout.
            var oLogoutLi = new TsgcHTMLContainer("li");
            oLogoutLi.CSSClass = "nav-item ms-2";
            var oLogoutForm = new TsgcHTMLForm();
            oLogoutForm.Method = "POST";
            oLogoutForm.Action = "/logout";
            oLogoutForm.CSSClass = "d-flex m-0";
            var oLogoutBtn = new TsgcHTMLContainer("button");
            oLogoutBtn.Attributes = "type=\"submit\" class=\"btn btn-outline-light btn-sm\"";
            oLogoutBtn.AddText(T("nav.logout", aLang));
            oLogoutForm.Add(oLogoutBtn);
            oLogoutLi.Add(oLogoutForm);
            oRightUl.Add(oLogoutLi);

            oContainer.Add(oRightUl);
            oNav.Add(oContainer);
            oRoot.Add(oNav);
            return oRoot.HTML;
        }

        // Page footer: eSeGeCe brand mark + a link to the sgcHTML product page.
        private string BuildFooter(string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "border-top mt-4 py-4 text-center text-muted small";

            // Brand row: eSeGeCe mark + the Live Monitor brand.
            var oInner = new TsgcHTMLContainer("div");
            oInner.CSSClass =
                "d-inline-flex align-items-center justify-content-center mb-2 fw-semibold";
            oInner.AddRaw(CS_ESEGECE_LOGO_SVG);
            oInner.AddText(BrandText(aLang));
            oFooter.Add(oInner);

            // "Built with sgcHTML components for Delphi and C++Builder."
            var oP = new TsgcHTMLContainer("p");
            oP.CSSClass = "mb-1";
            oP.AddText(T("footer.builtwith", aLang) + " ");
            var oLink = new TsgcHTMLLink("https://www.esegece.com/products/sgchtml/", "sgcHTML");
            oLink.Target = "_blank";
            oLink.Rel = "noopener";
            oLink.CSSClass = "fw-semibold text-decoration-none";
            oP.Add(oLink);
            oP.AddText(" " + T("footer.suffix", aLang));
            oFooter.Add(oP);

            // Copyright. The (c) symbol is emitted as the &copy; entity so the
            // source stays ASCII and the UTF-8 response is never given a raw high byte.
            var oCopy = new TsgcHTMLContainer("p");
            oCopy.CSSClass = "mb-0";
            oCopy.AddRaw("&copy; 2026 ");
            var oEseLink = new TsgcHTMLLink("https://www.esegece.com", "eSeGeCe");
            oEseLink.Target = "_blank";
            oEseLink.Rel = "noopener";
            oEseLink.CSSClass = "text-muted text-decoration-none";
            oCopy.Add(oEseLink);
            oFooter.Add(oCopy);

            oRoot.Add(oFooter);
            return oRoot.HTML;
        }

        // ---------------------------------------------------------------------
        // shared shell
        // ---------------------------------------------------------------------

        // Shared shell: wraps a page body in a themed/localized Bootstrap template
        // with a sidebar nav + a navbar (theme + language switchers, user + logout).
        public string BuildPageShell(string aTitle, string aBodyHTML, string aActiveMenu,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            var oBody = new TsgcHTMLNodeList();
            oBody.AddRaw(BuildNavBar(aDisplayName, aTheme, aLang));

            // container-fluid > row > col-auto[sidebar] + col[content].
            var oGrid = new TsgcHTMLContainer("div");
            oGrid.CSSClass = "container-fluid";
            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row";
            var oColSide = new TsgcHTMLContainer("div");
            oColSide.CSSClass = "col-auto p-0";
            oColSide.AddRaw(BuildSidebar(aActiveMenu, aRole, aLang));
            oRow.Add(oColSide);
            var oColMain = new TsgcHTMLContainer("div");
            oColMain.CSSClass = "col p-3 p-md-4";
            oColMain.AddRaw(aBodyHTML);
            oRow.Add(oColMain);
            oGrid.Add(oRow);
            oBody.Add(oGrid);
            oBody.AddRaw(BuildFooter(aLang));

            string vBody = oBody.HTML;
            return WrapTemplate(T("app.title", aLang) + " - " + aTitle, vBody, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // login page
        // ---------------------------------------------------------------------

        // Centered login card with username + password fields and a submit button.
        // Standalone (no sidebar) but themed + localized + with a small language
        // switcher. When aError <> '' a danger alert is shown above the form.
        public string BuildLoginPage(string aTheme, string aLang, string aError = "")
        {
            var oRoot = new TsgcHTMLNodeList();

            var oContainer = new TsgcHTMLContainer("div");
            oContainer.CSSClass = "container py-5";
            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row justify-content-center";
            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-md-6 col-lg-4";

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body p-4";

            oCard.Body.Add(new TsgcHTMLRaw("<div class=\"text-center mb-3\" " +
                "style=\"font-size:2.5rem;line-height:1\">" + CS_ESEGECE_LOGO_SVG +
                "</div>"));
            var oHeading = new TsgcHTMLHeading(T("app.title", aLang), 1);
            oHeading.CSSClass = "card-title h3 mb-4 text-center";
            oCard.Body.Add(oHeading);

            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oCard.Body.Add(oAlert);
            }

            var oForm = new TsgcHTMLForm();
            oForm.Method = "post";
            oForm.Action = "/login";

            var oUser = new TsgcHTMLField(TsgcHTMLInputType.itText, "username");
            oUser.FieldID = "username";
            oUser.Label_ = T("login.username", aLang);
            oUser.Placeholder = T("login.username", aLang);
            oUser.Autocomplete = "username";
            oUser.Required = true;
            oUser.ColClass = "mb-3";
            oForm.Add(oUser);

            var oPwd = new TsgcHTMLField(TsgcHTMLInputType.itPassword, "password");
            oPwd.FieldID = "password";
            oPwd.Label_ = T("login.password", aLang);
            oPwd.Placeholder = T("login.password", aLang);
            oPwd.Autocomplete = "current-password";
            oPwd.Required = true;
            oPwd.ColClass = "mb-3";
            oForm.Add(oPwd);

            var oSubmit = new TsgcHTMLButton(T("login.signin", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oSubmit.ButtonType = "submit";
            oSubmit.CSSClass = "w-100";
            oForm.Add(oSubmit);

            oCard.Body.Add(oForm);

            // --- Sign in with a passkey --- //
            oCard.Body.Add(new TsgcHTMLRaw("<hr class=\"my-3\">"));

            var oPasskeyBtn = new TsgcHTMLButton(T("login.passkey", aLang),
                TsgcHTMLButtonStyle.bsOutlinePrimary);
            oPasskeyBtn.ButtonType = "button";
            oPasskeyBtn.CSSClass = "w-100";
            oPasskeyBtn.Attributes = "id=\"passkey-login-btn\"";
            oCard.Body.Add(oPasskeyBtn);

            var oPasskeyStatus = new TsgcHTMLContainer("div");
            oPasskeyStatus.ID = "passkey-status";
            oPasskeyStatus.CSSClass = "text-muted small mt-2 text-center";
            oCard.Body.Add(oPasskeyStatus);

            // Small language switcher under the card (standalone login has no navbar).
            oCard.Body.Add(new TsgcHTMLRaw("<hr class=\"my-3\">"));
            var oLangRow = new TsgcHTMLContainer("ul");
            oLangRow.CSSClass = "nav justify-content-center";
            oLangRow.AddRaw(BuildLanguageDropdown(aLang));
            oCard.Body.Add(oLangRow);

            // Login-with-passkey JS (full <script> block from a trusted helper).
            var oScript = new TsgcHTMLScript();
            oScript.Code = PasskeyLoginScript();
            oCard.Body.Add(oScript);

            oCol.Add(oCard);
            oRow.Add(oCol);
            oContainer.Add(oRow);
            oRoot.Add(oContainer);
            oRoot.AddRaw(BuildFooter(aLang));

            string vBody = oRoot.HTML;
            return WrapTemplate(T("app.title", aLang) + " - " + T("login.title", aLang),
                vBody, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // dashboard
        // ---------------------------------------------------------------------

        // Live monitoring dashboard: KPI stat cards (CPU/Memory/WS connections/
        // Requests-per-sec), a live Chart.js line chart, and a streaming events
        // table. Values update in real time via htmx out-of-band fragments pushed
        // over the WebSocket by the server; this only renders the initial shell +
        // the client wiring. aConnections is the current live WS connection count.
        //
        // Client realtime stack (CDN only — the managed lib does not embed the
        // eSeGeCe client JS): htmx + the htmx websocket extension open a WebSocket
        // to the server (hx-ext="ws" ws-connect) and apply incoming hx-swap-oob
        // fragments to the KPI / chart-carrier / events-table element ids.
        public string BuildMonitorDashboardPage(string aDisplayName, string aRole,
            string aTheme, string aLang, int aConnections)
        {
            var oRoot = new TsgcHTMLNodeList();

            // --- heading + live badge --- //
            var oHeading = new TsgcHTMLHeading(T("nav.dashboard", aLang), 1);
            oHeading.CSSClass = "mb-1";
            oRoot.Add(oHeading);
            oRoot.AddRaw("<p class=\"text-muted mb-4\">" +
                HtmlEsc(T("monitor.subtitle", aLang)) +
                " <span class=\"badge bg-success ms-2\"><span id=\"monitor-live-dot\">" +
                HtmlEsc(T("monitor.status.live", aLang)) + "</span></span></p>");

            // The WebSocket container: htmx-ext-ws opens the WS to the same host/
            // port at the root path and applies the server's hx-swap-oob fragments.
            // ws-connect uses a relative URL so the browser resolves the ws:// (or
            // wss://) scheme + host + port from the page automatically.
            var oWsHost = new TsgcHTMLContainer("div");
            oWsHost.Attributes = "hx-ext=\"ws\" ws-connect=\"/\"";

            // --- KPI stat cards row (OOB targets) --- //
            var oKpiRow = new TsgcHTMLContainer("div");
            oKpiRow.CSSClass = "row g-3 mb-4";

            // One live KPI card. aId is the OOB target id whose innerHTML the server
            // swaps every push. aAccent/aTint are the brand colors for the accent.
            Action<string, string, string, string, string> addKpiCard =
                (aId, aLabelKey, aInitial, aAccent, aTint) =>
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-6 col-lg-3";

                var oKpi = new TsgcHTMLCard();
                oKpi.CSSClass = "h-100 shadow-sm";
                oKpi.BodyClass = "card-body";

                var oFlex = new TsgcHTMLContainer("div");
                oFlex.CSSClass = "d-flex align-items-center justify-content-between";

                var oText = new TsgcHTMLContainer("div");
                // The OOB target: server pushes the new value into this h2's innerHTML.
                var oNum = new TsgcHTMLContainer("h2");
                oNum.ID = aId;
                oNum.CSSClass = "fw-bold mb-0";
                oNum.AddText(aInitial);
                oText.Add(oNum);
                var oLbl = new TsgcHTMLParagraph(T(aLabelKey, aLang));
                oLbl.CSSClass = "text-muted mb-0 small text-uppercase fw-semibold";
                oText.Add(oLbl);
                oFlex.Add(oText);

                var oDot = new TsgcHTMLContainer("div");
                oDot.CSSClass = "rounded-circle";
                oDot.Style = "width:44px;height:44px;flex:0 0 44px;background:" + aTint + ";";
                oFlex.Add(oDot);

                oKpi.Body.Add(oFlex);

                oCol.AddRaw("<div style=\"border-left:4px solid " + aAccent +
                    ";border-radius:12px;\">");
                oCol.Add(oKpi);
                oCol.AddRaw("</div>");
                oKpiRow.Add(oCol);
            };

            addKpiCard("kpi-cpu", "monitor.kpi.cpu", "0.0 %", "#0057B8", "#E8F1FB");
            addKpiCard("kpi-mem", "monitor.kpi.memory", "0.0 %", "#0EA5A5", "#E3F6F5");
            addKpiCard("kpi-conn", "monitor.kpi.connections",
                aConnections.ToString(CultureInfo.InvariantCulture), "#7C3AED", "#F1EAFD");
            addKpiCard("kpi-rps", "monitor.kpi.rps", "0", "#F0A400", "#FFF8E5");
            oWsHost.Add(oKpiRow);

            // --- live line chart (Chart.js into a canvas) --- //
            var oChartCard = new TsgcHTMLCard();
            oChartCard.CSSClass = "shadow-sm mb-4";
            oChartCard.BodyClass = "card-body";
            oChartCard.Body.Add(new TsgcHTMLHeading(T("monitor.chart.title", aLang), 5));
            // Hidden value carrier: server OOB-swaps the latest CPU value into this
            // span; the client script reads it on a timer and appends a chart point.
            oChartCard.Body.AddRaw("<span id=\"metric-cpu-val\" class=\"d-none\">0</span>");
            var oCanvasWrap = new TsgcHTMLContainer("div");
            oCanvasWrap.Style = "height:260px;";
            oCanvasWrap.AddRaw("<canvas id=\"cpuChart\"></canvas>");
            oChartCard.Body.Add(oCanvasWrap);
            oWsHost.Add(oChartCard);

            // --- live events table --- //
            var oEventsCard = new TsgcHTMLCard();
            oEventsCard.CSSClass = "shadow-sm";
            oEventsCard.BodyClass = "card-body";
            oEventsCard.Body.Add(new TsgcHTMLHeading(T("monitor.events.title", aLang), 5));
            var oTableWrap = new TsgcHTMLContainer("div");
            oTableWrap.CSSClass = "table-responsive";
            // The tbody#events-body is the OOB target (afterbegin swap prepends rows).
            oTableWrap.AddRaw("<table class=\"table table-sm table-hover align-middle " +
                "mb-0\"><thead class=\"table-light\"><tr><th>" +
                HtmlEsc(T("monitor.events.time", aLang)) + "</th><th>" +
                HtmlEsc(T("monitor.events.level", aLang)) + "</th><th>" +
                HtmlEsc(T("monitor.events.message", aLang)) +
                "</th></tr></thead><tbody id=\"events-body\"><tr id=\"events-empty\"><td " +
                "colspan=\"3\" class=\"text-muted text-center py-3\">" +
                HtmlEsc(T("monitor.events.waiting", aLang)) +
                "</td></tr></tbody></table>");
            oEventsCard.Body.Add(oTableWrap);
            oWsHost.Add(oEventsCard);

            oRoot.Add(oWsHost);

            // --- client wiring: LOCAL scripts (chart + htmx + htmx websocket ext) --- //
            // Served self-hosting from the library via TsgcHTMXResponseRegistry (no CDN).
            // The htmx websocket extension on the oWsHost container opens the WS and
            // applies the server's hx-swap-oob fragments; the Chart.js script polls
            // the OOB-swapped #metric-cpu-val carrier and appends a live data point.
            oRoot.AddRaw("<script src=\"/chart.umd.min.js\"></script>");
            oRoot.AddRaw("<script src=\"/htmx.min.js\"></script>");
            oRoot.AddRaw("<script src=\"/htmx-ext-ws.min.js\"></script>");

            var oScript = new TsgcHTMLScript();
            oScript.Code = "document.addEventListener(\"DOMContentLoaded\",function(){" +
                "var ctx=document.getElementById(\"cpuChart\");var chart=null;" +
                "if(ctx&&window.Chart){chart=new Chart(ctx,{type:\"line\",data:{labels:[]," +
                "datasets:[{label:\"CPU %\",data:[],borderColor:\"#0057B8\"," +
                "backgroundColor:\"rgba(0,87,184,.12)\",fill:true,tension:.35," +
                "pointRadius:0,borderWidth:2}]},options:{animation:false,responsive:true," +
                "maintainAspectRatio:false,scales:{y:{min:0,max:100}}," +
                "plugins:{legend:{display:false}}}});}" + "var last=\"\";" +
                "setInterval(function(){" +
                "var el=document.getElementById(\"metric-cpu-val\");if(!el)return;" +
                "var v=el.textContent;if(v===last)return;last=v;" +
                "var n=parseFloat(v);if(isNaN(n))return;" +
                "if(chart){var t=new Date().toLocaleTimeString();" +
                "chart.data.labels.push(t);chart.data.datasets[0].data.push(n);" +
                "if(chart.data.labels.length>40){chart.data.labels.shift();" +
                "chart.data.datasets[0].data.shift();}chart.update();}" +
                "var tb=document.getElementById(\"events-body\");" +
                "if(tb){var rows=tb.querySelectorAll(\"tr\");" +
                "if(rows.length>30){for(var i=30;i<rows.length;i++){" +
                "rows[i].parentNode.removeChild(rows[i]);}}}" + "},1000);});";
            oRoot.Add(oScript);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("nav.dashboard", aLang), vBody, "dashboard",
                aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // security / passkeys
        // ---------------------------------------------------------------------

        // Logged-in "Security / Passkeys" page (inside shell): a table of registered
        // passkeys (with per-row delete form), an "Add a passkey" button + device-
        // name input, and the register JS.
        public string BuildSecurityPage(string aDisplayName, string aRole, string aTheme,
            string aLang, TERPPasskey[] aPasskeys)
        {
            var oRoot = new TsgcHTMLNodeList();
            var oHeading = new TsgcHTMLHeading(T("security.title", aLang), 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            // --- Registered passkeys table --- //
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4";
            oCard.BodyClass = "card-body";
            oCard.Body.Add(new TsgcHTMLHeading(T("security.your_passkeys", aLang), 5));

            if (aPasskeys.Length == 0)
                oCard.Body.AddRaw("<p class=\"text-muted mb-0\">" +
                    HtmlEsc(T("security.none", aLang)) + "</p>");
            else
            {
                var oTable = new TsgcHTMLContainer("table");
                oTable.CSSClass = "table table-sm align-middle mb-0";
                oTable.AddRaw("<thead><tr><th>" + HtmlEsc(T("security.device", aLang)) +
                    "</th><th>" + HtmlEsc(T("security.created", aLang)) + "</th>" + "<th>" +
                    HtmlEsc(T("security.last_used", aLang)) + "</th><th></th></tr></thead>");
                var oTbody = new TsgcHTMLContainer("tbody");
                for (int vI = 0; vI < aPasskeys.Length; vI++)
                {
                    // Per-row delete form (POST /security/passkey/delete with the row id).
                    string vRowHtml = "<tr><td>" + HtmlEsc(aPasskeys[vI].DeviceName) + "</td>" +
                        "<td>" + HtmlEsc(FmtDate(aPasskeys[vI].CreatedAt)) + "</td>" + "<td>" +
                        HtmlEsc(FmtDate(aPasskeys[vI].LastUsedAt)) + "</td>" +
                        "<td class=\"text-end\">" +
                        "<form method=\"post\" action=\"/security/passkey/delete\" " +
                        "class=\"d-inline\">" + "<input type=\"hidden\" name=\"id\" value=\"" +
                        aPasskeys[vI].Id.ToString(CultureInfo.InvariantCulture) + "\">" +
                        "<button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                        HtmlEsc(T("common.delete", aLang)) + "</button></form></td></tr>";
                    oTbody.AddRaw(vRowHtml);
                }
                oTable.Add(oTbody);
                oCard.Body.Add(oTable);
            }
            oRoot.Add(oCard);

            // --- Add a passkey --- //
            var oAddCard = new TsgcHTMLCard();
            oAddCard.CSSClass = "shadow-sm mb-4";
            oAddCard.BodyClass = "card-body";
            oAddCard.Body.Add(new TsgcHTMLHeading(T("security.add", aLang), 5));

            var oName = new TsgcHTMLField(TsgcHTMLInputType.itText, "device_name");
            oName.FieldID = "passkey-name";
            oName.Label_ = T("security.device_name", aLang);
            oName.Placeholder = T("security.device_name_ph", aLang);
            oName.ColClass = "mb-3";
            oAddCard.Body.Add(oName);

            var oAddBtn = new TsgcHTMLButton(T("security.add", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oAddBtn.ButtonType = "button";
            oAddBtn.Attributes = "id=\"passkey-register-btn\"";
            oAddCard.Body.Add(oAddBtn);

            var oStatus = new TsgcHTMLContainer("div");
            oStatus.ID = "passkey-register-status";
            oStatus.CSSClass = "text-muted small mt-2";
            oAddCard.Body.Add(oStatus);
            oRoot.Add(oAddCard);

            var oLink = new TsgcHTMLLink("/", T("common.back", aLang));
            oRoot.Add(oLink);

            // Register-passkey JS.
            var oScript = new TsgcHTMLScript();
            oScript.Code = PasskeyRegisterScript();
            oRoot.Add(oScript);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("security.title", aLang), vBody, "security",
                aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // users
        // ---------------------------------------------------------------------

        // Render the role as a coloured Bootstrap badge: admin -> primary (brand),
        // user (or anything else) -> secondary. Returns an already-escaped span.
        private static string RoleBadge(string aRole, string aLang)
        {
            string vVariant;
            string vKey;
            if (string.Equals((aRole ?? string.Empty).Trim(), "admin", StringComparison.OrdinalIgnoreCase))
            {
                vVariant = "primary";
                vKey = "user.role_admin";
            }
            else
            {
                vVariant = "secondary";
                vKey = "user.role_user";
            }
            return "<span class=\"badge bg-" + vVariant + "\">" + HtmlEsc(T(vKey, aLang)) + "</span>";
        }

        // Users list (inside shell, admin only).
        public string BuildUsersPage(TERPUser[] aUsers, long aCurrentUserId,
            string aDisplayName, string aRole, string aTheme, string aLang, string aFlash)
        {
            var oRoot = new TsgcHTMLNodeList();

            // Heading + "New user" button on one flex row.
            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "d-flex justify-content-between align-items-center mb-4";
            var oHeading = new TsgcHTMLHeading(T("user.title", aLang), 1);
            oHeading.CSSClass = "mb-0";
            oHeader.Add(oHeading);
            var oNewBtn = new TsgcHTMLButton(T("user.new", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oNewBtn.Href = "/users/new";
            oHeader.Add(oNewBtn);
            oRoot.Add(oHeader);

            // One-shot success alert (e.g. after a save / delete).
            if (aFlash != "")
            {
                var oAlert = new TsgcHTMLAlert(aFlash);
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                oAlert.Dismissible = true;
                oAlert.CSSClass = "mb-4";
                oRoot.Add(oAlert);
            }

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";

            // Users table.
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.AddColumn(T("user.username", aLang));
            oTable.AddColumn(T("user.display_name", aLang));
            oTable.AddColumn(T("user.email", aLang));
            oTable.AddColumn(T("user.role", aLang));
            oTable.AddColumn(T("security.created", aLang));
            oTable.AddColumn(T("common.actions", aLang), "text-end");

            if (aUsers.Length == 0)
                oTable.AddEmptyRow(T("user.none", aLang), 6);
            else
            {
                string vConfirm = T("common.confirm_delete", aLang);
                for (int vI = 0; vI < aUsers.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    // Mark the current user's own row with a "(you)" suffix.
                    string vName = aUsers[vI].Username;
                    if (aUsers[vI].Id == aCurrentUserId)
                        oRow.AddCellRaw(HtmlEsc(vName) +
                            " <span class=\"text-secondary\">(you)</span>");
                    else
                        oRow.AddCellText(vName);
                    oRow.AddCellText(aUsers[vI].DisplayName);
                    oRow.AddCellText(aUsers[vI].Email);
                    oRow.AddCellRaw(RoleBadge(aUsers[vI].Role, aLang));
                    oRow.AddCellText(FmtDate(aUsers[vI].CreatedAt));
                    // Actions: Edit link + per-row delete form (POST, confirm on submit).
                    var oActions = oRow.AddCell();
                    oActions.CellClass = "text-end";
                    oActions.AddRaw("<a href=\"/users/edit?id=" +
                        aUsers[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\" class=\"btn btn-sm btn-outline-secondary me-1\">" +
                        HtmlEsc(T("common.edit", aLang)) + "</a>");
                    oActions.AddRaw("<form method=\"post\" action=\"/users/delete\" " +
                        "class=\"d-inline\" onsubmit=\"return confirm('" +
                        HtmlEsc(vConfirm).Replace("'", "\\'") +
                        "');\"><input type=\"hidden\" name=\"id\" value=\"" +
                        aUsers[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\"><button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                        HtmlEsc(T("common.delete", aLang)) + "</button></form>");
                }
            }

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("user.title", aLang), vBody, "users", aDisplayName,
                aRole, aTheme, aLang);
        }

        // User add/edit form (inside shell, admin only).
        public string BuildUserFormPage(TERPUser aUser, bool aIsNew, string aError,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            string vTitle;
            if (aIsNew)
                vTitle = T("user.new", aLang);
            else
                vTitle = T("user.edit", aLang);

            var oRoot = new TsgcHTMLNodeList();
            var oHeading = new TsgcHTMLHeading(vTitle, 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";

            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oAlert.CSSClass = "mb-3";
                oCard.Body.Add(oAlert);
            }

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/users/save";
            oForm.AddHidden("id", aUser.Id.ToString(CultureInfo.InvariantCulture));

            // 2-column grid (.row) hosting the fields, each in its own col.
            var oRowGrid = new TsgcHTMLContainer("div");
            oRowGrid.CSSClass = "row";
            oForm.Add(oRowGrid);

            var oUsername = new TsgcHTMLField(TsgcHTMLInputType.itText, "username");
            oUsername.FieldID = "user-username";
            oUsername.Label_ = T("user.username", aLang);
            oUsername.Value = aUser.Username;
            oUsername.Required = true;
            oUsername.ColClass = "col-md-6 mb-3";
            oRowGrid.Add(oUsername);

            var oDisplay = new TsgcHTMLField(TsgcHTMLInputType.itText, "display_name");
            oDisplay.FieldID = "user-display_name";
            oDisplay.Label_ = T("user.display_name", aLang);
            oDisplay.Value = aUser.DisplayName;
            oDisplay.ColClass = "col-md-6 mb-3";
            oRowGrid.Add(oDisplay);

            var oEmail = new TsgcHTMLField(TsgcHTMLInputType.itEmail, "email");
            oEmail.FieldID = "user-email";
            oEmail.Label_ = T("user.email", aLang);
            oEmail.Value = aUser.Email;
            oEmail.ColClass = "col-md-6 mb-3";
            oRowGrid.Add(oEmail);

            var oRoleSel = new TsgcHTMLSelect();
            oRoleSel.FieldID = "user-role";
            oRoleSel.Name = "role";
            oRoleSel.Label_ = T("user.role", aLang);
            oRoleSel.ColClass = "col-md-6 mb-3";
            oRoleSel.AddOption("user", T("user.role_user", aLang),
                !string.Equals(aUser.Role, "admin", StringComparison.OrdinalIgnoreCase));
            oRoleSel.AddOption("admin", T("user.role_admin", aLang),
                string.Equals(aUser.Role, "admin", StringComparison.OrdinalIgnoreCase));
            oRowGrid.Add(oRoleSel);

            var oPwd = new TsgcHTMLField(TsgcHTMLInputType.itPassword, "password");
            oPwd.FieldID = "user-password";
            oPwd.Label_ = T("user.password", aLang);
            oPwd.Autocomplete = "new-password";
            // Required only when creating a new user; on edit a blank field keeps
            // the current password.
            oPwd.Required = aIsNew;
            if (aIsNew)
                oPwd.ColClass = "col-md-6 mb-3";
            else
                oPwd.ColClass = "col-md-6 mb-1";
            oRowGrid.Add(oPwd);

            // On edit, add a hint under the password field.
            if (!aIsNew)
            {
                var oHint = new TsgcHTMLFormText(T("user.password_hint", aLang));
                oHint.ColClass = "col-12 mb-3";
                oRowGrid.Add(oHint);
            }

            // Save + Cancel.
            var oButtons = new TsgcHTMLContainer("div");
            oButtons.CSSClass = "d-flex gap-2";
            var oSave = new TsgcHTMLButton(T("common.save", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oSave.ButtonType = "submit";
            oButtons.Add(oSave);
            var oCancel = new TsgcHTMLButton(T("common.cancel", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oCancel.Href = "/users";
            oButtons.Add(oCancel);
            oForm.Add(oButtons);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(vTitle, vBody, "users", aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // passkey scripts
        // ---------------------------------------------------------------------

        // Inline JS for the login-with-passkey button.
        private string PasskeyLoginScript()
        {
            return "(function(){" + "function b64uToBuf(s){if(!s)return new " +
                "ArrayBuffer(0);" + "s=s.replace(/-/g,'+').replace(/_/g,'/');" +
                "while(s.length%4)s+='=';" + "var bin=atob(s);" +
                "var arr=new Uint8Array(bin.length);" +
                "for(var i=0;i<bin.length;i++)arr[i]=bin.charCodeAt(i);" +
                "return arr.buffer;}" + "function bufToB64u(b){" +
                "var bytes=new Uint8Array(b);var s='';" +
                "for(var i=0;i<bytes.byteLength;i++)s+=String.fromCharCode(bytes[i]);" +
                "return btoa(s).replace(/\\+/g,'-').replace(/\\//g,'_')" +
                ".replace(/=+$/,'');}" +
                "var btn=document.getElementById('passkey-login-btn');" +
                "var status=document.getElementById('passkey-status');" +
                "if(!btn)return;" + "btn.addEventListener('click',async function(){" +
                "try{status.textContent='Requesting passkey...';" + "btn.disabled=true;" +
                "var r=await fetch('/passkey/login/options',{" +
                "method:'POST',headers:{'Content-Type':'application/json'," +
                "'Accept':'application/json'},body:'{}'," +
                "credentials:'same-origin'});" +
                "if(!r.ok){var je=await r.json().catch(function(){return {};});" +
                "throw new Error(je.error||('options '+r.status));}" +
                "var opts=await r.json();" + "opts.challenge=b64uToBuf(opts.challenge);" +
                "if(opts.allowCredentials){opts.allowCredentials.forEach(" +
                "function(c){c.id=b64uToBuf(c.id);});}" +
                "status.textContent='Touch your authenticator...';" +
                "var assertion=await navigator.credentials.get({publicKey:opts," +
                "mediation:'optional'});" +
                "var payload={id:assertion.id,rawId:bufToB64u(assertion.rawId)," +
                "type:assertion.type,response:{" +
                "clientDataJSON:bufToB64u(assertion.response.clientDataJSON)," +
                "authenticatorData:bufToB64u(assertion.response.authenticatorData)," +
                "signature:bufToB64u(assertion.response.signature)," +
                "userHandle:assertion.response.userHandle?" +
                "bufToB64u(assertion.response.userHandle):null}};" +
                "var v=await fetch('/passkey/login/verify',{" +
                "method:'POST',headers:{'Content-Type':'application/json'," +
                "'Accept':'application/json'}," +
                "body:JSON.stringify(payload),credentials:'same-origin'});" +
                "var vj=await v.json().catch(function(){return {};});" +
                "if(v.ok&&vj.ok){window.location=vj.redirect||'/';}" +
                "else{throw new Error(vj.error||('verify '+v.status));}" + "}catch(e){" +
                "status.textContent='Error: '+(e&&e.message?e.message:String(e));" +
                "btn.disabled=false;}" + "});" + "})();";
        }

        // Inline JS for the register-passkey button.
        private string PasskeyRegisterScript()
        {
            return "(function(){" + "function b64uToBuf(s){" +
                "if(!s)return new ArrayBuffer(0);" +
                "s=s.replace(/-/g,'+').replace(/_/g,'/');" +
                "while(s.length%4)s+='=';" + "var bin=atob(s);" +
                "var arr=new Uint8Array(bin.length);" +
                "for(var i=0;i<bin.length;i++)arr[i]=bin.charCodeAt(i);" +
                "return arr.buffer;}" + "function bufToB64u(b){" +
                "var bytes=new Uint8Array(b);var s='';" +
                "for(var i=0;i<bytes.byteLength;i++)s+=String.fromCharCode(bytes[i]);" +
                "return btoa(s).replace(/\\+/g,'-').replace(/\\//g,'_')" +
                ".replace(/=+$/,'');}" +
                "var btn=document.getElementById('passkey-register-btn');" +
                "var status=document.getElementById('passkey-register-status');" +
                "if(!btn)return;" + "btn.addEventListener('click',async function(){" +
                "try{" + "status.textContent='Requesting passkey...';" +
                "btn.disabled=true;" +
                "var nameInput=document.getElementById('passkey-name');" +
                "var name=nameInput?nameInput.value:'';" +
                "var qs=name?('?name='+encodeURIComponent(name)):'';" +
                "var r=await fetch('/passkey/register/options'+qs,{" +
                "method:'POST',headers:{'Content-Type':'application/json'," +
                "'Accept':'application/json'},body:'{}'," +
                "credentials:'same-origin'});" +
                "if(!r.ok){var je=await r.json().catch(function(){return {};});" +
                "throw new Error(je.error||('options '+r.status));}" +
                "var opts=await r.json();" + "opts.challenge=b64uToBuf(opts.challenge);" +
                "opts.user.id=b64uToBuf(opts.user.id);" +
                "if(opts.excludeCredentials){opts.excludeCredentials.forEach(" +
                "function(c){c.id=b64uToBuf(c.id);});}" +
                "status.textContent='Touch your authenticator...';" +
                "var cred=await navigator.credentials.create({publicKey:opts});" +
                "var attestation={id:cred.id,rawId:bufToB64u(cred.rawId)," +
                "type:cred.type,response:{" +
                "clientDataJSON:bufToB64u(cred.response.clientDataJSON)," +
                "attestationObject:bufToB64u(cred.response.attestationObject)}," +
                "clientExtensionResults:cred.getClientExtensionResults?" +
                "cred.getClientExtensionResults():{}};" +
                "if(cred.response.getTransports)attestation.response.transports=" +
                "cred.response.getTransports();" +
                "var v=await fetch('/passkey/register/verify'+qs,{" +
                "method:'POST',headers:{'Content-Type':'application/json'," +
                "'Accept':'application/json'}," +
                "body:JSON.stringify(attestation),credentials:'same-origin'});" +
                "var vj=await v.json().catch(function(){return {};});" +
                "if(v.ok&&vj.ok){status.textContent='Passkey registered.';" +
                "window.location.reload();}" +
                "else{throw new Error(vj.error||('verify '+v.status));}" + "}catch(e){" +
                "status.textContent='Error: '+(e&&e.message?e.message:String(e));" +
                "btn.disabled=false;}" + "});" + "})();";
        }

        // ---------------------------------------------------------------------
        // admin
        // ---------------------------------------------------------------------

        // Admin sub-nav tabs (Settings / Firewall / Audit log). aActive is
        // 'settings' | 'firewall' | 'audit'.
        private static string AdminSubNav(string aActive, string aLang)
        {
            Func<string, string, string, string> tab = (aHref, aKey, aId) =>
            {
                string vClass = "nav-link";
                if (string.Equals(aActive, aId, StringComparison.OrdinalIgnoreCase))
                    vClass = vClass + " active";
                return "<li class=\"nav-item\"><a class=\"" + vClass + "\" href=\"" + aHref +
                    "\">" + HtmlEsc(T(aKey, aLang)) + "</a></li>";
            };

            return "<ul class=\"nav nav-tabs mb-4\">" + tab("/admin", "admin.settings", "settings") +
                tab("/admin/firewall", "admin.firewall", "firewall") +
                tab("/admin/audit", "admin.audit", "audit") + "</ul>";
        }

        // Admin -> Settings page (inside shell, admin only).
        public string BuildAdminSettingsPage(KeyValuePair<string, string>[] aSettings,
            KeyValuePair<string, string>[] aServerInfo, string aDisplayName, string aRole,
            string aTheme, string aLang, string aFlash)
        {
            var oRoot = new TsgcHTMLNodeList();
            var oHeading = new TsgcHTMLHeading(T("admin.title", aLang), 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            oRoot.AddRaw(AdminSubNav("settings", aLang));

            if (aFlash != "")
            {
                var oAlert = new TsgcHTMLAlert(aFlash);
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                oAlert.Dismissible = true;
                oAlert.CSSClass = "mb-4";
                oRoot.Add(oAlert);
            }

            // --- editable settings form --- //
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4";
            oCard.BodyClass = "card-body";
            oCard.Body.Add(new TsgcHTMLHeading(T("admin.settings", aLang), 5));

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/admin/settings";

            var oRowGrid = new TsgcHTMLContainer("div");
            oRowGrid.CSSClass = "row";
            oForm.Add(oRowGrid);

            // The localized label for a setting key, plus its input type (number
            // for the numeric settings, text otherwise).
            Action<string, string> addSettingField = (aName, aValue) =>
            {
                TsgcHTMLInputType vType;
                if ((aName == "default_tax_rate") || (aName == "session_timeout_hours"))
                    vType = TsgcHTMLInputType.itNumber;
                else
                    vType = TsgcHTMLInputType.itText;
                var oField = new TsgcHTMLField(vType, aName);
                oField.FieldID = "setting-" + aName;
                oField.Label_ = T("admin.setting." + aName, aLang);
                oField.Value = aValue;
                oField.ColClass = "col-md-6 mb-3";
                oRowGrid.Add(oField);
            };

            for (int vI = 0; vI < aSettings.Length; vI++)
                addSettingField(aSettings[vI].Key, aSettings[vI].Value);

            var oSave = new TsgcHTMLButton(T("common.save", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oSave.ButtonType = "submit";
            oForm.Add(oSave);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            // --- read-only server info --- //
            var oInfoCard = new TsgcHTMLCard();
            oInfoCard.CSSClass = "shadow-sm";
            oInfoCard.BodyClass = "card-body";
            oInfoCard.Body.Add(new TsgcHTMLHeading(T("admin.server_info", aLang), 5));

            var oTable = new TsgcHTMLContainer("table");
            oTable.CSSClass = "table table-sm align-middle mb-0";
            var oTbody = new TsgcHTMLContainer("tbody");
            for (int vI = 0; vI < aServerInfo.Length; vI++)
                oTbody.AddRaw("<tr><th class=\"text-nowrap\" style=\"width:30%;\">" +
                    HtmlEsc(T("admin.info." + aServerInfo[vI].Key, aLang)) + "</th><td>" +
                    HtmlEsc(aServerInfo[vI].Value) + "</td></tr>");
            oTable.Add(oTbody);
            oInfoCard.Body.Add(oTable);
            oRoot.Add(oInfoCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("admin.title", aLang), vBody, "admin",
                aDisplayName, aRole, aTheme, aLang);
        }

        // Admin -> Firewall page (inside shell, admin only).
        public string BuildAdminFirewallPage(TERPFirewallEntry[] aBlocked,
            TERPFirewallEntry[] aIgnored, string aDisplayName, string aRole, string aTheme,
            string aLang, string aFlash)
        {
            string vConfirm = T("common.confirm_delete", aLang);
            var oRoot = new TsgcHTMLNodeList();

            // One firewall section: a table of entries plus an "Add" form.
            Action<string, TERPFirewallEntry[], string, string> addSection =
                (aTitleKey, aRows, aRemoveAction, aAddAction) =>
            {
                var oCard = new TsgcHTMLCard();
                oCard.CSSClass = "shadow-sm mb-4";
                oCard.BodyClass = "card-body";
                oCard.Body.Add(new TsgcHTMLHeading(T(aTitleKey, aLang), 5));

                var oTable = new TsgcHTMLTable();
                oTable.CSSClass = "table table-sm align-middle mb-3";
                oTable.AddColumn(T("firewall.ip", aLang));
                oTable.AddColumn(T("firewall.reason", aLang));
                oTable.AddColumn(T("firewall.created", aLang));
                oTable.AddColumn(T("common.actions", aLang), "text-end");

                if (aRows.Length == 0)
                    oTable.AddEmptyRow(T("firewall.none", aLang), 4);
                else
                    for (int vJ = 0; vJ < aRows.Length; vJ++)
                    {
                        var oRow = oTable.AddRow();
                        oRow.AddCellText(aRows[vJ].IP);
                        oRow.AddCellText(aRows[vJ].Reason);
                        oRow.AddCellText(FmtDate(aRows[vJ].CreatedAt));
                        var oActions = oRow.AddCell();
                        oActions.CellClass = "text-end";
                        oActions.AddRaw("<form method=\"post\" action=\"" + aRemoveAction +
                            "\" class=\"d-inline\" onsubmit=\"return confirm('" +
                            HtmlEsc(vConfirm).Replace("'", "\\'") +
                            "');\"><input type=\"hidden\" name=\"ip\" value=\"" +
                            HtmlEsc(aRows[vJ].IP) +
                            "\"><button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                            HtmlEsc(T("firewall.remove", aLang)) + "</button></form>");
                    }
                oCard.Body.Add(oTable);

                // Add form (ip + reason on one row).
                var oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = aAddAction;
                var oGrid = new TsgcHTMLContainer("div");
                oGrid.CSSClass = "row g-2 align-items-end";
                oForm.Add(oGrid);

                var oIPField = new TsgcHTMLField(TsgcHTMLInputType.itText, "ip");
                oIPField.Label_ = T("firewall.ip", aLang);
                oIPField.Placeholder = "1.2.3.4";
                oIPField.Required = true;
                oIPField.ColClass = "col-md-4";
                oGrid.Add(oIPField);

                var oReasonField = new TsgcHTMLField(TsgcHTMLInputType.itText, "reason");
                oReasonField.Label_ = T("firewall.reason", aLang);
                oReasonField.ColClass = "col-md-5";
                oGrid.Add(oReasonField);

                var oBtnCol = new TsgcHTMLContainer("div");
                oBtnCol.CSSClass = "col-md-3";
                var oAddBtn = new TsgcHTMLButton(T("firewall.add", aLang),
                    TsgcHTMLButtonStyle.bsPrimary);
                oAddBtn.ButtonType = "submit";
                oBtnCol.Add(oAddBtn);
                oGrid.Add(oBtnCol);

                oCard.Body.Add(oForm);
                oRoot.Add(oCard);
            };

            var oHeading = new TsgcHTMLHeading(T("admin.firewall", aLang), 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            oRoot.AddRaw(AdminSubNav("firewall", aLang));

            if (aFlash != "")
            {
                var oAlert = new TsgcHTMLAlert(aFlash);
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                oAlert.Dismissible = true;
                oAlert.CSSClass = "mb-4";
                oRoot.Add(oAlert);
            }

            addSection("firewall.blocked", aBlocked, "/admin/firewall/unblock",
                "/admin/firewall/block");
            addSection("firewall.ignored", aIgnored, "/admin/firewall/unignore",
                "/admin/firewall/ignore");

            string vBody = oRoot.HTML;
            return BuildPageShell(T("admin.firewall", aLang), vBody, "admin",
                aDisplayName, aRole, aTheme, aLang);
        }

        // A coloured Bootstrap badge for an audit action code.
        private static string AuditActionBadge(string aAction, string aLang)
        {
            string vCode = (aAction ?? string.Empty).Trim().ToLowerInvariant();
            string vVariant;
            if (vCode == "delete")
                vVariant = "danger";
            else if (vCode == "login_failed")
                vVariant = "warning";
            else if (vCode == "create")
                vVariant = "success";
            else if ((vCode == "login") || (vCode == "logout"))
                vVariant = "info";
            else
                vVariant = "secondary";
            string vLabel = T("audit.action." + vCode, aLang);
            if (vLabel == ("audit.action." + vCode))
                vLabel = aAction;
            // warning uses dark text for contrast (Bootstrap convention).
            if (vVariant == "warning")
                return "<span class=\"badge bg-warning text-dark\">" + HtmlEsc(vLabel) + "</span>";
            return "<span class=\"badge bg-" + vVariant + "\">" + HtmlEsc(vLabel) + "</span>";
        }

        // Admin -> Audit log page (inside shell, admin only).
        public string BuildAdminAuditPage(TERPAuditRow[] aRows, string[] aActions,
            string aActionFilter, string aUserFilter, int aPage, int aPageCount,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            string vAction = (aActionFilter ?? string.Empty).Trim();

            // The localized action label for the filter <option>.
            Func<string, string> actionLabel = (aCode) =>
            {
                string vResult = T("audit.action." + aCode.ToLowerInvariant(), aLang);
                if (vResult == ("audit.action." + aCode.ToLowerInvariant()))
                    vResult = aCode;
                return vResult;
            };

            var oRoot = new TsgcHTMLNodeList();
            var oHeading = new TsgcHTMLHeading(T("admin.audit", aLang), 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            oRoot.AddRaw(AdminSubNav("audit", aLang));

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";

            // Filter form (GET): action select + username text field + submit.
            var oFilterForm = new TsgcHTMLForm();
            oFilterForm.Method = "GET";
            oFilterForm.Action = "/admin/audit";
            oFilterForm.CSSClass = "mb-3";
            var oFilterRow = new TsgcHTMLContainer("div");
            oFilterRow.CSSClass = "row g-2 align-items-end";

            var oActionCol = new TsgcHTMLContainer("div");
            oActionCol.CSSClass = "col-auto";
            var oActionSel = new TsgcHTMLSelect();
            oActionSel.FieldID = "audit-action-filter";
            oActionSel.Name = "action";
            oActionSel.Label_ = T("audit.filter_action", aLang);
            oActionSel.AddOption("all", T("audit.all_actions", aLang),
                (vAction == "") || string.Equals(vAction, "all", StringComparison.OrdinalIgnoreCase));
            for (int vI = 0; vI < aActions.Length; vI++)
                oActionSel.AddOption(aActions[vI], actionLabel(aActions[vI]),
                    string.Equals(vAction, aActions[vI], StringComparison.OrdinalIgnoreCase));
            oActionCol.Add(oActionSel);
            oFilterRow.Add(oActionCol);

            var oUserCol = new TsgcHTMLContainer("div");
            oUserCol.CSSClass = "col-auto";
            var oUserInput = new TsgcHTMLField(TsgcHTMLInputType.itText, "user");
            oUserInput.FieldID = "audit-user-filter";
            oUserInput.Label_ = T("audit.filter_user", aLang);
            oUserInput.Value = aUserFilter;
            oUserCol.Add(oUserInput);
            oFilterRow.Add(oUserCol);

            var oBtnCol = new TsgcHTMLContainer("div");
            oBtnCol.CSSClass = "col-auto";
            var oFilterBtn = new TsgcHTMLButton(T("common.search", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oFilterBtn.ButtonType = "submit";
            oBtnCol.Add(oFilterBtn);
            oFilterRow.Add(oBtnCol);

            oFilterForm.Add(oFilterRow);
            oCard.Body.Add(oFilterForm);

            // Audit table (newest first).
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.AddColumn(T("audit.timestamp", aLang));
            oTable.AddColumn(T("audit.user", aLang));
            oTable.AddColumn(T("audit.action", aLang));
            oTable.AddColumn(T("audit.entity", aLang));
            oTable.AddColumn(T("audit.details", aLang));
            oTable.AddColumn(T("audit.ip", aLang));

            if (aRows.Length == 0)
                oTable.AddEmptyRow(T("audit.none", aLang), 6);
            else
                for (int vI = 0; vI < aRows.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    oRow.AddCellText(FmtDate(aRows[vI].Ts));
                    if ((aRows[vI].Username ?? string.Empty).Trim() != "")
                        oRow.AddCellText(aRows[vI].Username);
                    else
                        oRow.AddCellText("anonymous");
                    oRow.AddCellRaw(AuditActionBadge(aRows[vI].Action, aLang));
                    // Entity: "<type> #<id>" when an id is present, else just the type.
                    string vEntity = aRows[vI].EntityType;
                    if (aRows[vI].EntityId > 0)
                        vEntity = vEntity + " #" +
                            aRows[vI].EntityId.ToString(CultureInfo.InvariantCulture);
                    oRow.AddCellText(vEntity);
                    oRow.AddCellText(aRows[vI].Details);
                    oRow.AddCellText(aRows[vI].IP);
                }

            oCard.Body.Add(oTable);

            // Prev / next pager (only when there is more than one page).
            if (aPageCount > 1)
            {
                string vBaseQS = "/admin/audit?action=" + HtmlEsc(vAction) + "&user=" +
                    HtmlEsc(aUserFilter) + "&p=";
                string vPager = "<nav class=\"mt-3\"><ul class=\"pagination pagination-sm mb-0 " +
                    "justify-content-between\">";
                // Previous.
                if (aPage > 1)
                    vPager = vPager + "<li class=\"page-item\"><a class=\"page-link\" href=\"" +
                        vBaseQS + (aPage - 1).ToString(CultureInfo.InvariantCulture) + "\">" +
                        HtmlEsc(T("common.prev", aLang)) + "</a></li>";
                else
                    vPager = vPager + "<li class=\"page-item disabled\"><span " +
                        "class=\"page-link\">" + HtmlEsc(T("common.prev", aLang)) + "</span></li>";
                // Page indicator.
                vPager = vPager + "<li class=\"page-item disabled\"><span " +
                    "class=\"page-link\">" + aPage.ToString(CultureInfo.InvariantCulture) + " / " +
                    aPageCount.ToString(CultureInfo.InvariantCulture) + "</span></li>";
                // Next.
                if (aPage < aPageCount)
                    vPager = vPager + "<li class=\"page-item\"><a class=\"page-link\" href=\"" +
                        vBaseQS + (aPage + 1).ToString(CultureInfo.InvariantCulture) + "\">" +
                        HtmlEsc(T("common.next", aLang)) + "</a></li>";
                else
                    vPager = vPager + "<li class=\"page-item disabled\"><span " +
                        "class=\"page-link\">" + HtmlEsc(T("common.next", aLang)) + "</span></li>";
                vPager = vPager + "</ul></nav>";
                oCard.Body.AddRaw(vPager);
            }

            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("admin.audit", aLang), vBody, "admin",
                aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // forbidden (standalone, no shell)
        // ---------------------------------------------------------------------

        // Standalone (no shell) 403 page used by the firewall enforcement and the
        // non-admin admin gate. aMessageKey is the localized body message key.
        public string BuildForbiddenPage(string aTheme, string aLang, string aMessageKey)
        {
            var oRoot = new TsgcHTMLNodeList();
            var oContainer = new TsgcHTMLContainer("div");
            oContainer.CSSClass = "container py-5 text-center";
            var oHeading = new TsgcHTMLHeading("403 - " + T("admin.forbidden", aLang), 1);
            oHeading.CSSClass = "mb-3";
            oContainer.Add(oHeading);
            var oPara = new TsgcHTMLParagraph(T(aMessageKey, aLang));
            oPara.CSSClass = "lead";
            oContainer.Add(oPara);
            oRoot.Add(oContainer);
            string vBody = oRoot.HTML;
            return WrapTemplate(BrandText(aLang) + " - " + T("admin.forbidden", aLang),
                vBody, aTheme, aLang);
        }
    }
}
