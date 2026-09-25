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
        private static readonly string[,] CS_I18N = new string[48, CS_LANG_COUNT + 1]
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
                "Geplante Exporte" }
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
