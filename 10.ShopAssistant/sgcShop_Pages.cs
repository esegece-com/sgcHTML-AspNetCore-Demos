// ***************************************************************************
//  sgcShopAssistant - AI storefront demo (TechNest) (managed port)
//  Port of delphi\Demos\60.HTML\10.ShopAssistant\sgcShop_Pages.pas
//
//  View layer for the ShopAssistant demo: a single light-themed Bootstrap
//  storefront, built from real TsgcHTMLComponent_* widgets (NavBar, Carousel,
//  Badge, Rating, Breadcrumb, Tabs, Accordion, ButtonGroup, Toast,
//  AutoComplete) wherever a matching component exists, plus raw
//  TsgcHTMLContainer/NodeList trees for structural glue. Every page renders
//  inside a shared shell (a top navbar + footer + floating AI chat widget)
//  built by BuildPageShell. AddRaw() is used for values already sgcHTML-escaped
//  or trusted (component .HTML output, numeric HTML entities). A faithful 1:1
//  port of the Delphi node-layer view.
// ***************************************************************************

using System;
using System.Text;
// sgc
using esegece.sgcWebSockets;
using static esegece.sgcWebSockets.sgcHTMLHelpers;

namespace Shop
{
    public class TShopPages
    {
        // Base CSS for the whole storefront: an indigo/violet brand accent
        // layered on top of plain Bootstrap, plus the floating chat bubble/panel.
        private const string CS_SHOP_BASE_CSS =
            ":root{--shop-primary:#4f46e5;--shop-primary-dark:#4338ca;}" +
            "body{background:#f7f7fb;}" +
            ".shop-navbar{background:var(--shop-primary);}" +
            ".btn-primary{background-color:var(--shop-primary);border-color:var(--shop-primary);}" +
            ".btn-primary:hover,.btn-primary:focus{background-color:var(--shop-primary-dark);border-color:var(--shop-primary-dark);}" +
            ".btn-outline-primary{color:var(--shop-primary);border-color:var(--shop-primary);}" +
            ".btn-outline-primary:hover{background-color:var(--shop-primary);border-color:var(--shop-primary);color:#fff;}" +
            ".text-primary,.shop-price{color:var(--shop-primary) !important;}" +
            ".shop-product-icon{font-size:2.5rem;}" +
            ".shop-product-icon-lg{font-size:5rem;}" +
            ".shop-product-card{transition:transform .15s ease,box-shadow .15s ease;}" +
            ".shop-product-card:hover{transform:translateY(-2px);box-shadow:0 .5rem 1rem rgba(0,0,0,.1) !important;}" +
            ".shop-chat-bubble{position:fixed;bottom:20px;right:20px;width:56px;height:56px;font-size:1.5rem;z-index:1050;box-shadow:0 4px 14px rgba(0,0,0,.25);border:none;}" +
            ".shop-chat-panel{position:fixed;bottom:88px;right:20px;z-index:1050;width:360px;max-width:92vw;display:flex;flex-direction:column;box-shadow:0 8px 30px rgba(0,0,0,.2);border-radius:12px;overflow:hidden;}" +
            ".shop-chat-panel .card{border-radius:0;border-left:1px solid var(--bs-border-color,#dee2e6);border-right:1px solid var(--bs-border-color,#dee2e6);}" +
            ".shop-chat-panel .card-header{border-radius:0;}";

        // Three short inline SVG hero slides (no real product photography exists
        // for this fictional demo store). Gradient background + bold promo text,
        // turned into data URIs at render time by SVGDataURI.
        private const string CS_SHOP_HERO_SVG_1 =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"420\">" +
            "<defs><linearGradient id=\"g1\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">" +
            "<stop offset=\"0%\" stop-color=\"#4f46e5\"/>" +
            "<stop offset=\"100%\" stop-color=\"#7c3aed\"/></linearGradient></defs>" +
            "<rect width=\"1200\" height=\"420\" fill=\"url(#g1)\"/>" +
            "<text x=\"70\" y=\"200\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-size=\"56\" font-weight=\"700\" fill=\"#ffffff\">New Arrivals</text>" +
            "<text x=\"70\" y=\"255\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-size=\"24\" fill=\"#e0e7ff\">Laptops, audio, smart home and " +
            "wearables, freshly stocked.</text></svg>";

        private const string CS_SHOP_HERO_SVG_2 =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"420\">" +
            "<defs><linearGradient id=\"g2\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">" +
            "<stop offset=\"0%\" stop-color=\"#0ea5e9\"/>" +
            "<stop offset=\"100%\" stop-color=\"#4f46e5\"/></linearGradient></defs>" +
            "<rect width=\"1200\" height=\"420\" fill=\"url(#g2)\"/>" +
            "<text x=\"70\" y=\"200\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-size=\"56\" font-weight=\"700\" fill=\"#ffffff\">Free Shipping</text>" +
            "<text x=\"70\" y=\"255\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-size=\"24\" fill=\"#e0f2fe\">On every order over $50, delivered " +
            "in 3 to 5 business days.</text></svg>";

