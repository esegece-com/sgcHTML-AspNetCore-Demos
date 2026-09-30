// ***************************************************************************
//  sgcInstrumentsWeb - control room demo of the sgcHTML instrumentation
//  components on ASP.NET Core
//  Mirror of demos\60.HTML\20.Instruments (TsgcWebSocketHTTPServer host,
//  sgcInstrumentsDemo_Server.cs).
//
//  Owns the simulated process state, the verbatim TInstrumentsPages view,
//  the control handlers (POST /ctl/<name> and POST /ack) and the per-page
//  live channel.
//
//  PER-PAGE PUSH: the sgcHTMX bridge of every page opens a WebSocket to the
//  page URL itself (location.pathname), exactly as in the 60.HTML demo. The
//  adapter accepts those upgrades (AcceptWebSocketOnAnyPath in Program.cs) and
//  keeps the upgrade path with each connection, so PushToPageAsync is a
//  filtered ISgcHtmlHub broadcast (connection path -> page key), which is what
//  PushToPage did over the TsgcWebSocketHTTPServer connection list (filtered
//  by connection URL). The hub bounds every send and drops a stuck client.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;

namespace InstrumentsDemo
{
    public sealed class InstrumentsWebHost
    {
        public const string CS_INSTRUMENTS_SERVER_VERSION = "1.0.0";

        // Self-contained eSeGeCe favicon (served at /favicon.svg).
        public const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"eSeGeCe\">" +
            "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"#0057B8\"/>" +
            "<text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"900\" font-size=\"44\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">e</text></svg>";

        private readonly string FBasePath;
        private readonly object FLock = new object();
        private readonly Random FRandom = new Random();
        private readonly TInstrumentsPages FPages;
        private readonly TInstrumentsState FState;
        private readonly ISgcHtmlHub FHub;
        private double FTime;
        private int FTicks;

        public InstrumentsWebHost(ISgcHtmlHub aHub)
        {
            FHub = aHub;
            FBasePath = "";
            FState = new TInstrumentsState();
            FPages = new TInstrumentsPages();
            FPages.BasePath = FBasePath;
        }

        // ----- page routing ----- //

        public string PageOfURL(string aURL)
        {
            string vURL = aURL ?? "";
            int i = vURL.IndexOf('?');
            if (i >= 0)
                vURL = vURL.Substring(0, i);
            if (FBasePath != "" && vURL.StartsWith(FBasePath, StringComparison.OrdinalIgnoreCase))
                vURL = vURL.Substring(FBasePath.Length);
            if (vURL.Length > 1 && vURL[vURL.Length - 1] == '/')
                vURL = vURL.Substring(0, vURL.Length - 1);
            if (vURL == "" || string.Equals(vURL, "/gauges", StringComparison.OrdinalIgnoreCase))
                vURL = TInstrumentsConst.CS_PAGE_GAUGES;
            return vURL.ToLowerInvariant();
        }

        public bool IsPage(string aPage)
        {
            return aPage == TInstrumentsConst.CS_PAGE_GAUGES ||
                aPage == TInstrumentsConst.CS_PAGE_PANELS ||
                aPage == TInstrumentsConst.CS_PAGE_CONTROLS ||
                aPage == TInstrumentsConst.CS_PAGE_TRENDS;
        }

        // GET of one of the 4 pages (404 otherwise, as DispatchRequest did)
        public IResult PageGet(HttpContext aContext)
        {
            string vPage = PageOfURL(aContext.Request.Path.Value);
            string vHTML = "";
            lock (FLock)
            {
                if (vPage == TInstrumentsConst.CS_PAGE_GAUGES)
                    vHTML = FPages.PageGauges(FState);
                else if (vPage == TInstrumentsConst.CS_PAGE_PANELS)
                    vHTML = FPages.PagePanels(FState);
                else if (vPage == TInstrumentsConst.CS_PAGE_CONTROLS)
                    vHTML = FPages.PageControls(FState);
                else if (vPage == TInstrumentsConst.CS_PAGE_TRENDS)
                    vHTML = FPages.PageTrends(FState);
            }
            if (vHTML == "")
                return Results.Text("Not found", "text/plain; charset=utf-8",
                    null, 404);
            return Results.Content(vHTML, "text/html; charset=utf-8");
        }

        // ----- per-page live channel ----- //

