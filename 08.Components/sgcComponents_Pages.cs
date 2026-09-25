// ***************************************************************************
//  sgcComponents - sgcHTML components showcase (managed port)
//  Port of delphi\Demos\60.HTML\08.Components\sgcComponentsDemo_Pages.pas
//
//  Every Build* is a static method: the page components are created, rendered
//  to HTML and dropped inside the call, exactly like the sibling 60.HTML demos.
//
//  The /live page is the exception: its four push components (Presence,
//  ActivityFeed, JobProgress, LogViewer) must survive between requests so the
//  background push thread can mutate them and emit htmx OOB fragments. They
//  live in a locked singleton owned by this class (LiveInit / LiveDone), so the
//  server only ever exchanges plain strings with it.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
// sgc
using esegece.sgcWebSockets;

namespace Components
{
    // Supplies the master-detail row HTML for the /data orders grid.
    public class TsgcOrderDetailProvider
    {
        private static readonly string[] CS_LINES = new string[]
        {
            "<tr><td>Delphi 12 Athens</td><td class=\"text-end\">2</td>" +
            "<td class=\"text-end\">1.598,00</td></tr>" +
            "<tr><td>sgcWebSockets Enterprise</td><td class=\"text-end\">1</td>" +
            "<td class=\"text-end\">1.190,00</td></tr>",

            "<tr><td>sgcSign Professional</td><td class=\"text-end\">3</td>" +
            "<td class=\"text-end\">1.485,00</td></tr>",

            "<tr><td>sgcWebSockets Standard</td><td class=\"text-end\">5</td>" +
            "<td class=\"text-end\">1.245,00</td></tr>" +
            "<tr><td>Support renewal</td><td class=\"text-end\">1</td>" +
            "<td class=\"text-end\">240,00</td></tr>",

            "<tr><td>sgcOpenAPI</td><td class=\"text-end\">1</td>" +
            "<td class=\"text-end\">390,00</td></tr>"
        };

        public void GetDetailHTML(object Sender, int aRowIndex, ref string aHTML)
        {
            TsgcHTMLContainer oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "p-2";
            oWrap.AddRaw("<h6 class=\"mb-2\">Order lines</h6>" +
                "<table class=\"table table-sm mb-0\"><thead><tr><th>Item</th>" +
                "<th class=\"text-end\">Qty</th><th class=\"text-end\">Amount</th></tr>" +
                "</thead><tbody>" + CS_LINES[aRowIndex % CS_LINES.Length] +
                "</tbody></table>");
            aHTML = oWrap.HTML;
        }
    }

    // Owns the four push components + the server-mode AutoComplete. Guarded by a
    // lock because the push thread and the HTTP threads both touch it.
    public class TsgcComponentsLive
    {
        public const string CS_PRESENCE_ID = "sgcPresence";
        public const string CS_FEED_ID = "sgcActivityFeed";
        public const string CS_JOBS_ID = "sgcJobProgress";
        public const string CS_LOG_ID = "sgcLogViewer";
        public const string CS_AUTOCOMPLETE_ID = "sgcCityAuto";

        private static readonly string[] CS_LOG_MSG = new string[]
        {
            "GET /data 200 in 12 ms",
            "Cache hit ratio 94.2%",
            "Connection pool: 7/32 in use",
            "Slow query: SELECT * FROM invoices (412 ms)",
            "Retrying webhook delivery (attempt 2/5)",
            "Disk usage on /var at 85%"
        };

        private readonly object FLock = new object();
        private readonly TsgcHTMLComponent_Presence FPresence;
        private readonly TsgcHTMLComponent_ActivityFeed FFeed;
        private readonly TsgcHTMLComponent_JobProgress FJobs;
        private readonly TsgcHTMLComponent_LogViewer FLog;
        private readonly TsgcHTMLComponent_AutoComplete FAuto;
        private readonly List<string> FCities;
        private int FTick;

        public TsgcComponentsLive()
        {
            FTick = 0;

            FCities = new List<string>
            {
                "Amsterdam", "Barcelona", "Berlin", "Bilbao", "Bratislava",
                "Brussels", "Budapest", "Copenhagen", "Dublin", "Lisbon",
                "London", "Madrid", "Milan", "Munich", "Oslo", "Paris",
                "Prague", "Rome", "Stockholm", "Valencia", "Vienna",
                "Warsaw", "Zurich"
            };

            // ----- presence ----- //
            FPresence = new TsgcHTMLComponent_Presence();
            FPresence.PresenceID = CS_PRESENCE_ID;
            FPresence.Title = "Team online";
            FPresence.Layout = TsgcHTMLPresenceLayout.plList;
            FPresence.ShowCount = true;
            FPresence.MaxVisible = 8;
            FPresence.AddUser("u1", "Sergio Garcia",
                TsgcHTMLPresenceStatus.psOnline, "", "Reviewing PR #482");
            FPresence.AddUser("u2", "Marta Ruiz",
                TsgcHTMLPresenceStatus.psOnline, "", "Editing invoice batch");
            FPresence.AddUser("u3", "Tom Becker",
                TsgcHTMLPresenceStatus.psAway, "", "Away since 14:20");
            FPresence.AddUser("u4", "Aiko Tanaka",
                TsgcHTMLPresenceStatus.psBusy, "", "On a call");
            FPresence.AddUser("u5", "Paul Dupont",
                TsgcHTMLPresenceStatus.psOffline, "", "Last seen 09:12");

            // ----- activity feed ----- //
            FFeed = new TsgcHTMLComponent_ActivityFeed();
            FFeed.FeedID = CS_FEED_ID;
            FFeed.Title = "Activity";
            FFeed.MaxItems = 25;
            FFeed.AddActivity("Marta Ruiz", "approved", "invoice INV-2026-0184",
                TsgcHTMLColor.hcSuccess, "bi bi-check2-circle");
            FFeed.AddActivity("Tom Becker", "deployed", "build #482 to staging",
                TsgcHTMLColor.hcPrimary, "bi bi-rocket-takeoff");
            FFeed.AddActivity("Aiko Tanaka", "commented on", "ticket #1190",
                TsgcHTMLColor.hcInfo, "bi bi-chat-left-text");

            // ----- jobs ----- //
            FJobs = new TsgcHTMLComponent_JobProgress();
            FJobs.JobsID = CS_JOBS_ID;
            FJobs.Title = "Background jobs";
            FJobs.ShowCancelButton = true;
            FJobs.ShowCompleted = true;
            FJobs.AutoRemoveCompleted = false;

            TsgcHTMLJobItem oJob = FJobs.AddJob("j1", "Import customers",
                "customers_2026Q3.csv");
            oJob.Percent = 0;
            oJob.Status = TsgcHTMLJobStatus.jsRunning;
            oJob.StatusText = "Starting...";

            oJob = FJobs.AddJob("j2", "Rebuild search index",
                "documents + attachments");
            oJob.Percent = 35;
            oJob.Status = TsgcHTMLJobStatus.jsRunning;
            oJob.StatusText = "Indexing...";

            oJob = FJobs.AddJob("j3", "Export invoices", "PDF/A archive");
            oJob.Percent = 70;
            oJob.Status = TsgcHTMLJobStatus.jsRunning;
            oJob.StatusText = "Rendering...";

            // ----- log viewer ----- //
            FLog = new TsgcHTMLComponent_LogViewer();
            FLog.LogID = CS_LOG_ID;
            FLog.Title = "Application log";
            FLog.CSSHeight = "320px";
            FLog.Theme = TsgcHTMLLogTheme.ltDark;
            FLog.MaxLines = 200;
            FLog.AddLine(TsgcHTMLLogLevel.llInfo,
                "Server started on port 8093", "http");
            FLog.AddLine(TsgcHTMLLogLevel.llInfo,
                "WebSocket engine attached", "htmx");
            FLog.AddLine(TsgcHTMLLogLevel.llDebug,
                "Loaded 23 cities into the autocomplete index", "catalog");

            // ----- autocomplete (server mode) ----- //
            FAuto = new TsgcHTMLComponent_AutoComplete();
            FAuto.AutoCompleteID = CS_AUTOCOMPLETE_ID;
            FAuto.ElementName = "city";
            FAuto.Label_ = "City (server-side search over the WebSocket)";
            FAuto.Placeholder = "Type at least 2 letters, e.g. \"ma\"...";
            FAuto.Mode = TsgcHTMLAutoCompleteMode.acServer;
            FAuto.MinLength = 2;
            FAuto.DebounceMs = 200;
            FAuto.MaxResults = 8;
            FAuto.HighlightMatch = true;
            FAuto.OnSearch += DoAutoCompleteSearch;
        }

