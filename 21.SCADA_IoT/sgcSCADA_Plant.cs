// ***************************************************************************
//  sgcSCADAWeb - SCADA + IoT demo on ASP.NET Core
//  Mirror of demos\60.HTML\21.SCADA_IoT\Server\sgcSCADA_Plant.cs
//
//  The whole demo model: the HMI components (SCADA panel, gauge, thermometer,
//  strip chart, annunciator), the library binder that maps the PLC topics
//  onto them, and a simulated PLC that runs the cooling water loop.
//
//  DIFFERENCE FROM THE 60.HTML DEMO. The 60.HTML plant maps the topics with
//  TsgcHTMLInstrumentsMQTTBinder and, in broker mode, talks to a real MQTT
//  broker through TsgcWSPClient_MQTT. Neither exists in the self-contained
//  esegece.sgcHTML.AspNetCore assembly (no MQTT client there, and
//  esegece.sgcWebSockets cannot be referenced next to the ASP.NET Core
//  package: CS0433 / SGCHTML001). So this mirror runs in SIMULATE mode only
//  and uses the transport-free library binder, TsgcHTMLInstrumentsBinder,
//  with the SAME bindings: the simulated PLC hands every topic + payload pair
//  to ProcessMessage, and the binder OnBroadcast event sends the live
//  fragments to the browsers (ISgcHtmlHub.BroadcastAsync, see
//  sgcSCADAWebHost.cs). The PLC logic, the HMI layout and the dashboard are
//  unchanged.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;

using esegece.sgcWebSockets;

namespace SCADADemo
{
    public class TsgcSCADAConfig
    {
        public int HTTPPort = 8110;
        // 'broker' is not available in the ASP.NET Core flavour (see the header):
        // the plant always runs in 'simulate' mode (no network at all).
        public string Mode = "simulate";
        public string Host = "test.mosquitto.org";
        public int MQTTPort = 1883;
        // root of the topic tree; '' = generate a unique one per run
        public string TopicPrefix = "";
    }

    public delegate void TsgcSCADAPushEvent(string aHTML);

    public class TsgcSCADAPlant : IDisposable
    {
        private static readonly string[] CS_VALVES = { "V1", "V2", "V3" };
        private const int CS_TICK_MS = 1000;
        private const string CS_DEG = "°C";

        private static readonly Random FRandom = new Random();

        private readonly TsgcSCADAConfig FConfig;
        private readonly string FPrefix;
        private readonly object FLock = new object();
        private readonly object FPLCLock = new object();
        private readonly object FEmitLock = new object();
        // HMI
        private TsgcHTMLComponent_SCADAPanel FPanel;
        private TsgcHTMLComponent_Gauge FGauge;
        private TsgcHTMLComponent_LinearGauge FThermo;
        private TsgcHTMLComponent_StripChart FTrend;
        private TsgcHTMLComponent_Annunciator FAlarms;
        private TsgcHTMLInstrumentsBinder FBinder;
        private Thread FThread;
        private ManualResetEvent FTerminate;
        // PLC process image
        private double FT1Level;
        private double FT2Level;
        private readonly TsgcHTMLSCADAState[] FValves = new TsgcHTMLSCADAState[3];
        private readonly TsgcHTMLSCADAState[] FValveTarget = new TsgcHTMLSCADAState[3];
        private readonly int[] FValveTicks = new int[3];
        private TsgcHTMLSCADAState FPump;
        private TsgcHTMLSCADAState FFan;
        private double FTemp;
        private double FPressure;
        private double FFlow;
        private int FNoFlowTicks;
        private readonly Dictionary<string, string> FSent = new Dictionary<string, string>();

        public TsgcSCADAPushEvent OnPush;

        public string Prefix { get { return FPrefix; } }
        // always true in the ASP.NET Core flavour (no MQTT client, see the header)
        public bool Simulate { get { return true; } }
        public TsgcSCADAConfig Config { get { return FConfig; } }