        // writes the fragment to every live socket opened by a page with this key
        private async Task PushToPageAsync(string aPage, string aHTML)
        {
            if (string.IsNullOrEmpty(aHTML))
                return;
            try
            {
                await FHub.BroadcastAsync(c => PageOfURL(c.Path) == aPage, aHTML)
                    .ConfigureAwait(false);
            }
            catch
            {
                // connection closing concurrently
            }
        }

        // ----- simulation (verbatim from the 60.HTML host) ----- //

        private void SetTile(bool[] aChanged, int aIndex, TsgcHTMLAnnunciatorState aState)
        {
            aChanged[aIndex] = false;
            if (FState.TileState[aIndex] == aState)
                return;
            // a new or escalated condition must be acknowledged again
            if ((int)aState > (int)FState.TileState[aIndex])
                FState.TileAck[aIndex] = false;
            if (aState == TsgcHTMLAnnunciatorState.ansNormal)
                FState.TileAck[aIndex] = false;
            FState.TileState[aIndex] = aState;
            aChanged[aIndex] = true;
        }

        private void EvaluateTiles(bool[] aChanged)
        {
            SetTile(aChanged, 0, FState.Pressure > FState.BandHigh / 10.0 ?
                TsgcHTMLAnnunciatorState.ansAlarm : TsgcHTMLAnnunciatorState.ansNormal);
            SetTile(aChanged, 1, FState.Pump && FState.ActualSpeed > 300 &&
                FState.Pressure < FState.BandLow / 10.0 ?
                TsgcHTMLAnnunciatorState.ansWarning : TsgcHTMLAnnunciatorState.ansNormal);
            TsgcHTMLAnnunciatorState vState;
            if (FState.Temperature > 120)
                vState = TsgcHTMLAnnunciatorState.ansAlarm;
            else if (FState.Temperature > 100)
                vState = TsgcHTMLAnnunciatorState.ansWarning;
            else
                vState = TsgcHTMLAnnunciatorState.ansNormal;
            SetTile(aChanged, 2, vState);
            SetTile(aChanged, 3, FState.TankLevel < 15 ?
                TsgcHTMLAnnunciatorState.ansWarning : TsgcHTMLAnnunciatorState.ansNormal);
            SetTile(aChanged, 4, FState.TankLevel > 90 ?
                TsgcHTMLAnnunciatorState.ansAlarm : TsgcHTMLAnnunciatorState.ansNormal);
            SetTile(aChanged, 5, FState.Pump ?
                TsgcHTMLAnnunciatorState.ansNormal : TsgcHTMLAnnunciatorState.ansAdvisory);
            SetTile(aChanged, 6, FState.Heater ?
                TsgcHTMLAnnunciatorState.ansAdvisory : TsgcHTMLAnnunciatorState.ansNormal);
            SetTile(aChanged, 7, FState.EStop ?
                TsgcHTMLAnnunciatorState.ansAlarm : TsgcHTMLAnnunciatorState.ansNormal);
        }

        private static double EnsureRange(double aValue, double aMin, double aMax)
        {
            if (aValue < aMin)
                return aMin;
            if (aValue > aMax)
                return aMax;
            return aValue;
        }

        private double Rnd()
        {
            return FRandom.NextDouble();
        }

