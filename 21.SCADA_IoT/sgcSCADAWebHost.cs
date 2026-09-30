// ***************************************************************************
//  sgcSCADAWeb - SCADA + IoT demo on ASP.NET Core
//  Hosting layer: replaces demos\60.HTML\21.SCADA_IoT\Server\sgcSCADA_Server.cs
//  (the TsgcWebSocketHTTPServer + TsgcHTMX_Engine_Server host).
//
//  - Owns the plant (sgcSCADA_Plant.cs) and starts / stops its PLC thread.
//  - Renders the dashboard page (same TsgcHTMLComponent_Site shell as 60.HTML).
//  - /cmd and /ack handlers (same contract as the 60.HTML DispatchRequest).
//  - Push: the 60.HTML host called TsgcHTMX_Engine_Server.BroadcastFragment.
//    Under Kestrel the engine has no Server bound (BroadcastFragment is a
//    no-op), so every live fragment goes through ISgcHtmlHub.BroadcastAsync to
//    the browsers connected on the adapter's /ws channel.
// ***************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;

namespace SCADADemo
{
    public sealed class SCADAWebHost : IDisposable
    {
        private readonly ISgcHtmlHub FHub;
        private TsgcSCADAPlant FPlant;

        public TsgcSCADAPlant Plant { get { return FPlant; } }

        public SCADAWebHost(TsgcSCADAConfig aConfig, ISgcHtmlHub aHub)
        {
            FHub = aHub;
            FPlant = new TsgcSCADAPlant(aConfig);
            FPlant.OnPush = PushFragment;
        }

        public void Start()
        {
            FPlant.Start();
        }

        public void Stop()
        {
            if (FPlant != null)
                FPlant.Stop();
        }

        public void Dispose()
        {
            if (FPlant != null)
            {
                FPlant.Dispose();
                FPlant = null;
            }
        }

        // conf.json -> config (missing file or keys keep the defaults). Same
        // file layout as the 60.HTML demo; the http section is ignored here
        // (Kestrel owns the port, appsettings.json).
        public static TsgcSCADAConfig LoadSCADAConfig(string aFileName)
        {
            TsgcSCADAConfig vResult = new TsgcSCADAConfig();
            if (!File.Exists(aFileName))
                return vResult;
            using (JsonDocument oJSON = JsonDocument.Parse(File.ReadAllText(aFileName)))
            {
                JsonElement vRoot = oJSON.RootElement;
                JsonElement vMQTT;
                if (vRoot.ValueKind == JsonValueKind.Object &&
                    vRoot.TryGetProperty("mqtt", out vMQTT) &&
                    vMQTT.ValueKind == JsonValueKind.Object)
                {
                    vResult.Mode = Str(vMQTT, "mode", vResult.Mode);
                    vResult.Host = Str(vMQTT, "host", vResult.Host);
                    int vPort;
                    if (int.TryParse(Str(vMQTT, "port", ""), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out vPort))
                        vResult.MQTTPort = vPort;
                    vResult.TopicPrefix = Str(vMQTT, "topicPrefix", vResult.TopicPrefix);
                }
            }
            return vResult;
        }

        private static string Str(JsonElement aParent, string aName, string aDefault)
        {
            JsonElement vNode;
            if (!aParent.TryGetProperty(aName, out vNode))
                return aDefault;
            switch (vNode.ValueKind)
            {
                case JsonValueKind.String: return vNode.GetString() ?? "";
                case JsonValueKind.Null: return "";
                default: return vNode.GetRawText();
            }
        }

        // Deliver an OOB fragment to every browser on the /ws channel. Called
        // from the PLC thread and from /ack.
        public void PushFragment(string aHTML)
        {
            if (string.IsNullOrEmpty(aHTML))
                return;
            try
            {
                FHub.BroadcastAsync(aHTML).GetAwaiter().GetResult();
            }
            catch (Exception E)
            {
                Console.WriteLine("[push] error: " + E.Message);
            }
        }

        public string BuildPage()
        {
            using (TsgcHTMLComponent_Site oSite = new TsgcHTMLComponent_Site())
            {
                oSite.Title = "sgcHTML SCADA + IoT";
                oSite.Layout = TsgcHTMLSiteLayout.slSidebarLeft;
                oSite.Theme.Preset = TsgcHTMLSiteThemePreset.stpBlue;
                oSite.Theme.Mode = TsgcHTMLSiteThemeMode.stmLight;
                oSite.Theme.SidebarDark = true;
                oSite.Brand.Text = "Cooling Plant";
                oSite.Brand.Href = "/";
                oSite.Header.ShowThemeSwitcher = true;
                oSite.Footer.Text = "copyright (c) 2026 eSeGeCe.com";
                oSite.AddMenu("Plant", "/", "").Active = true;
                oSite.AddContent(FPlant.RenderDashboard());
                // htmx (hx-post of the SCADA / annunciator clicks) + the sgcHTMX
                // bridge on the adapter's WebSocket channel (/ws), where the
                // plant's OOB fragments arrive.
                oSite.BodyEndHTML = "<script src=\"/htmx.min.js\"></script>" +
                    "<script src=\"/sgcWebSockets.js\"></script>" +
                    "<script src=\"/sgcHTMX.min.js\"></script>" + "<script>" +
                    "document.addEventListener(\"DOMContentLoaded\",function(){" +
                    "if(window.sgcHTMX&&sgcHTMX.init){sgcHTMX.init({host:" +
                    "(location.protocol==='https:'?'wss:':'ws:')+'//'+" +
                    "location.host+'/ws'});}});</script>";
                return oSite.HTML;
            }
        }

        // query string first, then the urlencoded form body (60.HTML GetParam)
        private static string GetParam(HttpContext aContext, IFormCollection aForm, string aName)
        {
            string vValue = aContext.Request.Query[aName];
            if (vValue != null)
                return vValue;
            if (aForm != null)
            {
                vValue = aForm[aName];
                if (vValue != null)
                    return vValue;
            }
            return "";
        }

        // SCADA symbol click: hx-post with hx-vals {"panel":..,"symbol":..}
        public IResult CommandPost(HttpContext aContext, IFormCollection aForm)
        {
            FPlant.Command(GetParam(aContext, aForm, "symbol"));
            return Results.StatusCode(StatusCodes.Status204NoContent);
        }

        // annunciator tile click: hx-vals {"annunciator":..,"tile":..}
        public IResult AckPost(HttpContext aContext, IFormCollection aForm)
        {
            PushFragment(FPlant.Acknowledge(GetParam(aContext, aForm, "tile")));
            return Results.StatusCode(StatusCodes.Status204NoContent);
        }
    }
}
