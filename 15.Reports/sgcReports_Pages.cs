// ***************************************************************************
//  sgcReports - reporting and BI portal web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\15.Reports\sgcReports_Pages.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
//
//  The view layer. Every page is assembled from TsgcHTMLComponent_* classes;
//  the node layer (sgcHTML_Nodes*) is used only as glue - rows, columns, cards
//  and spacing around those components. There are no hand-written HTML pages:
//  the only things handed to AddRaw are the rendered HTML of an sgcHTML
//  component or node, a value already escaped through HtmlEsc, or a trusted
//  entity constant.
//
//  The live components of /jobs (JobProgress + LogViewer) must survive between
//  requests so the scheduled-export thread can mutate them and emit htmx
//  out-of-band fragments. They live in a locked singleton owned by this file
//  (LiveInit / LiveDone), and the server only ever exchanges plain strings with
//  it, exactly like the 08.Components demo.
//
//  The Delphi TDataSet handed to LoadFromDataSet is the managed
//  System.Data.DataTable (the migration's established mapping).
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
// sgc
using esegece.sgcWebSockets;

namespace Reports
{
    /// <summary>
    /// Everything every page needs to know about who is asking. Mirrors Delphi
    /// TReportsPageCtx.
    /// </summary>
    public class TReportsPageCtx
    {
        public long UserId;
        public string DisplayName = "";
        public string Role = "";
        public string Theme = "";
        public string Lang = "";
    }

    /// <summary>
    /// Owns the components the scheduled-export thread mutates. Guarded by a lock
    /// because the push thread and the HTTP threads both touch it. The server
    /// never sees these objects, only the strings they render.
    /// </summary>
    internal class TReportsLive
    {
        private readonly object FLock = new object();
        private readonly TsgcHTMLComponent_JobProgress FJobs;
        private readonly TsgcHTMLComponent_LogViewer FLog;
        private readonly TsgcHTMLComponent_ActivityFeed FFeed;
        private int FTick;

        public TReportsLive()
        {
            FTick = 0;

            FJobs = new TsgcHTMLComponent_JobProgress();
            FJobs.JobsID = TReportsPages.CS_JOBS_ID;
            FJobs.Title = "Export jobs";
            FJobs.EmptyText = "No export running. Start one from Schedules.";
            FJobs.ShowCancelButton = true;
            FJobs.ShowCompleted = true;
            FJobs.AutoRemoveCompleted = false;

            FLog = new TsgcHTMLComponent_LogViewer();
            FLog.LogID = TReportsPages.CS_LOG_ID;
            FLog.Title = "Export log";
            FLog.CSSHeight = "340px";
            FLog.Theme = TsgcHTMLLogTheme.ltDark;
            FLog.MaxLines = 200;
            FLog.AddLine(TsgcHTMLLogLevel.llInfo, "Report engine ready", "engine");
            FLog.AddLine(TsgcHTMLLogLevel.llDebug,
                "Read-only connection pool registered", "db");

            FFeed = new TsgcHTMLComponent_ActivityFeed();
            FFeed.FeedID = TReportsPages.CS_FEED_ID;
            FFeed.Title = "Recent activity";
            FFeed.MaxItems = 20;
            FFeed.EmptyText = "Nothing has run yet.";
        }

        private TsgcHTMLJobItem FindJob(string aId)
        {
            for (int vI = 0; vI < FJobs.Jobs.Count; vI++)
                if (FJobs.Jobs[vI].Id == aId)
                    return FJobs.Jobs[vI];
            return null;
        }

        private int RunningCount()
        {
            int vResult = 0;
            for (int vI = 0; vI < FJobs.Jobs.Count; vI++)
                if (FJobs.Jobs[vI].Status == TsgcHTMLJobStatus.jsRunning)
                    vResult++;
            return vResult;
        }

        public bool HasWork()
        {
            lock (FLock)
            {
                return RunningCount() > 0;
            }
        }

        public string RenderPanels()
        {
            lock (FLock)
            {
                TsgcHTMLRow oRow = new TsgcHTMLRow();
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(TReportsPages.SectionCard(
                    "Running exports",
                    "Pushed from the server over the WebSocket. No page reload, no " +
                    "polling: the push thread renders one job row and sends it as an " +
                    "htmx out-of-band swap.", FJobs.HTML));
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(TReportsPages.SectionCard(
                    "Activity", "Each finished export prepends one item.", FFeed.HTML));
                string vResult = oRow.HTML;
                vResult = vResult + TReportsPages.SectionCard("Export log",
                    "Every line below arrived as a pushed fragment while you were " +
                    "looking at this page.", FLog.HTML);
                return vResult;
            }
        }

        public string StartJob(string aJobId, string aName, string aDescription)
        {
            if (string.IsNullOrEmpty((aJobId ?? "").Trim()))
                return "";
            lock (FLock)
            {
                string vResult;
                TsgcHTMLJobItem oJob = FindJob(aJobId);
                if (oJob == null)
                {
                    oJob = FJobs.AddJob(aJobId, aName, aDescription);
                    oJob.Percent = 0;
                    oJob.Status = TsgcHTMLJobStatus.jsRunning;
                    oJob.StatusText = "Queued";
                    oJob.StartedAt = DateTime.Now;
                    vResult = FJobs.GetJobAppendFragmentHTML(aJobId);
                }
                else
                {
                    FJobs.UpdateJob(aJobId, 0, TsgcHTMLJobStatus.jsRunning, "Restarted");
                    vResult = FJobs.GetJobFragmentHTML(aJobId);
                }
                FLog.AddLine(TsgcHTMLLogLevel.llInfo,
                    "Export \"" + aName + "\" queued", "scheduler");
                return vResult + FLog.GetLastLineFragmentHTML();
            }
        }

        // One step of every running job. The percentages are the demo's own
        // simulation; the mechanism - render one row, push it out of band - is what
        // a real export loop would do from inside its own worker.
        public string Tick()
        {
            string vOut = "";
            lock (FLock)
            {
                FTick++;
                for (int vI = 0; vI < FJobs.Jobs.Count; vI++)
                {
                    TsgcHTMLJobItem oJob = FJobs.Jobs[vI];
                    if (oJob.Status != TsgcHTMLJobStatus.jsRunning)
                        continue;
                    int vStep = 7 + ((FTick + vI * 3) % 14);
                    if (oJob.Percent + vStep >= 100)
                    {
                        FJobs.UpdateJob(oJob.Id, 100, TsgcHTMLJobStatus.jsCompleted,
                            "Completed");
                        vOut = vOut + FJobs.GetJobFragmentHTML(oJob.Id);
                        FLog.AddLine(TsgcHTMLLogLevel.llInfo, "Export \"" + oJob.Name +
                            "\" finished and was written to disk", "export");
                        vOut = vOut + FLog.GetLastLineFragmentHTML();
                        FFeed.AddActivity("scheduler", "completed", oJob.Name,
                            TsgcHTMLColor.hcSuccess);
                        vOut = vOut + FFeed.GetLastItemFragmentHTML();
                    }
                    else
                    {
                        FJobs.UpdateJob(oJob.Id, oJob.Percent + vStep,
                            TsgcHTMLJobStatus.jsRunning, "Writing rows... " +
                            (oJob.Percent + vStep).ToString(CultureInfo.InvariantCulture) +
                            "%");
                        vOut = vOut + FJobs.GetJobFragmentHTML(oJob.Id);
                        if ((FTick % 2) == 0)
                        {
                            FLog.AddLine(TsgcHTMLLogLevel.llDebug, oJob.Name + ": " +
                                (oJob.Percent * 25).ToString(CultureInfo.InvariantCulture) +
                                " rows written", "export");
                            vOut = vOut + FLog.GetLastLineFragmentHTML();
                        }
                    }
                }
                return vOut;
            }
        }

        public string CancelJob(string aJobId)
        {
            if (string.IsNullOrEmpty((aJobId ?? "").Trim()))
                return "";
            lock (FLock)
            {
                TsgcHTMLJobItem oJob = FindJob(aJobId);
                if (oJob == null)
                    return "";
                FJobs.UpdateJob(aJobId, oJob.Percent, TsgcHTMLJobStatus.jsCancelled,
                    "Cancelled by user");
                FLog.AddLine(TsgcHTMLLogLevel.llWarning,
                    "Export \"" + oJob.Name + "\" cancelled", "export");
                return FJobs.GetJobFragmentHTML(aJobId) +
                    FLog.GetLastLineFragmentHTML();
            }
        }
    }

    /// <summary>The whole view layer. Mirrors Delphi TReportsPages.</summary>
    public class TReportsPages
    {
        private const string CS_ACCENT = ReportsConst.CS_BRAND_ACCENT;

        // Fixed ids: the push fragments target the ids the components render with,
        // so both sides read them from here.
        internal const string CS_JOBS_ID = "sgcExportJobs";
        internal const string CS_LOG_ID = "sgcExportLog";
        internal const string CS_FEED_ID = "sgcExportFeed";
        internal const string CS_PALETTE_ID = "sgcCmdPalette";

        private const string CS_EXPLORE_WRAP = "sgcExploreGrid-vswrap";
        private const string CS_EXPLORE_TABLE = "sgcExploreGrid";

        // Placeholder written into a Grid cell whose real content is component
        // markup. The Grid escapes cell text, so the markup is spliced in after
        // rendering, in the order the placeholders were written.
        private const string CS_BADGE_SLOT = "@@badge@@";

        // Self-contained favicon (indigo square, white R).
        public const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"sgcReports\">" +
            "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"" + CS_ACCENT + "\"/>" +
            "<text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"900\" font-size=\"42\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">R</text></svg>";

        // Inline brand mark for the sign-in card. No external asset, no CDN.
        private const string CS_LOGO_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 260 64\" width=\"210\" height=\"52\" role=\"img\" " +
            "aria-label=\"sgcReports\">" + "<rect x=\"0\" y=\"4\" width=\"56\" height=\"56\" " +
            "rx=\"12\" fill=\"" + CS_ACCENT + "\"/>" +
            "<rect x=\"13\" y=\"34\" width=\"8\" height=\"16\" fill=\"#FFFFFF\"/>" +
            "<rect x=\"25\" y=\"24\" width=\"8\" height=\"26\" fill=\"#FFFFFF\"/>" +
            "<rect x=\"37\" y=\"16\" width=\"8\" height=\"34\" fill=\"#FFFFFF\"/>" +
            "<text x=\"70\" y=\"42\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"700\" font-size=\"28\" fill=\"#212529\">sgcReports</text></svg>";

        // Empty-state artwork (inline SVG; the component sanitises it anyway).
        private const string CS_EMPTY_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"72\" " +
            "height=\"72\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"" + CS_ACCENT +
            "\" stroke-width=\"1.5\" stroke-linecap=\"round\">" +
            "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/>" +
            "<path d=\"M7 15v-4M12 15V8M17 15v-6\"/></svg>";

        private const string CS_BASE_CSS = "body{background:#f4f5fa;}" +
            ".rp-sql{background:#0f172a;color:#e2e8f0;border-radius:.5rem;" +
            "padding:1rem;font-size:.8125rem;line-height:1.5;overflow:auto;" +
            "max-height:420px;white-space:pre;margin:0;}" +
            ".rp-code{background:#111827;color:#d1fae5;border-radius:.5rem;" +
            "padding:1rem;font-size:.8125rem;line-height:1.5;overflow:auto;" +
            "max-height:420px;white-space:pre;margin:0;}" +
            ".rp-metric{font-variant-numeric:tabular-nums;}" +
            ".rp-accent{color:" + CS_ACCENT + ";}" +
            ".rp-chip{background:" + CS_ACCENT + "1a;color:" + CS_ACCENT + ";" +
            "border-radius:999px;padding:.15rem .6rem;font-size:.75rem;" +
            "font-weight:600;display:inline-block;}" +
            ".rp-toolbar{gap:.5rem;flex-wrap:wrap;}" +
            ".table td,.table th{font-size:.875rem;}" +
            ".rp-svg-scroll{overflow-x:auto;}" +
            ".rp-svg-scroll svg{max-width:none;}" +
            // Below md the /sql splitter stacks: two 170px columns side by side on a
            // phone would make both panes useless, and the gutter has nothing to drag.
            "@media (max-width:767.98px){" +
            ".sgc-splitter{flex-direction:column !important;height:auto !important;}" +
            ".sgc-splitter .sgc-splitter-pane{flex:0 0 auto !important;" +
            "width:100% !important;max-height:60vh;}" +
            ".sgc-splitter .sgc-splitter-gutter{display:none !important;}}" +
            "@media (max-width:575.98px){.rp-sql,.rp-code{font-size:.75rem;}}";

        private const string CS_DARK_CSS = "body{background:#12141a;color:#e4e6eb;}" +
            ".card{background:#1e2129;border:1px solid #2c3039;color:#e4e6eb;}" +
            ".card-header{background:#252932;border-bottom:1px solid #2c3039;}" +
            ".table{color:#e4e6eb;}" + ".text-muted{color:#8b8fa3 !important;}" +
            ".form-control,.form-select{background:#1e2129;border-color:#2c3039;" +
            "color:#e4e6eb;}" + ".form-control:focus,.form-select:focus{" +
            "background:#252932;color:#e4e6eb;border-color:" + CS_ACCENT + ";}" +
            ".list-group-item{background:#1e2129;border-color:#2c3039;" +
            "color:#e4e6eb;}" + ".page-link{background:#1e2129;border-color:#2c3039;" +
            "color:#e4e6eb;}" + ".page-item.active .page-link{background:" + CS_ACCENT +
            ";border-color:" + CS_ACCENT + ";}" + "a{color:#a5b4fc;}" +
            ".dropdown-menu{background:#1e2129;border-color:#2c3039;}" +
            ".dropdown-item{color:#e4e6eb;}" +
            ".dropdown-item:hover,.dropdown-item:focus{background:#2c3039;" +
            "color:#fff;}" + ".navbar{background:#1a1d24 !important;}" +
            ".modal-content{background:#1e2129;color:#e4e6eb;}";

        private static TReportsLive gLive;
        private static readonly object gLiveLock = new object();

        private readonly TReportsDBPool FDB;

        public TReportsPages(TReportsDBPool aDB)
        {
            if (aDB == null)
                throw new EReportsError("TReportsPages: database pool is nil");
            FDB = aDB;
        }

        // ==================================================================== //
        //  small helpers                                                       //
        // ==================================================================== //

        /// <summary>Minimal HTML-escape for text spliced into raw markup.</summary>
        internal static string HtmlEsc(string aValue)
        {
            string vResult = (aValue ?? "").Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            return vResult;
        }

        private static string FmtMoney(double aValue)
        {
            return aValue.ToString("#,##0.00", CultureInfo.InvariantCulture);
        }

        private static string FmtMoney0(double aValue)
        {
            return aValue.ToString("#,##0", CultureInfo.InvariantCulture);
        }

        private static string FmtInt(long aValue)
        {
            return aValue.ToString("#,##0", CultureInfo.InvariantCulture);
        }

