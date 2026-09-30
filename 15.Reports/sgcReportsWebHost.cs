// ***************************************************************************
//  sgcReportsWeb - reporting and BI portal web-app demo on ASP.NET Core
//  Mirror of demos\60.HTML\15.Reports (TsgcWebSocketHTTPServer host).
//
//  This is the REWRITTEN host layer, following the 01.ERP host pattern. It
//  reproduces every branch of the 60.HTML sgcReports_Server.cs DispatchRequest,
//  but on Kestrel:
//    - The reusable logic (Bcrypt, Config, DB, I18n, Pages, Passkeys, Sessions,
//      Types) is copied VERBATIM from the 60.HTML demo and NOT changed here.
//    - The 60.HTML host scanned RawHeaders for the Cookie line and wrote
//      Set-Cookie / redirects through CustomHeaders on TsgcWSHTTPResponseInfo.
//      Here every cookie is read/written through the native ASP.NET Core cookie
//      API (ctx.Request.Cookies / ctx.Response.Cookies) with the SAME cookie
//      names + attributes, and each handler returns an IResult.
//    - Query + form params are merged through GetParam / GetParamArray, matching
//      the Delphi ARequestInfo.Params (query first, then the urlencoded body).
//    - PASSKEYS: the 60.HTML host hard-coded RP id 'localhost' + origin
//      http://localhost:5708. Under Kestrel the RP id (request host name) and
//      origin (scheme://host) are DERIVED from the live request and threaded
//      into the reused TReportsPasskeys. A per-origin instance is cached so the
//      WebAuthn begin/finish challenge state survives across the two requests of
//      a ceremony (see GetPasskeys).
//    - The realtime /jobs page and the Analytics live chart / candle feed are
//      driven by a BackgroundService + ISgcHtmlHub instead of the 60.HTML push
//      thread + TsgcHTMX_Engine_Server.Broadcast.
//
//  The two host files that were NOT copied from 60.HTML are
//  sgcReports_Server.cs (the TsgcWebSocketHTTPServer host) and the console
//  Program.cs.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
// sgc
using esegece.sgcWebSockets.AspNetCore;

