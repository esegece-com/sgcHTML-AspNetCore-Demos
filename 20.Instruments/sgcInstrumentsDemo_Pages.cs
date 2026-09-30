// ***************************************************************************
//  sgcInstruments - control room demo of the sgcHTML instrumentation
//  components (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\20.Instruments\sgcInstrumentsDemo_Pages.pas
//
//  View layer. Owns one instance of every instrument (fixed ids), applies the
//  process state to them and renders either a full page or the live fragments
//  (GetLive*Fragment / GetLivePushScript) the server pushes over the sgcHTMX
//  bridge. Not thread-safe: the server calls it under its lock.
// ***************************************************************************

using System;
using System.Globalization;
// sgc
using esegece.sgcWebSockets;

namespace InstrumentsDemo
{
    public static class TInstrumentsConst
    {
        public const int CI_TILES = 8;
        public static readonly string[] CS_TILE_IDS = { "hipress", "lopress",
            "hitemp", "lotank", "hitank", "pumpoff", "heater", "estop" };
        public static readonly string[] CS_TILE_CAPTIONS = { "HIGH PRESS",
            "LOW PRESS", "HIGH TEMP", "TANK LOW", "TANK HIGH", "PUMP OFF",
            "HEATER ON", "E-STOP" };

        // page keys (also the URL of the page, relative to the base path)
        public const string CS_PAGE_GAUGES = "/";
        public const string CS_PAGE_PANELS = "/panels";
        public const string CS_PAGE_CONTROLS = "/controls";
        public const string CS_PAGE_TRENDS = "/trends";

        // controls posted by the Controls page (hx-post) and the annunciator
        public const string CS_CTL_SETPOINT = "setpoint";
        public const string CS_CTL_PUMP = "pump";
        public const string CS_CTL_HEATER = "heater";
        public const string CS_CTL_HORN = "horn";
        public const string CS_CTL_ESTOP = "estop";
        public const string CS_CTL_RESET = "reset";
        public const string CS_CTL_SPEED = "speed";
        public const string CS_CTL_VALVE = "valve";
        public const string CS_CTL_BAND = "band";
        public const string CS_ACK_URL = "/ack";
    }

    // Simulated process state. The server owns one instance, guarded by its
    // lock, and advances it every 500 ms.
    public class TInstrumentsState
    {
        public double Pressure; // bar
        public double Temperature; // deg C
        public double Flow; // m3/h
        public double TankLevel; // %
        public double Load; // %
        public double Speed; // requested pump speed, rpm
        public double ActualSpeed; // rpm
        public double Setpoint; // bar
        public int Valve; // %
        public int BandLow; // tenths of bar
        public int BandHigh; // tenths of bar
        public bool Pump;
        public bool Heater;
        public bool Horn;
        public bool EStop;
        public double TotalVolume; // m3
        public double NoiseL; // dB
        public double NoiseR; // dB
        public double Heading; // wind direction, deg
        public TsgcHTMLAnnunciatorState[] TileState =
            new TsgcHTMLAnnunciatorState[TInstrumentsConst.CI_TILES];
        public bool[] TileAck = new bool[TInstrumentsConst.CI_TILES];

        // the process state at start-up
        public TInstrumentsState()
        {
            Pressure = 0.5;
            Temperature = 22;
            Flow = 0;
            TankLevel = 55;
            Load = 0;
            Speed = 1500;
            ActualSpeed = 0;
            Setpoint = 6;
            Valve = 60;
            BandLow = 20;
            BandHigh = 85;
            Pump = true;
            Heater = true;
            Horn = false;
            EStop = false;
            TotalVolume = 12345.6;
            NoiseL = -12;
            NoiseR = -12;
            Heading = 240;
            for (int i = 0; i < TInstrumentsConst.CI_TILES; i++)
            {
                TileState[i] = TsgcHTMLAnnunciatorState.ansNormal;
                TileAck[i] = false;
            }
        }
    }

    public class TInstrumentsPages
    {
        private const string CS_DEG = "\u00B0";
        private const string CS_DEG_C = CS_DEG + "C";
        private const string CS_GREEN = "#198754";
        private const string CS_YELLOW = "#ffc107";
        private const string CS_RED = "#dc3545";
        private const string CS_BLUE = "#0dcaf0";
        private const string CS_ORANGE = "#fd7e14";
        private const string CS_TEAL = "#20c997";

        // dark "control room" look on top of the Bootstrap dark theme
        private const string CS_ROOM_CSS = "body{background:#0b0f14}" +
            ".card{background:#141a21;border-color:#243040}" +
            ".card-header{background:#1b232d;color:#9fb3c8;font-size:.8rem;" +
            "letter-spacing:.08em;text-transform:uppercase}" +
            ".room-sub{color:#7d8fa3}";

