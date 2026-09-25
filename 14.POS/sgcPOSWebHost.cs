// ***************************************************************************
//  sgcPOSWeb - retail point-of-sale web-app demo on ASP.NET Core
//  Mirror of demos\60.HTML\14.POS (TsgcWebSocketHTTPServer host), which is
//  itself the managed port of delphi\Demos\60.HTML\01.RunTime\14.POS.
//
//  This is the REWRITTEN host layer, following the 01.ERP host pattern. It
//  reproduces every branch of the Delphi sgcPOS_Server.pas DispatchRequest
//  (sgcPOS_Server.pas:2466-2880), but on Kestrel:
//    - The reusable logic (Bcrypt, Config, DB, Pages, Passkeys, Sessions,
//      Types) is copied VERBATIM from the 60.HTML demo and NOT changed here.
//    - The 60.HTML host scanned the Indy cookie list and wrote Set-Cookie /
//      redirects through CustomHeaders on TIdHTTPResponseInfo. Here every
//      cookie is read and written through the native ASP.NET Core cookie API
//      (ctx.Request.Cookies / ctx.Response.Cookies) with the SAME cookie names
//      and attributes, and each handler returns an IResult.
//    - Query and form params are merged through GetParam (query first, then
//      the urlencoded body) and trimmed, matching the Delphi Param() helper
//      over ARequestInfo.Params (sgcPOS_Server.pas:963-973).
//    - PASSKEYS: the 60.HTML host hard-coded RP id 'localhost' and origin
//      http://localhost:<port> (sgcPOS_Server.pas:571-572). Under Kestrel the
//      RP id (request host name) and origin (scheme://host) are DERIVED from
//      the live request and threaded into the reused TPOSPasskeys. A
//      per-origin instance is cached so the WebAuthn begin/finish challenge
//      state survives across the two requests of one ceremony (GetPasskeys).
//    - The base-path / shared-host machinery of the Delphi host (FBasePath,
//      PrefixAppURLs, CookiePath) is dropped: this app always owns the root of
//      its Kestrel endpoint, which is exactly the FBasePath = '' case where
//      PrefixAppURLs is a no-op.
//
//  Security decisions the Delphi takes on the server are taken here too, on
//  the server: the flash / error banner is decoded from a WHITELISTED code and
//  never echoed from the query string, money is parsed by POSDB.POSParseMoney
//  (never decimal.Parse), a discount over Config.DiscountPinThreshold and
//  EVERY refund need a manager PIN that bcrypt-verifies through
//  FDB.VerifyManagerPin, and a recalled sale must be parked AND owned by the
//  caller.
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
using esegece.sgcWebSockets;

