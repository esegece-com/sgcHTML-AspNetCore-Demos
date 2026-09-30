// ***************************************************************************
//  sgcHelpdeskWeb - Helpdesk support-ticket demo on ASP.NET Core
//  Mirror of demos\60.HTML\09.Helpdesk (TsgcWebSocketHTTPServer host).
//
//  This is the REWRITTEN host layer. It reproduces every branch of the 60.HTML
//  sgcHelpdesk_Server.cs DispatchRequest, but on Kestrel:
//    - The reusable logic (Bcrypt, Config, DB, Pages, Sessions, Types) is copied
//      VERBATIM from the 60.HTML demo and is NOT changed here.
//    - The 60.HTML host scanned RawHeaders for the Cookie line and wrote
//      Set-Cookie / redirects through CustomHeaders on TsgcWSHTTPResponseInfo.
//      Here every cookie is read/written through the native ASP.NET Core cookie
//      API (ctx.Request.Cookies / ctx.Response.Cookies) with the SAME cookie
//      names + attributes (helpdesk_session / helpdesk_theme), and each handler
//      returns an IResult.
//    - Query + form params are merged through GetParam (query first, then the
//      form body), matching the 60.HTML GetParam merge.
//    - MULTIPART: the 60.HTML host parsed multipart/form-data by hand
//      (THelpdeskMultipart) to read the ticket attachments. Here the uploaded
//      files come from ASP.NET Core IFormFile (ctx.Request.Form.Files); they are
//      materialized into THelpdeskUploadFile and persisted by the reused
//      THelpdeskAttachStore with the SAME on-disk layout, and the attachment
//      metadata row is written by the reused DB layer exactly as before. The
//      attachment DOWNLOAD streams the file via Results.File with the original
//      content-type + filename.
//    - The IDOR ticket-ownership check (a user may only see / act on their own
//      tickets unless admin) is preserved on EVERY ticket route, byte-for-byte.
//    - KANBAN LIVE PUSH: the 60.HTML host remembered the session cookie of every
//      WebSocket from its OnHandshake headers and wrote card fragments to the
//      admin ones over FHTTP. Here the board's sgcHTMX bridge WebSocket (opened on
//      the page URL '/') is accepted by the adapter (AcceptWebSocketOnAnyPath),
//      which keeps the upgrade cookies with each connection, and
//      PushKanbanFragment broadcasts through ISgcHtmlHub with a filter that
//      admits only the connections whose session is (still) an admin one.
//
//  The three host files that were NOT copied from 60.HTML are sgcHelpdesk_Server.cs
//  (the TsgcWebSocketHTTPServer host), the console Program.cs, and
//  sgcHelpdesk_Multipart.cs (the hand-rolled parser, replaced by IFormFile; only
//  its storage half lives on as sgcHelpdesk_Attachments.cs).
//
//  IMPORTANT hosting note (the reusable pattern): a Minimal API lambda whose ONLY
//  parameter is HttpContext and which returns Task<IResult> is treated as a raw
//  RequestDelegate, so its IResult is DISCARDED (ASP0016) - the handler's cookie
//  side effects run but the redirect / status / body are lost. To avoid that trap,
//  every endpoint lambda in Program.cs returns plain Task and the Run / RunForm
//  helpers execute the handler's IResult onto the response explicitly.
// ***************************************************************************

using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
// sgc
using esegece.sgcWebSockets;
using esegece.sgcWebSockets.AspNetCore;

namespace Helpdesk
{
    // Reusable host pattern for the 61.HTML.AspNetCore heavy demos: a plain-C#
    // object that owns the reused singletons (DB pool, session store, page
    // builder, attachment store) and exposes one IResult-returning method per
    // 60.HTML DispatchRequest branch. Program.cs maps the Minimal API endpoints
    // onto these methods and applies the auth gate through Guarded.
    public sealed class HelpdeskWebHost : IDisposable
    {
        public const string CS_HELPDESK_SERVER_VERSION = "1.0.0";

        // Self-contained favicon (served at /favicon.svg). Bootstrap blue "H".
        // Verbatim from the 60.HTML host constant (that unit is intentionally not
        // copied).
        public const string CS_FAVICON_SVG = "<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            "viewBox=\"0 0 64 64\" role=\"img\" aria-label=\"Helpdesk\">" +
            "<rect width=\"64\" height=\"64\" rx=\"12\" fill=\"#0D6EFD\"/>" +
            "<text x=\"32\" y=\"47\" font-family=\"Arial,Helvetica,sans-serif\" " +
            "font-weight=\"900\" font-size=\"44\" text-anchor=\"middle\" " +
            "fill=\"#FFFFFF\">H</text></svg>";

        // Cookie names - identical to the 60.HTML host so behavior matches exactly.
        public const string CS_HELPDESK_SESSION = "helpdesk_session";
        // Theme cookie: 'light' | 'dark' | 'system' (default).
        public const string CS_HELPDESK_THEME_COOKIE = "helpdesk_theme";

