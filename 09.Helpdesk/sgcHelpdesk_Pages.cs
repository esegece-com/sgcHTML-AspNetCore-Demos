// ***************************************************************************
//  sgcHelpdesk - support-ticket helpdesk web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\09.Helpdesk\sgcHelpdesk_Pages.pas
//
//  Zero custom HTML strings: every page below builds a sgcHTML node tree and
//  renders it to HTML (Node.HTML / NodeList.HTML). The only raw markup spliced
//  in is via AddRaw() for values already sgcHTML-escaped (HtmlEsc below) or
//  trusted constants, matching the Delphi convention.
// ***************************************************************************

using System;
using System.Globalization;
// sgc
using esegece.sgcWebSockets;

namespace Helpdesk
{
    // Node-based view layer for the Helpdesk demo (single English UI, themed
    // light/dark/system Bootstrap). Every in-app page renders inside a shared
    // shell (a top navbar + footer) built by BuildPageShell.
    public class THelpdeskPages
    {
        // Self-contained inline logo (brand mark + wordmark), centered above the
        // "Helpdesk" heading on the sign-in card.
        private const string CS_LOGO_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 220 64\" width=\"180\" height=\"52\" role=\"img\" " +
            "aria-label=\"Helpdesk\">" + "<rect x=\"0\" y=\"4\" width=\"56\" height=\"56\" " +
            "rx=\"12\" fill=\"#0D6EFD\"/>" +
            "<circle cx=\"28\" cy=\"32\" r=\"17\" fill=\"none\" stroke=\"#FFFFFF\" " +
            "stroke-width=\"6\"/>" +
            "<circle cx=\"28\" cy=\"32\" r=\"6\" fill=\"none\" stroke=\"#FFFFFF\" " +
            "stroke-width=\"4\"/>" +
            "<line x1=\"28\" y1=\"13\" x2=\"28\" y2=\"20\" stroke=\"#FFFFFF\" " +
            "stroke-width=\"4\"/>" + "<line x1=\"28\" y1=\"44\" x2=\"28\" y2=\"51\" " +
            "stroke=\"#FFFFFF\" stroke-width=\"4\"/>" +
            "<line x1=\"9\" y1=\"32\" x2=\"16\" y2=\"32\" stroke=\"#FFFFFF\" " +
            "stroke-width=\"4\"/>" + "<line x1=\"40\" y1=\"32\" x2=\"47\" y2=\"32\" " +
            "stroke=\"#FFFFFF\" stroke-width=\"4\"/>" +
            "<text x=\"70\" y=\"42\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"700\" font-size=\"30\" fill=\"#212529\">Helpdesk</text></svg>";

        // Base CSS shared by every theme.
        private const string CS_HELPDESK_BASE_CSS = "body{background:#f5f6f8;}" +
            ".navbar-brand{font-weight:700;}" +
            ".hd-thread .card{border-left:3px solid #6c757d;}" +
            ".hd-thread .card.hd-admin{border-left-color:#0d6efd;background:#f3f7ff;}" +
            ".table tbody tr[onclick]{cursor:pointer;}" +
            ".table tbody tr[onclick]:hover{background-color:#eef3ff;}";

        // Dark-mode overrides.
        private const string CS_THEME_DARK_RULES = "body{background:#1a1d23;color:#e4e6eb;}" +
            ".card{background:#2d3038;border:1px solid #3d4048;color:#e4e6eb;}" +
            ".card-header{background:#353840;border-bottom:1px solid #3d4048;}" +
            ".table{color:#e4e6eb;}" + ".text-muted{color:#8b8fa3 !important;}" +
            ".form-control,.form-select{background:#2d3038;border-color:#3d4048;color:#e4e6eb;}" +
            ".form-control:focus,.form-select:focus{background:#353840;color:#e4e6eb;border-color:#0d6efd;}" +
            ".list-group-item{background:#2d3038;border-color:#3d4048;color:#e4e6eb;}" +
            ".page-link{background:#2d3038;border-color:#3d4048;color:#e4e6eb;}" +
            ".page-item.active .page-link{background:#0d6efd;border-color:#0d6efd;}" +
            "a{color:#6ea8fe;}" +
            ".navbar.bg-white{background:#20232a !important;border-color:#3d4048 !important;}" +
            ".navbar.bg-white .navbar-brand,.navbar.bg-white .nav-link,.navbar.bg-white .text-muted{color:#e4e6eb !important;}" +
            ".dropdown-menu{background:#2d3038;border-color:#3d4048;}" +
            ".dropdown-item{color:#e4e6eb;}" +
            ".dropdown-item:hover,.dropdown-item:focus{background:#3d4048;color:#fff;}" +
            ".hd-thread .card{border-left-color:#495057;}" +
            ".hd-thread .card.hd-admin{border-left-color:#0d6efd;background:#1b263b;}" +
            ".table tbody tr[onclick]:hover{background-color:#2d3038;}";

        // ----- small text helpers ----- //

        // Minimal HTML-escape for text spliced into raw markup.
        private static string HtmlEsc(string aValue)
        {
            string vResult = (aValue ?? "").Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            return vResult;
        }

        // HTML-escape then turn line breaks into <br>.
        private static string HtmlEscNl(string aValue)
        {
            string vResult = HtmlEsc(aValue);
            vResult = vResult.Replace("\r\n", "<br>");
            vResult = vResult.Replace("\n", "<br>");
            return vResult;
        }

