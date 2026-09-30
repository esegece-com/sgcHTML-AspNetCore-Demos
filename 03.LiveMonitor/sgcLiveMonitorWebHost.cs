// ***************************************************************************
//  sgcLiveMonitorWeb - Live Monitor demo on ASP.NET Core
//  Mirror of demos\60.HTML\03.LiveMonitor (TsgcWebSocketHTTPServer host).
//
//  This is the REWRITTEN host layer. It reproduces every branch of the 60.HTML
//  sgcLiveMonitor_Server.cs DispatchRequest, but on Kestrel:
//    - The reusable logic (Bcrypt, DB, I18n, Pages, Passkeys, Sessions, Types,
//      Config) is copied VERBATIM from the 60.HTML demo and NOT changed here.
//    - The 60.HTML host scanned RawHeaders for the Cookie line and wrote
//      Set-Cookie / redirects through CustomHeaders on TsgcWSHTTPResponseInfo.
//      Here every cookie is read/written through the native ASP.NET Core cookie
//      API (ctx.Request.Cookies / ctx.Response.Cookies) with the SAME cookie
//      names + attributes, and each handler returns an IResult.
//    - Query + form params are merged through GetParam, matching the Delphi
//      ARequestInfo.Params (query first, then the urlencoded body).
//    - PASSKEYS: the 60.HTML host hard-coded RP id 'localhost' + origin
//      http://localhost:<port>. Under Kestrel the RP id (request host name) and
//      origin (scheme://host) are DERIVED from the live request and threaded into
//      the reused TERPPasskeys. A per-origin instance is cached so the WebAuthn
//      begin/finish challenge state survives across a ceremony (see GetPasskeys).
//
//  LIVE PUSH (the point of this demo): the 60.HTML host wired the HTMX engine to
//  the WebSocket HTTP server and a System.Threading.Timer called
//  BroadcastFragment(BuildMetricsFragment()) every ~1500 ms. Under Kestrel the
//  engine has no Server bound, so its BroadcastFragment is a no-op; server push
//  goes through ISgcHtmlHub instead. This host owns the SAME metric state +
//  BuildMetricsFragment builder (ported verbatim from the 60.HTML host, the one
//  file not copied), and the LiveMonitorPushService (a BackgroundService in
//  Program.cs) calls BuildMetricsFragment + hub.BroadcastAsync on the timer.
//  The live WS connection count the dashboard + fragment report is read from the
//  hub (ISgcHtmlHub.Count), the Kestrel counterpart of the 60.HTML FHTTP.Count.
//
//  The two host files that were NOT copied from 60.HTML are
//  sgcLiveMonitor_Server.cs (the TsgcWebSocketHTTPServer host) and the console
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