        public TsgcSCADAPlant(TsgcSCADAConfig aConfig)
        {
            FConfig = aConfig;
            FPrefix = FConfig.TopicPrefix ?? "";
            if (FPrefix == "")
            {
                int vRandom;
                lock (FRandom)
                    vRandom = FRandom.Next(0x7FFFFFFF);
                FPrefix = "sgc/scada-" + vRandom.ToString("X8");
            }
            FT1Level = 72;
            FT2Level = 38;
            for (int i = 0; i <= 2; i++)
            {
                FValves[i] = TsgcHTMLSCADAState.ssOpen;
                FValveTarget[i] = TsgcHTMLSCADAState.ssOpen;
                FValveTicks[i] = 0;
            }
            FPump = TsgcHTMLSCADAState.ssOn;
            FFan = TsgcHTMLSCADAState.ssOn;
            FTemp = 24;
            DoCreateHMI();
            DoCreateBindings();
        }

        public void Dispose()
        {
            Stop();
            if (FBinder != null) { FBinder.Dispose(); FBinder = null; }
            if (FPanel != null) { FPanel.Dispose(); FPanel = null; }
            if (FGauge != null) { FGauge.Dispose(); FGauge = null; }
            if (FThermo != null) { FThermo.Dispose(); FThermo = null; }
            if (FTrend != null) { FTrend.Dispose(); FTrend = null; }
            if (FAlarms != null) { FAlarms.Dispose(); FAlarms = null; }
        }

        // ----- helpers ----- //

        private static string DotFloat(double aValue, string aFormat = "0.0")
        {
            // payloads are JSON: always a '.' decimal separator, whatever the locale
            return aValue.ToString(aFormat, CultureInfo.InvariantCulture);
        }

        private static string StateName(TsgcHTMLSCADAState aState)
        {
            switch (aState)
            {
                case TsgcHTMLSCADAState.ssOn: return "on";
                case TsgcHTMLSCADAState.ssFault: return "fault";
                case TsgcHTMLSCADAState.ssOpen: return "open";
                case TsgcHTMLSCADAState.ssClosed: return "closed";
                case TsgcHTMLSCADAState.ssTransit: return "transit";
                default: return "off";
            }
        }

        private static string OnOff(bool aValue)
        {
            return aValue ? "ON" : "OFF";
        }

        private static double Clamp(double aValue, double aMin, double aMax)
        {
            double vResult = aValue;
            if (vResult < aMin)
                vResult = aMin;
            if (vResult > aMax)
                vResult = aMax;
            return vResult;
        }

        private static double Noise(double aAmplitude)
        {
            double vRandom;
            lock (FRandom)
                vRandom = FRandom.NextDouble();
            return (vRandom - 0.5) * 2 * aAmplitude;
        }

        // ----- HMI ----- //

        private TsgcHTMLSCADASymbol AddSymbol(string aID, TsgcHTMLSCADASymbolKind aKind,
            double aX, double aY, string aCaption, TsgcHTMLSCADAState aState,
            string aClickURL = "")
        {
            TsgcHTMLSCADASymbol oResult = FPanel.Symbols.Add();
            oResult.SymbolID = aID;
            oResult.Kind = aKind;
            oResult.X = aX;
            oResult.Y = aY;
            oResult.Caption = aCaption;
            oResult.State = aState;
            oResult.ClickURL = aClickURL;
            return oResult;
        }

        private void AddPipe(string aID, string aPoints, bool aFlow)
        {
            TsgcHTMLSCADAPipe oPipe = FPanel.Pipes.Add();
            oPipe.PipeID = aID;
            oPipe.Points = aPoints;
            oPipe.Flow = aFlow;
        }

        private void AddTile(string aID, string aCaption)
        {
            TsgcHTMLAnnunciatorTile oTile = FAlarms.Tiles.Add();
            oTile.TileID = aID;
            oTile.Caption = aCaption;
        }