        // OnSearch: fill aResults with the matching cities, newline separated.
        private void DoAutoCompleteSearch(object Sender, string aQuery,
            ref string aResults)
        {
            aResults = "";
            string vQuery = (aQuery ?? "").Trim().ToLowerInvariant();
            if (vQuery == "")
                return;

            StringBuilder oOut = new StringBuilder();
            int vFound = 0;
            for (int vI = 0; vI < FCities.Count; vI++)
            {
                if (FCities[vI].ToLowerInvariant().Contains(vQuery))
                {
                    oOut.AppendLine(FCities[vI]);
                    vFound++;
                    if (vFound >= 8)
                        break;
                }
            }
            aResults = oOut.ToString();
        }

        private TsgcHTMLJobItem? FindJob(string aId)
        {
            for (int vI = 0; vI < FJobs.Jobs.Count; vI++)
            {
                TsgcHTMLJobItem oItem = FJobs.Jobs[vI];
                if (string.Equals(oItem.Id, aId,
                    StringComparison.OrdinalIgnoreCase))
                    return oItem;
            }
            return null;
        }

        // The /live page body: the five components rendered from the SAME
        // instances the push thread mutates, so the OOB fragment ids line up
        // with the rendered DOM.
        public string RenderLive()
        {
            lock (FLock)
            {
                TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");
                oRoot.AddRaw("<div class=\"alert alert-info d-flex align-items-center " +
                    "gap-2\"><i class=\"bi bi-broadcast\"></i><div>This page is pushed " +
                    "from the server over the WebSocket. Jobs tick, log lines append, " +
                    "the feed grows and users flip online/away without a single page " +
                    "reload. Press <b>Cancel</b> on a job to send a message back to " +
                    "the server.</div></div>");

                TsgcHTMLRow oRow = new TsgcHTMLRow();
                oRow.Col(TsgcHTMLColWidth.cw8).AddRaw(TsgcComponentsDemoPages.SectionCard(
                    "JobProgress",
                    "Server pushes GetJobFragmentHTML on every tick; Cancel sends " +
                    "{\"action\":\"job:cancel\"} back over the socket.", FJobs.HTML));
                oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(TsgcComponentsDemoPages.SectionCard(
                    "Presence",
                    "Users flip online/away; GetUserFragmentHTML + " +
                    "GetCountFragmentHTML are broadcast.", FPresence.HTML));
                oRoot.AddRaw(oRow.HTML);

                oRow = new TsgcHTMLRow();
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(TsgcComponentsDemoPages.SectionCard(
                    "ActivityFeed",
                    "New items are prepended with GetLastItemFragmentHTML.",
                    FFeed.HTML));
                oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(TsgcComponentsDemoPages.SectionCard(
                    "LogViewer",
                    "New lines are appended with GetLastLineFragmentHTML.",
                    FLog.HTML));
                oRoot.AddRaw(oRow.HTML);

                return oRoot.HTML;
            }
        }

        public string RenderAutoComplete()
        {
            lock (FLock)
            {
                return FAuto.HTML;
            }
        }

        // Advance the simulated state one step and return the OOB fragments.
        public string Tick()
        {
            lock (FLock)
            {
                FTick++;
                StringBuilder oOut = new StringBuilder();

                // ---- jobs: tick each running job; restart a finished one ---- //
                for (int vI = 0; vI < FJobs.Jobs.Count; vI++)
                {
                    TsgcHTMLJobItem oJob = FJobs.Jobs[vI];
                    if (oJob.Status == TsgcHTMLJobStatus.jsRunning)
                    {
                        int vStep = 3 + ((FTick + vI * 5) % 9);
                        if (oJob.Percent + vStep >= 100)
                            FJobs.UpdateJob(oJob.Id, 100,
                                TsgcHTMLJobStatus.jsCompleted, "Completed");
                        else
                            FJobs.UpdateJob(oJob.Id, oJob.Percent + vStep,
                                TsgcHTMLJobStatus.jsRunning,
                                "Working... " + (oJob.Percent + vStep).ToString() + "%");
                        oOut.Append(FJobs.GetJobFragmentHTML(oJob.Id));
                    }
                    else if (oJob.Status == TsgcHTMLJobStatus.jsCompleted ||
                             oJob.Status == TsgcHTMLJobStatus.jsCancelled)
                    {
                        // Restart it a few ticks later so the demo keeps moving.
                        if ((FTick % 6) == 0)
                        {
                            FJobs.UpdateJob(oJob.Id, 0,
                                TsgcHTMLJobStatus.jsRunning, "Restarted");
                            oOut.Append(FJobs.GetJobFragmentHTML(oJob.Id));
                        }
                    }
                }

                // ---- log: append a line every tick ---- //
                TsgcHTMLLogLevel vLevel;
                switch (FTick % 7)
                {
                    case 3: vLevel = TsgcHTMLLogLevel.llWarning; break;
                    case 5: vLevel = TsgcHTMLLogLevel.llError; break;
                    case 1: vLevel = TsgcHTMLLogLevel.llDebug; break;
                    default: vLevel = TsgcHTMLLogLevel.llInfo; break;
                }
                FLog.AddLine(vLevel, CS_LOG_MSG[FTick % CS_LOG_MSG.Length], "app");
                oOut.Append(FLog.GetLastLineFragmentHTML());

                // ---- activity feed: a new item every 3rd tick ---- //
                if ((FTick % 3) == 0)
                {
                    switch ((FTick / 3) % 4)
                    {
                        case 0:
                            FFeed.AddActivity("Marta Ruiz", "created",
                                "order SO-2026-0912", TsgcHTMLColor.hcSuccess,
                                "bi bi-plus-circle");
                            break;
                        case 1:
                            FFeed.AddActivity("Tom Becker", "restarted",
                                "the search indexer", TsgcHTMLColor.hcWarning,
                                "bi bi-arrow-clockwise");
                            break;
                        case 2:
                            FFeed.AddActivity("Aiko Tanaka", "uploaded",
                                "contract_v3.pdf", TsgcHTMLColor.hcInfo,
                                "bi bi-cloud-arrow-up");
                            break;
                        default:
                            FFeed.AddActivity("Sergio Garcia", "merged",
                                "PR #482 into main", TsgcHTMLColor.hcPrimary,
                                "bi bi-git");
                            break;
                    }
                    oOut.Append(FFeed.GetLastItemFragmentHTML());
                }

                // ---- presence: flip one user every 2nd tick ---- //
                if ((FTick % 2) == 0)
                {
                    string vUserId = "u" + (1 + ((FTick / 2) % 5)).ToString();
                    if (((FTick / 2) % 2) == 0)
                        FPresence.SetUserStatus(vUserId,
                            TsgcHTMLPresenceStatus.psAway, "Stepped away");
                    else
                        FPresence.SetUserStatus(vUserId,
                            TsgcHTMLPresenceStatus.psOnline, "Active now");
                    oOut.Append(FPresence.GetUserFragmentHTML(vUserId));
                    oOut.Append(FPresence.GetCountFragmentHTML());
                }

                return oOut.ToString();
            }
        }

        // Inbound {"action":"job:cancel","job":"j1"}.
        public string CancelJob(string aJobId)
        {
            if (string.IsNullOrWhiteSpace(aJobId))
                return "";
            lock (FLock)
            {
                TsgcHTMLJobItem? oJob = FindJob(aJobId);
                if (oJob == null)
                    return "";
                FJobs.UpdateJob(aJobId, oJob.Percent,
                    TsgcHTMLJobStatus.jsCancelled, "Cancelled by user");
                FLog.AddLine(TsgcHTMLLogLevel.llWarning,
                    "Job \"" + oJob.Name + "\" cancelled by the user", "jobs");
                return FJobs.GetJobFragmentHTML(aJobId) +
                    FLog.GetLastLineFragmentHTML();
            }
        }

        // Inbound {"action":"autoCompleteSearch","query":"ma"}.
        public string Search(string aQuery)
        {
            lock (FLock)
            {
                FAuto.ProcessSearch(aQuery);
                return FAuto.GetResultsFragmentHTML();
            }
        }
    }

