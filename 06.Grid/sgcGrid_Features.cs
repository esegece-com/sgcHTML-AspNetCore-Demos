// ***************************************************************************
//  sgcGrid - Grid features demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\06.Grid\sgcGridDemo_Features.pas
//
//  The pages of the grid features added in 2026 (paging, selection, typed
//  columns and editing, summaries, keyboard and live rows). All the data
//  lives in memory, protected by a lock because the HTTP server serves every
//  request on its own thread.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
// sgc
using esegece.sgcWebSockets;

namespace GridDemo
{
    public static class TsgcGridDemoFeatures
    {
        private class TsgcDemoOrder
        {
            public string Id = "";
            public string Customer = "";
            public string Country = "";
            public string Date = "";
            public int AmountCents;
            public string Status = "";
            public bool Deleted;
        }

        private class TsgcDemoQuote
        {
            public string Symbol = "";
            public string Company = "";
            public int PriceCents;
            public int PrevCents;
            public int Volume;
            public string History = "";
            public string Alert = "";
        }

        private const int CS_ORDER_COUNT = 240;
        private const int CS_LIVE_BASE = 8;
        private const int CS_LIVE_MAX = 10;

        private static readonly string[] CS_CUSTOMERS = { "Acme Corp", "Globex",
            "Initech", "Umbrella Retail", "Stark Supplies", "Wayne Logistics",
            "Hooli", "Vandelay Imports", "Soylent Foods", "Tyrell Systems",
            "Cyberdyne", "Wonka Industries" };
        private static readonly string[] CS_COUNTRIES = { "Spain", "Germany", "France",
            "United States", "United Kingdom", "Italy", "Japan", "Canada" };
        private static readonly string[] CS_STATUSES = { "Pending", "Processing",
            "Shipped", "Delivered" };
        private static readonly string[] CS_SUPPLIERS = { "Northwind Traders",
            "Contoso Hardware", "Fabrikam Components", "Adventure Works",
            "Tailspin Toys", "Litware Software", "Proseware Services",
            "Wide World Importers", "Blue Yonder Parts", "Coho Electronics" };
        private static readonly string[] CS_MONTHS = { "jan", "feb", "mar", "apr",
            "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        private static readonly string[] CS_MONTH_TITLES = { "Jan", "Feb", "Mar", "Apr",
            "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        private static readonly string[] CS_IPOS = { "NOVA", "Nova Robotics", "QBIT",
            "Qubit Labs" };

        private static readonly object GLock = new object();
        private static readonly TsgcDemoOrder[] GOrders = new TsgcDemoOrder[CS_ORDER_COUNT];
        private static readonly string[][] GProducts = new string[12][];
        private static readonly TsgcDemoQuote[] GQuotes = new TsgcDemoQuote[CS_LIVE_MAX];
        private static readonly Random GRandom = new Random();
        private static int GQuoteCount;
        private static int GTick;
        private static int GIPO;

        static TsgcGridDemoFeatures()
        {
            InitOrders();
            InitProducts();
            InitQuotes();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        // invariant decimal text of an amount in cents: 123456 -> 1234.56
        private static string CentsToStr(int aCents)
        {
            int vAbs = Math.Abs(aCents);
            string vResult = (vAbs / 100).ToString(CultureInfo.InvariantCulture) + "." +
                (vAbs % 100).ToString("00", CultureInfo.InvariantCulture);
            if (aCents < 0)
                vResult = "-" + vResult;
            return vResult;
        }

        private static string ISODate(DateTime aDate)
        {
            return aDate.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);
        }

        private static string IntToStr(int aValue)
        {
            return aValue.ToString(CultureInfo.InvariantCulture);
        }