        private const string CS_SHOP_HERO_SVG_3 =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"420\">" +
            "<defs><linearGradient id=\"g3\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\">" +
            "<stop offset=\"0%\" stop-color=\"#7c3aed\"/>" +
            "<stop offset=\"100%\" stop-color=\"#db2777\"/></linearGradient></defs>" +
            "<rect width=\"1200\" height=\"420\" fill=\"url(#g3)\"/>" +
            "<text x=\"70\" y=\"200\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-size=\"56\" font-weight=\"700\" fill=\"#ffffff\">TechNest Season " +
            "Sale</text><text x=\"70\" y=\"255\" font-family=\"Arial,Helvetica," +
            "sans-serif\" font-size=\"24\" fill=\"#fce7f3\">Great deals across every " +
            "category, all season long.</text></svg>";

        // ----- price / rating / svg helpers ----- //

        private static string FmtPrice(decimal aPrice)
        {
            return ShopTypes.sgcShopFormatPrice(aPrice);
        }

        // Locale-independent one-decimal formatting for a 0.0-5.0 rating: never
        // let the thread's FormatSettings turn '4.6' into '4,6'.
        private static string FmtRating(double aRating)
        {
            int vScaled = (int)Math.Round(aRating * 10, MidpointRounding.AwayFromZero);
            if (vScaled < 0)
                vScaled = 0;
            return (vScaled / 10).ToString() + "." + (vScaled % 10).ToString();
        }

        // Turns a short inline SVG string into a data URI, so it can be used as
        // an <img>/carousel ImageSrc with no server round-trip.
        private static string SVGDataURI(string aSVGXML)
        {
            string vBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(aSVGXML));
            vBase64 = vBase64.Replace("\r", "").Replace("\n", "");
            return "data:image/svg+xml;base64," + vBase64;
        }

        // Stock as a coloured badge (In Stock / Low Stock / Out of Stock),
        // matching the 5-unit threshold used across the store.
        private static string StockBadge(int aStock)
        {
            if (aStock <= 0)
                return TsgcHTMLComponent_Badge.Build("Out of Stock",
                    TsgcHTMLBadgeStyle.bgDanger, true);
            if (aStock <= 5)
                return TsgcHTMLComponent_Badge.Build("Low Stock",
                    TsgcHTMLBadgeStyle.bgWarning, true);
            return TsgcHTMLComponent_Badge.Build("In Stock",
                TsgcHTMLBadgeStyle.bgSuccess, true);
        }

        // A 'Best Seller' promo badge for the store's top-rated products. ""
        // when aRating does not clear the bar (no badge rendered).
        private static string BestSellerBadge(double aRating)
        {
            if (aRating >= 4.6)
                return TsgcHTMLComponent_Badge.Build("Best Seller",
                    TsgcHTMLBadgeStyle.bgWarning, true);
            return "";
        }

        // Star rating (rounded to the nearest whole star, TsgcHTMLComponent_Rating
        // only takes an Integer Value) + the precise "4.6 (184 reviews)" text.
        private static string BuildRatingHTML(TShopProduct aProduct)
        {
            int vStars = (int)Math.Round(aProduct.Rating, MidpointRounding.AwayFromZero);
            if (vStars < 0)
                vStars = 0;
            else if (vStars > 5)
                vStars = 5;

            var oRoot = new TsgcHTMLNodeList();
            var oRating = new TsgcHTMLComponent_Rating();
            oRating.Value = vStars;
            oRating.MaxValue = 5;
            oRating.Size = "1rem";
            oRoot.AddRaw(oRating.HTML);

            var oValueSpan = new TsgcHTMLContainer("span");
            oValueSpan.CSSClass = "text-muted small ms-1";
            oValueSpan.AddText(FmtRating(aProduct.Rating) + " (" +
                aProduct.ReviewCount.ToString() + " reviews)");
            oRoot.Add(oValueSpan);

            return oRoot.HTML;
        }

        // ----- shell pieces ----- //

        private string WrapTemplate(string aTitle, string aBody)
        {
            var oTpl = new TsgcHTMLTemplate_Bootstrap();
            oTpl.Title = aTitle;
            oTpl.HtmlLang = "en";
            oTpl.Viewport = "width=device-width, initial-scale=1";
            oTpl.HeadNodes.AddRaw("<link rel=\"icon\" type=\"image/svg+xml\" " +
                "href=\"/favicon.svg\">");
            oTpl.CustomCSS = CS_SHOP_BASE_CSS;
            oTpl.BodyContent = aBody;
            return oTpl.GetHTML();
        }

