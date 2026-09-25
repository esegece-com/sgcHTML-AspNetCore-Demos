// ***************************************************************************
//  sgcGrid - Grid features demo (managed port)
//  Port of delphi\Demos\60.HTML\06.Grid\sgcGridDemo_Pages.pas
//
//  Faithful 1:1 port of the eight TsgcHTMLComponent_Grid showcase fragments
//  (basic, sort/filter, export, inline edit, group-by, column reorder,
//  pagination, virtual scroll) plus the tabbed shell. The grid feature toggles
//  (ShowSort / ShowFilter / ShowExport / InlineEdit / GroupByColumn /
//  ColumnReorder / VirtualScroll) drive the injected client-side script exactly
//  as in Delphi.
// ***************************************************************************

using System.Globalization;
using System.Text;
// sgc
using esegece.sgcWebSockets;

namespace GridDemo
{
    public static class TsgcGridDemoPages
    {
        // ----- sample data ----- //
        private const int CS_EMP_COUNT = 20;

        private static readonly string[] CS_NAMES = {
            "Alice Johnson", "Bob Smith", "Carol Williams", "David Brown", "Emma Davis",
            "Frank Miller", "Grace Wilson", "Henry Moore", "Isabella Taylor",
            "James Anderson", "Kate Thomas", "Liam Jackson", "Mia White", "Noah Harris",
            "Olivia Martin", "Paul Thompson", "Quinn Garcia", "Rachel Martinez",
            "Samuel Robinson", "Tessa Clark" };

        private static readonly string[] CS_DEPTS = {
            "Engineering", "Marketing", "Engineering", "HR", "Engineering", "Marketing",
            "Engineering", "Finance", "Marketing", "HR", "Engineering", "Finance",
            "Engineering", "Marketing", "Engineering", "HR", "Finance", "Engineering",
            "Marketing", "Engineering" };

        private static readonly string[] CS_CITIES = {
            "New York", "London", "Berlin", "Paris", "Tokyo", "New York", "London",
            "Chicago", "Paris", "Berlin", "Tokyo", "New York", "London", "Chicago",
            "Paris", "New York", "London", "Berlin", "Tokyo", "Chicago" };

        private static readonly string[] CS_POSITIONS = {
            "Senior Dev", "Manager", "Developer", "Director", "Tech Lead", "Analyst",
            "Developer", "Accountant", "Designer", "Manager", "Developer", "Controller",
            "Senior Dev", "Manager", "Architect", "Recruiter", "Analyst", "Developer",
            "Director", "Senior Dev" };

        private static readonly string[] CS_SALARIES = {
            "95,000", "78,000", "82,000", "91,000", "105,000", "65,000", "87,000",
            "72,000", "69,000", "80,000", "88,000", "76,000", "96,000", "79,000",
            "110,000", "64,000", "70,000", "85,000", "95,000", "92,000" };