        private void DoCreateHMI()
        {
            TsgcHTMLSCADASymbol oSymbol;
            TsgcHTMLInstrumentRange oRange;
            TsgcHTMLStripChartPen oPen;

            // ... SCADA mimic: raw water tank -> pump -> heat exchanger -> cooling
            // tank, with a gravity return line back to the raw water tank.
            FPanel = new TsgcHTMLComponent_SCADAPanel();
            FPanel.SCADAPanelID = "plant";
            FPanel.Title = "Cooling water loop";
            FPanel.DrawingWidth = 760;
            FPanel.DrawingHeight = 430;
            FPanel.ShowGrid = true;
            // pipes first: they are drawn under the symbols
            AddPipe("L1", "120,210 230,210", true);
            AddPipe("L2", "280,210 380,210", true);
            AddPipe("L3", "440,210 600,210", true);
            AddPipe("L4", "675,255 720,255 720,380 20,380 20,255 45,255", true);

            oSymbol = AddSymbol("T1", TsgcHTMLSCADASymbolKind.sskTank, 40, 150,
                "T1 Raw water", TsgcHTMLSCADAState.ssOn);
            oSymbol.Level = FT1Level;
            oSymbol = AddSymbol("T2", TsgcHTMLSCADASymbolKind.sskTank, 600, 150,
                "T2 Cooling", TsgcHTMLSCADAState.ssOn);
            oSymbol.Level = FT2Level;
            AddSymbol("V1", TsgcHTMLSCADASymbolKind.sskValve, 150, 196, "V1 Suction",
                TsgcHTMLSCADAState.ssOpen, "/cmd");
            AddSymbol("P1", TsgcHTMLSCADASymbolKind.sskPump, 230, 185, "P1 Circulation",
                TsgcHTMLSCADAState.ssOn, "/cmd");
            AddSymbol("M1", TsgcHTMLSCADASymbolKind.sskMotor, 230, 270, "M1 Drive",
                TsgcHTMLSCADAState.ssOn);
            AddSymbol("HX1", TsgcHTMLSCADASymbolKind.sskHeatExchanger, 380, 180, "HX1",
                TsgcHTMLSCADAState.ssOn);
            AddSymbol("F1", TsgcHTMLSCADASymbolKind.sskFan, 385, 80, "F1 Cooler fan",
                TsgcHTMLSCADAState.ssOn, "/cmd");
            AddSymbol("V2", TsgcHTMLSCADASymbolKind.sskValve, 500, 196, "V2 Discharge",
                TsgcHTMLSCADAState.ssOpen, "/cmd");
            AddSymbol("V3", TsgcHTMLSCADASymbolKind.sskValve, 340, 366, "V3 Return",
                TsgcHTMLSCADAState.ssOpen, "/cmd");
            oSymbol = AddSymbol("PT1", TsgcHTMLSCADASymbolKind.sskSensor, 300, 115, "PT1",
                TsgcHTMLSCADAState.ssOn);
            oSymbol.Unit_ = "bar";
            oSymbol.Decimals = 2;
            oSymbol = AddSymbol("TT1", TsgcHTMLSCADASymbolKind.sskSensor, 395, 280, "TT1",
                TsgcHTMLSCADAState.ssOn);
            oSymbol.Unit_ = CS_DEG;
            oSymbol = AddSymbol("FT1", TsgcHTMLSCADASymbolKind.sskSensor, 530, 115, "FT1",
                TsgcHTMLSCADAState.ssOn);
            oSymbol.Unit_ = "m3/h";
            AddSymbol("LBL", TsgcHTMLSCADASymbolKind.sskLabel, 20, 20,
                "Click a valve, the pump or the fan", TsgcHTMLSCADAState.ssOff);

            // ... flow gauge (value)
            FGauge = new TsgcHTMLComponent_Gauge();
            FGauge.GaugeID = "gFlow";
            FGauge.Title = "Loop flow (FT1)";
            FGauge.Unit_ = "m3/h";
            FGauge.MinValue = 0;
            FGauge.MaxValue = 120;
            FGauge.ArcAngle = 270;
            FGauge.ShowTicks = true;
            FGauge.NeedleStyle = TsgcHTMLGaugeNeedleStyle.gnsTriangle;
            FGauge.Animate = true;
            oRange = FGauge.Ranges.Add();
            oRange.StartValue = 0;
            oRange.EndValue = 20;
            oRange.Color = "#e03131";
            oRange.Caption = "Low flow";
            oRange = FGauge.Ranges.Add();
            oRange.StartValue = 20;
            oRange.EndValue = 60;
            oRange.Color = "#f59f00";
            oRange = FGauge.Ranges.Add();
            oRange.StartValue = 60;
            oRange.EndValue = 120;
            oRange.Color = "#2f9e44";

            // ... thermometer (value with Scale/Offset from raw ADC counts)
            FThermo = new TsgcHTMLComponent_LinearGauge();
            FThermo.LinearGaugeID = "lgTemp";
            FThermo.Title = "HX1 outlet (TT1)";
            FThermo.Unit_ = CS_DEG;
            FThermo.MinValue = 0;
            FThermo.MaxValue = 50;
            FThermo.Orientation = TsgcHTMLLinearOrientation.loVertical;
            FThermo.GaugeStyle = TsgcHTMLLinearGaugeStyle.lgsThermometer;
            FThermo.Value = FTemp;
            oRange = FThermo.Ranges.Add();
            oRange.StartValue = 30;
            oRange.EndValue = 50;
            oRange.Color = "#e03131";

            // ... strip chart (push with a JSON array: one sample per pen)
            FTrend = new TsgcHTMLComponent_StripChart();
            FTrend.StripChartID = "scTrend";
            FTrend.Title = "Loop trend (last 2 minutes)";
            FTrend.MinValue = 0;
            FTrend.MaxValue = 100;
            FTrend.TimeWindow = 120;
            FTrend.DrawingWidth = 700;
            FTrend.DrawingHeight = 220;
            oPen = FTrend.Pens.Add();
            oPen.Caption = "Supply " + CS_DEG;
            oPen.Color = "#e8590c";
            oPen = FTrend.Pens.Add();
            oPen.Caption = "Outlet " + CS_DEG;
            oPen.Color = "#1c7ed6";
            oPen = FTrend.Pens.Add();
            oPen.Caption = "Flow m3/h";
            oPen.Color = "#2f9e44";

            // ... annunciator (tile), acknowledged with a click
            FAlarms = new TsgcHTMLComponent_Annunciator();
            FAlarms.AnnunciatorID = "anAlarms";
            FAlarms.Columns = 3;
            FAlarms.AckURL = "/ack";
            AddTile("HI_LEVEL", "T2 HIGH LEVEL");
            AddTile("LO_LEVEL", "T1 LOW LEVEL");
            AddTile("HI_TEMP", "HX1 HIGH TEMP");
            AddTile("OVERPRESS", "PT1 OVERPRESSURE");
            AddTile("PUMP_FAULT", "P1 PUMP FAULT");
            AddTile("LOW_FLOW", "FT1 LOW FLOW");
        }