        private static int StrToIntDef(string aValue, int aDefault)
        {
            int vResult;
            if (int.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        private static string WrapSection(string aTitle, string aDesc, string aBody)
        {
            var oBox = new TsgcHTMLContainer("div");
            oBox.CSSClass = "mb-4";
            var oTitle = new TsgcHTMLContainer("div");
            oTitle.CSSClass = "fw-bold mb-1";
            oTitle.AddText(aTitle);
            oBox.Add(oTitle);
            var oDesc = new TsgcHTMLContainer("p");
            oDesc.CSSClass = "text-muted small mb-2";
            oDesc.AddRaw(aDesc);
            oBox.Add(oDesc);
            oBox.AddRaw(aBody);
            return oBox.HTML;
        }

        private static TsgcHTMLGridColumn AddCol(TsgcHTMLComponent_Grid oGrid, string aName,
            string aTitle, string aWidth)
        {
            TsgcHTMLGridColumn vResult = oGrid.Columns.Add();
            vResult.Name = aName;
            vResult.Title = aTitle;
            vResult.Width = aWidth;
            return vResult;
        }

        private static TsgcHTMLComponent_Grid NewGrid(string aTableID)
        {
            var vResult = new TsgcHTMLComponent_Grid();
            vResult.Striped = true;
            vResult.Hover = true;
            vResult.Responsive = true;
            vResult.TableID = aTableID;
            return vResult;
        }

        // ------------------------------------------------------------------
        // Sample data
        // ------------------------------------------------------------------

        private static void InitOrders()
        {
            DateTime vStart = new DateTime(2026, 1, 2);
            for (int i = 0; i < CS_ORDER_COUNT; i++)
            {
                var o = new TsgcDemoOrder();
                o.Id = "SO-" + IntToStr(10001 + i);
                o.Customer = CS_CUSTOMERS[(i * 7) % 12];
                o.Country = CS_COUNTRIES[(i * 3) % 8];
                o.Date = ISODate(vStart.AddDays((i * 11) % 260));
                o.AmountCents = 4990 + ((i * 7919) % 250000);
                o.Status = CS_STATUSES[(i * 5) % 4];
                o.Deleted = false;
                GOrders[i] = o;
            }
        }

        private static void SetProduct(int aIndex, string aSku, string aName, string aCategory,
            string aSupplier, string aPrice, string aStock, string aDate, string aActive)
        {
            GProducts[aIndex] = new string[] { aSku, aName, aCategory, aSupplier, aPrice,
                aStock, aDate, aActive };
        }

        private static void InitProducts()
        {
            SetProduct(0, "LAP-1001", "Ultrabook 14", "Hardware",
                "Contoso Hardware", "1299.00", "42", "2026-10-05", "1");
            SetProduct(1, "LAP-1002", "Workstation 16", "Hardware",
                "Contoso Hardware", "2499.90", "8", "2026-10-12", "1");
            SetProduct(2, "MON-2001", "27\" 4K Monitor", "Hardware",
                "Coho Electronics", "389.50", "120", "2026-11-02", "1");
            SetProduct(3, "KEY-3001", "Mechanical Keyboard", "Accessories",
                "Fabrikam Components", "129.99", "310", "2026-10-20", "1");
            SetProduct(4, "MOU-3002", "Wireless Mouse", "Accessories",
                "Fabrikam Components", "49.90", "0", "2026-09-30", "0");
            SetProduct(5, "DOC-3003", "USB-C Dock", "Accessories",
                "Blue Yonder Parts", "219.00", "64", "2026-10-15", "1");
            SetProduct(6, "SW-4001", "Office Suite License", "Software",
                "Litware Software", "99.00", "1000", "2026-12-01", "1");
            SetProduct(7, "SW-4002", "Antivirus 1 Year", "Software",
                "Litware Software", "39.95", "750", "2026-12-01", "1");
            SetProduct(8, "SRV-5001", "On-site Installation", "Services",
                "Proseware Services", "150.00", "25", "2026-10-08", "1");
            SetProduct(9, "SRV-5002", "Extended Warranty", "Services",
                "Proseware Services", "79.00", "400", "2026-11-15", "0");
            SetProduct(10, "TAB-1003", "Tablet 11", "Hardware",
                "Wide World Importers", "549.00", "17", "2026-10-28", "1");
            SetProduct(11, "HDS-3004", "Noise Cancelling Headset", "Accessories",
                "Northwind Traders", "189.00", "55", "2026-11-09", "1");
        }

        private static void SetQuote(int aIndex, string aSymbol, string aCompany,
            int aPriceCents, int aVolume)
        {
            var q = new TsgcDemoQuote();
            q.Symbol = aSymbol;
            q.Company = aCompany;
            q.PriceCents = aPriceCents;
            q.PrevCents = aPriceCents;
            q.Volume = aVolume;
            var oHistory = new StringBuilder();
            for (int i = 1; i <= 8; i++)
            {
                if (oHistory.Length > 0)
                    oHistory.Append(',');
                oHistory.Append(CentsToStr(aPriceCents));
            }
            q.History = oHistory.ToString();
            q.Alert = "";
            GQuotes[aIndex] = q;
        }

        private static void InitQuotes()
        {
            SetQuote(0, "ACME", "Acme Corp", 15420, 1250000);
            SetQuote(1, "GLBX", "Globex", 8735, 980000);
            SetQuote(2, "INIT", "Initech", 4210, 455000);
            SetQuote(3, "UMBR", "Umbrella Retail", 23150, 310000);
            SetQuote(4, "STRK", "Stark Supplies", 61200, 2100000);
            SetQuote(5, "WAYN", "Wayne Logistics", 12890, 760000);
            SetQuote(6, "HOOL", "Hooli", 33475, 1530000);
            SetQuote(7, "TYRL", "Tyrell Systems", 9960, 640000);
            GQuoteCount = CS_LIVE_BASE;
        }

        // ------------------------------------------------------------------
        // Orders (server paging, selection and bulk actions)
        // ------------------------------------------------------------------

        private static string OrderField(TsgcDemoOrder aOrder, string aName)
        {
            if (string.Equals(aName, "id", StringComparison.OrdinalIgnoreCase))
                return aOrder.Id;
            if (string.Equals(aName, "customer", StringComparison.OrdinalIgnoreCase))
                return aOrder.Customer;
            if (string.Equals(aName, "country", StringComparison.OrdinalIgnoreCase))
                return aOrder.Country;
            if (string.Equals(aName, "date", StringComparison.OrdinalIgnoreCase))
                return aOrder.Date;
            if (string.Equals(aName, "status", StringComparison.OrdinalIgnoreCase))
                return aOrder.Status;
            return "";
        }

        private static int CompareOrders(int aA, int aB, List<string> aSort)
        {
            int vResult = 0;
            for (int i = 0; i < aSort.Count; i++)
            {
                string vName = aSort[i];
                int p = vName.IndexOf(':');
                bool vDesc = false;
                if (p >= 0)
                {
                    vDesc = string.Equals(vName.Substring(p + 1), "desc",
                        StringComparison.OrdinalIgnoreCase);
                    vName = vName.Substring(0, p);
                }
                if (string.Equals(vName, "amount", StringComparison.OrdinalIgnoreCase))
                    vResult = GOrders[aA].AmountCents.CompareTo(GOrders[aB].AmountCents);
                else
                    vResult = string.Compare(OrderField(GOrders[aA], vName),
                        OrderField(GOrders[aB], vName), StringComparison.OrdinalIgnoreCase);
                if (vDesc)
                    vResult = -vResult;
                if (vResult != 0)
                    return vResult;
            }
            return aA - aB;
        }

        private static bool OrderMatches(TsgcDemoOrder aOrder, string aFilter)
        {
            return (aFilter == "") ||
                ((aOrder.Id + " " + aOrder.Customer + " " + aOrder.Country + " " +
                aOrder.Status + " " + aOrder.Date + " " +
                CentsToStr(aOrder.AmountCents)).ToLowerInvariant().IndexOf(aFilter,
                StringComparison.Ordinal) >= 0);
        }

        // comma separated list (the Delphi TStringList.CommaText), empty items skipped
        private static List<string> SplitList(string aText)
        {
            var vResult = new List<string>();
            foreach (string vItem in (aText ?? "").Split(','))
            {
                string vTrim = vItem.Trim();
                if (vTrim != "")
                    vResult.Add(vTrim);
            }
            return vResult;
        }

        // The same grid answers the first render, every page, sort and filter
        // request and every bulk action: the host keeps the configuration and only
        // hands over the rows of the current page plus TotalRows.
        private static string BuildOrdersGrid(string aTableID, string aPagingURL,
            bool aSelection, int aPage, int aPageSize, string aSort, string aFilter)
        {
            if (aPageSize <= 0)
                aPageSize = 10;
            string vFilter = (aFilter ?? "").Trim().ToLowerInvariant();
            var oGrid = NewGrid(aTableID);
            try
            {
                oGrid.ShowSort = true;
                oGrid.MultiSort = true;
                oGrid.ShowFilter = true;
                oGrid.ColumnResize = true;
                oGrid.PageSize = aPageSize;
                oGrid.PagingURL = aPagingURL;

                AddCol(oGrid, "id", "Order", "110");
                AddCol(oGrid, "customer", "Customer", "180");
                AddCol(oGrid, "country", "Country", "140");
                TsgcHTMLGridColumn oCol = AddCol(oGrid, "date", "Date", "120");
                oCol.DataType = TsgcHTMLGridDataType.gdtDate;
                oCol.DisplayFormat = "dd mmm yyyy";
                oCol = AddCol(oGrid, "amount", "Amount", "120");
                oCol.DataType = TsgcHTMLGridDataType.gdtCurrency;
                oCol.DisplayFormat = "#,##0.00";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                oCol = AddCol(oGrid, "status", "Status", "120");
                oCol.Renderer = TsgcHTMLGridRenderer.grrBadge;
                oCol.RendererOptions = "map=Pending:warning,Processing:info," +
                    "Shipped:primary,Delivered:success;style=secondary";

                if (aSelection)
                {
                    oGrid.SelectionMode = TsgcHTMLGridSelectionMode.gsmMultiple;
                    oGrid.KeyField = "id";
                    oGrid.BulkActionURL = "/grid/selection-bulk";
                    TsgcHTMLGridBulkAction oAction = oGrid.BulkActions.Add();
                    oAction.Caption = "Mark shipped";
                    oAction.ActionName = "ship";
                    oAction.ButtonStyle = TsgcHTMLButtonStyle.bsOutlinePrimary;
                    oAction = oGrid.BulkActions.Add();
                    oAction.Caption = "Mark delivered";
                    oAction.ActionName = "deliver";
                    oAction.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineSuccess;
                    oAction = oGrid.BulkActions.Add();
                    oAction.Caption = "Delete";
                    oAction.ActionName = "delete";
                    oAction.ButtonStyle = TsgcHTMLButtonStyle.bsOutlineDanger;
                    oAction.Confirm = "Delete the selected orders?";
                }

                List<string> oSort = SplitList(aSort);
                lock (GLock)
                {
                    // filter
                    int[] vIdx = new int[CS_ORDER_COUNT];
                    int vCount = 0;
                    for (int i = 0; i < CS_ORDER_COUNT; i++)
                        if (!GOrders[i].Deleted && OrderMatches(GOrders[i], vFilter))
                            vIdx[vCount++] = i;
                    // sort (insertion sort, a few hundred rows)
                    if (oSort.Count > 0)
                        for (int i = 1; i < vCount; i++)
                        {
                            int t = vIdx[i];
                            int j = i - 1;
                            while ((j >= 0) && (CompareOrders(vIdx[j], t, oSort) > 0))
                            {
                                vIdx[j + 1] = vIdx[j];
                                j--;
                            }
                            vIdx[j + 1] = t;
                        }
                    // page
                    int vPages = (vCount + aPageSize - 1) / aPageSize;
                    if (vPages < 1)
                        vPages = 1;
                    if (aPage > vPages)
                        aPage = vPages;
                    if (aPage < 1)
                        aPage = 1;
                    oGrid.TotalRows = vCount;
                    oGrid.CurrentPage = aPage;
                    for (int i = (aPage - 1) * aPageSize; i < aPage * aPageSize; i++)
                        if (i < vCount)
                        {
                            TsgcDemoOrder o = GOrders[vIdx[i]];
                            oGrid.AddRow(o.Id, o.Customer, o.Country, o.Date,
                                CentsToStr(o.AmountCents), o.Status);
                        }
                }
                return oGrid.HTML;
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        private static int ParamPage(Func<string, string> aParams)
        {
            return StrToIntDef(aParams("page"), 1);
        }

        private static int ParamPageSize(Func<string, string> aParams)
        {
            int vResult = StrToIntDef(aParams("pageSize"), 10);
            if ((vResult < 1) || (vResult > 100))
                vResult = 10;
            return vResult;
        }

        // ------------------------------------------------------------------
        // Paging page
        // ------------------------------------------------------------------

        private static string BuildClientPagingGrid()
        {
            string[] vNames = { "Alice Johnson", "Bob Smith",
                "Carol Williams", "David Brown", "Emma Davis", "Frank Miller",
                "Grace Wilson", "Henry Moore", "Isabella Taylor", "James Anderson",
                "Kate Thomas", "Liam Jackson", "Mia White", "Noah Harris",
                "Olivia Martin" };
            string[] vDepts = { "Engineering", "Marketing", "Finance", "HR" };
            var oGrid = NewGrid("grid-cpg");
            try
            {
                oGrid.ShowSort = true;
                oGrid.MultiSort = true;
                oGrid.ShowFilter = true;
                oGrid.PageSize = 5;
                AddCol(oGrid, "name", "Name", "170");
                AddCol(oGrid, "dept", "Department", "140");
                TsgcHTMLGridColumn oCol = AddCol(oGrid, "hired", "Hired", "120");
                oCol.DataType = TsgcHTMLGridDataType.gdtDate;
                oCol.DisplayFormat = "mmm yyyy";
                oCol = AddCol(oGrid, "salary", "Salary", "110");
                oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
                oCol.DisplayFormat = "#,##0";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                for (int i = 0; i <= 14; i++)
                    oGrid.AddRow(vNames[i], vDepts[i % 4],
                        ISODate(new DateTime(2018 + (i % 7), 1 + (i * 5) % 12, 1)),
                        IntToStr(58000 + ((i * 3719) % 50) * 1000));
                return oGrid.HTML;
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        private static string BuildFrozenGrid()
        {
            string[] vProducts = { "Ultrabook 14", "Workstation 16",
                "27\" 4K Monitor", "Mechanical Keyboard", "Wireless Mouse", "USB-C Dock",
                "Tablet 11", "Noise Cancelling Headset" };
            string[] vSkus = { "LAP-1001", "LAP-1002", "MON-2001",
                "KEY-3001", "MOU-3002", "DOC-3003", "TAB-1003", "HDS-3004" };
            var oGrid = NewGrid("grid-wide");
            try
            {
                oGrid.ShowSort = true;
                oGrid.ColumnResize = true;
                TsgcHTMLGridColumn oCol = AddCol(oGrid, "sku", "SKU", "100");
                oCol.Frozen = true;
                oCol = AddCol(oGrid, "product", "Product", "200");
                oCol.Frozen = true;
                for (int m = 0; m <= 11; m++)
                {
                    oCol = AddCol(oGrid, CS_MONTHS[m], CS_MONTH_TITLES[m], "90");
                    oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
                    oCol.DisplayFormat = "#,##0";
                    oCol.Align = TsgcHTMLGridAlign.gaRight;
                }
                oCol = AddCol(oGrid, "total", "Total 2025", "110");
                oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
                oCol.DisplayFormat = "#,##0";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                oCol.Summary = TsgcHTMLGridSummary.gsSum;
                for (int i = 0; i <= 7; i++)
                {
                    string[] oValues = new string[15];
                    oValues[0] = vSkus[i];
                    oValues[1] = vProducts[i];
                    int vTotal = 0;
                    for (int m = 0; m <= 11; m++)
                    {
                        int vUnits = 120 + ((i + 3) * (m + 7) * 37) % 900;
                        vTotal += vUnits;
                        oValues[m + 2] = IntToStr(vUnits);
                    }
                    oValues[14] = IntToStr(vTotal);
                    oGrid.AddRow(oValues);
                }
                return oGrid.HTML;
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildPaging()
        {
            return WrapSection("Server paging with MultiSort and ColumnResize",
                "240 sales orders live on the server. <code>PageSize = 10</code>, " +
                "<code>PagingURL = /grid/orders-page</code> and <code>TotalRows</code> " +
                "let the grid render the pager; every page, sort and filter change " +
                "asks the server for the rows of one page. Shift+click a second " +
                "header to sort by several columns (<code>MultiSort</code>) and drag " +
                "a header edge to resize it (<code>ColumnResize</code>).",
                BuildOrdersGrid("grid-srv", "/grid/orders-page", false, 1, 10, "", "")) +
                WrapSection("Client paging",
                "No <code>PagingURL</code>: all 15 rows are sent once and the browser " +
                "pages them 5 at a time, after filter and sort.",
                BuildClientPagingGrid()) +
                WrapSection("Frozen columns on a wide table",
                "SKU and Product have <code>Frozen = True</code>: scroll the table " +
                "sideways and they stay pinned on the left while the 12 months move.",
                BuildFrozenGrid());
        }

        public static string BuildOrdersPage(Func<string, string> aParams)
        {
            return BuildOrdersGrid("grid-srv", "/grid/orders-page", false,
                ParamPage(aParams), ParamPageSize(aParams), aParams("sort"),
                aParams("filter"));
        }

        // ------------------------------------------------------------------
        // Selection page
        // ------------------------------------------------------------------

        public static string BuildSelection()
        {
            return WrapSection("Multiple selection and bulk actions",
                "<code>SelectionMode = gsmMultiple</code> with <code>KeyField = " +
                "'id'</code> adds a checkbox column. Select a few orders (the " +
                "selection survives paging) and use the bulk bar: <em>Mark shipped</em>" +
                " and <em>Mark delivered</em> change the status, <em>Delete</em> asks " +
                "for confirmation first. <code>BulkActionURL</code> applies the action " +
                "on the server and answers with the refreshed grid.",
                BuildOrdersGrid("grid-sel", "/grid/selection-page", true, 1, 10, "", ""));
        }

        public static string BuildSelectionPage(Func<string, string> aParams)
        {
            return BuildOrdersGrid("grid-sel", "/grid/selection-page", true,
                ParamPage(aParams), ParamPageSize(aParams), aParams("sort"),
                aParams("filter"));
        }

        public static string ApplyBulkAction(Func<string, string> aParams)
        {
            string vAction = aParams("action");
            List<string> oKeys = SplitList(aParams("keys"));
            lock (GLock)
            {
                for (int i = 0; i < oKeys.Count; i++)
                    for (int j = 0; j < CS_ORDER_COUNT; j++)
                        if (GOrders[j].Id == oKeys[i])
                        {
                            if (vAction == "ship")
                                GOrders[j].Status = "Shipped";
                            else if (vAction == "deliver")
                                GOrders[j].Status = "Delivered";
                            else if (vAction == "delete")
                                GOrders[j].Deleted = true;
                        }
            }
            return BuildSelectionPage(aParams);
        }

        // ------------------------------------------------------------------
        // Typed columns and editing
        // ------------------------------------------------------------------

        public static string BuildTyped()
        {
            var oGrid = NewGrid("grid-typed");
            try
            {
                oGrid.ShowSort = true;
                oGrid.ShowFilter = true;
                oGrid.InlineEdit = true;
                oGrid.EditMode = TsgcHTMLGridEditMode.geCell;

                TsgcHTMLGridColumn oCol = AddCol(oGrid, "sku", "SKU", "110");
                oCol.Required = true;
                oCol.Pattern = "[A-Z]{2,3}-[0-9]{4}";
                oCol.ValidationMessage = "Use the format ABC-1234";
                oCol = AddCol(oGrid, "name", "Product", "200");
                oCol.Required = true;
                oCol.MaxLength = 40;
                oCol = AddCol(oGrid, "category", "Category", "130");
                oCol.EditorType = TsgcHTMLGridEditorType.getSelect;
                oCol.EditorItems.Add("Hardware");
                oCol.EditorItems.Add("Software");
                oCol.EditorItems.Add("Accessories");
                oCol.EditorItems.Add("Services");
                oCol = AddCol(oGrid, "supplier", "Supplier", "180");
                oCol.EditorType = TsgcHTMLGridEditorType.getLookup;
                oCol.LookupURL = "/grid/lookup/supplier";
                oCol = AddCol(oGrid, "price", "Price", "110");
                oCol.DataType = TsgcHTMLGridDataType.gdtCurrency;
                oCol.DisplayFormat = "#,##0.00";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                oCol.Required = true;
                oCol.MinValue = "0.01";
                oCol.MaxValue = "10000";
                oCol = AddCol(oGrid, "stock", "Stock", "90");
                oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
                oCol.DisplayFormat = "#,##0";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                oCol.MinValue = "0";
                oCol.MaxValue = "100000";
                oCol = AddCol(oGrid, "restock", "Next restock", "130");
                oCol.DataType = TsgcHTMLGridDataType.gdtDate;
                oCol.DisplayFormat = "dd mmm yyyy";
                oCol.MinValue = "2026-01-01";
                oCol = AddCol(oGrid, "active", "Active", "80");
                oCol.DataType = TsgcHTMLGridDataType.gdtBoolean;
                oCol.Align = TsgcHTMLGridAlign.gaCenter;

                lock (GLock)
                {
                    for (int i = 0; i < GProducts.Length; i++)
                        oGrid.AddRow((string[])GProducts[i].Clone());
                }
                return WrapSection("Typed columns, editors and validation",
                    "Double-click a cell to edit it. Each column picks its editor from " +
                    "<code>DataType</code> / <code>EditorType</code>: number inputs for " +
                    "Price and Stock, a date picker for Next restock, a checkbox for " +
                    "Active, a select from <code>EditorItems</code> for Category and a " +
                    "lookup for Supplier that asks <code>LookupURL</code> as you type. " +
                    "Values are validated in the browser (<code>Required</code>, " +
                    "<code>MinValue</code>/<code>MaxValue</code>, <code>Pattern</code>, " +
                    "<code>MaxLength</code>) and valid ones are sent to the server over " +
                    "the sgcHTMX WebSocket, which keeps them in memory: reload the tab " +
                    "and your changes are still there.", oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        public static string BuildSupplierLookup(string aQuery)
        {
            // only <option> elements: the grid puts them in the datalist of the editor
            var vResult = new StringBuilder();
            string vQuery = (aQuery ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < CS_SUPPLIERS.Length; i++)
                if ((vQuery == "") || (CS_SUPPLIERS[i].ToLowerInvariant().IndexOf(vQuery,
                    StringComparison.Ordinal) >= 0))
                {
                    var oOption = new TsgcHTMLContainer("option");
                    oOption.Attributes = "value=\"" +
                        sgcHTMLHelpers.sgcHTMLAttrEncode(CS_SUPPLIERS[i]) + "\"";
                    oOption.AddText(CS_SUPPLIERS[i]);
                    vResult.Append(oOption.HTML);
                }
            return vResult.ToString();
        }

        // gridEdit messages of the sgcHTMX bridge, returns true when handled
        public static bool ApplyGridEdit(string aMessage)
        {
            if (string.IsNullOrEmpty(aMessage) || aMessage.IndexOf("gridEdit",
                StringComparison.Ordinal) < 0)
                return false;
            string vAction, vTable, vValue;
            int vRow, vCol;
            try
            {
                using (JsonDocument oJSON = JsonDocument.Parse(aMessage))
                {
                    JsonElement vRoot = oJSON.RootElement;
                    if (vRoot.ValueKind != JsonValueKind.Object)
                        return false;
                    vAction = NodeText(vRoot, "action");
                    vTable = NodeText(vRoot, "table");
                    vRow = StrToIntDef(NodeText(vRoot, "row"), -1);
                    vCol = StrToIntDef(NodeText(vRoot, "col"), -1);
                    vValue = NodeText(vRoot, "value");
                }
            }
            catch
            {
                return false;
            }
            if (vAction != "gridEdit")
                return false;
            lock (GLock)
            {
                if ((vTable == "grid-typed") && (vRow >= 0) &&
                    (vRow < GProducts.Length) && (vCol >= 0) && (vCol <= 7))
                {
                    GProducts[vRow][vCol] = vValue;
                    return true;
                }
                if ((vTable == "grid-live") && (vRow >= 0) &&
                    (vRow < GQuoteCount) && (vCol == 6))
                {
                    GQuotes[vRow].Alert = vValue;
                    return true;
                }
            }
            return false;
        }

        private static string NodeText(JsonElement aRoot, string aName)
        {
            JsonElement vNode;
            if (!aRoot.TryGetProperty(aName, out vNode))
                return "";
            switch (vNode.ValueKind)
            {
                case JsonValueKind.String:
                    return vNode.GetString() ?? "";
                case JsonValueKind.Number:
                    return vNode.GetRawText();
                case JsonValueKind.True:
                    return "True";
                case JsonValueKind.False:
                    return "False";
                default:
                    return "";
            }
        }

        // ------------------------------------------------------------------
        // Summaries page
        // ------------------------------------------------------------------

        private static TsgcHTMLGridFormatRule AddRule(TsgcHTMLComponent_Grid oGrid,
            string aColumn, TsgcHTMLGridRuleOperator aOperator, string aValue,
            string aClass, bool aRow)
        {
            TsgcHTMLGridFormatRule vResult = oGrid.FormatRules.Add();
            vResult.ColumnName = aColumn;
            vResult.RuleOperator = aOperator;
            vResult.Value = aValue;
            vResult.CSSClass = aClass;
            vResult.ApplyToRow = aRow;
            return vResult;
        }

        public static string BuildSummaries()
        {
            string[] vReps = { "Laura Gomez", "Marco Rossi",
                "Hannah Weber", "Pierre Durand", "Sofia Lopez", "Tom Baker",
                "Yuki Tanaka", "Anna Schmidt", "Lucas Martin", "Emily Clark" };
            string[] vRegions = { "Americas", "Americas", "APAC",
                "APAC", "APAC", "EMEA", "EMEA", "EMEA", "EMEA", "EMEA" };
            int[] vDeals = { 18, 11, 23, 9, 14, 20, 7, 16, 12, 25 };
            int[] vRevenue = { 642000, 318500, 715250, 205900,
                433100, 588000, 149750, 502300, 297400, 811600 };
            int[] vQuota = { 107, 64, 119, 52, 88, 98, 41, 93, 71, 128 };
            string[] vStatus = { "On track", "At risk", "Ahead",
                "At risk", "On track", "On track", "Behind", "On track", "At risk",
                "Ahead" };
            string[] vTrend = { "4,6,5,8,9,11", "6,5,4,4,3,3",
                "3,5,7,8,10,12", "5,4,4,3,2,2", "5,5,6,6,7,7", "7,6,8,8,9,9",
                "4,3,3,2,2,1", "6,7,6,8,8,9", "5,5,4,5,4,4", "6,8,9,11,12,14" };
            string[] vLast = { "2026-09-18", "2026-08-30",
                "2026-09-22", "2026-07-14", "2026-09-10", "2026-09-24", "2026-06-28",
                "2026-09-19", "2026-09-02", "2026-09-25" };
            string[] vCert = { "1", "0", "1", "0", "1", "1", "0", "1", "0", "1" };

            var oGrid = NewGrid("grid-sum");
            try
            {
                oGrid.ShowSort = true;
                oGrid.ShowFilter = true;
                oGrid.GroupByColumn = "region";
                oGrid.ShowGrouping = true;

                TsgcHTMLGridColumn oCol = AddCol(oGrid, "rep", "Sales rep", "150");
                oCol.Summary = TsgcHTMLGridSummary.gsCount;
                oCol.SummaryFormat = "0\" reps\"";
                AddCol(oGrid, "region", "Region", "100");
                oCol = AddCol(oGrid, "deals", "Deals", "80");
                oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                oCol.Summary = TsgcHTMLGridSummary.gsSum;
                oCol = AddCol(oGrid, "revenue", "Revenue", "130");
                oCol.DataType = TsgcHTMLGridDataType.gdtCurrency;
                oCol.DisplayFormat = "#,##0";
                oCol.Align = TsgcHTMLGridAlign.gaRight;
                oCol.Summary = TsgcHTMLGridSummary.gsSum;
                oCol.SummaryFormat = "#,##0";
                oCol = AddCol(oGrid, "quota", "Quota %", "150");
                oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
                oCol.Renderer = TsgcHTMLGridRenderer.grrProgress;
                oCol.RendererOptions = "max=150;style=success";
                oCol.Summary = TsgcHTMLGridSummary.gsAvg;
                oCol.SummaryFormat = "0.0\"% avg\"";
                oCol = AddCol(oGrid, "status", "Status", "110");
                oCol.Renderer = TsgcHTMLGridRenderer.grrBadge;
                oCol.RendererOptions = "map=Ahead:success,On track:primary," +
                    "At risk:warning,Behind:danger;style=secondary";
                oCol = AddCol(oGrid, "trend", "Last 6 months", "110");
                oCol.Renderer = TsgcHTMLGridRenderer.grrSparkline;
                oCol = AddCol(oGrid, "last", "Last deal", "120");
                oCol.DataType = TsgcHTMLGridDataType.gdtDate;
                oCol.DisplayFormat = "dd mmm yyyy";
                oCol.Summary = TsgcHTMLGridSummary.gsMax;
                oCol = AddCol(oGrid, "cert", "Certified", "90");
                oCol.Renderer = TsgcHTMLGridRenderer.grrBoolean;
                oCol.Align = TsgcHTMLGridAlign.gaCenter;

                // row rules: whole row tinted, first match wins
                AddRule(oGrid, "quota", TsgcHTMLGridRuleOperator.groLess, "50",
                    "table-danger", true);
                AddRule(oGrid, "quota", TsgcHTMLGridRuleOperator.groGreaterOrEqual, "120",
                    "table-success", true);
                // cell rules
                AddRule(oGrid, "revenue", TsgcHTMLGridRuleOperator.groGreater, "600000",
                    "fw-bold text-success", false);
                AddRule(oGrid, "deals", TsgcHTMLGridRuleOperator.groLess, "10",
                    "text-danger", false);

                for (int i = 0; i <= 9; i++)
                    oGrid.AddRow(vReps[i], vRegions[i], IntToStr(vDeals[i]),
                        IntToStr(vRevenue[i]), IntToStr(vQuota[i]), vStatus[i], vTrend[i],
                        vLast[i], vCert[i]);
                return WrapSection("Summaries, subtotals, format rules and renderers",
                    "Footer <code>Summary</code> per column: count of reps, sum of deals " +
                    "and revenue, average quota and the most recent deal (max). With " +
                    "<code>GroupByColumn = 'region'</code> every region gets its own " +
                    "subtotal row. <code>FormatRules</code> tint whole rows (quota under " +
                    "50% red, 120% or more green) and single cells (revenue over 600,000 " +
                    "bold, fewer than 10 deals red). Renderers draw Quota as a progress " +
                    "bar, Status as a badge, the trend as a sparkline and Certified as a " +
                    "check mark. Filter or sort: the footer is recomputed in the browser.",
                    oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // Keyboard and live rows
        // ------------------------------------------------------------------

        private static string ChangeText(TsgcDemoQuote aQuote)
        {
            if (aQuote.PrevCents == 0)
                return "0.00";
            return CentsToStr((int)Math.Round((aQuote.PriceCents - aQuote.PrevCents) *
                10000.0 / aQuote.PrevCents, MidpointRounding.ToEven));
        }

        // called with GLock held
        private static TsgcHTMLComponent_Grid NewLiveGrid()
        {
            var vResult = NewGrid("grid-live");
            vResult.ShowSort = true;
            vResult.ShowFilter = true;
            vResult.LiveRows = true;
            vResult.KeyboardNavigation = true;
            vResult.InlineEdit = true;
            vResult.SelectionMode = TsgcHTMLGridSelectionMode.gsmMultiple;
            vResult.KeyField = "symbol";

            AddCol(vResult, "symbol", "Symbol", "90");
            AddCol(vResult, "company", "Company", "170");
            TsgcHTMLGridColumn oCol = AddCol(vResult, "price", "Price", "100");
            oCol.DataType = TsgcHTMLGridDataType.gdtCurrency;
            oCol.DisplayFormat = "#,##0.00";
            oCol.Align = TsgcHTMLGridAlign.gaRight;
            oCol = AddCol(vResult, "change", "Change %", "100");
            oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
            oCol.DisplayFormat = "+0.00;-0.00;0.00";
            oCol.Align = TsgcHTMLGridAlign.gaRight;
            oCol = AddCol(vResult, "volume", "Volume", "120");
            oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
            oCol.DisplayFormat = "#,##0";
            oCol.Align = TsgcHTMLGridAlign.gaRight;
            oCol.Summary = TsgcHTMLGridSummary.gsSum;
            oCol = AddCol(vResult, "trend", "Trend", "100");
            oCol.Renderer = TsgcHTMLGridRenderer.grrSparkline;
            oCol = AddCol(vResult, "alert", "Alert at", "100");
            oCol.DataType = TsgcHTMLGridDataType.gdtNumber;
            oCol.MinValue = "0";

            AddRule(vResult, "change", TsgcHTMLGridRuleOperator.groGreater, "0",
                "text-success", false);
            AddRule(vResult, "change", TsgcHTMLGridRuleOperator.groLess, "0",
                "text-danger", false);

            for (int i = 0; i < GQuoteCount; i++)
            {
                TsgcDemoQuote q = GQuotes[i];
                vResult.AddRow(q.Symbol, q.Company, CentsToStr(q.PriceCents),
                    ChangeText(q), IntToStr(q.Volume), q.History, q.Alert);
            }
            return vResult;
        }

        public static string BuildLive()
        {
            TsgcHTMLComponent_Grid oGrid;
            lock (GLock)
                oGrid = NewLiveGrid();
            try
            {
                return WrapSection("Keyboard navigation and live rows",
                    "Click a cell, then use the arrows, Home/End and PgUp/PgDn " +
                    "(<code>KeyboardNavigation</code>). Space toggles the row selection, " +
                    "Enter or F2 edits the <em>Alert at</em> cell, Tab moves on and Esc " +
                    "cancels. With <code>LiveRows</code> the server pushes a price " +
                    "update every 2 seconds through the sgcHTMX WebSocket " +
                    "(<code>GetRowFragmentHTML</code>), lists a new symbol from time to " +
                    "time (<code>GetRowInsertFragmentHTML</code>) and delists it later " +
                    "(<code>GetRowDeleteFragmentHTML</code>). Sort, selection and the " +
                    "volume total follow the pushed rows.", oGrid.HTML);
            }
            finally
            {
                oGrid.Dispose();
            }
        }

        private static void MoveQuote(int aIndex)
        {
            TsgcDemoQuote q = GQuotes[aIndex];
            int vDelta = (q.PriceCents * (GRandom.Next(401) - 200)) / 10000;
            if (vDelta == 0)
                vDelta = GRandom.Next(3) - 1;
            q.PriceCents = q.PriceCents + vDelta;
            if (q.PriceCents < 100)
                q.PriceCents = 100;
            q.Volume = q.Volume + GRandom.Next(25000);
            int p = q.History.IndexOf(',');
            if (p >= 0)
                q.History = q.History.Substring(p + 1);
            q.History = q.History + "," + CentsToStr(q.PriceCents);
        }

        // advances the market one step and returns the out of band fragments to
        // push, one push per item of aFragments
        public static void LiveTick(List<string> aFragments)
        {
            lock (GLock)
            {
                GTick++;
                if ((GTick % 12 == 6) && (GQuoteCount < CS_LIVE_MAX))
                {
                    // a listing: an insert push on its own (never mixed with updates)
                    SetQuote(GQuoteCount, CS_IPOS[(GIPO % 2) * 2],
                        CS_IPOS[(GIPO % 2) * 2 + 1], 2000 + GRandom.Next(3000), 50000);
                    GIPO++;
                    GQuoteCount++;
                    var oGrid = NewLiveGrid();
                    try
                    {
                        aFragments.Add(oGrid.GetRowInsertFragmentHTML(GQuoteCount - 1,
                            TsgcHTMLGridInsertPosition.gipBottom));
                    }
                    finally
                    {
                        oGrid.Dispose();
                    }
                }
                else if ((GTick % 12 == 0) && (GQuoteCount > CS_LIVE_BASE))
                {
                    // a delisting: the last listed symbol goes away, so the row indexes
                    // of the others never move
                    GQuoteCount--;
                    string vKey = GQuotes[GQuoteCount].Symbol;
                    var oGrid = NewLiveGrid();
                    try
                    {
                        aFragments.Add(oGrid.GetRowDeleteFragmentHTML(vKey));
                    }
                    finally
                    {
                        oGrid.Dispose();
                    }
                }
                else
                {
                    // two price updates in one push
                    int vA = GRandom.Next(GQuoteCount);
                    int vB = (vA + 1 + GRandom.Next(GQuoteCount - 1)) % GQuoteCount;
                    MoveQuote(vA);
                    MoveQuote(vB);
                    var oGrid = NewLiveGrid();
                    try
                    {
                        aFragments.Add(oGrid.GetRowFragmentHTML(vA) +
                            oGrid.GetRowFragmentHTML(vB));
                    }
                    finally
                    {
                        oGrid.Dispose();
                    }
                }
            }
        }
    }
}
