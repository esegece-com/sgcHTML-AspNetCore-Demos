// ***************************************************************************
//  sgcReports - reporting and BI portal web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\15.Reports\sgcReports_Analytics.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
//
//  The Analytics section: /analytics (chart gallery), /analytics/market
//  (candlesticks with indicators) and /analytics/pivot (pivot lab). Every
//  chart and pivot runs on the same SQLite order data as the rest of the
//  portal. Live points are pushed: the server push loop broadcasts
//  LiveChartFragment and LiveCandleFragment through the sgcHTMX WebSocket
//  bridge, which runs the scripts carried by out-of-band fragments.
//
//  The Delphi unit keeps the queries and components it creates in an owned
//  list and frees them in the destructor; here the garbage collector does
//  that, and every TReportsQuery is disposed as soon as its DataTable has been
//  read (the DataTable outlives the connection, see TReportsQuery).
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
// sgc
using esegece.sgcWebSockets;

namespace Reports
{
    public class TReportsAnalytics
    {
        private const string CS_COLORS = "#0d6efd,#6610f2,#198754,#fd7e14,#dc3545,#20c997," +
            "#6f42c1,#0dcaf0,#ffc107,#6c757d";
        private const string CS_SQL_FROM = " FROM order_lines ol JOIN orders o ON o.id = ol.order_id" +
            " JOIN customers c ON c.id = o.customer_id" +
            " JOIN regions r ON r.id = c.region_id" +
            " JOIN salespeople s ON s.id = o.salesperson_id" +
            " JOIN products p ON p.id = ol.product_id" +
            " JOIN categories cat ON cat.id = p.category_id";
        private const int CS_CANDLE_MAX = 80;

        // process-wide live state, like the Delphi unit globals
        private static readonly object GLock = new object();
        private static readonly Random GRandom = new Random();
        private static double GLiveBase = 0;
        private static readonly List<string> GCandleLabels = new List<string>();
        private static readonly List<double> GO = new List<double>();
        private static readonly List<double> GH = new List<double>();
        private static readonly List<double> GL = new List<double>();
        private static readonly List<double> GC = new List<double>();
        private static readonly List<double> GV = new List<double>();
        private static int GTick = 0;

        private readonly TReportsDBPool FDB;
        private readonly TReportsPages FPages;
        private string FLang = "";

        public TReportsAnalytics(TReportsDBPool aDB, TReportsPages aPages)
        {
            FDB = aDB;
            FPages = aPages;
        }

        private string Tr(string aKey)
        {
            return TReportsI18n.T(FLang, aKey);
        }

        // ----- helpers ----- //

        private static double NextRandom()
        {
            lock (GRandom)
                return GRandom.NextDouble();
        }

        private static int NextRandom(int aMax)
        {
            lock (GRandom)
                return GRandom.Next(aMax);
        }

        private static string ColorAt(int aIndex)
        {
            string[] vList = CS_COLORS.Split(',');
            return vList[aIndex % vList.Length];
        }

        private static string DaysAgo(int aDays)
        {
            return DateTime.Today.AddDays(-aDays).ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture);
        }