        private readonly THelpdeskDBPool FDB;
        private readonly THelpdeskSessionStore FSessions;
        private readonly THelpdeskPages FPages;
        private readonly THelpdeskAttachStore FAttachStore;
        private readonly int FListenPort;
        private readonly DateTime FStartedAt;
        // Kanban live sync: the adapter hub, which keeps the upgrade cookies of
        // every board WebSocket (the Kestrel counterpart of FWSSessionTokens).
        private readonly ISgcHtmlHub FHub;

        // Builds the DB (schema + admin seed + demo data), the session store, the
        // page builder and the attachment store - mirroring TsgcHelpdeskServer.Start
        // / InitRuntime.
        public HelpdeskWebHost(THelpdeskServerConfig aConfig, string aDatabasePath,
            string aStorageRoot, int aListenPort, ISgcHtmlHub aHub)
        {
            FHub = aHub;
            FListenPort = aListenPort;
            FStartedAt = DateTime.Now;

            FDB = new THelpdeskDBPool(aDatabasePath);
            FDB.EnsureSchema();
            FDB.SeedAdmin(aConfig.AdminUser, Bcrypt.BcryptHash(aConfig.AdminPassword));
            // Populate demo users (alice/bob/carol) + tickets the first time the DB
            // is empty. Idempotent (no-op once tickets exist).
            FDB.SeedDemoData();

            FSessions = new THelpdeskSessionStore(480, true);
            FPages = new THelpdeskPages();
            // the page WebSocket (Program.cs) carries the live board changes
            FPages.LiveSync = true;
            FAttachStore = new THelpdeskAttachStore(aStorageRoot);
        }

        public void Dispose()
        {
            if (FDB != null)
            {
                try { FDB.Dispose(); } catch { }
            }
        }

        public int ListenPort { get { return FListenPort; } }

        // ----- request param helpers (query + form merged) ----- //

        // First value for aName: query wins over form, matching the 60.HTML GetParam
        // merge. aForm is null for GET / non-form requests.
        private static string GetParam(HttpContext aCtx, IFormCollection aForm, string aName)
        {
            StringValues vValues;
            if (aCtx.Request.Query.TryGetValue(aName, out vValues) && vValues.Count > 0)
                return vValues[0] ?? "";
            if (aForm != null && aForm.TryGetValue(aName, out vValues) && vValues.Count > 0)
                return vValues[0] ?? "";
            return "";
        }

        // Route value (e.g. the {id} / {attId} segments) as a string.
        private static string RouteStr(HttpContext aCtx, string aName)
        {
            object vObj;
            if (aCtx.Request.RouteValues.TryGetValue(aName, out vObj) && vObj != null)
                return vObj.ToString();
            return "";
        }