        private string BuildNavbar(string aActiveMenu)
        {
            var oRoot = new TsgcHTMLNodeList();
            var oNavBar = new TsgcHTMLComponent_NavBar();

            oNavBar.Brand = "TechNest";
            oNavBar.BrandHref = "/";
            oNavBar.Theme = TsgcHTMLNavBarTheme.ntDark;
            oNavBar.Expand = TsgcHTMLNavBarExpand.neMedium;
            oNavBar.Fluid = false;
            oNavBar.CSSClass = "shop-navbar shadow-sm";
            AddNav(oNavBar, "/", "Home", "home", aActiveMenu);
            AddNav(oNavBar, "/catalog", "Catalog", "catalog", aActiveMenu);
            AddNav(oNavBar, "/about", "About", "about", aActiveMenu);
            oRoot.AddRaw(oNavBar.HTML);

            // Second-row search strip with a static datalist (the whole catalog
            // is small enough to ship at render time; no server route needed).
            TShopProduct[] vCatalog = ShopCatalog.sgcShopGetCatalog();
            var oSearch = new TsgcHTMLComponent_AutoComplete();
            oSearch.ElementName = "q";
            oSearch.AutoCompleteID = "shopSearch";
            oSearch.Placeholder = "Search products...";
            oSearch.Mode = TsgcHTMLAutoCompleteMode.acDatalist;
            for (int vI = 0; vI < vCatalog.Length; vI++)
                oSearch.Items.Add(vCatalog[vI].Name);

            var oSearchBar = new TsgcHTMLContainer("div");
            oSearchBar.CSSClass = "shop-navbar border-top border-light " +
                "border-opacity-25 py-2 mb-4";
            var oSearchInner = new TsgcHTMLContainer("div");
            oSearchInner.CSSClass = "container";
            var oSearchCol = new TsgcHTMLContainer("div");
            oSearchCol.CSSClass = "mx-auto";
            oSearchCol.Style = "max-width:420px;";
            oSearchCol.AddRaw(oSearch.HTML);
            oSearchInner.Add(oSearchCol);
            oSearchBar.Add(oSearchInner);
            oRoot.Add(oSearchBar);

            // Exact-match suggestion -> product page, entirely client side.
            var vProductMap = new StringBuilder("{");
            for (int vI = 0; vI < vCatalog.Length; vI++)
            {
                if (vI > 0)
                    vProductMap.Append(',');
                vProductMap.Append('"').Append(sgcJSEncode(vCatalog[vI].Name))
                    .Append("\":").Append(vCatalog[vI].Id.ToString());
            }
            vProductMap.Append('}');
            oRoot.AddScript("(function(){var m=" + vProductMap.ToString() +
                ";var i=document.getElementById('shopSearch');if(!i)return;" +
                "i.addEventListener('change',function(){var id=m[i.value];" +
                "if(id){window.location.href='/product/'+id;}});})();", false);

            return oRoot.HTML;
        }

        private static void AddNav(TsgcHTMLComponent_NavBar aNavBar, string aHref,
            string aText, string aMenu, string aActiveMenu)
        {
            TsgcHTMLNavItem oItem = aNavBar.Items.Add();
            oItem.Text = aText;
            oItem.Href = aHref;
            oItem.Active = string.Equals(aActiveMenu, aMenu,
                StringComparison.OrdinalIgnoreCase);
        }

        private string BuildFooter()
        {
            var oRoot = new TsgcHTMLNodeList();

            var oFooter = new TsgcHTMLContainer("footer");
            oFooter.CSSClass = "border-top mt-4 py-4 text-center text-muted small";

            var oP = new TsgcHTMLContainer("p");
            oP.CSSClass = "mb-1";
            oP.AddText("Built with ");
            var oLink = new TsgcHTMLLink("https://www.esegece.com/products/sgchtml/",
                "sgcHTML");
            oLink.Target = "_blank";
            oLink.Rel = "noopener";
            oLink.CSSClass = "fw-semibold text-decoration-none";
            oP.Add(oLink);
            oP.AddText(" for Delphi and C++Builder.");
            oFooter.Add(oP);

            // The (c) symbol is emitted as the &copy; entity so the source stays
            // ASCII and the UTF-8 response is never given a raw high byte.
            var oCopy = new TsgcHTMLContainer("p");
            oCopy.CSSClass = "mb-0";
            oCopy.AddRaw("&copy; 2026 ");
            var oEseLink = new TsgcHTMLLink("https://www.esegece.com", "eSeGeCe");
            oEseLink.Target = "_blank";
            oEseLink.Rel = "noopener";
            oEseLink.CSSClass = "text-muted text-decoration-none";
            oCopy.Add(oEseLink);
            oFooter.Add(oCopy);

            oRoot.Add(oFooter);
            return oRoot.HTML;
        }

