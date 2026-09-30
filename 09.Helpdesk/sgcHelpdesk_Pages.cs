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
        public const string CS_KANBAN_BOARD_ID = "hdKanban";

        // Kanban pipeline columns, in display order: status code + display label.
        private static readonly string[] CS_KANBAN_COLS = { "new", "pending_resolution",
            "pending_feedback", "closed" };
        private static readonly string[] CS_KANBAN_LABELS = { "New", "Pending Resolution",
            "Pending Feedback", "Closed" };
        // AllowedTargets of each column (same order). A ticket is closed only once
        // the customer confirmed the fix (Pending Feedback), and a closed ticket
        // can only be reopened into Pending Resolution.
        private static readonly string[] CS_KANBAN_TARGETS = { "pending_resolution",
            "new,pending_feedback", "pending_resolution,closed", "pending_resolution" };
        // Swimlanes = ticket priority.
        private static readonly string[] CS_KANBAN_LANES = { "critical", "high", "medium",
            "low" };
        private static readonly string[] CS_KANBAN_LANE_LABELS = { "Critical", "High",
            "Medium", "Low" };
        // SLA: days from creation to the due date, per priority (same order).
        private static readonly int[] CS_KANBAN_SLA_DAYS = { 1, 2, 5, 10 };
        // Work-in-progress limit of Pending Resolution (per swimlane cell).
        private const int CS_KANBAN_WIP_LIMIT = 2;
        private static readonly string[] CS_CATEGORIES = { "general", "account",
            "billing", "technical" };
        private static readonly string[] CS_CATEGORY_LABELS = { "General", "Account",
            "Billing", "Technical" };

        // True when the server pushes board changes to every open board over the
        // WebSocket (the board then loads the sgcHTMX bridge).
        public bool LiveSync { get; set; }

        private static int KanbanColumnIndex(string aColumn)
        {
            string vColumn = (aColumn ?? "").Trim();
            for (int i = 0; i < CS_KANBAN_COLS.Length; i++)
                if (string.Equals(CS_KANBAN_COLS[i], vColumn, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        // Workflow rules of the board, shared by the board markup (AllowedTargets)
        // and the server-side validation of every drop.
        public static bool KanbanColumnValid(string aColumn)
        {
            return KanbanColumnIndex(aColumn) >= 0;
        }

        public static bool KanbanMoveAllowed(string aFrom, string aTo)
        {
            if (!KanbanColumnValid(aTo))
                return false;
            int vFrom = KanbanColumnIndex(aFrom);
            // legacy / unknown status values live in the New column
            if (vFrom < 0)
                vFrom = 0;
            if (string.Equals(CS_KANBAN_COLS[vFrom], aTo, StringComparison.OrdinalIgnoreCase))
                return true;
            return ("," + CS_KANBAN_TARGETS[vFrom] + ",").IndexOf(
                "," + (aTo ?? "").Trim().ToLowerInvariant() + ",", StringComparison.Ordinal) >= 0;
        }

        // Card element id of a ticket ("tk" + id). htmx 2 locates out-of-band
        // targets with a '#id' CSS selector, which cannot start with a digit, so the
        // bare ticket id is not used. KanbanTicketId accepts both forms (drag POST,
        // edit dialog).
        public static string KanbanCardID(long aTicketId)
        {
            return "tk" + aTicketId.ToString(CultureInfo.InvariantCulture);
        }

        public static bool KanbanTicketId(string aValue, out long aTicketId)
        {
            string vValue = (aValue ?? "").Trim().ToLowerInvariant();
            if (vValue.StartsWith("tk", StringComparison.Ordinal))
                vValue = vValue.Substring(2);
            return long.TryParse(vValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out aTicketId) && aTicketId > 0;
        }

        private static int KanbanLaneIndex(string aPriority)
        {
            int vResult = 2; // medium
            for (int i = 0; i < CS_KANBAN_LANES.Length; i++)
                if (string.Equals(CS_KANBAN_LANES[i], aPriority, StringComparison.OrdinalIgnoreCase))
                    vResult = i;
            return vResult;
        }

        // Maps the demo ticket onto a board card: priority -> swimlane + dot,
        // category -> tag, owner -> initials, SLA due date (open tickets only).
        private static void FillKanbanCard(TsgcHTMLKanbanCard oCard, THelpdeskTicket aTicket)
        {
            int vLane = KanbanLaneIndex(aTicket.Priority);
            oCard.CardID = KanbanCardID(aTicket.Id);
            oCard.Title = "#" + aTicket.Id.ToString(CultureInfo.InvariantCulture);
            oCard.Description = aTicket.Subject;
            oCard.Assignee = aTicket.Username;
            oCard.Color = TsgcHTMLColor.hcLight;
            oCard.SwimlaneID = CS_KANBAN_LANES[vLane];
            switch (vLane)
            {
                case 0: oCard.Priority = TsgcHTMLKanbanPriority.kpCritical; break;
                case 1: oCard.Priority = TsgcHTMLKanbanPriority.kpHigh; break;
                case 3: oCard.Priority = TsgcHTMLKanbanPriority.kpLow; break;
                default: oCard.Priority = TsgcHTMLKanbanPriority.kpMedium; break;
            }
            TsgcHTMLKanbanTag oTag = oCard.Tags.Add();
            oTag.Text = CS_CATEGORY_LABELS[0];
            for (int i = 0; i < CS_CATEGORIES.Length; i++)
                if (string.Equals(CS_CATEGORIES[i], aTicket.Category, StringComparison.OrdinalIgnoreCase))
                    oTag.Text = CS_CATEGORY_LABELS[i];
            if (string.Equals(aTicket.Category, "billing", StringComparison.OrdinalIgnoreCase))
                oTag.Style = TsgcHTMLBadgeStyle.bgWarning;
            else if (string.Equals(aTicket.Category, "technical", StringComparison.OrdinalIgnoreCase))
                oTag.Style = TsgcHTMLBadgeStyle.bgInfo;
            else if (string.Equals(aTicket.Category, "account", StringComparison.OrdinalIgnoreCase))
                oTag.Style = TsgcHTMLBadgeStyle.bgPrimary;
            // the due date (SLA) only matters while the ticket is open
            if (!string.Equals(aTicket.Status, "closed", StringComparison.OrdinalIgnoreCase))
                oCard.DueDate = aTicket.CreatedAt.AddDays(CS_KANBAN_SLA_DAYS[vLane])
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // --- Kanban board (admin ticket list) --- //

        // The configured ticket board filled from aRows. The list page renders it
        // and the server builds the card fragments of the live updates from it.
        public TsgcHTMLComponent_KanbanBoard CreateTicketBoard(THelpdeskTicket[] aRows)
        {
            var vResult = new TsgcHTMLComponent_KanbanBoard();
            vResult.BoardID = CS_KANBAN_BOARD_ID;
            vResult.CSSHeight = "70vh";
            vResult.IncludeCSS = true;
            // drag a card to change its status (column) or priority (swimlane)
            vResult.DragEnabled = true;
            vResult.DragUpdateURL = "/tickets/kanban-move";
            vResult.ShowCardCount = true;
            vResult.CollapsibleColumns = true;
            vResult.ShowSearch = true;
            vResult.SearchPlaceholder = "Search tickets";
            // "+" form at the bottom of every column / lane cell
            vResult.QuickAdd = true;
            vResult.QuickAddURL = "/tickets/kanban-add";
            vResult.QuickAddText = "New ticket";
            // clicking a card opens the edit dialog
            vResult.EditURL = "/tickets/kanban-edit";
            vResult.ShowAssigneeInitials = true;
            vResult.LiveSync = LiveSync;

            for (int vI = 0; vI < CS_KANBAN_LANES.Length; vI++)
            {
                TsgcHTMLKanbanSwimlane oLane = vResult.Swimlanes.Add();
                oLane.SwimlaneID = CS_KANBAN_LANES[vI];
                oLane.Title = CS_KANBAN_LANE_LABELS[vI];
            }

            for (int vColIdx = 0; vColIdx < CS_KANBAN_COLS.Length; vColIdx++)
            {
                TsgcHTMLKanbanColumn oCol;
                switch (vColIdx)
                {
                    case 0: oCol = vResult.AddColumn(CS_KANBAN_LABELS[vColIdx], TsgcHTMLColor.hcPrimary); break;
                    case 1: oCol = vResult.AddColumn(CS_KANBAN_LABELS[vColIdx], TsgcHTMLColor.hcInfo); break;
                    case 2: oCol = vResult.AddColumn(CS_KANBAN_LABELS[vColIdx], TsgcHTMLColor.hcWarning); break;
                    default: oCol = vResult.AddColumn(CS_KANBAN_LABELS[vColIdx], TsgcHTMLColor.hcSecondary); break;
                }
                oCol.ColumnID = CS_KANBAN_COLS[vColIdx];
                oCol.AllowedTargets = CS_KANBAN_TARGETS[vColIdx];
                if (vColIdx == 1)
                    oCol.WIPLimit = CS_KANBAN_WIP_LIMIT;
                // the server refuses new tickets straight into Closed
                if (vColIdx == 3)
                    oCol.QuickAdd = false;
            }

            for (int vI = 0; vI < aRows.Length; vI++)
            {
                // unknown / legacy status values go to New
                int vColIdx = KanbanColumnIndex(aRows[vI].Status);
                if (vColIdx < 0)
                    vColIdx = 0;
                FillKanbanCard(vResult.Columns[vColIdx].Cards.Add(), aRows[vI]);
            }
            return vResult;
        }

        // Body of the board's edit dialog (EditURL): subject, priority and
        // category of aTicket. aSaved shows a confirmation above the form.
        public string BuildKanbanEditForm(THelpdeskTicket aTicket, bool aSaved,
            string aError = "")
        {
            var oRoot = new TsgcHTMLNodeList();
            if (aSaved)
            {
                var oAlert = new TsgcHTMLAlert("Ticket saved.");
                oAlert.Style = TsgcHTMLAlertStyle.asSuccess;
                oRoot.Add(oAlert);
            }
            if (aError != "")
            {
                var oAlert = new TsgcHTMLAlert(aError);
                oAlert.Style = TsgcHTMLAlertStyle.asDanger;
                oRoot.Add(oAlert);
            }

            // the answer replaces the dialog body; the saved card is re-rendered by
            // the out-of-band fragment that comes with it
            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Attributes = "hx-post=\"/tickets/kanban-edit\" hx-target=\"#" +
                CS_KANBAN_BOARD_ID + "_edit_body\" hx-swap=\"innerHTML\"";
            oForm.AddHidden("id", aTicket.Id.ToString(CultureInfo.InvariantCulture));

            var oField = new TsgcHTMLField(TsgcHTMLInputType.itText, "subject");
            oField.FieldID = "hdEditSubject";
            oField.Label_ = "Subject";
            oField.Value = aTicket.Subject;
            oField.Required = true;
            oField.MaxLength = 200;
            oField.ColClass = "mb-3";
            oForm.Add(oField);

            var oSelect = new TsgcHTMLSelect();
            oSelect.FieldID = "hdEditPriority";
            oSelect.Name = "priority";
            oSelect.Label_ = "Priority";
            oSelect.CSSClass = "form-select";
            oSelect.ColClass = "mb-3";
            for (int vI = 0; vI < CS_KANBAN_LANES.Length; vI++)
                oSelect.AddOption(CS_KANBAN_LANES[vI], CS_KANBAN_LANE_LABELS[vI],
                    string.Equals(CS_KANBAN_LANES[vI], aTicket.Priority, StringComparison.OrdinalIgnoreCase));
            oForm.Add(oSelect);

            oSelect = new TsgcHTMLSelect();
            oSelect.FieldID = "hdEditCategory";
            oSelect.Name = "category";
            oSelect.Label_ = "Category";
            oSelect.CSSClass = "form-select";
            oSelect.ColClass = "mb-3";
            for (int vI = 0; vI < CS_CATEGORIES.Length; vI++)
                oSelect.AddOption(CS_CATEGORIES[vI], CS_CATEGORY_LABELS[vI],
                    string.Equals(CS_CATEGORIES[vI], aTicket.Category, StringComparison.OrdinalIgnoreCase));
            oForm.Add(oSelect);

            var oBar = new TsgcHTMLContainer("div");
            oBar.CSSClass = "d-flex gap-2 align-items-center";
            var oBtn = new TsgcHTMLButton("Save", TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBar.Add(oBtn);
            var oLink = new TsgcHTMLLink("/tickets/" + aTicket.Id.ToString(CultureInfo.InvariantCulture),
                "Open ticket #" + aTicket.Id.ToString(CultureInfo.InvariantCulture));
            oLink.CSSClass = "btn btn-link";
            oBar.Add(oLink);
            oForm.Add(oBar);
            oRoot.Add(oForm);

            return oRoot.HTML;
        }

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
            string vStatus = (aStatus ?? "").Trim().ToLowerInvariant();
            if (vStatus == "closed")
                return TsgcHTMLComponent_Badge.Build("Closed", TsgcHTMLBadgeStyle.bgSecondary, true);
            if (vStatus == "pending_resolution")
                return TsgcHTMLComponent_Badge.Build("Pending Resolution", TsgcHTMLBadgeStyle.bgInfo, true);
            if (vStatus == "pending_feedback")
                return TsgcHTMLComponent_Badge.Build("Pending Feedback", TsgcHTMLBadgeStyle.bgWarning, true);
            return TsgcHTMLComponent_Badge.Build("New", TsgcHTMLBadgeStyle.bgPrimary, true);
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
                addFilterOption("new", "New");
                addFilterOption("pending_resolution", "Pending Resolution");
                addFilterOption("pending_feedback", "Pending Feedback");
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

            if (!vIsAdmin)
            {
                // Regular user: the filter/search card also carries the table.
                oCard.Body.Add(oTable);
                oRoot.Add(oCard);
            }
            else
            {
                // Admin: the filter card stays common above the tab strip; the grid goes
                // in the List tab and the Kanban board (same rows) in the Kanban tab.
                oRoot.Add(oCard);

                // ----- Build the Kanban board from the same rows -----
                string vKanbanHTML = CreateTicketBoard(aRows).HTML;

                // ----- Tabs: List | Kanban -----
                var oTabs = new TsgcHTMLContainer("ul");
                oTabs.CSSClass = "nav nav-tabs mb-3";
                oTabs.Attributes = "role=\"tablist\"";

                var oTabLi = new TsgcHTMLContainer("li");
                oTabLi.CSSClass = "nav-item";
                oTabLi.Attributes = "role=\"presentation\"";
                var oTabBtn = new TsgcHTMLContainer("button");
                oTabBtn.CSSClass = "nav-link active";
                oTabBtn.Attributes = "data-bs-toggle=\"tab\" data-bs-target=\"#hdListPane\" " +
                    "type=\"button\" role=\"tab\"";
                oTabBtn.AddText("List");
                oTabLi.Add(oTabBtn);
                oTabs.Add(oTabLi);

                oTabLi = new TsgcHTMLContainer("li");
                oTabLi.CSSClass = "nav-item";
                oTabLi.Attributes = "role=\"presentation\"";
                oTabBtn = new TsgcHTMLContainer("button");
                oTabBtn.CSSClass = "nav-link";
                oTabBtn.Attributes = "data-bs-toggle=\"tab\" data-bs-target=\"#hdKanbanPane\" " +
                    "type=\"button\" role=\"tab\"";
                oTabBtn.AddText("Kanban");
                oTabLi.Add(oTabBtn);
                oTabs.Add(oTabLi);

                oRoot.Add(oTabs);

                var oTabContent = new TsgcHTMLContainer("div");
                oTabContent.CSSClass = "tab-content";

                var oListPane = new TsgcHTMLContainer("div");
                oListPane.CSSClass = "tab-pane fade show active";
                oListPane.ID = "hdListPane";
                oListPane.Attributes = "role=\"tabpanel\"";
                var oListCard = new TsgcHTMLCard();
                oListCard.CSSClass = "shadow-sm";
                oListCard.BodyClass = "card-body";
                oListCard.Body.Add(oTable);
                oListPane.Add(oListCard);
                oTabContent.Add(oListPane);

                var oKanbanPane = new TsgcHTMLContainer("div");
                oKanbanPane.CSSClass = "tab-pane fade";
                oKanbanPane.ID = "hdKanbanPane";
                oKanbanPane.Attributes = "role=\"tabpanel\"";
                var oKanHint = new TsgcHTMLContainer("p");
                oKanHint.CSSClass = "text-muted small";
                oKanHint.AddText("Drag a card to another column to change its status " +
                    "or to another lane to change its priority (a ticket is closed only " +
                    "from Pending Feedback). Click a card to edit it, use + to add one.");
                oKanbanPane.Add(oKanHint);
                // the board is a component that renders its own markup
                oKanbanPane.AddRaw(vKanbanHTML);
                oTabContent.Add(oKanbanPane);

                oRoot.Add(oTabContent);

                // htmx runs the board's quick add and edit dialog requests
                oRoot.Add(new TsgcHTMLScript("/htmx.min.js"));
                if (LiveSync)
                {
                    // live sync: the sgcHTMX bridge opens a WebSocket to this server and
                    // applies the card fragments pushed after every change
                    oRoot.Add(new TsgcHTMLScript("/sgcWebSockets.js"));
                    oRoot.Add(new TsgcHTMLScript("/sgcHTMX.min.js"));
                    var oScript = new TsgcHTMLScript();
                    oScript.Code = "document.addEventListener(\"DOMContentLoaded\"," +
                        "function(){if(window.sgcHTMX&&sgcHTMX.init){sgcHTMX.init({host:" +
                        "(location.protocol===\"https:\"?\"wss:\":\"ws:\")+\"//\"+location.host+" +
                        "location.pathname});}});";
                    oRoot.Add(oScript);
                }
            }

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
            bool vIsOpen = !string.Equals(aTicket.Status, "closed", StringComparison.OrdinalIgnoreCase);
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