        private void DoBind(string aTopic, TsgcHTMLComponent aInstrument,
            TsgcHTMLInstrumentBindingTarget aTarget, string aItemID,
            string aJSONPath = "", double aScale = 1, double aOffset = 0)
        {
            TsgcHTMLInstrumentBinding oBinding = FBinder.Bindings.Add();
            oBinding.Topic = FPrefix + aTopic;
            oBinding.Instrument = aInstrument;
            oBinding.Target = aTarget;
            oBinding.ItemID = aItemID;
            oBinding.JSONPath = aJSONPath;
            oBinding.Scale = aScale;
            oBinding.Offset = aOffset;
        }

        // Same topic map as the 60.HTML binder bindings.
        private void DoCreateBindings()
        {
            // the library binder, without transport: fed by ProcessMessage,
            // fragments leave through OnBroadcast (no engine WebSocket server
            // in the ASP.NET Core build)
            FBinder = new TsgcHTMLInstrumentsBinder();
            FBinder.OnBroadcast += (s, f) => DoPushEvent(f);
            FBinder.OnBindingError += (s, b, e) =>
                Console.WriteLine("binding " + b.Topic + ": " + e);
            // tank levels: JSON payload {"level":62.4,"volume_m3":124.8}
            DoBind("/tank/T1", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolLevel, "T1", "level");
            DoBind("/tank/T2", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolLevel, "T2", "level");
            // valve / pump / motor / fan states: plain text 'open', 'on', 'fault'...
            for (int i = 0; i <= 2; i++)
                DoBind("/valve/" + CS_VALVES[i] + "/state", FPanel,
                    TsgcHTMLInstrumentBindingTarget.ibtSymbolState, CS_VALVES[i]);
            DoBind("/pump/P1/state", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolState, "P1");
            DoBind("/motor/M1/state", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolState, "M1");
            DoBind("/fan/F1/state", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolState, "F1");
            // animated flow in the pipes: 'ON' / 'OFF'
            DoBind("/pipe/L1/flow", FPanel, TsgcHTMLInstrumentBindingTarget.ibtPipeFlow, "L1");
            DoBind("/pipe/L2/flow", FPanel, TsgcHTMLInstrumentBindingTarget.ibtPipeFlow, "L2");
            DoBind("/pipe/L3/flow", FPanel, TsgcHTMLInstrumentBindingTarget.ibtPipeFlow, "L3");
            DoBind("/pipe/L4/flow", FPanel, TsgcHTMLInstrumentBindingTarget.ibtPipeFlow, "L4");
            // sensors in raw engineering units, scaled by the mapping:
            // TT1 12-bit ADC counts -> degC = raw * 0.025 - 10
            DoBind("/sensor/TT1", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolValue, "TT1", "raw", 0.025, -10);
            DoBind("/sensor/TT1", FThermo, TsgcHTMLInstrumentBindingTarget.ibtValue, "", "raw", 0.025, -10);
            // PT1 4..20 mA loop -> 0..10 bar = mA * 0.625 - 2.5
            DoBind("/sensor/PT1", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolValue, "PT1", "mA", 0.625, -2.5);
            // FT1 plain number payload, already in m3/h
            DoBind("/sensor/FT1", FPanel, TsgcHTMLInstrumentBindingTarget.ibtSymbolValue, "FT1");
            DoBind("/sensor/FT1", FGauge, TsgcHTMLInstrumentBindingTarget.ibtValue, "");
            // trend: JSON array [supply, outlet, flow], one sample per pen
            DoBind("/trend", FTrend, TsgcHTMLInstrumentBindingTarget.ibtPush, "");
            // alarms: 'normal' / 'advisory' / 'warning' / 'alarm'
            DoBind("/alarm/HI_LEVEL", FAlarms, TsgcHTMLInstrumentBindingTarget.ibtTile, "HI_LEVEL");
            DoBind("/alarm/LO_LEVEL", FAlarms, TsgcHTMLInstrumentBindingTarget.ibtTile, "LO_LEVEL");
            DoBind("/alarm/HI_TEMP", FAlarms, TsgcHTMLInstrumentBindingTarget.ibtTile, "HI_TEMP");
            DoBind("/alarm/OVERPRESS", FAlarms, TsgcHTMLInstrumentBindingTarget.ibtTile, "OVERPRESS");
            DoBind("/alarm/PUMP_FAULT", FAlarms, TsgcHTMLInstrumentBindingTarget.ibtTile, "PUMP_FAULT");
            DoBind("/alarm/LOW_FLOW", FAlarms, TsgcHTMLInstrumentBindingTarget.ibtTile, "LOW_FLOW");
        }

