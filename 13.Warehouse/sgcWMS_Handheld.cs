// ***************************************************************************
//  sgcWMS - warehouse management web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\13.Warehouse\sgcWMS_Handheld.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
// The handheld terminal (/hh/*).
//
// A deliberately different shell from the back office: dark, one task per
// screen, very large touch targets, a fixed bottom action bar and nothing that
// scrolls sideways at 390px. It is the same executable and the same
// TWMSDBPool; only the view changes.
//
// Scanning is TsgcHTMLComponent_CameraScanner, with its manual-entry box left
// switched on because a damaged label never decodes. Quantities are entered on
// TsgcHTMLComponent_NumPad, which also accepts the physical keypad of a rugged
// device.
//
// Zero custom HTML: every screen is a node tree built from sgcHTML components.
// .NET is GC-managed, so the Delphi .Free of a plain node is dropped; the
// sgcHTML components are IDisposable, so their try/finally Free becomes a
// using block.

using System;
using System.Globalization;
// sgc
using esegece.sgcWebSockets;

namespace WMS
{
    public class TWMSHandheld
    {
        private readonly TWMSDBPool FDB;

        // Handheld-only stylesheet: dark, wide fingers, nothing below 44px, and a
        // hard rule that no block may be wider than the viewport.
        private const string CS_HH_CSS = "body{background:#0b0f16;color:#e8eaf0;}" +
            ".hh-wrap{max-width:520px;margin:0 auto;padding:1rem 1rem 6rem;" +
            "min-width:0;}" + ".hh-wrap *{max-width:100%;}" +
            ".hh-wrap svg{height:auto;}" + ".hh-wrap canvas{max-width:100%;}" +
            ".hh-tile{display:flex;flex-direction:column;justify-content:center;" +
            "align-items:center;width:100%;min-height:104px;font-size:1.2rem;" +
            "font-weight:700;border-radius:1rem;text-decoration:none;}" +
            ".hh-tile small{font-weight:400;opacity:.75;font-size:.85rem;" +
            "margin-top:.25rem;}" +
            ".hh-bar{position:fixed;left:0;right:0;bottom:0;z-index:1030;" +
            "background:#121824;border-top:1px solid #24304a;padding:.65rem;" +
            "display:flex;gap:.5rem;}" +
            ".hh-bar .btn{flex:1 1 0;min-height:56px;font-size:1rem;font-weight:600;}" +
            ".hh-head{display:flex;align-items:center;gap:.5rem;" +
            "margin-bottom:1rem;}" + ".hh-head h1{font-size:1.15rem;margin:0;" +
            "flex:1 1 auto;overflow:hidden;text-overflow:ellipsis;" +
            "white-space:nowrap;}" +
            ".card{background:#121824;border-color:#24304a;color:#e8eaf0;}" +
            ".form-control,.form-select{min-height:54px;font-size:1.1rem;" +
            "background:#121824;border-color:#2b3752;color:#e8eaf0;}" +
            ".form-control::placeholder{color:#7b879e;}" + ".btn{min-height:48px;}" +
            ".list-group-item{background:#121824;border-color:#24304a;" +
            "color:#e8eaf0;min-height:64px;display:flex;align-items:center;" +
            "justify-content:space-between;gap:.5rem;font-size:1.05rem;}" +
            ".table{color:#e8eaf0;font-size:.95rem;}" +
            ".table-responsive{overflow-x:auto;}" +
            ".sgc-camera-scanner{border:1px solid #2b3752;border-radius:.75rem;" +
            "padding:.5rem;background:#0f1420;}" +
            ".sgc-scanner-stage video{width:100%;height:100%;object-fit:cover;" +
            "border-radius:.5rem;background:#000;}" +
            ".sgc-scanner-actions{display:flex;flex-wrap:wrap;gap:.5rem;" +
            "margin-top:.5rem;}" +
            ".sgc-scanner-manual{display:flex;gap:.5rem;margin-top:.5rem;}" +
            ".sgc-scanner-manual input{flex:1 1 auto;min-width:0;}" +
            ".sgc-scanner-status{margin-top:.5rem;font-size:.9rem;color:#9fb0cc;}" +
            ".sgc-numpad{margin:0 auto;}" +
            ".sgc-numpad-display{font-size:1.75rem;font-weight:700;text-align:right;" +
            "background:#0f1420;border:1px solid #2b3752;border-radius:.5rem;" +
            "padding:.5rem .75rem;margin-bottom:.5rem;}" +
            ".hh-flash{animation:hhflash .6s ease-out;}" +
            "@keyframes hhflash{0%{box-shadow:0 0 0 0 rgba(34,197,94,.9);}" +
            "100%{box-shadow:0 0 0 26px rgba(34,197,94,0);}}";