        private readonly Random FRandom = new Random();
        // Gauges page
        private TsgcHTMLComponent_Gauge FGaugePress;
        private TsgcHTMLComponent_Gauge FGaugeTemp;
        private TsgcHTMLComponent_Gauge FGaugeFlow;
        private TsgcHTMLComponent_LinearGauge FLinLoad;
        private TsgcHTMLComponent_LinearGauge FLinTemp;
        private TsgcHTMLComponent_LinearGauge FLinTank;
        private TsgcHTMLComponent_LEDBar FLEDBarSpeed;
        private TsgcHTMLComponent_SegmentDisplay FSegFlow;
        private TsgcHTMLComponent_SegmentDisplay FSegStatus;
        private TsgcHTMLComponent_LED FLEDPump;
        private TsgcHTMLComponent_LED FLEDHeater;
        private TsgcHTMLComponent_LED FLEDAlarm;
        private TsgcHTMLComponent_LED FLEDHorn;
        // Panels page
        private TsgcHTMLComponent_Annunciator FAnnunciator;
        private TsgcHTMLComponent_Odometer FOdometer;
        private TsgcHTMLComponent_VUMeter FVUNeedle;
        private TsgcHTMLComponent_VUMeter FVUBar;
        private TsgcHTMLComponent_AnalogClock FClockLocal;
        private TsgcHTMLComponent_AnalogClock FClockUTC;
        private TsgcHTMLComponent_Compass FCompass;
        // Controls page
        private TsgcHTMLComponent_Knob FKnob;
        private TsgcHTMLComponent_ToggleSwitch FSwPump;
        private TsgcHTMLComponent_ToggleSwitch FSwHeater;
        private TsgcHTMLComponent_PushButton FBtnHorn;
        private TsgcHTMLComponent_PushButton FBtnEStop;
        private TsgcHTMLComponent_PushButton FBtnReset;
        private TsgcHTMLComponent_NumericStepper FStepSpeed;
        private TsgcHTMLComponent_Slider FSliderValve;
        private TsgcHTMLComponent_Slider FSliderBand;
        private TsgcHTMLComponent_Gauge FCtlGauge;
        private TsgcHTMLComponent_SegmentDisplay FCtlSeg;
        private TsgcHTMLComponent_LED FCtlLEDPump;
        private TsgcHTMLComponent_LED FCtlLEDHeater;
        private TsgcHTMLComponent_LED FCtlLEDEStop;
        // Trends page
        private TsgcHTMLComponent_StripChart FStrip;
        private TsgcHTMLComponent_Oscilloscope FScope;

        public string BasePath { get; set; }

        public TInstrumentsPages()
        {
            BasePath = "";
            CreateGaugesPage();
            CreatePanelsPage();
            CreateControlsPage();
            CreateTrendsPage();
        }

        private string U(string aPath)
        {
            return BasePath + aPath;
        }

        private string CtlURL(string aName)
        {
            return U("/ctl/" + aName);
        }

        private static void AddRange(TsgcHTMLInstrumentRanges aRanges, double aStart,
            double aEnd, string aColor, string aCaption)
        {
            TsgcHTMLInstrumentRange oRange = aRanges.Add();
            oRange.StartValue = aStart;
            oRange.EndValue = aEnd;
            oRange.Color = aColor;
            oRange.Caption = aCaption;
        }

        private static void AddPressureRanges(TsgcHTMLInstrumentRanges aRanges)
        {
            AddRange(aRanges, 0, 7, CS_GREEN, "Normal");
            AddRange(aRanges, 7, 8.5, CS_YELLOW, "High");
            AddRange(aRanges, 8.5, 10, CS_RED, "Trip");
        }

        private static TsgcHTMLComponent_LED NewLED(string aID, string aCaption,
            string aColor)
        {
            TsgcHTMLComponent_LED oResult = new TsgcHTMLComponent_LED();
            oResult.LEDID = aID;
            oResult.Caption = aCaption;
            oResult.OnColor = aColor;
            oResult.Size = 22;
            return oResult;
        }

        private static TsgcHTMLCard NewCard(string aTitle)
        {
            TsgcHTMLCard oResult = new TsgcHTMLCard();
            oResult.CSSClass = "h-100";
            oResult.BodyClass = "text-center";
            if (aTitle != "")
                oResult.Header.AddText(aTitle);
            return oResult;
        }

