// ***************************************************************************
//  sgcWMSWeb - warehouse management demo on ASP.NET Core
//  Mirror of demos\60.HTML\13.Warehouse (TsgcWebSocketHTTPServer host).
//
//  This is the REWRITTEN host layer, following the 01.ERP / 02.AdminCRUD host
//  pattern. It reproduces every branch of the 60.HTML sgcWMS_Server.cs
//  DispatchRequest, but on Kestrel:
//    - The reusable logic (Bcrypt, Config, DB, Handheld, Multipart, Pages,
//      Passkeys, Reports, Sessions, Types) is copied VERBATIM from the 60.HTML
//      demo and is NOT changed here.
//    - The 60.HTML host scanned RawHeaders for the Cookie line and wrote
//      Set-Cookie / redirects through CustomHeaders. Here every cookie is
//      read/written through the native ASP.NET Core cookie API
//      (ctx.Request.Cookies / ctx.Response.Cookies) with the SAME cookie names +
//      attributes, and each handler returns an IResult.
//    - Query + form params are merged through GetParam, matching the Delphi
//      ARequestInfo.Params (query first, then the urlencoded body) - and, like
//      the Delphi TStringList.Values, the name match is case-insensitive.
//    - The multipart PARSER is not needed: the framework's IFormFile gives the
//      uploaded photos directly, which are handed to the SAME reused
//      TWMSAttachStore through TWMSMultipartField values, so the storage
//      semantics (data\photos\<id>\, the extension block-list, the size / count
//      caps) stay byte-identical to the 60.HTML demo.
//    - PASSKEYS: the 60.HTML host hard-coded RP id 'localhost' + origin
//      http://localhost:5706. Under Kestrel the RP id (request host name) and
//      origin (scheme://host) are DERIVED from the live request and threaded
//      into the reused TWMSPasskeys. A per-origin instance is cached so the
//      WebAuthn begin/finish challenge state survives across the two requests of
//      a ceremony (see GetPasskeys).
//
//  The two host files that were NOT copied from 60.HTML are sgcWMS_Server.cs
//  (the TsgcWebSocketHTTPServer host) and the console Program.cs.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace WMS
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, handheld builder, report builder, photo store, passkey factory)
    // and exposes one IResult-returning method per 60.HTML DispatchRequest
    // branch. Program.cs maps the Minimal API endpoints onto these methods and
    // applies the auth / role gates through Guarded*.
    public sealed class WMSWebHost : IDisposable
    {
        public const string CS_WMS_SERVER_VERSION = "1.0.0";

        // Cookie names - identical to the 60.HTML host so behavior matches exactly.
        public const string CS_WMS_SESSION = "wms_session";
        public const string CS_WMS_THEME_COOKIE = "wms_theme";

        // Self-contained favicon: the amber warehouse mark, served at /favicon.svg.
        public const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"sgcWMS\">" +
            "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"#F59E0B\"/>" +
            "<path d=\"M14 36 L32 22 L50 36 L50 52 L14 52 Z\" fill=\"none\" " +
            "stroke=\"#FFFFFF\" stroke-width=\"5\" stroke-linejoin=\"round\"/>" +
            "<rect x=\"26\" y=\"40\" width=\"12\" height=\"12\" fill=\"#FFFFFF\"/></svg>";

        private readonly TWMSServerConfig FConfig;
        private readonly TWMSDBPool FDB;
        private readonly TWMSSessionStore FSessions;
        private readonly TWMSPages FPages;
        private readonly TWMSHandheld FHandheld;
        private readonly TWMSReports FReports;
        private readonly TWMSAttachStore FPhotos;
        private readonly int FListenPort;
        private readonly DateTime FStartedAt;

        // Per-origin passkey cache. WebAuthn begin/finish share challenge state in
        // one TWMSPasskeys instance, so a ceremony's two requests (same browser,
        // same origin) must hit the SAME instance. Keyed by "rpId|origin".
        private readonly Dictionary<string, TWMSPasskeys> FPasskeys =
            new Dictionary<string, TWMSPasskeys>(StringComparer.Ordinal);
        private readonly object FPasskeysLock = new object();

        // Builds the DB (schema + admin seed + demo data), the session store and
        // the view builders - mirroring TWMSServer.InitRuntime.
        public WMSWebHost(TWMSServerConfig aConfig, string aDatabasePath, int aListenPort)
        {
            FConfig = aConfig;
            FListenPort = aListenPort;
            FStartedAt = DateTime.Now;

            FDB = new TWMSDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedAdmin(aConfig.AdminUser, Bcrypt.BcryptHash(aConfig.AdminPassword));
            FDB.SeedDemoData();

            // Two more demo accounts so the three roles can be tried straight away.
            string vHash = Bcrypt.BcryptHash("demo1234");
            if (!FDB.UsernameExists("supervisor"))
                FDB.RegisterUser("supervisor", vHash, WMSConst.CS_ROLE_SUPERVISOR,
                    "Sam Pereira");
            if (!FDB.UsernameExists("operator"))
                FDB.RegisterUser("operator", vHash, WMSConst.CS_ROLE_OPERATOR,
                    "Ola Nilsen");

            FSessions = new TWMSSessionStore(480, true);
            FPages = new TWMSPages(FDB);
            FHandheld = new TWMSHandheld(FDB);
            FReports = new TWMSReports(FDB);
            FPhotos = new TWMSAttachStore();
        }

        public void Dispose()
        {
            lock (FPasskeysLock)
            {
                foreach (KeyValuePair<string, TWMSPasskeys> vPair in FPasskeys)
                {
                    try { vPair.Value.Dispose(); } catch { }
                }
                FPasskeys.Clear();
            }
            if (FDB != null)
            {
                try { FDB.Dispose(); } catch { }
            }
        }

        public int ListenPort
        {
            get { return FListenPort; }
        }

        public DateTime StartedAt
        {
            get { return FStartedAt; }
        }

        // ----- passkey factory (request-derived rpId + origin) ----- //

        // Returns the TWMSPasskeys for THIS request's origin, creating + caching it
        // on first use. RP id = the request host name (no port); origin =
        // scheme://host (with port). This threads the live request values into the
        // reused passkey engine instead of the 60.HTML hard-coded localhost.
        private TWMSPasskeys GetPasskeys(HttpContext aCtx)
        {
            string vRPID = aCtx.Request.Host.Host;
            if (string.IsNullOrEmpty(vRPID))
                vRPID = "localhost";
            string vOrigin = aCtx.Request.Scheme + "://" + aCtx.Request.Host.Value;
            string vKey = vRPID + "|" + vOrigin;

            lock (FPasskeysLock)
            {
                TWMSPasskeys vPk;
                if (!FPasskeys.TryGetValue(vKey, out vPk))
                {
                    vPk = new TWMSPasskeys(FDB, vRPID, "sgcWMS Warehouse", vOrigin);
                    FPasskeys[vKey] = vPk;
                }
                return vPk;
            }
        }

        // ----- request param helpers (query + form merged) ----- //

        // First value for aName: query wins over form, matching the Delphi merged
        // ARequestInfo.Params. aForm is null for GET / non-form requests. The name
        // match is case-insensitive, like Delphi's TStringList.Values.
        private static string GetParam(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues) && vValues.Count > 0)
                return vValues[0] ?? "";
            foreach (KeyValuePair<string, StringValues> vPair in aCtx.Request.Query)
                if (string.Equals(vPair.Key, aName, StringComparison.OrdinalIgnoreCase) &&
                    vPair.Value.Count > 0)
                    return vPair.Value[0] ?? "";
            if (aForm != null)
            {
                if (aForm.TryGetValue(aName, out vValues) && vValues.Count > 0)
                    return vValues[0] ?? "";
                foreach (KeyValuePair<string, StringValues> vPair in aForm)
                    if (string.Equals(vPair.Key, aName,
                        StringComparison.OrdinalIgnoreCase) && vPair.Value.Count > 0)
                        return vPair.Value[0] ?? "";
            }
            return "";
        }

        private static long GetParamInt(HttpContext aCtx, IFormCollection aForm,
            string aName, long aDefault = 0)
        {
            return StrToInt64Def(GetParam(aCtx, aForm, aName).Trim(), aDefault);
        }

        // ----- numeric parse helpers (verbatim from 60.HTML) ----- //

        private static long StrToInt64Def(string aValue, long aDefault)
        {
            long vResult;
            if (long.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        private static int StrToIntDef(string aValue, int aDefault)
        {
            int vResult;
            if (int.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        // The Delphi reads a posted decimal with TFormatSettings.Invariant after
        // swapping ',' for '.', so a European keyboard entry still parses.
        private static double StrToFloatDef(string aValue, double aDefault)
        {
            string vText = (aValue ?? "").Trim().Replace(",", ".");
            if (vText.Length == 0)
                return aDefault;
            double vResult;
            if (double.TryParse(vText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out vResult))
                return vResult;
            return aDefault;
        }

        private static string Str(long aValue)
        {
            return aValue.ToString(CultureInfo.InvariantCulture);
        }

        private static string Str(int aValue)
        {
            return aValue.ToString(CultureInfo.InvariantCulture);
        }

        // ----- cookies (native ASP.NET Core cookie API) ----- //

        private static string ReadCookie(HttpContext aCtx, string aName)
        {
            string vValue;
            if (aCtx.Request.Cookies.TryGetValue(aName, out vValue))
                return vValue ?? "";
            return "";
        }

        private void WriteSessionCookie(HttpContext aCtx, string aToken)
        {
            if (string.IsNullOrEmpty(aToken))
                return;
            aCtx.Response.Cookies.Append(CS_WMS_SESSION, aToken, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private void ClearSessionCookie(HttpContext aCtx)
        {
            aCtx.Response.Cookies.Delete(CS_WMS_SESSION, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static string ReadThemeCookie(HttpContext aCtx)
        {
            string vS = ReadCookie(aCtx, CS_WMS_THEME_COOKIE).ToLowerInvariant();
            if (vS == "light" || vS == "dark" || vS == "system")
                return vS;
            // The warehouse demo follows the OS preference by default.
            return "system";
        }

        private void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vS = (aTheme ?? "").Trim().ToLowerInvariant();
            if (vS != "light" && vS != "dark" && vS != "system")
                vS = "system";
            aCtx.Response.Cookies.Append(CS_WMS_THEME_COOKIE, vS, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(31536000),
                SameSite = SameSiteMode.Lax
            });
        }

        // The Delphi read the peer IP off the Indy binding; Kestrel exposes the
        // connection IP (honouring X-Forwarded-For when a proxy set it).
        private static string ClientIPOf(HttpContext aCtx)
        {
            try
            {
                StringValues vXff;
                if (aCtx.Request.Headers.TryGetValue("X-Forwarded-For", out vXff) &&
                    vXff.Count > 0)
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
                // Normalize an IPv4-mapped IPv6 loopback to plain 127.0.0.1.
                if (vIP.IsIPv4MappedToIPv6)
                    vIP = vIP.MapToIPv4();
                return vIP.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string RefererOrRoot(HttpContext aCtx)
        {
            StringValues vRef;
            if (aCtx.Request.Headers.TryGetValue("Referer", out vRef) && vRef.Count > 0)
            {
                string vValue = vRef[0] ?? "";
                if (vValue != "")
                    return vValue;
            }
            return "/";
        }

        private static bool IsHtmx(HttpContext aCtx)
        {
            StringValues vValues;
            if (aCtx.Request.Headers.TryGetValue("HX-Request", out vValues) &&
                vValues.Count > 0)
                return string.Equals(vValues[0], "true",
                    StringComparison.OrdinalIgnoreCase);
            return false;
        }

        // ----- sessions ----- //

        // True + session when the request carries a valid session cookie.
        public bool CurrentSession(HttpContext aCtx, out TWMSSession aSession)
        {
            aSession = null;
            string vToken = ReadCookie(aCtx, CS_WMS_SESSION);
            if (vToken == "")
                return false;
            return FSessions.TryGet(vToken, out aSession);
        }

        // ----- response helpers (return an IResult) ----- //

        private static IResult Html(int aCode, string aHTML)
        {
            return Results.Content(aHTML, "text/html; charset=utf-8", null, aCode);
        }

        // htmx fragments: no shell, never cached.
        private static IResult Fragment(HttpContext aCtx, string aHTML)
        {
            aCtx.Response.Headers["Cache-Control"] = "no-store";
            return Results.Content(aHTML, "text/html; charset=utf-8", null, 200);
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

        private static IResult JsonError(HttpContext aCtx, int aCode, string aMessage)
        {
            return Json(aCtx, aCode, "{\"ok\":false,\"error\":\"" +
                JsonEscape(aMessage) + "\"}");
        }

        private static string JsonEscape(string aValue)
        {
            if (string.IsNullOrEmpty(aValue))
                return "";
            StringBuilder vBuf = new StringBuilder(aValue.Length);
            for (int vI = 0; vI < aValue.Length; vI++)
            {
                char vCh = aValue[vI];
                switch (vCh)
                {
                    case '\\': vBuf.Append("\\\\"); break;
                    case '"': vBuf.Append("\\\""); break;
                    case '\b': vBuf.Append("\\b"); break;
                    case '\t': vBuf.Append("\\t"); break;
                    case '\n': vBuf.Append("\\n"); break;
                    case '\f': vBuf.Append("\\f"); break;
                    case '\r': vBuf.Append("\\r"); break;
                    default:
                        if (vCh < 32)
                            vBuf.Append("\\u").Append(((int)vCh).ToString("x4",
                                CultureInfo.InvariantCulture));
                        else
                            vBuf.Append(vCh);
                        break;
                }
            }
            return vBuf.ToString();
        }

        // Strip characters that would break out of a Content-Disposition value.
        private static string SanitizeHeaderFilename(string aName)
        {
            StringBuilder vResult = new StringBuilder();
            string vName = aName ?? "";
            for (int vI = 0; vI < vName.Length; vI++)
            {
                char vCh = vName[vI];
                if (vCh == '"' || vCh == '\\' || vCh < ' ')
                    continue;
                vResult.Append(vCh);
            }
            if (vResult.Length == 0)
                return "download";
            return vResult.ToString();
        }

        // The report builders write into a MemoryStream; hand its bytes back as a
        // download with the same Content-Type / Content-Disposition the 60.HTML
        // host wrote.
        private static IResult FileDownload(HttpContext aCtx, MemoryStream aStream,
            string aContentType, string aFileName)
        {
            aCtx.Response.Headers["Cache-Control"] = "no-store";
            byte[] vBytes = aStream.ToArray();
            return Results.File(vBytes, aContentType, SanitizeHeaderFilename(aFileName));
        }

        private static async Task<string> ReadRawBodyAsync(HttpContext aCtx)
        {
            using (StreamReader vReader = new StreamReader(aCtx.Request.Body,
                Encoding.UTF8, false, 1024, true))
            {
                return await vReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        private TWMSPageCtx BuildCtx(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession, string aMenu)
        {
            TWMSPageCtx vResult = new TWMSPageCtx();
            vResult.UserId = aSession.UserId;
            vResult.Username = aSession.Username;
            vResult.DisplayName = aSession.Username;
            vResult.Role = aSession.Role;
            vResult.Theme = ReadThemeCookie(aCtx);
            vResult.Menu = aMenu;
            vResult.Flash = GetParam(aCtx, aForm, "flash");
            vResult.Error = "";
            vResult.Company = FConfig.CompanyName;
            return vResult;
        }

        private TWMSListFilter ReadFilter(HttpContext aCtx, IFormCollection aForm)
        {
            TWMSListFilter vResult = new TWMSListFilter();
            vResult.Search = GetParam(aCtx, aForm, "q").Trim();
            vResult.Category = GetParam(aCtx, aForm, "cat").Trim();
            vResult.Zone = GetParam(aCtx, aForm, "zone").Trim();
            vResult.Status = GetParam(aCtx, aForm, "status").Trim();
            vResult.Kind = GetParam(aCtx, aForm, "kind").Trim();
            vResult.Sort = GetParam(aCtx, aForm, "sort").Trim();
            vResult.Dir = GetParam(aCtx, aForm, "dir").Trim();
            vResult.DateFrom = GetParam(aCtx, aForm, "from").Trim();
            vResult.DateTo = GetParam(aCtx, aForm, "to").Trim();
            vResult.Page = StrToIntDef(GetParam(aCtx, aForm, "page").Trim(), 1);
            if (vResult.Page < 1)
                vResult.Page = 1;
            string vBelow = GetParam(aCtx, aForm, "below");
            vResult.BelowMin = (vBelow != "") &&
                !string.Equals(vBelow, "0", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(vResult.Kind, "all", StringComparison.OrdinalIgnoreCase))
                vResult.Kind = "";
            return vResult;
        }

        private void Audit(HttpContext aCtx, TWMSSession aSession, string aAction,
            string aEntity, long aEntityId, string aDetail)
        {
            if (FDB == null)
                return;
            FDB.AddAudit(aSession.UserId, aAction, aEntity, aEntityId, aDetail,
                ClientIPOf(aCtx));
        }

        // ----- auth gates (used by Program.cs endpoint mapping) ----- //

        // Signed-in gate, any role (the handheld + /security). Mirrors 60.HTML
        // DispatchRequest step 3: an htmx request gets a 401 + HX-Redirect so the
        // fragment swap navigates instead of painting the login page inline.
        public IResult Signed(HttpContext aCtx, Func<TWMSSession, IResult> aFn)
        {
            TWMSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return NotSignedIn(aCtx);
            return aFn(vSession);
        }

        // Back-office gate: signed in AND admin/supervisor. Mirrors 60.HTML
        // DispatchRequest step 6 - the back office is closed to operators.
        public IResult Guarded(HttpContext aCtx, Func<TWMSSession, IResult> aFn)
        {
            TWMSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return NotSignedIn(aCtx);
            if (!WMSConst.WMSRoleIsBackOffice(vSession.Role))
                return Html(403, FPages.BuildForbiddenPage(
                    BuildCtx(aCtx, null, vSession, "")));
            return aFn(vSession);
        }

        // Admin-only gate (/users*, /audit). Mirrors the RouteBackOffice admin
        // block, which answers the same 403 forbidden page.
        public IResult GuardedAdmin(HttpContext aCtx, Func<TWMSSession, IResult> aFn)
        {
            TWMSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return NotSignedIn(aCtx);
            if (!WMSConst.WMSRoleIsBackOffice(vSession.Role) ||
                !WMSConst.WMSRoleIsAdmin(vSession.Role))
                return Html(403, FPages.BuildForbiddenPage(
                    BuildCtx(aCtx, null, vSession, "")));
            return aFn(vSession);
        }

        // Signed-in JSON gate (the passkey register endpoints).
        public IResult GuardedJson(HttpContext aCtx, Func<TWMSSession, IResult> aFn)
        {
            TWMSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return JsonError(aCtx, 401, "Not signed in.");
            return aFn(vSession);
        }

        private IResult NotSignedIn(HttpContext aCtx)
        {
            if (IsHtmx(aCtx))
            {
                aCtx.Response.Headers["HX-Redirect"] = "/login";
                return Results.Content("unauthorized", "text/plain; charset=utf-8",
                    null, 401);
            }
            return Redirect("/login");
        }

        // ----- static asset the adapter does not own ----- //

        public IResult Favicon(HttpContext aCtx)
        {
            aCtx.Response.Headers["Cache-Control"] = "public, max-age=86400";
            return Results.Content(CS_FAVICON_SVG, "image/svg+xml", null, 200);
        }

        // ----- public auth / theme ----- //

        public IResult LoginGet(HttpContext aCtx)
        {
            TWMSSession vSession;
            if (CurrentSession(aCtx, out vSession))
                return Redirect("/");
            return Html(200, FPages.BuildLoginPage(ReadThemeCookie(aCtx), "",
                FConfig.AdminUser, FConfig.AdminPassword));
        }

        public IResult LoginPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vUser = GetParam(aCtx, aForm, "username").Trim();
            string vPwd = GetParam(aCtx, aForm, "password");
            if (vUser == "" || vPwd == "")
                return Html(200, FPages.BuildLoginPage(vTheme,
                    "Enter a user name and a password.", vUser, ""));

            TWMSUser oUser;
            if (!FDB.AuthenticateUser(vUser, vPwd, out oUser))
                return Html(200, FPages.BuildLoginPage(vTheme,
                    "That user name and password do not match.", vUser, ""));

            string vToken = FSessions.CreateSession(oUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            TWMSSession oSession;
            FSessions.TryGet(vToken, out oSession);
            Audit(aCtx, oSession, "login", "user", oUser.Id, oUser.Username);
            if (WMSConst.WMSRoleIsBackOffice(oUser.Role))
                return Redirect("/");
            return Redirect("/hh");
        }

        public IResult RegisterGet(HttpContext aCtx)
        {
            return Html(200, FPages.BuildRegisterPage(ReadThemeCookie(aCtx), ""));
        }

        public IResult RegisterPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vUser = GetParam(aCtx, aForm, "username").Trim();
            string vDisplay = GetParam(aCtx, aForm, "display_name").Trim();
            string vPwd = GetParam(aCtx, aForm, "password");
            string vConfirm = GetParam(aCtx, aForm, "confirm");

            if (vUser == "" || vPwd == "")
                return Html(200, FPages.BuildRegisterPage(vTheme,
                    "A user name and a password are required."));
            if (vPwd.Length < 8)
                return Html(200, FPages.BuildRegisterPage(vTheme,
                    "The password must be at least 8 characters."));
            if (vPwd != vConfirm)
                return Html(200, FPages.BuildRegisterPage(vTheme,
                    "The two passwords do not match."));
            if (FDB.UsernameExists(vUser))
                return Html(200, FPages.BuildRegisterPage(vTheme,
                    "That user name is already taken."));
            if (vDisplay == "")
                vDisplay = vUser;
            // Self-registration always creates an operator: handheld only.
            FDB.RegisterUser(vUser, Bcrypt.BcryptHash(vPwd), WMSConst.CS_ROLE_OPERATOR,
                vDisplay);
            return Redirect("/login");
        }

        public IResult Logout(HttpContext aCtx)
        {
            string vToken = ReadCookie(aCtx, CS_WMS_SESSION);
            if (vToken != "")
                FSessions.Destroy_(vToken);
            ClearSessionCookie(aCtx);
            return Redirect("/login");
        }

        public IResult SetTheme(HttpContext aCtx, IFormCollection aForm)
        {
            WriteThemeCookie(aCtx, GetParam(aCtx, aForm, "theme"));
            return Redirect(RefererOrRoot(aCtx));
        }

        // ----- passkeys (WebAuthn) ----- //

        public IResult PasskeyLoginOptions(HttpContext aCtx)
        {
            try
            {
                return Json(aCtx, 200, GetPasskeys(aCtx).BeginLogin(""));
            }
            catch (Exception E)
            {
                return JsonError(aCtx, 400, E.Message);
            }
        }

        public async Task<IResult> PasskeyLoginVerify(HttpContext aCtx)
        {
            long vUserID = 0;
            try
            {
                string vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
                if (vBody.Trim() == "")
                    return JsonError(aCtx, 400, "Empty request body.");
                GetPasskeys(aCtx).FinishLogin(vBody, out vUserID);
            }
            catch (Exception E)
            {
                return JsonError(aCtx, 401, E.Message);
            }
            TWMSUser oUser;
            if (vUserID <= 0 || !FDB.GetUserById(vUserID, out oUser))
                return JsonError(aCtx, 401, "Passkey did not match any user.");
            string vToken = FSessions.CreateSession(oUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            return Json(aCtx, 200, "{\"ok\":true,\"redirect\":\"/\"}");
        }

        public IResult PasskeyRegisterOptions(HttpContext aCtx, TWMSSession aSession)
        {
            try
            {
                return Json(aCtx, 200, GetPasskeys(aCtx).BeginRegister(aSession.UserId,
                    aSession.Username, aSession.Username));
            }
            catch (Exception E)
            {
                return JsonError(aCtx, 400, E.Message);
            }
        }

        public async Task<IResult> PasskeyRegisterVerifyGuarded(HttpContext aCtx)
        {
            TWMSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return JsonError(aCtx, 401, "Not signed in.");
            try
            {
                string vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
                if (vBody.Trim() == "")
                    return JsonError(aCtx, 400, "Empty request body.");
                string vName = GetParam(aCtx, null, "name").Trim();
                if (vName == "")
                    vName = "Passkey";
                string vResult = GetPasskeys(aCtx).FinishRegister(vSession.UserId, vBody,
                    vName);
                return Json(aCtx, 200, "{\"ok\":true,\"detail\":" + vResult + "}");
            }
            catch (Exception E)
            {
                return JsonError(aCtx, 400, E.Message);
            }
        }

        public IResult SecurityGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildSecurityPage(
                BuildCtx(aCtx, null, aSession, "security")));
        }

        // ----- handheld (open to all three roles) ----- //

        public IResult HandheldHome(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FHandheld.BuildHome(BuildCtx(aCtx, null, aSession, "hh")));
        }

        public IResult HandheldReceiveGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FHandheld.BuildReceive(BuildCtx(aCtx, null, aSession, "hh"),
                "", GetParam(aCtx, null, "msg")));
        }

        public IResult HandheldReceivePost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            TWMSPageCtx vCtx = BuildCtx(aCtx, aForm, aSession, "hh");
            if (GetParam(aCtx, aForm, "confirm") == "1")
            {
                long vProductId = GetParamInt(aCtx, aForm, "product_id");
                long vLocationId = GetParamInt(aCtx, aForm, "location_id");
                int vQty = StrToIntDef(GetParam(aCtx, aForm, "qty").Trim(), 0);
                if (vProductId <= 0 || vLocationId <= 0 || vQty <= 0)
                    return Html(200, FHandheld.BuildReceive(vCtx, "",
                        "Scan a product, pick a bin and key a quantity."));
                FDB.AdjustStock(vProductId, vLocationId, vQty, aSession.UserId,
                    "HH-RECEIVE");
                Audit(aCtx, aSession, "hh_receive", "stock", vProductId,
                    Str(vQty) + " units into bin " + Str(vLocationId));
                return Redirect("/hh/receive?msg=" + Str(vQty) + " units booked in.");
            }
            return Html(200, FHandheld.BuildReceive(vCtx,
                GetParam(aCtx, aForm, "scan").Trim(), ""));
        }

        public IResult HandheldPickOrders(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FHandheld.BuildPickOrders(
                BuildCtx(aCtx, null, aSession, "hh")));
        }

        public IResult HandheldPickOrder(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            return Html(200, FHandheld.BuildPickOrder(
                BuildCtx(aCtx, null, aSession, "hh"), aId, GetParam(aCtx, null, "msg")));
        }

        public IResult HandheldPickConfirm(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            long vSOId = GetParamInt(aCtx, aForm, "so_id");
            string vPick = GetParam(aCtx, aForm, "pick").Trim();
            int vQty = StrToIntDef(GetParam(aCtx, aForm, "qty").Trim(), 0);
            // 'pick' is '<line_id>:<location_id>'. Delphi splits it with Pos/Copy,
            // so a token with no ':' yields an empty line id and the whole token as
            // the location - both then fail the guard below.
            int vColon = vPick.IndexOf(':');
            long vLineId = StrToInt64Def(vColon > 0 ? vPick.Substring(0, vColon) : "", 0);
            long vLocationId = StrToInt64Def(vColon >= 0 ? vPick.Substring(vColon + 1)
                : vPick, 0);
            if (vSOId <= 0 || vLineId <= 0 || vQty <= 0)
                return Redirect("/hh/pick/" + Str(vSOId) +
                    "?msg=Pick a line and key a quantity.");
            int vDone = FDB.PickSOLine(vSOId, vLineId, vLocationId, vQty,
                aSession.UserId, "HH-PICK");
            Audit(aCtx, aSession, "hh_pick", "sales_order", vSOId,
                Str(vDone) + " units from line " + Str(vLineId));
            return Redirect("/hh/pick/" + Str(vSOId) + "?msg=" + Str(vDone) +
                " units picked.");
        }

        public IResult HandheldCountGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FHandheld.BuildCount(BuildCtx(aCtx, null, aSession, "hh"),
                "", GetParam(aCtx, null, "msg")));
        }

        public IResult HandheldCountPost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            TWMSPageCtx vCtx = BuildCtx(aCtx, aForm, aSession, "hh");
            if (GetParam(aCtx, aForm, "confirm") == "1")
            {
                long vProductId = GetParamInt(aCtx, aForm, "product_id");
                long vLocationId = GetParamInt(aCtx, aForm, "location_id");
                int vQty = StrToIntDef(GetParam(aCtx, aForm, "counted").Trim(), -1);
                if (vProductId <= 0 || vLocationId <= 0 || vQty < 0)
                    return Html(200, FHandheld.BuildCount(vCtx, "",
                        "Scan a product, pick a bin and key the counted quantity."));
                int vDelta = FDB.QuickCount(vProductId, vLocationId, vQty,
                    aSession.UserId);
                Audit(aCtx, aSession, "hh_count", "stock", vProductId,
                    "counted " + Str(vQty) + ", variance " + Str(vDelta));
                return Redirect("/hh/count?msg=Counted. Variance " + Str(vDelta) + ".");
            }
            return Html(200, FHandheld.BuildCount(vCtx,
                GetParam(aCtx, aForm, "scan").Trim(), ""));
        }

        public IResult HandheldLookup(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            return Html(200, FHandheld.BuildLookup(BuildCtx(aCtx, aForm, aSession, "hh"),
                GetParam(aCtx, aForm, "scan").Trim(), GetParam(aCtx, aForm, "msg")));
        }

        // ----- dashboard ----- //

        public IResult DashboardGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildDashboardPage(
                BuildCtx(aCtx, null, aSession, "dashboard")));
        }

        public IResult HeatmapFragment(HttpContext aCtx)
        {
            string vZone = GetParam(aCtx, null, "zone").Trim();
            if (vZone == "")
                vZone = "A";
            return Fragment(aCtx, FPages.BuildHeatmapFragment(vZone));
        }

        // ----- products ----- //

        public IResult ProductsGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildProductListPage(
                BuildCtx(aCtx, null, aSession, "products"), ReadFilter(aCtx, null)));
        }

        public IResult ProductFormFragment(HttpContext aCtx)
        {
            if (GetParam(aCtx, null, "cancel") != "")
                return Fragment(aCtx, "");
            return Fragment(aCtx, FPages.BuildProductFormFragment(
                GetParamInt(aCtx, null, "id")));
        }

        public IResult ProductsExportXLSX(HttpContext aCtx)
        {
            TWMSListFilter vFilter = ReadFilter(aCtx, null);
            MemoryStream oStream = new MemoryStream();
            FReports.ProductsXLSX(oStream, vFilter.Search, vFilter.Category,
                vFilter.Sort, vFilter.Dir);
            return FileDownload(aCtx, oStream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "products.xlsx");
        }

        public IResult ProductsExportPDF(HttpContext aCtx)
        {
            TWMSListFilter vFilter = ReadFilter(aCtx, null);
            MemoryStream oStream = new MemoryStream();
            FReports.ProductsPDF(oStream, vFilter.Search, vFilter.Category,
                vFilter.Sort, vFilter.Dir);
            return FileDownload(aCtx, oStream, "application/pdf", "products.pdf");
        }

        public IResult ProductSavePost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            TWMSListFilter vFilter = ReadFilter(aCtx, aForm);
            long vId = GetParamInt(aCtx, aForm, "id");
            vId = FDB.SaveProduct(vId, GetParam(aCtx, aForm, "sku").Trim(),
                GetParam(aCtx, aForm, "barcode").Trim(),
                GetParam(aCtx, aForm, "name").Trim(),
                GetParam(aCtx, aForm, "description"),
                GetParam(aCtx, aForm, "uom").Trim(),
                StrToFloatDef(GetParam(aCtx, aForm, "unit_cost").Trim(), 0),
                StrToIntDef(GetParam(aCtx, aForm, "min_stock").Trim(), 0),
                GetParam(aCtx, aForm, "category").Trim());
            Audit(aCtx, aSession, "save", "product", vId,
                GetParam(aCtx, aForm, "sku").Trim());
            if (IsHtmx(aCtx))
                return Fragment(aCtx, FPages.BuildProductTableFragment(vFilter));
            return Redirect("/products?flash=saved");
        }

        public IResult ProductDeletePost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            TWMSListFilter vFilter = ReadFilter(aCtx, aForm);
            long vId = GetParamInt(aCtx, aForm, "id");
            if (vId > 0)
            {
                FDB.DeleteProduct(vId);
                Audit(aCtx, aSession, "delete", "product", vId, "");
            }
            if (IsHtmx(aCtx))
                return Fragment(aCtx, FPages.BuildProductTableFragment(vFilter));
            return Redirect("/products?flash=deleted");
        }

        public IResult ProductDetailGet(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            return Html(200, FPages.BuildProductDetailPage(
                BuildCtx(aCtx, null, aSession, "products"), aId));
        }

        // ----- locations ----- //

        public IResult LocationsGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildLocationsPage(
                BuildCtx(aCtx, null, aSession, "locations"),
                ReadFilter(aCtx, null).Zone));
        }

        public IResult LocationDetailGet(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            return Html(200, FPages.BuildLocationDetailPage(
                BuildCtx(aCtx, null, aSession, "locations"), aId));
        }

        // ----- inbound (purchase orders) ----- //

        public IResult InboundListGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildInboundListPage(
                BuildCtx(aCtx, null, aSession, "inbound"), ReadFilter(aCtx, null)));
        }

        public IResult InboundDetailGet(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            return Html(200, FPages.BuildInboundDetailPage(
                BuildCtx(aCtx, null, aSession, "inbound"), aId));
        }

        public IResult InboundReceivePost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession, long aId)
        {
            long vLineId = GetParamInt(aCtx, aForm, "line_id");
            int vQty = StrToIntDef(GetParam(aCtx, aForm, "qty").Trim(), 0);
            string vCode = GetParam(aCtx, aForm, "location").Trim();
            long vLocationId = 0;
            using (TWMSDataSet oData = FDB.Query(
                "SELECT id FROM locations WHERE code = :c AND kind = 'bin'"))
            {
                oData.SetStr("c", vCode);
                oData.Open();
                if (!oData.IsEmpty())
                    vLocationId = oData.AsInt("id");
            }
            if (vLocationId == 0)
                return Redirect("/inbound/" + Str(aId) + "?flash=Unknown bin " + vCode);
            int vDone = FDB.ReceivePOLine(aId, vLineId, vLocationId, vQty,
                aSession.UserId, "PO-" + Str(aId));
            Audit(aCtx, aSession, "receive", "purchase_order", aId,
                Str(vDone) + " units to " + vCode);
            return Redirect("/inbound/" + Str(aId) + "?flash=received");
        }

        // The 60.HTML host parsed multipart/form-data by hand; here the framework
        // hands the files over as IFormFile and they are converted into the SAME
        // TWMSMultipartField values the reused TWMSAttachStore expects, so the
        // storage semantics are unchanged.
        public IResult InboundPhotoPost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession, long aId)
        {
            string vError = "";
            TWMSAttachSaved[] vSaved = new TWMSAttachSaved[0];
            try
            {
                vSaved = FPhotos.SaveFiles(aId, CollectUploads(aForm, "photo"),
                    out vError);
            }
            catch (Exception E)
            {
                vError = E.Message;
            }
            if (vError != "")
                return Redirect("/inbound/" + Str(aId) + "?flash=" + vError);
            Audit(aCtx, aSession, "photo", "purchase_order", aId,
                Str(vSaved.Length) + " file(s)");
            return Redirect("/inbound/" + Str(aId) + "?flash=Damage photos stored.");
        }

        // Materialize the posted files of one form field into the reused
        // TWMSMultipartField shape (original name, content type, raw bytes).
        private static TWMSMultipartField[] CollectUploads(IFormCollection aForm,
            string aField)
        {
            List<TWMSMultipartField> vResult = new List<TWMSMultipartField>();
            if (aForm == null || aForm.Files == null)
                return vResult.ToArray();
            for (int vI = 0; vI < aForm.Files.Count; vI++)
            {
                IFormFile oFile = aForm.Files[vI];
                if (oFile == null)
                    continue;
                if (!string.Equals(oFile.Name, aField, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(oFile.FileName) || oFile.Length <= 0)
                    continue;
                byte[] vBytes;
                using (MemoryStream oBuf = new MemoryStream())
                {
                    using (Stream oIn = oFile.OpenReadStream())
                    {
                        oIn.CopyTo(oBuf);
                    }
                    vBytes = oBuf.ToArray();
                }
                TWMSMultipartField oField = new TWMSMultipartField();
                oField.Name = oFile.Name;
                oField.FileName = Path.GetFileName(oFile.FileName);
                oField.ContentType = oFile.ContentType ?? "";
                oField.FileBytes = vBytes;
                vResult.Add(oField);
            }
            return vResult.ToArray();
        }

        public IResult InboundLabelsPDF(HttpContext aCtx, long aId)
        {
            MemoryStream oStream = new MemoryStream();
            FReports.LabelsPDF(oStream, aId, FConfig.CompanyName);
            return FileDownload(aCtx, oStream, "application/pdf",
                "labels-" + Str(aId) + ".pdf");
        }

        // ----- outbound (sales orders) ----- //

        public IResult OutboundListGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildOutboundListPage(
                BuildCtx(aCtx, null, aSession, "outbound"), ReadFilter(aCtx, null)));
        }

        public IResult OutboundDetailGet(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            return Html(200, FPages.BuildOutboundDetailPage(
                BuildCtx(aCtx, null, aSession, "outbound"), aId));
        }

        public IResult OutboundPickPost(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            int vDone = 0;
            using (TWMSDataSet oData = FDB.OpenSalesOrderLines(aId))
            {
                while (!oData.Eof)
                {
                    if (oData.AsInt("outstanding") > 0)
                        vDone = vDone + FDB.PickSOLine(aId, oData.AsInt("id"), 0,
                            (int)oData.AsInt("outstanding"), aSession.UserId,
                            "SO-" + Str(aId));
                    oData.Next();
                }
            }
            Audit(aCtx, aSession, "pick", "sales_order", aId, Str(vDone) + " units");
            return Redirect("/outbound/" + Str(aId) + "?flash=picked");
        }

        public IResult OutboundPackPost(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            if (!FDB.SalesOrderIsFullyPicked(aId))
                return Redirect("/outbound/" + Str(aId) +
                    "?flash=Not every line is picked yet.");
            FDB.SetSalesOrderStatus(aId, "packed", aSession.UserId);
            Audit(aCtx, aSession, "pack", "sales_order", aId, "");
            return Redirect("/outbound/" + Str(aId) + "?flash=packed");
        }

        public IResult OutboundShipPost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession, long aId)
        {
            string vDetail = GetParam(aCtx, aForm, "signature");
            FDB.SetSalesOrderStatus(aId, "shipped", aSession.UserId);
            Audit(aCtx, aSession, "ship", "sales_order", aId,
                "proof of delivery " + Str(vDetail.Length) + " bytes");
            return Redirect("/outbound/" + Str(aId) + "?flash=shipped");
        }

        public IResult OutboundPackingSlipPDF(HttpContext aCtx, long aId)
        {
            MemoryStream oStream = new MemoryStream();
            FReports.PackingSlipPDF(oStream, aId, FConfig.CompanyName);
            return FileDownload(aCtx, oStream, "application/pdf",
                "packing-slip-" + Str(aId) + ".pdf");
        }

        // ----- stock ----- //

        public IResult StockGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildStockPage(
                BuildCtx(aCtx, null, aSession, "stock"), ReadFilter(aCtx, null)));
        }

        public IResult MovementsGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildMovementsPage(
                BuildCtx(aCtx, null, aSession, "movements"), ReadFilter(aCtx, null)));
        }

        public IResult StockAdjustPost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            long vProductId = GetParamInt(aCtx, aForm, "product_id");
            int vDelta = StrToIntDef(GetParam(aCtx, aForm, "delta").Trim(), 0);
            string vCode = GetParam(aCtx, aForm, "location_id").Trim();
            long vLocationId = 0;
            if (vCode != "")
            {
                using (TWMSDataSet oData = FDB.Query(
                    "SELECT id FROM locations WHERE code = :c"))
                {
                    oData.SetStr("c", vCode);
                    oData.Open();
                    if (!oData.IsEmpty())
                        vLocationId = oData.AsInt("id");
                }
            }
            if (vProductId > 0 && vLocationId > 0 && vDelta != 0)
            {
                FDB.AdjustStock(vProductId, vLocationId, vDelta, aSession.UserId,
                    GetParam(aCtx, aForm, "reference").Trim());
                Audit(aCtx, aSession, "adjust", "stock", vProductId,
                    Str(vDelta) + " in " + vCode);
                return Redirect("/products/" + Str(vProductId) + "?flash=adjusted");
            }
            return Redirect("/products/" + Str(vProductId) + "?flash=Nothing to adjust.");
        }

        // ----- cycle counts ----- //

        public IResult CountsGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildCountsPage(
                BuildCtx(aCtx, null, aSession, "counts")));
        }

        public IResult CountNewPost(HttpContext aCtx, TWMSSession aSession)
        {
            string vRef = "CC-" + DateTime.Now.ToString("yyyyMMdd-HHmm",
                CultureInfo.InvariantCulture);
            long vId = FDB.CreateStockCount(vRef);
            Audit(aCtx, aSession, "create", "stock_count", vId, vRef);
            return Redirect("/counts/" + Str(vId));
        }

        public IResult CountDetailGet(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            return Html(200, FPages.BuildCountDetailPage(
                BuildCtx(aCtx, null, aSession, "counts"), aId));
        }

        public IResult CountLinePost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession, long aId)
        {
            long vLineId = GetParamInt(aCtx, aForm, "line_id");
            int vQty = StrToIntDef(GetParam(aCtx, aForm, "counted").Trim(), -1);
            if (vLineId > 0 && vQty >= 0)
            {
                int vDelta = FDB.SaveCountLine(aId, vLineId, vQty);
                Audit(aCtx, aSession, "count_line", "stock_count", aId,
                    "line " + Str(vLineId) + " variance " + Str(vDelta));
            }
            return Redirect("/counts/" + Str(aId) + "?flash=counted");
        }

        public IResult CountClosePost(HttpContext aCtx, TWMSSession aSession, long aId)
        {
            int vPosted = FDB.CloseStockCount(aId, aSession.UserId);
            Audit(aCtx, aSession, "close", "stock_count", aId,
                Str(vPosted) + " variance postings");
            return Redirect("/counts/" + Str(aId) + "?flash=closed");
        }

        // ----- reports ----- //

        public IResult ReportsGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildReportsPage(
                BuildCtx(aCtx, null, aSession, "reports")));
        }

        public IResult ValuationPDF(HttpContext aCtx)
        {
            MemoryStream oStream = new MemoryStream();
            FReports.ValuationPDF(oStream, FConfig.CompanyName);
            return FileDownload(aCtx, oStream, "application/pdf",
                "stock-valuation.pdf");
        }

        public IResult ValuationXLSX(HttpContext aCtx)
        {
            MemoryStream oStream = new MemoryStream();
            FReports.ValuationXLSX(oStream);
            return FileDownload(aCtx, oStream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "stock-valuation.xlsx");
        }

        // ----- the no-REST page ----- //

        public IResult SQLGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildSQLPage(BuildCtx(aCtx, null, aSession, "sql")));
        }

        // ----- admin only ----- //

        public IResult UsersGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildUsersPage(
                BuildCtx(aCtx, null, aSession, "users"), GetParamInt(aCtx, null, "id")));
        }

        public IResult UserSavePost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            long vId = GetParamInt(aCtx, aForm, "id");
            string vHash = GetParam(aCtx, aForm, "password").Trim();
            if (vHash != "")
                vHash = Bcrypt.BcryptHash(vHash);
            vId = FDB.SaveUser(vId, GetParam(aCtx, aForm, "username").Trim(), vHash,
                GetParam(aCtx, aForm, "role").Trim(),
                GetParam(aCtx, aForm, "display_name").Trim());
            if (vId <= 0)
                return Redirect("/users?flash=That user name is already taken.");
            Audit(aCtx, aSession, "save", "user", vId,
                GetParam(aCtx, aForm, "username").Trim());
            return Redirect("/users?flash=saved");
        }

        public IResult UserDeletePost(HttpContext aCtx, IFormCollection aForm,
            TWMSSession aSession)
        {
            long vId = GetParamInt(aCtx, aForm, "id");
            if (vId > 0 && vId != aSession.UserId && FDB.CountAdmins(vId) > 0)
            {
                FDB.DeleteUser(vId);
                Audit(aCtx, aSession, "delete", "user", vId, "");
                return Redirect("/users?flash=deleted");
            }
            return Redirect("/users?flash=That account cannot be removed.");
        }

        public IResult AuditGet(HttpContext aCtx, TWMSSession aSession)
        {
            return Html(200, FPages.BuildAuditPage(
                BuildCtx(aCtx, null, aSession, "audit"), ReadFilter(aCtx, null).Search));
        }

        // ----- fallback (mirrors DispatchRequest steps 3, 5, 6 and 8) ----- //

        // An unmatched path answers exactly as the 60.HTML dispatcher did: signed
        // out -> /login (or a 401 + HX-Redirect for htmx); signed in under /hh ->
        // the 404 page; signed in as an operator anywhere else -> the 403 page;
        // otherwise the 404 page.
        public IResult NotFound(HttpContext aCtx)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TWMSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return NotSignedIn(aCtx);
            string vPath = aCtx.Request.Path.HasValue ? aCtx.Request.Path.Value : "";
            if (vPath == "/hh" ||
                vPath.StartsWith("/hh/", StringComparison.OrdinalIgnoreCase))
                return Html(404, FPages.BuildNotFoundPage(vTheme));
            if (!WMSConst.WMSRoleIsBackOffice(vSession.Role))
                return Html(403, FPages.BuildForbiddenPage(
                    BuildCtx(aCtx, null, vSession, "")));
            return Html(404, FPages.BuildNotFoundPage(vTheme));
        }
    }
}