        // Visual confirmation of a scan. The audible one is the component's own
        // BeepOnScan; this adds the green pulse and drops the code into the status
        // line so the operator sees what was read without looking away.
        private const string CS_HH_SCAN_JS =
            "document.addEventListener(\"sgcCameraScanner:scan\"," +
            "function(e){var p=document.querySelector(\".sgc-camera-scanner\");" +
            "if(p){p.classList.remove(\"hh-flash\");void p.offsetWidth;" +
            "p.classList.add(\"hh-flash\");}" +
            "var f=document.getElementById(\"hhScanForm\");" +
            "if(f&&f.dataset.autosubmit===\"1\"){setTimeout(function(){" +
            "if(typeof f.requestSubmit===\"function\"){f.requestSubmit();}" +
            "else{f.submit();}},350);}});";

        // Wrap already-rendered HTML in a div, through the node layer.
        private static string Wrap(string aCSSClass, string aHTML)
        {
            var oBox = new TsgcHTMLContainer("div");
            oBox.CSSClass = aCSSClass;
            oBox.AddRaw(aHTML);
            return oBox.HTML;
        }

        // A form label, node-built.
        private static string FieldLabel(string aText)
        {
            var oLbl = new TsgcHTMLContainer("label");
            oLbl.CSSClass = "form-label mt-3";
            oLbl.AddText(aText);
            return oLbl.HTML;
        }

        private static string HHEsc(string aValue)
        {
            string vResult = (aValue ?? string.Empty).Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            vResult = vResult.Replace("\"", "&quot;");
            return vResult;
        }

        public TWMSHandheld(TWMSDBPool aDB)
        {
            FDB = aDB;
        }

        private string BuildMenu(TWMSPageCtx aCtx)
        {
            string vBody;
            using (var oList = new TsgcHTMLComponent_ListGroup())
            {
                oList.ListGroupID = "hhMenu";
                oList.Flush = true;
                oList.AddItem("Home", "/hh");
                oList.AddItem("Receive", "/hh/receive");
                oList.AddItem("Pick", "/hh/pick");
                oList.AddItem("Count", "/hh/count");
                oList.AddItem("Lookup", "/hh/lookup");
                if (WMSConst.WMSRoleIsBackOffice(aCtx.Role))
                {
                    oList.AddItem("Back office", "/");
                }

                oList.AddItem("Sign out", "/logout");
                vBody = Wrap("text-secondary small mb-2", "Signed in as " +
                    HHEsc(aCtx.DisplayName) + " (" +
                    HHEsc(WMSConst.WMSRoleCaption(aCtx.Role)) + ")") + oList.HTML;
            }

            return TsgcHTMLComponent_Offcanvas.Build("hhMenuPanel", "sgcWMS terminal",
                vBody, TsgcHTMLOffcanvasPlacement.opStart, true);
        }

        private string BuildMessage(string aMessage, string aKind)
        {
            if ((aMessage ?? string.Empty).Trim() == "")
            {
                return string.Empty;
            }

            TsgcHTMLColor vColor;
            if (string.Equals(aKind, "error", StringComparison.OrdinalIgnoreCase))
            {
                vColor = TsgcHTMLColor.hcDanger;
            }
            else
            {
                vColor = TsgcHTMLColor.hcSuccess;
            }

            return TsgcHTMLComponent_Toast.BuildContainer(
                TsgcHTMLComponent_Toast.Build("sgcWMS", aMessage, vColor,
                DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture)),
                TsgcHTMLToastPosition.tpTopCenter);
        }