        private void CreateGaugesPage()
        {
            // 270 degree arc: ticks, colored ranges, triangle needle, animated
            FGaugePress = new TsgcHTMLComponent_Gauge();
            FGaugePress.GaugeID = "g-press";
            FGaugePress.Title = "Boiler pressure";
            FGaugePress.Unit_ = "bar";
            FGaugePress.MinValue = 0;
            FGaugePress.MaxValue = 10;
            FGaugePress.Width = 260;
            FGaugePress.ArcAngle = 270;
            FGaugePress.ShowTicks = true;
            FGaugePress.MinorTicks = 4;
            FGaugePress.NeedleStyle = TsgcHTMLGaugeNeedleStyle.gnsTriangle;
            FGaugePress.Animate = true;
            AddPressureRanges(FGaugePress.Ranges);

            // 180 degree arc with a line needle
            FGaugeTemp = new TsgcHTMLComponent_Gauge();
            FGaugeTemp.GaugeID = "g-temp";
            FGaugeTemp.Title = "Water temperature";
            FGaugeTemp.Unit_ = CS_DEG_C;
            FGaugeTemp.MinValue = 0;
            FGaugeTemp.MaxValue = 150;
            FGaugeTemp.Width = 260;
            FGaugeTemp.ArcAngle = 180;
            FGaugeTemp.ShowTicks = true;
            FGaugeTemp.NeedleStyle = TsgcHTMLGaugeNeedleStyle.gnsLine;
            FGaugeTemp.Animate = true;
            AddRange(FGaugeTemp.Ranges, 0, 100, CS_BLUE, "Normal");
            AddRange(FGaugeTemp.Ranges, 100, 120, CS_YELLOW, "Warm");
            AddRange(FGaugeTemp.Ranges, 120, 150, CS_RED, "Hot");

            // 90 degree arc, value arc only
            FGaugeFlow = new TsgcHTMLComponent_Gauge();
            FGaugeFlow.GaugeID = "g-flow";
            FGaugeFlow.Title = "Outlet flow";
            FGaugeFlow.Unit_ = "m3/h";
            FGaugeFlow.MinValue = 0;
            FGaugeFlow.MaxValue = 50;
            FGaugeFlow.Width = 260;
            FGaugeFlow.ArcAngle = 90;
            FGaugeFlow.ShowTicks = true;
            FGaugeFlow.MajorTicks = 5;
            FGaugeFlow.NeedleStyle = TsgcHTMLGaugeNeedleStyle.gnsLine;
            FGaugeFlow.Animate = true;

            FLinLoad = new TsgcHTMLComponent_LinearGauge();
            FLinLoad.LinearGaugeID = "lg-load";
            FLinLoad.Title = "Motor load";
            FLinLoad.Unit_ = "%";
            FLinLoad.Orientation = TsgcHTMLLinearOrientation.loHorizontal;
            FLinLoad.GaugeStyle = TsgcHTMLLinearGaugeStyle.lgsBar;
            FLinLoad.Animate = true;
            AddRange(FLinLoad.Ranges, 0, 70, CS_GREEN, "Normal");
            AddRange(FLinLoad.Ranges, 70, 90, CS_YELLOW, "High");
            AddRange(FLinLoad.Ranges, 90, 100, CS_RED, "Overload");

            FLinTemp = new TsgcHTMLComponent_LinearGauge();
            FLinTemp.LinearGaugeID = "lg-temp";
            FLinTemp.Title = "Return temp";
            FLinTemp.Unit_ = CS_DEG_C;
            FLinTemp.MaxValue = 150;
            FLinTemp.Orientation = TsgcHTMLLinearOrientation.loVertical;
            FLinTemp.GaugeStyle = TsgcHTMLLinearGaugeStyle.lgsThermometer;
            FLinTemp.DrawingHeight = 220;
            FLinTemp.Animate = true;

            FLinTank = new TsgcHTMLComponent_LinearGauge();
            FLinTank.LinearGaugeID = "lg-tank";
            FLinTank.Title = "Tank level";
            FLinTank.Unit_ = "%";
            FLinTank.Orientation = TsgcHTMLLinearOrientation.loVertical;
            FLinTank.GaugeStyle = TsgcHTMLLinearGaugeStyle.lgsTank;
            FLinTank.DrawingHeight = 220;
            FLinTank.Animate = true;
            AddRange(FLinTank.Ranges, 0, 15, CS_RED, "Low");
            AddRange(FLinTank.Ranges, 90, 100, CS_RED, "High");

            FLEDBarSpeed = new TsgcHTMLComponent_LEDBar();
            FLEDBarSpeed.LEDBarID = "lb-speed";
            FLEDBarSpeed.Title = "Pump speed (rpm)";
            FLEDBarSpeed.MaxValue = 3000;
            FLEDBarSpeed.SegmentCount = 20;
            FLEDBarSpeed.DrawingWidth = 320;
            FLEDBarSpeed.DrawingHeight = 28;

            FSegFlow = new TsgcHTMLComponent_SegmentDisplay();
            FSegFlow.SegmentDisplayID = "sd-flow";
            FSegFlow.Title = "Flow m3/h (7 segment)";
            FSegFlow.SegmentType = TsgcHTMLSegmentDisplayType.sdt7Segment;
            FSegFlow.Digits = 4;
            FSegFlow.OnColor = "#ff453a";
            FSegFlow.BackgroundColor = "#000000";
            FSegFlow.SkewAngle = 8;

            FSegStatus = new TsgcHTMLComponent_SegmentDisplay();
            FSegStatus.SegmentDisplayID = "sd-status";
            FSegStatus.Title = "Plant status (14 segment)";
            FSegStatus.SegmentType = TsgcHTMLSegmentDisplayType.sdt14Segment;
            FSegStatus.Digits = 6;
            FSegStatus.OnColor = CS_TEAL;
            FSegStatus.BackgroundColor = "#000000";

            FLEDPump = NewLED("led-pump", "Pump", CS_GREEN);
            FLEDHeater = NewLED("led-heater", "Heater", CS_ORANGE);
            FLEDAlarm = NewLED("led-alarm", "Alarm", CS_RED);
            FLEDHorn = NewLED("led-horn", "Horn", CS_YELLOW);
            FLEDHorn.Shape = TsgcHTMLLEDShape.lsSquare;
        }

