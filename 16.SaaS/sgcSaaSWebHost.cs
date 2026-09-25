// ***************************************************************************
//  sgcSaaSWeb - multi-tenant SaaS control plane demo on ASP.NET Core
//  Mirror of demos\60.HTML\16.SaaS (TsgcWebSocketHTTPServer host).
//
//  This is the REWRITTEN host layer, following the 01.ERP host pattern. It
//  reproduces every branch of the Delphi sgcSaaS_Server.pas DispatchRequest,
//  but on Kestrel:
//    - The reusable logic (Bcrypt, Config, DB, Pages, Passkeys, Sessions,
//      Types) is copied VERBATIM from the 60.HTML demo and NOT changed here.
//    - The 60.HTML host scanned ARequestInfo.Cookies for the session / theme
//      cookie and wrote Set-Cookie / redirects through CustomHeaders on
//      TIdHTTPResponseInfo. Here every cookie is read/written through the
//      native ASP.NET Core cookie API (ctx.Request.Cookies /
//      ctx.Response.Cookies) with the SAME cookie names + attributes, and each
//      handler returns an IResult.
//    - Query + form params are merged through GetParam / GetParamArray,
//      matching the Delphi ARequestInfo.Params. GetParam is deliberately NOT
//      trimmed: sgcSaaS_Server.pas ParamOf returns the raw value and every call
//      site that wants it trimmed writes Trim(...) itself, so the same .Trim()
//      calls appear here in exactly the same places.
//    - PASSKEYS: the 60.HTML host hard-coded RP id 'localhost' + origin
//      http://localhost:<ListenPort>. Under Kestrel the RP id (request host
//      name) and origin (scheme://host) are DERIVED from the live request and
//      threaded into the reused TSaaSPasskeys. A per-origin instance is cached
//      so the WebAuthn begin/finish challenge state survives across the two
//      requests of a ceremony (see GetPasskeys). BaseURL - the absolute link
//      printed on the sign-up, forgot-password and invitation pages - is
//      derived from the same live request instead of the hard-coded port.
//    - The shared-host mounting of the Delphi unit (FBasePath / AttachAndStart
//      / PrefixAppURLs / HandleRequest) has no counterpart here: this app owns
//      the whole Kestrel pipeline, so FBasePath is always '' and the URL
//      rewriting it drove is not ported. CookiePath is therefore '/'.
//
//  The two host files that were NOT copied from 60.HTML are
//  sgcSaaS_Server.cs (the TsgcWebSocketHTTPServer host) and the console
//  Program.cs.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
// sgc
using esegece.sgcWebSockets;
using static SaaS.SaaSDB;
using static SaaS.SaaSSessions;
using static SaaS.SaaSTypes;