        private string WrapShell(TWMSPageCtx aCtx, string aTitle, string aBodyHTML,
            string aBackHref)
        {
            var oBody = new TsgcHTMLNodeList();

            var oWrap = new TsgcHTMLContainer("div");
            oWrap.CSSClass = "hh-wrap";

            var oHead = new TsgcHTMLContainer("div");
            oHead.CSSClass = "hh-head";
            oHead.AddRaw(TsgcHTMLComponent_Offcanvas.BuildTriggerButton("hhMenuPanel",
                "Menu", TsgcHTMLButtonStyle.bsOutlineLight));
            var oH = new TsgcHTMLHeading(aTitle, 1);
            oH.CSSClass = "";
            oHead.Add(oH);
            oWrap.Add(oHead);

            oWrap.AddRaw(aBodyHTML);
            oBody.Add(oWrap);

            // Fixed bottom action bar: reachable with one thumb.
            var oBar = new TsgcHTMLContainer("div");
            oBar.CSSClass = "hh-bar";
            var oBtn = new TsgcHTMLButton("Home", TsgcHTMLButtonStyle.bsOutlineLight);
            oBtn.Href = "/hh";
            oBar.Add(oBtn);
            if (aBackHref != "")
            {
                oBtn = new TsgcHTMLButton("Back", TsgcHTMLButtonStyle.bsOutlineSecondary);
                oBtn.Href = aBackHref;
                oBar.Add(oBtn);
            }

            oBtn = new TsgcHTMLButton("Scan", TsgcHTMLButtonStyle.bsWarning);
            oBtn.Href = "/hh/lookup";
            oBar.Add(oBtn);
            oBody.Add(oBar);

            oBody.AddRaw(BuildMenu(aCtx));
            oBody.AddScript(CS_HH_SCAN_JS, false);
            string vBody = oBody.HTML;

            using (var oTpl = new TsgcHTMLTemplate_Bootstrap())
            {
                oTpl.Title = "sgcWMS terminal - " + aTitle;
                oTpl.HtmlLang = "en";
                // Handheld: fixed 1.0 scale, no user zoom fighting the touch targets.
                oTpl.Viewport = "width=device-width, initial-scale=1, " +
                    "viewport-fit=cover";
                oTpl.HtmlTheme = "dark";
                oTpl.DarkMode = false;
                oTpl.CustomCSS = CS_HH_CSS;
                oTpl.HeadNodes.AddRaw("<link rel=\"icon\" type=\"image/svg+xml\" " +
                    "href=\"/favicon.svg\">");
                oTpl.BodyContent = vBody;
                return oTpl.GetHTML();
            }
        }

        // The shared scan panel: a POST form wrapping a CameraScanner whose
        // manual-entry box is always visible.
        private string BuildScanForm(string aAction, string aFieldName, string aPrompt,
            string aSubmitCaption, string aExtraHTML = "")
        {
            var oForm = new TsgcHTMLForm();
            oForm.FormID = "hhScanForm";
            oForm.Method = "POST";
            oForm.Action = aAction;
            oForm.Attributes = "data-autosubmit=\"1\"";

            var oP = new TsgcHTMLParagraph(aPrompt);
            oP.CSSClass = "text-secondary mb-2";
            oForm.Add(oP);

            using (var oScan = new TsgcHTMLComponent_CameraScanner())
            {
                oScan.ScannerID = "hhScanner";
                oScan.CSSWidth = "100%";
                oScan.CSSHeight = "240px";
                oScan.Mode = TsgcHTMLScannerMode.smBarcode;
                oScan.Formats = "ean_13,ean_8,code_128,code_39,qr_code";
                oScan.FacingMode = TsgcHTMLScannerFacing.sfEnvironment;
                oScan.ContinuousScan = false;
                oScan.ScanDelayMs = 400;
                oScan.BeepOnScan = true;
                oScan.ShowTorchButton = true;
                oScan.ShowDeviceSelector = true;
                oScan.ShowManualEntry = true;
                oScan.ShowCaptureButton = false;
                oScan.ManualEntryPlaceholder = "Key the code in";
                oScan.StartCaption = "Start camera";
                oScan.StopCaption = "Stop camera";
                oScan.FieldName = aFieldName;
                oScan.AutoStart = false;
                oForm.AddRaw(oScan.HTML);
            }

            if (aExtraHTML != "")
            {
                oForm.AddRaw(aExtraHTML);
            }

            var oBtn = new TsgcHTMLButton(aSubmitCaption, TsgcHTMLButtonStyle.bsWarning);
            oBtn.ButtonType = "submit";
            oBtn.CSSClass = "w-100 mt-3";
            oForm.Add(oBtn);

            return oForm.HTML;
        }

