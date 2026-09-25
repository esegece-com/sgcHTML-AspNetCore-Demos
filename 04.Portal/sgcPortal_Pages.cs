// ***************************************************************************
//  sgcPortal - mini ERP web-app demo (node-based view layer)
//  Port of delphi\Demos\60.HTML\50.Portal\Source\sgcPortal_Pages.pas
//
//  written by eSeGeCe
//  copyright © 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
// Node-based view layer for the Customer Portal demo. Zero custom HTML strings: each
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

namespace Portal
{
    public class TERPPages
    {
        // ---------------------------------------------------------------------
        // theme CSS constants (copied verbatim from the Delphi)
        // ---------------------------------------------------------------------

        // Dark CSS overrides for the Customer Portal. Airy dark surfaces with the
        // SAME emerald accent (#10B981) as the light theme, so the portal identity
        // (top navbar, hero, rounded cards, timeline, avatar) is preserved when the
        // visitor prefers dark mode. Light is the DEFAULT theme.
        private const string CS_THEME_DARK_RULES = "body{background:#0B1220;color:#E2E8F0;}" +
            ".card{background:#111A2B;border:1px solid #1E2A40;color:#E2E8F0;}" +
            ".card-header{background:#0F1626;border-bottom:1px solid #1E2A40;}" +
            ".table{--bs-table-bg:#111A2B;--bs-table-color:#E2E8F0;" +
            "--bs-table-border-color:#1E2A40;--bs-table-hover-bg:#16223A;" +
            "--bs-table-hover-color:#fff;color:#E2E8F0;border-color:#1E2A40;}" +
            ".table>:not(caption)>*>*{background-color:var(--bs-table-bg);" +
            "color:var(--bs-table-color);border-bottom-color:#1E2A40;}" +
            "thead.table-light{--bs-table-bg:#0F1626;--bs-table-color:#E2E8F0;" +
            "--bs-table-border-color:#1E2A40;}" +
            "thead.table-light th{background:#0F1626;color:#E2E8F0;border-color:#1E2A40;}" +
            ".text-muted{color:#94A3B8 !important;}" +
            ".form-control,.form-select{background:#0F1626;border-color:#1E2A40;color:#E2E8F0;}" +
            ".form-control:focus,.form-select:focus{background:#16223A;color:#E2E8F0;border-color:#10B981;box-shadow:0 0 0 .2rem rgba(16,185,129,.2);}" +
            ".list-group-item{background:#111A2B;border-color:#1E2A40;color:#E2E8F0;}" +
            ".page-link{background:#111A2B;border-color:#1E2A40;color:#E2E8F0;}" +
            ".page-item.active .page-link{background:#10B981;border-color:#10B981;}" +
            "a{color:#34D399;}a:hover{color:#6EE7B7;}" +
            ".dropdown-menu{background:#111A2B;border-color:#1E2A40;}" +
            ".dropdown-item{color:#E2E8F0;}" +
            ".dropdown-item:hover,.dropdown-item:focus{background:#16223A;color:#fff;}" +
            "nav.navbar.bg-dark{background:rgba(17,26,43,.97) !important;border-bottom:1px solid #1E2A40;}" +
            "nav.navbar.bg-dark .navbar-brand,nav.navbar.bg-dark .nav-link{color:#E2E8F0 !important;}" +
            "nav.navbar.bg-dark .portal-navlink{color:#94A3B8 !important;}" +
            "nav.navbar.bg-dark .portal-navlink:hover,nav.navbar.bg-dark .portal-navlink.active{color:#34D399 !important;}" +
            "nav.navbar.bg-dark .dropdown-menu{background:#111A2B;border:1px solid #1E2A40;}" +
            ".portal-card{background:#111A2B;border:1px solid #1E2A40;}" +
            ".portal-summary .value{color:#fff;}" +
            ".portal-timeline:before{background:#1E2A40;}" +
            ".portal-timeline .t-card{background:#0F1626;border-color:#1E2A40;}";

        // Light-mode theme = the Customer Portal identity. AIRY, LIGHT, friendly:
        // page bg #F8FAFC, white surfaces, emerald accent #10B981, a gradient hero,
        // rounded-4 cards with soft shadows, an order timeline and an avatar circle.
        // Bootstrap CSS variables drive buttons / links / badges; bespoke classes
        // (.portal-hero / .portal-summary / .portal-timeline / .portal-avatar /
        // .portal-navlink / .portal-account-toggle) drive the new look.
        private const string CS_THEME_LIGHT_RULES =
            "body{font-family:-apple-system,BlinkMacSystemFont,\"Segoe UI\",Roboto,Helvetica,Arial,sans-serif;color:#0F172A;background:#F8FAFC;line-height:1.6;}" +
            ":root,html[data-bs-theme=\"light\"]{" +
            "--bs-primary:#10B981;--bs-primary-rgb:16,185,129;" +
            "--bs-link-color:#059669;--bs-link-color-rgb:5,150,105;" +
            "--bs-link-hover-color:#047857;--bs-link-hover-color-rgb:4,120,87;" +
            "--bs-body-color:#0F172A;--bs-body-color-rgb:15,23,42;" +
            "--bs-body-bg:#F8FAFC;--bs-body-bg-rgb:248,250,252;" +
            "--bs-secondary-color:#64748B;--bs-secondary-color-rgb:100,116,139;" +
            "--bs-tertiary-bg:#F1F5F9;--bs-tertiary-bg-rgb:241,245,249;" +
            "--bs-border-color:#E2E8F0;--bs-border-color-translucent:rgba(16,185,129,.12);" +
            "--bs-success:#10B981;--bs-success-rgb:16,185,129;" +
            "--bs-info:#10B981;--bs-info-rgb:16,185,129;" + "}" +
            ".btn-primary{--bs-btn-bg:#10B981;--bs-btn-border-color:#10B981;--bs-btn-color:#fff;--bs-btn-hover-bg:#059669;--bs-btn-hover-border-color:#059669;--bs-btn-hover-color:#fff;--bs-btn-active-bg:#059669;--bs-btn-active-border-color:#047857;}" +
            ".btn-outline-primary{--bs-btn-color:#059669;--bs-btn-border-color:#10B981;--bs-btn-hover-bg:#10B981;--bs-btn-hover-border-color:#10B981;--bs-btn-hover-color:#fff;}" +
            ".btn-success{--bs-btn-bg:#10B981;--bs-btn-border-color:#10B981;--bs-btn-hover-bg:#059669;--bs-btn-hover-border-color:#059669;}" +
            ".btn{border-radius:.65rem;font-weight:600;}" +
            ".btn-lg{border-radius:.85rem;}" +
            // Rounded, soft-shadow surfaces with generous whitespace.
            ".card{border-radius:1rem;border:1px solid #E2E8F0;background:#FFFFFF;box-shadow:0 1px 3px rgba(15,23,42,.06),0 1px 2px rgba(15,23,42,.04);}" +
            ".card-header{background:#FFFFFF;border-bottom:1px solid #E2E8F0;font-weight:600;}" +
            ".rounded-4{border-radius:1rem !important;}" +
            ".table{--bs-table-color:#0F172A;--bs-table-border-color:#E2E8F0;}" +
            "thead.table-light{--bs-table-bg:#F8FAFC;--bs-table-color:#64748B;--bs-table-border-color:#E2E8F0;}" +
            ".form-control,.form-select{border-radius:.65rem;border-color:#E2E8F0;padding:.6rem .85rem;}" +
            ".form-control:focus,.form-select:focus{border-color:#10B981;box-shadow:0 0 0 .2rem rgba(16,185,129,.18);}" +
            "a{color:#059669;}a:hover{color:#047857;}" +
            ".badge.bg-primary,.badge.bg-success,.badge.bg-info{background:#10B981 !important;color:#fff;}" +
            ".alert-success{background:#ECFDF5;border-color:#A7F3D0;color:#065F46;}" +
            ".alert-info{background:#ECFDF5;border-color:#A7F3D0;color:#065F46;}" +
            // ----- TOP NAVBAR (white, subtle bottom shadow, NO sidebar) ----- //
            "nav.navbar.bg-dark,nav.navbar.navbar-dark{background:rgba(255,255,255,.95) !important;border-bottom:1px solid #E2E8F0;box-shadow:0 1px 3px rgba(15,23,42,.04);backdrop-filter:blur(12px);}" +
            "nav.navbar.bg-dark .navbar-brand{color:#0F172A !important;font-weight:800;}" +
            "nav.navbar .portal-navlink{color:#64748B !important;font-weight:500;border-radius:.5rem;padding:.4rem .75rem;}" +
            "nav.navbar .portal-navlink:hover{color:#059669 !important;background:#ECFDF5;}" +
            "nav.navbar .portal-navlink.active{color:#059669 !important;background:#ECFDF5;font-weight:600;}" +
            "nav.navbar .dropdown-menu{background:#fff;border:1px solid #E2E8F0;border-radius:.75rem;box-shadow:0 8px 24px rgba(15,23,42,.10);}" +
            "nav.navbar .dropdown-item{color:#0F172A;border-radius:.5rem;}" +
            "nav.navbar .dropdown-item:hover,nav.navbar .dropdown-item:focus{background:#ECFDF5;color:#059669;}" +
            ".portal-account-toggle{display:inline-flex;align-items:center;gap:.5rem;color:#0F172A !important;text-decoration:none;font-weight:600;}" +
            // ----- AVATAR CIRCLE (emerald bg, white initials) ----- //
            ".portal-avatar{display:inline-flex;align-items:center;justify-content:center;border-radius:50%;background:#10B981;color:#fff;font-weight:700;flex:0 0 auto;}" +
            ".portal-avatar-sm{width:34px;height:34px;font-size:.85rem;}" +
            ".portal-avatar-lg{width:84px;height:84px;font-size:2rem;box-shadow:0 6px 16px rgba(16,185,129,.30);}" +
            // ----- GRADIENT HERO ----- //
            ".portal-hero{background:linear-gradient(135deg,#34D399 0%,#10B981 100%);color:#fff;border-radius:1rem;padding:2.25rem 2rem;box-shadow:0 10px 24px rgba(16,185,129,.28);}" +
            ".portal-hero h1{font-weight:800;margin:0;}" +
            ".portal-hero p{margin:.5rem 0 0;opacity:.92;}" +
            // ----- SUMMARY CARDS ----- //
            ".portal-summary .value{font-size:1.9rem;font-weight:800;line-height:1.1;color:#0F172A;}" +
            ".portal-summary .label{color:#64748B;font-size:.8rem;text-transform:uppercase;letter-spacing:.03em;font-weight:600;}" +
            ".portal-summary .icon{display:inline-flex;align-items:center;justify-content:center;width:48px;height:48px;border-radius:.85rem;background:#ECFDF5;color:#10B981;}" +
            // ----- ORDER TIMELINE (vertical line + status dots) ----- //
            ".portal-timeline{position:relative;list-style:none;margin:0;padding:0 0 0 1.75rem;}" +
            ".portal-timeline:before{content:\"\";position:absolute;left:.42rem;top:.3rem;bottom:.3rem;width:2px;background:#E2E8F0;}" +
            ".portal-timeline>li{position:relative;padding:0 0 1rem 0;}" +
            ".portal-timeline>li:last-child{padding-bottom:0;}" +
            ".portal-timeline>li:before{content:\"\";position:absolute;left:-1.5rem;top:.45rem;width:.95rem;height:.95rem;border-radius:50%;background:#10B981;border:2px solid #fff;box-shadow:0 0 0 2px #10B981;}" +
            ".portal-timeline>li.dot-muted:before{background:#94A3B8;box-shadow:0 0 0 2px #94A3B8;}" +
            ".portal-timeline>li.dot-warning:before{background:#F59E0B;box-shadow:0 0 0 2px #F59E0B;}" +
            ".portal-timeline>li.dot-danger:before{background:#EF4444;box-shadow:0 0 0 2px #EF4444;}" +
            ".portal-timeline .t-card{background:#FFFFFF;border:1px solid #E2E8F0;border-radius:.85rem;padding:.85rem 1rem;display:flex;align-items:center;justify-content:space-between;gap:1rem;}" +
            ".portal-timeline .t-card a{font-weight:700;text-decoration:none;}" +
            ".portal-timeline .t-meta{color:#64748B;font-size:.85rem;}";

        // Page-level layout rules (navbar stacking + centered portal container).
        // No sidebar: the portal uses a single centered column.
        private const string CS_NAVBAR_STACKING_RULES = ".navbar{position:relative;z-index:1030;}" +
            ".navbar .dropdown-menu{z-index:1040;}" +
            ".portal-container{max-width:1040px;margin:0 auto;width:100%;}";

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
        // with the surrounding font). Used in the navbar, login and footer brand.
        // Emerald rounded square to match the Customer Portal accent.
        private const string CS_ESEGECE_LOGO_SVG =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\" " +
            "width=\"1.6em\" height=\"1.6em\" role=\"img\" aria-label=\"eSeGeCe\" " +
            "class=\"me-2 flex-shrink-0\"><rect width=\"64\" height=\"64\" rx=\"14\" " +
            "fill=\"#10B981\"/><text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica," +
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