        private void DoPushEvent(string aHTML)
        {
            TsgcSCADAPushEvent vHandler = OnPush;
            if (vHandler != null)
                vHandler(aHTML);
        }

        public void Start()
        {
            if (!string.Equals(FConfig.Mode, "simulate", StringComparison.OrdinalIgnoreCase))
                Console.WriteLine("[mqtt] broker mode is not available in the ASP.NET Core " +
                    "flavour (no MQTT client in esegece.sgcHTML.AspNetCore): simulate mode");
            FTerminate = new ManualResetEvent(false);
            FThread = new Thread(TickThreadExecute);
            FThread.IsBackground = true;
            FThread.Name = "sgcSCADA PLC";
            FThread.Start();
        }

        public void Stop()
        {
            if (FThread != null)
            {
                FTerminate.Set();
                FThread.Join();
                FThread = null;
                FTerminate.Dispose();
                FTerminate = null;
            }
        }

        private void TickThreadExecute()
        {
            while (!FTerminate.WaitOne(0))
            {
                try
                {
                    Tick();
                }
                catch (Exception E)
                {
                    Console.WriteLine("[plc] error: " + E.Message);
                }
                if (FTerminate.WaitOne(CS_TICK_MS))
                    break;
            }
        }

        // ----- PLC ----- //