        private string BuildQtyPad(string aFieldName, double aValue)
        {
            using (var oPad = new TsgcHTMLComponent_NumPad())
            {
                oPad.PadID = "hhQty";
                oPad.FieldName = aFieldName;
                oPad.Value = aValue;
                oPad.Mode = TsgcHTMLNumPadMode.npInteger;
                oPad.DecimalPlaces = 0;
                oPad.ShowDisplay = true;
                oPad.ShowClear = true;
                oPad.ShowBackspace = true;
                oPad.ShowDecimalPoint = false;
                oPad.ShowEnter = false;
                oPad.ButtonSize = 72;
                oPad.ColorStyle = TsgcHTMLButtonStyle.bsOutlineLight;
                oPad.QuickAmounts.Add("1");
                oPad.QuickAmounts.Add("5");
                oPad.QuickAmounts.Add("10");
                oPad.QuickAmounts.Add("25");
                return oPad.HTML;
            }
        }

        // ------------------------------------------------------------------ home ---

        public string BuildHome(TWMSPageCtx aCtx)
        {
            TWMSDashboardStats vStats = FDB.GetDashboardStats();
            var oRoot = new TsgcHTMLNodeList();
            TsgcHTMLContainer oRow;

            // One big touch tile: a link styled as a block button, with a caption and
            // a smaller subtitle underneath.
            void AddTile(string aCaption, string aSub, string aHref,
                TsgcHTMLButtonStyle aStyle)
            {
                var oCol = new TsgcHTMLContainer("div");
                oCol.CSSClass = "col-6";
                var oLink = new TsgcHTMLContainer("a");
                oLink.CSSClass = "btn hh-tile " +
                    sgcHTMLEnums.sgcButtonStyleToClass(aStyle);
                oLink.Attributes = "href=\"" + HHEsc(aHref) + "\" role=\"button\"";
                oLink.AddText(aCaption);
                if (aSub != "")
                {
                    var oSmall = new TsgcHTMLContainer("small");
                    oSmall.AddText(aSub);
                    oLink.Add(oSmall);
                }

                oCol.Add(oLink);
                oRow.Add(oCol);
            }

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3 mb-4";
            AddTile("Receive", "Book a delivery in", "/hh/receive",
                TsgcHTMLButtonStyle.bsWarning);
            AddTile("Pick", "Work an order", "/hh/pick", TsgcHTMLButtonStyle.bsPrimary);
            AddTile("Count", "Cycle count a bin", "/hh/count",
                TsgcHTMLButtonStyle.bsInfo);
            AddTile("Lookup", "Scan anything", "/hh/lookup",
                TsgcHTMLButtonStyle.bsSecondary);
            oRoot.Add(oRow);

            oRow = new TsgcHTMLContainer("div");
            oRow.CSSClass = "row g-3";
            oRow.AddRaw(Wrap("col-6", TsgcHTMLComponent_StatCard.Build("Received today",
                vStats.ReceivedToday.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scWarning)));
            oRow.AddRaw(Wrap("col-6", TsgcHTMLComponent_StatCard.Build("Picked today",
                vStats.PickedToday.ToString(CultureInfo.InvariantCulture),
                TsgcHTMLStatColor.scSuccess)));
            oRoot.Add(oRow);

            string vResult = oRoot.HTML;
            return WrapShell(aCtx, "Terminal", vResult, "");
        }

        // --------------------------------------------------------------- receive ---