namespace POS
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, per-origin passkey factory) and exposes one IResult-returning
    // method per 60.HTML DispatchRequest branch. Program.cs maps the Minimal
    // API endpoints onto these methods and applies the auth / back-office /
    // admin gates through Guarded, GuardedBackOffice and GuardedAdmin.
    public sealed class POSWebHost : IDisposable
    {
        public const string CS_POS_SERVER_VERSION = "1.0.0";

        // Cookie names - identical to the 60.HTML host so behaviour matches.
        public const string CS_POS_SESSION = "pos_session";
        public const string CS_POS_THEME_COOKIE = "pos_theme";

        // Self-contained favicon (served at /favicon.svg): the rose till mark.
        // Mirrors sgcPOS_Server.pas:42-47.
        public const string CS_FAVICON_SVG =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"sgcPOS\">" +
            "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"" +
            POSConst.CS_POS_ACCENT + "\"/>" +
            "<text x=\"32\" y=\"46\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"900\" font-size=\"34\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">P</text></svg>";

        // Promotion artwork tints (sgcPOS_Server.pas:938-939).
        private static readonly string[] CS_PROMO_TINTS = new string[]
        {
            "#E11D48", "#0EA5E9", "#22C55E", "#F59E0B", "#8B5CF6"
        };

        // Catalogue page size of /products (sgcPOS_Server.pas:1911).
        private const int CS_PRODUCT_PAGE_SIZE = 20;

        // 80mm till-roll geometry of the receipt PDF (sgcPOS_Server.pas:1573-1575).
        private const double CS_ROLL_W = 80.0;
        private const double CS_ROLL_LEFT = 5.0;
        private const double CS_ROLL_INNER = 70.0;

        private const string CS_CONTENT_TYPE_HTML = "text/html; charset=utf-8";
        private const string CS_CONTENT_TYPE_TEXT = "text/plain; charset=utf-8";
        private const string CS_CONTENT_TYPE_JSON = "application/json; charset=utf-8";
        private const string CS_CONTENT_TYPE_SVG = "image/svg+xml";
        private const string CS_CONTENT_TYPE_PDF = "application/pdf";
        private const string CS_CONTENT_TYPE_XLSX =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        private readonly TPOSServerConfig FConfig;
        private readonly TPOSDBPool FDB;
        private readonly TPOSSessionStore FSessions;
        private readonly TPOSPages FPages;
        private readonly int FListenPort;
        private readonly DateTime FStartedAt;

        // Per-origin passkey cache. WebAuthn begin/finish share challenge state
        // in one TPOSPasskeys instance, so a ceremony's two requests (same
        // browser, same origin) must hit the SAME instance. Keyed by
        // "rpId|origin".
        private readonly Dictionary<string, TPOSPasskeys> FPasskeys =
            new Dictionary<string, TPOSPasskeys>(StringComparer.Ordinal);
        private readonly object FPasskeysLock = new object();

        // Builds the DB (schema + admin seed + demo data), the session store and
        // the page builder, mirroring TPOSServer.InitRuntime
        // (sgcPOS_Server.pas:563-575). The passkey engine is built lazily per
        // request origin instead of once for localhost.
        public POSWebHost(TPOSServerConfig aConfig, string aDatabasePath, int aListenPort)
        {
            FConfig = aConfig;
            FListenPort = aListenPort;
            FStartedAt = DateTime.Now;

            FDB = new TPOSDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedAdmin(aConfig.AdminUser, Bcrypt.BcryptHash(aConfig.AdminPassword));
            FDB.SeedDemoData();

            FSessions = new TPOSSessionStore(480, true);
            FPages = new TPOSPages(aConfig);
        }

        public void Dispose()
        {
            lock (FPasskeysLock)
            {
                foreach (KeyValuePair<string, TPOSPasskeys> vPair in FPasskeys)
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

        // The Kestrel port, for the startup banner Program.cs prints.
        public int ListenPort
        {
            get { return FListenPort; }
        }

        // ----- whitelisted one-shot banners ----- //

        // A flash is carried in the URL as a short code, never as free text: a
        // reflected message is a stored XSS waiting to happen, and there is no
        // reason for a till to echo the query string.
        // Mirrors sgcPOS_Server.pas:429-452.
        private static string FlashText(string aCode)
        {
            if (aCode == "added")
                return "Line added.";
            if (aCode == "parked")
                return "Sale parked.";
            if (aCode == "recalled")
                return "Parked sale recalled to the till.";
            if (aCode == "paid")
                return "Payment taken.";
            if (aCode == "refunded")
                return "Sale refunded and stock restored.";
            if (aCode == "shift_open")
                return "Shift opened.";
            if (aCode == "shift_closed")
                return "Shift closed and the variance recorded.";
            if (aCode == "saved")
                return "Saved.";
            if (aCode == "deleted")
                return "Deactivated.";
            if (aCode == "customer")
                return "Customer attached to the sale.";
            return "";
        }

        // Mirrors sgcPOS_Server.pas:454-470.
        private static string ErrorText(string aCode)
        {
            if (aCode == "pin")
                return "That PIN is not a manager or admin PIN. Nothing was changed.";
            if (aCode == "pin_required")
                return "A manager PIN is required for this action.";
            if (aCode == "noshift")
                return "Open a shift before ringing anything up.";
            if (aCode == "empty")
                return "There is nothing on the till to do that with.";
            if (aCode == "notfound")
                return "That record does not exist.";
            if (aCode == "denied")
                return "Your role is not allowed to do that.";
            if (aCode == "amount")
                return "That amount is not a number.";
            return "";
        }

        // ----- passkey factory (request-derived rpId + origin) ----- //

        // Returns the TPOSPasskeys for THIS request's origin, creating and
        // caching it on first use. RP id = the request host name (no port),
        // origin = scheme://host (with port).
        private TPOSPasskeys GetPasskeys(HttpContext aCtx)
        {
            string vRPID = aCtx.Request.Host.Host;
            if (string.IsNullOrEmpty(vRPID))
                vRPID = "localhost";
            string vOrigin = aCtx.Request.Scheme + "://" + aCtx.Request.Host.Value;
            string vKey = vRPID + "|" + vOrigin;

            lock (FPasskeysLock)
            {
                TPOSPasskeys oPasskeys;
                if (!FPasskeys.TryGetValue(vKey, out oPasskeys))
                {
                    oPasskeys = new TPOSPasskeys(FDB, vRPID, "sgcPOS Till", vOrigin);
                    FPasskeys[vKey] = oPasskeys;
                }
                return oPasskeys;
            }
        }

        // ----- request param helpers (query + form merged, trimmed) ----- //

        // First value for aName: query wins over form, matching the Delphi
        // merged ARequestInfo.Params. The Delphi Param() trims, so this does
        // too. aForm is null for GET / non-form requests.
        private static string GetParam(HttpContext aCtx, IFormCollection aForm, string aName)
        {
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues) && vValues.Count > 0)
                return (vValues[0] ?? "").Trim();
            if (aForm != null && aForm.TryGetValue(aName, out vValues) && vValues.Count > 0)
                return (vValues[0] ?? "").Trim();
            return "";
        }

        // Mirrors TPOSServer.ParamInt (sgcPOS_Server.pas:975-984).
        private static long ParamInt(HttpContext aCtx, IFormCollection aForm, string aName,
            long aDefault)
        {
            string vText = GetParam(aCtx, aForm, aName);
            long vResult;
            if (vText == "" || !long.TryParse(vText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return aDefault;
            return vResult;
        }

        // Mirrors TPOSServer.ParamMoney (sgcPOS_Server.pas:986-991): money never
        // goes through decimal.Parse, always through the hand-walked
        // POSDB.POSParseMoney, so the same posted text means the same amount on
        // every locale.
        private static decimal ParamMoney(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            decimal vResult;
            if (!POSDB.POSParseMoney(GetParam(aCtx, aForm, aName), out vResult))
                return 0m;
            return vResult;
        }

        private static long StrToInt64Def(string aValue, long aDefault)
        {
            long vResult;
            if (long.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        // Mirrors Delphi sgcHTMLFloatToStr (sgcHTML_Helpers.pas). The managed
        // library has no public counterpart: the only copy is private inside
        // TsgcHTMLComponent_NumPad (sgcHTML_Component_NumPad.cs:685), so this
        // demo carries its own with the identical contract, a "0.##...#"
        // picture (trailing zeros dropped, no thousands separator, no exponent)
        // rendered with "." as the decimal separator on every locale.
        private static string sgcHTMLFloatToStr(double aValue, int aDecimals)
        {
            int vDecimals = aDecimals;
            if (vDecimals < 0)
                vDecimals = 6;
            if (vDecimals > 15)
                vDecimals = 15;
            string vFormat = "0";
            if (vDecimals > 0)
                vFormat = vFormat + "." + new string('#', vDecimals);
            return aValue.ToString(vFormat, CultureInfo.InvariantCulture);
        }

        // Delphi FormatDateTime('yyyy-mm-dd hh:nn', v) and ('yyyy-mm-dd', v).
        private static string StampMinutes(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static string StampDate(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string IntStr(long aValue)
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

        // Same attributes as sgcPOS_Server.pas:661-669: Path=/, HttpOnly,
        // SameSite=Lax.
        private static void WriteSessionCookie(HttpContext aCtx, string aToken)
        {
            if (string.IsNullOrEmpty(aToken))
                return;
            aCtx.Response.Cookies.Append(CS_POS_SESSION, aToken, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static void ClearSessionCookie(HttpContext aCtx)
        {
            aCtx.Response.Cookies.Delete(CS_POS_SESSION, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        // Mirrors sgcPOS_Server.pas:721-736: only light / dark / system are
        // accepted, anything else falls back to system.
        private static string ReadThemeCookie(HttpContext aCtx)
        {
            string vTheme = ReadCookie(aCtx, CS_POS_THEME_COOKIE).ToLowerInvariant();
            if (vTheme == "light" || vTheme == "dark" || vTheme == "system")
                return vTheme;
            return "system";
        }

        // No HttpOnly here, exactly like sgcPOS_Server.pas:738-751: the theme is
        // a display preference, not a credential.
        private static void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vTheme = (aTheme ?? "").Trim().ToLowerInvariant();
            if (vTheme != "light" && vTheme != "dark" && vTheme != "system")
                vTheme = "system";
            aCtx.Response.Cookies.Append(CS_POS_THEME_COOKIE, vTheme, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromSeconds(31536000),
                SameSite = SameSiteMode.Lax
            });
        }

        // ----- client IP ----- //

        // Honour X-Forwarded-For (first hop) when present, else the connection
        // IP. The Delphi read AContext.Binding.PeerIP; behind Kestrel the till
        // is normally reverse-proxied, so the forwarded header comes first.
        private static string ClientIPOf(HttpContext aCtx)
        {
            try
            {
                StringValues vForwarded;
                if (aCtx.Request.Headers.TryGetValue("X-Forwarded-For", out vForwarded) &&
                    vForwarded.Count > 0)
                {
                    string[] vHops = (vForwarded[0] ?? "").Split(',');
                    if (vHops.Length > 0)
                    {
                        string vFirst = vHops[0].Trim();
                        if (vFirst.Length > 0)
                            return vFirst;
                    }
                }
                System.Net.IPAddress oIP = aCtx.Connection.RemoteIpAddress;
                if (oIP == null)
                    return "";
                // Normalise an IPv4-mapped IPv6 loopback to plain 127.0.0.1.
                if (oIP.IsIPv4MappedToIPv6)
                    oIP = oIP.MapToIPv4();
                return oIP.ToString();
            }
            catch
            {
                return "";
            }
        }

        private static string RefererOrRoot(HttpContext aCtx)
        {
            StringValues vReferer;
            if (aCtx.Request.Headers.TryGetValue("Referer", out vReferer) &&
                vReferer.Count > 0)
            {
                string vValue = vReferer[0] ?? "";
                if (vValue != "")
                    return vValue;
            }
            return "/";
        }

        // ----- sessions / roles ----- //

        public bool CurrentSession(HttpContext aCtx, out TPOSSession aSession)
        {
            aSession = null;
            string vToken = ReadCookie(aCtx, CS_POS_SESSION);
            if (vToken == "")
                return false;
            return FSessions.TryGet(vToken, out aSession);
        }

        private static bool IsBackOffice(TPOSSession aSession)
        {
            return string.Equals(aSession.Role, POSConst.CS_ROLE_ADMIN,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(aSession.Role, POSConst.CS_ROLE_MANAGER,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAdmin(TPOSSession aSession)
        {
            return string.Equals(aSession.Role, POSConst.CS_ROLE_ADMIN,
                StringComparison.OrdinalIgnoreCase);
        }

        // ----- response helpers (return an IResult) ----- //

        private static IResult Html(int aCode, string aHTML)
        {
            return Results.Content(aHTML, CS_CONTENT_TYPE_HTML, null, aCode);
        }

        private static IResult Text(int aCode, string aText)
        {
            return Results.Content(aText, CS_CONTENT_TYPE_TEXT, null, aCode);
        }

        private static IResult Json(int aCode, string aJSON)
        {
            return Results.Content(aJSON, CS_CONTENT_TYPE_JSON, null, aCode);
        }

        private static IResult Redirect(string aLocation)
        {
            // 302, so a POST -> redirect -> GET drops the method and body,
            // exactly like the Delphi Redirect (sgcPOS_Server.pas:819-832).
            return Results.Redirect(aLocation);
        }

        // The managed shape of the Delphi WriteStream (sgcPOS_Server.pas:862-874),
        // which sends "Content-Disposition: inline" so a receipt or an X / Z report
        // opens in a browser tab and the cashier can print it straight away.
        // Results.File(bytes, type, name) always sends "attachment", which would
        // force a download instead, so the header is written by hand here and the
        // 60.HTML host and this one stay byte-for-byte equivalent on the wire.
        private sealed class TPOSInlineFileResult : IResult
        {
            private readonly byte[] FBytes;
            private readonly string FContentType;
            private readonly string FFileName;

            public TPOSInlineFileResult(byte[] aBytes, string aContentType,
                string aFileName)
            {
                FBytes = aBytes;
                FContentType = aContentType;
                FFileName = aFileName;
            }

            public Task ExecuteAsync(HttpContext aCtx)
            {
                aCtx.Response.StatusCode = 200;
                aCtx.Response.ContentType = FContentType;
                if (FFileName != "")
                    aCtx.Response.Headers["Content-Disposition"] =
                        "inline; filename=\"" + FFileName + "\"";
                aCtx.Response.ContentLength = FBytes.Length;
                return aCtx.Response.Body.WriteAsync(FBytes, 0, FBytes.Length);
            }
        }

        private static IResult FileInline(byte[] aBytes, string aContentType,
            string aFileName)
        {
            return new TPOSInlineFileResult(aBytes, aContentType, aFileName);
        }

        private static async Task<string> ReadRawBodyAsync(HttpContext aCtx)
        {
            using (StreamReader oReader = new StreamReader(aCtx.Request.Body,
                Encoding.UTF8, false, 1024, true))
            {
                return await oReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        // ----- auth gates (used by the Program.cs endpoint mapping) ----- //

        // Session-protected endpoint: redirect to /login when signed out
        // (sgcPOS_Server.pas:2534-2539).
        public IResult Guarded(HttpContext aCtx, Func<TPOSSession, IResult> aFn)
        {
            TPOSSession oSession;
            if (!CurrentSession(aCtx, out oSession))
                return Redirect("/login");
            return aFn(oSession);
        }

        // Back-office endpoint: manager or admin only, otherwise back to the
        // till with the whitelisted denial banner (sgcPOS_Server.pas:2723-2733).
        public IResult GuardedBackOffice(HttpContext aCtx, Func<TPOSSession, IResult> aFn)
        {
            TPOSSession oSession;
            if (!CurrentSession(aCtx, out oSession))
                return Redirect("/login");
            if (!IsBackOffice(oSession))
                return Redirect("/?err=denied");
            return aFn(oSession);
        }

        // Admin-only endpoint (sgcPOS_Server.pas:2854-2860).
        public IResult GuardedAdmin(HttpContext aCtx, Func<TPOSSession, IResult> aFn)
        {
            TPOSSession oSession;
            if (!CurrentSession(aCtx, out oSession))
                return Redirect("/login");
            if (!IsAdmin(oSession))
                return Redirect("/?err=denied");
            return aFn(oSession);
        }

        // ----- audit ----- //

        // Every mutation writes one row. A failure to audit never fails the
        // request, matching sgcPOS_Server.pas:1005-1014.
        private void Audit(HttpContext aCtx, TPOSSession aSession, string aAction,
            string aEntity, long aEntityId, string aDetail)
        {
            try
            {
                FDB.AddAudit(aSession.UserId, aAction, aEntity, aEntityId, aDetail,
                    ClientIPOf(aCtx));
            }
            catch { }
        }

        // ----- page context ----- //

        // Builds the shell context for a request: role, theme, shift state and
        // the one-shot banners decoded from WHITELISTED ?flash= / ?err= codes.
        // Mirrors sgcPOS_Server.pas:1032-1060.
        private TPOSPageCtx BuildCtx(HttpContext aCtx, TPOSSession aSession, string aMenu)
        {
            TPOSPageCtx oCtx = new TPOSPageCtx();
            oCtx.UserId = aSession.UserId;
            oCtx.DisplayName = aSession.Username;
            TPOSUser oUser;
            if (FDB.GetUserById(aSession.UserId, out oUser) && oUser.DisplayName != "")
                oCtx.DisplayName = oUser.DisplayName;
            oCtx.Role = aSession.Role;
            oCtx.Theme = ReadThemeCookie(aCtx);
            oCtx.Menu = aMenu;
            oCtx.Flash = FlashText(GetParam(aCtx, null, "flash"));
            oCtx.Error = ErrorText(GetParam(aCtx, null, "err"));
            TPOSShift oShift;
            oCtx.ShiftOpen = FDB.GetOpenShift(aSession.UserId, out oShift);
            if (oCtx.ShiftOpen)
                oCtx.ShiftId = oShift.Id;
            else
                oCtx.ShiftId = 0;
            oCtx.ParkedCount = FDB.ListParkedSales(aSession.UserId).Length;
            return oCtx;
        }

        // The current basket of the signed-in cashier, created on demand
        // (sgcPOS_Server.pas:1062-1071).
        private long CurrentSaleId(TPOSSession aSession)
        {
            long vShiftId = 0;
            TPOSShift oShift;
            if (FDB.GetOpenShift(aSession.UserId, out oShift))
                vShiftId = oShift.Id;
            return FDB.GetOrCreateOpenSale(aSession.UserId, vShiftId);
        }

        // Re-renders the cart fragment for the caller's current basket
        // (sgcPOS_Server.pas:1073-1091).
        private IResult RespondCart(HttpContext aCtx, TPOSSession aSession,
            string aNotice, string aColor)
        {
            long vSaleId = CurrentSaleId(aSession);
            TPOSSale oSale;
            if (!FDB.GetSale(vSaleId, out oSale))
                return Text(404, "no sale");
            TPOSSaleLine[] vLines = FDB.GetSaleLines(vSaleId);
            TPOSPageCtx oCtx = BuildCtx(aCtx, aSession, "till");
            return Html(200, FPages.BuildCartFragment(oSale, vLines, oCtx, aNotice, aColor));
        }

        // ----- public: health, assets, theme ----- //

        // sgcPOS_Server.pas:2493-2499.
        public IResult Healthz()
        {
            int vUptime = (int)(DateTime.Now - FStartedAt).TotalSeconds;
            return Text(200, "ok " + CS_POS_SERVER_VERSION + " uptime " +
                IntStr(vUptime) + "s");
        }

        // sgcPOS_Server.pas:900-909.
        public IResult FaviconSVG(HttpContext aCtx)
        {
            aCtx.Response.Headers["Cache-Control"] = "public, max-age=86400";
            return Results.Content(CS_FAVICON_SVG, CS_CONTENT_TYPE_SVG, null, 200);
        }

        // Promotion artwork, generated here rather than pulled from a CDN so the
        // till keeps working with the shop's internet connection down.
        // Mirrors TPOSServer.ServePromoSVG (sgcPOS_Server.pas:934-985).
        public IResult PromoSVG(HttpContext aCtx, string aId)
        {
            long vId;
            if (!long.TryParse(aId ?? "", NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vId))
                return Results.StatusCode(404);

            string vName = "Promotion";
            TPOSPromotion[] vPromos = FDB.ListPromotions();
            for (int vI = 0; vI < vPromos.Length; vI++)
                if (vPromos[vI].Id == vId)
                    vName = vPromos[vI].Name;
            // The Delphi indexes with a raw "mod". A negative id in the URL is
            // normalised here so the tint lookup can never leave the array.
            int vTint = (int)(((vId % CS_PROMO_TINTS.Length) +
                CS_PROMO_TINTS.Length) % CS_PROMO_TINTS.Length);

            string vSVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
                "viewBox=\"0 0 640 200\" role=\"img\" aria-label=\"" +
                sgcHTMLHelpers.sgcHTMLAttrEncode(vName) + "\">" +
                "<rect width=\"640\" height=\"200\" rx=\"16\" " +
                "fill=\"" + CS_PROMO_TINTS[vTint] + "\"/>" +
                "<circle cx=\"560\" cy=\"40\" r=\"90\" fill=\"#FFFFFF\" opacity=\"0.12\"/>" +
                "<text x=\"32\" y=\"96\" font-family=\"Arial,Helvetica,sans-serif\" " +
                "font-weight=\"800\" font-size=\"34\" fill=\"#FFFFFF\">" +
                sgcHTMLHelpers.sgcHTMLEncode(vName) + "</text>" +
                "<text x=\"32\" y=\"136\" font-family=\"Arial,Helvetica,sans-serif\" " +
                "font-size=\"20\" fill=\"#FFFFFF\" opacity=\"0.85\">" +
                sgcHTMLHelpers.sgcHTMLEncode(FConfig.StoreName) + "</text></svg>";

            aCtx.Response.Headers["Cache-Control"] = "public, max-age=3600";
            return Results.Content(vSVG, CS_CONTENT_TYPE_SVG, null, 200);
        }

        // The navbar dropdown is a link (GET ?set=), a form post uses "theme".
        // Mirrors TPOSServer.HandleSetTheme (sgcPOS_Server.pas:753-772), which
        // is reached on ANY verb and redirects back to the Referer.
        public IResult SetTheme(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = GetParam(aCtx, aForm, "set");
            if (vTheme == "")
                vTheme = GetParam(aCtx, aForm, "theme");
            WriteThemeCookie(aCtx, vTheme);
            return Redirect(RefererOrRoot(aCtx));
        }

        // ----- public: login / logout ----- //

        // sgcPOS_Server.pas:1095-1109.
        public IResult LoginGet(HttpContext aCtx)
        {
            TPOSSession oSession;
            if (CurrentSession(aCtx, out oSession))
                return Redirect("/");
            return Html(200, FPages.BuildLoginPage(ReadThemeCookie(aCtx),
                ErrorText(GetParam(aCtx, null, "err")), FConfig.AdminUser,
                FConfig.AdminPassword));
        }

        // sgcPOS_Server.pas:1111-1140.
        public IResult LoginPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vUser = GetParam(aCtx, aForm, "username");
            string vPwd = GetParam(aCtx, aForm, "password");

            if (vUser == "" || vPwd == "")
                return Html(400, FPages.BuildLoginPage(vTheme,
                    "User and password are both required.", vUser, ""));

            TPOSUser oUser;
            if (!FDB.AuthenticateUser(vUser, vPwd, out oUser))
                return Html(401, FPages.BuildLoginPage(vTheme,
                    "That user name and password do not match.", vUser, ""));

            string vToken = FSessions.CreateSession(oUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            TPOSSession oSession;
            if (FSessions.TryGet(vToken, out oSession))
                Audit(aCtx, oSession, "login", "user", oUser.Id, oUser.Username);
            return Redirect("/");
        }

        // sgcPOS_Server.pas:1142-1153. Reached on any verb.
        public IResult Logout(HttpContext aCtx)
        {
            string vToken = ReadCookie(aCtx, CS_POS_SESSION);
            if (vToken != "")
                FSessions.Destroy_(vToken);
            ClearSessionCookie(aCtx);
            return Redirect("/login");
        }

        // ----- public: passkeys (WebAuthn sign-in) ----- //

        // sgcPOS_Server.pas:1155-1170. Passkey sign-in is public by definition,
        // so there is no session gate on either endpoint.
        public IResult PasskeyLoginOptions(HttpContext aCtx)
        {
            try
            {
                return Json(200, GetPasskeys(aCtx).BeginLogin(""));
            }
            catch (Exception E)
            {
                return Json(400, "{\"error\":\"" +
                    sgcHTMLHelpers.sgcJSEncode(E.Message) + "\"}");
            }
        }

        // sgcPOS_Server.pas:1172-1216.
        public async Task<IResult> PasskeyLoginVerify(HttpContext aCtx)
        {
            string vBody;
            try
            {
                vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
            }
            catch
            {
                vBody = "";
            }

            try
            {
                long vUserId;
                GetPasskeys(aCtx).FinishLogin(vBody, out vUserId);
                TPOSUser oUser;
                if (!FDB.GetUserById(vUserId, out oUser))
                    throw new EPOSError("Unknown credential owner");
                string vToken = FSessions.CreateSession(oUser, ClientIPOf(aCtx));
                WriteSessionCookie(aCtx, vToken);
                TPOSSession oSession;
                if (FSessions.TryGet(vToken, out oSession))
                    Audit(aCtx, oSession, "login_passkey", "user", oUser.Id, oUser.Username);
                return Json(200, "{\"verified\":true}");
            }
            catch (Exception E)
            {
                return Json(401, "{\"verified\":false,\"error\":\"" +
                    sgcHTMLHelpers.sgcJSEncode(E.Message) + "\"}");
            }
        }

        // ----- the till ----- //

        // sgcPOS_Server.pas:1220-1252. The dispatch always calls this with
        // category 0 (the "all products" rail button), so the tile grid starts
        // unfiltered and htmx swaps it from /till/category/<id>.
        public IResult TillGet(HttpContext aCtx, TPOSSession aSession)
        {
            TPOSPageCtx oCtx = BuildCtx(aCtx, aSession, "till");
            TPOSCategory[] vCats = FDB.ListCategories();
            TPOSProduct[] vProds = FDB.ListProducts("", 0, true, "name", "asc");
            TPOSPromotion[] vPromos = FDB.ListPromotions();
            long vSaleId = CurrentSaleId(aSession);
            TPOSSale oSale;
            // GetOrCreateOpenSale always leaves a row behind, so this lookup
            // succeeds; the empty instance stands in for the Delphi zeroed
            // record rather than handing the page builder a null.
            if (!FDB.GetSale(vSaleId, out oSale))
                oSale = new TPOSSale();
            TPOSSaleLine[] vLines = FDB.GetSaleLines(vSaleId);

            // The customer picker is fed straight from a query cursor, no REST tier.
            using (TPOSQuery oCustomers = FDB.OpenQuery(
                "SELECT id, name FROM customers ORDER BY name LIMIT 200"))
            {
                oCustomers.Open();
                return Html(200, FPages.BuildTillPage(vCats, vProds, 0, oSale, vLines,
                    vPromos, oCustomers, oCtx));
            }
        }

        // sgcPOS_Server.pas:1254-1259.
        public IResult TillCategory(string aId)
        {
            long vCatId = StrToInt64Def(aId, 0);
            return Html(200, FPages.BuildTilesFragment(
                FDB.ListProducts("", vCatId, true, "name", "asc"), ""));
        }

        // sgcPOS_Server.pas:1261-1270.
        public IResult TillSearch(HttpContext aCtx)
        {
            string vQuery = GetParam(aCtx, null, "q");
            return Html(200, FPages.BuildTilesFragment(
                FDB.ListProducts(vQuery, 0, true, "name", "asc"), vQuery));
        }

        // sgcPOS_Server.pas:1272-1290.
        public IResult TillAdd(HttpContext aCtx, IFormCollection aForm, TPOSSession aSession)
        {
            long vProductId = ParamInt(aCtx, aForm, "product_id", 0);
            long vSaleId = CurrentSaleId(aSession);
            TPOSProduct oProd;
            if (vProductId <= 0 || !FDB.GetProduct(vProductId, out oProd))
                return RespondCart(aCtx, aSession, "That product no longer exists.", "danger");
            FDB.AddLine(vSaleId, vProductId, 1);
            Audit(aCtx, aSession, "line_add", "sale", vSaleId, oProd.Name);
            return RespondCart(aCtx, aSession, oProd.Name + " added", "");
        }

        // sgcPOS_Server.pas:1292-1311. The line id is scoped to the caller's own
        // basket: a line id from another till resolves to nothing.
        public IResult TillQty(HttpContext aCtx, IFormCollection aForm, TPOSSession aSession)
        {
            long vLineId = ParamInt(aCtx, aForm, "line_id", 0);
            double vQty;
            if (!POSDB.POSParseQty(GetParam(aCtx, aForm, "qty"), out vQty))
                return RespondCart(aCtx, aSession, "That quantity is not a number.", "danger");
            long vSaleId = CurrentSaleId(aSession);
            FDB.SetLineQty(vSaleId, vLineId, vQty);
            return RespondCart(aCtx, aSession, "", "");
        }

        // sgcPOS_Server.pas:1313-1323.
        public IResult TillRemove(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            long vLineId = ParamInt(aCtx, aForm, "line_id", 0);
            long vSaleId = CurrentSaleId(aSession);
            FDB.RemoveLine(vSaleId, vLineId);
            return RespondCart(aCtx, aSession, "Line removed", "");
        }

        // A discount above the configured threshold needs a manager PIN, and the
        // decision is taken HERE. The browser is free to hide the modal, edit the
        // DOM or post this form by hand; without a PIN that bcrypt-verifies
        // against a manager or admin account, nothing changes.
        // Mirrors sgcPOS_Server.pas:1325-1372.
        public IResult TillDiscount(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            decimal vAmount;
            if (!POSDB.POSParseMoney(GetParam(aCtx, aForm, "amount"), out vAmount))
                return RespondCart(aCtx, aSession, "That amount is not a number.", "danger");
            long vSaleId = CurrentSaleId(aSession);

            if (vAmount > FConfig.DiscountPinThreshold)
            {
                string vPin = GetParam(aCtx, aForm, "pin");
                TPOSUser oManager;
                if (!FDB.VerifyManagerPin(vPin, out oManager))
                {
                    Audit(aCtx, aSession, "discount_refused", "sale", vSaleId,
                        POSDB.POSMoneyStr(vAmount));
                    return Html(403, FPages.BuildCartFragment(new TPOSSale(), null,
                        BuildCtx(aCtx, aSession, "till"),
                        "A discount over " + FPages.Money(FConfig.DiscountPinThreshold) +
                        " needs a manager PIN.", "danger"));
                }
                Audit(aCtx, aSession, "discount_authorised", "sale", vSaleId,
                    POSDB.POSMoneyStr(vAmount) + " by " + oManager.Username);
            }

            FDB.SetSaleDiscount(vSaleId, vAmount);
            return RespondCart(aCtx, aSession, "Discount applied", "");
        }

        // sgcPOS_Server.pas:1374-1385.
        public IResult TillCustomer(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            long vCustomerId = ParamInt(aCtx, aForm, "customer_id", 0);
            long vSaleId = CurrentSaleId(aSession);
            FDB.SetSaleCustomer(vSaleId, vCustomerId);
            return Redirect("/?flash=customer");
        }

        // sgcPOS_Server.pas:1387-1398. Reached on any verb: the camera scanner
        // posts, the manual-entry box can arrive as a GET.
        public IResult TillScan(HttpContext aCtx, IFormCollection aForm, TPOSSession aSession)
        {
            string vCode = GetParam(aCtx, aForm, "scan");
            if (vCode == "")
                vCode = GetParam(aCtx, aForm, "q");
            long vSaleId = CurrentSaleId(aSession);
            TPOSProduct oProd;
            if (vCode == "" || !FDB.GetProductByBarcode(vCode, out oProd))
                return RespondCart(aCtx, aSession, "No product carries that code.", "danger");
            FDB.AddLine(vSaleId, oProd.Id, 1);
            return RespondCart(aCtx, aSession, oProd.Name + " scanned", "");
        }

        // sgcPOS_Server.pas:1400-1414.
        public IResult TillPark(HttpContext aCtx, TPOSSession aSession)
        {
            long vSaleId = CurrentSaleId(aSession);
            if (!FDB.ParkSale(vSaleId))
                return Redirect("/?err=empty");
            Audit(aCtx, aSession, "park", "sale", vSaleId, "");
            return Redirect("/till/parked?flash=parked");
        }

        // sgcPOS_Server.pas:1416-1422.
        public IResult TillParked(HttpContext aCtx, TPOSSession aSession)
        {
            return Html(200, FPages.BuildParkedPage(FDB.ListParkedSales(aSession.UserId),
                BuildCtx(aCtx, aSession, "parked")));
        }

        // Never trust an id from the request: the sale must be parked AND belong
        // to the caller before it is recalled (sgcPOS_Server.pas:1424-1443).
        public IResult TillRecall(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            long vSaleId = ParamInt(aCtx, aForm, "sale_id", 0);
            TPOSSale oSale;
            if (vSaleId <= 0 || !FDB.GetSale(vSaleId, out oSale) ||
                !string.Equals(oSale.Status, POSConst.CS_SALE_PARKED,
                    StringComparison.OrdinalIgnoreCase) ||
                oSale.UserId != aSession.UserId)
                return Redirect("/till/parked?err=notfound");
            FDB.RecallSale(vSaleId, aSession.UserId);
            Audit(aCtx, aSession, "recall", "sale", vSaleId, "");
            return Redirect("/?flash=recalled");
        }

        // sgcPOS_Server.pas:1445-1473.
        public IResult TillPayGet(HttpContext aCtx, TPOSSession aSession)
        {
            long vSaleId = CurrentSaleId(aSession);
            TPOSSale oSale;
            if (!FDB.GetSale(vSaleId, out oSale))
                return Redirect("/?err=empty");
            TPOSSaleLine[] vLines = FDB.GetSaleLines(vSaleId);
            if (vLines.Length == 0)
                return Redirect("/?err=empty");
            TPOSPayment[] vPayments = FDB.ListPayments(vSaleId);
            decimal vPaid = FDB.SumPayments(vSaleId);
            decimal vDue = oSale.Total - vPaid;
            if (vDue < 0)
                vDue = 0;
            return Html(200, FPages.BuildPayPage(oSale, vLines, vPayments, vPaid, vDue,
                BuildCtx(aCtx, aSession, "till")));
        }

        // Takes one tender. Split payments are simply several posts: each one
        // records what it took, and the sale only completes when the recorded
        // payments cover the total.
        //
        // The change is computed here, from the recorded rows: change = tendered
        // minus what is still due, and only cash gives change back. What the
        // drawer keeps is always amount - change_given, so the shift's expected
        // cash stays derivable from the payments table alone.
        // Mirrors sgcPOS_Server.pas:1475-1548.
        public IResult TillPayPost(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            long vSaleId = CurrentSaleId(aSession);
            TPOSSale oSale;
            if (!FDB.GetSale(vSaleId, out oSale))
                return Redirect("/?err=empty");
            if (FDB.GetSaleLines(vSaleId).Length == 0)
                return Redirect("/?err=empty");

            decimal vAmount;
            if (!POSDB.POSParseMoney(GetParam(aCtx, aForm, "amount"), out vAmount))
                return Redirect("/till/pay?err=amount");
            if (vAmount <= 0)
                return Redirect("/till/pay?err=amount");

            string vMethod = GetParam(aCtx, aForm, "method").ToLowerInvariant();
            decimal vPaid = FDB.SumPayments(vSaleId);
            decimal vDue = oSale.Total - vPaid;
            if (vDue < 0)
                vDue = 0;

            decimal vChange = 0;
            if (vAmount > vDue)
            {
                if (vMethod == POSConst.CS_PAY_CASH)
                    // Cash overpayment: the difference goes back to the customer.
                    vChange = vAmount - vDue;
                else
                    // A card, a voucher or points are never over-tendered: charge
                    // the outstanding amount and nothing more.
                    vAmount = vDue;
            }

            FDB.AddPayment(vSaleId, vMethod, vAmount, vChange);
            vPaid = FDB.SumPayments(vSaleId);

            if (vPaid >= oSale.Total)
            {
                string vRef = FDB.CompleteSale(vSaleId);
                Audit(aCtx, aSession, "sale_completed", "sale", vSaleId,
                    vRef + " " + POSDB.POSMoneyStr(oSale.Total));
                return Redirect("/till/receipt/" + IntStr(vSaleId) + "?flash=paid");
            }

            Audit(aCtx, aSession, "payment", "sale", vSaleId,
                vMethod + " " + POSDB.POSMoneyStr(vAmount));
            return Redirect("/till/pay");
        }

        // A cashier only sees their own receipts; a manager or admin sees them
        // all (sgcPOS_Server.pas:1550-1569).
        public IResult Receipt(HttpContext aCtx, TPOSSession aSession, string aSaleId)
        {
            long vSaleId;
            if (!long.TryParse(aSaleId ?? "", NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vSaleId))
                return Results.StatusCode(404);
            TPOSSale oSale;
            if (!FDB.GetSale(vSaleId, out oSale))
                return Redirect("/?err=notfound");
            if (!IsBackOffice(aSession) && oSale.UserId != aSession.UserId)
                return Redirect("/?err=denied");
            return Html(200, FPages.BuildReceiptPage(oSale, FDB.GetSaleLines(vSaleId),
                FDB.ListPayments(vSaleId), BuildCtx(aCtx, aSession, "till")));
        }

        // The 80mm till-roll receipt.
        //
        // This is the proof that the PDF writer handles a real, non-A4 layout: a
        // narrow page with a header, a wrapped line table and a totals block. The
        // document is laid out inside the top-left 80mm of an A5 sheet and the
        // page rectangle is then narrowed to exactly that region by
        // POSNarrowPDFPage. Mirrors sgcPOS_Server.pas:1571-1712.
        public IResult ReceiptPDF(TPOSSession aSession, string aSaleId)
        {
            long vSaleId;
            if (!long.TryParse(aSaleId ?? "", NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vSaleId))
                return Results.StatusCode(404);
            TPOSSale oSale;
            if (!FDB.GetSale(vSaleId, out oSale))
                return Results.StatusCode(404);
            if (!IsBackOffice(aSession) && oSale.UserId != aSession.UserId)
                return Results.StatusCode(403);

            TPOSSaleLine[] vLines = FDB.GetSaleLines(vSaleId);
            TPOSPayment[] vPayments = FDB.ListPayments(vSaleId);
            decimal vPaid = 0;
            decimal vChange = 0;
            for (int vI = 0; vI < vPayments.Length; vI++)
            {
                vPaid = vPaid + vPayments[vI].Amount;
                vChange = vChange + vPayments[vI].ChangeGiven;
            }

            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = "Receipt " + oSale.Reference;
            oPDF.Author = FConfig.StoreName;
            oPDF.Subject = "Till receipt";
            // A5 is the base sheet; only the top-left 80mm is ever drawn on.
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA5;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poPortrait;
            oPDF.MarginLeft = CS_ROLL_LEFT;
            oPDF.MarginRight = 148.0 - CS_ROLL_LEFT - CS_ROLL_INNER;
            oPDF.MarginTop = 5;
            oPDF.MarginBottom = 10;
            oPDF.NewPage();

            double vY = 10;

            // Local mirror of the Delphi nested Line() procedure.
            Action<string, string, bool> vLine = (aLabel, aValue, aBold) =>
            {
                if (aBold)
                    oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 9);
                else
                    oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8);
                oPDF.TextRect(CS_ROLL_LEFT, vY, CS_ROLL_INNER / 2, aLabel,
                    TsgcHTMLPDFAlign.paLeft);
                oPDF.TextRect(CS_ROLL_LEFT + (CS_ROLL_INNER / 2), vY, CS_ROLL_INNER / 2,
                    aValue, TsgcHTMLPDFAlign.paRight);
                vY = vY + 4.2;
            };

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
            oPDF.TextRect(CS_ROLL_LEFT, vY, CS_ROLL_INNER, FConfig.StoreName,
                TsgcHTMLPDFAlign.paCenter);
            vY = vY + 6;
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 7.5);
            oPDF.TextRect(CS_ROLL_LEFT, vY, CS_ROLL_INNER, FConfig.StoreAddress,
                TsgcHTMLPDFAlign.paCenter);
            vY = vY + 4;
            oPDF.TextRect(CS_ROLL_LEFT, vY, CS_ROLL_INNER, FConfig.StoreTaxId,
                TsgcHTMLPDFAlign.paCenter);
            vY = vY + 5;
            oPDF.SetLineWidth(0.3);
            oPDF.Line(CS_ROLL_LEFT, vY, CS_ROLL_LEFT + CS_ROLL_INNER, vY);
            vY = vY + 5;

            vLine("Receipt", oSale.Reference, false);
            vLine("Date", StampMinutes(oSale.CreatedAt), false);
            vLine("Cashier", oSale.UserName, false);
            if (oSale.CustomerName != "")
                vLine("Customer", oSale.CustomerName, false);
            if (string.Equals(oSale.Status, POSConst.CS_SALE_REFUNDED,
                StringComparison.OrdinalIgnoreCase))
                vLine("Status", "REFUNDED", true);

            vY = vY + 2;
            oPDF.CurrentY = vY;
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 7.5);
            oPDF.BeginTable(32, 8, 14, 16);
            oPDF.TableHeader("Item", "Qty", "Unit", "Total");
            for (int vI = 0; vI < vLines.Length; vI++)
                oPDF.TableRow(vLines[vI].ProductName,
                    sgcHTMLFloatToStr(vLines[vI].Qty, 3),
                    POSDB.POSMoneyStr(vLines[vI].UnitPrice),
                    POSDB.POSMoneyStr(vLines[vI].LineTotal));
            oPDF.EndTable();

            vY = oPDF.CurrentY + 4;
            oPDF.Line(CS_ROLL_LEFT, vY - 2, CS_ROLL_LEFT + CS_ROLL_INNER, vY - 2);
            vLine("Subtotal", FConfig.CurrencySymbol + POSDB.POSMoneyStr(oSale.Subtotal),
                false);
            vLine("Discount", "-" + FConfig.CurrencySymbol +
                POSDB.POSMoneyStr(oSale.Discount), false);
            vLine("Tax", FConfig.CurrencySymbol + POSDB.POSMoneyStr(oSale.Tax), false);
            vLine("TOTAL", FConfig.CurrencySymbol + POSDB.POSMoneyStr(oSale.Total), true);
            vY = vY + 1;
            for (int vI = 0; vI < vPayments.Length; vI++)
                vLine(vPayments[vI].Method.ToUpperInvariant(),
                    FConfig.CurrencySymbol + POSDB.POSMoneyStr(vPayments[vI].Amount), false);
            vLine("Tendered", FConfig.CurrencySymbol + POSDB.POSMoneyStr(vPaid), false);
            vLine("Change", FConfig.CurrencySymbol + POSDB.POSMoneyStr(vChange), true);

            vY = vY + 4;
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 7);
            oPDF.TextRect(CS_ROLL_LEFT, vY, CS_ROLL_INNER, "Thank you, please come again.",
                TsgcHTMLPDFAlign.paCenter);
            vY = vY + 4;
            oPDF.TextRect(CS_ROLL_LEFT, vY, CS_ROLL_INNER,
                "Printed by sgcPOS, built with sgcHTML.", TsgcHTMLPDFAlign.paCenter);
            vY = vY + 6;

            byte[] vBytes;
            using (MemoryStream oStream = new MemoryStream())
            {
                oPDF.SaveToStream(oStream);
                vBytes = oStream.ToArray();
            }
            double vHeight = vY;
            if (vHeight < 60)
                vHeight = 60;
            if (vHeight > 200)
                vHeight = 200;
            vBytes = POSNarrowPDFPage(vBytes, CS_ROLL_W, vHeight);

            return FileInline(vBytes, CS_CONTENT_TYPE_PDF,
                "receipt-" + IntStr(vSaleId) + ".pdf");
        }

        // Refunds are ALWAYS manager-authorised, whatever the caller's own role:
        // the PIN is bcrypt-verified against a manager or admin account here, on
        // the server. A post without one is refused with 403 and nothing changes.
        // Mirrors sgcPOS_Server.pas:1714-1755.
        public IResult Refund(HttpContext aCtx, IFormCollection aForm, TPOSSession aSession,
            string aSaleId)
        {
            long vSaleId;
            if (!long.TryParse(aSaleId ?? "", NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vSaleId))
                return Results.StatusCode(404);
            TPOSSale oSale;
            if (!FDB.GetSale(vSaleId, out oSale))
                return Redirect("/?err=notfound");

            string vPin = GetParam(aCtx, aForm, "pin");
            TPOSUser oManager;
            if (!FDB.VerifyManagerPin(vPin, out oManager))
            {
                Audit(aCtx, aSession, "refund_refused", "sale", vSaleId,
                    "no valid manager PIN");
                return Html(403, FPages.BuildReceiptPage(oSale, FDB.GetSaleLines(vSaleId),
                    FDB.ListPayments(vSaleId), BuildCtx(aCtx, aSession, "till")));
            }
            if (!FDB.RefundSale(vSaleId, oManager.Id))
                return Redirect("/till/receipt/" + IntStr(vSaleId) + "?err=notfound");
            Audit(aCtx, aSession, "refund", "sale", vSaleId,
                "authorised by " + oManager.Username + " " +
                POSDB.POSMoneyStr(oSale.Total));
            return Redirect("/till/receipt/" + IntStr(vSaleId) + "?flash=refunded");
        }

        // ----- shift ----- //

        // sgcPOS_Server.pas:1759-1778.
        public IResult ShiftGet(HttpContext aCtx, TPOSSession aSession)
        {
            TPOSPageCtx oCtx = BuildCtx(aCtx, aSession, "shift");
            TPOSTotals oTotals = new TPOSTotals();
            decimal vExpected = 0;
            TPOSShift oShift;
            if (!FDB.GetOpenShift(aSession.UserId, out oShift))
                oShift = new TPOSShift();
            else
            {
                oTotals = FDB.ShiftTotals(oShift.Id);
                vExpected = FDB.ComputeExpectedCash(oShift.Id);
            }
            return Html(200, FPages.BuildShiftPage(oShift, oTotals, vExpected,
                FDB.ListShifts(30), oCtx));
        }

        // sgcPOS_Server.pas:1780-1797.
        public IResult ShiftOpen(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            decimal vFloat;
            if (!POSDB.POSParseMoney(GetParam(aCtx, aForm, "opening_float"), out vFloat))
                vFloat = 0;
            long vId = FDB.OpenShift(aSession.UserId, vFloat);
            if (vId <= 0)
                return Redirect("/shift?err=denied");
            Audit(aCtx, aSession, "shift_open", "shift", vId, POSDB.POSMoneyStr(vFloat));
            return Redirect("/shift?flash=shift_open");
        }

        // The count comes from the browser. Everything else does NOT: expected
        // cash is recomputed from the recorded payments and the variance is the
        // difference the server works out, so a manipulated form can only
        // misreport what was counted, never what was owed.
        // Mirrors sgcPOS_Server.pas:1799-1831.
        public IResult ShiftClose(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            TPOSShift oShift;
            if (!FDB.GetOpenShift(aSession.UserId, out oShift))
                return Redirect("/shift?err=noshift");
            decimal vCounted;
            if (!POSDB.POSParseMoney(GetParam(aCtx, aForm, "counted_cash"), out vCounted))
                return Redirect("/shift?err=amount");
            decimal vExpected = FDB.ComputeExpectedCash(oShift.Id);
            if (!FDB.CloseShift(oShift.Id, vCounted))
                return Redirect("/shift?err=noshift");
            Audit(aCtx, aSession, "shift_close", "shift", oShift.Id,
                "expected " + POSDB.POSMoneyStr(vExpected) + ", counted " +
                POSDB.POSMoneyStr(vCounted) + ", variance " +
                POSDB.POSMoneyStr(vCounted - vExpected));
            return Redirect("/shift?flash=shift_closed");
        }

        // sgcPOS_Server.pas:1833-1908.
        public IResult ShiftReportPDF(TPOSSession aSession, string aShiftId, bool aIsZ)
        {
            long vShiftId;
            if (!long.TryParse(aShiftId ?? "", NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vShiftId))
                return Results.StatusCode(404);
            TPOSShift oShift;
            if (!FDB.GetShift(vShiftId, out oShift))
                return Results.StatusCode(404);
            if (!IsBackOffice(aSession) && oShift.UserId != aSession.UserId)
                return Results.StatusCode(403);

            TPOSTotals oTotals = FDB.ShiftTotals(vShiftId);
            decimal vExpected = FDB.ComputeExpectedCash(vShiftId);
            string vTitle = aIsZ ? "Z report" : "X report";

            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = vTitle + " shift " + IntStr(vShiftId);
            oPDF.Author = FConfig.StoreName;
            oPDF.HeaderText = FConfig.StoreName + " - " + vTitle;
            oPDF.FooterText = "Page {page} of {pages} - sgcPOS";
            oPDF.ShowPageNumbers = true;
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
            oPDF.BeginTable(70, 60);
            oPDF.TableHeader("Shift " + IntStr(vShiftId), oShift.UserName);
            oPDF.TableRow("Opened", StampMinutes(oShift.OpenedAt));
            // DateTime.MinValue is the managed spelling of the Delphi TDateTime 0
            // the DB layer uses for "not set" (sgcPOS_DB.cs:64).
            if (oShift.ClosedAt > DateTime.MinValue)
                oPDF.TableRow("Closed", StampMinutes(oShift.ClosedAt));
            else
                oPDF.TableRow("Closed", "still open");
            oPDF.TableRow("Sales", IntStr(oTotals.SaleCount));
            oPDF.TableRow("Net of tax", POSDB.POSMoneyStr(oTotals.NetSubtotal));
            oPDF.TableRow("Discount", POSDB.POSMoneyStr(oTotals.Discount));
            oPDF.TableRow("Tax", POSDB.POSMoneyStr(oTotals.Tax));
            oPDF.TableRow("Gross", POSDB.POSMoneyStr(oTotals.Gross));
            oPDF.TableRow("Refunds", IntStr(oTotals.RefundCount) + " / " +
                POSDB.POSMoneyStr(oTotals.Refunded));
            oPDF.TableRow("Cash taken", POSDB.POSMoneyStr(oTotals.CashTaken));
            oPDF.TableRow("Card taken", POSDB.POSMoneyStr(oTotals.CardTaken));
            oPDF.TableRow("Voucher", POSDB.POSMoneyStr(oTotals.VoucherTaken));
            oPDF.TableRow("Loyalty", POSDB.POSMoneyStr(oTotals.LoyaltyTaken));
            oPDF.TableRow("Change given", POSDB.POSMoneyStr(oTotals.ChangeGiven));
            oPDF.TableRow("Opening float", POSDB.POSMoneyStr(oShift.OpeningFloat));
            oPDF.TableRow("Expected cash", POSDB.POSMoneyStr(vExpected));
            if (aIsZ)
            {
                oPDF.TableRow("Counted cash", POSDB.POSMoneyStr(oShift.CountedCash));
                oPDF.TableRow("Variance", POSDB.POSMoneyStr(oShift.Variance));
            }
            oPDF.EndTable();

            byte[] vBytes;
            using (MemoryStream oStream = new MemoryStream())
            {
                oPDF.SaveToStream(oStream);
                vBytes = oStream.ToArray();
            }
            return FileInline(vBytes, CS_CONTENT_TYPE_PDF,
                vTitle.Substring(0, 1).ToLowerInvariant() + "report-" +
                IntStr(vShiftId) + ".pdf");
        }

        // ----- back office: catalogue ----- //

        // sgcPOS_Server.pas:1910-1953.
        public IResult Products(HttpContext aCtx, TPOSSession aSession)
        {
            string vSearch = GetParam(aCtx, null, "q");
            long vCatId = ParamInt(aCtx, null, "cat", 0);
            TPOSProduct[] vAll = FDB.ListProducts(vSearch, vCatId, false,
                GetParam(aCtx, null, "sort"), GetParam(aCtx, null, "dir"));

            int vPageCount = (vAll.Length + CS_PRODUCT_PAGE_SIZE - 1) / CS_PRODUCT_PAGE_SIZE;
            if (vPageCount < 1)
                vPageCount = 1;
            int vPageNo = (int)ParamInt(aCtx, null, "page", 1);
            if (vPageNo < 1)
                vPageNo = 1;
            if (vPageNo > vPageCount)
                vPageNo = vPageCount;
            int vFrom = (vPageNo - 1) * CS_PRODUCT_PAGE_SIZE;

            List<TPOSProduct> oPage = new List<TPOSProduct>();
            List<double[]> oTrend = new List<double[]>();
            int vLast = vFrom + CS_PRODUCT_PAGE_SIZE - 1;
            if (vLast > vAll.Length - 1)
                vLast = vAll.Length - 1;
            for (int vI = vFrom; vI <= vLast; vI++)
            {
                oPage.Add(vAll[vI]);
                oTrend.Add(FDB.GetProductTrend(vAll[vI].Id));
            }

            return Html(200, FPages.BuildProductsPage(oPage.ToArray(), FDB.ListCategories(),
                oTrend.ToArray(), vSearch, vCatId, vPageNo, vPageCount,
                BuildCtx(aCtx, aSession, "products")));
        }

        // sgcPOS_Server.pas:1955-1979.
        public IResult ProductForm(HttpContext aCtx)
        {
            if (GetParam(aCtx, null, "cancel") == "1")
                return Html(200, "");
            long vId = ParamInt(aCtx, null, "id", 0);
            TPOSProduct oProd;
            if (vId > 0 && FDB.GetProduct(vId, out oProd))
                return Html(200, FPages.BuildProductFormFragment(oProd,
                    FDB.ListCategories(), false, ""));
            oProd = new TPOSProduct();
            oProd.Id = 0;
            oProd.Active = true;
            oProd.TaxRate = 21;
            return Html(200, FPages.BuildProductFormFragment(oProd, FDB.ListCategories(),
                true, ""));
        }

        // htmx swapped this form in, so it expects the refreshed grid back, not a
        // whole page. The trend column is left empty on the swap: it costs 12
        // queries a row and the sparkline is a browse-time nicety, not part of
        // the save. Mirrors sgcPOS_Server.pas:1981-2015.
        public IResult ProductSave(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            TPOSProduct oProd = new TPOSProduct();
            oProd.Id = ParamInt(aCtx, aForm, "id", 0);
            oProd.Sku = GetParam(aCtx, aForm, "sku");
            oProd.Barcode = GetParam(aCtx, aForm, "barcode");
            oProd.Name = GetParam(aCtx, aForm, "name");
            oProd.CategoryId = ParamInt(aCtx, aForm, "category_id", 0);
            oProd.Price = ParamMoney(aCtx, aForm, "price");
            oProd.Cost = ParamMoney(aCtx, aForm, "cost");
            decimal vTax;
            if (POSDB.POSParseMoney(GetParam(aCtx, aForm, "tax_rate"), out vTax))
                oProd.TaxRate = (double)vTax;
            else
                oProd.TaxRate = 0;
            oProd.Stock = (int)ParamInt(aCtx, aForm, "stock", 0);
            oProd.Active = GetParam(aCtx, aForm, "active") != "";
            long vId = FDB.SaveProduct(oProd);
            Audit(aCtx, aSession, "product_save", "product", vId, oProd.Name);
            return Html(200, FPages.BuildProductsGridFragment(
                FDB.ListProducts("", 0, false, "name", "asc"), null));
        }

        // sgcPOS_Server.pas:2017-2031.
        public IResult ProductDelete(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            long vId = ParamInt(aCtx, aForm, "id", 0);
            if (vId > 0)
            {
                FDB.DeleteProduct(vId);
                Audit(aCtx, aSession, "product_delete", "product", vId, "");
            }
            return Html(200, FPages.BuildProductsGridFragment(
                FDB.ListProducts("", 0, false, "name", "asc"), null));
        }

        // The catalogue export goes through the Grid's own SaveToXLSXStream /
        // SaveToPDFStream, so the bytes are produced server-side from the very
        // rows the page shows, never from client JS.
        // Mirrors sgcPOS_Server.pas:2033-2088.
        public IResult ProductsExport(bool aPDF)
        {
            TPOSProduct[] vProds = FDB.ListProducts("", 0, false, "name", "asc");
            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.ExportFileName = "products";
            oGrid.ExportSheetName = "Products";
            oGrid.Columns.Add().Title = "SKU";
            oGrid.Columns.Add().Title = "Barcode";
            oGrid.Columns.Add().Title = "Product";
            oGrid.Columns.Add().Title = "Category";
            oGrid.Columns.Add().Title = "Price";
            oGrid.Columns.Add().Title = "Cost";
            oGrid.Columns.Add().Title = "Tax %";
            oGrid.Columns.Add().Title = "Stock";
            for (int vI = 0; vI < vProds.Length; vI++)
                oGrid.AddRow(vProds[vI].Sku, vProds[vI].Barcode, vProds[vI].Name,
                    vProds[vI].CategoryName, POSDB.POSMoneyStr(vProds[vI].Price),
                    POSDB.POSMoneyStr(vProds[vI].Cost),
                    sgcHTMLFloatToStr(vProds[vI].TaxRate, 2),
                    IntStr(vProds[vI].Stock));

            byte[] vBytes;
            using (MemoryStream oStream = new MemoryStream())
            {
                if (aPDF)
                    oGrid.SaveToPDFStream(oStream);
                else
                    oGrid.SaveToXLSXStream(oStream);
                vBytes = oStream.ToArray();
            }
            if (aPDF)
                return FileInline(vBytes, CS_CONTENT_TYPE_PDF, "products.pdf");
            return FileInline(vBytes, CS_CONTENT_TYPE_XLSX, "products.xlsx");
        }

        // sgcPOS_Server.pas:2090-2096.
        public IResult Categories(HttpContext aCtx, TPOSSession aSession)
        {
            return Html(200, FPages.BuildCategoriesPage(FDB.ListCategories(),
                BuildCtx(aCtx, aSession, "categories")));
        }

        // sgcPOS_Server.pas:2098-2107.
        public IResult CategorySave(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            long vId = FDB.SaveCategory(ParamInt(aCtx, aForm, "id", 0),
                GetParam(aCtx, aForm, "name"), GetParam(aCtx, aForm, "color"),
                (int)ParamInt(aCtx, aForm, "sort_order", 0));
            Audit(aCtx, aSession, "category_save", "category", vId,
                GetParam(aCtx, aForm, "name"));
            return Redirect("/categories?flash=saved");
        }

        // sgcPOS_Server.pas:2109-2114.
        public IResult Promotions(HttpContext aCtx, TPOSSession aSession)
        {
            return Html(200, FPages.BuildPromotionsPage(FDB.ListPromotions(),
                FDB.ListCategories(), BuildCtx(aCtx, aSession, "promotions")));
        }

        // sgcPOS_Server.pas:2116-2139.
        public IResult PromotionSave(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            TPOSPromotion oPromo = new TPOSPromotion();
            oPromo.Id = ParamInt(aCtx, aForm, "id", 0);
            oPromo.Name = GetParam(aCtx, aForm, "name");
            oPromo.Kind = GetParam(aCtx, aForm, "kind");
            decimal vValue;
            if (POSDB.POSParseMoney(GetParam(aCtx, aForm, "value"), out vValue))
                oPromo.Value = (double)vValue;
            oPromo.CategoryId = ParamInt(aCtx, aForm, "category_id", 0);
            oPromo.StartsAt = POSDB.ParsePOSTimestamp(
                GetParam(aCtx, aForm, "starts_at") + "T00:00:00");
            oPromo.EndsAt = POSDB.ParsePOSTimestamp(
                GetParam(aCtx, aForm, "ends_at") + "T23:59:59");
            oPromo.Active = true;
            long vId = FDB.SavePromotion(oPromo);
            Audit(aCtx, aSession, "promotion_save", "promotion", vId, oPromo.Name);
            return Redirect("/promotions?flash=saved");
        }

        // ----- back office: customers ----- //

        // sgcPOS_Server.pas:2141-2167.
        public IResult Customers(HttpContext aCtx, TPOSSession aSession)
        {
            string vSearch = GetParam(aCtx, null, "q");
            TPOSQuery oQuery;
            if (vSearch == "")
                oQuery = FDB.OpenQuery("SELECT id AS Id, name AS Customer, " +
                    "email AS Email, phone AS Phone, loyalty_points AS Points " +
                    "FROM customers ORDER BY name");
            else
            {
                oQuery = FDB.OpenQuery("SELECT id AS Id, name AS Customer, " +
                    "email AS Email, phone AS Phone, loyalty_points AS Points " +
                    "FROM customers WHERE name LIKE :q OR email LIKE :q " +
                    "ORDER BY name");
                oQuery.SetParam("q", "%" + vSearch + "%");
            }
            using (oQuery)
            {
                oQuery.Open();
                return Html(200, FPages.BuildCustomersPage(oQuery, vSearch,
                    BuildCtx(aCtx, aSession, "customers")));
            }
        }

        // sgcPOS_Server.pas:2169-2192.
        public IResult Customer(HttpContext aCtx, TPOSSession aSession, string aId)
        {
            long vId;
            if (!long.TryParse(aId ?? "", NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vId))
                return Results.StatusCode(404);
            TPOSCustomer oCust;
            if (!FDB.GetCustomer(vId, out oCust))
                return Redirect("/customers?err=notfound");
            // DateTime.MinValue is the managed spelling of the Delphi TDateTime 0
            // the DB layer treats as "no bound" (sgcPOS_DB.cs:64).
            TPOSSale[] vAll = FDB.ListSales("", 0, DateTime.MinValue, DateTime.MinValue, 400);
            List<TPOSSale> oSales = new List<TPOSSale>();
            for (int vI = 0; vI < vAll.Length; vI++)
                if (vAll[vI].CustomerId == vId && oSales.Count < 20)
                    oSales.Add(vAll[vI]);
            return Html(200, FPages.BuildCustomerPage(oCust, oSales.ToArray(),
                BuildCtx(aCtx, aSession, "customers")));
        }

        // sgcPOS_Server.pas:2194-2207.
        public IResult CustomerSave(HttpContext aCtx, IFormCollection aForm,
            TPOSSession aSession)
        {
            long vId = FDB.SaveCustomer(ParamInt(aCtx, aForm, "id", 0),
                GetParam(aCtx, aForm, "name"), GetParam(aCtx, aForm, "email"),
                GetParam(aCtx, aForm, "phone"),
                (int)ParamInt(aCtx, aForm, "loyalty_points", 0));
            Audit(aCtx, aSession, "customer_save", "customer", vId,
                GetParam(aCtx, aForm, "name"));
            if (vId > 0)
                return Redirect("/customers/" + IntStr(vId) + "?flash=saved");
            return Redirect("/customers?err=notfound");
        }

        // ----- back office: analytics ----- //

        // sgcPOS_Server.pas:2211-2238.
        public IResult Dashboard(HttpContext aCtx, TPOSSession aSession)
        {
            string vRange = GetParam(aCtx, null, "range").ToLowerInvariant();
            DateTime vToday = DateTime.Today;
            DateTime vTo = vToday.AddDays(1);
            DateTime vFrom;
            if (vRange == "week")
                vFrom = vToday.AddDays(-6);
            else if (vRange == "month")
                vFrom = new DateTime(vToday.Year, vToday.Month, 1);
            else if (vRange == "year")
                vFrom = new DateTime(vToday.Year, 1, 1);
            else
            {
                vRange = "today";
                vFrom = vToday;
            }

            TPOSTotals oToday = FDB.TotalsBetween(vToday, vToday.AddDays(1));
            TPOSTotals oRange = FDB.TotalsBetween(vFrom, vTo);

            return Html(200, FPages.BuildDashboardPage(oToday, oRange,
                FDB.SalesByHour(vFrom, vTo), FDB.PaymentMix(vFrom, vTo),
                FDB.SalesHeat(vToday.AddDays(-90), vTo), FDB.TopProducts(vFrom, vTo, 10),
                vRange, BuildCtx(aCtx, aSession, "dashboard")));
        }

        // sgcPOS_Server.pas:2240-2280.
        public IResult Reports(HttpContext aCtx, TPOSSession aSession)
        {
            string vFrom = GetParam(aCtx, null, "from");
            string vTo = GetParam(aCtx, null, "to");
            DateTime vFromDate = POSDB.ParsePOSTimestamp(vFrom + "T00:00:00");
            DateTime vToDate = POSDB.ParsePOSTimestamp(vTo + "T23:59:59");
            if (vFromDate <= DateTime.MinValue)
            {
                vFromDate = DateTime.Today.AddMonths(-5);
                vFrom = StampDate(vFromDate);
            }
            if (vToDate <= DateTime.MinValue)
            {
                vToDate = DateTime.Today.AddDays(1);
                vTo = StampDate(DateTime.Today);
            }

            using (TPOSQuery oPivot = FDB.OpenQuery("SELECT cat.name AS category, " +
                "substr(s.created_at, 1, 7) AS month, " +
                "SUM(l.line_total) AS amount FROM sale_lines l " +
                "INNER JOIN sales s ON s.id = l.sale_id " +
                "INNER JOIN products p ON p.id = l.product_id " +
                "INNER JOIN categories cat ON cat.id = p.category_id " +
                "WHERE s.status = 'completed' AND s.created_at >= :f " +
                "AND s.created_at < :t GROUP BY cat.name, month " +
                "ORDER BY cat.name, month"))
            {
                oPivot.SetParam("f", POSDB.POSTimestamp(vFromDate));
                oPivot.SetParam("t", POSDB.POSTimestamp(vToDate));
                oPivot.Open();
                return Html(200, FPages.BuildReportsPage(oPivot, FDB.SalesByMonth(12),
                    vFrom, vTo, FDB.TotalsBetween(vFromDate, vToDate),
                    BuildCtx(aCtx, aSession, "reports")));
            }
        }

        // sgcPOS_Server.pas:2282-2384.
        public IResult SalesReport(HttpContext aCtx, bool aPDF)
        {
            DateTime vFromDate = POSDB.ParsePOSTimestamp(
                GetParam(aCtx, null, "from") + "T00:00:00");
            DateTime vToDate = POSDB.ParsePOSTimestamp(
                GetParam(aCtx, null, "to") + "T23:59:59");
            if (vFromDate <= DateTime.MinValue)
                vFromDate = DateTime.Today.AddMonths(-5);
            if (vToDate <= DateTime.MinValue)
                vToDate = DateTime.Today.AddDays(1);

            TPOSSale[] vSales = FDB.ListSales(POSConst.CS_SALE_COMPLETED, 0, vFromDate,
                vToDate, 500);
            TPOSTotals oTotals = FDB.TotalsBetween(vFromDate, vToDate);

            byte[] vBytes;
            if (aPDF)
            {
                TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
                oPDF.Title = "Sales report";
                oPDF.Author = FConfig.StoreName;
                oPDF.HeaderText = FConfig.StoreName + " - sales " + StampDate(vFromDate) +
                    " to " + StampDate(vToDate);
                oPDF.FooterText = "Page {page} of {pages} - sgcPOS";
                oPDF.ShowPageNumbers = true;
                oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
                oPDF.Orientation = TsgcHTMLPDFOrientation.poLandscape;
                oPDF.BeginTable(38, 34, 40, 46, 26, 26, 26, 30);
                oPDF.TableHeader("Receipt", "Date", "Cashier", "Customer", "Subtotal",
                    "Discount", "Tax", "Total");
                for (int vI = 0; vI < vSales.Length; vI++)
                    oPDF.TableRow(vSales[vI].Reference, StampMinutes(vSales[vI].CreatedAt),
                        vSales[vI].UserName, vSales[vI].CustomerName,
                        POSDB.POSMoneyStr(vSales[vI].Subtotal),
                        POSDB.POSMoneyStr(vSales[vI].Discount),
                        POSDB.POSMoneyStr(vSales[vI].Tax),
                        POSDB.POSMoneyStr(vSales[vI].Total));
                oPDF.TableRow("TOTAL", "", "", IntStr(oTotals.SaleCount) + " sales",
                    POSDB.POSMoneyStr(oTotals.NetSubtotal),
                    POSDB.POSMoneyStr(oTotals.Discount), POSDB.POSMoneyStr(oTotals.Tax),
                    POSDB.POSMoneyStr(oTotals.Gross));
                oPDF.EndTable();
                using (MemoryStream oStream = new MemoryStream())
                {
                    oPDF.SaveToStream(oStream);
                    vBytes = oStream.ToArray();
                }
                return FileInline(vBytes, CS_CONTENT_TYPE_PDF, "sales.pdf");
            }

            TsgcHTMLExportXLSX oXLSX = new TsgcHTMLExportXLSX();
            oXLSX.AddSheet("Sales");
            oXLSX.AddRow();
            oXLSX.AddCell("Receipt");
            oXLSX.AddCell("Date");
            oXLSX.AddCell("Cashier");
            oXLSX.AddCell("Customer");
            oXLSX.AddCell("Subtotal");
            oXLSX.AddCell("Discount");
            oXLSX.AddCell("Tax");
            oXLSX.AddCell("Total");
            for (int vI = 0; vI < vSales.Length; vI++)
            {
                oXLSX.AddRow();
                oXLSX.AddCell(vSales[vI].Reference);
                oXLSX.AddCell(StampMinutes(vSales[vI].CreatedAt));
                oXLSX.AddCell(vSales[vI].UserName);
                oXLSX.AddCell(vSales[vI].CustomerName);
                oXLSX.AddCellNumber((double)vSales[vI].Subtotal);
                oXLSX.AddCellNumber((double)vSales[vI].Discount);
                oXLSX.AddCellNumber((double)vSales[vI].Tax);
                oXLSX.AddCellNumber((double)vSales[vI].Total);
            }
            using (MemoryStream oStream = new MemoryStream())
            {
                oXLSX.SaveToStream(oStream);
                vBytes = oStream.ToArray();
            }
            return FileInline(vBytes, CS_CONTENT_TYPE_XLSX, "sales.xlsx");
        }

        // The SQL next to the component: the page shows the very statement it
        // binds (sgcPOS_Server.pas:2386-2410).
        public IResult SQL(HttpContext aCtx, TPOSSession aSession)
        {
            string vEOL = "\r\n";
            using (TPOSQuery oQuery = FDB.OpenQuery(
                "SELECT s.reference   AS Receipt," + vEOL +
                "       s.created_at  AS Taken," + vEOL +
                "       u.display_name AS Cashier," + vEOL +
                "       COALESCE(c.name, 'Walk-in') AS Customer," + vEOL +
                "       s.subtotal    AS Net," + vEOL +
                "       s.discount    AS Discount," + vEOL +
                "       s.tax         AS Tax," + vEOL +
                "       s.total       AS Total" + vEOL +
                "FROM sales s" + vEOL +
                "LEFT JOIN users     u ON u.id = s.user_id" + vEOL +
                "LEFT JOIN customers c ON c.id = s.customer_id" + vEOL +
                "WHERE s.status = 'completed'" + vEOL +
                "ORDER BY s.created_at DESC" + vEOL +
                "LIMIT 50"))
            {
                oQuery.Open();
                return Html(200, FPages.BuildSQLPage(oQuery,
                    BuildCtx(aCtx, aSession, "sql")));
            }
        }

        // ----- admin ----- //

        // sgcPOS_Server.pas:2412-2425.
        public IResult Users(HttpContext aCtx, TPOSSession aSession)
        {
            using (TPOSQuery oQuery = FDB.OpenQuery(
                "SELECT id, username, display_name, role FROM users ORDER BY role, username"))
            {
                oQuery.Open();
                return Html(200, FPages.BuildUsersPage(oQuery,
                    BuildCtx(aCtx, aSession, "users")));
            }
        }

        // sgcPOS_Server.pas:2427-2444.
        public IResult UserSave(HttpContext aCtx, IFormCollection aForm, TPOSSession aSession)
        {
            long vId = FDB.SaveUser(ParamInt(aCtx, aForm, "id", 0),
                GetParam(aCtx, aForm, "username"), GetParam(aCtx, aForm, "display_name"),
                GetParam(aCtx, aForm, "role"), GetParam(aCtx, aForm, "password"),
                GetParam(aCtx, aForm, "pin"));
            if (vId <= 0)
                return Redirect("/users?err=denied");
            Audit(aCtx, aSession, "user_save", "user", vId,
                GetParam(aCtx, aForm, "username") + " as " + GetParam(aCtx, aForm, "role"));
            return Redirect("/users?flash=saved");
        }

        // sgcPOS_Server.pas:2446-2452.
        public IResult AuditGet(HttpContext aCtx, TPOSSession aSession)
        {
            return Html(200, FPages.BuildAuditPage(FDB.ListAudit(300),
                BuildCtx(aCtx, aSession, "audit")));
        }

        // ----- 404 ----- //

        // sgcPOS_Server.pas:2877-2878: the not-found page is rendered for a
        // signed-in caller; a signed-out one is redirected to /login by the gate.
        public IResult NotFoundPage(HttpContext aCtx, TPOSSession aSession)
        {
            return Html(404, FPages.BuildNotFoundPage(BuildCtx(aCtx, aSession, "")));
        }

        // ----- PDF page narrowing ----- //

        // Narrows the page of an already-written PDF to aWidthMM x aHeightMM.
        //
        // TsgcHTMLExportPDF exposes PageSize as an enum of five paper sizes and
        // has no custom-size property, so an 80mm till roll cannot be asked for
        // directly. The document is therefore laid out inside the top-left 80mm
        // of an A5 page and the /MediaBox of every page is then rewritten to
        // that region.
        //
        // Two things make this safe rather than a hack. The MediaBox of a PDF
        // page is an arbitrary rectangle, not required to start at the origin,
        // so keeping its TOP edge where the writer put it leaves every
        // coordinate on the page exactly where it was drawn. And the replacement
        // is padded with spaces to the byte length of the text it replaces, so
        // the cross-reference offsets and the startxref of the file stay valid:
        // nothing moves.
        //
        // Returns the ORIGINAL document untouched when the shortened rectangle
        // will not fit in the space available, rather than shipping a silently
        // wrong page. Mirrors POSNarrowPDFPage (sgcPOS_Server.pas:318-424).
        private static byte[] POSNarrowPDFPage(byte[] aBytes, double aWidthMM,
            double aHeightMM)
        {
            const double CS_MM_TO_PT = 72.0 / 25.4;
            const string CS_TAG = "/MediaBox [";

            if (aBytes == null || aBytes.Length == 0)
                return aBytes;

            byte[] vBytes = (byte[])aBytes.Clone();
            int vCount = 0;
            int vI = 0;
            while (vI < vBytes.Length)
            {
                if (!TagAt(vBytes, vI, CS_TAG))
                {
                    vI++;
                    continue;
                }
                int vStart = vI + CS_TAG.Length;
                int vClose = vStart;
                while (vClose < vBytes.Length && vBytes[vClose] != (byte)']')
                    vClose++;
                if (vClose >= vBytes.Length)
                    return aBytes;
                int vLen = vClose - vStart;
                if (vLen <= 0 || vLen > 120)
                    return aBytes;

                StringBuilder oInner = new StringBuilder(vLen);
                for (int vJ = vStart; vJ < vClose; vJ++)
                    oInner.Append((char)vBytes[vJ]);
                string vInner = oInner.ToString();

                // Keep the TOP edge verbatim: it is what every drawn coordinate
                // on the page was measured down from.
                int vSpace = vInner.LastIndexOf(' ');
                if (vSpace < 0)
                    return aBytes;
                string vTop = vInner.Substring(vSpace + 1);
                double vTopValue;
                if (!sgcHTMLHelpers.sgcHTMLTryParseNumber(vTop, out vTopValue))
                    return aBytes;
                double vRight = aWidthMM * CS_MM_TO_PT;
                double vBottom = vTopValue - (aHeightMM * CS_MM_TO_PT);
                if (vBottom < 0)
                    vBottom = 0;
                // Whole points, which is how most real PDFs write a MediaBox and
                // short enough to fit inside the text being replaced.
                string vNew = "0 " + IntStr((long)Math.Round(vBottom)) + " " +
                    IntStr((long)Math.Round(vRight)) + " " + vTop;
                if (vNew.Length > vLen)
                    return aBytes;
                vNew = vNew.PadRight(vLen, ' ');
                for (int vJ = 0; vJ < vLen; vJ++)
                    vBytes[vStart + vJ] = (byte)vNew[vJ];
                vCount++;
                vI = vClose + 1;
            }

            if (vCount == 0)
                return aBytes;
            return vBytes;
        }

        // ASCII-only compare of aTag at aPos.
        private static bool TagAt(byte[] aBytes, int aPos, string aTag)
        {
            if (aPos + aTag.Length > aBytes.Length)
                return false;
            for (int vI = 0; vI < aTag.Length; vI++)
                if (aBytes[aPos + vI] != (byte)aTag[vI])
                    return false;
            return true;
        }
    }
}