        private void CreatePanelsPage()
        {
            FAnnunciator = new TsgcHTMLComponent_Annunciator();
            FAnnunciator.AnnunciatorID = "ann";
            FAnnunciator.Columns = 4;
            FAnnunciator.Blink = true;
            FAnnunciator.AckURL = U(TInstrumentsConst.CS_ACK_URL);
            for (int i = 0; i < TInstrumentsConst.CI_TILES; i++)
            {
                TsgcHTMLAnnunciatorTile oTile = FAnnunciator.Tiles.Add();
                oTile.TileID = TInstrumentsConst.CS_TILE_IDS[i];
                oTile.Caption = TInstrumentsConst.CS_TILE_CAPTIONS[i];
            }

            FOdometer = new TsgcHTMLComponent_Odometer();
            FOdometer.OdometerID = "odo";
            FOdometer.Digits = 7;
            FOdometer.Decimals = 1;
            FOdometer.Unit_ = "m3";

            FVUNeedle = new TsgcHTMLComponent_VUMeter();
            FVUNeedle.VUMeterID = "vu-l";
            FVUNeedle.Title = "Pump noise L";
            FVUNeedle.MeterStyle = TsgcHTMLVUMeterStyle.vmsNeedle;
            FVUNeedle.PeakHold = true;

            FVUBar = new TsgcHTMLComponent_VUMeter();
            FVUBar.VUMeterID = "vu-r";
            FVUBar.Title = "Pump noise R";
            FVUBar.MeterStyle = TsgcHTMLVUMeterStyle.vmsBar;
            FVUBar.PeakHold = true;

            FClockLocal = new TsgcHTMLComponent_AnalogClock();
            FClockLocal.AnalogClockID = "clk-local";
            FClockLocal.Title = "Plant time";
            FClockLocal.Live = true;
            FClockLocal.Numerals = TsgcHTMLClockNumerals.cknRoman;
            FClockLocal.FaceColor = "#1b232d";
            FClockLocal.HandColor = "#e9ecef";

            FClockUTC = new TsgcHTMLComponent_AnalogClock();
            FClockUTC.AnalogClockID = "clk-utc";
            FClockUTC.Title = "UTC";
            FClockUTC.Live = true;
            FClockUTC.UseUTCOffset = true;
            FClockUTC.UTCOffset = 0;
            FClockUTC.Numerals = TsgcHTMLClockNumerals.cknArabic;
            FClockUTC.ShowSeconds = false;
            FClockUTC.FaceColor = "#1b232d";
            FClockUTC.HandColor = "#e9ecef";

            FCompass = new TsgcHTMLComponent_Compass();
            FCompass.CompassID = "compass";
            FCompass.Title = "Wind direction (stack)";
            FCompass.CompassStyle = TsgcHTMLCompassStyle.csRotatingNeedle;
            FCompass.ShowTarget = true;
            FCompass.TargetHeading = 270;
            FCompass.Animate = true;
        }

        private void CreateControlsPage()
        {
            FKnob = new TsgcHTMLComponent_Knob();
            FKnob.KnobID = "k-setpoint";
            FKnob.Title = "Pressure setpoint";
            FKnob.Unit_ = "bar";
            FKnob.MinValue = 0;
            FKnob.MaxValue = 10;
            FKnob.Step = 0.1;
            FKnob.Size = 180;
            FKnob.FieldName = "value";
            FKnob.PostURL = CtlURL(TInstrumentsConst.CS_CTL_SETPOINT);
            AddPressureRanges(FKnob.Ranges);

            FSwPump = new TsgcHTMLComponent_ToggleSwitch();
            FSwPump.ToggleSwitchID = "sw-pump";
            FSwPump.Caption = "Feed pump";
            FSwPump.SwitchStyle = TsgcHTMLToggleSwitchStyle.tssToggle;
            FSwPump.Size = 32;
            FSwPump.FieldName = "value";
            FSwPump.PostURL = CtlURL(TInstrumentsConst.CS_CTL_PUMP);

            FSwHeater = new TsgcHTMLComponent_ToggleSwitch();
            FSwHeater.ToggleSwitchID = "sw-heater";
            FSwHeater.Caption = "Heater";
            FSwHeater.SwitchStyle = TsgcHTMLToggleSwitchStyle.tssRocker;
            FSwHeater.OnColor = CS_ORANGE;
            FSwHeater.Size = 32;
            FSwHeater.FieldName = "value";
            FSwHeater.PostURL = CtlURL(TInstrumentsConst.CS_CTL_HEATER);

            FBtnHorn = new TsgcHTMLComponent_PushButton();
            FBtnHorn.PushButtonID = "pb-horn";
            FBtnHorn.Caption = "Horn (hold)";
            FBtnHorn.Momentary = true;
            FBtnHorn.ButtonStyle = TsgcHTMLButtonStyle.bsWarning;
            FBtnHorn.LEDColor = CS_YELLOW;
            FBtnHorn.FieldName = "value";
            FBtnHorn.PostURL = CtlURL(TInstrumentsConst.CS_CTL_HORN);

            FBtnEStop = new TsgcHTMLComponent_PushButton();
            FBtnEStop.PushButtonID = "pb-estop";
            FBtnEStop.Caption = "Emergency stop";
            FBtnEStop.Confirm = "Stop the pump and the heater now?";
            FBtnEStop.ButtonStyle = TsgcHTMLButtonStyle.bsDanger;
            FBtnEStop.LEDColor = CS_RED;
            FBtnEStop.FieldName = "value";
            FBtnEStop.PostURL = CtlURL(TInstrumentsConst.CS_CTL_ESTOP);

            FBtnReset = new TsgcHTMLComponent_PushButton();
            FBtnReset.PushButtonID = "pb-reset";
            FBtnReset.Caption = "Reset";
            FBtnReset.ButtonStyle = TsgcHTMLButtonStyle.bsSecondary;
            FBtnReset.FieldName = "value";
            FBtnReset.PostURL = CtlURL(TInstrumentsConst.CS_CTL_RESET);

            FStepSpeed = new TsgcHTMLComponent_NumericStepper();
            FStepSpeed.NumericStepperID = "ns-speed";
            FStepSpeed.MinValue = 0;
            FStepSpeed.MaxValue = 3000;
            FStepSpeed.Step = 50;
            FStepSpeed.Unit_ = "rpm";
            FStepSpeed.FieldName = "value";
            FStepSpeed.PostURL = CtlURL(TInstrumentsConst.CS_CTL_SPEED);

            FSliderValve = new TsgcHTMLComponent_Slider();
            FSliderValve.SliderID = "sl-valve";
            FSliderValve.LabelText = "Outlet valve %";
            FSliderValve.Orientation = TsgcHTMLSliderOrientation.sloVertical;
            FSliderValve.Min = 0;
            FSliderValve.Max = 100;
            FSliderValve.Step = 5;
            FSliderValve.FieldName = "value";
            FSliderValve.PostURL = CtlURL(TInstrumentsConst.CS_CTL_VALVE);

            FSliderBand = new TsgcHTMLComponent_Slider();
            FSliderBand.SliderID = "sl-band";
            FSliderBand.LabelText = "Pressure alarm band (x0.1 bar)";
            FSliderBand.DualThumb = true;
            FSliderBand.Min = 0;
            FSliderBand.Max = 100;
            FSliderBand.FieldName = "value";
            FSliderBand.PostURL = CtlURL(TInstrumentsConst.CS_CTL_BAND);

            // the process as seen from the Controls page
            FCtlGauge = new TsgcHTMLComponent_Gauge();
            FCtlGauge.GaugeID = "c-press";
            FCtlGauge.Title = "Boiler pressure";
            FCtlGauge.Unit_ = "bar";
            FCtlGauge.MinValue = 0;
            FCtlGauge.MaxValue = 10;
            FCtlGauge.Width = 220;
            FCtlGauge.ArcAngle = 270;
            FCtlGauge.ShowTicks = true;
            FCtlGauge.NeedleStyle = TsgcHTMLGaugeNeedleStyle.gnsTriangle;
            FCtlGauge.Animate = true;
            AddPressureRanges(FCtlGauge.Ranges);

            FCtlSeg = new TsgcHTMLComponent_SegmentDisplay();
            FCtlSeg.SegmentDisplayID = "c-speed";
            FCtlSeg.Title = "Actual speed (rpm)";
            FCtlSeg.Digits = 4;
            FCtlSeg.OnColor = CS_TEAL;
            FCtlSeg.BackgroundColor = "#000000";
            FCtlSeg.DigitHeight = 36;

            FCtlLEDPump = NewLED("c-led-pump", "Pump", CS_GREEN);
            FCtlLEDHeater = NewLED("c-led-heater", "Heater", CS_ORANGE);
            FCtlLEDEStop = NewLED("c-led-estop", "E-stop", CS_RED);
        }