        // Virtual scroll: 60 employees generated on the fly.
        private static void GetVirtualEmployee(int i, out string vName, out string vDept,
            out string vCity, out string vPos, out string vSal)
        {
            string[] vFN = { "Alice", "Bob", "Carol", "David", "Emma", "Frank", "Grace",
                "Henry", "Isabella", "James" };
            string[] vLN = { "Johnson", "Smith", "Williams", "Brown", "Davis", "Miller",
                "Wilson", "Moore", "Taylor", "Anderson" };
            string[] vDP = { "Engineering", "Marketing", "HR", "Finance", "Operations" };
            string[] vCI = { "New York", "London", "Berlin", "Paris", "Tokyo", "Chicago",
                "Amsterdam", "Sydney", "Toronto", "Singapore" };
            string[] vPO = { "Developer", "Senior Dev", "Manager", "Analyst", "Tech Lead" };
            vName = vFN[i % 10] + " " + vLN[(i / 10) % 10];
            vDept = vDP[i % 5];
            vCity = vCI[i % 10];
            vPos = vPO[i % 5];
            vSal = (60000 + (i % 5) * 5000 + (i / 10) * 1000)
                .ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Grid factory helpers
        // ------------------------------------------------------------------

        private static void AddStdColumns(TsgcHTMLComponent_Grid oGrid)
        {
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
        }

        private static void AddSampleRows(TsgcHTMLComponent_Grid oGrid)
        {
            for (int i = 0; i < CS_EMP_COUNT; i++)
                oGrid.AddRow(CS_NAMES[i], CS_DEPTS[i], CS_CITIES[i], CS_POSITIONS[i],
                    CS_SALARIES[i]);
        }

        // Add rows sorted by department for the grouping demo.
        private static void AddSortedRows(TsgcHTMLComponent_Grid oGrid)
        {
            string[] vOrder = { "Engineering", "Finance", "HR", "Marketing" };
            for (int d = 0; d <= 3; d++)
                for (int i = 0; i < CS_EMP_COUNT; i++)
                    if (CS_DEPTS[i] == vOrder[d])
                        oGrid.AddRow(CS_NAMES[i], CS_DEPTS[i], CS_CITIES[i], CS_POSITIONS[i],
                            CS_SALARIES[i]);
        }

        private static string WrapFragment(string aTitle, string aDesc, string aGridHTML)
        {
            return @"<div class=""fw-bold mb-1"" style=""font-size:1rem;"">" + aTitle +
                "</div>" + @"<p class=""text-muted mb-3"" style=""font-size:0.82rem;"">" +
                aDesc + "</p>" + aGridHTML;
        }

        // ------------------------------------------------------------------
        // Shell page
        // ------------------------------------------------------------------

        public static string BuildShell()
        {
            const string CS_GRID_CSS =
                ".nav-tabs .nav-link{font-size:0.875rem;padding:0.4rem 0.85rem;}" +
                "#grid-panel{min-height:300px;}";

            string vBasicGrid;
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.TableID = "grid-basic";
                AddStdColumns(oGrid);
                AddSampleRows(oGrid);
                vBasicGrid = WrapFragment("Basic Grid",
                    "A responsive Bootstrap table with Striped and Hover styling. All 20 employee records rendered by the Delphi server.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }

            string vFooter;
            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "border-top mt-4 py-3 text-center text-muted small";
            var oP = new TsgcHTMLContainer("p");
            oP.CSSClass = "mb-0";
            oP.AddText("Built with ");
            var oA = new TsgcHTMLContainer("a");
            oA.Attributes =
                @"href=""https://www.esegece.com/products/sgchtml/"" target=""_blank"" rel=""noopener""";
            oA.CSSClass = "fw-semibold text-decoration-none";
            oA.AddRaw("sgcHTML");
            oP.Add(oA);
            oP.AddText(" components for Delphi and C++Builder.");
            oFooter.Add(oP);
            vFooter = oFooter.HTML;

            return @"<!DOCTYPE html><html lang=""en""><head>" + @"<meta charset=""UTF-8"">" +
                @"<meta name=""viewport"" content=""width=device-width, initial-scale=1"">" +
                @"<title>Grid Features Demo &mdash; sgcHTML</title>" +
                @"<link rel=""stylesheet"" href=""/bootstrap.min.css"">" + "<style>" +
                CS_GRID_CSS + "</style>" + "</head><body>" +
                @"<div class=""container-fluid py-4"" style=""max-width:1100px;"">" +
                @"<div class=""d-flex align-items-center gap-3 mb-4"">" + "<div>" +
                @"<h1 class=""mb-0"" style=""font-size:1.3rem;font-weight:700;"">Grid Features Demo</h1>" +
                @"<p class=""text-muted small mb-0"">TsgcHTMLComponent_Grid &mdash; sgcHTML showcase</p>" +
                "</div>" + "</div>" + @"<ul class=""nav nav-tabs mb-0"" id=""gridTabs"">" +
                @"<li class=""nav-item""><a class=""nav-link active"" href=""#""" +
                @" hx-get=""/grid/basic"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Basic</a></li>" +
                @"<li class=""nav-item""><a class=""nav-link"" href=""#""" +
                @" hx-get=""/grid/sort-filter"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Sort &amp; Filter</a></li>" +
                @"<li class=""nav-item""><a class=""nav-link"" href=""#""" +
                @" hx-get=""/grid/export"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Export</a></li>" +
                @"<li class=""nav-item""><a class=""nav-link"" href=""#""" +
                @" hx-get=""/grid/edit"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Inline Edit</a></li>" +
                @"<li class=""nav-item""><a class=""nav-link"" href=""#""" +
                @" hx-get=""/grid/group"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Group By</a></li>" +
                @"<li class=""nav-item""><a class=""nav-link"" href=""#""" +
                @" hx-get=""/grid/reorder"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Col Reorder</a></li>" +
                @"<li class=""nav-item""><a class=""nav-link"" href=""#""" +
                @" hx-get=""/grid/page?page=1"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Pagination</a></li>" +
                @"<li class=""nav-item""><a class=""nav-link"" href=""#""" +
                @" hx-get=""/grid/scroll"" hx-target=""#grid-panel"" hx-swap=""innerHTML""" +
                @" onclick=""setTab(this);return false;"">Virtual Scroll</a></li>" + "</ul>" +
                @"<div id=""grid-panel"" class=""border border-top-0 rounded-bottom p-3"">" +
                vBasicGrid + "</div>" + "</div>" + vFooter +
                @"<script src=""/bootstrap.bundle.min.js""></script>" +
                @"<script src=""/htmx.min.js""></script>" + "<script>" + "function setTab(el){" +
                @"document.querySelectorAll(""#gridTabs .nav-link"").forEach(function(a){a.classList.remove(""active"");});" +
                @"el.classList.add(""active"");" + "}" + "</script>" + "</body></html>";
        }

        // ------------------------------------------------------------------
        // Grid fragments
        // ------------------------------------------------------------------

        public static string BuildGridBasic()
        {
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.TableID = "grid-basic";
                AddStdColumns(oGrid);
                AddSampleRows(oGrid);
                return WrapFragment("Basic Grid",
                    "A responsive Bootstrap table with Striped and Hover styling. All 20 employee records rendered by the Delphi server.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildGridSortFilter()
        {
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.ShowSort = true;
                oGrid.ShowFilter = true;
                oGrid.TableID = "grid-sf";
                AddStdColumns(oGrid);
                AddSampleRows(oGrid);
                return WrapFragment("Sort &amp; Filter",
                    "Click any column header to sort. Use the filter input to narrow rows. All logic runs client-side via injected JavaScript &mdash; no server round-trips.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildGridExport()
        {
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.ShowExport = true;
                oGrid.ExportFileName = "employees.csv";
                oGrid.TableID = "grid-exp";
                AddStdColumns(oGrid);
                AddSampleRows(oGrid);
                return WrapFragment("Export",
                    "Click <strong>Export CSV</strong> to download the table as a CSV file, or <strong>Export PDF</strong> to open the print dialog. Both use browser-native APIs; no server call needed.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildGridInlineEdit()
        {
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.InlineEdit = true;
                oGrid.EditMode = TsgcHTMLGridEditMode.geCell;
                oGrid.TableID = "grid-edit";
                AddStdColumns(oGrid);
                AddSampleRows(oGrid);
                return WrapFragment("Inline Edit",
                    "Double-click any cell to edit it in place. Press Enter or click away to confirm. In production, wire a WebSocket endpoint to persist changes; here edits are local to the browser session.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildGridGroupBy()
        {
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.GroupByColumn = "dept";
                oGrid.ShowGrouping = true;
                oGrid.TableID = "grid-grp";
                AddStdColumns(oGrid);
                AddSortedRows(oGrid);
                return WrapFragment("Group By",
                    "Rows are grouped by Department with an item count badge. The Delphi server sorts the data and sets <code>GroupByColumn := 'dept'</code> before calling <code>.HTML</code>.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildGridColumnReorder()
        {
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.ColumnReorder = true;
                oGrid.TableID = "grid-reorder";
                AddStdColumns(oGrid);
                AddSampleRows(oGrid);
                return WrapFragment("Column Reorder",
                    "Drag any column header left or right to reposition it. The reorder uses the HTML5 Drag and Drop API injected by the grid renderer &mdash; no extra libraries needed.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildGridPagination(int aPage)
        {
            const int CS_PAGE_SIZE = 5;
            const int CS_TOTAL_PAGES = 4;

            if (aPage < 1)
                aPage = 1;
            if (aPage > CS_TOTAL_PAGES)
                aPage = CS_TOTAL_PAGES;

            int vStart = (aPage - 1) * CS_PAGE_SIZE;
            int vEnd = vStart + CS_PAGE_SIZE - 1;
            if (vEnd >= CS_EMP_COUNT)
                vEnd = CS_EMP_COUNT - 1;

            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.TableID = "grid-page";
                AddStdColumns(oGrid);
                for (int i = vStart; i <= vEnd; i++)
                    oGrid.AddRow(CS_NAMES[i], CS_DEPTS[i], CS_CITIES[i], CS_POSITIONS[i],
                        CS_SALARIES[i]);

                string vNavHTML;
                var oNav = new TsgcHTMLContainer("nav");
                oNav.Attributes = @"aria-label=""Grid pagination""";
                var oUL = new TsgcHTMLContainer("ul");
                oUL.CSSClass = "pagination pagination-sm justify-content-center mt-3";
                oNav.Add(oUL);

                // Prev
                var oLI = new TsgcHTMLContainer("li");
                if (aPage == 1)
                    oLI.CSSClass = "page-item disabled";
                else
                    oLI.CSSClass = "page-item";
                var oA = new TsgcHTMLContainer("a");
                oA.CSSClass = "page-link";
                if (aPage > 1)
                    oA.Attributes = @"href=""#"" hx-get=""/grid/page?page=" +
                        (aPage - 1).ToString(CultureInfo.InvariantCulture) +
                        @""" hx-target=""#grid-panel"" hx-swap=""innerHTML""";
                else
                    oA.Attributes = @"href=""#"" tabindex=""-1"" aria-disabled=""true""";
                oA.AddRaw("&laquo;");
                oLI.Add(oA);
                oUL.Add(oLI);

                // Page numbers
                for (int i = 1; i <= CS_TOTAL_PAGES; i++)
                {
                    oLI = new TsgcHTMLContainer("li");
                    if (i == aPage)
                        oLI.CSSClass = "page-item active";
                    else
                        oLI.CSSClass = "page-item";
                    oA = new TsgcHTMLContainer("a");
                    oA.CSSClass = "page-link";
                    if (i != aPage)
                        oA.Attributes = @"href=""#"" hx-get=""/grid/page?page=" +
                            i.ToString(CultureInfo.InvariantCulture) +
                            @""" hx-target=""#grid-panel"" hx-swap=""innerHTML""";
                    else
                        oA.Attributes = @"href=""#""";
                    oA.AddRaw(i.ToString(CultureInfo.InvariantCulture));
                    oLI.Add(oA);
                    oUL.Add(oLI);
                }

                // Next
                oLI = new TsgcHTMLContainer("li");
                if (aPage == CS_TOTAL_PAGES)
                    oLI.CSSClass = "page-item disabled";
                else
                    oLI.CSSClass = "page-item";
                oA = new TsgcHTMLContainer("a");
                oA.CSSClass = "page-link";
                if (aPage < CS_TOTAL_PAGES)
                    oA.Attributes = @"href=""#"" hx-get=""/grid/page?page=" +
                        (aPage + 1).ToString(CultureInfo.InvariantCulture) +
                        @""" hx-target=""#grid-panel"" hx-swap=""innerHTML""";
                else
                    oA.Attributes = @"href=""#"" tabindex=""-1"" aria-disabled=""true""";
                oA.AddRaw("&raquo;");
                oLI.Add(oA);
                oUL.Add(oLI);

                vNavHTML = oNav.HTML;

                return WrapFragment("Pagination", "Page " +
                    aPage.ToString(CultureInfo.InvariantCulture) + " of " +
                    CS_TOTAL_PAGES.ToString(CultureInfo.InvariantCulture) + " &mdash; " +
                    CS_PAGE_SIZE.ToString(CultureInfo.InvariantCulture) +
                    " records per page. The server returns only the current page slice; the Bootstrap pagination nav uses htmx to load each page without a full reload.",
                    oGrid.HTML + vNavHTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildGridVirtualScroll()
        {
            var oGrid = new TsgcHTMLComponent_Grid();
            try
            {
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.VirtualScroll = true;
                oGrid.VirtualScrollURL = "/grid/scroll-rows";
                oGrid.VisibleRows = 20;
                oGrid.TableID = "grid-vs";
                AddStdColumns(oGrid);
                for (int i = 0; i <= 59; i++)
                {
                    string vN, vD, vC, vP, vS;
                    GetVirtualEmployee(i, out vN, out vD, out vC, out vP, out vS);
                    oGrid.AddRow(vN, vD, vC, vP, vS);
                }
                return WrapFragment("Virtual Scroll",
                    "Only the first 20 rows render initially. Scroll down inside the table to trigger <code>hx-trigger=\"intersect root:#grid-vs-vswrap\"</code>, which loads the next 20 rows. 60 employees total.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildScrollRows(int aOffset, int aLimit, string aRootId)
        {
            if (aLimit <= 0)
                aLimit = 20;
            int vEnd = aOffset + aLimit - 1;
            if (vEnd > 59)
                vEnd = 59;

            var oResult = new StringBuilder();
            for (int i = aOffset; i <= vEnd; i++)
            {
                string vN, vD, vC, vP, vS;
                GetVirtualEmployee(i, out vN, out vD, out vC, out vP, out vS);
                oResult.Append("<tr><td>" + vN + "</td>" + "<td>" + vD + "</td>" +
                    "<td>" + vC + "</td>" + "<td>" + vP + "</td>" +
                    @"<td class=""text-end"">" + vS + "</td></tr>");
            }

            if (vEnd < 59)
            {
                string vTrigger;
                string vRootParam;
                if (aRootId != "")
                {
                    vTrigger = "intersect root:#" + aRootId;
                    vRootParam = "&rootId=" + aRootId;
                }
                else
                {
                    vTrigger = "revealed";
                    vRootParam = "";
                }
                oResult.Append(@"<tr hx-get=""/grid/scroll-rows?offset=" +
                    (vEnd + 1).ToString(CultureInfo.InvariantCulture) + "&limit=" +
                    aLimit.ToString(CultureInfo.InvariantCulture) + vRootParam + @"""" +
                    @" hx-trigger=""" + vTrigger + @"""" + @" hx-swap=""outerHTML"">" +
                    @"<td colspan=""5"" class=""text-center py-2 text-muted small"">" +
                    @"<span class=""spinner-border spinner-border-sm me-2""></span>" +
                    "Loading more&hellip;</td></tr>");
            }
            else
            {
                oResult.Append(
                    @"<tr><td colspan=""5"" class=""text-center py-2 text-muted small"">" +
                    "All 60 employees loaded.</td></tr>");
            }

            return oResult.ToString();
        }
    }
}
