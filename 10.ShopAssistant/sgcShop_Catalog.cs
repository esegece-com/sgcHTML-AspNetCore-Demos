// ***************************************************************************
//  sgcShopAssistant - AI storefront demo (TechNest) (managed port)
//  Port of delphi\Demos\60.HTML\10.ShopAssistant\sgcShop_Catalog.pas
//
//  Seed data for the TechNest demo store: 16 fictional consumer-electronics
//  products across 4 categories, plus the store info the AI assistant grounds
//  its answers in. Names, models and prices are invented for this demo; no
//  real brand is referenced. sgcShopSearchProducts is the keyword-matching
//  retrieval step used by the RAG glue in sgcShop_AI.cs. A faithful 1:1 port
//  of the Delphi tokenizer / word-boundary matcher / weighting.
// ***************************************************************************

using System;
using System.Collections.Generic;

namespace Shop
{
    public static class ShopCatalog
    {
        private const string CS_CATEGORY_LAPTOPS = "Laptops & Tablets";
        private const string CS_CATEGORY_AUDIO = "Audio";
        private const string CS_CATEGORY_SMARTHOME = "Smart Home";
        private const string CS_CATEGORY_WEARABLES = "Wearables";

        // A small stopword list keeps single-word searches (e.g. 'the', 'is')
        // from matching every product in the catalog.
        private static readonly string[] CS_STOPWORDS = {
            "the", "a", "an", "is", "are", "do", "does", "you", "your", "have",
            "has", "what", "which", "for", "of", "and", "to", "me", "about",
            "tell", "i", "any", "some", "can", "will", "with", "that", "this",
            "in", "on", "at", "or", "if" };

        // Fixed display order for the 4 catalog categories.
        public static string[] sgcShopGetCategories()
        {
            return new[]
            {
                CS_CATEGORY_LAPTOPS, CS_CATEGORY_AUDIO,
                CS_CATEGORY_SMARTHOME, CS_CATEGORY_WEARABLES
            };
        }

        // Store info (name, tagline, hours, shipping/return policy, contact).
        public static TShopStoreInfo sgcShopGetStoreInfo()
        {
            return new TShopStoreInfo
            {
                Name = "TechNest",
                Tagline = "Smart gear for everyday life.",
                FoundedYear = 2016,
                Hours = "Monday to Friday 9:00 to 18:00, Saturday 10:00 to 16:00 " +
                    "(store time, UTC minus 5). Closed Sundays and public holidays.",
                ShippingPolicy = "Free standard shipping on orders over $50. " +
                    "Standard delivery takes 3 to 5 business days; express delivery " +
                    "takes 1 to 2 business days for a $12.99 flat fee.",
                ReturnPolicy = "Unopened items can be returned within 30 days " +
                    "for a full refund. Opened electronics can be returned within 14 " +
                    "days with proof of purchase, subject to a restocking check.",
                ContactEmail = "support@technest.example"
            };
        }

        private static TShopProduct P(int aId, string aName, string aCategory,
            decimal aPrice, int aStock, double aRating, int aReviewCount,
            string aIcon, string aShort, string aFull, params string[] aSpecs)
        {
            return new TShopProduct
            {
                Id = aId,
                Name = aName,
                Category = aCategory,
                Price = aPrice,
                Stock = aStock,
                Rating = aRating,
                ReviewCount = aReviewCount,
                Icon = aIcon,
                ShortDescription = aShort,
                FullDescription = aFull,
                Specs = aSpecs
            };
        }