        private void Simulate()
        {
            const double CD_DT = 0.5;
            double vTarget;
            FTime = FTime + CD_DT;
            if (FState.EStop)
            {
                FState.Pump = false;
                FState.Heater = false;
            }
            // pump speed ramps towards the request
            vTarget = FState.Pump ? FState.Speed : 0;
            FState.ActualSpeed = FState.ActualSpeed + (vTarget - FState.ActualSpeed) * 0.25;
            // pressure follows the setpoint while the pump runs
            if (FState.Pump && FState.ActualSpeed > 200)
                vTarget = FState.Setpoint * Math.Min(1, FState.ActualSpeed / 1200);
            else
                vTarget = 0.3;
            FState.Pressure = FState.Pressure + (vTarget - FState.Pressure) * 0.15 +
                (Rnd() - 0.5) * 0.08;
            FState.Pressure = EnsureRange(FState.Pressure, 0, 10);
            // temperature
            vTarget = FState.Heater ? 112 : 20;
            FState.Temperature = FState.Temperature + (vTarget - FState.Temperature) *
                0.015 + (Rnd() - 0.5) * 0.3;
            // flow and tank level
            FState.Flow = FState.ActualSpeed / 3000 * FState.Valve / 100 * 50 +
                (Rnd() - 0.5) * 0.4;
            FState.Flow = EnsureRange(FState.Flow, 0, 50);
            FState.TankLevel = EnsureRange(FState.TankLevel + (FState.Flow - 15) *
                0.04, 0, 100);
            FState.Load = EnsureRange(FState.ActualSpeed / 3000 * 100 *
                (0.6 + 0.4 * FState.Valve / 100) + (Rnd() - 0.5) * 2, 0, 100);
            // totalizer, sped up x60 so the wheels visibly turn
            FState.TotalVolume = FState.TotalVolume + FState.Flow * CD_DT / 60;
            // noise meters and wind
            if (FState.ActualSpeed > 50)
                vTarget = -18 + FState.ActualSpeed / 3000 * 18;
            else
                vTarget = -20;
            FState.NoiseL = EnsureRange(vTarget + 3 * Math.Sin(FTime) + (Rnd() - 0.5) *
                4, -20, 3);
            FState.NoiseR = EnsureRange(vTarget + 3 * Math.Cos(FTime * 1.3) + (Rnd() -
                0.5) * 4, -20, 3);
            FState.Heading = FState.Heading + (Rnd() - 0.5) * 24;
            if (FState.Heading < 0)
                FState.Heading = FState.Heading + 360;
            else if (FState.Heading >= 360)
                FState.Heading = FState.Heading - 360;
        }