        private static string FmtPct(double aValue)
        {
            return aValue.ToString("#,##0.0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FmtDateTime(DateTime aValue)
        {
            if (aValue <= DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        // Break a one-line statement onto readable lines before the major clauses.
        // Purely cosmetic: the string that is displayed, never the one that is run.
        private static string PrettySQL(string aSQL)
        {
            string[] CS_BREAK = new string[]
            {
                " FROM ", " WHERE ", " GROUP BY ", " ORDER BY ", " HAVING ", " LIMIT ",
                " OFFSET ", " JOIN ", " LEFT JOIN ", " INNER JOIN ", " UNION ", " AND "
            };
            string vResult = (aSQL ?? "").Trim();
            vResult = vResult.Replace("\r\n", " ");
            vResult = vResult.Replace("\n", " ");
            while (vResult.IndexOf("  ", StringComparison.Ordinal) >= 0)
                vResult = vResult.Replace("  ", " ");
            for (int vI = 0; vI < CS_BREAK.Length; vI++)
                vResult = ReplaceIgnoreCase(vResult, CS_BREAK[vI],
                    "\r\n" + CS_BREAK[vI].Trim() + " ");
            // LEFT / INNER JOIN would otherwise be split twice; re-join the pair.
            vResult = vResult.Replace("LEFT\r\nJOIN ", "LEFT JOIN ");
            vResult = vResult.Replace("INNER\r\nJOIN ", "INNER JOIN ");
            return vResult;
        }

        private static string ReplaceIgnoreCase(string aText, string aFrom, string aTo)
        {
            if (string.IsNullOrEmpty(aFrom))
                return aText;
            StringBuilder vBuf = new StringBuilder(aText.Length);
            int vPos = 0;
            while (vPos < aText.Length)
            {
                int vFound = aText.IndexOf(aFrom, vPos,
                    StringComparison.OrdinalIgnoreCase);
                if (vFound < 0)
                {
                    vBuf.Append(aText, vPos, aText.Length - vPos);
                    break;
                }
                vBuf.Append(aText, vPos, vFound - vPos);
                vBuf.Append(aTo);
                vPos = vFound + aFrom.Length;
            }
            return vBuf.ToString();
        }

        // A <pre> block. The text goes through AddText, which escapes, so a value
        // out of the database can never become markup.
        private static string PreBlock(string aText, string aCSSClass)
        {
            TsgcHTMLContainer oPre = new TsgcHTMLContainer("pre");
            oPre.CSSClass = aCSSClass;
            oPre.AddText(aText);
            return oPre.HTML;
        }

        private static string SQLBlock(string aSQL)
        {
            return PreBlock(PrettySQL(aSQL), "rp-sql");
        }

        private static string CodeBlock(string aCode)
        {
            return PreBlock(aCode, "rp-code");
        }

        // Card wrapper built from the node layer: title, optional subtitle, raw body.
        internal static string SectionCard(string aTitle, string aSubtitle,
            string aBodyHTML, string aCSSClass = "")
        {
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4 " + aCSSClass;
            oCard.Title = aTitle;
            if (aSubtitle != "")
            {
                TsgcHTMLParagraph oSub = new TsgcHTMLParagraph(aSubtitle);
                oSub.CSSClass = "text-muted small mb-3";
                oCard.Body.Add(oSub);
            }
            oCard.Body.AddRaw(aBodyHTML);
            return oCard.HTML;
        }

        private static string InfoAlert(string aText,
            TsgcHTMLAlertStyle aStyle = TsgcHTMLAlertStyle.asInfo)
        {
            TsgcHTMLAlert oAlert = new TsgcHTMLAlert();
            oAlert.Style = aStyle;
            oAlert.CSSClass = "mb-4";
            oAlert.AddText(aText);
            return oAlert.HTML;
        }

        private static string StatTile(string aTitle, string aValue, string aFooter,
            TsgcHTMLStatColor aColor, TsgcHTMLStatTrend aTrend, string aTrendValue)
        {
            TsgcHTMLComponent_StatCard oCard = new TsgcHTMLComponent_StatCard();
            oCard.Title = aTitle;
            oCard.Value = aValue;
            oCard.Color = aColor;
            oCard.Trend = aTrend;
            oCard.TrendValue = aTrendValue;
            oCard.FooterText = aFooter;
            oCard.CSSClass = "h-100 shadow-sm rp-metric";
            return oCard.HTML;
        }

        /// <summary>Percent-encoded query string, blank parts omitted.</summary>
        private static string BuildQS(params string[] aPairs)
        {
            string vQS = "";
            int vI = 0;
            while (vI < aPairs.Length - 1)
            {
                if ((aPairs[vI + 1] ?? "").Trim() != "")
                    vQS = vQS + "&" + aPairs[vI] + "=" +
                        Uri.EscapeDataString(aPairs[vI + 1]);
                vI += 2;
            }
            if (vQS == "")
                return "";
            return "?" + vQS.Substring(1);
        }

        // The /explore filter as a query string. Status is repeated once per
        // selected value, which is how a multi-select round-trips through a GET
        // form.
        private static string ExploreQS(TReportsExploreFilter aFilter, int aPageSize)
        {
            string vResult = BuildQS("search", aFilter.Search, "region", aFilter.Region,
                "segment", aFilter.Segment, "category", aFilter.Category, "dfrom",
                aFilter.DateFrom, "dto", aFilter.DateTo, "sort", aFilter.Sort, "dir",
                aFilter.Dir);
            for (int vI = 0; vI < aFilter.Statuses.Length; vI++)
                if (vResult == "")
                    vResult = "?status=" + Uri.EscapeDataString(aFilter.Statuses[vI]);
                else
                    vResult = vResult + "&status=" +
                        Uri.EscapeDataString(aFilter.Statuses[vI]);
            if (aFilter.MinQty > 0)
                vResult = vResult + (vResult == "" ? "?" : "&") + "qtylow=" +
                    aFilter.MinQty.ToString(CultureInfo.InvariantCulture);
            if (aFilter.MaxQty > 0)
                vResult = vResult + (vResult == "" ? "?" : "&") + "qtyhigh=" +
                    aFilter.MaxQty.ToString(CultureInfo.InvariantCulture);
            if (aPageSize > 0)
                vResult = vResult + (vResult == "" ? "?" : "&") + "size=" +
                    aPageSize.ToString(CultureInfo.InvariantCulture);
            return vResult;
        }

        private static bool RoleCanExport(string aRole)
        {
            return string.Equals(aRole, ReportsConst.CS_ROLE_ADMIN,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aRole, ReportsConst.CS_ROLE_ANALYST,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool RoleIsAdmin(string aRole)
        {
            return string.Equals(aRole, ReportsConst.CS_ROLE_ADMIN,
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Field-name -&gt; readable column title ('line_total' -&gt; 'Line Total').</summary>
        internal static string PrettyColumn(string aName)
        {
            char[] vResult = (aName ?? "").Replace("_", " ").ToCharArray();
            bool vUp = true;
            for (int vI = 0; vI < vResult.Length; vI++)
            {
                if (vUp)
                    vResult[vI] = char.ToUpperInvariant(vResult[vI]);
                vUp = vResult[vI] == ' ';
            }
            return new string(vResult);
        }

        internal static bool FieldIsNumeric(DataColumn aColumn)
        {
            Type vType = aColumn.DataType;
            return (vType == typeof(long)) || (vType == typeof(int)) ||
                (vType == typeof(short)) || (vType == typeof(byte)) ||
                (vType == typeof(double)) || (vType == typeof(float)) ||
                (vType == typeof(decimal));
        }

        private static bool FieldIsInteger(DataColumn aColumn)
        {
            Type vType = aColumn.DataType;
            return (vType == typeof(long)) || (vType == typeof(int)) ||
                (vType == typeof(short)) || (vType == typeof(byte));
        }

        // One display cell: numbers get thousands separators, everything else is
        // the field's own text.
        internal static string CellText(DataRow aRow, int aIndex)
        {
            DataColumn oColumn = aRow.Table.Columns[aIndex];
            object vValue = aRow[aIndex];
            if ((vValue == null) || (vValue == DBNull.Value))
                return "";
            if (FieldIsNumeric(oColumn))
            {
                if (FieldIsInteger(oColumn))
                    return FmtInt(Convert.ToInt64(vValue, CultureInfo.InvariantCulture));
                return FmtMoney(Convert.ToDouble(vValue, CultureInfo.InvariantCulture));
            }
            return Convert.ToString(vValue, CultureInfo.InvariantCulture) ?? "";
        }

        // Indigo-family palette for the charts, so every board reads as one product.
        private static string ChartColor(int aIndex)
        {
            string[] CS_PALETTE = new string[]
            {
                "#4F46E5", "#7C3AED", "#0EA5E9", "#10B981", "#F59E0B", "#EF4444",
                "#EC4899", "#64748B"
            };
            return CS_PALETTE[Math.Abs(aIndex) % CS_PALETTE.Length];
        }

        // Fill a Grid from an open query, the way every page in this portal does it.
        //
        // LoadFromDataSet is what derives the columns, their titles and their
        // alignment from the table metadata - that is the whole binding, and it is
        // the point of the demo. It fills the cells with the raw value text, which
        // prints a float at full precision, so the rows are re-emitted here through
        // CellText and the titles tidied through PrettyColumn. The export paths
        // deliberately do NOT go through this: a workbook wants the raw number, not
        // a formatted string.
        private static void GridLoadFormatted(TsgcHTMLComponent_Grid aGrid,
            DataTable aDataSet)
        {
            aGrid.LoadFromDataSet(aDataSet);
            for (int vI = 0; vI < aGrid.Columns.Count; vI++)
                aGrid.Columns.Items(vI).Title = PrettyColumn(aGrid.Columns.Items(vI).Title);
            aGrid.Rows.Clear();
            if (aDataSet == null)
                return;
            int vFields = aDataSet.Columns.Count;
            string[] vValues = new string[vFields];
            for (int vR = 0; vR < aDataSet.Rows.Count; vR++)
            {
                for (int vI = 0; vI < vFields; vI++)
                    vValues[vI] = CellText(aDataSet.Rows[vR], vI);
                aGrid.AddRow(vValues);
            }
        }

        // ==================================================================== //
        //  live (WebSocket push) state                                         //
        // ==================================================================== //

        public static void LiveInit()
        {
            lock (gLiveLock)
            {
                if (gLive == null)
                    gLive = new TReportsLive();
            }
        }

        public static void LiveDone()
        {
            lock (gLiveLock)
            {
                gLive = null;
            }
        }

        /// <summary>
        /// One step of every running export job. Returns the concatenated
        /// out-of-band fragments to broadcast, or "" when nothing changed.
        /// </summary>
        public static string LiveTick()
        {
            TReportsLive oLive = gLive;
            if (oLive == null)
                return "";
            return oLive.Tick();
        }

        /// <summary>Queue a scheduled export. Returns the fragments announcing it.</summary>
        public static string LiveStartJob(string aJobId, string aName,
            string aDescription)
        {
            TReportsLive oLive = gLive;
            if (oLive == null)
                return "";
            return oLive.StartJob(aJobId, aName, aDescription);
        }

        public static string LiveCancelJob(string aJobId)
        {
            TReportsLive oLive = gLive;
            if (oLive == null)
                return "";
            return oLive.CancelJob(aJobId);
        }

        public static bool LiveHasWork()
        {
            TReportsLive oLive = gLive;
            return (oLive != null) && oLive.HasWork();
        }

        // ==================================================================== //
        //  template + shell                                                    //
        // ==================================================================== //

        private static string WrapTemplate(string aTitle, string aBody, string aTheme,
            string aLang, bool aRealtime)
        {
            TsgcHTMLTemplate_Bootstrap oTpl = new TsgcHTMLTemplate_Bootstrap();
            oTpl.Title = aTitle;
            oTpl.HtmlLang = TReportsI18n.Normalize(aLang);
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
                oTpl.HeadNodes.AddRaw(
                    "<script>(function(){if(window.matchMedia&&window.matchMedia(" +
                    "'(prefers-color-scheme: dark)').matches){document." +
                    "documentElement.setAttribute('data-bs-theme','dark');}})();" +
                    "</script>");
            }
            oTpl.BodyContent = aBody;
            // htmx everywhere (fragment swaps), plus the WebSocket bridge on the
            // pages that receive pushed fragments.
            oTpl.BodyEndNodes.AddRaw("<script src=\"/htmx.min.js\"></script>");
            if (aRealtime)
                oTpl.BodyEndNodes.AddRaw("<script src=\"/sgcWebSockets.js\"></script>" +
                    "<script src=\"/sgcHTMX.min.js\"></script>" + "<script>" +
                    "document.addEventListener(\"DOMContentLoaded\",function(){" +
                    "if(window.sgcHTMX&&sgcHTMX.init){sgcHTMX.init({host:" +
                    "(location.protocol==='https:'?'wss:':'ws:')+'//'+" +
                    "location.host+location.pathname});}});</script>");
            return oTpl.GetHTML();
        }

        // Theme / language switcher, rendered from the node layer as two POST forms
        // inside a dropdown, so no client-side state is involved at all.
        private static string BuildPickerDropdown(string aID, string aCaption,
            string aAction, string aField, string[] aValues, string[] aLabels,
            string aCurrent)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "dropdown";

            TsgcHTMLContainer oToggle = new TsgcHTMLContainer("button");
            oToggle.Attributes = "type=\"button\" class=\"btn btn-sm " +
                "btn-outline-secondary dropdown-toggle\" id=\"" + HtmlEsc(aID) +
                "\" data-bs-toggle=\"dropdown\" aria-expanded=\"false\"";
            oToggle.AddText(aCaption);
            oWrap.Add(oToggle);

            TsgcHTMLContainer oUl = new TsgcHTMLContainer("ul");
            oUl.Attributes = "class=\"dropdown-menu dropdown-menu-end\" " +
                "aria-labelledby=\"" + HtmlEsc(aID) + "\"";
            for (int vI = 0; vI < aValues.Length; vI++)
            {
                TsgcHTMLContainer oLi = new TsgcHTMLContainer("li");
                TsgcHTMLForm oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = aAction;
                oForm.CSSClass = "m-0";
                oForm.AddHidden(aField, aValues[vI]);
                TsgcHTMLContainer oBtn = new TsgcHTMLContainer("button");
                oBtn.Attributes = "type=\"submit\" class=\"dropdown-item\"";
                TsgcHTMLContainer oSpan = new TsgcHTMLContainer("span");
                oSpan.AddText(aLabels[vI]);
                oBtn.Add(oSpan);
                if (string.Equals(aCurrent, aValues[vI],
                    StringComparison.OrdinalIgnoreCase))
                    oBtn.AddRaw(" <span class=\"ms-2\">&#10004;</span>");
                oForm.Add(oBtn);
                oLi.Add(oForm);
                oUl.Add(oLi);
            }
            oWrap.Add(oUl);
            oRoot.Add(oWrap);
            return oRoot.HTML;
        }

        private static string BuildTopBar(TReportsPageCtx aCtx, string aTitle,
            string aSubtitle)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TsgcHTMLContainer oBar = new TsgcHTMLContainer("div");
            oBar.CSSClass =
                "d-flex flex-wrap justify-content-between align-items-start mb-4 " +
                "rp-toolbar";

            TsgcHTMLContainer oLeft = new TsgcHTMLContainer("div");
            TsgcHTMLHeading oH = new TsgcHTMLHeading(aTitle, 4);
            oH.CSSClass = "mb-1 fw-semibold";
            oLeft.Add(oH);
            if (aSubtitle != "")
            {
                TsgcHTMLParagraph oP = new TsgcHTMLParagraph(aSubtitle);
                oP.CSSClass = "text-muted small mb-0";
                oLeft.Add(oP);
            }
            oBar.Add(oLeft);

            TsgcHTMLContainer oRight = new TsgcHTMLContainer("div");
            oRight.CSSClass = "d-flex align-items-center gap-2";

            TsgcHTMLContainer oUser = new TsgcHTMLContainer("span");
            oUser.CSSClass = "rp-chip d-none d-md-inline-block";
            oUser.AddText(aCtx.DisplayName + " (" + aCtx.Role + ")");
            oRight.Add(oUser);

            oRight.AddRaw(BuildPickerDropdown("rpLang",
                TReportsI18n.LangCaption(aCtx.Lang), "/lang", "lang",
                new string[] { "en", "es", "de" },
                new string[] { "English", "Espanol", "Deutsch" }, aCtx.Lang));

            oRight.AddRaw(BuildPickerDropdown("rpTheme",
                TReportsI18n.T(aCtx.Lang, "theme." + (aCtx.Theme ?? "").ToLowerInvariant()),
                "/theme", "theme",
                new string[] { "light", "dark", "system" },
                new string[]
                {
                    TReportsI18n.T(aCtx.Lang, "theme.light"),
                    TReportsI18n.T(aCtx.Lang, "theme.dark"),
                    TReportsI18n.T(aCtx.Lang, "theme.system")
                }, aCtx.Theme));

            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/logout";
            oForm.CSSClass = "m-0";
            TsgcHTMLButton oBtn = new TsgcHTMLButton(
                TReportsI18n.T(aCtx.Lang, "nav.logout"),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "btn-sm";
            oForm.Add(oBtn);
            oRight.Add(oForm);

            oBar.Add(oRight);
            oRoot.Add(oBar);
            return oRoot.HTML;
        }

        public string BuildPageShell(TReportsPageCtx aCtx, string aTitle,
            string aSubtitle, string aBodyHTML, string aActiveMenu,
            bool aRealtime = false)
        {
            TsgcHTMLDashboardLayout oLayout = new TsgcHTMLDashboardLayout();
            oLayout.LayoutID = "rpShell";
            oLayout.Sidebar.Brand = "sgcReports";
            oLayout.Sidebar.BrandHref = "/";
            oLayout.Sidebar.Dark = true;
            oLayout.Sidebar.Responsive = true;
            oLayout.Sidebar.CSSWidth = "240px";
            oLayout.Sidebar.FooterText = "Port 5708";
            oLayout.FooterText = "sgcHTML for Delphi and C++Builder";
            oLayout.SectionTitleBorderColor = CS_ACCENT;
            oLayout.DarkMode = string.Equals(aCtx.Theme, "dark",
                StringComparison.OrdinalIgnoreCase);

            AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.home"), "/", "home",
                aActiveMenu);
            AddHeader(oLayout, TReportsI18n.T(aCtx.Lang, "nav.dashboards"));
            AddMenu(oLayout, "Sales", "/dashboards/sales", "db.sales", aActiveMenu);
            AddMenu(oLayout, "Margin", "/dashboards/margin", "db.margin", aActiveMenu);
            AddMenu(oLayout, "Pipeline", "/dashboards/pipeline", "db.pipeline",
                aActiveMenu);
            AddMenu(oLayout, "Operations", "/dashboards/ops", "db.ops", aActiveMenu);
            AddHeader(oLayout, "Analyse");
            AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.reports"), "/reports",
                "reports", aActiveMenu);
            AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.pivot"), "/pivot", "pivot",
                aActiveMenu);
            AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.explore"), "/explore",
                "explore", aActiveMenu);
            AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.views"), "/views", "views",
                aActiveMenu);
            AddHeader(oLayout, "Automation");
            AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.schedules"), "/schedules",
                "schedules", aActiveMenu);
            AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.jobs"), "/jobs", "jobs",
                aActiveMenu);
            AddHeader(oLayout, "About");
            AddMenu(oLayout, "No REST layer", "/sql", "sql", aActiveMenu);
            if (RoleIsAdmin(aCtx.Role))
                AddMenu(oLayout, TReportsI18n.T(aCtx.Lang, "nav.users"), "/users",
                    "users", aActiveMenu);

            string vBody = BuildTopBar(aCtx, aTitle, aSubtitle) + aBodyHTML;

            // Ctrl+K jumps to any report or page from anywhere in the portal.
            TsgcHTMLComponent_CommandPalette oPalette =
                new TsgcHTMLComponent_CommandPalette();
            oPalette.PaletteID = CS_PALETTE_ID;
            oPalette.Placeholder = "Jump to a report or a page...";
            oPalette.EmptyText = "Nothing matches that.";
            oPalette.HotKey = "k";
            oPalette.MaxResults = 12;
            oPalette.ShowCategories = true;
            oPalette.ShowShortcuts = true;
            oPalette.AddItem("Dashboard", "/", "", "Pages", "G then D");
            oPalette.AddItem("Report catalogue", "/reports", "", "Pages");
            oPalette.AddItem("Pivot builder", "/pivot", "", "Pages");
            oPalette.AddItem("Explore order lines", "/explore", "", "Pages");
            oPalette.AddItem("Saved views", "/views", "", "Pages");
            oPalette.AddItem("Schedules", "/schedules", "", "Pages");
            oPalette.AddItem("Export jobs", "/jobs", "", "Pages");
            oPalette.AddItem("No REST layer", "/sql", "", "Pages");
            List<TReportsReportDef> vDefs = FDB.ListReportDefs();
            for (int vI = 0; vI < vDefs.Count; vI++)
                oPalette.AddItem(vDefs[vI].Name, "/reports/" +
                    vDefs[vI].Id.ToString(CultureInfo.InvariantCulture), "",
                    vDefs[vI].Category);
            vBody = vBody + oPalette.HTML;

            oLayout.AddRawContent(vBody);
            return WrapTemplate("sgcReports - " + aTitle, oLayout.HTML, aCtx.Theme,
                aCtx.Lang, aRealtime);
        }