        // Queue a message, but only when its payload changed since the last one
        // sent on that topic (report by exception). Sensors pass aForce.
        private void DoQueue(List<KeyValuePair<string, string>> aMessages, string aTopic,
            string aPayload, bool aForce)
        {
            string vTopic = FPrefix + aTopic;
            string vLast;
            if (!aForce && FSent.TryGetValue(vTopic, out vLast) && vLast == aPayload)
                return;
            FSent[vTopic] = aPayload;
            aMessages.Add(new KeyValuePair<string, string>(vTopic, aPayload));
        }

        // Deliver the queued messages straight into the binder (simulate mode):
        // ProcessMessage is the path an MQTT PUBLISH takes. Applied under the
        // page lock, so a page render never sees a half-updated panel. Never
        // called while holding the PLC lock.
        private void DoEmit(List<KeyValuePair<string, string>> aMessages)
        {
            lock (FEmitLock)
            {
                for (int i = 0; i < aMessages.Count; i++)
                    lock (FLock)
                        FBinder.ProcessMessage(aMessages[i].Key, aMessages[i].Value);
            }
        }

        private void DoPLCCommand(string aSymbol, string aCommand,
            List<KeyValuePair<string, string>> aMessages)
        {
            if (!string.Equals(aCommand, "toggle", StringComparison.OrdinalIgnoreCase))
                return;
            lock (FPLCLock)
            {
                for (int i = 0; i <= 2; i++)
                    if (string.Equals(aSymbol, CS_VALVES[i], StringComparison.OrdinalIgnoreCase))
                    {
                        if (FValves[i] == TsgcHTMLSCADAState.ssTransit)
                            return;
                        if (FValves[i] == TsgcHTMLSCADAState.ssOpen)
                            FValveTarget[i] = TsgcHTMLSCADAState.ssClosed;
                        else
                            FValveTarget[i] = TsgcHTMLSCADAState.ssOpen;
                        // the actuator needs 2 scans to travel
                        FValves[i] = TsgcHTMLSCADAState.ssTransit;
                        FValveTicks[i] = 2;
                        DoQueue(aMessages, "/valve/" + CS_VALVES[i] + "/state", "transit", false);
                    }
                if (string.Equals(aSymbol, "P1", StringComparison.OrdinalIgnoreCase))
                {
                    // on -> off, off -> on, fault -> off (reset)
                    if (FPump == TsgcHTMLSCADAState.ssOff)
                        FPump = TsgcHTMLSCADAState.ssOn;
                    else
                        FPump = TsgcHTMLSCADAState.ssOff;
                    FNoFlowTicks = 0;
                    DoQueue(aMessages, "/pump/P1/state", StateName(FPump), false);
                    DoQueue(aMessages, "/motor/M1/state", StateName(FPump), false);
                }
                if (string.Equals(aSymbol, "F1", StringComparison.OrdinalIgnoreCase))
                {
                    if (FFan == TsgcHTMLSCADAState.ssOn)
                        FFan = TsgcHTMLSCADAState.ssOff;
                    else
                        FFan = TsgcHTMLSCADAState.ssOn;
                    DoQueue(aMessages, "/fan/F1/state", StateName(FFan), false);
                }
            }
        }

        private static string Level3(double aValue, double aWarn, double aAlarm, bool aHigh)
        {
            string vResult = "normal";
            if (aHigh)
            {
                if (aValue >= aAlarm)
                    vResult = "alarm";
                else if (aValue >= aWarn)
                    vResult = "warning";
            }
            else
            {
                if (aValue <= aAlarm)
                    vResult = "alarm";
                else if (aValue <= aWarn)
                    vResult = "warning";
            }
            return vResult;
        }