        private static long StrToInt64Def(string aValue, long aDefault)
        {
            long vResult;
            if (long.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        // Materialize the IFormFile parts posted under aFieldName into the reused
        // attachment-store input type. Replaces the 60.HTML hand-rolled multipart
        // parser (THelpdeskMultipart.Files).
        private static THelpdeskUploadFile[] CollectUploads(IFormCollection aForm,
            string aFieldName)
        {
            List<THelpdeskUploadFile> vResult = new List<THelpdeskUploadFile>();
            if (aForm == null || aForm.Files == null)
                return vResult.ToArray();
            foreach (IFormFile vFile in aForm.Files.GetFiles(aFieldName))
            {
                if (vFile == null)
                    continue;
                byte[] vBytes;
                using (MemoryStream vMs = new MemoryStream())
                {
                    vFile.CopyTo(vMs);
                    vBytes = vMs.ToArray();
                }
                vResult.Add(new THelpdeskUploadFile
                {
                    FileName = vFile.FileName ?? "",
                    ContentType = vFile.ContentType ?? "",
                    FileBytes = vBytes
                });
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

        private void WriteSessionCookie(HttpContext aCtx, string aToken)
        {
            if (string.IsNullOrEmpty(aToken))
                return;
            aCtx.Response.Cookies.Append(CS_HELPDESK_SESSION, aToken, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private void ClearSessionCookie(HttpContext aCtx)
        {
            aCtx.Response.Cookies.Delete(CS_HELPDESK_SESSION, new CookieOptions
            {
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }

        private static string ReadThemeCookie(HttpContext aCtx)
        {
            string vS = ReadCookie(aCtx, CS_HELPDESK_THEME_COOKIE).ToLowerInvariant();
            if (vS == "light" || vS == "dark" || vS == "system")
                return vS;
            return "system";
        }

        private void WriteThemeCookie(HttpContext aCtx, string aTheme)
        {
            string vS = (aTheme ?? "").Trim().ToLowerInvariant();
            if (vS != "light" && vS != "dark" && vS != "system")
                vS = "system";
            aCtx.Response.Cookies.Append(CS_HELPDESK_THEME_COOKIE, vS, new CookieOptions
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
        public bool CurrentSession(HttpContext aCtx, out THelpdeskSession aSession)
        {
            aSession = null;
            string vToken = ReadCookie(aCtx, CS_HELPDESK_SESSION);
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

        // ----- auth gate (used by Program.cs endpoint mapping) ----- //

        // Session-protected page endpoint: redirect to /login when signed out.
        // (The 60.HTML DispatchRequest applies a single logged-in gate, then each
        // handler applies its own admin / ownership checks.)
        public IResult Guarded(HttpContext aCtx, Func<THelpdeskSession, IResult> aFn)
        {
            THelpdeskSession vSession;
            if (!CurrentSession(aCtx, out vSession))
                return Redirect("/login");
            return aFn(vSession);
        }

        // ----- theme (public) ----- //

        // POST /theme: set the theme cookie from form field 'theme', then 302 back.
        public IResult SetTheme(HttpContext aCtx, IFormCollection aForm)
        {
            WriteThemeCookie(aCtx, GetParam(aCtx, aForm, "theme"));
            return Redirect(RefererOrRoot(aCtx));
        }

        // ----- login / register / logout (public) ----- //

        public IResult LoginGet(HttpContext aCtx)
        {
            THelpdeskSession vSession;
            if (CurrentSession(aCtx, out vSession))
                return Redirect("/");
            // Pre-fill admin/admin while only the seeded admin account exists.
            string vDefaultUser = "";
            string vDefaultPwd = "";
            if (!FDB.HasAnyNonAdminUsers())
            {
                vDefaultUser = "admin";
                vDefaultPwd = "admin";
            }
            return Html(200, FPages.BuildLoginPage(ReadThemeCookie(aCtx), "",
                vDefaultUser, vDefaultPwd));
        }

        public IResult LoginPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vUser = GetParam(aCtx, aForm, "username").Trim();
            string vPwd = GetParam(aCtx, aForm, "password");

            if (vUser == "" || vPwd == "")
                return Html(400, FPages.BuildLoginPage(vTheme,
                    "Please enter your username and password."));

            THelpdeskUser vDBUser;
            if (!FDB.AuthenticateUser(vUser, vPwd, out vDBUser))
                return Html(401, FPages.BuildLoginPage(vTheme,
                    "Invalid username or password."));

            string vToken = FSessions.CreateSession(vDBUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            return Redirect("/");
        }

        public IResult RegisterGet(HttpContext aCtx)
        {
            THelpdeskSession vSession;
            if (CurrentSession(aCtx, out vSession))
                return Redirect("/");
            return Html(200, FPages.BuildRegisterPage(ReadThemeCookie(aCtx), ""));
        }

        public IResult RegisterPost(HttpContext aCtx, IFormCollection aForm)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vUser = GetParam(aCtx, aForm, "username").Trim();
            string vPwd = GetParam(aCtx, aForm, "password");
            string vPwd2 = GetParam(aCtx, aForm, "password_confirm");

            if (vUser == "" || vPwd == "")
                return Html(400, FPages.BuildRegisterPage(vTheme,
                    "Please enter a username and password."));
            if (vUser.Length < 3)
                return Html(400, FPages.BuildRegisterPage(vTheme,
                    "Username must be at least 3 characters."));
            if (vPwd.Length < 6)
                return Html(400, FPages.BuildRegisterPage(vTheme,
                    "Password must be at least 6 characters."));
            if (vPwd != vPwd2)
                return Html(400, FPages.BuildRegisterPage(vTheme,
                    "Passwords do not match."));
            if (FDB.UsernameExists(vUser))
                return Html(400, FPages.BuildRegisterPage(vTheme,
                    "That username is already taken."));

            long vNewId = FDB.RegisterUser(vUser, Bcrypt.BcryptHash(vPwd));
            THelpdeskUser vDBUser;
            if (vNewId <= 0 || !FDB.GetUserById(vNewId, out vDBUser))
                return Html(500, FPages.BuildRegisterPage(vTheme,
                    "Registration failed. Please try a different username."));

            string vToken = FSessions.CreateSession(vDBUser, ClientIPOf(aCtx));
            WriteSessionCookie(aCtx, vToken);
            return Redirect("/");
        }

        public IResult Logout(HttpContext aCtx)
        {
            string vToken = ReadCookie(aCtx, CS_HELPDESK_SESSION);
            if (vToken != "")
                FSessions.Destroy_(vToken);
            ClearSessionCookie(aCtx);
            return Redirect("/login");
        }

        // ----- ticket list (root) ----- //

        private void ParseTicketFilterParams(HttpContext aCtx, IFormCollection aForm,
            out string aStatus, out string aUser, out string aSearch, out string aSort,
            out string aDir)
        {
            aStatus = GetParam(aCtx, aForm, "status").Trim();
            aUser = GetParam(aCtx, aForm, "user").Trim();
            aSearch = GetParam(aCtx, aForm, "search").Trim();
            aSort = GetParam(aCtx, aForm, "sort").Trim().ToLowerInvariant();
            aDir = GetParam(aCtx, aForm, "dir").Trim().ToLowerInvariant();
            if (aSort == "")
                aSort = "updated";
            if (aDir != "asc" && aDir != "desc")
                aDir = "desc";
        }

        public IResult TicketListGet(HttpContext aCtx, THelpdeskSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            string vStatus, vUserFilter, vSearch, vSort, vDir;
            ParseTicketFilterParams(aCtx, null, out vStatus, out vUserFilter, out vSearch,
                out vSort, out vDir);

            if (string.Equals(aSession.Role, "admin", StringComparison.OrdinalIgnoreCase))
            {
                THelpdeskTicket[] vRows = FDB.ListAllTickets(vStatus, vUserFilter, vSearch,
                    vSort, vDir);
                THelpdeskUser[] vUsers = FDB.ListUsersWithRole("user");
                return Html(200, FPages.BuildTicketListPage(vRows, "admin",
                    aSession.Username, vTheme, vStatus, vUsers, vUserFilter, vSearch,
                    vSort, vDir));
            }
            else
            {
                THelpdeskTicket[] vRows = FDB.ListTicketsForUser(aSession.UserId, vSearch,
                    vSort, vDir);
                return Html(200, FPages.BuildTicketListPage(vRows, "user",
                    aSession.Username, vTheme, "", null, "", vSearch, vSort, vDir));
            }
        }

        // ----- create-ticket form (user role only) ----- //

        public IResult TicketNewGet(HttpContext aCtx, THelpdeskSession aSession)
        {
            if (string.Equals(aSession.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return Redirect("/");
            return Html(200, FPages.BuildTicketNewPage(aSession.Username,
                ReadThemeCookie(aCtx), ""));
        }

        public IResult TicketNewPost(HttpContext aCtx, IFormCollection aForm,
            THelpdeskSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            if (string.Equals(aSession.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return Redirect("/");

            string vSubject = GetParam(aCtx, aForm, "subject").Trim();
            string vMessage = GetParam(aCtx, aForm, "message");
            THelpdeskUploadFile[] vFiles = CollectUploads(aForm, "attachments");

            if (vSubject == "" || vMessage.Trim() == "")
                return Html(400, FPages.BuildTicketNewPage(aSession.Username, vTheme,
                    "Please fill in both the subject and a description."));

            string vAttachError;
            if (vFiles.Length > 0 && !FAttachStore.ValidateFiles(vFiles, out vAttachError))
                return Html(400, FPages.BuildTicketNewPage(aSession.Username, vTheme,
                    vAttachError));

            long vMessageId;
            long vId = FDB.CreateTicket(aSession.UserId, vSubject, vMessage, out vMessageId);
            if (vId <= 0)
                return Html(500, FPages.BuildTicketNewPage(aSession.Username, vTheme,
                    "Could not create the ticket. Please try again."));

            if (vFiles.Length > 0 && vMessageId > 0)
            {
                THelpdeskAttachSaved[] vSaved = FAttachStore.SaveFiles(vId, vFiles,
                    out vAttachError);
                for (int vI = 0; vI < vSaved.Length; vI++)
                    FDB.AddAttachment(vMessageId, vId, vSaved[vI].OriginalName,
                        vSaved[vI].StoredName, vSaved[vI].ContentType, vSaved[vI].SizeBytes);
            }

            return Redirect("/tickets/" + vId.ToString(CultureInfo.InvariantCulture) +
                "?flash=created");
        }

        // ----- admin-only CSV export ----- //

        private static string CsvField(string aValue)
        {
            if (aValue.IndexOf(',') >= 0 || aValue.IndexOf('"') >= 0 ||
                aValue.IndexOf('\r') >= 0 || aValue.IndexOf('\n') >= 0)
                return "\"" + aValue.Replace("\"", "\"\"") + "\"";
            return aValue;
        }

        private static string FmtCsvDate(DateTime aValue)
        {
            if (aValue == DateTime.MinValue)
                return "-";
            return aValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public IResult TicketsExportCsvGet(HttpContext aCtx, THelpdeskSession aSession)
        {
            if (!string.Equals(aSession.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return Redirect("/");

            string vStatus, vUserFilter, vSearch, vSort, vDir;
            ParseTicketFilterParams(aCtx, null, out vStatus, out vUserFilter, out vSearch,
                out vSort, out vDir);
            THelpdeskTicket[] vRows = FDB.ListAllTickets(vStatus, vUserFilter, vSearch,
                vSort, vDir);

            StringBuilder vCsv = new StringBuilder();
            vCsv.Append("Id,Subject,Owner,Status,Created,Updated").Append("\r\n");
            for (int vI = 0; vI < vRows.Length; vI++)
            {
                vCsv.Append(CsvField(vRows[vI].Id.ToString(CultureInfo.InvariantCulture))).Append(',');
                vCsv.Append(CsvField(vRows[vI].Subject)).Append(',');
                vCsv.Append(CsvField(vRows[vI].Username)).Append(',');
                vCsv.Append(CsvField(vRows[vI].Status)).Append(',');
                vCsv.Append(CsvField(FmtCsvDate(vRows[vI].CreatedAt))).Append(',');
                vCsv.Append(CsvField(FmtCsvDate(vRows[vI].UpdatedAt))).Append("\r\n");
            }

            // text/csv attachment named tickets.csv (Results.File sets
            // Content-Disposition: attachment; filename=tickets.csv).
            return Results.File(Encoding.UTF8.GetBytes(vCsv.ToString()),
                "text/csv; charset=utf-8", "tickets.csv");
        }

        // ----- ticket detail + actions (IDOR-guarded) ----- //

        private THelpdeskMessage[] LoadMessagesWithAttachments(long aTicketId)
        {
            THelpdeskMessage[] vResult = FDB.ListMessages(aTicketId);
            for (int vI = 0; vI < vResult.Length; vI++)
                vResult[vI].Attachments = FDB.ListAttachments(vResult[vI].Id);
            return vResult;
        }

        public IResult TicketDetailGet(HttpContext aCtx, THelpdeskSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            long vTicketId = StrToInt64Def(RouteStr(aCtx, "id"), 0);
            if (vTicketId <= 0)
                return Redirect("/");

            THelpdeskTicket vTicket;
            if (!FDB.GetTicket(vTicketId, out vTicket))
                return Redirect("/");

            bool vIsAdmin = string.Equals(aSession.Role, "admin",
                StringComparison.OrdinalIgnoreCase);
            bool vIsOwner = vTicket.UserId == aSession.UserId;
            if (!vIsAdmin && !vIsOwner)
                return Redirect("/"); // IDOR: not the owner and not staff.

            string vFlash = GetParam(aCtx, null, "flash");
            THelpdeskMessage[] vMessages = LoadMessagesWithAttachments(vTicketId);
            return Html(200, FPages.BuildTicketDetailPage(vTicket, vMessages,
                aSession.Role, aSession.Username, vTheme, vIsAdmin || vIsOwner,
                vIsAdmin || vIsOwner, "", vFlash));
        }

        public IResult TicketReplyPost(HttpContext aCtx, IFormCollection aForm,
            THelpdeskSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            long vTicketId = StrToInt64Def(RouteStr(aCtx, "id"), 0);
            if (vTicketId <= 0)
                return Redirect("/");

            THelpdeskTicket vTicket;
            if (!FDB.GetTicket(vTicketId, out vTicket))
                return Redirect("/");

            bool vIsAdmin = string.Equals(aSession.Role, "admin",
                StringComparison.OrdinalIgnoreCase);
            bool vIsOwner = vTicket.UserId == aSession.UserId;
            if (!vIsAdmin && !vIsOwner)
                return Redirect("/"); // IDOR: not the owner and not staff.

            // Replies are allowed on any non-closed ticket (new / pending_* / etc).
            if (string.Equals(vTicket.Status, "closed", StringComparison.OrdinalIgnoreCase))
                return Redirect("/tickets/" + vTicketId.ToString(CultureInfo.InvariantCulture));

            string vBody = GetParam(aCtx, aForm, "body").Trim();
            THelpdeskUploadFile[] vFiles = CollectUploads(aForm, "attachments");

            string vAttachError;
            if (vFiles.Length > 0 && !FAttachStore.ValidateFiles(vFiles, out vAttachError))
            {
                THelpdeskMessage[] vMsgs = LoadMessagesWithAttachments(vTicket.Id);
                return Html(400, FPages.BuildTicketDetailPage(vTicket, vMsgs,
                    aSession.Role, aSession.Username, vTheme, true, vIsAdmin || vIsOwner,
                    vAttachError));
            }

            if (vBody == "")
                return Redirect("/tickets/" + vTicketId.ToString(CultureInfo.InvariantCulture));

            long vMessageId = FDB.AddMessage(vTicketId, aSession.UserId, vIsAdmin, vBody);

            if (vFiles.Length > 0 && vMessageId > 0)
            {
                THelpdeskAttachSaved[] vSaved = FAttachStore.SaveFiles(vTicketId, vFiles,
                    out vAttachError);
                for (int vI = 0; vI < vSaved.Length; vI++)
                    FDB.AddAttachment(vMessageId, vTicketId, vSaved[vI].OriginalName,
                        vSaved[vI].StoredName, vSaved[vI].ContentType, vSaved[vI].SizeBytes);
            }

            return Redirect("/tickets/" + vTicketId.ToString(CultureInfo.InvariantCulture) +
                "?flash=replied");
        }

        // Shared close / reopen handler (aNewStatus + aFlashCode differ).
        public IResult TicketStatusPost(HttpContext aCtx, THelpdeskSession aSession,
            string aNewStatus, string aFlashCode)
        {
            long vTicketId = StrToInt64Def(RouteStr(aCtx, "id"), 0);
            if (vTicketId <= 0)
                return Redirect("/");

            THelpdeskTicket vTicket;
            if (!FDB.GetTicket(vTicketId, out vTicket))
                return Redirect("/");

            bool vIsAdmin = string.Equals(aSession.Role, "admin",
                StringComparison.OrdinalIgnoreCase);
            bool vIsOwner = vTicket.UserId == aSession.UserId;
            if (!vIsAdmin && !vIsOwner)
                return Redirect("/"); // IDOR: not the owner and not staff.

            FDB.SetTicketStatus(vTicketId, aNewStatus);
            return Redirect("/tickets/" + vTicketId.ToString(CultureInfo.InvariantCulture) +
                "?flash=" + aFlashCode);
        }

        // ----- Kanban board (admin) ----- //

        private static bool IsAdmin(THelpdeskSession aSession)
        {
            return string.Equals(aSession.Role, "admin", StringComparison.OrdinalIgnoreCase);
        }

        // POST /tickets/kanban-move: admin drag-drop, sets a ticket's status from id +
        // status in the body (and the priority from the swimlane).
        public IResult TicketKanbanMovePost(HttpContext aCtx, IFormCollection aForm,
            THelpdeskSession aSession)
        {
            // Admin only: the Kanban board is shown on the admin ticket list.
            if (!IsAdmin(aSession))
                return Html(403, "forbidden");

            // the board posts the card element id ("tk" + ticket id)
            long vTicketId;
            if (!THelpdeskPages.KanbanTicketId(GetParam(aCtx, aForm, "id"), out vTicketId))
                return Html(400, "invalid id");

            string vStatus = GetParam(aCtx, aForm, "status").Trim().ToLowerInvariant();
            // with swimlanes the drop also posts the destination lane (= priority)
            string vLane = GetParam(aCtx, aForm, "swimlane").Trim().ToLowerInvariant();
            THelpdeskTicket oTicket;
            if (!FDB.GetTicket(vTicketId, out oTicket))
                return Html(404, "not found");

            // The board blocks moves outside the column AllowedTargets in the browser,
            // but that is a convenience only: every drop is validated here too. A non
            // 2xx answer makes the board reload, putting the card back.
            if (!THelpdeskPages.KanbanMoveAllowed(oTicket.Status, vStatus))
                return Html(409, "move not allowed");
            if (vLane != "" && THelpdeskDBPool.NormalizePriority(vLane) != vLane)
                return Html(400, "invalid swimlane");

            if (!string.Equals(oTicket.Status, vStatus, StringComparison.OrdinalIgnoreCase))
                FDB.SetTicketStatus(vTicketId, vStatus);
            if (vLane != "" && vLane != oTicket.Priority)
                FDB.SetTicketPriority(vTicketId, vLane);
            // move the card on every other open board
            PushKanbanFragment(BuildKanbanFragment(vTicketId, true));
            return Html(200, "ok");
        }

        // POST /tickets/kanban-add: admin quick add (QuickAddURL), creates a ticket
        // from title + column (+ swimlane = priority) and answers the card fragment.
        public IResult TicketKanbanAddPost(HttpContext aCtx, IFormCollection aForm,
            THelpdeskSession aSession)
        {
            if (!IsAdmin(aSession))
                return Html(403, "forbidden");
            string vTitle = GetParam(aCtx, aForm, "title").Trim();
            string vColumn = GetParam(aCtx, aForm, "column").Trim().ToLowerInvariant();
            string vLane = GetParam(aCtx, aForm, "swimlane").Trim().ToLowerInvariant();
            if (vTitle == "" || vTitle.Length > 200)
                return Html(400, "invalid title");
            // a ticket is closed only from Pending Feedback, never created closed
            if (!THelpdeskPages.KanbanColumnValid(vColumn) || vColumn == "closed")
                return Html(409, "column not allowed");
            if (vLane != "" && THelpdeskDBPool.NormalizePriority(vLane) != vLane)
                return Html(400, "invalid swimlane");

            long vMessageId;
            long vTicketId = FDB.CreateTicket(aSession.UserId, vTitle, "", out vMessageId);
            if (vTicketId <= 0)
                return Html(500, "not created");
            if (vColumn != "new")
                FDB.SetTicketStatus(vTicketId, vColumn);
            if (vLane != "")
                FDB.SetTicketPriority(vTicketId, vLane);

            // The move fragment (remove + insert) is idempotent, so the browser that
            // added the card can apply both this answer and the live push.
            string vFragment = BuildKanbanFragment(vTicketId, true);
            PushKanbanFragment(vFragment);
            return Html(200, vFragment);
        }

        // GET /tickets/kanban-edit: admin edit dialog (EditURL) form.
        public IResult TicketKanbanEditGet(HttpContext aCtx, THelpdeskSession aSession)
        {
            if (!IsAdmin(aSession))
                return Html(403, "forbidden");
            long vTicketId;
            THelpdeskTicket oTicket = null;
            if (!THelpdeskPages.KanbanTicketId(GetParam(aCtx, null, "id"), out vTicketId) ||
                !FDB.GetTicket(vTicketId, out oTicket))
                return Html(404, "not found");
            return Html(200, FPages.BuildKanbanEditForm(oTicket, false));
        }

        // POST /tickets/kanban-edit: saves the dialog and answers the form plus the
        // card fragment.
        public IResult TicketKanbanEditPost(HttpContext aCtx, IFormCollection aForm,
            THelpdeskSession aSession)
        {
            if (!IsAdmin(aSession))
                return Html(403, "forbidden");
            long vTicketId;
            THelpdeskTicket oTicket = null;
            if (!THelpdeskPages.KanbanTicketId(GetParam(aCtx, aForm, "id"), out vTicketId) ||
                !FDB.GetTicket(vTicketId, out oTicket))
                return Html(404, "not found");
            string vSubject = GetParam(aCtx, aForm, "subject").Trim();
            if (vSubject == "" || vSubject.Length > 200)
            {
                // answered 200 so htmx swaps the form back in with the error
                return Html(200, FPages.BuildKanbanEditForm(oTicket, false,
                    "The subject is required (200 characters max)."));
            }

            FDB.UpdateTicketDetails(vTicketId, vSubject, GetParam(aCtx, aForm, "priority"),
                GetParam(aCtx, aForm, "category"));
            THelpdeskTicket oSaved;
            if (!FDB.GetTicket(vTicketId, out oSaved))
                oSaved = oTicket;
            // a new priority means a new swimlane: move the card, else re-render it
            string vFragment = BuildKanbanFragment(vTicketId, oSaved.Priority != oTicket.Priority);
            PushKanbanFragment(vFragment);
            return Html(200, FPages.BuildKanbanEditForm(oSaved, true) + vFragment);
        }

        // htmx out-of-band fragment of a ticket card, built from the same board the
        // list page renders: aMove = remove + insert into the ticket's column,
        // otherwise an in-place re-render.
        private string BuildKanbanFragment(long aTicketId, bool aMove)
        {
            THelpdeskTicket oTicket;
            if (!FDB.GetTicket(aTicketId, out oTicket))
                return "";
            // the same board (columns, lanes, card mapping) the list page renders
            TsgcHTMLComponent_KanbanBoard oBoard = FPages.CreateTicketBoard(FDB.ListAllTickets(""));
            if (aMove)
            {
                string vColumn = oTicket.Status.ToLowerInvariant();
                if (!THelpdeskPages.KanbanColumnValid(vColumn))
                    vColumn = "new";
                return oBoard.GetCardMoveFragmentHTML(THelpdeskPages.KanbanCardID(aTicketId),
                    vColumn);
            }
            return oBoard.GetCardFragmentHTML(THelpdeskPages.KanbanCardID(aTicketId));
        }

        // Sends a card fragment to every board open by an admin over the WebSocket
        // (the sgcHTMX bridge applies it): ticket data never reaches anybody else.
        // The session is checked at push time from the cookie of the upgrade
        // request: a signed-out or non-admin browser gets nothing. The hub bounds
        // every send (SendTimeout) and drops a client that stopped reading.
        private void PushKanbanFragment(string aHTML)
        {
            if (string.IsNullOrEmpty(aHTML))
                return;
            try
            {
                FHub.BroadcastAsync(IsAdminConnection, aHTML).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // ignore: a connection is closing concurrently
            }
        }

        private bool IsAdminConnection(SgcHtmlConnection aConnection)
        {
            string vToken;
            THelpdeskSession oSession;
            return aConnection.Cookies.TryGetValue(CS_HELPDESK_SESSION, out vToken) &&
                vToken != "" && FSessions.TryGet(vToken, out oSession) && IsAdmin(oSession);
        }

        // ----- attachment download (IDOR-guarded) ----- //

        private static string SanitizeHeaderFilename(string aName)
        {
            StringBuilder vResult = new StringBuilder();
            for (int vI = 0; vI < (aName ?? "").Length; vI++)
            {
                char vCh = aName[vI];
                if (vCh == '"' || vCh == '\\' || vCh < 32)
                    continue;
                vResult.Append(vCh);
            }
            if (vResult.Length == 0)
                return "file";
            return vResult.ToString();
        }

        public IResult TicketFileGet(HttpContext aCtx, THelpdeskSession aSession)
        {
            long vTicketId = StrToInt64Def(RouteStr(aCtx, "id"), 0);
            long vAttachmentId = StrToInt64Def(RouteStr(aCtx, "attId"), 0);
            if (vTicketId <= 0 || vAttachmentId <= 0)
                return Redirect("/");

            THelpdeskTicket vTicket;
            if (!FDB.GetTicket(vTicketId, out vTicket))
                return Redirect("/");

            bool vIsAdmin = string.Equals(aSession.Role, "admin",
                StringComparison.OrdinalIgnoreCase);
            bool vIsOwner = vTicket.UserId == aSession.UserId;
            if (!vIsAdmin && !vIsOwner)
                return Redirect("/"); // IDOR: not the owner and not staff.

            // Defense in depth (IDOR): the attachment must belong to THIS ticket.
            THelpdeskAttachment vAttachment;
            if (!FDB.GetAttachment(vAttachmentId, out vAttachment) ||
                vAttachment.TicketId != vTicketId)
                return Results.StatusCode(404);

            string vPath = FAttachStore.ResolvePath(vTicketId, vAttachment.StoredFilename);
            if (vPath == "")
                return Results.StatusCode(404);

            byte[] vBytes;
            try
            {
                vBytes = File.ReadAllBytes(vPath);
            }
            catch
            {
                return Results.StatusCode(404);
            }

            string vContentType = vAttachment.ContentType != ""
                ? vAttachment.ContentType : "application/octet-stream";
            // Results.File emits Content-Disposition: attachment; filename="...".
            return Results.File(vBytes, vContentType,
                SanitizeHeaderFilename(vAttachment.OriginalFilename));
        }

        // ----- admin-only stats dashboard ----- //

        public IResult DashboardGet(HttpContext aCtx, THelpdeskSession aSession)
        {
            string vTheme = ReadThemeCookie(aCtx);
            if (!string.Equals(aSession.Role, "admin", StringComparison.OrdinalIgnoreCase))
                return Redirect("/");

            string vRange = GetParam(aCtx, null, "range").Trim().ToLowerInvariant();
            if (vRange != "today" && vRange != "yesterday" && vRange != "week" &&
                vRange != "month" && vRange != "3months" && vRange != "6months" &&
                vRange != "year")
                vRange = "month";

            DateTime vToday = DateTime.Today;
            DateTime vNow = DateTime.Now;

            int vOpenedToday = FDB.CountTicketsCreatedBetween(vToday, vToday.AddDays(1));
            int vOpenedYesterday = FDB.CountTicketsCreatedBetween(vToday.AddDays(-1), vToday);
            int vClosedToday = FDB.CountTicketsClosedBetween(vToday, vToday.AddDays(1));
            DateTime vStartOfMonth = new DateTime(vNow.Year, vNow.Month, 1);
            int vCreatedMonth = FDB.CountTicketsCreatedBetween(vStartOfMonth,
                vStartOfMonth.AddMonths(1));
            DateTime vStartOfYear = new DateTime(vNow.Year, 1, 1);
            int vCreatedYear = FDB.CountTicketsCreatedBetween(vStartOfYear,
                vStartOfYear.AddYears(1));

            DateTime vFrom;
            DateTime vTo;
            string vBucket;
            if (vRange == "today")
            {
                vFrom = vToday;
                vTo = vToday.AddDays(1);
                vBucket = "hour4";
            }
            else if (vRange == "yesterday")
            {
                vFrom = vToday.AddDays(-1);
                vTo = vToday;
                vBucket = "hour4";
            }
            else if (vRange == "week")
            {
                vFrom = vToday.AddDays(-6);
                vTo = vToday.AddDays(1);
                vBucket = "day";
            }
            else if (vRange == "3months")
            {
                vFrom = vToday.AddMonths(-3);
                vTo = vToday.AddDays(1);
                vBucket = "week";
            }
            else if (vRange == "6months")
            {
                vFrom = vToday.AddMonths(-6);
                vTo = vToday.AddDays(1);
                vBucket = "month";
            }
            else if (vRange == "year")
            {
                vFrom = vToday.AddMonths(-12);
                vTo = vToday.AddDays(1);
                vBucket = "month";
            }
            else // 'month' (default)
            {
                vFrom = vToday.AddMonths(-1);
                vTo = vToday.AddDays(1);
                vBucket = "week";
            }

            THelpdeskActivityPoint[] vSeries = FDB.GetTicketActivitySeries(vFrom, vTo, vBucket);

            return Html(200, FPages.BuildDashboardPage(aSession.Username, vTheme,
                vOpenedToday, vOpenedYesterday, vClosedToday, vCreatedMonth, vCreatedYear,
                vRange, vSeries));
        }
    }
}