        private void CreateTrendsPage()
        {
            FStrip = new TsgcHTMLComponent_StripChart();
            FStrip.StripChartID = "strip";
            FStrip.Title = "Process trends (% of span)";
            FStrip.MinValue = 0;
            FStrip.MaxValue = 100;
            FStrip.TimeWindow = 60;
            FStrip.DrawingWidth = 900;
            FStrip.DrawingHeight = 280;
            FStrip.BackgroundColor = "#101418";
            TsgcHTMLStripChartPen oPen = FStrip.Pens.Add();
            oPen.Caption = "Pressure (0-10 bar)";
            oPen.Color = CS_BLUE;
            oPen.MinValue = 0;
            oPen.MaxValue = 10;
            oPen = FStrip.Pens.Add();
            oPen.Caption = "Temperature (0-150 C)";
            oPen.Color = CS_ORANGE;
            oPen.MinValue = 0;
            oPen.MaxValue = 150;
            oPen = FStrip.Pens.Add();
            oPen.Caption = "Flow (0-50 m3/h)";
            oPen.Color = CS_TEAL;
            oPen.MinValue = 0;
            oPen.MaxValue = 50;

            FScope = new TsgcHTMLComponent_Oscilloscope();
            FScope.OscilloscopeID = "scope";
            FScope.Title = "Pump vibration and tacho";
            FScope.TimePerDiv = 0.002;
            FScope.TriggerMode = TsgcHTMLOscilloscopeTriggerMode.otmAuto;
            FScope.TriggerLevel = 0;
            FScope.TriggerChannel = 0;
            FScope.DrawingWidth = 640;
            FScope.DrawingHeight = 400;
            TsgcHTMLOscilloscopeChannel oChannel = FScope.Channels.Add();
            oChannel.Caption = "CH1 vibration";
            oChannel.Color = "#f7d046";
            oChannel.VoltsPerDiv = 1;
            oChannel.Offset = 1.5;
            oChannel = FScope.Channels.Add();
            oChannel.Caption = "CH2 tacho";
            oChannel.Color = "#4dd0e1";
            oChannel.VoltsPerDiv = 1;
            oChannel.Offset = -2.5;
        }

        private static bool AnyAlarm(TInstrumentsState S)
        {
            for (int i = 0; i < TInstrumentsConst.CI_TILES; i++)
                if (S.TileState[i] == TsgcHTMLAnnunciatorState.ansAlarm && !S.TileAck[i])
                    return true;
            return false;
        }

        private static string StatusText(TInstrumentsState S)
        {
            if (S.EStop)
                return "ESTOP";
            else if (AnyAlarm(S))
                return "ALARM";
            else if (S.Pump)
                return "RUN";
            else
                return "IDLE";
        }