namespace SaaS
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, passkey factory) and exposes one IResult-returning method per
    // 60.HTML DispatchRequest branch. Program.cs maps the Minimal API endpoints
    // onto these methods and applies the auth / axis gates through Guarded*.
    public sealed class SaaSWebHost : IDisposable
    {
        public const string CS_SAAS_SERVER_VERSION = "1.0.0";

        // Self-contained favicon (served at /favicon.svg). Cyan to match the
        // brand. Copied verbatim from sgcSaaS_Server.pas:46-51.
        public const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"eSeGeCe\">" +
            "<rect width=\"64\" height=\"64\" rx=\"14\" fill=\"#0891B2\"/>" +
            "<text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"900\" font-size=\"44\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">e</text></svg>";

        // Cookie names - identical to the 60.HTML host so behavior matches exactly.
        public const string CS_SAAS_SESSION = "saas_session";
        public const string CS_THEME_COOKIE = "saas_theme";

        // Tenant settings keys.
        public const string CS_SET_NOTE = "welcome_note";
        public const string CS_SET_TIMEZONE = "timezone";
        public const string CS_SET_CONTACT = "billing_contact";
        public const string CS_SET_LOGO = "logo_url";

        private readonly TSaaSDBPool FDB;
        private readonly TSaaSSessionStore FSessions;
        private readonly TSaaSPages FPages;

        // Password reset tokens, in memory only (there is no SMTP here).
        private readonly Dictionary<string, long> FResetTokens =
            new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly object FResetLock = new object();

        // Per-origin passkey cache. WebAuthn begin/finish share challenge state in
        // one TSaaSPasskeys instance, so a ceremony's two requests (same browser,
        // same origin) must hit the SAME instance. Keyed by "rpId|origin".
        private readonly Dictionary<string, TSaaSPasskeys> FPasskeys =
            new Dictionary<string, TSaaSPasskeys>(StringComparer.Ordinal);
        private readonly object FPasskeysLock = new object();

        // Builds the DB (schema + demo seed + vendor staff), the session store and
        // the page builder - mirroring TSaaSServer.InitRuntime.
        public SaaSWebHost(TSaaSServerConfig aConfig, string aDatabasePath)
        {
            FDB = new TSaaSDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedDemoDataIfEmpty();
            FDB.SeedVendorStaff(aConfig.AdminUser,
                Bcrypt.BcryptHash(aConfig.AdminPassword));

            FSessions = new TSaaSSessionStore(480, true);
            FPages = new TSaaSPages();
        }

        public void Dispose()
        {
            lock (FPasskeysLock)
            {
                foreach (KeyValuePair<string, TSaaSPasskeys> vPair in FPasskeys)
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

        // ----- passkey factory (request-derived rpId + origin) ----- //

        private TSaaSPasskeys GetPasskeys(HttpContext aCtx)
        {
            string vRPID = aCtx.Request.Host.Host;
            if (string.IsNullOrEmpty(vRPID))
                vRPID = "localhost";
            string vOrigin = BaseURL(aCtx);
            string vKey = vRPID + "|" + vOrigin;

            lock (FPasskeysLock)
            {
                TSaaSPasskeys vPk;
                if (!FPasskeys.TryGetValue(vKey, out vPk))
                {
                    vPk = new TSaaSPasskeys(FDB, vRPID, "sgcSaaS", vOrigin);
                    FPasskeys[vKey] = vPk;
                }
                return vPk;
            }
        }

        // ----- fixed flash / error texts (sgcSaaS_Server.pas FlashText / ErrorText) //

        // A code travels in the query string; the text never does, so nothing
        // user-supplied reaches the page this way.
        private static string FlashText(string aCode)
        {
            if (aCode == "project.saved")
                return "Project saved.";
            if (aCode == "project.deleted")
                return "Project deleted.";
            if (aCode == "task.saved")
                return "Task saved.";
            if (aCode == "task.moved")
                return "Task moved.";
            if (aCode == "invite.sent")
                return "Invitation created. The link is shown below.";
            if (aCode == "invite.revoked")
                return "Invitation revoked.";
            if (aCode == "role.changed")
                return "Role updated.";
            if (aCode == "member.removed")
                return "Member removed.";
            if (aCode == "plan.changed")
                return "Plan changed. An invoice was recorded locally, nothing " +
                    "was charged.";
            if (aCode == "settings.saved")
                return "Settings saved.";
            if (aCode == "note.saved")
                return "Note saved.";
            if (aCode == "notify.read")
                return "Marked as read.";
            if (aCode == "grants.saved")
                return "Permission matrix saved for this workspace.";
            if (aCode == "onboarding.saved")
                return "Step saved. You can leave and come back.";
            if (aCode == "onboarding.done")
                return "Onboarding finished.";
            if (aCode == "tenant.suspended")
                return "Tenant suspended.";
            if (aCode == "tenant.activated")
                return "Tenant activated.";
            if (aCode == "plan.saved")
                return "Plan saved.";
            if (aCode == "flags.saved")
                return "Feature flags saved.";
            if (aCode == "impersonate.started")
                return "Impersonation started. Both the start and the stop are " +
                    "audited.";
            if (aCode == "impersonate.stopped")
                return "Impersonation stopped.";
            return "";
        }

        private static string ErrorText(string aCode)
        {
            if (aCode == "limit.users")
                return "Refused: this workspace is at the seat limit of its plan. " +
                    "Upgrade the plan to add another member.";
            if (aCode == "limit.projects")
                return "Refused: this workspace is at the project limit of its " +
                    "plan. Upgrade the plan to create another project.";
            if (aCode == "forbidden.role")
                return "Your role does not allow that.";
            if (aCode == "invalid.input")
                return "Some of the values were not accepted.";
            if (aCode == "invite.duplicate")
                return "That address is already a member or already invited.";
            if (aCode == "owner.last")
                return "A workspace must keep at least one owner.";
            if (aCode == "notfound")
                return "That record does not exist in this workspace.";
            return "";
        }

        // ----- request param helpers (query + form merged) ----- //

        // First value for aName, NOT trimmed: the exact counterpart of the Delphi
        // ParamOf, which reads the merged ARequestInfo.Params. Query wins over the
        // form. aForm is null for GET / non-form requests.
        private static string GetParam(HttpContext aCtx, IFormCollection aForm,
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

        // Delphi IntParamOf: the trimmed value parsed as Int64, aDefault on a miss.
        private static long GetParamInt(HttpContext aCtx, IFormCollection aForm,
            string aName, long aDefault)
        {
            return StrToInt64Def(GetParam(aCtx, aForm, aName).Trim(), aDefault);
        }

        // Every value for aName in order (query values, then form values). The
        // permission matrix posts one sgc_grants entry per checked cell.
        private static string[] GetParamArray(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            List<string> vResult = new List<string>();
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues))
                for (int vI = 0; vI < vValues.Count; vI++)
                    vResult.Add(vValues[vI] ?? "");
            if ((aForm != null) && aForm.TryGetValue(aName, out vValues))
                for (int vI = 0; vI < vValues.Count; vI++)
                    vResult.Add(vValues[vI] ?? "");
            return vResult.ToArray();
        }

        // Delphi StrToIntDef / StrToInt64Def / StrToFloatDef, invariant culture so
        // a machine locale can never change what the demo parses.
        private static int StrToIntDef(string aValue, int aDefault)
        {
            int vResult;
            if (int.TryParse(aValue, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        private static long StrToInt64Def(string aValue, long aDefault)
        {
            long vResult;
            if (long.TryParse(aValue, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        private static double StrToFloatDef(string aValue, double aDefault)
        {
            double vResult;
            if (double.TryParse(aValue, NumberStyles.Float,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        private static string IntToStr(long aValue)
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

        private static void WriteSessionCookie(HttpContext aCtx, string aToken)
        {
            if (string.IsNullOrEmpty(aToken))
                return;
            aCtx.Response.Cookies.Append(CS_SAAS_SESSION, aToken, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static void ClearSessionCookie(HttpContext aCtx)
        {
            aCtx.Response.Cookies.Delete(CS_SAAS_SESSION, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        // The 60.HTML default is 'light', not 'system'.
        private static string ReadThemeCookie(HttpContext aCtx)
        {
            string vS = ReadCookie(aCtx, CS_THEME_COOKIE).ToLowerInvariant();
            if ((vS == "light") || (vS == "dark") || (vS == "system"))
                return vS;
            return "light";
        }

        private static void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vS = (aTheme ?? "").Trim().ToLowerInvariant();
            if ((vS != "light") && (vS != "dark") && (vS != "system"))
                vS = "light";
            aCtx.Response.Cookies.Append(CS_THEME_COOKIE, vS, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(31536000),
                SameSite = SameSiteMode.Lax
            });
        }

        // ----- request-derived values ----- //

        // The Delphi reads AContext.Binding.PeerIP. Under Kestrel the connection
        // IP is the equivalent, with X-Forwarded-For honored when a proxy set it.
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

        // The Delphi builds 'http://localhost:<ListenPort>' + FBasePath. Kestrel
        // owns the port and the app is never mounted under a prefix, so the live
        // request is the honest source of the absolute links this demo prints.
        private static string BaseURL(HttpContext aCtx)
        {
            return aCtx.Request.Scheme + "://" + aCtx.Request.Host.Value;
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

        public bool CurrentSession(HttpContext aCtx, out TSaaSSession aSession)
        {
            aSession = null;
            string vToken = ReadCookie(aCtx, CS_SAAS_SESSION);
            if (vToken == "")
                return false;
            return FSessions.TryGet(vToken, out aSession);
        }

        // Build the page context for the signed-in session. TenantId is copied
        // from the session and from nowhere else. aCtx null => no flash / error
        // (the Delphi passes a nil ARequestInfo from WriteForbidden).
        private TSaaSPageContext MakeContext(TSaaSSession aSession,
            string aActiveMenu, string aTheme, HttpContext aCtx)
        {
            TSaaSPageContext vResult = new TSaaSPageContext();
            vResult.Username = aSession.Username;
            vResult.DisplayName = aSession.DisplayName;
            vResult.Role = aSession.Role;
            // The one and only source of the tenant id for every page and query.
            vResult.TenantId = aSession.TenantId;
            vResult.TenantName = aSession.TenantName;
            vResult.TenantSlug = aSession.TenantSlug;
            vResult.Impersonating = SaaSIsImpersonating(aSession);
            vResult.ImpersonatorName = aSession.RealUsername;
            vResult.Unread = 0;
            vResult.ActiveMenu = aActiveMenu;
            vResult.Theme = aTheme;
            vResult.Flash = "";
            vResult.ErrorMsg = "";

            if (aSession.TenantId > 0)
            {
                TSaaSTenant oTenant;
                if (FDB.GetTenant(aSession.TenantId, out oTenant))
                {
                    vResult.TenantName = oTenant.Name;
                    vResult.TenantSlug = oTenant.Slug;
                }
                vResult.Unread = FDB.CountUnreadNotifications(aSession.TenantId,
                    aSession.UserId);
            }

            if (aCtx != null)
            {
                vResult.Flash = FlashText(GetParam(aCtx, null, "flash"));
                vResult.ErrorMsg = ErrorText(GetParam(aCtx, null, "err"));
            }
            return vResult;
        }

        // An impersonated action always names both parties.
        private void Audit(HttpContext aCtx, TSaaSSession aSession,
            string aAction, string aEntity, long aEntityId, string aDetail)
        {
            string vDetail = aDetail;
            if (SaaSIsImpersonating(aSession))
                vDetail = vDetail + " [impersonated by " + aSession.RealUsername + "]";
            FDB.AddAudit(aSession.TenantId, aSession.UserId, aAction, aEntity,
                aEntityId, vDetail, ClientIPOf(aCtx));
        }

        // The Delphi builds a partial TSaaSSession record on the stack for the
        // audit rows written before a session exists (sign-up, invite accept).
        private static TSaaSSession AuditIdentity(long aTenantId, long aUserId,
            string aUsername)
        {
            TSaaSSession vResult = new TSaaSSession();
            vResult.TenantId = aTenantId;
            vResult.UserId = aUserId;
            vResult.Username = aUsername;
            vResult.RealUserId = 0;
            vResult.RealUsername = "";
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

        private static IResult Json(int aCode, string aJSON)
        {
            return Results.Content(aJSON, "application/json; charset=utf-8", null,
                aCode);
        }

        private static string JsonEscape(string aValue)
        {
            string vResult = (aValue ?? "").Replace("\\", "\\\\");
            vResult = vResult.Replace("\"", "\\\"");
            vResult = vResult.Replace("\r", "\\r");
            vResult = vResult.Replace("\n", "\\n");
            vResult = vResult.Replace("\t", "\\t");
            return vResult;
        }

        private static IResult JsonError(int aCode, string aMessage)
        {
            return Json(aCode, "{\"error\":\"" + JsonEscape(aMessage) + "\"}");
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

        // The 403 page, with the signed-in chrome when there is a session.
        private IResult Forbidden(string aReason, TSaaSSession aSession,
            bool aLoggedIn, string aTheme)
        {
            TSaaSPageContext vCtx;
            if (aLoggedIn)
                vCtx = MakeContext(aSession, "", aTheme, null);
            else
            {
                vCtx = new TSaaSPageContext();
                vCtx.Theme = aTheme;
            }
            return Html(403, FPages.BuildForbiddenPage(aReason, aTheme, vCtx,
                aLoggedIn));
        }

        // ----- guards ----- //

        // Step 5 of the 60.HTML dispatcher, the auth gate. A signed-out POST is
        // answered with the 403 page (it names the reason) rather than a redirect
        // that would silently drop the body; a signed-out GET goes to /login.
        public IResult Guarded(HttpContext aCtx, Func<TSaaSSession, IResult> aFn)
        {
            TSaaSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
            {
                if (string.Equals(aCtx.Request.Method, "POST",
                    StringComparison.OrdinalIgnoreCase))
                    return Forbidden("That action needs a signed-in session.", null,
                        false, ReadThemeCookie(aCtx));
                return Redirect("/login");
            }
            return aFn(vSession);
        }

        // The /passkey/register/* pair: JSON in, JSON out, so a signed-out call is
        // answered with a JSON 401 instead of the HTML gate above.
        public IResult GuardedJson(HttpContext aCtx, Func<TSaaSSession, IResult> aFn)
        {
            TSaaSSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return JsonError(401, "Not signed in.");
            return aFn(vSession);
        }

        // Step 6: the tenant workspace. A vendor account has no tenant, so it is
        // refused here with a reason rather than redirected somewhere confusing.
        public IResult GuardedTenant(HttpContext aCtx, Func<TSaaSSession, IResult> aFn)
        {
            return Guarded(aCtx, delegate(TSaaSSession aSession)
            {
                if (aSession.TenantId <= 0)
                    return Forbidden("The workspace is tenant-scoped and your " +
                        "account (" + aSession.Role + ") has no tenant. Use the " +
                        "vendor console, or impersonate a tenant user from a " +
                        "tenant page.", aSession, true, ReadThemeCookie(aCtx));
                return aFn(aSession);
            });
        }

        // Step 7: the vendor console. A tenant account reaching here gets a 403
        // with a reason, never a redirect, and never another tenant's data.
        public IResult GuardedVendor(HttpContext aCtx, Func<TSaaSSession, IResult> aFn)
        {
            return Guarded(aCtx, delegate(TSaaSSession aSession)
            {
                if (!SaaSIsVendorRole(aSession.Role))
                    return Forbidden("The vendor console is reachable by " +
                        "superadmin and support accounts only. Your role is " +
                        aSession.Role + ", scoped to tenant " +
                        IntToStr(aSession.TenantId) + ".", aSession, true,
                        ReadThemeCookie(aCtx));
                return aFn(aSession);
            });
        }

        // ==================================================================== //
        //  public routes                                                       //
        // ==================================================================== //

        public IResult Healthz()
        {
            return Text(200, "ok");
        }

        // Step 1 of the dispatcher served the favicon before the auth gate so the
        // login page is styled. Bootstrap / Chart.js come from the adapter's asset
        // registry here, leaving this one asset to the app.
        public IResult Favicon(HttpContext aCtx)
        {
            aCtx.Response.Headers["Cache-Control"] = "public, max-age=86400";
            return Results.Content(CS_FAVICON_SVG, "image/svg+xml", null, 200);
        }

        public IResult SetTheme(HttpContext aCtx, IFormCollection aForm)
        {
            WriteThemeCookie(aCtx, GetParam(aCtx, aForm, "theme"));
            return Redirect(RefererOrRoot(aCtx));
        }

        // GET / - the marketing page, or the axis home for a signed-in visitor.
        public IResult Root(HttpContext aCtx)
        {
            TSaaSSession vSession;
            if (CurrentSession(aCtx, out vSession))
            {
                if (SaaSIsVendorRole(vSession.Role))
                    return Redirect("/admin");
                return Redirect("/app");
            }
            return Html(200, FPages.BuildMarketingPage(FDB.ListPlans(),
                FDB.PlatformKPI(), ReadThemeCookie(aCtx)));
        }

        public IResult Pricing(HttpContext aCtx)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(FDB,
                "SELECT code, name AS plan, price_monthly AS price, " +
                "max_users AS users, max_projects AS projects, " +
                "max_storage_mb AS storage_mb FROM plans ORDER BY sort_order"))
            {
                oQ.Open();
                return Html(200, FPages.BuildPricingPage(oQ.DataSet, FDB.ListPlans(),
                    ReadThemeCookie(aCtx)));
            }
        }

        public IResult SignupGet(HttpContext aCtx)
        {
            return Html(200, FPages.BuildSignupPage("", "", "", "",
                GetParam(aCtx, null, "plan"), "", ReadThemeCookie(aCtx),
                FDB.ListPlans()));
        }

        public IResult SignupPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vCompany = GetParam(aCtx, aForm, "company").Trim();
            string vName = GetParam(aCtx, aForm, "display_name").Trim();
            string vEmail = GetParam(aCtx, aForm, "email").Trim();
            string vUsername = GetParam(aCtx, aForm, "username").ToLowerInvariant()
                .Trim();
            string vPassword = GetParam(aCtx, aForm, "password");
            string vPlanCode = GetParam(aCtx, aForm, "plan").Trim();

            string vError = "";
            if (vCompany == "")
                vError = "A workspace name is required.";
            else if (vName == "")
                vError = "Your name is required.";
            else if ((vEmail == "") || (vEmail.IndexOf('@') < 0))
                vError = "A valid email address is required.";
            else if (vUsername == "")
                vError = "A sign-in name is required.";
            else if (vPassword.Length < 6)
                vError = "The password must be at least 6 characters.";
            else if (FDB.UsernameExists(vUsername))
                vError = "That sign-in name is already taken.";
            else if (FDB.EmailExists(vEmail))
                vError = "That email address already has an account.";

            if (vError != "")
                return Html(200, FPages.BuildSignupPage(vCompany, vName, vEmail,
                    vUsername, vPlanCode, vError, vTheme, FDB.ListPlans()));

            TSaaSPlan vPlan;
            if ((vPlanCode == "") || !FDB.GetPlanByCode(vPlanCode, out vPlan))
                if (!FDB.GetPlanByCode("free", out vPlan))
                    return Html(500, FPages.BuildSignupPage(vCompany, vName, vEmail,
                        vUsername, vPlanCode, "No plans are configured.", vTheme,
                        FDB.ListPlans()));

            // A unique slug, derived from the display name.
            string vBase = SaaSSlugify(vCompany);
            if (vBase == "")
                vBase = "workspace";
            string vSlug = vBase;
            int vI = 1;
            while (FDB.SlugExists(vSlug))
            {
                vI++;
                vSlug = vBase + "-" + IntToStr(vI);
            }

            long vTenantId = FDB.InsertTenant(vSlug, vCompany, vPlan.Id,
                CS_TENANT_TRIAL, DateTime.Now.AddDays(14));
            string vToken = SaaSRandomToken(24);
            long vUserId = FDB.InsertUser(vTenantId, vUsername, vEmail,
                Bcrypt.BcryptHash(vPassword), CS_ROLE_OWNER, vName, "pending",
                vToken, DateTime.Now.AddHours(24));
            FDB.ChangeSubscription(vTenantId, vPlan.Id);
            FDB.InsertNotification(vTenantId, 0, "Workspace created",
                "Welcome to " + vCompany + ".", "info");

            Audit(aCtx, AuditIdentity(vTenantId, vUserId, vUsername),
                "tenant.signup", "tenant", vTenantId, "Workspace " + vCompany +
                " created on the " + vPlan.Name + " plan");

            return Html(200, FPages.BuildSignupDonePage(vCompany,
                BaseURL(aCtx) + "/verify?token=" + vToken, vTheme));
        }

        public IResult Verify(HttpContext aCtx)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vToken = GetParam(aCtx, null, "token").Trim();
            TSaaSUser vUser;
            if ((vToken == "") || !FDB.GetUserByVerifyToken(vToken, out vUser))
                return Html(404, FPages.BuildVerifyPage(false,
                    "That verification link is not valid. Tokens are single use, " +
                    "so it may already have been used.", vTheme));
            if ((vUser.VerifyExpiresAt != DateTime.MinValue) &&
                (vUser.VerifyExpiresAt < DateTime.Now))
                return Html(410, FPages.BuildVerifyPage(false,
                    "That verification link expired. Sign up again to get a fresh " +
                    "one.", vTheme));
            FDB.MarkUserVerified(vUser.Id);
            FDB.AddAudit(vUser.TenantId, vUser.Id, "user.verified", "user",
                vUser.Id, "Email verified", "");
            return Html(200, FPages.BuildVerifyPage(true,
                "Address verified. The account is active and the token has been " +
                "consumed, so this link will not work again.", vTheme));
        }

        public IResult LoginGet(HttpContext aCtx)
        {
            string vInfo = "";
            if (GetParam(aCtx, null, "reset") == "1")
                vInfo = "Password changed. Sign in with the new one.";
            return Html(200, FPages.BuildLoginPage("", vInfo, ReadThemeCookie(aCtx)));
        }

        public IResult LoginPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vUsername = GetParam(aCtx, aForm, "username").Trim();
            if (vUsername == "")
                vUsername = GetParam(aCtx, aForm, "user").Trim();
            string vPassword = GetParam(aCtx, aForm, "password");

            TSaaSUser vUser;
            if ((vUsername == "") || !FDB.GetUserByUsername(vUsername, out vUser) ||
                (vUser.PasswordHash == "") || (vUser.PasswordHash == "*") ||
                !Bcrypt.BcryptVerify(vPassword, vUser.PasswordHash))
                return Html(401, FPages.BuildLoginPage(
                    "Those credentials were not accepted.", "", vTheme));

            string vTenantName = "";
            string vTenantSlug = "";
            if (vUser.TenantId > 0)
            {
                TSaaSTenant vTenant;
                if (!FDB.GetTenant(vUser.TenantId, out vTenant))
                    return Html(401, FPages.BuildLoginPage(
                        "That workspace no longer exists.", "", vTheme));
                if (string.Equals(vTenant.Status, CS_TENANT_SUSPENDED,
                    StringComparison.OrdinalIgnoreCase))
                    return Html(403, FPages.BuildLoginPage(
                        "This workspace is suspended. Contact the vendor to " +
                        "reactivate it.", "", vTheme));
                vTenantName = vTenant.Name;
                vTenantSlug = vTenant.Slug;
            }

            WriteSessionCookie(aCtx, FSessions.CreateSession(vUser, vTenantName,
                vTenantSlug, ClientIPOf(aCtx)));
            FDB.TouchLastLogin(vUser.Id);

            Audit(aCtx, AuditIdentity(vUser.TenantId, vUser.Id, vUser.Username),
                "auth.login", "user", vUser.Id, "Signed in as " + vUser.Role);

            if (SaaSIsVendorRole(vUser.Role))
                return Redirect("/admin");
            return Redirect("/app");
        }

        public IResult Logout(HttpContext aCtx)
        {
            FSessions.Destroy_(ReadCookie(aCtx, CS_SAAS_SESSION));
            ClearSessionCookie(aCtx);
            return Redirect("/login");
        }

        public IResult ForgotGet(HttpContext aCtx)
        {
            return Html(200, FPages.BuildForgotPage("", "", ReadThemeCookie(aCtx)));
        }

        public IResult ForgotPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vEmail = GetParam(aCtx, aForm, "email").Trim();
            TSaaSUser vUser;
            if ((vEmail == "") || !FDB.GetUserByEmail(vEmail, out vUser))
                // Do not disclose whether the address exists.
                return Html(200, FPages.BuildForgotPage(
                    "If that address has an account, a reset link would be " +
                    "emailed. Nothing appears below because no matching account " +
                    "was found in this demo database.", "", vTheme));

            string vToken = SaaSRandomToken(24);
            lock (FResetLock)
            {
                FResetTokens[vToken] = vUser.Id;
            }
            return Html(200, FPages.BuildForgotPage(
                "No email was sent. The single-use link is printed below.",
                BaseURL(aCtx) + "/reset?token=" + vToken, vTheme));
        }

        public IResult ResetPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vToken = GetParam(aCtx, aForm, "token").Trim();
            string vPassword = GetParam(aCtx, aForm, "password");
            long vUserId = 0;
            lock (FResetLock)
            {
                if (FResetTokens.TryGetValue(vToken, out vUserId))
                    // Single use: consume it here, whatever happens next.
                    FResetTokens.Remove(vToken);
            }
            if ((vUserId == 0) || (vPassword.Length < 6))
                return Html(400, FPages.BuildForgotPage(
                    "That reset link is not valid any more, or the password was " +
                    "too short. Start again.", "", vTheme));

            using (TSaaSQuery oQ = new TSaaSQuery(FDB,
                "UPDATE users SET password_hash = :p WHERE id = :i"))
            {
                oQ.ParamStr("p", Bcrypt.BcryptHash(vPassword));
                oQ.ParamInt("i", vUserId);
                oQ.Exec();
            }
            return Redirect("/login?reset=1");
        }

        // Nothing is configured, so the "provider" hands straight back to the
        // callback page, which says so plainly.
        public IResult SocialStart(string aProvider)
        {
            return Redirect("/auth/callback?provider=" +
                (aProvider ?? "").ToLowerInvariant());
        }

        public IResult OAuthCallback(HttpContext aCtx)
        {
            string vProvider = GetParam(aCtx, null, "provider").Trim()
                .ToLowerInvariant();
            if ((vProvider != "google") && (vProvider != "github") &&
                (vProvider != "microsoft"))
                vProvider = "the provider";
            return Html(200, FPages.BuildOAuthCallbackPage(vProvider,
                ReadThemeCookie(aCtx)));
        }

        // Invitations: the opaque token is the credential, so these are public.
        public IResult InviteGet(HttpContext aCtx, string aToken)
        {
            string vTheme = ReadThemeCookie(aCtx);
            // Null when the token is unknown, mirroring the Delphi zeroed record:
            // every branch that passes it on also passes a non-empty error, and
            // BuildInvitePage returns before touching the invitation in that case.
            TSaaSInvitation vInvite = null;
            if ((aToken == "") || !FDB.GetInvitationByToken(aToken, out vInvite))
                return Html(404, FPages.BuildInvitePage(vInvite, "this workspace",
                    "That invitation link is not valid.", vTheme));
            if (vInvite.AcceptedAt != DateTime.MinValue)
                return Html(410, FPages.BuildInvitePage(vInvite, "this workspace",
                    "That invitation has already been used. Invitations are " +
                    "single use.", vTheme));
            if ((vInvite.ExpiresAt != DateTime.MinValue) &&
                (vInvite.ExpiresAt < DateTime.Now))
                return Html(410, FPages.BuildInvitePage(vInvite, "this workspace",
                    "That invitation has expired. Ask for a new one.", vTheme));
            TSaaSTenant vTenant;
            if (!FDB.GetTenant(vInvite.TenantId, out vTenant))
                return Html(404, FPages.BuildInvitePage(vInvite, "this workspace",
                    "That workspace no longer exists.", vTheme));
            return Html(200, FPages.BuildInvitePage(vInvite, vTenant.Name, "",
                vTheme));
        }

        public IResult InviteAccept(HttpContext aCtx, IFormCollection aForm,
            string aToken)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TSaaSInvitation vInvite = null;
            if ((aToken == "") || !FDB.GetInvitationByToken(aToken, out vInvite) ||
                (vInvite.AcceptedAt != DateTime.MinValue) ||
                ((vInvite.ExpiresAt != DateTime.MinValue) &&
                 (vInvite.ExpiresAt < DateTime.Now)))
                return Html(410, FPages.BuildInvitePage(vInvite, "this workspace",
                    "That invitation is no longer usable.", vTheme));
            TSaaSTenant vTenant;
            if (!FDB.GetTenant(vInvite.TenantId, out vTenant))
                return Html(404, FPages.BuildInvitePage(vInvite, "this workspace",
                    "That workspace no longer exists.", vTheme));

            // The seat limit is re-checked at accept time, not only at invite time.
            TSaaSPlan vPlan;
            if (FDB.GetPlan(vTenant.PlanId, out vPlan) && (vPlan.MaxUsers > 0) &&
                (FDB.CountTenantUsers(vTenant.Id) >= vPlan.MaxUsers))
                return Html(403, FPages.BuildInvitePage(vInvite, vTenant.Name,
                    "This workspace is at the seat limit of its " + vPlan.Name +
                    " plan, so the invitation cannot be accepted until the plan " +
                    "changes.", vTheme));

            string vName = GetParam(aCtx, aForm, "display_name").Trim();
            string vUsername = GetParam(aCtx, aForm, "username").ToLowerInvariant()
                .Trim();
            string vPassword = GetParam(aCtx, aForm, "password");
            if ((vName == "") || (vUsername == "") || (vPassword.Length < 6) ||
                FDB.UsernameExists(vUsername))
                return Html(400, FPages.BuildInvitePage(vInvite, vTenant.Name,
                    "Check the name, sign-in name (must be free) and a password " +
                    "of at least 6 characters.", vTheme));

            long vUserId = FDB.InsertUser(vInvite.TenantId, vUsername, vInvite.Email,
                Bcrypt.BcryptHash(vPassword), vInvite.Role, vName, "active", "",
                DateTime.MinValue);
            FDB.MarkUserVerified(vUserId);
            // Single use: burn the token now.
            FDB.AcceptInvitation(vInvite.Id);

            TSaaSUser vUser;
            if (FDB.GetUserById(vUserId, out vUser))
                WriteSessionCookie(aCtx, FSessions.CreateSession(vUser,
                    vTenant.Name, vTenant.Slug, ClientIPOf(aCtx)));

            Audit(aCtx, AuditIdentity(vInvite.TenantId, vUserId, vUsername),
                "team.invite.accept", "user", vUserId, vInvite.Email +
                " joined as " + vInvite.Role);

            return Redirect("/app");
        }

        // ==================================================================== //
        //  passkeys (WebAuthn)                                                 //
        // ==================================================================== //

        public IResult PasskeyLoginOptions(HttpContext aCtx)
        {
            try
            {
                return Json(200, GetPasskeys(aCtx).BeginLogin(""));
            }
            catch (Exception E)
            {
                return JsonError(400, E.Message);
            }
        }

        public async Task<IResult> PasskeyLoginVerify(HttpContext aCtx)
        {
            string vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
            long vUserId = 0;
            try
            {
                GetPasskeys(aCtx).FinishLogin(vBody, out vUserId);
            }
            catch (Exception E)
            {
                return JsonError(401, E.Message);
            }
            TSaaSUser vUser;
            if ((vUserId <= 0) || !FDB.GetUserById(vUserId, out vUser))
                return JsonError(401, "Unknown credential.");

            string vTenantName = "";
            string vTenantSlug = "";
            TSaaSTenant vTenant;
            if ((vUser.TenantId > 0) && FDB.GetTenant(vUser.TenantId, out vTenant))
            {
                if (string.Equals(vTenant.Status, CS_TENANT_SUSPENDED,
                    StringComparison.OrdinalIgnoreCase))
                    return JsonError(403, "This workspace is suspended.");
                vTenantName = vTenant.Name;
                vTenantSlug = vTenant.Slug;
            }
            WriteSessionCookie(aCtx, FSessions.CreateSession(vUser, vTenantName,
                vTenantSlug, ClientIPOf(aCtx)));
            FDB.TouchLastLogin(vUser.Id);
            if (SaaSIsVendorRole(vUser.Role))
                return Json(200, "{\"ok\":true,\"redirect\":\"/admin\"}");
            return Json(200, "{\"ok\":true,\"redirect\":\"/app\"}");
        }

        public IResult PasskeyRegisterOptions(HttpContext aCtx,
            TSaaSSession aSession)
        {
            try
            {
                return Json(200, GetPasskeys(aCtx).BeginRegister(aSession.UserId,
                    aSession.Username, aSession.DisplayName));
            }
            catch (Exception E)
            {
                return JsonError(400, E.Message);
            }
        }

        // The body is the raw attestation JSON, so the device label can only come
        // from the query string (there is no form to merge).
        public async Task<IResult> PasskeyRegisterVerify(HttpContext aCtx)
        {
            string vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
            return GuardedJson(aCtx, delegate(TSaaSSession aSession)
            {
                try
                {
                    GetPasskeys(aCtx).FinishRegister(aSession.UserId, vBody,
                        GetParam(aCtx, null, "device").Trim());
                    return Json(200, "{\"ok\":true}");
                }
                catch (Exception E)
                {
                    return JsonError(400, E.Message);
                }
            });
        }

        // ==================================================================== //
        //  both axes                                                           //
        // ==================================================================== //

        // The data-layer page is available on both axes: a tenant session sees its
        // own scoped projects, a vendor session the platform view.
        public IResult SQLPage(HttpContext aCtx, TSaaSSession aSession)
        {
            TSaaSPageContext vCtx = MakeContext(aSession, "sql",
                ReadThemeCookie(aCtx), aCtx);
            if (aSession.TenantId > 0)
                using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB, CS_SQL_PROJECTS,
                    aSession.TenantId))
                {
                    oQ.Open();
                    return Html(200, FPages.BuildSQLPage(oQ.DataSet, CS_SQL_PROJECTS,
                        vCtx));
                }

            // A vendor account has no tenant, so the page shows the platform view.
            const string CS_SQL_PLATFORM = "SELECT t.name AS tenant, t.status, " +
                "COALESCE(p.name, '') AS plan FROM tenants t " +
                "LEFT JOIN plans p ON p.id = t.plan_id ORDER BY t.name";
            using (TSaaSQuery oQ = new TSaaSQuery(FDB, CS_SQL_PLATFORM))
            {
                oQ.Open();
                return Html(200, FPages.BuildSQLPage(oQ.DataSet, CS_SQL_PLATFORM,
                    vCtx));
            }
        }

        // Stopping an impersonation must stay reachable while the session carries a
        // tenant identity, so it is matched before the axis gate. GET is accepted
        // as well as POST: TsgcHTMLComponent_ImpersonateBanner renders its stop
        // control as a link, and clicking it has to work.
        public IResult ImpersonateStop(HttpContext aCtx, TSaaSSession aSession)
        {
            if (!SaaSIsImpersonating(aSession))
                return Redirect("/admin");

            TSaaSUser vReal;
            if (!FDB.GetUserById(aSession.RealUserId, out vReal))
            {
                // The operator account is gone: drop the session entirely.
                FSessions.Destroy_(ReadCookie(aCtx, CS_SAAS_SESSION));
                ClearSessionCookie(aCtx);
                return Redirect("/login");
            }

            // Audited on the way out too, before the identity changes back.
            FDB.AddAudit(aSession.TenantId, aSession.RealUserId, "impersonate.stop",
                "user", aSession.UserId, aSession.RealUsername +
                " stopped impersonating " + aSession.Username, ClientIPOf(aCtx));

            TSaaSSession vNew;
            FSessions.EndImpersonation(ReadCookie(aCtx, CS_SAAS_SESSION), vReal,
                out vNew);
            return Redirect("/admin?flash=impersonate.stopped");
        }

        // ==================================================================== //
        //  tenant workspace                                                    //
        // ==================================================================== //

        public IResult AppDashboard(HttpContext aCtx, TSaaSSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TSaaSDashboardVM vVM = new TSaaSDashboardVM();
            if (!FDB.GetTenant(aSession.TenantId, out vVM.Tenant))
                return Forbidden("The workspace on your session no longer exists.",
                    aSession, true, vTheme);

            vVM.Usage = FDB.PlanUsage(aSession.TenantId);
            vVM.TotalTasks = FDB.CountTasks(aSession.TenantId, "");
            vVM.DoneTasks = FDB.CountTasks(aSession.TenantId, "done");
            vVM.OpenTasks = vVM.TotalTasks - vVM.DoneTasks;
            vVM.Projects = FDB.ListProjects(aSession.TenantId);
            vVM.Audit = FDB.ListTenantAudit(aSession.TenantId, 8, 0);
            vVM.Team = FDB.ListTenantUsers(aSession.TenantId);
            vVM.ApiSeries = FDB.TenantActivitySeries(aSession.TenantId, 12);
            vVM.UsageMonths = FDB.UsageSeries(aSession.TenantId, "api_calls", 12);
            vVM.PendingInvites = FDB.PendingInviteCount(aSession.TenantId);
            if (vVM.Tenant.TrialEndsAt == DateTime.MinValue)
                vVM.TrialDaysLeft = 0;
            else
                vVM.TrialDaysLeft = Math.Max(0,
                    (int)(vVM.Tenant.TrialEndsAt - DateTime.Now).TotalDays);

            return Html(200, FPages.BuildDashboardPage(vVM,
                MakeContext(aSession, "dashboard", vTheme, aCtx)));
        }

        public IResult OnboardingGet(HttpContext aCtx, TSaaSSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TSaaSTenant vTenant;
            if (!FDB.GetTenant(aSession.TenantId, out vTenant))
                return Forbidden("The workspace on your session no longer exists.",
                    aSession, true, vTheme);
            return Html(200, FPages.BuildOnboardingPage(vTenant.OnboardingStep,
                vTenant, FDB.GetSetting(aSession.TenantId, CS_SET_NOTE, ""),
                MakeContext(aSession, "onboarding", vTheme, aCtx)));
        }

        public IResult OnboardingPost(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            int vStep = StrToIntDef(GetParam(aCtx, aForm, "step").Trim(), 0);
            bool vBack = GetParam(aCtx, aForm, "back") == "1";
            TSaaSTenant vTenant;
            if (!FDB.GetTenant(aSession.TenantId, out vTenant))
                return Redirect("/app");

            if (vBack)
            {
                FDB.SetTenantOnboardingStep(aSession.TenantId,
                    Math.Max(0, vStep - 1));
                return Redirect("/app/onboarding");
            }

            switch (vStep)
            {
                case 0:
                    {
                        string vName = GetParam(aCtx, aForm, "workspace_name").Trim();
                        if (vName != "")
                            using (TSaaSQuery oQ = new TSaaSQuery(FDB,
                                "UPDATE tenants SET name = :n WHERE id = :i"))
                            {
                                oQ.ParamStr("n", vName);
                                oQ.ParamInt("i", aSession.TenantId);
                                oQ.Exec();
                            }
                        FDB.SetSetting(aSession.TenantId, CS_SET_TIMEZONE,
                            GetParam(aCtx, aForm, "timezone").Trim());
                        break;
                    }
                case 1:
                    FDB.SetSetting(aSession.TenantId, CS_SET_NOTE,
                        GetParam(aCtx, aForm, "welcome_note"));
                    break;
                case 2:
                    {
                        string vEmail = GetParam(aCtx, aForm, "invite_email").Trim();
                        string vRole = GetParam(aCtx, aForm, "invite_role").Trim();
                        if (!SaaSIsTenantRole(vRole))
                            vRole = CS_ROLE_MEMBER;
                        if ((vEmail != "") && (vEmail.IndexOf('@') >= 0))
                        {
                            // The seat limit applies here too.
                            TSaaSPlan vPlan;
                            if (FDB.GetPlan(vTenant.PlanId, out vPlan) &&
                                (vPlan.MaxUsers > 0) &&
                                (FDB.CountTenantUsers(aSession.TenantId) +
                                 FDB.PendingInviteCount(aSession.TenantId) >=
                                 vPlan.MaxUsers))
                                return Redirect("/app/onboarding?err=limit.users");
                            FDB.InsertInvitation(aSession.TenantId, vEmail, vRole,
                                SaaSRandomToken(24), DateTime.Now.AddDays(7),
                                aSession.UserId);
                            Audit(aCtx, aSession, "team.invite", "invitation", 0,
                                "Invited " + vEmail + " as " + vRole);
                        }
                        break;
                    }
                case 3:
                    {
                        string vName = GetParam(aCtx, aForm, "project_name").Trim();
                        if (vName != "")
                            Audit(aCtx, aSession, "project.create", "project",
                                FDB.InsertProject(aSession.TenantId, vName,
                                    GetParam(aCtx, aForm, "project_desc").Trim(),
                                    "active", aSession.UserId),
                                "Created " + vName + " during onboarding");
                        break;
                    }
            }

            FDB.SetTenantOnboardingStep(aSession.TenantId, Math.Min(4, vStep + 1));
            if (vStep >= 3)
                return Redirect("/app?flash=onboarding.done");
            return Redirect("/app/onboarding?flash=onboarding.saved");
        }

        public IResult ProjectsGet(HttpContext aCtx, TSaaSSession aSession)
        {
            TSaaSProject[] vRows = FDB.ListProjects(aSession.TenantId);
            int vPage = StrToIntDef(GetParam(aCtx, null, "page"), 1);
            if (vPage < 1)
                vPage = 1;
            int vPageCount = Math.Max(1, (vRows.Length + 9) / 10);
            TSaaSPageContext vCtx = MakeContext(aSession, "projects",
                ReadThemeCookie(aCtx), aCtx);

            // Every list on this page is opened through the scoped helper.
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB,
                "SELECT p.id, p.name, p.status, " +
                "COALESCE(u.display_name, '') AS owner, " +
                "(SELECT COUNT(*) FROM tasks t WHERE t.tenant_id = p.tenant_id " +
                "AND t.project_id = p.id) AS tasks, p.created_at AS created " +
                "FROM projects p LEFT JOIN users u ON u.id = p.owner_id " +
                "AND u.tenant_id = p.tenant_id WHERE p.tenant_id = :tenant_id " +
                "ORDER BY p.created_at DESC", aSession.TenantId))
            {
                oQ.Open();
                return Html(200, FPages.BuildProjectsPage(oQ.DataSet, vRows,
                    FDB.ListTenantUsers(aSession.TenantId), vPage, vPageCount, vCtx));
            }
        }

        // THE isolation rule in one place: the lookup carries the session tenant id.
        // A row belonging to anyone else simply is not found, and the answer is a
        // 403 page that says why, never a redirect and never someone else's data.
        public IResult ProjectDetail(HttpContext aCtx, TSaaSSession aSession,
            long aId)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TSaaSProject vProject;
            if (!FDB.GetProject(aSession.TenantId, aId, out vProject))
            {
                long vOwnerTenantId;
                string vOwnerName;
                if (FDB.ProbeProjectOwner(aId, out vOwnerTenantId, out vOwnerName))
                    return Forbidden("Project " + IntToStr(aId) +
                        " exists, but it belongs to tenant " +
                        IntToStr(vOwnerTenantId) +
                        " and your session is scoped to tenant " +
                        IntToStr(aSession.TenantId) +
                        ". The query that looked for it carried your tenant id, " +
                        "so the row was never returned.", aSession, true, vTheme);
                return Forbidden("Project " + IntToStr(aId) +
                    " does not exist in tenant " + IntToStr(aSession.TenantId) + ".",
                    aSession, true, vTheme);
            }

            TSaaSPageContext vCtx = MakeContext(aSession, "projects", vTheme, aCtx);
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB,
                "SELECT id, display_name FROM users WHERE tenant_id = :tenant_id " +
                "ORDER BY display_name", aSession.TenantId))
            {
                oQ.Open();
                return Html(200, FPages.BuildProjectDetailPage(vProject,
                    FDB.ListTasks(aSession.TenantId, aId),
                    FDB.ListTenantUsers(aSession.TenantId), oQ.DataSet,
                    FDB.GetSetting(aSession.TenantId,
                        "project_note_" + IntToStr(aId), ""), vCtx));
            }
        }

        public IResult ProjectSave(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (string.Equals(aSession.Role, CS_ROLE_READONLY,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/app/projects?err=forbidden.role");

            long vId = GetParamInt(aCtx, aForm, "id", 0);
            string vName = GetParam(aCtx, aForm, "name").Trim();
            string vDesc = GetParam(aCtx, aForm, "description").Trim();
            string vStatus = GetParam(aCtx, aForm, "status").Trim();
            if ((vStatus != "active") && (vStatus != "on_hold") &&
                (vStatus != "archived"))
                vStatus = "active";
            long vOwnerId = GetParamInt(aCtx, aForm, "owner_id", aSession.UserId);
            if (vName == "")
                return Redirect("/app/projects?err=invalid.input");

            if (vId > 0)
            {
                // Update only ever touches a row already inside this tenant.
                TSaaSProject vProject;
                if (!FDB.GetProject(aSession.TenantId, vId, out vProject))
                    return Redirect("/app/projects?err=notfound");
                FDB.UpdateProject(aSession.TenantId, vId, vName, vDesc, vStatus,
                    vOwnerId);
                Audit(aCtx, aSession, "project.update", "project", vId,
                    "Updated " + vName);
            }
            else
            {
                // Plan limit, enforced on the server.
                TSaaSTenant vTenant;
                TSaaSPlan vPlan;
                if (FDB.GetTenant(aSession.TenantId, out vTenant) &&
                    FDB.GetPlan(vTenant.PlanId, out vPlan) &&
                    (vPlan.MaxProjects > 0) &&
                    (FDB.CountProjects(aSession.TenantId) >= vPlan.MaxProjects))
                    return Redirect("/app/projects?err=limit.projects");
                vId = FDB.InsertProject(aSession.TenantId, vName, vDesc, vStatus,
                    vOwnerId);
                Audit(aCtx, aSession, "project.create", "project", vId,
                    "Created " + vName);
            }
            return Redirect("/app/projects?flash=project.saved");
        }

        public IResult ProjectDelete(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (string.Equals(aSession.Role, CS_ROLE_READONLY,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aSession.Role, CS_ROLE_MEMBER,
                    StringComparison.OrdinalIgnoreCase))
                return Redirect("/app/projects?err=forbidden.role");
            long vId = GetParamInt(aCtx, aForm, "id", 0);
            if (!FDB.DeleteProject(aSession.TenantId, vId))
                return Redirect("/app/projects?err=notfound");
            Audit(aCtx, aSession, "project.delete", "project", vId,
                "Deleted project " + IntToStr(vId));
            return Redirect("/app/projects?flash=project.deleted");
        }

        public IResult TasksGet(HttpContext aCtx, TSaaSSession aSession)
        {
            string vFrom = GetParam(aCtx, null, "from").Trim();
            string vTo = GetParam(aCtx, null, "to").Trim();
            DateTime vNow = DateTime.Now;
            TSaaSPageContext vCtx = MakeContext(aSession, "tasks",
                ReadThemeCookie(aCtx), aCtx);

            string vSQL = "SELECT t.id, t.title, t.status, " +
                "COALESCE(p.name, '') AS project_name, " +
                "COALESCE(u.display_name, '') AS assignee_name, " +
                "COALESCE(t.due_at, '') AS due_at FROM tasks t " +
                "LEFT JOIN projects p ON p.id = t.project_id " +
                "AND p.tenant_id = t.tenant_id " +
                "LEFT JOIN users u ON u.id = t.assignee_id " +
                "AND u.tenant_id = t.tenant_id " +
                "WHERE t.tenant_id = :tenant_id ";
            if (vFrom != "")
                vSQL = vSQL + "AND t.due_at >= :dfrom ";
            if (vTo != "")
                vSQL = vSQL + "AND t.due_at <= :dto ";
            vSQL = vSQL + "ORDER BY t.due_at";

            using (TSaaSQuery oBoard = TSaaSQuery.CreateScoped(FDB, vSQL,
                aSession.TenantId))
            {
                if (vFrom != "")
                    oBoard.ParamStr("dfrom", vFrom);
                if (vTo != "")
                    oBoard.ParamStr("dto", vTo + "T23:59:59");
                oBoard.Open();
                return Html(200, FPages.BuildTasksPage(oBoard.DataSet,
                    FDB.ListTasks(aSession.TenantId, 0),
                    FDB.ListProjects(aSession.TenantId),
                    FDB.ListTenantUsers(aSession.TenantId), vNow.Year, vNow.Month,
                    vFrom, vTo, vCtx));
            }
        }

        public IResult TaskSave(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (string.Equals(aSession.Role, CS_ROLE_READONLY,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/app/tasks?err=forbidden.role");

            long vId = GetParamInt(aCtx, aForm, "id", 0);
            long vProjectId = GetParamInt(aCtx, aForm, "project_id", 0);
            long vAssigneeId = GetParamInt(aCtx, aForm, "assignee_id", 0);
            string vTitle = GetParam(aCtx, aForm, "title").Trim();
            string vStatus = GetParam(aCtx, aForm, "status").Trim();
            if ((vStatus != "todo") && (vStatus != "doing") &&
                (vStatus != "review") && (vStatus != "done"))
                vStatus = "todo";
            DateTime vDueAt = ParseSaaSTimestamp(
                GetParam(aCtx, aForm, "due_at").Trim());
            if (vDueAt == DateTime.MinValue)
                vDueAt = DateTime.Now.AddDays(7);

            if (vTitle == "")
                return Redirect("/app/tasks?err=invalid.input");
            // The parent project must live inside this tenant.
            TSaaSProject vProject;
            if ((vProjectId > 0) &&
                !FDB.GetProject(aSession.TenantId, vProjectId, out vProject))
                return Redirect("/app/tasks?err=notfound");

            if (vId > 0)
            {
                TSaaSTask vTask;
                if (!FDB.GetTask(aSession.TenantId, vId, out vTask))
                    return Redirect("/app/tasks?err=notfound");
                FDB.UpdateTask(aSession.TenantId, vId, vProjectId, vTitle, vStatus,
                    vAssigneeId, vDueAt);
                Audit(aCtx, aSession, "task.update", "task", vId,
                    "Updated " + vTitle);
            }
            else
            {
                vId = FDB.InsertTask(aSession.TenantId, vProjectId, vTitle, vStatus,
                    vAssigneeId, vDueAt);
                Audit(aCtx, aSession, "task.create", "task", vId,
                    "Created " + vTitle);
            }
            return Redirect("/app/tasks?flash=task.saved");
        }

        public IResult TaskMove(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (string.Equals(aSession.Role, CS_ROLE_READONLY,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/app/tasks?err=forbidden.role");
            long vId = GetParamInt(aCtx, aForm, "id", 0);
            string vStatus = GetParam(aCtx, aForm, "status").Trim().ToLowerInvariant();
            if ((vStatus != "todo") && (vStatus != "doing") &&
                (vStatus != "review") && (vStatus != "done"))
                return Redirect("/app/tasks?err=invalid.input");
            // A miss means "not a task of this tenant": the UPDATE carried the
            // session tenant id, so another tenant's task can never be moved here.
            if (!FDB.MoveTask(aSession.TenantId, vId, vStatus))
                return Redirect("/app/tasks?err=notfound");
            Audit(aCtx, aSession, "task.move", "task", vId, "Moved to " + vStatus);
            return Redirect("/app/tasks?flash=task.moved");
        }

        public IResult TeamGet(HttpContext aCtx, TSaaSSession aSession)
        {
            TSaaSTeamVM vVM = new TSaaSTeamVM();
            vVM.Members = FDB.ListTenantUsers(aSession.TenantId);
            vVM.Invites = FDB.ListInvitations(aSession.TenantId);
            vVM.Usage = FDB.PlanUsage(aSession.TenantId);
            vVM.InviteURL = "";
            vVM.InviteEmail = "";
            vVM.CanManage = string.Equals(aSession.Role, CS_ROLE_OWNER,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aSession.Role, CS_ROLE_ADMIN,
                    StringComparison.OrdinalIgnoreCase);

            // An invitation token just created is handed back through the query
            // string so the page can print it.
            string vInvite = GetParam(aCtx, null, "invite").Trim();
            if (vInvite != "")
                vVM.InviteURL = BaseURL(aCtx) + "/invite/" + vInvite;
            vVM.InviteEmail = GetParam(aCtx, null, "to").Trim();

            TSaaSPageContext vCtx = MakeContext(aSession, "team",
                ReadThemeCookie(aCtx), aCtx);
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB, CS_SQL_TEAM,
                aSession.TenantId))
            {
                oQ.Open();
                return Html(200, FPages.BuildTeamPage(oQ.DataSet, vVM, vCtx));
            }
        }

        public IResult TeamInvite(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (!(string.Equals(aSession.Role, CS_ROLE_OWNER,
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(aSession.Role, CS_ROLE_ADMIN,
                      StringComparison.OrdinalIgnoreCase)))
                return Redirect("/app/team?err=forbidden.role");

            long vRevokeId = GetParamInt(aCtx, aForm, "revoke_id", 0);
            if (vRevokeId > 0)
            {
                if (FDB.RevokeInvitation(aSession.TenantId, vRevokeId))
                    Audit(aCtx, aSession, "team.invite.revoke", "invitation",
                        vRevokeId, "Invitation revoked");
                return Redirect("/app/team?flash=invite.revoked");
            }

            string vEmail = GetParam(aCtx, aForm, "email").Trim();
            string vRole = GetParam(aCtx, aForm, "role").Trim();
            if (!SaaSIsTenantRole(vRole))
                vRole = CS_ROLE_MEMBER;
            if ((vEmail == "") || (vEmail.IndexOf('@') < 0))
                return Redirect("/app/team?err=invalid.input");

            // Plan limit, enforced on the server before anything is written. Seats
            // in use counts members plus outstanding invitations.
            TSaaSTenant vTenant;
            TSaaSPlan vPlan;
            if (FDB.GetTenant(aSession.TenantId, out vTenant) &&
                FDB.GetPlan(vTenant.PlanId, out vPlan) && (vPlan.MaxUsers > 0))
            {
                int vSeats = FDB.CountTenantUsers(aSession.TenantId) +
                    FDB.PendingInviteCount(aSession.TenantId);
                if (vSeats >= vPlan.MaxUsers)
                {
                    Audit(aCtx, aSession, "team.invite.refused", "invitation", 0,
                        "Refused: seat limit " + IntToStr(vPlan.MaxUsers) +
                        " reached");
                    return Redirect("/app/team?err=limit.users");
                }
            }

            string vToken = SaaSRandomToken(24);
            FDB.InsertInvitation(aSession.TenantId, vEmail, vRole, vToken,
                DateTime.Now.AddDays(7), aSession.UserId);
            Audit(aCtx, aSession, "team.invite", "invitation", 0,
                "Invited " + vEmail + " as " + vRole);
            return Redirect("/app/team?flash=invite.sent&invite=" + vToken +
                "&to=" + vEmail);
        }

        public IResult TeamRole(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (!(string.Equals(aSession.Role, CS_ROLE_OWNER,
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(aSession.Role, CS_ROLE_ADMIN,
                      StringComparison.OrdinalIgnoreCase)))
                return Redirect("/app/team?err=forbidden.role");
            long vUserId = GetParamInt(aCtx, aForm, "user_id", 0);
            string vRole = GetParam(aCtx, aForm, "role").Trim();
            if (!SaaSIsTenantRole(vRole))
                return Redirect("/app/team?err=invalid.input");
            // The target must already be inside this tenant.
            TSaaSUser vUser;
            if (!FDB.GetTenantUser(aSession.TenantId, vUserId, out vUser))
                return Redirect("/app/team?err=notfound");
            if (string.Equals(vUser.Role, CS_ROLE_OWNER,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(vRole, CS_ROLE_OWNER,
                    StringComparison.OrdinalIgnoreCase) &&
                (FDB.CountTenantOwners(aSession.TenantId) <= 1))
                return Redirect("/app/team?err=owner.last");
            FDB.UpdateTenantUserRole(aSession.TenantId, vUserId, vRole);
            Audit(aCtx, aSession, "team.role", "user", vUserId,
                vUser.DisplayName + " is now " + vRole);
            return Redirect("/app/team?flash=role.changed");
        }

        public IResult TeamRemove(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (!(string.Equals(aSession.Role, CS_ROLE_OWNER,
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(aSession.Role, CS_ROLE_ADMIN,
                      StringComparison.OrdinalIgnoreCase)))
                return Redirect("/app/team?err=forbidden.role");
            long vUserId = GetParamInt(aCtx, aForm, "user_id", 0);
            TSaaSUser vUser;
            if (!FDB.GetTenantUser(aSession.TenantId, vUserId, out vUser))
                return Redirect("/app/team?err=notfound");
            if (string.Equals(vUser.Role, CS_ROLE_OWNER,
                    StringComparison.OrdinalIgnoreCase) &&
                (FDB.CountTenantOwners(aSession.TenantId) <= 1))
                return Redirect("/app/team?err=owner.last");
            FDB.RemoveTenantUser(aSession.TenantId, vUserId);
            Audit(aCtx, aSession, "team.remove", "user", vUserId,
                "Removed " + vUser.DisplayName);
            return Redirect("/app/team?flash=member.removed");
        }

        public IResult RolesGet(HttpContext aCtx, TSaaSSession aSession)
        {
            TSaaSPageContext vCtx = MakeContext(aSession, "roles",
                ReadThemeCookie(aCtx), aCtx);
            // Only the granted pairs are selected, so the matrix loader needs no
            // boolean column: SQLite stores 0/1 in an INTEGER column.
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB,
                "SELECT role, permission FROM role_grants " +
                "WHERE tenant_id = :tenant_id AND COALESCE(granted, 0) = 1",
                aSession.TenantId))
            {
                oQ.Open();
                return Html(200, FPages.BuildRolesPage(null, null, oQ.DataSet, vCtx));
            }
        }

        public IResult RolesGrant(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            string[] vRoles = new string[] { CS_ROLE_OWNER, CS_ROLE_ADMIN,
                CS_ROLE_MEMBER, CS_ROLE_READONLY };
            string[] vPerms = new string[] { "project.view", "project.edit",
                "task.edit", "team.manage", "billing.manage", "settings.manage" };
            const string CS_FIELD = "sgc_grants";

            if (!(string.Equals(aSession.Role, CS_ROLE_OWNER,
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(aSession.Role, CS_ROLE_ADMIN,
                      StringComparison.OrdinalIgnoreCase)))
                return Redirect("/app/roles?err=forbidden.role");

            // The matrix posts every checked cell as a repeated CS_FIELD parameter
            // valued <role>|<permission>. That list is untrusted, so it is only
            // ever read as a membership test: the writes below still run over the
            // fixed role/permission pairs, and a crafted value cannot invent one.
            HashSet<string> vPosted = new HashSet<string>(
                GetParamArray(aCtx, aForm, CS_FIELD), StringComparer.Ordinal);

            for (int vI = 0; vI < vRoles.Length; vI++)
                for (int vJ = 0; vJ < vPerms.Length; vJ++)
                    FDB.SetGrant(aSession.TenantId, vRoles[vI], vPerms[vJ],
                        vPosted.Contains(vRoles[vI] + "|" + vPerms[vJ]));

            Audit(aCtx, aSession, "roles.grant", "role_grants", 0,
                "Permission matrix updated");
            return Redirect("/app/roles?flash=grants.saved");
        }

        public IResult BillingGet(HttpContext aCtx, TSaaSSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TSaaSBillingVM vVM = new TSaaSBillingVM();
            if (!FDB.GetTenant(aSession.TenantId, out vVM.Tenant))
                return Forbidden("The workspace on your session no longer exists.",
                    aSession, true, vTheme);
            FDB.GetPlan(vVM.Tenant.PlanId, out vVM.Plan);
            vVM.Plans = FDB.ListPlans();
            vVM.Usage = FDB.PlanUsage(aSession.TenantId);
            vVM.Invoices = FDB.ListInvoices(aSession.TenantId);
            FDB.CurrentSubscription(aSession.TenantId, out vVM.Sub);

            TSaaSPageContext vCtx = MakeContext(aSession, "billing", vTheme, aCtx);

            List<TSaaSMonthPoint> vSpend = new List<TSaaSMonthPoint>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB,
                "SELECT SUBSTR(issued_at, 1, 7) AS ym, SUM(total) AS v " +
                "FROM invoices WHERE tenant_id = :tenant_id " +
                "GROUP BY ym ORDER BY ym", aSession.TenantId))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSMonthPoint vPoint = new TSaaSMonthPoint();
                    vPoint.MonthLabel = oQ.Str("ym");
                    vPoint.Value = oQ.Flt("v");
                    vPoint.Cnt = 1;
                    vSpend.Add(vPoint);
                    oQ.Next();
                }
            }
            vVM.Spend = vSpend.ToArray();

            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB,
                "SELECT number, COALESCE(issued_at, '') AS issued, subtotal, tax, " +
                "total, status FROM invoices WHERE tenant_id = :tenant_id " +
                "ORDER BY issued_at DESC", aSession.TenantId))
            {
                oQ.Open();
                return Html(200, FPages.BuildBillingPage(oQ.DataSet, vVM, vCtx));
            }
        }

        public IResult BillingChangePlan(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (!(string.Equals(aSession.Role, CS_ROLE_OWNER,
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(aSession.Role, CS_ROLE_ADMIN,
                      StringComparison.OrdinalIgnoreCase)))
                return Redirect("/app/billing?err=forbidden.role");
            long vPlanId = GetParamInt(aCtx, aForm, "plan_id", 0);
            TSaaSPlan vPlan;
            if (!FDB.GetPlan(vPlanId, out vPlan))
                return Redirect("/app/billing?err=notfound");
            // Downgrading below the seats already in use is refused.
            int vSeats = FDB.CountTenantUsers(aSession.TenantId);
            if ((vPlan.MaxUsers > 0) && (vSeats > vPlan.MaxUsers))
                return Redirect("/app/billing?err=limit.users");

            FDB.ChangeSubscription(aSession.TenantId, vPlanId);
            if (!string.Equals(vPlan.Code, "free", StringComparison.OrdinalIgnoreCase))
                FDB.SetTenantStatus(aSession.TenantId, CS_TENANT_ACTIVE);

            // Simulated billing: an invoice row, no payment provider.
            if (vPlan.PriceMonthly > 0)
            {
                DateTime vNow = DateTime.Now;
                DateTime vFirst = new DateTime(vNow.Year, vNow.Month, 1);
                DateTime vLast = vFirst.AddMonths(1).AddDays(-1);
                double vTax = Math.Round(vPlan.PriceMonthly * 0.21, 2,
                    MidpointRounding.ToEven);
                long vInvoiceId = FDB.InsertInvoice(aSession.TenantId,
                    "INV-" + vNow.ToString("yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture),
                    vFirst, vLast, vPlan.PriceMonthly, vTax,
                    vPlan.PriceMonthly + vTax, "open", vNow, DateTime.MinValue);
                FDB.InsertInvoiceLine(vInvoiceId, vPlan.Name + " plan, one month",
                    1, vPlan.PriceMonthly);
            }

            FDB.InsertNotification(aSession.TenantId, 0, "Plan changed",
                "This workspace is now on the " + vPlan.Name + " plan.", "billing");
            Audit(aCtx, aSession, "billing.plan", "plan", vPlanId,
                "Switched to " + vPlan.Name + " (simulated, nothing charged)");
            return Redirect("/app/billing?flash=plan.changed");
        }

        // GET /app/billing/invoice/{id}[.pdf]. PDF generation is scoped exactly
        // like every page: the invoice is fetched with the session tenant id in
        // the WHERE clause.
        public IResult InvoicePDF(HttpContext aCtx, TSaaSSession aSession,
            string aIdStr)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vSeg = aIdStr ?? "";
            if (vSeg.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                vSeg = vSeg.Substring(0, vSeg.Length - 4);
            long vId;
            if (!long.TryParse(vSeg, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vId))
                return Results.StatusCode(404);

            TSaaSInvoice vInvoice;
            if (!FDB.GetInvoice(aSession.TenantId, vId, out vInvoice))
                return Forbidden("Invoice " + IntToStr(vId) +
                    " is not an invoice of tenant " + IntToStr(aSession.TenantId) +
                    ". The lookup carried your tenant id, so nothing was returned " +
                    "and no PDF was produced.", aSession, true, vTheme);

            TSaaSTenant vTenant;
            FDB.GetTenant(aSession.TenantId, out vTenant);
            TSaaSInvoiceLine[] vLines = FDB.ListInvoiceLines(aSession.TenantId, vId);

            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = "Invoice " + vInvoice.Number;
            oPDF.Author = "sgcSaaS";
            oPDF.Subject = "Simulated invoice";
            oPDF.HeaderText = "sgcSaaS - " + vTenant.Name;
            oPDF.FooterText = "Simulated invoice, no payment was taken. " +
                "Page {page} of {pages}";
            oPDF.ShowPageNumbers = true;
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poPortrait;

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 18);
            oPDF.SetColor("#0891B2");
            oPDF.TextOut(20, 40, "Invoice " + vInvoice.Number);

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            oPDF.SetColor("#333333");
            oPDF.TextOut(20, 52, "Workspace: " + vTenant.Name + " (tenant id " +
                IntToStr(vTenant.Id) + ")");
            oPDF.TextOut(20, 58, "Period: " +
                vInvoice.PeriodStart.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture) + " to " +
                vInvoice.PeriodEnd.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
            oPDF.TextOut(20, 64, "Status: " + vInvoice.Status);

            oPDF.BeginTable(90.0, 25.0, 30.0, 30.0);
            oPDF.TableHeader("Description", "Qty", "Unit", "Total");
            if (vLines.Length == 0)
                oPDF.TableRow("Subscription", "1", Fmt2(vInvoice.Subtotal),
                    Fmt2(vInvoice.Subtotal));
            else
                for (int vI = 0; vI < vLines.Length; vI++)
                    oPDF.TableRow(vLines[vI].Description, FmtQty(vLines[vI].Qty),
                        Fmt2(vLines[vI].UnitPrice), Fmt2(vLines[vI].LineTotal));
            oPDF.EndTable();

            double vY = oPDF.CurrentY + 10;
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            oPDF.TextOut(120, vY, "Subtotal: " + Fmt2(vInvoice.Subtotal));
            oPDF.TextOut(120, vY + 6, "Tax: " + Fmt2(vInvoice.Tax));
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
            oPDF.TextOut(120, vY + 14, "Total: " + Fmt2(vInvoice.Total));

            byte[] vBytes;
            using (MemoryStream oStream = new MemoryStream())
            {
                oPDF.SaveToStream(oStream);
                vBytes = oStream.ToArray();
            }
            return FileBytes(aCtx, vBytes, "application/pdf",
                vInvoice.Number + ".pdf", false);
        }

        // The Delphi FormatFloat('0.00', x) / FormatFloat('0.##', x) masks, pinned
        // to the invariant culture so the PDF never picks up a machine locale.
        private static string Fmt2(double aValue)
        {
            return aValue.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string FmtQty(double aValue)
        {
            return aValue.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public IResult SettingsGet(HttpContext aCtx, TSaaSSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TSaaSTenant vTenant;
            if (!FDB.GetTenant(aSession.TenantId, out vTenant))
                return Forbidden("The workspace on your session no longer exists.",
                    aSession, true, vTheme);
            return Html(200, FPages.BuildSettingsPage(vTenant,
                FDB.GetSetting(aSession.TenantId, CS_SET_NOTE, ""),
                FDB.GetSetting(aSession.TenantId, CS_SET_LOGO, ""),
                FDB.GetSetting(aSession.TenantId, CS_SET_TIMEZONE, "UTC"),
                FDB.GetSetting(aSession.TenantId, CS_SET_CONTACT, ""),
                MakeContext(aSession, "settings", vTheme, aCtx)));
        }

        public IResult SettingsSave(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            string vScope = GetParam(aCtx, aForm, "scope").Trim();

            if (vScope == "project_note")
            {
                long vProjectId = GetParamInt(aCtx, aForm, "project_id", 0);
                // The note is keyed by project, so the project must be in this
                // tenant.
                TSaaSProject vProject;
                if (!FDB.GetProject(aSession.TenantId, vProjectId, out vProject))
                    return Redirect("/app/projects?err=notfound");
                FDB.SetSetting(aSession.TenantId,
                    "project_note_" + IntToStr(vProjectId),
                    GetParam(aCtx, aForm, "note"));
                Audit(aCtx, aSession, "project.note", "project", vProjectId,
                    "Knowledge base note updated");
                return Redirect("/app/projects/" + IntToStr(vProjectId) +
                    "?flash=note.saved");
            }

            if (!(string.Equals(aSession.Role, CS_ROLE_OWNER,
                      StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(aSession.Role, CS_ROLE_ADMIN,
                      StringComparison.OrdinalIgnoreCase)))
                return Redirect("/app/settings?err=forbidden.role");

            string vName = GetParam(aCtx, aForm, "workspace_name").Trim();
            if (vName != "")
                using (TSaaSQuery oQ = new TSaaSQuery(FDB,
                    "UPDATE tenants SET name = :n WHERE id = :i"))
                {
                    oQ.ParamStr("n", vName);
                    oQ.ParamInt("i", aSession.TenantId);
                    oQ.Exec();
                }
            FDB.SetSetting(aSession.TenantId, CS_SET_TIMEZONE,
                GetParam(aCtx, aForm, "timezone").Trim());
            FDB.SetSetting(aSession.TenantId, CS_SET_CONTACT,
                GetParam(aCtx, aForm, "contact").Trim());
            FDB.SetSetting(aSession.TenantId, CS_SET_NOTE,
                GetParam(aCtx, aForm, "welcome_note"));
            Audit(aCtx, aSession, "settings.save", "tenant", aSession.TenantId,
                "Workspace settings updated");
            return Redirect("/app/settings?flash=settings.saved");
        }

        public IResult NotificationsGet(HttpContext aCtx, TSaaSSession aSession)
        {
            return Html(200, FPages.BuildNotificationsPage(
                FDB.ListNotifications(aSession.TenantId, aSession.UserId),
                MakeContext(aSession, "notifications", ReadThemeCookie(aCtx), aCtx)));
        }

        public IResult NotificationRead(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            long vId = GetParamInt(aCtx, aForm, "id", 0);
            if (!FDB.MarkNotificationRead(aSession.TenantId, aSession.UserId, vId))
                return Redirect("/app/notifications?err=notfound");
            return Redirect("/app/notifications?flash=notify.read");
        }

        public IResult AppAudit(HttpContext aCtx, TSaaSSession aSession)
        {
            int vPage = StrToIntDef(GetParam(aCtx, null, "page"), 1);
            if (vPage < 1)
                vPage = 1;
            int vTotal = FDB.CountTenantAudit(aSession.TenantId);
            int vPageCount = Math.Max(1, (vTotal + 24) / 25);
            if (vPage > vPageCount)
                vPage = vPageCount;
            // ListTenantAudit runs CS_SQL_AUDIT through the scoped helper, so the
            // tenant bind is applied there exactly as it is for every other list.
            return Html(200, FPages.BuildAuditPage(
                FDB.ListTenantAudit(aSession.TenantId, 25, (vPage - 1) * 25), vPage,
                vPageCount, MakeContext(aSession, "audit", ReadThemeCookie(aCtx),
                    aCtx)));
        }

        public IResult Isolation(HttpContext aCtx, TSaaSSession aSession)
        {
            TSaaSIsolationVM vVM = new TSaaSIsolationVM();
            vVM.SessionTenantId = aSession.TenantId;
            vVM.TenantName = aSession.TenantName;
            vVM.SQLText = CS_SQL_PROJECTS;
            vVM.CountSQLText = CS_SQL_COUNT_PROJECTS;
            vVM.VisibleProjects = FDB.CountProjects(aSession.TenantId);
            vVM.TotalProjects = FDB.CountProjectsAllTenants();
            vVM.VisibleTasks = FDB.CountTasks(aSession.TenantId, "");
            vVM.TotalTasks = FDB.CountTasksAllTenants();

            // Pick a real row belonging to somebody else and try to read it through
            // the same call the detail page uses.
            vVM.ForeignProjectId = FDB.FindForeignProjectId(aSession.TenantId);
            vVM.ForeignOwnerId = 0;
            vVM.ForeignOwnerName = "";
            vVM.ForeignFetchRefused = true;
            vVM.RefusalReason = "";
            if (vVM.ForeignProjectId > 0)
            {
                FDB.ProbeProjectOwner(vVM.ForeignProjectId, out vVM.ForeignOwnerId,
                    out vVM.ForeignOwnerName);
                TSaaSProject vProject;
                vVM.ForeignFetchRefused = !FDB.GetProject(aSession.TenantId,
                    vVM.ForeignProjectId, out vProject);
                if (vVM.ForeignFetchRefused)
                    vVM.RefusalReason = "The statement carried tenant_id = " +
                        IntToStr(aSession.TenantId) + " and the row carries " +
                        "tenant_id = " + IntToStr(vVM.ForeignOwnerId) +
                        ", so it matched nothing. The route answers 403 with this " +
                        "same reason rather than redirecting.";
                else
                    vVM.RefusalReason = "This should never happen.";
            }

            // A workspace statement written without the bind: the helper stops it.
            vVM.GuardReason = "";
            try
            {
                using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB,
                    "SELECT id FROM projects", aSession.TenantId))
                {
                    oQ.Open();
                    vVM.GuardReason = "The guard did not fire. That is a bug.";
                }
            }
            catch (Exception E)
            {
                vVM.GuardReason = "TSaaSQuery.CreateScoped(pool, " +
                    "'SELECT id FROM projects', " + IntToStr(aSession.TenantId) +
                    ")" + Environment.NewLine + "-> " + E.GetType().Name + ": " +
                    E.Message;
            }

            TSaaSProject[] vRows = FDB.ListProjects(aSession.TenantId);
            int vCount = Math.Min(vRows.Length, 10);
            vVM.Rows = new TSaaSProject[vCount];
            for (int vI = 0; vI < vCount; vI++)
                vVM.Rows[vI] = vRows[vI];

            return Html(200, FPages.BuildIsolationPage(vVM,
                MakeContext(aSession, "isolation", ReadThemeCookie(aCtx), aCtx)));
        }

        // ==================================================================== //
        //  vendor console                                                      //
        // ==================================================================== //

        public IResult AdminDashboard(HttpContext aCtx, TSaaSSession aSession)
        {
            TSaaSAdminVM vVM = new TSaaSAdminVM();
            vVM.KPI = FDB.PlatformKPI();
            vVM.MRR = FDB.MRRSeries(12);
            vVM.Signups = FDB.SignupSeries(12);
            vVM.Churn = FDB.ChurnSeries(12);
            vVM.Search = GetParam(aCtx, null, "q").Trim();
            vVM.StatusFilter = GetParam(aCtx, null, "status").Trim();
            if (vVM.StatusFilter == "")
                vVM.StatusFilter = "all";
            vVM.Tenants = FDB.ListTenants(vVM.Search, vVM.StatusFilter);
            TSaaSPageContext vCtx = MakeContext(aSession, "admin",
                ReadThemeCookie(aCtx), aCtx);

            bool vHasStatus = !string.Equals(vVM.StatusFilter, "all",
                StringComparison.OrdinalIgnoreCase);
            bool vHasSearch = vVM.Search != "";
            string vSQL = "SELECT t.name AS tenant, t.slug, t.status, " +
                "COALESCE(p.name, '') AS plan, " +
                "(SELECT COUNT(*) FROM users u WHERE u.tenant_id = t.id) AS users, " +
                "(SELECT COUNT(*) FROM projects r WHERE r.tenant_id = t.id) " +
                "AS projects, COALESCE(t.created_at, '') AS created FROM tenants t " +
                "LEFT JOIN plans p ON p.id = t.plan_id WHERE 1 = 1 ";
            if (vHasStatus)
                vSQL = vSQL + "AND t.status = :st ";
            if (vHasSearch)
                vSQL = vSQL + "AND t.name LIKE :q ESCAPE '\\' ";
            vSQL = vSQL + "ORDER BY t.name";

            using (TSaaSQuery oQ = new TSaaSQuery(FDB, vSQL))
            {
                if (vHasStatus)
                    oQ.ParamStr("st", vVM.StatusFilter);
                if (vHasSearch)
                    oQ.ParamStr("q", "%" + SaaSEscapeLike(vVM.Search) + "%");
                oQ.Open();
                return Html(200, FPages.BuildAdminDashboardPage(oQ.DataSet, vVM,
                    vCtx));
            }
        }

        public IResult AdminTenant(HttpContext aCtx, TSaaSSession aSession, long aId)
        {
            string vTheme = ReadThemeCookie(aCtx);
            TSaaSTenantDetailVM vVM = new TSaaSTenantDetailVM();
            if (!FDB.GetTenant(aId, out vVM.Tenant))
                return Forbidden("No tenant with id " + IntToStr(aId) + ".",
                    aSession, true, vTheme);
            FDB.GetPlan(vVM.Tenant.PlanId, out vVM.Plan);
            vVM.Users = FDB.ListTenantUsers(aId);
            vVM.Invoices = FDB.ListInvoices(aId);
            vVM.UsageMonths = FDB.UsageSeries(aId, "api_calls", 12);
            FDB.CurrentSubscription(aId, out vVM.Sub);
            vVM.CanImpersonate = string.Equals(aSession.Role, CS_ROLE_SUPERADMIN,
                StringComparison.OrdinalIgnoreCase);

            TSaaSPageContext vCtx = MakeContext(aSession, "admin", vTheme, aCtx);
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(FDB,
                "SELECT id, display_name, email, role, status FROM users " +
                "WHERE tenant_id = :tenant_id ORDER BY display_name", aId))
            {
                oQ.Open();
                return Html(200, FPages.BuildAdminTenantPage(oQ.DataSet, vVM, vCtx));
            }
        }

        public IResult AdminTenantStatus(HttpContext aCtx, TSaaSSession aSession,
            long aId, string aStatus)
        {
            string vTheme = ReadThemeCookie(aCtx);
            if (!string.Equals(aSession.Role, CS_ROLE_SUPERADMIN,
                StringComparison.OrdinalIgnoreCase))
                return Forbidden("Only a superadmin can change a tenant status. " +
                    "Your role is " + aSession.Role + ".", aSession, true, vTheme);
            TSaaSTenant vTenant;
            if (!FDB.GetTenant(aId, out vTenant))
                return Forbidden("No tenant with id " + IntToStr(aId) + ".",
                    aSession, true, vTheme);
            FDB.SetTenantStatus(aId, aStatus);
            FDB.AddAudit(aId, aSession.UserId, "tenant." + aStatus, "tenant", aId,
                vTenant.Name + " set to " + aStatus + " by " + aSession.Username,
                ClientIPOf(aCtx));
            if (string.Equals(aStatus, CS_TENANT_SUSPENDED,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/admin/tenants/" + IntToStr(aId) +
                    "?flash=tenant.suspended");
            return Redirect("/admin/tenants/" + IntToStr(aId) +
                "?flash=tenant.activated");
        }

        public IResult ImpersonateStart(HttpContext aCtx, TSaaSSession aSession,
            long aUserId)
        {
            string vTheme = ReadThemeCookie(aCtx);
            // Superadmin only. If already impersonating, the real role is what
            // counts, and BeginImpersonation refuses to chain anyway.
            if (!string.Equals(aSession.Role, CS_ROLE_SUPERADMIN,
                StringComparison.OrdinalIgnoreCase))
                return Forbidden("Impersonation is restricted to superadmin " +
                    "accounts. Your role is " + aSession.Role + ".", aSession, true,
                    vTheme);
            TSaaSUser vTarget;
            if (!FDB.GetUserById(aUserId, out vTarget))
                return Forbidden("No user with id " + IntToStr(aUserId) + ".",
                    aSession, true, vTheme);
            // A superadmin can never be impersonated.
            if (string.Equals(vTarget.Role, CS_ROLE_SUPERADMIN,
                StringComparison.OrdinalIgnoreCase))
                return Forbidden("A superadmin account cannot be impersonated.",
                    aSession, true, vTheme);
            if (vTarget.Id == aSession.UserId)
                return Forbidden("You are already signed in as that account.",
                    aSession, true, vTheme);

            TSaaSTenant vTenant = new TSaaSTenant();
            if (vTarget.TenantId > 0)
                FDB.GetTenant(vTarget.TenantId, out vTenant);
            TSaaSSession vNew;
            if (!FSessions.BeginImpersonation(ReadCookie(aCtx, CS_SAAS_SESSION),
                vTarget, vTenant.Name, vTenant.Slug, out vNew))
                return Forbidden("An impersonation is already active on this " +
                    "session. Stop it first.", aSession, true, vTheme);

            // Audited on the way in, under the target tenant, naming both parties.
            FDB.AddAudit(vTarget.TenantId, aSession.UserId, "impersonate.start",
                "user", vTarget.Id, aSession.Username + " started impersonating " +
                vTarget.Username, ClientIPOf(aCtx));
            return Redirect("/app?flash=impersonate.started");
        }

        public IResult AdminPlansGet(HttpContext aCtx, TSaaSSession aSession)
        {
            TSaaSPageContext vCtx = MakeContext(aSession, "plans",
                ReadThemeCookie(aCtx), aCtx);
            using (TSaaSQuery oQ = new TSaaSQuery(FDB,
                "SELECT code, name AS plan, price_monthly AS price, " +
                "max_users AS users, max_projects AS projects, " +
                "max_storage_mb AS storage_mb FROM plans ORDER BY sort_order"))
            {
                oQ.Open();
                return Html(200, FPages.BuildAdminPlansPage(oQ.DataSet,
                    FDB.ListPlans(), vCtx));
            }
        }

        public IResult AdminPlansSave(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (!string.Equals(aSession.Role, CS_ROLE_SUPERADMIN,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/admin/plans?err=forbidden.role");
            long vId = GetParamInt(aCtx, aForm, "id", 0);
            TSaaSPlan vPlan;
            if (!FDB.GetPlan(vId, out vPlan))
                return Redirect("/admin/plans?err=notfound");
            if (GetParam(aCtx, aForm, "name").Trim() != "")
                vPlan.Name = GetParam(aCtx, aForm, "name").Trim();
            vPlan.PriceMonthly = StrToFloatDef(
                GetParam(aCtx, aForm, "price_monthly").Trim().Replace(",", "."),
                vPlan.PriceMonthly);
            vPlan.MaxUsers = StrToIntDef(
                GetParam(aCtx, aForm, "max_users").Trim(), vPlan.MaxUsers);
            vPlan.MaxProjects = StrToIntDef(
                GetParam(aCtx, aForm, "max_projects").Trim(), vPlan.MaxProjects);
            vPlan.MaxStorageMB = StrToIntDef(
                GetParam(aCtx, aForm, "max_storage_mb").Trim(), vPlan.MaxStorageMB);
            FDB.UpdatePlan(vPlan);
            FDB.AddAudit(0, aSession.UserId, "plan.save", "plan", vPlan.Id,
                "Plan " + vPlan.Name + " updated", ClientIPOf(aCtx));
            return Redirect("/admin/plans?flash=plan.saved");
        }

        public IResult AdminFlagsGet(HttpContext aCtx, TSaaSSession aSession)
        {
            return Html(200, FPages.BuildAdminFlagsPage(FDB.ListFlags(0),
                MakeContext(aSession, "flags", ReadThemeCookie(aCtx), aCtx)));
        }

        public IResult AdminFlagsSave(HttpContext aCtx, IFormCollection aForm,
            TSaaSSession aSession)
        {
            if (!string.Equals(aSession.Role, CS_ROLE_SUPERADMIN,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/admin/flags?err=forbidden.role");
            TSaaSFeatureFlag[] vFlags = FDB.ListFlags(0);
            for (int vI = 0; vI < vFlags.Length; vI++)
                FDB.SetFlag(vFlags[vI].TenantId, vFlags[vI].Flag,
                    GetParam(aCtx, aForm, "flag_" + vFlags[vI].Flag) != "");
            FDB.AddAudit(0, aSession.UserId, "flags.save", "feature_flags", 0,
                "Feature flags updated", ClientIPOf(aCtx));
            return Redirect("/admin/flags?flash=flags.saved");
        }

        public IResult AdminAudit(HttpContext aCtx, TSaaSSession aSession)
        {
            string vFilter = GetParam(aCtx, null, "action").Trim();
            if (vFilter == "")
                vFilter = "all";
            int vPage = StrToIntDef(GetParam(aCtx, null, "page"), 1);
            if (vPage < 1)
                vPage = 1;
            int vTotal = FDB.CountVendorAudit(vFilter);
            int vPageCount = Math.Max(1, (vTotal + 24) / 25);
            if (vPage > vPageCount)
                vPage = vPageCount;
            return Html(200, FPages.BuildAdminAuditPage(
                FDB.ListVendorAudit(vFilter, 25, (vPage - 1) * 25), vFilter, vPage,
                vPageCount, MakeContext(aSession, "audit", ReadThemeCookie(aCtx),
                    aCtx)));
        }

        // ==================================================================== //
        //  404 / 403 (the protected catch-all)                                 //
        // ==================================================================== //

        // Steps 6, 7 and 8 of the dispatcher's tail: an unknown path under /app
        // still meets the tenant gate, an unknown path under /admin still meets
        // the vendor gate, and everything else is the styled 404 page.
        public IResult NotFound(HttpContext aCtx, TSaaSSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vDoc = aCtx.Request.Path.Value ?? "";
            if (vDoc.StartsWith("/app", StringComparison.OrdinalIgnoreCase) &&
                (aSession.TenantId <= 0))
                return Forbidden("The workspace is tenant-scoped and your account " +
                    "(" + aSession.Role + ") has no tenant. Use the vendor " +
                    "console, or impersonate a tenant user from a tenant page.",
                    aSession, true, vTheme);
            if (vDoc.StartsWith("/admin", StringComparison.OrdinalIgnoreCase) &&
                !SaaSIsVendorRole(aSession.Role))
                return Forbidden("The vendor console is reachable by superadmin " +
                    "and support accounts only. Your role is " + aSession.Role +
                    ", scoped to tenant " + IntToStr(aSession.TenantId) + ".",
                    aSession, true, vTheme);
            return Html(404, FPages.BuildNotFoundPage(vTheme));
        }
    }
}
