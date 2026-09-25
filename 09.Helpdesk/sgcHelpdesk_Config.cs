// ***************************************************************************
//  sgcHelpdesk - support-ticket helpdesk web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\09.Helpdesk\sgcHelpdesk_Config.pas
//
//  The Delphi loader used the sgcJSON parser; here System.Text.Json parses the
//  same { listen, database, admin } config shape. Tolerant: a missing file
//  leaves the defaults in place.
// ***************************************************************************

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Helpdesk
{
    public class THelpdeskConfigLoader
    {
        private readonly THelpdeskServerConfig FConfig;

        public THelpdeskConfigLoader(THelpdeskServerConfig aConfig)
        {
            if (aConfig == null)
                throw new EHelpdeskError("THelpdeskConfigLoader: config is nil");
            FConfig = aConfig;
        }

        // Tolerant: a missing file leaves the defaults in place (no exception).
        public void LoadFromFile(string aPath)
        {
            if (string.IsNullOrEmpty(aPath) || !File.Exists(aPath))
                return;

            string vText;
            try
            {
                vText = File.ReadAllText(aPath, Encoding.UTF8);
            }
            catch (Exception E)
            {
                throw new EHelpdeskError(
                    string.Format("Failed to read config file {0}: {1}", aPath, E.Message));
            }
            LoadFromJSONString(vText);
        }

        public void LoadFromJSONString(string aJSON)
        {
            if (string.IsNullOrWhiteSpace(aJSON))
                return;
            if (FConfig == null)
                throw new EHelpdeskError("Config target is nil");

            JsonDocument oJSON;
            try
            {
                oJSON = JsonDocument.Parse(aJSON);
            }
            catch (Exception E)
            {
                throw new EHelpdeskError(string.Format("Invalid JSON config: {0}", E.Message));
            }

            using (oJSON)
            {
                JsonElement vRoot = oJSON.RootElement;
                if (vRoot.ValueKind != JsonValueKind.Object)
                    return;

                JsonElement vListen;
                if (vRoot.TryGetProperty("listen", out vListen) &&
                    vListen.ValueKind == JsonValueKind.Object)
                    ParseListen(vListen);

                JsonElement vDatabase;
                if (vRoot.TryGetProperty("database", out vDatabase) &&
                    vDatabase.ValueKind == JsonValueKind.Object)
                    ParseDatabase(vDatabase);

                JsonElement vAdmin;
                if (vRoot.TryGetProperty("admin", out vAdmin) &&
                    vAdmin.ValueKind == JsonValueKind.Object)
                    ParseAdmin(vAdmin);
            }
        }

        private string NodeToString(JsonElement aValue)
        {
            switch (aValue.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return "";
                case JsonValueKind.String:
                    {
                        string vStr = aValue.GetString();
                        return vStr == null ? "" : vStr;
                    }
                case JsonValueKind.Number:
                    return aValue.GetRawText();
                case JsonValueKind.True:
                    return "True";
                case JsonValueKind.False:
                    return "False";
                default:
                    return "";
            }
        }

        private int NodeToInt(JsonElement aValue, int aDefault)
        {
            int vResult = aDefault;
            switch (aValue.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return vResult;
                case JsonValueKind.Number:
                    {
                        int vInt;
                        if (aValue.TryGetInt32(out vInt))
                            return vInt;
                        double vDbl;
                        if (aValue.TryGetDouble(out vDbl))
                            return (int)vDbl;
                        return vResult;
                    }
            }

            string vStr = NodeToString(aValue);
            if (vStr.Length == 0)
                return vResult;

            int vParsed;
            if (int.TryParse(vStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out vParsed))
                vResult = vParsed;
            return vResult;
        }

        private string JNodeStr(JsonElement aParent, string aName, string aDefault)
        {
            string vResult = aDefault;
            JsonElement vNode;
            if (!aParent.TryGetProperty(aName, out vNode))
                return vResult;
            string vStr = NodeToString(vNode);
            if (vStr.Length != 0)
                vResult = vStr;
            return vResult;
        }

        private int JNodeInt(JsonElement aParent, string aName, int aDefault)
        {
            JsonElement vNode;
            if (!aParent.TryGetProperty(aName, out vNode))
                return aDefault;
            return NodeToInt(vNode, aDefault);
        }

        private void ParseListen(JsonElement aNode)
        {
            FConfig.ListenAddress = JNodeStr(aNode, "address", FConfig.ListenAddress);
            FConfig.ListenPort = JNodeInt(aNode, "port", FConfig.ListenPort);
        }

        private void ParseDatabase(JsonElement aNode)
        {
            FConfig.DatabaseFile = JNodeStr(aNode, "file", FConfig.DatabaseFile);
        }

        private void ParseAdmin(JsonElement aNode)
        {
            FConfig.AdminUser = JNodeStr(aNode, "user", FConfig.AdminUser);
            FConfig.AdminPassword = JNodeStr(aNode, "password", FConfig.AdminPassword);
        }
    }
}