        private static string FmtDate(DateTime aValue)
        {
            if (aValue == DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // Human-readable file size (B / KB / MB).
        private static string FmtSize(long aBytes)
        {
            if (aBytes < 1024)
                return aBytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (aBytes < 1024 * 1024)
                return ((double)aBytes / 1024).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
            return ((double)aBytes / (1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        }

        // Status as a coloured Bootstrap badge pill.
        private static string StatusBadge(string aStatus)
        {
            if (string.Equals(aStatus, "closed", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLComponent_Badge.Build("Closed", TsgcHTMLBadgeStyle.bgSecondary, true);
            return TsgcHTMLComponent_Badge.Build("Open", TsgcHTMLBadgeStyle.bgSuccess, true);
        }

        // Maps a flash-message code to its display text. Blank/unknown -> "".
        private static string FlashMessage(string aCode)
        {
            if (string.Equals(aCode, "created", StringComparison.OrdinalIgnoreCase))
                return "Ticket created.";
            if (string.Equals(aCode, "replied", StringComparison.OrdinalIgnoreCase))
                return "Reply sent.";
            if (string.Equals(aCode, "closed", StringComparison.OrdinalIgnoreCase))
                return "Ticket closed.";
            if (string.Equals(aCode, "reopened", StringComparison.OrdinalIgnoreCase))
                return "Ticket reopened.";
            return "";
        }

        // ----- shell / template ----- //

        private string SystemThemeAutodetectScript()
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
                oTpl.CustomCSS = CS_HELPDESK_BASE_CSS + CS_THEME_DARK_RULES;
            }
            else if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
            {
                oTpl.HtmlTheme = "light";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_HELPDESK_BASE_CSS;
            }
            else
            {
                // System theme: no data-bs-theme; an autodetect script flips it.
                oTpl.HtmlTheme = "";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_HELPDESK_BASE_CSS +
                    "@media (prefers-color-scheme: dark){" + CS_THEME_DARK_RULES + "}";
                oTpl.HeadNodes.AddRaw(SystemThemeAutodetectScript());
            }
            oTpl.BodyContent = aBody;
            return oTpl.GetHTML();
        }

        private string BuildThemeDropdown(string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oLi = new TsgcHTMLContainer("li");
            oLi.CSSClass = "nav-item dropdown ms-2";

            string vCurLabel;
            if (string.Equals(aTheme, "light", StringComparison.OrdinalIgnoreCase))
                vCurLabel = "Light";
            else if (string.Equals(aTheme, "dark", StringComparison.OrdinalIgnoreCase))
                vCurLabel = "Dark";
            else
                vCurLabel = "System";

            var oToggle = new TsgcHTMLContainer("a");
            oToggle.Attributes = "class=\"nav-link dropdown-toggle\" href=\"#\" " +
                "id=\"hdThemeDropdown\" role=\"button\" data-bs-toggle=\"dropdown\" " +
                "aria-expanded=\"false\" aria-label=\"Theme: " + vCurLabel + "\"";
            oToggle.AddText(vCurLabel);
            oLi.Add(oToggle);

            var oUl = new TsgcHTMLContainer("ul");
            oUl.Attributes = "class=\"dropdown-menu dropdown-menu-end\" " +
                "aria-labelledby=\"hdThemeDropdown\"";

            Action<string, string> addThemeItem = (aValue, aLabel) =>
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
                oSpan.AddText(aLabel);
                oBtn.Add(oSpan);
                if (string.Equals(aTheme, aValue, StringComparison.OrdinalIgnoreCase))
                    oBtn.AddRaw(" <span class=\"ms-2\">&#10004;</span>");
                oForm.Add(oBtn);
                oItemLi.Add(oForm);
                oUl.Add(oItemLi);
            };

            addThemeItem("light", "Light");
            addThemeItem("dark", "Dark");
            addThemeItem("system", "System");
            oLi.Add(oUl);

            oRoot.Add(oLi);
            return oRoot.HTML;
        }

        private string BuildNavbar(string aDisplayName, string aRole, string aActiveMenu,
            string aTheme)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oNav = new TsgcHTMLContainer("nav");
            oNav.CSSClass = "navbar navbar-expand-md navbar-light bg-white shadow-sm mb-4";

            var oInner = new TsgcHTMLContainer("div");
            oInner.CSSClass = "container";

            var oBrand = new TsgcHTMLLink("/", "Helpdesk");
            oBrand.CSSClass = "navbar-brand";
            oInner.Add(oBrand);

            var oList = new TsgcHTMLContainer("ul");
            oList.CSSClass = "navbar-nav me-auto";

            Action<string, string, string> addNavLink = (aHref, aText, aMenu) =>
            {
                var oLi = new TsgcHTMLContainer("li");
                oLi.CSSClass = "nav-item";
                string vClass = "nav-link";
                if (string.Equals(aActiveMenu, aMenu, StringComparison.OrdinalIgnoreCase))
                    vClass += " active";
                var oLink = new TsgcHTMLLink(aHref, aText);
                oLink.CSSClass = vClass;
                oLi.Add(oLink);
                oList.Add(oLi);
            };

            if (string.Equals(aRole, "admin", StringComparison.OrdinalIgnoreCase))
            {
                addNavLink("/", "All Tickets", "tickets");
                addNavLink("/dashboard", "Dashboard", "dashboard");
            }
            else
            {
                addNavLink("/", "My Tickets", "tickets");
                addNavLink("/tickets/new", "New Ticket", "new");
            }
            oInner.Add(oList);

            var oRight = new TsgcHTMLContainer("ul");
            oRight.CSSClass = "navbar-nav ms-auto align-items-center";

            var oUserLi = new TsgcHTMLContainer("li");
            oUserLi.CSSClass = "nav-item me-2";
            var oUserSpan = new TsgcHTMLContainer("span");
            oUserSpan.CSSClass = "nav-link text-muted";
            oUserSpan.AddText(aDisplayName);
            oUserLi.Add(oUserSpan);
            oRight.Add(oUserLi);

            oRight.AddRaw(BuildThemeDropdown(aTheme));

            var oLogoutLi = new TsgcHTMLContainer("li");
            oLogoutLi.CSSClass = "nav-item";
            var oLogoutForm = new TsgcHTMLForm();
            oLogoutForm.Method = "POST";
            oLogoutForm.Action = "/logout";
            oLogoutForm.CSSClass = "m-0";
            var oLogoutBtn = new TsgcHTMLButton("Logout", TsgcHTMLButtonStyle.bsOutlineSecondary);
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
            var oRoot = new TsgcHTMLNodeList();

            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "border-top mt-4 py-4 text-center text-muted small";

            var oP = new TsgcHTMLContainer("p");
            oP.CSSClass = "mb-1";
            oP.AddText("Built with ");
            var oLink = new TsgcHTMLLink("https://www.esegece.com/products/sgchtml/", "sgcHTML");
            oLink.Target = "_blank";
            oLink.Rel = "noopener";
            oLink.CSSClass = "fw-semibold text-decoration-none";
            oP.Add(oLink);
            oP.AddText(" for Delphi and C++Builder.");
            oFooter.Add(oP);

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

        public string BuildPageShell(string aTitle, string aBodyHTML, string aActiveMenu,
            string aDisplayName, string aRole, string aTheme)
        {
            var oBody = new TsgcHTMLNodeList();
            oBody.AddRaw(BuildNavbar(aDisplayName, aRole, aActiveMenu, aTheme));

            var oMain = new TsgcHTMLContainer("main");
            oMain.CSSClass = "container pb-4";
            oMain.AddRaw(aBodyHTML);
            oBody.Add(oMain);

            oBody.AddRaw(BuildFooter());

            return WrapTemplate("Helpdesk - " + aTitle, oBody.HTML, aTheme);
        }

        private string BuildTicketQueryString(string aStatus, string aUser, string aSearch,
            string aSort, string aDir)
        {
            string vQS = "";
            if (!string.IsNullOrEmpty(aStatus))
                vQS += "&status=" + Uri.EscapeDataString(aStatus);
            if (!string.IsNullOrEmpty(aUser))
                vQS += "&user=" + Uri.EscapeDataString(aUser);
            if (!string.IsNullOrEmpty(aSearch))
                vQS += "&search=" + Uri.EscapeDataString(aSearch);
            if (!string.IsNullOrEmpty(aSort))
                vQS += "&sort=" + Uri.EscapeDataString(aSort);
            if (!string.IsNullOrEmpty(aDir))
                vQS += "&dir=" + Uri.EscapeDataString(aDir);
            if (vQS.Length > 0)
                return "?" + vQS.Substring(1);
            return "";
        }

        // ----- auth pages ----- //

        public string BuildLoginPage(string aTheme, string aError = "",
            string aDefaultUsername = "", string aDefaultPassword = "")
        {
            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row justify-content-center";
            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-md-6 col-lg-4";

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body p-4";

            var oLogoWrap = new TsgcHTMLContainer("div");
            oLogoWrap.CSSClass = "text-center mb-2";
            oLogoWrap.AddRaw(CS_LOGO_SVG);
            oCard.Body.Add(oLogoWrap);

            var oHeading = new TsgcHTMLHeading("Helpdesk", 1);
            oHeading.CSSClass = "card-title h3 mb-4 text-center";
            oCard.Body.Add(oHeading);

            if (!string.IsNullOrEmpty(aError))
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oCard.Body.Add(oAlert);
            }

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/login";

            var oUser = new TsgcHTMLField(TsgcHTMLInputType.itText, "username");
            oUser.FieldID = "username";
            oUser.Label_ = "Username";
            oUser.Autocomplete = "username";
            oUser.Value = aDefaultUsername;
            oUser.Required = true;
            oUser.ColClass = "mb-3";
            oForm.Add(oUser);

            var oPwd = new TsgcHTMLField(TsgcHTMLInputType.itPassword, "password");
            oPwd.FieldID = "password";
            oPwd.Label_ = "Password";
            oPwd.Autocomplete = "current-password";
            oPwd.Value = aDefaultPassword;
            oPwd.Required = true;
            oPwd.ColClass = "mb-3";
            oForm.Add(oPwd);

            var oSubmit = new TsgcHTMLButton("Sign in", TsgcHTMLButtonStyle.bsPrimary);
            oSubmit.ButtonType = "submit";
            oSubmit.CSSClass = "w-100";
            oForm.Add(oSubmit);

            oCard.Body.Add(oForm);

            var oRegisterP = new TsgcHTMLContainer("p");
            oRegisterP.CSSClass = "text-center mt-3 mb-0 small";
            oRegisterP.AddText("No account yet? ");
            var oRegisterLink = new TsgcHTMLLink("/register", "Register");
            oRegisterP.Add(oRegisterLink);
            oCard.Body.Add(oRegisterP);

            var oDemoP = new TsgcHTMLContainer("p");
            oDemoP.CSSClass = "text-center mt-2 mb-0 small text-muted";
            oDemoP.AddText("Demo accounts: admin / admin (admin), " +
                "alice / bob / carol with password demo1234 (user).");
            oCard.Body.Add(oDemoP);

            oCol.Add(oCard);
            oRow.Add(oCol);
            oRoot.Add(oRow);

            return WrapTemplate("Helpdesk - Sign in", "<div class=\"container py-5\">" +
                oRoot.HTML + "</div>" + BuildFooter(), aTheme);
        }

        public string BuildRegisterPage(string aTheme, string aError = "")
        {
            var oRoot = new TsgcHTMLNodeList();

            var oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row justify-content-center";
            var oCol = new TsgcHTMLContainer("div");
            oCol.CSSClass = "col-md-6 col-lg-4";

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body p-4";

            var oHeading = new TsgcHTMLHeading("Create an account", 1);
            oHeading.CSSClass = "card-title h3 mb-4 text-center";
            oCard.Body.Add(oHeading);

            if (!string.IsNullOrEmpty(aError))
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oCard.Body.Add(oAlert);
            }

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/register";

            var oUser = new TsgcHTMLField(TsgcHTMLInputType.itText, "username");
            oUser.FieldID = "reg-username";
            oUser.Label_ = "Username";
            oUser.Autocomplete = "username";
            oUser.Required = true;
            oUser.ColClass = "mb-3";
            oForm.Add(oUser);

            var oPwd = new TsgcHTMLField(TsgcHTMLInputType.itPassword, "password");
            oPwd.FieldID = "reg-password";
            oPwd.Label_ = "Password";
            oPwd.Autocomplete = "new-password";
            oPwd.Required = true;
            oPwd.ColClass = "mb-3";
            oForm.Add(oPwd);

            var oPwd2 = new TsgcHTMLField(TsgcHTMLInputType.itPassword, "password_confirm");
            oPwd2.FieldID = "reg-password-confirm";
            oPwd2.Label_ = "Confirm password";
            oPwd2.Autocomplete = "new-password";
            oPwd2.Required = true;
            oPwd2.ColClass = "mb-3";
            oForm.Add(oPwd2);

            var oSubmit = new TsgcHTMLButton("Register", TsgcHTMLButtonStyle.bsPrimary);
            oSubmit.ButtonType = "submit";
            oSubmit.CSSClass = "w-100";
            oForm.Add(oSubmit);

            oCard.Body.Add(oForm);

            var oLoginP = new TsgcHTMLContainer("p");
            oLoginP.CSSClass = "text-center mt-3 mb-0 small";
            oLoginP.AddText("Already have an account? ");
            var oLoginLink = new TsgcHTMLLink("/login", "Sign in");
            oLoginP.Add(oLoginLink);
            oCard.Body.Add(oLoginP);

            oCol.Add(oCard);
            oRow.Add(oCol);
            oRoot.Add(oRow);

            return WrapTemplate("Helpdesk - Register", "<div class=\"container py-5\">" +
                oRoot.HTML + "</div>" + BuildFooter(), aTheme);
        }

        // ----- ticket list ----- //

        public string BuildTicketListPage(THelpdeskTicket[] aRows, string aRole,
            string aDisplayName, string aTheme, string aStatusFilter,
            THelpdeskUser[] aUsers, string aUserFilter, string aSearch, string aSort,
            string aDir)
        {
            bool vIsAdmin = string.Equals(aRole, "admin", StringComparison.OrdinalIgnoreCase);
            var oRoot = new TsgcHTMLNodeList();

            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "d-flex justify-content-between align-items-center mb-4";
            var oHeading = new TsgcHTMLHeading(vIsAdmin ? "All Tickets" : "My Tickets", 1);
            oHeading.CSSClass = "mb-0";
            oHeader.Add(oHeading);
            if (!vIsAdmin)
            {
                var oNewBtn = new TsgcHTMLButton("New Ticket", TsgcHTMLButtonStyle.bsPrimary);
                oNewBtn.Href = "/tickets/new";
                oHeader.Add(oNewBtn);
            }
            oRoot.Add(oHeader);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";

            var oFilterForm = new TsgcHTMLForm();
            oFilterForm.Method = "GET";
            oFilterForm.Action = "/";
            oFilterForm.CSSClass = "mb-3";
            var oFilterRow = new TsgcHTMLContainer("div");
            oFilterRow.CSSClass = "row g-2 align-items-end";

            if (vIsAdmin)
            {
                var oFilterCol = new TsgcHTMLContainer("div");
                oFilterCol.CSSClass = "col-auto";
                var oFilterSelect = new TsgcHTMLContainer("select");
                oFilterSelect.Attributes = "name=\"status\" class=\"form-select\"";

                Action<string, string> addFilterOption = (aValue, aText) =>
                {
                    string vSel = (string.Equals(aStatusFilter, aValue,
                        StringComparison.OrdinalIgnoreCase) ||
                        (aStatusFilter == "" && aValue == "all")) ? " selected" : "";
                    oFilterSelect.AddRaw("<option value=\"" + aValue + "\"" + vSel + ">" +
                        HtmlEsc(aText) + "</option>");
                };

                addFilterOption("all", "All statuses");
                addFilterOption("open", "Open");
                addFilterOption("closed", "Closed");
                oFilterCol.Add(oFilterSelect);
                oFilterRow.Add(oFilterCol);

                var oUserFilterCol = new TsgcHTMLContainer("div");
                oUserFilterCol.CSSClass = "col-auto";
                var oUserFilterSelect = new TsgcHTMLContainer("select");
                oUserFilterSelect.Attributes = "name=\"user\" class=\"form-select\"";

                Action<string, string> addUserFilterOption = (aValue, aText) =>
                {
                    string vSel = string.Equals(aUserFilter, aValue,
                        StringComparison.OrdinalIgnoreCase) ? " selected" : "";
                    oUserFilterSelect.AddRaw("<option value=\"" + HtmlEsc(aValue) + "\"" +
                        vSel + ">" + HtmlEsc(aText) + "</option>");
                };

                addUserFilterOption("", "All users");
                if (aUsers != null)
                    for (int vI = 0; vI < aUsers.Length; vI++)
                        addUserFilterOption(aUsers[vI].Username, aUsers[vI].Username);
                oUserFilterCol.Add(oUserFilterSelect);
                oFilterRow.Add(oUserFilterCol);
            }

            var oSearchCol = new TsgcHTMLContainer("div");
            oSearchCol.CSSClass = "col-auto";
            var oSearchField = new TsgcHTMLField(TsgcHTMLInputType.itText, "search");
            oSearchField.Placeholder = "Search subject...";
            oSearchField.Value = aSearch;
            oSearchCol.Add(oSearchField);
            oFilterRow.Add(oSearchCol);

            oFilterForm.AddHidden("sort", aSort);
            oFilterForm.AddHidden("dir", aDir);

            var oFilterBtnCol = new TsgcHTMLContainer("div");
            oFilterBtnCol.CSSClass = "col-auto";
            var oFilterBtn = new TsgcHTMLButton(vIsAdmin ? "Filter" : "Search",
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oFilterBtn.ButtonType = "submit";
            oFilterBtnCol.Add(oFilterBtn);
            oFilterRow.Add(oFilterBtnCol);

            if (vIsAdmin)
            {
                var oExportCol = new TsgcHTMLContainer("div");
                oExportCol.CSSClass = "col-auto";
                var oExportBtn = new TsgcHTMLButton("Export CSV",
                    TsgcHTMLButtonStyle.bsOutlineSecondary);
                oExportBtn.Href = "/tickets/export.csv" + BuildTicketQueryString(
                    aStatusFilter, aUserFilter, aSearch, aSort, aDir);
                oExportCol.Add(oExportBtn);
                oFilterRow.Add(oExportCol);
            }

            oFilterForm.Add(oFilterRow);
            oCard.Body.Add(oFilterForm);

            var oTable = new TsgcHTMLTable();
            oTable.CSSClass = "table table-sm align-middle mb-0";

            Action<string, string> addSortCol = (aTitle, aColumn) =>
            {
                string vNextDir = (string.Equals(aSort, aColumn, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(aDir, "asc", StringComparison.OrdinalIgnoreCase)) ? "desc" : "asc";
                string vArrow = "";
                if (string.Equals(aSort, aColumn, StringComparison.OrdinalIgnoreCase))
                    vArrow = string.Equals(aDir, "asc", StringComparison.OrdinalIgnoreCase)
                        ? "&#9650;" : "&#9660;";
                string vHref = "/" + BuildTicketQueryString(aStatusFilter, aUserFilter,
                    aSearch, aColumn, vNextDir);
                oTable.AddSortColumn(aTitle, vHref, vArrow);
            };

            addSortCol("Subject", "subject");
            if (vIsAdmin)
                addSortCol("Owner", "owner");
            addSortCol("Status", "status");
            addSortCol("Created", "created");
            addSortCol("Updated", "updated");

            if (aRows.Length == 0)
            {
                oTable.AddEmptyRow("No tickets yet.", vIsAdmin ? 5 : 4);
            }
            else
            {
                for (int vI = 0; vI < aRows.Length; vI++)
                {
                    var oRow = oTable.AddRow();
                    oRow.Style = "cursor:pointer";
                    oRow.OnClick = "window.location='/tickets/" +
                        aRows[vI].Id.ToString(CultureInfo.InvariantCulture) + "'";
                    oRow.AddCellText(aRows[vI].Subject);
                    if (vIsAdmin)
                        oRow.AddCellText(aRows[vI].Username);
                    oRow.AddCellRaw(StatusBadge(aRows[vI].Status));
                    oRow.AddCellText(FmtDate(aRows[vI].CreatedAt));
                    oRow.AddCellText(FmtDate(aRows[vI].UpdatedAt));
                }
            }

            oCard.Body.Add(oTable);
            oRoot.Add(oCard);

            return BuildPageShell(vIsAdmin ? "All Tickets" : "My Tickets", oRoot.HTML,
                "tickets", aDisplayName, aRole, aTheme);
        }

        // ----- create ticket ----- //

        public string BuildTicketNewPage(string aDisplayName, string aTheme, string aError = "")
        {
            var oRoot = new TsgcHTMLNodeList();

            var oHeading = new TsgcHTMLHeading("New Ticket", 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm";
            oCard.BodyClass = "card-body";

            if (!string.IsNullOrEmpty(aError))
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oAlert.CSSClass = "mb-3";
                oCard.Body.Add(oAlert);
            }

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/tickets/new";
            oForm.Enctype = "multipart/form-data";

            var oSubject = new TsgcHTMLField(TsgcHTMLInputType.itText, "subject");
            oSubject.FieldID = "ticket-subject";
            oSubject.Label_ = "Subject";
            oSubject.Required = true;
            oSubject.MaxLength = 200;
            oSubject.ColClass = "mb-3";
            oForm.Add(oSubject);

            var oMessage = new TsgcHTMLTextArea("message");
            oMessage.FieldID = "ticket-message";
            oMessage.Label_ = "Describe your issue";
            oMessage.Rows = 5;
            oMessage.Required = true;
            oMessage.ColClass = "mb-3";
            oForm.Add(oMessage);

            var oFiles = new TsgcHTMLField(TsgcHTMLInputType.itFile, "attachments");
            oFiles.FieldID = "ticket-attachments";
            oFiles.Label_ = "Attachments (optional)";
            oFiles.Multiple = true;
            oFiles.ColClass = "mb-3";
            oForm.Add(oFiles);

            var oButtons = new TsgcHTMLContainer("div");
            oButtons.CSSClass = "d-flex gap-2";
            var oSubmit = new TsgcHTMLButton("Create Ticket", TsgcHTMLButtonStyle.bsPrimary);
            oSubmit.ButtonType = "submit";
            oButtons.Add(oSubmit);
            var oCancel = new TsgcHTMLButton("Cancel", TsgcHTMLButtonStyle.bsOutlineSecondary);
            oCancel.Href = "/";
            oButtons.Add(oCancel);
            oForm.Add(oButtons);

            oCard.Body.Add(oForm);
            oRoot.Add(oCard);

            return BuildPageShell("New Ticket", oRoot.HTML, "new", aDisplayName, "user", aTheme);
        }

        // ----- message card ----- //

        private string BuildMessageCard(THelpdeskMessage aMessage)
        {
            var oRoot = new TsgcHTMLNodeList();

            string vClass = "card mb-2";
            if (aMessage.IsAdmin)
                vClass += " hd-admin";
            var oCard = new TsgcHTMLContainer("div");
            oCard.CSSClass = vClass;

            var oBody = new TsgcHTMLContainer("div");
            oBody.CSSClass = "card-body py-2";

            var oHead = new TsgcHTMLContainer("div");
            oHead.CSSClass = "d-flex justify-content-between align-items-center mb-1";
            var oAuthor = new TsgcHTMLContainer("span");
            oAuthor.CSSClass = "fw-semibold small";
            oAuthor.AddText(aMessage.Username);
            if (aMessage.IsAdmin)
                oAuthor.AddRaw(" " + TsgcHTMLComponent_Badge.Build("Admin",
                    TsgcHTMLBadgeStyle.bgPrimary, true));
            oHead.Add(oAuthor);
            var oWhen = new TsgcHTMLContainer("span");
            oWhen.CSSClass = "text-muted small";
            oWhen.AddText(FmtDate(aMessage.CreatedAt));
            oHead.Add(oWhen);
            oBody.Add(oHead);

            var oText = new TsgcHTMLContainer("div");
            oText.AddRaw(HtmlEscNl(aMessage.Body));
            oBody.Add(oText);

            if (aMessage.Attachments.Length > 0)
            {
                var oAttWrap = new TsgcHTMLContainer("div");
                oAttWrap.CSSClass = "mt-2 small";
                for (int vJ = 0; vJ < aMessage.Attachments.Length; vJ++)
                {
                    var oAttItem = new TsgcHTMLContainer("div");
                    var oAttLink = new TsgcHTMLLink(
                        "/tickets/" + aMessage.TicketId.ToString(CultureInfo.InvariantCulture) +
                        "/files/" + aMessage.Attachments[vJ].Id.ToString(CultureInfo.InvariantCulture),
                        aMessage.Attachments[vJ].OriginalFilename);
                    oAttLink.CSSClass = "text-decoration-none";
                    oAttItem.AddRaw("&#128206; ");
                    oAttItem.Add(oAttLink);
                    oAttItem.AddText(" (" + FmtSize(aMessage.Attachments[vJ].SizeBytes) + ")");
                    oAttWrap.Add(oAttItem);
                }
                oBody.Add(oAttWrap);
            }

            oCard.Add(oBody);
            oRoot.Add(oCard);
            return oRoot.HTML;
        }

        // ----- ticket detail ----- //

        public string BuildTicketDetailPage(THelpdeskTicket aTicket,
            THelpdeskMessage[] aMessages, string aRole, string aDisplayName, string aTheme,
            bool aCanReply, bool aCanModerate, string aError = "", string aFlash = "")
        {
            bool vIsOpen = string.Equals(aTicket.Status, "open", StringComparison.OrdinalIgnoreCase);
            var oRoot = new TsgcHTMLNodeList();

            // Self-dismissing flash toast.
            string vFlashText = FlashMessage(aFlash);
            if (!string.IsNullOrEmpty(vFlashText))
            {
                var oToastWrap = new TsgcHTMLContainer("div");
                oToastWrap.Attributes =
                    "class=\"toast-container position-fixed bottom-0 end-0 p-3\" " +
                    "style=\"z-index:1080\"";
                var oToast = new TsgcHTMLToast("hd-flash-toast", "Helpdesk");
                oToast.CSSClass = "text-bg-success border-0";
                oToast.Delay = 4000;
                oToast.Body.AddText(vFlashText);
                oToastWrap.AddRaw(oToast.HTML);
                oRoot.Add(oToastWrap);

                var oToastScript = new TsgcHTMLScript();
                oToastScript.Code =
                    "document.addEventListener('DOMContentLoaded',function(){" +
                    "var el=document.getElementById('hd-flash-toast');" +
                    "if(el&&window.bootstrap){new bootstrap.Toast(el).show();}" +
                    "if(window.history&&window.history.replaceState){" +
                    "var u=new URL(window.location.href);u.searchParams.delete('flash');" +
                    "window.history.replaceState({},'',u.toString());}});";
                oRoot.Add(oToastScript);
            }

            var oBackP = new TsgcHTMLContainer("p");
            oBackP.CSSClass = "mb-3";
            var oBackLink = new TsgcHTMLLink("/", "Back to tickets");
            oBackP.Add(oBackLink);
            oRoot.Add(oBackP);

            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "d-flex justify-content-between align-items-start mb-1";
            var oHeading = new TsgcHTMLHeading(aTicket.Subject, 1);
            oHeading.CSSClass = "mb-0";
            oHeader.Add(oHeading);
            oHeader.AddRaw(StatusBadge(aTicket.Status));
            oRoot.Add(oHeader);

            var oMeta = new TsgcHTMLContainer("p");
            oMeta.CSSClass = "text-muted small mb-4";
            oMeta.AddText("Ticket #" + aTicket.Id.ToString(CultureInfo.InvariantCulture) +
                " opened " + FmtDate(aTicket.CreatedAt) + " by " + aTicket.Username);
            oRoot.Add(oMeta);

            var oThread = new TsgcHTMLContainer("div");
            if (aMessages.Length == 0)
            {
                oThread.CSSClass = "hd-thread mb-4 text-muted small";
                oThread.AddText("No messages yet.");
            }
            else
            {
                oThread.CSSClass = "hd-thread mb-4";
                for (int vI = 0; vI < aMessages.Length; vI++)
                    oThread.AddRaw(BuildMessageCard(aMessages[vI]));
            }
            oRoot.Add(oThread);

            if (aCanReply && vIsOpen)
            {
                var oCard = new TsgcHTMLCard();
                oCard.CSSClass = "shadow-sm mb-3";
                oCard.BodyClass = "card-body";

                if (!string.IsNullOrEmpty(aError))
                {
                    var oAlert = new TsgcHTMLAlert(aError);
                    oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                    oAlert.CSSClass = "mb-3";
                    oCard.Body.Add(oAlert);
                }

                var oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/tickets/" + aTicket.Id.ToString(CultureInfo.InvariantCulture) +
                    "/reply";
                oForm.Enctype = "multipart/form-data";

                var oReply = new TsgcHTMLTextArea("body");
                oReply.FieldID = "reply-body";
                oReply.Label_ = "Reply";
                oReply.Rows = 3;
                oReply.Required = true;
                oReply.ColClass = "mb-3";
                oForm.Add(oReply);

                var oReplyFiles = new TsgcHTMLField(TsgcHTMLInputType.itFile, "attachments");
                oReplyFiles.FieldID = "reply-attachments";
                oReplyFiles.Label_ = "Attachments (optional)";
                oReplyFiles.Multiple = true;
                oReplyFiles.ColClass = "mb-3";
                oForm.Add(oReplyFiles);

                var oSubmit = new TsgcHTMLButton("Send Reply", TsgcHTMLButtonStyle.bsPrimary);
                oSubmit.ButtonType = "submit";
                oForm.Add(oSubmit);

                oCard.Body.Add(oForm);
                oRoot.Add(oCard);
            }
            else if (!vIsOpen)
            {
                var oClosedNote = new TsgcHTMLContainer("p");
                oClosedNote.CSSClass = "text-muted small";
                oClosedNote.AddText("This ticket is closed. Reopen it to add a reply.");
                oRoot.Add(oClosedNote);
            }

            if (aCanModerate)
            {
                var oToggleForm = new TsgcHTMLForm();
                oToggleForm.Method = "POST";
                TsgcHTMLButton oToggleBtn;
                if (vIsOpen)
                {
                    oToggleForm.Action = "/tickets/" +
                        aTicket.Id.ToString(CultureInfo.InvariantCulture) + "/close";
                    oToggleBtn = new TsgcHTMLButton("Close Ticket",
                        TsgcHTMLButtonStyle.bsOutlineDanger);
                }
                else
                {
                    oToggleForm.Action = "/tickets/" +
                        aTicket.Id.ToString(CultureInfo.InvariantCulture) + "/reopen";
                    oToggleBtn = new TsgcHTMLButton("Reopen Ticket",
                        TsgcHTMLButtonStyle.bsOutlineSuccess);
                }
                oToggleBtn.ButtonType = "submit";
                oToggleForm.Add(oToggleBtn);
                oRoot.Add(oToggleForm);
            }

            return BuildPageShell(aTicket.Subject, oRoot.HTML, "tickets", aDisplayName,
                aRole, aTheme);
        }

        // ----- dashboard ----- //

        public string BuildDashboardPage(string aDisplayName, string aTheme,
            int aOpenedToday, int aOpenedYesterday, int aClosedToday, int aCreatedThisMonth,
            int aCreatedThisYear, string aRange, THelpdeskActivityPoint[] aSeries)
        {
            int vMaxValue = 0;
            for (int vI = 0; vI < aSeries.Length; vI++)
            {
                if (aSeries[vI].Opened > vMaxValue)
                    vMaxValue = aSeries[vI].Opened;
                if (aSeries[vI].Closed > vMaxValue)
                    vMaxValue = aSeries[vI].Closed;
            }

            var oRoot = new TsgcHTMLNodeList();

            var oHeading = new TsgcHTMLHeading("Dashboard", 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            var oStatRow = new TsgcHTMLContainer("div");
            oStatRow.CSSClass = "row g-3 mb-4";

            Action<string, int> addStatCard = (aLabel, aValue) =>
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-6 col-md-4 col-lg";
                var oStatCard = new TsgcHTMLCard();
                oStatCard.CSSClass = "shadow-sm text-center h-100";
                oStatCard.BodyClass = "card-body";
                var oNum = new TsgcHTMLHeading(aValue.ToString(CultureInfo.InvariantCulture), 2);
                oNum.CSSClass = "fw-bold mb-0";
                oStatCard.Body.Add(oNum);
                var oLbl = new TsgcHTMLContainer("div");
                oLbl.CSSClass = "text-muted small";
                oLbl.AddText(aLabel);
                oStatCard.Body.Add(oLbl);
                oCol.Add(oStatCard);
                oStatRow.Add(oCol);
            };

            addStatCard("Opened Today", aOpenedToday);
            addStatCard("Opened Yesterday", aOpenedYesterday);
            addStatCard("Closed Today", aClosedToday);
            addStatCard("Created This Month", aCreatedThisMonth);
            addStatCard("Created This Year", aCreatedThisYear);
            oRoot.Add(oStatRow);

            var oFilterCard = new TsgcHTMLCard();
            oFilterCard.CSSClass = "shadow-sm mb-4";
            oFilterCard.BodyClass = "card-body";

            var oFilterForm = new TsgcHTMLForm();
            oFilterForm.Method = "GET";
            oFilterForm.Action = "/dashboard";
            var oFilterRow = new TsgcHTMLContainer("div");
            oFilterRow.CSSClass = "row g-2 align-items-end";

            var oFilterCol = new TsgcHTMLContainer("div");
            oFilterCol.CSSClass = "col-auto";
            var oRangeSelect = new TsgcHTMLContainer("select");
            oRangeSelect.Attributes = "name=\"range\" class=\"form-select\"";

            Action<string, string> addRangeOption = (aValue, aText) =>
            {
                string vSel = string.Equals(aRange, aValue, StringComparison.OrdinalIgnoreCase)
                    ? " selected" : "";
                oRangeSelect.AddRaw("<option value=\"" + aValue + "\"" + vSel + ">" +
                    HtmlEsc(aText) + "</option>");
            };

            addRangeOption("today", "Today");
            addRangeOption("yesterday", "Yesterday");
            addRangeOption("week", "Week");
            addRangeOption("month", "Month");
            addRangeOption("3months", "3 Months");
            addRangeOption("6months", "6 Months");
            addRangeOption("year", "1 Year");
            oFilterCol.Add(oRangeSelect);
            oFilterRow.Add(oFilterCol);

            var oFilterBtnCol = new TsgcHTMLContainer("div");
            oFilterBtnCol.CSSClass = "col-auto";
            var oFilterBtn = new TsgcHTMLButton("Apply", TsgcHTMLButtonStyle.bsOutlineSecondary);
            oFilterBtn.ButtonType = "submit";
            oFilterBtnCol.Add(oFilterBtn);
            oFilterRow.Add(oFilterBtnCol);

            oFilterForm.Add(oFilterRow);
            oFilterCard.Body.Add(oFilterForm);
            oRoot.Add(oFilterCard);

            var oChartCard = new TsgcHTMLCard();
            oChartCard.CSSClass = "shadow-sm";
            oChartCard.BodyClass = "card-body";
            oChartCard.Body.Add(new TsgcHTMLHeading("Tickets Opened vs Closed", 5));

            var oOpenedLabel = new TsgcHTMLContainer("div");
            oOpenedLabel.CSSClass = "small fw-semibold text-primary mt-2 mb-1";
            oOpenedLabel.AddText("Opened");
            oChartCard.Body.Add(oOpenedLabel);
            var oOpenedChart = new TsgcHTMLMiniBarChart();
            oOpenedChart.ShowCard = false;
            oOpenedChart.Height = "110px";
            oOpenedChart.BarClass = "bg-primary";
            oOpenedChart.MaxValue = vMaxValue;
            oOpenedChart.EmptyText = "No data for this period.";
            for (int vI = 0; vI < aSeries.Length; vI++)
                oOpenedChart.AddBar(aSeries[vI].BucketLabel, aSeries[vI].Opened,
                    aSeries[vI].Opened.ToString(CultureInfo.InvariantCulture));
            oChartCard.Body.AddRaw(oOpenedChart.HTML);

            var oClosedLabel = new TsgcHTMLContainer("div");
            oClosedLabel.CSSClass = "small fw-semibold text-secondary mt-3 mb-1";
            oClosedLabel.AddText("Closed");
            oChartCard.Body.Add(oClosedLabel);
            var oClosedChart = new TsgcHTMLMiniBarChart();
            oClosedChart.ShowCard = false;
            oClosedChart.Height = "110px";
            oClosedChart.BarClass = "bg-secondary";
            oClosedChart.MaxValue = vMaxValue;
            oClosedChart.EmptyText = "No data for this period.";
            for (int vI = 0; vI < aSeries.Length; vI++)
                oClosedChart.AddBar(aSeries[vI].BucketLabel, aSeries[vI].Closed,
                    aSeries[vI].Closed.ToString(CultureInfo.InvariantCulture));
            oChartCard.Body.AddRaw(oClosedChart.HTML);

            oRoot.Add(oChartCard);

            return BuildPageShell("Dashboard", oRoot.HTML, "dashboard", aDisplayName,
                "admin", aTheme);
        }

        public string BuildNotFoundPage(string aTheme)
        {
            string vBody = "<div class=\"container py-5 text-center\">" +
                "<h1 class=\"display-6\">404</h1>" +
                "<p class=\"text-muted\">Page not found.</p>" +
                "<a href=\"/\" class=\"btn btn-primary\">Go home</a></div>" + BuildFooter();
            return WrapTemplate("Helpdesk - Not Found", vBody, aTheme);
        }
    }
}