        // Receive: scan, then quantity and put-away bin.
        public string BuildReceive(TWMSPageCtx aCtx, string aScan, string aMessage)
        {
            TWMSProduct vProduct = null;
            bool vHas = ((aScan ?? string.Empty).Trim() != "") &&
                FDB.GetProductByBarcode(aScan, out vProduct);

            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildMessage(aMessage, ""));

            if (!vHas)
            {
                if ((aScan ?? string.Empty).Trim() != "")
                {
                    using (var oEmpty = new TsgcHTMLComponent_EmptyState())
                    {
                        oEmpty.Title = "Unknown code";
                        oEmpty.Description = "Nothing in the catalogue matches " +
                            (aScan ?? string.Empty).Trim() +
                            ". Scan again or key the SKU.";
                        oEmpty.Compact = true;
                        oEmpty.Bordered = true;
                        oRoot.AddRaw(oEmpty.HTML);
                    }
                }

                oRoot.AddRaw(BuildScanForm("/hh/receive", "scan",
                    "Scan the product label on the pallet.", "Look the product up"));
            }
            else
            {
                long vSuggested = FDB.SuggestPutawayBin(vProduct.Id, 1);
                using (var oPanel = new TsgcHTMLComponent_Panel())
                {
                    oPanel.PanelID = "hhRecProd";
                    oPanel.Title = vProduct.SKU;
                    oPanel.Color = TsgcHTMLColor.hcDark;
                    oPanel.Body = HHEsc(vProduct.Name) + "<br>" +
                        TsgcHTMLComponent_Badge.Build(vProduct.Category,
                        TsgcHTMLBadgeStyle.bgSecondary, true) + " " +
                        TsgcHTMLComponent_Badge.Build(vProduct.UOM,
                        TsgcHTMLBadgeStyle.bgInfo, true);
                    oRoot.AddRaw(oPanel.HTML);
                }

                var oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/hh/receive";
                oForm.AddHidden("product_id",
                    vProduct.Id.ToString(CultureInfo.InvariantCulture));
                oForm.AddHidden("confirm", "1");

                oForm.AddRaw(FieldLabel("Quantity received"));
                oForm.AddRaw(BuildQtyPad("qty", 0));

                using (var oSel = new TsgcHTMLComponent_Select())
                {
                    oSel.SelectID = "hhRecBin";
                    oSel.ElementName = "location_id";
                    oSel.Label_ = "Put away to bin";
                    using (TWMSDataSet oData = FDB.OpenSQL(
                        "SELECT id, code FROM locations " +
                        "WHERE kind = 'bin' ORDER BY code"))
                    {
                        oSel.LoadFromDataSet(oData.DataSet, "id", "code");
                    }

                    TWMSLocation vLoc = null;
                    if ((vSuggested > 0) && FDB.GetLocation(vSuggested, out vLoc))
                    {
                        oSel.AddOption(vSuggested.ToString(CultureInfo.InvariantCulture),
                            vLoc.Code + "  (suggested)", true);
                    }

                    oForm.AddRaw(Wrap("mt-3", oSel.HTML));
                }

                var oBtn = new TsgcHTMLButton("Confirm receipt",
                    TsgcHTMLButtonStyle.bsSuccess);
                oBtn.ButtonType = "submit";
                oBtn.CSSClass = "w-100 mt-3";
                oForm.Add(oBtn);

                oRoot.AddRaw(oForm.HTML);

                oBtn = new TsgcHTMLButton("Scan another product",
                    TsgcHTMLButtonStyle.bsOutlineLight);
                oBtn.Href = "/hh/receive";
                oBtn.CSSClass = "w-100 mt-3";
                oRoot.AddRaw(oBtn.HTML);
            }