    public static class TsgcComponentsDemoPages
    {
        private const string CS_BI_INPUTS = "<i class=\"bi bi-ui-checks\"></i>";
        private const string CS_BI_DISPLAY = "<i class=\"bi bi-columns-gap\"></i>";
        private const string CS_BI_CODES = "<i class=\"bi bi-qr-code\"></i>";
        private const string CS_BI_DATA = "<i class=\"bi bi-table\"></i>";
        private const string CS_BI_CHARTS = "<i class=\"bi bi-bar-chart\"></i>";
        private const string CS_BI_LIVE = "<i class=\"bi bi-broadcast\"></i>";
        private const string CS_BI_ADMIN = "<i class=\"bi bi-people\"></i>";
        private const string CS_BI_DOCS = "<i class=\"bi bi-file-earmark-pdf\"></i>";

        private static TsgcComponentsLive? gLive;

        // ----- live (WebSocket) state ----- //

        public static void LiveInit()
        {
            if (gLive == null)
                gLive = new TsgcComponentsLive();
        }

        public static void LiveDone()
        {
            gLive = null;
        }

        public static string LiveTick()
        {
            return gLive != null ? gLive.Tick() : "";
        }

        public static string LiveCancelJob(string aJobId)
        {
            return gLive != null ? gLive.CancelJob(aJobId) : "";
        }

        public static string LiveAutoCompleteSearch(string aQuery)
        {
            return gLive != null ? gLive.Search(aQuery) : "";
        }

        // ----- small helpers ----- //

        // Card wrapper: title + raw body HTML, built with the node layer.
        public static string SectionCard(string aTitle, string aSubtitle,
            string aBodyHTML)
        {
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4";
            oCard.Title = aTitle;
            if (aSubtitle != "")
                oCard.Body.AddRaw("<p class=\"text-muted small mb-3\">" +
                    aSubtitle + "</p>");
            oCard.Body.AddRaw(aBodyHTML);
            return oCard.HTML;
        }

        private static string TwoCols(string aLeft, string aRight)
        {
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(aLeft);
            oRow.Col(TsgcHTMLColWidth.cw6).AddRaw(aRight);
            return oRow.HTML;
        }

        // ----- /inputs ----- //

        private static string BuildInputsBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            // --- MultiSelect + Transfer --- //
            TsgcHTMLComponent_MultiSelect oMulti = new TsgcHTMLComponent_MultiSelect();
            oMulti.FieldName = "stack";
            oMulti.Placeholder = "Pick the technologies for this project...";
            oMulti.ShowSearch = true;
            oMulti.MaxSelections = 5;
            oMulti.AddOption("delphi", "Delphi 12 Athens", true);
            oMulti.AddOption("cbuilder", "C++Builder 12", false);
            oMulti.AddOption("csharp", "C# / .NET 8", true);
            oMulti.AddOption("ts", "TypeScript", false);
            oMulti.AddOption("python", "Python", false);
            oMulti.AddOption("go", "Go", false);
            oMulti.AddOption("rust", "Rust", false);
            string vLeft = SectionCard("MultiSelect",
                "Searchable multi-value select with a selection cap.", oMulti.HTML);

            TsgcHTMLComponent_Transfer oTransfer = new TsgcHTMLComponent_Transfer();
            oTransfer.FieldName = "members";
            oTransfer.TitleSource = "Available engineers";
            oTransfer.TitleTarget = "Assigned to Sprint 42";
            oTransfer.ShowFilter = true;
            oTransfer.CSSHeight = "220px";
            oTransfer.AddItem("sgarcia", "Sergio Garcia", true);
            oTransfer.AddItem("mruiz", "Marta Ruiz", true);
            oTransfer.AddItem("tbecker", "Tom Becker", false);
            oTransfer.AddItem("atanaka", "Aiko Tanaka", false);
            oTransfer.AddItem("pdupont", "Paul Dupont", false);
            oTransfer.AddItem("lrossi", "Luca Rossi", false);
            string vRight = SectionCard("Transfer",
                "Dual list box with a filter; assigned items start on the right.",
                oTransfer.HTML);
            oRoot.AddRaw(TwoCols(vLeft, vRight));

            // --- pickers --- //
            TsgcHTMLComponent_TimePicker oTime = new TsgcHTMLComponent_TimePicker();
            oTime.FieldName = "shift_start";
            oTime.LabelText = "Shift start";
            oTime.Value = "08:30";
            oTime.MinTime = "06:00";
            oTime.MaxTime = "22:00";
            vLeft = oTime.HTML;

            TsgcHTMLComponent_DateTimePicker oDateTime =
                new TsgcHTMLComponent_DateTimePicker();
            oDateTime.FieldName = "maintenance_window";
            oDateTime.LabelText = "Maintenance window";
            oDateTime.Value = "2026-07-18T23:00";
            vRight = oDateTime.HTML;
            oRoot.AddRaw(SectionCard("TimePicker + DateTimePicker",
                "Native-input based pickers with min/max bounds.",
                TwoCols(vLeft, vRight)));

            TsgcHTMLComponent_DateRangePicker oRange =
                new TsgcHTMLComponent_DateRangePicker();
            oRange.FieldNameStart = "from";
            oRange.FieldNameEnd = "to";
            oRange.LabelStart = "Report from";
            oRange.LabelEnd = "Report to";
            oRange.StartValue = "2026-07-01";
            oRange.EndValue = "2026-07-31";
            oRange.ShowPresets = true;
            oRange.Layout = TsgcHTMLDateRangeLayout.drlInline;
            oRoot.AddRaw(SectionCard("DateRangePicker",
                "Start/end pair with one-click presets (today, last 7, last 30, " +
                "this month).", oRange.HTML));

            // --- sliders --- //
            TsgcHTMLComponent_Slider oSlider = new TsgcHTMLComponent_Slider();
            oSlider.FieldName = "max_connections";
            oSlider.LabelText = "Max concurrent connections";
            oSlider.Min = 0;
            oSlider.Max = 10000;
            oSlider.Step = 100;
            oSlider.Value = 2500;
            oSlider.ShowValue = true;
            oSlider.ShowMinMax = true;
            oSlider.ColorStyle = TsgcHTMLColor.hcPrimary;
            vLeft = oSlider.HTML;

            TsgcHTMLComponent_RangeSlider oRangeSlider =
                new TsgcHTMLComponent_RangeSlider();
            oRangeSlider.FieldNameLow = "price_min";
            oRangeSlider.FieldNameHigh = "price_max";
            oRangeSlider.Min = 0;
            oRangeSlider.Max = 2000;
            oRangeSlider.Step = 10;
            oRangeSlider.ValueLow = 250;
            oRangeSlider.ValueHigh = 1250;
            oRangeSlider.MinGap = 100;
            oRangeSlider.ShowValues = true;
            vRight = oRangeSlider.HTML;
            oRoot.AddRaw(SectionCard("Slider + RangeSlider",
                "Single value and a two-thumb range with a minimum gap.",
                TwoCols(vLeft, vRight)));

            // --- colour picker + autocomplete --- //
            TsgcHTMLComponent_ColorPicker oColor = new TsgcHTMLComponent_ColorPicker();
            oColor.FieldName = "brand_color";
            oColor.LabelText = "Brand colour";
            oColor.Value = "#0057B8";
            oColor.ShowValueInput = true;
            oColor.Swatches.Add("#0057B8");
            oColor.Swatches.Add("#198754");
            oColor.Swatches.Add("#dc3545");
            oColor.Swatches.Add("#6f42c1");
            oColor.Swatches.Add("#fd7e14");
            oColor.Swatches.Add("#20c997");
            vLeft = oColor.HTML;

            vRight = gLive != null ? gLive.RenderAutoComplete() : "";

            oRoot.AddRaw(SectionCard("ColorPicker + AutoComplete (acServer)",
                "The autocomplete runs in server mode: each keystroke is debounced, " +
                "sent over the WebSocket as {\"action\":\"autoCompleteSearch\"}, " +
                "matched server-side and the result list is pushed back as an OOB " +
                "fragment.", TwoCols(vLeft, vRight)));

            return oRoot.HTML;
        }

        // ----- /display ----- //

        private static string BuildDisplayBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            // --- progress bars (single + stacked) --- //
            TsgcHTMLComponent_ProgressBar oProgress = new TsgcHTMLComponent_ProgressBar();
            oProgress.Value = 68;
            oProgress.Max = 100;
            oProgress.ColorStyle = TsgcHTMLColor.hcPrimary;
            oProgress.Striped = true;
            oProgress.Animated = true;
            oProgress.ShowLabel = true;
            oProgress.CSSHeight = "20px";
            string vLeft = "<p class=\"small text-muted mb-1\">Backup in progress</p>" +
                oProgress.HTML;

