// ***************************************************************************
//  sgcWMS - warehouse management web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\13.Warehouse\sgcWMS_Reports.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
// Printable output: PDF through TsgcHTMLExportPDF and XLSX through
// TsgcHTMLExportXLSX, both fed by the same query the screens use.
//
// Three documents, as the demo brief asks for:
//   - the stock valuation report (landscape, auto-paginating table, totals),
//   - a packing slip for one sales order,
//   - a sheet of scannable product and bin labels.
//
// The product list exports reuse the Grid's own SaveToXLSXStream /
// SaveToPDFStream, so what the operator downloads is exactly the grid that is
// on screen, in its current sort order.
//
// The managed port keeps the Delphi class + method names. .NET is GC-managed,
// so the Delphi try/finally .Free pairs are dropped where the managed type is
// not IDisposable, and become "using" where it is.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using esegece.sgcWebSockets;

namespace WMS
{
    public class TWMSReports
    {
        private readonly TWMSDBPool FDB;

        // The amber accent of the printed documents plus the two neutral inks.
        private const string CS_ACCENT = "#B45309";
        private const string CS_INK = "#111827";
        private const string CS_GREY = "#6B7280";

        // Code 128 element widths, one entry per symbol value. Each character of
        // the pattern is the width of the next element in modules, starting with a
        // bar and alternating bar / space. The last entry is the stop pattern,
        // which has a seventh element. Same table the Barcode component uses; it
        // lives in that unit's implementation section, so the label sheet carries
        // its own copy.
        private static readonly string[] CS_CODE128_PATTERNS = new string[]
        {
            "212222", "222122",
            "222221", "121223", "121322", "131222", "122213", "122312", "132212",
            "221213", "221312", "231212", "112232", "122132", "122231", "113222",
            "123122", "123221", "223211", "221132", "221231", "213212", "223112",
            "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313",
            "132113", "132311", "211313", "231113", "231311", "112133", "112331",
            "132131", "113123", "113321", "133121", "313121", "211331", "231131",
            "213113", "213311", "213131", "311123", "311321", "331121", "312113",
            "312311", "332111", "314111", "221411", "431111", "111224", "111422",
            "121124", "121421", "141122", "141221", "112214", "112412", "122114",
            "122411", "142112", "142211", "241211", "221114", "413111", "241112",
            "134111", "111242", "121142", "121241", "114212", "124112", "124211",
            "411212", "421112", "421211", "212141", "214121", "412121", "111143",
            "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
        };

        private const int CS_CODE128_START_B = 104;
        private const int CS_CODE128_STOP = 106;

