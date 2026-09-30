// ***************************************************************************
//  sgcReports - reporting and BI portal web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\15.Reports\sgcReports_I18n.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
//
//  Compact UI string table for the chrome of the portal (navigation, page
//  headings, common buttons). Three languages: en / es / de, selected by the
//  'reports_lang' cookie which POST /lang writes.
//
//  The Delphi unit writes its accented characters as Pascal character escapes
//  so the file keeps exactly one high byte; here the same characters are
//  written as C# \uXXXX escapes for the same reason.
// ***************************************************************************

using System;

namespace Reports
{
    // Every UI string the shell and the page headings need. Values are looked up
    // by key; an unknown key returns the key itself, which makes a missing entry
    // obvious on screen instead of silently blank.
    public static class TReportsI18n
    {
        private const int CS_LANG_COUNT = 3;

        // key, en, es, de
        private static readonly string[,] CS_I18N = new string[95, CS_LANG_COUNT + 1]
        {
            { "nav.home", "Dashboard", "Panel", "Dashboard" },
            { "nav.dashboards", "Boards", "Cuadros", "Boards" },
            { "nav.reports", "Reports", "Informes", "Berichte" },
            { "nav.pivot", "Pivot", "Dinámica", "Pivot" },
            { "nav.explore", "Explore", "Explorar", "Analyse" },
            { "nav.views", "Saved views", "Vistas", "Ansichten" },
            { "nav.schedules", "Schedules", "Programaciones", "Zeitpläne" },
            { "nav.jobs", "Jobs", "Trabajos", "Jobs" },
            { "nav.sql", "SQL", "SQL", "SQL" },
            { "nav.users", "Users", "Usuarios", "Benutzer" },
            { "nav.logout", "Sign out", "Salir", "Abmelden" },
            { "theme.light", "Light", "Claro", "Hell" },
            { "theme.dark", "Dark", "Oscuro", "Dunkel" },
            { "theme.system", "System", "Sistema", "System" },
            { "btn.run", "Run report", "Ejecutar", "Bericht starten" },
            { "btn.apply", "Apply", "Aplicar", "Anwenden" },
            { "btn.reset", "Reset", "Limpiar", "Zurücksetzen" },
            { "btn.save", "Save", "Guardar", "Speichern" },
            { "btn.delete", "Delete", "Borrar", "Löschen" },
            { "btn.pdf", "PDF", "PDF", "PDF" },
            { "btn.xlsx", "Excel", "Excel", "Excel" },
            { "btn.csv", "CSV", "CSV", "CSV" },
            { "btn.preview", "Preview", "Vista previa", "Vorschau" },
            { "btn.runnow", "Run now", "Ejecutar ahora", "Jetzt starten" },
            { "lbl.rows", "rows", "filas", "Zeilen" },
            { "lbl.ms", "server ms", "ms servidor", "Server-ms" },
            { "lbl.page", "Page", "Página", "Seite" },
            { "lbl.of", "of", "de", "von" },
            { "lbl.search", "Search", "Buscar", "Suchen" },
            { "lbl.sort", "Sort by", "Ordenar por", "Sortieren nach" },
            { "lbl.dir", "Direction", "Sentido", "Richtung" },
            { "lbl.asc", "Ascending", "Ascendente", "Aufsteigend" },
            { "lbl.desc", "Descending", "Descendente", "Absteigend" },
            { "lbl.category", "Category", "Categoría", "Kategorie" },
            { "lbl.region", "Region", "Región", "Region" },
            { "lbl.segment", "Segment", "Segmento", "Segment" },
            { "lbl.from", "From", "Desde", "Von" },
            { "lbl.to", "To", "Hasta", "Bis" },
            { "lbl.revenue", "Revenue", "Ingresos", "Umsatz" },
            { "lbl.margin", "Margin", "Margen", "Marge" },
            { "lbl.orders", "Orders", "Pedidos", "Aufträge" },
            { "lbl.customers", "Customers", "Clientes", "Kunden" },
            { "lbl.lines", "Order lines", "Líneas", "Positionen" },
            { "ttl.catalogue", "Report catalogue", "Catálogo de informes",
                "Berichtskatalog" },
            { "ttl.explore", "Explore order lines",
                "Explorar líneas de pedido", "Positionen analysieren" },
            { "ttl.pivot", "Ad-hoc pivot builder",
                "Constructor de tabla dinámica", "Pivot-Baukasten" },
            { "ttl.sql", "This is the whole data layer",
                "Esta es toda la capa de datos", "Das ist die ganze Datenschicht" },
            { "ttl.jobs", "Scheduled export jobs", "Trabajos programados",
                "Geplante Exporte" },
            { "nav.analytics", "Analytics", "Analítica", "Analytik" },
            { "nav.charts", "Chart gallery", "Galería de gráficos",
                "Diagrammgalerie" },
            { "nav.market", "Price candles", "Velas de precio", "Preiskerzen" },
            { "nav.pivotlab", "Pivot lab", "Laboratorio pivot", "Pivot-Labor" },
            { "sub.charts", "Every new chart type on the real order data.",
                "Todos los nuevos tipos de gráfico sobre los pedidos reales.",
                "Alle neuen Diagrammtypen auf den echten Auftragsdaten." },
            { "sub.market", "Weekly OHLC of the average selling price, with " +
                "indicators and live ticks.",
                "OHLC semanal del precio medio de venta, con indicadores " +
                "y ticks en vivo.",
                "Wöchentliches OHLC des durchschnittlichen " +
                "Verkaufspreises, mit Indikatoren und Live-Ticks." },
            { "sub.pivotlab", "Expandable pivots with date groups, top N, heatmap, " +
                "drill-through and export.",
                "Tablas dinámicas expandibles con grupos de fechas, top N, " +
                "mapa de calor, detalle y exportación.",
                "Aufklappbare Pivots mit Datumsgruppen, Top N, Heatmap, " +
                "Drill-through und Export." },
            { "an.area", "Revenue trend (area)", "Tendencia de ingresos (área)",
                "Umsatztrend (Fläche)" },
            { "an.mixed", "Revenue and margin % (two Y axes)",
                "Ingresos y % de margen (dos ejes Y)",
                "Umsatz und Marge % (zwei Y-Achsen)" },
            { "an.hbar", "Top salespeople (horizontal bars)",
                "Mejores comerciales (barras horizontales)",
                "Top-Verkäufer (horizontale Balken)" },
            { "an.stack100", "Category mix per region (100% stacked)",
                "Mezcla de categorías por región (apilado 100%)",
                "Kategoriemix je Region (100 % gestapelt)" },
            { "an.funnel", "Order status funnel", "Embudo de estados de pedido",
                "Auftragsstatus-Trichter" },
            { "an.waterfall", "From gross sales to margin (waterfall)",
                "De ventas brutas a margen (cascada)",
                "Von Bruttoumsatz zu Marge (Wasserfall)" },
            { "an.pareto", "Revenue by customer (Pareto)",
                "Ingresos por cliente (Pareto)", "Umsatz je Kunde (Pareto)" },
            { "an.bullet", "Region revenue against target (bullet)",
                "Ingresos por región frente al objetivo (bala)",
                "Regionsumsatz gegen Ziel (Bullet)" },
            { "an.radial", "Revenue share per region (radial bars)",
                "Cuota de ingresos por región (barras radiales)",
                "Umsatzanteil je Region (Radialbalken)" },
            { "an.click", "Revenue by region, click a bar",
                "Ingresos por región, pulse una barra",
                "Umsatz je Region, Balken anklicken" },
            { "an.clickhint", "Click a bar to load the top customers of that " +
                "region below.",
                "Pulse una barra para cargar debajo los mejores clientes " +
                "de esa región.",
                "Klicken Sie auf einen Balken, um unten die Top-Kunden " +
                "dieser Region zu laden." },
            { "an.live", "Live order flow", "Flujo de pedidos en vivo",
                "Live-Auftragsfluss" },
            { "an.livehint", "A hidden poller asks the server every 2 seconds " +
                "and the reply appends a point.",
                "Un sondeo oculto consulta al servidor cada 2 segundos y " +
                "la respuesta añade un punto.",
                "Ein versteckter Poller fragt den Server alle 2 " +
                "Sekunden, die Antwort hängt einen Punkt an." },
            { "an.detail", "Top customers", "Mejores clientes", "Top-Kunden" },
            { "an.placed", "Placed", "Recibidos", "Eingegangen" },
            { "an.notcancelled", "Not cancelled", "No cancelados",
                "Nicht storniert" },
            { "an.shipped", "Shipped", "Enviados", "Versendet" },
            { "an.delivered", "Delivered", "Entregados", "Geliefert" },
            { "an.gross", "Gross sales", "Ventas brutas", "Bruttoumsatz" },
            { "an.discounts", "Discounts", "Descuentos", "Rabatte" },
            { "an.cost", "Product cost", "Coste de producto", "Produktkosten" },
            { "an.target", "Target", "Objetivo", "Ziel" },
            { "an.pcttarget", "% of target", "% del objetivo", "% vom Ziel" },
            { "an.market", "Average selling price", "Precio medio de venta",
                "Durchschnittlicher Verkaufspreis" },
            { "an.style", "Style", "Estilo", "Stil" },
            { "an.candle", "Candles", "Velas", "Kerzen" },
            { "an.ohlc", "OHLC bars", "Barras OHLC", "OHLC-Balken" },
            { "an.group", "Group dates by", "Agrupar fechas por",
                "Datum gruppieren nach" },
            { "an.year", "Year", "Año", "Jahr" },
            { "an.quarter", "Quarter", "Trimestre", "Quartal" },
            { "an.month", "Month", "Mes", "Monat" },
            { "an.pivotmain", "Revenue by region and category",
                "Ingresos por región y categoría",
                "Umsatz nach Region und Kategorie" },
            { "an.pivothint", "Click a cell for the order lines behind it, drag " +
                "fields to change the layout.",
                "Pulse una celda para ver sus líneas de pedido, arrastre " +
                "campos para cambiar el diseño.",
                "Klicken Sie auf eine Zelle für die Positionen dahinter, " +
                "ziehen Sie Felder für ein anderes Layout." },
            { "an.pivotstats", "Segment statistics",
                "Estadísticas por segmento", "Segmentstatistik" },
            { "an.median", "Median line", "Mediana por línea",
                "Median je Position" },
            { "an.stddev", "Std. deviation", "Desviación típica",
                "Standardabweichung" },
            { "an.ofcol", "of column", "de la columna", "der Spalte" },
            { "an.ofrow", "of row", "de la fila", "der Zeile" },
            { "an.oftotal", "of total", "del total", "vom Gesamt" },
            { "an.running", "Running total", "Total acumulado", "Laufende Summe" }
        };

        /// <summary>'en' | 'es' | 'de'. Anything else (including '') resolves to 'en'.</summary>
        public static string Normalize(string aLang)
        {
            string vLang = (aLang ?? "").Trim().ToLowerInvariant();
            if (vLang == "es" || vLang == "de")
                return vLang;
            return "en";
        }

        public static string LangCaption(string aLang)
        {
            if (Normalize(aLang) == "es")
                return "Espanol";
            if (Normalize(aLang) == "de")
                return "Deutsch";
            return "English";
        }

        public static string T(string aLang, string aKey)
        {
            string vLang = Normalize(aLang);
            int vCol;
            if (vLang == "es")
                vCol = 2;
            else if (vLang == "de")
                vCol = 3;
            else
                vCol = 1;

            for (int vI = 0; vI < CS_I18N.GetLength(0); vI++)
                if (CS_I18N[vI, 0] == aKey)
                    return CS_I18N[vI, vCol];
            return aKey;
        }
    }
}