        // The full 16-product catalog, in a fixed, stable Id order (1..16).
        public static TShopProduct[] sgcShopGetCatalog()
        {
            return new[]
            {
                // --- Laptops & Tablets -------------------------------------
                P(1, "Nimbus Air 14", CS_CATEGORY_LAPTOPS, 899.00m, 7, 4.6, 184,
                    "&#128187;",
                    "An ultralight 14 inch laptop built for all day portability.",
                    "The Nimbus Air 14 pairs a fanless aluminum shell with a bright " +
                    "1920x1200 display, so it stays quiet and cool through a full day of " +
                    "browsing, writing and video calls. A generous 512GB SSD and 16GB of " +
                    "RAM keep everyday multitasking smooth.",
                    "Display: 14\" 1920x1200 IPS", "Memory: 16GB LPDDR5",
                    "Storage: 512GB SSD", "Battery: up to 11 hours", "Weight: 1.2 kg"),

                P(2, "Nimbus Pro 16", CS_CATEGORY_LAPTOPS, 1499.00m, 3, 4.8, 97,
                    "&#128187;",
                    "A 16 inch performance laptop with discrete graphics for creative work.",
                    "The Nimbus Pro 16 steps up to a 16 inch QHD display, a discrete " +
                    "graphics chip and 32GB of RAM, giving photo and video editors " +
                    "headroom the Air series does not have. The 1TB SSD leaves plenty of " +
                    "room for large project files.",
                    "Display: 16\" 2560x1600 IPS", "Memory: 32GB LPDDR5",
                    "Storage: 1TB SSD", "Graphics: discrete 8GB GPU",
                    "Battery: up to 8 hours"),

                P(3, "Voyager Tab 11", CS_CATEGORY_LAPTOPS, 429.00m, 12, 4.3, 152,
                    "&#128241;",
                    "An 11 inch tablet with stylus support for notes and sketching.",
                    "The Voyager Tab 11 combines a crisp 11 inch display with " +
                    "pressure-sensitive stylus support (sold separately), making it a " +
                    "natural fit for note taking, light photo editing and reading. 128GB " +
                    "of storage covers most personal libraries.",
                    "Display: 11\" 2000x1200 IPS", "Storage: 128GB",
                    "Stylus support: yes (sold separately)", "Battery: up to 10 hours",
                    "Weight: 460 g"),

                P(4, "Voyager Tab Mini 8", CS_CATEGORY_LAPTOPS, 249.00m, 20, 4.1, 246,
                    "&#128241;",
                    "A pocketable 8 inch tablet for reading and streaming on the go.",
                    "The Voyager Tab Mini 8 trades screen size for portability, slipping " +
                    "easily into a bag or large pocket. It is a favorite for e-reading, " +
                    "video streaming and travel.",
                    "Display: 8\" 1280x800 IPS", "Storage: 64GB",
                    "Battery: up to 9 hours", "Weight: 320 g"),

                // --- Audio -------------------------------------------------
                P(5, "EchoBuds Pro", CS_CATEGORY_AUDIO, 149.00m, 25, 4.7, 312,
                    "&#127911;",
                    "Wireless earbuds with active noise cancellation and a 30 hour case.",
                    "EchoBuds Pro blocks out background noise for calls and commutes, " +
                    "while the compact charging case extends total playtime to roughly " +
                    "30 hours. Touch controls handle playback, calls and noise " +
                    "cancellation toggling.",
                    "Type: in-ear, active noise cancellation",
                    "Battery: 7h (buds) + 23h (case)", "Charging: USB-C, wireless case",
                    "Water resistance: IPX4"),

                P(6, "EchoBuds Lite", CS_CATEGORY_AUDIO, 59.00m, 40, 4.2, 340,
                    "&#127911;",
                    "Budget wireless earbuds with reliable sound and all day battery.",
                    "EchoBuds Lite keeps things simple: solid sound, a secure fit and a " +
                    "case that tops up the buds in about 15 minutes. A dependable " +
                    "everyday pick for calls, podcasts and music.",
                    "Type: in-ear", "Battery: 6h (buds) + 18h (case)", "Charging: USB-C",
                    "Water resistance: IPX4"),

                P(7, "Resonate S1", CS_CATEGORY_AUDIO, 89.00m, 18, 4.5, 128,
                    "&#128266;",
                    "A rugged, waterproof Bluetooth speaker for outdoor listening.",
                    "Resonate S1 is built to handle splashes, dust and the occasional " +
                    "drop, with a 12 hour battery that easily covers a day at the beach " +
                    "or a weekend camping trip. Pair two units together for stereo sound.",
                    "Water resistance: IPX7", "Battery: up to 12 hours",
                    "Connectivity: Bluetooth 5.3", "Stereo pairing: yes"),

                P(8, "Resonate Max", CS_CATEGORY_AUDIO, 199.00m, 9, 4.4, 89,
                    "&#128266;",
                    "A voice-assistant smart speaker for whole room sound at home.",
                    "Resonate Max fills a living room with rich, room-filling sound and " +
                    "includes a built-in voice assistant for music, timers and smart " +
                    "home control. Multiple units can be grouped for multi-room audio.",
                    "Built-in voice assistant: yes",
                    "Connectivity: WiFi + Bluetooth 5.3", "Multi-room grouping: yes",
                    "Power: mains-powered"),

                // --- Smart Home --------------------------------------------
                P(9, "HearthGlow Bulb 4-Pack", CS_CATEGORY_SMARTHOME, 39.00m, 50, 4.5,
                    275, "&#128161;",
                    "Color-changing smart LED bulbs, four to a pack, controlled from an app.",
                    "HearthGlow bulbs screw into any standard fixture and connect over " +
                    "WiFi, letting you dim, schedule and change color from the companion " +
                    "app or a voice assistant. This pack includes four bulbs, enough for " +
                    "a full room.",
                    "Bulbs per pack: 4", "Connectivity: WiFi",
                    "Color range: 16 million colors", "Lifespan: rated 25,000 hours"),

                P(10, "HearthCam Outdoor", CS_CATEGORY_SMARTHOME, 79.00m, 15, 4.0, 143,
                    "&#128247;",
                    "A weatherproof 1080p security camera with night vision.",
                    "HearthCam Outdoor watches over a porch, driveway or yard in 1080p, " +
                    "switching automatically to night vision after dark. Motion alerts " +
                    "arrive on your phone, and the housing is rated for rain, heat and " +
                    "cold.",
                    "Resolution: 1080p", "Night vision: yes",
                    "Weatherproof rating: IP65", "Motion alerts: push notification"),

                P(11, "HearthLock Smart Deadbolt", CS_CATEGORY_SMARTHOME, 129.00m, 6,
                    3.9, 67, "&#128274;",
                    "A keypad and app-controlled smart deadbolt for the front door.",
                    "HearthLock replaces a standard deadbolt with keypad entry, app " +
                    "unlock and temporary codes for guests, so you can let someone in " +
                    "without handing over a physical key. A battery backup keeps it " +
                    "working during a power outage.",
                    "Unlock methods: keypad, app, physical key", "Guest codes: up to 20",
                    "Battery life: up to 12 months", "Backup power: 9V battery terminal"),

                P(12, "HearthHub Mini", CS_CATEGORY_SMARTHOME, 69.00m, 22, 4.3, 118,
                    "&#128225;",
                    "A compact smart home hub that bridges WiFi and Zigbee devices.",
                    "HearthHub Mini sits quietly on a shelf and lets WiFi and Zigbee " +
                    "smart home devices, from bulbs to sensors, work together in one " +
                    "app and one set of automations. Setup takes about five minutes.",
                    "Protocols: WiFi, Zigbee", "Connected devices: up to 100",
                    "Power: USB-C, always-on", "App: TechNest Home"),

                // --- Wearables ---------------------------------------------
                P(13, "PulseFit Band 3", CS_CATEGORY_WEARABLES, 79.00m, 30, 4.2, 201,
                    "&#8986;",
                    "A slim fitness tracker for heart rate, sleep and step tracking.",
                    "PulseFit Band 3 tracks heart rate, sleep stages and daily steps in " +
                    "a lightweight band that lasts about a week per charge. It is a " +
                    "straightforward, affordable way to keep an eye on daily activity.",
                    "Sensors: heart rate, accelerometer", "Battery: up to 7 days",
                    "Water resistance: 5 ATM", "Display: monochrome touch"),

                P(14, "PulseFit Watch SE", CS_CATEGORY_WEARABLES, 199.00m, 11, 4.6, 176,
                    "&#8986;",
                    "A GPS smartwatch with heart rate tracking and two day battery life.",
                    "PulseFit Watch SE adds built-in GPS for accurate outdoor workout " +
                    "tracking, continuous heart rate monitoring and smartphone " +
                    "notifications, all in a battery that comfortably lasts two days " +
                    "between charges.",
                    "Display: color touchscreen", "GPS: built-in",
                    "Battery: up to 2 days", "Water resistance: 5 ATM"),

                P(15, "PulseFit Watch Pro", CS_CATEGORY_WEARABLES, 329.00m, 4, 4.9, 54,
                    "&#8986;",
                    "A premium smartwatch with an always-on display and ECG readings.",
                    "PulseFit Watch Pro is the flagship of the lineup, with an always-on " +
                    "display, on-demand ECG readings and up to five days of battery " +
                    "life, aimed at users who want deeper health insight without daily " +
                    "charging.",
                    "Display: always-on color touchscreen", "ECG: on-demand",
                    "Battery: up to 5 days", "Water resistance: 5 ATM"),

                P(16, "PulseFit Ring", CS_CATEGORY_WEARABLES, 299.00m, 2, 3.8, 22,
                    "&#128141;",
                    "A titanium smart ring that tracks sleep and recovery around the clock.",
                    "PulseFit Ring packs sleep, heart rate and recovery tracking into a " +
                    "lightweight titanium ring you barely notice you are wearing, with " +
                    "roughly a week of battery life between charges.",
                    "Material: titanium",
                    "Sensors: heart rate, temperature, accelerometer",
                    "Battery: up to 7 days", "Sizes available: 6 to 13")
            };
        }