            oProgress = new TsgcHTMLComponent_ProgressBar();
            oProgress.CSSHeight = "20px";
            TsgcHTMLProgressBarItem oBar = oProgress.Bars.Add();
            oBar.Value = 45;
            oBar.ColorStyle = TsgcHTMLColor.hcSuccess;
            oBar.Label_ = "App 45%";
            oBar = oProgress.Bars.Add();
            oBar.Value = 25;
            oBar.ColorStyle = TsgcHTMLColor.hcWarning;
            oBar.Label_ = "Cache 25%";
            oBar = oProgress.Bars.Add();
            oBar.Value = 15;
            oBar.ColorStyle = TsgcHTMLColor.hcDanger;
            oBar.Label_ = "Logs 15%";
            string vRight = "<p class=\"small text-muted mb-1\">Disk usage by area " +
                "(stacked)</p>" + oProgress.HTML;
            oRoot.AddRaw(SectionCard("ProgressBar",
                "Striped + animated single bar, and a stacked multi-segment bar.",
                TwoCols(vLeft, vRight)));

            // --- badges + chips --- //
            TsgcHTMLContainer oBadges = new TsgcHTMLContainer("div");
            oBadges.CSSClass = "d-flex flex-wrap gap-2 align-items-center";
            oBadges.AddRaw(TsgcHTMLComponent_Badge.Build("Enterprise",
                TsgcHTMLBadgeStyle.bgPrimary, false));
            oBadges.AddRaw(TsgcHTMLComponent_Badge.Build("Paid",
                TsgcHTMLBadgeStyle.bgSuccess, true));
            oBadges.AddRaw(TsgcHTMLComponent_Badge.Build("Overdue",
                TsgcHTMLBadgeStyle.bgDanger, true));
            oBadges.AddRaw(TsgcHTMLComponent_Badge.Build("Trial",
                TsgcHTMLBadgeStyle.bgWarning, false));
            oBadges.AddRaw(TsgcHTMLComponent_Badge.Build("Archived",
                TsgcHTMLBadgeStyle.bgSecondary, false));
            oBadges.AddRaw("<span class=\"vr mx-2\"></span>");
            oBadges.AddRaw(TsgcHTMLComponent_Chip.Build("Delphi",
                TsgcHTMLBadgeStyle.bgPrimary, true));
            oBadges.AddRaw(TsgcHTMLComponent_Chip.Build("WebSockets",
                TsgcHTMLBadgeStyle.bgInfo, true));
            oBadges.AddRaw(TsgcHTMLComponent_Chip.Build("MQTT",
                TsgcHTMLBadgeStyle.bgSecondary, true));
            oBadges.AddRaw(TsgcHTMLComponent_Chip.Build("read-only",
                TsgcHTMLBadgeStyle.bgDark, false));
            oRoot.AddRaw(SectionCard("Badge + Chip",
                "Status badges (pill and square) and dismissible chips.",
                oBadges.HTML));

            // --- sparklines --- //
            TsgcHTMLComponent_Sparkline oSpark = new TsgcHTMLComponent_Sparkline();
            oSpark.ChartType = TsgcHTMLSparklineType.slLine;
            oSpark.Width = 140;
            oSpark.Height = 36;
            oSpark.LineColor = "#0d6efd";
            oSpark.ShowLastPoint = true;
            oSpark.SetData(new double[] { 18, 22, 19, 27, 31, 28, 35, 33, 41, 44, 39, 48 });
            string vSparks = "<div class=\"d-flex align-items-center gap-3 mb-2\">" +
                "<span class=\"text-muted small\" style=\"width:120px;\">Revenue</span>" +
                oSpark.HTML + "<b class=\"ms-2\">48.2k</b></div>";

            oSpark = new TsgcHTMLComponent_Sparkline();
            oSpark.ChartType = TsgcHTMLSparklineType.slBar;
            oSpark.Width = 140;
            oSpark.Height = 36;
            oSpark.LineColor = "#198754";
            oSpark.SetData(new double[] { 5, 9, 7, 12, 8, 14, 11, 16, 13, 18, 15, 21 });
            vSparks += "<div class=\"d-flex align-items-center gap-3 mb-2\">" +
                "<span class=\"text-muted small\" style=\"width:120px;\">New signups" +
                "</span>" + oSpark.HTML + "<b class=\"ms-2\">21</b></div>";

            oSpark = new TsgcHTMLComponent_Sparkline();
            oSpark.ChartType = TsgcHTMLSparklineType.slArea;
            oSpark.Width = 140;
            oSpark.Height = 36;
            oSpark.LineColor = "#dc3545";
            oSpark.FillColor = "#dc3545";
            oSpark.ShowMinMax = true;
            oSpark.SetData(new double[] { 120, 98, 143, 165, 132, 178, 154, 190, 210, 176, 199, 188 });
            vSparks += "<div class=\"d-flex align-items-center gap-3\">" +
                "<span class=\"text-muted small\" style=\"width:120px;\">Error rate</span>" +
                oSpark.HTML + "<b class=\"ms-2\">188</b></div>";
            oRoot.AddRaw(SectionCard("Sparkline",
                "Line, bar and area micro-charts, rendered server-side as inline SVG.",
                vSparks));

            // --- splitter --- //
            TsgcHTMLComponent_Splitter oSplitter = new TsgcHTMLComponent_Splitter();
            oSplitter.SplitterID = "sgcServersSplit";
            oSplitter.Orientation = TsgcHTMLSplitterOrientation.soHorizontal;
            oSplitter.InitialSplit = 35;
            oSplitter.MinSizeA = 150;
            oSplitter.MinSizeB = 200;
            oSplitter.CSSHeight = "260px";
            oSplitter.PersistKey = "sgc-demo-split";
            oSplitter.AddPaneA("<div class=\"p-3\"><h6>Servers</h6>" +
                "<ul class=\"list-group list-group-flush\">" +
                "<li class=\"list-group-item d-flex justify-content-between\">web-01" +
                "<span class=\"badge bg-success\">up</span></li>" +
                "<li class=\"list-group-item d-flex justify-content-between\">web-02" +
                "<span class=\"badge bg-success\">up</span></li>" +
                "<li class=\"list-group-item d-flex justify-content-between\">db-01" +
                "<span class=\"badge bg-warning text-dark\">degraded</span></li>" +
                "<li class=\"list-group-item d-flex justify-content-between\">cache-01" +
                "<span class=\"badge bg-danger\">down</span></li></ul></div>");
            oSplitter.AddPaneB("<div class=\"p-3\"><h6>db-01</h6>" +
                "<dl class=\"row mb-0 small\">" +
                "<dt class=\"col-4\">Role</dt><dd class=\"col-8\">PostgreSQL primary</dd>" +
                "<dt class=\"col-4\">Region</dt><dd class=\"col-8\">eu-west-1</dd>" +
                "<dt class=\"col-4\">CPU</dt><dd class=\"col-8\">78%</dd>" +
                "<dt class=\"col-4\">Connections</dt><dd class=\"col-8\">184 / 200</dd>" +
                "<dt class=\"col-4\">Replication</dt><dd class=\"col-8\">lag 3.2s</dd>" +
                "</dl></div>");
            oRoot.AddRaw(SectionCard("Splitter",
                "Draggable gutter; the split position is persisted in localStorage.",
                oSplitter.HTML));

            // --- context menu --- //
            TsgcHTMLComponent_ContextMenu oMenu = new TsgcHTMLComponent_ContextMenu();
            oMenu.MenuID = "sgcDemoContextMenu";
            oMenu.TargetSelector = "#ctx-target";

            TsgcHTMLContextMenuItem oMenuItem = oMenu.Items.Add();
            oMenuItem.Header = true;
            oMenuItem.Caption = "Invoice INV-2026-0184";

            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Open";
            oMenuItem.Icon = "bi bi-box-arrow-up-right";
            oMenuItem.DataAction = "invoice:open";

            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Duplicate";
            oMenuItem.Icon = "bi bi-files";
            oMenuItem.DataAction = "invoice:duplicate";

            oMenuItem = oMenu.Items.Add();
            oMenuItem.Divider = true;

            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Send to customer";
            oMenuItem.Icon = "bi bi-envelope";
            oMenuItem.DataAction = "invoice:send";