namespace LiveMonitor
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, passkey factory) and exposes one IResult-returning method per
    // 60.HTML DispatchRequest branch. Program.cs maps the Minimal API endpoints
    // onto these methods and applies the auth / admin gates through Guarded*.
    // On top of the admin stack it owns the live-metric state + the OOB fragment
    // builder the push loop broadcasts over the WebSocket.
    public sealed class LiveMonitorWebHost : IDisposable
    {
        public const string CS_MINIERP_SERVER_VERSION = "1.0.0";

        // Cookie names - identical to the 60.HTML host so behavior matches exactly.
        public const string CS_ERP_SESSION = "merp_session";
        public const string CS_THEME_COOKIE = "merp_theme";
        public const string CS_LANG_COOKIE = "merp_lang";

        // Editable settings (settings table) + their defaults. Order drives the form.
        public const string CS_SETTING_COMPANY = "company_name";
        public const string CS_SETTING_CURRENCY = "default_currency";
        public const string CS_SETTING_TAX_RATE = "default_tax_rate";
        public const string CS_SETTING_INV_PREFIX = "invoice_prefix";
        public const string CS_SETTING_SESSION_HOURS = "session_timeout_hours";

        public const string CS_DEFAULT_COMPANY = "Live Monitor";
        public const string CS_DEFAULT_CURRENCY = "EUR";
        public const string CS_DEFAULT_TAX_RATE = "21";
        public const string CS_DEFAULT_INV_PREFIX = "INV-";
        public const string CS_DEFAULT_SESSION_HOURS = "8";

        // Self-contained eSeGeCe favicon (served at /favicon.svg). Blue (#0057B8)
        // to match the Live Monitor identity. Verbatim from the 60.HTML host
        // constant (that unit is intentionally not copied).
        public const string CS_FAVICON_SVG =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 64 64\" " +
            "role=\"img\" aria-label=\"eSeGeCe\">" +
            "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"#0057B8\"/>" +
            "<text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"900\" font-size=\"44\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">e</text></svg>";

        private readonly TERPDBPool FDB;
        private readonly TERPSessionStore FSessions;
        private readonly TERPPages FPages;
        private readonly int FListenPort;
        private readonly DateTime FStartedAt;

        // The Kestrel push channel + live-connection counter (the counterpart of
        // the 60.HTML FHTTP for BroadcastFragment / FHTTP.Count).
        private readonly ISgcHtmlHub FHub;

        // Realtime metric state (random-walk), advanced once per push tick. Ported
        // verbatim from the 60.HTML host (the one file not copied). Guarded by
        // FMetricLock because the push BackgroundService thread and a dashboard
        // GET can both touch it.
        private readonly object FMetricLock = new object();
        private readonly Random FRandom = new Random();
        private double FCpu;
        private double FMem;
        private double FRps;
        private int FEventSeq;

        // Per-origin passkey cache. WebAuthn begin/finish share challenge state in
        // one TERPPasskeys instance, so a ceremony's two requests (same browser,
        // same origin) must hit the SAME instance. Keyed by "rpId|origin".
        private readonly Dictionary<string, TERPPasskeys> FPasskeys =
            new Dictionary<string, TERPPasskeys>(StringComparer.Ordinal);
        private readonly object FPasskeysLock = new object();

        // Builds the DB (schema + admin seed + demo data), the session store, the
        // page builder and seeds the settings - mirroring TsgcLiveMonitorServer.Start.
        public LiveMonitorWebHost(TERPServerConfig aConfig, string aDatabasePath,
            int aListenPort, ISgcHtmlHub aHub)
        {
            FListenPort = aListenPort;
            FStartedAt = DateTime.Now;
            FHub = aHub;

            // Metric start values, verbatim from the 60.HTML host constructor.
            FCpu = 18;
            FMem = 42;
            FRps = 120;
            FEventSeq = 0;

            FDB = new TERPDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedAdmin(aConfig.AdminUser, Bcrypt.BcryptHash(aConfig.AdminPassword));
            // Populate the demo the first time the DB is empty so the reports look
            // full. Idempotent.
            FDB.SeedDemoDataIfEmpty();

            FSessions = new TERPSessionStore(480, true);
            FPages = new TERPPages();

            // Seed default settings (if absent) and push company_name into the brand.
            ApplySettings();
        }

        public void Dispose()
        {
            lock (FPasskeysLock)
            {
                foreach (KeyValuePair<string, TERPPasskeys> vPair in FPasskeys)
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

        // ----- firewall (called from the pipeline middleware) ----- //

        // True when this client IP must be blocked (403). Loopback is never blocked;
        // an IP on the ignored allow-list is never blocked. Mirrors the 60.HTML
        // DispatchRequest firewall gate.
        public bool IsRequestBlocked(HttpContext aCtx)
        {
            string vIP = ClientIPOf(aCtx);
            return vIP != "" && vIP != "127.0.0.1" && vIP != "::1" &&
                !FDB.IsIgnoredIP(vIP) && FDB.IsBlockedIP(vIP);
        }

        public IResult ForbiddenFirewall(HttpContext aCtx)
        {
            return Html(403, FPages.BuildForbiddenPage(ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx), "firewall.denied"));
        }

        // ----- passkey factory (request-derived rpId + origin) ----- //

        // Returns the TERPPasskeys for THIS request's origin, creating + caching it
        // on first use. RP id = the request host name (no port); origin =
        // scheme://host (with port). This threads the live request values into the
        // reused passkey engine instead of the 60.HTML hard-coded localhost.
        private TERPPasskeys GetPasskeys(HttpContext aCtx)
        {
            string vRPID = aCtx.Request.Host.Host;
            if (string.IsNullOrEmpty(vRPID))
                vRPID = "localhost";
            string vOrigin = aCtx.Request.Scheme + "://" + aCtx.Request.Host.Value;
            string vKey = vRPID + "|" + vOrigin;

            lock (FPasskeysLock)
            {
                TERPPasskeys vPk;
                if (!FPasskeys.TryGetValue(vKey, out vPk))
                {
                    vPk = new TERPPasskeys(FDB, vRPID, "Live Monitor", vOrigin);
                    FPasskeys[vKey] = vPk;
                }
                return vPk;
            }
        }

        // ----- request param helpers (query + form merged) ----- //

        // First value for aName: query wins over form, matching the Delphi merged
        // ARequestInfo.Params. aForm is null for GET / non-form requests.
        private static string GetParam(HttpContext aCtx, IFormCollection aForm, string aName)
        {
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues) && vValues.Count > 0)
                return vValues[0] ?? "";
            if (aForm != null && aForm.TryGetValue(aName, out vValues) && vValues.Count > 0)
                return vValues[0] ?? "";
            return "";
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
            aCtx.Response.Cookies.Append(CS_ERP_SESSION, aToken, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private void ClearSessionCookie(HttpContext aCtx)
        {
            aCtx.Response.Cookies.Delete(CS_ERP_SESSION, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static string ReadThemeCookie(HttpContext aCtx)
        {
            string vS = ReadCookie(aCtx, CS_THEME_COOKIE).ToLowerInvariant();
            if (vS == "light" || vS == "dark" || vS == "system")
                return vS;
            // Mirrors the 60.HTML host: 'system' follows the OS preference.
            return "system";
        }

        private void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vS = (aTheme ?? "").Trim().ToLowerInvariant();
            if (vS != "light" && vS != "dark" && vS != "system")
                vS = "system";
            aCtx.Response.Cookies.Append(CS_THEME_COOKIE, vS, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(31536000),
                SameSite = SameSiteMode.Lax
            });
        }

        private static bool IsValidLang(string aLang)
        {
            return aLang == "en" || aLang == "es" || aLang == "de" || aLang == "fr" ||
                aLang == "it" || aLang == "nl" || aLang == "pl" || aLang == "br" ||
                aLang == "zh" || aLang == "ko" || aLang == "ja" || aLang == "tr";
        }

        private static string ReadLanguageCookie(HttpContext aCtx)
        {
            string vS = ReadCookie(aCtx, CS_LANG_COOKIE).ToLowerInvariant();
            if (IsValidLang(vS))
                return vS;
            return "en";
        }

        private void WriteLanguageCookie(HttpContext aCtx, string aLang)
        {
            string vS = (aLang ?? "").Trim().ToLowerInvariant();
            if (!IsValidLang(vS))
                vS = "en";
            aCtx.Response.Cookies.Append(CS_LANG_COOKIE, vS, new CookieOptions
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

        // ----- sessions ----- //

        // True + session when the request carries a valid session cookie.
        public bool CurrentSession(HttpContext aCtx, out TERPSession aSession)
        {
            aSession = null;
            string vToken = ReadCookie(aCtx, CS_ERP_SESSION);
            if (vToken == "")
                return false;
            return FSessions.TryGet(vToken, out aSession);
        }

        // ----- response helpers (return an IResult) ----- //

        private static IResult Html(int aCode, string aHTML)
        {
            return Results.Content(aHTML, "text/html; charset=utf-8", null, aCode);
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

        private static async Task<string> ReadRawBodyAsync(HttpContext aCtx)
        {
            using (StreamReader vReader = new StreamReader(aCtx.Request.Body,
                Encoding.UTF8, false, 1024, true))
            {
                return await vReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        // ----- auth gates (used by Program.cs endpoint mapping) ----- //

        // Session-protected page endpoint: redirect to /login when signed out.
        public IResult Guarded(HttpContext aCtx, Func<TERPSession, IResult> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            return aFn(vSession);
        }

        // Admin-role page endpoint: 403 forbidden page for non-admins.
        public IResult GuardedAdmin(HttpContext aCtx, Func<TERPSession, IResult> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            if (!string.Equals(vSession.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return Html(403, FPages.BuildForbiddenPage(ReadThemeCookie(aCtx),
                    ReadLanguageCookie(aCtx), "admin.forbidden_msg"));
            return aFn(vSession);
        }

        // Session-protected JSON endpoint: JSON 401 when signed out.
        public IResult GuardedJson(HttpContext aCtx, Func<TERPSession, IResult> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return JsonError(aCtx, 401, "Not signed in.");
            return aFn(vSession);
        }

        public async Task<IResult> GuardedJsonAsync(HttpContext aCtx,
            Func<TERPSession, Task<IResult>> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return JsonError(aCtx, 401, "Not signed in.");
            return await aFn(vSession).ConfigureAwait(false);
        }

        // Session-gated passkey-register-verify. Wrapping the GuardedJsonAsync call
        // here (rather than inline in the endpoint) keeps the Program.cs route
        // handler free of a nested Task<IResult> lambda, which the ASP0016 analyzer
        // flags as a (false-positive) discarded RequestDelegate return.
        public Task<IResult> PasskeyRegisterVerifyGuarded(HttpContext aCtx)
        {
            return GuardedJsonAsync(aCtx, s => PasskeyRegisterVerify(aCtx, s));
        }

        // ----- audit ----- //

        private void Audit(HttpContext aCtx, string aAction, string aEntityType,
            long aEntityId, string aDetails)
        {
            string vUsername = "";
            long vUserID = 0;
            TERPSession vSession;
            if (CurrentSession(aCtx, out vSession))
            {
                vUsername = vSession.Username;
                vUserID = vSession.UserId;
            }
            FDB.AddAuditLog(vUserID, vUsername, aAction, aEntityType, aEntityId,
                aDetails, ClientIPOf(aCtx));
        }

        // ----- login / logout / theme / language (public) ----- //

        public IResult LoginGet(HttpContext aCtx)
        {
            TERPSession vSession;
            if (CurrentSession(aCtx, out vSession))
                return Redirect("/");
            return Html(200, FPages.BuildLoginPage(ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx), ""));
        }

        public IResult LoginPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            string vUser = GetParam(aCtx, aForm, "username").Trim();
            string vPwd = GetParam(aCtx, aForm, "password");

            if (vUser == "" || vPwd == "")
                return Html(400, FPages.BuildLoginPage(vTheme, vLang,
                    I18n.T("login.required", vLang)));

            TERPUser vDBUser;
            if (!FDB.GetUserByUsername(vUser, out vDBUser))
            {
                FDB.AddAuditLog(0, vUser, "login_failed", "auth", 0, vUser, ClientIPOf(aCtx));
                return Html(401, FPages.BuildLoginPage(vTheme, vLang,
                    I18n.T("login.error", vLang)));
            }

            if (!Bcrypt.BcryptVerify(vPwd, vDBUser.PasswordHash))
            {
                FDB.AddAuditLog(0, vUser, "login_failed", "auth", 0, vUser, ClientIPOf(aCtx));
                return Html(401, FPages.BuildLoginPage(vTheme, vLang,
                    I18n.T("login.error", vLang)));
            }

            string vToken = FSessions.CreateSession(vDBUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            FDB.AddAuditLog(vDBUser.Id, vDBUser.Username, "login", "auth", 0,
                vDBUser.Username, ClientIPOf(aCtx));
            return Redirect("/");
        }

        public IResult Logout(HttpContext aCtx)
        {
            string vToken = ReadCookie(aCtx, CS_ERP_SESSION);
            TERPSession vSession;
            if (vToken != "" && FSessions.TryGet(vToken, out vSession))
                FDB.AddAuditLog(vSession.UserId, vSession.Username, "logout", "auth", 0,
                    vSession.Username, "");
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

        public IResult SetLanguage(HttpContext aCtx, IFormCollection aForm)
        {
            WriteLanguageCookie(aCtx, GetParam(aCtx, aForm, "lang"));
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

            if (vUserID <= 0)
                return JsonError(aCtx, 401, "Passkey did not match any user.");

            TERPUser vUser;
            if (!FDB.GetUserById(vUserID, out vUser))
                return JsonError(aCtx, 401, "User not found.");

            string vToken = FSessions.CreateSession(vUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            return Json(aCtx, 200, "{\"ok\":true,\"redirect\":\"/\"}");
        }

        public IResult PasskeyRegisterOptions(HttpContext aCtx, TERPSession aSession)
        {
            try
            {
                string vDisplay = aSession.DisplayName;
                if (vDisplay == "")
                    vDisplay = aSession.Username;
                return Json(aCtx, 200, GetPasskeys(aCtx).BeginRegister(aSession.UserId,
                    aSession.Username, vDisplay));
            }
            catch (Exception E)
            {
                return JsonError(aCtx, 400, E.Message);
            }
        }

        public async Task<IResult> PasskeyRegisterVerify(HttpContext aCtx, TERPSession aSession)
        {
            try
            {
                string vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
                if (vBody.Trim() == "")
                    return JsonError(aCtx, 400, "Empty request body.");
                string vName = GetParam(aCtx, null, "name").Trim();
                if (vName == "")
                    vName = "Passkey";
                string vResult = GetPasskeys(aCtx).FinishRegister(aSession.UserId, vBody, vName);
                Audit(aCtx, "passkey_add", "security", aSession.UserId, vName);
                return Json(aCtx, 200, "{\"ok\":true,\"detail\":" + vResult + "}");
            }
            catch (Exception E)
            {
                return JsonError(aCtx, 400, E.Message);
            }
        }

        public IResult SecurityGet(HttpContext aCtx, TERPSession aSession)
        {
            TERPPasskey[] vRows = FDB.GetPasskeysByUser(aSession.UserId);
            return Html(200, FPages.BuildSecurityPage(aSession.DisplayName, aSession.Role,
                ReadThemeCookie(aCtx), ReadLanguageCookie(aCtx), vRows));
        }

        public IResult SecurityPasskeyDelete(HttpContext aCtx, IFormCollection aForm,
            TERPSession aSession)
        {
            long vId = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            if (vId > 0)
            {
                try
                {
                    if (FDB.DeletePasskey(vId, aSession.UserId))
                        Audit(aCtx, "passkey_remove", "security", aSession.UserId, "");
                }
                catch { }
            }
            return Redirect("/security");
        }

        // ----- dashboard (live monitoring) ----- //

        // The live monitoring dashboard: KPI cards, a live chart and an events log,
        // all updated in real time by the push loop over the WebSocket. The initial
        // render only needs the live WS connection count (from the hub - the Kestrel
        // counterpart of the 60.HTML FHTTP.Count).
        //
        // The reused BuildMonitorDashboardPage hard-codes ws-connect="/" (the
        // self-hosted server accepted the WebSocket upgrade at the root path); the
        // adapter accepts it there too (AcceptWebSocketOnAnyPath in Program.cs).
        public IResult DashboardGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            int vCount = FHub != null ? FHub.Count : 0;

            string vHtml = FPages.BuildMonitorDashboardPage(aSession.DisplayName,
                aSession.Role, vTheme, vLang, vCount);
            return Html(200, vHtml);
        }

        // ----- realtime metric push (fragment builder) ----- //

        // Advance the simulated metrics one step (random walk) and build the htmx OOB
        // fragment bundle the push loop broadcasts. Reads the live WS connection count
        // from the hub. Returns the combined OOB HTML (one bundle of <div hx-swap-oob>
        // targets + a tbody afterbegin swap for the events table). Ported verbatim
        // from the 60.HTML host's BuildMetricsFragment (FHTTP.Count -> FHub.Count).
        //
        // Called by the LiveMonitorPushService BackgroundService (Program.cs) once
        // per ~1500 ms tick; its result is sent through ISgcHtmlHub.BroadcastAsync.
        public string BuildMetricsFragment()
        {
            string vLevel;
            string vLevelClass;
            string vMsg;
            int vR;

            lock (FMetricLock)
            {
                // Random-walk the metrics within sane bounds.
                FCpu = FCpu + (FRandom.NextDouble() * 2 - 1) * 6;
                if (FCpu < 2) FCpu = 2;
                if (FCpu > 98) FCpu = 98;
                FMem = FMem + (FRandom.NextDouble() * 2 - 1) * 3;
                if (FMem < 10) FMem = 10;
                if (FMem > 95) FMem = 95;
                FRps = FRps + (FRandom.NextDouble() * 2 - 1) * 25;
                if (FRps < 0) FRps = 0;
                if (FRps > 1000) FRps = 1000;
                FEventSeq++;

                // Pick an event level + message for this tick.
                vR = FRandom.Next(100);
                if (vR < 70)
                {
                    vLevel = "INFO";
                    vLevelClass = "bg-info";
                    vMsg = "Request handled in " + (8 + FRandom.Next(120)).ToString(
                        CultureInfo.InvariantCulture) + " ms";
                }
                else if (vR < 92)
                {
                    vLevel = "WARN";
                    vLevelClass = "bg-warning";
                    vMsg = "Slow query detected (" + (200 + FRandom.Next(800)).ToString(
                        CultureInfo.InvariantCulture) + " ms)";
                }
                else
                {
                    vLevel = "ERROR";
                    vLevelClass = "bg-danger";
                    vMsg = "Upstream timeout on worker #" + (1 + FRandom.Next(8)).ToString(
                        CultureInfo.InvariantCulture);
                }
            }

            int vConn = 0;
            try { if (FHub != null) vConn = FHub.Count; } catch { vConn = 0; }

            string vCpu = FCpu.ToString("0.0", CultureInfo.InvariantCulture);
            string vMem = FMem.ToString("0.0", CultureInfo.InvariantCulture);

            // One bundled HTML string carrying every hx-swap-oob fragment. htmx applies
            // each OOB element to the matching id in the live DOM.
            StringBuilder oBuf = new StringBuilder();
            // KPI values (innerHTML swap, the default for a bare hx-swap-oob="true").
            oBuf.Append("<div id=\"kpi-cpu\" hx-swap-oob=\"innerHTML\">").Append(vCpu)
                .Append(" %</div>");
            oBuf.Append("<div id=\"kpi-mem\" hx-swap-oob=\"innerHTML\">").Append(vMem)
                .Append(" %</div>");
            oBuf.Append("<div id=\"kpi-conn\" hx-swap-oob=\"innerHTML\">")
                .Append(vConn.ToString(CultureInfo.InvariantCulture)).Append("</div>");
            oBuf.Append("<div id=\"kpi-rps\" hx-swap-oob=\"innerHTML\">")
                .Append(((int)Math.Round(FRps)).ToString(CultureInfo.InvariantCulture))
                .Append("</div>");
            // Latest CPU value carrier the chart script polls (innerHTML swap).
            oBuf.Append("<div id=\"metric-cpu-val\" hx-swap-oob=\"innerHTML\">").Append(vCpu)
                .Append("</div>");
            // tbody OOB so the prepended <tr> is valid table content (afterbegin swap).
            oBuf.Append("<tbody id=\"events-body\" hx-swap-oob=\"afterbegin\"><tr>")
                .Append("<td class=\"text-nowrap small\">")
                .Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("</td><td><span class=\"badge ").Append(vLevelClass).Append("\">")
                .Append(vLevel).Append("</span></td><td class=\"small\">")
                .Append(HtmlEscServer(vMsg)).Append("</td></tr></tbody>");
            return oBuf.ToString();
        }

        // Minimal HTML-escape for the event message spliced into the OOB row markup.
        // Verbatim from the 60.HTML host.
        private static string HtmlEscServer(string aValue)
        {
            string vResult = (aValue ?? "").Replace("&", "&amp;");
            vResult = vResult.Replace("<", "&lt;");
            vResult = vResult.Replace(">", "&gt;");
            return vResult;
        }

        // ----- settings + admin ----- //

        private void ApplySettings()
        {
            if (FDB == null)
                return;
            if (FDB.GetSetting(CS_SETTING_COMPANY, "") == "")
                FDB.SetSetting(CS_SETTING_COMPANY, CS_DEFAULT_COMPANY);
            if (FDB.GetSetting(CS_SETTING_CURRENCY, "") == "")
                FDB.SetSetting(CS_SETTING_CURRENCY, CS_DEFAULT_CURRENCY);
            if (FDB.GetSetting(CS_SETTING_TAX_RATE, "") == "")
                FDB.SetSetting(CS_SETTING_TAX_RATE, CS_DEFAULT_TAX_RATE);
            if (FDB.GetSetting(CS_SETTING_INV_PREFIX, "") == "")
                FDB.SetSetting(CS_SETTING_INV_PREFIX, CS_DEFAULT_INV_PREFIX);
            if (FDB.GetSetting(CS_SETTING_SESSION_HOURS, "") == "")
                FDB.SetSetting(CS_SETTING_SESSION_HOURS, CS_DEFAULT_SESSION_HOURS);

            string vCompany = FDB.GetSetting(CS_SETTING_COMPANY, CS_DEFAULT_COMPANY);
            if (FPages != null)
                FPages.Brand = vCompany;
        }

        private KeyValuePair<string, string>[] EditableSettings()
        {
            KeyValuePair<string, string>[] vResult = new KeyValuePair<string, string>[5];
            vResult[0] = new KeyValuePair<string, string>(CS_SETTING_COMPANY,
                FDB.GetSetting(CS_SETTING_COMPANY, CS_DEFAULT_COMPANY));
            vResult[1] = new KeyValuePair<string, string>(CS_SETTING_CURRENCY,
                FDB.GetSetting(CS_SETTING_CURRENCY, CS_DEFAULT_CURRENCY));
            vResult[2] = new KeyValuePair<string, string>(CS_SETTING_TAX_RATE,
                FDB.GetSetting(CS_SETTING_TAX_RATE, CS_DEFAULT_TAX_RATE));
            vResult[3] = new KeyValuePair<string, string>(CS_SETTING_INV_PREFIX,
                FDB.GetSetting(CS_SETTING_INV_PREFIX, CS_DEFAULT_INV_PREFIX));
            vResult[4] = new KeyValuePair<string, string>(CS_SETTING_SESSION_HOURS,
                FDB.GetSetting(CS_SETTING_SESSION_HOURS, CS_DEFAULT_SESSION_HOURS));
            return vResult;
        }

        private KeyValuePair<string, string>[] ServerInfo()
        {
            string vStarted;
            if (FStartedAt > DateTime.MinValue)
                vStarted = FStartedAt.ToString("yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture);
            else
                vStarted = "-";
            KeyValuePair<string, string>[] vResult = new KeyValuePair<string, string>[4];
            vResult[0] = new KeyValuePair<string, string>("version",
                CS_MINIERP_SERVER_VERSION);
            vResult[1] = new KeyValuePair<string, string>("listen_port",
                FListenPort.ToString(CultureInfo.InvariantCulture));
            vResult[2] = new KeyValuePair<string, string>("db_file", FDB.DatabaseFile);
            vResult[3] = new KeyValuePair<string, string>("started", vStarted);
            return vResult;
        }

        public IResult AdminSettingsGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash;
            if (vFlashKey == "saved")
                vFlash = I18n.T("admin.saved", vLang);
            else
                vFlash = "";

            return Html(200, FPages.BuildAdminSettingsPage(EditableSettings(), ServerInfo(),
                aSession.DisplayName, aSession.Role, vTheme, vLang, vFlash));
        }

        public IResult AdminSettingsPost(HttpContext aCtx, IFormCollection aForm,
            TERPSession aSession)
        {
            try
            {
                FDB.SetSetting(CS_SETTING_COMPANY, GetParam(aCtx, aForm, CS_SETTING_COMPANY).Trim());
                FDB.SetSetting(CS_SETTING_CURRENCY, GetParam(aCtx, aForm, CS_SETTING_CURRENCY).Trim());
                FDB.SetSetting(CS_SETTING_TAX_RATE, GetParam(aCtx, aForm, CS_SETTING_TAX_RATE).Trim());
                FDB.SetSetting(CS_SETTING_INV_PREFIX, GetParam(aCtx, aForm, CS_SETTING_INV_PREFIX).Trim());
                FDB.SetSetting(CS_SETTING_SESSION_HOURS,
                    GetParam(aCtx, aForm, CS_SETTING_SESSION_HOURS).Trim());
                ApplySettings();
                Audit(aCtx, "settings_update", "settings", 0, "settings");
            }
            catch { }
            return Redirect("/admin?flash=saved");
        }

        public IResult AdminFirewallGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash;
            if (vFlashKey == "saved")
                vFlash = I18n.T("firewall.saved", vLang);
            else
                vFlash = "";

            TERPFirewallEntry[] vBlocked = FDB.ListBlockedIPs();
            TERPFirewallEntry[] vIgnored = FDB.ListIgnoredIPs();
            return Html(200, FPages.BuildAdminFirewallPage(vBlocked, vIgnored,
                aSession.DisplayName, aSession.Role, vTheme, vLang, vFlash));
        }

        public IResult AdminFirewallAdd(HttpContext aCtx, IFormCollection aForm, bool aBlocked)
        {
            string vIP = GetParam(aCtx, aForm, "ip").Trim();
            string vReason = GetParam(aCtx, aForm, "reason").Trim();
            if (vIP != "")
            {
                try
                {
                    if (aBlocked)
                    {
                        FDB.AddBlockedIP(vIP, vReason);
                        Audit(aCtx, "firewall_block", "firewall", 0, vIP);
                    }
                    else
                    {
                        FDB.AddIgnoredIP(vIP, vReason);
                        Audit(aCtx, "firewall_ignore", "firewall", 0, vIP);
                    }
                }
                catch { }
            }
            return Redirect("/admin/firewall?flash=saved");
        }

        public IResult AdminFirewallRemove(HttpContext aCtx, IFormCollection aForm, bool aBlocked)
        {
            string vIP = GetParam(aCtx, aForm, "ip").Trim();
            if (vIP != "")
            {
                try
                {
                    if (aBlocked)
                    {
                        FDB.RemoveBlockedIP(vIP);
                        Audit(aCtx, "firewall_unblock", "firewall", 0, vIP);
                    }
                    else
                    {
                        FDB.RemoveIgnoredIP(vIP);
                        Audit(aCtx, "firewall_unignore", "firewall", 0, vIP);
                    }
                }
                catch { }
            }
            return Redirect("/admin/firewall?flash=saved");
        }

        public IResult AdminAuditGet(HttpContext aCtx, TERPSession aSession)
        {
            const int CS_AUDIT_PAGE_SIZE = 50;
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vAction = GetParam(aCtx, null, "action").Trim();
            string vUser = GetParam(aCtx, null, "user").Trim();
            int vPage = StrToIntDef(GetParam(aCtx, null, "p"), 1);
            if (vPage < 1)
                vPage = 1;

            int vTotal = FDB.CountAuditLog(vAction, vUser);
            int vPageCount = (vTotal + CS_AUDIT_PAGE_SIZE - 1) / CS_AUDIT_PAGE_SIZE;
            if (vPageCount < 1)
                vPageCount = 1;
            if (vPage > vPageCount)
                vPage = vPageCount;

            TERPAuditRow[] vRows = FDB.ListAuditLog(vAction, vUser, CS_AUDIT_PAGE_SIZE,
                (vPage - 1) * CS_AUDIT_PAGE_SIZE);
            string[] vActions = new string[]
            {
                "login", "login_failed", "logout", "create", "update", "delete",
                "settings_update", "firewall_block", "firewall_unblock",
                "firewall_ignore", "firewall_unignore", "passkey_add", "passkey_remove"
            };

            return Html(200, FPages.BuildAdminAuditPage(vRows, vActions, vAction, vUser,
                vPage, vPageCount, aSession.DisplayName, aSession.Role, vTheme, vLang));
        }

        // ----- users management (admin only) ----- //

        public IResult UsersGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash;
            if (vFlashKey == "saved")
                vFlash = I18n.T("user.saved", vLang);
            else if (vFlashKey == "deleted")
                vFlash = I18n.T("user.deleted", vLang);
            else if (vFlashKey == "self")
                vFlash = I18n.T("user.cannot_delete_self", vLang);
            else if (vFlashKey == "lastadmin")
                vFlash = I18n.T("user.cannot_delete_last_admin", vLang);
            else
                vFlash = "";

            TERPUser[] vRows = FDB.ListUsers();
            return Html(200, FPages.BuildUsersPage(vRows, aSession.UserId,
                aSession.DisplayName, aSession.Role, vTheme, vLang, vFlash));
        }

        public IResult UserNewGet(HttpContext aCtx, TERPSession aSession)
        {
            TERPUser vUser = new TERPUser();
            vUser.Role = "user";
            return Html(200, FPages.BuildUserFormPage(vUser, true, "", aSession.DisplayName,
                aSession.Role, ReadThemeCookie(aCtx), ReadLanguageCookie(aCtx)));
        }

        public IResult UserEditGet(HttpContext aCtx, TERPSession aSession)
        {
            long vId = StrToInt64Def(GetParam(aCtx, null, "id"), 0);
            TERPUser vUser;
            if (vId <= 0 || !FDB.GetUserById(vId, out vUser))
                return Redirect("/users");
            vUser.PasswordHash = "";
            return Html(200, FPages.BuildUserFormPage(vUser, false, "", aSession.DisplayName,
                aSession.Role, ReadThemeCookie(aCtx), ReadLanguageCookie(aCtx)));
        }

        public IResult UserSavePost(HttpContext aCtx, IFormCollection aForm, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            TERPUser vUser = new TERPUser();
            vUser.Id = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            vUser.Username = GetParam(aCtx, aForm, "username").Trim();
            vUser.DisplayName = GetParam(aCtx, aForm, "display_name").Trim();
            vUser.Email = GetParam(aCtx, aForm, "email").Trim();
            vUser.Role = GetParam(aCtx, aForm, "role").Trim();
            string vPassword = GetParam(aCtx, aForm, "password");
            bool vIsNew = vUser.Id <= 0;

            if (string.Equals(vUser.Role, "admin", StringComparison.OrdinalIgnoreCase))
                vUser.Role = "admin";
            else
                vUser.Role = "user";

            Func<string, IResult> vRenderError = (aMsg) =>
                Html(400, FPages.BuildUserFormPage(vUser, vIsNew, aMsg, aSession.DisplayName,
                    aSession.Role, vTheme, vLang));

            if (vUser.Username == "")
                return vRenderError(I18n.T("user.username_required", vLang));
            if (FDB.UsernameExists(vUser.Username, vUser.Id))
                return vRenderError(I18n.T("user.username_taken", vLang));
            if (vIsNew && vPassword == "")
                return vRenderError(I18n.T("user.password_required", vLang));

            try
            {
                if (vIsNew)
                    Audit(aCtx, "create", "user", FDB.InsertUser(vUser,
                        Bcrypt.BcryptHash(vPassword)), vUser.Username);
                else
                {
                    FDB.UpdateUser(vUser);
                    if (vPassword != "")
                        FDB.UpdateUserPassword(vUser.Id, Bcrypt.BcryptHash(vPassword));
                    Audit(aCtx, "update", "user", vUser.Id, vUser.Username);
                }
            }
            catch (Exception E)
            {
                return Html(500, FPages.BuildUserFormPage(vUser, vIsNew, E.Message,
                    aSession.DisplayName, aSession.Role, vTheme, vLang));
            }

            return Redirect("/users?flash=saved");
        }

        public IResult UserDeletePost(HttpContext aCtx, IFormCollection aForm, TERPSession aSession)
        {
            long vId = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            if (vId <= 0)
                return Redirect("/users");

            if (vId == aSession.UserId)
                return Redirect("/users?flash=self");

            TERPUser vUser;
            if (!FDB.GetUserById(vId, out vUser))
            {
                vUser = new TERPUser();
                vUser.Username = "";
            }

            if (vUser.Username != "" &&
                string.Equals(vUser.Role, "admin", StringComparison.OrdinalIgnoreCase) &&
                FDB.CountAdmins() <= 1)
                return Redirect("/users?flash=lastadmin");

            try
            {
                FDB.DeleteUser(vId);
                Audit(aCtx, "delete", "user", vId, vUser.Username);
            }
            catch { }
            return Redirect("/users?flash=deleted");
        }
    }
}