        // All products in aCategory ('' or unrecognized -> the full catalog).
        public static TShopProduct[] sgcShopProductsByCategory(string aCategory)
        {
            TShopProduct[] vCatalog = sgcShopGetCatalog();
            if (string.IsNullOrEmpty((aCategory ?? "").Trim()))
                return vCatalog;

            var vList = new List<TShopProduct>();
            for (int vI = 0; vI < vCatalog.Length; vI++)
                if (string.Equals(vCatalog[vI].Category, aCategory,
                    StringComparison.OrdinalIgnoreCase))
                    vList.Add(vCatalog[vI]);
            // unrecognized category -> fall back to the full catalog
            if (vList.Count == 0)
                return vCatalog;
            return vList.ToArray();
        }

        // Looks up a single product by Id. Returns false when not found.
        public static bool sgcShopFindProductById(int aId, out TShopProduct aProduct)
        {
            aProduct = null;
            TShopProduct[] vCatalog = sgcShopGetCatalog();
            for (int vI = 0; vI < vCatalog.Length; vI++)
                if (vCatalog[vI].Id == aId)
                {
                    aProduct = vCatalog[vI];
                    return true;
                }
            return false;
        }

        private static bool IsStopword(string aWord)
        {
            for (int vI = 0; vI < CS_STOPWORDS.Length; vI++)
                if (CS_STOPWORDS[vI] == aWord)
                    return true;
            return false;
        }