        private string BuildChatWidgetHTML(TShopSession aSession, string aReturnTo,
            bool aOpenChat)
        {
            var oAIChat = new TsgcHTMLComponent_AIChat();
            oAIChat.AIChatID = "shopChat";
            oAIChat.AIName = "Nova";
            oAIChat.WelcomeMessage = "Hi, I'm Nova! Ask me about TechNest " +
                "products, pricing, stock, shipping or returns.";
            oAIChat.RAGEnabled = true;
            oAIChat.ShowModelSelector = false;
            oAIChat.StreamingEnabled = false;
            oAIChat.ShowSourceReferences = true;
            oAIChat.RAGLabel = "Sources";
            oAIChat.RAGDisplayMode = TsgcRAGDisplayMode.rdCollapsible;
            // The component's own input form targets a JS/WS send handler this
            // demo does not wire up; a plain <form method="POST"> is rendered
            // below instead, matching the Helpdesk demo's synchronous flow.
            oAIChat.ShowInput = false;
            oAIChat.CSSHeight = "320px";

            for (int vI = 0; vI < aSession.Messages.Count; vI++)
                if (aSession.Messages[vI].Role == TShopChatRole.scrUser)
                    oAIChat.AddUserMessage(aSession.Messages[vI].Text);
                else
                    oAIChat.AddAIMessageWithSources(aSession.Messages[vI].Text,
                        aSession.Messages[vI].SourcesHTML);

            string vPanelDisplay;
            string vBubbleDisplay;
            if (aOpenChat)
            {
                vPanelDisplay = "flex";
                vBubbleDisplay = "none";
            }
            else
            {
                vPanelDisplay = "none";
                vBubbleDisplay = "flex";
            }

            var oRoot = new TsgcHTMLNodeList();

            var oOuter = new TsgcHTMLContainer("div");
            oOuter.ID = "shopChatPanel";
            // Deliberately NOT Bootstrap's 'd-flex' utility class: it sets
            // 'display:flex !important', which would permanently beat the inline
            // display toggle below. '.shop-chat-panel' supplies its own plain
            // (non-!important) 'display:flex' instead.
            oOuter.CSSClass = "shop-chat-panel";
            oOuter.Style = "display:" + vPanelDisplay + ";";

            var oHeader = new TsgcHTMLContainer("div");
            oHeader.CSSClass = "card-header bg-primary text-white d-flex " +
                "justify-content-between align-items-center";
            var oStrong = new TsgcHTMLContainer("strong");
            oStrong.AddText("Nova");
            oStrong.AddRaw(" <span class=\"fw-normal small\">&middot; " +
                "TechNest Assistant</span>");
            oHeader.Add(oStrong);
            var oClose = new TsgcHTMLContainer("button");
            oClose.Attributes = "type=\"button\" onclick=\"shopToggleChat()\" " +
                "aria-label=\"Close chat\"";
            oClose.CSSClass = "btn-close btn-close-white";
            oHeader.Add(oClose);
            oOuter.Add(oHeader);

            // The widget is added (not AddRaw'd), so the node tree renders it.
            oOuter.Add(oAIChat);

            var oFormWrap = new TsgcHTMLContainer("div");
            oFormWrap.CSSClass = "card";
            var oCardBody = new TsgcHTMLContainer("div");
            oCardBody.CSSClass = "card-body p-2";

            var oForm = new TsgcHTMLForm();
            oForm.Method = "POST";
            oForm.Action = "/api/chat";
            oForm.CSSClass = "d-flex gap-2";
            oForm.AddHidden("return_to", aReturnTo);

            var oMsgField = new TsgcHTMLField(TsgcHTMLInputType.itText, "message");
            oMsgField.Placeholder = "Ask about products, shipping...";
            oMsgField.Required = true;
            oMsgField.MaxLength = 500;
            oMsgField.InputCSSClass = "form-control form-control-sm";
            oForm.Add(oMsgField);

            var oSendBtn = new TsgcHTMLButton("Send", TsgcHTMLButtonStyle.bsPrimary);
            oSendBtn.ButtonType = "submit";
            oSendBtn.CSSClass = "btn-sm";
            oForm.Add(oSendBtn);

            oCardBody.Add(oForm);
            oFormWrap.Add(oCardBody);
            oOuter.Add(oFormWrap);

            oRoot.Add(oOuter);

            var oBubble = new TsgcHTMLContainer("button");
            oBubble.ID = "shopChatBubble";
            oBubble.Attributes = "type=\"button\" onclick=\"shopToggleChat()\" " +
                "aria-label=\"Open chat\"";
            oBubble.CSSClass = "btn btn-primary rounded-circle shop-chat-bubble";
            oBubble.Style = "display:" + vBubbleDisplay + ";";
            oBubble.AddRaw("&#128172;");
            oRoot.Add(oBubble);

            oRoot.AddScript("function shopToggleChat(){" +
                "var p=document.getElementById('shopChatPanel');" +
                "var b=document.getElementById('shopChatBubble');" +
                "if(!p||!b)return;" +
                "if(p.style.display==='none'){p.style.display='flex';" +
                "b.style.display='none';}else{p.style.display='none';" +
                "b.style.display='flex';}}", false);

            return oRoot.HTML;
        }

        private string BuildCartToastHTML()
        {
            var oToast = new TsgcHTMLComponent_Toast();
            oToast.ToastID = "shopCartToast";
            oToast.Title = "TechNest";
            oToast.Body = "<span id=\"shopCartToastMsg\">Added to cart.</span>";
            oToast.ColorStyle = TsgcHTMLColor.hcLight;
            oToast.AutoHide = true;
            oToast.Delay = 2500;
            string vToastHTML = oToast.HTML;
            // The component may render class="toast ... show" (visible right
            // away); this toast only appears on demand via shopAddToCart(), so
            // the initial 'show' is stripped here - bootstrap.Toast.show() re-adds it.
            vToastHTML = vToastHTML.Replace(" show\"", "\"");
            return TsgcHTMLComponent_Toast.BuildContainer(vToastHTML,
                TsgcHTMLToastPosition.tpTopEnd);
        }