            string vBody = oRoot.HTML;
            return WrapShell(aCtx, "Receive", vBody, "/hh");
        }

        // ------------------------------------------------------------------ pick ---

        // Pick: the open orders, then one order's lines.
        public string BuildPickOrders(TWMSPageCtx aCtx)
        {
            var oRoot = new TsgcHTMLNodeList();
            using (TWMSDataSet oData = FDB.OpenSalesOrders("", ""))
            {
                using (var oList = new TsgcHTMLComponent_ListGroup())
                {
                    oList.ListGroupID = "hhOrders";
                    while (!oData.Eof)
                    {
                        if ((oData.AsStr("status") == "new") ||
                            (oData.AsStr("status") == "picking"))
                        {
                            oList.AddItem(oData.AsStr("reference") + "  " +
                                oData.AsStr("customer"),
                                "/hh/pick/" + oData.AsStr("id"),
                                oData.AsStr("qty_picked") + "/" +
                                oData.AsStr("qty_ordered"));
                        }

                        oData.Next();
                    }

                    if (oList.Items.Count == 0)
                    {
                        using (var oEmpty = new TsgcHTMLComponent_EmptyState())
                        {
                            oEmpty.Title = "Nothing to pick";
                            oEmpty.Description = "Every order is packed or shipped.";
                            oEmpty.ActionCaption = "Back to the terminal";
                            oEmpty.ActionHref = "/hh";
                            oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsOutlineLight;
                            oEmpty.Bordered = true;
                            oRoot.AddRaw(oEmpty.HTML);
                        }
                    }
                    else
                    {
                        oRoot.AddRaw(oList.HTML);
                    }
                }
            }

            string vBody = oRoot.HTML;
            return WrapShell(aCtx, "Pick", vBody, "/hh");
        }

        public string BuildPickOrder(TWMSPageCtx aCtx, long aSOId, string aMessage)
        {
            TWMSSalesOrder vSO = null;
            if (!FDB.GetSalesOrder(aSOId, out vSO))
            {
                return WrapShell(aCtx, "Pick", TsgcHTMLComponent_EmptyState.Build(
                    "Order not found", "That order is not in the system."), "/hh/pick");
            }

            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildMessage(aMessage, ""));
            oRoot.AddRaw(Wrap("text-secondary mb-3", HHEsc(vSO.Reference) +
                " &middot; " + HHEsc(vSO.CustomerName)));

            bool vAny;
            using (var oGrid = new TsgcHTMLComponent_Grid())
            {
                oGrid.TableID = "hhPickGrid";
                oGrid.Responsive = true;
                oGrid.Dark = true;
                oGrid.Striped = true;
                oGrid.EmptyText = "Every line is picked.";
                using (TWMSDataSet oData = FDB.OpenPickList(aSOId))
                {
                    vAny = !oData.IsEmpty();
                    if (oData.DataSet.Columns.Contains("location"))
                    {
                        oData.DataSet.Columns["location"].Caption = "Bin";
                    }

                    if (oData.DataSet.Columns.Contains("sku"))
                    {
                        oData.DataSet.Columns["sku"].Caption = "SKU";
                    }

                    if (oData.DataSet.Columns.Contains("outstanding"))
                    {
                        oData.DataSet.Columns["outstanding"].Caption = "Qty";
                    }

                    oGrid.LoadFromDataSet(oData.DataSet,
                        new string[] { "location", "sku", "outstanding" });
                }

                oRoot.AddRaw(oGrid.HTML);
            }

            if (!vAny)
            {
                using (var oEmpty = new TsgcHTMLComponent_EmptyState())
                {
                    oEmpty.Title = "Order complete";
                    oEmpty.Description = "Hand it to packing.";
                    oEmpty.ActionCaption = "Next order";
                    oEmpty.ActionHref = "/hh/pick";
                    oEmpty.ActionStyle = TsgcHTMLButtonStyle.bsWarning;
                    oEmpty.Compact = true;
                    oRoot.AddRaw(oEmpty.HTML);
                }
            }
            else
            {
                var oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/hh/pick/confirm";
                oForm.AddHidden("so_id", aSOId.ToString(CultureInfo.InvariantCulture));

                using (var oSel = new TsgcHTMLComponent_Select())
                {
                    oSel.SelectID = "hhPickLine";
                    oSel.ElementName = "pick";
                    oSel.Label_ = "Line to confirm";
                    using (TWMSDataSet oData = FDB.OpenPickList(aSOId))
                    {
                        while (!oData.Eof)
                        {
                            oSel.AddOption(oData.AsStr("line_id") + ":" +
                                oData.AsStr("location_id"),
                                oData.AsStr("location") + "  " +
                                oData.AsStr("sku") + "  x" +
                                oData.AsStr("outstanding"));
                            oData.Next();
                        }
                    }

                    oForm.AddRaw(oSel.HTML);
                }

                oForm.AddRaw(FieldLabel("Quantity picked"));
                oForm.AddRaw(BuildQtyPad("qty", 0));

                var oBtn = new TsgcHTMLButton("Confirm pick",
                    TsgcHTMLButtonStyle.bsSuccess);
                oBtn.ButtonType = "submit";
                oBtn.CSSClass = "w-100 mt-3";
                oForm.Add(oBtn);
                oRoot.AddRaw(oForm.HTML);
            }

            string vBody = oRoot.HTML;
            return WrapShell(aCtx, "Pick " + vSO.Reference, vBody, "/hh/pick");
        }

        // ----------------------------------------------------------------- count ---

        // Count: scan a product in a bin and key the counted quantity.
        public string BuildCount(TWMSPageCtx aCtx, string aScan, string aMessage)
        {
            TWMSProduct vProduct = null;
            bool vHas = ((aScan ?? string.Empty).Trim() != "") &&
                FDB.GetProductByBarcode(aScan, out vProduct);

            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildMessage(aMessage, ""));
            if (!vHas)
            {
                if ((aScan ?? string.Empty).Trim() != "")
                {
                    using (var oEmpty = new TsgcHTMLComponent_EmptyState())
                    {
                        oEmpty.Title = "Unknown code";
                        oEmpty.Description = "Nothing matches " +
                            (aScan ?? string.Empty).Trim() + ".";
                        oEmpty.Compact = true;
                        oEmpty.Bordered = true;
                        oRoot.AddRaw(oEmpty.HTML);
                    }
                }

                oRoot.AddRaw(BuildScanForm("/hh/count", "scan",
                    "Scan the product you are counting.", "Count this product"));
            }
            else
            {
                using (var oPanel = new TsgcHTMLComponent_Panel())
                {
                    oPanel.PanelID = "hhCountProd";
                    oPanel.Title = vProduct.SKU;
                    oPanel.Color = TsgcHTMLColor.hcDark;
                    oPanel.Body = HHEsc(vProduct.Name);
                    oRoot.AddRaw(oPanel.HTML);
                }

                var oForm = new TsgcHTMLForm();
                oForm.Method = "POST";
                oForm.Action = "/hh/count";
                oForm.AddHidden("product_id",
                    vProduct.Id.ToString(CultureInfo.InvariantCulture));
                oForm.AddHidden("confirm", "1");

                using (var oSel = new TsgcHTMLComponent_Select())
                {
                    oSel.SelectID = "hhCountBin";
                    oSel.ElementName = "location_id";
                    oSel.Label_ = "Bin";
                    // The join is scoped to the scanned product, so each bin appears
                    // exactly once and the caption shows what the system believes is in
                    // that bin for THIS product - which is the number the operator is
                    // about to disagree with.
                    using (TWMSDataSet oData = FDB.Query(
                        "SELECT l.id, l.code || '  (' || " +
                        "COALESCE(s.qty, 0) || ' on file)' AS code FROM locations l " +
                        "LEFT JOIN stock s ON s.location_id = l.id " +
                        "AND s.product_id = :p WHERE l.kind = 'bin' ORDER BY l.code"))
                    {
                        oData.SetInt("p", vProduct.Id);
                        oData.Open();
                        oSel.LoadFromDataSet(oData.DataSet, "id", "code");
                    }

                    oForm.AddRaw(oSel.HTML);
                }

                oForm.AddRaw(FieldLabel("Counted quantity"));
                oForm.AddRaw(BuildQtyPad("counted", 0));

                var oBtn = new TsgcHTMLButton("Post count", TsgcHTMLButtonStyle.bsSuccess);
                oBtn.ButtonType = "submit";
                oBtn.CSSClass = "w-100 mt-3";
                oForm.Add(oBtn);
                oRoot.AddRaw(oForm.HTML);
            }

            string vBody = oRoot.HTML;
            return WrapShell(aCtx, "Count", vBody, "/hh");
        }

        // ---------------------------------------------------------------- lookup ---

        // Lookup: scan a barcode, see the product and every bin holding it.
        public string BuildLookup(TWMSPageCtx aCtx, string aScan, string aMessage)
        {
            TWMSProduct vProduct = null;
            bool vHas = ((aScan ?? string.Empty).Trim() != "") &&
                FDB.GetProductByBarcode(aScan, out vProduct);

            var oRoot = new TsgcHTMLNodeList();
            oRoot.AddRaw(BuildMessage(aMessage, ""));
            if (!vHas)
            {
                if ((aScan ?? string.Empty).Trim() != "")
                {
                    using (var oEmpty = new TsgcHTMLComponent_EmptyState())
                    {
                        oEmpty.Title = "Nothing found";
                        oEmpty.Description = "No product carries the code " +
                            (aScan ?? string.Empty).Trim() + ". Try the SKU instead.";
                        oEmpty.Compact = true;
                        oEmpty.Bordered = true;
                        oRoot.AddRaw(oEmpty.HTML);
                    }
                }

                oRoot.AddRaw(BuildScanForm("/hh/lookup", "scan",
                    "Point the camera at any barcode, or key the SKU.", "Look it up"));
            }
            else
            {
                long vTotal = FDB.ScalarInt("SELECT COALESCE(SUM(qty), 0) FROM stock " +
                    "WHERE product_id = " +
                    vProduct.Id.ToString(CultureInfo.InvariantCulture));
                using (var oPanel = new TsgcHTMLComponent_Panel())
                {
                    oPanel.PanelID = "hhLookProd";
                    oPanel.Title = vProduct.SKU;
                    oPanel.Color = TsgcHTMLColor.hcDark;
                    oPanel.Body = HHEsc(vProduct.Name) + "<br>" +
                        TsgcHTMLComponent_Badge.Build(
                        vTotal.ToString(CultureInfo.InvariantCulture) + " " +
                        vProduct.UOM + " on hand", TsgcHTMLBadgeStyle.bgSuccess, true) +
                        " " + TsgcHTMLComponent_Badge.Build(vProduct.Category,
                        TsgcHTMLBadgeStyle.bgSecondary, true);
                    oRoot.AddRaw(oPanel.HTML);
                }

                using (var oCode = new TsgcHTMLComponent_Barcode())
                {
                    oCode.Data = vProduct.SKU;
                    oCode.Height = 50;
                    oCode.ModuleWidth = 2;
                    oCode.Color = "#e8eaf0";
                    oCode.BackgroundColor = "#121824";
                    oRoot.AddRaw(Wrap("text-center my-3", oCode.HTML));
                }

                using (var oGrid = new TsgcHTMLComponent_Grid())
                {
                    oGrid.TableID = "hhLookStock";
                    oGrid.Responsive = true;
                    oGrid.Dark = true;
                    oGrid.Striped = true;
                    oGrid.EmptyText = "Not stocked anywhere.";
                    using (TWMSDataSet oData = FDB.OpenProductStock(vProduct.Id))
                    {
                        if (oData.DataSet.Columns.Contains("location"))
                        {
                            oData.DataSet.Columns["location"].Caption = "Bin";
                        }

                        if (oData.DataSet.Columns.Contains("qty"))
                        {
                            oData.DataSet.Columns["qty"].Caption = "Qty";
                        }

                        oGrid.LoadFromDataSet(oData.DataSet,
                            new string[] { "location", "qty" });
                    }

                    oRoot.AddRaw(oGrid.HTML);
                }

                var oBtn = new TsgcHTMLButton("Scan another",
                    TsgcHTMLButtonStyle.bsOutlineLight);
                oBtn.Href = "/hh/lookup";
                oBtn.CSSClass = "w-100 mt-3";
                oRoot.AddRaw(oBtn.HTML);
            }

            string vBody = oRoot.HTML;
            return WrapShell(aCtx, "Lookup", vBody, "/hh");
        }
    }
}