        private static string[] TokenizeQuery(string aQuery)
        {
            var vList = new List<string>();
            var vWord = new System.Text.StringBuilder();
            string vSrc = aQuery ?? "";
            for (int vI = 0; vI < vSrc.Length; vI++)
            {
                char vCh = vSrc[vI];
                if ((vCh >= 'a' && vCh <= 'z') || (vCh >= 'A' && vCh <= 'Z') ||
                    (vCh >= '0' && vCh <= '9'))
                {
                    vWord.Append(char.ToLowerInvariant(vCh));
                }
                else
                {
                    string vW = vWord.ToString();
                    if (vW.Length >= 2 && !IsStopword(vW))
                        vList.Add(vW);
                    vWord.Clear();
                }
            }
            string vLast = vWord.ToString();
            if (vLast.Length >= 2 && !IsStopword(vLast))
                vList.Add(vLast);
            return vList.ToArray();
        }

        // True when aWord occurs in aHaystack at a word start (position 0, or
        // immediately preceded by a non letter/digit). A plain substring test
        // would let a short word like 'any' match inside 'many'; this keeps the
        // deliberately simple prefix-friendly matching ('laptop' still matches
        // 'laptops') while rejecting mid-word coincidences.
        private static bool ContainsWordLike(string aHaystack, string aWord)
        {
            if (string.IsNullOrEmpty(aWord) || string.IsNullOrEmpty(aHaystack))
                return false;
            int vPos = aHaystack.IndexOf(aWord, 0, StringComparison.Ordinal);
            while (vPos >= 0)
            {
                if (vPos == 0)
                    return true;
                char vPrev = aHaystack[vPos - 1];
                bool vPrevIsWord = (vPrev >= 'a' && vPrev <= 'z') ||
                    (vPrev >= '0' && vPrev <= '9');
                if (!vPrevIsWord)
                    return true;
                vPos = aHaystack.IndexOf(aWord, vPos + 1, StringComparison.Ordinal);
            }
            return false;
        }

