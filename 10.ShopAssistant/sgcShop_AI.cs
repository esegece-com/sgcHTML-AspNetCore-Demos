// ***************************************************************************
//  sgcShopAssistant - AI storefront demo (TechNest) (managed port)
//  Port of delphi\Demos\60.HTML\10.ShopAssistant\sgcShop_AI.pas
//
//  Grounded (RAG) shop-assistant glue. TShopAIResponder always produces an
//  answer: when an AI provider and API key are configured (provider != sapNone
//  and the configured env var resolves to a non-empty key) it asks the LLM
//  over a plain HttpClient, grounded in a CONTEXT block built from the in-demo catalog and
//  store info; otherwise - and whenever the LLM call fails or comes back empty
//  - it falls back to a deterministic, template-built answer from the same
//  matched records, so the demo works fully out of the box with zero external
//  dependencies and never invents a product or a price.
// ***************************************************************************

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Shop
{
    public class TShopAIResponder
    {
        private const string CS_SHOP_SYSTEM_PROMPT =
            "You are TechNest's shop assistant. Answer only using the CONTEXT " +
            "below; if the answer isn't in the context, say you don't know and " +
            "offer to connect the visitor with a human. Be concise.";

        // A product match at or above this score includes a Name-level hit
        // (Name matches are weighted +3 per word in ScoreProduct), not just a
        // shared generic word from a description or spec line (+1). Below this,
        // a match is too weak to override an explicit store-info question.
        private const int CS_STRONG_PRODUCT_MATCH_SCORE = 3;

        private readonly TShopServerConfig FConfig;
        private readonly string FApiKey;

        public TShopAIResponder(TShopServerConfig aConfig)
        {
            if (aConfig == null)
                throw new EShopError("TShopAIResponder: config is nil");
            FConfig = aConfig;
            FApiKey = ResolveApiKey();
        }

        private string ResolveApiKey()
        {
            if (FConfig.AIProvider == TShopAIProvider.sapNone ||
                string.IsNullOrEmpty((FConfig.AIApiKeyEnvVar ?? "").Trim()))
                return "";
            return (Environment.GetEnvironmentVariable(FConfig.AIApiKeyEnvVar) ?? "")
                .Trim();
        }

        private static bool WantsStoreInfo(string aQuery)
        {
            string vQ = (aQuery ?? "").ToLowerInvariant();
            return vQ.Contains("hour") || vQ.Contains("open") || vQ.Contains("close") ||
                vQ.Contains("ship") || vQ.Contains("deliver") || vQ.Contains("return") ||
                vQ.Contains("refund") || vQ.Contains("contact") || vQ.Contains("email") ||
                vQ.Contains("about") || vQ.Contains("found") || vQ.Contains("policy") ||
                vQ.Contains("warranty") || vQ.Contains("store");
        }

        // Builds the CONTEXT block for aQuery from the catalog search + (when
        // relevant) store info, and returns the products used as sources.
        public string BuildContext(string aQuery, out TShopProduct[] aMatched)
        {
            aMatched = ShopCatalog.sgcShopSearchProducts(aQuery, 5);
            var oSB = new StringBuilder();

            if (aMatched.Length > 0)
            {
                oSB.Append("PRODUCTS:").Append('\n');
                for (int vI = 0; vI < aMatched.Length; vI++)
                    oSB.Append(string.Format("- {0} ({1}): {2}, {3} in stock. {4}",
                        aMatched[vI].Name, aMatched[vI].Category,
                        ShopTypes.sgcShopFormatPrice(aMatched[vI].Price),
                        aMatched[vI].Stock, aMatched[vI].ShortDescription))
                        .Append('\n');
            }

            if (WantsStoreInfo(aQuery) || aMatched.Length == 0)
            {
                TShopStoreInfo vInfo = ShopCatalog.sgcShopGetStoreInfo();
                if (oSB.Length > 0)
                    oSB.Append('\n');
                oSB.Append("STORE INFO:").Append('\n');
                oSB.Append(vInfo.Name + " - " + vInfo.Tagline).Append('\n');
                oSB.Append("Founded: " + vInfo.FoundedYear.ToString()).Append('\n');
                oSB.Append("Hours: " + vInfo.Hours).Append('\n');
                oSB.Append("Shipping: " + vInfo.ShippingPolicy).Append('\n');
                oSB.Append("Returns: " + vInfo.ReturnPolicy).Append('\n');
                oSB.Append("Contact: " + vInfo.ContactEmail).Append('\n');
            }

            return oSB.ToString();
        }

        private string LocalAnswer(string aQuery, TShopProduct[] aMatched)
        {
            bool vWantsStoreInfo = WantsStoreInfo(aQuery);
            bool vStrongProductMatch = (aMatched.Length > 0) &&
                (ShopCatalog.sgcShopScoreProduct(aQuery, aMatched[0]) >=
                    CS_STRONG_PRODUCT_MATCH_SCORE);

            // Same priority as BuildContext: a clear store-info question (hours,
            // shipping, returns, ...) must get a store-info answer, even when the
            // keyword search also turned up a weak, incidental product match.
            // Only an explicit, Name-level product match is allowed to win.
            if ((aMatched.Length > 0) && (!vWantsStoreInfo || vStrongProductMatch))
            {
                var oSB = new StringBuilder();
                for (int vI = 0; vI < aMatched.Length; vI++)
                {
                    if (vI > 0)
                        oSB.Append(' ');
                    if (aMatched[vI].Stock > 0)
                        oSB.Append(string.Format(
                            "The {0} ({1}) is {2} and we have {3} in stock. {4}",
                            aMatched[vI].Name, aMatched[vI].Category,
                            ShopTypes.sgcShopFormatPrice(aMatched[vI].Price),
                            aMatched[vI].Stock, aMatched[vI].ShortDescription));
                    else
                        oSB.Append(string.Format(
                            "The {0} ({1}) is {2} but is currently out of stock. {3}",
                            aMatched[vI].Name, aMatched[vI].Category,
                            ShopTypes.sgcShopFormatPrice(aMatched[vI].Price),
                            aMatched[vI].ShortDescription));
                }
                return oSB.ToString();
            }

            TShopStoreInfo vInfo = ShopCatalog.sgcShopGetStoreInfo();
            if (vWantsStoreInfo)
                return string.Format(
                    "{0} (est. {1}). Hours: {2} Shipping: {3} Returns: {4} " +
                    "Questions? Reach us at {5}.", vInfo.Tagline, vInfo.FoundedYear,
                    vInfo.Hours, vInfo.ShippingPolicy, vInfo.ReturnPolicy,
                    vInfo.ContactEmail);

            return "I do not have that in our catalog or store information, so " +
                "I do not want to guess. I can answer questions about TechNest " +
                "products, hours, shipping and returns. Would you like me to connect " +
                "you with a human at " + vInfo.ContactEmail + "?";
        }

        // One HttpClient for the whole app (HttpClient is meant to be reused).
        private static readonly HttpClient FHttp = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Bring your own LLM: a plain HttpClient call to the OpenAI chat
        // completions endpoint, or the Anthropic messages endpoint, grounded in
        // aContext. Returns "" on ANY failure (missing/invalid key, network error,
        // empty response) so the caller falls back to the local grounded answer.
        private string AIAnswer(string aQuery, string aContext)
        {
            try
            {
                string vSystemPrompt = CS_SHOP_SYSTEM_PROMPT + "\n\n" +
                    "CONTEXT:\n" + aContext;
                bool vAnthropic = FConfig.AIProvider == TShopAIProvider.sapAnthropic;
                string vModel = !string.IsNullOrEmpty(FConfig.AIModel) ? FConfig.AIModel
                    : (vAnthropic ? "claude-3-haiku-20240307" : "gpt-4o-mini");

                var oRequest = new HttpRequestMessage(HttpMethod.Post, vAnthropic
                    ? "https://api.anthropic.com/v1/messages"
                    : "https://api.openai.com/v1/chat/completions");
                string vBody;
                if (vAnthropic)
                {
                    oRequest.Headers.Add("x-api-key", FApiKey);
                    oRequest.Headers.Add("anthropic-version", "2023-06-01");
                    vBody = JsonSerializer.Serialize(new
                    {
                        model = vModel,
                        max_tokens = 400,
                        temperature = 0.3,
                        system = vSystemPrompt,
                        messages = new[] { new { role = "user", content = aQuery } }
                    });
                }
                else
                {
                    oRequest.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", FApiKey);
                    vBody = JsonSerializer.Serialize(new
                    {
                        model = vModel,
                        max_tokens = 400,
                        temperature = 0.3,
                        messages = new[]
                        {
                            new { role = "system", content = vSystemPrompt },
                            new { role = "user", content = aQuery }
                        }
                    });
                }
                oRequest.Content = new StringContent(vBody, Encoding.UTF8,
                    "application/json");

                using (oRequest)
                using (HttpResponseMessage oResponse = FHttp.Send(oRequest))
                {
                    if (!oResponse.IsSuccessStatusCode)
                        return "";
                    string vJSON = oResponse.Content.ReadAsStringAsync()
                        .GetAwaiter().GetResult();
                    using (JsonDocument oDoc = JsonDocument.Parse(vJSON))
                    {
                        JsonElement vRoot = oDoc.RootElement;
                        string vText = vAnthropic
                            ? vRoot.GetProperty("content")[0].GetProperty("text")
                                .GetString()
                            : vRoot.GetProperty("choices")[0].GetProperty("message")
                                .GetProperty("content").GetString();
                        return (vText ?? "").Trim();
                    }
                }
            }
            catch
            {
                // Any failure -> empty, so Reply falls back to LocalAnswer.
                return "";
            }
        }

        // Answers aQuery given a pre-built aContext (from BuildContext) and its
        // aMatched source products. Tries the configured LLM first (when
        // available); always falls back to a grounded local answer.
        public string Reply(string aQuery, string aContext, TShopProduct[] aMatched)
        {
            string vResult = "";
            if (FConfig.AIProvider != TShopAIProvider.sapNone && FApiKey != "")
                vResult = AIAnswer(aQuery, aContext);
            if (string.IsNullOrEmpty(vResult))
                vResult = LocalAnswer(aQuery, aMatched);
            return vResult;
        }
    }
}
