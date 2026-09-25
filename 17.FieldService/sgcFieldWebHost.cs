// ***************************************************************************
//  sgcFieldWeb - field service management demo on ASP.NET Core
//  Mirror of demos\60.HTML\17.FieldService (TsgcWebSocketHTTPServer host).
//
//  This is the REWRITTEN host layer, following the 01.ERP / 13.Warehouse /
//  15.Reports / 16.SaaS host pattern. It reproduces every branch of the
//  60.HTML sgcField_Server.cs DispatchRequest (itself a 1:1 port of
//  delphi\Demos\60.HTML\01.RunTime\17.FieldService\sgcField_Server.pas), but on
//  Kestrel:
//    - The reusable logic (Bcrypt, Config, DB, Multipart, Pages, Passkeys,
//      Sessions, Types) is copied VERBATIM from the 60.HTML demo and is NOT
//      changed here. Every byte of HTML still comes out of TFieldPages.
//    - The 60.HTML host scanned RawHeaders for the Cookie line and wrote
//      Set-Cookie / redirects through CustomHeaders. Here every cookie is
//      read/written through the native ASP.NET Core cookie API
//      (ctx.Request.Cookies / ctx.Response.Cookies) with the SAME cookie names +
//      attributes, and each handler returns an IResult.
//    - Query + form params are merged through GetParam, matching the Delphi
//      ARequestInfo.Params (query first, then the urlencoded body) - and, like
//      the Delphi TStringList.Values, the name match is case-insensitive.
//      GetParam TRIMS, exactly as the Delphi Param does; GetRawParam is the
//      untrimmed read the password / signature fields use.
//    - The multipart PARSER is not needed: the framework's IFormFile gives the
//      uploaded photos directly, which are handed to the SAME reused
//      TFieldPhotoStore through TFieldMultipartField values, so the storage
//      semantics (data\photos\<job id>\, the extension block-list, the size /
//      count caps in ValidateFiles) stay identical to the 60.HTML demo.
//    - PASSKEYS: the 60.HTML host hard-coded RP id 'localhost' + origin
//      http://localhost:5710. Under Kestrel the RP id (request host name) and
//      origin (scheme://host) are DERIVED from the live request and threaded
//      into the reused TFieldPasskeys. A per-origin instance is cached so the
//      WebAuthn begin/finish challenge state survives across the two requests of
//      one ceremony (see GetPasskeys).
//    - REALTIME: the 60.HTML host attached a TsgcHTMX_Engine_Server to its own
//      TsgcWebSocketHTTPServer and broadcast through it. Under Kestrel the
//      engine has NO Server bound, so its BroadcastFragment is a no-op and the
//      adapter's ISgcHtmlHub is the push channel (the 15.Reports pattern). The
//      world simulator (Delphi TFieldPushThread) becomes the FieldPushService
//      BackgroundService in Program.cs, which calls SimulateTick + PushFragment
//      here on the same CS_PUSH_INTERVAL_MS beat.
//    - The shared-host contract (AttachAndStart / FBasePath / PrefixAppURLs) is
//      not ported: this app owns the whole Kestrel pipeline, so the prefix is
//      always empty and the cookie path is '/'.
//
//  The two host files that were NOT copied from 60.HTML are sgcField_Server.cs
//  (the TsgcWebSocketHTTPServer host) and the console Program.cs.
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
using esegece.sgcWebSockets.AspNetCore;