        // every 500 ms: advance the process and push every instrument to its page
        public async Task DoTickAsync()
        {
            string vGauges, vPanels, vControls, vTrends;
            bool[] vChanged = new bool[TInstrumentsConst.CI_TILES];
            lock (FLock)
            {
                Simulate();
                EvaluateTiles(vChanged);
                FTicks++;
                vGauges = FPages.FragGauges(FState);
                vPanels = FPages.FragPanels(FState);
                for (int i = 0; i < TInstrumentsConst.CI_TILES; i++)
                    if (vChanged[i])
                        vPanels = vPanels + FPages.FragTile(FState, i);
                vControls = FPages.FragControls(FState);
                // the e-stop latch also flips the switches: keep them in sync
                if (FState.EStop)
                    vControls = vControls + FPages.FragControl(FState,
                        TInstrumentsConst.CS_CTL_ESTOP);
                vTrends = FPages.FragStrip(FState);
            }
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_GAUGES, vGauges).ConfigureAwait(false);
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_PANELS, vPanels).ConfigureAwait(false);
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_CONTROLS, vControls).ConfigureAwait(false);
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_TRENDS, vTrends).ConfigureAwait(false);
        }

        // every 200 ms: one oscilloscope frame to the Trends page
        public async Task DoScopeAsync()
        {
            string vHTML;
            lock (FLock)
            {
                vHTML = FPages.FragScope(FTime + FTicks * 0.0137, FState.ActualSpeed,
                    FState.Pump && FState.ActualSpeed > 100);
            }
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_TRENDS, vHTML).ConfigureAwait(false);
        }

        // ----- request parameter helpers (query string + form body) ----- //

        private static List<KeyValuePair<string, string>> AllParams(HttpRequest aReq,
            IFormCollection aForm)
        {
            var vResult = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> vPair
                in aReq.Query)
                vResult.Add(new KeyValuePair<string, string>(vPair.Key,
                    vPair.Value.Count > 0 ? vPair.Value[0] ?? "" : ""));
            if (aForm != null)
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> vPair
                    in aForm)
                    vResult.Add(new KeyValuePair<string, string>(vPair.Key,
                        vPair.Value.Count > 0 ? vPair.Value[0] ?? "" : ""));
            return vResult;
        }

        private static string GetParam(List<KeyValuePair<string, string>> aParams,
            string aName)
        {
            for (int vI = 0; vI < aParams.Count; vI++)
                if (string.Equals(aParams[vI].Key, aName, StringComparison.Ordinal))
                    return aParams[vI].Value;
            return "";
        }

        private static string CommaText(List<KeyValuePair<string, string>> aParams)
        {
            List<string> vItems = new List<string>();
            for (int vI = 0; vI < aParams.Count; vI++)
                vItems.Add(aParams[vI].Key + "=" + aParams[vI].Value);
            return string.Join(",", vItems.ToArray());
        }

        private static double ParamFloat(List<KeyValuePair<string, string>> aParams,
            string aName, double aDefault)
        {
            double vResult;
            if (double.TryParse(GetParam(aParams, aName).Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        // ----- control endpoints ----- //

        // POST /ctl/<name>: 204 on a known control, 404 "Unknown control" otherwise
        public async Task<IResult> ControlPost(HttpContext aContext, string aName,
            IFormCollection aForm)
        {
            string vName = (aName ?? "").ToLowerInvariant();
            List<KeyValuePair<string, string>> vParams = AllParams(aContext.Request, aForm);
            bool vKnown = true;
            string vHTML = "", vGauges = "";
            lock (FLock)
            {
                if (vName == TInstrumentsConst.CS_CTL_SETPOINT)
                    FState.Setpoint = Math.Round(EnsureRange(ParamFloat(vParams, "value",
                        FState.Setpoint), 0, 10) * 10) / 10;
                else if (vName == TInstrumentsConst.CS_CTL_PUMP)
                    FState.Pump = GetParam(vParams, "value") == "1" && !FState.EStop;
                else if (vName == TInstrumentsConst.CS_CTL_HEATER)
                    FState.Heater = GetParam(vParams, "value") == "1" && !FState.EStop;
                else if (vName == TInstrumentsConst.CS_CTL_HORN)
                    FState.Horn = GetParam(vParams, "value") == "1";
                else if (vName == TInstrumentsConst.CS_CTL_ESTOP)
                {
                    FState.EStop = true;
                    FState.Pump = false;
                    FState.Heater = false;
                }
                else if (vName == TInstrumentsConst.CS_CTL_RESET)
                    FState.EStop = false;
                else if (vName == TInstrumentsConst.CS_CTL_SPEED)
                    FState.Speed = Math.Round(EnsureRange(ParamFloat(vParams, "value",
                        FState.Speed), 0, 3000));
                else if (vName == TInstrumentsConst.CS_CTL_VALVE)
                    FState.Valve = (int)Math.Round(EnsureRange(ParamFloat(vParams, "value",
                        FState.Valve), 0, 100));
                else if (vName == TInstrumentsConst.CS_CTL_BAND)
                {
                    FState.BandLow = (int)Math.Round(EnsureRange(ParamFloat(vParams,
                        "value_low", FState.BandLow), 0, 100));
                    FState.BandHigh = (int)Math.Round(EnsureRange(ParamFloat(vParams,
                        "value_high", FState.BandHigh), FState.BandLow, 100));
                }
                else
                    vKnown = false;
                if (vKnown)
                {
                    vHTML = FPages.FragControl(FState, vName) + FPages.FragControls(FState);
                    vGauges = FPages.FragGauges(FState);
                }
            }
            if (!vKnown)
                return Results.Text("Unknown control", "text/plain; charset=utf-8",
                    null, 404);
            Console.WriteLine("[control] " + vName + " " + CommaText(vParams));
            // every browser on the Controls page sees the new control position, and
            // the Gauges page reflects the change right away (not on the next tick)
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_CONTROLS, vHTML).ConfigureAwait(false);
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_GAUGES, vGauges).ConfigureAwait(false);
            return Results.StatusCode(204);
        }

        // POST /ack: acknowledges one annunciator tile (tile=<id>)
        public async Task<IResult> AckPost(HttpContext aContext, IFormCollection aForm)
        {
            string vTile = GetParam(AllParams(aContext.Request, aForm), "tile");
            string vHTML = "";
            lock (FLock)
            {
                for (int i = 0; i < TInstrumentsConst.CI_TILES; i++)
                    if (string.Equals(TInstrumentsConst.CS_TILE_IDS[i], vTile,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        FState.TileAck[i] = true;
                        vHTML = FPages.FragTile(FState, i);
                        break;
                    }
            }
            if (vHTML == "")
                return Results.Text("Unknown tile", "text/plain; charset=utf-8",
                    null, 404);
            Console.WriteLine("[ack] " + vTile);
            await PushToPageAsync(TInstrumentsConst.CS_PAGE_PANELS, vHTML).ConfigureAwait(false);
            return Results.StatusCode(204);
        }
    }
}