        // Date-only (no time) for invoice issue/due dates. '-' when unset.
        private static string FmtDateOnly(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // A value as an HTML date-input value (yyyy-mm-dd), empty when unset.
        private static string FmtDateInput(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return string.Empty;
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // Format an amount with 2 decimals using a '.' separator, then prefix the
        // currency code (e.g. 'EUR 1234.50').
        private static string FmtMoney(double aValue, string aCurrency)
        {
            string vCur = (aCurrency ?? string.Empty).Trim();
            if (vCur == "")
                vCur = "EUR";
            return vCur + " " + aValue.ToString("#,##0.00", CultureInfo.InvariantCulture);
        }

        // Render a double with 2 decimals and a '.' separator for input values.
        private static string FmtNum2(double aValue)
        {
            return aValue.ToString("0.00", CultureInfo.InvariantCulture);
        }

        // Up to two uppercase initials from a display name, for the avatar circle.
        // 'Maria Anders' -> 'MA'; 'customer' -> 'C'; '' -> 'U'.
        private static string InitialsOf(string aName)
        {
            string vTrim = (aName ?? string.Empty).Trim();
            if (vTrim == "")
                return "U";
            string[] vParts = vTrim.Split(' ');
            if (vParts.Length == 1)
                return vParts[0].Substring(0, 1).ToUpperInvariant();
            return (vParts[0].Substring(0, 1) +
                vParts[vParts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        // Maps an order status to a timeline-dot colour modifier class. 'paid' and
        // 'sent' keep the default emerald dot; the rest are tinted (draft -> muted,
        // cancelled -> danger, others -> warning).
        private static string TimelineDotClass(string aStatus)
        {
            string vS = (aStatus ?? string.Empty).Trim().ToLowerInvariant();
            if (vS == "draft")
                return "dot-muted";
            if (vS == "cancelled")
                return "dot-danger";
            if (vS == "paid" || vS == "sent")
                return "";
            return "dot-warning";
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

        // Portal nav links rendered inline in the top navbar (Dashboard / My Orders
        // / Profile / Support). Active link highlighted. NO sidebar.
        private string BuildNavLinks(string aActiveMenu, string aRole, string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oUl = new TsgcHTMLContainer("ul");
            oUl.CSSClass = "navbar-nav me-auto mb-2 mb-lg-0 align-items-lg-center";

            Action<string, string, string> addLink = (aHref, aKey, aMenu) =>
            {
                string vClass = "nav-link portal-navlink";
                if (string.Equals(aActiveMenu, aMenu, StringComparison.OrdinalIgnoreCase))
                    vClass = vClass + " active";
                var oLi = new TsgcHTMLContainer("li");
                oLi.CSSClass = "nav-item";
                var oLink = new TsgcHTMLLink(aHref, T(aKey, aLang));
                oLink.CSSClass = vClass;
                oLi.Add(oLink);
                oUl.Add(oLi);
            };

            // The portal is a customer self-service area: the only nav is the
            // customer's own surfaces. (Any non-customer role lands here too, but
            // the server confines customers to these routes and never shows ERP
            // tables.)
            addLink("/", "nav.account", "account");
            addLink("/orders", "nav.orders", "orders");
            addLink("/profile", "nav.profile", "profile");
            addLink("/support", "nav.support", "support");

            oRoot.Add(oUl);
            return oRoot.HTML;
        }

        // Account dropdown: an emerald avatar circle (white initials of the
        // customer name) + the name, with Profile / Logout items.
        private string BuildAccountDropdown(string aDisplayName, string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oLi = new TsgcHTMLContainer("li");
            oLi.CSSClass = "nav-item dropdown ms-lg-3";

            var oToggle = new TsgcHTMLContainer("a");
            oToggle.Attributes =
                "class=\"portal-account-toggle dropdown-toggle nav-link\" href=\"#\" " +
                "id=\"portalAccountDropdown\" role=\"button\" data-bs-toggle=\"dropdown\" " +
                "aria-expanded=\"false\"";
            var oAvatar = new TsgcHTMLContainer("span");
            oAvatar.CSSClass = "portal-avatar portal-avatar-sm";
            oAvatar.AddText(InitialsOf(aDisplayName));
            oToggle.Add(oAvatar);
            var oName = new TsgcHTMLContainer("span");
            oName.CSSClass = "d-none d-sm-inline";
            oName.AddText(aDisplayName);
            oToggle.Add(oName);
            oLi.Add(oToggle);

            var oUl = new TsgcHTMLContainer("ul");
            oUl.Attributes = "class=\"dropdown-menu dropdown-menu-end\" " +
                "aria-labelledby=\"portalAccountDropdown\"";

            var oItemProfile = new TsgcHTMLContainer("li");
            var oProfileLink = new TsgcHTMLLink("/profile", T("nav.profile", aLang));
            oProfileLink.CSSClass = "dropdown-item";
            oItemProfile.Add(oProfileLink);
            oUl.Add(oItemProfile);

            oUl.AddRaw("<li><hr class=\"dropdown-divider\"></li>");

            var oItemLogout = new TsgcHTMLContainer("li");
            var oLogoutForm = new TsgcHTMLForm();
            oLogoutForm.Method = "POST";
            oLogoutForm.Action = "/logout";
            oLogoutForm.CSSClass = "m-0";
            var oLogoutBtn = new TsgcHTMLContainer("button");
            oLogoutBtn.Attributes = "type=\"submit\" class=\"dropdown-item\"";
            oLogoutBtn.AddText(T("nav.logout", aLang));
            oLogoutForm.Add(oLogoutBtn);
            oItemLogout.Add(oLogoutForm);
            oUl.Add(oItemLogout);

            oLi.Add(oUl);
            oRoot.Add(oLi);
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

        // Top navbar: logo + brand, inline portal nav links, theme + language
        // switchers and an account dropdown (avatar + name). No sidebar.
        private string BuildNavBar(string aActiveMenu, string aDisplayName,
            string aRole, string aTheme, string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            // White top navbar with a subtle bottom border (themed via the
            // light/dark rules; bg-dark is the marker class the theme CSS recolours
            // to white).
            var oNav = new TsgcHTMLContainer("nav");
            oNav.CSSClass = "navbar navbar-expand-lg bg-dark mb-4";
            var oContainer = new TsgcHTMLContainer("div");
            oContainer.CSSClass = "container portal-container";

            // Left: logo + brand.
            var oBrand = new TsgcHTMLContainer("a");
            oBrand.Attributes =
                "class=\"navbar-brand d-inline-flex align-items-center\" href=\"/\"";
            oBrand.AddRaw(CS_ESEGECE_LOGO_SVG);
            oBrand.AddText(BrandText(aLang));
            oContainer.Add(oBrand);

            // Mobile toggler for the collapsible right side.
            var oToggler = new TsgcHTMLContainer("button");
            oToggler.Attributes = "class=\"navbar-toggler\" type=\"button\" " +
                "data-bs-toggle=\"collapse\" data-bs-target=\"#portalNav\" " +
                "aria-controls=\"portalNav\" aria-expanded=\"false\" " +
                "aria-label=\"Toggle navigation\"";
            oToggler.AddRaw("<span class=\"navbar-toggler-icon\"></span>");
            oContainer.Add(oToggler);

            var oCollapse = new TsgcHTMLContainer("div");
            oCollapse.Attributes = "class=\"collapse navbar-collapse\" id=\"portalNav\"";

            // Center-left: inline portal nav links.
            oCollapse.AddRaw(BuildNavLinks(aActiveMenu, aRole, aLang));

            // Right: theme + language switchers and the account dropdown.
            var oRightUl = new TsgcHTMLContainer("ul");
            oRightUl.CSSClass = "navbar-nav ms-auto mb-2 mb-lg-0 align-items-lg-center";
            oRightUl.AddRaw(BuildThemeDropdown(aTheme, aLang));
            oRightUl.AddRaw(BuildLanguageDropdown(aLang));
            oRightUl.AddRaw(BuildAccountDropdown(aDisplayName, aLang));
            oCollapse.Add(oRightUl);

            oContainer.Add(oCollapse);
            oNav.Add(oContainer);
            oRoot.Add(oNav);
            return oRoot.HTML;
        }

        // ---------------------------------------------------------------------
        // shared shell
        // ---------------------------------------------------------------------

        // Shared shell: wraps a page body in a themed/localized Bootstrap template
        // with a top navbar (inline portal nav + account dropdown). NO sidebar; a
        // single centered content column with generous whitespace.
        public string BuildPageShell(string aTitle, string aBodyHTML, string aActiveMenu,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            var oBody = new TsgcHTMLNodeList();
            // Top navbar (with inline portal nav + account dropdown). NO sidebar.
            oBody.AddRaw(BuildNavBar(aActiveMenu, aDisplayName, aRole, aTheme, aLang));

            // A single centered content column with generous whitespace.
            var oMain = new TsgcHTMLContainer("main");
            oMain.CSSClass = "container portal-container px-3 pb-4";
            oMain.AddRaw(aBodyHTML);
            oBody.Add(oMain);
            oBody.AddRaw(BuildFooter(aLang));

            string vBody = oBody.HTML;
            return WrapTemplate(T("app.title", aLang) + " - " + aTitle, vBody, aTheme, aLang);
        }

        // Page footer: eSeGeCe brand mark + a "Built with sgcHTML ..." line + a
        // copyright. The (c) symbol is emitted as the &copy; entity so the source
        // stays ASCII and the UTF-8 response is never given a raw high byte.
        private string BuildFooter(string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "border-top mt-4 py-4 text-center text-muted small";

            // Brand row: eSeGeCe mark + the Customer Portal brand.
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
            var oLink = new TsgcHTMLLink("https://www.esegece.com/products/sgchtml/",
                "sgcHTML");
            oLink.Target = "_blank";
            oLink.Rel = "noopener";
            oLink.CSSClass = "fw-semibold text-decoration-none";
            oP.Add(oLink);
            oP.AddText(" " + T("footer.suffix", aLang));
            oFooter.Add(oP);

            // Copyright.
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

        // Post-login dashboard rendered inside the shared shell: KPI stat cards, a
        // revenue-by-month bar chart beside an invoices-by-status summary, a recent-
        // invoices table, and a quick-action button row.
        public string BuildDashboardPage(string aDisplayName, string aRole, string aTheme,
            string aLang, int[] aCounts, double aRevenue, string aCurrency, int[] aByStatus,
            TERPRevenueMonth[] aMonths, TERPInvoiceListRow[] aRecent)
        {
            var oRoot = new TsgcHTMLNodeList();

            // --- heading + welcome line --- //
            var oHeading = new TsgcHTMLHeading(T("nav.dashboard", aLang), 1);
            oHeading.CSSClass = "mb-1";
            oRoot.Add(oHeading);
            var oWelcome = new TsgcHTMLParagraph(T("common.welcome", aLang) + ", " +
                aDisplayName + ". " + T("dashboard.welcome", aLang));
            oWelcome.CSSClass = "text-muted mb-4";
            oRoot.Add(oWelcome);

            // --- KPI stat cards row --- //
            var oKpiRow = new TsgcHTMLContainer("div");
            oKpiRow.CSSClass = "row g-3 mb-4";

            // One KPI stat card: big number + label + tinted SVG icon in a soft
            // circle, with a coloured left-border accent.
            Action<string, string, string, string, string> addKpiCard =
                (aLabelKey, aValue, aIcon, aBorder, aTint) =>
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-6 col-lg";

                var oKpi = new TsgcHTMLCard();
                oKpi.CSSClass = "h-100 shadow-sm";
                oKpi.BodyClass = "card-body";

                var oFlex = new TsgcHTMLContainer("div");
                oFlex.CSSClass = "d-flex align-items-center justify-content-between";

                var oText = new TsgcHTMLContainer("div");
                var oNum = new TsgcHTMLHeading(aValue, 2);
                oNum.CSSClass = "fw-bold mb-0";
                oText.Add(oNum);
                var oLbl = new TsgcHTMLParagraph(T(aLabelKey, aLang));
                oLbl.CSSClass = "text-muted mb-0 small text-uppercase fw-semibold";
                oText.Add(oLbl);
                oFlex.Add(oText);

                var oIcon = new TsgcHTMLContainer("div");
                oIcon.CSSClass = "d-flex align-items-center justify-content-center rounded-circle";
                oIcon.Style = "width:52px;height:52px;flex:0 0 52px;background:" + aTint +
                    ";color:" + aBorder + ";";
                oIcon.AddRaw(aIcon);
                oFlex.Add(oIcon);

                oKpi.Body.Add(oFlex);

                // Wrap the card in a div carrying the left-accent border.
                oCol.AddRaw("<div style=\"border-left:4px solid " + aBorder +
                    ";border-radius:12px;\">");
                oCol.Add(oKpi);
                oCol.AddRaw("</div>");
                oKpiRow.Add(oCol);
            };

            // aCounts = [customers, products, providers, invoices].
            addKpiCard("dashboard.kpi.customers", aCounts[0].ToString(CultureInfo.InvariantCulture),
                CS_ICON_CUSTOMERS, "#10B981", "#D1FAE5");
            addKpiCard("dashboard.kpi.products", aCounts[1].ToString(CultureInfo.InvariantCulture),
                CS_ICON_PRODUCTS, "#0EA5A5", "#E3F6F5");
            addKpiCard("dashboard.kpi.providers", aCounts[2].ToString(CultureInfo.InvariantCulture),
                CS_ICON_PROVIDERS, "#10B981", "#D1FAE5");
            addKpiCard("dashboard.kpi.invoices", aCounts[3].ToString(CultureInfo.InvariantCulture),
                CS_ICON_INVOICES, "#10B981", "#D1FAE5");
            addKpiCard("dashboard.kpi.revenue", FmtMoney(aRevenue, aCurrency),
                CS_ICON_REVENUE, "#F0A400", "#FFF8E5");
            oRoot.Add(oKpiRow);

            // --- chart + status summary (2 columns) --- //
            var oSplitRow = new TsgcHTMLContainer("div");
            oSplitRow.CSSClass = "row g-3 mb-4";

            // Left: revenue-by-month bar chart inside a card.
            var oChartCol = new TsgcHTMLContainer("div");
            oChartCol.CSSClass = "col-12 col-lg-8";
            var oChart = new TsgcHTMLMiniBarChart();
            oChart.Title = T("dashboard.revenue_trend", aLang);
            oChart.CardClass = "card shadow-sm h-100";
            oChart.Height = "220px";
            oChart.BarClass = "bg-primary";
            oChart.ValueClass = "small text-muted";
            oChart.EmptyText = T("invoice.none", aLang);
            for (int vI = 0; vI < aMonths.Length; vI++)
                oChart.AddBar(aMonths[vI].MonthLabel, aMonths[vI].Total,
                    FmtMoney(aMonths[vI].Total, aCurrency));
            oChartCol.AddRaw(oChart.HTML);
            oSplitRow.Add(oChartCol);

            // Right: invoices-by-status summary card with coloured badges + counts.
            var oStatusCol = new TsgcHTMLContainer("div");
            oStatusCol.CSSClass = "col-12 col-lg-4";
            var oStatusCard = new TsgcHTMLCard();
            oStatusCard.CSSClass = "shadow-sm h-100";
            oStatusCard.BodyClass = "card-body";
            oStatusCard.Body.Add(new TsgcHTMLHeading(T("dashboard.by_status", aLang), 5));

            // One badge + count row in the invoices-by-status summary list.
            Action<string, string, int> addStatusRow = (aLabelKey, aVariant, aCount) =>
            {
                var oLi = new TsgcHTMLContainer("div");
                oLi.CSSClass = "d-flex justify-content-between align-items-center py-2 border-bottom";
                oLi.AddRaw("<span class=\"badge bg-" + aVariant + "\">" +
                    HtmlEsc(T(aLabelKey, aLang)) + "</span>");
                oLi.AddRaw("<span class=\"fw-bold fs-5\">" +
                    aCount.ToString(CultureInfo.InvariantCulture) + "</span>");
                oStatusCard.Body.Add(oLi);
            };

            // aByStatus = [draft, sent, paid, cancelled].
            addStatusRow("invoice.status.draft", "secondary", aByStatus[0]);
            addStatusRow("invoice.status.sent", "info", aByStatus[1]);
            addStatusRow("invoice.status.paid", "success", aByStatus[2]);
            addStatusRow("invoice.status.cancelled", "danger", aByStatus[3]);
            oStatusCol.Add(oStatusCard);
            oSplitRow.Add(oStatusCol);
            oRoot.Add(oSplitRow);

            // --- recent invoices table --- //
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4";
            oCard.BodyClass = "card-body";

            var oHeaderBar = new TsgcHTMLContainer("div");
            oHeaderBar.CSSClass = "d-flex justify-content-between align-items-center mb-3";
            oHeaderBar.Add(new TsgcHTMLHeading(T("dashboard.recent_invoices", aLang), 5));
            oHeaderBar.AddRaw("<a href=\"/invoices\" class=\"btn btn-sm btn-outline-primary\">" +
                HtmlEsc(T("dashboard.view_all", aLang)) + "</a>");
            oCard.Body.Add(oHeaderBar);

            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm table-hover align-middle mb-0";
            oTable.TheadClass = "table-light";
            oTable.AddColumn(T("invoice.number", aLang));
            oTable.AddColumn(T("invoice.customer", aLang));
            oTable.AddColumn(T("invoice.date", aLang));
            oTable.AddColumn(T("invoice.status", aLang));
            oTable.AddColumn(T("invoice.total", aLang), "text-end");

            if (aRecent.Length == 0)
                oTable.AddEmptyRow(T("invoice.none", aLang), 5);
            else
                for (int vI = 0; vI < aRecent.Length; vI++)
                {
                    var oTr = oTable.AddRow();
                    oTr.AddCellRaw("<a href=\"/invoices/edit?id=" +
                        aRecent[vI].Id.ToString(CultureInfo.InvariantCulture) + "\">" +
                        HtmlEsc(aRecent[vI].Number) + "</a>");
                    oTr.AddCellText(aRecent[vI].CustomerName);
                    oTr.AddCellText(FmtDateOnly(aRecent[vI].IssueDate));
                    oTr.AddCellRaw(InvoiceStatusBadge(aRecent[vI].Status, aLang));
                    oTr.AddCellRaw(HtmlEsc(FmtMoney(aRecent[vI].Total, aRecent[vI].Currency)),
                        "text-end fw-semibold");
                }
            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            // --- quick actions --- //
            var oQuickCard = new TsgcHTMLCard();
            oQuickCard.CSSClass = "shadow-sm";
            oQuickCard.BodyClass = "card-body";
            oQuickCard.Body.Add(new TsgcHTMLHeading(T("dashboard.quick_actions", aLang), 5));
            var oQuickRow = new TsgcHTMLContainer("div");
            oQuickRow.CSSClass = "d-flex flex-wrap mt-2";

            // One quick-action brand button (link styled as a button).
            Action<string, string, TsgcHTMLButtonStyle> addQuickAction = (aHref, aLabelKey, aStyle) =>
            {
                var oBtn = new TsgcHTMLButton(T(aLabelKey, aLang), aStyle);
                oBtn.Href = aHref;
                oBtn.CSSClass = "btn-lg me-2 mb-2";
                oQuickRow.Add(oBtn);
            };

            addQuickAction("/customers/new", "action.new_customer", TsgcHTMLButtonStyle.bsPrimary);
            addQuickAction("/products/new", "action.new_product", TsgcHTMLButtonStyle.bsOutlinePrimary);
            addQuickAction("/invoices/new", "action.new_invoice", TsgcHTMLButtonStyle.bsWarning);
            oQuickCard.Body.Add(oQuickRow);
            oRoot.Add(oQuickCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("nav.dashboard", aLang), vBody, "dashboard",
                aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // coming soon
        // ---------------------------------------------------------------------

        // "Coming soon" page for protected routes not yet implemented, inside shell.
        public string BuildComingSoonPage(string aTitle, string aDisplayName, string aRole,
            string aTheme, string aLang)
        {
            // Map the route title to a localized heading + active menu item.
            string vMenu;
            string vTitleKey;
            if (string.Equals(aTitle, "Customers", StringComparison.OrdinalIgnoreCase))
            {
                vTitleKey = "nav.customers";
                vMenu = "customers";
            }
            else if (string.Equals(aTitle, "Providers", StringComparison.OrdinalIgnoreCase))
            {
                vTitleKey = "nav.providers";
                vMenu = "providers";
            }
            else if (string.Equals(aTitle, "Invoices", StringComparison.OrdinalIgnoreCase))
            {
                vTitleKey = "nav.invoices";
                vMenu = "invoices";
            }
            else if (string.Equals(aTitle, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                vTitleKey = "nav.admin";
                vMenu = "admin";
            }
            else
            {
                vTitleKey = aTitle;
                vMenu = "";
            }

            var oRoot = new TsgcHTMLNodeList();
            var oHeading = new TsgcHTMLHeading(T(vTitleKey, aLang), 1);
            oHeading.CSSClass = "mb-3";
            oRoot.Add(oHeading);

            var oPara = new TsgcHTMLParagraph(T("common.coming_soon", aLang));
            oPara.CSSClass = "lead";
            oRoot.Add(oPara);

            var oLink = new TsgcHTMLLink("/", T("common.back", aLang));
            oRoot.Add(oLink);

            string vBody = oRoot.HTML;
            return BuildPageShell(T(vTitleKey, aLang), vBody, vMenu, aDisplayName,
                aRole, aTheme, aLang);
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
        // customers
        // ---------------------------------------------------------------------

        // Customers list (inside shell): "New customer" link, a GET search form, and
        // a table of customers (code/name/email/phone/city + Actions).
        public string BuildCustomersPage(TERPCustomer[] aRows, string aSearch,
            string aDisplayName, string aRole, string aTheme, string aLang, string aFlash)
        {
            var oRoot = new TsgcHTMLNodeList();

            // Heading + "New customer" button on one flex row.
            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "d-flex justify-content-between align-items-center mb-4";
            var oHeading = new TsgcHTMLHeading(T("customer.title", aLang), 1);
            oHeading.CSSClass = "mb-0";
            oHeader.Add(oHeading);
            var oNewBtn = new TsgcHTMLButton(T("customer.new", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oNewBtn.Href = "/customers/new";
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

            // Search form (GET so the query is shareable / bookmarkable).
            var oSearchForm = new TsgcHTMLForm();
            oSearchForm.Method = "GET";
            oSearchForm.Action = "/customers";
            oSearchForm.CSSClass = "mb-3";
            var oSearchRow = new TsgcHTMLContainer("div");
            oSearchRow.CSSClass = "row g-2";
            var oSearchCol = new TsgcHTMLContainer("div");
            oSearchCol.CSSClass = "col";
            var oSearchInput = new TsgcHTMLField(TsgcHTMLInputType.itText, "q");
            oSearchInput.FieldID = "customer-search";
            oSearchInput.Placeholder = T("common.search", aLang);
            oSearchInput.Value = aSearch;
            oSearchCol.Add(oSearchInput);
            oSearchRow.Add(oSearchCol);
            var oSearchBtnCol = new TsgcHTMLContainer("div");
            oSearchBtnCol.CSSClass = "col-auto";
            var oSearchBtn = new TsgcHTMLButton(T("common.search", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oSearchBtn.ButtonType = "submit";
            oSearchBtnCol.Add(oSearchBtn);
            oSearchRow.Add(oSearchBtnCol);
            oSearchForm.Add(oSearchRow);
            oCard.Body.Add(oSearchForm);

            // Customers table.
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.AddColumn(T("customer.code", aLang));
            oTable.AddColumn(T("customer.name", aLang));
            oTable.AddColumn(T("customer.email", aLang));
            oTable.AddColumn(T("customer.phone", aLang));
            oTable.AddColumn(T("customer.city", aLang));
            oTable.AddColumn(T("common.actions", aLang), "text-end");

            if (aRows.Length == 0)
                oTable.AddEmptyRow(T("customer.none", aLang), 6);
            else
            {
                string vConfirm = T("common.confirm_delete", aLang);
                for (int vI = 0; vI < aRows.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    oRow.AddCellText(aRows[vI].Code);
                    oRow.AddCellText(aRows[vI].Name);
                    oRow.AddCellText(aRows[vI].Email);
                    oRow.AddCellText(aRows[vI].Phone);
                    oRow.AddCellText(aRows[vI].City);
                    // Actions: Edit link + per-row delete form (POST, confirm on submit).
                    var oActions = oRow.AddCell();
                    oActions.CellClass = "text-end";
                    oActions.AddRaw("<a href=\"/customers/edit?id=" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\" class=\"btn btn-sm btn-outline-secondary me-1\">" +
                        HtmlEsc(T("common.edit", aLang)) + "</a>");
                    oActions.AddRaw("<form method=\"post\" action=\"/customers/delete\" " +
                        "class=\"d-inline\" onsubmit=\"return confirm('" +
                        HtmlEsc(vConfirm).Replace("'", "\\'") +
                        "');\"><input type=\"hidden\" name=\"id\" value=\"" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\"><button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                        HtmlEsc(T("common.delete", aLang)) + "</button></form>");
                }
            }

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("customer.title", aLang), vBody, "customers",
                aDisplayName, aRole, aTheme, aLang);
        }

        // Customer add/edit form (inside shell): a card with a POST /customers/save
        // form laid out in a 2-column grid.
        public string BuildCustomerFormPage(TERPCustomer aCust, bool aIsNew, string aError,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            string vTitle;
            if (aIsNew)
                vTitle = T("customer.new", aLang);
            else
                vTitle = T("customer.edit", aLang);

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
            oForm.Action = "/customers/save";
            oForm.AddHidden("id", aCust.Id.ToString(CultureInfo.InvariantCulture));

            // 2-column grid (.row) hosting the scalar fields, each in its own col.
            var oRowGrid = new TsgcHTMLContainer("div");
            oRowGrid.CSSClass = "row";
            oForm.Add(oRowGrid);

            // Append a labeled half-width field to the 2-column grid.
            Action<string, string, string, TsgcHTMLInputType> addField =
                (aName, aLabelKey, aValue, aType) =>
            {
                var oField = new TsgcHTMLField(aType, aName);
                oField.FieldID = "customer-" + aName;
                oField.Label_ = T(aLabelKey, aLang);
                oField.Value = aValue;
                oField.ColClass = "col-md-6 mb-3";
                oRowGrid.Add(oField);
            };

            addField("code", "customer.code", aCust.Code, TsgcHTMLInputType.itText);
            addField("name", "customer.name", aCust.Name, TsgcHTMLInputType.itText);
            addField("tax_id", "customer.tax_id", aCust.TaxID, TsgcHTMLInputType.itText);
            addField("email", "customer.email", aCust.Email, TsgcHTMLInputType.itEmail);
            addField("phone", "customer.phone", aCust.Phone, TsgcHTMLInputType.itText);
            addField("address", "customer.address", aCust.Address, TsgcHTMLInputType.itText);
            addField("city", "customer.city", aCust.City, TsgcHTMLInputType.itText);
            addField("country", "customer.country", aCust.Country, TsgcHTMLInputType.itText);

            var oNotes = new TsgcHTMLTextArea("notes");
            oNotes.FieldID = "customer-notes";
            oNotes.Label_ = T("customer.notes", aLang);
            oNotes.Value = aCust.Notes;
            oNotes.Rows = 3;
            oNotes.ColClass = "col-12 mb-3";
            oRowGrid.Add(oNotes);

            // Save + Cancel.
            var oButtons = new TsgcHTMLContainer("div");
            oButtons.CSSClass = "d-flex gap-2";
            var oSave = new TsgcHTMLButton(T("common.save", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oSave.ButtonType = "submit";
            oButtons.Add(oSave);
            var oCancel = new TsgcHTMLButton(T("common.cancel", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oCancel.Href = "/customers";
            oButtons.Add(oCancel);
            oForm.Add(oButtons);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(vTitle, vBody, "customers", aDisplayName, aRole, aTheme, aLang);
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
        // providers
        // ---------------------------------------------------------------------

        // Providers list (inside shell): "New provider" link, a GET search form, and
        // a table of providers (code/name/email/phone/city + Actions).
        public string BuildProvidersPage(TERPProvider[] aRows, string aSearch,
            string aDisplayName, string aRole, string aTheme, string aLang, string aFlash)
        {
            var oRoot = new TsgcHTMLNodeList();

            // Heading + "New provider" button on one flex row.
            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "d-flex justify-content-between align-items-center mb-4";
            var oHeading = new TsgcHTMLHeading(T("provider.title", aLang), 1);
            oHeading.CSSClass = "mb-0";
            oHeader.Add(oHeading);
            var oNewBtn = new TsgcHTMLButton(T("provider.new", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oNewBtn.Href = "/providers/new";
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

            // Search form (GET so the query is shareable / bookmarkable).
            var oSearchForm = new TsgcHTMLForm();
            oSearchForm.Method = "GET";
            oSearchForm.Action = "/providers";
            oSearchForm.CSSClass = "mb-3";
            var oSearchRow = new TsgcHTMLContainer("div");
            oSearchRow.CSSClass = "row g-2";
            var oSearchCol = new TsgcHTMLContainer("div");
            oSearchCol.CSSClass = "col";
            var oSearchInput = new TsgcHTMLField(TsgcHTMLInputType.itText, "q");
            oSearchInput.FieldID = "provider-search";
            oSearchInput.Placeholder = T("common.search", aLang);
            oSearchInput.Value = aSearch;
            oSearchCol.Add(oSearchInput);
            oSearchRow.Add(oSearchCol);
            var oSearchBtnCol = new TsgcHTMLContainer("div");
            oSearchBtnCol.CSSClass = "col-auto";
            var oSearchBtn = new TsgcHTMLButton(T("common.search", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oSearchBtn.ButtonType = "submit";
            oSearchBtnCol.Add(oSearchBtn);
            oSearchRow.Add(oSearchBtnCol);
            oSearchForm.Add(oSearchRow);
            oCard.Body.Add(oSearchForm);

            // Providers table.
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.AddColumn(T("provider.code", aLang));
            oTable.AddColumn(T("provider.name", aLang));
            oTable.AddColumn(T("provider.email", aLang));
            oTable.AddColumn(T("provider.phone", aLang));
            oTable.AddColumn(T("provider.city", aLang));
            oTable.AddColumn(T("common.actions", aLang), "text-end");

            if (aRows.Length == 0)
                oTable.AddEmptyRow(T("provider.none", aLang), 6);
            else
            {
                string vConfirm = T("common.confirm_delete", aLang);
                for (int vI = 0; vI < aRows.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    oRow.AddCellText(aRows[vI].Code);
                    oRow.AddCellText(aRows[vI].Name);
                    oRow.AddCellText(aRows[vI].Email);
                    oRow.AddCellText(aRows[vI].Phone);
                    oRow.AddCellText(aRows[vI].City);
                    // Actions: Edit link + per-row delete form (POST, confirm on submit).
                    var oActions = oRow.AddCell();
                    oActions.CellClass = "text-end";
                    oActions.AddRaw("<a href=\"/providers/edit?id=" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\" class=\"btn btn-sm btn-outline-secondary me-1\">" +
                        HtmlEsc(T("common.edit", aLang)) + "</a>");
                    oActions.AddRaw("<form method=\"post\" action=\"/providers/delete\" " +
                        "class=\"d-inline\" onsubmit=\"return confirm('" +
                        HtmlEsc(vConfirm).Replace("'", "\\'") +
                        "');\"><input type=\"hidden\" name=\"id\" value=\"" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\"><button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                        HtmlEsc(T("common.delete", aLang)) + "</button></form>");
                }
            }

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("provider.title", aLang), vBody, "providers",
                aDisplayName, aRole, aTheme, aLang);
        }

        // Provider add/edit form (inside shell): a card with a POST /providers/save
        // form laid out in a 2-column grid.
        public string BuildProviderFormPage(TERPProvider aProv, bool aIsNew, string aError,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            string vTitle;
            if (aIsNew)
                vTitle = T("provider.new", aLang);
            else
                vTitle = T("provider.edit", aLang);

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
            oForm.Action = "/providers/save";
            oForm.AddHidden("id", aProv.Id.ToString(CultureInfo.InvariantCulture));

            // 2-column grid (.row) hosting the scalar fields, each in its own col.
            var oRowGrid = new TsgcHTMLContainer("div");
            oRowGrid.CSSClass = "row";
            oForm.Add(oRowGrid);

            Action<string, string, string, TsgcHTMLInputType> addField =
                (aName, aLabelKey, aValue, aType) =>
            {
                var oField = new TsgcHTMLField(aType, aName);
                oField.FieldID = "provider-" + aName;
                oField.Label_ = T(aLabelKey, aLang);
                oField.Value = aValue;
                oField.ColClass = "col-md-6 mb-3";
                oRowGrid.Add(oField);
            };

            addField("code", "provider.code", aProv.Code, TsgcHTMLInputType.itText);
            addField("name", "provider.name", aProv.Name, TsgcHTMLInputType.itText);
            addField("tax_id", "provider.tax_id", aProv.TaxID, TsgcHTMLInputType.itText);
            addField("email", "provider.email", aProv.Email, TsgcHTMLInputType.itEmail);
            addField("phone", "provider.phone", aProv.Phone, TsgcHTMLInputType.itText);
            addField("address", "provider.address", aProv.Address, TsgcHTMLInputType.itText);
            addField("city", "provider.city", aProv.City, TsgcHTMLInputType.itText);
            addField("country", "provider.country", aProv.Country, TsgcHTMLInputType.itText);

            var oNotes = new TsgcHTMLTextArea("notes");
            oNotes.FieldID = "provider-notes";
            oNotes.Label_ = T("provider.notes", aLang);
            oNotes.Value = aProv.Notes;
            oNotes.Rows = 3;
            oNotes.ColClass = "col-12 mb-3";
            oRowGrid.Add(oNotes);

            // Save + Cancel.
            var oButtons = new TsgcHTMLContainer("div");
            oButtons.CSSClass = "d-flex gap-2";
            var oSave = new TsgcHTMLButton(T("common.save", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oSave.ButtonType = "submit";
            oButtons.Add(oSave);
            var oCancel = new TsgcHTMLButton(T("common.cancel", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oCancel.Href = "/providers";
            oButtons.Add(oCancel);
            oForm.Add(oButtons);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(vTitle, vBody, "providers", aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // products
        // ---------------------------------------------------------------------

        // Products list (inside shell): "New product" link, a GET search form, and a
        // table of products (code/name/unit/price + Actions).
        public string BuildProductsPage(TERPProduct[] aRows, string aSearch,
            string aDisplayName, string aRole, string aTheme, string aLang, string aFlash)
        {
            var oRoot = new TsgcHTMLNodeList();

            // Heading + "New product" button on one flex row.
            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "d-flex justify-content-between align-items-center mb-4";
            var oHeading = new TsgcHTMLHeading(T("product.title", aLang), 1);
            oHeading.CSSClass = "mb-0";
            oHeader.Add(oHeading);
            var oNewBtn = new TsgcHTMLButton(T("product.new", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oNewBtn.Href = "/products/new";
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

            // Search form (GET so the query is shareable / bookmarkable).
            var oSearchForm = new TsgcHTMLForm();
            oSearchForm.Method = "GET";
            oSearchForm.Action = "/products";
            oSearchForm.CSSClass = "mb-3";
            var oSearchRow = new TsgcHTMLContainer("div");
            oSearchRow.CSSClass = "row g-2";
            var oSearchCol = new TsgcHTMLContainer("div");
            oSearchCol.CSSClass = "col";
            var oSearchInput = new TsgcHTMLField(TsgcHTMLInputType.itText, "q");
            oSearchInput.FieldID = "product-search";
            oSearchInput.Placeholder = T("common.search", aLang);
            oSearchInput.Value = aSearch;
            oSearchCol.Add(oSearchInput);
            oSearchRow.Add(oSearchCol);
            var oSearchBtnCol = new TsgcHTMLContainer("div");
            oSearchBtnCol.CSSClass = "col-auto";
            var oSearchBtn = new TsgcHTMLButton(T("common.search", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oSearchBtn.ButtonType = "submit";
            oSearchBtnCol.Add(oSearchBtn);
            oSearchRow.Add(oSearchBtnCol);
            oSearchForm.Add(oSearchRow);
            oCard.Body.Add(oSearchForm);

            // Products table.
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.AddColumn(T("product.code", aLang));
            oTable.AddColumn(T("product.name", aLang));
            oTable.AddColumn(T("product.unit", aLang));
            oTable.AddColumn(T("product.price", aLang), "text-end");
            oTable.AddColumn(T("common.actions", aLang), "text-end");

            if (aRows.Length == 0)
                oTable.AddEmptyRow(T("product.none", aLang), 5);
            else
            {
                string vConfirm = T("common.confirm_delete", aLang);
                for (int vI = 0; vI < aRows.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    oRow.AddCellText(aRows[vI].Code);
                    oRow.AddCellText(aRows[vI].Name);
                    oRow.AddCellText(aRows[vI].Unit_);
                    oRow.AddCellText(FmtNum2(aRows[vI].Price), "text-end");
                    // Actions: Edit link + per-row delete form (POST, confirm on submit).
                    var oActions = oRow.AddCell();
                    oActions.CellClass = "text-end";
                    oActions.AddRaw("<a href=\"/products/edit?id=" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\" class=\"btn btn-sm btn-outline-secondary me-1\">" +
                        HtmlEsc(T("common.edit", aLang)) + "</a>");
                    oActions.AddRaw("<form method=\"post\" action=\"/products/delete\" " +
                        "class=\"d-inline\" onsubmit=\"return confirm('" +
                        HtmlEsc(vConfirm).Replace("'", "\\'") +
                        "');\"><input type=\"hidden\" name=\"id\" value=\"" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\"><button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                        HtmlEsc(T("common.delete", aLang)) + "</button></form>");
                }
            }

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("product.title", aLang), vBody, "products",
                aDisplayName, aRole, aTheme, aLang);
        }

        // Product add/edit form (inside shell): a card with a POST /products/save
        // form. price/tax_rate are numeric, description is a TextArea.
        public string BuildProductFormPage(TERPProduct aProd, bool aIsNew, string aError,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            string vTitle;
            if (aIsNew)
                vTitle = T("product.new", aLang);
            else
                vTitle = T("product.edit", aLang);

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
            oForm.Action = "/products/save";
            oForm.AddHidden("id", aProd.Id.ToString(CultureInfo.InvariantCulture));

            // 2-column grid (.row) hosting the scalar fields, each in its own col.
            var oRowGrid = new TsgcHTMLContainer("div");
            oRowGrid.CSSClass = "row";
            oForm.Add(oRowGrid);

            Action<string, string, string, TsgcHTMLInputType> addField =
                (aName, aLabelKey, aValue, aType) =>
            {
                var oField = new TsgcHTMLField(aType, aName);
                oField.FieldID = "product-" + aName;
                oField.Label_ = T(aLabelKey, aLang);
                oField.Value = aValue;
                oField.ColClass = "col-md-6 mb-3";
                oRowGrid.Add(oField);
            };

            addField("code", "product.code", aProd.Code, TsgcHTMLInputType.itText);
            addField("name", "product.name", aProd.Name, TsgcHTMLInputType.itText);
            addField("unit", "product.unit", aProd.Unit_, TsgcHTMLInputType.itText);
            addField("price", "product.price", FmtNum2(aProd.Price), TsgcHTMLInputType.itNumber);
            addField("tax_rate", "product.tax_rate", FmtNum2(aProd.TaxRate), TsgcHTMLInputType.itNumber);

            var oDesc = new TsgcHTMLTextArea("description");
            oDesc.FieldID = "product-description";
            oDesc.Label_ = T("product.description", aLang);
            oDesc.Value = aProd.Description;
            oDesc.Rows = 3;
            oDesc.ColClass = "col-12 mb-3";
            oRowGrid.Add(oDesc);

            // Save + Cancel.
            var oButtons = new TsgcHTMLContainer("div");
            oButtons.CSSClass = "d-flex gap-2";
            var oSave = new TsgcHTMLButton(T("common.save", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oSave.ButtonType = "submit";
            oButtons.Add(oSave);
            var oCancel = new TsgcHTMLButton(T("common.cancel", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oCancel.Href = "/products";
            oButtons.Add(oCancel);
            oForm.Add(oButtons);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(vTitle, vBody, "products", aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // invoices
        // ---------------------------------------------------------------------

        // Map an invoice status to a Bootstrap badge variant + localized label.
        private static string InvoiceStatusBadge(string aStatus, string aLang)
        {
            string vStatus = (aStatus ?? string.Empty).Trim().ToLowerInvariant();
            string vVariant;
            string vKey;
            if (vStatus == "sent")
            {
                vVariant = "info";
                vKey = "invoice.status.sent";
            }
            else if (vStatus == "paid")
            {
                vVariant = "success";
                vKey = "invoice.status.paid";
            }
            else if (vStatus == "cancelled")
            {
                vVariant = "danger";
                vKey = "invoice.status.cancelled";
            }
            else
            {
                vVariant = "secondary";
                vKey = "invoice.status.draft";
            }
            return "<span class=\"badge bg-" + vVariant + "\">" + HtmlEsc(T(vKey, aLang)) + "</span>";
        }

        // Invoices list (inside shell): "New invoice" link, a GET filter form
        // (search text + a status select), and a table of invoices.
        public string BuildInvoicesPage(TERPInvoiceListRow[] aRows, string aSearch,
            string aStatus, string aDisplayName, string aRole, string aTheme, string aLang,
            string aFlash)
        {
            string vStatus = (aStatus ?? string.Empty).Trim().ToLowerInvariant();
            var oRoot = new TsgcHTMLNodeList();

            // Heading + "New invoice" button on one flex row.
            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "d-flex justify-content-between align-items-center mb-4";
            var oHeading = new TsgcHTMLHeading(T("invoice.title", aLang), 1);
            oHeading.CSSClass = "mb-0";
            oHeader.Add(oHeading);
            var oNewBtn = new TsgcHTMLButton(T("invoice.new", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oNewBtn.Href = "/invoices/new";
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

            // Filter form (GET): search text + status select.
            var oFilterForm = new TsgcHTMLForm();
            oFilterForm.Method = "GET";
            oFilterForm.Action = "/invoices";
            oFilterForm.CSSClass = "mb-3";
            var oFilterRow = new TsgcHTMLContainer("div");
            oFilterRow.CSSClass = "row g-2 align-items-end";

            var oSearchCol = new TsgcHTMLContainer("div");
            oSearchCol.CSSClass = "col";
            var oSearchInput = new TsgcHTMLField(TsgcHTMLInputType.itText, "q");
            oSearchInput.FieldID = "invoice-search";
            oSearchInput.Placeholder = T("common.search", aLang);
            oSearchInput.Value = aSearch;
            oSearchCol.Add(oSearchInput);
            oFilterRow.Add(oSearchCol);

            var oStatusCol = new TsgcHTMLContainer("div");
            oStatusCol.CSSClass = "col-auto";
            var oStatusSel = new TsgcHTMLSelect();
            oStatusSel.FieldID = "invoice-status-filter";
            oStatusSel.Name = "status";

            Action<string, string> addStatusOption = (aValue, aKey) =>
            {
                oStatusSel.AddOption(aValue, T(aKey, aLang),
                    string.Equals(vStatus, aValue, StringComparison.OrdinalIgnoreCase) ||
                    ((vStatus == "") && (aValue == "all")));
            };

            addStatusOption("all", "invoice.status.all");
            addStatusOption("draft", "invoice.status.draft");
            addStatusOption("sent", "invoice.status.sent");
            addStatusOption("paid", "invoice.status.paid");
            addStatusOption("cancelled", "invoice.status.cancelled");
            oStatusCol.Add(oStatusSel);
            oFilterRow.Add(oStatusCol);

            var oBtnCol = new TsgcHTMLContainer("div");
            oBtnCol.CSSClass = "col-auto";
            var oFilterBtn = new TsgcHTMLButton(T("common.search", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oFilterBtn.ButtonType = "submit";
            oBtnCol.Add(oFilterBtn);
            oFilterRow.Add(oBtnCol);

            oFilterForm.Add(oFilterRow);
            oCard.Body.Add(oFilterForm);

            // Invoices table.
            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";
            oTable.AddColumn(T("invoice.number", aLang));
            oTable.AddColumn(T("invoice.customer", aLang));
            oTable.AddColumn(T("invoice.issue_date", aLang));
            oTable.AddColumn(T("invoice.due_date", aLang));
            oTable.AddColumn(T("invoice.status", aLang));
            oTable.AddColumn(T("invoice.total", aLang), "text-end");
            oTable.AddColumn(T("common.actions", aLang), "text-end");

            if (aRows.Length == 0)
                oTable.AddEmptyRow(T("invoice.none", aLang), 7);
            else
            {
                string vConfirm = T("common.confirm_delete", aLang);
                for (int vI = 0; vI < aRows.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    oRow.AddCellText(aRows[vI].Number);
                    oRow.AddCellText(aRows[vI].CustomerName);
                    oRow.AddCellText(FmtDateOnly(aRows[vI].IssueDate));
                    oRow.AddCellText(FmtDateOnly(aRows[vI].DueDate));
                    oRow.AddCellRaw(InvoiceStatusBadge(aRows[vI].Status, aLang));
                    oRow.AddCellText(FmtMoney(aRows[vI].Total, aRows[vI].Currency), "text-end");
                    // Actions: Edit link + per-row delete form (POST, confirm on submit).
                    var oActions = oRow.AddCell();
                    oActions.CellClass = "text-end";
                    oActions.AddRaw("<a href=\"/invoices/edit?id=" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\" class=\"btn btn-sm btn-outline-secondary me-1\">" +
                        HtmlEsc(T("common.edit", aLang)) + "</a>");
                    oActions.AddRaw("<form method=\"post\" action=\"/invoices/delete\" " +
                        "class=\"d-inline\" onsubmit=\"return confirm('" +
                        HtmlEsc(vConfirm).Replace("'", "\\'") +
                        "');\"><input type=\"hidden\" name=\"id\" value=\"" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "\"><button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                        HtmlEsc(T("common.delete", aLang)) + "</button></form>");
                }
            }

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("invoice.title", aLang), vBody, "invoices",
                aDisplayName, aRole, aTheme, aLang);
        }

        // Inline JS for the invoice form: clone a hidden <tr> template to add line
        // rows, remove rows, and recompute each line total + subtotal/tax/total.
        // Assigned to TsgcHTMLScript.Code (no <script> wrapper here).
        private static string InvoiceFormScript()
        {
            return "(function(){" + "function num(v){var n=parseFloat(v);" +
                "return isNaN(n)?0:n;}" + "function fmt(n){return n.toFixed(2);}" +
                "var tbody=document.getElementById('merp-lines');" +
                "var tmpl=document.getElementById('merp-line-tmpl');" +
                "var rateEl=document.getElementById('invoice-tax_rate');" +
                "var subEl=document.getElementById('merp-subtotal');" +
                "var taxEl=document.getElementById('merp-tax');" +
                "var totEl=document.getElementById('merp-total');" +
                "function recalc(){var sub=0;" + "var rows=tbody.querySelectorAll('tr');" +
                "for(var i=0;i<rows.length;i++){" +
                "var q=rows[i].querySelector('.merp-qty');" +
                "var p=rows[i].querySelector('.merp-price');" +
                "var lt=rows[i].querySelector('.merp-ltotal');" + "if(!q||!p)continue;" +
                "var line=num(q.value)*num(p.value);" + "if(lt)lt.value=fmt(line);" +
                "sub+=line;}" + "var rate=rateEl?num(rateEl.value):0;" +
                "var tax=sub*rate/100;" + "if(subEl)subEl.value=fmt(sub);" +
                "if(taxEl)taxEl.value=fmt(tax);" + "if(totEl)totEl.value=fmt(sub+tax);}" +
                "function onProduct(sel,row){" + "var opt=sel.options[sel.selectedIndex];" +
                "if(!opt)return;" + "if(opt.value&&opt.value!==\"0\"){" +
                "var d=row.querySelector('.merp-desc');" +
                "var p=row.querySelector('.merp-price');" +
                "if(d)d.value=opt.getAttribute('data-desc')||'';" +
                "if(p)p.value=opt.getAttribute('data-price')||'';}" + "recalc();}" +
                "function wire(row){" +
                "var ins=row.querySelectorAll('.merp-qty,.merp-price');" +
                "for(var i=0;i<ins.length;i++){ins[i].addEventListener('input',recalc);}" +
                "var sel=row.querySelector('.merp-product');" +
                "if(sel){sel.addEventListener('change',function(){onProduct(sel,row);});}" +
                "var rm=row.querySelector('.merp-remove');" +
                "if(rm){rm.addEventListener('click',function(){" +
                "row.parentNode.removeChild(row);recalc();});}}" +
                "function addRow(){var html=tmpl.innerHTML;" +
                "var wrap=document.createElement('tbody');" +
                "wrap.innerHTML=html.trim();" + "var row=wrap.firstChild;" +
                "tbody.appendChild(row);wire(row);return row;}" +
                "var addBtn=document.getElementById('merp-add-line');" +
                "if(addBtn){addBtn.addEventListener('click',function(){addRow();});}" +
                "if(rateEl){rateEl.addEventListener('input',recalc);}" +
                "var existing=tbody.querySelectorAll('tr');" +
                "for(var i=0;i<existing.length;i++){wire(existing[i]);}" +
                "if(existing.length===0){addRow();}" + "recalc();" + "})();";
        }

        // Invoice add/edit form (inside shell): header fields + an editable line-
        // items table driven by inline JS that recomputes line totals live.
        public string BuildInvoiceFormPage(TERPInvoice aInv, TERPInvoiceLine[] aLines,
            TERPCustomer[] aCustomers, TERPProduct[] aProducts, bool aIsNew, string aError,
            string aDisplayName, string aRole, string aTheme, string aLang)
        {
            string vTitle;
            if (aIsNew)
                vTitle = T("invoice.new", aLang);
            else
                vTitle = T("invoice.edit", aLang);

            string vCurrency = (aInv.Currency ?? string.Empty).Trim();
            if (vCurrency == "")
                vCurrency = "EUR";
            string vStatus = (aInv.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (vStatus == "")
                vStatus = "draft";

            // The <option> list for a line product select.
            Func<long, string> productOptions = (aSelected) =>
            {
                string vSel;
                if (aSelected <= 0)
                    vSel = " selected";
                else
                    vSel = "";
                string vResult = "<option value=\"0\"" + vSel + ">" +
                    HtmlEsc(T("invoice.line.product_none", aLang)) + "</option>";
                for (int vJ = 0; vJ < aProducts.Length; vJ++)
                {
                    if (aProducts[vJ].Id == aSelected)
                        vSel = " selected";
                    else
                        vSel = "";
                    vResult = vResult + "<option value=\"" +
                        aProducts[vJ].Id.ToString(CultureInfo.InvariantCulture) + "\"" +
                        vSel + " data-desc=\"" + HtmlEsc(aProducts[vJ].Name) + "\" data-price=\"" +
                        HtmlEsc(FmtNum2(aProducts[vJ].Price)) + "\">" +
                        HtmlEsc(aProducts[vJ].Name) + "</option>";
                }
                return vResult;
            };

            // Build one editable <tr> for the lines table.
            Func<long, string, string, string, string, string> lineRowHtml =
                (aProductId, aDesc, aQty, aPrice, aLineTotal) =>
            {
                return "<tr>" +
                    "<td style=\"min-width:11rem;\"><select name=\"line_product\" " +
                    "class=\"form-select form-select-sm merp-product\">" +
                    productOptions(aProductId) + "</select></td>" +
                    "<td><input type=\"text\" name=\"line_desc\" " +
                    "class=\"form-control form-control-sm merp-desc\" value=\"" + HtmlEsc(aDesc) +
                    "\"></td>" +
                    "<td style=\"max-width:7rem;\"><input type=\"number\" step=\"any\" " +
                    "name=\"line_qty\" class=\"form-control form-control-sm merp-qty\" value=\"" +
                    HtmlEsc(aQty) + "\"></td>" +
                    "<td style=\"max-width:9rem;\"><input type=\"number\" step=\"any\" " +
                    "name=\"line_price\" class=\"form-control form-control-sm merp-price\" " +
                    "value=\"" + HtmlEsc(aPrice) + "\"></td>" +
                    "<td style=\"max-width:9rem;\"><input type=\"text\" readonly " +
                    "class=\"form-control form-control-sm merp-ltotal\" value=\"" +
                    HtmlEsc(aLineTotal) + "\"></td>" + "<td class=\"text-end\">" +
                    "<button type=\"button\" " +
                    "class=\"btn btn-sm btn-outline-danger merp-remove\">" +
                    HtmlEsc(T("invoice.remove", aLang)) + "</button></td>" + "</tr>";
            };

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
            oForm.Action = "/invoices/save";
            oForm.AddHidden("id", aInv.Id.ToString(CultureInfo.InvariantCulture));

            // ---- header fields (2-column grid) ----
            var oRowGrid = new TsgcHTMLContainer("div");
            oRowGrid.CSSClass = "row";
            oForm.Add(oRowGrid);

            var oNumber = new TsgcHTMLField(TsgcHTMLInputType.itText, "number");
            oNumber.FieldID = "invoice-number";
            oNumber.Label_ = T("invoice.number", aLang);
            oNumber.Value = aInv.Number;
            oNumber.ColClass = "col-md-6 mb-3";
            oRowGrid.Add(oNumber);

            var oCustomer = new TsgcHTMLSelect();
            oCustomer.FieldID = "invoice-customer";
            oCustomer.Name = "customer_id";
            oCustomer.Label_ = T("invoice.customer", aLang);
            oCustomer.ColClass = "col-md-6 mb-3";
            oCustomer.AddOption("", T("invoice.customer_none", aLang), aInv.CustomerId <= 0);
            for (int vI = 0; vI < aCustomers.Length; vI++)
                oCustomer.AddOption(aCustomers[vI].Id.ToString(CultureInfo.InvariantCulture),
                    aCustomers[vI].Name, aCustomers[vI].Id == aInv.CustomerId);
            oRowGrid.Add(oCustomer);

            var oIssue = new TsgcHTMLField(TsgcHTMLInputType.itDate, "issue_date");
            oIssue.FieldID = "invoice-issue_date";
            oIssue.Label_ = T("invoice.issue_date", aLang);
            oIssue.Value = FmtDateInput(aInv.IssueDate);
            oIssue.ColClass = "col-md-3 mb-3";
            oRowGrid.Add(oIssue);

            var oDue = new TsgcHTMLField(TsgcHTMLInputType.itDate, "due_date");
            oDue.FieldID = "invoice-due_date";
            oDue.Label_ = T("invoice.due_date", aLang);
            oDue.Value = FmtDateInput(aInv.DueDate);
            oDue.ColClass = "col-md-3 mb-3";
            oRowGrid.Add(oDue);

            var oStatus = new TsgcHTMLSelect();
            oStatus.FieldID = "invoice-status";
            oStatus.Name = "status";
            oStatus.Label_ = T("invoice.status", aLang);
            oStatus.ColClass = "col-md-3 mb-3";
            oStatus.AddOption("draft", T("invoice.status.draft", aLang), vStatus == "draft");
            oStatus.AddOption("sent", T("invoice.status.sent", aLang), vStatus == "sent");
            oStatus.AddOption("paid", T("invoice.status.paid", aLang), vStatus == "paid");
            oStatus.AddOption("cancelled", T("invoice.status.cancelled", aLang),
                vStatus == "cancelled");
            oRowGrid.Add(oStatus);

            var oCurrency = new TsgcHTMLField(TsgcHTMLInputType.itText, "currency");
            oCurrency.FieldID = "invoice-currency";
            oCurrency.Label_ = T("invoice.currency", aLang);
            oCurrency.Value = vCurrency;
            oCurrency.ColClass = "col-md-3 mb-3";
            oRowGrid.Add(oCurrency);

            var oTaxRate = new TsgcHTMLField(TsgcHTMLInputType.itNumber, "tax_rate");
            oTaxRate.FieldID = "invoice-tax_rate";
            oTaxRate.Label_ = T("invoice.tax_rate", aLang);
            oTaxRate.Value = FmtNum2(aInv.TaxRate);
            oTaxRate.ColClass = "col-md-3 mb-3";
            oRowGrid.Add(oTaxRate);

            // ---- line items section ----
            var oLinesHeading = new TsgcHTMLHeading(T("invoice.line.description", aLang), 5);
            oLinesHeading.CSSClass = "mt-2 mb-2";
            // Use a generic section heading instead of the column label.
            oLinesHeading.Text = T("nav.invoices", aLang);
            oForm.Add(oLinesHeading);

            // Editable lines table (header + the dynamic tbody #merp-lines).
            string vTableHtml = "<div class=\"table-responsive\">" +
                "<table class=\"table table-sm align-middle\">" + "<thead><tr><th>" +
                HtmlEsc(T("invoice.line.product", aLang)) + "</th><th>" +
                HtmlEsc(T("invoice.line.description", aLang)) + "</th><th>" +
                HtmlEsc(T("invoice.line.qty", aLang)) + "</th><th>" +
                HtmlEsc(T("invoice.line.price", aLang)) + "</th><th>" +
                HtmlEsc(T("invoice.line.total", aLang)) + "</th><th></th></tr></thead>" +
                "<tbody id=\"merp-lines\">";
            for (int vI = 0; vI < aLines.Length; vI++)
                vTableHtml = vTableHtml + lineRowHtml(aLines[vI].ProductId,
                    aLines[vI].Description, FmtNum2(aLines[vI].Quantity),
                    FmtNum2(aLines[vI].UnitPrice), FmtNum2(aLines[vI].LineTotal));
            vTableHtml = vTableHtml + "</tbody></table></div>";
            oForm.AddRaw(vTableHtml);

            // Hidden row template used by the JS to clone new blank lines.
            string vRowHtml = lineRowHtml(0, "", "", "", "");
            oForm.AddRaw("<template id=\"merp-line-tmpl\">" + vRowHtml + "</template>");

            var oAddBtn = new TsgcHTMLButton(T("invoice.add_line", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oAddBtn.ButtonType = "button";
            oAddBtn.CSSClass = "btn-sm mb-3";
            oAddBtn.Attributes = "id=\"merp-add-line\"";
            oForm.Add(oAddBtn);

            // ---- totals (read-only, recomputed by JS) ----
            var oTotalsGrid = new TsgcHTMLContainer("div");
            oTotalsGrid.CSSClass = "row";
            oForm.Add(oTotalsGrid);

            // Append a labeled read-only totals field (raw input so we can emit the
            // 'readonly' attribute, which TsgcHTMLField does not expose).
            Action<string, string, string, string> addTotalField =
                (aName, aLabelKey, aFieldId, aValue) =>
            {
                oTotalsGrid.AddRaw("<div class=\"col-md-4 mb-3\">" +
                    "<label class=\"form-label\" for=\"" + aFieldId + "\">" +
                    HtmlEsc(T(aLabelKey, aLang)) + "</label>" + "<input type=\"text\" id=\"" +
                    aFieldId + "\" name=\"" + aName + "\" class=\"form-control\" readonly " +
                    "value=\"" + HtmlEsc(aValue) + "\"></div>");
            };

            addTotalField("subtotal", "invoice.subtotal", "merp-subtotal", FmtNum2(aInv.Subtotal));
            addTotalField("tax_amount", "invoice.tax", "merp-tax", FmtNum2(aInv.TaxAmount));
            addTotalField("total", "invoice.total", "merp-total", FmtNum2(aInv.Total));

            var oNotes = new TsgcHTMLTextArea("notes");
            oNotes.FieldID = "invoice-notes";
            oNotes.Label_ = T("invoice.notes", aLang);
            oNotes.Value = aInv.Notes;
            oNotes.Rows = 2;
            oNotes.ColClass = "col-12 mb-3";
            oForm.Add(oNotes);

            // Save + Cancel.
            var oButtons = new TsgcHTMLContainer("div");
            oButtons.CSSClass = "d-flex gap-2";
            var oSave = new TsgcHTMLButton(T("common.save", aLang), TsgcHTMLButtonStyle.bsPrimary);
            oSave.ButtonType = "submit";
            oButtons.Add(oSave);
            var oCancel = new TsgcHTMLButton(T("common.cancel", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oCancel.Href = "/invoices";
            oButtons.Add(oCancel);
            oForm.Add(oButtons);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            // Inline JS that powers add/remove lines + live totals.
            var oScript = new TsgcHTMLScript();
            oScript.Code = InvoiceFormScript();
            oRoot.Add(oScript);

            string vBody = oRoot.HTML;
            return BuildPageShell(vTitle, vBody, "invoices", aDisplayName, aRole, aTheme, aLang);
        }

        // ---------------------------------------------------------------------
        // reports
        // ---------------------------------------------------------------------

        // Reports / Statistics page (inside shell, any logged-in user).
        public string BuildReportsPage(string aGranularity, TERPRevenueMonth[] aSeries,
            int aCurCount, double aCurRevenue, int aCurNewCustomers, int aPrevCount,
            double aPrevRevenue, int aPrevNewCustomers, TERPInvoiceListRow[] aTopCustomers,
            string aCurrency, string aDisplayName, string aRole, string aTheme, string aLang)
        {
            // Normalize the granularity to one of day/week/month.
            string vG = (aGranularity ?? string.Empty).Trim().ToLowerInvariant();
            if ((vG != "day") && (vG != "week") && (vG != "month"))
                vG = "month";

            // "per day | per week | per month" suffix for the chart titles.
            string vPerKey;
            if (vG == "day")
                vPerKey = "reports.per_day";
            else if (vG == "week")
                vPerKey = "reports.per_week";
            else
                vPerKey = "reports.per_month";
            string vTrendTitle = T("reports.revenue_trend", aLang) + " (" +
                T(vPerKey, aLang) + ")";

            // Average invoice value = revenue / count for each period.
            double vCurAvg;
            double vPrevAvg;
            if (aCurCount > 0)
                vCurAvg = aCurRevenue / aCurCount;
            else
                vCurAvg = 0;
            if (aPrevCount > 0)
                vPrevAvg = aPrevRevenue / aPrevCount;
            else
                vPrevAvg = 0;

            // Format a period-over-period delta: a coloured span.
            Func<double, double, bool, string> deltaSpan = (aCur, aPrev, aIsMoney) =>
            {
                string vClass;
                string vText;
                if (aPrev == 0)
                {
                    if (aCur == 0)
                        return "<span class=\"text-muted small fw-semibold\">&mdash;</span>";
                    vClass = "text-success";
                    vText = "&#9650; " + T("reports.new", aLang);
                }
                else
                {
                    double vPct = ((aCur - aPrev) * 100.0) / aPrev;
                    string vSign;
                    if (vPct > 0)
                    {
                        vClass = "text-success";
                        vSign = "&#9650; +";
                    }
                    else if (vPct < 0)
                    {
                        vClass = "text-danger";
                        vSign = "&#9660; ";
                    }
                    else
                    {
                        vClass = "text-muted";
                        vSign = "";
                    }
                    vText = vSign + vPct.ToString("0.0", CultureInfo.InvariantCulture) + "%";
                }
                return "<span class=\"small fw-semibold " + vClass + "\">" + vText +
                    "</span> <span class=\"text-muted small\">" +
                    HtmlEsc(T("reports.vs_previous", aLang)) + "</span>";
            };

            var oRoot = new TsgcHTMLNodeList();

            // --- heading + subtitle --- //
            var oHeading = new TsgcHTMLHeading(T("reports.title", aLang), 1);
            oHeading.CSSClass = "mb-1";
            oRoot.Add(oHeading);
            var oSub = new TsgcHTMLParagraph(T("reports.subtitle", aLang));
            oSub.CSSClass = "text-muted mb-4";
            oRoot.Add(oSub);

            // --- granularity selector (GET, auto-submit on change) --- //
            var oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/reports";
            oForm.CSSClass = "row g-3 mb-4 align-items-end";
            var oColG = new TsgcHTMLContainer("div");
            oColG.CSSClass = "col-auto";
            var oSelect = new TsgcHTMLSelect();
            oSelect.Name = "g";
            oSelect.Label_ = T("reports.granularity", aLang);
            oSelect.OnChange = "this.form.submit();";
            oSelect.AddOption("day", T("reports.day", aLang), vG == "day");
            oSelect.AddOption("week", T("reports.week", aLang), vG == "week");
            oSelect.AddOption("month", T("reports.month", aLang), vG == "month");
            oColG.Add(oSelect);
            oForm.Add(oColG);
            oRoot.Add(oForm);

            // --- comparison KPI cards (current vs previous period) --- //
            var oKpiRow = new TsgcHTMLContainer("div");
            oKpiRow.CSSClass = "row g-3 mb-4";

            // One comparison KPI card.
            Action<string, string, string, string, string, string, string> addCompareCard =
                (aLabelKey, aCurText, aPrevText, aDelta, aIcon, aBorder, aTint) =>
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-6 col-lg-3";

                var oKpi = new TsgcHTMLCard();
                oKpi.CSSClass = "h-100 shadow-sm";
                oKpi.BodyClass = "card-body";

                var oFlex = new TsgcHTMLContainer("div");
                oFlex.CSSClass = "d-flex align-items-start justify-content-between";

                var oText = new TsgcHTMLContainer("div");
                var oLbl = new TsgcHTMLParagraph(T(aLabelKey, aLang));
                oLbl.CSSClass = "text-muted mb-1 small text-uppercase fw-semibold";
                oText.Add(oLbl);
                var oNum = new TsgcHTMLHeading(aCurText, 3);
                oNum.CSSClass = "fw-bold mb-1";
                oText.Add(oNum);
                oText.AddRaw("<div class=\"text-muted small mb-1\">" +
                    HtmlEsc(T("reports.previous", aLang)) + ": <span class=\"fw-semibold\">" +
                    HtmlEsc(aPrevText) + "</span></div>");
                oText.AddRaw("<div>" + aDelta + "</div>");
                oFlex.Add(oText);

                var oIcon = new TsgcHTMLContainer("div");
                oIcon.CSSClass = "d-flex align-items-center justify-content-center rounded-circle";
                oIcon.Style = "width:48px;height:48px;flex:0 0 48px;background:" + aTint +
                    ";color:" + aBorder + ";";
                oIcon.AddRaw(aIcon);
                oFlex.Add(oIcon);

                oKpi.Body.Add(oFlex);

                oCol.AddRaw("<div style=\"border-left:4px solid " + aBorder +
                    ";border-radius:12px;\">");
                oCol.Add(oKpi);
                oCol.AddRaw("</div>");
                oKpiRow.Add(oCol);
            };

            addCompareCard("reports.invoices",
                aCurCount.ToString(CultureInfo.InvariantCulture),
                aPrevCount.ToString(CultureInfo.InvariantCulture),
                deltaSpan(aCurCount, aPrevCount, false), CS_ICON_INVOICES, "#10B981", "#D1FAE5");
            addCompareCard("reports.revenue", FmtMoney(aCurRevenue, aCurrency),
                FmtMoney(aPrevRevenue, aCurrency), deltaSpan(aCurRevenue, aPrevRevenue, true),
                CS_ICON_REVENUE, "#F0A400", "#FFF8E5");
            addCompareCard("reports.new_customers",
                aCurNewCustomers.ToString(CultureInfo.InvariantCulture),
                aPrevNewCustomers.ToString(CultureInfo.InvariantCulture),
                deltaSpan(aCurNewCustomers, aPrevNewCustomers, false), CS_ICON_CUSTOMERS,
                "#0EA5A5", "#E3F6F5");
            addCompareCard("reports.avg_invoice", FmtMoney(vCurAvg, aCurrency),
                FmtMoney(vPrevAvg, aCurrency), deltaSpan(vCurAvg, vPrevAvg, true),
                CS_ICON_PRODUCTS, "#10B981", "#D1FAE5");
            oRoot.Add(oKpiRow);

            // --- revenue bar chart (full width) --- //
            var oChart = new TsgcHTMLMiniBarChart();
            oChart.Title = vTrendTitle;
            oChart.CardClass = "card shadow-sm mb-4";
            oChart.Height = "240px";
            oChart.BarClass = "bg-primary";
            oChart.ValueClass = "small text-muted";
            oChart.EmptyText = T("reports.no_data", aLang);
            // Day granularity packs 30 buckets, so let it scroll if it overflows.
            if (vG == "day")
            {
                oChart.Scrollable = true;
                oChart.MinWidth = "900px";
            }
            for (int vI = 0; vI < aSeries.Length; vI++)
                oChart.AddBar(aSeries[vI].MonthLabel, aSeries[vI].Total,
                    FmtMoney(aSeries[vI].Total, aCurrency));
            oRoot.AddRaw(oChart.HTML);

            // --- invoice-count bar chart + top customers (2 columns) --- //
            var oSplitRow = new TsgcHTMLContainer("div");
            oSplitRow.CSSClass = "row g-3 mb-4";

            // Left: invoices-issued-per-bucket count chart.
            var oChartCol = new TsgcHTMLContainer("div");
            oChartCol.CSSClass = "col-12 col-lg-7";
            var oCountChart = new TsgcHTMLMiniBarChart();
            oCountChart.Title = T("reports.count_trend", aLang) + " (" + T(vPerKey, aLang) + ")";
            oCountChart.CardClass = "card shadow-sm h-100";
            oCountChart.Height = "220px";
            oCountChart.BarClass = "bg-info";
            oCountChart.ValueClass = "small text-muted";
            oCountChart.EmptyText = T("reports.no_data", aLang);
            if (vG == "day")
            {
                oCountChart.Scrollable = true;
                oCountChart.MinWidth = "900px";
            }
            for (int vI = 0; vI < aSeries.Length; vI++)
                oCountChart.AddBar(aSeries[vI].MonthLabel, aSeries[vI].Cnt,
                    aSeries[vI].Cnt.ToString(CultureInfo.InvariantCulture));
            oChartCol.AddRaw(oCountChart.HTML);
            oSplitRow.Add(oChartCol);

            // Right: top customers by revenue table.
            var oCustCol = new TsgcHTMLContainer("div");
            oCustCol.CSSClass = "col-12 col-lg-5";
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm h-100";
            oCard.BodyClass = "card-body";
            oCard.Body.Add(new TsgcHTMLHeading(T("reports.top_customers", aLang), 5));

            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm table-hover align-middle mb-0";
            oTable.TheadClass = "table-light";
            oTable.AddColumn(T("reports.customer", aLang));
            oTable.AddColumn(T("reports.invoices", aLang), "text-end");
            oTable.AddColumn(T("reports.revenue", aLang), "text-end");
            if (aTopCustomers.Length == 0)
                oTable.AddEmptyRow(T("reports.no_data", aLang), 3);
            else
                for (int vI = 0; vI < aTopCustomers.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    // CustomerName + Total carry the customer + revenue; CustomerId
                    // is repurposed to carry the per-customer invoice count.
                    oRow.AddCellText(aTopCustomers[vI].CustomerName);
                    oRow.AddCellText(
                        aTopCustomers[vI].CustomerId.ToString(CultureInfo.InvariantCulture),
                        "text-end");
                    oRow.AddCellRaw(HtmlEsc(FmtMoney(aTopCustomers[vI].Total, aCurrency)),
                        "text-end fw-semibold");
                }
            oCard.Body.Add(oTable);
            oCustCol.Add(oCard);
            oSplitRow.Add(oCustCol);
            oRoot.Add(oSplitRow);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("reports.title", aLang), vBody, "reports",
                aDisplayName, aRole, aTheme, aLang);
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
        // =====================================================================
        // customer-portal pages (role 'customer', inside shell)
        // =====================================================================

        // Customer account dashboard: a welcome line + a row of KPI summary cards
        // (number of orders, total spend, paid to date, outstanding balance) for
        // the signed-in customer, a recent-orders table (their invoices only) and
        // a couple of quick links. All figures are gathered + scoped by the server.
        public string BuildAccountDashboardPage(string aDisplayName, string aTheme,
            string aLang, int aOrderCount, double aTotalSpend, double aPaid,
            double aOutstanding, string aCurrency, TERPInvoiceListRow[] aRecent)
        {
            var oRoot = new TsgcHTMLNodeList();

            // --- gradient hero banner --- //
            var oHero = new TsgcHTMLContainer("div");
            oHero.CSSClass = "portal-hero mb-4";
            var oHeroH = new TsgcHTMLHeading(T("common.welcome", aLang) + ", " +
                aDisplayName, 1);
            oHero.Add(oHeroH);
            var oHeroP = new TsgcHTMLParagraph(T("account.welcome", aLang));
            oHero.Add(oHeroP);
            oRoot.Add(oHero);

            // --- three rounded summary cards (scoped to this customer) --- //
            var oKpiRow = new TsgcHTMLContainer("div");
            oKpiRow.CSSClass = "row g-3 mb-4";

            // One rounded white summary card (big number + label + emerald
            // inline-SVG icon), built with nodes.
            Action<string, string, string> addSummaryCard = (aLabelKey, aValue, aIcon) =>
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-12 col-md-4";
                var oSum = new TsgcHTMLCard();
                oSum.CSSClass = "portal-summary rounded-4 h-100";
                oSum.BodyClass = "card-body p-4";
                var oFlex = new TsgcHTMLContainer("div");
                oFlex.CSSClass = "d-flex align-items-center justify-content-between";
                var oText = new TsgcHTMLContainer("div");
                var oNum = new TsgcHTMLContainer("div");
                oNum.CSSClass = "value";
                oNum.AddText(aValue);
                oText.Add(oNum);
                var oLbl = new TsgcHTMLContainer("div");
                oLbl.CSSClass = "label";
                oLbl.AddText(T(aLabelKey, aLang));
                oText.Add(oLbl);
                oFlex.Add(oText);
                var oIcon = new TsgcHTMLContainer("div");
                oIcon.CSSClass = "icon";
                oIcon.AddRaw(aIcon);
                oFlex.Add(oIcon);
                oSum.Body.Add(oFlex);
                oCol.Add(oSum);
                oKpiRow.Add(oCol);
            };

            addSummaryCard("account.kpi.orders",
                aOrderCount.ToString(CultureInfo.InvariantCulture), CS_ICON_INVOICES);
            addSummaryCard("account.kpi.total_spend", FmtMoney(aTotalSpend, aCurrency),
                CS_ICON_REVENUE);
            addSummaryCard("account.kpi.outstanding", FmtMoney(aOutstanding, aCurrency),
                CS_ICON_REVENUE);
            oRoot.Add(oKpiRow);

            // --- recent orders TIMELINE --- //
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "rounded-4 mb-4";
            oCard.BodyClass = "card-body p-4";
            var oHeaderBar = new TsgcHTMLContainer("div");
            oHeaderBar.CSSClass = "d-flex justify-content-between align-items-center mb-3";
            oHeaderBar.Add(new TsgcHTMLHeading(T("account.recent_orders", aLang), 5));
            oHeaderBar.AddRaw("<a href=\"/orders\" class=\"btn btn-sm " +
                "btn-outline-primary\">" + HtmlEsc(T("account.view_orders", aLang)) +
                "</a>");
            oCard.Body.Add(oHeaderBar);

            if (aRecent.Length == 0)
                oCard.Body.Add(new TsgcHTMLParagraph(T("orders.none", aLang)));
            else
            {
                var oList = new TsgcHTMLContainer("ul");
                oList.CSSClass = "portal-timeline";

                // One timeline entry: a coloured status dot on the vertical line,
                // then a rounded "t-card" with order no (link) + date + amount + a
                // status pill.
                for (int vI = 0; vI < aRecent.Length; vI++)
                {
                    var aRow = aRecent[vI];
                    string vDot = TimelineDotClass(aRow.Status);
                    var oLi = new TsgcHTMLContainer("li");
                    if (vDot != "")
                        oLi.CSSClass = vDot;
                    var oTCard = new TsgcHTMLContainer("div");
                    oTCard.CSSClass = "t-card";
                    var oLeft = new TsgcHTMLContainer("div");
                    oLeft.AddRaw("<a href=\"/orders/view?id=" +
                        aRow.Id.ToString(CultureInfo.InvariantCulture) + "\">" +
                        HtmlEsc(aRow.Number) + "</a>");
                    oLeft.AddRaw("<div class=\"t-meta\">" +
                        HtmlEsc(FmtDateOnly(aRow.IssueDate)) + "</div>");
                    oTCard.Add(oLeft);
                    var oRight = new TsgcHTMLContainer("div");
                    oRight.CSSClass = "text-end";
                    oRight.AddRaw("<div class=\"fw-bold\">" +
                        HtmlEsc(FmtMoney(aRow.Total, aRow.Currency)) + "</div>");
                    oRight.AddRaw(InvoiceStatusBadge(aRow.Status, aLang));
                    oTCard.Add(oRight);
                    oLi.Add(oTCard);
                    oList.Add(oLi);
                }
                oCard.Body.Add(oList);
            }
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("account.title", aLang), vBody, "account",
                aDisplayName, "customer", aTheme, aLang);
        }

        // "My Orders" list (the signed-in customer's invoices only): a table of
        // number / date / due date / status badge / total with a per-row "View"
        // link to the order detail. Empty-state row when the customer has none.
        public string BuildMyOrdersPage(TERPInvoiceListRow[] aRows,
            string aDisplayName, string aTheme, string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oHeading = new TsgcHTMLHeading(T("orders.title", aLang), 1);
            oHeading.CSSClass = "mb-1";
            oRoot.Add(oHeading);
            var oSub = new TsgcHTMLParagraph(T("orders.subtitle", aLang));
            oSub.CSSClass = "text-muted mb-4";
            oRoot.Add(oSub);

            if (aRows.Length == 0)
            {
                oRoot.AddRaw("<div class=\"card rounded-4\"><div class=\"card-body p-5 " +
                    "text-center text-muted\">" + HtmlEsc(T("orders.none", aLang)) +
                    "</div></div>");
            }
            else
            {
                var oList = new TsgcHTMLContainer("div");

                // One order rendered as a rounded card row: number + date on the
                // left, a status pill + amount + a "View" link on the right.
                for (int vI = 0; vI < aRows.Length; vI++)
                {
                    var aRow = aRows[vI];
                    var oCard = new TsgcHTMLCard();
                    oCard.CSSClass = "rounded-4 mb-3";
                    oCard.BodyClass = "card-body p-3 px-md-4";
                    var oFlex = new TsgcHTMLContainer("div");
                    oFlex.CSSClass =
                        "d-flex flex-wrap align-items-center justify-content-between gap-3";
                    var oLeft = new TsgcHTMLContainer("div");
                    oLeft.AddRaw("<a href=\"/orders/view?id=" +
                        aRow.Id.ToString(CultureInfo.InvariantCulture) +
                        "\" class=\"fw-bold text-decoration-none fs-5\">" +
                        HtmlEsc(aRow.Number) + "</a>");
                    oLeft.AddRaw("<div class=\"text-muted small\">" +
                        HtmlEsc(FmtDateOnly(aRow.IssueDate)) + "</div>");
                    oFlex.Add(oLeft);
                    var oRight = new TsgcHTMLContainer("div");
                    oRight.CSSClass = "d-flex align-items-center gap-3";
                    oRight.AddRaw(InvoiceStatusBadge(aRow.Status, aLang));
                    oRight.AddRaw("<span class=\"fw-bold\">" +
                        HtmlEsc(FmtMoney(aRow.Total, aRow.Currency)) + "</span>");
                    var oView = new TsgcHTMLButton(T("orders.view", aLang),
                        TsgcHTMLButtonStyle.bsOutlinePrimary);
                    oView.Href = "/orders/view?id=" +
                        aRow.Id.ToString(CultureInfo.InvariantCulture);
                    oView.CSSClass = "btn-sm";
                    oRight.Add(oView);
                    oFlex.Add(oRight);
                    oCard.Body.Add(oFlex);
                    oList.Add(oCard);
                }
                oRoot.Add(oList);
            }

            string vBody = oRoot.HTML;
            return BuildPageShell(T("orders.title", aLang), vBody, "orders",
                aDisplayName, "customer", aTheme, aLang);
        }

        // Order (invoice) detail, read-only: a header description list (number /
        // dates / status / customer) plus the line-item table and the
        // subtotal / tax / total summary.
        public string BuildOrderDetailPage(TERPInvoice aInv, TERPInvoiceLine[] aLines,
            string aCustomerName, string aDisplayName, string aTheme, string aLang)
        {
            string vCurrency = (aInv.Currency ?? "").Trim();
            if (vCurrency == "")
                vCurrency = "EUR";

            var oRoot = new TsgcHTMLNodeList();

            var oHeading = new TsgcHTMLHeading(T("order.title", aLang) + " " +
                aInv.Number, 1);
            oHeading.CSSClass = "mb-3";
            oRoot.Add(oHeading);

            var oBackBtn = new TsgcHTMLButton(T("order.back", aLang),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBackBtn.Href = "/orders";
            oBackBtn.CSSClass = "btn-sm mb-3";
            oRoot.Add(oBackBtn);

            // --- header description list --- //
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4";
            oCard.BodyClass = "card-body";
            oCard.Body.Add(new TsgcHTMLHeading(T("order.detail", aLang), 5));

            Action<string, string> addDetail = (aLabelKey, aValueHtml) =>
            {
                oCard.Body.AddRaw("<div class=\"row py-1 border-bottom\">" +
                    "<div class=\"col-4 col-md-3 text-muted small text-uppercase " +
                    "fw-semibold\">" + HtmlEsc(T(aLabelKey, aLang)) + "</div>" +
                    "<div class=\"col-8 col-md-9\">" + aValueHtml + "</div></div>");
            };

            addDetail("invoice.number", HtmlEsc(aInv.Number));
            addDetail("invoice.customer", HtmlEsc(aCustomerName));
            addDetail("invoice.issue_date", HtmlEsc(FmtDateOnly(aInv.IssueDate)));
            addDetail("invoice.due_date", HtmlEsc(FmtDateOnly(aInv.DueDate)));
            addDetail("invoice.status", InvoiceStatusBadge(aInv.Status, aLang));
            if ((aInv.Notes ?? "").Trim() != "")
                addDetail("invoice.notes", HtmlEsc(aInv.Notes));
            oRoot.Add(oCard);

            // --- line items + totals --- //
            oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";
            oCard.Body.Add(new TsgcHTMLHeading(T("order.lines", aLang), 5));

            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-3";
            oTable.TheadClass = "table-light";
            oTable.AddColumn(T("invoice.line.description", aLang));
            oTable.AddColumn(T("invoice.line.qty", aLang), "text-end");
            oTable.AddColumn(T("invoice.line.price", aLang), "text-end");
            oTable.AddColumn(T("invoice.line.total", aLang), "text-end");
            if (aLines.Length == 0)
                oTable.AddEmptyRow(T("invoice.none", aLang), 4);
            else
                for (int vI = 0; vI < aLines.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    oRow.AddCellText(aLines[vI].Description);
                    oRow.AddCellText(FmtNum2(aLines[vI].Quantity), "text-end");
                    oRow.AddCellRaw(HtmlEsc(FmtMoney(aLines[vI].UnitPrice, vCurrency)),
                        "text-end");
                    oRow.AddCellRaw(HtmlEsc(FmtMoney(aLines[vI].LineTotal, vCurrency)),
                        "text-end fw-semibold");
                }
            oCard.Body.Add(oTable);

            // Totals summary (read-only).
            oCard.Body.AddRaw("<div class=\"row justify-content-end\"><div " +
                "class=\"col-12 col-md-5\">" +
                "<div class=\"d-flex justify-content-between py-1\">" + "<span>" +
                HtmlEsc(T("invoice.subtotal", aLang)) + "</span><span>" +
                HtmlEsc(FmtMoney(aInv.Subtotal, vCurrency)) + "</span></div>" +
                "<div class=\"d-flex justify-content-between py-1\">" + "<span>" +
                HtmlEsc(T("invoice.tax", aLang)) + "</span><span>" +
                HtmlEsc(FmtMoney(aInv.TaxAmount, vCurrency)) + "</span></div>" +
                "<div class=\"d-flex justify-content-between py-1 border-top fw-bold " +
                "fs-5\"><span>" + HtmlEsc(T("invoice.total", aLang)) + "</span><span>" +
                HtmlEsc(FmtMoney(aInv.Total, vCurrency)) + "</span></div>" +
                "</div></div>");
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("order.title", aLang) + " " + aInv.Number, vBody,
                "orders", aDisplayName, "customer", aTheme, aLang);
        }

        // "My Profile" form: lets the signed-in customer view + edit their own
        // contact details (name, email, phone, address, city, country) via a
        // POST /profile/save form. Read-only code / tax id are shown for context.
        public string BuildMyProfilePage(TERPCustomer aCust, string aError,
            string aFlash, string aDisplayName, string aTheme, string aLang)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oHeading = new TsgcHTMLHeading(T("profile.title", aLang), 1);
            oHeading.CSSClass = "mb-1";
            oRoot.Add(oHeading);
            var oSub = new TsgcHTMLParagraph(T("profile.subtitle", aLang));
            oSub.CSSClass = "text-muted mb-4";
            oRoot.Add(oSub);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "rounded-4";
            oCard.BodyClass = "card-body p-4";

            // --- large emerald avatar circle (initials) + the customer name --- //
            string vWhoName = (aCust.Name ?? "").Trim();
            if (vWhoName == "")
                vWhoName = aDisplayName;
            var oAvatarBar = new TsgcHTMLContainer("div");
            oAvatarBar.CSSClass = "d-flex align-items-center gap-3 mb-4";
            var oAvatar = new TsgcHTMLContainer("span");
            oAvatar.CSSClass = "portal-avatar portal-avatar-lg";
            oAvatar.AddText(InitialsOf(vWhoName));
            oAvatarBar.Add(oAvatar);
            var oWho = new TsgcHTMLContainer("div");
            oWho.AddRaw("<div class=\"h4 fw-bold mb-0\">" + HtmlEsc(vWhoName) + "</div>");
            if ((aCust.Email ?? "").Trim() != "")
                oWho.AddRaw("<div class=\"text-muted\">" + HtmlEsc(aCust.Email) + "</div>");
            oAvatarBar.Add(oWho);
            oCard.Body.Add(oAvatarBar);

            if (aFlash != "")
            {
                var oAlert = new TsgcHTMLAlert(aFlash);
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                oAlert.CSSClass = "mb-3";
                oCard.Body.Add(oAlert);
            }
            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oAlert.CSSClass = "mb-3";
                oCard.Body.Add(oAlert);
            }

            // A non-editable detail row (code / tax id) shown for context above the
            // editable fields. These are never part of the editable form.
            Action<string, string> addReadOnly = (aLabelKey, aValue) =>
            {
                oCard.Body.AddRaw("<div class=\"row py-1 border-bottom\">" +
                    "<div class=\"col-4 col-md-3 text-muted small text-uppercase " +
                    "fw-semibold\">" + HtmlEsc(T(aLabelKey, aLang)) + "</div>" +
                    "<div class=\"col-8 col-md-9\">" + HtmlEsc(aValue) + "</div></div>");
            };

            // Code + tax id are shown read-only for context; the rest are editable.
            // The server never trusts a customer_id field: it overwrites only the
            // row tied to the session.
            if ((aCust.Code ?? "").Trim() != "")
                addReadOnly("customer.code", aCust.Code);
            if ((aCust.TaxID ?? "").Trim() != "")
                addReadOnly("customer.tax_id", aCust.TaxID);

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/profile/save";

            var oRowGrid = new TsgcHTMLContainer("div");
            oRowGrid.CSSClass = "row mt-3";
            oForm.Add(oRowGrid);

            Action<string, string, string, TsgcHTMLInputType> addField =
                (aName, aLabelKey, aValue, aType) =>
            {
                var oField = new TsgcHTMLField(aType, aName);
                oField.FieldID = "profile-" + aName;
                oField.Label_ = T(aLabelKey, aLang);
                oField.Value = aValue;
                oField.ColClass = "col-md-6 mb-3";
                oRowGrid.Add(oField);
            };

            addField("name", "customer.name", aCust.Name, TsgcHTMLInputType.itText);
            addField("email", "customer.email", aCust.Email, TsgcHTMLInputType.itEmail);
            addField("phone", "customer.phone", aCust.Phone, TsgcHTMLInputType.itText);
            addField("address", "customer.address", aCust.Address,
                TsgcHTMLInputType.itText);
            addField("city", "customer.city", aCust.City, TsgcHTMLInputType.itText);
            addField("country", "customer.country", aCust.Country,
                TsgcHTMLInputType.itText);

            var oButtons = new TsgcHTMLContainer("div");
            oButtons.CSSClass = "d-flex gap-2";
            var oSave = new TsgcHTMLButton(T("common.save", aLang),
                TsgcHTMLButtonStyle.bsPrimary);
            oSave.ButtonType = "submit";
            oButtons.Add(oSave);
            oForm.Add(oButtons);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("profile.title", aLang), vBody, "profile",
                aDisplayName, "customer", aTheme, aLang);
        }

        // Simple support / contact page: how to reach the support team (email,
        // phone, hours), inside the shell.
        public string BuildSupportPage(string aDisplayName, string aTheme,
            string aLang, string aSupportEmail, string aSupportPhone)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oHeading = new TsgcHTMLHeading(T("support.title", aLang), 1);
            oHeading.CSSClass = "mb-1";
            oRoot.Add(oHeading);
            var oSub = new TsgcHTMLParagraph(T("support.subtitle", aLang));
            oSub.CSSClass = "text-muted mb-4";
            oRoot.Add(oSub);

            // --- contact info card --- //
            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "rounded-4 mb-4";
            oCard.BodyClass = "card-body p-4";
            var oIntro = new TsgcHTMLParagraph(T("support.intro", aLang));
            oIntro.CSSClass = "mb-3";
            oCard.Body.Add(oIntro);

            Action<string, string> addDetail = (aLabelKey, aValueHtml) =>
            {
                oCard.Body.AddRaw("<div class=\"row py-2 border-bottom\">" +
                    "<div class=\"col-4 col-md-3 text-muted small text-uppercase " +
                    "fw-semibold\">" + HtmlEsc(T(aLabelKey, aLang)) + "</div>" +
                    "<div class=\"col-8 col-md-9\">" + aValueHtml + "</div></div>");
            };

            addDetail("support.email", "<a href=\"mailto:" + HtmlEsc(aSupportEmail) +
                "\">" + HtmlEsc(aSupportEmail) + "</a>");
            addDetail("support.phone", HtmlEsc(aSupportPhone));
            addDetail("support.hours", HtmlEsc(T("support.hours_value", aLang)));
            oRoot.Add(oCard);

            // --- simple message form (opens the visitor's mail client) --- //
            var oFormCard = new TsgcHTMLCard();
            oFormCard.CSSClass = "rounded-4";
            oFormCard.BodyClass = "card-body p-4";
            oFormCard.Body.Add(new TsgcHTMLHeading(T("support.title", aLang), 5));
            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "mailto:" + aSupportEmail;
            oForm.Enctype = "text/plain";
            var oMsg = new TsgcHTMLTextArea("body");
            oMsg.FieldID = "support-message";
            oMsg.Label_ = T("support.intro", aLang);
            oMsg.Rows = 4;
            oMsg.CSSClass = "form-control mb-3";
            oForm.Add(oMsg);
            var oSend = new TsgcHTMLButton(T("common.save", aLang),
                TsgcHTMLButtonStyle.bsPrimary);
            oSend.ButtonType = "submit";
            oForm.Add(oSend);
            oFormCard.Body.Add(oForm);
            oRoot.Add(oFormCard);

            string vBody = oRoot.HTML;
            return BuildPageShell(T("support.title", aLang), vBody, "support",
                aDisplayName, "customer", aTheme, aLang);
        }

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
