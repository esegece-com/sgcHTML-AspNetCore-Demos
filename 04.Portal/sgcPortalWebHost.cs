// ***************************************************************************
//  sgcPortalWeb - Customer Portal web-app demo on ASP.NET Core
//  Mirror of demos\60.HTML\04.Portal (TsgcWebSocketHTTPServer host).
//
//  This is the REWRITTEN host layer, following the 01.ERP / 02.AdminCRUD host
//  pattern. It reproduces every branch of the 60.HTML sgcPortal_Server.cs
//  DispatchRequest, but on Kestrel:
//    - The reusable logic (Bcrypt, DB, I18n, Pages, Passkeys, Sessions, Types,
//      Config) is copied VERBATIM from the 60.HTML demo and NOT changed here.
//    - The 60.HTML host scanned RawHeaders for the Cookie line and wrote
//      Set-Cookie / redirects through CustomHeaders on TsgcWSHTTPResponseInfo.
//      Here every cookie is read/written through the native ASP.NET Core cookie
//      API (ctx.Request.Cookies / ctx.Response.Cookies) with the SAME cookie
//      names + attributes, and each handler returns an IResult.
//    - Query + form params are merged through GetParam / GetParamArray, matching
//      the Delphi ARequestInfo.Params (query first, then the urlencoded body).
//    - PASSKEYS: the 60.HTML host hard-coded RP id 'localhost' + origin
//      http://localhost:<port>. Under Kestrel the RP id (request host name) and
//      origin (scheme://host) are DERIVED from the live request and threaded into
//      the reused TERPPasskeys. A per-origin instance is cached so the WebAuthn
//      begin/finish challenge state survives across the two requests of a
//      ceremony (see GetPasskeys).
//
//  PORTAL-SPECIFIC (the dual-area role model). Unlike the ERP / AdminCRUD ports
//  (single back-office role set), the Portal demo has TWO areas gated by role:
//    - role 'customer' -> the self-service area only: the account dashboard (/),
//      /orders, /orders/view, /profile, /profile/save, /support. Every one of
//      these scopes its data to session.CustomerId server-side, so a customer can
//      only ever see / edit their own account. Any OTHER in-app path is refused.
//    - role 'user' / 'admin' -> the back-office (customers / providers / products
//      / invoices / reports). 'admin' additionally owns users + settings +
//      firewall + audit.
//  The 60.HTML DispatchRequest enforced this with a role branch (the 'customer'
//  block handled the self-service routes and returned 403 for anything else; the
//  back-office routes further checked role == 'admin' for /users + /admin). Here
//  the same model is reproduced through four per-endpoint guards, applied in
//  Program.cs:
//    - Guarded         : any signed-in role (used by /security + passkey mgmt,
//                        reachable by customers too - it manages their own
//                        passkeys).
//    - GuardedStaff    : signed-in AND role != 'customer' (403 for a customer) -
//                        the back-office CRUD. Matches the 60.HTML 'customer'
//                        block returning 403 for every back-office path.
//    - GuardedAdmin    : signed-in AND role == 'admin' (403 otherwise) - users +
//                        admin section.
//    - GuardedCustomer : signed-in AND role == 'customer' (redirect '/' otherwise)
//                        - the self-service routes. A staff login hitting a
//                        customer route lands back on its own dashboard, matching
//                        the 60.HTML fallthrough (Redirect '/').
//  The root '/' is role-branched by RootGet (customer -> account dashboard,
//  staff -> back-office dashboard), exactly as the 60.HTML dispatcher did.
//
//  The two host files that were NOT copied from 60.HTML are sgcPortal_Server.cs
//  (the TsgcWebSocketHTTPServer host) and the console Program.cs.
//
//  IMPORTANT hosting note (the reusable pattern): a Minimal API lambda whose ONLY
//  parameter is HttpContext and which returns Task<IResult> is treated as a raw
//  RequestDelegate, so its IResult is DISCARDED (ASP0016) - the handler's cookie
//  side effects run but the redirect / status / body are lost. To avoid that trap
//  uniformly, every endpoint lambda returns plain Task and the Run / RunForm
//  helpers execute the handler's IResult onto the response explicitly.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Portal
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, passkey factory) and exposes one IResult-returning method per
    // 60.HTML DispatchRequest branch. Program.cs maps the Minimal API endpoints
    // onto these methods and applies the auth / role gates through Guarded*.
    public sealed class PortalWebHost : IDisposable
    {
        public const string CS_PORTAL_SERVER_VERSION = "1.0.0";

        // Cookie names - identical to the 60.HTML host so behavior matches exactly.
        public const string CS_ERP_SESSION = "merp_session";
        public const string CS_THEME_COOKIE = "merp_theme";
        public const string CS_LANG_COOKIE = "merp_lang";

        // Self-contained eSeGeCe favicon (served at /favicon.svg). Emerald to
        // match the Customer Portal accent. Verbatim from the 60.HTML host.
        public const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"eSeGeCe\">" +
            "<rect width=\"64\" height=\"64\" rx=\"14\" fill=\"#10B981\"/>" +
            "<text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"900\" font-size=\"44\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">e</text></svg>";

        // Editable settings (settings table) + their defaults. Order drives the form.
        public const string CS_SETTING_COMPANY = "company_name";
        public const string CS_SETTING_CURRENCY = "default_currency";
        public const string CS_SETTING_TAX_RATE = "default_tax_rate";
        public const string CS_SETTING_INV_PREFIX = "invoice_prefix";
        public const string CS_SETTING_SESSION_HOURS = "session_timeout_hours";

        public const string CS_DEFAULT_COMPANY = "Customer Portal Demo";
        public const string CS_DEFAULT_CURRENCY = "EUR";
        public const string CS_DEFAULT_TAX_RATE = "21";
        public const string CS_DEFAULT_INV_PREFIX = "INV-";
        public const string CS_DEFAULT_SESSION_HOURS = "8";

        private readonly TERPDBPool FDB;
        private readonly TERPSessionStore FSessions;
        private readonly TERPPages FPages;
        private readonly int FListenPort;
        private readonly DateTime FStartedAt;

        // Per-origin passkey cache. WebAuthn begin/finish share challenge state in
        // one TERPPasskeys instance, so a ceremony's two requests (same browser,
        // same origin) must hit the SAME instance. Keyed by "rpId|origin".
        private readonly Dictionary<string, TERPPasskeys> FPasskeys =
            new Dictionary<string, TERPPasskeys>(StringComparer.Ordinal);
        private readonly object FPasskeysLock = new object();

        // Builds the DB (schema + admin seed + demo data + the demo customer login),
        // the session store, the page builder and seeds the settings - mirroring
        // TsgcPortalServer.Start.
        public PortalWebHost(TERPServerConfig aConfig, string aDatabasePath, int aListenPort)
        {
            FListenPort = aListenPort;
            FStartedAt = DateTime.Now;

            FDB = new TERPDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedAdmin(aConfig.AdminUser, Bcrypt.BcryptHash(aConfig.AdminPassword));
            // Populate the demo the first time the DB is empty so the dashboard /
            // report charts look full. Idempotent.
            FDB.SeedDemoDataIfEmpty();
            // Seed the demo customer login (customer/customer, role 'customer') tied
            // to the first seeded customer so the portal can be shown as a customer,
            // not just admin. Idempotent: a no-op once it exists.
            FDB.SeedCustomerUserIfMissing("customer", Bcrypt.BcryptHash("customer"));

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
                    vPk = new TERPPasskeys(FDB, vRPID, "Customer Portal", vOrigin);
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

        // Every value for aName in order (query values, then form values). Used for
        // the parallel invoice-line arrays.
        private static string[] GetParamArray(HttpContext aCtx, IFormCollection aForm, string aName)
        {
            List<string> vResult = new List<string>();
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues))
                for (int vI = 0; vI < vValues.Count; vI++)
                    vResult.Add(vValues[vI] ?? "");
            if (aForm != null && aForm.TryGetValue(aName, out vValues))
                for (int vI = 0; vI < vValues.Count; vI++)
                    vResult.Add(vValues[vI] ?? "");
            return vResult.ToArray();
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

        private static double ParseFormFloat(string aValue)
        {
            string vText = (aValue ?? "").Trim();
            if (vText.Length == 0)
                return 0;
            vText = vText.Replace(",", ".");
            double vResult;
            if (double.TryParse(vText, NumberStyles.Float, CultureInfo.InvariantCulture,
                out vResult))
                return vResult;
            return 0;
        }

        private static DateTime ParseFormDate(string aValue)
        {
            string vText = (aValue ?? "").Trim();
            if (vText.Length == 0)
                return DateTime.MinValue;
            DateTime vResult;
            if (DateTime.TryParseExact(vText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out vResult))
                return vResult;
            return DateTime.MinValue;
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
            // Light is the default Customer Portal theme (airy / emerald).
            return "light";
        }

        private void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vS = (aTheme ?? "").Trim().ToLowerInvariant();
            if (vS != "light" && vS != "dark" && vS != "system")
                vS = "light";
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

        private static bool IsCustomer(TERPSession aSession)
        {
            return aSession != null &&
                string.Equals(aSession.Role, "customer", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdmin(TERPSession aSession)
        {
            return aSession != null &&
                string.Equals(aSession.Role, "admin", StringComparison.OrdinalIgnoreCase);
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

        // ----- auth / role gates (used by Program.cs endpoint mapping) ----- //

        // Session-protected page endpoint (ANY role, customers included): redirect
        // to /login when signed out. Used by /security + passkey management, which a
        // customer may reach to manage its own passkeys.
        public IResult Guarded(HttpContext aCtx, Func<TERPSession, IResult> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            return aFn(vSession);
        }

        // Back-office (staff) page endpoint: signed-in AND role != 'customer'. A
        // customer gets the 403 forbidden page, mirroring the 60.HTML 'customer'
        // block that refused every back-office path.
        public IResult GuardedStaff(HttpContext aCtx, Func<TERPSession, IResult> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            if (IsCustomer(vSession))
                return Html(403, FPages.BuildForbiddenPage(ReadThemeCookie(aCtx),
                    ReadLanguageCookie(aCtx), "admin.forbidden_msg"));
            return aFn(vSession);
        }

        // Admin-role page endpoint: 403 forbidden page for non-admins (customers and
        // plain users alike), mirroring the 60.HTML /users + /admin role check.
        public IResult GuardedAdmin(HttpContext aCtx, Func<TERPSession, IResult> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            if (!IsAdmin(vSession))
                return Html(403, FPages.BuildForbiddenPage(ReadThemeCookie(aCtx),
                    ReadLanguageCookie(aCtx), "admin.forbidden_msg"));
            return aFn(vSession);
        }

        // Customer self-service page endpoint: signed-in AND role == 'customer'. A
        // staff login (admin / user) is sent back to '/', mirroring the 60.HTML
        // fallthrough where a non-customer hitting a customer route was redirected to
        // the root (its own back-office dashboard).
        public IResult GuardedCustomer(HttpContext aCtx, Func<TERPSession, IResult> aFn)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            if (!IsCustomer(vSession))
                return Redirect("/");
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

        // ----- root (role-branched: customer -> account, staff -> dashboard) ----- //

        // Mirrors the 60.HTML dispatcher: '/' resolves to the customer account
        // dashboard for a 'customer' login, and to the back-office dashboard for a
        // staff (admin / user) login.
        public IResult RootGet(HttpContext aCtx)
        {
            TERPSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            if (IsCustomer(vSession))
                return AccountDashboardGet(aCtx, vSession);
            return DashboardGet(aCtx, vSession);
        }

        // ----- back-office dashboard (staff) ----- //

        public IResult DashboardGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            int[] vCounts = new int[4];
            vCounts[0] = FDB.CountCustomers();
            vCounts[1] = FDB.CountProducts();
            vCounts[2] = FDB.CountProviders();
            vCounts[3] = FDB.CountInvoices();
            double vRevenue = FDB.SumInvoiceTotal("");
            string vCurrency = FDB.GetSetting(CS_SETTING_CURRENCY, CS_DEFAULT_CURRENCY);
            int[] vByStatus = new int[4];
            vByStatus[0] = FDB.CountInvoicesByStatus("draft");
            vByStatus[1] = FDB.CountInvoicesByStatus("sent");
            vByStatus[2] = FDB.CountInvoicesByStatus("paid");
            vByStatus[3] = FDB.CountInvoicesByStatus("cancelled");
            TERPRevenueMonth[] vMonths = FDB.InvoiceRevenueByMonth(6);
            TERPInvoiceListRow[] vRecent = FDB.RecentInvoices(8);

            return Html(200, FPages.BuildDashboardPage(aSession.DisplayName, aSession.Role,
                vTheme, vLang, vCounts, vRevenue, vCurrency, vByStatus, vMonths, vRecent));
        }

        // ----- reports / statistics (staff) ----- //

        public IResult ReportsGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            string vG = GetParam(aCtx, null, "g").Trim().ToLowerInvariant();
            if (vG != "day" && vG != "week" && vG != "month")
                vG = "month";

            int vBuckets;
            if (vG == "day")
                vBuckets = 30;
            else
                vBuckets = 12;

            string vCurrency = FDB.GetSetting(CS_SETTING_CURRENCY, CS_DEFAULT_CURRENCY);

            TERPRevenueMonth[] vSeries = FDB.InvoiceSeries(vG, vBuckets);
            int vCurCount, vPrevCount;
            double vCurRevenue, vPrevRevenue;
            int vCurNew, vPrevNew;
            FDB.PeriodSummary(vG, true, out vCurCount, out vCurRevenue, out vCurNew);
            FDB.PeriodSummary(vG, false, out vPrevCount, out vPrevRevenue, out vPrevNew);
            TERPInvoiceListRow[] vTop = FDB.TopCustomersByRevenue(5);

            return Html(200, FPages.BuildReportsPage(vG, vSeries, vCurCount, vCurRevenue,
                vCurNew, vPrevCount, vPrevRevenue, vPrevNew, vTop, vCurrency,
                aSession.DisplayName, aSession.Role, vTheme, vLang));
        }

        // ----- customers CRUD (staff) ----- //

        public IResult CustomersGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vSearch = GetParam(aCtx, null, "q").Trim();
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash;
            if (vFlashKey == "saved")
                vFlash = I18n.T("customer.saved", vLang);
            else if (vFlashKey == "deleted")
                vFlash = I18n.T("customer.deleted", vLang);
            else
                vFlash = "";

            TERPCustomer[] vRows = FDB.ListCustomers(vSearch);
            return Html(200, FPages.BuildCustomersPage(vRows, vSearch, aSession.DisplayName,
                aSession.Role, vTheme, vLang, vFlash));
        }

        public IResult CustomerNewGet(HttpContext aCtx, TERPSession aSession)
        {
            TERPCustomer vCust = new TERPCustomer();
            return Html(200, FPages.BuildCustomerFormPage(vCust, true, "",
                aSession.DisplayName, aSession.Role, ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx)));
        }

        public IResult CustomerEditGet(HttpContext aCtx, TERPSession aSession)
        {
            long vId = StrToInt64Def(GetParam(aCtx, null, "id"), 0);
            TERPCustomer vCust;
            if (vId <= 0 || !FDB.GetCustomer(vId, out vCust))
                return Redirect("/customers");
            return Html(200, FPages.BuildCustomerFormPage(vCust, false, "",
                aSession.DisplayName, aSession.Role, ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx)));
        }

        public IResult CustomerSavePost(HttpContext aCtx, IFormCollection aForm,
            TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            TERPCustomer vCust = new TERPCustomer();
            vCust.Id = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            vCust.Code = GetParam(aCtx, aForm, "code").Trim();
            vCust.Name = GetParam(aCtx, aForm, "name").Trim();
            vCust.TaxID = GetParam(aCtx, aForm, "tax_id").Trim();
            vCust.Email = GetParam(aCtx, aForm, "email").Trim();
            vCust.Phone = GetParam(aCtx, aForm, "phone").Trim();
            vCust.Address = GetParam(aCtx, aForm, "address").Trim();
            vCust.City = GetParam(aCtx, aForm, "city").Trim();
            vCust.Country = GetParam(aCtx, aForm, "country").Trim();
            vCust.Notes = GetParam(aCtx, aForm, "notes");

            if (vCust.Name == "")
                return Html(400, FPages.BuildCustomerFormPage(vCust, vCust.Id <= 0,
                    I18n.T("customer.name_required", vLang), aSession.DisplayName,
                    aSession.Role, vTheme, vLang));

            try
            {
                if (vCust.Id > 0)
                {
                    FDB.UpdateCustomer(vCust);
                    Audit(aCtx, "update", "customer", vCust.Id, vCust.Name);
                }
                else
                    Audit(aCtx, "create", "customer", FDB.InsertCustomer(vCust), vCust.Name);
            }
            catch (Exception E)
            {
                return Html(500, FPages.BuildCustomerFormPage(vCust, vCust.Id <= 0,
                    E.Message, aSession.DisplayName, aSession.Role, vTheme, vLang));
            }

            return Redirect("/customers?flash=saved");
        }

        public IResult CustomerDeletePost(HttpContext aCtx, IFormCollection aForm)
        {
            long vId = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            if (vId > 0)
            {
                string vLabel = "";
                TERPCustomer vCust;
                if (FDB.GetCustomer(vId, out vCust))
                    vLabel = vCust.Name;
                try
                {
                    FDB.DeleteCustomer(vId);
                    Audit(aCtx, "delete", "customer", vId, vLabel);
                }
                catch { }
            }
            return Redirect("/customers?flash=deleted");
        }

        // ----- providers CRUD (staff) ----- //

        public IResult ProvidersGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vSearch = GetParam(aCtx, null, "q").Trim();
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash;
            if (vFlashKey == "saved")
                vFlash = I18n.T("provider.saved", vLang);
            else if (vFlashKey == "deleted")
                vFlash = I18n.T("provider.deleted", vLang);
            else
                vFlash = "";

            TERPProvider[] vRows = FDB.ListProviders(vSearch);
            return Html(200, FPages.BuildProvidersPage(vRows, vSearch, aSession.DisplayName,
                aSession.Role, vTheme, vLang, vFlash));
        }

        public IResult ProviderNewGet(HttpContext aCtx, TERPSession aSession)
        {
            TERPProvider vProv = new TERPProvider();
            return Html(200, FPages.BuildProviderFormPage(vProv, true, "",
                aSession.DisplayName, aSession.Role, ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx)));
        }

        public IResult ProviderEditGet(HttpContext aCtx, TERPSession aSession)
        {
            long vId = StrToInt64Def(GetParam(aCtx, null, "id"), 0);
            TERPProvider vProv;
            if (vId <= 0 || !FDB.GetProvider(vId, out vProv))
                return Redirect("/providers");
            return Html(200, FPages.BuildProviderFormPage(vProv, false, "",
                aSession.DisplayName, aSession.Role, ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx)));
        }

        public IResult ProviderSavePost(HttpContext aCtx, IFormCollection aForm,
            TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            TERPProvider vProv = new TERPProvider();
            vProv.Id = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            vProv.Code = GetParam(aCtx, aForm, "code").Trim();
            vProv.Name = GetParam(aCtx, aForm, "name").Trim();
            vProv.TaxID = GetParam(aCtx, aForm, "tax_id").Trim();
            vProv.Email = GetParam(aCtx, aForm, "email").Trim();
            vProv.Phone = GetParam(aCtx, aForm, "phone").Trim();
            vProv.Address = GetParam(aCtx, aForm, "address").Trim();
            vProv.City = GetParam(aCtx, aForm, "city").Trim();
            vProv.Country = GetParam(aCtx, aForm, "country").Trim();
            vProv.Notes = GetParam(aCtx, aForm, "notes");

            if (vProv.Name == "")
                return Html(400, FPages.BuildProviderFormPage(vProv, vProv.Id <= 0,
                    I18n.T("provider.name_required", vLang), aSession.DisplayName,
                    aSession.Role, vTheme, vLang));

            try
            {
                if (vProv.Id > 0)
                {
                    FDB.UpdateProvider(vProv);
                    Audit(aCtx, "update", "provider", vProv.Id, vProv.Name);
                }
                else
                    Audit(aCtx, "create", "provider", FDB.InsertProvider(vProv), vProv.Name);
            }
            catch (Exception E)
            {
                return Html(500, FPages.BuildProviderFormPage(vProv, vProv.Id <= 0,
                    E.Message, aSession.DisplayName, aSession.Role, vTheme, vLang));
            }

            return Redirect("/providers?flash=saved");
        }

        public IResult ProviderDeletePost(HttpContext aCtx, IFormCollection aForm)
        {
            long vId = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            if (vId > 0)
            {
                string vLabel = "";
                TERPProvider vProv;
                if (FDB.GetProvider(vId, out vProv))
                    vLabel = vProv.Name;
                try
                {
                    FDB.DeleteProvider(vId);
                    Audit(aCtx, "delete", "provider", vId, vLabel);
                }
                catch { }
            }
            return Redirect("/providers?flash=deleted");
        }

        // ----- products CRUD (staff; page-based forms, as in the 60.HTML Portal) --- //

        public IResult ProductsGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vSearch = GetParam(aCtx, null, "q").Trim();
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash;
            if (vFlashKey == "saved")
                vFlash = I18n.T("product.saved", vLang);
            else if (vFlashKey == "deleted")
                vFlash = I18n.T("product.deleted", vLang);
            else
                vFlash = "";

            TERPProduct[] vRows = FDB.ListProducts(vSearch);
            return Html(200, FPages.BuildProductsPage(vRows, vSearch, aSession.DisplayName,
                aSession.Role, vTheme, vLang, vFlash));
        }

        public IResult ProductNewGet(HttpContext aCtx, TERPSession aSession)
        {
            TERPProduct vProd = new TERPProduct();
            return Html(200, FPages.BuildProductFormPage(vProd, true, "",
                aSession.DisplayName, aSession.Role, ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx)));
        }

        public IResult ProductEditGet(HttpContext aCtx, TERPSession aSession)
        {
            long vId = StrToInt64Def(GetParam(aCtx, null, "id"), 0);
            TERPProduct vProd;
            if (vId <= 0 || !FDB.GetProduct(vId, out vProd))
                return Redirect("/products");
            return Html(200, FPages.BuildProductFormPage(vProd, false, "",
                aSession.DisplayName, aSession.Role, ReadThemeCookie(aCtx),
                ReadLanguageCookie(aCtx)));
        }

        public IResult ProductSavePost(HttpContext aCtx, IFormCollection aForm,
            TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            TERPProduct vProd = new TERPProduct();
            vProd.Id = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            vProd.Code = GetParam(aCtx, aForm, "code").Trim();
            vProd.Name = GetParam(aCtx, aForm, "name").Trim();
            vProd.Description = GetParam(aCtx, aForm, "description");
            vProd.Unit_ = GetParam(aCtx, aForm, "unit").Trim();
            vProd.Price = ParseFormFloat(GetParam(aCtx, aForm, "price"));
            vProd.TaxRate = ParseFormFloat(GetParam(aCtx, aForm, "tax_rate"));

            if (vProd.Name == "")
                return Html(400, FPages.BuildProductFormPage(vProd, vProd.Id <= 0,
                    I18n.T("product.name_required", vLang), aSession.DisplayName,
                    aSession.Role, vTheme, vLang));

            try
            {
                if (vProd.Id > 0)
                {
                    FDB.UpdateProduct(vProd);
                    Audit(aCtx, "update", "product", vProd.Id, vProd.Name);
                }
                else
                    Audit(aCtx, "create", "product", FDB.InsertProduct(vProd), vProd.Name);
            }
            catch (Exception E)
            {
                return Html(500, FPages.BuildProductFormPage(vProd, vProd.Id <= 0,
                    E.Message, aSession.DisplayName, aSession.Role, vTheme, vLang));
            }

            return Redirect("/products?flash=saved");
        }

        public IResult ProductDeletePost(HttpContext aCtx, IFormCollection aForm)
        {
            long vId = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            if (vId > 0)
            {
                string vLabel = "";
                TERPProduct vProd;
                if (FDB.GetProduct(vId, out vProd))
                    vLabel = vProd.Name;
                try
                {
                    FDB.DeleteProduct(vId);
                    Audit(aCtx, "delete", "product", vId, vLabel);
                }
                catch { }
            }
            return Redirect("/products?flash=deleted");
        }

        // ----- invoices CRUD (staff) ----- //

        public IResult InvoicesGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vSearch = GetParam(aCtx, null, "q").Trim();
            string vStatus = GetParam(aCtx, null, "status").Trim();
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash;
            if (vFlashKey == "saved")
                vFlash = I18n.T("invoice.saved", vLang);
            else if (vFlashKey == "deleted")
                vFlash = I18n.T("invoice.deleted", vLang);
            else
                vFlash = "";

            TERPInvoiceListRow[] vRows = FDB.ListInvoices(vSearch, vStatus);
            return Html(200, FPages.BuildInvoicesPage(vRows, vSearch, vStatus,
                aSession.DisplayName, aSession.Role, vTheme, vLang, vFlash));
        }

        public IResult InvoiceNewGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            TERPInvoice vInv = new TERPInvoice();
            TERPInvoiceLine[] vLines = new TERPInvoiceLine[0];
            vInv.IssueDate = DateTime.Now.Date;
            vInv.Status = "draft";
            vInv.Currency = FDB.GetSetting(CS_SETTING_CURRENCY, CS_DEFAULT_CURRENCY);
            vInv.TaxRate = ParseFormFloat(FDB.GetSetting(CS_SETTING_TAX_RATE, CS_DEFAULT_TAX_RATE));
            TERPCustomer[] vCustomers = FDB.ListCustomers("");
            TERPProduct[] vProducts = FDB.ListProducts("");
            return Html(200, FPages.BuildInvoiceFormPage(vInv, vLines, vCustomers, vProducts,
                true, "", aSession.DisplayName, aSession.Role, vTheme, vLang));
        }

        public IResult InvoiceEditGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            long vId = StrToInt64Def(GetParam(aCtx, null, "id"), 0);
            TERPInvoice vInv;
            TERPInvoiceLine[] vLines;
            if (vId <= 0 || !FDB.GetInvoice(vId, out vInv, out vLines))
                return Redirect("/invoices");
            TERPCustomer[] vCustomers = FDB.ListCustomers("");
            TERPProduct[] vProducts = FDB.ListProducts("");
            return Html(200, FPages.BuildInvoiceFormPage(vInv, vLines, vCustomers, vProducts,
                false, "", aSession.DisplayName, aSession.Role, vTheme, vLang));
        }

        public IResult InvoiceSavePost(HttpContext aCtx, IFormCollection aForm,
            TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            TERPInvoice vInv = new TERPInvoice();
            List<TERPInvoiceLine> vLineList = new List<TERPInvoiceLine>();

            vInv.Id = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            vInv.Number = GetParam(aCtx, aForm, "number").Trim();
            vInv.CustomerId = StrToInt64Def(GetParam(aCtx, aForm, "customer_id"), 0);
            vInv.IssueDate = ParseFormDate(GetParam(aCtx, aForm, "issue_date"));
            vInv.DueDate = ParseFormDate(GetParam(aCtx, aForm, "due_date"));
            vInv.Status = GetParam(aCtx, aForm, "status").Trim().ToLowerInvariant();
            vInv.Currency = GetParam(aCtx, aForm, "currency").Trim();
            vInv.TaxRate = ParseFormFloat(GetParam(aCtx, aForm, "tax_rate"));
            vInv.Notes = GetParam(aCtx, aForm, "notes");

            if (vInv.Status != "draft" && vInv.Status != "sent" &&
                vInv.Status != "paid" && vInv.Status != "cancelled")
                vInv.Status = "draft";
            if (vInv.Currency == "")
                vInv.Currency = FDB.GetSetting(CS_SETTING_CURRENCY, CS_DEFAULT_CURRENCY);
            if (vInv.IssueDate <= DateTime.MinValue)
                vInv.IssueDate = DateTime.Now.Date;

            string[] vProductIds = GetParamArray(aCtx, aForm, "line_product");
            string[] vDescs = GetParamArray(aCtx, aForm, "line_desc");
            string[] vQtys = GetParamArray(aCtx, aForm, "line_qty");
            string[] vPrices = GetParamArray(aCtx, aForm, "line_price");

            vInv.Subtotal = 0;
            int vCount = vDescs.Length;
            if (vQtys.Length > vCount)
                vCount = vQtys.Length;
            if (vPrices.Length > vCount)
                vCount = vPrices.Length;
            if (vProductIds.Length > vCount)
                vCount = vProductIds.Length;
            for (int vI = 0; vI < vCount; vI++)
            {
                TERPInvoiceLine vLine = new TERPInvoiceLine();
                vLine.Id = 0;
                vLine.InvoiceId = 0;
                long vProductId;
                if (vI < vProductIds.Length)
                    vProductId = StrToInt64Def(vProductIds[vI].Trim(), 0);
                else
                    vProductId = 0;
                if (vProductId < 0)
                    vProductId = 0;
                vLine.ProductId = vProductId;
                if (vI < vDescs.Length)
                    vLine.Description = vDescs[vI].Trim();
                else
                    vLine.Description = "";
                double vQty;
                if (vI < vQtys.Length)
                    vQty = ParseFormFloat(vQtys[vI]);
                else
                    vQty = 0;
                double vPrice;
                if (vI < vPrices.Length)
                    vPrice = ParseFormFloat(vPrices[vI]);
                else
                    vPrice = 0;
                if (vLine.ProductId == 0 && vLine.Description == "" && vQty == 0 && vPrice == 0)
                    continue;
                vLine.Quantity = vQty;
                vLine.UnitPrice = vPrice;
                vLine.LineTotal = vQty * vPrice;
                vInv.Subtotal += vLine.LineTotal;
                vLineList.Add(vLine);
            }

            vInv.TaxAmount = vInv.Subtotal * vInv.TaxRate / 100;
            vInv.Total = vInv.Subtotal + vInv.TaxAmount;

            TERPInvoiceLine[] vLines = vLineList.ToArray();

            Func<string, int, IResult> vFailWith = (aMessageKey, aCode) =>
            {
                TERPCustomer[] vC = FDB.ListCustomers("");
                TERPProduct[] vP = FDB.ListProducts("");
                return Html(aCode, FPages.BuildInvoiceFormPage(vInv, vLines, vC, vP,
                    vInv.Id <= 0, I18n.T(aMessageKey, vLang), aSession.DisplayName,
                    aSession.Role, vTheme, vLang));
            };

            if (vInv.CustomerId <= 0)
                return vFailWith("invoice.customer_required", 400);
            if (vLines.Length == 0)
                return vFailWith("invoice.none", 400);

            if (vInv.Number == "")
            {
                string vPrefix = FDB.GetSetting(CS_SETTING_INV_PREFIX, CS_DEFAULT_INV_PREFIX);
                if (vInv.Id > 0)
                    vInv.Number = vPrefix + vInv.Id.ToString("D5", CultureInfo.InvariantCulture);
                else
                    vInv.Number = vPrefix + DateTime.Now.ToString("yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture);
            }

            try
            {
                if (vInv.Id > 0)
                {
                    FDB.UpdateInvoice(vInv, vLines);
                    Audit(aCtx, "update", "invoice", vInv.Id, vInv.Number);
                }
                else
                {
                    long vNewId = FDB.InsertInvoice(vInv, vLines);
                    if (GetParam(aCtx, aForm, "number").Trim() == "" && vNewId > 0)
                    {
                        vInv.Id = vNewId;
                        vInv.Number = FDB.GetSetting(CS_SETTING_INV_PREFIX,
                            CS_DEFAULT_INV_PREFIX) + vNewId.ToString("D5",
                            CultureInfo.InvariantCulture);
                        FDB.UpdateInvoice(vInv, vLines);
                    }
                    Audit(aCtx, "create", "invoice", vNewId, vInv.Number);
                }
            }
            catch (Exception E)
            {
                TERPCustomer[] vC = FDB.ListCustomers("");
                TERPProduct[] vP = FDB.ListProducts("");
                return Html(500, FPages.BuildInvoiceFormPage(vInv, vLines, vC, vP,
                    vInv.Id <= 0, E.Message, aSession.DisplayName, aSession.Role,
                    vTheme, vLang));
            }

            return Redirect("/invoices?flash=saved");
        }

        public IResult InvoiceDeletePost(HttpContext aCtx, IFormCollection aForm)
        {
            long vId = StrToInt64Def(GetParam(aCtx, aForm, "id"), 0);
            if (vId > 0)
            {
                string vLabel = "";
                TERPInvoice vInv;
                TERPInvoiceLine[] vLines;
                if (FDB.GetInvoice(vId, out vInv, out vLines))
                    vLabel = vInv.Number;
                try
                {
                    FDB.DeleteInvoice(vId);
                    Audit(aCtx, "delete", "invoice", vId, vLabel);
                }
                catch { }
            }
            return Redirect("/invoices?flash=deleted");
        }

        // ----- customer portal (role 'customer' only; gated by GuardedCustomer) --- //
        // Every one of these scopes its data to aSession.CustomerId server-side so a
        // customer can only ever see / edit their own account.

        // GET / (for a customer login): the account dashboard (summary cards +
        // recent orders). Reached via RootGet's role branch.
        public IResult AccountDashboardGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            long vCid = aSession.CustomerId;
            string vCurrency = FDB.GetSetting(CS_SETTING_CURRENCY, CS_DEFAULT_CURRENCY);
            // All figures are scoped to the session's customer id, never to anything
            // from the request.
            int vOrderCount = FDB.CountInvoicesByCustomer(vCid);
            double vTotalSpend = FDB.SumInvoiceTotalByCustomer(vCid, "");
            double vPaid = FDB.SumInvoiceTotalByCustomer(vCid, "paid");
            // Outstanding = issued (sent) but not yet paid.
            double vOutstanding = FDB.SumInvoiceTotalByCustomer(vCid, "sent");
            TERPInvoiceListRow[] vRecent = FDB.ListInvoicesByCustomer(vCid, "");
            if (vRecent.Length > 5)
            {
                TERPInvoiceListRow[] vTrimmed = new TERPInvoiceListRow[5];
                Array.Copy(vRecent, vTrimmed, 5);
                vRecent = vTrimmed;
            }

            return Html(200, FPages.BuildAccountDashboardPage(aSession.DisplayName,
                vTheme, vLang, vOrderCount, vTotalSpend, vPaid, vOutstanding,
                vCurrency, vRecent));
        }

        // GET /orders : the customer's own invoices (their orders).
        public IResult MyOrdersGet(HttpContext aCtx, TERPSession aSession)
        {
            TERPInvoiceListRow[] vRows = FDB.ListInvoicesByCustomer(aSession.CustomerId, "");
            return Html(200, FPages.BuildMyOrdersPage(vRows, aSession.DisplayName,
                ReadThemeCookie(aCtx), ReadLanguageCookie(aCtx)));
        }

        // GET /orders/view?id= : one order's detail. The invoice's customer_id MUST
        // match the session; otherwise it is treated as not found (a customer cannot
        // probe another customer's order ids).
        public IResult OrderDetailGet(HttpContext aCtx, TERPSession aSession)
        {
            long vId = StrToInt64Def(GetParam(aCtx, null, "id"), 0);
            TERPInvoice vInv;
            TERPInvoiceLine[] vLines;
            if (vId <= 0 || !FDB.GetInvoice(vId, out vInv, out vLines) ||
                vInv.CustomerId != aSession.CustomerId)
                return Redirect("/orders");

            string vCustomerName = aSession.DisplayName;
            TERPCustomer vCust;
            if (FDB.GetCustomer(vInv.CustomerId, out vCust))
                vCustomerName = vCust.Name;
            return Html(200, FPages.BuildOrderDetailPage(vInv, vLines, vCustomerName,
                aSession.DisplayName, ReadThemeCookie(aCtx), ReadLanguageCookie(aCtx)));
        }

        // GET /profile : the customer's own profile edit form.
        public IResult ProfileGet(HttpContext aCtx, TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);
            string vFlashKey = GetParam(aCtx, null, "flash").Trim();
            string vFlash = (vFlashKey == "saved") ? I18n.T("profile.saved", vLang) : "";

            TERPCustomer vCust;
            if (!FDB.GetCustomer(aSession.CustomerId, out vCust))
            {
                // No linked customer row: show an empty form carrying the error.
                vCust = new TERPCustomer();
                return Html(200, FPages.BuildMyProfilePage(vCust,
                    I18n.T("profile.not_found", vLang), "", aSession.DisplayName, vTheme,
                    vLang));
            }
            return Html(200, FPages.BuildMyProfilePage(vCust, "", vFlash,
                aSession.DisplayName, vTheme, vLang));
        }

        // POST /profile/save : update only the customer row tied to the session.
        public IResult ProfileSavePost(HttpContext aCtx, IFormCollection aForm,
            TERPSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vLang = ReadLanguageCookie(aCtx);

            // Load the customer row tied to the SESSION (never an id from the
            // request), then overwrite only its editable contact fields.
            TERPCustomer vCust;
            if (!FDB.GetCustomer(aSession.CustomerId, out vCust))
            {
                vCust = new TERPCustomer();
                return Html(200, FPages.BuildMyProfilePage(vCust,
                    I18n.T("profile.not_found", vLang), "", aSession.DisplayName, vTheme,
                    vLang));
            }

            vCust.Name = GetParam(aCtx, aForm, "name").Trim();
            vCust.Email = GetParam(aCtx, aForm, "email").Trim();
            vCust.Phone = GetParam(aCtx, aForm, "phone").Trim();
            vCust.Address = GetParam(aCtx, aForm, "address").Trim();
            vCust.City = GetParam(aCtx, aForm, "city").Trim();
            vCust.Country = GetParam(aCtx, aForm, "country").Trim();

            if (vCust.Name == "")
                return Html(400, FPages.BuildMyProfilePage(vCust,
                    I18n.T("profile.name_required", vLang), "", aSession.DisplayName,
                    vTheme, vLang));

            try
            {
                FDB.UpdateCustomer(vCust);
                Audit(aCtx, "update", "customer", vCust.Id, vCust.Name);
            }
            catch (Exception E)
            {
                return Html(500, FPages.BuildMyProfilePage(vCust, E.Message, "",
                    aSession.DisplayName, vTheme, vLang));
            }
            return Redirect("/profile?flash=saved");
        }

        // GET /support : the support / contact page.
        public IResult SupportGet(HttpContext aCtx, TERPSession aSession)
        {
            string vEmail = FDB.GetSetting("support_email", "support@esegece.com");
            string vPhone = FDB.GetSetting("support_phone", "+34 900 000 000");
            return Html(200, FPages.BuildSupportPage(aSession.DisplayName,
                ReadThemeCookie(aCtx), ReadLanguageCookie(aCtx), vEmail, vPhone));
        }

        // ----- settings + admin (admin only) ----- //

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
                CS_PORTAL_SERVER_VERSION);
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