        // one PLC scan: advance the process and publish what changed
        public void Tick()
        {
            List<KeyValuePair<string, string>> oMessages = new List<KeyValuePair<string, string>>();
            lock (FPLCLock)
            {
                // valve actuators
                for (int i = 0; i <= 2; i++)
                {
                    if (FValveTicks[i] > 0)
                    {
                        FValveTicks[i]--;
                        if (FValveTicks[i] == 0)
                            FValves[i] = FValveTarget[i];
                    }
                    DoQueue(oMessages, "/valve/" + CS_VALVES[i] + "/state",
                        StateName(FValves[i]), false);
                }

                // hydraulics
                bool vRun = FPump == TsgcHTMLSCADAState.ssOn;
                bool vL1 = vRun && (FValves[0] == TsgcHTMLSCADAState.ssOpen) && (FT1Level > 2);
                bool vL3 = vL1 && (FValves[1] == TsgcHTMLSCADAState.ssOpen);
                bool vL4 = (FValves[2] == TsgcHTMLSCADAState.ssOpen) && (FT2Level > 2);
                if (vL3)
                    FFlow = 78 + Noise(3);
                else
                    FFlow = 0;
                if (!vRun)
                    FPressure = 0.2 + Noise(0.05);
                else if (vL3)
                    FPressure = 4.6 + Noise(0.2);
                else if (vL1)
                    FPressure = 9.1 + Noise(0.2); // blocked discharge
                else
                    FPressure = 0.4 + Noise(0.1); // dry running
                // a pump running without flow for 6 scans trips
                if (vRun && !vL3)
                {
                    FNoFlowTicks++;
                    if (FNoFlowTicks >= 6)
                    {
                        FPump = TsgcHTMLSCADAState.ssFault;
                        FNoFlowTicks = 0;
                    }
                }
                else
                    FNoFlowTicks = 0;
                FT1Level = FT1Level - FFlow * 0.02;
                FT2Level = FT2Level + FFlow * 0.02;
                if (vL4)
                {
                    FT1Level = FT1Level + 1.45 + Noise(0.2);
                    FT2Level = FT2Level - 1.45;
                }
                FT1Level = Clamp(FT1Level, 0, 100);
                FT2Level = Clamp(FT2Level, 0, 100);

                // thermal: the fan cools the heat exchanger outlet
                double vSupply = 41 + Noise(0.5);
                double vTarget;
                if (FFlow > 0)
                    vTarget = FFan == TsgcHTMLSCADAState.ssOn ? 23 : 38;
                else
                    vTarget = 27;
                FTemp = FTemp + (vTarget - FTemp) * 0.12 + Noise(0.15);

                // process image -> topics
                DoQueue(oMessages, "/pump/P1/state", StateName(FPump), false);
                DoQueue(oMessages, "/motor/M1/state", StateName(FPump), false);
                DoQueue(oMessages, "/fan/F1/state", StateName(FFan), false);
                DoQueue(oMessages, "/pipe/L1/flow", OnOff(vL1), false);
                DoQueue(oMessages, "/pipe/L2/flow", OnOff(vL1), false);
                DoQueue(oMessages, "/pipe/L3/flow", OnOff(vL3), false);
                DoQueue(oMessages, "/pipe/L4/flow", OnOff(vL4), false);
                DoQueue(oMessages, "/tank/T1", "{\"level\":" + DotFloat(FT1Level) +
                    ",\"volume_m3\":" + DotFloat(FT1Level * 2) + "}", true);
                DoQueue(oMessages, "/tank/T2", "{\"level\":" + DotFloat(FT2Level) +
                    ",\"volume_m3\":" + DotFloat(FT2Level * 1.5) + "}", true);
                DoQueue(oMessages, "/sensor/TT1", "{\"raw\":" +
                    ((long)Math.Round((FTemp + 10) / 0.025)).ToString(CultureInfo.InvariantCulture) +
                    "}", true);
                DoQueue(oMessages, "/sensor/PT1", "{\"mA\":" +
                    DotFloat(FPressure * 1.6 + 4, "0.000") + "}", true);
                DoQueue(oMessages, "/sensor/FT1", DotFloat(FFlow), true);
                DoQueue(oMessages, "/trend", "[" + DotFloat(vSupply) + "," +
                    DotFloat(FTemp) + "," + DotFloat(FFlow) + "]", true);
                DoQueue(oMessages, "/alarm/HI_LEVEL", Level3(FT2Level, 85, 92, true), false);
                DoQueue(oMessages, "/alarm/LO_LEVEL", Level3(FT1Level, 20, 10, false), false);
                DoQueue(oMessages, "/alarm/HI_TEMP", Level3(FTemp, 30, 34, true), false);
                DoQueue(oMessages, "/alarm/OVERPRESS", Level3(FPressure, 6.5, 8, true), false);
                if (FPump == TsgcHTMLSCADAState.ssFault)
                    DoQueue(oMessages, "/alarm/PUMP_FAULT", "alarm", false);
                else
                    DoQueue(oMessages, "/alarm/PUMP_FAULT", "normal", false);
                if (vRun && (FFlow < 10))
                    DoQueue(oMessages, "/alarm/LOW_FLOW", "advisory", false);
                else
                    DoQueue(oMessages, "/alarm/LOW_FLOW", "normal", false);
            }
            DoEmit(oMessages);
        }

