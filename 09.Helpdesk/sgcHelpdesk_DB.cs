// ***************************************************************************
//  sgcHelpdesk - support-ticket helpdesk web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\09.Helpdesk\sgcHelpdesk_DB.pas
//
//  FireDAC (TFDConnection / FDManager / TFDQuery) is replaced by
//  Microsoft.Data.Sqlite. The pool builds a connection string once from the
//  absolute DB file path; Microsoft.Data.Sqlite pools the underlying
//  connections automatically by connection string. Acquire() returns an open
//  SqliteConnection the caller disposes (which returns it to the pool).
//
//  Timestamp columns are stored as 'yyyy-MM-ddTHH:mm:ss', matching the Delphi
//  FormatDateTime mask, so the lexical string comparisons the dashboard queries
//  rely on (created_at >= :f AND created_at < :t) order correctly.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Helpdesk
{
    public class EHelpdeskDBError : Exception
    {
        public EHelpdeskDBError(string message) : base(message) { }
    }

    public class THelpdeskDBPool : IDisposable
    {
        private const string CS_TS_FMT = "yyyy-MM-ddTHH:mm:ss";

        private readonly string FDatabaseFile;
        private readonly string FConnStr;

        // aDatabaseFile is resolved to an absolute path internally.
        public THelpdeskDBPool(string aDatabaseFile)
        {
            string vFile = aDatabaseFile;
            if (string.IsNullOrEmpty(vFile))
                vFile = Path.Combine("data", "helpdesk.db");
            // Resolve relative paths against the current directory (the EXE dir,
            // which the launcher sets via SetCurrentDirectory).
            if (!Path.IsPathRooted(vFile))
                vFile = Path.Combine(Directory.GetCurrentDirectory(), vFile);
            FDatabaseFile = vFile;
            EnsureDatabaseDir();

            FConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = FDatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }

        public void Dispose()
        {
            // No FDManager connection definition to unregister.
        }

        public string DatabaseFile
        {
            get { return FDatabaseFile; }
        }

        private void EnsureDatabaseDir()
        {
            string vDir = Path.GetDirectoryName(FDatabaseFile);
            if (!string.IsNullOrEmpty(vDir) && !Directory.Exists(vDir))
                Directory.CreateDirectory(vDir);
        }

        // Acquire a pooled connection. Caller MUST Dispose the returned object
        // (via 'using'), which returns the underlying connection to the pool.
        public SqliteConnection Acquire()
        {
            SqliteConnection oConn = new SqliteConnection(FConnStr);
            try
            {
                oConn.Open();
                return oConn;
            }
            catch
            {
                oConn.Dispose();
                throw;
            }
        }

        // ----- timestamp / formatting helpers ----- //

        private static string NowTimestamp()
        {
            return DateTime.Now.ToString(CS_TS_FMT, CultureInfo.InvariantCulture);
        }

        private static string FormatHelpdeskTimestamp(DateTime aValue)
        {
            return aValue.ToString(CS_TS_FMT, CultureInfo.InvariantCulture);
        }

        // Parse a 'yyyy-MM-ddTHH:mm:ss' timestamp. Returns MinValue for blank /
        // unparsable values (rendered by the pages as a "-" placeholder).
        private static DateTime ParseHelpdeskTimestamp(string aValue)
        {
            string vText = (aValue ?? "").Trim();
            if (vText.Length < 19)
                return DateTime.MinValue;
            DateTime vResult;
            if (DateTime.TryParseExact(vText.Substring(0, 19), CS_TS_FMT,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out vResult))
                return vResult;
            return DateTime.MinValue;
        }

        // Coerce an arbitrary status string to one of the two allowed values.
        private static string NormalizeStatus(string aStatus)
        {
            if (string.Equals((aStatus ?? "").Trim(), "closed",
                StringComparison.OrdinalIgnoreCase))
                return "closed";
            return "open";
        }

        // Escape LIKE metacharacters ('\', '%', '_') before wrapping in '%...%'.
        // Paired with an explicit ESCAPE '\' clause at every call site.
        private static string EscapeLikeValue(string aValue)
        {
            string vResult = (aValue ?? "").Replace("\\", "\\\\");
            vResult = vResult.Replace("%", "\\%");
            vResult = vResult.Replace("_", "\\_");
            return vResult;
        }

        // Whitelist aSort against the columns the ticket list / CSV export can
        // sort by; anything else falls back to 'updated'. aAllowOwner is false
        // for ListTicketsForUser, which has no owner column.
        private static string SortColumnSQL(string aSort, bool aAllowOwner)
        {
            if (string.Equals(aSort, "subject", StringComparison.OrdinalIgnoreCase))
                return "t.subject";
            if (aAllowOwner && string.Equals(aSort, "owner", StringComparison.OrdinalIgnoreCase))
                return "u.username";
            if (string.Equals(aSort, "status", StringComparison.OrdinalIgnoreCase))
                return "t.status";
            if (string.Equals(aSort, "created", StringComparison.OrdinalIgnoreCase))
                return "t.created_at";
            return "t.updated_at";
        }

        // Whitelist aDir to ASC/DESC; anything else defaults to DESC.
        private static string DirSQL(string aDir)
        {
            if (string.Equals((aDir ?? "").Trim(), "asc", StringComparison.OrdinalIgnoreCase))
                return "ASC";
            return "DESC";
        }

        // ----- low-level command helpers ----- //

        private static SqliteCommand NewCmd(SqliteConnection aConn, string aSQL)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.CommandText = aSQL;
            return oCmd;
        }

        private static void AddParam(SqliteCommand aCmd, string aName, object aValue)
        {
            aCmd.Parameters.AddWithValue(aName, aValue ?? DBNull.Value);
        }

        private static long LastInsertRowId(SqliteConnection aConn, SqliteTransaction aTx)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, "SELECT last_insert_rowid()"))
            {
                oCmd.Transaction = aTx;
                object vObj = oCmd.ExecuteScalar();
                return vObj == null || vObj == DBNull.Value
                    ? 0 : Convert.ToInt64(vObj, CultureInfo.InvariantCulture);
            }
        }

        private static long ToLong(object aValue)
        {
            if (aValue == null || aValue == DBNull.Value)
                return 0;
            return Convert.ToInt64(aValue, CultureInfo.InvariantCulture);
        }

        private static string ToStr(object aValue)
        {
            if (aValue == null || aValue == DBNull.Value)
                return "";
            return Convert.ToString(aValue, CultureInfo.InvariantCulture) ?? "";
        }

        // ----- row fillers ----- //

        private static THelpdeskUser ReadUser(SqliteDataReader aR)
        {
            return new THelpdeskUser
            {
                Id = ToLong(aR["id"]),
                Username = ToStr(aR["username"]),
                PasswordHash = ToStr(aR["password_hash"]),
                Role = ToStr(aR["role"]),
                CreatedAt = ParseHelpdeskTimestamp(ToStr(aR["created_at"]))
            };
        }

        private static THelpdeskTicket ReadTicket(SqliteDataReader aR)
        {
            return new THelpdeskTicket
            {
                Id = ToLong(aR["id"]),
                UserId = ToLong(aR["user_id"]),
                Username = ToStr(aR["username"]),
                Subject = ToStr(aR["subject"]),
                Status = ToStr(aR["status"]),
                CreatedAt = ParseHelpdeskTimestamp(ToStr(aR["created_at"])),
                UpdatedAt = ParseHelpdeskTimestamp(ToStr(aR["updated_at"]))
            };
        }

        private static THelpdeskMessage ReadMessage(SqliteDataReader aR)
        {
            return new THelpdeskMessage
            {
                Id = ToLong(aR["id"]),
                TicketId = ToLong(aR["ticket_id"]),
                UserId = ToLong(aR["user_id"]),
                Username = ToStr(aR["username"]),
                IsAdmin = ToLong(aR["is_admin"]) != 0,
                Body = ToStr(aR["body"]),
                CreatedAt = ParseHelpdeskTimestamp(ToStr(aR["created_at"])),
                Attachments = Array.Empty<THelpdeskAttachment>()
            };
        }

        private static THelpdeskAttachment ReadAttachment(SqliteDataReader aR)
        {
            return new THelpdeskAttachment
            {
                Id = ToLong(aR["id"]),
                MessageId = ToLong(aR["message_id"]),
                TicketId = ToLong(aR["ticket_id"]),
                OriginalFilename = ToStr(aR["original_filename"]),
                StoredFilename = ToStr(aR["stored_filename"]),
                ContentType = ToStr(aR["content_type"]),
                SizeBytes = ToLong(aR["size_bytes"]),
                CreatedAt = ParseHelpdeskTimestamp(ToStr(aR["created_at"]))
            };
        }

        // ----- schema + seeding ----- //

        public void EnsureSchema()
        {
            string[] vTables =
            {
                "CREATE TABLE IF NOT EXISTS users (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE, " +
                "password_hash TEXT, role TEXT, created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS tickets (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, " +
                "subject TEXT, status TEXT, created_at TEXT, updated_at TEXT)",

                "CREATE TABLE IF NOT EXISTS ticket_messages (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, ticket_id INTEGER, " +
                "user_id INTEGER, is_admin INTEGER, body TEXT, created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS ticket_attachments (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, message_id INTEGER, " +
                "ticket_id INTEGER, original_filename TEXT, stored_filename TEXT, " +
                "content_type TEXT, size_bytes INTEGER, created_at TEXT)"
            };

            using (SqliteConnection oConn = Acquire())
                for (int vI = 0; vI < vTables.Length; vI++)
                    using (SqliteCommand oCmd = NewCmd(oConn, vTables[vI]))
                        oCmd.ExecuteNonQuery();
        }

        // INSERT the admin user (role 'admin') only when the users table has no
        // admin yet. aPasswordHash is the bcrypt hash of the configured password.
        public void SeedAdmin(string aUser, string aPasswordHash)
        {
            using (SqliteConnection oConn = Acquire())
            {
                long vCount;
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) FROM users WHERE role = 'admin'"))
                    vCount = ToLong(oCmd.ExecuteScalar());
                if (vCount > 0)
                    return;

                using (SqliteCommand oCmd = NewCmd(oConn, "INSERT INTO users " +
                    "(username, password_hash, role, created_at) VALUES (:u, :p, :r, :c)"))
                {
                    AddParam(oCmd, ":u", aUser);
                    AddParam(oCmd, ":p", aPasswordHash);
                    AddParam(oCmd, ":r", "admin");
                    AddParam(oCmd, ":c", NowTimestamp());
                    oCmd.ExecuteNonQuery();
                }
            }
        }

        // One-time demo-data seeding: 3 demo users (alice/bob/carol, password
        // 'demo1234') plus a dozen sample tickets spread across the past year so
        // every dashboard time-range bucket has data. Runs only when the tickets
        // table is empty, so it never re-seeds once real tickets exist.
        public void SeedDemoData()
        {
            const string CS_DEMO_PASSWORD = "demo1234";

            using (SqliteConnection oConn = Acquire())
            {
                long vCount;
                using (SqliteCommand oCmd = NewCmd(oConn, "SELECT COUNT(*) FROM tickets"))
                    vCount = ToLong(oCmd.ExecuteScalar());
                if (vCount > 0)
                    return;

                long vAdminId;
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT id FROM users WHERE role = 'admin' ORDER BY id ASC LIMIT 1"))
                {
                    object vObj = oCmd.ExecuteScalar();
                    if (vObj == null || vObj == DBNull.Value)
                        return;
                    vAdminId = Convert.ToInt64(vObj, CultureInfo.InvariantCulture);
                }

                DateTime vNow = DateTime.Now;
                string vPasswordHash = Bcrypt.BcryptHash(CS_DEMO_PASSWORD);

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                {
                    long vAliceId = EnsureDemoUser(oConn, oTx, "alice", vPasswordHash);
                    long vBobId = EnsureDemoUser(oConn, oTx, "bob", vPasswordHash);
                    long vCarolId = EnsureDemoUser(oConn, oTx, "carol", vPasswordHash);

                    if (vAliceId <= 0 || vBobId <= 0 || vCarolId <= 0)
                    {
                        oTx.Rollback();
                        return;
                    }

                    // Today, open, no reply yet.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vAliceId,
                        "Cannot log in after password reset",
                        "I used the forgot password link and set a new password, but " +
                        "every time I try to log in it says invalid username or password.",
                        "open", 3.0 / 24, 3.0 / 24, "");

                    // Today, closed the same day after a quick fix.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vBobId,
                        "Export button not working on Safari",
                        "Clicking Export on the ticket list does nothing for me in " +
                        "Safari. It works fine in Chrome though.", "closed", 0.75 / 24,
                        0.2 / 24, "This turned out to be a Safari caching issue on our " +
                        "side. Should be fixed now, thanks for reporting it.");

                    // Yesterday, closed.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vCarolId,
                        "Feature request: dark mode",
                        "Would love a dark theme option, the bright white page is rough " +
                        "on the eyes in the evening.", "closed", 1.3, 1.0,
                        "Dark mode is already on our roadmap, marking this as planned " +
                        "and closing for tracking. Thanks for the suggestion!");

                    // 3 days ago, open, admin already replied.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vAliceId,
                        "Invoice #4021 shows wrong total",
                        "The total on invoice #4021 does not match the sum of the line " +
                        "items, it is about 15 dollars too high.", "open", 3, 2.7,
                        "Thanks for flagging this, I can reproduce it on our end and " +
                        "we are looking into it now.");

                    // Last week, closed.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vBobId,
                        "Password reset email never arrives",
                        "I requested a password reset three times and never got the " +
                        "email, even after checking spam.", "closed", 5, 4,
                        "This was caused by an outbound mail delay on our side, it is " +
                        "fixed now, please try again.");

                    // Last month, open.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vCarolId,
                        "Mobile layout broken on iPhone Safari",
                        "The sidebar overlaps the main content on my iPhone in Safari, " +
                        "it makes the ticket list unreadable.", "open", 12, 12, "");

                    // Last month, closed.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vAliceId,
                        "How do I change my account email?",
                        "I cannot find a setting to update the email address on my " +
                        "account, is that possible?", "closed", 20, 18,
                        "You can change it from Account Settings, Email. Closing this " +
                        "out, let us know if you hit any issues.");

                    // ~1.5 months ago, open.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vBobId,
                        "Two-factor authentication setup fails",
                        "The QR code on the 2FA setup page never scans with my " +
                        "authenticator app, tried three different apps.", "open", 45, 45, "");

                    // 2 months ago, closed.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vCarolId,
                        "API rate limit too low for our integration",
                        "We keep hitting the API rate limit during normal usage, could " +
                        "the limit be raised for our account?", "closed", 60, 55,
                        "We have bumped your rate limit, this should be resolved now.");

                    // 4 months ago, open.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vAliceId,
                        "Slow page load on the dashboard",
                        "The admin dashboard takes 8 to 10 seconds to load once we have " +
                        "a few hundred tickets, is that expected?", "open", 120, 120, "");

                    // 8 months ago, closed.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vBobId,
                        "Feature request: CSV import for contacts",
                        "It would help a lot if we could bulk import contacts from a " +
                        "CSV file instead of adding them one by one.", "closed", 240, 235,
                        "This is now available under Contacts, Import. Closing this " +
                        "one out.");

                    // 11 months ago, open.
                    SeedTicket(oConn, oTx, vAdminId, vNow, vCarolId,
                        "Cannot upload attachments larger than 5MB",
                        "Trying to attach an 8MB screen recording to a ticket fails " +
                        "silently, no error message shown.", "open", 330, 330, "");

                    oTx.Commit();
                }
            }
        }

        // Insert a demo user only when the username is not already taken,
        // returning its id either way.
        private long EnsureDemoUser(SqliteConnection aConn, SqliteTransaction aTx,
            string aUsername, string aPasswordHash)
        {
            using (SqliteCommand oCmd = NewCmd(aConn,
                "SELECT id FROM users WHERE LOWER(username) = LOWER(:u) LIMIT 1"))
            {
                oCmd.Transaction = aTx;
                AddParam(oCmd, ":u", aUsername);
                object vObj = oCmd.ExecuteScalar();
                if (vObj != null && vObj != DBNull.Value)
                    return Convert.ToInt64(vObj, CultureInfo.InvariantCulture);
            }

            using (SqliteCommand oCmd = NewCmd(aConn, "INSERT INTO users " +
                "(username, password_hash, role, created_at) VALUES (:u, :p, 'user', :c)"))
            {
                oCmd.Transaction = aTx;
                AddParam(oCmd, ":u", aUsername);
                AddParam(oCmd, ":p", aPasswordHash);
                AddParam(oCmd, ":c", NowTimestamp());
                oCmd.ExecuteNonQuery();
            }
            return LastInsertRowId(aConn, aTx);
        }

        private long InsertSeedTicket(SqliteConnection aConn, SqliteTransaction aTx,
            long aOwnerId, string aSubject, string aStatus, DateTime aCreatedAt,
            DateTime aUpdatedAt)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, "INSERT INTO tickets " +
                "(user_id, subject, status, created_at, updated_at) " +
                "VALUES (:uid, :subj, :st, :ca, :ua)"))
            {
                oCmd.Transaction = aTx;
                AddParam(oCmd, ":uid", aOwnerId);
                AddParam(oCmd, ":subj", aSubject);
                AddParam(oCmd, ":st", NormalizeStatus(aStatus));
                AddParam(oCmd, ":ca", FormatHelpdeskTimestamp(aCreatedAt));
                AddParam(oCmd, ":ua", FormatHelpdeskTimestamp(aUpdatedAt));
                oCmd.ExecuteNonQuery();
            }
            return LastInsertRowId(aConn, aTx);
        }

        private void InsertSeedMessage(SqliteConnection aConn, SqliteTransaction aTx,
            long aTicketId, long aAuthorId, bool aIsAdmin, string aBody, DateTime aCreatedAt)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, "INSERT INTO ticket_messages " +
                "(ticket_id, user_id, is_admin, body, created_at) " +
                "VALUES (:tid, :uid, :adm, :body, :ca)"))
            {
                oCmd.Transaction = aTx;
                AddParam(oCmd, ":tid", aTicketId);
                AddParam(oCmd, ":uid", aAuthorId);
                AddParam(oCmd, ":adm", aIsAdmin ? 1 : 0);
                AddParam(oCmd, ":body", aBody);
                AddParam(oCmd, ":ca", FormatHelpdeskTimestamp(aCreatedAt));
                oCmd.ExecuteNonQuery();
            }
        }

        // Seeds one ticket + its opening message, plus (when aAdminReplyBody is
        // non-blank) a follow-up admin reply timestamped at aUpdatedDaysAgo.
        private void SeedTicket(SqliteConnection aConn, SqliteTransaction aTx,
            long aAdminId, DateTime aNow, long aOwnerId, string aSubject,
            string aInitialBody, string aStatus, double aCreatedDaysAgo,
            double aUpdatedDaysAgo, string aAdminReplyBody)
        {
            DateTime vCreatedAt = aNow.AddDays(-aCreatedDaysAgo);
            DateTime vUpdatedAt = aNow.AddDays(-aUpdatedDaysAgo);
            long vTicketId = InsertSeedTicket(aConn, aTx, aOwnerId, aSubject, aStatus,
                vCreatedAt, vUpdatedAt);
            InsertSeedMessage(aConn, aTx, vTicketId, aOwnerId, false, aInitialBody, vCreatedAt);
            if (!string.IsNullOrEmpty((aAdminReplyBody ?? "").Trim()))
                InsertSeedMessage(aConn, aTx, vTicketId, aAdminId, true, aAdminReplyBody,
                    vUpdatedAt);
        }

        // ----- users ----- //

        public bool GetUserByUsername(string aUsername, out THelpdeskUser aUser)
        {
            aUser = null;
            if (string.IsNullOrEmpty((aUsername ?? "").Trim()))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT id, username, " +
                "password_hash, role, created_at FROM users " +
                "WHERE LOWER(username) = LOWER(:u) LIMIT 1"))
            {
                AddParam(oCmd, ":u", aUsername);
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                {
                    if (!oR.Read())
                        return false;
                    aUser = ReadUser(oR);
                    return true;
                }
            }
        }

        public bool GetUserById(long aId, out THelpdeskUser aUser)
        {
            aUser = null;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT id, username, " +
                "password_hash, role, created_at FROM users WHERE id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aId);
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                {
                    if (!oR.Read())
                        return false;
                    aUser = ReadUser(oR);
                    return true;
                }
            }
        }

        public bool UsernameExists(string aUsername)
        {
            if (string.IsNullOrEmpty((aUsername ?? "").Trim()))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT COUNT(*) FROM users WHERE LOWER(username) = LOWER(:u)"))
            {
                AddParam(oCmd, ":u", aUsername);
                return ToLong(oCmd.ExecuteScalar()) > 0;
            }
        }

        // All users with the given role (e.g. 'user'), ordered by username.
        public THelpdeskUser[] ListUsersWithRole(string aRole)
        {
            List<THelpdeskUser> vResult = new List<THelpdeskUser>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT id, username, " +
                "password_hash, role, created_at FROM users WHERE role = :r " +
                "ORDER BY username ASC"))
            {
                AddParam(oCmd, ":r", aRole);
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                    while (oR.Read())
                        vResult.Add(ReadUser(oR));
            }
            return vResult.ToArray();
        }

        // True when at least one role='user' account exists.
        public bool HasAnyNonAdminUsers()
        {
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT COUNT(*) FROM users WHERE role = 'user'"))
                return ToLong(oCmd.ExecuteScalar()) > 0;
        }

        // Self-registration: INSERT a new user with role always 'user'. Returns
        // the new user id, or 0 when the username is already taken.
        public long RegisterUser(string aUsername, string aPasswordHash)
        {
            if (string.IsNullOrEmpty((aUsername ?? "").Trim()))
                return 0;
            if (UsernameExists(aUsername))
                return 0;

            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn, "INSERT INTO users " +
                    "(username, password_hash, role, created_at) " +
                    "VALUES (:u, :p, 'user', :c)"))
                {
                    AddParam(oCmd, ":u", aUsername.Trim());
                    AddParam(oCmd, ":p", aPasswordHash);
                    AddParam(oCmd, ":c", NowTimestamp());
                    oCmd.ExecuteNonQuery();
                }
                return LastInsertRowId(oConn, null);
            }
        }

        // Look up the user by username and verify aPassword against its bcrypt
        // hash. Returns true (and aUser) only on a successful match.
        public bool AuthenticateUser(string aUsername, string aPassword,
            out THelpdeskUser aUser)
        {
            if (!GetUserByUsername(aUsername, out aUser))
                return false;
            return Bcrypt.BcryptVerify(aPassword, aUser.PasswordHash);
        }

        // ----- tickets ----- //

        // INSERT a new ticket (status 'open') plus its first message in a single
        // transaction. Returns the new ticket id; aMessageId returns the id of
        // the initial message row (0 when aInitialMessage was blank).
        public long CreateTicket(long aUserId, string aSubject, string aInitialMessage,
            out long aMessageId)
        {
            aMessageId = 0;
            long vResult = 0;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                string vNow = NowTimestamp();
                using (SqliteCommand oCmd = NewCmd(oConn, "INSERT INTO tickets " +
                    "(user_id, subject, status, created_at, updated_at) " +
                    "VALUES (:uid, :subj, 'open', :ca, :ua)"))
                {
                    oCmd.Transaction = oTx;
                    AddParam(oCmd, ":uid", aUserId);
                    AddParam(oCmd, ":subj", aSubject);
                    AddParam(oCmd, ":ca", vNow);
                    AddParam(oCmd, ":ua", vNow);
                    oCmd.ExecuteNonQuery();
                }
                vResult = LastInsertRowId(oConn, oTx);

                if (vResult > 0 && !string.IsNullOrEmpty((aInitialMessage ?? "").Trim()))
                {
                    using (SqliteCommand oCmd = NewCmd(oConn, "INSERT INTO ticket_messages " +
                        "(ticket_id, user_id, is_admin, body, created_at) " +
                        "VALUES (:tid, :uid, 0, :body, :ca)"))
                    {
                        oCmd.Transaction = oTx;
                        AddParam(oCmd, ":tid", vResult);
                        AddParam(oCmd, ":uid", aUserId);
                        AddParam(oCmd, ":body", aInitialMessage);
                        AddParam(oCmd, ":ca", vNow);
                        oCmd.ExecuteNonQuery();
                    }
                    aMessageId = LastInsertRowId(oConn, oTx);
                }

                oTx.Commit();
            }
            return vResult;
        }

        // All tickets owned by aUserId, newest first (or per aSort/aDir).
        public THelpdeskTicket[] ListTicketsForUser(long aUserId, string aSearch = "",
            string aSort = "", string aDir = "")
        {
            List<THelpdeskTicket> vResult = new List<THelpdeskTicket>();
            string vSearch = (aSearch ?? "").Trim();
            bool vHasSearch = vSearch.Length > 0;
            string vOrderCol = SortColumnSQL(aSort, false);
            string vOrderDir = DirSQL(aDir);

            string vSQL = "SELECT t.id, t.user_id, u.username, t.subject, t.status, " +
                "t.created_at, t.updated_at FROM tickets t " +
                "JOIN users u ON u.id = t.user_id WHERE t.user_id = :uid ";
            if (vHasSearch)
                vSQL += "AND t.subject LIKE :s ESCAPE '\\' ";
            vSQL += "ORDER BY " + vOrderCol + " " + vOrderDir + ", t.id " + vOrderDir;

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                AddParam(oCmd, ":uid", aUserId);
                if (vHasSearch)
                    AddParam(oCmd, ":s", "%" + EscapeLikeValue(vSearch) + "%");
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                    while (oR.Read())
                        vResult.Add(ReadTicket(oR));
            }
            return vResult.ToArray();
        }

        // All tickets (joined to users for the owner display name), newest first.
        public THelpdeskTicket[] ListAllTickets(string aStatusFilter,
            string aUsernameFilter = "", string aSearch = "", string aSort = "",
            string aDir = "")
        {
            List<THelpdeskTicket> vResult = new List<THelpdeskTicket>();
            string vStatus = (aStatusFilter ?? "").Trim().ToLowerInvariant();
            string vUsername = (aUsernameFilter ?? "").Trim();
            string vSearch = (aSearch ?? "").Trim();
            bool vHasStatus = vStatus.Length > 0 && vStatus != "all";
            bool vHasUsername = vUsername.Length > 0;
            bool vHasSearch = vSearch.Length > 0;
            string vOrderCol = SortColumnSQL(aSort, true);
            string vOrderDir = DirSQL(aDir);

            string vWhere = "";
            if (vHasStatus)
                vWhere = "t.status = :st";
            if (vHasUsername)
                vWhere = (vWhere.Length > 0 ? vWhere + " AND " : "") + "u.username = :un";
            if (vHasSearch)
                vWhere = (vWhere.Length > 0 ? vWhere + " AND " : "") +
                    "t.subject LIKE :s ESCAPE '\\'";

            string vSQL = "SELECT t.id, t.user_id, u.username, t.subject, t.status, " +
                "t.created_at, t.updated_at FROM tickets t " +
                "JOIN users u ON u.id = t.user_id ";
            if (vWhere.Length > 0)
                vSQL += "WHERE " + vWhere + " ";
            vSQL += "ORDER BY " + vOrderCol + " " + vOrderDir + ", t.id " + vOrderDir;

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (vHasStatus)
                    AddParam(oCmd, ":st", NormalizeStatus(vStatus));
                if (vHasUsername)
                    AddParam(oCmd, ":un", vUsername);
                if (vHasSearch)
                    AddParam(oCmd, ":s", "%" + EscapeLikeValue(vSearch) + "%");
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                    while (oR.Read())
                        vResult.Add(ReadTicket(oR));
            }
            return vResult.ToArray();
        }

        public bool GetTicket(long aId, out THelpdeskTicket aTicket)
        {
            aTicket = null;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT t.id, t.user_id, " +
                "u.username, t.subject, t.status, t.created_at, t.updated_at " +
                "FROM tickets t JOIN users u ON u.id = t.user_id WHERE t.id = :id LIMIT 1"))
            {
                AddParam(oCmd, ":id", aId);
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                {
                    if (!oR.Read())
                        return false;
                    aTicket = ReadTicket(oR);
                    return true;
                }
            }
        }

        public bool SetTicketStatus(long aTicketId, string aStatus)
        {
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "UPDATE tickets SET status = :st, updated_at = :ua WHERE id = :id"))
            {
                AddParam(oCmd, ":st", NormalizeStatus(aStatus));
                AddParam(oCmd, ":ua", NowTimestamp());
                AddParam(oCmd, ":id", aTicketId);
                return oCmd.ExecuteNonQuery() > 0;
            }
        }

        // ----- dashboard stats ----- //

        public int CountTicketsCreatedBetween(DateTime aFrom, DateTime aTo)
        {
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT COUNT(*) FROM tickets " +
                "WHERE created_at >= :f AND created_at < :t"))
            {
                AddParam(oCmd, ":f", FormatHelpdeskTimestamp(aFrom));
                AddParam(oCmd, ":t", FormatHelpdeskTimestamp(aTo));
                return (int)ToLong(oCmd.ExecuteScalar());
            }
        }

        public int CountTicketsClosedBetween(DateTime aFrom, DateTime aTo)
        {
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT COUNT(*) FROM tickets " +
                "WHERE status = 'closed' AND updated_at >= :f AND updated_at < :t"))
            {
                AddParam(oCmd, ":f", FormatHelpdeskTimestamp(aFrom));
                AddParam(oCmd, ":t", FormatHelpdeskTimestamp(aTo));
                return (int)ToLong(oCmd.ExecuteScalar());
            }
        }

        // Buckets [aFrom, aTo) into a small number of points (per aBucket) and
        // returns the opened/closed counts per bucket, oldest first.
        public THelpdeskActivityPoint[] GetTicketActivitySeries(DateTime aFrom,
            DateTime aTo, string aBucket)
        {
            List<THelpdeskActivityPoint> vResult = new List<THelpdeskActivityPoint>();
            int vSafety = 0;
            DateTime vCur = aFrom;
            while (vCur < aTo && vSafety < 400)
            {
                vSafety++;
                DateTime vNext;
                if (string.Equals(aBucket, "hour4", StringComparison.OrdinalIgnoreCase))
                    vNext = vCur.AddHours(4);
                else if (string.Equals(aBucket, "week", StringComparison.OrdinalIgnoreCase))
                    vNext = vCur.AddDays(7);
                else if (string.Equals(aBucket, "month", StringComparison.OrdinalIgnoreCase))
                    vNext = vCur.AddMonths(1);
                else
                    vNext = vCur.AddDays(1); // 'day' (default)
                if (vNext > aTo)
                    vNext = aTo;

                THelpdeskActivityPoint vPoint = new THelpdeskActivityPoint();
                if (string.Equals(aBucket, "hour4", StringComparison.OrdinalIgnoreCase))
                    vPoint.BucketLabel = vCur.ToString("HH:mm", CultureInfo.InvariantCulture);
                else if (string.Equals(aBucket, "month", StringComparison.OrdinalIgnoreCase))
                    vPoint.BucketLabel = vCur.ToString("MMM yyyy", CultureInfo.InvariantCulture);
                else
                    vPoint.BucketLabel = vCur.ToString("MM-dd", CultureInfo.InvariantCulture);
                vPoint.BucketStart = vCur;
                vPoint.Opened = CountTicketsCreatedBetween(vCur, vNext);
                vPoint.Closed = CountTicketsClosedBetween(vCur, vNext);
                vResult.Add(vPoint);

                vCur = vNext;
            }
            return vResult.ToArray();
        }

        // ----- ticket messages ----- //

        public THelpdeskMessage[] ListMessages(long aTicketId)
        {
            List<THelpdeskMessage> vResult = new List<THelpdeskMessage>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT m.id, m.ticket_id, " +
                "m.user_id, u.username, m.is_admin, m.body, m.created_at " +
                "FROM ticket_messages m JOIN users u ON u.id = m.user_id " +
                "WHERE m.ticket_id = :tid ORDER BY m.created_at ASC, m.id ASC"))
            {
                AddParam(oCmd, ":tid", aTicketId);
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                    while (oR.Read())
                        vResult.Add(ReadMessage(oR));
            }
            return vResult.ToArray();
        }

        // INSERT a new message and bump the parent ticket's updated_at, in a
        // single transaction. Returns the new message id.
        public long AddMessage(long aTicketId, long aUserId, bool aIsAdmin, string aBody)
        {
            long vResult = 0;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                string vNow = NowTimestamp();
                using (SqliteCommand oCmd = NewCmd(oConn, "INSERT INTO ticket_messages " +
                    "(ticket_id, user_id, is_admin, body, created_at) " +
                    "VALUES (:tid, :uid, :adm, :body, :ca)"))
                {
                    oCmd.Transaction = oTx;
                    AddParam(oCmd, ":tid", aTicketId);
                    AddParam(oCmd, ":uid", aUserId);
                    AddParam(oCmd, ":adm", aIsAdmin ? 1 : 0);
                    AddParam(oCmd, ":body", aBody);
                    AddParam(oCmd, ":ca", vNow);
                    oCmd.ExecuteNonQuery();
                }
                vResult = LastInsertRowId(oConn, oTx);

                using (SqliteCommand oCmd = NewCmd(oConn,
                    "UPDATE tickets SET updated_at = :ua WHERE id = :id"))
                {
                    oCmd.Transaction = oTx;
                    AddParam(oCmd, ":ua", vNow);
                    AddParam(oCmd, ":id", aTicketId);
                    oCmd.ExecuteNonQuery();
                }

                oTx.Commit();
            }
            return vResult;
        }

        // ----- attachments ----- //

        public long AddAttachment(long aMessageId, long aTicketId, string aOriginalName,
            string aStoredName, string aContentType, long aSizeBytes)
        {
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn, "INSERT INTO ticket_attachments " +
                    "(message_id, ticket_id, original_filename, stored_filename, " +
                    "content_type, size_bytes, created_at) " +
                    "VALUES (:mid, :tid, :orig, :stored, :ct, :sz, :ca)"))
                {
                    AddParam(oCmd, ":mid", aMessageId);
                    AddParam(oCmd, ":tid", aTicketId);
                    AddParam(oCmd, ":orig", aOriginalName);
                    AddParam(oCmd, ":stored", aStoredName);
                    AddParam(oCmd, ":ct", aContentType);
                    AddParam(oCmd, ":sz", aSizeBytes);
                    AddParam(oCmd, ":ca", NowTimestamp());
                    oCmd.ExecuteNonQuery();
                }
                return LastInsertRowId(oConn, null);
            }
        }

        public THelpdeskAttachment[] ListAttachments(long aMessageId)
        {
            List<THelpdeskAttachment> vResult = new List<THelpdeskAttachment>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT id, message_id, ticket_id, " +
                "original_filename, stored_filename, content_type, size_bytes, created_at " +
                "FROM ticket_attachments WHERE message_id = :mid ORDER BY id ASC"))
            {
                AddParam(oCmd, ":mid", aMessageId);
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                    while (oR.Read())
                        vResult.Add(ReadAttachment(oR));
            }
            return vResult.ToArray();
        }

        public bool GetAttachment(long aId, out THelpdeskAttachment aAttachment)
        {
            aAttachment = null;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, "SELECT id, message_id, ticket_id, " +
                "original_filename, stored_filename, content_type, size_bytes, created_at " +
                "FROM ticket_attachments WHERE id = :id LIMIT 1"))
            {
                AddParam(oCmd, ":id", aId);
                using (SqliteDataReader oR = oCmd.ExecuteReader())
                {
                    if (!oR.Read())
                        return false;
                    aAttachment = ReadAttachment(oR);
                    return true;
                }
            }
        }
    }
}
