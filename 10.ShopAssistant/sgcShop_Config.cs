// ***************************************************************************
//  sgcShopAssistant - AI storefront demo (TechNest) (managed port)
//  Port of delphi\Demos\60.HTML\10.ShopAssistant\sgcShop_Config.pas
//
//  The Delphi loader parses the JSON with sgcJSON; the managed port uses
//  System.Text.Json. Tolerant of a missing file (defaults from
//  TShopServerConfig stay in place) and of a missing/unknown 'ai' section
//  (AI stays disabled).
// ***************************************************************************

using System;
using System.IO;
using System.Text.Json;

namespace Shop
{
    public class TShopConfigLoader
    {
        private readonly TShopServerConfig FConfig;

        public TShopConfigLoader(TShopServerConfig aConfig)
        {
            if (aConfig == null)
                throw new EShopError("TShopConfigLoader: config is nil");
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
                vText = File.ReadAllText(aPath, System.Text.Encoding.UTF8);
            }
            catch (Exception E)
            {
                throw new EShopError(string.Format(
                    "Failed to read config file {0}: {1}", aPath, E.Message));
            }
            LoadFromJSONString(vText);
        }

        public void LoadFromJSONString(string aJSON)
        {
            if (string.IsNullOrWhiteSpace(aJSON))
                return;

            JsonDocument oJSON;
            try
            {
                oJSON = JsonDocument.Parse(aJSON);
            }
            catch (Exception E)
            {
                throw new EShopError(string.Format("Invalid JSON config: {0}",
                    E.Message));
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

                JsonElement vAI;
                if (vRoot.TryGetProperty("ai", out vAI) &&
                    vAI.ValueKind == JsonValueKind.Object)
                    ParseAI(vAI);
            }
        }

        private static string NodeStr(JsonElement aParent, string aName,
            string aDefault)
        {
            JsonElement vNode;
            if (aParent.TryGetProperty(aName, out vNode) &&
                vNode.ValueKind == JsonValueKind.String)
            {
                string vStr = vNode.GetString();
                if (!string.IsNullOrEmpty(vStr))
                    return vStr;
            }
            return aDefault;
        }

        private static int NodeInt(JsonElement aParent, string aName, int aDefault)
        {
            JsonElement vNode;
            if (!aParent.TryGetProperty(aName, out vNode))
                return aDefault;
            if (vNode.ValueKind == JsonValueKind.Number)
            {
                int vInt;
                if (vNode.TryGetInt32(out vInt))
                    return vInt;
            }
            else if (vNode.ValueKind == JsonValueKind.String)
            {
                int vInt;
                if (int.TryParse(vNode.GetString(), out vInt))
                    return vInt;
            }
            return aDefault;
        }

        private void ParseListen(JsonElement aNode)
        {
            FConfig.ListenAddress = NodeStr(aNode, "address", FConfig.ListenAddress);
            FConfig.ListenPort = NodeInt(aNode, "port", FConfig.ListenPort);
        }

        private void ParseAI(JsonElement aNode)
        {
            string vProvider = NodeStr(aNode, "provider",
                ShopTypes.sgcShopAIProviderToStr(FConfig.AIProvider));
            FConfig.AIProvider = ShopTypes.sgcShopAIProviderFromStr(vProvider);
            FConfig.AIApiKeyEnvVar = NodeStr(aNode, "apiKeyEnvVar",
                FConfig.AIApiKeyEnvVar);
            FConfig.AIModel = NodeStr(aNode, "model", FConfig.AIModel);
        }
    }
}