        // Small badge list of the product names (+ price) used as RAG sources,
        // consumed by AddAIMessageWithSources.
        public string BuildChatSourcesHTML(TShopProduct[] aMatched)
        {
            if (aMatched == null || aMatched.Length == 0)
                return "";
            var oRoot = new TsgcHTMLNodeList();
            for (int vI = 0; vI < aMatched.Length; vI++)
            {
                if (vI > 0)
                    oRoot.AddRaw(" ");
                oRoot.AddRaw(TsgcHTMLComponent_Badge.Build(aMatched[vI].Name + " " +
                    FmtPrice(aMatched[vI].Price), TsgcHTMLBadgeStyle.bgSecondary, true));
            }
            return oRoot.HTML;
        }

        // Home > ... > current-page trail. The last crumb always renders as the
        // active/no-link entry (aHrefs[last] is ignored).
        private string BuildBreadcrumbHTML(string[] aTexts, string[] aHrefs)
        {
            var oBreadcrumb = new TsgcHTMLComponent_Breadcrumb();
            for (int vI = 0; vI < aTexts.Length; vI++)
            {
                TsgcHTMLBreadcrumbItem oItem = oBreadcrumb.Items.Add();
                oItem.Text = aTexts[vI];
                if (vI < aTexts.Length - 1)
                    oItem.Href = aHrefs[vI];
            }
            return oBreadcrumb.HTML;
        }

        private string BuildProductCard(TShopProduct aProduct)
        {
            var oRoot = new TsgcHTMLNodeList();

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "h-100 shadow-sm shop-product-card";
            oCard.BodyClass = "card-body d-flex flex-column";

            var oIconWrap = new TsgcHTMLContainer("div");
            oIconWrap.CSSClass = "shop-product-icon text-center mb-2";
            oIconWrap.AddRaw(aProduct.Icon);
            oCard.Body.Add(oIconWrap);

            var oTitleRow = new TsgcHTMLContainer("div");
            oTitleRow.CSSClass = "d-flex justify-content-between " +
                "align-items-start mb-1 gap-2";
            var oHeading = new TsgcHTMLHeading(aProduct.Name, 6);
            oHeading.CSSClass = "card-title mb-0";
            oTitleRow.Add(oHeading);
            oTitleRow.AddRaw(StockBadge(aProduct.Stock));
            oCard.Body.Add(oTitleRow);

            string vBestSeller = BestSellerBadge(aProduct.Rating);
            if (vBestSeller != "")
            {
                var oBadgeWrap = new TsgcHTMLContainer("div");
                oBadgeWrap.CSSClass = "mb-1";
                oBadgeWrap.AddRaw(vBestSeller);
                oCard.Body.Add(oBadgeWrap);
            }

            var oCatRow = new TsgcHTMLContainer("div");
            oCatRow.CSSClass = "text-muted small mb-1";
            oCatRow.AddText(aProduct.Category);
            oCard.Body.Add(oCatRow);

            var oRatingRow = new TsgcHTMLContainer("div");
            oRatingRow.CSSClass = "mb-1";
            oRatingRow.AddRaw(BuildRatingHTML(aProduct));
            oCard.Body.Add(oRatingRow);

            var oPriceRow = new TsgcHTMLContainer("div");
            oPriceRow.CSSClass = "fw-bold shop-price mb-2";
            oPriceRow.AddText(FmtPrice(aProduct.Price));
            oCard.Body.Add(oPriceRow);

            var oDesc = new TsgcHTMLParagraph(aProduct.ShortDescription);
            oDesc.CSSClass = "small text-muted flex-grow-1";
            oCard.Body.Add(oDesc);

            var oActionsRow = new TsgcHTMLContainer("div");
            oActionsRow.CSSClass = "d-flex gap-2 mt-auto";

            var oViewBtn = new TsgcHTMLButton("View details",
                TsgcHTMLButtonStyle.bsOutlinePrimary);
            oViewBtn.Href = "/product/" + aProduct.Id.ToString();
            oViewBtn.CSSClass = "btn-sm";
            oActionsRow.Add(oViewBtn);

            var oCartBtn = new TsgcHTMLButton("Add to cart",
                TsgcHTMLButtonStyle.bsPrimary);
            oCartBtn.CSSClass = "btn-sm";
            oCartBtn.Attributes = "onclick=\"shopAddToCart('" +
                sgcJSEncode(aProduct.Name) + "')\"";
            oActionsRow.Add(oCartBtn);

            oCard.Body.Add(oActionsRow);

            oRoot.Add(oCard);
            return oRoot.HTML;
        }

        private string BuildCategoryFilter(string aActiveCategory)
        {
            var oRoot = new TsgcHTMLNodeList();
            var oGroup = new TsgcHTMLComponent_ButtonGroup();
            oGroup.AriaLabel = "Filter by category";
            oGroup.GroupID = "shopCategoryFilter";

            TsgcHTMLButtonItem oItem = oGroup.Items.Add();
            oItem.Text = "All categories";
            oItem.Href = "/catalog";
            oItem.Active = string.IsNullOrEmpty((aActiveCategory ?? "").Trim());
            oItem.ButtonStyle = TsgcHTMLButtonStyle.bsOutlinePrimary;

            string[] vCategories = ShopCatalog.sgcShopGetCategories();
            for (int vI = 0; vI < vCategories.Length; vI++)
            {
                oItem = oGroup.Items.Add();
                oItem.Text = vCategories[vI];
                oItem.Href = "/catalog?category=" + Uri.EscapeDataString(vCategories[vI]);
                oItem.Active = string.Equals(aActiveCategory, vCategories[vI],
                    StringComparison.OrdinalIgnoreCase);
                oItem.ButtonStyle = TsgcHTMLButtonStyle.bsOutlinePrimary;
            }

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "mb-4";
            oWrap.AddRaw(oGroup.HTML);
            oRoot.Add(oWrap);

            return oRoot.HTML;
        }