namespace FieldService
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, photo store, per-origin passkey factory) and exposes one
    // IResult-returning method per 60.HTML DispatchRequest branch. Program.cs maps
    // the Minimal API endpoints onto these methods and applies the auth / role
    // gates through Guarded*.
    public sealed class FieldWebHost : IDisposable
    {
        public const string CS_FIELD_SERVER_VERSION = "1.0.0";

        // Cookie names - identical to the 60.HTML host so behavior matches exactly.
        public const string CS_FIELD_SESSION = "field_session";
        public const string CS_FIELD_THEME_COOKIE = "field_theme";

        // How often the simulator advances the world and pushes fragments.
        public const int CS_PUSH_INTERVAL_MS = 3000;

        // Self-contained favicon (served at /favicon.svg), in the demo's accent.
        // The one trusted constant this host emits verbatim, exactly as the Delphi
        // does at sgcField_Server.pas:48.
        public const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"Field Service\">" +
            "<rect width=\"64\" height=\"64\" rx=\"13\" fill=\"#EA580C\"/>" +
            "<path d=\"M20 46 L32 20 L44 46 Z\" fill=\"none\" stroke=\"#FFFFFF\" " +
            "stroke-width=\"5\" stroke-linejoin=\"round\"/>" +
            "<circle cx=\"32\" cy=\"39\" r=\"4.5\" fill=\"#FFFFFF\"/></svg>";

        // A new job reaches the technician with the standard steps already on it.
        private static readonly string[] CS_DEFAULT_CHECKLIST = new string[]
        {
            "Isolate power and lock off",
            "Inspect and record the fault",
            "Carry out the repair or service",
            "Test and record readings",
            "Walk the customer through the work"
        };

        private readonly TFieldServerConfig FConfig;
        private readonly TFieldDBPool FDB;
        private readonly TFieldSessionStore FSessions;
        private readonly TFieldPages FPages;
        private readonly TFieldPhotoStore FPhotos;
        private readonly ISgcHtmlHub FHub;
        private readonly DateTime FStartedAt;

        // The Delphi guarded the simulator with a TCriticalSection; a plain monitor
        // object here. Only SimulateTick takes it.
        private readonly object FSimLock = new object();
        private int FTick;

        // Per-origin passkey cache. WebAuthn begin/finish share challenge state in
        // one TFieldPasskeys instance, so a ceremony's two requests (same browser,
        // same origin) must hit the SAME instance. Keyed by "rpId|origin".
        private readonly Dictionary<string, TFieldPasskeys> FPasskeys =
            new Dictionary<string, TFieldPasskeys>(StringComparer.Ordinal);
        private readonly object FPasskeysLock = new object();

        // Builds the DB (schema + dispatcher seed + demo data), the session store,
        // the page builder and the live components - mirroring
        // TFieldServer.InitRuntime.
        public FieldWebHost(TFieldServerConfig aConfig, string aDatabasePath,
            ISgcHtmlHub aHub)
        {
            FConfig = aConfig;
            FHub = aHub;
            FStartedAt = DateTime.Now;
            FTick = 0;

            FDB = new TFieldDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedAdmin(aConfig.AdminUser, Bcrypt.BcryptHash(aConfig.AdminPassword));
            FDB.SeedDemoData();

            FSessions = new TFieldSessionStore(480, true);
            FPages = new TFieldPages();
            FPhotos = new TFieldPhotoStore();

            // The live components are created before anything renders, so every
            // technician already has an element in the DOM a push can target.
            TFieldPages.LiveInit();
            TFieldTechnician[] vTechs = FDB.ListTechnicians(false);
            TFieldPages.LiveSyncTechnicians(vTechs);
            TFieldPages.LiveSyncJobs(CollectLiveJobs());
            TFieldPages.LiveAddLog("info", "Dispatch started with " +
                Str(vTechs.Length) + " technician(s) on the roster", "server");
        }

        public void Dispose()
        {
            lock (FPasskeysLock)
            {
                foreach (KeyValuePair<string, TFieldPasskeys> vPair in FPasskeys)
                {
                    try { vPair.Value.Dispose(); } catch (Exception) { }
                }
                FPasskeys.Clear();
            }
            if (FDB != null)
            {
                try { FDB.Dispose(); } catch (Exception) { }
            }
            TFieldPages.LiveDone();
        }

        public DateTime StartedAt
        {
            get { return FStartedAt; }
        }

        // ----- passkey factory (request-derived rpId + origin) ----- //

        // Returns the TFieldPasskeys for THIS request's origin, creating + caching
        // it on first use. RP id = the request host name (no port); origin =
        // scheme://host (with port). This threads the live request values into the
        // reused passkey engine instead of the 60.HTML hard-coded localhost.
        private TFieldPasskeys GetPasskeys(HttpContext aCtx)
        {
            string vRPID = aCtx.Request.Host.Host;
            if (string.IsNullOrEmpty(vRPID))
                vRPID = "localhost";
            string vOrigin = aCtx.Request.Scheme + "://" + aCtx.Request.Host.Value;
            string vKey = vRPID + "|" + vOrigin;

            lock (FPasskeysLock)
            {
                TFieldPasskeys oPk;
                if (!FPasskeys.TryGetValue(vKey, out oPk))
                {
                    oPk = new TFieldPasskeys(FDB, vRPID, "sgcField Service", vOrigin);
                    FPasskeys[vKey] = oPk;
                }
                return oPk;
            }
        }

        // ----- numeric / date helpers (verbatim from the 60.HTML host) ----- //

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
            if (double.TryParse(vText, NumberStyles.Float,
                CultureInfo.InvariantCulture, out vResult))
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

        // Delphi FormatDateTime masks, invariant. 'nn' is minutes in Pascal.
        private static string IsoStamp(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture);
        }

        private static string IsoDateTimeText(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static string IsoDateText(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // 'yyyy-mm-dd' -> DateTime, DateTime.MinValue when unparsable. Fixed
        // position, never a locale parse: the system locale must not be able to
        // break a URL parameter.
        private static DateTime ParseISODate(string aValue)
        {
            string vText = (aValue ?? "").Trim();
            if (vText.Length < 10)
                return DateTime.MinValue;
            int vY = StrToIntDef(vText.Substring(0, 4), -1);
            int vM = StrToIntDef(vText.Substring(5, 2), -1);
            int vD = StrToIntDef(vText.Substring(8, 2), -1);
            if ((vY < 1) || (vM < 1) || (vM > 12) || (vD < 1) || (vD > 31))
                return DateTime.MinValue;
            try
            {
                return new DateTime(vY, vM, vD);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        // 'HH:MM' -> fraction of a day, -1 when unparsable.
        private static double ParseISOTime(string aValue)
        {
            string vText = (aValue ?? "").Trim();
            if (vText.Length < 4)
                return -1;
            int vH = StrToIntDef(vText.Substring(0, 2), -1);
            int vN = StrToIntDef(vText.Substring(3, 2), -1);
            if ((vH < 0) || (vH > 23) || (vN < 0) || (vN > 59))
                return -1;
            return (vH * 60 + vN) / 1440.0;
        }

        // Quote / escape one exported cell. Only the newlines matter here.
        private static string TextCell(string aValue)
        {
            string vResult = (aValue ?? "").Replace("\r\n", " ");
            return vResult.Replace("\n", " ");
        }

        // Strip anything that could break out of a Content-Disposition header value.
        private static string SanitizeHeaderFilename(string aName)
        {
            string vName = aName ?? "";
            StringBuilder vBuf = new StringBuilder(vName.Length);
            for (int vI = 0; vI < vName.Length; vI++)
            {
                char vCh = vName[vI];
                if ((vCh < ' ') || (vCh == '"') || (vCh == '\\') || (vCh == '/'))
                    continue;
                vBuf.Append(vCh);
            }
            if (vBuf.Length == 0)
                return "download";
            return vBuf.ToString();
        }

        // Decode a 'data:image/png;base64,...' URL into raw PNG bytes. Returns an
        // empty array for anything that is not a base64 data URL.
        private static byte[] DataURLToPDFBytes(string aDataURL)
        {
            string vURL = aDataURL ?? "";
            int vPos = vURL.IndexOf(',');
            if (vPos < 0)
                return new byte[0];
            if (vURL.Substring(0, vPos + 1).ToLowerInvariant().IndexOf("base64",
                StringComparison.Ordinal) < 0)
                return new byte[0];
            string vB64 = vURL.Substring(vPos + 1);
            if (vB64.Trim() == "")
                return new byte[0];
            try
            {
                return Convert.FromBase64String(vB64);
            }
            catch (Exception)
            {
                return new byte[0];
            }
        }

        private static string JsonEscape(string aValue)
        {
            string vValue = aValue ?? "";
            StringBuilder vBuf = new StringBuilder(vValue.Length);
            for (int vI = 0; vI < vValue.Length; vI++)
            {
                char vCh = vValue[vI];
                switch (vCh)
                {
                    case '"': vBuf.Append("\\\""); break;
                    case '\\': vBuf.Append("\\\\"); break;
                    case '\b': vBuf.Append("\\b"); break;
                    case '\t': vBuf.Append("\\t"); break;
                    case '\n': vBuf.Append("\\n"); break;
                    case '\f': vBuf.Append("\\f"); break;
                    case '\r': vBuf.Append("\\r"); break;
                    default:
                        if (vCh < ' ')
                            vBuf.Append("\\u").Append(((int)vCh).ToString("X4",
                                CultureInfo.InvariantCulture));
                        else
                            vBuf.Append(vCh);
                        break;
                }
            }
            return vBuf.ToString();
        }

        // ----- request param helpers (query + form merged) ----- //

        // First value for aName: query wins over form, matching the Delphi merged
        // ARequestInfo.Params. aForm is null for GET / non-form requests. The name
        // match is case-insensitive, like Delphi's TStringList.Values. TRIMMED,
        // exactly as the Delphi Param does.
        private static string GetParam(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            return GetRawParam(aCtx, aForm, aName).Trim();
        }

        // The untrimmed read, for the fields the Delphi pulled straight off
        // aReq.Params.Values (password, signature).
        private static string GetRawParam(HttpContext aCtx, IFormCollection aForm,
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
            string aName, long aDefault)
        {
            string vText = GetParam(aCtx, aForm, aName);
            if (vText == "")
                return aDefault;
            long vResult;
            if (!long.TryParse(vText, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return aDefault;
            return vResult;
        }

        private static DateTime GetParamDate(HttpContext aCtx, IFormCollection aForm,
            string aName, DateTime aDefault)
        {
            DateTime vValue = ParseISODate(GetParam(aCtx, aForm, aName));
            if (!TFieldDBPool.IsZeroDate(vValue))
                return vValue;
            return aDefault;
        }

        // Every repeated occurrence of aName, as integers. A Values lookup only ever
        // hands back the first one, and the lane filter is a MultiSelect that posts
        // the same field several times.
        private static long[] GetParamIntList(HttpContext aCtx, IFormCollection aForm,
            string aName)
        {
            List<long> vResult = new List<long>();
            foreach (KeyValuePair<string, StringValues> vPair in aCtx.Request.Query)
            {
                if (!string.Equals(vPair.Key, aName, StringComparison.OrdinalIgnoreCase))
                    continue;
                for (int vI = 0; vI < vPair.Value.Count; vI++)
                {
                    long vValue;
                    if (long.TryParse((vPair.Value[vI] ?? "").Trim(),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out vValue))
                        vResult.Add(vValue);
                }
            }
            if (aForm != null)
                foreach (KeyValuePair<string, StringValues> vPair in aForm)
                {
                    if (!string.Equals(vPair.Key, aName,
                        StringComparison.OrdinalIgnoreCase))
                        continue;
                    for (int vI = 0; vI < vPair.Value.Count; vI++)
                    {
                        long vValue;
                        if (long.TryParse((vPair.Value[vI] ?? "").Trim(),
                            NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out vValue))
                            vResult.Add(vValue);
                    }
                }
            return vResult.ToArray();
        }

        // ----- cookies (native ASP.NET Core cookie API) ----- //

        // The app owns the whole pipeline, so the shared-host base path is always
        // empty and the cookie path is the root (Delphi CookiePath).
        private static string CookiePath()
        {
            return "/";
        }

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
            aCtx.Response.Cookies.Append(CS_FIELD_SESSION, aToken, new CookieOptions
            {
                Path = CookiePath(),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static void ClearSessionCookie(HttpContext aCtx)
        {
            aCtx.Response.Cookies.Delete(CS_FIELD_SESSION, new CookieOptions
            {
                Path = CookiePath(),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static string ReadThemeCookie(HttpContext aCtx)
        {
            string vS = ReadCookie(aCtx, CS_FIELD_THEME_COOKIE).ToLowerInvariant();
            if ((vS == "light") || (vS == "dark") || (vS == "system"))
                return vS;
            // The field demo follows the OS preference by default.
            return "system";
        }

        private static void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vS = (aTheme ?? "").Trim().ToLowerInvariant();
            if ((vS != "light") && (vS != "dark") && (vS != "system"))
                vS = "system";
            aCtx.Response.Cookies.Append(CS_FIELD_THEME_COOKIE, vS, new CookieOptions
            {
                Path = CookiePath(),
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
                System.Net.IPAddress oIP = aCtx.Connection.RemoteIpAddress;
                if (oIP == null)
                    return "";
                // Normalize an IPv4-mapped IPv6 loopback to plain 127.0.0.1.
                if (oIP.IsIPv4MappedToIPv6)
                    oIP = oIP.MapToIPv4();
                return oIP.ToString();
            }
            catch (Exception)
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
        public bool CurrentSession(HttpContext aCtx, out TFieldSession aSession)
        {
            aSession = null;
            string vToken = ReadCookie(aCtx, CS_FIELD_SESSION);
            if (vToken == "")
                return false;
            return FSessions.TryGet(vToken, out aSession);
        }

        // ----- response helpers (return an IResult) ----- //

        private static IResult Html(int aCode, string aHTML)
        {
            return Results.Content(aHTML, "text/html; charset=utf-8", null, aCode);
        }

        private static IResult Text(HttpContext aCtx, int aCode, string aText)
        {
            aCtx.Response.Headers["Cache-Control"] = "no-store";
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

        private static IResult JsonError(HttpContext aCtx, int aCode, string aMessage)
        {
            return Json(aCtx, aCode, "{\"ok\":false,\"error\":\"" +
                JsonEscape(aMessage) + "\"}");
        }

        private static IResult Status(int aCode)
        {
            return Results.StatusCode(aCode);
        }

        // The PDF builders write into a MemoryStream; hand its bytes back with the
        // same inline Content-Disposition the 60.HTML host wrote.
        private static IResult InlineFile(HttpContext aCtx, MemoryStream aStream,
            string aContentType, string aFileName)
        {
            aCtx.Response.Headers["Content-Disposition"] = "inline; filename=\"" +
                SanitizeHeaderFilename(aFileName) + "\"";
            return Results.Bytes(aStream.ToArray(), aContentType);
        }

        private static async Task<string> ReadRawBodyAsync(HttpContext aCtx)
        {
            using (StreamReader oReader = new StreamReader(aCtx.Request.Body,
                Encoding.UTF8, false, 1024, true))
            {
                return await oReader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        // ----- page context / audit / roles ----- //

        // Fills the page context from the session plus the request theme.
        private TFieldPageCtx CtxOf(HttpContext aCtx, TFieldSession aSession)
        {
            TFieldPageCtx vResult = new TFieldPageCtx();
            vResult.UserId = aSession.UserId;
            vResult.DisplayName = aSession.DisplayName;
            if (vResult.DisplayName == "")
                vResult.DisplayName = aSession.Username;
            vResult.Initials = aSession.Initials;
            if (vResult.Initials == "")
                vResult.Initials = vResult.DisplayName.Length <= 2
                    ? vResult.DisplayName.ToUpperInvariant()
                    : vResult.DisplayName.Substring(0, 2).ToUpperInvariant();
            vResult.Role = aSession.Role;
            vResult.Theme = ReadThemeCookie(aCtx);
            vResult.Unread = 0;
            try
            {
                if (FDB != null)
                    vResult.Unread = FDB.CountUnreadFor(aSession.UserId);
            }
            catch (Exception)
            {
                vResult.Unread = 0;
            }
            return vResult;
        }

        private void Audit(HttpContext aCtx, TFieldSession aSession, string aAction,
            string aEntity, long aEntityId, string aDetail)
        {
            try
            {
                FDB.AddAudit(aSession.UserId, aAction, aEntity, aEntityId, aDetail,
                    ClientIPOf(aCtx));
            }
            catch (Exception)
            {
            }
        }

        private static bool IsDispatcher(TFieldSession aSession)
        {
            return string.Equals(aSession.Role, FieldConst.CS_ROLE_DISPATCHER,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsManager(TFieldSession aSession)
        {
            return string.Equals(aSession.Role, FieldConst.CS_ROLE_MANAGER,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTechnician(TFieldSession aSession)
        {
            return string.Equals(aSession.Role, FieldConst.CS_ROLE_TECHNICIAN,
                StringComparison.OrdinalIgnoreCase);
        }

        // The technician row that owns aSession, or false for anyone else.
        private bool SessionTechnician(TFieldSession aSession,
            out TFieldTechnician aTech)
        {
            return FDB.GetTechnicianByUserId(aSession.UserId, out aTech);
        }

        // True when aSession may act on aJob: the assigned technician, or a
        // dispatcher / manager. Every /my route is checked with this.
        private bool CanActOnJob(TFieldSession aSession, TFieldJob aJob)
        {
            if (IsDispatcher(aSession) || IsManager(aSession))
                return true;
            if (!IsTechnician(aSession))
                return false;
            // Never trust the id in the URL: the job must belong to THIS technician.
            TFieldTechnician oTech;
            return SessionTechnician(aSession, out oTech) &&
                (aJob.TechnicianId == oTech.Id);
        }

        // ----- auth gates (used by Program.cs endpoint mapping) ----- //

        // Signed-in gate, any role. Mirrors 60.HTML DispatchRequest step 3, which
        // redirects to /login for every unauthenticated non-public path.
        public IResult Guarded(HttpContext aCtx, Func<TFieldSession, IResult> aFn)
        {
            TFieldSession oSession;
            if (!CurrentSession(aCtx, out oSession))
                return Redirect("/login");
            return aFn(oSession);
        }

        // Office gate: signed in AND dispatcher or manager. Mirrors the Delphi
        // RequireDispatch local function, which answers a page that says so rather
        // than a blank 403.
        public IResult GuardedDispatch(HttpContext aCtx,
            Func<TFieldSession, IResult> aFn)
        {
            TFieldSession oSession;
            if (!CurrentSession(aCtx, out oSession))
                return Redirect("/login");
            if (!(IsDispatcher(oSession) || IsManager(oSession)))
                return Html(403, FPages.BuildDeniedPage(CtxOf(aCtx, oSession),
                    "The dispatch board and the office pages are for dispatchers " +
                    "and managers. Your work is on the \"My jobs\" screen."));
            return aFn(oSession);
        }

        // Signed-in JSON gate (the passkey registration pair, which the 60.HTML
        // dispatcher answers with a JSON 401 rather than the HTML redirect).
        public IResult GuardedJson(HttpContext aCtx, Func<TFieldSession, IResult> aFn)
        {
            TFieldSession oSession;
            if (!CurrentSession(aCtx, out oSession))
                return JsonError(aCtx, 401, "Not signed in.");
            return aFn(oSession);
        }

        // ----- static asset the adapter does not own ----- //

        public IResult Favicon(HttpContext aCtx)
        {
            aCtx.Response.Headers["Cache-Control"] = "public, max-age=86400";
            return Results.Content(CS_FAVICON_SVG, "image/svg+xml", null, 200);
        }

        // ----- health ----- //

        public IResult Healthz(HttpContext aCtx)
        {
            int vJobs;
            try
            {
                vJobs = FDB.CountJobs("", -1, "", DateTime.MinValue, DateTime.MinValue,
                    "");
            }
            catch (Exception)
            {
                vJobs = -1;
            }
            return Json(aCtx, 200, "{\"ok\":true,\"version\":\"" +
                CS_FIELD_SERVER_VERSION + "\",\"started\":\"" + IsoStamp(FStartedAt) +
                "\",\"sessions\":" + Str(FSessions.ActiveCount()) + ",\"jobs\":" +
                Str(vJobs) + "}");
        }

        // ----- public auth / theme ----- //

        public IResult SetTheme(HttpContext aCtx, IFormCollection aForm)
        {
            WriteThemeCookie(aCtx, GetParam(aCtx, aForm, "theme"));
            return Redirect(RefererOrRoot(aCtx));
        }

        public IResult LoginGet(HttpContext aCtx)
        {
            TFieldSession oSession;
            if (CurrentSession(aCtx, out oSession))
                return Redirect("/");
            // The demo arrives ready to use: the dispatcher account is pre-filled.
            return Html(200, FPages.BuildLoginPage(ReadThemeCookie(aCtx), "",
                FConfig.AdminUser, FConfig.AdminPassword));
        }

        public IResult LoginPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vUser = GetParam(aCtx, aForm, "username");
            string vPwd = GetRawParam(aCtx, aForm, "password");

            if ((vUser == "") || (vPwd == ""))
                return Html(400, FPages.BuildLoginPage(vTheme,
                    "Please enter your username and password."));
            TFieldUser oUser;
            if (!FDB.AuthenticateUser(vUser, vPwd, out oUser))
                return Html(401, FPages.BuildLoginPage(vTheme,
                    "Invalid username or password."));

            string vToken = FSessions.CreateSession(oUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            try
            {
                FDB.AddAudit(oUser.Id, "login", "user", oUser.Id, oUser.Role,
                    ClientIPOf(aCtx));
            }
            catch (Exception)
            {
            }
            if (string.Equals(oUser.Role, FieldConst.CS_ROLE_TECHNICIAN,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/my");
            if (string.Equals(oUser.Role, FieldConst.CS_ROLE_CUSTOMER,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/track");
            return Redirect("/");
        }

        public IResult Logout(HttpContext aCtx)
        {
            string vToken = ReadCookie(aCtx, CS_FIELD_SESSION);
            if (vToken != "")
                FSessions.Destroy_(vToken);
            ClearSessionCookie(aCtx);
            return Redirect("/login");
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
            TFieldUser oUser;
            if (!FDB.GetUserById(vUserID, out oUser))
                return JsonError(aCtx, 401, "User not found.");
            string vToken = FSessions.CreateSession(oUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            return Json(aCtx, 200, "{\"ok\":true,\"redirect\":\"" + CookiePath() +
                "\"}");
        }

        public IResult PasskeyRegisterOptions(HttpContext aCtx, TFieldSession aSession)
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

        public async Task<IResult> PasskeyRegisterVerify(HttpContext aCtx)
        {
            TFieldSession oSession;
            if (!CurrentSession(aCtx, out oSession))
                return JsonError(aCtx, 401, "Not signed in.");
            try
            {
                string vBody = await ReadRawBodyAsync(aCtx).ConfigureAwait(false);
                if (vBody.Trim() == "")
                    return JsonError(aCtx, 400, "Empty request body.");
                string vName = GetParam(aCtx, null, "name");
                if (vName == "")
                    vName = "Passkey";
                string vResult = GetPasskeys(aCtx).FinishRegister(oSession.UserId,
                    vBody, vName);
                return Json(aCtx, 200, "{\"ok\":true,\"detail\":" + vResult + "}");
            }
            catch (Exception E)
            {
                return JsonError(aCtx, 400, E.Message);
            }
        }

        // ----- dispatcher: board ----- //

        private static bool BoardWindow(DateTime aAnchor, string aView,
            out DateTime aFrom, out DateTime aTo)
        {
            // Both views render the same seven-day grid; the day view simply draws
            // only the anchor day's chips, so the window handed to the DB is always
            // the week. The grid is Sunday-based and must be the week that CONTAINS
            // the anchor, which is what the Delphi DayOfWeek gives (1 on a Sunday);
            // .NET DayOfWeek is 0 on a Sunday, so the offset is the enum itself.
            aFrom = aAnchor.Date.AddDays(-(int)aAnchor.DayOfWeek);
            aTo = aFrom.AddDays(7);
            return string.Equals(aView, "day", StringComparison.OrdinalIgnoreCase);
        }

        // Builds the lanes for aAnchor / aView: the unassigned queue first, then one
        // lane per active technician.
        private TFieldLane[] BuildLanes(DateTime aAnchor, string aView,
            long[] aShownTechs)
        {
            DateTime vFrom;
            DateTime vTo;
            BoardWindow(aAnchor, aView, out vFrom, out vTo);
            TFieldTechnician[] vTechs = FDB.ListTechnicians(true);
            List<TFieldLane> vResult = new List<TFieldLane>();

            // Lane 0 is the unassigned queue, and it is a real drop target.
            TFieldLane oQueue = new TFieldLane();
            oQueue.TechnicianId = 0;
            oQueue.DisplayName = "Unassigned";
            oQueue.Initials = "UN";
            oQueue.Presence = "";
            oQueue.Skills = "Drop a job on a technician to schedule it";
            oQueue.Jobs = FDB.ListJobsInRange(vFrom, vTo, 0);
            vResult.Add(oQueue);

            for (int vI = 0; vI < vTechs.Length; vI++)
            {
                // No selection at all means everyone; a selection means only those
                // lanes.
                bool vShow = (aShownTechs == null) || (aShownTechs.Length == 0);
                if (!vShow && aShownTechs != null)
                    for (int vJ = 0; vJ < aShownTechs.Length; vJ++)
                        if (aShownTechs[vJ] == vTechs[vI].Id)
                        {
                            vShow = true;
                            break;
                        }
                if (!vShow)
                    continue;
                TFieldLane oLane = new TFieldLane();
                oLane.TechnicianId = vTechs[vI].Id;
                oLane.DisplayName = vTechs[vI].DisplayName;
                oLane.Initials = vTechs[vI].AvatarInitials;
                oLane.Presence = vTechs[vI].Presence;
                oLane.Skills = vTechs[vI].Skills;
                oLane.Jobs = FDB.ListJobsInRange(vFrom, vTo, vTechs[vI].Id);
                vResult.Add(oLane);
            }
            return vResult.ToArray();
        }

        // The jobs being carried out right now, for the live "work in progress"
        // panel: the percentage is the job's own checklist.
        private TFieldLiveJob[] CollectLiveJobs()
        {
            TFieldJob[] vJobs = FDB.ListJobs(FieldConst.CS_JOB_ONSITE, -1, "",
                DateTime.MinValue, DateTime.MinValue, "", "sched", "asc", 8, 0);
            TFieldLiveJob[] vResult = new TFieldLiveJob[vJobs.Length];
            for (int vI = 0; vI < vJobs.Length; vI++)
            {
                TFieldLiveJob oLive = new TFieldLiveJob();
                oLive.Id = vJobs[vI].Id;
                oLive.Reference = vJobs[vI].Reference;
                oLive.Title = vJobs[vI].Title;
                oLive.Technician = vJobs[vI].TechnicianName;
                oLive.Status = vJobs[vI].Status;
                int vDone;
                int vTotal;
                FDB.ChecklistProgress(vJobs[vI].Id, out vDone, out vTotal);
                if (vTotal > 0)
                    oLive.Percent = (int)Math.Round(vDone * 100.0 / vTotal,
                        MidpointRounding.AwayFromZero);
                else
                    oLive.Percent = 0;
                vResult[vI] = oLive;
            }
            return vResult;
        }

        private TFieldBoardStats CollectBoardStats()
        {
            TFieldBoardStats vResult = new TFieldBoardStats();
            vResult.Unassigned = FDB.CountJobs("", 0, "", DateTime.MinValue,
                DateTime.MinValue, "");
            vResult.Scheduled = FDB.CountJobsByStatus(FieldConst.CS_JOB_SCHEDULED);
            vResult.EnRoute = FDB.CountJobsByStatus(FieldConst.CS_JOB_ENROUTE);
            vResult.OnSite = FDB.CountJobsByStatus(FieldConst.CS_JOB_ONSITE);
            vResult.CompletedToday = FDB.CountJobs(FieldConst.CS_JOB_COMPLETE, -1, "",
                DateTime.Today, DateTime.Today.AddDays(1), "");
            vResult.SlaAtRisk = 0;
            try
            {
                using (TFieldDataSet oData = FDB.OpenDataSet(
                    "SELECT COUNT(*) AS n FROM jobs " +
                    "WHERE status NOT IN ('complete', 'cancelled') " +
                    "AND sla_due_at <> '' AND sla_due_at < :due",
                    new string[] { ":due" },
                    new object[] { IsoStamp(DateTime.Now.AddHours(2)) }))
                {
                    if (oData.DataSet.Rows.Count > 0)
                        vResult.SlaAtRisk = Convert.ToInt32(oData.DataSet.Rows[0]["n"],
                            CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                vResult.SlaAtRisk = 0;
            }
            return vResult;
        }

        // GET / for a dispatcher or manager. A technician is sent to /my and a
        // customer to /track before this is reached (see Root).
        public IResult BoardGet(HttpContext aCtx, TFieldSession aSession)
        {
            string vView = GetParam(aCtx, null, "view").ToLowerInvariant();
            if (vView != "week")
                vView = "day";
            DateTime vAnchor = GetParamDate(aCtx, null, "anchor", DateTime.Today);
            long[] vShown = GetParamIntList(aCtx, null, "tech");
            // The live panel is rendered from the same roster the push updates, so
            // the element ids those fragments target already exist in this page.
            TFieldPages.LiveSyncJobs(CollectLiveJobs());
            return Html(200, FPages.BuildBoardPage(CtxOf(aCtx, aSession),
                BuildLanes(vAnchor, vView, vShown), CollectBoardStats(),
                FDB.ListTechnicians(true), vShown, vAnchor, vView,
                TFieldPages.LiveRenderPresence(), TFieldPages.LiveRenderFeed(),
                TFieldPages.LiveRenderLog(), TFieldPages.LiveRenderJobs()));
        }

        // GET /, the axis split of the 60.HTML dispatcher's root branch.
        public IResult Root(HttpContext aCtx, TFieldSession aSession)
        {
            if (IsTechnician(aSession))
                return Redirect("/my");
            if (string.Equals(aSession.Role, FieldConst.CS_ROLE_CUSTOMER,
                StringComparison.OrdinalIgnoreCase))
                return Redirect("/track");
            return BoardGet(aCtx, aSession);
        }

        // The htmx / fetch refresh of the lane strip. Every dispatcher asks for
        // their OWN view and anchor, which is why the push signals "stale" rather
        // than broadcasting one board to everybody.
        public IResult BoardFragment(HttpContext aCtx)
        {
            string vView = GetParam(aCtx, null, "view").ToLowerInvariant();
            if (vView != "week")
                vView = "day";
            DateTime vAnchor = GetParamDate(aCtx, null, "anchor", DateTime.Today);
            return Html(200, FPages.BuildBoardLanes(BuildLanes(vAnchor, vView,
                GetParamIntList(aCtx, null, "tech")), vAnchor, vView));
        }

        // POST /board/assign and POST /board/unassign. Answers with the re-rendered
        // lane strip on success and with a plain-text reason on refusal, so the drag
        // handler can show exactly what happened.
        public IResult BoardAssign(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, bool aUnassign)
        {
            TFieldJob oJob;
            long vJobId = GetParamInt(aCtx, aForm, "job", 0);
            if ((vJobId <= 0) || (!FDB.GetJob(vJobId, out oJob)))
                return Text(aCtx, 404, "That job no longer exists.");
            if (FieldConst.FieldStatusIsFinal(oJob.Status))
                return Text(aCtx, 409, "Job " + oJob.Reference + " is " +
                    FieldConst.FieldStatusLabel(oJob.Status).ToLowerInvariant() +
                    " and cannot be moved.");

            // Durations stay in Delphi units: a fraction of a day.
            double vDur = 1.0 / 24;
            if ((oJob.ScheduledEnd > oJob.ScheduledStart) &&
                (!TFieldDBPool.IsZeroDate(oJob.ScheduledStart)))
                vDur = (oJob.ScheduledEnd - oJob.ScheduledStart).TotalDays;

            if (aUnassign)
            {
                if (!FDB.AssignJob(vJobId, 0, oJob.ScheduledStart, oJob.ScheduledEnd))
                    return Text(aCtx, 500, "The queue would not take the job back.");
                FDB.AddJobEvent(vJobId, aSession.UserId, "unassigned",
                    "Sent back to the unassigned queue", 0, 0);
                Audit(aCtx, aSession, "job.unassign", "job", vJobId, oJob.Reference);
                PushAfterChange(aSession.DisplayName, "unassigned", oJob.Reference,
                    "secondary");
            }
            else
            {
                // technician: absent or -1 means "keep whoever has it".
                TFieldTechnician oTech = null;
                long vTechId = GetParamInt(aCtx, aForm, "technician", -1);
                if (vTechId < 0)
                    vTechId = oJob.TechnicianId;
                if ((vTechId > 0) && (!FDB.GetTechnician(vTechId, out oTech)))
                    return Text(aCtx, 400, "Unknown technician.");

                // date / time: either may be absent, and what is absent is kept.
                DateTime vStart = oJob.ScheduledStart;
                DateTime vDate = ParseISODate(GetParam(aCtx, aForm, "date"));
                double vTime = ParseISOTime(GetParam(aCtx, aForm, "time"));
                if (!TFieldDBPool.IsZeroDate(vDate))
                {
                    if (!TFieldDBPool.IsZeroDate(vStart))
                        vStart = vDate.Add(vStart.TimeOfDay);
                    else
                        vStart = vDate.AddDays(9.0 / 24);
                }
                if (vTime >= 0)
                {
                    if (!TFieldDBPool.IsZeroDate(vStart))
                        vStart = vStart.Date.AddDays(vTime);
                    else
                        vStart = DateTime.Today.AddDays(vTime);
                }
                if (TFieldDBPool.IsZeroDate(vStart))
                    vStart = DateTime.Today.AddDays(9.0 / 24);
                DateTime vEnd = vStart.AddDays(vDur);

                if (!FDB.AssignJob(vJobId, vTechId, vStart, vEnd))
                    return Text(aCtx, 500, "The assignment could not be saved.");
                if (vTechId > 0)
                    FDB.AddJobEvent(vJobId, aSession.UserId, "assigned",
                        "Assigned to " + oTech.DisplayName + " for " +
                        IsoDateTimeText(vStart), 0, 0);
                else
                    FDB.AddJobEvent(vJobId, aSession.UserId, "rescheduled",
                        "Moved to " + IsoDateTimeText(vStart), 0, 0);
                Audit(aCtx, aSession, "job.assign", "job", vJobId,
                    oJob.Reference + " -> tech " + Str(vTechId));
                if (vTechId > 0)
                    PushAfterChange(aSession.DisplayName,
                        "assigned " + oJob.Reference + " to", oTech.DisplayName,
                        "primary");
                else
                    PushAfterChange(aSession.DisplayName, "rescheduled",
                        oJob.Reference, "info");
            }

            string vView = GetParam(aCtx, aForm, "view").ToLowerInvariant();
            if (vView != "week")
                vView = "day";
            DateTime vAnchor = GetParamDate(aCtx, aForm, "anchor", DateTime.Today);
            return Html(200, FPages.BuildBoardLanes(BuildLanes(vAnchor, vView,
                GetParamIntList(aCtx, aForm, "tech")), vAnchor, vView));
        }

        // ----- dispatcher: gantt ----- //

        public IResult GanttGet(HttpContext aCtx, TFieldSession aSession)
        {
            DateTime vFrom = GetParamDate(aCtx, null, "from",
                DateTime.Today.AddDays(-3));
            DateTime vTo = vFrom.AddDays(35);
            TFieldJob[] vAll = FDB.ListJobsInRange(vFrom, vTo, -1);
            List<TFieldJob> vMulti = new List<TFieldJob>();
            for (int vI = 0; vI < vAll.Length; vI++)
                // The Gantt is for work that spans days; single visits live on the
                // board.
                if ((!TFieldDBPool.IsZeroDate(vAll[vI].ScheduledStart)) &&
                    (vAll[vI].ScheduledEnd >= vAll[vI].ScheduledStart.AddDays(1)))
                    vMulti.Add(vAll[vI]);
            return Html(200, FPages.BuildGanttPage(CtxOf(aCtx, aSession),
                vMulti.ToArray(), vFrom, vTo, GetParam(aCtx, null, "flash")));
        }

        // POST /gantt/move: task=j<id>&start=yyyy-mm-dd&end=yyyy-mm-dd
        public IResult GanttMove(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession)
        {
            string vTask = GetParam(aCtx, aForm, "task");
            if ((vTask.Length > 1) && (char.ToLowerInvariant(vTask[0]) == 'j'))
                vTask = vTask.Substring(1);
            long vJobId = StrToInt64Def(vTask, 0);
            TFieldJob oJob;
            if ((vJobId <= 0) || (!FDB.GetJob(vJobId, out oJob)))
                return Text(aCtx, 404, "That job no longer exists.");
            if (FieldConst.FieldStatusIsFinal(oJob.Status))
                return Text(aCtx, 409, oJob.Reference + " is " +
                    FieldConst.FieldStatusLabel(oJob.Status).ToLowerInvariant() +
                    "; its dates are frozen.");

            DateTime vStartDate = ParseISODate(GetParam(aCtx, aForm, "start"));
            DateTime vEndDate = ParseISODate(GetParam(aCtx, aForm, "end"));
            if (TFieldDBPool.IsZeroDate(vStartDate) ||
                TFieldDBPool.IsZeroDate(vEndDate))
                return Text(aCtx, 400, "The move carried no usable dates.");
            if (vEndDate < vStartDate)
                return Text(aCtx, 400, "A job cannot end before it starts.");

            // Keep the time of day the job already had; the Gantt only moves days.
            DateTime vStart;
            DateTime vEnd;
            if (!TFieldDBPool.IsZeroDate(oJob.ScheduledStart))
                vStart = vStartDate.Add(oJob.ScheduledStart.TimeOfDay);
            else
                vStart = vStartDate.AddDays(9.0 / 24);
            if (!TFieldDBPool.IsZeroDate(oJob.ScheduledEnd))
                vEnd = vEndDate.Add(oJob.ScheduledEnd.TimeOfDay);
            else
                vEnd = vEndDate.AddDays(17.0 / 24);
            if (vEnd <= vStart)
                vEnd = vStart.AddDays(2.0 / 24);

            if (!FDB.RescheduleJob(vJobId, vStart, vEnd))
                return Text(aCtx, 500, "The new dates could not be saved.");
            FDB.AddJobEvent(vJobId, aSession.UserId, "rescheduled",
                "Gantt: " + IsoDateTimeText(vStart) + " to " + IsoDateTimeText(vEnd),
                0, 0);
            Audit(aCtx, aSession, "job.reschedule", "job", vJobId,
                oJob.Reference + " " + IsoDateText(vStart));
            PushAfterChange(aSession.DisplayName, "rescheduled", oJob.Reference,
                "info");
            // yyyy-mm-dd, not a locale month name: the UI is English whatever the
            // machine's regional settings say.
            return Text(aCtx, 200, oJob.Reference + " now runs " +
                IsoDateText(vStart) + " to " + IsoDateText(vEnd) + ", saved.");
        }

        // ----- dispatcher: map ----- //

        public IResult MapGet(HttpContext aCtx, TFieldSession aSession)
        {
            return Html(200, FPages.BuildMapPage(CtxOf(aCtx, aSession),
                FDB.ListTechnicians(true),
                FDB.ListJobsForMap(DateTime.Today, DateTime.Today.AddDays(2))));
        }

        public IResult MapFragment(HttpContext aCtx)
        {
            return Html(200, FPages.BuildMapPayload(FDB.ListTechnicians(true),
                FDB.ListJobsForMap(DateTime.Today, DateTime.Today.AddDays(2)), true));
        }

        // ----- dispatcher: calendar ----- //

        private const string CS_CAL_SQL = "SELECT\r\n" +
            "  CAST(strftime('%d', j.scheduled_start) AS INTEGER) AS day_of_month,\r\n" +
            "  j.title      AS title,\r\n" +
            "  j.status     AS status,\r\n" +
            "  j.reference  AS reference\r\n" +
            "FROM jobs j\r\n" +
            "WHERE j.scheduled_start >= :f AND j.scheduled_start < :t\r\n" +
            "  AND j.status <> 'cancelled'\r\n" +
            "ORDER BY j.scheduled_start";

        public IResult CalendarGet(HttpContext aCtx, TFieldSession aSession)
        {
            int vYear = (int)GetParamInt(aCtx, null, "year", DateTime.Today.Year);
            int vMonth = (int)GetParamInt(aCtx, null, "month", DateTime.Today.Month);
            if ((vYear < 2000) || (vYear > 2100))
                vYear = DateTime.Today.Year;
            if ((vMonth < 1) || (vMonth > 12))
                vMonth = DateTime.Today.Month;
            DateTime vFrom = new DateTime(vYear, vMonth, 1);
            DateTime vTo = vFrom.AddMonths(1);

            using (TFieldDataSet oData = FDB.OpenDataSet(CS_CAL_SQL,
                new string[] { ":f", ":t" },
                new object[] { IsoStamp(vFrom), IsoStamp(vTo) }))
            {
                return Html(200, FPages.BuildCalendarPage(CtxOf(aCtx, aSession),
                    oData.DataSet, vYear, vMonth, oData.SQLText));
            }
        }

        // ----- dispatcher: job list / detail / create ----- //

        public IResult JobsGet(HttpContext aCtx, TFieldSession aSession)
        {
            const int CS_PAGE_SIZE = 25;
            TFieldJobListFilter vFilter = new TFieldJobListFilter();
            vFilter.Status = GetParam(aCtx, null, "status").ToLowerInvariant();
            if ((vFilter.Status == "") || (!FieldConst.FieldIsStatus(vFilter.Status)))
                vFilter.Status = "all";
            vFilter.TechnicianId = GetParamInt(aCtx, null, "technician", -1);
            if (vFilter.TechnicianId < -1)
                vFilter.TechnicianId = -1;
            vFilter.Priority = GetParam(aCtx, null, "priority").ToLowerInvariant();
            if ((vFilter.Priority == "") ||
                (!FieldConst.FieldIsPriority(vFilter.Priority)))
                vFilter.Priority = "all";
            vFilter.Search = GetParam(aCtx, null, "q");
            vFilter.Sort = GetParam(aCtx, null, "sort").ToLowerInvariant();
            if (vFilter.Sort == "")
                vFilter.Sort = "sched";
            vFilter.Dir = GetParam(aCtx, null, "dir").ToLowerInvariant();
            if (vFilter.Dir == "")
                vFilter.Dir = "desc";
            vFilter.Page = (int)GetParamInt(aCtx, null, "page", 1);
            if (vFilter.Page < 1)
                vFilter.Page = 1;
            vFilter.PageSize = CS_PAGE_SIZE;

            vFilter.Total = FDB.CountJobs(vFilter.Status, vFilter.TechnicianId,
                vFilter.Priority, DateTime.MinValue, DateTime.MinValue,
                vFilter.Search);
            TFieldJob[] vJobs = FDB.ListJobs(vFilter.Status, vFilter.TechnicianId,
                vFilter.Priority, DateTime.MinValue, DateTime.MinValue,
                vFilter.Search, vFilter.Sort, vFilter.Dir, CS_PAGE_SIZE,
                (vFilter.Page - 1) * CS_PAGE_SIZE);

            return Html(200, FPages.BuildJobListPage(CtxOf(aCtx, aSession), vJobs,
                FDB.ListTechnicians(true), vFilter));
        }

        // The new-job form. The Delphi builds its context from the session + theme
        // only (no unread count), so this does too.
        public IResult JobNewGet(HttpContext aCtx, TFieldSession aSession,
            string aError)
        {
            TFieldPageCtx oCtx = new TFieldPageCtx();
            oCtx.UserId = aSession.UserId;
            oCtx.DisplayName = aSession.DisplayName;
            oCtx.Initials = aSession.Initials;
            oCtx.Role = aSession.Role;
            oCtx.Theme = ReadThemeCookie(aCtx);
            oCtx.Unread = 0;
            return Html(200, FPages.BuildJobNewPage(oCtx, FDB.ListCustomers(),
                FDB.ListSites(), FDB.ListTechnicians(true), aError));
        }

        public IResult JobSave(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession)
        {
            string vTitle = GetParam(aCtx, aForm, "title");
            string vDescription = GetParam(aCtx, aForm, "description");
            string vPriority = GetParam(aCtx, aForm, "priority").ToLowerInvariant();
            if (!FieldConst.FieldIsPriority(vPriority))
                vPriority = FieldConst.CS_PRIORITY_NORMAL;
            long vCustomerId = GetParamInt(aCtx, aForm, "customer", 0);
            long vSiteId = GetParamInt(aCtx, aForm, "site", 0);
            long vTechId = GetParamInt(aCtx, aForm, "technician", 0);

            if (vTitle == "")
                return JobNewGet(aCtx, aSession, "A job needs a title.");
            if (vCustomerId <= 0)
                return JobNewGet(aCtx, aSession,
                    "Pick the customer this job is for.");
            // The site must belong to the customer: never trust the id in the post.
            if (vSiteId > 0)
            {
                TFieldSite oSite;
                if ((!FDB.GetSite(vSiteId, out oSite)) ||
                    (oSite.CustomerId != vCustomerId))
                    return JobNewGet(aCtx, aSession,
                        "That site does not belong to the chosen customer.");
            }

            DateTime vDate = ParseISODate(GetParam(aCtx, aForm, "date"));
            if (TFieldDBPool.IsZeroDate(vDate))
                vDate = DateTime.Today.AddDays(1);
            double vTime = ParseISOTime(GetParam(aCtx, aForm, "time"));
            if (vTime < 0)
                vTime = 9.0 / 24;
            double vHours = StrToFloatDef(GetParam(aCtx, aForm, "hours"), 2);
            if (vHours <= 0)
                vHours = 2;
            if (vHours > 120)
                vHours = 120;
            DateTime vStart = vDate.AddDays(vTime);
            DateTime vEnd = vStart.AddDays(vHours / 24);

            DateTime vSla;
            if (vPriority == FieldConst.CS_PRIORITY_URGENT)
                vSla = vStart.AddDays(4.0 / 24);
            else if (vPriority == FieldConst.CS_PRIORITY_HIGH)
                vSla = vStart.AddDays(8.0 / 24);
            else if (vPriority == FieldConst.CS_PRIORITY_LOW)
                vSla = vStart.AddDays(3);
            else
                vSla = vStart.AddDays(1);

            string vRef;
            long vNewId = FDB.CreateJob(vCustomerId, vSiteId, 0, vTechId, vTitle,
                vDescription, vPriority, vStart, vEnd, vSla, out vRef);
            if (vNewId <= 0)
                return JobNewGet(aCtx, aSession, "The job could not be created.");
            for (int vI = 0; vI < CS_DEFAULT_CHECKLIST.Length; vI++)
                FDB.AddChecklistItem(vNewId, vI + 1, CS_DEFAULT_CHECKLIST[vI]);
            FDB.AddJobEvent(vNewId, aSession.UserId, "created",
                "Created by " + aSession.DisplayName, 0, 0);
            if (vTechId > 0)
                FDB.AddJobEvent(vNewId, aSession.UserId, "assigned",
                    "Assigned on creation", 0, 0);
            Audit(aCtx, aSession, "job.create", "job", vNewId, vRef);
            PushAfterChange(aSession.DisplayName, "created", vRef, "success");
            return Redirect("/jobs/" + Str(vNewId) + "?flash=created");
        }

        public IResult JobCancel(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession)
        {
            long vJobId = GetParamInt(aCtx, aForm, "job", 0);
            TFieldJob oJob;
            if ((vJobId <= 0) || (!FDB.GetJob(vJobId, out oJob)))
                return Text(aCtx, 404, "That job no longer exists.");
            // Same machine as everywhere else, checked here too.
            if (!FieldConst.FieldCanTransition(oJob.Status,
                FieldConst.CS_JOB_CANCELLED))
                return Text(aCtx, 409, "A job that is " +
                    FieldConst.FieldStatusLabel(oJob.Status).ToLowerInvariant() +
                    " cannot be cancelled.");
            FDB.SetJobStatus(vJobId, FieldConst.CS_JOB_CANCELLED);
            FDB.AddJobEvent(vJobId, aSession.UserId, "cancelled",
                "Cancelled by " + aSession.DisplayName, 0, 0);
            Audit(aCtx, aSession, "job.cancel", "job", vJobId, oJob.Reference);
            PushAfterChange(aSession.DisplayName, "cancelled", oJob.Reference,
                "secondary");
            string vView = GetParam(aCtx, aForm, "view").ToLowerInvariant();
            if (vView != "week")
                vView = "day";
            DateTime vAnchor = GetParamDate(aCtx, aForm, "anchor", DateTime.Today);
            return Html(200, FPages.BuildBoardLanes(BuildLanes(vAnchor, vView,
                GetParamIntList(aCtx, aForm, "tech")), vAnchor, vView));
        }

        public IResult JobDetailGet(HttpContext aCtx, TFieldSession aSession,
            long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx)));
            FDB.MarkMessagesRead(aJobId, aSession.UserId);
            TFieldSignature oSig;
            bool vHasSig = FDB.GetSignature(aJobId, out oSig);
            return Html(200, FPages.BuildJobDetailPage(CtxOf(aCtx, aSession), oJob,
                FDB.ListJobEvents(aJobId), FDB.ListChecklist(aJobId),
                FDB.ListJobParts(aJobId), FDB.ListJobPhotos(aJobId), oSig, vHasSig,
                FDB.ListMessages(aJobId), GetParam(aCtx, null, "flash"),
                GetParam(aCtx, null, "error")));
        }

        // THE state machine gate. Every status write in the application funnels
        // through here or through MyStatusPost, and both ask FieldCanTransition
        // first; an illegal move is refused with 409 and the row is never touched.
        public IResult JobStatusPost(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Text(aCtx, 404, "That job no longer exists.");
            string vNew = GetParam(aCtx, aForm, "status").ToLowerInvariant();
            if (!FieldConst.FieldIsStatus(vNew))
                return Text(aCtx, 400, "\"" + vNew + "\" is not a job status.");
            if (!FieldConst.FieldCanTransition(oJob.Status, vNew))
                return Text(aCtx, 409, "Refused: " + oJob.Reference + " is " +
                    FieldConst.FieldStatusLabel(oJob.Status).ToLowerInvariant() +
                    " and the state machine has " + "no transition from " +
                    FieldConst.FieldStatusLabel(oJob.Status).ToLowerInvariant() +
                    " to " + FieldConst.FieldStatusLabel(vNew).ToLowerInvariant() +
                    ".");
            if ((vNew == FieldConst.CS_JOB_SCHEDULED) &&
                (oJob.Status == FieldConst.CS_JOB_NEW) && (oJob.TechnicianId <= 0))
                return Text(aCtx, 409, "Assign a technician before scheduling " +
                    oJob.Reference + ".");

            FDB.SetJobStatus(aJobId, vNew);
            FDB.AddJobEvent(aJobId, aSession.UserId, vNew,
                "Set to " + FieldConst.FieldStatusLabel(vNew) + " by " +
                aSession.DisplayName, 0, 0);
            Audit(aCtx, aSession, "job.status", "job", aJobId,
                oJob.Status + " -> " + vNew);
            PushAfterChange(aSession.DisplayName, "set " + oJob.Reference + " to",
                FieldConst.FieldStatusLabel(vNew),
                FieldConst.FieldStatusColorName(vNew));
            string vReturn = GetParam(aCtx, aForm, "return");
            if (vReturn == "")
                vReturn = "/jobs/" + Str(aJobId);
            if (vReturn.IndexOf('?') >= 0)
                return Redirect(vReturn + "&flash=status");
            return Redirect(vReturn + "?flash=status");
        }

        // ----- the service report (job-scoped, NOT office-only) ----- //

        // The 60.HTML dispatcher lets the technician who holds the job through this
        // route without the office gate; CanActOnJob is the real check.
        public IResult JobReportPDF(HttpContext aCtx, TFieldSession aSession,
            long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Status(404);
            if (!CanActOnJob(aSession, oJob))
                return Status(403);

            TFieldChecklistItem[] vChecklist = FDB.ListChecklist(aJobId);
            TFieldJobPart[] vParts = FDB.ListJobParts(aJobId);
            TFieldJobPhoto[] vPhotos = FDB.ListJobPhotos(aJobId);
            TFieldJobEvent[] vEvents = FDB.ListJobEvents(aJobId);
            TFieldSignature oSig;
            bool vHasSig = FDB.GetSignature(aJobId, out oSig);

            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = "Service report " + oJob.Reference;
            oPDF.Author = "sgcField Service";
            oPDF.Subject = oJob.Title;
            oPDF.HeaderText = "sgcField Service - service report " + oJob.Reference;
            oPDF.FooterText = "Page {page} of {pages}";
            oPDF.ShowPageNumbers = true;
            oPDF.PageSize = TsgcHTMLPDFPageSize.psA4;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poPortrait;

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 16);
            oPDF.SetColor("#EA580C");
            oPDF.TextOut(20, 38, oJob.Title);
            oPDF.SetColor("#212529");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            oPDF.TextOut(20, 46, oJob.Reference + "   " +
                FieldConst.FieldStatusLabel(oJob.Status) + "   " +
                FieldConst.FieldPriorityLabel(oJob.Priority) + " priority");

            oPDF.SetLineWidth(0.3);
            oPDF.Line(20, 50, 190, 50);

            oPDF.BeginTable(45, 125);
            oPDF.TableHeader("Field", "Value");
            oPDF.TableRow("Customer", TextCell(oJob.CustomerName));
            oPDF.TableRow("Site", TextCell(oJob.SiteName + " - " + oJob.SiteAddress));
            if (oJob.AssetName != "")
                oPDF.TableRow("Asset", TextCell(oJob.AssetName));
            if (oJob.TechnicianName != "")
                oPDF.TableRow("Technician", TextCell(oJob.TechnicianName));
            else
                oPDF.TableRow("Technician", "Unassigned");
            if (!TFieldDBPool.IsZeroDate(oJob.ScheduledStart))
                oPDF.TableRow("Scheduled", IsoDateTimeText(oJob.ScheduledStart));
            if (!TFieldDBPool.IsZeroDate(oJob.ActualStart))
                oPDF.TableRow("Started", IsoDateTimeText(oJob.ActualStart));
            if (!TFieldDBPool.IsZeroDate(oJob.ActualEnd))
                oPDF.TableRow("Finished", IsoDateTimeText(oJob.ActualEnd));
            if (!TFieldDBPool.IsZeroDate(oJob.SlaDueAt))
                oPDF.TableRow("SLA due", IsoDateTimeText(oJob.SlaDueAt));
            if (oJob.Description != "")
                oPDF.TableRow("Reported fault", TextCell(oJob.Description));
            oPDF.EndTable();

            // --- work carried out --- //
            double vY = oPDF.CurrentY + 8;
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
            oPDF.TextOut(20, vY, "Work carried out");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            oPDF.CurrentY = vY + 3;
            oPDF.BeginTable(140, 30);
            oPDF.TableHeader("Checklist step", "Done");
            if (vChecklist.Length == 0)
                oPDF.TableRow("No checklist recorded", "");
            else
                for (int vI = 0; vI < vChecklist.Length; vI++)
                {
                    string vDone = vChecklist[vI].Done ? "yes" : "no";
                    oPDF.TableRow(TextCell(vChecklist[vI].Text_), vDone);
                }
            oPDF.EndTable();

            // --- parts --- //
            vY = oPDF.CurrentY + 8;
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
            oPDF.TextOut(20, vY, "Parts used");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            oPDF.CurrentY = vY + 3;
            oPDF.BeginTable(30, 80, 20, 20, 20);
            oPDF.TableHeader("SKU", "Part", "Qty", "Unit", "Total");
            double vTotal = 0;
            if (vParts.Length == 0)
                oPDF.TableRow("", "No parts used", "", "", "");
            else
                for (int vI = 0; vI < vParts.Length; vI++)
                {
                    vTotal = vTotal + vParts[vI].Qty * vParts[vI].UnitPrice;
                    oPDF.TableRow(TextCell(vParts[vI].Sku),
                        TextCell(vParts[vI].Name),
                        vParts[vI].Qty.ToString(CultureInfo.InvariantCulture),
                        vParts[vI].UnitPrice.ToString("0.00",
                            CultureInfo.InvariantCulture),
                        (vParts[vI].Qty * vParts[vI].UnitPrice).ToString("0.00",
                            CultureInfo.InvariantCulture));
                }
            if (vParts.Length > 0)
                oPDF.TableRow("", "Total", "", "",
                    vTotal.ToString("0.00", CultureInfo.InvariantCulture));
            oPDF.EndTable();

            // --- photos --- //
            if (vPhotos.Length > 0)
            {
                oPDF.NewPage();
                oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
                oPDF.TextOut(20, 38, "Photos from site");
                vY = 44;
                double vX = 20;
                for (int vI = 0; vI < vPhotos.Length; vI++)
                {
                    string vPath = FPhotos.ResolvePath(aJobId, vPhotos[vI].Filename);
                    if (vPath == "")
                        continue;
                    try
                    {
                        oPDF.ImageFromFile(vX, vY, 55, 42, vPath);
                    }
                    catch (Exception)
                    {
                        // an unreadable or unsupported image must never sink the
                        // report
                        continue;
                    }
                    oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8);
                    string vCaption = vPhotos[vI].Caption ?? "";
                    if (vCaption.Length > 34)
                        vCaption = vCaption.Substring(0, 34);
                    oPDF.TextOut(vX, vY + 46, TextCell(vCaption));
                    vX = vX + 58;
                    if (vX > 150)
                    {
                        vX = 20;
                        vY = vY + 52;
                        if (vY > 230)
                        {
                            oPDF.NewPage();
                            vY = 38;
                        }
                    }
                }
            }

            // --- history --- //
            oPDF.NewPage();
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
            oPDF.TextOut(20, 38, "Job history");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            oPDF.CurrentY = 41;
            oPDF.BeginTable(38, 32, 100);
            oPDF.TableHeader("When", "Event", "Detail");
            for (int vI = 0; vI < vEvents.Length; vI++)
                oPDF.TableRow(IsoDateTimeText(vEvents[vI].CreatedAt),
                    TextCell(vEvents[vI].Kind), TextCell(vEvents[vI].Detail));
            oPDF.EndTable();

            // --- signature --- //
            vY = oPDF.CurrentY + 10;
            if (vY > 220)
            {
                oPDF.NewPage();
                vY = 40;
            }
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
            oPDF.TextOut(20, vY, "Customer sign-off");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            if (vHasSig)
            {
                oPDF.TextOut(20, vY + 8, "Signed by " + TextCell(oSig.SignerName) +
                    " on " + IsoDateTimeText(oSig.SignedAt));
                byte[] vBytes = DataURLToPDFBytes(oSig.SignaturePng);
                if (vBytes.Length > 0)
                {
                    try
                    {
                        oPDF.Image(20, vY + 12, 80, 42, vBytes);
                    }
                    catch (Exception)
                    {
                        oPDF.TextOut(20, vY + 20,
                            "[signature image could not be embedded]");
                    }
                    oPDF.SetLineWidth(0.3);
                    oPDF.Line(20, vY + 56, 100, vY + 56);
                    oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 8);
                    oPDF.TextOut(20, vY + 60, "Captured on the technician's phone");
                }
                else
                    oPDF.TextOut(20, vY + 20,
                        "[signature stored in an unreadable form]");
            }
            else
                oPDF.TextOut(20, vY + 8, "Not signed off yet.");

            MemoryStream oStream = new MemoryStream();
            oPDF.SaveToStream(oStream);
            return InlineFile(aCtx, oStream, "application/pdf",
                SanitizeHeaderFilename(oJob.Reference) + ".pdf");
        }

        // The job photos belong to the job, not to the office: the technician who
        // holds it reaches them too (CanActOnJob is the gate).
        public IResult JobPhotoGet(HttpContext aCtx, TFieldSession aSession,
            long aJobId, long aPhotoId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Status(404);
            if (!CanActOnJob(aSession, oJob))
                return Status(403);
            // Defense in depth: the photo must belong to THIS job.
            TFieldJobPhoto oPhoto;
            if ((!FDB.GetJobPhoto(aPhotoId, out oPhoto)) || (oPhoto.JobId != aJobId))
                return Status(404);
            string vPath = FPhotos.ResolvePath(aJobId, oPhoto.Filename);
            if (vPath == "")
                return Status(404);
            byte[] vBytes;
            try
            {
                vBytes = File.ReadAllBytes(vPath);
            }
            catch (Exception)
            {
                return Status(404);
            }
            aCtx.Response.Headers["Cache-Control"] = "private, max-age=600";
            string vType = oPhoto.ContentType;
            if (vType == "")
                vType = "application/octet-stream";
            return Results.Bytes(vBytes, vType);
        }

        // ----- dispatcher: customers, sites, assets, parts ----- //

        public IResult CustomersGet(HttpContext aCtx, TFieldSession aSession)
        {
            string vSearch = GetParam(aCtx, null, "q");
            return Html(200, FPages.BuildCustomerListPage(CtxOf(aCtx, aSession),
                FDB.ListCustomers(vSearch, GetParam(aCtx, null, "sort"),
                    GetParam(aCtx, null, "dir")), vSearch));
        }

        // The Delphi walked the id list and appended whatever GetJob left behind;
        // the managed GetJob hands back false on a miss, so a miss is skipped rather
        // than pushing a null into the page builder.
        private TFieldJob[] JobsByIdQuery(string aSQL, string aParamName,
            object aParamValue)
        {
            List<TFieldJob> vJobs = new List<TFieldJob>();
            using (TFieldDataSet oData = FDB.OpenDataSet(aSQL,
                new string[] { aParamName }, new object[] { aParamValue }))
            {
                foreach (DataRow oRow in oData.DataSet.Rows)
                {
                    TFieldJob oJob;
                    if (FDB.GetJob(Convert.ToInt64(oRow["id"],
                        CultureInfo.InvariantCulture), out oJob))
                        vJobs.Add(oJob);
                }
            }
            return vJobs.ToArray();
        }

        public IResult CustomerDetailGet(HttpContext aCtx, TFieldSession aSession,
            long aCustomerId)
        {
            TFieldCustomer oCustomer;
            if (!FDB.GetCustomer(aCustomerId, out oCustomer))
                return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx)));
            TFieldJob[] vJobs = JobsByIdQuery(
                "SELECT j.id FROM jobs j WHERE j.customer_id = :c " +
                "ORDER BY j.scheduled_start DESC LIMIT 25", ":c", aCustomerId);
            return Html(200, FPages.BuildCustomerDetailPage(CtxOf(aCtx, aSession),
                oCustomer, FDB.ListSitesForCustomer(aCustomerId), vJobs,
                GetParam(aCtx, null, "flash")));
        }

        public IResult CustomerSave(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession)
        {
            long vId = GetParamInt(aCtx, aForm, "id", 0);
            string vName = GetParam(aCtx, aForm, "name");
            if (vName == "")
                return Redirect("/customers");
            long vNewId = FDB.SaveCustomer(vId, vName, GetParam(aCtx, aForm, "contact"),
                GetParam(aCtx, aForm, "email"), GetParam(aCtx, aForm, "phone"),
                GetParam(aCtx, aForm, "address"), GetParam(aCtx, aForm, "city"));
            if (vNewId <= 0)
                return Redirect("/customers");
            Audit(aCtx, aSession, "customer.save", "customer", vNewId, vName);
            return Redirect("/customers/" + Str(vNewId) + "?flash=saved");
        }

        public IResult SiteDetailGet(HttpContext aCtx, TFieldSession aSession,
            long aSiteId)
        {
            TFieldSite oSite;
            if (!FDB.GetSite(aSiteId, out oSite))
                return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx)));
            TFieldJob[] vJobs = JobsByIdQuery(
                "SELECT j.id FROM jobs j WHERE j.site_id = :s " +
                "ORDER BY j.scheduled_start DESC LIMIT 25", ":s", aSiteId);
            return Html(200, FPages.BuildSiteDetailPage(CtxOf(aCtx, aSession), oSite,
                FDB.ListAssetsForSite(aSiteId), vJobs));
        }

        private const string CS_ASSET_SQL = "SELECT\r\n" +
            "  a.id            AS id,\r\n" +
            "  a.parent_id     AS parent_id,\r\n" +
            "  a.name          AS name,\r\n" +
            "  a.model         AS model,\r\n" +
            "  a.serial        AS serial,\r\n" +
            "  a.status        AS status,\r\n" +
            "  s.name          AS site,\r\n" +
            "  c.name          AS customer\r\n" +
            "FROM assets a\r\n" +
            "LEFT JOIN sites s     ON s.id = a.site_id\r\n" +
            "LEFT JOIN customers c ON c.id = s.customer_id\r\n" +
            "WHERE (:q = '' OR a.name LIKE :like ESCAPE '\\'\r\n" +
            "                OR a.model LIKE :like ESCAPE '\\'\r\n" +
            "                OR a.serial LIKE :like ESCAPE '\\')\r\n" +
            "ORDER BY c.name, s.name, a.parent_id, a.name\r\n" +
            "LIMIT 400";

        public IResult AssetsGet(HttpContext aCtx, TFieldSession aSession)
        {
            string vSearch = GetParam(aCtx, null, "q");
            string vLike = "%" + vSearch.Replace("\\", "\\\\").Replace("%", "\\%")
                .Replace("_", "\\_") + "%";
            using (TFieldDataSet oData = FDB.OpenDataSet(CS_ASSET_SQL,
                new string[] { ":q", ":like" }, new object[] { vSearch, vLike }))
            {
                return Html(200, FPages.BuildAssetListPage(CtxOf(aCtx, aSession),
                    oData.DataSet, oData.SQLText, vSearch));
            }
        }

        public IResult AssetDetailGet(HttpContext aCtx, TFieldSession aSession,
            long aAssetId)
        {
            TFieldAsset oAsset;
            if (!FDB.GetAsset(aAssetId, out oAsset))
                return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx)));
            TFieldAsset[] vSiblings = FDB.ListAssetsForSite(oAsset.SiteId);
            List<TFieldAsset> vChildren = new List<TFieldAsset>();
            for (int vI = 0; vI < vSiblings.Length; vI++)
                if (vSiblings[vI].ParentId == aAssetId)
                    vChildren.Add(vSiblings[vI]);

            TFieldJob[] vJobs = JobsByIdQuery(
                "SELECT j.id FROM jobs j WHERE j.asset_id = :a " +
                "ORDER BY j.scheduled_start DESC LIMIT 25", ":a", aAssetId);
            return Html(200, FPages.BuildAssetDetailPage(CtxOf(aCtx, aSession),
                oAsset, vChildren.ToArray(), vJobs));
        }

        public IResult PartsGet(HttpContext aCtx, TFieldSession aSession)
        {
            string vSearch = GetParam(aCtx, null, "q");
            return Html(200, FPages.BuildPartsPage(CtxOf(aCtx, aSession),
                FDB.ListParts(vSearch), vSearch, GetParam(aCtx, null, "flash")));
        }

        public IResult PartsSave(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession)
        {
            long vId = GetParamInt(aCtx, aForm, "id", 0);
            string vSku = GetParam(aCtx, aForm, "sku");
            string vName = GetParam(aCtx, aForm, "name");
            if ((vSku == "") || (vName == ""))
                return Redirect("/parts");
            double vPrice = StrToFloatDef(GetParam(aCtx, aForm, "price"), 0);
            int vStock = (int)GetParamInt(aCtx, aForm, "stock", 0);
            long vNewId = FDB.SavePart(vId, vSku, vName, vPrice, vStock);
            if (vNewId > 0)
                Audit(aCtx, aSession, "part.save", "part", vNewId, vSku);
            return Redirect("/parts?flash=saved");
        }

        // ----- technician (mobile) ----- //

        public IResult MyJobsGet(HttpContext aCtx, TFieldSession aSession)
        {
            TFieldTechnician oTech;
            if (!SessionTechnician(aSession, out oTech))
                return Html(403, FPages.BuildDeniedPage(CtxOf(aCtx, aSession),
                    "This account is not linked to a technician record, so it has " +
                    "no job list of its own."));
            TFieldJob[] vToday = FDB.ListJobsForTechnicianDay(oTech.Id, DateTime.Today);
            TFieldJob[] vUpcoming = FDB.ListJobsInRange(DateTime.Today.AddDays(1),
                DateTime.Today.AddDays(15), oTech.Id);
            return Html(200, FPages.BuildMyJobsPage(CtxOf(aCtx, aSession), oTech,
                vToday, vUpcoming, GetParam(aCtx, null, "flash")));
        }

        public IResult MyJobGet(HttpContext aCtx, TFieldSession aSession, long aJobId,
            string aError)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx)));
            if (!CanActOnJob(aSession, oJob))
                return Html(403, FPages.BuildDeniedPage(CtxOf(aCtx, aSession),
                    "That job is not on your list."));
            TFieldSignature oSig;
            bool vHasSig = FDB.GetSignature(aJobId, out oSig);
            return Html(200, FPages.BuildMyJobPage(CtxOf(aCtx, aSession), oJob,
                FDB.ListChecklist(aJobId), FDB.ListJobParts(aJobId),
                FDB.ListJobPhotos(aJobId), oSig, vHasSig,
                GetParam(aCtx, null, "flash"), aError));
        }

        // The three big buttons on the phone. Each one is a state-machine transition
        // and is refused server-side when the machine says no, whatever the page was
        // showing when the thumb landed.
        public IResult MyStatusPost(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, long aJobId, string aNewStatus)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Text(aCtx, 404, "That job no longer exists.");
            if (!CanActOnJob(aSession, oJob))
                return Text(aCtx, 403, "That job is not on your list.");
            if (!FieldConst.FieldCanTransition(oJob.Status, aNewStatus))
                return MyJobGet(aCtx, aSession, aJobId, "Refused: this job is " +
                    FieldConst.FieldStatusLabel(oJob.Status).ToLowerInvariant() +
                    ", so it cannot go straight to " +
                    FieldConst.FieldStatusLabel(aNewStatus).ToLowerInvariant() +
                    ". Nothing was changed.");
            // Completing with an unticked checklist is a real-world mistake, so it is
            // stopped here rather than quietly accepted.
            if (string.Equals(aNewStatus, FieldConst.CS_JOB_COMPLETE,
                StringComparison.OrdinalIgnoreCase))
            {
                int vDone;
                int vTotal;
                FDB.ChecklistProgress(aJobId, out vDone, out vTotal);
                if ((vTotal > 0) && (vDone < vTotal))
                    return MyJobGet(aCtx, aSession, aJobId,
                        "Finish the checklist first: " + Str(vTotal - vDone) +
                        " step(s) still open.");
            }

            FDB.SetJobStatus(aJobId, aNewStatus);
            // Coordinates: en route reports where the van left from, on site reports
            // the site itself. That is what moves the marker on the dispatcher's map.
            double vLat = 0;
            double vLng = 0;
            if (string.Equals(aNewStatus, FieldConst.CS_JOB_ONSITE,
                StringComparison.OrdinalIgnoreCase))
            {
                vLat = oJob.SiteLat;
                vLng = oJob.SiteLng;
            }
            else if (string.Equals(aNewStatus, FieldConst.CS_JOB_ENROUTE,
                StringComparison.OrdinalIgnoreCase))
            {
                vLat = StrToFloatDef(GetParam(aCtx, aForm, "lat"), 0);
                vLng = StrToFloatDef(GetParam(aCtx, aForm, "lng"), 0);
            }
            FDB.AddJobEvent(aJobId, aSession.UserId, aNewStatus,
                aSession.DisplayName + " set " +
                FieldConst.FieldStatusLabel(aNewStatus), vLat, vLng);
            Audit(aCtx, aSession, "job.status", "job", aJobId,
                oJob.Status + " -> " + aNewStatus);
            // This is the path the spec cares about: a technician's status change has
            // to show up on the dispatcher's board without a reload.
            PushAfterChange(aSession.DisplayName, "is " +
                FieldConst.FieldStatusLabel(aNewStatus).ToLowerInvariant() + " for",
                oJob.Reference, FieldConst.FieldStatusColorName(aNewStatus));
            return Redirect("/my/" + Str(aJobId) + "?flash=status");
        }

        public IResult MyCheckPost(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Text(aCtx, 404, "That job no longer exists.");
            if (!CanActOnJob(aSession, oJob))
                return Text(aCtx, 403, "That job is not on your list.");
            long vItemId = GetParamInt(aCtx, aForm, "item", 0);
            bool vDone = GetParam(aCtx, aForm, "done") == "1";
            // The DB layer re-checks that the item belongs to this job.
            if (!FDB.SetChecklistDone(vItemId, aJobId, vDone))
                return MyJobGet(aCtx, aSession, aJobId,
                    "That checklist step is not part of this job.");
            Audit(aCtx, aSession, "job.check", "job", aJobId, "item " + Str(vItemId));
            return Redirect("/my/" + Str(aJobId) + "?flash=checked");
        }

        // Both the CameraScanner and the typed fallback land here. The scanner posts
        // scan=<code>, the form posts scan=<sku>&qty=<n>.
        public IResult MyPartPost(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Text(aCtx, 404, "That job no longer exists.");
            if (!CanActOnJob(aSession, oJob))
                return Text(aCtx, 403, "That job is not on your list.");
            string vCode = GetParam(aCtx, aForm, "scan");
            if (vCode == "")
                vCode = GetParam(aCtx, aForm, "sku");
            if (vCode == "")
                return MyJobGet(aCtx, aSession, aJobId,
                    "No part code arrived. Scan again, or type the SKU.");
            TFieldPart oPart;
            if (!FDB.GetPartBySku(vCode, out oPart))
                return MyJobGet(aCtx, aSession, aJobId, "No part matches \"" + vCode +
                    "\". Check the label, or type the SKU by hand.");
            int vQty = (int)GetParamInt(aCtx, aForm, "qty", 1);
            if (vQty < 1)
                vQty = 1;
            if (vQty > 99)
                vQty = 99;
            FDB.AddJobPart(aJobId, oPart.Id, vQty, oPart.UnitPrice);
            FDB.AddJobEvent(aJobId, aSession.UserId, "part",
                Str(vQty) + " x " + oPart.Sku + " - " + oPart.Name, 0, 0);
            Audit(aCtx, aSession, "part.add", "job", aJobId, oPart.Sku);
            PushAfterChange(aSession.DisplayName, "used " + oPart.Sku + " on",
                oJob.Reference, "info");
            return Redirect("/my/" + Str(aJobId) + "?flash=part");
        }

        // Materialize the posted files of one form field into the reused
        // TFieldMultipartField shape (original name, content type, raw bytes). The
        // 60.HTML host parsed multipart/form-data by hand (TFieldMultipart); here the
        // framework hands the files over as IFormFile and the SAME TFieldPhotoStore
        // does the validating and the storing, so the semantics are unchanged.
        private static TFieldMultipartField[] CollectUploads(IFormCollection aForm,
            string aField)
        {
            List<TFieldMultipartField> vResult = new List<TFieldMultipartField>();
            if (aForm == null || aForm.Files == null)
                return vResult.ToArray();
            for (int vI = 0; vI < aForm.Files.Count; vI++)
            {
                IFormFile oFile = aForm.Files[vI];
                if (oFile == null)
                    continue;
                if (!string.Equals(oFile.Name, aField,
                    StringComparison.OrdinalIgnoreCase))
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
                TFieldMultipartField oField = new TFieldMultipartField();
                oField.Name = oFile.Name;
                oField.FileName = Path.GetFileName(oFile.FileName);
                oField.ContentType = oFile.ContentType ?? "";
                oField.FileBytes = vBytes;
                vResult.Add(oField);
            }
            return vResult.ToArray();
        }

        public IResult MyPhotoPost(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Text(aCtx, 404, "That job no longer exists.");
            if (!CanActOnJob(aSession, oJob))
                return Text(aCtx, 403, "That job is not on your list.");

            TFieldMultipartField[] vFiles = CollectUploads(aForm, "photo");
            string vCaption = GetParam(aCtx, aForm, "caption");
            if (vFiles.Length == 0)
                return MyJobGet(aCtx, aSession, aJobId,
                    "No photo came through. Pick a file and try again.");
            string vError;
            if (!FPhotos.ValidateFiles(vFiles, out vError))
                return MyJobGet(aCtx, aSession, aJobId,
                    "That photo was rejected: " + vError);
            TFieldPhotoSaved[] vSaved = FPhotos.SaveFiles(aJobId, vFiles, out vError);
            if (vSaved.Length == 0)
                return MyJobGet(aCtx, aSession, aJobId,
                    "The photo could not be stored: " + vError);
            for (int vI = 0; vI < vSaved.Length; vI++)
                FDB.AddJobPhoto(aJobId, vSaved[vI].StoredName, vSaved[vI].ContentType,
                    vSaved[vI].SizeBytes, vCaption);
            FDB.AddJobEvent(aJobId, aSession.UserId, "photo",
                Str(vSaved.Length) + " photo(s) from site", 0, 0);
            Audit(aCtx, aSession, "job.photo", "job", aJobId,
                Str(vSaved.Length) + " file(s)");
            PushAfterChange(aSession.DisplayName, "added a photo to", oJob.Reference,
                "secondary");
            return Redirect("/my/" + Str(aJobId) + "?flash=photo");
        }

        public IResult MySignPost(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Text(aCtx, 404, "That job no longer exists.");
            if (!CanActOnJob(aSession, oJob))
                return Text(aCtx, 403, "That job is not on your list.");
            string vSigner = GetParam(aCtx, aForm, "signer");
            if (vSigner == "")
                vSigner = oJob.CustomerName;
            string vPng = GetRawParam(aCtx, aForm, "signature");
            if ((vPng.Trim() == "") ||
                (!vPng.ToLowerInvariant().StartsWith("data:image",
                    StringComparison.Ordinal)))
                return MyJobGet(aCtx, aSession, aJobId,
                    "Nothing was drawn on the pad, so nothing was saved. " +
                    "Ask the customer to sign and press Save again.");
            if (vPng.Length > 400 * 1024)
                return MyJobGet(aCtx, aSession, aJobId,
                    "That signature is too large to store.");
            FDB.SaveSignature(aJobId, vSigner, vPng);
            FDB.AddJobEvent(aJobId, aSession.UserId, "signed", "Signed by " + vSigner,
                oJob.SiteLat, oJob.SiteLng);
            Audit(aCtx, aSession, "job.sign", "job", aJobId, vSigner);
            PushAfterChange(aSession.DisplayName, "captured a signature on",
                oJob.Reference, "success");
            return Redirect("/my/" + Str(aJobId) + "?flash=signed");
        }

        // ----- chat, both ways ----- //

        public IResult ChatGet(HttpContext aCtx, TFieldSession aSession, long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx)));
            if (!CanActOnJob(aSession, oJob))
                return Html(403, FPages.BuildDeniedPage(CtxOf(aCtx, aSession),
                    "That conversation belongs to another technician."));
            FDB.MarkMessagesRead(aJobId, aSession.UserId);
            return Html(200, FPages.BuildMyChatPage(CtxOf(aCtx, aSession), oJob,
                FDB.ListMessages(aJobId)));
        }

        // Answers with the rendered bubble so the page can append it without a
        // reload, and with a plain-text reason on failure so the client can say what
        // went wrong and offer a retry instead of losing what was typed.
        public IResult ChatPost(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession, long aJobId)
        {
            TFieldJob oJob;
            if (!FDB.GetJob(aJobId, out oJob))
                return Text(aCtx, 404, "That job no longer exists.");
            if (!CanActOnJob(aSession, oJob))
                return Text(aCtx, 403, "That conversation is not yours.");
            string vBody = GetParam(aCtx, aForm, "body");
            if (vBody == "")
                return Text(aCtx, 400, "An empty message was not sent.");
            if (vBody.Length > 2000)
                vBody = vBody.Substring(0, 2000);

            // Route it to the other side: a technician writes to dispatch, dispatch
            // writes to the technician who holds the job.
            long vToUser = 0;
            if (IsTechnician(aSession))
            {
                TFieldUser oUser;
                if (FDB.GetUserByUsername(FConfig.AdminUser, out oUser))
                    vToUser = oUser.Id;
            }
            else if (oJob.TechnicianId > 0)
            {
                TFieldTechnician oTech;
                if (FDB.GetTechnician(oJob.TechnicianId, out oTech))
                    vToUser = oTech.UserId;
            }

            long vNewId = FDB.AddMessage(aJobId, aSession.UserId, vToUser, vBody);
            if (vNewId <= 0)
                return Text(aCtx, 500, "The message was not stored. Nothing is lost, " +
                    "press Retry.");
            Audit(aCtx, aSession, "chat.send", "job", aJobId,
                vBody.Length <= 60 ? vBody : vBody.Substring(0, 60));
            PushAfterChange(aSession.DisplayName, "messaged about", oJob.Reference,
                "primary");

            TFieldMessage[] vMessages = FDB.ListMessages(aJobId);
            for (int vI = 0; vI < vMessages.Length; vI++)
                if (vMessages[vI].Id == vNewId)
                    return Html(200, FPages.BuildChatBubble(CtxOf(aCtx, aSession),
                        vMessages[vI]));
            return Text(aCtx, 200, "sent");
        }

        // ----- customer portal (public: the reference IS the credential) ----- //

        public IResult TrackLookup(HttpContext aCtx, IFormCollection aForm,
            bool aIsPost)
        {
            string vTheme = ReadThemeCookie(aCtx);
            if (aIsPost)
            {
                string vRef = GetParam(aCtx, aForm, "reference");
                TFieldJob oJob;
                if ((vRef != "") && FDB.GetJobByReference(vRef, out oJob))
                    return Redirect("/track/" + oJob.Reference);
            }
            // No session, no browsing: the only way in is a reference somebody was
            // given, so this page is a single box and nothing else.
            return Html(200, FPages.BuildTrackLookupPage(vTheme,
                aIsPost && (GetParam(aCtx, aForm, "reference") != "")));
        }

        public IResult TrackGet(HttpContext aCtx, string aReference)
        {
            string vTheme = ReadThemeCookie(aCtx);
            // The reference is the whole credential. An unknown one gets the same 404
            // as any other unknown URL: no hint that a job might exist.
            TFieldJob oJob;
            if ((aReference.Length < 8) ||
                (!FDB.GetJobByReference(aReference, out oJob)))
                return Html(404, FPages.BuildNotFoundPage(vTheme));
            TFieldJobEvent[] vEvents = FDB.ListJobEvents(oJob.Id);
            bool vApproved = false;
            for (int vI = 0; vI < vEvents.Length; vI++)
                if (string.Equals(vEvents[vI].Kind, "approved",
                    StringComparison.OrdinalIgnoreCase))
                    vApproved = true;
            int vDone;
            int vTotal;
            FDB.ChecklistProgress(oJob.Id, out vDone, out vTotal);
            return Html(200, FPages.BuildTrackPage(oJob, vEvents, vDone, vTotal,
                vApproved, vTheme, GetParam(aCtx, null, "flash")));
        }

        public IResult TrackApprove(HttpContext aCtx, IFormCollection aForm,
            string aReference)
        {
            TFieldJob oJob;
            if ((aReference.Length < 8) ||
                (!FDB.GetJobByReference(aReference, out oJob)))
                return Status(404);
            string vAction = GetParam(aCtx, aForm, "action").ToLowerInvariant();
            if (vAction == "rate")
            {
                int vRating = (int)GetParamInt(aCtx, aForm, "rating", 0);
                if ((vRating < 1) || (vRating > 5))
                    return Redirect("/track/" + oJob.Reference);
                // Satisfaction lives in job_events, which is where the schema puts it.
                FDB.AddJobEvent(oJob.Id, 0, "rating", Str(vRating), 0, 0);
                FDB.AddAudit(0, "job.rate", "job", oJob.Id, Str(vRating), "public");
                PushAfterChange("Customer", "rated", oJob.Reference, "warning");
                return Redirect("/track/" + oJob.Reference + "?flash=rated");
            }

            FDB.AddJobEvent(oJob.Id, 0, "approved", "Quote approved by the customer",
                0, 0);
            FDB.AddAudit(0, "job.approve", "job", oJob.Id, oJob.Reference, "public");
            PushAfterChange("Customer", "approved the quote for", oJob.Reference,
                "success");
            return Redirect("/track/" + oJob.Reference + "?flash=approved");
        }

        // ----- manager ----- //

        public IResult ReportsGet(HttpContext aCtx, TFieldSession aSession)
        {
            DateTime vFrom = GetParamDate(aCtx, null, "from",
                DateTime.Today.AddMonths(-3));
            DateTime vTo = GetParamDate(aCtx, null, "to", DateTime.Today.AddDays(1));
            if (vTo <= vFrom)
                vTo = vFrom.AddDays(1);
            return Html(200, FPages.BuildReportsPage(CtxOf(aCtx, aSession),
                FDB.GetReportStats(vFrom, vTo), FDB.ListTechStats(vFrom, vTo),
                FDB.GetJobsPerDaySeries(vFrom, vTo), FDB.GetStatusBreakdown(),
                FDB.GetHeatmap(vFrom, vTo), FDB.GetRevenueByCustomer(vFrom, vTo, 14),
                vFrom, vTo));
        }

        public IResult ReportsSlaPDF(HttpContext aCtx)
        {
            DateTime vFrom = GetParamDate(aCtx, null, "from",
                DateTime.Today.AddMonths(-3));
            DateTime vTo = GetParamDate(aCtx, null, "to", DateTime.Today.AddDays(1));
            if (vTo <= vFrom)
                vTo = vFrom.AddDays(1);
            TFieldReportStats vStats = FDB.GetReportStats(vFrom, vTo);
            TFieldTechStat[] vTechs = FDB.ListTechStats(vFrom, vTo);

            double vSla;
            if ((vStats.SlaMet + vStats.SlaBreached) > 0)
                vSla = vStats.SlaMet * 100.0 / (vStats.SlaMet + vStats.SlaBreached);
            else
                vSla = 0;

            TsgcHTMLExportPDF oPDF = new TsgcHTMLExportPDF();
            oPDF.Title = "SLA report";
            oPDF.Author = "sgcField Service";
            oPDF.HeaderText = "sgcField Service - SLA report " + IsoDateText(vFrom) +
                " to " + IsoDateText(vTo);
            oPDF.FooterText = "Page {page} of {pages}";
            oPDF.ShowPageNumbers = true;
            oPDF.Orientation = TsgcHTMLPDFOrientation.poPortrait;

            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 16);
            oPDF.SetColor("#EA580C");
            oPDF.TextOut(20, 38, "Service level report");
            oPDF.SetColor("#212529");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            // Invariant settings: the report is English whatever the machine's
            // regional settings are.
            oPDF.TextOut(20, 46, vFrom.ToString("d MMM yyyy",
                CultureInfo.InvariantCulture) + "  to  " +
                vTo.ToString("d MMM yyyy", CultureInfo.InvariantCulture));
            oPDF.SetLineWidth(0.3);
            oPDF.Line(20, 50, 190, 50);

            oPDF.CurrentY = 52;
            oPDF.BeginTable(90, 80);
            oPDF.TableHeader("Measure", "Value");
            oPDF.TableRow("Jobs in window", Str(vStats.TotalJobs));
            oPDF.TableRow("Completed", Str(vStats.CompletedJobs));
            oPDF.TableRow("Cancelled", Str(vStats.CancelledJobs));
            oPDF.TableRow("Still open", Str(vStats.OpenJobs));
            oPDF.TableRow("SLA met", Str(vStats.SlaMet));
            oPDF.TableRow("SLA breached", Str(vStats.SlaBreached));
            oPDF.TableRow("SLA attainment",
                vSla.ToString("0.0", CultureInfo.InvariantCulture) + " %");
            oPDF.TableRow("First-time fix", Str(vStats.FirstTimeFix));
            oPDF.TableRow("Revisits", Str(vStats.Revisits));
            oPDF.TableRow("Labour hours",
                vStats.LabourHours.ToString("0.0", CultureInfo.InvariantCulture));
            oPDF.TableRow("Parts revenue",
                vStats.PartsRevenue.ToString("#,##0.00",
                    CultureInfo.InvariantCulture));
            oPDF.TableRow("Average satisfaction",
                vStats.AvgSatisfaction.ToString("0.00", CultureInfo.InvariantCulture) +
                " / 5 (" + Str(vStats.RatedJobs) + " rated)");
            oPDF.EndTable();

            oPDF.CurrentY = oPDF.CurrentY + 8;
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelveticaBold, 12);
            oPDF.TextOut(20, oPDF.CurrentY, "By technician");
            oPDF.SetFont(TsgcHTMLPDFFont.pfHelvetica, 10);
            oPDF.CurrentY = oPDF.CurrentY + 3;
            oPDF.BeginTable(55, 25, 25, 25, 25, 25);
            oPDF.TableHeader("Technician", "Done", "SLA met", "Hours", "Util %",
                "Rating");
            for (int vI = 0; vI < vTechs.Length; vI++)
                oPDF.TableRow(TextCell(vTechs[vI].DisplayName),
                    Str(vTechs[vI].Completed), Str(vTechs[vI].SlaMet),
                    vTechs[vI].Hours.ToString("0.0", CultureInfo.InvariantCulture),
                    vTechs[vI].Utilisation.ToString("0", CultureInfo.InvariantCulture),
                    vTechs[vI].Satisfaction.ToString("0.00",
                        CultureInfo.InvariantCulture));
            oPDF.EndTable();

            MemoryStream oStream = new MemoryStream();
            oPDF.SaveToStream(oStream);
            return InlineFile(aCtx, oStream, "application/pdf", "sla-report.pdf");
        }

        // The XLSX comes out of the Grid component itself: the same rows the report
        // page renders, written by SaveToXLSXStream. No spreadsheet library here.
        public IResult ReportsUtilisationXLSX(HttpContext aCtx)
        {
            DateTime vFrom = GetParamDate(aCtx, null, "from",
                DateTime.Today.AddMonths(-3));
            DateTime vTo = GetParamDate(aCtx, null, "to", DateTime.Today.AddDays(1));
            if (vTo <= vFrom)
                vTo = vFrom.AddDays(1);
            TFieldTechStat[] vTechs = FDB.ListTechStats(vFrom, vTo);

            TsgcHTMLComponent_Grid oGrid = new TsgcHTMLComponent_Grid();
            oGrid.ExportSheetName = "Utilisation";
            TsgcHTMLGridColumn oCol = oGrid.Columns.Add();
            oCol.Name = "tech";
            oCol.Title = "Technician";
            oCol = oGrid.Columns.Add();
            oCol.Name = "done";
            oCol.Title = "Completed";
            oCol = oGrid.Columns.Add();
            oCol.Name = "sla";
            oCol.Title = "SLA met";
            oCol = oGrid.Columns.Add();
            oCol.Name = "hours";
            oCol.Title = "Hours";
            oCol = oGrid.Columns.Add();
            oCol.Name = "util";
            oCol.Title = "Utilisation %";
            oCol = oGrid.Columns.Add();
            oCol.Name = "sat";
            oCol.Title = "Satisfaction";
            for (int vI = 0; vI < vTechs.Length; vI++)
                oGrid.AddRow(vTechs[vI].DisplayName, Str(vTechs[vI].Completed),
                    Str(vTechs[vI].SlaMet),
                    vTechs[vI].Hours.ToString("0.0", CultureInfo.InvariantCulture),
                    vTechs[vI].Utilisation.ToString("0", CultureInfo.InvariantCulture),
                    vTechs[vI].Satisfaction.ToString("0.00",
                        CultureInfo.InvariantCulture));

            MemoryStream oStream = new MemoryStream();
            oGrid.SaveToXLSXStream(oStream);
            aCtx.Response.Headers["Cache-Control"] = "no-store";
            return Results.File(oStream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "utilisation.xlsx");
        }

        // ----- the "there is no REST tier" page ----- //

        // Six queries, six components, nothing in between. The datasets stay open
        // while the page renders and are closed on the way out.
        private const string CS_SQL_JOBS = "SELECT\r\n" +
            "  j.reference   AS \"Reference\",\r\n" +
            "  j.title       AS \"Job\",\r\n" +
            "  c.name        AS \"Customer\",\r\n" +
            "  u.display_name AS \"Technician\",\r\n" +
            "  j.priority    AS \"Priority\",\r\n" +
            "  j.status      AS \"Status\",\r\n" +
            "  j.scheduled_start AS \"Scheduled\"\r\n" +
            "FROM jobs j\r\n" +
            "LEFT JOIN customers   c ON c.id = j.customer_id\r\n" +
            "LEFT JOIN technicians t ON t.id = j.technician_id\r\n" +
            "LEFT JOIN users       u ON u.id = t.user_id\r\n" +
            "WHERE j.status NOT IN ('complete', 'cancelled')\r\n" +
            "ORDER BY j.scheduled_start\r\n" +
            "LIMIT 25";

        private const string CS_SQL_TREE = "SELECT\r\n" +
            "  a.id        AS id,\r\n" +
            "  a.parent_id AS parent_id,\r\n" +
            "  a.name      AS \"Asset\",\r\n" +
            "  a.model     AS \"Model\",\r\n" +
            "  a.status    AS \"Condition\"\r\n" +
            "FROM assets a\r\n" +
            "WHERE a.site_id IN (SELECT id FROM sites ORDER BY id LIMIT 12)\r\n" +
            "ORDER BY a.parent_id, a.name";

        private const string CS_SQL_CHART = "SELECT\r\n" +
            "  j.priority AS label,\r\n" +
            "  COUNT(*)   AS value\r\n" +
            "FROM jobs j\r\n" +
            "GROUP BY j.priority\r\n" +
            "ORDER BY value DESC";

        private const string CS_SQL_SELECT = "SELECT\r\n" +
            "  t.id           AS value,\r\n" +
            "  u.display_name AS text\r\n" +
            "FROM technicians t\r\n" +
            "JOIN users u ON u.id = t.user_id\r\n" +
            "WHERE t.active = 1\r\n" +
            "ORDER BY u.display_name";

        private const string CS_SQL_TREEMAP = "SELECT\r\n" +
            "  c.name AS label,\r\n" +
            "  SUM(jp.qty * jp.unit_price) AS value\r\n" +
            "FROM job_parts jp\r\n" +
            "JOIN jobs      j ON j.id = jp.job_id\r\n" +
            "JOIN customers c ON c.id = j.customer_id\r\n" +
            "GROUP BY c.name\r\n" +
            "HAVING value > 0\r\n" +
            "ORDER BY value DESC\r\n" +
            "LIMIT 12";

        private const string CS_SQL_PALETTE = "SELECT\r\n" +
            "  j.reference || ' - ' || j.title AS caption,\r\n" +
            "  c.name                          AS description,\r\n" +
            "  '/jobs/' || j.id                AS href\r\n" +
            "FROM jobs j\r\n" +
            "LEFT JOIN customers c ON c.id = j.customer_id\r\n" +
            "WHERE j.status = 'scheduled'\r\n" +
            "ORDER BY j.scheduled_start\r\n" +
            "LIMIT 20";

        public IResult SqlGet(HttpContext aCtx, TFieldSession aSession)
        {
            using (TFieldDataSet oJobs = FDB.OpenDataSet(CS_SQL_JOBS, null, null))
            using (TFieldDataSet oTree = FDB.OpenDataSet(CS_SQL_TREE, null, null))
            using (TFieldDataSet oChart = FDB.OpenDataSet(CS_SQL_CHART, null, null))
            using (TFieldDataSet oSelect = FDB.OpenDataSet(CS_SQL_SELECT, null, null))
            using (TFieldDataSet oTreeMap = FDB.OpenDataSet(CS_SQL_TREEMAP, null, null))
            using (TFieldDataSet oPalette = FDB.OpenDataSet(CS_SQL_PALETTE, null, null))
            {
                TFieldSqlBlock[] vBlocks = new TFieldSqlBlock[6];

                vBlocks[0] = new TFieldSqlBlock();
                vBlocks[0].Caption = "Open work, in a DataTable";
                vBlocks[0].Note = "TsgcHTMLComponent_DataTable.LoadFromDataSet" +
                    "(oQuery). The column captions are the SQL aliases.";
                vBlocks[0].SQL = oJobs.SQLText;
                vBlocks[0].Kind = "datatable";
                vBlocks[0].Data = oJobs.DataSet;

                vBlocks[1] = new TFieldSqlBlock();
                vBlocks[1].Caption = "Asset hierarchy, in a TreeGrid";
                vBlocks[1].Note = "TsgcHTMLComponent_TreeGrid.LoadFromDataSet" +
                    "(oQuery, 'id', 'parent_id'). The nesting comes from the " +
                    "parent_id column.";
                vBlocks[1].SQL = oTree.SQLText;
                vBlocks[1].Kind = "treegrid";
                vBlocks[1].Data = oTree.DataSet;

                vBlocks[2] = new TFieldSqlBlock();
                vBlocks[2].Caption = "Jobs by priority, in a Chart";
                vBlocks[2].Note = "TsgcHTMLComponent_Chart.LoadFromDataSet(oQuery, " +
                    "'label', ['value']). SQLite does the GROUP BY, Chart.js " +
                    "draws it.";
                vBlocks[2].SQL = oChart.SQLText;
                vBlocks[2].Kind = "chart";
                vBlocks[2].Data = oChart.DataSet;

                vBlocks[3] = new TFieldSqlBlock();
                vBlocks[3].Caption = "Active technicians, in a Select";
                vBlocks[3].Note = "TsgcHTMLComponent_Select.LoadFromDataSet" +
                    "(oQuery, 'value', 'text'). A lookup list with no lookup " +
                    "endpoint.";
                vBlocks[3].SQL = oSelect.SQLText;
                vBlocks[3].Kind = "select";
                vBlocks[3].Data = oSelect.DataSet;

                vBlocks[4] = new TFieldSqlBlock();
                vBlocks[4].Caption = "Parts revenue by customer, in a TreeMap";
                vBlocks[4].Note = "TsgcHTMLComponent_TreeMap.LoadFromDataSet" +
                    "(oQuery, 'label', 'value'). The SVG is produced on the server.";
                vBlocks[4].SQL = oTreeMap.SQLText;
                vBlocks[4].Kind = "treemap";
                vBlocks[4].Data = oTreeMap.DataSet;

                vBlocks[5] = new TFieldSqlBlock();
                vBlocks[5].Caption = "Scheduled jobs, in a CommandPalette";
                vBlocks[5].Note = "TsgcHTMLComponent_CommandPalette.LoadFromDataSet" +
                    "(oQuery, 'caption', 'description', 'href'). Ctrl+J opens it.";
                vBlocks[5].SQL = oPalette.SQLText;
                vBlocks[5].Kind = "palette";
                vBlocks[5].Data = oPalette.DataSet;

                return Html(200, FPages.BuildSqlPage(CtxOf(aCtx, aSession), vBlocks));
            }
        }

        // ----- users + audit ----- //

        public IResult UsersGet(HttpContext aCtx, TFieldSession aSession,
            string aError)
        {
            return Html(200, FPages.BuildUsersPage(CtxOf(aCtx, aSession),
                FDB.ListUsers(), GetParam(aCtx, null, "flash"), aError));
        }

        public IResult UsersSave(HttpContext aCtx, IFormCollection aForm,
            TFieldSession aSession)
        {
            long vId = GetParamInt(aCtx, aForm, "id", 0);
            string vUsername = GetParam(aCtx, aForm, "username").ToLowerInvariant();
            string vDisplay = GetParam(aCtx, aForm, "display_name");
            string vRole = GetParam(aCtx, aForm, "role").ToLowerInvariant();
            string vPhone = GetParam(aCtx, aForm, "phone");
            string vPassword = GetRawParam(aCtx, aForm, "password");

            if ((vRole != FieldConst.CS_ROLE_DISPATCHER) &&
                (vRole != FieldConst.CS_ROLE_TECHNICIAN) &&
                (vRole != FieldConst.CS_ROLE_MANAGER) &&
                (vRole != FieldConst.CS_ROLE_CUSTOMER))
                vRole = FieldConst.CS_ROLE_TECHNICIAN;
            if ((vUsername == "") || (vUsername.Length < 3))
                return UsersGet(aCtx, aSession,
                    "A username of at least 3 characters is required.");
            if (vDisplay == "")
                vDisplay = vUsername;
            if ((vId == 0) && FDB.UsernameExists(vUsername))
                return UsersGet(aCtx, aSession, "That username is already taken.");
            if ((vId == 0) && (vPassword.Length < 6))
                return UsersGet(aCtx, aSession,
                    "A new account needs a password of at least 6 characters.");

            string vHash = "";
            if (vPassword != "")
                vHash = Bcrypt.BcryptHash(vPassword);
            long vNewId = FDB.SaveUser(vId, vUsername, vHash, vRole, vDisplay, vPhone);
            if (vNewId <= 0)
                return UsersGet(aCtx, aSession, "The account could not be saved.");
            Audit(aCtx, aSession, "user.save", "user", vNewId,
                vUsername + " (" + vRole + ")");
            return Redirect("/users?flash=saved");
        }

        public IResult AuditGet(HttpContext aCtx, TFieldSession aSession)
        {
            return Html(200, FPages.BuildAuditPage(CtxOf(aCtx, aSession),
                FDB.ListAudit(200)));
        }

        // ----- fallback (mirrors the 60.HTML DispatchRequest tail) ----- //

        // An unmatched path answers exactly as the 60.HTML dispatcher did: signed
        // out -> /login (the Guarded gate in Program.cs), signed in -> the styled 404
        // page.
        public IResult NotFound(HttpContext aCtx)
        {
            return Html(404, FPages.BuildNotFoundPage(ReadThemeCookie(aCtx)));
        }

        // ----- realtime ----- //

        // Broadcast an out-of-band fragment to every connected browser. Under Kestrel
        // the engine has NO Server bound, so its BroadcastFragment is a no-op and the
        // adapter's hub is the push channel (the 15.Reports pattern).
        public void PushFragment(string aHTML)
        {
            if (string.IsNullOrEmpty(aHTML) || (FHub == null))
                return;
            try
            {
                FHub.BroadcastAsync(aHTML).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        // The fragments that follow any change a request made, so the dispatcher
        // board, the stat tiles, the feed and the map all catch up at once.
        private string BuildLiveFragments(string aActor, string aAction,
            string aTarget, string aColor)
        {
            string vOut = "";
            try
            {
                TFieldTechnician[] vTechs = FDB.ListTechnicians(false);
                if (aAction != "")
                {
                    vOut = vOut + TFieldPages.LiveAddActivity(aActor, aAction, aTarget,
                        aColor);
                    vOut = vOut + TFieldPages.LiveAddLog("info",
                        aActor + " " + aAction + " " + aTarget, "dispatch");
                }
                vOut = vOut + TFieldPages.LiveSyncTechnicians(vTechs);
                vOut = vOut + TFieldPages.LiveSyncJobs(CollectLiveJobs());
                vOut = vOut + FPages.BuildStatStrip(CollectBoardStats(), true);
                vOut = vOut + FPages.BuildMapPayload(vTechs, null, true);
                // Every dispatcher watches their own day or week, so the board itself
                // is not broadcast: this only says "it moved", and each browser
                // re-fetches /board/fragment for the view it is showing.
                vOut = vOut + FPages.BuildBoardStaleSignal();
            }
            catch (Exception E)
            {
                Console.WriteLine("[push] fragment build failed: " + E.Message);
            }
            return vOut;
        }

        private void PushAfterChange(string aActor, string aAction, string aTarget,
            string aColor)
        {
            try
            {
                PushFragment(BuildLiveFragments(aActor, aAction, aTarget, aColor));
            }
            catch (Exception)
            {
                // a broadcast must never take down the request that triggered it
            }
        }

        // One step of the simulated world, then the fragments it produced. Everything
        // it does is a real write: the technicians really move (a job_events row with
        // coordinates), and a job really changes status through the same state
        // machine the UI uses. Called by the FieldPushService background loop.
        public string SimulateTick()
        {
            string vActor = "";
            string vAction = "";
            string vTarget = "";
            string vColor = "info";
            DateTime vNow = DateTime.Now;
            TFieldTechnician[] vTechs;
            int vMoved;

            lock (FSimLock)
            {
                FTick++;

                // 1) walk every en-route van 18% of the way to its site
                TFieldJob[] vEnroute = FDB.ListJobs(FieldConst.CS_JOB_ENROUTE, -1, "",
                    DateTime.MinValue, DateTime.MinValue, "", "sched", "asc", 8, 0);
                vMoved = 0;
                for (int vI = 0; vI < vEnroute.Length; vI++)
                {
                    if (vEnroute[vI].TechnicianId <= 0)
                        continue;
                    if ((vEnroute[vI].SiteLat == 0) && (vEnroute[vI].SiteLng == 0))
                        continue;
                    TFieldTechnician oTech;
                    if (!FDB.GetTechnician(vEnroute[vI].TechnicianId, out oTech))
                        continue;
                    double vLat = oTech.CurrentLat;
                    double vLng = oTech.CurrentLng;
                    if ((vLat == 0) && (vLng == 0))
                    {
                        vLat = oTech.HomeLat;
                        vLng = oTech.HomeLng;
                    }
                    vLat = vLat + (vEnroute[vI].SiteLat - vLat) * 0.18;
                    vLng = vLng + (vEnroute[vI].SiteLng - vLng) * 0.18;
                    FDB.AddJobEvent(vEnroute[vI].Id, oTech.UserId, "gps", "En route",
                        vLat, vLng);
                    vMoved++;
                    if (vMoved >= 4)
                        break;
                }

                // 2) every third tick, advance one job along the machine
                if ((FTick % 3) == 0)
                {
                    TFieldJob[] vOnsite = FDB.ListJobs(FieldConst.CS_JOB_ONSITE, -1, "",
                        DateTime.MinValue, DateTime.MinValue, "", "sched", "asc", 6, 0);
                    vEnroute = FDB.ListJobs(FieldConst.CS_JOB_ENROUTE, -1, "",
                        DateTime.MinValue, DateTime.MinValue, "", "sched", "asc", 6, 0);
                    TFieldJob[] vDue = FDB.ListJobs(FieldConst.CS_JOB_SCHEDULED, -1, "",
                        DateTime.Today, vNow, "", "sched", "asc", 6, 0);

                    if ((vOnsite.Length > 0) &&
                        (!TFieldDBPool.IsZeroDate(vOnsite[0].ActualStart)) &&
                        (vOnsite[0].ActualStart < vNow.AddMinutes(-10)) &&
                        FieldConst.FieldCanTransition(vOnsite[0].Status,
                            FieldConst.CS_JOB_COMPLETE))
                    {
                        // A completed job leaves a finished checklist behind, exactly
                        // as the technician app would.
                        TFieldChecklistItem[] vItems = FDB.ListChecklist(vOnsite[0].Id);
                        for (int vI = 0; vI < vItems.Length; vI++)
                            if (!vItems[vI].Done)
                                FDB.SetChecklistDone(vItems[vI].Id, vOnsite[0].Id, true);
                        FDB.SetJobStatus(vOnsite[0].Id, FieldConst.CS_JOB_COMPLETE);
                        FDB.AddJobEvent(vOnsite[0].Id, 0, "completed", "Work finished",
                            vOnsite[0].SiteLat, vOnsite[0].SiteLng);
                        vActor = vOnsite[0].TechnicianName;
                        vAction = "completed";
                        vTarget = vOnsite[0].Reference;
                        vColor = "success";
                    }
                    else if ((vEnroute.Length > 0) &&
                        FieldConst.FieldCanTransition(vEnroute[0].Status,
                            FieldConst.CS_JOB_ONSITE))
                    {
                        FDB.SetJobStatus(vEnroute[0].Id, FieldConst.CS_JOB_ONSITE);
                        FDB.AddJobEvent(vEnroute[0].Id, 0, "onsite", "Arrived on site",
                            vEnroute[0].SiteLat, vEnroute[0].SiteLng);
                        vActor = vEnroute[0].TechnicianName;
                        vAction = "arrived at";
                        vTarget = vEnroute[0].SiteName;
                        vColor = "warning";
                    }
                    else if ((vDue.Length > 0) && (vDue[0].TechnicianId > 0) &&
                        FieldConst.FieldCanTransition(vDue[0].Status,
                            FieldConst.CS_JOB_ENROUTE))
                    {
                        FDB.SetJobStatus(vDue[0].Id, FieldConst.CS_JOB_ENROUTE);
                        FDB.AddJobEvent(vDue[0].Id, 0, "enroute", "Left for the site",
                            0, 0);
                        vActor = vDue[0].TechnicianName;
                        vAction = "set off for";
                        vTarget = vDue[0].Reference;
                        vColor = "info";
                    }
                }

                vTechs = FDB.ListTechnicians(false);
            }

            if (vAction != "")
                return BuildLiveFragments(vActor, vAction, vTarget, vColor);
            if ((vMoved > 0) || ((FTick % 5) == 0))
                // Nothing newsworthy happened, but the vans moved: refresh the
                // markers, the presence strip and the tiles without writing a feed
                // entry.
                return TFieldPages.LiveSyncTechnicians(vTechs) +
                    TFieldPages.LiveSyncJobs(CollectLiveJobs()) +
                    FPages.BuildStatStrip(CollectBoardStats(), true) +
                    FPages.BuildMapPayload(vTechs, null, true);
            return "";
        }
    }
}