        private static int ScoreProductWords(TShopProduct aProduct, string[] aWords)
        {
            int vResult = 0;
            string vName = (aProduct.Name ?? "").ToLowerInvariant();
            string vCategory = (aProduct.Category ?? "").ToLowerInvariant();
            string vShort = (aProduct.ShortDescription ?? "").ToLowerInvariant();
            string vFull = (aProduct.FullDescription ?? "").ToLowerInvariant();
            for (int vI = 0; vI < aWords.Length; vI++)
            {
                if (ContainsWordLike(vName, aWords[vI]))
                    vResult += 3;
                if (ContainsWordLike(vCategory, aWords[vI]))
                    vResult += 2;
                if (ContainsWordLike(vShort, aWords[vI]))
                    vResult += 1;
                if (ContainsWordLike(vFull, aWords[vI]))
                    vResult += 1;
                for (int vJ = 0; vJ < aProduct.Specs.Length; vJ++)
                    if (ContainsWordLike(aProduct.Specs[vJ].ToLowerInvariant(),
                        aWords[vI]))
                        vResult += 1;
            }
            return vResult;
        }

        // Relevance score of aQuery against a single aProduct, using the same
        // tokenizing/weighting rules as sgcShopSearchProducts.
        public static int sgcShopScoreProduct(string aQuery, TShopProduct aProduct)
        {
            return ScoreProductWords(aProduct, TokenizeQuery(aQuery));
        }

        // Case-insensitive keyword search over Name/Category/descriptions/Specs,
        // returning up to aMaxResults products ranked by relevance. This is the
        // RAG retrieval step: it never invents a product, only returns catalog
        // matches.
        public static TShopProduct[] sgcShopSearchProducts(string aQuery,
            int aMaxResults = 5)
        {
            string[] vWords = TokenizeQuery(aQuery);
            if (vWords.Length == 0)
                return Array.Empty<TShopProduct>();
            if (aMaxResults <= 0)
                aMaxResults = 5;

            TShopProduct[] vCatalog = sgcShopGetCatalog();
            int[] vScores = new int[vCatalog.Length];
            bool[] vUsed = new bool[vCatalog.Length];
            for (int vI = 0; vI < vCatalog.Length; vI++)
                vScores[vI] = ScoreProductWords(vCatalog[vI], vWords);

            var vOut = new List<TShopProduct>();
            while (vOut.Count < aMaxResults)
            {
                int vBestIdx = -1;
                int vBestScore = 0;
                for (int vI = 0; vI < vCatalog.Length; vI++)
                    if (!vUsed[vI] && vScores[vI] > vBestScore)
                    {
                        vBestScore = vScores[vI];
                        vBestIdx = vI;
                    }
                if (vBestIdx < 0)
                    break;
                vUsed[vBestIdx] = true;
                vOut.Add(vCatalog[vBestIdx]);
            }
            return vOut.ToArray();
        }
    }
}