        // ----- shared shell ----- //

        public string BuildPageShell(string aTitle, string aBodyHTML,
            string aActiveMenu, string aReturnTo, TShopSession aSession,
            bool aOpenChat)
        {
            var oBody = new TsgcHTMLNodeList();

            oBody.AddRaw(BuildNavbar(aActiveMenu));

            var oMain = new TsgcHTMLContainer("main");
            oMain.CSSClass = "container pb-4";
            oMain.AddRaw(aBodyHTML);
            oBody.Add(oMain);

            oBody.AddRaw(BuildFooter());
            oBody.AddRaw(BuildChatWidgetHTML(aSession, aReturnTo, aOpenChat));
            oBody.AddRaw(BuildCartToastHTML());
            oBody.AddScript("function shopAddToCart(name){" +
                "var m=document.getElementById('shopCartToastMsg');" +
                "if(m)m.textContent=name+' added to cart.';" +
                "var t=document.getElementById('shopCartToast');" +
                "if(t&&window.bootstrap&&window.bootstrap.Toast){" +
                "bootstrap.Toast.getOrCreateInstance(t,{delay:2500}).show();}}", false);

            return WrapTemplate("TechNest - " + aTitle, oBody.HTML);
        }

        // ----- pages ----- //

        public string BuildHomePage(TShopSession aSession, string aReturnTo,
            bool aOpenChat)
        {
            TShopStoreInfo vInfo = ShopCatalog.sgcShopGetStoreInfo();
            TShopProduct[] vCatalog = ShopCatalog.sgcShopGetCatalog();

            // One product per category (the catalog is seeded 4-per-category, so
            // every 4th entry starting at 0 lands on a fresh category's first).
            var vFeatured = new System.Collections.Generic.List<TShopProduct>();
            for (int vJ = 0; vJ < vCatalog.Length; vJ += 4)
                vFeatured.Add(vCatalog[vJ]);

            var oRoot = new TsgcHTMLNodeList();

            var oCarousel = new TsgcHTMLComponent_Carousel();
            oCarousel.CarouselID = "shopHeroCarousel";
            oCarousel.CSSHeight = "340px";
            oCarousel.ShowIndicators = true;
            oCarousel.ShowControls = true;
            oCarousel.Autoplay = true;
            oCarousel.Interval = 5000;
            oCarousel.Dark = true;
            oCarousel.AddSlide(SVGDataURI(CS_SHOP_HERO_SVG_1),
                vInfo.Name + " - New Arrivals",
                "Discover the latest laptops, audio, smart home and wearables.");
            oCarousel.AddSlide(SVGDataURI(CS_SHOP_HERO_SVG_2), "Free Shipping",
                vInfo.ShippingPolicy);
            oCarousel.AddSlide(SVGDataURI(CS_SHOP_HERO_SVG_3), "TechNest Season Sale",
                "Great deals across every category, all season long.");

            var oHeroWrap = new TsgcHTMLContainer("div");
            oHeroWrap.CSSClass = "rounded-4 overflow-hidden shadow-sm mb-3";
            oHeroWrap.AddRaw(oCarousel.HTML);
            oRoot.Add(oHeroWrap);

            var oCtaWrap = new TsgcHTMLContainer("div");
            oCtaWrap.CSSClass = "text-center mb-5";
            var oHeroBtn = new TsgcHTMLButton("Shop the catalog",
                TsgcHTMLButtonStyle.bsPrimary);
            oHeroBtn.Href = "/catalog";
            oHeroBtn.CSSClass = "btn-lg";
            oCtaWrap.Add(oHeroBtn);
            oRoot.Add(oCtaWrap);

            var oSectionHeading = new TsgcHTMLHeading("Featured products", 2);
            oSectionHeading.CSSClass = "h4 mb-3";
            oRoot.Add(oSectionHeading);

            var oGrid = new TsgcHTMLContainer("div");
            oGrid.CSSClass = "row row-cols-1 row-cols-md-2 row-cols-lg-4 g-3 mb-4";
            for (int vI = 0; vI < vFeatured.Count; vI++)
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col";
                oCol.AddRaw(BuildProductCard(vFeatured[vI]));
                oGrid.Add(oCol);
            }
            oRoot.Add(oGrid);

