// ***************************************************************************
//  sgcShopAssistant - AI storefront demo (TechNest) (managed port)
//  Port of delphi\Demos\60.HTML\10.ShopAssistant\sgcShop_Types.pas
//
//  Core value types + the in-memory anonymous-visitor chat session store.
//  Delphi records become mutable C# classes; the Windows BCryptGenRandom token
//  source becomes the cross-platform RandomNumberGenerator (same 32-byte hex
//  token), matching the sibling Helpdesk port.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Shop
{
    public class EShopError : Exception
    {
        public EShopError(string message) : base(message) { }
    }

    // Default listen port for the standalone server. 5700-5704 are used by
    // sibling 60.HTML demos (01-04 use 5700-5703; 09.Helpdesk uses 5704).
    public static class ShopConst
    {
        public const int CS_SHOP_DEFAULT_PORT = 5705;

        // A session's chat history is capped at this many turns (user +
        // assistant messages combined) so an unattended demo cannot grow
        // memory unbounded.
        public const int CS_SHOP_MAX_SESSION_MESSAGES = 40;
    }

    // One product in the in-demo catalog (see sgcShop_Catalog.cs for the seed
    // data). Specs is a short list of 'Key: Value' strings rendered as-is.
    public class TShopProduct
    {
        public int Id;
        public string Name = "";
        public string Category = "";
        public decimal Price;
        public int Stock;
        public string Icon = "";           // trusted HTML (numeric entity), e.g. "&#128187;"
        public string ShortDescription = "";
        public string FullDescription = "";
        public string[] Specs = Array.Empty<string>();
        // Average customer rating (0.0-5.0) and the review count backing it,
        // shown via TsgcHTMLComponent_Rating on the storefront pages.
        public double Rating;
        public int ReviewCount;
    }

    public class TShopStoreInfo
    {
        public string Name = "";
        public string Tagline = "";
        public int FoundedYear;
        public string Hours = "";
        public string ShippingPolicy = "";
        public string ReturnPolicy = "";
        public string ContactEmail = "";
    }

    public enum TShopChatRole { scrUser, scrAssistant }

    // One turn of a visitor's chat history, replayed into the AIChat widget on
    // every page render so the conversation survives a full page reload.
    public class TShopChatMessage
    {
        public TShopChatRole Role;
        public string Text = "";
        public string SourcesHTML = "";    // "" unless Role = scrAssistant and sources matched
    }

    public class TShopSession
    {
        public string Token = "";
        public DateTime CreatedAt;
        public DateTime ExpiresAt;
        public List<TShopChatMessage> Messages = new List<TShopChatMessage>();
    }

    // Thread-safe in-memory store for anonymous visitor chat sessions, keyed by
    // a random cookie token. Scoped-down sibling of the Helpdesk demo's
    // THelpdeskSessionStore: no user identity here, just a rolling per-visitor
    // chat transcript. TryGet returns a defensive snapshot (matching the Delphi
    // record value-semantics) so a page can render a session's Messages while
    // another thread appends to the stored one.
    public class TShopSessionStore
    {
        private readonly object FLock = new object();
        private readonly Dictionary<string, TShopSession> FSessions;
        private readonly int FTTLMinutes;

        // aTTLMinutes <= 0 defaults to 2 hours (120 minutes).
        public TShopSessionStore(int aTTLMinutes = 120)
        {
            FTTLMinutes = aTTLMinutes <= 0 ? 120 : aTTLMinutes;
            FSessions = new Dictionary<string, TShopSession>();
        }

        private static byte[] RandomBytes(int aLen)
        {
            if (aLen <= 0)
                return Array.Empty<byte>();
            byte[] vResult = new byte[aLen];
            RandomNumberGenerator.Fill(vResult);
            return vResult;
        }

        private static string BytesToHex(byte[] aBytes)
        {
            const string CS_HEX = "0123456789abcdef";
            var vResult = new StringBuilder(aBytes.Length * 2);
            for (int vI = 0; vI < aBytes.Length; vI++)
            {
                vResult.Append(CS_HEX[(aBytes[vI] >> 4) & 0x0F]);
                vResult.Append(CS_HEX[aBytes[vI] & 0x0F]);
            }
            return vResult.ToString();
        }

        private static string GenerateToken()
        {
            return BytesToHex(RandomBytes(32));
        }

        // Snapshot copy (value-semantics like the Delphi record return).
        private static TShopSession CloneSession(TShopSession aSource)
        {
            var oResult = new TShopSession
            {
                Token = aSource.Token,
                CreatedAt = aSource.CreatedAt,
                ExpiresAt = aSource.ExpiresAt,
                Messages = new List<TShopChatMessage>(aSource.Messages.Count)
            };
            for (int vI = 0; vI < aSource.Messages.Count; vI++)
            {
                TShopChatMessage vSrc = aSource.Messages[vI];
                oResult.Messages.Add(new TShopChatMessage
                {
                    Role = vSrc.Role,
                    Text = vSrc.Text,
                    SourcesHTML = vSrc.SourcesHTML
                });
            }
            return oResult;
        }

        // Creates a fresh, empty session, returning the random token.
        public string CreateSession()
        {
            DateTime vNow = DateTime.Now;
            var oSession = new TShopSession
            {
                Token = GenerateToken(),
                CreatedAt = vNow,
                ExpiresAt = vNow.AddMinutes(FTTLMinutes),
                Messages = new List<TShopChatMessage>()
            };

            lock (FLock)
            {
                FSessions[oSession.Token] = oSession;
            }

            return oSession.Token;
        }

        // Looks up a non-expired session by token; sliding-expiry refreshes it.
        // Returns false for a missing/expired token. aSession is a snapshot.
        public bool TryGet(string aToken, out TShopSession aSession)
        {
            aSession = null;
            if (string.IsNullOrEmpty(aToken))
                return false;
            lock (FLock)
            {
                TShopSession oSession;
                if (!FSessions.TryGetValue(aToken, out oSession))
                    return false;
                if (DateTime.Now > oSession.ExpiresAt)
                {
                    FSessions.Remove(aToken);
                    return false;
                }
                // Sliding expiry: refresh the window on the stored session.
                oSession.ExpiresAt = DateTime.Now.AddMinutes(FTTLMinutes);
                aSession = CloneSession(oSession);
                return true;
            }
        }

        // Appends a chat turn to an existing session (no-op when aToken is
        // absent). Oldest turns are dropped past CS_SHOP_MAX_SESSION_MESSAGES.
        public void AppendMessage(string aToken, TShopChatRole aRole,
            string aText, string aSourcesHTML)
        {
            if (string.IsNullOrEmpty(aToken))
                return;
            lock (FLock)
            {
                TShopSession oSession;
                if (!FSessions.TryGetValue(aToken, out oSession))
                    return;

                oSession.Messages.Add(new TShopChatMessage
                {
                    Role = aRole,
                    Text = aText,
                    SourcesHTML = aSourcesHTML
                });

                int vMax = ShopConst.CS_SHOP_MAX_SESSION_MESSAGES;
                if (oSession.Messages.Count > vMax)
                    oSession.Messages.RemoveRange(0, oSession.Messages.Count - vMax);

                oSession.ExpiresAt = DateTime.Now.AddMinutes(FTTLMinutes);
            }
        }

        public void PurgeExpired()
        {
            DateTime vNow = DateTime.Now;
            lock (FLock)
            {
                var vExpired = new List<string>();
                foreach (KeyValuePair<string, TShopSession> oPair in FSessions)
                    if (vNow > oPair.Value.ExpiresAt)
                        vExpired.Add(oPair.Key);
                for (int vI = 0; vI < vExpired.Count; vI++)
                    FSessions.Remove(vExpired[vI]);
            }
        }

        public int ActiveCount()
        {
            lock (FLock)
            {
                return FSessions.Count;
            }
        }
    }

    public enum TShopAIProvider { sapNone, sapOpenAI, sapAnthropic }

    // Server configuration. Defaults are applied in the constructor so the
    // server runs out-of-the-box (AI disabled, local grounded responder only)
    // even when the JSON config file is absent.
    public class TShopServerConfig
    {
        public string ListenAddress;
        public int ListenPort;
        public TShopAIProvider AIProvider;
        public string AIApiKeyEnvVar;
        public string AIModel;

        public TShopServerConfig()
        {
            ListenAddress = "0.0.0.0";
            ListenPort = ShopConst.CS_SHOP_DEFAULT_PORT;
            AIProvider = TShopAIProvider.sapNone;
            AIApiKeyEnvVar = "TECHNEST_AI_API_KEY";
            AIModel = "";
        }
    }

    public static class ShopTypes
    {
        public static string sgcShopAIProviderToStr(TShopAIProvider aProvider)
        {
            switch (aProvider)
            {
                case TShopAIProvider.sapOpenAI: return "openai";
                case TShopAIProvider.sapAnthropic: return "anthropic";
                default: return "none";
            }
        }

        public static TShopAIProvider sgcShopAIProviderFromStr(string aValue)
        {
            if (string.Equals(aValue, "openai", StringComparison.OrdinalIgnoreCase))
                return TShopAIProvider.sapOpenAI;
            if (string.Equals(aValue, "anthropic", StringComparison.OrdinalIgnoreCase))
                return TShopAIProvider.sapAnthropic;
            return TShopAIProvider.sapNone;
        }

        // Locale-independent '$1234.56' formatting. Using a '.' decimal
        // separator explicitly (never the thread's culture) so a machine whose
        // regional settings use ',' still renders '$899.00', not '$899,00'.
        public static string sgcShopFormatPrice(decimal aPrice)
        {
            bool vNeg = aPrice < 0;
            long vCents = (long)Math.Round(Math.Abs(aPrice) * 100m,
                MidpointRounding.ToEven);
            long vWhole = vCents / 100;
            long vFrac = vCents % 100;
            string vFracStr = vFrac.ToString(CultureInfo.InvariantCulture);
            if (vFracStr.Length < 2)
                vFracStr = "0" + vFracStr;
            string vResult = "$" + vWhole.ToString(CultureInfo.InvariantCulture) +
                "." + vFracStr;
            if (vNeg)
                vResult = "-" + vResult;
            return vResult;
        }
    }
}