        // copy the process state into the instrument properties
        public void ApplyState(TInstrumentsState S)
        {
            FGaugePress.Value = S.Pressure;
            FGaugeTemp.Value = S.Temperature;
            FGaugeFlow.Value = S.Flow;
            FLinLoad.Value = S.Load;
            FLinTemp.Value = S.Temperature;
            FLinTank.Value = S.TankLevel;
            FLEDBarSpeed.Value = S.ActualSpeed;
            FSegFlow.Text = S.Flow.ToString("00.0", CultureInfo.InvariantCulture);
            FSegStatus.Text = StatusText(S);
            FLEDPump.State = S.Pump;
            FLEDHeater.State = S.Heater;
            FLEDAlarm.State = AnyAlarm(S);
            FLEDAlarm.Blink = FLEDAlarm.State;
            FLEDHorn.State = S.Horn;
            FLEDHorn.Blink = S.Horn;

            for (int i = 0; i < TInstrumentsConst.CI_TILES; i++)
            {
                FAnnunciator.Tiles[i].State = S.TileState[i];
                FAnnunciator.Tiles[i].Acknowledged = S.TileAck[i];
            }
            FOdometer.Value = S.TotalVolume;
            FVUNeedle.Value = S.NoiseL;
            FVUBar.Value = S.NoiseR;
            FCompass.Heading = S.Heading;

            FKnob.Value = S.Setpoint;
            FSwPump.Checked = S.Pump;
            FSwHeater.Checked = S.Heater;
            FBtnHorn.LEDState = S.Horn;
            FBtnEStop.LEDState = S.EStop;
            FStepSpeed.Value = S.Speed;
            FSliderValve.Value = S.Valve;
            FSliderBand.ValueLow = S.BandLow;
            FSliderBand.ValueHigh = S.BandHigh;
            FCtlGauge.Value = S.Pressure;
            FCtlSeg.Text = ((long)Math.Round(S.ActualSpeed)).ToString(
                CultureInfo.InvariantCulture);
            FCtlLEDPump.State = S.Pump;
            FCtlLEDHeater.State = S.Heater;
            FCtlLEDEStop.State = S.EStop;
            FCtlLEDEStop.Blink = S.EStop;
        }

        private string WrapPage(string aTitle, string aSubtitle, string aActive,
            TsgcHTMLContainer aBody)
        {
            TsgcHTMLContainer oPage = new TsgcHTMLContainer();
            TsgcHTMLNavbarNode oNav = new TsgcHTMLNavbarNode();
            oNav.Brand = "sgcHTML Control Room";
            oNav.BrandHref = U(TInstrumentsConst.CS_PAGE_GAUGES);
            oNav.Dark = true;
            oNav.AddItem("Gauges", U(TInstrumentsConst.CS_PAGE_GAUGES),
                aActive == TInstrumentsConst.CS_PAGE_GAUGES);
            oNav.AddItem("Panels", U(TInstrumentsConst.CS_PAGE_PANELS),
                aActive == TInstrumentsConst.CS_PAGE_PANELS);
            oNav.AddItem("Controls", U(TInstrumentsConst.CS_PAGE_CONTROLS),
                aActive == TInstrumentsConst.CS_PAGE_CONTROLS);
            oNav.AddItem("Trends", U(TInstrumentsConst.CS_PAGE_TRENDS),
                aActive == TInstrumentsConst.CS_PAGE_TRENDS);
            oPage.Add(oNav);

            TsgcHTMLContainer oMain = new TsgcHTMLContainer("main");
            oMain.CSSClass = "container-fluid py-3";
            TsgcHTMLHeading oHeading = new TsgcHTMLHeading(aTitle, 1);
            oHeading.CSSClass = "h4 mb-1";
            oMain.Add(oHeading);
            TsgcHTMLParagraph oSub = new TsgcHTMLParagraph(aSubtitle);
            oSub.CSSClass = "room-sub mb-3";
            oMain.Add(oSub);
            oMain.Add(aBody);
            oPage.Add(oMain);

            // htmx (control posts) + sgcWebSockets + the sgcHTMX bridge, which opens
            // a WebSocket to this page URL and swaps the pushed OOB fragments
            oPage.Add(new TsgcHTMLScript(U("/htmx.min.js")));
            oPage.Add(new TsgcHTMLScript(U("/sgcWebSockets.js")));
            oPage.Add(new TsgcHTMLScript(U("/sgcHTMX.min.js")));
            TsgcHTMLScript oScript = new TsgcHTMLScript();
            oScript.Code = "document.addEventListener(\"DOMContentLoaded\"," +
                "function(){if(window.sgcHTMX&&sgcHTMX.init){sgcHTMX.init({host:" +
                "(location.protocol===\"https:\"?\"wss:\":\"ws:\")+\"//\"+location.host+" +
                "location.pathname});}});";
            oPage.Add(oScript);

            TsgcHTMLTemplate_Bootstrap oTpl = new TsgcHTMLTemplate_Bootstrap();
            oTpl.Title = aTitle + " - sgcHTML Control Room";
            oTpl.HtmlLang = "en";
            oTpl.HtmlTheme = "dark";
            oTpl.DarkMode = true;
            oTpl.Viewport = "width=device-width, initial-scale=1";
            oTpl.BootstrapCSSPath = U("/bootstrap.min.css");
            oTpl.BootstrapJSPath = U("/bootstrap.bundle.min.js");
            oTpl.CustomCSS = CS_ROOM_CSS;
            oTpl.BodyContent = oPage.HTML;
            return oTpl.GetHTML();
        }

        // ----- full pages ----- //