            return BuildPageShell(vInfo.Name, oRoot.HTML, "home", aReturnTo,
                aSession, aOpenChat);
        }

        public string BuildCatalogPage(string aCategory, TShopSession aSession,
            string aReturnTo, bool aOpenChat)
        {
            TShopProduct[] vProducts = ShopCatalog.sgcShopProductsByCategory(aCategory);

            var oRoot = new TsgcHTMLNodeList();

            if (string.IsNullOrEmpty((aCategory ?? "").Trim()))
                oRoot.AddRaw(BuildBreadcrumbHTML(new[] { "Home", "Catalog" },
                    new[] { "/", "" }));
            else
                oRoot.AddRaw(BuildBreadcrumbHTML(new[] { "Home", "Catalog", aCategory },
                    new[] { "/", "/catalog", "" }));

            var oHeading = new TsgcHTMLHeading("Catalog", 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            oRoot.AddRaw(BuildCategoryFilter(aCategory));

            if (vProducts.Length == 0)
            {
                var oEmpty = new TsgcHTMLParagraph("No products in this category yet.");
                oEmpty.CSSClass = "text-muted";
                oRoot.Add(oEmpty);
            }
            else
            {
                var oGrid = new TsgcHTMLContainer("div");
                oGrid.CSSClass = "row row-cols-1 row-cols-md-2 row-cols-lg-4 g-3";
                for (int vI = 0; vI < vProducts.Length; vI++)
                {
                    var oCol = new TsgcHTMLContainer("div");
                    oCol.CSSClass = "col";
                    oCol.AddRaw(BuildProductCard(vProducts[vI]));
                    oGrid.Add(oCol);
                }
                oRoot.Add(oGrid);
            }

            return BuildPageShell("Catalog", oRoot.HTML, "catalog", aReturnTo,
                aSession, aOpenChat);
        }

        public string BuildProductPage(TShopProduct aProduct, bool aFound,
            TShopSession aSession, string aReturnTo, bool aOpenChat)
        {
            var oRoot = new TsgcHTMLNodeList();

            if (!aFound)
            {
                oRoot.AddRaw(BuildBreadcrumbHTML(
                    new[] { "Home", "Catalog", "Not found" },
                    new[] { "/", "/catalog", "" }));

                var oHeading = new TsgcHTMLHeading("Product not found", 1);
                oHeading.CSSClass = "mb-3";
                oRoot.Add(oHeading);
                var oNotFoundP = new TsgcHTMLParagraph("We could not find that " +
                    "product. It may have been removed from the catalog.");
                oNotFoundP.CSSClass = "text-muted";
                oRoot.Add(oNotFoundP);
            }
            else
            {
                oRoot.AddRaw(BuildBreadcrumbHTML(
                    new[] { "Home", "Catalog", aProduct.Name },
                    new[] { "/", "/catalog", "" }));

                var oRow = new TsgcHTMLContainer("div");
                oRow.CSSClass = "row g-4 mb-4";

                var oIconCol = new TsgcHTMLContainer("div");
                oIconCol.CSSClass = "col-md-4 text-center";
                var oIconWrap = new TsgcHTMLContainer("div");
                oIconWrap.CSSClass = "shop-product-icon shop-product-icon-lg";
                oIconWrap.AddRaw(aProduct.Icon);
                oIconCol.Add(oIconWrap);
                oRow.Add(oIconCol);

                var oInfoCol = new TsgcHTMLContainer("div");
                oInfoCol.CSSClass = "col-md-8";

                var oHeaderRow = new TsgcHTMLContainer("div");
                oHeaderRow.CSSClass = "d-flex justify-content-between " +
                    "align-items-start mb-2 gap-2";
                var oHeading = new TsgcHTMLHeading(aProduct.Name, 1);
                oHeading.CSSClass = "mb-0";
                oHeaderRow.Add(oHeading);
                oHeaderRow.AddRaw(StockBadge(aProduct.Stock));
                oInfoCol.Add(oHeaderRow);

                var oCatP = new TsgcHTMLParagraph(aProduct.Category);
                oCatP.CSSClass = "text-muted mb-2";
                oInfoCol.Add(oCatP);

                var oRatingRow = new TsgcHTMLContainer("div");
                oRatingRow.CSSClass = "mb-2";
                oRatingRow.AddRaw(BuildRatingHTML(aProduct));
                oInfoCol.Add(oRatingRow);

                var oPriceP = new TsgcHTMLParagraph(FmtPrice(aProduct.Price));
                oPriceP.CSSClass = "fs-3 fw-bold shop-price mb-3";
                oInfoCol.Add(oPriceP);

                var oCartRow = new TsgcHTMLContainer("div");
                oCartRow.CSSClass = "mb-4";
                var oCartBtn = new TsgcHTMLButton("Add to cart",
                    TsgcHTMLButtonStyle.bsPrimary);
                oCartBtn.Attributes = "onclick=\"shopAddToCart('" +
                    sgcJSEncode(aProduct.Name) + "')\"";
                oCartRow.Add(oCartBtn);
                oInfoCol.Add(oCartRow);

                oRow.Add(oInfoCol);
                oRoot.Add(oRow);

                // Description / Specifications / Reviews as tabs.
                var oTabs = new TsgcHTMLComponent_Tabs();
                oTabs.TabsID = "shopProductTabs";

                TsgcHTMLTabItem oTabItem = oTabs.Items.Add();
                oTabItem.Title = "Description";
                oTabItem.Active = true;
                var oDescNode = new TsgcHTMLParagraph(aProduct.FullDescription);
                oTabItem.Content = oDescNode.HTML;

                if (aProduct.Specs.Length > 0)
                {
                    var oSpecsList = new TsgcHTMLDescriptionList();
                    oSpecsList.TermClass = "col-sm-4 text-muted";
                    oSpecsList.DescClass = "col-sm-8";
                    for (int vI = 0; vI < aProduct.Specs.Length; vI++)
                    {
                        int vColonPos = aProduct.Specs[vI].IndexOf(':');
                        string vKey;
                        string vVal;
                        if (vColonPos >= 0)
                        {
                            vKey = aProduct.Specs[vI].Substring(0, vColonPos).Trim();
                            vVal = aProduct.Specs[vI].Substring(vColonPos + 1).Trim();
                        }
                        else
                        {
                            vKey = aProduct.Specs[vI];
                            vVal = "";
                        }
                        oSpecsList.AddItem(vKey, vVal);
                    }
                    oTabItem = oTabs.Items.Add();
                    oTabItem.Title = "Specifications";
                    oTabItem.Content = oSpecsList.HTML;
                }

                var oReviewsWrap = new TsgcHTMLContainer("div");
                var oRatingLine = new TsgcHTMLContainer("div");
                oRatingLine.CSSClass = "mb-2";
                oRatingLine.AddRaw(BuildRatingHTML(aProduct));
                oReviewsWrap.Add(oRatingLine);
                var oReviewsP = new TsgcHTMLParagraph("Average rating from " +
                    aProduct.ReviewCount.ToString() + " TechNest customers who " +
                    "bought this product.");
                oReviewsP.CSSClass = "text-muted mb-0";
                oReviewsWrap.Add(oReviewsP);

                oTabItem = oTabs.Items.Add();
                oTabItem.Title = "Reviews";
                oTabItem.Content = oReviewsWrap.HTML;

                oRoot.AddRaw(oTabs.HTML);
            }

            if (aFound)
                return BuildPageShell(aProduct.Name, oRoot.HTML, "catalog",
                    aReturnTo, aSession, aOpenChat);
            return BuildPageShell("Product not found", oRoot.HTML, "catalog",
                aReturnTo, aSession, aOpenChat);
        }

        public string BuildAboutPage(TShopSession aSession, string aReturnTo,
            bool aOpenChat)
        {
            TShopStoreInfo vInfo = ShopCatalog.sgcShopGetStoreInfo();

            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildBreadcrumbHTML(new[] { "Home", "About" },
                new[] { "/", "" }));

            var oHeading = new TsgcHTMLHeading("About " + vInfo.Name, 1);
            oHeading.CSSClass = "mb-4";
            oRoot.Add(oHeading);

            var oCard = new TsgcHTMLCard();
            oCard.CSSClass = "shadow-sm mb-4";
            oCard.BodyClass = "card-body";

            var oList = new TsgcHTMLDescriptionList();
            oList.TermClass = "col-sm-3 text-muted";
            oList.DescClass = "col-sm-9";
            oList.AddItem("Founded", vInfo.FoundedYear.ToString());
            oList.AddItem("Hours", vInfo.Hours);
            oList.AddItem("Shipping", vInfo.ShippingPolicy);
            oList.AddItem("Returns", vInfo.ReturnPolicy);
            oList.AddItem("Contact", vInfo.ContactEmail);
            oCard.Body.Add(oList);

            oRoot.Add(oCard);

            var oFaqHeading = new TsgcHTMLHeading("Frequently asked questions", 2);
            oFaqHeading.CSSClass = "h4 mb-3";
            oRoot.Add(oFaqHeading);

            var oAccordion = new TsgcHTMLComponent_Accordion();
            oAccordion.AccordionID = "shopFaq";

            TsgcHTMLAccordionItem oAccItem = oAccordion.Items.Add();
            oAccItem.Title = "What are your store hours?";
            oAccItem.Content = vInfo.Hours;
            oAccItem.Expanded = true;

            oAccItem = oAccordion.Items.Add();
            oAccItem.Title = "What is your shipping policy?";
            oAccItem.Content = vInfo.ShippingPolicy;

            oAccItem = oAccordion.Items.Add();
            oAccItem.Title = "What is your return policy?";
            oAccItem.Content = vInfo.ReturnPolicy;

            oAccItem = oAccordion.Items.Add();
            oAccItem.Title = "How do I contact support?";
            oAccItem.Content = "Email us at " + vInfo.ContactEmail +
                ", or ask Nova, our shop assistant, right from this site.";

            oRoot.AddRaw(oAccordion.HTML);

            return BuildPageShell("About", oRoot.HTML, "about", aReturnTo,
                aSession, aOpenChat);
        }

        // Standalone (no shell, no chat widget) 404 page for unknown routes.
        public string BuildNotFoundPage()
        {
            var oRoot = new TsgcHTMLNodeList();
            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "container py-5 text-center";
            var oHeading = new TsgcHTMLHeading("404 - Page not found", 1);
            oHeading.CSSClass = "mb-3";
            oWrap.Add(oHeading);
            var oP = new TsgcHTMLParagraph(
                "The page you are looking for does not exist.");
            oP.CSSClass = "text-muted mb-4";
            oWrap.Add(oP);
            var oLink = new TsgcHTMLLink("/", "Back to TechNest");
            oLink.CSSClass = "btn btn-primary";
            oWrap.Add(oLink);
            oRoot.Add(oWrap);
            return WrapTemplate("TechNest - Not found", oRoot.HTML);
        }
    }
}