        private static double AsFloat(DataRow aRow, string aField)
        {
            if (!aRow.Table.Columns.Contains(aField))
                return 0;
            object vValue = aRow[aField];
            if (vValue == null || vValue is DBNull)
                return 0;
            try
            {
                return Convert.ToDouble(vValue, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string AsString(DataRow aRow, string aField)
        {
            object vValue = aRow[aField];
            if (vValue == null || vValue is DBNull)
                return "";
            return Convert.ToString(vValue, CultureInfo.InvariantCulture) ?? "";
        }

        private static double RoundTo1(double aValue)
        {
            return Math.Round(aValue, 1);
        }

        private static string CardHTML(string aTitle, string aHint, string aBody)
        {
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4 h-100";
            oCard.Title = aTitle;
            if (aHint != "")
            {
                TsgcHTMLParagraph oHint = new TsgcHTMLParagraph(aHint);
                oHint.CSSClass = "text-muted small mb-3";
                oCard.Body.Add(oHint);
            }
            oCard.Body.AddRaw(aBody);
            return oCard.HTML;
        }

        private static string ToggleHTML(string aCaption, string[] aHrefs,
            string[] aTexts, string[] aKeys, string aActive)
        {
            TsgcHTMLContainer oBox = new TsgcHTMLContainer("div");
            oBox.CSSClass = "d-flex align-items-center gap-2 mb-3";
            TsgcHTMLContainer oText = new TsgcHTMLContainer("span");
            oText.CSSClass = "small text-muted";
            oText.AddText(aCaption);
            oBox.Add(oText);
            TsgcHTMLContainer oGroup = new TsgcHTMLContainer("div");
            oGroup.CSSClass = "btn-group btn-group-sm";
            oGroup.Attributes = "role=\"group\"";
            for (int vI = 0; vI < aHrefs.Length; vI++)
            {
                TsgcHTMLLink oLink = new TsgcHTMLLink(aHrefs[vI], aTexts[vI]);
                if (string.Equals(aKeys[vI], aActive, StringComparison.OrdinalIgnoreCase))
                    oLink.CSSClass = "btn btn-primary";
                else
                    oLink.CSSClass = "btn btn-outline-primary";
                oGroup.Add(oLink);
            }
            oBox.Add(oGroup);
            return oBox.HTML;
        }

        private static string NoteHTML(string aText)
        {
            TsgcHTMLParagraph oP = new TsgcHTMLParagraph(aText);
            oP.CSSClass = "fw-semibold small mb-2";
            return oP.HTML;
        }

        private static string EmptyDiv(string aID, string aCSSClass)
        {
            TsgcHTMLContainer oDiv = new TsgcHTMLContainer("div");
            oDiv.ID = aID;
            oDiv.CSSClass = aCSSClass;
            return oDiv.HTML;
        }

        private static string URLEnc(string aValue)
        {
            StringBuilder vResult = new StringBuilder();
            byte[] vB = Encoding.UTF8.GetBytes(aValue ?? "");
            for (int vI = 0; vI < vB.Length; vI++)
            {
                char vCh = (char)vB[vI];
                if ((vCh >= 'A' && vCh <= 'Z') || (vCh >= 'a' && vCh <= 'z') ||
                    (vCh >= '0' && vCh <= '9') || vCh == '-' || vCh == '_' ||
                    vCh == '.' || vCh == ',' || vCh == ':')
                    vResult.Append(vCh);
                else
                    vResult.Append('%').Append(vB[vI].ToString("X2"));
            }
            return vResult.ToString();
        }

        private static string GroupKey(string aGroup)
        {
            if (string.Equals(aGroup, "quarter", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aGroup, "month", StringComparison.OrdinalIgnoreCase))
                return aGroup.ToLowerInvariant();
            return "year";
        }

        private static string StyleKey(string aStyle)
        {
            if (string.Equals(aStyle, "ohlc", StringComparison.OrdinalIgnoreCase))
                return "ohlc";
            return "candle";
        }

        // Layout query string carried by drill-through, export and field chooser
        // URLs. lr / lc / lv (not rows / cols) because the drill-through post uses
        // rows and cols for the clicked cell path.
        private static string LayoutQS(string aGroup, string aRows, string aCols,
            string aValues)
        {
            string vResult = "?grp=" + GroupKey(aGroup);
            if (aRows != "" || aCols != "" || aValues != "")
                vResult = vResult + "&lr=" + URLEnc(aRows) + "&lc=" + URLEnc(aCols) +
                    "&lv=" + URLEnc(aValues);
            return vResult;
        }

        private class TSeries
        {
            public List<string> Labels = new List<string>();
            public List<double> V1 = new List<double>();
            public List<double> V2 = new List<double>();
        }

        private static TSeries LoadSeries(TReportsDBPool aDB, string aSQL, string aFrom)
        {
            TSeries vResult = new TSeries();
            using (TReportsQuery oQ = aDB.NewQuery(aSQL, true))
            {
                if (aFrom != "")
                    oQ.SetParamStr("d", aFrom);
                oQ.Open();
                foreach (DataRow oRow in oQ.DataSet.Rows)
                {
                    vResult.Labels.Add(AsString(oRow, "lbl"));
                    vResult.V1.Add(AsFloat(oRow, "v1"));
                    vResult.V2.Add(AsFloat(oRow, "v2"));
                }
            }
            return vResult;
        }

        private static void FillLabels(TsgcHTMLComponent_Chart aChart,
            List<string> aLabels)
        {
            for (int vI = 0; vI < aLabels.Count; vI++)
                aChart.AddLabel(aLabels[vI]);
        }

        private static TsgcHTMLChartDataset NewDataset(TsgcHTMLComponent_Chart aChart,
            string aLabel, IList<double> aValues, string aColor)
        {
            TsgcHTMLChartDataset vResult = aChart.Datasets.Add();
            vResult.Label_ = aLabel;
            double[] vValues = new double[aValues.Count];
            aValues.CopyTo(vValues, 0);
            vResult.SetData(vValues);
            vResult.BorderColor = aColor;
            vResult.BackgroundColor = aColor;
            return vResult;
        }

        private static TsgcHTMLComponent_Chart NewChart(string aID,
            TsgcHTMLChartType aType)
        {
            TsgcHTMLComponent_Chart vResult = new TsgcHTMLComponent_Chart();
            vResult.ChartID = aID;
            vResult.ChartType = aType;
            vResult.CSSHeight = "300px";
            vResult.Theme = TsgcHTMLChartTheme.chtAuto;
            return vResult;
        }

        // ----- chart gallery ----- //

        public string BuildCharts(TReportsPageCtx aCtx)
        {
            FLang = aCtx.Lang;
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-4 mb-4";
            TsgcHTMLComponent_Chart oChart;
            TsgcHTMLChartDataset oDS;
            TsgcHTMLChartAxis oAxis;
            double[] vVals;
            double vSum;

            void AddCard(TsgcHTMLColWidth aWidth, string aTitleKey, string aHint,
                string aBody)
            {
                oRow.Col(aWidth).AddRaw(CardHTML(Tr(aTitleKey), aHint, aBody));
            }

            // 1. area: monthly revenue and margin, 24 months
            TSeries vS = LoadSeries(FDB, "SELECT substr(o.order_date, 1, 7) AS lbl, " +
                "ROUND(SUM(ol.line_total), 0) AS v1, " +
                "ROUND(SUM(ol.line_total - ol.qty * p.unit_cost), 0) AS v2" +
                CS_SQL_FROM + " WHERE o.status <> 'Cancelled' AND " +
                "o.order_date >= :d GROUP BY lbl ORDER BY lbl", DaysAgo(730));
            oChart = NewChart("anArea", TsgcHTMLChartType.ctArea);
            FillLabels(oChart, vS.Labels);
            NewDataset(oChart, Tr("lbl.revenue"), vS.V1, ColorAt(0));
            NewDataset(oChart, Tr("lbl.margin"), vS.V2, ColorAt(2));
            oChart.ShowExportPNG = true;
            oChart.ExportFileName = "revenue-trend";
            oChart.TooltipFormat = "{v}";
            AddCard(TsgcHTMLColWidth.cw6, "an.area",
                "ChartType ctArea, ShowExportPNG, Theme chtAuto", oChart.HTML);

            // 2. mixed: revenue bars + margin % line on a second Y axis
            oChart = NewChart("anMixed", TsgcHTMLChartType.ctMixed);
            vVals = new double[vS.V1.Count];
            for (int vI = 0; vI < vS.V1.Count; vI++)
                if (vS.V1[vI] != 0)
                    vVals[vI] = RoundTo1(vS.V2[vI] * 100 / vS.V1[vI]);
                else
                    vVals[vI] = 0;
            FillLabels(oChart, vS.Labels);
            oDS = NewDataset(oChart, Tr("lbl.revenue"), vS.V1, ColorAt(0));
            oDS.ChartType = TsgcHTMLChartDatasetType.cdtBar;
            oDS.YAxisID = "y";
            oDS = NewDataset(oChart, Tr("lbl.margin") + " %", vVals, ColorAt(4));
            oDS.ChartType = TsgcHTMLChartDatasetType.cdtLine;
            oDS.YAxisID = "y2";
            oAxis = oChart.Axes.Add();
            oAxis.AxisID = "y";
            oAxis.Title = Tr("lbl.revenue");
            oAxis.BeginAtZero = true;
            oAxis.TickFormat = "{v}";
            oAxis = oChart.Axes.Add();
            oAxis.AxisID = "y2";
            oAxis.Title = Tr("lbl.margin") + " %";
            oAxis.Position = TsgcHTMLChartAxisPosition.capRight;
            oAxis.Min = "0";
            oAxis.GridLines = false;
            oAxis.TickFormat = "{v}%";
            oChart.ShowExportPNG = true;
            oChart.ExportFileName = "revenue-margin";
            AddCard(TsgcHTMLColWidth.cw6, "an.mixed", "ctMixed, Dataset.ChartType / YAxisID, " +
                "Axes (Title, Position, TickFormat)", oChart.HTML);

            // 3. horizontal bar: top salespeople
            vS = LoadSeries(FDB, "SELECT s.name AS lbl, " +
                "ROUND(SUM(ol.line_total), 0) AS v1" + CS_SQL_FROM +
                " WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY s.id ORDER BY v1 DESC LIMIT 10", DaysAgo(365));
            oChart = NewChart("anHBar", TsgcHTMLChartType.ctHorizontalBar);
            oChart.CSSHeight = "340px";
            oChart.ShowLegend = false;
            FillLabels(oChart, vS.Labels);
            oDS = NewDataset(oChart, Tr("lbl.revenue"), vS.V1, ColorAt(1));
            oDS.PointColors = CS_COLORS;
            oChart.ShowDataLabels = true;
            oChart.DataLabelFormat = "{v}";
            AddCard(TsgcHTMLColWidth.cw6, "an.hbar",
                "ctHorizontalBar, PointColors, ShowDataLabels", oChart.HTML);

            // 4. 100% stacked: category mix per region
            SortedSet<string> vRegionSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            SortedSet<string> vCatSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            DataTable vTable;
            using (TReportsQuery oQ = FDB.NewQuery("SELECT r.name AS region, " +
                "cat.name AS category, ROUND(SUM(ol.line_total), 0) AS v1" + CS_SQL_FROM +
                " WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY r.name, cat.name ORDER BY r.name, cat.name", true))
            {
                oQ.SetParamStr("d", DaysAgo(365));
                oQ.Open();
                vTable = oQ.DataSet;
            }
            foreach (DataRow oR in vTable.Rows)
            {
                vRegionSet.Add(AsString(oR, "region"));
                vCatSet.Add(AsString(oR, "category"));
            }
            List<string> vRegions = new List<string>(vRegionSet);
            List<string> vCats = new List<string>(vCatSet);
            double[][] vMatrix = new double[vCats.Count][];
            for (int vC = 0; vC < vCats.Count; vC++)
                vMatrix[vC] = new double[vRegions.Count];
            foreach (DataRow oR in vTable.Rows)
            {
                int vR = vRegions.FindIndex(x => string.Equals(x, AsString(oR, "region"),
                    StringComparison.OrdinalIgnoreCase));
                int vC = vCats.FindIndex(x => string.Equals(x, AsString(oR, "category"),
                    StringComparison.OrdinalIgnoreCase));
                vMatrix[vC][vR] = AsFloat(oR, "v1");
            }
            oChart = NewChart("anStack", TsgcHTMLChartType.ctStackedBar100);
            for (int vR = 0; vR < vRegions.Count; vR++)
                oChart.AddLabel(vRegions[vR]);
            for (int vC = 0; vC < vCats.Count; vC++)
                NewDataset(oChart, vCats[vC], vMatrix[vC], ColorAt(vC));
            oChart.TooltipFormat = "{v}%";
            AddCard(TsgcHTMLColWidth.cw6, "an.stack100", "ctStackedBar100, TooltipFormat",
                oChart.HTML);

            // 5. funnel: order status flow, last 12 months
            vVals = new double[4];
            using (TReportsQuery oQ = FDB.NewQuery("SELECT COUNT(*) AS a, " +
                "SUM(CASE WHEN status <> 'Cancelled' THEN 1 ELSE 0 END) AS b, " +
                "SUM(CASE WHEN status IN ('Shipped', 'Delivered') THEN 1 " +
                "ELSE 0 END) AS c, " +
                "SUM(CASE WHEN status = 'Delivered' THEN 1 ELSE 0 END) AS d " +
                "FROM orders WHERE order_date >= :d", true))
            {
                oQ.SetParamStr("d", DaysAgo(365));
                oQ.Open();
                if (oQ.DataSet.Rows.Count > 0)
                {
                    DataRow oR = oQ.DataSet.Rows[0];
                    vVals[0] = AsFloat(oR, "a");
                    vVals[1] = AsFloat(oR, "b");
                    vVals[2] = AsFloat(oR, "c");
                    vVals[3] = AsFloat(oR, "d");
                }
            }
            oChart = NewChart("anFunnel", TsgcHTMLChartType.ctFunnel);
            oChart.AddLabel(Tr("an.placed"));
            oChart.AddLabel(Tr("an.notcancelled"));
            oChart.AddLabel(Tr("an.shipped"));
            oChart.AddLabel(Tr("an.delivered"));
            oDS = NewDataset(oChart, Tr("lbl.orders"), vVals, ColorAt(0));
            oDS.PointColors = "#0d6efd,#3d8bfd,#6ea8fe,#9ec5fe";
            oChart.ShowDataLabels = true;
            oChart.TooltipFormat = "{v}";
            AddCard(TsgcHTMLColWidth.cw6, "an.funnel", "ctFunnel (SVG, no script)",
                oChart.HTML);

            // 6. waterfall: gross sales -> discounts -> product cost -> margin
            vVals = new double[3];
            using (TReportsQuery oQ = FDB.NewQuery(
                "SELECT ROUND(SUM(ol.qty * ol.unit_price), 0) AS g, " +
                "ROUND(SUM(ol.qty * ol.unit_price * ol.discount), 0) AS dsc, " +
                "ROUND(SUM(ol.qty * p.unit_cost), 0) AS cost" + CS_SQL_FROM +
                " WHERE o.status <> 'Cancelled' AND o.order_date >= :d", true))
            {
                oQ.SetParamStr("d", DaysAgo(365));
                oQ.Open();
                if (oQ.DataSet.Rows.Count > 0)
                {
                    DataRow oR = oQ.DataSet.Rows[0];
                    vVals[0] = AsFloat(oR, "g");
                    vVals[1] = -AsFloat(oR, "dsc");
                    vVals[2] = -AsFloat(oR, "cost");
                }
            }
            oChart = NewChart("anWaterfall", TsgcHTMLChartType.ctWaterfall);
            oChart.AddLabel(Tr("an.gross"));
            oChart.AddLabel(Tr("an.discounts"));
            oChart.AddLabel(Tr("an.cost"));
            NewDataset(oChart, Tr("lbl.revenue"), vVals, "#198754");
            oChart.WaterfallTotalText = Tr("lbl.margin");
            oChart.ShowDataLabels = true;
            AddCard(TsgcHTMLColWidth.cw6, "an.waterfall", "ctWaterfall, WaterfallTotalText",
                oChart.HTML);

            // 7. pareto: revenue by customer
            vS = LoadSeries(FDB, "SELECT c.name AS lbl, " +
                "ROUND(SUM(ol.line_total), 0) AS v1" + CS_SQL_FROM +
                " WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY c.id ORDER BY v1 DESC LIMIT 12", DaysAgo(365));
            oChart = NewChart("anPareto", TsgcHTMLChartType.ctPareto);
            FillLabels(oChart, vS.Labels);
            NewDataset(oChart, Tr("lbl.revenue"), vS.V1, ColorAt(0));
            oChart.ShowExportPNG = true;
            oChart.ExportFileName = "pareto";
            AddCard(TsgcHTMLColWidth.cw6, "an.pareto",
                "ctPareto (sorted bars + cumulative % line)", oChart.HTML);

            // 8 + 9. bullet and radial bar: region revenue against the summed
            // monthly targets of the salespeople of the region.
            vS = LoadSeries(FDB, "SELECT r.name AS lbl, " +
                "ROUND(SUM(ol.line_total), 0) AS v1" + CS_SQL_FROM +
                " WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY r.name ORDER BY r.name", DaysAgo(365));
            TSeries vT = LoadSeries(FDB, "SELECT r.name AS lbl, " +
                "SUM(s.target_monthly) * 12 AS v1 FROM salespeople s " +
                "JOIN regions r ON r.id = s.region_id GROUP BY r.name ORDER BY r.name",
                "");
            vVals = new double[vS.V1.Count];
            for (int vI = 0; vI < vS.V1.Count; vI++)
            {
                vVals[vI] = 0;
                for (int vR = 0; vR < vT.Labels.Count; vR++)
                    if (vT.Labels[vR] == vS.Labels[vI] && vT.V1[vR] > 0)
                        vVals[vI] = RoundTo1(vS.V1[vI] * 100 / vT.V1[vR]);
            }
            oChart = NewChart("anBullet", TsgcHTMLChartType.ctBullet);
            FillLabels(oChart, vS.Labels);
            NewDataset(oChart, Tr("an.pcttarget"), vVals, ColorAt(0));
            double[] vTargets = new double[vVals.Length];
            for (int vI = 0; vI < vVals.Length; vI++)
                vTargets[vI] = 100;
            NewDataset(oChart, Tr("an.target"), vTargets, "#212529");
            oChart.BulletRanges = "60,90,120";
            oChart.ShowDataLabels = true;
            oChart.DataLabelFormat = "{v}%";
            AddCard(TsgcHTMLColWidth.cw6, "an.bullet", "ctBullet, BulletRanges", oChart.HTML);

            vSum = 0;
            for (int vI = 0; vI < vS.V1.Count; vI++)
                vSum = vSum + vS.V1[vI];
            vVals = new double[vS.V1.Count];
            for (int vI = 0; vI < vS.V1.Count; vI++)
                if (vSum > 0)
                    vVals[vI] = RoundTo1(vS.V1[vI] * 100 / vSum);
                else
                    vVals[vI] = 0;
            oChart = NewChart("anRadial", TsgcHTMLChartType.ctRadialBar);
            FillLabels(oChart, vS.Labels);
            oDS = NewDataset(oChart, Tr("lbl.revenue"), vVals, ColorAt(0));
            oDS.PointColors = CS_COLORS;
            oChart.RadialMax = "100";
            oChart.ShowDataLabels = true;
            oChart.DataLabelFormat = "{v}%";
            AddCard(TsgcHTMLColWidth.cw6, "an.radial", "ctRadialBar, RadialMax", oChart.HTML);

            // 10. click drill-down: revenue by region, the bar posts to ClickURL
            oChart = NewChart("anClick", TsgcHTMLChartType.ctBar);
            oChart.ShowLegend = false;
            FillLabels(oChart, vS.Labels);
            oDS = NewDataset(oChart, Tr("lbl.revenue"), vS.V1, ColorAt(0));
            oDS.PointColors = CS_COLORS;
            oChart.ShowDataLabels = true;
            oChart.ClickURL = "/analytics/charts/region";
            oChart.ClickTarget = "#anRegionDetail";
            AddCard(TsgcHTMLColWidth.cw6, "an.click", Tr("an.clickhint"), oChart.HTML +
                EmptyDiv("anRegionDetail", "mt-3"));

            // 11. live: daily revenue of the last 14 days, then a point every 2 s
            vS = LoadSeries(FDB, "SELECT o.order_date AS lbl, " +
                "ROUND(SUM(ol.line_total), 0) AS v1" + CS_SQL_FROM +
                " WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY o.order_date ORDER BY o.order_date", DaysAgo(14));
            vSum = 0;
            for (int vI = 0; vI < vS.V1.Count; vI++)
                vSum = vSum + vS.V1[vI];
            if (vS.V1.Count > 0)
                lock (GLock)
                    GLiveBase = vSum / vS.V1.Count;
            oChart = NewChart("anLive", TsgcHTMLChartType.ctLine);
            oChart.LiveMaxPoints = 30;
            FillLabels(oChart, vS.Labels);
            NewDataset(oChart, Tr("lbl.revenue"), vS.V1, ColorAt(3));
            AddCard(TsgcHTMLColWidth.cw6, "an.live", Tr("an.livehint"), oChart.HTML);

            oRoot.Add(oRow);
            // realtime: the page opens the sgcHTMX bridge for the pushed points
            return FPages.BuildPageShell(aCtx, Tr("nav.charts"), Tr("sub.charts"),
                oRoot.HTML, "an.charts", true);
        }

        public string BuildRegionDetail(TReportsPageCtx aCtx, string aRegion)
        {
            FLang = aCtx.Lang;
            using (TReportsQuery oQ = FDB.NewQuery("SELECT c.name AS customer, " +
                "c.segment AS segment, COUNT(DISTINCT o.id) AS orders, " +
                "ROUND(SUM(ol.line_total), 2) AS revenue" +
                CS_SQL_FROM + " WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "AND r.name = :r GROUP BY c.id ORDER BY revenue DESC LIMIT 10", true))
            {
                oQ.SetParamStr("d", DaysAgo(365));
                oQ.SetParamStr("r", aRegion);
                oQ.Open();
                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "anRegionGrid";
                oGrid.LoadFromDataSet(oQ.DataSet);
                return NoteHTML(Tr("an.detail") + ": " + aRegion) + oGrid.HTML;
            }
        }

        public string LiveChartFragment()
        {
            double vBase;
            lock (GLock)
                vBase = GLiveBase;
            if (vBase <= 0)
                vBase = 1000;
            TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "anLive";
            oChart.LiveMaxPoints = 30;
            return oChart.GetLiveAppendScript(DateTime.Now.ToString("HH:mm:ss",
                CultureInfo.InvariantCulture),
                Math.Truncate(vBase * (0.6 + NextRandom() * 0.8)));
        }

        // ----- candlesticks ----- //

        // caller holds GLock
        private static void EnsureCandles(TReportsDBPool aDB)
        {
            if (GCandleLabels.Count > 0)
                return;
            using (TReportsQuery oQ = aDB.NewQuery("WITH daily AS (SELECT o.order_date AS d, " +
                "strftime('%Y-%W', o.order_date) AS w, " +
                "AVG(ol.unit_price * (1 - ol.discount)) AS p, COUNT(*) AS n " +
                "FROM order_lines ol JOIN orders o ON o.id = ol.order_id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY o.order_date) " +
                "SELECT MIN(d) AS lbl, ROUND(MAX(p), 2) AS hi, ROUND(MIN(p), 2) AS lo, " +
                "SUM(n) AS vol, " +
                "ROUND((SELECT x.p FROM daily x WHERE x.w = daily.w " +
                "ORDER BY x.d ASC LIMIT 1), 2) AS op, " +
                "ROUND((SELECT y.p FROM daily y WHERE y.w = daily.w " +
                "ORDER BY y.d DESC LIMIT 1), 2) AS cl " +
                "FROM daily GROUP BY w ORDER BY w", true))
            {
                oQ.SetParamStr("d", DaysAgo(420));
                oQ.Open();
                foreach (DataRow oR in oQ.DataSet.Rows)
                {
                    GCandleLabels.Add(AsString(oR, "lbl"));
                    GO.Add(AsFloat(oR, "op"));
                    GH.Add(AsFloat(oR, "hi"));
                    GL.Add(AsFloat(oR, "lo"));
                    GC.Add(AsFloat(oR, "cl"));
                    GV.Add(AsFloat(oR, "vol"));
                }
            }
        }

        // caller holds GLock
        private static TsgcHTMLComponent_CandlestickChart NewCandleChart(string aStyle,
            int aCount)
        {
            TsgcHTMLComponent_CandlestickChart vResult =
                new TsgcHTMLComponent_CandlestickChart();
            // one id per style, so a pushed tick only replaces the matching chart
            vResult.ChartID = StyleKey(aStyle) == "ohlc" ? "anOhlc" : "anCandle";
            vResult.Width = 900;
            vResult.Height = 380;
            vResult.ShowVolume = true;
            vResult.ShowCrosshair = true;
            vResult.LiveMaxPoints = CS_CANDLE_MAX;
            vResult.UpColor = "#10B981";
            vResult.DownColor = "#EF4444";
            if (StyleKey(aStyle) == "ohlc")
                vResult.CandleStyle = TsgcHTMLCandlestickStyle.cssOHLC;
            else
                vResult.CandleStyle = TsgcHTMLCandlestickStyle.cssCandle;
            TsgcHTMLCandlestickIndicator oInd = vResult.Indicators.Add();
            oInd.Kind = TsgcHTMLCandlestickIndicatorKind.cikSMA;
            oInd.Period = 10;
            oInd = vResult.Indicators.Add();
            oInd.Kind = TsgcHTMLCandlestickIndicatorKind.cikEMA;
            oInd.Period = 20;
            oInd = vResult.Indicators.Add();
            oInd.Kind = TsgcHTMLCandlestickIndicatorKind.cikBollinger;
            oInd.Period = 20;
            oInd.StdDevs = 2;
            for (int vI = 0; vI < aCount; vI++)
                vResult.AddPoint(GCandleLabels[vI], GO[vI], GH[vI], GL[vI], GC[vI],
                    GV[vI]);
            return vResult;
        }

        public string BuildMarket(TReportsPageCtx aCtx, string aStyle)
        {
            FLang = aCtx.Lang;
            string vStyle = StyleKey(aStyle);
            TsgcHTMLComponent_CandlestickChart oChart;
            lock (GLock)
            {
                EnsureCandles(FDB);
                oChart = NewCandleChart(vStyle, GCandleLabels.Count);
            }
            TsgcHTMLContainer oScroll = new TsgcHTMLContainer("div");
            oScroll.CSSClass = "rp-svg-scroll";
            oScroll.AddRaw(oChart.HTML);
            string vBody = ToggleHTML(Tr("an.style"),
                new[] { "/analytics/market?style=candle", "/analytics/market?style=ohlc" },
                new[] { Tr("an.candle"), Tr("an.ohlc") },
                new[] { "candle", "ohlc" }, vStyle) + oScroll.HTML;
            return FPages.BuildPageShell(aCtx, Tr("nav.market"), Tr("sub.market"),
                CardHTML(Tr("an.market"), "CandleStyle, Indicators (SMA 10, EMA 20, " +
                "Bollinger 20,2), ShowCrosshair, GetLiveTickFragment", vBody),
                "an.market", true);
        }

        // One tick for every client: the fragment carries the candle chart and the
        // OHLC chart (different ids), each page swaps the one it shows.
        public string LiveCandleFragment()
        {
            lock (GLock)
            {
                EnsureCandles(FDB);
                int vN = GCandleLabels.Count;
                if (vN == 0)
                    return "";
                GTick++;
                // every 5th tick opens a new candle, the others move the last one
                if (GTick % 5 == 0)
                {
                    double vOpen = GC[vN - 1];
                    GCandleLabels.Add(DateTime.Now.ToString("HH:mm:ss",
                        CultureInfo.InvariantCulture));
                    GO.Add(vOpen);
                    GH.Add(vOpen);
                    GL.Add(vOpen);
                    GC.Add(vOpen);
                    GV.Add(0);
                    vN++;
                }
                int vI = vN - 1;
                double vClose = Math.Round(GC[vI] * (1 + (NextRandom() - 0.5) * 0.03), 2);
                GC[vI] = vClose;
                GH[vI] = Math.Max(GH[vI], vClose);
                GL[vI] = Math.Min(GL[vI], vClose);
                GV[vI] = GV[vI] + 1 + NextRandom(25);
                // keep the server copy as short as the chart keeps it
                if (vN > CS_CANDLE_MAX)
                {
                    int vDrop = vN - CS_CANDLE_MAX;
                    GCandleLabels.RemoveRange(0, vDrop);
                    GO.RemoveRange(0, vDrop);
                    GH.RemoveRange(0, vDrop);
                    GL.RemoveRange(0, vDrop);
                    GC.RemoveRange(0, vDrop);
                    GV.RemoveRange(0, vDrop);
                    vN = CS_CANDLE_MAX;
                    vI = vN - 1;
                }
                string vResult = "";
                foreach (string vStyle in new[] { "candle", "ohlc" })
                {
                    TsgcHTMLComponent_CandlestickChart oChart = NewCandleChart(vStyle, vN - 1);
                    vResult += oChart.GetLiveTickFragment(GCandleLabels[vI], GO[vI],
                        GH[vI], GL[vI], GC[vI], GV[vI], false);
                }
                return vResult;
            }
        }

        // ----- pivot lab ----- //

        private static string PivotSQL()
        {
            return "SELECT o.order_date AS order_date, r.name AS region, " +
                "cat.name AS category, c.segment AS segment, s.name AS salesperson, " +
                "c.name AS customer, o.status AS status, ol.qty AS qty, " +
                "ROUND(ol.line_total, 2) AS revenue, " +
                "ROUND(ol.line_total - ol.qty * p.unit_cost, 2) AS margin" +
                CS_SQL_FROM + " WHERE o.order_date >= :d";
        }

        private DataTable OpenPivotQuery()
        {
            using (TReportsQuery oQ = FDB.NewQuery(PivotSQL(), true))
            {
                oQ.SetParamStr("d", DaysAgo(3 * 365));
                oQ.Open();
                return oQ.DataSet;
            }
        }

        private TsgcHTMLComponent_PivotTable MainPivot(string aGroup, string aRows,
            string aCols, string aValues)
        {
            TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "anPivotChart";
            oChart.ChartType = TsgcHTMLChartType.ctBar;
            oChart.CSSHeight = "280px";
            oChart.Theme = TsgcHTMLChartTheme.chtAuto;

            TsgcHTMLComponent_PivotTable vResult = new TsgcHTMLComponent_PivotTable();
            vResult.TableID = "anPivot";
            vResult.Compact = true;
            vResult.EmptyCellText = "-";
            vResult.Caption = Tr("an.pivotmain");

            // two row fields, expandable, top 3 categories per region + Others
            vResult.Expandable = true;
            TsgcHTMLPivotField oField = vResult.RowFields.Add();
            oField.FieldName = "region";
            oField.Caption = Tr("lbl.region");
            oField = vResult.RowFields.Add();
            oField.FieldName = "category";
            oField.Caption = Tr("lbl.category");
            oField.SortBy = TsgcHTMLPivotSortBy.psbValueDesc;
            oField.TopN = 3;
            oField.TopNOthers = true;

            // order dates grouped by year, quarter or month
            oField = vResult.ColumnFields.Add();
            oField.FieldName = "order_date";
            oField.Caption = Tr("an." + GroupKey(aGroup));
            if (GroupKey(aGroup) == "quarter")
                oField.DateGrouping = TsgcHTMLPivotDateGrouping.pdgQuarter;
            else if (GroupKey(aGroup) == "month")
                oField.DateGrouping = TsgcHTMLPivotDateGrouping.pdgMonth;
            else
                oField.DateGrouping = TsgcHTMLPivotDateGrouping.pdgYear;

            // filter-only field: cancelled orders never reach the pivot
            oField = vResult.FilterFields.Add();
            oField.FieldName = "status";
            oField.FilterValues = "Cancelled";
            oField.FilterExclude = true;

            TsgcHTMLPivotMeasure oMeasure = vResult.Measures.Add();
            oMeasure.SourceField = "revenue";
            oMeasure.Caption = Tr("lbl.revenue");
            oMeasure.Aggregation = TsgcHTMLPivotAggregation.paSum;
            oMeasure.Format = "#,##0";
            oMeasure = vResult.Measures.Add();
            oMeasure.SourceField = "revenue";
            oMeasure.Caption = "% " + Tr("an.ofcol");
            oMeasure.Aggregation = TsgcHTMLPivotAggregation.paSum;
            oMeasure.ShowAs = TsgcHTMLPivotShowAs.psaPercentOfColumn;
            oMeasure.Format = "0.0";

            vResult.Heatmap = true;
            vResult.HeatmapMeasure = 0;

            string vQS = LayoutQS(aGroup, aRows, aCols, aValues);
            vResult.DrillThroughURL = "/analytics/pivot/drill" + vQS;
            vResult.ShowExport = true;
            vResult.ExportFileName = "revenue-pivot";
            vResult.ExportURL = "/analytics/pivot/export.xlsx" + vQS;
            vResult.ShowFieldChooser = true;
            vResult.FieldChooserURL = "/analytics/pivot/layout?grp=" + GroupKey(aGroup);
            vResult.LinkedChart = oChart;
            vResult.LinkedChartMeasure = 0;

            DataTable vTable = OpenPivotQuery();
            vResult.DataSource = vTable;
            if (aRows != "" || aCols != "" || aValues != "")
                vResult.ApplyLayout(aRows, aCols, aValues);
            else if (vResult.Data.Count == 0)
                vResult.LoadFromDataSet(vTable);
            return vResult;
        }

        private TsgcHTMLComponent_PivotTable StatsPivot()
        {
            TsgcHTMLComponent_PivotTable vResult = new TsgcHTMLComponent_PivotTable();

            void AddMeasure(string aField, string aCaption,
                TsgcHTMLPivotAggregation aAgg, TsgcHTMLPivotShowAs aShowAs,
                string aFormat)
            {
                TsgcHTMLPivotMeasure oM = vResult.Measures.Add();
                oM.SourceField = aField;
                oM.Caption = aCaption;
                oM.Aggregation = aAgg;
                oM.ShowAs = aShowAs;
                oM.Format = aFormat;
            }

            vResult.TableID = "anStats";
            vResult.Compact = true;
            vResult.EmptyCellText = "-";
            vResult.Caption = Tr("an.pivotstats");
            TsgcHTMLPivotField oField = vResult.RowFields.Add();
            oField.FieldName = "segment";
            oField.Caption = Tr("lbl.segment");
            oField = vResult.ColumnFields.Add();
            oField.FieldName = "order_date";
            oField.Caption = Tr("an.year");
            oField.DateGrouping = TsgcHTMLPivotDateGrouping.pdgYear;
            oField = vResult.FilterFields.Add();
            oField.FieldName = "status";
            oField.FilterValues = "Cancelled";
            oField.FilterExclude = true;
            AddMeasure("customer", Tr("lbl.customers"),
                TsgcHTMLPivotAggregation.paDistinctCount, TsgcHTMLPivotShowAs.psaValue, "");
            AddMeasure("revenue", Tr("an.median"), TsgcHTMLPivotAggregation.paMedian,
                TsgcHTMLPivotShowAs.psaValue, "#,##0.00");
            AddMeasure("revenue", Tr("an.stddev"), TsgcHTMLPivotAggregation.paStdDev,
                TsgcHTMLPivotShowAs.psaValue, "#,##0.00");
            AddMeasure("revenue", "% " + Tr("an.ofrow"), TsgcHTMLPivotAggregation.paSum,
                TsgcHTMLPivotShowAs.psaPercentOfRow, "0.0");
            AddMeasure("revenue", "% " + Tr("an.oftotal"), TsgcHTMLPivotAggregation.paSum,
                TsgcHTMLPivotShowAs.psaPercentOfGrandTotal, "0.0");
            AddMeasure("revenue", Tr("an.running"), TsgcHTMLPivotAggregation.paSum,
                TsgcHTMLPivotShowAs.psaRunningTotal, "#,##0");
            vResult.LoadFromDataSet(OpenPivotQuery());
            return vResult;
        }

        public string BuildPivotLab(TReportsPageCtx aCtx, string aGroup)
        {
            FLang = aCtx.Lang;
            string vG = GroupKey(aGroup);
            TsgcHTMLContainer oBox = new TsgcHTMLContainer("div");
            oBox.CSSClass = "table-responsive";
            oBox.AddRaw(MainPivot(vG, "", "", "").HTML);
            string vBody = ToggleHTML(Tr("an.group"),
                new[] { "/analytics/pivot?grp=year", "/analytics/pivot?grp=quarter",
                    "/analytics/pivot?grp=month" },
                new[] { Tr("an.year"), Tr("an.quarter"), Tr("an.month") },
                new[] { "year", "quarter", "month" }, vG) + oBox.HTML;
            vBody = CardHTML(Tr("an.pivotmain"), Tr("an.pivothint"), vBody) +
                CardHTML(Tr("an.pivotstats"), "paDistinctCount, paMedian, paStdDev, " +
                "psaPercentOfRow, psaPercentOfGrandTotal, psaRunningTotal",
                StatsPivot().HTML);
            return FPages.BuildPageShell(aCtx, Tr("nav.pivotlab"),
                Tr("sub.pivotlab"), vBody, "an.pivot");
        }

        public string PivotLayoutFragment(TReportsPageCtx aCtx, string aGroup,
            string aRows, string aCols, string aValues)
        {
            FLang = aCtx.Lang;
            return MainPivot(aGroup, aRows, aCols, aValues).HTML;
        }

        private static void ParsePath(string aJSON, List<string> aList)
        {
            if (string.IsNullOrEmpty(aJSON))
                return;
            try
            {
                using (JsonDocument oDoc = JsonDocument.Parse(aJSON))
                {
                    if (oDoc.RootElement.ValueKind != JsonValueKind.Object)
                        return;
                    foreach (JsonProperty oPair in oDoc.RootElement.EnumerateObject())
                    {
                        string vValue = oPair.Value.ValueKind == JsonValueKind.String
                            ? (oPair.Value.GetString() ?? "")
                            : oPair.Value.GetRawText();
                        aList.Add(oPair.Name + "=" + vValue);
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        public string PivotDrillFragment(TReportsPageCtx aCtx, string aGroup,
            string aRows, string aCols, string aValues, string aRowPath, string aColPath)
        {
            FLang = aCtx.Lang;
            List<string> oRowPath = new List<string>();
            List<string> oColPath = new List<string>();
            List<string> oRows = new List<string>();
            ParsePath(aRowPath, oRowPath);
            ParsePath(aColPath, oColPath);
            TsgcHTMLComponent_PivotTable oPivot = MainPivot(aGroup, aRows, aCols, aValues);
            oPivot.GetDrillThroughRows(oRowPath, oColPath, oRows);
            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "anDrillGrid";
            for (int vI = 0; vI < oPivot.DataFields.Count; vI++)
            {
                TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
                oCol.Name = oPivot.DataFields[vI];
                oCol.Title = oPivot.DataFields[vI];
            }
            string[] vValues = new string[oPivot.DataFields.Count];
            for (int vI = 0; vI < Math.Min(oRows.Count, 200); vI++)
            {
                string[] vCells = oRows[vI].Split('\t');
                for (int vJ = 0; vJ < vValues.Length; vJ++)
                    vValues[vJ] = vJ < vCells.Length ? vCells[vJ] : "";
                oGrid.AddRow(vValues);
            }
            return NoteHTML(oRows.Count.ToString(CultureInfo.InvariantCulture) + " " +
                Tr("lbl.rows") + " (max. 200)") + oGrid.HTML;
        }

        public void PivotXLSX(string aGroup, string aRows, string aCols, string aValues,
            Stream aStream)
        {
            MainPivot(aGroup, aRows, aCols, aValues).SaveToXLSXStream(aStream);
        }
    }
}