        // operator command from the browser (SCADA symbol click): same PLC
        // logic as the 60.HTML simulate mode, no network
        public void Command(string aSymbol)
        {
            Console.WriteLine("[hmi] command toggle " + aSymbol);
            List<KeyValuePair<string, string>> oMessages = new List<KeyValuePair<string, string>>();
            DoPLCCommand(aSymbol, "toggle", oMessages);
            DoEmit(oMessages);
        }

        // annunciator acknowledge; returns the tile OOB fragment
        public string Acknowledge(string aTileID)
        {
            lock (FLock)
            {
                TsgcHTMLAnnunciatorTile oTile = FAlarms.Tiles.FindTile(aTileID);
                if (oTile == null)
                    return "";
                oTile.Acknowledged = true;
                return FAlarms.GetLiveTileFragment(aTileID);
            }
        }

        private static string Card(string aTitle, string aBody)
        {
            TsgcHTMLCard oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-3";
            oCard.Title = aTitle;
            oCard.Body.AddRaw(aBody);
            return oCard.HTML;
        }

        private static string Muted(string aText)
        {
            TsgcHTMLContainer oText = new TsgcHTMLContainer("p");
            oText.CSSClass = "text-muted small mb-2";
            oText.AddText(aText);
            return oText.HTML;
        }

        // the dashboard body (panel + instruments), rendered under the lock
        public string RenderDashboard()
        {
            double vFlow, vTemp;
            // snapshot the PLC first (never nest the PLC lock inside the demo lock)
            lock (FPLCLock)
            {
                vFlow = FFlow;
                vTemp = FTemp;
            }
            string vSource = "Simulate mode: the PLC feeds the panel directly (no broker).";

            lock (FLock)
            {
                FGauge.Value = vFlow;
                FThermo.Value = vTemp;
                TsgcHTMLContainer oRoot = new TsgcHTMLContainer("div");
                TsgcHTMLRow oRow = new TsgcHTMLRow();
                oRoot.Add(oRow);
                oRow.Col(TsgcHTMLColWidth.cw8).AddRaw(Card("Plant mimic",
                    Muted(vSource + " Topic root: " + FPrefix + "/") + FPanel.HTML));
                TsgcHTMLCol oCol = oRow.Col(TsgcHTMLColWidth.cw4);
                oCol.AddRaw(Card("Flow", FGauge.HTML));
                oCol.AddRaw(Card("Temperature", FThermo.HTML));
                oRow = new TsgcHTMLRow();
                oRoot.Add(oRow);
                oRow.Col(TsgcHTMLColWidth.cw7).AddRaw(Card("Trend", FTrend.HTML));
                oRow.Col(TsgcHTMLColWidth.cw5).AddRaw(Card("Alarms",
                    Muted("Click a flashing tile to acknowledge it.") + FAlarms.HTML));
                return oRoot.HTML;
            }
        }
    }
}