namespace Reports
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, passkey factory) and exposes one IResult-returning method per
    // 60.HTML DispatchRequest branch. Program.cs maps the Minimal API endpoints
    // onto these methods and applies the auth / role gates through Guarded*.
    public sealed class ReportsWebHost : IDisposable
    {
        public const string CS_REPORTS_SERVER_VERSION = "1.0.0";

        // Cookie names - identical to the 60.HTML host so behavior matches exactly.
        public const string CS_REPORTS_SESSION = "reports_session";
        public const string CS_REPORTS_THEME_COOKIE = "reports_theme";
        public const string CS_REPORTS_LANG_COOKIE = "reports_lang";

        // Where /pdf.min.js is redirected when no local copy has been dropped into
        // assets\. Nothing on any rendered page points here; only this redirect
        // does, and only when the file is absent.
        private const string CS_PDFJS_FALLBACK =
            "https://cdn.jsdelivr.net/npm/pdfjs-dist@3.11.174/build/";

        private readonly TReportsDBPool FDB;
        private readonly TReportsSessionStore FSessions;
        private readonly TReportsPages FPages;
        private readonly ISgcHtmlHub FHub;
        private readonly DateTime FStartedAt;

        // Per-origin passkey cache. WebAuthn begin/finish share challenge state in
        // one TReportsPasskeys instance, so a ceremony's two requests (same
        // browser, same origin) must hit the SAME instance. Keyed by "rpId|origin".
        private readonly Dictionary<string, TReportsPasskeys> FPasskeys =
            new Dictionary<string, TReportsPasskeys>(StringComparer.Ordinal);
        private readonly object FPasskeysLock = new object();

        // Builds the DB (schema + admin seed + demo data), the session store and
        // the page builder - mirroring TReportsServer.InitRuntime.
        public ReportsWebHost(TReportsServerConfig aConfig, string aDatabasePath,
            ISgcHtmlHub aHub)
        {
            FStartedAt = DateTime.Now;
            FHub = aHub;

            FDB = new TReportsDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedAdmin(aConfig.AdminUser, Bcrypt.BcryptHash(aConfig.AdminPassword));
            FDB.SeedDemoData();

            FSessions = new TReportsSessionStore(480, true);
            FPages = new TReportsPages(FDB);

            // The live components must exist before the first request AND before the
            // push service touches them.
            TReportsPages.LiveInit();
        }

        public void Dispose()
        {
            lock (FPasskeysLock)
            {
                foreach (KeyValuePair<string, TReportsPasskeys> vPair in FPasskeys)
                {
                    try { vPair.Value.Dispose(); } catch { }
                }
                FPasskeys.Clear();
            }
            if (FDB != null)
            {
                try { FDB.Dispose(); } catch { }
            }
            TReportsPages.LiveDone();
        }

        // ----- passkey factory (request-derived rpId + origin) ----- //

        private TReportsPasskeys GetPasskeys(HttpContext aCtx)
        {
            string vRPID = aCtx.Request.Host.Host;
            if (string.IsNullOrEmpty(vRPID))
                vRPID = "localhost";
            string vOrigin = aCtx.Request.Scheme + "://" + aCtx.Request.Host.Value;
            string vKey = vRPID + "|" + vOrigin;

            lock (FPasskeysLock)
            {
                TReportsPasskeys vPk;
                if (!FPasskeys.TryGetValue(vKey, out vPk))
                {
                    vPk = new TReportsPasskeys(FDB, vRPID, "sgcReports BI", vOrigin);
                    FPasskeys[vKey] = vPk;
                }
                return vPk;
            }
        }

        // ----- request param helpers (query + form merged) ----- //

        // First value for aName, trimmed: query wins over form, matching the Delphi
        // merged ARequestInfo.Params. aForm is null for GET / non-form requests.
        private static string GetParam(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            return GetRawParam(aCtx, aForm, aName).Trim();
        }

        // First value for aName, NOT trimmed (a password / SQL body must keep
        // whatever the user typed).
        private static string GetRawParam(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues) && (vValues.Count > 0))
                return vValues[0] ?? "";
            if ((aForm != null) && aForm.TryGetValue(aName, out vValues) &&
                (vValues.Count > 0))
                return vValues[0] ?? "";
            return "";
        }

        private static int GetParamInt(HttpContext aCtx, IFormCollection aForm,
            string aName, int aDefault)
        {
            return TReportsDBPool.StrToIntDef(GetParam(aCtx, aForm, aName), aDefault);
        }

        // Every value for aName in order (query values, then form values). The
        // status MultiSelect posts one entry per selected option.
        private static string[] GetParamArray(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            List<string> vResult = new List<string>();
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues))
                for (int vI = 0; vI < vValues.Count; vI++)
                {
                    string vValue = (vValues[vI] ?? "").Trim();
                    if (vValue != "")
                        vResult.Add(vValue);
                }
            if ((aForm != null) && aForm.TryGetValue(aName, out vValues))
                for (int vI = 0; vI < vValues.Count; vI++)
                {
                    string vValue = (vValues[vI] ?? "").Trim();
                    if (vValue != "")
                        vResult.Add(vValue);
                }
            return vResult.ToArray();
        }

        // ----- cookies (native ASP.NET Core cookie API) ----- //

        private static string ReadCookie(HttpContext aCtx, string aName)
        {
            string vValue;
            if (aCtx.Request.Cookies.TryGetValue(aName, out vValue))
                return vValue ?? "";
            return "";
        }

        private static void WriteSessionCookie(HttpContext aCtx, string aToken)
        {
            if (string.IsNullOrEmpty(aToken))
                return;
            aCtx.Response.Cookies.Append(CS_REPORTS_SESSION, aToken, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static void ClearSessionCookie(HttpContext aCtx)
        {
            aCtx.Response.Cookies.Delete(CS_REPORTS_SESSION, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static string ReadThemeCookie(HttpContext aCtx)
        {
            string vS = ReadCookie(aCtx, CS_REPORTS_THEME_COOKIE).ToLowerInvariant();
            if ((vS == "light") || (vS == "dark") || (vS == "system"))
                return vS;
            return "system";
        }

        private static void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vS = (aTheme ?? "").Trim().ToLowerInvariant();
            if ((vS != "light") && (vS != "dark") && (vS != "system"))
                vS = "system";
            aCtx.Response.Cookies.Append(CS_REPORTS_THEME_COOKIE, vS, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(31536000),
                SameSite = SameSiteMode.Lax
            });
        }

        private static string ReadLangCookie(HttpContext aCtx)
        {
            return TReportsI18n.Normalize(ReadCookie(aCtx, CS_REPORTS_LANG_COOKIE));
        }

        private static void WriteLangCookie(HttpContext aCtx, string aLang)
        {
            aCtx.Response.Cookies.Append(CS_REPORTS_LANG_COOKIE,
                TReportsI18n.Normalize(aLang), new CookieOptions
                {
                    Path = "/",
                    MaxAge = TimeSpan.FromSeconds(31536000),
                    SameSite = SameSiteMode.Lax
                });
        }

        // Honor X-Forwarded-For (first IP) when present, else the connection IP.
        private static string ClientIPOf(HttpContext aCtx)
        {
            try
            {
                StringValues vXff;
                if (aCtx.Request.Headers.TryGetValue("X-Forwarded-For", out vXff) &&
                    (vXff.Count > 0))
                {
                    string[] vIPs = (vXff[0] ?? "").Split(',');
                    if (vIPs.Length > 0)
                    {
                        string vFirst = vIPs[0].Trim();
                        if (vFirst.Length > 0)
                            return vFirst;
                    }
                }
                System.Net.IPAddress vIP = aCtx.Connection.RemoteIpAddress;
                if (vIP == null)
                    return "";
                if (vIP.IsIPv4MappedToIPv6)
                    vIP = vIP.MapToIPv4();
                return vIP.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static string RefererOrRoot(HttpContext aCtx)
        {
            StringValues vRef;
            if (aCtx.Request.Headers.TryGetValue("Referer", out vRef) &&
                (vRef.Count > 0))
            {
                string vValue = vRef[0] ?? "";
                if (vValue != "")
                    return vValue;
            }
            return "/";
        }

        // ----- sessions ----- //

        public bool CurrentSession(HttpContext aCtx, out TReportsSession aSession)
        {
            aSession = null;
            string vToken = ReadCookie(aCtx, CS_REPORTS_SESSION);
            if (vToken == "")
                return false;
            return FSessions.TryGet(vToken, out aSession);
        }

        private TReportsPageCtx BuildCtx(HttpContext aCtx, TReportsSession aSession)
        {
            TReportsPageCtx vResult = new TReportsPageCtx();
            vResult.UserId = aSession.UserId;
            vResult.Role = aSession.Role;
            vResult.DisplayName = aSession.Username;
            TReportsUser oUser;
            if (FDB.GetUserById(aSession.UserId, out oUser) &&
                (oUser.DisplayName != ""))
                vResult.DisplayName = oUser.DisplayName;
            vResult.Theme = ReadThemeCookie(aCtx);
            vResult.Lang = ReadLangCookie(aCtx);
            return vResult;
        }

        // ----- response helpers (return an IResult) ----- //

        private static IResult Html(int aCode, string aHTML)
        {
            return Results.Content(aHTML, "text/html; charset=utf-8", null, aCode);
        }

        private static IResult Text(int aCode, string aText)
        {
            return Results.Content(aText, "text/plain; charset=utf-8", null, aCode);
        }

        private static IResult Redirect(string aLocation)
        {
            // 302, so a POST->redirect->GET drops the method + body (matches 60.HTML).
            return Results.Redirect(aLocation);
        }

        private static IResult Json(HttpContext aCtx, int aCode, string aJSON)
        {
            aCtx.Response.Headers["Cache-Control"] = "no-store";
            return Results.Content(aJSON, "application/json", null, aCode);
        }

        // Sanitize a filename so it cannot break out of the Content-Disposition
        // header value.
        private static string SanitizeHeaderFilename(string aName)
        {
            StringBuilder vBuf = new StringBuilder();
            string vName = aName ?? "";
            for (int vI = 0; vI < vName.Length; vI++)
            {
                char vCh = vName[vI];
                if (((vCh >= 'A') && (vCh <= 'Z')) || ((vCh >= 'a') && (vCh <= 'z')) ||
                    ((vCh >= '0') && (vCh <= '9')) || (vCh == '-') || (vCh == '_') ||
                    (vCh == '.') || (vCh == ' '))
                    vBuf.Append(vCh);
                else
                    vBuf.Append('-');
            }
            string vResult = vBuf.ToString().Trim();
            if (vResult == "")
                vResult = "download";
            return vResult;
        }

        private static IResult FileBytes(HttpContext aCtx, byte[] aBytes,
            string aContentType, string aFileName, bool aInline)
        {
            aCtx.Response.Headers["Content-Disposition"] =
                (aInline ? "inline" : "attachment") + "; filename=\"" +
                SanitizeHeaderFilename(aFileName) + "\"";
            return Results.Bytes(aBytes, aContentType);
        }

        private static async Task<string> ReadRawBodyAsync(HttpContext aCtx)
        {
            using (StreamReader vReader = new StreamReader(aCtx.Request.Body,
                Encoding.UTF8, false, 1024, true))
            {
                return await vReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        // ----- guards ----- //

        // Session-protected page endpoint: a signed-out visitor is redirected.
        public IResult Guarded(HttpContext aCtx, Func<TReportsSession, IResult> aFn)
        {
            TReportsSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            return aFn(vSession);
        }

        // Admin-role endpoint: the 60.HTML host answered a plain-text 403.
        public IResult GuardedRole(HttpContext aCtx, string aRole, string aMessage,
            Func<TReportsSession, IResult> aFn)
        {
            TReportsSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            if (!string.Equals(vSession.Role, aRole,
                StringComparison.OrdinalIgnoreCase))
                return Text(403, aMessage);
            return aFn(vSession);
        }

        // Everything except the viewer role (the schedule routes).
        public IResult GuardedNotViewer(HttpContext aCtx, string aMessage,
            Func<TReportsSession, IResult> aFn)
        {
            TReportsSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            if (string.Equals(vSession.Role, ReportsConst.CS_ROLE_VIEWER,
                StringComparison.OrdinalIgnoreCase))
                return Text(403, aMessage);
            return aFn(vSession);
        }

        // ==================================================================== //
        //  public routes                                                       //
        // ==================================================================== //

        public IResult Health()
        {
            return Text(200, "ok " + CS_REPORTS_SERVER_VERSION + " uptime=" +
                ((long)(DateTime.Now - FStartedAt).TotalSeconds).ToString(
                    CultureInfo.InvariantCulture) + "s");
        }

        public IResult Favicon()
        {
            return Results.Content(TReportsPages.CS_FAVICON_SVG, "image/svg+xml");
        }

        // pdf.js for the PDFViewer: served from assets\ when a copy is there and
        // redirected to the public build when it is not.
        public IResult PdfJs(string aFileName)
        {
            string vFile = Path.Combine(AppContext.BaseDirectory,
                Path.Combine("assets", aFileName));
            if (File.Exists(vFile))
                return Results.Bytes(File.ReadAllBytes(vFile),
                    "application/javascript");
            return Results.Redirect(CS_PDFJS_FALLBACK + aFileName);
        }

        public IResult LoginGet(HttpContext aCtx)
        {
            TReportsSession vSession;
            if (CurrentSession(aCtx, out vSession))
                return Redirect("/");
            return Html(200, FPages.BuildLoginPage(ReadThemeCookie(aCtx),
                ReadLangCookie(aCtx), "", "admin", "admin"));
        }

        public IResult LoginPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLangCookie(aCtx);
            string vUser = GetParam(aCtx, aForm, "username");
            string vPwd = GetRawParam(aCtx, aForm, "password");

            if ((vUser == "") || (vPwd == ""))
                return Html(400, FPages.BuildLoginPage(vTheme, vLang,
                    "Please enter your user name and password."));
            TReportsUser oUser;
            if (!FDB.AuthenticateUser(vUser, vPwd, out oUser))
                return Html(401, FPages.BuildLoginPage(vTheme, vLang,
                    "Invalid user name or password."));

            WriteSessionCookie(aCtx, FSessions.CreateSession(oUser, ClientIPOf(aCtx)));
            return Redirect("/");
        }

        public IResult Logout(HttpContext aCtx)
        {
            FSessions.Destroy_(ReadCookie(aCtx, CS_REPORTS_SESSION));
            ClearSessionCookie(aCtx);
            return Redirect("/login");
        }

        public IResult SetTheme(HttpContext aCtx, IFormCollection aForm)
        {
            WriteThemeCookie(aCtx, GetParam(aCtx, aForm, "theme"));
            return Redirect(RefererOrRoot(aCtx));
        }

        public IResult SetLanguage(HttpContext aCtx, IFormCollection aForm)
        {
            WriteLangCookie(aCtx, GetParam(aCtx, aForm, "lang"));
            return Redirect(RefererOrRoot(aCtx));
        }

        // WebAuthn. The crypto lives in TReportsPasskeys; this handler only moves
        // JSON in and out. The 60.HTML host routed the whole /webauthn/ prefix to
        // one handler, so the same action strings reach the same branches here.
        public async Task<IResult> WebAuthn(HttpContext aCtx, string aAction)
        {
            string vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
            TReportsSession vSession;
            bool vLoggedIn = CurrentSession(aCtx, out vSession);
            TReportsPasskeys oPasskeys = GetPasskeys(aCtx);

            try
            {
                if (string.Equals(aAction, "register/begin",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (!vLoggedIn)
                        return Json(aCtx, 401, "{\"error\":\"not signed in\"}");
                    TReportsUser oUser;
                    if (FDB.GetUserById(vSession.UserId, out oUser))
                        return Json(aCtx, 200, oPasskeys.BeginRegister(oUser.Id,
                            oUser.Username, oUser.DisplayName));
                    return Json(aCtx, 200, "{\"error\":\"unknown user\"}");
                }
                if (string.Equals(aAction, "register/finish",
                    StringComparison.OrdinalIgnoreCase))
                {
                    if (!vLoggedIn)
                        return Json(aCtx, 401, "{\"error\":\"not signed in\"}");
                    return Json(aCtx, 200, oPasskeys.FinishRegister(vSession.UserId,
                        vBody, "Browser passkey"));
                }
                if (string.Equals(aAction, "authenticate",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(aAction, "authenticate/begin",
                        StringComparison.OrdinalIgnoreCase))
                    return Json(aCtx, 200, oPasskeys.BeginLogin(""));
                if (string.Equals(aAction, "authenticate/finish",
                    StringComparison.OrdinalIgnoreCase))
                {
                    long vUserId;
                    oPasskeys.FinishLogin(vBody, out vUserId);
                    TReportsUser oUser;
                    if (FDB.GetUserById(vUserId, out oUser))
                    {
                        WriteSessionCookie(aCtx, FSessions.CreateSession(oUser,
                            ClientIPOf(aCtx)));
                        return Json(aCtx, 200, "{\"ok\":true,\"redirect\":\"/\"}");
                    }
                    return Json(aCtx, 401, "{\"error\":\"unknown credential\"}");
                }
                return Json(aCtx, 404, "{\"error\":\"unknown webauthn route\"}");
            }
            catch (Exception)
            {
                return Json(aCtx, 400, "{\"error\":\"webauthn failed\"}");
            }
        }

        // ==================================================================== //
        //  protected pages                                                     //
        // ==================================================================== //

        public IResult Home(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildHome(BuildCtx(aCtx, aSession)));
        }

        public IResult Dashboard(HttpContext aCtx, TReportsSession aSession,
            string aSlug)
        {
            return Html(200, FPages.BuildDashboard(BuildCtx(aCtx, aSession), aSlug));
        }

        public IResult SQLPage(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildSQLPage(BuildCtx(aCtx, aSession)));
        }

        public IResult JobsPage(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildJobs(BuildCtx(aCtx, aSession)));
        }

        // ----- reports ----- //

        public IResult ReportsGet(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildReportCatalogue(BuildCtx(aCtx, aSession),
                GetParam(aCtx, null, "category")));
        }

        public IResult ReportNew(HttpContext aCtx, TReportsSession aSession)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            TReportsReportDef vDef = new TReportsReportDef();
            vDef.Id = 0;
            vDef.CreatedAt = DateTime.Now;
            vDef.Name = "";
            vDef.Description = "";
            vDef.Category = "Sales";
            vDef.SqlText = "SELECT r.name AS region, " +
                "ROUND(SUM(ol.line_total), 2) AS revenue" + "\r\n" +
                "FROM order_lines ol" + "\r\n" +
                "JOIN orders o ON o.id = ol.order_id" + "\r\n" +
                "JOIN customers c ON c.id = o.customer_id" + "\r\n" +
                "JOIN regions r ON r.id = c.region_id" + "\r\n" +
                "WHERE o.order_date >= :dfrom AND o.order_date <= :dto" + "\r\n" +
                "GROUP BY r.name ORDER BY revenue DESC";
            vDef.ParamsJSON = "{\"params\":[" +
                "{\"name\":\"dfrom\",\"caption\":\"From date\",\"kind\":\"date\"," +
                "\"default\":\"-365\"}," +
                "{\"name\":\"dto\",\"caption\":\"To date\",\"kind\":\"date\"," +
                "\"default\":\"0\"}]}";
            vDef.ChartKind = "bar";
            vDef.OwnerId = vCtx.UserId;
            vDef.Shared = true;
            return Html(200, FPages.BuildReportEditor(vCtx, vDef, ""));
        }

        public IResult ReportSave(HttpContext aCtx, IFormCollection aForm,
            TReportsSession aSession)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            TReportsReportDef vDef = new TReportsReportDef();
            vDef.Id = TReportsDBPool.StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            vDef.Name = GetParam(aCtx, aForm, "name");
            vDef.Description = GetParam(aCtx, aForm, "description");
            vDef.Category = GetParam(aCtx, aForm, "category");
            vDef.SqlText = GetRawParam(aCtx, aForm, "sql_text");
            vDef.ParamsJSON = GetRawParam(aCtx, aForm, "params_json");
            vDef.ChartKind = GetParam(aCtx, aForm, "chart_kind");
            vDef.OwnerId = vCtx.UserId;
            vDef.Shared = GetParam(aCtx, aForm, "shared") != "";

            long vId;
            try
            {
                vId = FDB.SaveReportDef(vDef.Id, vDef.Name, vDef.Description,
                    vDef.Category, vDef.SqlText, vDef.ParamsJSON, vDef.ChartKind,
                    vCtx.UserId, vDef.Shared);
            }
            catch (Exception E)
            {
                // Re-render the editor with the rejection reason instead of a bare
                // 400, so an administrator can see exactly which rule the statement
                // broke.
                return Html(400, FPages.BuildReportEditor(vCtx, vDef, E.Message));
            }
            return Redirect("/reports/" + vId.ToString(CultureInfo.InvariantCulture));
        }

        public IResult ReportDelete(HttpContext aCtx, IFormCollection aForm)
        {
            FDB.DeleteReportDef(TReportsDBPool.StrToInt64Def(
                GetParam(aCtx, aForm, "id"), 0));
            return Redirect("/reports");
        }

        // Collect the values a report's declared parameters were given, falling back
        // to each declaration's own default.
        private static List<TReportsParamValue> CollectParamValues(HttpContext aCtx,
            IFormCollection aForm, TReportsReportDef aDef)
        {
            List<TReportsParamDef> vDefs = TReportsDBPool.ParseParamDefs(
                aDef.ParamsJSON);
            List<TReportsParamValue> vResult = new List<TReportsParamValue>();
            for (int vI = 0; vI < vDefs.Count; vI++)
            {
                string vValue = GetParam(aCtx, aForm, vDefs[vI].Name);
                if (vValue == "")
                {
                    int vOffset;
                    if ((vDefs[vI].Kind == "date") &&
                        int.TryParse((vDefs[vI].DefaultValue ?? "").Trim(),
                            NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out vOffset))
                        vValue = ReportsDBHelpers.FormatReportsDate(
                            DateTime.Today.AddDays(vOffset));
                    else
                        vValue = vDefs[vI].DefaultValue;
                }
                TReportsParamValue vItem = new TReportsParamValue();
                vItem.Name = vDefs[vI].Name;
                vItem.Kind = vDefs[vI].Kind;
                vItem.Value = vValue;
                vResult.Add(vItem);
            }
            return vResult;
        }

        // GET /reports/{id} - the parameter form.
        public IResult ReportForm(HttpContext aCtx, TReportsSession aSession,
            string aIdStr)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            TReportsReportDef vDef;
            IResult vFail = ResolveReport(aCtx, vCtx, aIdStr, out vDef);
            if (vFail != null)
                return vFail;
            return Html(200, FPages.BuildReportForm(vCtx, vDef,
                CollectParamValues(aCtx, null, vDef)));
        }

        // POST /reports/{id}/run - the htmx results fragment.
        public IResult ReportRun(HttpContext aCtx, IFormCollection aForm,
            TReportsSession aSession, string aIdStr)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            TReportsReportDef vDef;
            IResult vFail = ResolveReport(aCtx, vCtx, aIdStr, out vDef);
            if (vFail != null)
                return vFail;
            List<TReportsParamValue> vValues = CollectParamValues(aCtx, aForm, vDef);
            int vRows;
            int vMS;
            string vHTML = FPages.BuildReportResultFragment(vCtx, vDef, vValues,
                out vRows, out vMS);
            // The run history is what makes the throughput visible rather than
            // merely claimed.
            FDB.LogRun(vDef.Id, vCtx.UserId,
                TReportsDBPool.EncodeParamValues(vValues), vRows, vMS, "ok");
            return Html(200, vHTML);
        }

        public IResult ReportPreview(HttpContext aCtx, TReportsSession aSession,
            string aIdStr)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            TReportsReportDef vDef;
            IResult vFail = ResolveReport(aCtx, vCtx, aIdStr, out vDef);
            if (vFail != null)
                return vFail;
            return Html(200, FPages.BuildReportPreview(vCtx, vDef,
                CollectParamValues(aCtx, null, vDef)));
        }

        // GET /reports/{id}/run.{pdf|xlsx|csv}. The raw-row exports are analyst /
        // admin only: a viewer may read a report and its PDF, but not carry the
        // rows away.
        public IResult ReportExport(HttpContext aCtx, TReportsSession aSession,
            string aIdStr, string aFormat)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            TReportsReportDef vDef;
            IResult vFail = ResolveReport(aCtx, vCtx, aIdStr, out vDef);
            if (vFail != null)
                return vFail;

            bool vIsPDF = string.Equals(aFormat, "pdf",
                StringComparison.OrdinalIgnoreCase);
            if (!vIsPDF && !RoleCanExport(vCtx.Role))
                return Text(403, "The viewer role cannot export raw rows.");

            List<TReportsParamValue> vValues = CollectParamValues(aCtx, null, vDef);
            byte[] vBytes;
            try
            {
                using (MemoryStream oStream = new MemoryStream())
                {
                    if (vIsPDF)
                        FPages.ExportReportPDF(vDef, vValues, oStream);
                    else if (string.Equals(aFormat, "xlsx",
                        StringComparison.OrdinalIgnoreCase))
                        FPages.ExportReportXLSX(vDef, vValues, oStream);
                    else
                        FPages.ExportReportCSV(vDef, vValues, oStream);
                    vBytes = oStream.ToArray();
                }
            }
            catch (Exception E)
            {
                return Text(400, "Export failed: " + E.Message);
            }

            if (vIsPDF)
                return FileBytes(aCtx, vBytes, "application/pdf",
                    vDef.Name + ".pdf", true);
            if (string.Equals(aFormat, "xlsx", StringComparison.OrdinalIgnoreCase))
                return FileBytes(aCtx, vBytes, "application/vnd.openxmlformats-" +
                    "officedocument.spreadsheetml.sheet", vDef.Name + ".xlsx", false);
            return FileBytes(aCtx, vBytes, "text/csv; charset=utf-8",
                vDef.Name + ".csv", false);
        }

        // Shared id resolution: a non-numeric id redirects to the catalogue, an
        // unknown id renders the 404 page (mirrors HandleReportRoute).
        private IResult ResolveReport(HttpContext aCtx, TReportsPageCtx aPageCtx,
            string aIdStr, out TReportsReportDef aDef)
        {
            aDef = null;
            long vId;
            if (!long.TryParse(aIdStr, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vId) || (vId <= 0))
                return Redirect("/reports");
            if (!FDB.GetReportDef(vId, out aDef))
                return Html(404, FPages.BuildNotFoundPage(aPageCtx.Theme,
                    aPageCtx.Lang));
            return null;
        }

        private static bool RoleCanExport(string aRole)
        {
            return string.Equals(aRole, ReportsConst.CS_ROLE_ADMIN,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aRole, ReportsConst.CS_ROLE_ANALYST,
                    StringComparison.OrdinalIgnoreCase);
        }

        // ----- pivot ----- //

        // Read the six cross-tab keys, each defaulted so a bare GET still renders.
        private static void ReadPivotParams(HttpContext aCtx, IFormCollection aForm,
            out string aRowField, out string aColField, out string aMeasure,
            out string aAgg, out string aFrom, out string aTo)
        {
            aRowField = GetParam(aCtx, aForm, "rowfield");
            aColField = GetParam(aCtx, aForm, "colfield");
            aMeasure = GetParam(aCtx, aForm, "measure");
            aAgg = GetParam(aCtx, aForm, "agg");
            aFrom = GetParam(aCtx, aForm, "dfrom");
            aTo = GetParam(aCtx, aForm, "dto");
            if (aRowField == "")
                aRowField = "region";
            if (aColField == "")
                aColField = "year";
            if (aMeasure == "")
                aMeasure = "revenue";
            if (aAgg == "")
                aAgg = "sum";
            if (aFrom == "")
                aFrom = ReportsDBHelpers.FormatReportsDate(
                    DateTime.Today.AddDays(-730));
            if (aTo == "")
                aTo = ReportsDBHelpers.FormatReportsDate(DateTime.Today);
        }

        public IResult Pivot(HttpContext aCtx, TReportsSession aSession)
        {
            string vRow, vCol, vMeasure, vAgg, vFrom, vTo;
            ReadPivotParams(aCtx, null, out vRow, out vCol, out vMeasure, out vAgg,
                out vFrom, out vTo);
            return Html(200, FPages.BuildPivot(BuildCtx(aCtx, aSession), vRow, vCol,
                vMeasure, vAgg, vFrom, vTo));
        }

        public IResult PivotBuild(HttpContext aCtx, IFormCollection aForm)
        {
            string vRow, vCol, vMeasure, vAgg, vFrom, vTo;
            ReadPivotParams(aCtx, aForm, out vRow, out vCol, out vMeasure, out vAgg,
                out vFrom, out vTo);
            int vMS;
            return Html(200, FPages.BuildPivotFragment(vRow, vCol, vMeasure, vAgg,
                vFrom, vTo, out vMS));
        }

        public IResult PivotExport(HttpContext aCtx, TReportsSession aSession,
            bool aPDF)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            if (!RoleCanExport(vCtx.Role))
                return Text(403, "The viewer role cannot export raw rows.");
            string vRow, vCol, vMeasure, vAgg, vFrom, vTo;
            ReadPivotParams(aCtx, null, out vRow, out vCol, out vMeasure, out vAgg,
                out vFrom, out vTo);
            byte[] vBytes;
            try
            {
                using (MemoryStream oStream = new MemoryStream())
                {
                    if (aPDF)
                        FPages.ExportPivotPDF(vRow, vCol, vMeasure, vAgg, vFrom, vTo,
                            oStream);
                    else
                        FPages.ExportPivotXLSX(vRow, vCol, vMeasure, vAgg, vFrom, vTo,
                            oStream);
                    vBytes = oStream.ToArray();
                }
            }
            catch (Exception E)
            {
                return Text(400, "Export failed: " + E.Message);
            }
            if (aPDF)
                return FileBytes(aCtx, vBytes, "application/pdf", "cross-tab.pdf",
                    true);
            return FileBytes(aCtx, vBytes, "application/vnd.openxmlformats-" +
                "officedocument.spreadsheetml.sheet", "cross-tab.xlsx", false);
        }

        // ----- analytics ----- //

        // The Analytics section (sgcReports_Analytics, reused verbatim). One
        // TReportsAnalytics per request, mirror of the 60.HTML HandleAnalytics:
        // aDoc is the route, aForm carries the field chooser / drill-through post.
        public IResult Analytics(HttpContext aCtx, IFormCollection aForm,
            TReportsSession aSession, string aDoc)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            string vGrp = GetParam(aCtx, aForm, "grp");
            string vLR = GetParam(aCtx, aForm, "lr");
            string vLC = GetParam(aCtx, aForm, "lc");
            string vLV = GetParam(aCtx, aForm, "lv");
            TReportsAnalytics oA = new TReportsAnalytics(FDB, FPages);
            if (aDoc == "/analytics")
                return Html(200, oA.BuildCharts(vCtx));
            if (aDoc == "/analytics/charts/region")
                return Html(200, oA.BuildRegionDetail(vCtx,
                    GetParam(aCtx, aForm, "label")));
            if (aDoc == "/analytics/market")
                return Html(200, oA.BuildMarket(vCtx, GetParam(aCtx, aForm, "style")));
            if (aDoc == "/analytics/pivot")
                return Html(200, oA.BuildPivotLab(vCtx, vGrp));
            if (aDoc == "/analytics/pivot/layout")
                // field chooser: rows / cols / values posted by the pivot script
                return Html(200, oA.PivotLayoutFragment(vCtx, vGrp,
                    GetParam(aCtx, aForm, "rows"), GetParam(aCtx, aForm, "cols"),
                    GetParam(aCtx, aForm, "values")));
            if (aDoc == "/analytics/pivot/drill")
                // drill-through: rows / cols are the JSON paths of the clicked cell
                return Html(200, oA.PivotDrillFragment(vCtx, vGrp, vLR, vLC, vLV,
                    GetParam(aCtx, aForm, "rows"), GetParam(aCtx, aForm, "cols")));
            if (aDoc == "/analytics/pivot/export.xlsx")
            {
                if (!RoleCanExport(vCtx.Role))
                    return Text(403, "The viewer role cannot export.");
                byte[] vBytes;
                using (MemoryStream oStream = new MemoryStream())
                {
                    oA.PivotXLSX(vGrp, vLR, vLC, vLV, oStream);
                    vBytes = oStream.ToArray();
                }
                return FileBytes(aCtx, vBytes, "application/vnd.openxmlformats-" +
                    "officedocument.spreadsheetml.sheet", "revenue-pivot.xlsx", false);
            }
            return NotFound(aCtx);
        }

        // One live chart point plus one candle tick, broadcast by
        // ReportsPushService about every 2 s (mirror of the 60.HTML push thread).
        public string AnalyticsLiveFragment()
        {
            TReportsAnalytics oA = new TReportsAnalytics(FDB, FPages);
            return oA.LiveChartFragment() + oA.LiveCandleFragment();
        }

        // ----- explore ----- //

        private static TReportsExploreFilter BuildExploreFilter(HttpContext aCtx,
            IFormCollection aForm)
        {
            TReportsExploreFilter vResult = new TReportsExploreFilter();
            vResult.Search = GetParam(aCtx, aForm, "search");
            vResult.Region = GetParam(aCtx, aForm, "region");
            vResult.Segment = GetParam(aCtx, aForm, "segment");
            vResult.Category = GetParam(aCtx, aForm, "category");
            vResult.Statuses = GetParamArray(aCtx, aForm, "status");
            vResult.DateFrom = GetParam(aCtx, aForm, "dfrom");
            vResult.DateTo = GetParam(aCtx, aForm, "dto");
            // The RangeSlider posts its two ends. A band that spans the whole scale
            // is no filter at all, so it is dropped rather than pushed into the
            // query.
            int vLow = GetParamInt(aCtx, aForm, "qtylow", 0);
            int vHigh = GetParamInt(aCtx, aForm, "qtyhigh", 0);
            if (vLow <= 1)
                vLow = 0;
            if (vHigh >= 20)
                vHigh = 0;
            vResult.MinQty = vLow;
            vResult.MaxQty = vHigh;
            vResult.Sort = GetParam(aCtx, aForm, "sort");
            if (vResult.Sort == "")
                vResult.Sort = "date";
            vResult.Dir = GetParam(aCtx, aForm, "dir");
            if (vResult.Dir == "")
                vResult.Dir = "desc";
            // Defensive: the DB layer whitelists both again before ORDER BY.
            return vResult;
        }

        public IResult Explore(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildExplore(BuildCtx(aCtx, aSession),
                BuildExploreFilter(aCtx, null), GetParamInt(aCtx, null, "page", 1),
                GetParamInt(aCtx, null, "size", 50),
                GetParam(aCtx, null, "virtual") == "1"));
        }

        public IResult ExploreRows(HttpContext aCtx)
        {
            // The limit arrives in the URL the Grid's own sentinel wrote, so it is
            // clamped here rather than trusted.
            int vLimit = GetParamInt(aCtx, null, "limit", 50);
            if (vLimit <= 0)
                vLimit = 50;
            if (vLimit > 500)
                vLimit = 500;
            return Html(200, FPages.BuildExploreRowsFragment(
                BuildExploreFilter(aCtx, null), GetParamInt(aCtx, null, "offset", 0),
                vLimit, GetParam(aCtx, null, "rootId")));
        }

        public IResult ExploreExport(HttpContext aCtx, TReportsSession aSession,
            bool aPDF)
        {
            TReportsPageCtx vCtx = BuildCtx(aCtx, aSession);
            if (!RoleCanExport(vCtx.Role))
                return Text(403, "The viewer role cannot export raw rows.");
            TReportsExploreFilter vFilter = BuildExploreFilter(aCtx, null);
            byte[] vBytes;
            try
            {
                using (MemoryStream oStream = new MemoryStream())
                {
                    if (aPDF)
                        FPages.ExportExplorePDF(vFilter, oStream);
                    else
                        FPages.ExportExploreXLSX(vFilter, oStream);
                    vBytes = oStream.ToArray();
                }
            }
            catch (Exception E)
            {
                return Text(400, "Export failed: " + E.Message);
            }
            if (aPDF)
                return FileBytes(aCtx, vBytes, "application/pdf", "order-lines.pdf",
                    true);
            return FileBytes(aCtx, vBytes, "application/vnd.openxmlformats-" +
                "officedocument.spreadsheetml.sheet", "order-lines.xlsx", false);
        }

        // ----- drilldown ----- //

        public IResult Drilldown(HttpContext aCtx, TReportsSession aSession,
            string aDim, string aIdStr)
        {
            string vDim = (aDim ?? "").ToLowerInvariant();
            long vId;
            if (!long.TryParse(aIdStr, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vId) || (vId <= 0))
                return Redirect("/");
            // The dimension picks one of four fixed statements; it is never
            // interpolated into SQL.
            if ((vDim != "region") && (vDim != "category") &&
                (vDim != "salesperson") && (vDim != "customer"))
                return Redirect("/");
            return Html(200, FPages.BuildDrilldown(BuildCtx(aCtx, aSession), vDim,
                vId));
        }

        // ----- saved views ----- //

        public IResult Views(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildViews(BuildCtx(aCtx, aSession)));
        }

        public IResult ViewsSave(HttpContext aCtx, IFormCollection aForm,
            TReportsSession aSession)
        {
            // The owner is always the session's user: an id from the request would
            // be an easy way to write into someone else's list.
            FDB.SaveSavedView(aSession.UserId,
                TReportsDBPool.StrToInt64Def(GetParam(aCtx, aForm, "report_id"), 0),
                GetParam(aCtx, aForm, "name"),
                GetRawParam(aCtx, aForm, "state_json"));
            return Redirect("/views");
        }

        public IResult ViewsDelete(HttpContext aCtx, IFormCollection aForm,
            TReportsSession aSession)
        {
            // Scoped by user id in the DELETE itself, so another user's view id
            // deletes nothing.
            FDB.DeleteSavedView(TReportsDBPool.StrToInt64Def(
                GetParam(aCtx, aForm, "id"), 0), aSession.UserId);
            return Redirect("/views");
        }

        // ----- schedules ----- //

        public IResult Schedules(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildSchedules(BuildCtx(aCtx, aSession),
                GetParam(aCtx, null, "flash") == "saved"
                    ? "The schedule was saved." : ""));
        }

        public IResult ScheduleSave(HttpContext aCtx, IFormCollection aForm)
        {
            FDB.SaveSchedule(
                TReportsDBPool.StrToInt64Def(GetParam(aCtx, aForm, "id"), 0),
                TReportsDBPool.StrToInt64Def(GetParam(aCtx, aForm, "report_id"), 0),
                GetParam(aCtx, aForm, "cron_text"), GetParam(aCtx, aForm, "format"),
                GetParam(aCtx, aForm, "recipients"),
                GetParam(aCtx, aForm, "active") != "");
            return Redirect("/schedules?flash=saved");
        }

        public IResult ScheduleRunNow(HttpContext aCtx, IFormCollection aForm)
        {
            long vId = TReportsDBPool.StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            TReportsSchedule vRow;
            if ((vId <= 0) || !FDB.GetSchedule(vId, out vRow))
                return Redirect("/schedules");
            FDB.TouchSchedule(vId);
            string vFragment = TReportsPages.LiveStartJob("sched" +
                vId.ToString(CultureInfo.InvariantCulture),
                vRow.ReportName + " (" + vRow.Format.ToUpperInvariant() + ")",
                "to " + vRow.Recipients);
            PushFragment(vFragment);
            return Redirect("/jobs");
        }

        // Broadcast an out-of-band fragment to every connected browser. Under
        // Kestrel the adapter's hub replaces the 60.HTML engine broadcast.
        public void PushFragment(string aHTML)
        {
            if (string.IsNullOrEmpty(aHTML) || (FHub == null))
                return;
            try
            {
                // the hub bounds every send (SendTimeout), so a client that
                // stopped reading cannot hang this request
                FHub.BroadcastAsync(aHTML).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        // ----- users (admin) ----- //

        public IResult Users(HttpContext aCtx, TReportsSession aSession)
        {
            return Html(200, FPages.BuildUsers(BuildCtx(aCtx, aSession), ""));
        }

        public IResult UsersSave(HttpContext aCtx, IFormCollection aForm,
            TReportsSession aSession)
        {
            string vPassword = GetRawParam(aCtx, aForm, "password");
            string vHash = "";
            if (vPassword.Trim() != "")
                vHash = Bcrypt.BcryptHash(vPassword);

            long vId = FDB.SaveUser(
                TReportsDBPool.StrToInt64Def(GetParam(aCtx, aForm, "id"), 0),
                GetParam(aCtx, aForm, "username"),
                GetParam(aCtx, aForm, "display_name"),
                GetParam(aCtx, aForm, "role"), vHash);
            if (vId == 0)
                return Html(400, FPages.BuildUsers(BuildCtx(aCtx, aSession),
                    "That user name is already taken, or the form was incomplete."));
            return Redirect("/users");
        }

        // ----- 404 (the protected catch-all) ----- //

        public IResult NotFound(HttpContext aCtx)
        {
            return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx),
                ReadLangCookie(aCtx)));
        }
    }
}