        public string PageGauges(TInstrumentsState S)
        {
            ApplyState(S);
            TsgcHTMLContainer oBody = new TsgcHTMLContainer();
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";
            TsgcHTMLCard oCard = NewCard("Gauge, 270" + CS_DEG + " arc");
            oCard.Body.Add(FGaugePress);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oCard = NewCard("Gauge, 180" + CS_DEG + " arc");
            oCard.Body.Add(FGaugeTemp);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oCard = NewCard("Gauge, 90" + CS_DEG + " arc");
            oCard.Body.Add(FGaugeFlow);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oBody.Add(oRow);

            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";
            oCard = NewCard("LinearGauge, bar");
            oCard.Body.Add(FLinLoad);
            oCard.Body.Add(new TsgcHTMLHeading("LEDBar", 6));
            oCard.Body.Add(FLEDBarSpeed);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oCard = NewCard("LinearGauge, thermometer");
            oCard.Body.Add(FLinTemp);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oCard = NewCard("LinearGauge, tank");
            oCard.Body.Add(FLinTank);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oBody.Add(oRow);

            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            oCard = NewCard("SegmentDisplay");
            oCard.Body.Add(FSegFlow);
            oCard.Body.Add(FSegStatus);
            oRow.Col(TsgcHTMLColWidth.cw6).Add(oCard);
            oCard = NewCard("LED indicators (alarm and horn blink)");
            TsgcHTMLContainer oLEDs = new TsgcHTMLContainer();
            oLEDs.CSSClass = "d-flex justify-content-around align-items-center " +
                "flex-wrap gap-3 py-4";
            oLEDs.Add(FLEDPump);
            oLEDs.Add(FLEDHeater);
            oLEDs.Add(FLEDAlarm);
            oLEDs.Add(FLEDHorn);
            oCard.Body.Add(oLEDs);
            oRow.Col(TsgcHTMLColWidth.cw6).Add(oCard);
            oBody.Add(oRow);

            return WrapPage("Gauges", "Boiler loop instruments. The server " +
                "simulates the process and pushes every value twice a second.",
                TInstrumentsConst.CS_PAGE_GAUGES, oBody);
        }

        public string PagePanels(TInstrumentsState S)
        {
            ApplyState(S);
            TsgcHTMLContainer oBody = new TsgcHTMLContainer();
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";
            TsgcHTMLCard oCard = NewCard("Annunciator (click a flashing tile to acknowledge)");
            oCard.Body.Add(FAnnunciator);
            oRow.Col(TsgcHTMLColWidth.cw8).Add(oCard);
            oCard = NewCard("Odometer, total pumped volume");
            oCard.BodyClass = "d-flex justify-content-center align-items-center";
            oCard.Body.Add(FOdometer);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oBody.Add(oRow);

            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            oCard = NewCard("VUMeter with PeakHold");
            oCard.Body.Add(FVUNeedle);
            oCard.Body.Add(FVUBar);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oCard = NewCard("AnalogClock (Live)");
            TsgcHTMLContainer oFlex = new TsgcHTMLContainer();
            oFlex.CSSClass = "d-flex justify-content-around flex-wrap";
            oFlex.Add(FClockLocal);
            oFlex.Add(FClockUTC);
            oCard.Body.Add(oFlex);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oCard = NewCard("Compass (Animate, target 270" + CS_DEG + ")");
            oCard.Body.Add(FCompass);
            oRow.Col(TsgcHTMLColWidth.cw4).Add(oCard);
            oBody.Add(oRow);

            return WrapPage("Panels", "Alarm annunciator, totalizer, noise " +
                "meters, clocks and wind direction.", TInstrumentsConst.CS_PAGE_PANELS,
                oBody);
        }

        public string PageControls(TInstrumentsState S)
        {
            ApplyState(S);
            TsgcHTMLContainer oBody = new TsgcHTMLContainer();
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";
            TsgcHTMLCard oCard = NewCard("Knob, setpoint");
            oCard.Body.Add(FKnob);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oCard = NewCard("ToggleSwitch, toggle and rocker");
            TsgcHTMLContainer oFlex = new TsgcHTMLContainer();
            oFlex.CSSClass = "d-flex flex-column align-items-center gap-4 py-3";
            oFlex.Add(FSwPump);
            oFlex.Add(FSwHeater);
            oCard.Body.Add(oFlex);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oCard = NewCard("PushButton, momentary and confirm");
            oFlex = new TsgcHTMLContainer();
            oFlex.CSSClass = "d-flex flex-column align-items-center gap-3 py-3";
            oFlex.Add(FBtnHorn);
            oFlex.Add(FBtnEStop);
            oFlex.Add(FBtnReset);
            oCard.Body.Add(oFlex);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oCard = NewCard("NumericStepper, pump speed");
            oCard.Body.Add(FStepSpeed);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oBody.Add(oRow);

            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            oCard = NewCard("Slider, vertical");
            oCard.Body.Add(FSliderValve);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oCard = NewCard("Slider, DualThumb");
            oCard.Body.Add(FSliderBand);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oCard = NewCard("Process response");
            oCard.Body.Add(FCtlGauge);
            oCard.Body.Add(FCtlSeg);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oCard = NewCard("Status");
            oFlex = new TsgcHTMLContainer();
            oFlex.CSSClass = "d-flex flex-column align-items-center gap-3 py-3";
            oFlex.Add(FCtlLEDPump);
            oFlex.Add(FCtlLEDHeater);
            oFlex.Add(FCtlLEDEStop);
            oCard.Body.Add(oFlex);
            oRow.Col(TsgcHTMLColWidth.cw3).Add(oCard);
            oBody.Add(oRow);

            return WrapPage("Controls", "Every control posts to the server. The " +
                "server changes the process and pushes the new state to every browser.",
                TInstrumentsConst.CS_PAGE_CONTROLS, oBody);
        }

        public string PageTrends(TInstrumentsState S)
        {
            ApplyState(S);
            TsgcHTMLContainer oBody = new TsgcHTMLContainer();
            TsgcHTMLRow oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3 mb-3";
            TsgcHTMLCard oCard = NewCard("StripChart, 3 pens (2 samples per second)");
            oCard.Body.Add(FStrip);
            oRow.Col(TsgcHTMLColWidth.cw12).Add(oCard);
            oBody.Add(oRow);

            oRow = new TsgcHTMLRow();
            oRow.CSSClass = "g-3";
            oCard = NewCard("Oscilloscope, 2 channels (5 frames per second)");
            oCard.Body.Add(FScope);
            oRow.Col(TsgcHTMLColWidth.cw12).Add(oCard);
            oBody.Add(oRow);

            return WrapPage("Trends", "Canvas instruments fed by small push " +
                "scripts, no page refresh and no polling.", TInstrumentsConst.CS_PAGE_TRENDS,
                oBody);
        }