            oMenuItem = oMenu.Items.Add();
            oMenuItem.Caption = "Delete";
            oMenuItem.Icon = "bi bi-trash";
            oMenuItem.DataAction = "invoice:delete";
            oMenuItem.Disabled = true;

            oRoot.AddRaw(SectionCard("ContextMenu",
                "Right-click the dashed area below. Selecting an entry fires the " +
                "sgcContextMenu:action DOM event with its data-sgc-action.",
                "<div id=\"ctx-target\" class=\"border border-2 rounded p-5 " +
                "text-center text-muted\" style=\"border-style:dashed;\">" +
                "Right-click here</div>" + oMenu.HTML));

            return oRoot.HTML;
        }

        // ----- /codes ----- //

        private static string BuildCodesBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            TsgcHTMLComponent_QRCode oQR = new TsgcHTMLComponent_QRCode();
            oQR.Data = "https://www.esegece.com/websockets";
            oQR.ECCLevel = TsgcHTMLQRECCLevel.qrQuartile;
            oQR.ModuleSize = 4;
            oQR.QuietZone = 4;
            oQR.Caption = "https://www.esegece.com/websockets";
            oQR.CaptionVisible = true;
            string vLeft = SectionCard("QRCode",
                "Pure-managed QR encoder: no JavaScript, no external service. The " +
                "SVG below was generated by the server.", oQR.HTML);

            TsgcHTMLComponent_Barcode oBar = new TsgcHTMLComponent_Barcode();
            oBar.Data = "SGC-2026-000184";
            oBar.Symbology = TsgcHTMLBarcodeSymbology.bsCode128;
            oBar.ModuleWidth = 2;
            oBar.Height = 70;
            oBar.ShowText = true;
            string vRight = SectionCard("Barcode (Code 128)",
                "Server-rendered Code 128 SVG, ready for a picking list or a label " +
                "printer.", oBar.HTML);
            oRoot.AddRaw(TwoCols(vLeft, vRight));

            TsgcHTMLComponent_SignaturePad oPad = new TsgcHTMLComponent_SignaturePad();
            oPad.PadID = "sgcDeliverySign";
            oPad.Width = 460;
            oPad.Height = 200;
            oPad.PenColor = "#1a1a2e";
            oPad.PenWidth = 2;
            oPad.FieldName = "delivery_signature";
            oPad.ShowClear = true;
            oPad.ShowUndo = true;
            oPad.UploadURL = "/codes/signature";
            oPad.SaveCaption = "Save signature";
            oRoot.AddRaw(SectionCard("SignaturePad",
                "Canvas capture with undo/clear; the stroke is posted as a data URL " +
                "in a hidden field.", oPad.HTML));

            return oRoot.HTML;
        }

        // ----- /data ----- //

        private static void FillPivotData(TsgcHTMLComponent_PivotTable aPivot)
        {
            aPivot.DataFields.Clear();
            aPivot.DataFields.Add("Region");
            aPivot.DataFields.Add("Product");
            aPivot.DataFields.Add("Amount");

            aPivot.AddRow(new string[] { "North", "Licenses", "48200" });
            aPivot.AddRow(new string[] { "North", "Support", "12400" });
            aPivot.AddRow(new string[] { "North", "Training", "5200" });
            aPivot.AddRow(new string[] { "South", "Licenses", "31900" });
            aPivot.AddRow(new string[] { "South", "Support", "9800" });
            aPivot.AddRow(new string[] { "South", "Training", "3100" });
            aPivot.AddRow(new string[] { "East", "Licenses", "27450" });
            aPivot.AddRow(new string[] { "East", "Support", "7350" });
            aPivot.AddRow(new string[] { "East", "Training", "2400" });
            aPivot.AddRow(new string[] { "West", "Licenses", "52100" });
            aPivot.AddRow(new string[] { "West", "Support", "15600" });
            aPivot.AddRow(new string[] { "West", "Training", "6900" });
        }

        // Bill of materials, loaded into the Grid in TREE mode (id / parent_id).
        private static void FillBOMGrid(TsgcHTMLComponent_Grid aGrid)
        {
            TsgcHTMLGridColumn oCol = aGrid.Columns.Add();
            oCol.Name = "id";
            oCol.Title = "Id";
            oCol.Width = "70px";

            oCol = aGrid.Columns.Add();
            oCol.Name = "parent_id";
            oCol.Title = "Parent";
            oCol.Width = "80px";

            oCol = aGrid.Columns.Add();
            oCol.Name = "part";
            oCol.Title = "Part";

            oCol = aGrid.Columns.Add();
            oCol.Name = "qty";
            oCol.Title = "Qty";
            oCol.Align = TsgcHTMLGridAlign.gaRight;
            oCol.Width = "80px";

            oCol = aGrid.Columns.Add();
            oCol.Name = "cost";
            oCol.Title = "Unit cost";
            oCol.Align = TsgcHTMLGridAlign.gaRight;
            oCol.Width = "110px";

            aGrid.AddRow(new string[] { "1", "", "Industrial Router IR-900", "1", "1240.00" });
            aGrid.AddRow(new string[] { "2", "1", "Mainboard assembly", "1", "540.00" });
            aGrid.AddRow(new string[] { "3", "2", "SoC MT7621A", "1", "86.00" });
            aGrid.AddRow(new string[] { "4", "2", "DDR3 RAM 512MB", "2", "9.40" });
            aGrid.AddRow(new string[] { "5", "2", "NAND flash 128MB", "1", "6.20" });
            aGrid.AddRow(new string[] { "6", "1", "Radio module 802.11ac", "2", "78.50" });
            aGrid.AddRow(new string[] { "7", "6", "Antenna 5dBi", "2", "4.10" });
            aGrid.AddRow(new string[] { "8", "1", "Enclosure (die-cast)", "1", "112.00" });
            aGrid.AddRow(new string[] { "9", "8", "Mounting bracket", "2", "7.80" });
            aGrid.AddRow(new string[] { "10", "1", "Power supply 12V/2A", "1", "31.00" });

            aGrid.TreeMode = true;
            aGrid.TreeIdField = "id";
            aGrid.TreeParentIdField = "parent_id";
            aGrid.TreeExpandAll = true;
        }

        public static void BuildDataXLSX(Stream aStream)
        {
            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.ExportSheetName = "BOM";
            FillBOMGrid(oGrid);
            oGrid.SaveToXLSXStream(aStream);
        }

        private static string BuildDataBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            // --- PivotTable --- //
            TsgcHTMLComponent_PivotTable oPivot = new TsgcHTMLComponent_PivotTable();
            oPivot.TableID = "sgcSalesPivot";
            oPivot.Caption = "Sales 2026 by region and product line";

            TsgcHTMLPivotField oField = oPivot.RowFields.Add();
            oField.FieldName = "Region";
            oField.Caption = "Region";

            oField = oPivot.ColumnFields.Add();
            oField.FieldName = "Product";
            oField.Caption = "Product line";

            TsgcHTMLPivotMeasure oMeasure = oPivot.Measures.Add();
            oMeasure.SourceField = "Amount";
            oMeasure.Caption = "Revenue";
            oMeasure.Aggregation = TsgcHTMLPivotAggregation.paSum;
            oMeasure.Format = "#,##0";

            oPivot.ShowRowTotals = true;
            oPivot.ShowColumnTotals = true;
            oPivot.ShowGrandTotal = true;
            oPivot.TotalText = "Total";
            FillPivotData(oPivot);

            oRoot.AddRaw(SectionCard("PivotTable",
                "Region x product line, summed server-side, with row totals, column " +
                "totals and the grand total.", oPivot.HTML));

            // --- TreeGrid (org chart) --- //
            TsgcHTMLComponent_TreeGrid oTree = new TsgcHTMLComponent_TreeGrid();
            oTree.TreeGridID = "sgcOrgTree";
            oTree.IndentPixels = 22;
            oTree.ExpandedByDefault = true;

            TsgcHTMLTreeGridColumn oTCol = oTree.Columns.Add();
            oTCol.Caption = "Unit / person";
            oTCol.FieldName = "name";
            oTCol.Width = "45%";

            oTCol = oTree.Columns.Add();
            oTCol.Caption = "Role";
            oTCol.FieldName = "role";

            oTCol = oTree.Columns.Add();
            oTCol.Caption = "Headcount";
            oTCol.FieldName = "headcount";
            oTCol.Align = TsgcHTMLTreeGridAlign.tgaRight;

            oTCol = oTree.Columns.Add();
            oTCol.Caption = "Budget";
            oTCol.FieldName = "budget";
            oTCol.Align = TsgcHTMLTreeGridAlign.tgaRight;

            oTree.AddNode("c", "", new string[] { "eSeGeCe", "Company", "48", "4,120,000" });
            oTree.AddNode("eng", "c", new string[] { "Engineering", "Department", "26", "2,480,000" });
            oTree.AddNode("eng1", "eng", new string[] { "Sergio Garcia", "Head of Engineering", "1", "-" });
            oTree.AddNode("eng2", "eng", new string[] { "Core libraries", "Team", "11", "1,050,000" });
            oTree.AddNode("eng3", "eng", new string[] { "Tooling & QA", "Team", "8", "720,000" });
            oTree.AddNode("eng4", "eng", new string[] { "Documentation", "Team", "6", "480,000" });
            oTree.AddNode("sal", "c", new string[] { "Sales", "Department", "13", "980,000" });
            oTree.AddNode("sal1", "sal", new string[] { "Marta Ruiz", "VP Sales", "1", "-" });
            oTree.AddNode("sal2", "sal", new string[] { "EMEA", "Region", "7", "540,000" });
            oTree.AddNode("sal3", "sal", new string[] { "Americas", "Region", "5", "440,000" });
            oTree.AddNode("sup", "c", new string[] { "Support", "Department", "9", "660,000" });
            oTree.AddNode("sup1", "sup", new string[] { "Tier 1", "Team", "6", "360,000" });
            oTree.AddNode("sup2", "sup", new string[] { "Tier 2", "Team", "3", "300,000" });

            oRoot.AddRaw(SectionCard("TreeGrid",
                "An org hierarchy: expandable parent rows, aligned numeric columns.",
                oTree.HTML));

            // --- Grid in TREE mode + XLSX + columns menu --- //
            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "sgcBOMGrid";
            oGrid.ShowSort = true;
            oGrid.ShowFilter = true;
            oGrid.ShowColumnsMenu = true;
            oGrid.ColumnsText = "Columns";
            oGrid.ExportXLSX = true;
            oGrid.ExportURL = "/data/export.xlsx";
            oGrid.ExportXLSXText = "Export XLSX";
            oGrid.ExportSheetName = "BOM";
            FillBOMGrid(oGrid);

            oRoot.AddRaw(SectionCard("Grid - tree mode + XLSX export + columns menu",
                "The same Grid in hierarchical mode (id / parent_id). \"Export XLSX\" " +
                "downloads a real workbook built by the server with SaveToXLSXStream; " +
                "\"Columns\" toggles column visibility.", oGrid.HTML));

            // --- Grid master-detail --- //
            TsgcOrderDetailProvider oDetail = new TsgcOrderDetailProvider();
            oGrid = new TsgcHTMLComponent_Grid();
            oGrid.TableID = "sgcOrdersGrid";
            oGrid.MasterDetail = true;
            oGrid.OnGetDetailHTML += oDetail.GetDetailHTML;
            oGrid.ShowSort = true;

            TsgcHTMLGridColumn oGCol = oGrid.Columns.Add();
            oGCol.Name = "order";
            oGCol.Title = "Order";

            oGCol = oGrid.Columns.Add();
            oGCol.Name = "customer";
            oGCol.Title = "Customer";

            oGCol = oGrid.Columns.Add();
            oGCol.Name = "date";
            oGCol.Title = "Date";

            oGCol = oGrid.Columns.Add();
            oGCol.Name = "total";
            oGCol.Title = "Total";
            oGCol.Align = TsgcHTMLGridAlign.gaRight;

            oGrid.AddRow(new string[] { "SO-2026-0912", "Contoso Ltd", "2026-07-02", "2.788,00" });
            oGrid.AddRow(new string[] { "SO-2026-0913", "Northwind GmbH", "2026-07-03", "1.485,00" });
            oGrid.AddRow(new string[] { "SO-2026-0914", "Fabrikam SA", "2026-07-05", "1.485,00" });
            oGrid.AddRow(new string[] { "SO-2026-0915", "Adventure Works", "2026-07-06", "390,00" });

            oRoot.AddRaw(SectionCard("Grid - master / detail",
                "Click the chevron on a row to expand the order lines. The detail " +
                "HTML is produced server-side by the OnGetDetailHTML event.",
                oGrid.HTML));

            return oRoot.HTML;
        }

        // ----- /charts ----- //

        private static readonly int[,] CS_TRAFFIC = new int[,]
        {
            { 12, 8, 30, 92, 140, 155, 120, 48 },
            { 14, 9, 34, 101, 152, 168, 131, 52 },
            { 13, 10, 36, 110, 161, 176, 139, 55 },
            { 15, 11, 38, 118, 170, 188, 146, 60 },
            { 18, 12, 41, 126, 182, 201, 158, 71 },
            { 9, 6, 15, 38, 52, 61, 44, 28 },
            { 7, 5, 11, 27, 39, 45, 33, 21 }
        };

        private static string BuildChartsBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            // --- Heatmap --- //
            TsgcHTMLComponent_Heatmap oHeat = new TsgcHTMLComponent_Heatmap();
            foreach (string vD in new string[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" })
                oHeat.RowLabels.Add(vD);
            foreach (string vH in new string[] { "00h", "03h", "06h", "09h", "12h", "15h", "18h", "21h" })
                oHeat.ColumnLabels.Add(vH);

            for (int vR = 0; vR < 7; vR++)
                for (int vC = 0; vC < 8; vC++)
                    oHeat.SetCell(vR, vC, CS_TRAFFIC[vR, vC]);

            oHeat.CellSize = 52;
            oHeat.CellGap = 3;
            oHeat.ShowValues = true;
            oHeat.ShowLegend = true;
            oHeat.Rounded = true;
            oHeat.MinColor = "#e3f2fd";
            oHeat.MaxColor = "#0d47a1";

            oRoot.AddRaw(SectionCard("Heatmap",
                "Requests per weekday and hour bucket. Server-rendered SVG, one rect " +
                "per cell, with a gradient legend.", oHeat.HTML));

            // --- TreeMap --- //
            TsgcHTMLComponent_TreeMap oTreeMap = new TsgcHTMLComponent_TreeMap();
            oTreeMap.Width = 820;
            oTreeMap.Height = 380;
            oTreeMap.ShowLabels = true;
            oTreeMap.ShowValues = true;
            oTreeMap.Decimals = 0;
            oTreeMap.ColorScheme = TsgcHTMLTreeMapScheme.tmCool;

            TsgcHTMLTreeMapItem oItem = oTreeMap.AddItem("sgcWebSockets", 159650);
            oItem.ID = "ws";
            oItem = oTreeMap.AddItem("Enterprise", 78400);
            oItem.ID = "ws-ent";
            oItem.ParentID = "ws";
            oItem = oTreeMap.AddItem("Professional", 52100);
            oItem.ID = "ws-pro";
            oItem.ParentID = "ws";
            oItem = oTreeMap.AddItem("Standard", 29150);
            oItem.ID = "ws-std";
            oItem.ParentID = "ws";

            oItem = oTreeMap.AddItem("sgcSign", 61200);
            oItem.ID = "sign";
            oItem = oTreeMap.AddItem("sgcOpenAPI", 24800);
            oItem.ID = "oapi";
            oItem = oTreeMap.AddItem("sgcBiometrics", 14300);
            oItem.ID = "bio";
            oItem = oTreeMap.AddItem("Training & support", 33500);
            oItem.ID = "svc";

            oRoot.AddRaw(SectionCard("TreeMap",
                "Revenue by product line; sgcWebSockets is broken down into its " +
                "editions (a parent rectangle with children squarified inside it).",
                oTreeMap.HTML));

            // --- CandlestickChart --- //
            TsgcHTMLComponent_CandlestickChart oCandle =
                new TsgcHTMLComponent_CandlestickChart();
            oCandle.Width = 860;
            oCandle.Height = 420;
            oCandle.ShowVolume = true;
            oCandle.ShowGrid = true;
            oCandle.ShowAxis = true;
            oCandle.Decimals = 2;
            oCandle.GridLines = 6;

            oCandle.AddPoint("01/07", 182.10, 185.40, 181.20, 184.90, 42100);
            oCandle.AddPoint("02/07", 184.90, 186.20, 183.10, 183.60, 38400);
            oCandle.AddPoint("03/07", 183.60, 184.00, 179.80, 180.40, 51200);
            oCandle.AddPoint("06/07", 180.40, 182.90, 179.90, 182.50, 33900);
            oCandle.AddPoint("07/07", 182.50, 188.10, 182.20, 187.70, 61800);
            oCandle.AddPoint("08/07", 187.70, 189.90, 186.40, 186.90, 47300);
            oCandle.AddPoint("09/07", 186.90, 187.30, 182.60, 183.10, 55600);
            oCandle.AddPoint("10/07", 183.10, 185.80, 182.80, 185.40, 40200);
            oCandle.AddPoint("13/07", 185.40, 191.20, 185.10, 190.60, 72400);
            oCandle.AddPoint("14/07", 190.60, 192.40, 188.70, 189.20, 58100);
            oCandle.AddPoint("15/07", 189.20, 190.10, 185.30, 186.00, 49700);
            oCandle.AddPoint("16/07", 186.00, 186.60, 181.40, 182.10, 63500);
            oCandle.AddPoint("17/07", 182.10, 184.70, 181.60, 184.30, 41800);
            oCandle.AddPoint("20/07", 184.30, 188.90, 184.00, 188.40, 52900);
            oCandle.AddPoint("21/07", 188.40, 190.70, 187.20, 190.10, 46300);
            oCandle.AddPoint("22/07", 190.10, 194.30, 189.80, 193.80, 81200);
            oCandle.AddPoint("23/07", 193.80, 195.10, 191.40, 192.20, 57400);
            oCandle.AddPoint("24/07", 192.20, 193.00, 188.10, 188.90, 60100);
            oCandle.AddPoint("27/07", 188.90, 191.60, 188.40, 191.20, 44700);
            oCandle.AddPoint("28/07", 191.20, 196.80, 190.90, 196.10, 93500);

            oRoot.AddRaw(SectionCard("CandlestickChart",
                "Twenty OHLC bars with a volume sub-chart. Green when the close is " +
                "above the open, red otherwise; hover a candle for its OHLC tooltip.",
                oCandle.HTML));

            return oRoot.HTML;
        }

        // ----- /admin ----- //

        private static string BuildAdminBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            // --- ImpersonateBanner --- //
            TsgcHTMLComponent_ImpersonateBanner oBanner =
                new TsgcHTMLComponent_ImpersonateBanner();
            oBanner.UserName = "marta.ruiz";
            oBanner.Color = TsgcHTMLColor.hcWarning;
            oBanner.Sticky = false;
            oBanner.StopURL = "/admin";
            oRoot.AddRaw(oBanner.HTML);

            // --- UserManagement --- //
            TsgcHTMLComponent_UserManagement oUsers =
                new TsgcHTMLComponent_UserManagement();
            oUsers.TableID = "sgcUsers";
            oUsers.ShowSearch = true;
            oUsers.ShowAddButton = true;
            oUsers.AllowImpersonate = true;
            oUsers.AllowDelete = true;
            oUsers.ShowLastLogin = true;

            TsgcHTMLUser oUser = oUsers.AddUser("1", "sergio.garcia", "sergio@esegece.com");
            oUser.DisplayName = "Sergio Garcia";
            oUser.Roles = "admin,developer";
            oUser.Status = TsgcHTMLUserStatus.usActive;
            oUser.LastLogin = "2026-07-11 08:12";

            oUser = oUsers.AddUser("2", "marta.ruiz", "marta@esegece.com");
            oUser.DisplayName = "Marta Ruiz";
            oUser.Roles = "sales,manager";
            oUser.Status = TsgcHTMLUserStatus.usActive;
            oUser.LastLogin = "2026-07-11 07:48";

            oUser = oUsers.AddUser("3", "tom.becker", "tom@esegece.com");
            oUser.DisplayName = "Tom Becker";
            oUser.Roles = "developer";
            oUser.Status = TsgcHTMLUserStatus.usActive;
            oUser.LastLogin = "2026-07-10 18:31";

            oUser = oUsers.AddUser("4", "aiko.tanaka", "aiko@esegece.com");
            oUser.DisplayName = "Aiko Tanaka";
            oUser.Roles = "support";
            oUser.Status = TsgcHTMLUserStatus.usInvited;
            oUser.LastLogin = "never";

            oUser = oUsers.AddUser("5", "paul.dupont", "paul@esegece.com");
            oUser.DisplayName = "Paul Dupont";
            oUser.Roles = "sales";
            oUser.Status = TsgcHTMLUserStatus.usSuspended;
            oUser.LastLogin = "2026-06-28 11:05";

            oRoot.AddRaw(SectionCard("UserManagement",
                "Row actions (edit, disable, reset password, impersonate, delete) are " +
                "sent to the server over the WebSocket and surface in OnUserAction.",
                oUsers.HTML));

            // --- RolesPermissions --- //
            TsgcHTMLComponent_RolesPermissions oRoles =
                new TsgcHTMLComponent_RolesPermissions();
            oRoles.MatrixID = "sgcRoles";
            oRoles.ShowCategories = true;
            oRoles.ShowDescriptions = true;
            oRoles.StickyHeader = true;

            oRoles.AddRole("admin", "Administrator");
            oRoles.AddRole("manager", "Manager");
            oRoles.AddRole("developer", "Developer");
            oRoles.AddRole("sales", "Sales");
            oRoles.AddRole("support", "Support");

            oRoles.AddPermission("inv.view", "View invoices", "Invoicing");
            oRoles.AddPermission("inv.edit", "Create / edit invoices", "Invoicing");
            oRoles.AddPermission("inv.void", "Void an invoice", "Invoicing");
            oRoles.AddPermission("cust.view", "View customers", "CRM");
            oRoles.AddPermission("cust.edit", "Edit customers", "CRM");
            oRoles.AddPermission("rel.deploy", "Deploy a release", "Engineering");
            oRoles.AddPermission("rel.rollback", "Roll back a release", "Engineering");
            oRoles.AddPermission("usr.manage", "Manage users", "Administration");
            oRoles.AddPermission("usr.impersonate", "Impersonate a user", "Administration");

            // Admin: everything.
            oRoles.SetGrant("admin", "inv.view", true);
            oRoles.SetGrant("admin", "inv.edit", true);
            oRoles.SetGrant("admin", "inv.void", true);
            oRoles.SetGrant("admin", "cust.view", true);
            oRoles.SetGrant("admin", "cust.edit", true);
            oRoles.SetGrant("admin", "rel.deploy", true);
            oRoles.SetGrant("admin", "rel.rollback", true);
            oRoles.SetGrant("admin", "usr.manage", true);
            oRoles.SetGrant("admin", "usr.impersonate", true);

            oRoles.SetGrant("manager", "inv.view", true);
            oRoles.SetGrant("manager", "inv.edit", true);
            oRoles.SetGrant("manager", "inv.void", true);
            oRoles.SetGrant("manager", "cust.view", true);
            oRoles.SetGrant("manager", "cust.edit", true);

            oRoles.SetGrant("developer", "rel.deploy", true);
            oRoles.SetGrant("developer", "rel.rollback", true);
            oRoles.SetGrant("developer", "cust.view", true);

            oRoles.SetGrant("sales", "cust.view", true);
            oRoles.SetGrant("sales", "cust.edit", true);
            oRoles.SetGrant("sales", "inv.view", true);

            oRoles.SetGrant("support", "cust.view", true);
            oRoles.SetGrant("support", "inv.view", true);

            oRoot.AddRaw(SectionCard("RolesPermissions",
                "Role x permission matrix grouped by category. Toggling a checkbox " +
                "sends {\"action\":\"grantChanged\"} to the server.", oRoles.HTML));

            return oRoot.HTML;
        }

        // ----- /live ----- //

        private static string BuildLiveBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            if (gLive != null)
                oRoot.AddRaw(gLive.RenderLive());

            TsgcHTMLComponent_AuditTrail oAudit = new TsgcHTMLComponent_AuditTrail();
            oAudit.AuditID = "sgcAudit";
            oAudit.Title = "Audit trail";
            oAudit.ShowFilter = true;
            oAudit.PageSize = 10;
            oAudit.AddEntry("sergio.garcia", "login", "auth", "10.0.0.14", "success");
            oAudit.AddEntry("marta.ruiz", "invoice.approve", "INV-2026-0184",
                "10.0.0.31", "success", "Amount 2.788,00 EUR");
            oAudit.AddEntry("tom.becker", "release.deploy", "build #482",
                "10.0.0.52", "success", "staging");
            oAudit.AddEntry("unknown", "login", "auth", "203.0.113.9", "failed",
                "Bad password (3rd attempt)");
            oAudit.AddEntry("aiko.tanaka", "user.impersonate", "paul.dupont",
                "10.0.0.77", "denied", "Missing usr.impersonate permission");
            oAudit.AddEntry("paul.dupont", "customer.export", "customers.csv",
                "10.0.0.61", "success", "1.284 rows");
            oAudit.AddEntry("marta.ruiz", "invoice.void", "INV-2026-0161",
                "10.0.0.31", "success", "Duplicate");
            oAudit.AddEntry("tom.becker", "release.rollback", "build #481",
                "10.0.0.52", "error", "Migration 0043 failed");

            oRoot.AddRaw(SectionCard("AuditTrail",
                "A filterable, paginated audit log (client-side filter, server-side " +
                "data).", oAudit.HTML));

            return oRoot.HTML;
        }

        // ----- /docs ----- //

        private static string BuildDocsBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");

            TsgcHTMLComponent_PDFViewer oPDF = new TsgcHTMLComponent_PDFViewer();
            oPDF.ViewerID = "sgcDocsViewer";
            oPDF.PDFURL = "/docs/sample.pdf";
            oPDF.CSSHeight = "640px";
            oPDF.InitialPage = 1;
            oPDF.Zoom = "page-width";
            oPDF.ShowToolbar = true;
            oPDF.ShowPageNav = true;
            oPDF.ShowZoomControls = true;
            oPDF.ShowDownload = true;
            oPDF.ShowPrint = true;
            oPDF.ShowSearch = true;
            oPDF.DownloadFileName = "sgcHTML-sample.pdf";

            oRoot.AddRaw(SectionCard("PDFViewer",
                "Embedded pdf.js viewer with page navigation, zoom, search, print and " +
                "download. The document is served by this demo at /docs/sample.pdf.",
                oPDF.HTML));

            return oRoot.HTML;
        }

        // ----- / (overview) ----- //

        private static string Tile(string aIcon, string aTitle, string aHref,
            string aText)
        {
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "h-100 shadow-sm";
            oCard.Body.AddRaw("<div class=\"d-flex align-items-center gap-2 mb-2\">" +
                aIcon + "<h5 class=\"mb-0\">" + aTitle + "</h5></div>" +
                "<p class=\"text-muted small\">" + aText + "</p>" + "<a href=\"" +
                aHref + "\" class=\"btn btn-sm btn-primary stretched-link\">Open</a>");
            return oCard.HTML;
        }

        private static string BuildHomeBody()
        {
            TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");
            oRoot.AddRaw("<div class=\"p-4 mb-4 bg-body-tertiary rounded-3\">" +
                "<h1 class=\"display-6\">31 new sgcHTML components</h1>" +
                "<p class=\"lead mb-0\">Every page below is plain C#: the components " +
                "render server-side HTML, no JavaScript framework and no build step. " +
                "The <a href=\"/live\">Live</a> page adds server push over the " +
                "WebSocket.</p></div>");

            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_INPUTS, "Inputs", "/inputs",
                "MultiSelect, Transfer, TimePicker, DateTimePicker, DateRangePicker, " +
                "Slider, RangeSlider, ColorPicker, AutoComplete."));
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_DISPLAY, "Display", "/display",
                "ProgressBar, Badge, Chip, Splitter, ContextMenu, Sparkline."));
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_CODES, "Codes", "/codes",
                "QRCode, Barcode (Code 128) and SignaturePad."));
            oRoot.AddRaw(oRow.HTML);

            oRoot.AddRaw("<div class=\"mb-3\"></div>");

            oRow = new TsgcHTMLRow();
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_DATA, "Data", "/data",
                "PivotTable, TreeGrid and the Grid in tree mode, with master-detail, " +
                "XLSX export and a columns chooser."));
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_CHARTS, "Charts", "/charts",
                "Heatmap, TreeMap and CandlestickChart, all server-rendered SVG."));
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_LIVE, "Live", "/live",
                "Presence, ActivityFeed, JobProgress, LogViewer and AuditTrail, " +
                "pushed over the WebSocket."));
            oRoot.AddRaw(oRow.HTML);

            oRoot.AddRaw("<div class=\"mb-3\"></div>");

            oRow = new TsgcHTMLRow();
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_ADMIN, "Admin", "/admin",
                "UserManagement, ImpersonateBanner and the RolesPermissions matrix."));
            oRow.Col(TsgcHTMLColWidth.cw4).AddRaw(Tile(CS_BI_DOCS, "Docs", "/docs",
                "PDFViewer with navigation, zoom, search, print and download."));
            oRoot.AddRaw(oRow.HTML);

            return oRoot.HTML;
        }

        // ----- page shell ----- //

        // Route -> full page HTML. Unknown routes return "" (the host 404s).
        public static string BuildPage(string aRoute)
        {
            string vRoute = (aRoute ?? "").Trim().ToLowerInvariant();
            if (vRoute == "")
                vRoute = "/";

            string vBody;
            if (vRoute == "/")
                vBody = BuildHomeBody();
            else if (vRoute == "/inputs")
                vBody = BuildInputsBody();
            else if (vRoute == "/display")
                vBody = BuildDisplayBody();
            else if (vRoute == "/codes")
                vBody = BuildCodesBody();
            else if (vRoute == "/data")
                vBody = BuildDataBody();
            else if (vRoute == "/charts")
                vBody = BuildChartsBody();
            else if (vRoute == "/live")
                vBody = BuildLiveBody();
            else if (vRoute == "/admin")
                vBody = BuildAdminBody();
            else if (vRoute == "/docs")
                vBody = BuildDocsBody();
            else
                return ""; // unknown route -> the host answers 404

            TsgcHTMLComponent_Site oSite = new TsgcHTMLComponent_Site();
            oSite.Title = "sgcHTML Components Showcase";
            oSite.Layout = TsgcHTMLSiteLayout.slSidebarLeft;
            oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpBlue;
            oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmLight;
            oSite.Theme.SidebarDark = true;
            oSite.Brand.Text = "sgcHTML";
            oSite.Brand.Href = "/";
            oSite.Header.ShowThemeSwitcher = true;
            oSite.Header.ShowUser = true;
            oSite.Header.UserName = "Demo User";
            oSite.Footer.Text = "copyright (c) 2026 eSeGeCe.com";
            oSite.CustomHead = "<link rel=\"stylesheet\" href=\"https://cdn.jsdelivr." +
                "net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.css\">";

            oSite.AddMenu("Overview", "/", "<i class=\"bi bi-grid\"></i>").Active =
                (vRoute == "/");
            oSite.AddMenu("Inputs", "/inputs", CS_BI_INPUTS).Active = (vRoute == "/inputs");
            oSite.AddMenu("Display", "/display", CS_BI_DISPLAY).Active = (vRoute == "/display");
            oSite.AddMenu("Codes", "/codes", CS_BI_CODES).Active = (vRoute == "/codes");
            oSite.AddMenu("Data", "/data", CS_BI_DATA).Active = (vRoute == "/data");
            oSite.AddMenu("Charts", "/charts", CS_BI_CHARTS).Active = (vRoute == "/charts");
            oSite.AddMenu("Live (WebSocket)", "/live", CS_BI_LIVE).Active = (vRoute == "/live");
            oSite.AddMenu("Admin", "/admin", CS_BI_ADMIN).Active = (vRoute == "/admin");
            oSite.AddMenu("Docs", "/docs", CS_BI_DOCS).Active = (vRoute == "/docs");

            oSite.AddContent(vBody);

            // The realtime client: htmx + the sgcWebSockets bridge. Loaded on every
            // page so the AutoComplete (/inputs) and the job Cancel button (/live)
            // can both talk back to the server over the same socket.
            oSite.BodyEndHTML = "<script src=\"/htmx.min.js\"></script>" +
                "<script src=\"/sgcWebSockets.js\"></script>" +
                "<script src=\"/sgcHTMX.min.js\"></script>" + "<script>" +
                "document.addEventListener(\"DOMContentLoaded\",function(){" +
                "if(window.sgcHTMX&&sgcHTMX.init){sgcHTMX.init({host:" +
                "(location.protocol==='https:'?'wss:':'ws:')+'//'+" +
                "location.host+'/'});}});</script>";

            return oSite.HTML;
        }
    }
}