        // Draw aData as a Code 128 subset B symbol, aX/aY millimetres from the top
        // left, aWidth millimetres wide, aHeight tall. Subset B covers ASCII
        // 32..126, which is every SKU and bin code this demo produces. Characters
        // outside that range are skipped rather than raising: a label sheet must
        // always print.
        private static void DrawCode128(TsgcHTMLExportPDF aPDF, double aX, double aY,
            double aWidth, double aHeight, string aData)
        {
            List<int> vValues = new List<int>();
            for (int vI = 0; vI < aData.Length; vI++)
            {
                int vOrd = (int)aData[vI];
                if ((vOrd < 32) || (vOrd > 126))
                {
                    continue;
                }

                vValues.Add(vOrd - 32);
            }

            if (vValues.Count == 0)
            {
                return;
            }

            // Checksum: start value plus each symbol weighted by its position, mod 103.
            int vCheck = CS_CODE128_START_B;
            for (int vI = 0; vI < vValues.Count; vI++)
            {
                vCheck = vCheck + ((vI + 1) * vValues[vI]);
            }

            vCheck = vCheck % 103;

            // Total module count: start + data + check + stop (the stop is 13 modules).
            int vModules = 11 + (vValues.Count * 11) + 11 + 13;
            if (vModules <= 0)
            {
                return;
            }

            double vUnit = aWidth / vModules;

            aPDF.SetColor(CS_INK);
            double vPos = aX;

            // Emit start, data, check, stop in one pass.
            for (int vI = -1; vI <= vValues.Count + 1; vI++)
            {
                string vPattern;
                if (vI == -1)
                {
                    vPattern = CS_CODE128_PATTERNS[CS_CODE128_START_B];
                }
                else if (vI == vValues.Count)
                {
                    vPattern = CS_CODE128_PATTERNS[vCheck];
                }
                else if (vI == vValues.Count + 1)
                {
                    vPattern = CS_CODE128_PATTERNS[CS_CODE128_STOP];
                }
                else
                {
                    vPattern = CS_CODE128_PATTERNS[vValues[vI]];
                }

                bool vIsBar = true;
                for (int vJ = 0; vJ < vPattern.Length; vJ++)
                {
                    int vElement;
                    if (!int.TryParse(vPattern.Substring(vJ, 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out vElement))
                    {
                        vElement = 0;
                    }

                    if (vElement > 0)
                    {
                        if (vIsBar)
                        {
                            aPDF.Rect(vPos, aY, vElement * vUnit, aHeight, true);
                        }

                        vPos = vPos + (vElement * vUnit);
                    }

                    vIsBar = !vIsBar;
                }
            }
        }

        // Draw aData as a QR code, aSize millimetres square. Uses the encoder the
        // QRCode component exposes, so the printed code and the on-screen one are
        // the same symbol.
        private static void DrawQRCode(TsgcHTMLExportPDF aPDF, double aX, double aY,
            double aSize, string aData)
        {
            if (aData.Trim() == "")
            {
                return;
            }

            TsgcHTMLQRCodeEncoder oEnc = new TsgcHTMLQRCodeEncoder();
            try
            {
                oEnc.Encode(aData, TsgcHTMLQRECCLevel.qrMedium);
            }
            catch (Exception)
            {
                return;
            }

            if (oEnc.Size <= 0)
            {
                return;
            }

            double vModule = aSize / oEnc.Size;
            aPDF.SetColor(CS_INK);
            for (int vRow = 0; vRow < oEnc.Size; vRow++)
            {
                for (int vCol = 0; vCol < oEnc.Size; vCol++)
                {
                    if (oEnc.GetModule(vCol, vRow))
                    {
                        aPDF.Rect(aX + (vCol * vModule), aY + (vRow * vModule),
                            vModule, vModule, true);
                    }
                }
            }
        }

        // The amber brand mark, drawn rather than embedded so the PDF stays small
        // and the demo needs no image asset on disk.
        private static void DrawLogo(TsgcHTMLExportPDF aPDF, double aX, double aY, double aSize)
        {
            aPDF.SetColor(WMSConst.CS_WMS_ACCENT);
            aPDF.Rect(aX, aY, aSize, aSize, true);
            aPDF.SetColor("#FFFFFF");
            aPDF.Rect(aX + (aSize * 0.25), aY + (aSize * 0.45), aSize * 0.5,
                aSize * 0.35, true);
        }

        private static string MoneyStr(double aValue)
        {
            return aValue.ToString("#,##0.00", CultureInfo.InvariantCulture);
        }

        // Delphi: oData.DataSet.FindField('x').DisplayLabel := 'Y'. On the managed
        // side the DataTable column Caption is what the grid reads as its header.
        // The lookup is guarded, so a query that does not expose the column simply
        // keeps the column name as its header instead of raising.
        private static void SetDisplayLabel(DataTable aTable, string aField, string aLabel)
        {
            if ((aTable != null) && aTable.Columns.Contains(aField))
            {
                aTable.Columns[aField].Caption = aLabel;
            }
        }

        public TWMSReports(TWMSDBPool aDB)
        {
            FDB = aDB;
        }

        // ------------------------------------------------------- product exports ---

        // Product catalogue in the caller's current filter and sort order.
        public void ProductsXLSX(Stream aStream, string aSearch, string aCategory,
            string aSort, string aDir)
        {
            using (TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid())
            {
                oGrid.ExportSheetName = "Products";
                using (TWMSDataSet oData = FDB.OpenProducts(aSearch, aCategory, aSort,
                    aDir, 1, 5000))
                {
                    SetDisplayLabel(oData.DataSet, "sku", "SKU");
                    SetDisplayLabel(oData.DataSet, "name", "Product");
                    SetDisplayLabel(oData.DataSet, "category", "Category");
                    SetDisplayLabel(oData.DataSet, "uom", "UoM");
                    SetDisplayLabel(oData.DataSet, "unit_cost", "Unit cost");
                    SetDisplayLabel(oData.DataSet, "min_stock", "Minimum");
                    SetDisplayLabel(oData.DataSet, "onhand", "On hand");
                    oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                        "category", "uom", "unit_cost", "min_stock", "onhand" });
                }

                oGrid.SaveToXLSXStream(aStream);
            }
        }