        // ----- live fragments pushed every 500 ms ----- //

        public string FragGauges(TInstrumentsState S)
        {
            ApplyState(S);
            return FGaugePress.GetLiveValueFragment(S.Pressure) +
                FGaugeTemp.GetLiveValueFragment(S.Temperature) +
                FGaugeFlow.GetLiveValueFragment(S.Flow) +
                FLinLoad.GetLiveValueFragment(S.Load) +
                FLinTemp.GetLiveValueFragment(S.Temperature) +
                FLinTank.GetLiveValueFragment(S.TankLevel) +
                FLEDBarSpeed.GetLiveValueFragment(S.ActualSpeed) +
                FSegFlow.GetLiveTextFragment(FSegFlow.Text) +
                FSegStatus.GetLiveTextFragment(FSegStatus.Text) +
                FLEDPump.GetLiveStateFragment(S.Pump) +
                FLEDHeater.GetLiveStateFragment(S.Heater) +
                FLEDAlarm.GetLiveStateFragment(FLEDAlarm.State) +
                FLEDHorn.GetLiveStateFragment(S.Horn);
        }

        public string FragPanels(TInstrumentsState S)
        {
            ApplyState(S);
            return FOdometer.GetLiveValueFragment(S.TotalVolume) +
                FVUNeedle.GetLiveValueFragment(S.NoiseL) +
                FVUBar.GetLiveValueFragment(S.NoiseR) +
                FCompass.GetLiveValueFragment(S.Heading);
        }

        public string FragControls(TInstrumentsState S)
        {
            ApplyState(S);
            return FCtlGauge.GetLiveValueFragment(S.Pressure) +
                FCtlSeg.GetLiveTextFragment(FCtlSeg.Text) +
                FCtlLEDPump.GetLiveStateFragment(S.Pump) +
                FCtlLEDHeater.GetLiveStateFragment(S.Heater) +
                FCtlLEDEStop.GetLiveStateFragment(S.EStop);
        }

        public string FragStrip(TInstrumentsState S)
        {
            return FStrip.GetLivePushScript(new double[] { S.Pressure, S.Temperature,
                S.Flow });
        }

        // one annunciator tile (pushed when it changes or is acknowledged)
        public string FragTile(TInstrumentsState S, int aIndex)
        {
            ApplyState(S);
            return FAnnunciator.GetLiveTileFragment(TInstrumentsConst.CS_TILE_IDS[aIndex]);
        }

        // the control a POST changed, pushed to every Controls page so all the
        // browsers show the same position
        public string FragControl(TInstrumentsState S, string aName)
        {
            ApplyState(S);
            if (aName == TInstrumentsConst.CS_CTL_SETPOINT)
                return FKnob.GetLiveValueFragment(S.Setpoint);
            else if (aName == TInstrumentsConst.CS_CTL_SPEED)
                return FStepSpeed.GetLiveValueFragment(S.Speed);
            else if (aName == TInstrumentsConst.CS_CTL_VALVE)
                return FSliderValve.GetLiveValueFragment(S.Valve);
            else if (aName == TInstrumentsConst.CS_CTL_BAND)
                return FSliderBand.GetLiveValuesFragment(S.BandLow, S.BandHigh);
            else if (aName == TInstrumentsConst.CS_CTL_HORN)
                return FBtnHorn.GetLiveLEDFragment(S.Horn);
            else if (aName == TInstrumentsConst.CS_CTL_PUMP ||
                aName == TInstrumentsConst.CS_CTL_HEATER ||
                aName == TInstrumentsConst.CS_CTL_ESTOP ||
                aName == TInstrumentsConst.CS_CTL_RESET)
                // the e-stop also forces both switches off
                return FSwPump.GetLiveStateFragment(S.Pump) +
                    FSwHeater.GetLiveStateFragment(S.Heater) +
                    FBtnEStop.GetLiveLEDFragment(S.EStop);
            return "";
        }

        // one oscilloscope frame per channel (~5 Hz)
        public string FragScope(double aTime, double aSpeed, bool aPump)
        {
            const int CI_SAMPLES = 200;
            // 200 samples over the 20 ms screen (10 divisions x 2 ms)
            double[] vCh1 = new double[CI_SAMPLES];
            double[] vCh2 = new double[CI_SAMPLES];
            double vF1 = 120 + aSpeed / 25;
            double vF2 = Math.Max(aSpeed / 20, 1);
            for (int i = 0; i < CI_SAMPLES; i++)
            {
                double vT = aTime + i * 0.0001;
                if (aPump)
                {
                    vCh1[i] = 1.6 * Math.Sin(2 * Math.PI * vF1 * vT) + 0.4 *
                        Math.Sin(2 * Math.PI * 3 * vF1 * vT) +
                        (FRandom.NextDouble() - 0.5) * 0.2;
                    double vX = vT * vF2;
                    if (vX - Math.Truncate(vX) < 0.5)
                        vCh2[i] = 1.5;
                    else
                        vCh2[i] = -1.5;
                }
                else
                {
                    vCh1[i] = (FRandom.NextDouble() - 0.5) * 0.1;
                    vCh2[i] = -1.5;
                }
            }
            return FScope.GetLivePushScript(0, vCh1) +
                FScope.GetLivePushScript(1, vCh2);
        }
    }
}