        private static void AddMenu(TsgcHTMLDashboardLayout aLayout, string aText,
            string aHref, string aKey, string aActiveMenu)
        {
            TsgcHTMLSidebarItem oItem = aLayout.Sidebar.Items.Add();
            oItem.Text = aText;
            oItem.Href = aHref;
            oItem.Active = string.Equals(aActiveMenu, aKey,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void AddHeader(TsgcHTMLDashboardLayout aLayout, string aText)
        {
            TsgcHTMLSidebarItem oItem = aLayout.Sidebar.Items.Add();
            oItem.Text = aText;
            oItem.Header = true;
        }

        // ==================================================================== //
        //  login                                                               //
        // ==================================================================== //

        public string BuildLoginPage(string aTheme, string aLang, string aError = "",
            string aDefaultUser = "", string aDefaultPassword = "")
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container py-5";
            oWrap.Style = "max-width:520px;";

            TsgcHTMLComponent_Login oLogin = new TsgcHTMLComponent_Login();
            oLogin.Title = "sgcReports";
            oLogin.Subtitle = "Reporting and BI portal";
            oLogin.FormAction = "/login";
            oLogin.FormMethod = "POST";
            oLogin.LoginStyle = TsgcHTMLLoginStyle.lsCard;
            oLogin.UserLabel = "User";
            oLogin.PasswordLabel = "Password";
            oLogin.ButtonText = "Sign in";
            oLogin.ButtonStyleEnum = TsgcHTMLButtonStyle.bsPrimary;
            oLogin.LogoHTML = CS_LOGO_SVG;
            oLogin.UserValue = aDefaultUser;
            oLogin.PasswordValue = aDefaultPassword;
            oLogin.UserAutocomplete = "username";
            oLogin.PasswordAutocomplete = "current-password";
            oLogin.ErrorMessage = aError;
            oWrap.AddRaw(oLogin.HTML);

            // Passkeys: the same WebAuthn flow the AdminCRUD demo uses, wired to
            // /webauthn/* on this server.
            TsgcHTMLComponent_WebAuthnLogin oAuthn =
                new TsgcHTMLComponent_WebAuthnLogin();
            oAuthn.WebAuthnID = "rpPasskey";
            oAuthn.Mode = TsgcHTMLWebAuthnMode.wamAuthenticate;
            oAuthn.Title = "Passkey";
            oAuthn.Description =
                "Sign in with a device passkey instead of a password.";
            oAuthn.AuthenticateURL = "/webauthn/authenticate";
            oAuthn.CallbackURL = "/";
            oAuthn.AuthenticateButtonText = "Sign in with a passkey";
            oWrap.AddRaw("<div class=\"mt-3\">" + oAuthn.HTML + "</div>");

            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "mt-3 shadow-sm";
            oCard.Title = "Demo accounts";
            TsgcHTMLComponent_ListGroup oList = new TsgcHTMLComponent_ListGroup();
            oList.Flush = true;
            oList.AddItem("admin / admin", "", "builds and schedules",
                TsgcHTMLBadgeStyle.bgPrimary);
            oList.AddItem("analyst / demo1234", "", "runs and exports",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("viewer / demo1234", "", "reads only",
                TsgcHTMLBadgeStyle.bgSecondary);
            oCard.Body.AddRaw(oList.HTML);
            oWrap.Add(oCard);

            oRoot.Add(oWrap);
            return WrapTemplate("sgcReports - Sign in", oRoot.HTML, aTheme, aLang,
                false);
        }

        public string BuildNotFoundPage(string aTheme, string aLang)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container py-5";
            TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
            oEmpty.Title = "That page is not here";
            oEmpty.Description =
                "The address you asked for is not one of this portal's routes.";
            oEmpty.Icon = CS_EMPTY_SVG;
            oEmpty.ActionCaption = "Back to the dashboard";
            oEmpty.ActionHref = "/";
            oEmpty.Bordered = true;
            oWrap.AddRaw(oEmpty.HTML);
            oRoot.Add(oWrap);
            return WrapTemplate("sgcReports - Not found", oRoot.HTML, aTheme, aLang,
                false);
        }

        // ==================================================================== //
        //  home dashboard                                                      //
        // ==================================================================== //

        public string BuildHome(TReportsPageCtx aCtx)
        {
            TReportsKPIs vKPI = FDB.GetKPIs();
            double vTrendPct = 0;
            if (vKPI.RevenuePrev12M > 0)
                vTrendPct = 100.0 * (vKPI.Revenue12M - vKPI.RevenuePrev12M) /
                    vKPI.RevenuePrev12M;
            TsgcHTMLStatTrend vTrend;
            if (vTrendPct > 0.5)
                vTrend = TsgcHTMLStatTrend.stUp;
            else if (vTrendPct < -0.5)
                vTrend = TsgcHTMLStatTrend.stDown;
            else
                vTrend = TsgcHTMLStatTrend.stNeutral;

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            // ----- stat tiles ----- //
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-4";
            oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile("Revenue, last 12 months",
                FmtMoney0(vKPI.Revenue12M), "vs " + FmtMoney0(vKPI.RevenuePrev12M) +
                " the year before", TsgcHTMLStatColor.scPrimary, vTrend,
                FmtPct(vTrendPct)));
            oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile("Orders",
                FmtInt(vKPI.Orders12M), "average " + FmtMoney(vKPI.AvgOrderValue),
                TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, ""));
            oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile("Gross margin",
                FmtPct(vKPI.Margin12MPct), "revenue minus product cost",
                TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, ""));
            oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile(
                "Order lines in the database", FmtInt(vKPI.LinesTotal),
                "every page below reads these directly", TsgcHTMLStatColor.scDark,
                TsgcHTMLStatTrend.stNone, ""));
            oRoot.AddRaw(oRow.HTML);

            // ----- revenue trend + gauge ----- //
            List<TReportsSeriesPoint> vSeries = FDB.GetRevenueByMonth(24);
            double[] vValues = new double[vSeries.Count];
            double[] vOrders = new double[vSeries.Count];
            TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
            oChart.ChartID = "rpHomeTrend";
            oChart.ChartType = TsgcHTMLChartType.ctLine;
            oChart.CSSHeight = "300px";
            oChart.Title = "Revenue by month";
            oChart.PointRadius = 2;
            for (int vI = 0; vI < vSeries.Count; vI++)
            {
                oChart.AddLabel(vSeries[vI].BucketLabel);
                vValues[vI] = vSeries[vI].Value1;
                vOrders[vI] = vSeries[vI].Value2;
            }
            oChart.AddDataset("Revenue", vValues, ChartColor(0),
                ChartColor(0) + "33", true);
            string vBody = oChart.HTML;

            TsgcHTMLComponent_Gauge oGauge = new TsgcHTMLComponent_Gauge();
            oGauge.GaugeID = "rpHomeGauge";
            oGauge.Title = "Gross margin";
            oGauge.Unit_ = "%";
            oGauge.MinValue = 0;
            oGauge.MaxValue = 60;
            oGauge.Value = vKPI.Margin12MPct;
            oGauge.ThresholdMid = 25;
            oGauge.ThresholdHigh = 40;
            oGauge.Width = 220;
            // The gauge reads the other way round here: high margin is good.
            oGauge.ColorLow = "#EF4444";
            oGauge.ColorMid = "#F59E0B";
            oGauge.ColorHigh = "#10B981";

            TsgcHTMLComponent_Sparkline oSpark = new TsgcHTMLComponent_Sparkline();
            double[] vSpark = FDB.GetDailyRevenueSparkline(45);
            oSpark.ChartType = TsgcHTMLSparklineType.slArea;
            oSpark.Width = 220;
            oSpark.Height = 48;
            oSpark.LineColor = CS_ACCENT;
            oSpark.FillColor = CS_ACCENT + "33";
            oSpark.ShowLastPoint = true;
            oSpark.SetData(vSpark);

            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-4";
            oRow.Col(TsgcHTMLColWidth.cw8).AddRaw(SectionCard("Trend",
                "Chart.js series fed straight from a query with LoadFromDataSet. " +
                "No JSON endpoint sits between them.", vBody));
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(SectionCard("Health",
                "Gauge and Sparkline, both server-rendered SVG.",
                "<div class=\"text-center\">" + oGauge.HTML + "<div class=\"mt-3\">" +
                oSpark.HTML + "</div>" +
                "<div class=\"small text-muted mt-2\">Daily revenue, 45 days" +
                "</div></div>"));
            oRoot.AddRaw(oRow.HTML);

            // ----- revenue by region treemap + latest orders grid ----- //
            vSeries = FDB.GetRevenueByRegion();
            TsgcHTMLComponent_TreeMap oTreeMap = new TsgcHTMLComponent_TreeMap();
            oTreeMap.Width = 520;
            oTreeMap.Height = 300;
            oTreeMap.ShowValues = true;
            oTreeMap.Decimals = 0;
            oTreeMap.ColorScheme = TsgcHTMLTreeMapScheme.tmCool;
            for (int vI = 0; vI < vSeries.Count; vI++)
                oTreeMap.AddItem(vSeries[vI].BucketLabel, vSeries[vI].Value1,
                    ChartColor(vI));
            // The TreeMap draws a fixed-width SVG, so it gets its own scroller
            // rather than pushing the page wider than the phone it is read on.
            vBody = "<div class=\"rp-svg-scroll text-center\">" + oTreeMap.HTML +
                "</div>";

            using (TReportsQuery oQ = FDB.NewQuery(
                "SELECT o.order_date AS \"Date\", c.name AS \"Customer\", " +
                "o.status AS \"Status\", ROUND(o.total, 2) AS \"Total\" FROM orders o " +
                "JOIN customers c ON c.id = o.customer_id " +
                "ORDER BY o.order_date DESC, o.id DESC LIMIT 12"))
            {
                oQ.Open();
                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "rpHomeLatest";
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.EmptyText = "No orders yet.";
                oGrid.HeaderClass = "table-light";
                GridLoadFormatted(oGrid, oQ.DataSet);

                oRow = new TsgcHTMLRow();
                oRow.CSSClass = "g-3";
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(SectionCard("Revenue by region",
                    "TreeMap, sized by revenue.", vBody));
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(SectionCard("Latest orders",
                    "Grid.LoadFromDataSet(oQuery.DataSet) - that is the whole " +
                    "binding.", oGrid.HTML));
                oRoot.AddRaw(oRow.HTML);
            }

            vBody = oRoot.HTML;
            return BuildPageShell(aCtx, "Dashboard", "Live figures from " +
                FmtInt(vKPI.LinesTotal) + " order lines, queried in-process.", vBody,
                "home");
        }

        // ==================================================================== //
        //  dashboards/{slug}                                                   //
        // ==================================================================== //

        public string BuildDashboard(TReportsPageCtx aCtx, string aSlug)
        {
            string vSlug = (aSlug ?? "").Trim().ToLowerInvariant();
            string vTitle;
            string vSubtitle;
            string vBody;
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            TsgcHTMLRow oRow;

            TsgcHTMLComponent_Breadcrumb oBread = new TsgcHTMLComponent_Breadcrumb();
            TsgcHTMLBreadcrumbItem oCrumb = oBread.Items.Add();
            oCrumb.Text = "Dashboard";
            oCrumb.Href = "/";
            oCrumb = oBread.Items.Add();
            oCrumb.Text = "Boards";
            oCrumb.Href = "/dashboards/sales";
            oCrumb = oBread.Items.Add();
            oCrumb.Text = PrettyColumn(vSlug);
            oCrumb.Active = true;
            oRoot.AddRaw(oBread.HTML);

            if (vSlug == "margin")
            {
                vTitle = "Margin board";
                vSubtitle = "Where the gross margin actually comes from.";
                List<TReportsSeriesPoint> vSeries = FDB.GetMarginByCategory();
                double[] vValues = new double[vSeries.Count];
                double[] vValues2 = new double[vSeries.Count];
                TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
                oChart.ChartID = "rpMarginChart";
                oChart.ChartType = TsgcHTMLChartType.ctBar;
                oChart.CSSHeight = "320px";
                oChart.Title = "Revenue and margin by category";
                for (int vI = 0; vI < vSeries.Count; vI++)
                {
                    oChart.AddLabel(vSeries[vI].BucketLabel);
                    vValues[vI] = vSeries[vI].Value1;
                    vValues2[vI] = vSeries[vI].Value2;
                }
                oChart.AddDataset("Revenue", vValues, ChartColor(0), ChartColor(0));
                oChart.AddDataset("Margin", vValues2, ChartColor(3), ChartColor(3));
                oRoot.AddRaw(SectionCard("Margin by category",
                    "Two datasets, one dataset each per series, from one grouped " +
                    "query.", oChart.HTML));

                // Pivot: category down the rows, discount band across the columns.
                using (TReportsQuery oQ = FDB.NewQuery(
                    "SELECT cat.name AS category, " +
                    "CAST(ROUND(ol.discount * 100) AS INTEGER) || '%' AS disc, " +
                    "ROUND(ol.line_total, 2) AS revenue, " +
                    "ROUND(ol.line_total - ol.qty * p.unit_cost, 2) AS margin " +
                    "FROM order_lines ol JOIN orders o ON o.id = ol.order_id " +
                    "JOIN products p ON p.id = ol.product_id " +
                    "JOIN categories cat ON cat.id = p.category_id " +
                    "WHERE o.status <> 'Cancelled' AND o.order_date >= :d"))
                {
                    oQ.SetParamStr("d", ReportsDBHelpers.FormatReportsDate(
                        DateTime.Today.AddDays(-365)));
                    oQ.Open();
                    TsgcHTMLComponent_PivotTable oPivot =
                        new TsgcHTMLComponent_PivotTable();
                    oPivot.TableID = "rpMarginPivot";
                    oPivot.Caption = "Margin by category and discount band";
                    oPivot.Compact = true;
                    oPivot.RowFields.Add().FieldName = "category";
                    oPivot.ColumnFields.Add().FieldName = "disc";
                    TsgcHTMLPivotMeasure oMeasure = oPivot.Measures.Add();
                    oMeasure.SourceField = "margin";
                    oMeasure.Caption = "Margin";
                    oMeasure.Aggregation = TsgcHTMLPivotAggregation.paSum;
                    oMeasure.Format = "#,##0";
                    oPivot.LoadFromDataSet(oQ.DataSet);
                    oRoot.AddRaw(SectionCard("Discount impact",
                        "PivotTable.LoadFromDataSet over " + FmtInt(oQ.RecordCount) +
                        " order lines, aggregated in the component.", oPivot.HTML));
                }
            }
            else if (vSlug == "pipeline")
            {
                vTitle = "Pipeline board";
                vSubtitle = "What is still open, and what it is worth.";
                List<TReportsSeriesPoint> vSeries = FDB.GetStatusMix();
                double[] vValues = new double[vSeries.Count];
                TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
                oChart.ChartID = "rpPipeChart";
                oChart.ChartType = TsgcHTMLChartType.ctDoughnut;
                oChart.CSSHeight = "300px";
                oChart.Title = "Orders by status";
                for (int vI = 0; vI < vSeries.Count; vI++)
                {
                    oChart.AddLabel(vSeries[vI].BucketLabel);
                    vValues[vI] = vSeries[vI].Value1;
                }
                oChart.AddDataset("Orders", vValues, "", ChartColor(0));
                vBody = oChart.HTML;

                using (TReportsQuery oQ = FDB.NewQuery(
                    "SELECT o.order_date AS \"Ordered\", " +
                    "c.name AS \"Customer\", r.name AS \"Region\", s.name AS \"Rep\", " +
                    "ROUND(o.total, 2) AS \"Value\" FROM orders o " +
                    "JOIN customers c ON c.id = o.customer_id " +
                    "JOIN regions r ON r.id = c.region_id " +
                    "JOIN salespeople s ON s.id = o.salesperson_id " +
                    "WHERE o.status = 'Open' ORDER BY o.total DESC LIMIT 25"))
                {
                    oQ.Open();
                    TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                    oGrid.TableID = "rpPipeGrid";
                    oGrid.ShowSort = true;
                    oGrid.ShowFilter = true;
                    oGrid.Responsive = true;
                    oGrid.HeaderClass = "table-light";
                    oGrid.EmptyText = "Nothing open.";
                    GridLoadFormatted(oGrid, oQ.DataSet);
                    oRow = new TsgcHTMLRow();
                    oRow.CSSClass = "g-3";
                    oRow.Col(TsgcHTMLColWidth.cw5).AddRaw(SectionCard("Status mix", "",
                        vBody));
                    oRow.Col(TsgcHTMLColWidth.cw7).AddRaw(SectionCard(
                        "Biggest open orders",
                        "Client-side sort and filter on top of a server-side query.",
                        oGrid.HTML));
                    oRoot.AddRaw(oRow.HTML);
                }
            }
            else if (vSlug == "ops")
            {
                vTitle = "Operations board";
                vSubtitle = "Fulfilment and the shape of the catalogue.";
                List<string> vRowLabels;
                List<string> vColLabels;
                double[] vHeat;
                FDB.GetRevenueHeatmap(out vRowLabels, out vColLabels, out vHeat);
                TsgcHTMLComponent_Heatmap oHeat = new TsgcHTMLComponent_Heatmap();
                oHeat.CellSize = 44;
                oHeat.CellGap = 3;
                oHeat.Rounded = true;
                oHeat.ShowLegend = true;
                oHeat.MinColor = "#EEF2FF";
                oHeat.MaxColor = CS_ACCENT;
                oHeat.AutoMin = false;
                oHeat.MinValue = 0;
                for (int vI = 0; vI < vRowLabels.Count; vI++)
                    oHeat.RowLabels.Add(vRowLabels[vI]);
                for (int vI = 0; vI < vColLabels.Count; vI++)
                    oHeat.ColumnLabels.Add(vColLabels[vI].Length >= 7
                        ? vColLabels[vI].Substring(5, 2) : vColLabels[vI]);
                for (int vI = 0; vI < vRowLabels.Count; vI++)
                    for (int vJ = 0; vJ < vColLabels.Count; vJ++)
                        oHeat.SetCell(vI, vJ, vHeat[vI * vColLabels.Count + vJ]);
                oRoot.AddRaw(SectionCard("Revenue heatmap",
                    "Region down the rows, month across the columns, one dense SVG " +
                    "rendered on the server.", "<div class=\"rp-svg-scroll\">" +
                    oHeat.HTML + "</div>"));

                List<string> vLabels;
                double[] vOpen, vHigh, vLow, vClose, vVol;
                int vCount = FDB.GetPriceHistoryOHLC(18, out vLabels, out vOpen,
                    out vHigh, out vLow, out vClose, out vVol);
                TsgcHTMLComponent_CandlestickChart oCandle =
                    new TsgcHTMLComponent_CandlestickChart();
                oCandle.Width = 900;
                oCandle.Height = 360;
                oCandle.ShowVolume = true;
                oCandle.ShowGrid = true;
                oCandle.UpColor = "#10B981";
                oCandle.DownColor = "#EF4444";
                oCandle.Decimals = 0;
                for (int vI = 0; vI < vCount; vI++)
                    oCandle.AddPoint(vLabels[vI], vOpen[vI], vHigh[vI], vLow[vI],
                        vClose[vI], vVol[vI]);
                oRoot.AddRaw(SectionCard("Average selling price, monthly OHLC",
                    "The candlesticks are computed in SQL from the order lines " +
                    "themselves: open, high, low and close of the average net unit " +
                    "price, with the line count as volume.",
                    "<div class=\"rp-svg-scroll\">" + oCandle.HTML + "</div>"));
            }
            else
            {
                vSlug = "sales";
                vTitle = "Sales board";
                vSubtitle = "Who sold what, and where.";
                List<TReportsSeriesPoint> vSeries = FDB.GetTopSalespeople(12);
                double[] vValues = new double[vSeries.Count];
                double[] vValues2 = new double[vSeries.Count];
                TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
                oChart.ChartID = "rpSalesChart";
                oChart.ChartType = TsgcHTMLChartType.ctBar;
                oChart.CSSHeight = "340px";
                oChart.Title = "Revenue against annual target";
                for (int vI = 0; vI < vSeries.Count; vI++)
                {
                    oChart.AddLabel(vSeries[vI].BucketLabel);
                    vValues[vI] = vSeries[vI].Value1;
                    vValues2[vI] = vSeries[vI].Value2;
                }
                oChart.AddDataset("Revenue", vValues, ChartColor(0), ChartColor(0));
                oChart.AddDataset("Target", vValues2, ChartColor(4), ChartColor(4));
                oRoot.AddRaw(SectionCard("Salesperson performance", "", oChart.HTML));

                using (TReportsQuery oQ = FDB.NewQuery(
                    "SELECT r.id AS region_id, r.name AS \"Region\", " +
                    "COUNT(DISTINCT c.id) AS \"Customers\", " +
                    "COUNT(DISTINCT o.id) AS \"Orders\", " +
                    "ROUND(SUM(ol.line_total), 2) AS \"Revenue\" FROM regions r " +
                    "JOIN customers c ON c.region_id = r.id " +
                    "JOIN orders o ON o.customer_id = c.id " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.status <> 'Cancelled' GROUP BY r.id, r.name " +
                    "ORDER BY \"Revenue\" DESC"))
                {
                    oQ.Open();
                    TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                    oGrid.TableID = "rpSalesRegions";
                    oGrid.Responsive = true;
                    oGrid.HeaderClass = "table-light";
                    GridLoadFormatted(oGrid, oQ.DataSet);
                    if (oGrid.Columns.Count > 0)
                        oGrid.Columns.Items(0).Title = "id";
                    oRoot.AddRaw(SectionCard("Regions",
                        "Click a region in the drilldown links below to walk from the " +
                        "summary down to the individual customers.", oGrid.HTML));

                    // Drilldown links, one per region, built from the same open query.
                    vBody = "";
                    DataTable oTable = oQ.DataSet;
                    for (int vI = 0; vI < oTable.Rows.Count; vI++)
                        vBody = vBody + "<a class=\"btn btn-sm btn-outline-primary me-2 " +
                            "mb-2\" href=\"/drilldown/region/" +
                            HtmlEsc(TReportsDBPool.CellString(oTable.Rows[vI], "region_id")) +
                            "\">" +
                            HtmlEsc(TReportsDBPool.CellString(oTable.Rows[vI], "Region")) +
                            "</a>";
                    oRoot.AddRaw(SectionCard("Drill down", "", vBody));
                }
            }

            vBody = oRoot.HTML;
            return BuildPageShell(aCtx, vTitle, vSubtitle, vBody, "db." + vSlug);
        }

        // ==================================================================== //
        //  report catalogue                                                    //
        // ==================================================================== //

        public string BuildReportCatalogue(TReportsPageCtx aCtx, string aCategory)
        {
            List<TReportsReportDef> vDefs = FDB.ListReportDefs(aCategory);
            List<string> vCats = FDB.ListReportCategories();

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TsgcHTMLComponent_Toolbar oToolbar = new TsgcHTMLComponent_Toolbar();
            oToolbar.ToolbarID = "rpCatToolbar";
            oToolbar.AddButton("All categories", TsgcHTMLButtonStyle.bsOutlinePrimary,
                "/reports");
            for (int vI = 0; vI < vCats.Count; vI++)
                oToolbar.AddButton(vCats[vI], TsgcHTMLButtonStyle.bsOutlineSecondary,
                    "/reports" + BuildQS("category", vCats[vI]));
            if (RoleIsAdmin(aCtx.Role))
            {
                oToolbar.AddSeparator();
                oToolbar.AddButton("New report (admin)", TsgcHTMLButtonStyle.bsPrimary,
                    "/reports/new");
            }
            oRoot.AddRaw("<div class=\"mb-3\">" + oToolbar.HTML + "</div>");

            if (vDefs.Count == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No report in this category";
                oEmpty.Description =
                    "Pick another category, or create one if you are an administrator.";
                oEmpty.Icon = CS_EMPTY_SVG;
                oEmpty.ActionCaption = "Show every report";
                oEmpty.ActionHref = "/reports";
                oEmpty.Bordered = true;
                oRoot.AddRaw(oEmpty.HTML);
            }
            else
            {
                // The node-layer accordion is used here on purpose: its item Body is a
                // node list that takes the rendered ListGroup through AddRaw, while
                // TsgcHTMLComponent_Accordion.Content is HTML-encoded and would print
                // the markup instead of rendering it.
                TsgcHTMLAccordion oAccordion = new TsgcHTMLAccordion("rpCatalogue");
                for (int vI = 0; vI < vCats.Count; vI++)
                {
                    TsgcHTMLComponent_ListGroup oList = new TsgcHTMLComponent_ListGroup();
                    oList.Flush = true;
                    for (int vJ = 0; vJ < vDefs.Count; vJ++)
                        if (string.Equals(vDefs[vJ].Category, vCats[vI],
                            StringComparison.OrdinalIgnoreCase))
                            oList.AddItem(vDefs[vJ].Name + " - " + vDefs[vJ].Description,
                                "/reports/" + vDefs[vJ].Id.ToString(
                                    CultureInfo.InvariantCulture), "Run",
                                TsgcHTMLBadgeStyle.bgPrimary);
                    string vInner = oList.HTML;
                    if (vInner.IndexOf("list-group-item", StringComparison.Ordinal) < 0)
                        continue;
                    oAccordion.AddItem(vCats[vI], true).Body.AddRaw(vInner);
                }
                oRoot.AddRaw(SectionCard("Report catalogue",
                    "Every entry runs one parameterised SELECT in this process and " +
                    "renders straight into a component.", oAccordion.HTML));
            }

            // How a report actually runs. Plain text in a component Accordion, which
            // HTML-encodes its Content (unlike its node-layer namesake), so this is
            // exactly the shape that component is meant for.
            TsgcHTMLComponent_Accordion oExplain = new TsgcHTMLComponent_Accordion();
            oExplain.AccordionID = "rpHow";
            TsgcHTMLAccordionItem oStep = oExplain.Items.Add();
            oStep.Title = "1. You pick the parameters";
            oStep.Content = "Each report declares its parameters in params_json. " +
                "The form is rendered from those declarations, one sgcHTML input " +
                "component per parameter, and the values you enter are bound to " +
                "command parameters. Nothing you type is ever concatenated into " +
                "the statement.";
            oStep = oExplain.Items.Add();
            oStep.Title = "2. The query runs in this process";
            oStep.Content = "A query opens the stored SELECT against the " +
                "database on a connection the SQLite driver opened read-only. " +
                "There is no REST call, no JSON payload and no second server.";
            oStep = oExplain.Items.Add();
            oStep.Title = "3. The dataset is handed to a component";
            oStep.Content = "Grid.LoadFromDataSet and Chart.LoadFromDataSet read the " +
                "same open dataset and render HTML. The row count and the " +
                "milliseconds the database took are recorded, and both are shown " +
                "on screen, so the numbers can be checked rather than believed.";
            oRoot.AddRaw(SectionCard("How a report runs here", "", oExplain.HTML));

            // Run history. AuditTrail is the component for a who-did-what-when log,
            // and the row count and server milliseconds are what make the throughput
            // claim checkable instead of merely asserted.
            List<TReportsRun> vRuns = FDB.ListRuns(25);
            if (vRuns.Count > 0)
            {
                TsgcHTMLComponent_AuditTrail oAudit = new TsgcHTMLComponent_AuditTrail();
                oAudit.AuditID = "rpRuns";
                oAudit.ShowFilter = true;
                oAudit.FilterPlaceholder = "Filter the run history";
                oAudit.PageSize = 10;
                oAudit.Striped = true;
                oAudit.EmptyText = "Nothing has been run yet.";
                for (int vI = 0; vI < vRuns.Count; vI++)
                    oAudit.AddEntry(vRuns[vI].Username, "ran", vRuns[vI].ReportName,
                        "", vRuns[vI].Status, FmtInt(vRuns[vI].RowCount) + " rows in " +
                        FmtInt(vRuns[vI].DurationMS) + " ms").Timestamp =
                        vRuns[vI].CreatedAt;
                oRoot.AddRaw(SectionCard("Recent runs",
                    "Every run records its row count and the milliseconds the " +
                    "database actually took.", oAudit.HTML));
            }

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "ttl.catalogue"),
                vDefs.Count.ToString(CultureInfo.InvariantCulture) +
                " reports available.", oRoot.HTML, "reports");
        }

        // ==================================================================== //
        //  report parameter form                                               //
        // ==================================================================== //

        // Build the input for one declared parameter, using the component that
        // matches its kind.
        private static string BuildParamField(TReportsDBPool aDB,
            TReportsParamDef aDef, string aValue)
        {
            if (aDef.Kind == "date")
            {
                TsgcHTMLComponent_DatePicker oDate = new TsgcHTMLComponent_DatePicker();
                oDate.DatePickerID = "rpP_" + aDef.Name;
                oDate.ElementName = aDef.Name;
                oDate.Label_ = aDef.Caption;
                oDate.Mode = TsgcHTMLDatePickerMode.dmDate;
                oDate.Value = aValue;
                return oDate.HTML;
            }
            if (aDef.Kind == "list")
            {
                TsgcHTMLComponent_Select oSelect = new TsgcHTMLComponent_Select();
                oSelect.SelectID = "rpP_" + aDef.Name;
                oSelect.ElementName = aDef.Name;
                oSelect.Label_ = aDef.Caption;
                oSelect.AddOption("", "(all)", aValue == "");
                List<string> vOptions;
                if (aDef.Options == "categories")
                    vOptions = aDB.ListCategories();
                else if (aDef.Options == "regions")
                    vOptions = aDB.ListRegions();
                else if (aDef.Options == "segments")
                    vOptions = aDB.ListSegments();
                else if (aDef.Options == "statuses")
                    vOptions = aDB.ListStatuses();
                else
                    vOptions = new List<string>();
                for (int vI = 0; vI < vOptions.Count; vI++)
                {
                    int vPos = vOptions[vI].IndexOf('|');
                    if (vPos < 0)
                        continue;
                    string vValue = vOptions[vI].Substring(0, vPos);
                    oSelect.AddOption(vValue, vOptions[vI].Substring(vPos + 1),
                        string.Equals(aValue, vValue, StringComparison.OrdinalIgnoreCase));
                }
                return oSelect.HTML;
            }
            if (aDef.Kind == "range")
            {
                TsgcHTMLComponent_Slider oSlider = new TsgcHTMLComponent_Slider();
                oSlider.SliderID = "rpP_" + aDef.Name;
                oSlider.FieldName = aDef.Name;
                oSlider.LabelText = aDef.Caption;
                oSlider.Min = aDef.MinValue;
                if (aDef.MaxValue > aDef.MinValue)
                    oSlider.Max = aDef.MaxValue;
                else
                    oSlider.Max = 100;
                oSlider.Step = 1;
                oSlider.Value = TReportsDBPool.StrToIntDef(aValue, aDef.MinValue);
                oSlider.ShowValue = true;
                oSlider.ShowMinMax = true;
                oSlider.Color = ReportsConst.CS_BRAND_ACCENT;
                return oSlider.HTML;
            }

            TsgcHTMLField oField = new TsgcHTMLField(TsgcHTMLInputType.itText,
                aDef.Name);
            if (aDef.Kind == "int")
                oField.InputType = TsgcHTMLInputType.itNumber;
            oField.FieldID = "rpP_" + aDef.Name;
            oField.Label_ = aDef.Caption;
            oField.Value = aValue;
            if ((aDef.Kind == "int") && (aDef.MaxValue > aDef.MinValue))
                oField.Attributes = "min=\"" +
                    aDef.MinValue.ToString(CultureInfo.InvariantCulture) + "\" max=\"" +
                    aDef.MaxValue.ToString(CultureInfo.InvariantCulture) + "\"";
            return oField.HTML;
        }

        // Resolve the default of a declared parameter: a date default is a relative
        // day offset ('-365'), everything else is taken literally.
        private static string ResolveParamDefault(TReportsParamDef aDef)
        {
            string vResult = aDef.DefaultValue;
            if (aDef.Kind != "date")
                return vResult;
            int vOffset;
            if (int.TryParse((aDef.DefaultValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vOffset))
                return ReportsDBHelpers.FormatReportsDate(
                    DateTime.Today.AddDays(vOffset));
            if (vResult == "")
                return ReportsDBHelpers.FormatReportsDate(DateTime.Today);
            return vResult;
        }

        public string BuildReportForm(TReportsPageCtx aCtx, TReportsReportDef aDef,
            List<TReportsParamValue> aValues, string aError = "")
        {
            List<TReportsParamDef> vDefs = TReportsDBPool.ParseParamDefs(aDef.ParamsJSON);

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TsgcHTMLComponent_Breadcrumb oBread = new TsgcHTMLComponent_Breadcrumb();
            TsgcHTMLBreadcrumbItem oCrumb = oBread.Items.Add();
            oCrumb.Text = "Reports";
            oCrumb.Href = "/reports";
            oCrumb = oBread.Items.Add();
            oCrumb.Text = aDef.Category;
            oCrumb.Href = "/reports" + BuildQS("category", aDef.Category);
            oCrumb = oBread.Items.Add();
            oCrumb.Text = aDef.Name;
            oCrumb.Active = true;
            oRoot.AddRaw(oBread.HTML);

            if (aError != "")
                oRoot.AddRaw(InfoAlert(aError, TsgcHTMLAlertStyle.asDanger));

            // ----- parameter form, posted to the htmx results target ----- //
            string vId = aDef.Id.ToString(CultureInfo.InvariantCulture);
            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/reports/" + vId + "/run";
            oForm.FormID = "rpRunForm";
            oForm.Attributes = "hx-post=\"/reports/" + vId +
                "/run\" hx-target=\"#rpResults\" hx-swap=\"innerHTML\" " +
                "hx-indicator=\"#rpSkeleton\"";
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 align-items-end";
            for (int vI = 0; vI < vDefs.Count; vI++)
            {
                string vValue = ResolveParamDefault(vDefs[vI]);
                for (int vJ = 0; vJ < aValues.Count; vJ++)
                    if (string.Equals(aValues[vJ].Name, vDefs[vI].Name,
                        StringComparison.OrdinalIgnoreCase))
                        vValue = aValues[vJ].Value;
                oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(
                    BuildParamField(FDB, vDefs[vI], vValue));
            }
            TsgcHTMLCol oCol = oRow.Col(TsgcHTMLColWidth.cw3);
            TsgcHTMLButton oBtn = new TsgcHTMLButton(
                TReportsI18n.T(aCtx.Lang, "btn.run"), TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100";
            oCol.Add(oBtn);
            oForm.Add(oRow);
            oRoot.AddRaw(SectionCard(aDef.Name, aDef.Description, oForm.HTML));

            // ----- column chooser (Transfer), purely a display preference ----- //
            List<string> vCols = new List<string>();
            try
            {
                using (TReportsQuery oQ = FDB.NewReportQuery(aDef, aValues))
                {
                    for (int vI = 0; vI < oQ.DataSet.Columns.Count; vI++)
                        vCols.Add(oQ.DataSet.Columns[vI].ColumnName);
                }
            }
            catch (Exception)
            {
                vCols.Clear();
            }

            if (vCols.Count > 0)
            {
                TsgcHTMLComponent_Transfer oTransfer = new TsgcHTMLComponent_Transfer();
                oTransfer.TransferID = "rpCols";
                oTransfer.FieldName = "columns";
                oTransfer.TitleSource = "Available columns";
                oTransfer.TitleTarget = "Shown in the export";
                oTransfer.ShowFilter = true;
                oTransfer.CSSHeight = "190px";
                for (int vI = 0; vI < vCols.Count; vI++)
                    oTransfer.AddItem(vCols[vI], PrettyColumn(vCols[vI]), true);
                oRoot.AddRaw(SectionCard("Columns",
                    "Transfer.LoadFromDataSet would fill this from a table just as " +
                    "easily; here the columns come from the report's own result " +
                    "set metadata.", oTransfer.HTML));
            }

            // ----- export buttons + results target ----- //
            string vQS = "";
            for (int vI = 0; vI < aValues.Count; vI++)
                if ((aValues[vI].Value ?? "").Trim() != "")
                    vQS = vQS + "&" + aValues[vI].Name + "=" +
                        Uri.EscapeDataString(aValues[vI].Value);
            if (vQS != "")
                vQS = "?" + vQS.Substring(1);

            string vBody = "";
            if (RoleCanExport(aCtx.Role))
                vBody = "<a class=\"btn btn-outline-danger btn-sm me-2\" href=\"/reports/" +
                    vId + "/run.pdf" + HtmlEsc(vQS) + "\">" +
                    HtmlEsc(TReportsI18n.T(aCtx.Lang, "btn.pdf")) + "</a>" +
                    "<a class=\"btn btn-outline-success btn-sm me-2\" href=\"/reports/" +
                    vId + "/run.xlsx" + HtmlEsc(vQS) + "\">" +
                    HtmlEsc(TReportsI18n.T(aCtx.Lang, "btn.xlsx")) + "</a>" +
                    "<a class=\"btn btn-outline-secondary btn-sm me-2\" href=\"/reports/" +
                    vId + "/run.csv" + HtmlEsc(vQS) + "\">" +
                    HtmlEsc(TReportsI18n.T(aCtx.Lang, "btn.csv")) + "</a>";
            vBody = vBody + "<a class=\"btn btn-primary btn-sm\" href=\"/reports/" +
                vId + "/preview" + HtmlEsc(vQS) + "\">" +
                HtmlEsc(TReportsI18n.T(aCtx.Lang, "btn.preview")) + "</a>";
            if (!RoleCanExport(aCtx.Role))
                vBody = vBody + "<div class=\"small text-muted mt-2\">The viewer role " +
                    "can read a report and its PDF preview, but not export the raw " +
                    "rows.</div>";
            oRoot.AddRaw(SectionCard("Output",
                "The PDF is written by TsgcHTMLExportPDF on the server, the " +
                "workbook by TsgcHTMLExportXLSX. No third-party reporting tool is " +
                "involved.", vBody));

            // Skeleton shown by htmx while the report runs.
            TsgcHTMLComponent_Placeholder oPlaceholder =
                new TsgcHTMLComponent_Placeholder();
            oPlaceholder.PlaceholderID = "rpSkeleton";
            oPlaceholder.LineCount = 6;
            oPlaceholder.Animation = TsgcHTMLPlaceholderAnimation.paWave;
            oPlaceholder.ShowTitle = true;
            oRoot.AddRaw("<div id=\"rpSkeleton\" class=\"htmx-indicator\">" +
                oPlaceholder.HTML + "</div>");

            oRoot.AddRaw("<div id=\"rpResults\"></div>");

            return BuildPageShell(aCtx, aDef.Name, aDef.Description, oRoot.HTML,
                "reports");
        }

        // ==================================================================== //
        //  report results fragment                                             //
        // ==================================================================== //

        /// <summary>
        /// The htmx fragment POST /reports/{id}/run answers with: the results grid,
        /// the chart and the run metrics. aRowCount / aDurationMS come back so the
        /// caller can write the run history row.
        /// </summary>
        public string BuildReportResultFragment(TReportsPageCtx aCtx,
            TReportsReportDef aDef, List<TReportsParamValue> aValues,
            out int aRowCount, out int aDurationMS)
        {
            aRowCount = 0;
            aDurationMS = 0;
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TReportsQuery oQ;
            try
            {
                oQ = FDB.NewReportQuery(aDef, aValues);
            }
            catch (Exception E)
            {
                return InfoAlert("The report could not be run: " + E.Message,
                    TsgcHTMLAlertStyle.asDanger);
            }

            using (oQ)
            {
                aRowCount = oQ.RecordCount;
                aDurationMS = oQ.ElapsedMS;

                if (aRowCount == 0)
                {
                    TsgcHTMLComponent_EmptyState oEmpty =
                        new TsgcHTMLComponent_EmptyState();
                    oEmpty.Title = "No rows matched";
                    oEmpty.Description =
                        "Widen the date range, or clear the optional filters.";
                    oEmpty.Icon = CS_EMPTY_SVG;
                    oEmpty.Compact = true;
                    oEmpty.Bordered = true;
                    oRoot.AddRaw(oEmpty.HTML);
                    return oRoot.HTML;
                }

                // Metrics strip: the numbers that make the claim checkable.
                TsgcHTMLRow oRow = new TsgcHTMLRow();
                oRow.CSSClass = "g-3 mb-3";
                oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(StatTile("Rows returned",
                    FmtInt(aRowCount), "materialised in this process",
                    TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone, ""));
                oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(StatTile("Server time",
                    FmtInt(aDurationMS) + " ms", "time the database took",
                    TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, ""));
                oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(StatTile("Round trips", "1",
                    "one HTTP request, zero API calls", TsgcHTMLStatColor.scInfo,
                    TsgcHTMLStatTrend.stNone, ""));
                oRoot.AddRaw(oRow.HTML);

                // Chart, when the definition asks for one. The first text column is
                // the label, the last numeric column the measure.
                if ((aDef.ChartKind ?? "").Trim() != "")
                {
                    string vLabelField = "";
                    string vValueField = "";
                    DataTable oTable = oQ.DataSet;
                    for (int vI = 0; vI < oTable.Columns.Count; vI++)
                    {
                        if ((vLabelField == "") && !FieldIsNumeric(oTable.Columns[vI]))
                            vLabelField = oTable.Columns[vI].ColumnName;
                        if (FieldIsNumeric(oTable.Columns[vI]))
                            vValueField = oTable.Columns[vI].ColumnName;
                    }
                    if ((vLabelField != "") && (vValueField != ""))
                    {
                        TsgcHTMLChartType vChartType;
                        if (string.Equals(aDef.ChartKind, "pie",
                            StringComparison.OrdinalIgnoreCase))
                            vChartType = TsgcHTMLChartType.ctPie;
                        else if (string.Equals(aDef.ChartKind, "doughnut",
                            StringComparison.OrdinalIgnoreCase))
                            vChartType = TsgcHTMLChartType.ctDoughnut;
                        else if (string.Equals(aDef.ChartKind, "line",
                            StringComparison.OrdinalIgnoreCase))
                            vChartType = TsgcHTMLChartType.ctLine;
                        else
                            vChartType = TsgcHTMLChartType.ctBar;
                        TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
                        oChart.ChartID = "rpResultChart";
                        oChart.ChartType = vChartType;
                        oChart.CSSHeight = "320px";
                        oChart.Title = aDef.Name;
                        // The SAME open query feeds the chart; the grid below sees
                        // every row too.
                        oChart.LoadFromDataSet(oTable, vLabelField, vValueField);
                        if (oChart.Datasets.Count > 0)
                        {
                            oChart.Datasets.Items(0).BorderColor = ChartColor(0);
                            oChart.Datasets.Items(0).BackgroundColor = ChartColor(0);
                        }
                        oRoot.AddRaw(SectionCard("Chart", "", oChart.HTML));
                    }
                }

                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "rpResultGrid";
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = true;
                oGrid.ShowSort = true;
                oGrid.ShowFilter = true;
                oGrid.ShowColumnsMenu = true;
                oGrid.ColumnsText = "Columns";
                oGrid.SavedViews = true;
                oGrid.PersistKey = "rpReport" +
                    aDef.Id.ToString(CultureInfo.InvariantCulture);
                oGrid.HeaderClass = "table-light";
                oGrid.EmptyText = "No rows.";
                if (RoleCanExport(aCtx.Role))
                {
                    oGrid.ExportXLSX = true;
                    oGrid.ExportURL = "/reports/" +
                        aDef.Id.ToString(CultureInfo.InvariantCulture) + "/run.xlsx";
                    oGrid.ExportXLSXText = "Excel";
                    oGrid.ExportPDF = true;
                    oGrid.ExportPDFURL = "/reports/" +
                        aDef.Id.ToString(CultureInfo.InvariantCulture) + "/run.pdf";
                }
                GridLoadFormatted(oGrid, oQ.DataSet);
                oRoot.AddRaw(SectionCard("Result", FmtInt(aRowCount) + " rows in " +
                    FmtInt(aDurationMS) + " ms of server time.", oGrid.HTML));

                oRoot.AddRaw(SectionCard("The query that produced this",
                    "Parameters are bound, never concatenated. This is the whole data " +
                    "path.", SQLBlock(aDef.SqlText)));

                return oRoot.HTML;
            }
        }

        // ==================================================================== //
        //  inline PDF preview                                                  //
        // ==================================================================== //

        public string BuildReportPreview(TReportsPageCtx aCtx, TReportsReportDef aDef,
            List<TReportsParamValue> aValues)
        {
            string vQS = "";
            for (int vI = 0; vI < aValues.Count; vI++)
                if ((aValues[vI].Value ?? "").Trim() != "")
                    vQS = vQS + "&" + aValues[vI].Name + "=" +
                        Uri.EscapeDataString(aValues[vI].Value);
            if (vQS != "")
                vQS = "?" + vQS.Substring(1);

            string vId = aDef.Id.ToString(CultureInfo.InvariantCulture);
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(InfoAlert("The document below was generated a moment ago " +
                "by TsgcHTMLExportPDF, inside this server process, and is being read " +
                "back by the PDFViewer component on the same screen. Generator and " +
                "viewer, one screen, no third-party reporting tool."));

            TsgcHTMLComponent_PDFViewer oViewer = new TsgcHTMLComponent_PDFViewer();
            oViewer.ViewerID = "rpPreview";
            oViewer.PDFURL = "/reports/" + vId + "/run.pdf" + vQS;
            oViewer.CSSHeight = "720px";
            oViewer.Zoom = "page-width";
            oViewer.ShowToolbar = true;
            oViewer.ShowPageNav = true;
            oViewer.ShowZoomControls = true;
            oViewer.ShowSearch = true;
            oViewer.ShowPrint = true;
            oViewer.ShowDownload = true;
            oViewer.DownloadFileName = aDef.Name + ".pdf";
            // Point the viewer's pdf.js dependency at this server rather than at a
            // CDN, so the rendered page carries no external URL at all. The server
            // serves the library from assets\ when a copy is there, and redirects
            // to the public build when it is not.
            oViewer.PDFJSLibURL = "/pdf.min.js";
            oViewer.PDFJSWorkerURL = "/pdf.worker.min.js";
            oRoot.AddRaw(SectionCard("Preview", "", oViewer.HTML));

            oRoot.AddRaw(SectionCard("If the viewer cannot render",
                "The PDFViewer component needs pdf.js. This server offers it at " +
                "/pdf.min.js: drop pdf.min.js and pdf.worker.min.js into the assets " +
                "folder next to the executable and the viewer works with no internet " +
                "at all. Without them the request is redirected to the public build, " +
                "and on a machine with no connection the toolbar says so. The " +
                "document itself is always there, generated by this process:",
                "<a class=\"btn btn-outline-danger\" href=\"/reports/" + vId +
                "/run.pdf" + HtmlEsc(vQS) + "\">Open the PDF</a>" +
                " <a class=\"btn btn-link\" href=\"/reports/" + vId +
                "\">Back to the parameters</a>"));

            return BuildPageShell(aCtx, aDef.Name + " - preview",
                "Server-generated PDF, rendered inline.", oRoot.HTML, "reports");
        }

        // ==================================================================== //
        //  report editor (admin)                                               //
        // ==================================================================== //

        public string BuildReportEditor(TReportsPageCtx aCtx, TReportsReportDef aDef,
            string aError)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(InfoAlert("This is the one place in the portal where raw " +
                "SQL is accepted, and it is restricted to administrators. The " +
                "statement is rejected unless it is a single read-only SELECT, it is " +
                "executed on a connection the SQLite driver opened read-only, and " +
                "every parameter is bound rather than concatenated.",
                TsgcHTMLAlertStyle.asWarning));

            if (aError != "")
                oRoot.AddRaw(InfoAlert(aError, TsgcHTMLAlertStyle.asDanger));

            TsgcHTMLComponent_Stepper oStepper = new TsgcHTMLComponent_Stepper();
            oStepper.StepperID = "rpNewSteps";
            oStepper.Layout = TsgcHTMLStepperLayout.slHorizontal;
            oStepper.ShowContent = false;
            oStepper.CurrentColor = CS_ACCENT;
            TsgcHTMLStepItem oStep = oStepper.Items.Add();
            oStep.Title = "Describe";
            oStep.Description = "Name and category";
            oStep.State = TsgcHTMLStepState.ssCompleted;
            oStep = oStepper.Items.Add();
            oStep.Title = "Write the SELECT";
            oStep.Description = "Bound :parameters only";
            oStep.State = TsgcHTMLStepState.ssCurrent;
            oStep = oStepper.Items.Add();
            oStep.Title = "Declare parameters";
            oStep.Description = "params_json";
            oStep.State = TsgcHTMLStepState.ssUpcoming;
            oStep = oStepper.Items.Add();
            oStep.Title = "Run it";
            oStep.Description = "From the catalogue";
            oStep.State = TsgcHTMLStepState.ssUpcoming;
            oRoot.AddRaw("<div class=\"mb-4\">" + oStepper.HTML + "</div>");

            TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "rpReportForm";
            oForm.Action = "/reports/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = "Save report";
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;
            oForm.ShowReset = true;

            TsgcHTMLFormField oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftHidden;
            oFld.Name = "id";
            oFld.Value = aDef.Id.ToString(CultureInfo.InvariantCulture);
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftText;
            oFld.Name = "name";
            oFld.Label_ = "Report name";
            oFld.Value = aDef.Name;
            oFld.Required = true;
            oFld.ColSpan = 6;
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftText;
            oFld.Name = "category";
            oFld.Label_ = "Category";
            oFld.Value = aDef.Category;
            oFld.HelpText = "Sales, Margin, Operations, Customers...";
            oFld.ColSpan = 6;
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftText;
            oFld.Name = "description";
            oFld.Label_ = "Description";
            oFld.Value = aDef.Description;
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftTextArea;
            oFld.Name = "sql_text";
            oFld.Label_ = "SELECT statement (admin only)";
            oFld.Value = aDef.SqlText;
            oFld.TextAreaRows = 10;
            oFld.Required = true;
            oFld.HelpText = "One statement. Use :name for every parameter.";
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftTextArea;
            oFld.Name = "params_json";
            oFld.Label_ = "Parameter declarations (JSON)";
            oFld.Value = aDef.ParamsJSON;
            oFld.TextAreaRows = 5;
            oFld.HelpText =
                "{\"params\":[{\"name\":\"dfrom\",\"caption\":\"From\",\"kind\":\"date\"," +
                "\"default\":\"-365\"}]}";
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftSelect;
            oFld.Name = "chart_kind";
            oFld.Label_ = "Chart";
            oFld.Value = aDef.ChartKind;
            oFld.Options.Add("=(none)");
            oFld.Options.Add("bar=Bar");
            oFld.Options.Add("line=Line");
            oFld.Options.Add("pie=Pie");
            oFld.Options.Add("doughnut=Doughnut");
            oFld.ColSpan = 6;
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftSwitch;
            oFld.Name = "shared";
            oFld.Label_ = "Shared with everyone";
            oFld.Value = "on";
            oFld.ColSpan = 6;

            oRoot.AddRaw(SectionCard("Report definition", "", oForm.HTML));

            TsgcHTMLComponent_ListGroup oList = new TsgcHTMLComponent_ListGroup();
            oList.Numbered = true;
            oList.AddItem("The statement must start with SELECT or WITH", "",
                "enforced", TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Only one statement: an inner semicolon is rejected", "",
                "enforced", TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("INSERT, UPDATE, DELETE, DROP, PRAGMA, ATTACH and their " +
                "friends are rejected as whole words", "", "enforced",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Keywords hidden inside string literals or comments do " +
                "not fool the check: literals are blanked first", "", "enforced",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("The statement runs on a connection the SQLite driver " +
                "opened in read-only mode", "", "enforced",
                TsgcHTMLBadgeStyle.bgSuccess);
            oList.AddItem("Parameters are bound through command parameters, never " +
                "concatenated into the text", "", "enforced",
                TsgcHTMLBadgeStyle.bgSuccess);
            oRoot.AddRaw(SectionCard("What the server checks before it runs this", "",
                oList.HTML));

            return BuildPageShell(aCtx, "Report builder",
                "Admin only. Raw SQL is accepted here and nowhere else.", oRoot.HTML,
                "reports");
        }

        // ==================================================================== //
        //  pivot builder                                                       //
        // ==================================================================== //

        // The ad-hoc pivot always reads the same shaped query; only the grouping
        // and the measure change, and all three are picked from a fixed whitelist.
        private static string PivotFieldSQL(string aKey)
        {
            if (string.Equals(aKey, "region", StringComparison.OrdinalIgnoreCase))
                return "r.name";
            if (string.Equals(aKey, "segment", StringComparison.OrdinalIgnoreCase))
                return "c.segment";
            if (string.Equals(aKey, "category", StringComparison.OrdinalIgnoreCase))
                return "cat.name";
            if (string.Equals(aKey, "salesperson", StringComparison.OrdinalIgnoreCase))
                return "s.name";
            if (string.Equals(aKey, "status", StringComparison.OrdinalIgnoreCase))
                return "o.status";
            if (string.Equals(aKey, "year", StringComparison.OrdinalIgnoreCase))
                return "substr(o.order_date, 1, 4)";
            if (string.Equals(aKey, "quarter", StringComparison.OrdinalIgnoreCase))
                return "'Q' || ((CAST(substr(o.order_date, 6, 2) AS INTEGER) + 2) / 3)";
            return "substr(o.order_date, 1, 7)";
        }

        private static string PivotMeasureSQL(string aKey)
        {
            if (string.Equals(aKey, "qty", StringComparison.OrdinalIgnoreCase))
                return "ol.qty";
            if (string.Equals(aKey, "margin", StringComparison.OrdinalIgnoreCase))
                return "ROUND(ol.line_total - ol.qty * p.unit_cost, 2)";
            if (string.Equals(aKey, "discount", StringComparison.OrdinalIgnoreCase))
                return "ROUND(ol.qty * ol.unit_price * ol.discount, 2)";
            return "ROUND(ol.line_total, 2)";
        }

        private static TsgcHTMLPivotAggregation PivotAgg(string aKey)
        {
            if (string.Equals(aKey, "count", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLPivotAggregation.paCount;
            if (string.Equals(aKey, "avg", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLPivotAggregation.paAverage;
            if (string.Equals(aKey, "min", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLPivotAggregation.paMin;
            if (string.Equals(aKey, "max", StringComparison.OrdinalIgnoreCase))
                return TsgcHTMLPivotAggregation.paMax;
            return TsgcHTMLPivotAggregation.paSum;
        }

        public string BuildPivotFragment(string aRowField, string aColField,
            string aMeasure, string aAgg, string aFrom, string aTo,
            out int aDurationMS)
        {
            aDurationMS = 0;
            string vSQL = "SELECT " + PivotFieldSQL(aRowField) + " AS rowdim, " +
                PivotFieldSQL(aColField) + " AS coldim, " + PivotMeasureSQL(aMeasure) +
                " AS measure" + " FROM order_lines ol " +
                "JOIN orders o ON o.id = ol.order_id " +
                "JOIN customers c ON c.id = o.customer_id " +
                "JOIN regions r ON r.id = c.region_id " +
                "JOIN salespeople s ON s.id = o.salesperson_id " +
                "JOIN products p ON p.id = ol.product_id " +
                "JOIN categories cat ON cat.id = p.category_id " +
                "WHERE o.status <> 'Cancelled'";
            if (ReportsDBHelpers.IsISODate(aFrom))
                vSQL = vSQL + " AND o.order_date >= :dfrom";
            if (ReportsDBHelpers.IsISODate(aTo))
                vSQL = vSQL + " AND o.order_date <= :dto";

            using (TReportsQuery oQ = FDB.NewQuery(vSQL))
            {
                if (ReportsDBHelpers.IsISODate(aFrom))
                    oQ.SetParamStr("dfrom", aFrom);
                if (ReportsDBHelpers.IsISODate(aTo))
                    oQ.SetParamStr("dto", aTo);
                oQ.Open();
                aDurationMS = oQ.ElapsedMS;

                TsgcHTMLComponent_PivotTable oPivot = new TsgcHTMLComponent_PivotTable();
                oPivot.TableID = "rpPivotTable";
                oPivot.Compact = true;
                oPivot.ShowRowTotals = true;
                oPivot.ShowColumnTotals = true;
                oPivot.ShowGrandTotal = true;
                oPivot.EmptyCellText = "-";
                oPivot.Caption = PrettyColumn(aRowField) + " by " +
                    PrettyColumn(aColField);
                TsgcHTMLPivotField oRowField = oPivot.RowFields.Add();
                oRowField.FieldName = "rowdim";
                oRowField.Caption = PrettyColumn(aRowField);
                TsgcHTMLPivotField oColField = oPivot.ColumnFields.Add();
                oColField.FieldName = "coldim";
                oColField.Caption = PrettyColumn(aColField);
                TsgcHTMLPivotMeasure oMeasure = oPivot.Measures.Add();
                oMeasure.SourceField = "measure";
                oMeasure.Caption = PrettyColumn(aMeasure);
                oMeasure.Aggregation = PivotAgg(aAgg);
                oMeasure.Format = "#,##0";
                oPivot.LoadFromDataSet(oQ.DataSet);
                return "<div class=\"table-responsive\">" + oPivot.HTML + "</div>" +
                    "<p class=\"small text-muted mt-2 mb-0\">" +
                    HtmlEsc(FmtInt(oQ.RecordCount)) + " order lines aggregated, " +
                    HtmlEsc(FmtInt(aDurationMS)) + " ms of server time.</p>";
            }
        }

        public string BuildPivot(TReportsPageCtx aCtx, string aRowField,
            string aColField, string aMeasure, string aAgg, string aFrom, string aTo)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/pivot/build";
            oForm.FormID = "rpPivotForm";
            oForm.Attributes = "hx-post=\"/pivot/build\" hx-target=\"#rpPivotOut\" " +
                "hx-swap=\"innerHTML\"";
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 align-items-end";

            AddPivotSelect(oRow, "rowfield", "Rows",
                new string[] { "region", "segment", "category", "salesperson", "status" },
                new string[] { "Region", "Segment", "Category", "Salesperson", "Status" },
                aRowField, TsgcHTMLColWidth.cw2);
            AddPivotSelect(oRow, "colfield", "Columns",
                new string[] { "year", "quarter", "month", "segment", "status" },
                new string[] { "Year", "Quarter", "Month", "Segment", "Status" },
                aColField, TsgcHTMLColWidth.cw2);
            AddPivotSelect(oRow, "measure", "Measure",
                new string[] { "revenue", "margin", "qty", "discount" },
                new string[] { "Revenue", "Margin", "Units", "Discount given" },
                aMeasure, TsgcHTMLColWidth.cw2);
            AddPivotSelect(oRow, "agg", "Aggregation",
                new string[] { "sum", "avg", "count", "min", "max" },
                new string[] { "Sum", "Average", "Count", "Minimum", "Maximum" },
                aAgg, TsgcHTMLColWidth.cw2);

            TsgcHTMLCol oCol = oRow.Col(TsgcHTMLColWidth.cw3);
            TsgcHTMLComponent_DateRangePicker oRange =
                new TsgcHTMLComponent_DateRangePicker();
            oRange.FieldNameStart = "dfrom";
            oRange.FieldNameEnd = "dto";
            oRange.LabelStart = TReportsI18n.T(aCtx.Lang, "lbl.from");
            oRange.LabelEnd = TReportsI18n.T(aCtx.Lang, "lbl.to");
            oRange.StartValue = aFrom;
            oRange.EndValue = aTo;
            oRange.ShowPresets = true;
            oRange.Layout = TsgcHTMLDateRangeLayout.drlInline;
            oCol.AddRaw(oRange.HTML);

            oCol = oRow.Col(TsgcHTMLColWidth.cw1);
            TsgcHTMLButton oBtn = new TsgcHTMLButton(
                TReportsI18n.T(aCtx.Lang, "btn.apply"), TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100";
            oCol.Add(oBtn);

            oForm.Add(oRow);
            oRoot.AddRaw(SectionCard("Build a cross-tab",
                "Pick the two dimensions and the measure. Every choice is a fixed " +
                "key mapped to a fixed SQL expression: nothing you type reaches " +
                "the statement.", oForm.HTML));

            string vQS = BuildQS("rowfield", aRowField, "colfield", aColField,
                "measure", aMeasure, "agg", aAgg, "dfrom", aFrom, "dto", aTo);
            if (RoleCanExport(aCtx.Role))
                oRoot.AddRaw(SectionCard("Export this cross-tab", "",
                    "<a class=\"btn btn-outline-danger btn-sm me-2\" " +
                    "href=\"/pivot/export.pdf" + HtmlEsc(vQS) + "\">PDF</a>" +
                    "<a class=\"btn btn-outline-success btn-sm\" " +
                    "href=\"/pivot/export.xlsx" + HtmlEsc(vQS) + "\">Excel</a>"));

            int vMS;
            oRoot.AddRaw("<div id=\"rpPivotOut\">" + SectionCard("Cross-tab", "",
                BuildPivotFragment(aRowField, aColField, aMeasure, aAgg, aFrom, aTo,
                out vMS)) + "</div>");

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "ttl.pivot"),
                "Cross-tab any two dimensions over the order lines.", oRoot.HTML,
                "pivot");
        }

        private static void AddPivotSelect(TsgcHTMLRow aRow, string aName,
            string aLabel, string[] aValues, string[] aLabels, string aCurrent,
            TsgcHTMLColWidth aWidth)
        {
            TsgcHTMLCol oCol = aRow.Col(aWidth);
            TsgcHTMLComponent_Select oSelect = new TsgcHTMLComponent_Select();
            oSelect.SelectID = "rpPv_" + aName;
            oSelect.ElementName = aName;
            oSelect.Label_ = aLabel;
            for (int vI = 0; vI < aValues.Length; vI++)
                oSelect.AddOption(aValues[vI], aLabels[vI],
                    string.Equals(aCurrent, aValues[vI],
                        StringComparison.OrdinalIgnoreCase));
            oCol.AddRaw(oSelect.HTML);
        }

        // ==================================================================== //
        //  explore: 25k rows, paged / sorted / filtered in SQL                 //
        // ==================================================================== //

        /// <summary>
        /// Virtual-scroll continuation: the &lt;tr&gt; rows plus a fresh sentinel row.
        /// </summary>
        public string BuildExploreRowsFragment(TReportsExploreFilter aFilter,
            int aOffset, int aLimit, string aRootId)
        {
            string vWrap = aRootId;
            if ((vWrap ?? "").Trim() == "")
                vWrap = CS_EXPLORE_WRAP;
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            using (TReportsQuery oQ = FDB.NewExploreQuery(aFilter, aOffset, aLimit))
            {
                DataTable oTable = oQ.DataSet;
                int vFields = oTable.Columns.Count;
                for (int vR = 0; vR < oTable.Rows.Count; vR++)
                {
                    TsgcHTMLTableRow oRow = new TsgcHTMLTableRow();
                    for (int vI = 0; vI < vFields; vI++)
                        oRow.AddCellText(CellText(oTable.Rows[vR], vI));
                    oRoot.AddRaw(oRow.HTML);
                }

                // A fresh sentinel for the next page, unless this page was short.
                if (oQ.RecordCount >= aLimit)
                {
                    // BuildQS returns '?a=b&c=d'; this URL already has a query string,
                    // so its leading '?' becomes an '&'.
                    string vFilterQS = ExploreQS(aFilter, 0);
                    if (vFilterQS != "")
                        vFilterQS = "&" + vFilterQS.Substring(1);
                    TsgcHTMLContainer oSentinel = new TsgcHTMLContainer("tr");
                    oSentinel.Attributes = "hx-get=\"/explore/rows?offset=" +
                        (aOffset + aLimit).ToString(CultureInfo.InvariantCulture) +
                        "&limit=" + aLimit.ToString(CultureInfo.InvariantCulture) +
                        "&rootId=" + HtmlEsc(vWrap) + HtmlEsc(vFilterQS) +
                        "\" hx-trigger=\"intersect root:#" + HtmlEsc(vWrap) +
                        "\" hx-swap=\"outerHTML\"";
                    TsgcHTMLContainer oCell = new TsgcHTMLContainer("td");
                    oCell.Attributes = "colspan=\"" +
                        vFields.ToString(CultureInfo.InvariantCulture) +
                        "\" class=\"text-center text-muted small\"";
                    oCell.AddText("Loading more rows...");
                    oSentinel.Add(oCell);
                    oRoot.AddRaw(oSentinel.HTML);
                }
                return oRoot.HTML;
            }
        }

        public string BuildExplore(TReportsPageCtx aCtx, TReportsExploreFilter aFilter,
            int aPage, int aPageSize, bool aVirtual)
        {
            if (aPageSize <= 0)
                aPageSize = 50;
            if (aPageSize > 500)
                aPageSize = 500;
            if (aPage < 1)
                aPage = 1;

            // Both halves of the work are timed separately, and both are shown.
            DateTime vStart = DateTime.Now;
            int vTotal = FDB.ExploreCount(aFilter);
            int vCountMS = (int)Math.Round((DateTime.Now - vStart).TotalMilliseconds);

            int vTotalPages = 1;
            if (vTotal > 0)
                vTotalPages = (vTotal + aPageSize - 1) / aPageSize;
            if (aPage > vTotalPages)
                aPage = vTotalPages;
            int vOffset = (aPage - 1) * aPageSize;

            string vQS = ExploreQS(aFilter, aPageSize);

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            // ----- filter bar ----- //
            TsgcHTMLForm oForm = new TsgcHTMLForm();
            oForm.Method = "GET";
            oForm.Action = "/explore";
            oForm.FormID = "rpExploreForm";
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-2 align-items-end";

            TsgcHTMLCol oCol = oRow.Col(TsgcHTMLColWidth.cw3);
            TsgcHTMLField oField = new TsgcHTMLField(TsgcHTMLInputType.itSearch,
                "search");
            oField.FieldID = "rpEx_search";
            oField.Label_ = TReportsI18n.T(aCtx.Lang, "lbl.search");
            oField.Placeholder = "Product, SKU or customer";
            oField.Value = aFilter.Search;
            oCol.AddRaw(oField.HTML);

            AddLookup(oRow, "region", TReportsI18n.T(aCtx.Lang, "lbl.region"),
                FDB.ListRegions(), aFilter.Region, TsgcHTMLColWidth.cw2);
            AddLookup(oRow, "category", TReportsI18n.T(aCtx.Lang, "lbl.category"),
                FDB.ListCategories(), aFilter.Category, TsgcHTMLColWidth.cw2);
            AddLookup(oRow, "segment", TReportsI18n.T(aCtx.Lang, "lbl.segment"),
                FDB.ListSegments(), aFilter.Segment, TsgcHTMLColWidth.cw2);

            // Status is a MultiSelect: several states can be looked at at once and
            // each selected value becomes its own bound parameter in the IN list.
            oCol = oRow.Col(TsgcHTMLColWidth.cw3);
            TsgcHTMLComponent_MultiSelect oMulti = new TsgcHTMLComponent_MultiSelect();
            oMulti.MultiSelectID = "rpEx_status";
            oMulti.FieldName = "status";
            oMulti.Placeholder = "Status (all)";
            oMulti.ShowSearch = false;
            oMulti.Size = TsgcHTMLMultiSelectSize.mssSmall;
            List<string> vOptions = FDB.ListStatuses();
            for (int vI = 0; vI < vOptions.Count; vI++)
            {
                int vPos = vOptions[vI].IndexOf('|');
                if (vPos < 0)
                    continue;
                string vName = vOptions[vI].Substring(0, vPos);
                bool vSelected = false;
                for (int vJ = 0; vJ < aFilter.Statuses.Length; vJ++)
                    if (string.Equals(aFilter.Statuses[vJ], vName,
                        StringComparison.OrdinalIgnoreCase))
                        vSelected = true;
                oMulti.AddOption(vName, vOptions[vI].Substring(vPos + 1), vSelected);
            }
            oCol.AddRaw(oMulti.HTML);

            oCol = oRow.Col(TsgcHTMLColWidth.cw2);
            oField = new TsgcHTMLField(TsgcHTMLInputType.itDate, "dfrom");
            oField.FieldID = "rpEx_dfrom";
            oField.Label_ = TReportsI18n.T(aCtx.Lang, "lbl.from");
            oField.Value = aFilter.DateFrom;
            oCol.AddRaw(oField.HTML);

            oCol = oRow.Col(TsgcHTMLColWidth.cw2);
            oField = new TsgcHTMLField(TsgcHTMLInputType.itDate, "dto");
            oField.FieldID = "rpEx_dto";
            oField.Label_ = TReportsI18n.T(aCtx.Lang, "lbl.to");
            oField.Value = aFilter.DateTo;
            oCol.AddRaw(oField.HTML);

            AddLookup(oRow, "sort", TReportsI18n.T(aCtx.Lang, "lbl.sort"),
                new List<string>(new string[]
                {
                    "date|Order date", "customer|Customer", "region|Region",
                    "salesperson|Salesperson", "product|Product", "sku|SKU",
                    "category|Category", "qty|Quantity", "price|Unit price",
                    "discount|Discount", "total|Line total"
                }), aFilter.Sort, TsgcHTMLColWidth.cw2);
            AddLookup(oRow, "dir", TReportsI18n.T(aCtx.Lang, "lbl.dir"),
                new List<string>(new string[]
                {
                    "asc|" + TReportsI18n.T(aCtx.Lang, "lbl.asc"),
                    "desc|" + TReportsI18n.T(aCtx.Lang, "lbl.desc")
                }), aFilter.Dir, TsgcHTMLColWidth.cw2);
            AddLookup(oRow, "size", "Page size",
                new List<string>(new string[]
                    { "25|25", "50|50", "100|100", "250|250", "500|500" }),
                aPageSize.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLColWidth.cw2);

            // Quantity band: two bound parameters, ol.qty >= :minqty and <= :maxqty.
            oCol = oRow.Col(TsgcHTMLColWidth.cw3);
            TsgcHTMLComponent_RangeSlider oRange = new TsgcHTMLComponent_RangeSlider();
            oRange.RangeSliderID = "rpEx_qty";
            oRange.FieldNameLow = "qtylow";
            oRange.FieldNameHigh = "qtyhigh";
            oRange.Min = 1;
            oRange.Max = 20;
            oRange.Step = 1;
            oRange.ShowValues = true;
            if (aFilter.MinQty > 0)
                oRange.ValueLow = aFilter.MinQty;
            else
                oRange.ValueLow = 1;
            if (aFilter.MaxQty > 0)
                oRange.ValueHigh = aFilter.MaxQty;
            else
                oRange.ValueHigh = 20;
            oCol.AddRaw("<label class=\"form-label\">Quantity band</label>" +
                oRange.HTML);

            oCol = oRow.Col(TsgcHTMLColWidth.cw2);
            TsgcHTMLButton oBtn = new TsgcHTMLButton(
                TReportsI18n.T(aCtx.Lang, "btn.apply"), TsgcHTMLButtonStyle.bsPrimary);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100";
            oCol.Add(oBtn);

            oCol = oRow.Col(TsgcHTMLColWidth.cw2);
            oBtn = new TsgcHTMLButton(TReportsI18n.T(aCtx.Lang, "btn.reset"),
                TsgcHTMLButtonStyle.bsOutlineSecondary);
            oBtn.Href = "/explore";
            oBtn.CSSClass = "w-100";
            oCol.Add(oBtn);

            oForm.Add(oRow);
            oRoot.AddRaw(SectionCard("Filter",
                "Every predicate below is a bound parameter and every sort key is " +
                "whitelisted before it reaches the ORDER BY. The paging, the " +
                "sorting and the filtering all happen in SQL, not in the browser.",
                oForm.HTML));

            if (vTotal == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "Nothing matched that filter";
                oEmpty.Description = "Clear a field and try again.";
                oEmpty.Icon = CS_EMPTY_SVG;
                oEmpty.ActionCaption = "Clear the filter";
                oEmpty.ActionHref = "/explore";
                oEmpty.Bordered = true;
                oRoot.AddRaw(oEmpty.HTML);
                return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "ttl.explore"),
                    "0 rows matched in " + FmtInt(vCountMS) + " ms.", oRoot.HTML,
                    "explore");
            }

            // ----- the page of rows ----- //
            int vPageMS;
            using (TReportsQuery oQ = FDB.NewExploreQuery(aFilter, vOffset, aPageSize))
            {
                vPageMS = oQ.ElapsedMS;

                oRow = new TsgcHTMLRow();
                oRow.CSSClass = "g-3 mb-3";
                oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile(
                    "Rows matching the filter", FmtInt(vTotal), "counted in SQL",
                    TsgcHTMLStatColor.scPrimary, TsgcHTMLStatTrend.stNone, ""));
                oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile("Count query",
                    FmtInt(vCountMS) + " ms", "COUNT(*) over the join",
                    TsgcHTMLStatColor.scInfo, TsgcHTMLStatTrend.stNone, ""));
                oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile("Page query",
                    FmtInt(vPageMS) + " ms", "ORDER BY + LIMIT " +
                    aPageSize.ToString(CultureInfo.InvariantCulture) + " OFFSET " +
                    vOffset.ToString(CultureInfo.InvariantCulture),
                    TsgcHTMLStatColor.scSuccess, TsgcHTMLStatTrend.stNone, ""));
                oRow.Col(TsgcHTMLColWidth.cw3).AddRaw(StatTile("Page",
                    aPage.ToString(CultureInfo.InvariantCulture) + " / " +
                    vTotalPages.ToString(CultureInfo.InvariantCulture),
                    FmtInt(oQ.RecordCount) + " rows on this page",
                    TsgcHTMLStatColor.scDark, TsgcHTMLStatTrend.stNone, ""));
                oRoot.AddRaw(oRow.HTML);

                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = CS_EXPLORE_TABLE;
                oGrid.Striped = true;
                oGrid.Hover = true;
                oGrid.Responsive = !aVirtual;
                oGrid.HeaderClass = "table-light";
                oGrid.EmptyText = "No rows.";
                oGrid.ShowColumnsMenu = true;
                oGrid.ColumnsText = "Columns";
                oGrid.PersistKey = "rpExplore";
                if (aVirtual)
                {
                    oGrid.VirtualScroll = true;
                    oGrid.VisibleRows = aPageSize;
                    oGrid.VirtualScrollURL = "/explore/rows";
                }
                if (RoleCanExport(aCtx.Role))
                {
                    oGrid.ExportXLSX = true;
                    oGrid.ExportURL = "/explore/export.xlsx" + vQS;
                    oGrid.ExportXLSXText = "Excel";
                    oGrid.ExportPDF = true;
                    oGrid.ExportPDFURL = "/explore/export.pdf" + vQS;
                }
                GridLoadFormatted(oGrid, oQ.DataSet);
                string vGridHTML = oGrid.HTML;
                // The Grid builds its own virtual-scroll sentinel as
                // <VirtualScrollURL>?offset=..&limit=..&rootId=.., so the active
                // filter has to be appended to that URL here or the second page
                // would come back unfiltered.
                if (aVirtual)
                {
                    string vFilterQS = ExploreQS(aFilter, 0);
                    if (vFilterQS != "")
                        vGridHTML = vGridHTML.Replace(
                            "&rootId=" + CS_EXPLORE_WRAP + "\"",
                            "&rootId=" + CS_EXPLORE_WRAP + "&" +
                            vFilterQS.Substring(1) + "\"");
                }
                oRoot.AddRaw(SectionCard("Order lines", FmtInt(vTotal) +
                    " rows matched. This page was fetched in " + FmtInt(vPageMS) +
                    " ms of server time.", vGridHTML));

                // ----- pager ----- //
                if (!aVirtual)
                {
                    TsgcHTMLComponent_Pagination oPager =
                        new TsgcHTMLComponent_Pagination();
                    oPager.PaginationID = "rpExplorePager";
                    oPager.CurrentPage = aPage;
                    oPager.TotalPages = vTotalPages;
                    oPager.TotalItems = vTotal;
                    oPager.PageSize = aPageSize;
                    oPager.MaxVisible = 7;
                    oPager.ShowFirstLast = true;
                    oPager.ShowPrevNext = true;
                    oPager.ContentAlign = TsgcHTMLPaginationAlign.paCenter;
                    if (vQS == "")
                        oPager.BaseURL = "/explore?page=";
                    else
                        oPager.BaseURL = "/explore" + vQS + "&page=";
                    oRoot.AddRaw(oPager.HTML);
                    oRoot.AddRaw("<p class=\"text-center small text-muted\">" +
                        "<a href=\"/explore" + HtmlEsc(vQS) + "&virtual=1\">" +
                        "Switch to virtual scrolling</a> to load the same set as you " +
                        "scroll instead of page by page.</p>");
                }
                else
                    oRoot.AddRaw("<p class=\"text-center small text-muted\">" +
                        "<a href=\"/explore" + HtmlEsc(vQS) + "\">Switch back to paging" +
                        "</a>. Virtual scrolling fetches the next " +
                        aPageSize.ToString(CultureInfo.InvariantCulture) +
                        " rows from /explore/rows as the sentinel row comes into " +
                        "view.</p>");

                oRoot.AddRaw(SectionCard("The statement behind this page",
                    "This is verbatim what ran, with :lim and :off bound to " +
                    aPageSize.ToString(CultureInfo.InvariantCulture) + " and " +
                    vOffset.ToString(CultureInfo.InvariantCulture) + ".",
                    SQLBlock(FDB.ExploreSQLText(aFilter))));
            }

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "ttl.explore"),
                FmtInt(vTotal) + " rows matched, page fetched in " + FmtInt(vPageMS) +
                " ms.", oRoot.HTML, "explore");
        }

        private static void AddLookup(TsgcHTMLRow aRow, string aName, string aLabel,
            List<string> aList, string aCurrent, TsgcHTMLColWidth aWidth)
        {
            TsgcHTMLCol oCol = aRow.Col(aWidth);
            TsgcHTMLComponent_Select oSelect = new TsgcHTMLComponent_Select();
            oSelect.SelectID = "rpEx_" + aName;
            oSelect.ElementName = aName;
            oSelect.Label_ = aLabel;
            oSelect.AddOption("", "(all)", aCurrent == "");
            for (int vJ = 0; vJ < aList.Count; vJ++)
            {
                int vPos = aList[vJ].IndexOf('|');
                if (vPos < 0)
                    continue;
                string vValue = aList[vJ].Substring(0, vPos);
                oSelect.AddOption(vValue, aList[vJ].Substring(vPos + 1),
                    string.Equals(aCurrent, vValue, StringComparison.OrdinalIgnoreCase));
            }
            oCol.AddRaw(oSelect.HTML);
        }

        // ==================================================================== //
        //  drilldown                                                           //
        // ==================================================================== //

        public string BuildDrilldown(TReportsPageCtx aCtx, string aDim, long aId)
        {
            string vTitle = FDB.DrilldownTitle(aDim, aId);
            if (vTitle == "")
                vTitle = PrettyColumn(aDim) + " " +
                    aId.ToString(CultureInfo.InvariantCulture);

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            TsgcHTMLComponent_Breadcrumb oBread = new TsgcHTMLComponent_Breadcrumb();
            TsgcHTMLBreadcrumbItem oCrumb = oBread.Items.Add();
            oCrumb.Text = "Dashboard";
            oCrumb.Href = "/";
            oCrumb = oBread.Items.Add();
            oCrumb.Text = PrettyColumn(aDim);
            oCrumb.Href = "/dashboards/sales";
            oCrumb = oBread.Items.Add();
            oCrumb.Text = vTitle;
            oCrumb.Active = true;
            oRoot.AddRaw(oBread.HTML);

            using (TReportsQuery oQ = FDB.NewDrilldownQuery(aDim, aId))
            {
                TsgcHTMLComponent_TreeGrid oTree = new TsgcHTMLComponent_TreeGrid();
                oTree.TreeGridID = "rpDrill";
                oTree.Striped = true;
                oTree.Hover = true;
                oTree.Sortable = true;
                oTree.ExpandedByDefault = true;
                TsgcHTMLTreeGridColumn oCol = oTree.Columns.Add();
                oCol.FieldName = "label";
                oCol.Caption = PrettyColumn(aDim) + " detail";
                oCol = oTree.Columns.Add();
                oCol.FieldName = "detail";
                oCol.Caption = "Detail";
                oCol = oTree.Columns.Add();
                oCol.FieldName = "orders";
                oCol.Caption = "Count";
                oCol.Align = TsgcHTMLTreeGridAlign.tgaRight;
                oCol = oTree.Columns.Add();
                oCol.FieldName = "revenue";
                oCol.Caption = "Revenue";
                oCol.Align = TsgcHTMLTreeGridAlign.tgaRight;
                oTree.LoadFromDataSet(oQ.DataSet, "node_id", "parent_id");
                oRoot.AddRaw(SectionCard("Drilldown: " + vTitle,
                    "TreeGrid.LoadFromDataSet with an id and a parent-id column. " +
                    FmtInt(oQ.RecordCount) + " rows in " + FmtInt(oQ.ElapsedMS) +
                    " ms.", oTree.HTML));
                oRoot.AddRaw(SectionCard("The query", "", SQLBlock(oQ.SQLText)));
            }

            return BuildPageShell(aCtx, "Drilldown", vTitle, oRoot.HTML, "db.sales");
        }

        // ==================================================================== //
        //  saved views                                                         //
        // ==================================================================== //

        public string BuildViews(TReportsPageCtx aCtx)
        {
            List<TReportsSavedView> vViews = FDB.ListSavedViews(aCtx.UserId);
            List<TReportsReportDef> vDefs = FDB.ListReportDefs();

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            if (vViews.Count == 0)
            {
                TsgcHTMLComponent_EmptyState oEmpty = new TsgcHTMLComponent_EmptyState();
                oEmpty.Title = "No saved view yet";
                oEmpty.Description = "Save one below, then it appears here. Views " +
                    "are scoped to your own account: the query that lists them is " +
                    "filtered by the session's user id, never by an id from the URL.";
                oEmpty.Icon = CS_EMPTY_SVG;
                oEmpty.Bordered = true;
                oRoot.AddRaw(oEmpty.HTML);
            }
            else
            {
                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "rpViews";
                oGrid.Responsive = true;
                oGrid.HeaderClass = "table-light";
                TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
                oCol.Name = "name";
                oCol.Title = "View";
                oCol = oGrid.Columns.Add();
                oCol.Name = "report";
                oCol.Title = "Report";
                oCol = oGrid.Columns.Add();
                oCol.Name = "state";
                oCol.Title = "State";
                oCol = oGrid.Columns.Add();
                oCol.Name = "created";
                oCol.Title = "Saved";
                oCol = oGrid.Columns.Add();
                oCol.Name = "act";
                oCol.Title = "";
                for (int vI = 0; vI < vViews.Count; vI++)
                    oGrid.AddRow(vViews[vI].Name, vViews[vI].ReportName,
                        Truncate(vViews[vI].StateJSON, 70),
                        FmtDateTime(vViews[vI].CreatedAt), "");
                // The delete action is a real POST form per row, spliced into the
                // rendered table rather than being faked with a link.
                string vBody = oGrid.HTML;
                for (int vI = 0; vI < vViews.Count; vI++)
                    vBody = ReplaceFirst(vBody, "<td></td>",
                        "<td class=\"text-end\"><form method=\"POST\" " +
                        "action=\"/views/delete\" class=\"m-0\">" +
                        "<input type=\"hidden\" name=\"id\" value=\"" +
                        vViews[vI].Id.ToString(CultureInfo.InvariantCulture) + "\">" +
                        "<button type=\"submit\" class=\"btn btn-sm btn-outline-danger\">" +
                        HtmlEsc(TReportsI18n.T(aCtx.Lang, "btn.delete")) +
                        "</button></form></td>");
                oRoot.AddRaw(SectionCard("Your saved views",
                    vViews.Count.ToString(CultureInfo.InvariantCulture) + " saved.",
                    vBody));
            }

            TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "rpViewForm";
            oForm.Action = "/views/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flHorizontal;
            oForm.SubmitText = TReportsI18n.T(aCtx.Lang, "btn.save");
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;
            TsgcHTMLFormField oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftText;
            oFld.Name = "name";
            oFld.Label_ = "View name";
            oFld.Required = true;
            oFld.Placeholder = "Last quarter, Enterprise only";
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftSelect;
            oFld.Name = "report_id";
            oFld.Label_ = "Report";
            for (int vI = 0; vI < vDefs.Count; vI++)
                oFld.Options.Add(vDefs[vI].Id.ToString(CultureInfo.InvariantCulture) +
                    "=" + vDefs[vI].Name);
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftTextArea;
            oFld.Name = "state_json";
            oFld.Label_ = "State";
            oFld.TextAreaRows = 3;
            oFld.Value = "{\"dfrom\":\"" + ReportsDBHelpers.FormatReportsDate(
                DateTime.Today.AddDays(-90)) + "\",\"dto\":\"" +
                ReportsDBHelpers.FormatReportsDate(DateTime.Today) + "\"}";
            oFld.HelpText = "Whatever the page needs to restore itself.";
            oRoot.AddRaw(SectionCard("Save a view", "", oForm.HTML));

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "nav.views"),
                "Saved parameter sets, scoped to your account.", oRoot.HTML, "views");
        }

        private static string Truncate(string aValue, int aLength)
        {
            string vValue = aValue ?? "";
            if (vValue.Length <= aLength)
                return vValue;
            return vValue.Substring(0, aLength);
        }

        // StringReplace without rfReplaceAll: replaces the FIRST occurrence only.
        private static string ReplaceFirst(string aText, string aFrom, string aTo)
        {
            int vPos = aText.IndexOf(aFrom, StringComparison.Ordinal);
            if (vPos < 0)
                return aText;
            return aText.Substring(0, vPos) + aTo +
                aText.Substring(vPos + aFrom.Length);
        }

        // ==================================================================== //
        //  schedules                                                           //
        // ==================================================================== //

        public string BuildSchedules(TReportsPageCtx aCtx, string aFlash)
        {
            List<TReportsSchedule> vRows = FDB.ListSchedules();
            List<TReportsReportDef> vDefs = FDB.ListReportDefs();

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            if (aFlash != "")
            {
                TsgcHTMLComponent_Toast oToast = new TsgcHTMLComponent_Toast();
                oToast.ToastID = "rpSchedToast";
                oToast.Title = "Scheduler";
                oToast.Body = aFlash;
                oToast.ColorStyle = TsgcHTMLColor.hcSuccess;
                oToast.AutoHide = true;
                oToast.Delay = 6000;
                oRoot.AddRaw(TsgcHTMLComponent_Toast.BuildContainer(oToast.HTML,
                    TsgcHTMLToastPosition.tpTopEnd));
            }

            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "rpSchedules";
            oGrid.Responsive = true;
            oGrid.HeaderClass = "table-light";
            oGrid.EmptyText = "No schedule configured.";
            TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
            oCol.Name = "report";
            oCol.Title = "Report";
            oCol = oGrid.Columns.Add();
            oCol.Name = "cron";
            oCol.Title = "Cron";
            oCol = oGrid.Columns.Add();
            oCol.Name = "format";
            oCol.Title = "Format";
            oCol = oGrid.Columns.Add();
            oCol.Name = "to";
            oCol.Title = "Recipients";
            oCol = oGrid.Columns.Add();
            oCol.Name = "last";
            oCol.Title = "Last run";
            oCol = oGrid.Columns.Add();
            oCol.Name = "state";
            oCol.Title = "State";
            oCol = oGrid.Columns.Add();
            oCol.Name = "act";
            oCol.Title = "";

            List<string> vBadges = new List<string>();
            for (int vI = 0; vI < vRows.Count; vI++)
            {
                // The state and format cells carry real Badge components. The Grid
                // escapes its cell text, so the markup is spliced in afterwards
                // through the same placeholder pass the action column uses.
                string vState;
                if (vRows[vI].Active)
                    vState = TsgcHTMLComponent_Badge.Build("Active",
                        TsgcHTMLBadgeStyle.bgSuccess, true);
                else
                    vState = TsgcHTMLComponent_Badge.Build("Paused",
                        TsgcHTMLBadgeStyle.bgSecondary, true);
                vBadges.Add(TsgcHTMLComponent_Badge.Build(
                    vRows[vI].Format.ToUpperInvariant(), TsgcHTMLBadgeStyle.bgInfo,
                    false));
                vBadges.Add(vState);
                oGrid.AddRow(vRows[vI].ReportName, vRows[vI].CronText, CS_BADGE_SLOT,
                    vRows[vI].Recipients, FmtDateTime(vRows[vI].LastRunAt),
                    CS_BADGE_SLOT, "");
            }
            string vBody = oGrid.HTML;
            // Replace each placeholder cell, in order, with its rendered Badge.
            for (int vI = 0; vI < vBadges.Count; vI++)
                vBody = ReplaceFirst(vBody, CS_BADGE_SLOT, vBadges[vI]);
            for (int vI = 0; vI < vRows.Count; vI++)
            {
                string vActions = "<td class=\"text-end\"><form method=\"POST\" " +
                    "action=\"/schedules/run-now\" class=\"m-0\">" +
                    "<input type=\"hidden\" name=\"id\" value=\"" +
                    vRows[vI].Id.ToString(CultureInfo.InvariantCulture) + "\">" +
                    "<button type=\"submit\" class=\"btn btn-sm btn-primary\">" +
                    HtmlEsc(TReportsI18n.T(aCtx.Lang, "btn.runnow")) +
                    "</button></form></td>";
                vBody = ReplaceFirst(vBody, "<td></td>", vActions);
            }
            oRoot.AddRaw(SectionCard("Schedules",
                "Run one now and watch it progress on the Jobs page: the export " +
                "thread pushes its progress over the WebSocket.", vBody));

            if (RoleCanExport(aCtx.Role))
            {
                TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
                oForm.FormID = "rpSchedForm";
                oForm.Action = "/schedules/save";
                oForm.Method = TsgcHTMLFormMethod.fmPost;
                oForm.Layout = TsgcHTMLFormLayout.flVertical;
                oForm.SubmitText = TReportsI18n.T(aCtx.Lang, "btn.save");
                oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;
                TsgcHTMLFormField oFld = oForm.Fields.Add();
                oFld.FieldType = TsgcHTMLFieldType.ftHidden;
                oFld.Name = "id";
                oFld.Value = "0";
                oFld = oForm.Fields.Add();
                oFld.FieldType = TsgcHTMLFieldType.ftSelect;
                oFld.Name = "report_id";
                oFld.Label_ = "Report";
                oFld.ColSpan = 4;
                for (int vI = 0; vI < vDefs.Count; vI++)
                    oFld.Options.Add(vDefs[vI].Id.ToString(CultureInfo.InvariantCulture) +
                        "=" + vDefs[vI].Name);
                oFld = oForm.Fields.Add();
                oFld.FieldType = TsgcHTMLFieldType.ftText;
                oFld.Name = "cron_text";
                oFld.Label_ = "Cron";
                oFld.Value = "0 7 * * 1";
                oFld.ColSpan = 3;
                oFld = oForm.Fields.Add();
                oFld.FieldType = TsgcHTMLFieldType.ftSelect;
                oFld.Name = "format";
                oFld.Label_ = "Format";
                oFld.ColSpan = 2;
                oFld.Options.Add("pdf=PDF");
                oFld.Options.Add("xlsx=Excel");
                oFld.Options.Add("csv=CSV");
                oFld = oForm.Fields.Add();
                oFld.FieldType = TsgcHTMLFieldType.ftText;
                oFld.Name = "recipients";
                oFld.Label_ = "Recipients";
                oFld.Placeholder = "someone@example.com";
                oFld.ColSpan = 3;
                oFld = oForm.Fields.Add();
                oFld.FieldType = TsgcHTMLFieldType.ftSwitch;
                oFld.Name = "active";
                oFld.Label_ = "Active";
                oFld.Value = "on";
                oRoot.AddRaw(SectionCard("Add a schedule", "", oForm.HTML));
            }

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "nav.schedules"),
                "Unattended exports, and the live progress of the ones running now.",
                oRoot.HTML, "schedules");
        }

        // ==================================================================== //
        //  jobs (realtime)                                                     //
        // ==================================================================== //

        public string BuildJobs(TReportsPageCtx aCtx)
        {
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(InfoAlert("Everything on this page is pushed. The export " +
                "thread renders one JobProgress row or one LogViewer line and sends " +
                "it over the sgcHTMX WebSocket bridge as an out-of-band swap. Start " +
                "an export from Schedules and watch it arrive here without a reload."));
            TReportsLive oLive = gLive;
            if (oLive != null)
                oRoot.AddRaw(oLive.RenderPanels());
            else
                oRoot.AddRaw(InfoAlert("The realtime engine is not running in this " +
                    "build.", TsgcHTMLAlertStyle.asWarning));

            TsgcHTMLComponent_Panel oPanel = new TsgcHTMLComponent_Panel();
            oPanel.PanelID = "rpWiring";
            oPanel.Title = "How this is wired";
            oPanel.UseColorClass = false;
            oPanel.Collapsible = true;
            oPanel.Expanded = true;
            oPanel.CSSClass = "shadow-sm mb-4";
            oPanel.Footer = "Shutdown order is terminate-and-wait the thread " +
                "first, free the engine second.";
            oPanel.Body = "<p class=\"text-muted small\">The push components live " +
                "in a locked singleton owned by the Pages unit. The server only " +
                "ever exchanges strings with it, and every broadcast write is " +
                "guarded.</p>" + CodeBlock(
                "private void PushLoop()\r\n" +
                "{\r\n" +
                "    while (!FTerminated)\r\n" +
                "    {\r\n" +
                "        string vFragment = TReportsPages.LiveTick();\r\n" +
                "        if (vFragment != \"\")\r\n" +
                "            FHTMXEngine.BroadcastFragment(vFragment);\r\n" +
                "        Thread.Sleep(900);\r\n" +
                "    }\r\n" +
                "}\r\n\r\n" +
                "// every write is guarded exactly like the framework does\r\n" +
                "if (FHTMXEngine != null && FHTTP != null && FHTTP.Active)\r\n" +
                "    try { FHTMXEngine.BroadcastFragment(aHTML); } catch { }");
            oRoot.AddRaw(oPanel.HTML);

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "ttl.jobs"),
                "JobProgress and LogViewer, pushed over the WebSocket.", oRoot.HTML,
                "jobs", true);
        }

        // ==================================================================== //
        //  users (admin)                                                       //
        // ==================================================================== //

        public string BuildUsers(TReportsPageCtx aCtx, string aError)
        {
            List<TReportsUser> vUsers = FDB.ListUsers();
            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            if (aError != "")
                oRoot.AddRaw(InfoAlert(aError, TsgcHTMLAlertStyle.asDanger));

            TsgcHTMLComponent_UserManagement oUsers =
                new TsgcHTMLComponent_UserManagement();
            oUsers.TableID = "rpUsers";
            oUsers.ShowSearch = true;
            oUsers.ShowAddButton = false;
            oUsers.ShowActions = false;
            oUsers.ShowLastLogin = false;
            oUsers.EmptyText = "No user.";
            for (int vI = 0; vI < vUsers.Count; vI++)
            {
                TsgcHTMLUser oUser = oUsers.AddUser(
                    vUsers[vI].Id.ToString(CultureInfo.InvariantCulture),
                    vUsers[vI].Username, vUsers[vI].Username + "@example.com");
                oUser.DisplayName = vUsers[vI].DisplayName;
                oUser.Roles = vUsers[vI].Role;
                oUser.Status = TsgcHTMLUserStatus.usActive;
            }
            oRoot.AddRaw(SectionCard("Users",
                "UserManagement, filled from the users table.", oUsers.HTML));

            TsgcHTMLComponent_RolesPermissions oRoles =
                new TsgcHTMLComponent_RolesPermissions();
            oRoles.MatrixID = "rpRoles";
            oRoles.ReadOnly = true;
            oRoles.ShowCategories = true;
            oRoles.AddRole(ReportsConst.CS_ROLE_ADMIN, "Admin");
            oRoles.AddRole(ReportsConst.CS_ROLE_ANALYST, "Analyst");
            oRoles.AddRole(ReportsConst.CS_ROLE_VIEWER, "Viewer");
            oRoles.AddPermission("report.run", "Run a report", "Reports");
            oRoles.AddPermission("report.export", "Export raw rows", "Reports");
            oRoles.AddPermission("report.author", "Write report SQL", "Reports");
            oRoles.AddPermission("view.save", "Save a view", "Analysis");
            oRoles.AddPermission("pivot.build", "Build a cross-tab", "Analysis");
            oRoles.AddPermission("schedule.manage", "Manage schedules", "Automation");
            oRoles.AddPermission("user.manage", "Manage users", "Administration");
            oRoles.SetGrant(ReportsConst.CS_ROLE_ADMIN, "report.run", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ADMIN, "report.export", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ADMIN, "report.author", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ADMIN, "view.save", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ADMIN, "pivot.build", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ADMIN, "schedule.manage", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ADMIN, "user.manage", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ANALYST, "report.run", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ANALYST, "report.export", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ANALYST, "view.save", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ANALYST, "pivot.build", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_ANALYST, "schedule.manage", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_VIEWER, "report.run", true);
            oRoles.SetGrant(ReportsConst.CS_ROLE_VIEWER, "pivot.build", true);
            oRoot.AddRaw(SectionCard("Roles and permissions",
                "Shown read-only: this matrix is what the server actually enforces " +
                "on every route.", oRoles.HTML));

            TsgcHTMLComponent_Form oForm = new TsgcHTMLComponent_Form();
            oForm.FormID = "rpUserForm";
            oForm.Action = "/users/save";
            oForm.Method = TsgcHTMLFormMethod.fmPost;
            oForm.Layout = TsgcHTMLFormLayout.flVertical;
            oForm.SubmitText = TReportsI18n.T(aCtx.Lang, "btn.save");
            oForm.SubmitStyle = TsgcHTMLButtonStyle.bsPrimary;
            TsgcHTMLFormField oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftHidden;
            oFld.Name = "id";
            oFld.Value = "0";
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftText;
            oFld.Name = "username";
            oFld.Label_ = "User name";
            oFld.Required = true;
            oFld.ColSpan = 4;
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftText;
            oFld.Name = "display_name";
            oFld.Label_ = "Display name";
            oFld.ColSpan = 4;
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftSelect;
            oFld.Name = "role";
            oFld.Label_ = "Role";
            oFld.ColSpan = 2;
            oFld.Options.Add(ReportsConst.CS_ROLE_ADMIN + "=Admin");
            oFld.Options.Add(ReportsConst.CS_ROLE_ANALYST + "=Analyst");
            oFld.Options.Add(ReportsConst.CS_ROLE_VIEWER + "=Viewer");
            oFld = oForm.Fields.Add();
            oFld.FieldType = TsgcHTMLFieldType.ftPassword;
            oFld.Name = "password";
            oFld.Label_ = "Password";
            oFld.ColSpan = 2;
            oFld.HelpText = "bcrypt-hashed before it is stored.";
            oRoot.AddRaw(SectionCard("Add or update a user", "", oForm.HTML));

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "nav.users"),
                "Admin only.", oRoot.HTML, "users");
        }

        // ==================================================================== //
        //  /sql : the flagship page                                            //
        // ==================================================================== //

        public string BuildSQLPage(TReportsPageCtx aCtx)
        {
            const string CS_TRIO_SQL = "SELECT r.name AS region, " +
                "substr(o.order_date, 1, 4) AS year, " +
                "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                "COUNT(DISTINCT o.id) AS orders " + "FROM order_lines ol " +
                "JOIN orders o ON o.id = ol.order_id " +
                "JOIN customers c ON c.id = o.customer_id " +
                "JOIN regions r ON r.id = c.region_id " +
                "WHERE o.status <> 'Cancelled' " + "GROUP BY r.name, year " +
                "ORDER BY r.name, year";

            const string CS_TRIO_CODE =
                "using (TReportsQuery oQ = FDB.NewQuery(CS_TRIO_SQL))\r\n" +
                "{\r\n" +
                "    oQ.Open();\r\n\r\n" +
                "    // 1. the grid\r\n" +
                "    oGrid.LoadFromDataSet(oQ.DataSet);\r\n\r\n" +
                "    // 2. the chart, from the SAME open query\r\n" +
                "    oChart.LoadFromDataSet(oQ.DataSet, \"region\", \"revenue\");\r\n\r\n" +
                "    // 3. and the pivot table, still the same one\r\n" +
                "    oPivot.RowFields.Add().FieldName = \"region\";\r\n" +
                "    oPivot.ColumnFields.Add().FieldName = \"year\";\r\n" +
                "    oPivot.LoadFromDataSet(oQ.DataSet);\r\n" +
                "}";

            const string CS_GRID_SQL = "SELECT p.sku AS \"SKU\", p.name AS \"Product\", " +
                "cat.name AS \"Category\", ROUND(p.list_price, 2) AS \"List price\" " +
                "FROM products p JOIN categories cat ON cat.id = p.category_id " +
                "ORDER BY p.list_price DESC LIMIT 12";

            const string CS_LIST_SQL = "SELECT c.name AS customer, " +
                "ROUND(SUM(ol.line_total), 0) AS revenue FROM customers c " +
                "JOIN orders o ON o.customer_id = c.id " +
                "JOIN order_lines ol ON ol.order_id = o.id " +
                "WHERE o.status <> 'Cancelled' GROUP BY c.id, c.name " +
                "ORDER BY revenue DESC LIMIT 8";

            const string CS_TREE_SQL = "SELECT 'c' || cat.id AS node_id, '' AS parent_id, " +
                "cat.name AS label, COUNT(p.id) AS products FROM categories cat " +
                "LEFT JOIN products p ON p.category_id = cat.id " +
                "GROUP BY cat.id, cat.name ORDER BY cat.name";

            TsgcHTMLNodeList oRoot = new TsgcHTMLNodeList();

            // ----- the claim, stated plainly ----- //
            oRoot.AddRaw(SectionCard("There is no REST layer in this application", "",
                "<p class=\"mb-2\">Every page in this portal is built the same way. A " +
                "<code>query</code> is opened here, in this process, " +
                "against the database. The dataset is handed to an sgcHTML component " +
                "with <code>LoadFromDataSet</code>. The component renders HTML. The " +
                "HTTP server writes that HTML to the socket.</p>" +
                "<p class=\"mb-2\">That is the entire data path. There is no REST " +
                "endpoint, no JSON serialisation, no DTO layer, no client-side ORM " +
                "and no state store in the browser. Nothing has to be modelled " +
                "twice.</p>" + "<p class=\"mb-0 small text-muted\">Below: the literal " +
                "SQL on one side, the live component it feeds on the other. The " +
                "components on this page are showing real rows from the " +
                FmtInt(FDB.GetKPIs().LinesTotal) +
                " order lines in this database.</p>"));

            // ----- the money shot: one query, three components ----- //
            using (TReportsQuery oQ = FDB.NewQuery(CS_TRIO_SQL))
            {
                oQ.Open();
                int vRows = oQ.RecordCount;
                int vMS = oQ.ElapsedMS;

                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.TableID = "rpTrioGrid";
                oGrid.Striped = true;
                oGrid.Responsive = true;
                oGrid.HeaderClass = "table-light";
                GridLoadFormatted(oGrid, oQ.DataSet);
                string vTrio = oGrid.HTML;

                TsgcHTMLComponent_Chart oChart = new TsgcHTMLComponent_Chart();
                oChart.ChartID = "rpTrioChart";
                oChart.ChartType = TsgcHTMLChartType.ctBar;
                oChart.CSSHeight = "280px";
                oChart.Title = "Revenue by region";
                // Same open query.
                oChart.LoadFromDataSet(oQ.DataSet, "region", "revenue");
                if (oChart.Datasets.Count > 0)
                {
                    oChart.Datasets.Items(0).BackgroundColor = ChartColor(0);
                    oChart.Datasets.Items(0).BorderColor = ChartColor(0);
                }
                vTrio = vTrio + "<div class=\"mt-3\">" + oChart.HTML + "</div>";

                TsgcHTMLComponent_PivotTable oPivot = new TsgcHTMLComponent_PivotTable();
                oPivot.TableID = "rpTrioPivot";
                oPivot.Compact = true;
                oPivot.ShowRowTotals = true;
                oPivot.ShowGrandTotal = true;
                oPivot.Caption = "Revenue by region and year";
                TsgcHTMLPivotField oPF = oPivot.RowFields.Add();
                oPF.FieldName = "region";
                oPF.Caption = "Region";
                oPF = oPivot.ColumnFields.Add();
                oPF.FieldName = "year";
                oPF.Caption = "Year";
                TsgcHTMLPivotMeasure oPM = oPivot.Measures.Add();
                oPM.SourceField = "revenue";
                oPM.Caption = "Revenue";
                oPM.Aggregation = TsgcHTMLPivotAggregation.paSum;
                oPM.Format = "#,##0";
                // ...and a third time, still the same query object.
                oPivot.LoadFromDataSet(oQ.DataSet);
                vTrio = vTrio + "<div class=\"mt-3 table-responsive\">" + oPivot.HTML +
                    "</div>";

                // Side by side: the query and its code on the left, the three
                // components it feeds on the right.
                TsgcHTMLComponent_Splitter oSplit = new TsgcHTMLComponent_Splitter();
                oSplit.SplitterID = "rpTrioSplit";
                oSplit.Orientation = TsgcHTMLSplitterOrientation.soHorizontal;
                oSplit.InitialSplit = 44;
                oSplit.CSSHeight = "760px";
                oSplit.MinSizeA = 260;
                oSplit.MinSizeB = 320;
                oSplit.PersistKey = "rpSqlSplit";
                oSplit.AddPaneA("<div class=\"p-2\">" +
                    "<h6 class=\"fw-semibold\">The query</h6>" + SQLBlock(CS_TRIO_SQL) +
                    "<h6 class=\"fw-semibold mt-3\">The code that binds it</h6>" +
                    CodeBlock(CS_TRIO_CODE) + "</div>");
                oSplit.AddPaneB("<div class=\"p-2\">" + vTrio + "</div>");
                oRoot.AddRaw(SectionCard(
                    "One query. Three components. Zero endpoints.",
                    "The query below ran once, in this process, and returned " +
                    FmtInt(vRows) + " rows in " + FmtInt(vMS) +
                    " ms. The Grid, the Chart and the PivotTable on the right were " +
                    "all filled from that same open dataset, one after another, " +
                    "through LoadFromDataSet. No REST endpoint was called, nothing " +
                    "was serialised to JSON and no ORM was involved on either side.",
                    oSplit.HTML));
            }

            // ----- per-component variants, in tabs ----- //
            TsgcHTMLComponent_Tabs oTabs = new TsgcHTMLComponent_Tabs();
            oTabs.TabsID = "rpSqlTabs";
            oTabs.Style = TsgcHTMLTabStyle.tsPill;

            string vBody;

            // Grid / DataTable
            using (TReportsQuery oQ = FDB.NewQuery(CS_GRID_SQL))
            {
                oQ.Open();
                TsgcHTMLComponent_DataTable oTable = new TsgcHTMLComponent_DataTable();
                oTable.TableID = "rpSqlDataTable";
                oTable.Title = "Most expensive products";
                oTable.ShowSearch = true;
                oTable.ShowRowCount = true;
                oTable.ShowPageSize = true;
                oTable.LoadFromDataSet(oQ.DataSet, 6);
                TsgcHTMLRow oRow = new TsgcHTMLRow();
                oRow.CSSClass = "g-3";
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(
                    "<h6 class=\"fw-semibold\">SQL</h6>" + SQLBlock(CS_GRID_SQL) +
                    CodeBlock("oTable.LoadFromDataSet(oQ.DataSet, 6);"));
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(
                    "<h6 class=\"fw-semibold\">DataTable</h6>" + oTable.HTML);
                vBody = oRow.HTML;
            }
            TsgcHTMLTabItem oTab = oTabs.Items.Add();
            oTab.Title = "DataTable";
            oTab.Content = vBody;
            oTab.Active = true;

            // ListGroup + Select from one query
            using (TReportsQuery oQ = FDB.NewQuery(CS_LIST_SQL))
            {
                oQ.Open();
                TsgcHTMLComponent_ListGroup oList = new TsgcHTMLComponent_ListGroup();
                oList.ListGroupID = "rpSqlList";
                oList.LoadFromDataSet(oQ.DataSet, "customer", "", "revenue");
                vBody = oList.HTML;
                TsgcHTMLComponent_Select oSelect = new TsgcHTMLComponent_Select();
                oSelect.SelectID = "rpSqlSelect";
                oSelect.ElementName = "customer";
                oSelect.Label_ = "and the same rows as a Select";
                oSelect.LoadFromDataSet(oQ.DataSet, "customer", "customer");
                vBody = vBody + "<div class=\"mt-3\">" + oSelect.HTML + "</div>";
                TsgcHTMLRow oRow = new TsgcHTMLRow();
                oRow.CSSClass = "g-3";
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(
                    "<h6 class=\"fw-semibold\">SQL</h6>" + SQLBlock(CS_LIST_SQL) +
                    CodeBlock("oList.LoadFromDataSet(oQ.DataSet, \"customer\", \"\", " +
                    "\"revenue\");\r\n" +
                    "oSelect.LoadFromDataSet(oQ.DataSet, \"customer\", \"customer\");"));
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(
                    "<h6 class=\"fw-semibold\">ListGroup and Select, both from the " +
                    "same open query</h6>" + vBody);
                vBody = oRow.HTML;
            }
            oTab = oTabs.Items.Add();
            oTab.Title = "ListGroup + Select";
            oTab.Content = vBody;

            // TreeGrid
            using (TReportsQuery oQ = FDB.NewQuery(CS_TREE_SQL))
            {
                oQ.Open();
                TsgcHTMLComponent_TreeGrid oTree = new TsgcHTMLComponent_TreeGrid();
                oTree.TreeGridID = "rpSqlTree";
                oTree.Striped = true;
                TsgcHTMLTreeGridColumn oTC = oTree.Columns.Add();
                oTC.FieldName = "label";
                oTC.Caption = "Category";
                oTC = oTree.Columns.Add();
                oTC.FieldName = "products";
                oTC.Caption = "Products";
                oTC.Align = TsgcHTMLTreeGridAlign.tgaRight;
                oTree.LoadFromDataSet(oQ.DataSet, "node_id", "parent_id");
                TsgcHTMLRow oRow = new TsgcHTMLRow();
                oRow.CSSClass = "g-3";
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(
                    "<h6 class=\"fw-semibold\">SQL</h6>" + SQLBlock(CS_TREE_SQL) +
                    CodeBlock("oTree.LoadFromDataSet(oQ.DataSet, \"node_id\", " +
                    "\"parent_id\");"));
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(
                    "<h6 class=\"fw-semibold\">TreeGrid</h6>" + oTree.HTML);
                vBody = oRow.HTML;
            }
            oTab = oTabs.Items.Add();
            oTab.Title = "TreeGrid";
            oTab.Content = vBody;

            oRoot.AddRaw(SectionCard("The same idea, component by component",
                "Twenty-seven sgcHTML components take a dataset directly. Here " +
                "are three more of them, each with the statement that fed it.",
                oTabs.HTML));

            // ----- what you did not have to build ----- //
            TsgcHTMLComponent_ListGroup oNotNeeded = new TsgcHTMLComponent_ListGroup();
            oNotNeeded.ListGroupID = "rpNotNeeded";
            oNotNeeded.AddItem("A REST controller per resource", "", "not written",
                TsgcHTMLBadgeStyle.bgSuccess);
            oNotNeeded.AddItem("DTO / view-model classes on the server", "",
                "not written", TsgcHTMLBadgeStyle.bgSuccess);
            oNotNeeded.AddItem("JSON serialisation and its date-format bugs", "",
                "not written", TsgcHTMLBadgeStyle.bgSuccess);
            oNotNeeded.AddItem("A matching set of TypeScript interfaces", "",
                "not written", TsgcHTMLBadgeStyle.bgSuccess);
            oNotNeeded.AddItem("An HTTP client, retry policy and error mapping", "",
                "not written", TsgcHTMLBadgeStyle.bgSuccess);
            oNotNeeded.AddItem("A client-side store to hold the rows", "",
                "not written", TsgcHTMLBadgeStyle.bgSuccess);
            oNotNeeded.AddItem("CORS configuration and a second auth path", "",
                "not written", TsgcHTMLBadgeStyle.bgSuccess);
            oNotNeeded.AddItem("A build pipeline for the front end", "", "not written",
                TsgcHTMLBadgeStyle.bgSuccess);
            oRoot.AddRaw(SectionCard("What this application did not have to build",
                "None of the following exists in this codebase. The whole demo is " +
                "plain C# files and one HTTP server.", oNotNeeded.HTML));

            // ----- security note on the one place raw SQL is accepted ----- //
            vBody = TsgcHTMLComponent_Popover.BuildButton("Why is that safe?",
                "Admin-authored SQL", "It must be a single SELECT, it runs on a " +
                "connection the SQLite driver opened read-only, and every " +
                "parameter is bound rather than concatenated.",
                TsgcHTMLButtonStyle.bsOutlineSecondary, TsgcHTMLPlacement.plTop);

            TsgcHTMLComponent_Modal oModal = new TsgcHTMLComponent_Modal();
            oModal.ModalID = "rpSqlModal";
            oModal.Title = "The validation, in full";
            oModal.Size = TsgcHTMLModalSize.msLarge;
            oModal.Body = CodeBlock(
                "public static bool ValidateReportSQL(\r\n" +
                "    string aSQL, out string aError)\r\n" +
                "// 1. blank out string literals and comments first\r\n" +
                "// 2. reject an inner semicolon (one statement only)\r\n" +
                "// 3. require SELECT or WITH at the start\r\n" +
                "// 4. reject INSERT/UPDATE/DELETE/DROP/PRAGMA/ATTACH/... as\r\n" +
                "//    whole words\r\n" +
                "// then, at execution time, run it on the read-only\r\n" +
                "// connection string:\r\n" +
                "Mode = SqliteOpenMode.ReadOnly;");
            oRoot.AddRaw(SectionCard("The one exception",
                "A stored report body is raw SQL, written by an administrator. " +
                "That is the only raw SQL this application accepts, and it is " +
                "gated three ways.", vBody + " " +
                TsgcHTMLComponent_Modal.BuildTriggerButton("rpSqlModal",
                    "Show the checks", TsgcHTMLButtonStyle.bsOutlinePrimary) +
                oModal.HTML));

            return BuildPageShell(aCtx, TReportsI18n.T(aCtx.Lang, "ttl.sql"),
                "The literal SQL, and the live component it feeds.", oRoot.HTML, "sql");
        }

        // ==================================================================== //
        //  exports                                                             //
        // ==================================================================== //

        // Distribute the content width across the columns, giving the numeric ones
        // less room than the text ones.
        private static double[] ComputeColumnWidths(DataTable aDataSet,
            double aContentWidth)
        {
            int vCount = aDataSet.Columns.Count;
            double[] vResult = new double[vCount];
            double[] vWeights = new double[vCount];
            double vTotal = 0;
            for (int vI = 0; vI < vCount; vI++)
            {
                if (FieldIsNumeric(aDataSet.Columns[vI]))
                    vWeights[vI] = 1.0;
                else
                    vWeights[vI] = 1.8;
                vTotal = vTotal + vWeights[vI];
            }
            if (vTotal <= 0)
                vTotal = 1;
            for (int vI = 0; vI < vCount; vI++)
                vResult[vI] = aContentWidth * vWeights[vI] / vTotal;
            return vResult;
        }

        private static void WriteDataSetToPDF(DataTable aDataSet, Stream aStream,
            string aTitle, string aSubtitle, int aMaxRows)
        {
            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = aTitle;
            oPDF.Author = "sgcReports";
            oPDF.Subject = aSubtitle;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poLandscape;
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
            oPDF.HeaderText = "sgcReports - " + aTitle;
            oPDF.FooterText = "Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm",
                CultureInfo.InvariantCulture) + "   -   Page {page} of {pages}";
            oPDF.ShowPageNumbers = true;

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 16);
            oPDF.SetColor(CS_ACCENT);
            oPDF.TextOut(oPDF.MarginLeft, oPDF.MarginTop + 14, aTitle);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 9);
            oPDF.SetColor("#444444");
            if (aSubtitle != "")
                oPDF.TextOut(oPDF.MarginLeft, oPDF.MarginTop + 21, aSubtitle);
            oPDF.SetColor("#000000");
            oPDF.CurrentY = oPDF.MarginTop + 28;

            int vFields = aDataSet.Columns.Count;
            string[] vHeaders = new string[vFields];
            string[] vCells = new string[vFields];
            for (int vI = 0; vI < vFields; vI++)
                vHeaders[vI] = PrettyColumn(aDataSet.Columns[vI].ColumnName);

            // A4 landscape is 297 mm wide; the writer's default margin is 15 mm.
            double[] vWidths = ComputeColumnWidths(aDataSet,
                297 - oPDF.MarginLeft - oPDF.MarginRight);

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8);
            oPDF.BeginTable(vWidths);
            oPDF.TableHeader(vHeaders);
            int vCount = 0;
            int vRow = 0;
            while ((vRow < aDataSet.Rows.Count) &&
                ((aMaxRows <= 0) || (vCount < aMaxRows)))
            {
                for (int vI = 0; vI < vFields; vI++)
                    vCells[vI] = CellText(aDataSet.Rows[vRow], vI);
                oPDF.TableRow(vCells);
                vCount++;
                vRow++;
            }
            oPDF.EndTable();

            if ((aMaxRows > 0) && (vRow < aDataSet.Rows.Count))
            {
                oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8);
                oPDF.SetColor("#888888");
                oPDF.TextOut(oPDF.MarginLeft, oPDF.CurrentY + 6, "Truncated at " +
                    aMaxRows.ToString(CultureInfo.InvariantCulture) +
                    " rows. Use the Excel or CSV export for the whole set.");
            }

            oPDF.SaveToStream(aStream);
        }

        private static void WriteDataSetToXLSX(DataTable aDataSet, Stream aStream,
            string aSheetName)
        {
            TsgcHTMLExportXLSX oXLSX = new TsgcHTMLExportXLSX();
            oXLSX.AddSheet(aSheetName);
            oXLSX.AddRow();
            int vFields = aDataSet.Columns.Count;
            for (int vI = 0; vI < vFields; vI++)
                oXLSX.AddCell(PrettyColumn(aDataSet.Columns[vI].ColumnName));
            for (int vR = 0; vR < aDataSet.Rows.Count; vR++)
            {
                oXLSX.AddRow();
                for (int vI = 0; vI < vFields; vI++)
                {
                    object vValue = aDataSet.Rows[vR][vI];
                    bool vNull = (vValue == null) || (vValue == DBNull.Value);
                    if (FieldIsNumeric(aDataSet.Columns[vI]) && !vNull)
                        oXLSX.AddCellNumber(Convert.ToDouble(vValue,
                            CultureInfo.InvariantCulture));
                    else
                        oXLSX.AddCell(vNull ? "" : Convert.ToString(vValue,
                            CultureInfo.InvariantCulture) ?? "");
                }
            }
            oXLSX.SaveToStream(aStream);
        }

        // The value a CSV cell should carry: the raw number with a '.' separator
        // and no thousands grouping, whatever the machine's regional settings say.
        private static string CsvValue(DataRow aRow, int aIndex)
        {
            object vValue = aRow[aIndex];
            if ((vValue == null) || (vValue == DBNull.Value))
                return "";
            if (FieldIsNumeric(aRow.Table.Columns[aIndex]))
                return Convert.ToDouble(vValue, CultureInfo.InvariantCulture)
                    .ToString("G15", CultureInfo.InvariantCulture);
            return Convert.ToString(vValue, CultureInfo.InvariantCulture) ?? "";
        }

        // RFC 4180 field.
        private static string CsvField(string aValue)
        {
            if ((aValue.IndexOf(',') >= 0) || (aValue.IndexOf('"') >= 0) ||
                (aValue.IndexOf('\r') >= 0) || (aValue.IndexOf('\n') >= 0))
                return "\"" + aValue.Replace("\"", "\"\"") + "\"";
            return aValue;
        }

        private static void WriteDataSetToCSV(DataTable aDataSet, Stream aStream)
        {
            StringBuilder oText = new StringBuilder();
            int vFields = aDataSet.Columns.Count;
            string vLine = "";
            for (int vI = 0; vI < vFields; vI++)
            {
                if (vI > 0)
                    vLine = vLine + ",";
                vLine = vLine + CsvField(PrettyColumn(
                    aDataSet.Columns[vI].ColumnName));
            }
            oText.Append(vLine).Append("\r\n");
            for (int vR = 0; vR < aDataSet.Rows.Count; vR++)
            {
                vLine = "";
                for (int vI = 0; vI < vFields; vI++)
                {
                    if (vI > 0)
                        vLine = vLine + ",";
                    vLine = vLine + CsvField(CsvValue(aDataSet.Rows[vR], vI));
                }
                oText.Append(vLine).Append("\r\n");
            }
            byte[] vBytes = Encoding.UTF8.GetBytes(oText.ToString());
            if (vBytes.Length > 0)
                aStream.Write(vBytes, 0, vBytes.Length);
        }

        public void ExportReportPDF(TReportsReportDef aDef,
            List<TReportsParamValue> aValues, Stream aStream)
        {
            using (TReportsQuery oQ = FDB.NewReportQuery(aDef, aValues))
                WriteDataSetToPDF(oQ.DataSet, aStream, aDef.Name, aDef.Description, 0);
        }

        public void ExportReportXLSX(TReportsReportDef aDef,
            List<TReportsParamValue> aValues, Stream aStream)
        {
            using (TReportsQuery oQ = FDB.NewReportQuery(aDef, aValues))
                WriteDataSetToXLSX(oQ.DataSet, aStream, Truncate(aDef.Name, 28));
        }

        public void ExportReportCSV(TReportsReportDef aDef,
            List<TReportsParamValue> aValues, Stream aStream)
        {
            using (TReportsQuery oQ = FDB.NewReportQuery(aDef, aValues))
                WriteDataSetToCSV(oQ.DataSet, aStream);
        }

        // The Grid's own writers. Loading the rows into the component first is what
        // makes the on-screen column order and the file agree.
        public void ExportExplorePDF(TReportsExploreFilter aFilter, Stream aStream)
        {
            const int CS_MAX_PDF_ROWS = 2000;
            using (TReportsQuery oQ = FDB.NewExploreQuery(aFilter, 0, CS_MAX_PDF_ROWS))
            {
                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.ExportFileName = "order-lines";
                oGrid.LoadFromDataSet(oQ.DataSet);
                for (int vI = 0; vI < oGrid.Columns.Count; vI++)
                    oGrid.Columns.Items(vI).Title =
                        PrettyColumn(oGrid.Columns.Items(vI).Title);
                oGrid.SaveToPDFStream(aStream);
            }
        }

        public void ExportExploreXLSX(TReportsExploreFilter aFilter, Stream aStream)
        {
            const int CS_MAX_XLSX_ROWS = 20000;
            using (TReportsQuery oQ = FDB.NewExploreQuery(aFilter, 0, CS_MAX_XLSX_ROWS))
            {
                TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
                oGrid.ExportSheetName = "Order lines";
                oGrid.ExportFileName = "order-lines";
                oGrid.LoadFromDataSet(oQ.DataSet);
                for (int vI = 0; vI < oGrid.Columns.Count; vI++)
                    oGrid.Columns.Items(vI).Title =
                        PrettyColumn(oGrid.Columns.Items(vI).Title);
                oGrid.SaveToXLSXStream(aStream);
            }
        }

        // The cross-tab exports run the same aggregated statement the page runs,
        // then write the grouped rows out. The pivot itself is a display concern.
        private static string PivotExportSQL(string aRowField, string aColField,
            string aMeasure, string aAgg, string aFrom, string aTo)
        {
            string vAgg;
            if (string.Equals(aAgg, "count", StringComparison.OrdinalIgnoreCase))
                vAgg = "COUNT";
            else if (string.Equals(aAgg, "avg", StringComparison.OrdinalIgnoreCase))
                vAgg = "AVG";
            else if (string.Equals(aAgg, "min", StringComparison.OrdinalIgnoreCase))
                vAgg = "MIN";
            else if (string.Equals(aAgg, "max", StringComparison.OrdinalIgnoreCase))
                vAgg = "MAX";
            else
                vAgg = "SUM";
            string vResult = "SELECT " + PivotFieldSQL(aRowField) + " AS rowdim, " +
                PivotFieldSQL(aColField) + " AS coldim, ROUND(" + vAgg + "(" +
                PivotMeasureSQL(aMeasure) + "), 2) AS measure" +
                " FROM order_lines ol JOIN orders o ON o.id = ol.order_id " +
                "JOIN customers c ON c.id = o.customer_id " +
                "JOIN regions r ON r.id = c.region_id " +
                "JOIN salespeople s ON s.id = o.salesperson_id " +
                "JOIN products p ON p.id = ol.product_id " +
                "JOIN categories cat ON cat.id = p.category_id " +
                "WHERE o.status <> 'Cancelled'";
            if (ReportsDBHelpers.IsISODate(aFrom))
                vResult = vResult + " AND o.order_date >= :dfrom";
            if (ReportsDBHelpers.IsISODate(aTo))
                vResult = vResult + " AND o.order_date <= :dto";
            return vResult + " GROUP BY rowdim, coldim ORDER BY rowdim, coldim";
        }

        public void ExportPivotPDF(string aRowField, string aColField,
            string aMeasure, string aAgg, string aFrom, string aTo, Stream aStream)
        {
            using (TReportsQuery oQ = FDB.NewQuery(PivotExportSQL(aRowField, aColField,
                aMeasure, aAgg, aFrom, aTo)))
            {
                if (ReportsDBHelpers.IsISODate(aFrom))
                    oQ.SetParamStr("dfrom", aFrom);
                if (ReportsDBHelpers.IsISODate(aTo))
                    oQ.SetParamStr("dto", aTo);
                oQ.Open();
                WriteDataSetToPDF(oQ.DataSet, aStream,
                    PrettyColumn(aRowField) + " by " + PrettyColumn(aColField),
                    PrettyColumn(aMeasure) + ", " + (aAgg ?? "").ToUpperInvariant(), 0);
            }
        }

        public void ExportPivotXLSX(string aRowField, string aColField,
            string aMeasure, string aAgg, string aFrom, string aTo, Stream aStream)
        {
            using (TReportsQuery oQ = FDB.NewQuery(PivotExportSQL(aRowField, aColField,
                aMeasure, aAgg, aFrom, aTo)))
            {
                if (ReportsDBHelpers.IsISODate(aFrom))
                    oQ.SetParamStr("dfrom", aFrom);
                if (ReportsDBHelpers.IsISODate(aTo))
                    oQ.SetParamStr("dto", aTo);
                oQ.Open();
                WriteDataSetToXLSX(oQ.DataSet, aStream, "Cross-tab");
            }
        }
    }
}