        public void ProductsPDF(Stream aStream, string aSearch, string aCategory,
            string aSort, string aDir)
        {
            using (TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid())
            {
                using (TWMSDataSet oData = FDB.OpenProducts(aSearch, aCategory, aSort,
                    aDir, 1, 2000))
                {
                    SetDisplayLabel(oData.DataSet, "sku", "SKU");
                    SetDisplayLabel(oData.DataSet, "name", "Product");
                    SetDisplayLabel(oData.DataSet, "category", "Category");
                    SetDisplayLabel(oData.DataSet, "uom", "UoM");
                    SetDisplayLabel(oData.DataSet, "unit_cost", "Unit cost");
                    SetDisplayLabel(oData.DataSet, "onhand", "On hand");
                    oGrid.LoadFromDataSet(oData.DataSet, new string[] { "sku", "name",
                        "category", "uom", "unit_cost", "onhand" });
                }

                oGrid.SaveToPDFStream(aStream);
            }
        }

        // ----------------------------------------------------- stock  valuation ----

        // Stock valuation: every product, its on-hand quantity and its value.
        public void ValuationPDF(Stream aStream, string aCompany)
        {
            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = "Stock valuation";
            oPDF.Author = "sgcWMS";
            oPDF.Subject = "Stock valuation at " +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poLandscape;
            oPDF.MarginLeft = 12;
            oPDF.MarginRight = 12;
            oPDF.MarginTop = 22;
            oPDF.MarginBottom = 16;
            oPDF.HeaderText = aCompany + "   |   Stock valuation   |   " +
                DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            oPDF.FooterText = "sgcWMS - generated by a Delphi executable, no " +
                "reporting server involved.   Page {page} of {pages}";
            oPDF.ShowPageNumbers = true;

            // The brand mark sits above the table on the first page.
            DrawLogo(oPDF, 12, 6, 10);
            oPDF.SetColor(CS_ACCENT);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 15);
            oPDF.TextOut(26, 14, "Stock valuation");
            oPDF.SetColor(CS_GREY);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8);
            oPDF.TextOut(26, 19, "On-hand quantity times unit cost, per product.");

            oPDF.SetColor(CS_INK);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8.5);
            oPDF.CurrentY = 26;
            oPDF.BeginTable(32, 78, 34, 16, 26, 26, 32);
            oPDF.TableHeader("SKU", "Product", "Category", "UoM", "Unit cost",
                "On hand", "Value");

            double vTotalValue = 0;
            long vTotalUnits = 0;
            using (TWMSDataSet oData = FDB.OpenValuation())
            {
                while (!oData.Eof)
                {
                    oPDF.TableRow(oData.AsStr("sku"), oData.AsStr("name"),
                        oData.AsStr("category"), oData.AsStr("uom"),
                        MoneyStr(oData.AsFloat("unit_cost")), oData.AsStr("onhand"),
                        MoneyStr(oData.AsFloat("value")));
                    vTotalValue = vTotalValue + oData.AsFloat("value");
                    vTotalUnits = vTotalUnits + oData.AsInt("onhand");
                    oData.Next();
                }
            }

            oPDF.TableRow("", "TOTAL", "", "", "",
                vTotalUnits.ToString(CultureInfo.InvariantCulture),
                MoneyStr(vTotalValue));
            oPDF.EndTable();

            oPDF.SaveToStream(aStream);
        }

        public void ValuationXLSX(Stream aStream)
        {
            TsgcHTMLExportXLSX oXLSX = new TsgcHTMLExportXLSX();
            oXLSX.AddSheet("Valuation");
            oXLSX.AddRow();
            oXLSX.AddCell("SKU");
            oXLSX.AddCell("Product");
            oXLSX.AddCell("Category");
            oXLSX.AddCell("UoM");
            oXLSX.AddCell("Unit cost");
            oXLSX.AddCell("On hand");
            oXLSX.AddCell("Value");

            double vTotal = 0;
            using (TWMSDataSet oData = FDB.OpenValuation())
            {
                while (!oData.Eof)
                {
                    oXLSX.AddRow();
                    oXLSX.AddCell(oData.AsStr("sku"));
                    oXLSX.AddCell(oData.AsStr("name"));
                    oXLSX.AddCell(oData.AsStr("category"));
                    oXLSX.AddCell(oData.AsStr("uom"));
                    oXLSX.AddCellNumber(oData.AsFloat("unit_cost"));
                    oXLSX.AddCellNumber(oData.AsInt("onhand"));
                    oXLSX.AddCellNumber(oData.AsFloat("value"));
                    vTotal = vTotal + oData.AsFloat("value");
                    oData.Next();
                }
            }

            oXLSX.AddRow();
            oXLSX.AddRow();
            oXLSX.AddCell("TOTAL");
            oXLSX.AddCell("");
            oXLSX.AddCell("");
            oXLSX.AddCell("");
            oXLSX.AddCell("");
            oXLSX.AddCell("");
            oXLSX.AddCellNumber(vTotal);

            oXLSX.SaveToStream(aStream);
        }

        // ---------------------------------------------------------- packing slip ---

        // One sales order as a packing slip, with a proof-of-delivery box.
        public void PackingSlipPDF(Stream aStream, long aSOId, string aCompany)
        {
            TWMSSalesOrder vSO;
            if (!FDB.GetSalesOrder(aSOId, out vSO))
            {
                throw new EWMSError("Sales order not found");
            }

            string vAddress = "";
            using (TWMSDataSet oData = FDB.Query("SELECT name, address, city, country, contact " +
                "FROM customers WHERE id = :i"))
            {
                oData.SetInt("i", vSO.CustomerId);
                oData.Open();
                if (!oData.IsEmpty())
                {
                    vAddress = oData.AsStr("address") + ", " + oData.AsStr("city") + ", " +
                        oData.AsStr("country");
                }
            }

            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = "Packing slip " + vSO.Reference;
            oPDF.Author = "sgcWMS";
            oPDF.Subject = "Packing slip for " + vSO.CustomerName;
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poPortrait;
            oPDF.MarginLeft = 18;
            oPDF.MarginRight = 18;
            oPDF.MarginTop = 20;
            oPDF.MarginBottom = 18;
            oPDF.HeaderText = aCompany + "   |   Packing slip " + vSO.Reference;
            oPDF.FooterText = "Packing slip " + vSO.Reference +
                "   Page {page} of {pages}";
            oPDF.ShowPageNumbers = true;

            DrawLogo(oPDF, 18, 8, 11);
            oPDF.SetColor(CS_ACCENT);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 17);
            oPDF.TextOut(33, 16, "PACKING SLIP");

            oPDF.SetColor(CS_INK);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 10);
            oPDF.TextOut(18, 32, "Deliver to");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 9.5);
            oPDF.TextRect(18, 38, 85, vSO.CustomerName, TsgcHTMLPDFAlign.paLeft);
            oPDF.TextRect(18, 43.5, 85, vAddress, TsgcHTMLPDFAlign.paLeft);

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 10);
            oPDF.TextOut(115, 32, "Order");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 9.5);
            oPDF.TextOut(115, 38, "Reference: " + vSO.Reference);
            oPDF.TextOut(115, 43.5, "Raised: " +
                vSO.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            oPDF.TextOut(115, 49, "Status: " + vSO.Status);
            oPDF.TextOut(115, 54.5, "Priority: " + vSO.Priority);

            // A scannable copy of the order reference for the loading bay.
            DrawCode128(oPDF, 115, 60, 75, 12, vSO.Reference);
            oPDF.SetFont(TsgcHTMLPDFFont.pfCourier, 8);
            oPDF.SetColor(CS_GREY);
            oPDF.TextRect(115, 76, 75, vSO.Reference, TsgcHTMLPDFAlign.paCenter);

            oPDF.SetColor(CS_INK);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 9);
            oPDF.CurrentY = 86;
            oPDF.BeginTable(28, 74, 20, 20, 22);
            oPDF.TableHeader("SKU", "Product", "UoM", "Ordered", "Picked");

            int vLines = 0;
            using (TWMSDataSet oData = FDB.OpenSalesOrderLines(aSOId))
            {
                while (!oData.Eof)
                {
                    oPDF.TableRow(oData.AsStr("sku"), oData.AsStr("name"),
                        oData.AsStr("uom"), oData.AsStr("qty_ordered"),
                        oData.AsStr("qty_picked"));
                    vLines++;
                    oData.Next();
                }
            }

            oPDF.EndTable();

            double vY = oPDF.CurrentY + 14;
            if (vY > 240)
            {
                oPDF.NewPage();
                vY = 40;
            }

            oPDF.SetColor(CS_GREY);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8.5);
            oPDF.TextOut(18, vY, vLines.ToString(CultureInfo.InvariantCulture) +
                " line(s). Goods remain the " +
                "property of the seller until paid in full.");

            // Proof of delivery: the box the driver gets signed. The on-screen
            // SignaturePad captures the same thing electronically.
            vY = vY + 12;
            oPDF.SetColor(CS_INK);
            oPDF.SetLineWidth(0.4);
            oPDF.Rect(18, vY, 174, 34, false);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 9);
            oPDF.TextOut(21, vY + 7, "Received in good condition");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8.5);
            oPDF.SetColor(CS_GREY);
            oPDF.Line(21, vY + 24, 105, vY + 24);
            oPDF.TextOut(21, vY + 29, "Signature");
            oPDF.Line(115, vY + 24, 189, vY + 24);
            oPDF.TextOut(115, vY + 29, "Name and date");

            oPDF.SaveToStream(aStream);
        }

        // ----------------------------------------------------------- label sheet ---

        // Scannable labels for a purchase order: one product label per line plus
        // the bin labels of the suggested put-away locations.
        public void LabelsPDF(Stream aStream, long aPOId, string aCompany)
        {
            const int CS_COLS = 2;
            const int CS_ROWS = 5;
            const double CS_LEFT = 12;
            const double CS_TOP = 26;
            const double CS_W = 93;
            const double CS_H = 50;

            TWMSPurchaseOrder vPO;
            if (!FDB.GetPurchaseOrder(aPOId, out vPO))
            {
                throw new EWMSError("Purchase order not found");
            }

            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            List<string> vBins = new List<string>();

            int vIndex = 0;
            int vCol = 0;
            int vRow = 0;
            double vX = CS_LEFT;
            double vY = CS_TOP;

            // Advance to the next label slot, starting a fresh sheet (and repeating
            // the amber title) every time the 2 x 5 grid wraps round.
            void NextSlot()
            {
                vCol = vIndex % CS_COLS;
                vRow = (vIndex / CS_COLS) % CS_ROWS;
                if ((vIndex > 0) && (vCol == 0) && (vRow == 0))
                {
                    oPDF.NewPage();
                    oPDF.SetColor(CS_ACCENT);
                    oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 13);
                    oPDF.TextOut(CS_LEFT, 18, aCompany + " - label sheet (continued)");
                }

                vX = CS_LEFT + (vCol * (CS_W + 4));
                vY = CS_TOP + (vRow * (CS_H + 4));
                vIndex++;
            }

            // One label: framed box, title and subtitle on the left, the Code 128
            // symbol under them with the human-readable code, and the QR code of the
            // same payload on the right.
            void DrawLabel(string aTitle, string aSubtitle, string aCode, string aFootnote)
            {
                oPDF.SetColor("#D1D5DB");
                oPDF.SetLineWidth(0.3);
                oPDF.Rect(vX, vY, CS_W, CS_H, false);

                oPDF.SetColor(CS_INK);
                oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
                oPDF.TextRect(vX + 3, vY + 8, CS_W - 34, aTitle, TsgcHTMLPDFAlign.paLeft);
                oPDF.SetColor(CS_GREY);
                oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 7.5);
                oPDF.TextRect(vX + 3, vY + 13.5, CS_W - 34, aSubtitle,
                    TsgcHTMLPDFAlign.paLeft);

                DrawCode128(oPDF, vX + 3, vY + 18, CS_W - 34, 15, aCode);
                oPDF.SetColor(CS_INK);
                oPDF.SetFont(TsgcHTMLPDFFont.pfCourier, 8);
                oPDF.TextRect(vX + 3, vY + 38, CS_W - 34, aCode, TsgcHTMLPDFAlign.paLeft);

                DrawQRCode(oPDF, vX + CS_W - 29, vY + 8, 26, aCode);

                if (aFootnote != "")
                {
                    oPDF.SetColor(CS_GREY);
                    oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 6.5);
                    oPDF.TextRect(vX + 3, vY + 45, CS_W - 6, aFootnote,
                        TsgcHTMLPDFAlign.paLeft);
                }
            }

            oPDF.Title = "Labels " + vPO.Reference;
            oPDF.Author = "sgcWMS";
            oPDF.Subject = "Product and bin labels for " + vPO.Reference;
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poPortrait;
            oPDF.MarginLeft = 10;
            oPDF.MarginRight = 10;
            oPDF.MarginTop = 14;
            oPDF.MarginBottom = 12;
            oPDF.HeaderText = aCompany + "   |   Labels for " + vPO.Reference;
            oPDF.FooterText = "Print on 2 x 5 label stock.   Page {page} of {pages}";
            oPDF.ShowPageNumbers = true;

            DrawLogo(oPDF, CS_LEFT, 8, 9);
            oPDF.SetColor(CS_ACCENT);
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 13);
            oPDF.TextOut(CS_LEFT + 13, 15, aCompany + " - label sheet");

            vIndex = 0;
            vX = CS_LEFT;
            vY = CS_TOP;

            using (TWMSDataSet oData = FDB.OpenPurchaseOrderLines(aPOId))
            {
                while (!oData.Eof)
                {
                    NextSlot();
                    DrawLabel(oData.AsStr("sku"), oData.AsStr("name"),
                        oData.AsStr("barcode"), "Ordered " + oData.AsStr("qty_ordered") +
                        " " + oData.AsStr("uom") + " on " + vPO.Reference);

                    TWMSLocation vLoc;
                    long vSuggested = FDB.SuggestPutawayBin(oData.AsInt("product_id"),
                        (int)Math.Max(1, oData.AsInt("outstanding")));
                    if ((vSuggested > 0) && FDB.GetLocation(vSuggested, out vLoc))
                    {
                        // The Delphi keeps the bin codes in a TStringList, whose
                        // IndexOf is case-insensitive by default, so the managed
                        // lookup compares ordinal-ignore-case too.
                        if (!vBins.Contains(vLoc.Code, StringComparer.OrdinalIgnoreCase))
                        {
                            vBins.Add(vLoc.Code);
                        }
                    }

                    oData.Next();
                }
            }

            for (int vI = 0; vI < vBins.Count; vI++)
            {
                NextSlot();
                DrawLabel(vBins[vI], "Put-away location", vBins[vI],
                    "Suggested destination for " + vPO.Reference);
            }

            oPDF.SaveToStream(aStream);
        }
    }
}
