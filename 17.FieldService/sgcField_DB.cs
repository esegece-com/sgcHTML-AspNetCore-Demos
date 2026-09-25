// ***************************************************************************
//  sgcField - field service management web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\17.FieldService\sgcField_DB.pas
//
//  FireDAC (TFDConnection / FDManager / TFDQuery) is replaced by
//  Microsoft.Data.Sqlite. The pool builds a connection string once from the
//  absolute DB file path; Microsoft.Data.Sqlite pools the underlying
//  connections automatically by connection string. Acquire() returns an open
//  SqliteConnection that the caller disposes (which returns it to the pool).
//
//  TFieldDataSet materialises the open query into a System.Data.DataTable,
//  which is what the managed sgcHTML components' LoadFromDataSet takes (the
//  established TDataSet mapping for this migration), and keeps the SQL text
//  that produced it for the /sql page.
//
//  Timestamp columns keep the very same 'yyyy-MM-ddTHH:mm:ss' shape the Delphi
//  unit writes, because every report compares them lexically as TEXT.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace FieldService
{
    /// <summary>Field service DB error. Mirrors Delphi EFieldDBError.</summary>
    public class EFieldDBError : Exception
    {
        public EFieldDBError(string message) : base(message) { }
    }

    // Delphi's System.Random LCG, reproduced exactly (RandSeed * $08088405 + 1,
    // result = the high 32 bits of RandSeed * Range). The demo seed pins
    // RandSeed on purpose so the generated data is always the same; this keeps
    // the managed port generating the very same rows.
    public sealed class TFieldRandom
    {
        private int FSeed;

        public TFieldRandom(int aSeed)
        {
            FSeed = aSeed;
        }

        public int Seed
        {
            get { return FSeed; }
            set { FSeed = value; }
        }

        public int Next(int aRange)
        {
            unchecked
            {
                FSeed = FSeed * 0x08088405 + 1;
            }
            return (int)(((ulong)(uint)FSeed * (ulong)(uint)aRange) >> 32);
        }
    }

    // A materialised, already-open dataset. The caller owns it and MUST Dispose
    // it. DataSet is what the sgcHTML components' LoadFromDataSet is handed
    // (that is the whole point of this demo: no REST tier), SQLText is the very
    // SQL that produced it, printed next to the rendered component on /sql.
    public class TFieldDataSet : IDisposable
    {
        private DataTable FData;
        private readonly string FSQLText;

        public TFieldDataSet(SqliteConnection aConn, string aSQL,
            string[] aParamNames, object[] aParamValues)
        {
            FSQLText = aSQL;
            FData = new DataTable();
            using (SqliteCommand oCmd = aConn.CreateCommand())
            {
                oCmd.CommandText = aSQL;
                if (aParamNames != null)
                {
                    for (int vI = 0; vI < aParamNames.Length; vI++)
                    {
                        if (aParamValues == null || vI >= aParamValues.Length)
                            break;
                        object vValue = aParamValues[vI];
                        oCmd.Parameters.AddWithValue(aParamNames[vI],
                            vValue == null ? DBNull.Value : vValue);
                    }
                }
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    FData.Load(oReader);
                }
            }
        }

        public DataTable DataSet
        {
            get { return FData; }
        }

        public string SQLText
        {
            get { return FSQLText; }
        }

        public void Dispose()
        {
            if (FData != null)
            {
                FData.Dispose();
                FData = null;
            }
        }
    }

    public class TFieldDBPool : IDisposable
    {
        public const string CS_FIELD_DB_DEF_NAME = "sgcField";

        // 'yyyy-mm-dd"T"hh:nn:ss' in Delphi terms.
        private const string CS_TS_FORMAT = "yyyy-MM-dd'T'HH:mm:ss";

        // ----- shared SELECT shapes ----- //

        private const string CS_USER_SELECT =
            "SELECT id, username, password_hash, role, display_name, " +
            "phone, avatar_initials, created_at FROM users ";

        // The technician list is one flat statement: the correlated subqueries carry
        // the derived columns (last known position, presence counters, workload) so
        // the map and the dispatch board never need a second round trip per row.
        private const string CS_TECH_SELECT =
            "SELECT t.id, t.user_id, u.username, u.display_name, " +
            "u.avatar_initials, u.phone, t.skills, t.home_lat, t.home_lng, " +
            "t.active, t.hourly_rate, " +
            "(SELECT e.lat FROM job_events e WHERE e.user_id = t.user_id " +
            "AND (e.lat <> 0 OR e.lng <> 0) ORDER BY e.created_at DESC, e.id DESC " +
            "LIMIT 1) AS cur_lat, " +
            "(SELECT e.lng FROM job_events e WHERE e.user_id = t.user_id " +
            "AND (e.lat <> 0 OR e.lng <> 0) ORDER BY e.created_at DESC, e.id DESC " +
            "LIMIT 1) AS cur_lng, " +
            "(SELECT COUNT(*) FROM jobs j WHERE j.technician_id = t.id " +
            "AND j.status IN ('new', 'scheduled', 'enroute', 'onsite')) " +
            "AS open_jobs, " +
            "(SELECT COUNT(*) FROM jobs j WHERE j.technician_id = t.id " +
            "AND j.status = 'onsite') AS n_onsite, " +
            "(SELECT COUNT(*) FROM jobs j WHERE j.technician_id = t.id " +
            "AND j.status = 'enroute') AS n_enroute " +
            "FROM technicians t LEFT JOIN users u ON u.id = t.user_id ";

        private const string CS_CUSTOMER_SELECT =
            "SELECT c.id, c.name, c.contact, c.email, c.phone, " +
            "c.address, c.city, c.lat, c.lng, c.created_at, " +
            "(SELECT COUNT(*) FROM sites s WHERE s.customer_id = c.id) " +
            "AS site_count, " +
            "(SELECT COUNT(*) FROM jobs j WHERE j.customer_id = c.id) AS job_count " +
            "FROM customers c ";

        private const string CS_SITE_SELECT =
            "SELECT s.id, s.customer_id, c.name AS customer_name, " +
            "s.name AS site_name, s.address, s.lat, s.lng, s.access_notes " +
            "FROM sites s LEFT JOIN customers c ON c.id = s.customer_id ";

        private const string CS_ASSET_SELECT =
            "SELECT a.id, a.site_id, s.name AS site_name, " +
            "c.name AS customer_name, a.parent_id, a.name AS asset_name, a.model, " +
            "a.serial, a.installed_at, a.warranty_until, a.status " +
            "FROM assets a LEFT JOIN sites s ON s.id = a.site_id " +
            "LEFT JOIN customers c ON c.id = s.customer_id ";

        private const string CS_PART_SELECT =
            "SELECT id, sku, name, unit_price, stock FROM parts ";

        // Every job-returning statement shares these columns and these LEFT JOINs,
        // so a job with no technician, no asset or no site still comes back.
        private const string CS_JOB_COLS =
            "SELECT j.id, j.reference, j.customer_id, j.site_id, " +
            "j.asset_id, j.technician_id, j.title, j.description, j.priority, " +
            "j.status, j.scheduled_start, j.scheduled_end, j.actual_start, " +
            "j.actual_end, j.sla_due_at, j.created_at, c.name AS customer_name, " +
            "s.name AS site_name, s.address AS site_address, s.lat AS site_lat, " +
            "s.lng AS site_lng, a.name AS asset_name, " +
            "tu.display_name AS technician_name, " +
            "tu.avatar_initials AS technician_initials ";

        private const string CS_JOB_FROM =
            "FROM jobs j " +
            "LEFT JOIN customers c ON c.id = j.customer_id " +
            "LEFT JOIN sites s ON s.id = j.site_id " +
            "LEFT JOIN assets a ON a.id = j.asset_id " +
            "LEFT JOIN technicians t ON t.id = j.technician_id " +
            "LEFT JOIN users tu ON tu.id = t.user_id ";

        private const string CS_JOB_SELECT = CS_JOB_COLS + CS_JOB_FROM;

        private const string CS_EVENT_SELECT =
            "SELECT e.id, e.job_id, e.user_id, " +
            "u.display_name AS user_name, e.kind, e.detail, e.lat, e.lng, " +
            "e.created_at FROM job_events e LEFT JOIN users u ON u.id = e.user_id ";

        private const string CS_CHECK_SELECT =
            "SELECT id, job_id, \"position\", \"text\" AS item_text, " +
            "done, done_at FROM job_checklist ";

        private const string CS_JOBPART_SELECT =
            "SELECT jp.id, jp.job_id, jp.part_id, p.sku, " +
            "p.name AS part_name, jp.qty, jp.unit_price FROM job_parts jp " +
            "LEFT JOIN parts p ON p.id = jp.part_id ";

        private const string CS_PHOTO_SELECT =
            "SELECT id, job_id, filename, content_type, size_bytes, " +
            "caption, created_at FROM job_photos ";

        private const string CS_SIGN_SELECT =
            "SELECT id, job_id, signer_name, signature_png, " +
            "signed_at FROM job_signatures ";

        private const string CS_MESSAGE_SELECT =
            "SELECT m.id, m.job_id, m.from_user_id, " +
            "u.display_name AS from_name, u.avatar_initials AS from_initials, " +
            "u.role AS from_role, m.to_user_id, m.body, m.created_at, m.read_at " +
            "FROM messages m LEFT JOIN users u ON u.id = m.from_user_id ";

        private const string CS_AUDIT_SELECT =
            "SELECT g.id, g.user_id, u.display_name AS user_name, " +
            "g.action, g.entity, g.entity_id, g.detail, g.ip, g.created_at " +
            "FROM audit_log g LEFT JOIN users u ON u.id = g.user_id ";

        // ----- live statuses ----- //

        private const string CS_LIVE_STATUSES = "('new', 'scheduled', 'enroute', 'onsite')";

        // Delphi's global RandSeed starts at 0 and SeedDemoData pins it; CreateJob
        // draws from the same stream. One shared generator reproduces that.
        private static readonly TFieldRandom GRandom = new TFieldRandom(0);
        private static readonly object GRandomLock = new object();

        private readonly string FDatabaseFile;
        private readonly string FConnStr;

        // aDatabaseFile is resolved to an absolute path internally.
        public TFieldDBPool(string aDatabaseFile)
        {
            string vFile = aDatabaseFile;
            if (string.IsNullOrEmpty(vFile))
                vFile = "data\\field.db";
            // Resolve relative paths against the current directory (the EXE dir,
            // which the launcher sets via SetCurrentDir).
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
                using (SqliteCommand oPragma = oConn.CreateCommand())
                {
                    oPragma.CommandText = "PRAGMA foreign_keys=ON;";
                    oPragma.ExecuteNonQuery();
                    oPragma.CommandText = "PRAGMA journal_mode=WAL;";
                    oPragma.ExecuteNonQuery();
                }
                return oConn;
            }
            catch
            {
                oConn.Dispose();
                throw;
            }
        }

        // Open aSQL on a pooled connection. aParamNames / aParamValues must have
        // the same length (pass null, null for a parameterless statement). Caller
        // disposes the result.
        public TFieldDataSet OpenDataSet(string aSQL, string[] aParamNames,
            object[] aParamValues)
        {
            using (SqliteConnection oConn = Acquire())
            {
                return new TFieldDataSet(oConn, aSQL, aParamNames, aParamValues);
            }
        }

        // ----- timestamp helpers ----- //

        public static string FieldNowTimestamp()
        {
            return DateTime.Now.ToString(CS_TS_FORMAT, CultureInfo.InvariantCulture);
        }

        public static string FormatFieldTimestamp(DateTime aValue)
        {
            if (IsZeroDate(aValue))
                return "";
            return aValue.ToString(CS_TS_FORMAT, CultureInfo.InvariantCulture);
        }

        // Parse the fixed 'yyyy-MM-ddTHH:mm:ss' shape this unit writes. Returns
        // DateTime.MinValue (the managed stand-in for the Delphi 0) for blank /
        // unparsable values. Parsed by fixed position on purpose, exactly like the
        // Delphi original.
        public static DateTime ParseFieldTimestamp(string aValue)
        {
            string vText = (aValue ?? "").Trim();
            if (vText.Length == 0)
                return DateTime.MinValue;
            if (vText.Length < 19)
                return DateTime.MinValue;
            int vYear = IntDef(vText.Substring(0, 4), -1);
            int vMonth = IntDef(vText.Substring(5, 2), -1);
            int vDay = IntDef(vText.Substring(8, 2), -1);
            int vHour = IntDef(vText.Substring(11, 2), -1);
            int vMin = IntDef(vText.Substring(14, 2), -1);
            int vSec = IntDef(vText.Substring(17, 2), -1);
            if ((vYear < 1) || (vMonth < 1) || (vMonth > 12) || (vDay < 1) ||
                (vDay > 31) || (vHour < 0) || (vHour > 23) || (vMin < 0) ||
                (vMin > 59) || (vSec < 0) || (vSec > 59))
                return DateTime.MinValue;
            try
            {
                return new DateTime(vYear, vMonth, vDay, vHour, vMin, vSec);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        // The managed stand-in for the Delphi "TDateTime = 0" sentinel.
        public static bool IsZeroDate(DateTime aValue)
        {
            return aValue <= DateTime.MinValue;
        }

        private static int IntDef(string aValue, int aDefault)
        {
            int vResult;
            if (int.TryParse(aValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out vResult))
                return vResult;
            return aDefault;
        }

        // ----- small SQL helpers ----- //

        // Escape LIKE metacharacters ('\', '%', '_') in a user-supplied search term
        // before wrapping it in '%...%'. Paired with an explicit ESCAPE '\' clause at
        // every call site.
        private static string EscapeLikeValue(string aValue)
        {
            string vResult = (aValue ?? "").Replace("\\", "\\\\");
            vResult = vResult.Replace("%", "\\%");
            vResult = vResult.Replace("_", "\\_");
            return vResult;
        }

        // Whitelist aDir to ASC/DESC; anything else defaults to DESC.
        private static string DirSQL(string aDir)
        {
            if (string.Equals((aDir ?? "").Trim(), "asc", StringComparison.OrdinalIgnoreCase))
                return "ASC";
            return "DESC";
        }

        // Same, but the safe default is ASC (name-like columns).
        private static string DirSQLAsc(string aDir)
        {
            if (string.Equals((aDir ?? "").Trim(), "desc", StringComparison.OrdinalIgnoreCase))
                return "DESC";
            return "ASC";
        }

        // The ONLY thing interpolated into the job ORDER BY, and only ever one of
        // these fixed strings.
        private static string JobSortColumnSQL(string aSort)
        {
            string vSort = aSort ?? "";
            if (string.Equals(vSort, "ref", StringComparison.OrdinalIgnoreCase))
                return "j.reference";
            if (string.Equals(vSort, "title", StringComparison.OrdinalIgnoreCase))
                return "j.title";
            if (string.Equals(vSort, "customer", StringComparison.OrdinalIgnoreCase))
                return "c.name";
            if (string.Equals(vSort, "tech", StringComparison.OrdinalIgnoreCase))
                return "tu.display_name";
            if (string.Equals(vSort, "priority", StringComparison.OrdinalIgnoreCase))
                return "CASE j.priority WHEN 'urgent' THEN 4 WHEN 'high' THEN 3 " +
                    "WHEN 'normal' THEN 2 ELSE 1 END";
            if (string.Equals(vSort, "status", StringComparison.OrdinalIgnoreCase))
                return "j.status";
            if (string.Equals(vSort, "created", StringComparison.OrdinalIgnoreCase))
                return "j.created_at";
            return "j.scheduled_start";
        }

        private static string CustomerSortColumnSQL(string aSort)
        {
            string vSort = aSort ?? "";
            if (string.Equals(vSort, "city", StringComparison.OrdinalIgnoreCase))
                return "c.city";
            if (string.Equals(vSort, "sites", StringComparison.OrdinalIgnoreCase))
                return "site_count";
            if (string.Equals(vSort, "jobs", StringComparison.OrdinalIgnoreCase))
                return "job_count";
            if (string.Equals(vSort, "created", StringComparison.OrdinalIgnoreCase))
                return "c.created_at";
            return "c.name";
        }

        // Build a LIMIT / OFFSET tail from integers only.
        private static string LimitSQL(int aLimit, int aOffset)
        {
            string vResult = "";
            if (aLimit > 0)
            {
                vResult = " LIMIT " + aLimit.ToString(CultureInfo.InvariantCulture);
                if (aOffset > 0)
                    vResult = vResult + " OFFSET " +
                        aOffset.ToString(CultureInfo.InvariantCulture);
            }
            else if (aOffset > 0)
                vResult = " LIMIT -1 OFFSET " +
                    aOffset.ToString(CultureInfo.InvariantCulture);
            return vResult;
        }

        // Two-letter avatar initials from a display name, falling back to the
        // username when the name is blank.
        private static string InitialsOf(string aName, string aUsername)
        {
            string vName = (aName ?? "").Trim();
            if (vName.Length == 0)
                vName = (aUsername ?? "").Trim();
            if (vName.Length == 0)
                return "??";
            int vSpace = vName.IndexOf(' ') + 1; // Delphi Pos() is 1-based
            if ((vSpace > 1) && (vSpace < vName.Length))
                return (vName.Substring(0, 1) + vName.Substring(vSpace, 1)).ToUpperInvariant();
            if (vName.Length >= 2)
                return vName.Substring(0, 2).ToUpperInvariant();
            return vName.Substring(0, 1).ToUpperInvariant();
        }

        // --- low-level command helpers -------------------------------------- //

        private static SqliteCommand NewCmd(SqliteConnection aConn, string aSQL)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.CommandText = aSQL;
            return oCmd;
        }

        private static void ApplyArgs(SqliteCommand aCmd, object[] aArgs)
        {
            if (aArgs == null)
                return;
            int vI = 0;
            while (vI + 1 < aArgs.Length)
            {
                aCmd.Parameters.AddWithValue((string)aArgs[vI],
                    aArgs[vI + 1] == null ? DBNull.Value : aArgs[vI + 1]);
                vI += 2;
            }
        }

        private static int ExecNonQuery(SqliteConnection aConn, string aSQL,
            params object[] aArgs)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
            {
                ApplyArgs(oCmd, aArgs);
                return oCmd.ExecuteNonQuery();
            }
        }

        private static long ExecScalarLong(SqliteConnection aConn, string aSQL,
            params object[] aArgs)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
            {
                ApplyArgs(oCmd, aArgs);
                object vVal = oCmd.ExecuteScalar();
                if (vVal == null || vVal == DBNull.Value)
                    return 0;
                return Convert.ToInt64(vVal, CultureInfo.InvariantCulture);
            }
        }

        private static double ExecScalarDouble(SqliteConnection aConn, string aSQL,
            params object[] aArgs)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
            {
                ApplyArgs(oCmd, aArgs);
                object vVal = oCmd.ExecuteScalar();
                if (vVal == null || vVal == DBNull.Value)
                    return 0;
                return Convert.ToDouble(vVal, CultureInfo.InvariantCulture);
            }
        }

        private static long LastInsertRowId(SqliteConnection aConn)
        {
            return ExecScalarLong(aConn, "SELECT last_insert_rowid()");
        }

        // --- reader column helpers (map DBNull / ordinals) ------------------ //

        private static long RInt64(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return 0;
            return Convert.ToInt64(aReader.GetValue(vOrd), CultureInfo.InvariantCulture);
        }

        private static int RInt(SqliteDataReader aReader, string aName)
        {
            return (int)RInt64(aReader, aName);
        }

        private static double RFloat(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return 0;
            return Convert.ToDouble(aReader.GetValue(vOrd), CultureInfo.InvariantCulture);
        }

        private static string RStr(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return "";
            object vValue = aReader.GetValue(vOrd);
            if (vValue is string)
                return (string)vValue;
            return Convert.ToString(vValue, CultureInfo.InvariantCulture) ?? "";
        }

        private static DateTime RDate(SqliteDataReader aReader, string aName)
        {
            return ParseFieldTimestamp(RStr(aReader, aName));
        }

        // ----- record fillers ----- //

        private static TFieldUser FillUser(SqliteDataReader aReader)
        {
            TFieldUser oRow = new TFieldUser();
            oRow.Id = RInt64(aReader, "id");
            oRow.Username = RStr(aReader, "username");
            oRow.PasswordHash = RStr(aReader, "password_hash");
            oRow.Role = RStr(aReader, "role");
            oRow.DisplayName = RStr(aReader, "display_name");
            oRow.Phone = RStr(aReader, "phone");
            oRow.AvatarInitials = RStr(aReader, "avatar_initials");
            oRow.CreatedAt = RDate(aReader, "created_at");
            return oRow;
        }

        private static TFieldTechnician FillTechnician(SqliteDataReader aReader)
        {
            TFieldTechnician oRow = new TFieldTechnician();
            oRow.Id = RInt64(aReader, "id");
            oRow.UserId = RInt64(aReader, "user_id");
            oRow.Username = RStr(aReader, "username");
            oRow.DisplayName = RStr(aReader, "display_name");
            oRow.AvatarInitials = RStr(aReader, "avatar_initials");
            oRow.Phone = RStr(aReader, "phone");
            oRow.Skills = RStr(aReader, "skills");
            oRow.HomeLat = RFloat(aReader, "home_lat");
            oRow.HomeLng = RFloat(aReader, "home_lng");
            oRow.Active = RInt(aReader, "active") != 0;
            oRow.HourlyRate = RFloat(aReader, "hourly_rate");
            oRow.CurrentLat = RFloat(aReader, "cur_lat");
            oRow.CurrentLng = RFloat(aReader, "cur_lng");
            if ((oRow.CurrentLat == 0) && (oRow.CurrentLng == 0))
            {
                oRow.CurrentLat = oRow.HomeLat;
                oRow.CurrentLng = oRow.HomeLng;
            }
            oRow.OpenJobs = RInt(aReader, "open_jobs");
            int vOnsite = RInt(aReader, "n_onsite");
            int vEnroute = RInt(aReader, "n_enroute");
            if (!oRow.Active)
                oRow.Presence = "off";
            else if (vOnsite > 0)
                oRow.Presence = FieldConst.CS_JOB_ONSITE;
            else if (vEnroute > 0)
                oRow.Presence = FieldConst.CS_JOB_ENROUTE;
            else
                oRow.Presence = "idle";
            return oRow;
        }

        private static TFieldCustomer FillCustomer(SqliteDataReader aReader)
        {
            TFieldCustomer oRow = new TFieldCustomer();
            oRow.Id = RInt64(aReader, "id");
            oRow.Name = RStr(aReader, "name");
            oRow.Contact = RStr(aReader, "contact");
            oRow.Email = RStr(aReader, "email");
            oRow.Phone = RStr(aReader, "phone");
            oRow.Address = RStr(aReader, "address");
            oRow.City = RStr(aReader, "city");
            oRow.Lat = RFloat(aReader, "lat");
            oRow.Lng = RFloat(aReader, "lng");
            oRow.CreatedAt = RDate(aReader, "created_at");
            oRow.SiteCount = RInt(aReader, "site_count");
            oRow.JobCount = RInt(aReader, "job_count");
            return oRow;
        }

        private static TFieldSite FillSite(SqliteDataReader aReader)
        {
            TFieldSite oRow = new TFieldSite();
            oRow.Id = RInt64(aReader, "id");
            oRow.CustomerId = RInt64(aReader, "customer_id");
            oRow.CustomerName = RStr(aReader, "customer_name");
            oRow.Name = RStr(aReader, "site_name");
            oRow.Address = RStr(aReader, "address");
            oRow.Lat = RFloat(aReader, "lat");
            oRow.Lng = RFloat(aReader, "lng");
            oRow.AccessNotes = RStr(aReader, "access_notes");
            return oRow;
        }

        private static TFieldAsset FillAsset(SqliteDataReader aReader)
        {
            TFieldAsset oRow = new TFieldAsset();
            oRow.Id = RInt64(aReader, "id");
            oRow.SiteId = RInt64(aReader, "site_id");
            oRow.SiteName = RStr(aReader, "site_name");
            oRow.CustomerName = RStr(aReader, "customer_name");
            oRow.ParentId = RInt64(aReader, "parent_id");
            oRow.Name = RStr(aReader, "asset_name");
            oRow.Model = RStr(aReader, "model");
            oRow.Serial = RStr(aReader, "serial");
            oRow.InstalledAt = RDate(aReader, "installed_at");
            oRow.WarrantyUntil = RDate(aReader, "warranty_until");
            oRow.Status = RStr(aReader, "status");
            return oRow;
        }

        private static TFieldPart FillPart(SqliteDataReader aReader)
        {
            TFieldPart oRow = new TFieldPart();
            oRow.Id = RInt64(aReader, "id");
            oRow.Sku = RStr(aReader, "sku");
            oRow.Name = RStr(aReader, "name");
            oRow.UnitPrice = RFloat(aReader, "unit_price");
            oRow.Stock = RInt(aReader, "stock");
            return oRow;
        }

        private static TFieldJob FillJob(SqliteDataReader aReader)
        {
            TFieldJob oRow = new TFieldJob();
            oRow.Id = RInt64(aReader, "id");
            oRow.Reference = RStr(aReader, "reference");
            oRow.CustomerId = RInt64(aReader, "customer_id");
            oRow.CustomerName = RStr(aReader, "customer_name");
            oRow.SiteId = RInt64(aReader, "site_id");
            oRow.SiteName = RStr(aReader, "site_name");
            oRow.SiteAddress = RStr(aReader, "site_address");
            oRow.SiteLat = RFloat(aReader, "site_lat");
            oRow.SiteLng = RFloat(aReader, "site_lng");
            oRow.AssetId = RInt64(aReader, "asset_id");
            oRow.AssetName = RStr(aReader, "asset_name");
            oRow.TechnicianId = RInt64(aReader, "technician_id");
            oRow.TechnicianName = RStr(aReader, "technician_name");
            oRow.TechnicianInitials = RStr(aReader, "technician_initials");
            oRow.Title = RStr(aReader, "title");
            oRow.Description = RStr(aReader, "description");
            oRow.Priority = RStr(aReader, "priority");
            oRow.Status = RStr(aReader, "status");
            oRow.ScheduledStart = RDate(aReader, "scheduled_start");
            oRow.ScheduledEnd = RDate(aReader, "scheduled_end");
            oRow.ActualStart = RDate(aReader, "actual_start");
            oRow.ActualEnd = RDate(aReader, "actual_end");
            oRow.SlaDueAt = RDate(aReader, "sla_due_at");
            oRow.CreatedAt = RDate(aReader, "created_at");
            return oRow;
        }

        private static TFieldJobEvent FillEvent(SqliteDataReader aReader)
        {
            TFieldJobEvent oRow = new TFieldJobEvent();
            oRow.Id = RInt64(aReader, "id");
            oRow.JobId = RInt64(aReader, "job_id");
            oRow.UserId = RInt64(aReader, "user_id");
            oRow.UserName = RStr(aReader, "user_name");
            oRow.Kind = RStr(aReader, "kind");
            oRow.Detail = RStr(aReader, "detail");
            oRow.Lat = RFloat(aReader, "lat");
            oRow.Lng = RFloat(aReader, "lng");
            oRow.CreatedAt = RDate(aReader, "created_at");
            return oRow;
        }

        private static TFieldChecklistItem FillChecklist(SqliteDataReader aReader)
        {
            TFieldChecklistItem oRow = new TFieldChecklistItem();
            oRow.Id = RInt64(aReader, "id");
            oRow.JobId = RInt64(aReader, "job_id");
            oRow.Position = RInt(aReader, "position");
            oRow.Text_ = RStr(aReader, "item_text");
            oRow.Done = RInt(aReader, "done") != 0;
            oRow.DoneAt = RDate(aReader, "done_at");
            return oRow;
        }

        private static TFieldJobPart FillJobPart(SqliteDataReader aReader)
        {
            TFieldJobPart oRow = new TFieldJobPart();
            oRow.Id = RInt64(aReader, "id");
            oRow.JobId = RInt64(aReader, "job_id");
            oRow.PartId = RInt64(aReader, "part_id");
            oRow.Sku = RStr(aReader, "sku");
            oRow.Name = RStr(aReader, "part_name");
            oRow.Qty = RInt(aReader, "qty");
            oRow.UnitPrice = RFloat(aReader, "unit_price");
            return oRow;
        }

        private static TFieldJobPhoto FillPhoto(SqliteDataReader aReader)
        {
            TFieldJobPhoto oRow = new TFieldJobPhoto();
            oRow.Id = RInt64(aReader, "id");
            oRow.JobId = RInt64(aReader, "job_id");
            oRow.Filename = RStr(aReader, "filename");
            oRow.ContentType = RStr(aReader, "content_type");
            oRow.SizeBytes = RInt64(aReader, "size_bytes");
            oRow.Caption = RStr(aReader, "caption");
            oRow.CreatedAt = RDate(aReader, "created_at");
            return oRow;
        }

        private static TFieldSignature FillSignature(SqliteDataReader aReader)
        {
            TFieldSignature oRow = new TFieldSignature();
            oRow.Id = RInt64(aReader, "id");
            oRow.JobId = RInt64(aReader, "job_id");
            oRow.SignerName = RStr(aReader, "signer_name");
            oRow.SignaturePng = RStr(aReader, "signature_png");
            oRow.SignedAt = RDate(aReader, "signed_at");
            return oRow;
        }

        private static TFieldMessage FillMessage(SqliteDataReader aReader)
        {
            TFieldMessage oRow = new TFieldMessage();
            oRow.Id = RInt64(aReader, "id");
            oRow.JobId = RInt64(aReader, "job_id");
            oRow.FromUserId = RInt64(aReader, "from_user_id");
            oRow.FromName = RStr(aReader, "from_name");
            oRow.FromInitials = RStr(aReader, "from_initials");
            oRow.FromRole = RStr(aReader, "from_role");
            oRow.ToUserId = RInt64(aReader, "to_user_id");
            oRow.Body = RStr(aReader, "body");
            oRow.CreatedAt = RDate(aReader, "created_at");
            oRow.ReadAt = RDate(aReader, "read_at");
            return oRow;
        }

        private static TFieldAuditEntry FillAudit(SqliteDataReader aReader)
        {
            TFieldAuditEntry oRow = new TFieldAuditEntry();
            oRow.Id = RInt64(aReader, "id");
            oRow.UserId = RInt64(aReader, "user_id");
            oRow.UserName = RStr(aReader, "user_name");
            oRow.Action = RStr(aReader, "action");
            oRow.Entity = RStr(aReader, "entity");
            oRow.EntityId = RInt64(aReader, "entity_id");
            oRow.Detail = RStr(aReader, "detail");
            oRow.IP = RStr(aReader, "ip");
            oRow.CreatedAt = RDate(aReader, "created_at");
            return oRow;
        }

        // ----------------------------------------------------------------- //
        // schema + seed
        // ----------------------------------------------------------------- //

        public void EnsureSchema()
        {
            string[] vTables =
            {
                "CREATE TABLE IF NOT EXISTS users (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE, " +
                "password_hash TEXT, role TEXT, display_name TEXT, phone TEXT, " +
                "avatar_initials TEXT, created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS passkeys (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, " +
                "credential_id TEXT, public_key TEXT, sign_count INTEGER, " +
                "device_name TEXT, created_at TEXT, last_used_at TEXT)",

                "CREATE TABLE IF NOT EXISTS technicians (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, skills TEXT, " +
                "home_lat REAL, home_lng REAL, active INTEGER, hourly_rate REAL)",

                "CREATE TABLE IF NOT EXISTS customers (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, contact TEXT, " +
                "email TEXT, phone TEXT, address TEXT, city TEXT, lat REAL, lng REAL, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS sites (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER, name TEXT, " +
                "address TEXT, lat REAL, lng REAL, access_notes TEXT)",

                "CREATE TABLE IF NOT EXISTS assets (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, site_id INTEGER, " +
                "parent_id INTEGER, name TEXT, model TEXT, serial TEXT, " +
                "installed_at TEXT, warranty_until TEXT, status TEXT)",

                "CREATE TABLE IF NOT EXISTS jobs (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, reference TEXT, " +
                "customer_id INTEGER, site_id INTEGER, asset_id INTEGER, " +
                "technician_id INTEGER, title TEXT, description TEXT, priority TEXT, " +
                "status TEXT, scheduled_start TEXT, scheduled_end TEXT, " +
                "actual_start TEXT, actual_end TEXT, sla_due_at TEXT, created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS job_events (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER, " +
                "user_id INTEGER, kind TEXT, detail TEXT, lat REAL, lng REAL, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS job_checklist (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER, " +
                "\"position\" INTEGER, \"text\" TEXT, done INTEGER, done_at TEXT)",

                "CREATE TABLE IF NOT EXISTS job_parts (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER, " +
                "part_id INTEGER, qty INTEGER, unit_price REAL)",

                "CREATE TABLE IF NOT EXISTS parts (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, sku TEXT, name TEXT, " +
                "unit_price REAL, stock INTEGER)",

                "CREATE TABLE IF NOT EXISTS job_photos (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER, filename TEXT, " +
                "content_type TEXT, size_bytes INTEGER, caption TEXT, created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS job_signatures (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER, " +
                "signer_name TEXT, signature_png TEXT, signed_at TEXT)",

                "CREATE TABLE IF NOT EXISTS messages (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, job_id INTEGER, " +
                "from_user_id INTEGER, to_user_id INTEGER, body TEXT, " +
                "created_at TEXT, read_at TEXT)",

                "CREATE TABLE IF NOT EXISTS audit_log (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, action TEXT, " +
                "entity TEXT, entity_id INTEGER, detail TEXT, ip TEXT, " +
                "created_at TEXT)"
            };

            string[] vIndexes =
            {
                "CREATE INDEX IF NOT EXISTS idx_jobs_tech ON jobs (technician_id)",
                "CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs (status)",
                "CREATE INDEX IF NOT EXISTS idx_jobs_sched ON jobs (scheduled_start)",
                "CREATE INDEX IF NOT EXISTS idx_jobs_ref ON jobs (reference)",
                "CREATE INDEX IF NOT EXISTS idx_events_job ON job_events (job_id)",
                "CREATE INDEX IF NOT EXISTS idx_messages_job ON messages (job_id)",
                "CREATE INDEX IF NOT EXISTS idx_checklist_job ON job_checklist (job_id)",
                "CREATE INDEX IF NOT EXISTS idx_jobparts_job ON job_parts (job_id)",
                "CREATE INDEX IF NOT EXISTS idx_sites_customer ON sites (customer_id)",
                "CREATE INDEX IF NOT EXISTS idx_assets_site ON assets (site_id)"
            };

            using (SqliteConnection oConn = Acquire())
            {
                for (int vI = 0; vI < vTables.Length; vI++)
                    ExecNonQuery(oConn, vTables[vI]);
                for (int vI = 0; vI < vIndexes.Length; vI++)
                    ExecNonQuery(oConn, vIndexes[vI]);
            }
        }

        // Creates the dispatcher account when no dispatcher exists yet.
        public void SeedAdmin(string aUser, string aPasswordHash)
        {
            using (SqliteConnection oConn = Acquire())
            {
                long vCount = ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM users WHERE role = :r",
                    ":r", FieldConst.CS_ROLE_DISPATCHER);
                if (vCount > 0)
                    return;

                ExecNonQuery(oConn,
                    "INSERT INTO users " +
                    "(username, password_hash, role, display_name, phone, " +
                    "avatar_initials, created_at) " +
                    "VALUES (:u, :p, :r, :dn, :ph, :ai, :c)",
                    ":u", aUser, ":p", aPasswordHash, ":r", FieldConst.CS_ROLE_DISPATCHER,
                    ":dn", "Dispatch Desk", ":ph", "+34 900 123 456", ":ai", "DD",
                    ":c", FieldNowTimestamp());
            }
        }

        // ----- seed helpers (nested routines in the Delphi original) ----- //

        private sealed class TSeedContext
        {
            public TFieldRandom Rnd;
            public SqliteConnection Conn;
            public SqliteTransaction Tx;
            public string Hash = "";
            public DateTime Now;
            public DateTime Today;
            public long EventSeq;
            public long CheckSeq;
            public Dictionary<string, byte> Refs = new Dictionary<string, byte>();
        }

        private static long MaxIdOf(SqliteConnection aConn, string aTable)
        {
            return ExecScalarLong(aConn,
                "SELECT COALESCE(MAX(id), 0) AS m FROM " + aTable);
        }

        // One-time demo data. Everything below is deterministic (the seed is
        // pinned) so the demo always looks the same, and the whole thing runs
        // inside a single transaction: ~9000 inserts outside one would take
        // minutes on SQLite. Rows are inserted with EXPLICIT ids taken from a
        // per-table counter seeded from MAX(id).
        public void SeedDemoData()
        {
            const string CS_DEMO_PASSWORD = "demo1234";

            string[] CS_CITY_NAME = { "Madrid", "Barcelona", "Valencia", "Seville", "Bilbao" };
            double[] CS_CITY_LAT = { 40.4168, 41.3874, 39.4699, 37.3891, 43.263 };
            double[] CS_CITY_LNG = { -3.7038, 2.1686, -0.3763, -5.9845, -2.935 };

            string[] CS_TECH_NAME = { "Javier Lopez", "Marta Garcia", "Ruben Ortiz",
                "Elena Navarro", "Diego Ramos", "Sofia Herrera", "Andres Molina",
                "Lucia Vidal", "Pablo Serrano", "Nuria Castro" };
            string[] CS_TECH_USER = { "jlopez", "mgarcia", "rortiz", "enavarro",
                "dramos", "sherrera", "amolina", "lvidal", "pserrano", "ncastro" };
            string[] CS_TECH_INITIALS = { "JL", "MG", "RO", "EN", "DR", "SH", "AM",
                "LV", "PS", "NC" };
            string[] CS_SKILLS = { "HVAC,Boilers", "Electrical", "Refrigeration,HVAC",
                "Plumbing", "Controls,Electrical" };

            string[] CS_CO_A = { "Aurora", "Iberia", "Delta", "Nova", "Atlas", "Vertex",
                "Solera", "Marea", "Pinar", "Costa", "Duero", "Ebro", "Sierra",
                "Cantabria", "Levante", "Aljibe", "Orion", "Zenit", "Boreal", "Alameda" };
            string[] CS_CO_B = { "Foods", "Cold Storage", "Logistics", "Hospitality",
                "Retail", "Pharma", "Textiles", "Bakery", "Dairy", "Electronics",
                "Hotels", "Clinics" };
            string[] CS_CO_C = { "SL", "SA", "Group", "Iberica" };

            string[] CS_FIRST = { "Ana", "Carlos", "Beatriz", "David", "Eva", "Fernando",
                "Gloria", "Hector", "Irene", "Jorge", "Laura", "Miguel", "Nerea",
                "Oscar", "Paula", "Raul" };
            string[] CS_LAST = { "Alonso", "Blanco", "Cabrera", "Duran", "Esteban",
                "Fuentes", "Gil", "Hidalgo", "Iglesias", "Jimenez", "Leon", "Marin",
                "Nieto", "Pardo", "Quintana", "Rios" };

            string[] CS_STREET = { "Calle Mayor", "Avenida del Puerto",
                "Poligono Industrial Sur", "Calle Alcala", "Ronda de Levante",
                "Camino de la Vega", "Avenida de la Industria", "Calle Real",
                "Paseo del Prado", "Travesia del Molino" };

            string[] CS_SITE_NAME = { "Head Office", "Warehouse 2", "Plant North",
                "Distribution Centre", "Branch South", "Workshop", "Cold Store",
                "Logistics Hub" };
            string[] CS_ACCESS_NOTE = {
                "Report to reception, ask for the facilities manager.",
                "Loading bay access only before 11:00.",
                "Plant room key is held by site security.",
                "Parking on the street, gate code changes monthly.",
                "Roof access requires a harness and a permit." };

            string[] CS_ASSET_ROOT = { "Chiller CH-1", "AHU-2", "Boiler B-1",
                "Cold Room 3", "Compressor C-2", "Rooftop Unit RTU-1",
                "Freezer Unit FZ-2", "Air Handler AH-3" };
            string[] CS_ASSET_CHILD = { "Fan Motor", "Control Panel", "Condenser Coil",
                "Pump", "Expansion Valve", "Filter Bank" };
            string[] CS_ASSET_MODEL = { "Carrier 30XA", "Daikin EWAD", "Trane RTAC",
                "Mitsubishi PUHZ", "York YCIV", "Bitzer 4FES" };

            string[] CS_JOB_TITLE = { "Annual boiler service", "Chiller not cooling",
                "Replace condenser fan motor", "Quarterly HVAC inspection",
                "Emergency: refrigeration alarm", "Cold room temperature drift",
                "Leak on chilled water circuit", "Replace air filters",
                "Compressor high pressure trip", "Thermostat calibration",
                "Annual safety inspection", "Noisy fan bearing replacement",
                "Control panel fault finding", "Pump seal replacement",
                "Preventive maintenance visit", "Install new expansion valve" };

            string[] CS_JOB_DESC = {
                "Customer reports the unit runs but never reaches set point.",
                "Scheduled preventive maintenance as per the service contract.",
                "Alarm triggered overnight, site staff reset it twice.",
                "Intermittent fault, happens mostly in the afternoon.",
                "Follow-up visit after the last repair, parts were on order.",
                "Unit is tripping on high pressure under full load.",
                "Routine inspection, record readings and check the safeties.",
                "Water on the floor under the plant room header.",
                "Replace the worn component and test the full cycle.",
                "Site reports abnormal noise coming from the fan section." };

            string[] CS_CHECK_ITEM = { "Isolate power", "Check refrigerant pressures",
                "Clean condenser coil", "Test safety cut-outs", "Record readings",
                "Customer walkthrough", "Inspect electrical connections",
                "Check belt tension", "Verify control setpoints", "Leave site tidy" };

            // The first six SKUs are deliberately short so they can be typed by hand
            // into the scanner demo without an actual barcode scanner.
            string[] CS_PART_SKU = { "FLT-01", "MTR-02", "CAP-03", "VLV-04", "BLT-05",
                "FUS-06", "FLT-1042", "FLT-1043", "CTR-2210", "CTR-2211", "MTR-3305",
                "MTR-3306", "CMP-4401", "CMP-4402", "VLV-5120", "VLV-5121", "SNS-6010",
                "SNS-6011", "THR-6500", "PMP-7001", "PMP-7002", "BRG-7310", "BLT-7420",
                "GAS-7800", "REF-8100", "REF-8101", "OIL-8200", "PCB-8600", "PCB-8601",
                "FUS-8900", "RLY-9010", "RLY-9011", "CAP-9200", "CAP-9201", "HOS-9400",
                "DRY-9500", "ISO-9600", "FAN-9700", "FAN-9701", "KIT-9900" };
            string[] CS_PART_NAME = { "HEPA filter 600x600", "Condenser fan motor 1/4HP",
                "Run capacitor 35uF", "Thermostatic expansion valve", "V-belt A38",
                "Ceramic fuse 20A", "Panel filter G4 592x592", "Bag filter F7 592x592",
                "Contactor 32A", "Contactor 50A", "Fan motor 1/2HP", "Pump motor 0.75kW",
                "Scroll compressor 5HP", "Compressor mounting kit", "Solenoid valve 3/8",
                "Ball valve 1 inch", "Temperature sensor PT1000",
                "Pressure transducer 0-30 bar", "Digital thermostat",
                "Circulator pump 25-60", "Condensate pump", "Fan bearing 6204",
                "V-belt B50", "Gasket set plate heat exchanger", "Refrigerant R410A 10kg",
                "Refrigerant R134a 12kg", "Compressor oil POE 1L",
                "Control board universal", "Display board HMI", "Fuse set assorted",
                "Relay 24V DPDT", "Relay 230V SPDT", "Start capacitor 88uF",
                "Dual run capacitor 45+5uF", "Flexible hose 1/2 inch", "Filter drier 3/8",
                "Vibration isolator set", "Axial fan 450mm", "Centrifugal fan 250mm",
                "Annual service kit" };

            string[] CS_MSG_TECH = { "On my way, ETA 20 min",
                "Arrived on site, checking the unit now",
                "Part not in van, ordering it today",
                "Customer says the alarm is still sounding",
                "Found a failed contactor, replacing it now",
                "All tests passed, I am closing the job",
                "Access blocked, waiting for the site manager",
                "I need a second pair of hands for the coil" };
            string[] CS_MSG_DISP = { "Thanks, keep me posted",
                "Site contact is Ana on the front desk",
                "Ok, I will raise the purchase order",
                "Can you send a photo of the nameplate?",
                "Priority raised to high, the site is losing stock",
                "Nice one, the invoice goes out tomorrow",
                "I called them, someone is coming down now",
                "I can send Diego over after lunch" };

            string[] CS_AUDIT_ACTION = { "job.create", "job.assign", "job.status",
                "part.add", "login" };
            string[] CS_AUDIT_ENTITY = { "job", "job", "job", "job_part", "user" };

            double[] CS_DURATION = { 1, 1.5, 2, 3 };

            using (SqliteConnection oConn = Acquire())
            {
                // Seed once, and only once: any job at all means this database is live.
                if (ExecScalarLong(oConn, "SELECT COUNT(*) FROM jobs") > 0)
                    return;

                TSeedContext oCtx = new TSeedContext();
                oCtx.Conn = oConn;

                // Reproducible demo data, on purpose: the generator is pinned.
                TFieldRandom oRnd = new TFieldRandom(20260817);
                oCtx.Rnd = oRnd;
                lock (GRandomLock)
                {
                    GRandom.Seed = 20260817;
                }

                DateTime vNow = DateTime.Now;
                DateTime vToday = vNow.Date;
                oCtx.Now = vNow;
                oCtx.Today = vToday;
                // bcrypt is deliberately slow: hash the shared demo password ONCE and
                // reuse the very same hash for all 14 demo accounts.
                string vHash = Bcrypt.BcryptHash(CS_DEMO_PASSWORD);
                oCtx.Hash = vHash;

                long vDispatcherId = 0;
                long vManagerId = 0;
                vDispatcherId = ExecScalarLong(oConn,
                    "SELECT id FROM users WHERE role = :r ORDER BY id ASC LIMIT 1",
                    ":r", FieldConst.CS_ROLE_DISPATCHER);

                long vUserSeq = MaxIdOf(oConn, "users");
                long vTechSeq = MaxIdOf(oConn, "technicians");
                long vCustSeq = MaxIdOf(oConn, "customers");
                long vSiteSeq = MaxIdOf(oConn, "sites");
                long vAssetSeq = MaxIdOf(oConn, "assets");
                long vPartSeq = MaxIdOf(oConn, "parts");
                long vJobSeq = MaxIdOf(oConn, "jobs");
                oCtx.EventSeq = MaxIdOf(oConn, "job_events");
                oCtx.CheckSeq = MaxIdOf(oConn, "job_checklist");
                long vJobPartSeq = MaxIdOf(oConn, "job_parts");
                long vMsgSeq = MaxIdOf(oConn, "messages");
                long vAuditSeq = MaxIdOf(oConn, "audit_log");

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                {
                    oCtx.Tx = oTx;

                    // Every INSERT is prepared exactly once; the loops only rebind params.
                    using (SqliteCommand oQUser = NewSeedCmd(oConn, oTx,
                        "INSERT INTO users (id, username, password_hash, role, " +
                        "display_name, phone, avatar_initials, created_at) " +
                        "VALUES (:id, :un, :pw, :ro, :dn, :ph, :ai, :ca)",
                        ":id", ":un", ":pw", ":ro", ":dn", ":ph", ":ai", ":ca"))
                    using (SqliteCommand oQTech = NewSeedCmd(oConn, oTx,
                        "INSERT INTO technicians (id, user_id, skills, home_lat, " +
                        "home_lng, active, hourly_rate) " +
                        "VALUES (:id, :uid, :sk, :hla, :hln, :ac, :hr)",
                        ":id", ":uid", ":sk", ":hla", ":hln", ":ac", ":hr"))
                    using (SqliteCommand oQCust = NewSeedCmd(oConn, oTx,
                        "INSERT INTO customers (id, name, contact, email, phone, " +
                        "address, city, lat, lng, created_at) " +
                        "VALUES (:id, :nm, :co, :em, :ph, :ad, :ci, :la, :ln, :ca)",
                        ":id", ":nm", ":co", ":em", ":ph", ":ad", ":ci", ":la", ":ln", ":ca"))
                    using (SqliteCommand oQSite = NewSeedCmd(oConn, oTx,
                        "INSERT INTO sites (id, customer_id, name, address, lat, lng, " +
                        "access_notes) VALUES (:id, :cid, :nm, :ad, :la, :ln, :an)",
                        ":id", ":cid", ":nm", ":ad", ":la", ":ln", ":an"))
                    using (SqliteCommand oQAsset = NewSeedCmd(oConn, oTx,
                        "INSERT INTO assets (id, site_id, parent_id, name, model, " +
                        "serial, installed_at, warranty_until, status) " +
                        "VALUES (:id, :sid, :pid, :nm, :mo, :se, :ia, :wu, :st)",
                        ":id", ":sid", ":pid", ":nm", ":mo", ":se", ":ia", ":wu", ":st"))
                    using (SqliteCommand oQPart = NewSeedCmd(oConn, oTx,
                        "INSERT INTO parts (id, sku, name, unit_price, stock) " +
                        "VALUES (:id, :sk, :nm, :up, :st)",
                        ":id", ":sk", ":nm", ":up", ":st"))
                    using (SqliteCommand oQJob = NewSeedCmd(oConn, oTx,
                        "INSERT INTO jobs (id, reference, customer_id, site_id, " +
                        "asset_id, technician_id, title, description, priority, " +
                        "status, scheduled_start, scheduled_end, actual_start, " +
                        "actual_end, sla_due_at, created_at) VALUES (:id, :rf, :cid, " +
                        ":sid, :aid, :tid, :ti, :de, :pr, :st, :ss, :se, :ast, :aen, " +
                        ":sla, :cre)",
                        ":id", ":rf", ":cid", ":sid", ":aid", ":tid", ":ti", ":de",
                        ":pr", ":st", ":ss", ":se", ":ast", ":aen", ":sla", ":cre"))
                    using (SqliteCommand oQEvent = NewSeedCmd(oConn, oTx,
                        "INSERT INTO job_events (id, job_id, user_id, kind, detail, " +
                        "lat, lng, created_at) " +
                        "VALUES (:id, :jid, :uid, :ki, :de, :la, :ln, :ca)",
                        ":id", ":jid", ":uid", ":ki", ":de", ":la", ":ln", ":ca"))
                    using (SqliteCommand oQCheck = NewSeedCmd(oConn, oTx,
                        "INSERT INTO job_checklist (id, job_id, \"position\", " +
                        "\"text\", done, done_at) VALUES (:id, :jid, :po, :tx, :dn, :da)",
                        ":id", ":jid", ":po", ":tx", ":dn", ":da"))
                    using (SqliteCommand oQJobPart = NewSeedCmd(oConn, oTx,
                        "INSERT INTO job_parts (id, job_id, part_id, qty, unit_price) " +
                        "VALUES (:id, :jid, :pid, :qt, :up)",
                        ":id", ":jid", ":pid", ":qt", ":up"))
                    using (SqliteCommand oQMsg = NewSeedCmd(oConn, oTx,
                        "INSERT INTO messages (id, job_id, from_user_id, to_user_id, " +
                        "body, created_at, read_at) " +
                        "VALUES (:id, :jid, :fu, :tu, :bo, :ca, :ra)",
                        ":id", ":jid", ":fu", ":tu", ":bo", ":ca", ":ra"))
                    using (SqliteCommand oQAudit = NewSeedCmd(oConn, oTx,
                        "INSERT INTO audit_log (id, user_id, action, entity, " +
                        "entity_id, detail, ip, created_at) " +
                        "VALUES (:id, :uid, :ac, :en, :ei, :de, :ip, :ca)",
                        ":id", ":uid", ":ac", ":en", ":ei", ":de", ":ip", ":ca"))
                    {
                        long[] vTechId = new long[10];
                        long[] vTechUser = new long[10];
                        int[] vTechCity = new int[10];

                        long[] vCustId = new long[120];
                        int[] vCustCity = new int[120];
                        double[] vCustLat = new double[120];
                        double[] vCustLng = new double[120];

                        long[] vSiteId = new long[260];
                        int[] vSiteCust = new int[260];
                        double[] vSiteLat = new double[260];
                        double[] vSiteLng = new double[260];
                        int vSiteCount = 0;

                        long[] vAssetId = new long[1400];
                        int[] vAssetSite = new int[1400];
                        int vAssetCount = 0;

                        long[] vPartId = new long[40];
                        double[] vPartPrice = new double[40];

                        long[] vCandJob = new long[900];
                        long[] vCandTech = new long[900];
                        DateTime[] vCandWhen = new DateTime[900];
                        int vCandCount = 0;
                        long[] vSeededJobIds = new long[900];

                        int vI, vJ, vK, vN, vRoots, vKids, vRoll;
                        int vCityIdx, vTechIdx, vSiteIdx, vAssetIdx, vCustIdx;
                        long vParentId;
                        string vFirst, vLast, vName, vTitle, vDesc, vPriority, vStatus, vRef;
                        DateTime vCreated, vStart, vEnd, vActualStart, vActualEnd, vSla, vWhen;
                        double vDuration;
                        long vJobId, vTechRow, vTechUserId;
                        int vMultiDay, vUnassignedDone, vFutureIdx;
                        double vLat, vLng;

                        // ----- 1. users + technicians ----- //
                        for (vI = 0; vI <= 9; vI++)
                        {
                            vCityIdx = vI % 5;
                            vTechCity[vI] = vCityIdx;
                            vUserSeq++;
                            vTechUser[vI] = vUserSeq;
                            InsUser(oQUser, oCtx, vUserSeq, CS_TECH_USER[vI],
                                FieldConst.CS_ROLE_TECHNICIAN, CS_TECH_NAME[vI],
                                Phone6(oRnd), CS_TECH_INITIALS[vI],
                                vNow.AddDays(-400 + vI * 3));

                            vTechSeq++;
                            vTechId[vI] = vTechSeq;
                            SetP(oQTech, ":id", vTechSeq);
                            SetP(oQTech, ":uid", vTechUser[vI]);
                            SetP(oQTech, ":sk", CS_SKILLS[vI % 5]);
                            SetP(oQTech, ":hla", CS_CITY_LAT[vCityIdx] + Jit(oRnd, 0.045));
                            SetP(oQTech, ":hln", CS_CITY_LNG[vCityIdx] + Jit(oRnd, 0.045));
                            // Exactly one technician is off the road.
                            SetP(oQTech, ":ac", vI == 9 ? 0 : 1);
                            SetP(oQTech, ":hr", (double)(38 + oRnd.Next(25)));
                            oQTech.ExecuteNonQuery();
                        }

                        vUserSeq++;
                        InsUser(oQUser, oCtx, vUserSeq, "dispatch2",
                            FieldConst.CS_ROLE_DISPATCHER, "Carlos Pena", Phone9(oRnd),
                            "CP", vNow.AddDays(-380));
                        vUserSeq++;
                        vManagerId = vUserSeq;
                        InsUser(oQUser, oCtx, vUserSeq, "manager",
                            FieldConst.CS_ROLE_MANAGER, "Isabel Marin", Phone9(oRnd),
                            "IM", vNow.AddDays(-395));
                        vUserSeq++;
                        InsUser(oQUser, oCtx, vUserSeq, "acme",
                            FieldConst.CS_ROLE_CUSTOMER, "Acme Facilities", Phone9(oRnd),
                            "AF", vNow.AddDays(-300));
                        vUserSeq++;
                        InsUser(oQUser, oCtx, vUserSeq, "northwind",
                            FieldConst.CS_ROLE_CUSTOMER, "Northwind Retail", Phone9(oRnd),
                            "NR", vNow.AddDays(-280));

                        if (vDispatcherId == 0)
                            vDispatcherId = vManagerId;

                        // ----- 2/3. customers, clustered on the five city centres ----- //
                        for (vI = 0; vI <= 119; vI++)
                        {
                            vCityIdx = vI % 5;
                            vCustCity[vI] = vCityIdx;
                            vLat = CS_CITY_LAT[vCityIdx] + Jit(oRnd, 0.055);
                            vLng = CS_CITY_LNG[vCityIdx] + Jit(oRnd, 0.055);
                            vCustLat[vI] = vLat;
                            vCustLng[vI] = vLng;

                            vName = CS_CO_A[vI % 20] + " " + CS_CO_B[(vI / 20) % 12] +
                                " " + CS_CO_C[(vI / 5) % 4];
                            vFirst = CS_FIRST[oRnd.Next(16)];
                            vLast = CS_LAST[oRnd.Next(16)];

                            vCustSeq++;
                            vCustId[vI] = vCustSeq;
                            SetP(oQCust, ":id", vCustSeq);
                            SetP(oQCust, ":nm", vName);
                            SetP(oQCust, ":co", vFirst + " " + vLast);
                            SetP(oQCust, ":em",
                                (vFirst.Substring(0, 1) + "." + vLast).ToLowerInvariant() +
                                "@" + CS_CO_A[vI % 20].ToLowerInvariant() + ".example");
                            SetP(oQCust, ":ph", Phone9(oRnd));
                            SetP(oQCust, ":ad", CS_STREET[oRnd.Next(10)] + " " +
                                (1 + oRnd.Next(180)).ToString(CultureInfo.InvariantCulture) +
                                ", " + CS_CITY_NAME[vCityIdx]);
                            SetP(oQCust, ":ci", CS_CITY_NAME[vCityIdx]);
                            SetP(oQCust, ":la", vLat);
                            SetP(oQCust, ":ln", vLng);
                            SetP(oQCust, ":ca", FormatFieldTimestamp(
                                vNow.AddDays(-30).AddDays(-oRnd.Next(700))));
                            oQCust.ExecuteNonQuery();
                        }

                        // ----- 4. sites: one each, plus a second one for 60 of them ----- //
                        for (vI = 0; vI <= 119; vI++)
                        {
                            vN = ((vI % 2) == 0) ? 2 : 1;
                            for (vJ = 0; vJ <= vN - 1; vJ++)
                            {
                                vSiteSeq++;
                                vLat = vCustLat[vI] + Jit(oRnd, 0.02);
                                vLng = vCustLng[vI] + Jit(oRnd, 0.02);
                                vSiteId[vSiteCount] = vSiteSeq;
                                vSiteCust[vSiteCount] = vI;
                                vSiteLat[vSiteCount] = vLat;
                                vSiteLng[vSiteCount] = vLng;
                                vSiteCount++;

                                SetP(oQSite, ":id", vSiteSeq);
                                SetP(oQSite, ":cid", vCustId[vI]);
                                if (vJ == 0)
                                    SetP(oQSite, ":nm", CS_SITE_NAME[0]);
                                else
                                    SetP(oQSite, ":nm", CS_SITE_NAME[1 + oRnd.Next(7)]);
                                SetP(oQSite, ":ad", CS_STREET[oRnd.Next(10)] + " " +
                                    (1 + oRnd.Next(180)).ToString(CultureInfo.InvariantCulture) +
                                    ", " + CS_CITY_NAME[vCustCity[vI]]);
                                SetP(oQSite, ":la", vLat);
                                SetP(oQSite, ":ln", vLng);
                                SetP(oQSite, ":an", CS_ACCESS_NOTE[oRnd.Next(5)]);
                                oQSite.ExecuteNonQuery();
                            }
                        }

                        // ----- 5. assets, two levels deep ----- //
                        for (vI = 0; vI <= vSiteCount - 1; vI++)
                        {
                            vRoots = 1;
                            if (oRnd.Next(100) < 32)
                                vRoots++;
                            if (oRnd.Next(100) < 10)
                                vRoots++;
                            for (vJ = 0; vJ <= vRoots - 1; vJ++)
                            {
                                vAssetSeq++;
                                vParentId = vAssetSeq;
                                vAssetId[vAssetCount] = vAssetSeq;
                                vAssetSite[vAssetCount] = vI;
                                vAssetCount++;

                                SetP(oQAsset, ":id", vAssetSeq);
                                SetP(oQAsset, ":sid", vSiteId[vI]);
                                SetP(oQAsset, ":pid", 0L);
                                SetP(oQAsset, ":nm", CS_ASSET_ROOT[(vJ + vI) % 8]);
                                SetP(oQAsset, ":mo", CS_ASSET_MODEL[oRnd.Next(6)]);
                                SetP(oQAsset, ":se", "SN-" +
                                    oRnd.Next(1000000).ToString("D6", CultureInfo.InvariantCulture));
                                vWhen = vNow.AddDays(-365 * (1 + oRnd.Next(9)));
                                vWhen = vWhen.AddDays(-oRnd.Next(365));
                                SetP(oQAsset, ":ia", FormatFieldTimestamp(vWhen));
                                SetP(oQAsset, ":wu", FormatFieldTimestamp(
                                    vWhen.AddDays(365 * (2 + oRnd.Next(8)))));
                                vRoll = oRnd.Next(100);
                                if (vRoll < 70)
                                    SetP(oQAsset, ":st", "ok");
                                else if (vRoll < 90)
                                    SetP(oQAsset, ":st", "due");
                                else
                                    SetP(oQAsset, ":st", "fault");
                                oQAsset.ExecuteNonQuery();

                                // Roughly half of the roots carry one or two sub-components.
                                if (oRnd.Next(100) < 45)
                                {
                                    vKids = 1;
                                    if (oRnd.Next(100) < 30)
                                        vKids++;
                                    for (vK = 0; vK <= vKids - 1; vK++)
                                    {
                                        vAssetSeq++;
                                        vAssetId[vAssetCount] = vAssetSeq;
                                        vAssetSite[vAssetCount] = vI;
                                        vAssetCount++;

                                        SetP(oQAsset, ":id", vAssetSeq);
                                        SetP(oQAsset, ":sid", vSiteId[vI]);
                                        SetP(oQAsset, ":pid", vParentId);
                                        SetP(oQAsset, ":nm", CS_ASSET_CHILD[(vK + vJ) % 6]);
                                        SetP(oQAsset, ":mo", CS_ASSET_MODEL[oRnd.Next(6)]);
                                        SetP(oQAsset, ":se", "SN-" +
                                            oRnd.Next(1000000).ToString("D6",
                                                CultureInfo.InvariantCulture));
                                        vWhen = vNow.AddDays(-365 * (1 + oRnd.Next(9)));
                                        vWhen = vWhen.AddDays(-oRnd.Next(365));
                                        SetP(oQAsset, ":ia", FormatFieldTimestamp(vWhen));
                                        SetP(oQAsset, ":wu", FormatFieldTimestamp(
                                            vWhen.AddDays(365 * (2 + oRnd.Next(8)))));
                                        vRoll = oRnd.Next(100);
                                        if (vRoll < 70)
                                            SetP(oQAsset, ":st", "ok");
                                        else if (vRoll < 90)
                                            SetP(oQAsset, ":st", "due");
                                        else
                                            SetP(oQAsset, ":st", "fault");
                                        oQAsset.ExecuteNonQuery();
                                    }
                                }
                            }
                        }

                        // ----- 9. parts ----- //
                        for (vI = 0; vI <= 39; vI++)
                        {
                            vPartSeq++;
                            vPartId[vI] = vPartSeq;
                            vPartPrice[vI] = 4 + oRnd.Next(47600) / 100.0;
                            SetP(oQPart, ":id", vPartSeq);
                            SetP(oQPart, ":sk", CS_PART_SKU[vI]);
                            SetP(oQPart, ":nm", CS_PART_NAME[vI]);
                            SetP(oQPart, ":up", vPartPrice[vI]);
                            if (vI < 6)
                                SetP(oQPart, ":st", 40 + oRnd.Next(160));
                            else
                                SetP(oQPart, ":st", oRnd.Next(201));
                            oQPart.ExecuteNonQuery();
                        }

                        // ----- 6/7/8/10. jobs and their children ----- //
                        vMultiDay = 0;
                        vUnassignedDone = 0;
                        vFutureIdx = 0;

                        for (vJ = 0; vJ <= 899; vJ++)
                        {
                            vAssetIdx = oRnd.Next(vAssetCount);
                            vSiteIdx = vAssetSite[vAssetIdx];
                            vCustIdx = vSiteCust[vSiteIdx];
                            vCityIdx = vCustCity[vCustIdx];
                            vLat = vSiteLat[vSiteIdx];
                            vLng = vSiteLng[vSiteIdx];

                            vTitle = CS_JOB_TITLE[oRnd.Next(16)];
                            vDesc = CS_JOB_DESC[oRnd.Next(10)];
                            if (oRnd.Next(100) < 40)
                                vDesc = vDesc + " " + CS_JOB_DESC[oRnd.Next(10)];
                            vPriority = PickPriority(oRnd);
                            vDuration = CS_DURATION[oRnd.Next(4)];
                            vActualStart = DateTime.MinValue;
                            vActualEnd = DateTime.MinValue;
                            vTechRow = 0;
                            vTechUserId = 0;
                            vStatus = "";

                            if (vJ < 620)
                            {
                                // --- past work --- //
                                vStart = SlotStart(oRnd, vToday.AddDays(-(1 + oRnd.Next(360))));
                                vEnd = vStart.AddHours(vDuration);
                                vCreated = vStart.AddHours(-(2 + oRnd.Next(72)));
                                vRoll = oRnd.Next(100);
                                if (vRoll < 80)
                                    vStatus = FieldConst.CS_JOB_COMPLETE;
                                else if (vRoll < 85)
                                    vStatus = FieldConst.CS_JOB_CANCELLED;
                                else
                                    // The rest are still open, but never left in a live
                                    // state: 'enroute' and 'onsite' belong to the twelve
                                    // jobs happening right now, otherwise every technician
                                    // would read as busy on the presence board forever, and
                                    // 'new' belongs to the unassigned queue.
                                    vStatus = FieldConst.CS_JOB_SCHEDULED;

                                // Past work is always somebody's: the unassigned queue is
                                // built from future jobs only.
                                vTechIdx = PickTech(oRnd, vTechCity, vCityIdx, true);
                                vTechRow = vTechId[vTechIdx];
                                vTechUserId = vTechUser[vTechIdx];

                                vSla = vStart.AddHours(SlaHours(vPriority));
                                if (vStatus == FieldConst.CS_JOB_COMPLETE)
                                {
                                    vActualStart = vStart.AddMinutes(oRnd.Next(45));
                                    vActualEnd = vActualStart.AddHours(
                                        vDuration * (70 + oRnd.Next(60)) / 100.0);
                                    // ~12% of the completed work misses its SLA, so the
                                    // gauge on the manager report is never a flat 100%.
                                    if (oRnd.Next(100) < 12)
                                        vActualEnd = vSla.AddMinutes(10 + oRnd.Next(600));
                                }
                            }
                            else if (vJ < 632)
                            {
                                // --- 12 jobs that are live right now --- //
                                vStart = vToday.AddHours(8 + oRnd.Next(20) * 0.5);
                                vEnd = vStart.AddHours(vDuration);
                                vCreated = vNow.AddDays(-(1 + oRnd.Next(6)));
                                vTechIdx = PickTech(oRnd, vTechCity, vCityIdx, false);
                                vTechRow = vTechId[vTechIdx];
                                vTechUserId = vTechUser[vTechIdx];
                                if (vJ < 624)
                                    vStatus = FieldConst.CS_JOB_ENROUTE;
                                else if (vJ < 628)
                                    vStatus = FieldConst.CS_JOB_ONSITE;
                                else
                                    vStatus = FieldConst.CS_JOB_SCHEDULED;
                                vSla = vStart.AddHours(SlaHours(vPriority));
                                if (vStatus == FieldConst.CS_JOB_ONSITE)
                                    vActualStart = vNow.AddMinutes(-(5 + oRnd.Next(85)));
                            }
                            else if (vUnassignedDone < 35)
                            {
                                // --- the unassigned queue the dispatch board drags from --- //
                                vUnassignedDone++;
                                vStart = SlotStart(oRnd, vToday.AddDays(1 + oRnd.Next(10)));
                                vEnd = vStart.AddHours(vDuration);
                                vCreated = vNow.AddDays(-oRnd.Next(5));
                                vCreated = vCreated.AddHours(-oRnd.Next(24));
                                vStatus = FieldConst.CS_JOB_NEW;
                                vSla = vStart.AddHours(SlaHours(vPriority));
                            }
                            else
                            {
                                // --- ordinary scheduled work over the next three weeks --- //
                                vFutureIdx++;
                                vStart = SlotStart(oRnd, vToday.AddDays(1 + oRnd.Next(21)));
                                if (((vFutureIdx % 6) == 0) && (vMultiDay < 40))
                                {
                                    // Genuine multi-day bars for the Gantt page.
                                    vMultiDay++;
                                    vEnd = vStart.AddDays(2 + oRnd.Next(4));
                                }
                                else
                                    vEnd = vStart.AddHours(vDuration);
                                vCreated = vNow.AddDays(-(1 + oRnd.Next(20)));
                                vTechIdx = PickTech(oRnd, vTechCity, vCityIdx, false);
                                vTechRow = vTechId[vTechIdx];
                                vTechUserId = vTechUser[vTechIdx];
                                vStatus = FieldConst.CS_JOB_SCHEDULED;
                                vSla = vStart.AddHours(SlaHours(vPriority));
                            }

                            vJobSeq++;
                            vJobId = vJobSeq;
                            vSeededJobIds[vJ] = vJobId;
                            vRef = NewRef(oRnd, oCtx.Refs, vCreated);

                            // ~10% of the jobs deliberately carry no asset, which is what
                            // keeps the LEFT JOIN on assets honest.
                            if (oRnd.Next(100) < 10)
                                vAssetIdx = -1;

                            if (vAssetIdx >= 0)
                                InsJob(oQJob, vJobId, vRef, vCustId[vCustIdx],
                                    vSiteId[vSiteIdx], vAssetId[vAssetIdx], vTechRow,
                                    vTitle, vDesc, vPriority, vStatus, vStart, vEnd,
                                    vActualStart, vActualEnd, vSla, vCreated);
                            else
                                InsJob(oQJob, vJobId, vRef, vCustId[vCustIdx],
                                    vSiteId[vSiteIdx], 0, vTechRow, vTitle, vDesc,
                                    vPriority, vStatus, vStart, vEnd, vActualStart,
                                    vActualEnd, vSla, vCreated);

                            // ----- 7. events ----- //
                            InsEvent(oQEvent, oCtx, vJobId, vDispatcherId, "created",
                                vTitle, 0, 0, vCreated);
                            if ((vTechRow > 0) && (vStatus != FieldConst.CS_JOB_NEW))
                                InsEvent(oQEvent, oCtx, vJobId, vDispatcherId, "assigned",
                                    "", 0, 0, vCreated.AddMinutes(12));

                            if (vStatus == FieldConst.CS_JOB_ENROUTE)
                                InsEvent(oQEvent, oCtx, vJobId, vTechUserId, "enroute", "",
                                    vLat + Jit(oRnd, 0.02), vLng + Jit(oRnd, 0.02),
                                    vNow.AddMinutes(-oRnd.Next(40)));
                            else if (vStatus == FieldConst.CS_JOB_ONSITE)
                            {
                                InsEvent(oQEvent, oCtx, vJobId, vTechUserId, "enroute", "",
                                    vLat + Jit(oRnd, 0.02), vLng + Jit(oRnd, 0.02),
                                    vActualStart.AddMinutes(-22));
                                InsEvent(oQEvent, oCtx, vJobId, vTechUserId, "onsite", "",
                                    vLat + Jit(oRnd, 0.002), vLng + Jit(oRnd, 0.002),
                                    vActualStart);
                            }
                            else if (vStatus == FieldConst.CS_JOB_COMPLETE)
                            {
                                InsEvent(oQEvent, oCtx, vJobId, vTechUserId, "enroute", "",
                                    vLat + Jit(oRnd, 0.02), vLng + Jit(oRnd, 0.02),
                                    vActualStart.AddMinutes(-25));
                                InsEvent(oQEvent, oCtx, vJobId, vTechUserId, "onsite", "",
                                    vLat + Jit(oRnd, 0.002), vLng + Jit(oRnd, 0.002),
                                    vActualStart);
                                InsEvent(oQEvent, oCtx, vJobId, vTechUserId, "completed", "",
                                    vLat + Jit(oRnd, 0.002), vLng + Jit(oRnd, 0.002),
                                    vActualEnd);
                                // Satisfaction lives here: there is no rating column, only
                                // this event, weighted towards 4 and 5 like real survey data.
                                if (oRnd.Next(100) < 65)
                                {
                                    vRoll = oRnd.Next(100);
                                    if (vRoll < 4)
                                        vName = "1";
                                    else if (vRoll < 10)
                                        vName = "2";
                                    else if (vRoll < 22)
                                        vName = "3";
                                    else if (vRoll < 58)
                                        vName = "4";
                                    else
                                        vName = "5";
                                    InsEvent(oQEvent, oCtx, vJobId, 0, "rating", vName, 0, 0,
                                        vActualEnd.AddHours(1 + oRnd.Next(48)));
                                }
                                if (oRnd.Next(100) < 30)
                                    InsEvent(oQEvent, oCtx, vJobId, vDispatcherId,
                                        "approved", "", 0, 0,
                                        vActualEnd.AddHours(2 + oRnd.Next(72)));
                            }

                            // ----- 8. checklist ----- //
                            vN = 3 + oRnd.Next(4);
                            vK = oRnd.Next(10);
                            for (vI = 0; vI <= vN - 1; vI++)
                            {
                                if (vStatus == FieldConst.CS_JOB_COMPLETE)
                                    InsCheck(oQCheck, oCtx, vJobId, vI + 1,
                                        CS_CHECK_ITEM[(vK + vI) % 10], true, vActualEnd);
                                else if (vStatus == FieldConst.CS_JOB_ONSITE)
                                    InsCheck(oQCheck, oCtx, vJobId, vI + 1,
                                        CS_CHECK_ITEM[(vK + vI) % 10], vI < (vN / 2), vNow);
                                else
                                    InsCheck(oQCheck, oCtx, vJobId, vI + 1,
                                        CS_CHECK_ITEM[(vK + vI) % 10], false,
                                        DateTime.MinValue);
                            }

                            // ----- 10. parts used ----- //
                            if ((vStatus == FieldConst.CS_JOB_COMPLETE) && (oRnd.Next(100) < 70))
                            {
                                vN = oRnd.Next(5);
                                for (vI = 0; vI <= vN - 1; vI++)
                                {
                                    vK = oRnd.Next(40);
                                    vJobPartSeq++;
                                    SetP(oQJobPart, ":id", vJobPartSeq);
                                    SetP(oQJobPart, ":jid", vJobId);
                                    SetP(oQJobPart, ":pid", vPartId[vK]);
                                    SetP(oQJobPart, ":qt", 1 + oRnd.Next(4));
                                    SetP(oQJobPart, ":up", vPartPrice[vK]);
                                    oQJobPart.ExecuteNonQuery();
                                }
                            }

                            if (vTechUserId > 0)
                            {
                                vCandJob[vCandCount] = vJobId;
                                vCandTech[vCandCount] = vTechUserId;
                                vCandWhen[vCandCount] = vCreated;
                                vCandCount++;
                            }
                        }

                        // ----- 11. dispatcher / technician conversations on 40 jobs ----- //
                        if (vCandCount > 0)
                        {
                            vK = vCandCount / 40;
                            if (vK < 1)
                                vK = 1;
                            for (vI = 0; vI <= 39; vI++)
                            {
                                vJ = vI * vK;
                                if (vJ >= vCandCount)
                                    break;
                                vN = 2 + oRnd.Next(5);
                                vWhen = vCandWhen[vJ].AddHours(1 + oRnd.Next(20));
                                for (vRoll = 0; vRoll <= vN - 1; vRoll++)
                                {
                                    vMsgSeq++;
                                    SetP(oQMsg, ":id", vMsgSeq);
                                    SetP(oQMsg, ":jid", vCandJob[vJ]);
                                    if ((vRoll % 2) == 0)
                                    {
                                        SetP(oQMsg, ":fu", vCandTech[vJ]);
                                        SetP(oQMsg, ":tu", vDispatcherId);
                                        SetP(oQMsg, ":bo", CS_MSG_TECH[oRnd.Next(8)]);
                                    }
                                    else
                                    {
                                        SetP(oQMsg, ":fu", vDispatcherId);
                                        SetP(oQMsg, ":tu", vCandTech[vJ]);
                                        SetP(oQMsg, ":bo", CS_MSG_DISP[oRnd.Next(8)]);
                                    }
                                    SetP(oQMsg, ":ca", FormatFieldTimestamp(vWhen));
                                    // The last message (and sometimes the one before it)
                                    // stays unread, so the badge counters are never all zero.
                                    if (vRoll < vN - 1 - ((oRnd.Next(100) < 40) ? 1 : 0))
                                        SetP(oQMsg, ":ra", FormatFieldTimestamp(
                                            vWhen.AddMinutes(5 + oRnd.Next(90))));
                                    else
                                        SetP(oQMsg, ":ra", "");
                                    oQMsg.ExecuteNonQuery();
                                    vWhen = vWhen.AddMinutes(6 + oRnd.Next(180));
                                }
                            }
                        }

                        // ----- 12. audit trail ----- //
                        for (vI = 0; vI <= 59; vI++)
                        {
                            vK = oRnd.Next(5);
                            vAuditSeq++;
                            SetP(oQAudit, ":id", vAuditSeq);
                            if (vK == 4)
                                SetP(oQAudit, ":uid", vTechUser[oRnd.Next(10)]);
                            else
                                SetP(oQAudit, ":uid", vDispatcherId);
                            SetP(oQAudit, ":ac", CS_AUDIT_ACTION[vK]);
                            SetP(oQAudit, ":en", CS_AUDIT_ENTITY[vK]);
                            if (vK == 4)
                                SetP(oQAudit, ":ei", 0L);
                            else
                                SetP(oQAudit, ":ei", vSeededJobIds[oRnd.Next(900)]);
                            SetP(oQAudit, ":de", CS_AUDIT_ACTION[vK] + " from the demo seed");
                            SetP(oQAudit, ":ip", "10.0." +
                                oRnd.Next(255).ToString(CultureInfo.InvariantCulture) + "." +
                                (1 + oRnd.Next(254)).ToString(CultureInfo.InvariantCulture));
                            DateTime vAuditAt = vNow.AddDays(-oRnd.Next(90));
                            vAuditAt = vAuditAt.AddHours(-oRnd.Next(24));
                            SetP(oQAudit, ":ca", FormatFieldTimestamp(vAuditAt));
                            oQAudit.ExecuteNonQuery();
                        }
                    }

                    oTx.Commit();
                }
            }
        }

        // Builds a prepared insert command with every parameter pre-created, so the
        // loops only rebind values (the Delphi original prepares each INSERT once).
        private static SqliteCommand NewSeedCmd(SqliteConnection aConn,
            SqliteTransaction aTx, string aSQL, params string[] aParams)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.Transaction = aTx;
            oCmd.CommandText = aSQL;
            for (int vI = 0; vI < aParams.Length; vI++)
                oCmd.Parameters.Add(new SqliteParameter(aParams[vI], DBNull.Value));
            return oCmd;
        }

        private static void SetP(SqliteCommand aCmd, string aName, object aValue)
        {
            aCmd.Parameters[aName].Value = aValue == null ? DBNull.Value : aValue;
        }

        // Symmetric jitter in [-aSpan, +aSpan].
        private static double Jit(TFieldRandom aRnd, double aSpan)
        {
            return ((aRnd.Next(2001) - 1000) / 1000.0) * aSpan;
        }

        private static string Phone6(TFieldRandom aRnd)
        {
            return "+34 6" + aRnd.Next(100).ToString("D2", CultureInfo.InvariantCulture) +
                " " + aRnd.Next(1000).ToString("D3", CultureInfo.InvariantCulture) +
                " " + aRnd.Next(1000).ToString("D3", CultureInfo.InvariantCulture);
        }

        private static string Phone9(TFieldRandom aRnd)
        {
            return "+34 9" + aRnd.Next(100).ToString("D2", CultureInfo.InvariantCulture) +
                " " + aRnd.Next(1000).ToString("D3", CultureInfo.InvariantCulture) +
                " " + aRnd.Next(1000).ToString("D3", CultureInfo.InvariantCulture);
        }

        // 'FS-<year>-<10 lowercase hex>'. Not a security token, just unguessable
        // enough that /track/<ref> cannot be walked; uniqueness is enforced against
        // the set built while seeding.
        private static string NewRef(TFieldRandom aRnd, Dictionary<string, byte> aRefs,
            DateTime aWhen)
        {
            string vTry;
            do
            {
                vTry = "FS-" + aWhen.Year.ToString("D4", CultureInfo.InvariantCulture) + "-" +
                    (aRnd.Next(0x10000).ToString("X4", CultureInfo.InvariantCulture) +
                     aRnd.Next(0x10000).ToString("X4", CultureInfo.InvariantCulture) +
                     aRnd.Next(0x100).ToString("X2", CultureInfo.InvariantCulture))
                    .ToLowerInvariant();
            }
            while (aRefs.ContainsKey(vTry));
            aRefs.Add(vTry, 1);
            return vTry;
        }

        // A whole or half hour between 08:00 and 17:30 on aDay.
        private static DateTime SlotStart(TFieldRandom aRnd, DateTime aDay)
        {
            return aDay.Date.AddHours(8 + aRnd.Next(20) * 0.5);
        }

        private static string PickPriority(TFieldRandom aRnd)
        {
            int vR = aRnd.Next(100);
            if (vR < 15)
                return FieldConst.CS_PRIORITY_LOW;
            if (vR < 70)
                return FieldConst.CS_PRIORITY_NORMAL;
            if (vR < 92)
                return FieldConst.CS_PRIORITY_HIGH;
            return FieldConst.CS_PRIORITY_URGENT;
        }

        private static double SlaHours(string aPriority)
        {
            if (aPriority == FieldConst.CS_PRIORITY_URGENT)
                return 4;
            if (aPriority == FieldConst.CS_PRIORITY_HIGH)
                return 8;
            if (aPriority == FieldConst.CS_PRIORITY_LOW)
                return 72;
            return 24;
        }

        // Prefer a technician based in the job's city; 20% of the time anybody, so
        // the map shows some cross-country travel. aAll includes the inactive one.
        private static int PickTech(TFieldRandom aRnd, int[] aTechCity, int aCityIdx,
            bool aAll)
        {
            int vTotal = aAll ? 10 : 9;
            int vStart = aRnd.Next(vTotal);
            int vResult = vStart;
            if (aRnd.Next(100) < 20)
                return vResult;
            for (int vX = 0; vX <= vTotal - 1; vX++)
                if (aTechCity[(vStart + vX) % vTotal] == aCityIdx)
                {
                    vResult = (vStart + vX) % vTotal;
                    break;
                }
            return vResult;
        }

        private static void InsUser(SqliteCommand aCmd, TSeedContext aCtx, long aId,
            string aUsername, string aRole, string aDisplay, string aPhone,
            string aInitials, DateTime aCreatedAt)
        {
            SetP(aCmd, ":id", aId);
            SetP(aCmd, ":un", aUsername);
            SetP(aCmd, ":pw", aCtx.Hash);
            SetP(aCmd, ":ro", aRole);
            SetP(aCmd, ":dn", aDisplay);
            SetP(aCmd, ":ph", aPhone);
            SetP(aCmd, ":ai", aInitials);
            SetP(aCmd, ":ca", FormatFieldTimestamp(aCreatedAt));
            aCmd.ExecuteNonQuery();
        }

        private static void InsEvent(SqliteCommand aCmd, TSeedContext aCtx, long aJobId,
            long aUserId, string aKind, string aDetail, double aLat, double aLng,
            DateTime aWhen)
        {
            aCtx.EventSeq++;
            SetP(aCmd, ":id", aCtx.EventSeq);
            SetP(aCmd, ":jid", aJobId);
            SetP(aCmd, ":uid", aUserId);
            SetP(aCmd, ":ki", aKind);
            SetP(aCmd, ":de", aDetail);
            SetP(aCmd, ":la", aLat);
            SetP(aCmd, ":ln", aLng);
            SetP(aCmd, ":ca", FormatFieldTimestamp(aWhen));
            aCmd.ExecuteNonQuery();
        }

        private static void InsCheck(SqliteCommand aCmd, TSeedContext aCtx, long aJobId,
            int aPos, string aText, bool aDone, DateTime aDoneAt)
        {
            aCtx.CheckSeq++;
            SetP(aCmd, ":id", aCtx.CheckSeq);
            SetP(aCmd, ":jid", aJobId);
            SetP(aCmd, ":po", aPos);
            SetP(aCmd, ":tx", aText);
            SetP(aCmd, ":dn", aDone ? 1 : 0);
            if (aDone)
                SetP(aCmd, ":da", FormatFieldTimestamp(aDoneAt));
            else
                SetP(aCmd, ":da", "");
            aCmd.ExecuteNonQuery();
        }

        private static void InsJob(SqliteCommand aCmd, long aId, string aRef,
            long aCustomer, long aSite, long aAsset, long aTech, string aTitle,
            string aDesc, string aPriority, string aStatus, DateTime aSS, DateTime aSE,
            DateTime aAS, DateTime aAE, DateTime aSla, DateTime aCreatedAt)
        {
            SetP(aCmd, ":id", aId);
            SetP(aCmd, ":rf", aRef);
            SetP(aCmd, ":cid", aCustomer);
            SetP(aCmd, ":sid", aSite);
            SetP(aCmd, ":aid", aAsset);
            SetP(aCmd, ":tid", aTech);
            SetP(aCmd, ":ti", aTitle);
            SetP(aCmd, ":de", aDesc);
            SetP(aCmd, ":pr", aPriority);
            SetP(aCmd, ":st", aStatus);
            SetP(aCmd, ":ss", FormatFieldTimestamp(aSS));
            SetP(aCmd, ":se", FormatFieldTimestamp(aSE));
            SetP(aCmd, ":ast", FormatFieldTimestamp(aAS));
            SetP(aCmd, ":aen", FormatFieldTimestamp(aAE));
            SetP(aCmd, ":sla", FormatFieldTimestamp(aSla));
            SetP(aCmd, ":cre", FormatFieldTimestamp(aCreatedAt));
            aCmd.ExecuteNonQuery();
        }

        // ----------------------------------------------------------------- //
        // users
        // ----------------------------------------------------------------- //

        public bool GetUserByUsername(string aUsername, out TFieldUser aUser)
        {
            aUser = null;
            if ((aUsername ?? "").Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_USER_SELECT + "WHERE LOWER(username) = LOWER(:u) LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":u", aUsername.Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aUser = FillUser(oReader);
                    return true;
                }
            }
        }

        public bool GetUserById(long aId, out TFieldUser aUser)
        {
            aUser = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_USER_SELECT + "WHERE id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aUser = FillUser(oReader);
                    return true;
                }
            }
        }

        public bool AuthenticateUser(string aUsername, string aPassword,
            out TFieldUser aUser)
        {
            if (!GetUserByUsername(aUsername, out aUser))
                return false;
            return Bcrypt.BcryptVerify(aPassword, aUser.PasswordHash);
        }

        public bool UsernameExists(string aUsername)
        {
            if ((aUsername ?? "").Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                return ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM users WHERE LOWER(username) = LOWER(:u)",
                    ":u", aUsername.Trim()) > 0;
            }
        }

        public TFieldUser[] ListUsers()
        {
            List<TFieldUser> vResult = new List<TFieldUser>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_USER_SELECT + "ORDER BY role ASC, username ASC"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                    vResult.Add(FillUser(oReader));
            }
            return vResult.ToArray();
        }

        // aId = 0 inserts, otherwise updates. aPasswordHash = "" keeps the current
        // hash on an update. Returns the row id, 0 on failure.
        public long SaveUser(long aId, string aUsername, string aPasswordHash,
            string aRole, string aDisplayName, string aPhone)
        {
            string vUsername = (aUsername ?? "").Trim();
            if (vUsername.Length == 0)
                return 0;

            using (SqliteConnection oConn = Acquire())
            {
                // The username is unique across the table, whichever row owns it.
                if (ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM users " +
                    "WHERE LOWER(username) = LOWER(:u) AND id <> :i",
                    ":u", vUsername, ":i", aId) > 0)
                    return 0;

                if (aId <= 0)
                {
                    ExecNonQuery(oConn,
                        "INSERT INTO users (username, password_hash, role, " +
                        "display_name, phone, avatar_initials, created_at) " +
                        "VALUES (:u, :p, :r, :dn, :ph, :ai, :ca)",
                        ":u", vUsername, ":p", aPasswordHash ?? "", ":r", aRole ?? "",
                        ":dn", aDisplayName ?? "", ":ph", aPhone ?? "",
                        ":ai", InitialsOf(aDisplayName, vUsername),
                        ":ca", FieldNowTimestamp());
                    return LastInsertRowId(oConn);
                }

                // A blank hash means "leave the stored password alone".
                string vSQL = "UPDATE users SET username = :u, role = :r, " +
                    "display_name = :dn, phone = :ph, avatar_initials = :ai";
                if (!string.IsNullOrEmpty(aPasswordHash))
                    vSQL = vSQL + ", password_hash = :p";
                vSQL = vSQL + " WHERE id = :i";

                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    oCmd.Parameters.AddWithValue(":u", vUsername);
                    oCmd.Parameters.AddWithValue(":r", aRole ?? "");
                    oCmd.Parameters.AddWithValue(":dn", aDisplayName ?? "");
                    oCmd.Parameters.AddWithValue(":ph", aPhone ?? "");
                    oCmd.Parameters.AddWithValue(":ai", InitialsOf(aDisplayName, vUsername));
                    if (!string.IsNullOrEmpty(aPasswordHash))
                        oCmd.Parameters.AddWithValue(":p", aPasswordHash);
                    oCmd.Parameters.AddWithValue(":i", aId);
                    if (oCmd.ExecuteNonQuery() > 0)
                        return aId;
                }
                return 0;
            }
        }

        // ----------------------------------------------------------------- //
        // passkeys
        //
        // The Delphi demo issues these statements inline from
        // sgcField_Passkeys.pas (which owns a TFieldDBPool and calls Acquire
        // directly). The managed port keeps every SQL statement in this unit,
        // exactly like the already-ported sibling demos.
        // ----------------------------------------------------------------- //

        private static TFieldPasskey FillPasskey(SqliteDataReader aReader)
        {
            TFieldPasskey oRow = new TFieldPasskey();
            oRow.Id = RInt64(aReader, "id");
            oRow.UserId = RInt64(aReader, "user_id");
            oRow.CredentialId = RStr(aReader, "credential_id");
            oRow.PublicKey = RStr(aReader, "public_key");
            oRow.SignCount = RInt64(aReader, "sign_count");
            oRow.DeviceName = RStr(aReader, "device_name");
            oRow.CreatedAt = RDate(aReader, "created_at");
            oRow.LastUsedAt = RDate(aReader, "last_used_at");
            return oRow;
        }

        public void AddPasskey(long aUserId, string aCredentialId, string aPublicKey,
            long aSignCount, string aDeviceName)
        {
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO passkeys " +
                    "(user_id, credential_id, public_key, sign_count, device_name, " +
                    "created_at, last_used_at) " +
                    "VALUES (:uid, :cid, :pk, :sc, :dn, :ca, :lu)",
                    ":uid", aUserId, ":cid", aCredentialId ?? "", ":pk", aPublicKey ?? "",
                    ":sc", aSignCount, ":dn", aDeviceName ?? "", ":ca", FieldNowTimestamp(),
                    ":lu", "");
            }
        }

        public TFieldPasskey[] GetPasskeysByUser(long aUserId)
        {
            List<TFieldPasskey> vResult = new List<TFieldPasskey>();
            if (aUserId <= 0)
                return vResult.ToArray();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, sign_count, " +
                "device_name, created_at, last_used_at FROM passkeys " +
                "WHERE user_id = :uid ORDER BY id DESC"))
            {
                oCmd.Parameters.AddWithValue(":uid", aUserId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillPasskey(oReader));
                }
            }
            return vResult.ToArray();
        }

        public bool GetPasskeyByCredentialId(string aCredId, out TFieldPasskey aPk)
        {
            aPk = null;
            if ((aCredId ?? "").Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, sign_count, " +
                "device_name, created_at, last_used_at FROM passkeys " +
                "WHERE credential_id = :cid LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":cid", aCredId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aPk = FillPasskey(oReader);
                    return true;
                }
            }
        }

        public void UpdatePasskeySignCount(long aId, long aSignCount)
        {
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "UPDATE passkeys SET sign_count = :sc, last_used_at = :lu " +
                    "WHERE id = :id",
                    ":sc", aSignCount, ":lu", FieldNowTimestamp(), ":id", aId);
            }
        }

        public bool DeletePasskey(long aId, long aUserId)
        {
            if ((aId <= 0) || (aUserId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                // Scoped to user_id so a user can only delete their own credentials.
                return ExecNonQuery(oConn,
                    "DELETE FROM passkeys WHERE id = :id AND user_id = :uid",
                    ":id", aId, ":uid", aUserId) > 0;
            }
        }

        // ----------------------------------------------------------------- //
        // technicians
        // ----------------------------------------------------------------- //

        public TFieldTechnician[] ListTechnicians(bool aOnlyActive = true)
        {
            List<TFieldTechnician> vResult = new List<TFieldTechnician>();
            string vSQL = CS_TECH_SELECT;
            if (aOnlyActive)
                vSQL = vSQL + "WHERE t.active = 1 ";
            vSQL = vSQL + "ORDER BY u.display_name ASC, t.id ASC";
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                    vResult.Add(FillTechnician(oReader));
            }
            return vResult.ToArray();
        }

        public bool GetTechnician(long aId, out TFieldTechnician aTech)
        {
            aTech = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_TECH_SELECT + "WHERE t.id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aTech = FillTechnician(oReader);
                    return true;
                }
            }
        }

        public bool GetTechnicianByUserId(long aUserId, out TFieldTechnician aTech)
        {
            aTech = null;
            if (aUserId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_TECH_SELECT + "WHERE t.user_id = :u LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":u", aUserId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aTech = FillTechnician(oReader);
                    return true;
                }
            }
        }

        // ----------------------------------------------------------------- //
        // customers / sites / assets
        // ----------------------------------------------------------------- //

        public TFieldCustomer[] ListCustomers(string aSearch = "", string aSort = "",
            string aDir = "")
        {
            List<TFieldCustomer> vResult = new List<TFieldCustomer>();
            string vSearch = (aSearch ?? "").Trim();
            bool vHasSearch = vSearch.Length != 0;
            string vSQL = CS_CUSTOMER_SELECT;
            if (vHasSearch)
                vSQL = vSQL + "WHERE (c.name LIKE :s ESCAPE '\\' " +
                    "OR c.city LIKE :s ESCAPE '\\' " +
                    "OR c.contact LIKE :s ESCAPE '\\') ";
            vSQL = vSQL + "ORDER BY " + CustomerSortColumnSQL(aSort) + " " +
                DirSQLAsc(aDir) + ", c.id ASC";
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (vHasSearch)
                    oCmd.Parameters.AddWithValue(":s", "%" + EscapeLikeValue(vSearch) + "%");
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillCustomer(oReader));
                }
            }
            return vResult.ToArray();
        }

        public bool GetCustomer(long aId, out TFieldCustomer aCustomer)
        {
            aCustomer = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_CUSTOMER_SELECT + "WHERE c.id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aCustomer = FillCustomer(oReader);
                    return true;
                }
            }
        }

        public long SaveCustomer(long aId, string aName, string aContact, string aEmail,
            string aPhone, string aAddress, string aCity)
        {
            if ((aName ?? "").Trim().Length == 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                if (aId <= 0)
                {
                    ExecNonQuery(oConn,
                        "INSERT INTO customers (name, contact, email, phone, " +
                        "address, city, lat, lng, created_at) " +
                        "VALUES (:nm, :co, :em, :ph, :ad, :ci, 0, 0, :ca)",
                        ":nm", aName.Trim(), ":co", aContact ?? "", ":em", aEmail ?? "",
                        ":ph", aPhone ?? "", ":ad", aAddress ?? "", ":ci", aCity ?? "",
                        ":ca", FieldNowTimestamp());
                    return LastInsertRowId(oConn);
                }
                if (ExecNonQuery(oConn,
                    "UPDATE customers SET name = :nm, contact = :co, email = :em, " +
                    "phone = :ph, address = :ad, city = :ci WHERE id = :i",
                    ":nm", aName.Trim(), ":co", aContact ?? "", ":em", aEmail ?? "",
                    ":ph", aPhone ?? "", ":ad", aAddress ?? "", ":ci", aCity ?? "",
                    ":i", aId) > 0)
                    return aId;
                return 0;
            }
        }

        public TFieldSite[] ListSites()
        {
            List<TFieldSite> vResult = new List<TFieldSite>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_SITE_SELECT + "ORDER BY c.name ASC, s.name ASC, s.id ASC"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                    vResult.Add(FillSite(oReader));
            }
            return vResult.ToArray();
        }

        public TFieldSite[] ListSitesForCustomer(long aCustomerId)
        {
            List<TFieldSite> vResult = new List<TFieldSite>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_SITE_SELECT + "WHERE s.customer_id = :c ORDER BY s.name ASC, s.id ASC"))
            {
                oCmd.Parameters.AddWithValue(":c", aCustomerId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillSite(oReader));
                }
            }
            return vResult.ToArray();
        }

        public bool GetSite(long aId, out TFieldSite aSite)
        {
            aSite = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_SITE_SELECT + "WHERE s.id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aSite = FillSite(oReader);
                    return true;
                }
            }
        }

        public TFieldAsset[] ListAssets(string aSearch = "")
        {
            List<TFieldAsset> vResult = new List<TFieldAsset>();
            string vSearch = (aSearch ?? "").Trim();
            bool vHasSearch = vSearch.Length != 0;
            string vSQL = CS_ASSET_SELECT;
            if (vHasSearch)
                vSQL = vSQL + "WHERE (a.name LIKE :s ESCAPE '\\' " +
                    "OR a.serial LIKE :s ESCAPE '\\' " +
                    "OR a.model LIKE :s ESCAPE '\\') ";
            vSQL = vSQL + "ORDER BY c.name ASC, s.name ASC, a.parent_id ASC, a.id ASC";
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (vHasSearch)
                    oCmd.Parameters.AddWithValue(":s", "%" + EscapeLikeValue(vSearch) + "%");
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillAsset(oReader));
                }
            }
            return vResult.ToArray();
        }

        public TFieldAsset[] ListAssetsForSite(long aSiteId)
        {
            List<TFieldAsset> vResult = new List<TFieldAsset>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_ASSET_SELECT + "WHERE a.site_id = :s ORDER BY a.parent_id ASC, a.id ASC"))
            {
                oCmd.Parameters.AddWithValue(":s", aSiteId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillAsset(oReader));
                }
            }
            return vResult.ToArray();
        }

        public bool GetAsset(long aId, out TFieldAsset aAsset)
        {
            aAsset = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_ASSET_SELECT + "WHERE a.id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aAsset = FillAsset(oReader);
                    return true;
                }
            }
        }

        // ----------------------------------------------------------------- //
        // parts
        // ----------------------------------------------------------------- //

        public TFieldPart[] ListParts(string aSearch = "")
        {
            List<TFieldPart> vResult = new List<TFieldPart>();
            string vSearch = (aSearch ?? "").Trim();
            bool vHasSearch = vSearch.Length != 0;
            string vSQL = CS_PART_SELECT;
            if (vHasSearch)
                vSQL = vSQL + "WHERE (sku LIKE :s ESCAPE '\\' " +
                    "OR name LIKE :s ESCAPE '\\') ";
            vSQL = vSQL + "ORDER BY sku ASC";
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (vHasSearch)
                    oCmd.Parameters.AddWithValue(":s", "%" + EscapeLikeValue(vSearch) + "%");
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillPart(oReader));
                }
            }
            return vResult.ToArray();
        }

        public bool GetPart(long aId, out TFieldPart aPart)
        {
            aPart = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_PART_SELECT + "WHERE id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aPart = FillPart(oReader);
                    return true;
                }
            }
        }

        public bool GetPartBySku(string aSku, out TFieldPart aPart)
        {
            aPart = null;
            if ((aSku ?? "").Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_PART_SELECT + "WHERE LOWER(sku) = LOWER(:s) LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":s", aSku.Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aPart = FillPart(oReader);
                    return true;
                }
            }
        }

        public long SavePart(long aId, string aSku, string aName, double aUnitPrice,
            int aStock)
        {
            if ((aSku ?? "").Trim().Length == 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                if (aId <= 0)
                {
                    ExecNonQuery(oConn,
                        "INSERT INTO parts (sku, name, unit_price, stock) " +
                        "VALUES (:sk, :nm, :up, :st)",
                        ":sk", aSku.Trim(), ":nm", aName ?? "", ":up", aUnitPrice,
                        ":st", aStock);
                    return LastInsertRowId(oConn);
                }
                if (ExecNonQuery(oConn,
                    "UPDATE parts SET sku = :sk, name = :nm, unit_price = :up, " +
                    "stock = :st WHERE id = :i",
                    ":sk", aSku.Trim(), ":nm", aName ?? "", ":up", aUnitPrice,
                    ":st", aStock, ":i", aId) > 0)
                    return aId;
                return 0;
            }
        }

        // ----------------------------------------------------------------- //
        // jobs
        // ----------------------------------------------------------------- //

        // Every job filter lives in one record so ListJobs and CountJobs can never
        // drift apart: one builder, one binder, two callers.
        private sealed class TFieldJobFilter
        {
            public string Where = "";
            public string Status = "";
            public long TechnicianId = -1;
            public string Priority = "";
            public string FromText = "";
            public string ToText = "";
            public string Search = "";
            public bool HasStatus;
            public bool HasTech;
            public bool HasPriority;
            public bool HasFrom;
            public bool HasTo;
            public bool HasSearch;

            public void AddCond(string aCond)
            {
                if (Where.Length != 0)
                    Where = Where + " AND ";
                Where = Where + aCond;
            }
        }

        private static TFieldJobFilter BuildJobFilter(string aStatus, long aTechnicianId,
            string aPriority, DateTime aFrom, DateTime aTo, string aSearch)
        {
            TFieldJobFilter vResult = new TFieldJobFilter();

            string vStatus = (aStatus ?? "").Trim().ToLowerInvariant();
            string vPriority = (aPriority ?? "").Trim().ToLowerInvariant();
            string vSearch = (aSearch ?? "").Trim();

            if ((vStatus.Length != 0) && (vStatus != "all"))
            {
                vResult.HasStatus = true;
                vResult.Status = vStatus;
                vResult.AddCond("j.status = :fst");
            }
            // Negative means "any technician"; 0 is a real filter, the unassigned queue.
            if (aTechnicianId >= 0)
            {
                vResult.HasTech = true;
                vResult.TechnicianId = aTechnicianId;
                vResult.AddCond("j.technician_id = :ftid");
            }
            if ((vPriority.Length != 0) && (vPriority != "all"))
            {
                vResult.HasPriority = true;
                vResult.Priority = vPriority;
                vResult.AddCond("j.priority = :fpr");
            }
            if (!IsZeroDate(aFrom))
            {
                vResult.HasFrom = true;
                vResult.FromText = FormatFieldTimestamp(aFrom);
                vResult.AddCond("j.scheduled_start >= :ffrom");
            }
            if (!IsZeroDate(aTo))
            {
                vResult.HasTo = true;
                vResult.ToText = FormatFieldTimestamp(aTo);
                vResult.AddCond("j.scheduled_start < :fto");
            }
            if (vSearch.Length != 0)
            {
                vResult.HasSearch = true;
                vResult.Search = "%" + EscapeLikeValue(vSearch) + "%";
                vResult.AddCond("(j.reference LIKE :fs ESCAPE '\\' " +
                    "OR j.title LIKE :fs ESCAPE '\\' " +
                    "OR c.name LIKE :fs ESCAPE '\\' " +
                    "OR s.name LIKE :fs ESCAPE '\\')");
            }
            return vResult;
        }

        private static void ApplyJobFilter(SqliteCommand aCmd, TFieldJobFilter aFilter)
        {
            if (aFilter.HasStatus)
                aCmd.Parameters.AddWithValue(":fst", aFilter.Status);
            if (aFilter.HasTech)
                aCmd.Parameters.AddWithValue(":ftid", aFilter.TechnicianId);
            if (aFilter.HasPriority)
                aCmd.Parameters.AddWithValue(":fpr", aFilter.Priority);
            if (aFilter.HasFrom)
                aCmd.Parameters.AddWithValue(":ffrom", aFilter.FromText);
            if (aFilter.HasTo)
                aCmd.Parameters.AddWithValue(":fto", aFilter.ToText);
            if (aFilter.HasSearch)
                aCmd.Parameters.AddWithValue(":fs", aFilter.Search);
        }

        // Every filter is optional: aStatus/aPriority "" or "all" = no filter,
        // aTechnicianId < 0 = no filter (0 means "unassigned"), aFrom/aTo =
        // DateTime.MinValue = no date bound. aSort/aDir are whitelisted before they
        // reach ORDER BY.
        public TFieldJob[] ListJobs(string aStatus, long aTechnicianId, string aPriority,
            DateTime aFrom, DateTime aTo, string aSearch, string aSort, string aDir,
            int aLimit, int aOffset)
        {
            List<TFieldJob> vResult = new List<TFieldJob>();
            TFieldJobFilter vFilter = BuildJobFilter(aStatus, aTechnicianId, aPriority,
                aFrom, aTo, aSearch);
            string vSQL = CS_JOB_SELECT;
            if (vFilter.Where.Length != 0)
                vSQL = vSQL + "WHERE " + vFilter.Where + " ";
            vSQL = vSQL + "ORDER BY " + JobSortColumnSQL(aSort) + " " + DirSQL(aDir) +
                ", j.id DESC" + LimitSQL(aLimit, aOffset);

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                ApplyJobFilter(oCmd, vFilter);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillJob(oReader));
                }
            }
            return vResult.ToArray();
        }

        public int CountJobs(string aStatus, long aTechnicianId, string aPriority,
            DateTime aFrom, DateTime aTo, string aSearch)
        {
            TFieldJobFilter vFilter = BuildJobFilter(aStatus, aTechnicianId, aPriority,
                aFrom, aTo, aSearch);
            // The same joins as ListJobs, because the search reaches into c.name/s.name.
            string vSQL = "SELECT COUNT(*) " + CS_JOB_FROM;
            if (vFilter.Where.Length != 0)
                vSQL = vSQL + "WHERE " + vFilter.Where;

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                ApplyJobFilter(oCmd, vFilter);
                object vVal = oCmd.ExecuteScalar();
                if (vVal == null || vVal == DBNull.Value)
                    return 0;
                return Convert.ToInt32(vVal, CultureInfo.InvariantCulture);
            }
        }

        public bool GetJob(long aId, out TFieldJob aJob)
        {
            aJob = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_JOB_SELECT + "WHERE j.id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aJob = FillJob(oReader);
                    return true;
                }
            }
        }

        public bool GetJobByReference(string aReference, out TFieldJob aJob)
        {
            aJob = null;
            if ((aReference ?? "").Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_JOB_SELECT + "WHERE LOWER(j.reference) = LOWER(:r) LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":r", aReference.Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aJob = FillJob(oReader);
                    return true;
                }
            }
        }

        // Scheduled work in [aFrom, aTo). aTechnicianId < 0 = every technician,
        // 0 = the unassigned queue.
        public TFieldJob[] ListJobsInRange(DateTime aFrom, DateTime aTo,
            long aTechnicianId = -1)
        {
            List<TFieldJob> vResult = new List<TFieldJob>();
            // Overlap, not containment: a multi-day bar that starts before the window
            // must still be drawn inside it.
            string vSQL = CS_JOB_SELECT + "WHERE j.scheduled_start <> '' " +
                "AND j.scheduled_start < :t " +
                "AND (CASE WHEN j.scheduled_end <> '' THEN j.scheduled_end " +
                "ELSE j.scheduled_start END) >= :f ";
            if (aTechnicianId >= 0)
                vSQL = vSQL + "AND j.technician_id = :tid ";
            vSQL = vSQL + "ORDER BY j.scheduled_start ASC, j.id ASC";

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                oCmd.Parameters.AddWithValue(":f", FormatFieldTimestamp(aFrom));
                oCmd.Parameters.AddWithValue(":t", FormatFieldTimestamp(aTo));
                if (aTechnicianId >= 0)
                    oCmd.Parameters.AddWithValue(":tid", aTechnicianId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillJob(oReader));
                }
            }
            return vResult.ToArray();
        }

        // Jobs assigned to aTechnicianId whose scheduled_start falls on aDay.
        public TFieldJob[] ListJobsForTechnicianDay(long aTechnicianId, DateTime aDay)
        {
            List<TFieldJob> vResult = new List<TFieldJob>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_JOB_SELECT + "WHERE j.technician_id = :tid " +
                "AND j.scheduled_start >= :f AND j.scheduled_start < :t " +
                "ORDER BY j.scheduled_start ASC, j.id ASC"))
            {
                oCmd.Parameters.AddWithValue(":tid", aTechnicianId);
                oCmd.Parameters.AddWithValue(":f", FormatFieldTimestamp(aDay.Date));
                oCmd.Parameters.AddWithValue(":t", FormatFieldTimestamp(aDay.Date.AddDays(1)));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillJob(oReader));
                }
            }
            return vResult.ToArray();
        }

        // Every live job that carries site coordinates, for the map.
        public TFieldJob[] ListJobsForMap(DateTime aFrom, DateTime aTo)
        {
            List<TFieldJob> vResult = new List<TFieldJob>();
            // Only work that is still moving, and only where there is something to pin.
            string vSQL = CS_JOB_SELECT + "WHERE j.status IN " + CS_LIVE_STATUSES +
                " AND (s.lat <> 0 OR s.lng <> 0) ";
            if (!IsZeroDate(aFrom))
                vSQL = vSQL + "AND j.scheduled_start >= :f ";
            if (!IsZeroDate(aTo))
                vSQL = vSQL + "AND j.scheduled_start < :t ";
            vSQL = vSQL + "ORDER BY j.scheduled_start ASC, j.id ASC";

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (!IsZeroDate(aFrom))
                    oCmd.Parameters.AddWithValue(":f", FormatFieldTimestamp(aFrom));
                if (!IsZeroDate(aTo))
                    oCmd.Parameters.AddWithValue(":t", FormatFieldTimestamp(aTo));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillJob(oReader));
                }
            }
            return vResult.ToArray();
        }

        // Inserts a job with status 'new' (or 'scheduled' when aTechnicianId > 0
        // and aStart > 0), generating the unguessable public reference. Returns the
        // new id and the generated reference.
        public long CreateJob(long aCustomerId, long aSiteId, long aAssetId,
            long aTechnicianId, string aTitle, string aDescription, string aPriority,
            DateTime aScheduledStart, DateTime aScheduledEnd, DateTime aSlaDueAt,
            out string aReference)
        {
            aReference = "";
            if ((aTitle ?? "").Trim().Length == 0)
                return 0;

            string vPriority = (aPriority ?? "").Trim().ToLowerInvariant();
            if (!FieldConst.FieldIsPriority(vPriority))
                vPriority = FieldConst.CS_PRIORITY_NORMAL;

            // Same generator the seed uses: 40 bits of hex is plenty to stop /track
            // from being walked, and the loop guarantees it is free.
            string vRef;
            int vTries = 0;
            TFieldJob vExisting;
            do
            {
                vTries++;
                lock (GRandomLock)
                {
                    vRef = "FS-" + DateTime.Now.Year.ToString("D4", CultureInfo.InvariantCulture) +
                        "-" + (GRandom.Next(0x10000).ToString("X4", CultureInfo.InvariantCulture) +
                        GRandom.Next(0x10000).ToString("X4", CultureInfo.InvariantCulture) +
                        GRandom.Next(0x100).ToString("X2", CultureInfo.InvariantCulture))
                        .ToLowerInvariant();
                }
            }
            while (GetJobByReference(vRef, out vExisting) && (vTries <= 50));

            string vStatus;
            if ((aTechnicianId > 0) && (!IsZeroDate(aScheduledStart)))
                vStatus = FieldConst.CS_JOB_SCHEDULED;
            else
                vStatus = FieldConst.CS_JOB_NEW;

            long vResult;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO jobs (reference, customer_id, site_id, asset_id, " +
                    "technician_id, title, description, priority, status, " +
                    "scheduled_start, scheduled_end, actual_start, actual_end, " +
                    "sla_due_at, created_at) VALUES (:rf, :cid, :sid, :aid, :tid, " +
                    ":ti, :de, :pr, :st, :ss, :se, '', '', :sla, :cre)",
                    ":rf", vRef, ":cid", aCustomerId, ":sid", aSiteId, ":aid", aAssetId,
                    ":tid", aTechnicianId, ":ti", aTitle.Trim(), ":de", aDescription ?? "",
                    ":pr", vPriority, ":st", vStatus,
                    ":ss", FormatFieldTimestamp(aScheduledStart),
                    ":se", FormatFieldTimestamp(aScheduledEnd),
                    ":sla", FormatFieldTimestamp(aSlaDueAt),
                    ":cre", FieldNowTimestamp());
                vResult = LastInsertRowId(oConn);
            }

            if (vResult > 0)
                aReference = vRef;
            return vResult;
        }

        // Raw status write. The legality of the transition is decided by the caller
        // through FieldCanTransition; this only persists it, and stamps actual_start
        // when moving to 'onsite' and actual_end on 'complete'.
        public bool SetJobStatus(long aJobId, string aNewStatus)
        {
            string vStatus = (aNewStatus ?? "").Trim().ToLowerInvariant();
            if (!FieldConst.FieldIsStatus(vStatus))
                return false;
            string vNow = FieldNowTimestamp();

            using (SqliteConnection oConn = Acquire())
            {
                bool vResult = ExecNonQuery(oConn,
                    "UPDATE jobs SET status = :st WHERE id = :i",
                    ":st", vStatus, ":i", aJobId) > 0;
                if (!vResult)
                    return false;

                // Stamp the clock the moment the job physically starts / finishes, but
                // never overwrite a start that is already on the row.
                if (vStatus == FieldConst.CS_JOB_ONSITE)
                {
                    ExecNonQuery(oConn,
                        "UPDATE jobs SET actual_start = :ts " +
                        "WHERE id = :i AND (actual_start IS NULL OR actual_start = '')",
                        ":ts", vNow, ":i", aJobId);
                }
                else if (vStatus == FieldConst.CS_JOB_COMPLETE)
                {
                    ExecNonQuery(oConn,
                        "UPDATE jobs SET actual_start = :ts " +
                        "WHERE id = :i AND (actual_start IS NULL OR actual_start = '')",
                        ":ts", vNow, ":i", aJobId);
                    ExecNonQuery(oConn,
                        "UPDATE jobs SET actual_end = :ts WHERE id = :i",
                        ":ts", vNow, ":i", aJobId);
                }
                return true;
            }
        }

        // Assign / reschedule. aTechnicianId = 0 unassigns. When a technician and a
        // start are given and the job is still 'new', the status becomes 'scheduled';
        // unassigning a 'scheduled' job takes it back to 'new'.
        public bool AssignJob(long aJobId, long aTechnicianId, DateTime aStart,
            DateTime aEnd)
        {
            if (aJobId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                string vStatus;
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT status FROM jobs WHERE id = :i LIMIT 1"))
                {
                    oCmd.Parameters.AddWithValue(":i", aJobId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (!oReader.Read())
                            return false;
                        vStatus = RStr(oReader, "status").ToLowerInvariant();
                    }
                }

                string vNewStatus = vStatus;
                if ((aTechnicianId > 0) && (!IsZeroDate(aStart)) &&
                    (vStatus == FieldConst.CS_JOB_NEW))
                    vNewStatus = FieldConst.CS_JOB_SCHEDULED;
                else if ((aTechnicianId == 0) && (vStatus == FieldConst.CS_JOB_SCHEDULED))
                    vNewStatus = FieldConst.CS_JOB_NEW;

                string vSQL = "UPDATE jobs SET technician_id = :tid, status = :st";
                if (!IsZeroDate(aStart))
                    vSQL = vSQL + ", scheduled_start = :ss, scheduled_end = :se";
                vSQL = vSQL + " WHERE id = :i";

                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    oCmd.Parameters.AddWithValue(":tid", aTechnicianId);
                    oCmd.Parameters.AddWithValue(":st", vNewStatus);
                    if (!IsZeroDate(aStart))
                    {
                        oCmd.Parameters.AddWithValue(":ss", FormatFieldTimestamp(aStart));
                        oCmd.Parameters.AddWithValue(":se", FormatFieldTimestamp(aEnd));
                    }
                    oCmd.Parameters.AddWithValue(":i", aJobId);
                    return oCmd.ExecuteNonQuery() > 0;
                }
            }
        }

        public bool RescheduleJob(long aJobId, DateTime aStart, DateTime aEnd)
        {
            if (aJobId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                return ExecNonQuery(oConn,
                    "UPDATE jobs SET scheduled_start = :ss, scheduled_end = :se " +
                    "WHERE id = :i",
                    ":ss", FormatFieldTimestamp(aStart),
                    ":se", FormatFieldTimestamp(aEnd), ":i", aJobId) > 0;
            }
        }

        // ----------------------------------------------------------------- //
        // job children
        // ----------------------------------------------------------------- //

        public long AddJobEvent(long aJobId, long aUserId, string aKind, string aDetail,
            double aLat, double aLng)
        {
            if (aJobId <= 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO job_events (job_id, user_id, kind, detail, lat, lng, " +
                    "created_at) VALUES (:jid, :uid, :ki, :de, :la, :ln, :ca)",
                    ":jid", aJobId, ":uid", aUserId, ":ki", aKind ?? "",
                    ":de", aDetail ?? "", ":la", aLat, ":ln", aLng,
                    ":ca", FieldNowTimestamp());
                return LastInsertRowId(oConn);
            }
        }

        public TFieldJobEvent[] ListJobEvents(long aJobId)
        {
            List<TFieldJobEvent> vResult = new List<TFieldJobEvent>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_EVENT_SELECT + "WHERE e.job_id = :j ORDER BY e.created_at ASC, e.id ASC"))
            {
                oCmd.Parameters.AddWithValue(":j", aJobId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillEvent(oReader));
                }
            }
            return vResult.ToArray();
        }

        public TFieldJobEvent[] ListRecentEvents(int aLimit)
        {
            List<TFieldJobEvent> vResult = new List<TFieldJobEvent>();
            if (aLimit <= 0)
                aLimit = 25;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_EVENT_SELECT + "ORDER BY e.created_at DESC, e.id DESC" +
                LimitSQL(aLimit, 0)))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                    vResult.Add(FillEvent(oReader));
            }
            return vResult.ToArray();
        }

        public TFieldChecklistItem[] ListChecklist(long aJobId)
        {
            List<TFieldChecklistItem> vResult = new List<TFieldChecklistItem>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_CHECK_SELECT + "WHERE job_id = :j ORDER BY \"position\" ASC, id ASC"))
            {
                oCmd.Parameters.AddWithValue(":j", aJobId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillChecklist(oReader));
                }
            }
            return vResult.ToArray();
        }

        // Appends one checklist step to a job. Used when a job is created from the
        // dispatch board, so a new job reaches the technician with the standard steps
        // on it rather than empty.
        public long AddChecklistItem(long aJobId, int aPosition, string aText)
        {
            if ((aJobId <= 0) || ((aText ?? "").Trim().Length == 0))
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO job_checklist (job_id, \"position\", \"text\", done, " +
                    "done_at) VALUES (:j, :p, :t, 0, '')",
                    ":j", aJobId, ":p", aPosition, ":t", aText);
                return LastInsertRowId(oConn);
            }
        }

        // Ticks / unticks one item, but only when it belongs to aJobId (IDOR).
        public bool SetChecklistDone(long aItemId, long aJobId, bool aDone)
        {
            if ((aItemId <= 0) || (aJobId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                // job_id is part of the WHERE on purpose: an item id from another job
                // must not be tickable just because the caller guessed the number.
                return ExecNonQuery(oConn,
                    "UPDATE job_checklist SET done = :dn, done_at = :da " +
                    "WHERE id = :i AND job_id = :j",
                    ":dn", aDone ? 1 : 0, ":da", aDone ? FieldNowTimestamp() : "",
                    ":i", aItemId, ":j", aJobId) > 0;
            }
        }

        public void ChecklistProgress(long aJobId, out int aDone, out int aTotal)
        {
            aDone = 0;
            aTotal = 0;
            if (aJobId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT COUNT(*) AS n_all, " +
                "COALESCE(SUM(CASE WHEN done <> 0 THEN 1 ELSE 0 END), 0) AS n_done " +
                "FROM job_checklist WHERE job_id = :j"))
            {
                oCmd.Parameters.AddWithValue(":j", aJobId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (oReader.Read())
                    {
                        aTotal = RInt(oReader, "n_all");
                        aDone = RInt(oReader, "n_done");
                    }
                }
            }
        }

        public TFieldJobPart[] ListJobParts(long aJobId)
        {
            List<TFieldJobPart> vResult = new List<TFieldJobPart>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_JOBPART_SELECT + "WHERE jp.job_id = :j ORDER BY jp.id ASC"))
            {
                oCmd.Parameters.AddWithValue(":j", aJobId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillJobPart(oReader));
                }
            }
            return vResult.ToArray();
        }

        public long AddJobPart(long aJobId, long aPartId, int aQty, double aUnitPrice)
        {
            if ((aJobId <= 0) || (aPartId <= 0))
                return 0;
            if (aQty <= 0)
                aQty = 1;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO job_parts (job_id, part_id, qty, unit_price) " +
                    "VALUES (:j, :p, :q, :u)",
                    ":j", aJobId, ":p", aPartId, ":q", aQty, ":u", aUnitPrice);
                return LastInsertRowId(oConn);
            }
        }

        public long AddJobPhoto(long aJobId, string aFilename, string aContentType,
            long aSizeBytes, string aCaption)
        {
            if (aJobId <= 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO job_photos (job_id, filename, content_type, " +
                    "size_bytes, caption, created_at) " +
                    "VALUES (:j, :fn, :ct, :sz, :cp, :ca)",
                    ":j", aJobId, ":fn", aFilename ?? "", ":ct", aContentType ?? "",
                    ":sz", aSizeBytes, ":cp", aCaption ?? "", ":ca", FieldNowTimestamp());
                return LastInsertRowId(oConn);
            }
        }

        public TFieldJobPhoto[] ListJobPhotos(long aJobId)
        {
            List<TFieldJobPhoto> vResult = new List<TFieldJobPhoto>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_PHOTO_SELECT + "WHERE job_id = :j ORDER BY id ASC"))
            {
                oCmd.Parameters.AddWithValue(":j", aJobId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillPhoto(oReader));
                }
            }
            return vResult.ToArray();
        }

        public bool GetJobPhoto(long aId, out TFieldJobPhoto aPhoto)
        {
            aPhoto = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_PHOTO_SELECT + "WHERE id = :i LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aPhoto = FillPhoto(oReader);
                    return true;
                }
            }
        }

        // One signature per job: replaces the previous row when there is one.
        public long SaveSignature(long aJobId, string aSignerName, string aSignaturePng)
        {
            if (aJobId <= 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                long vResult;
                using (SqliteCommand oDel = NewCmd(oConn,
                    "DELETE FROM job_signatures WHERE job_id = :j"))
                {
                    oDel.Transaction = oTx;
                    oDel.Parameters.AddWithValue(":j", aJobId);
                    oDel.ExecuteNonQuery();
                }
                using (SqliteCommand oIns = NewCmd(oConn,
                    "INSERT INTO job_signatures (job_id, signer_name, signature_png, " +
                    "signed_at) VALUES (:j, :sn, :pg, :sa)"))
                {
                    oIns.Transaction = oTx;
                    oIns.Parameters.AddWithValue(":j", aJobId);
                    oIns.Parameters.AddWithValue(":sn", aSignerName ?? "");
                    oIns.Parameters.AddWithValue(":pg", aSignaturePng ?? "");
                    oIns.Parameters.AddWithValue(":sa", FieldNowTimestamp());
                    oIns.ExecuteNonQuery();
                }
                using (SqliteCommand oId = NewCmd(oConn, "SELECT last_insert_rowid()"))
                {
                    oId.Transaction = oTx;
                    object vVal = oId.ExecuteScalar();
                    vResult = (vVal == null || vVal == DBNull.Value) ? 0
                        : Convert.ToInt64(vVal, CultureInfo.InvariantCulture);
                }
                oTx.Commit();
                return vResult;
            }
        }

        public bool GetSignature(long aJobId, out TFieldSignature aSig)
        {
            aSig = null;
            if (aJobId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_SIGN_SELECT + "WHERE job_id = :j ORDER BY id DESC LIMIT 1"))
            {
                oCmd.Parameters.AddWithValue(":j", aJobId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aSig = FillSignature(oReader);
                    return true;
                }
            }
        }

        // ----------------------------------------------------------------- //
        // messages
        // ----------------------------------------------------------------- //

        public TFieldMessage[] ListMessages(long aJobId)
        {
            List<TFieldMessage> vResult = new List<TFieldMessage>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_MESSAGE_SELECT + "WHERE m.job_id = :j ORDER BY m.created_at ASC, m.id ASC"))
            {
                oCmd.Parameters.AddWithValue(":j", aJobId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        vResult.Add(FillMessage(oReader));
                }
            }
            return vResult.ToArray();
        }

        public long AddMessage(long aJobId, long aFromUserId, long aToUserId, string aBody)
        {
            if ((aJobId <= 0) || ((aBody ?? "").Trim().Length == 0))
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO messages (job_id, from_user_id, to_user_id, body, " +
                    "created_at, read_at) VALUES (:j, :fu, :tu, :bo, :ca, '')",
                    ":j", aJobId, ":fu", aFromUserId, ":tu", aToUserId, ":bo", aBody,
                    ":ca", FieldNowTimestamp());
                return LastInsertRowId(oConn);
            }
        }

        public void MarkMessagesRead(long aJobId, long aUserId)
        {
            if ((aJobId <= 0) || (aUserId <= 0))
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "UPDATE messages SET read_at = :ts " +
                    "WHERE job_id = :j AND to_user_id = :u " +
                    "AND (read_at IS NULL OR read_at = '')",
                    ":ts", FieldNowTimestamp(), ":j", aJobId, ":u", aUserId);
            }
        }

        public int CountUnreadFor(long aUserId)
        {
            if (aUserId <= 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                return (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM messages " +
                    "WHERE to_user_id = :u AND (read_at IS NULL OR read_at = '')",
                    ":u", aUserId);
            }
        }

        // Jobs that carry at least one message, newest conversation first.
        public TFieldJob[] ListJobsWithMessages(int aLimit)
        {
            List<TFieldJob> vResult = new List<TFieldJob>();
            if (aLimit <= 0)
                aLimit = 25;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_JOB_SELECT + "WHERE j.id IN (SELECT DISTINCT job_id FROM messages) " +
                "ORDER BY (SELECT MAX(m2.created_at) FROM messages m2 " +
                "WHERE m2.job_id = j.id) DESC, j.id DESC" + LimitSQL(aLimit, 0)))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                    vResult.Add(FillJob(oReader));
            }
            return vResult.ToArray();
        }

        // ----------------------------------------------------------------- //
        // audit
        // ----------------------------------------------------------------- //

        public void AddAudit(long aUserId, string aAction, string aEntity, long aEntityId,
            string aDetail, string aIP)
        {
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO audit_log (user_id, action, entity, entity_id, " +
                    "detail, ip, created_at) VALUES (:u, :ac, :en, :ei, :de, :ip, :ca)",
                    ":u", aUserId, ":ac", aAction ?? "", ":en", aEntity ?? "",
                    ":ei", aEntityId, ":de", aDetail ?? "", ":ip", aIP ?? "",
                    ":ca", FieldNowTimestamp());
            }
        }

        public TFieldAuditEntry[] ListAudit(int aLimit)
        {
            List<TFieldAuditEntry> vResult = new List<TFieldAuditEntry>();
            if (aLimit <= 0)
                aLimit = 100;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                CS_AUDIT_SELECT + "ORDER BY g.created_at DESC, g.id DESC" +
                LimitSQL(aLimit, 0)))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                    vResult.Add(FillAudit(oReader));
            }
            return vResult.ToArray();
        }

        // ----------------------------------------------------------------- //
        // reports / dashboard
        // ----------------------------------------------------------------- //

        // Timestamps are compared as TEXT, so an open bound has to become a real
        // string: '' would make "col >= ''" match everything and "col < ''" match
        // nothing, which silently empties every report.
        private static string LowerBoundText(DateTime aValue)
        {
            if (IsZeroDate(aValue))
                return "0001-01-01T00:00:00";
            return FormatFieldTimestamp(aValue);
        }

        private static string UpperBoundText(DateTime aValue)
        {
            if (IsZeroDate(aValue))
                return "9999-12-31T23:59:59";
            return FormatFieldTimestamp(aValue);
        }

        // Delphi's DayOfTheWeek: 1 = Monday .. 7 = Sunday.
        private static int DayOfTheWeek(DateTime aValue)
        {
            return ((int)aValue.DayOfWeek + 6) % 7 + 1;
        }

        // Monday to Friday days inside [aFrom, aTo).
        private static int WorkingDaysIn(DateTime aFrom, DateTime aTo)
        {
            int vResult = 0;
            if (IsZeroDate(aFrom) || (aTo <= aFrom))
                return vResult;
            int vGuard = 0;
            DateTime vDay = aFrom.Date;
            while ((vDay < aTo) && (vGuard < 2000))
            {
                vGuard++;
                if (DayOfTheWeek(vDay) <= 5)
                    vResult++;
                vDay = vDay.AddDays(1);
            }
            return vResult;
        }

        public int CountJobsByStatus(string aStatus)
        {
            using (SqliteConnection oConn = Acquire())
            {
                return (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM jobs WHERE status = :st",
                    ":st", (aStatus ?? "").Trim().ToLowerInvariant());
            }
        }

        public int CountJobsScheduledBetween(DateTime aFrom, DateTime aTo)
        {
            using (SqliteConnection oConn = Acquire())
            {
                return (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM jobs " +
                    "WHERE scheduled_start >= :f AND scheduled_start < :t",
                    ":f", LowerBoundText(aFrom), ":t", UpperBoundText(aTo));
            }
        }

        // The window is applied to created_at throughout, so every number on the page
        // describes the same population of jobs.
        public TFieldReportStats GetReportStats(DateTime aFrom, DateTime aTo)
        {
            TFieldReportStats vResult = new TFieldReportStats();
            string vFrom = LowerBoundText(aFrom);
            string vTo = UpperBoundText(aTo);

            using (SqliteConnection oConn = Acquire())
            {
                // --- headline counts --- //
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) AS n_all, " +
                    "COALESCE(SUM(CASE WHEN status = 'complete' THEN 1 ELSE 0 END), 0) " +
                    "AS n_done, " +
                    "COALESCE(SUM(CASE WHEN status = 'cancelled' THEN 1 ELSE 0 END), 0)" +
                    " AS n_cancel FROM jobs WHERE created_at >= :f AND created_at < :t"))
                {
                    oCmd.Parameters.AddWithValue(":f", vFrom);
                    oCmd.Parameters.AddWithValue(":t", vTo);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (oReader.Read())
                        {
                            vResult.TotalJobs = RInt(oReader, "n_all");
                            vResult.CompletedJobs = RInt(oReader, "n_done");
                            vResult.CancelledJobs = RInt(oReader, "n_cancel");
                            vResult.OpenJobs = vResult.TotalJobs - vResult.CompletedJobs -
                                vResult.CancelledJobs;
                            if (vResult.OpenJobs < 0)
                                vResult.OpenJobs = 0;
                        }
                    }
                }

                // --- SLA --- //
                // Both columns share the fixed ISO shape, so a lexical comparison is
                // exactly a chronological one.
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) AS n, " +
                    "COALESCE(SUM(CASE WHEN actual_end > sla_due_at THEN 1 ELSE 0 END), 0)" +
                    " AS n_breach FROM jobs WHERE status = 'complete' " +
                    "AND created_at >= :f AND created_at < :t " +
                    "AND sla_due_at <> '' AND actual_end <> ''"))
                {
                    oCmd.Parameters.AddWithValue(":f", vFrom);
                    oCmd.Parameters.AddWithValue(":t", vTo);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (oReader.Read())
                        {
                            vResult.SlaBreached = RInt(oReader, "n_breach");
                            vResult.SlaMet = RInt(oReader, "n") - vResult.SlaBreached;
                            if (vResult.SlaMet < 0)
                                vResult.SlaMet = 0;
                        }
                    }
                }

                // --- first time fix: one completed visit per site + asset --- //
                vResult.FirstTimeFix = (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) AS n FROM (" +
                    "SELECT site_id, asset_id, COUNT(*) AS c FROM jobs " +
                    "WHERE status = 'complete' AND created_at >= :f " +
                    "AND created_at < :t GROUP BY site_id, asset_id HAVING c = 1) AS x",
                    ":f", vFrom, ":t", vTo);
                vResult.Revisits = vResult.CompletedJobs - vResult.FirstTimeFix;
                if (vResult.Revisits < 0)
                    vResult.Revisits = 0;

                // --- satisfaction, which lives only in the 'rating' events --- //
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) AS n, " +
                    "COALESCE(AVG(CAST(e.detail AS REAL)), 0) AS avg_rating " +
                    "FROM job_events e JOIN jobs j ON j.id = e.job_id " +
                    "WHERE e.kind = 'rating' AND j.created_at >= :f " +
                    "AND j.created_at < :t"))
                {
                    oCmd.Parameters.AddWithValue(":f", vFrom);
                    oCmd.Parameters.AddWithValue(":t", vTo);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (oReader.Read())
                        {
                            vResult.RatedJobs = RInt(oReader, "n");
                            vResult.AvgSatisfaction = RFloat(oReader, "avg_rating");
                        }
                    }
                }

                // --- parts revenue --- //
                vResult.PartsRevenue = ExecScalarDouble(oConn,
                    "SELECT COALESCE(SUM(jp.qty * jp.unit_price), 0) AS rev " +
                    "FROM job_parts jp JOIN jobs j ON j.id = jp.job_id " +
                    "WHERE j.created_at >= :f AND j.created_at < :t",
                    ":f", vFrom, ":t", vTo);

                // --- labour hours, summed in managed code so the ISO text never has to
                // be handed to a SQLite date function --- //
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT actual_start, actual_end FROM jobs " +
                    "WHERE status = 'complete' AND created_at >= :f " +
                    "AND created_at < :t AND actual_start <> '' AND actual_end <> ''"))
                {
                    oCmd.Parameters.AddWithValue(":f", vFrom);
                    oCmd.Parameters.AddWithValue(":t", vTo);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            DateTime vStartAt = RDate(oReader, "actual_start");
                            DateTime vEndAt = RDate(oReader, "actual_end");
                            if ((!IsZeroDate(vStartAt)) && (vEndAt > vStartAt))
                            {
                                double vSpan = (vEndAt - vStartAt).TotalHours;
                                if (vSpan < 240)
                                    vResult.LabourHours = vResult.LabourHours + vSpan;
                            }
                        }
                    }
                }
            }
            return vResult;
        }

        public TFieldTechStat[] ListTechStats(DateTime aFrom, DateTime aTo)
        {
            string vFrom = LowerBoundText(aFrom);
            string vTo = UpperBoundText(aTo);
            TFieldTechnician[] vTechs = ListTechnicians(false);
            if (vTechs.Length == 0)
                return new TFieldTechStat[0];

            int vWorkDays = WorkingDaysIn(aFrom, aTo);
            if (vWorkDays <= 0)
                vWorkDays = 1;
            double vCapacity = vWorkDays * 8.0;

            TFieldTechStat[] vResult = new TFieldTechStat[vTechs.Length];
            using (SqliteConnection oConn = Acquire())
            {
                for (int vI = 0; vI < vTechs.Length; vI++)
                {
                    TFieldTechStat vRow = new TFieldTechStat();
                    vRow.TechnicianId = vTechs[vI].Id;
                    vRow.DisplayName = vTechs[vI].DisplayName;
                    vRow.AvatarInitials = vTechs[vI].AvatarInitials;

                    using (SqliteCommand oCmd = NewCmd(oConn,
                        "SELECT actual_start, actual_end, sla_due_at FROM jobs " +
                        "WHERE technician_id = :tid AND status = 'complete' " +
                        "AND created_at >= :f AND created_at < :t"))
                    {
                        oCmd.Parameters.AddWithValue(":tid", vTechs[vI].Id);
                        oCmd.Parameters.AddWithValue(":f", vFrom);
                        oCmd.Parameters.AddWithValue(":t", vTo);
                        using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        {
                            while (oReader.Read())
                            {
                                vRow.Completed++;
                                string vEndText = RStr(oReader, "actual_end");
                                string vDueText = RStr(oReader, "sla_due_at");
                                if ((vEndText.Length != 0) && (vDueText.Length != 0) &&
                                    (string.CompareOrdinal(vEndText, vDueText) <= 0))
                                    vRow.SlaMet++;
                                DateTime vStartAt = RDate(oReader, "actual_start");
                                DateTime vEndAt = ParseFieldTimestamp(vEndText);
                                if ((!IsZeroDate(vStartAt)) && (vEndAt > vStartAt))
                                {
                                    double vSpan = (vEndAt - vStartAt).TotalHours;
                                    if (vSpan < 240)
                                        vRow.Hours = vRow.Hours + vSpan;
                                }
                            }
                        }
                    }

                    vRow.Satisfaction = ExecScalarDouble(oConn,
                        "SELECT COALESCE(AVG(CAST(e.detail AS REAL)), 0) AS avg_rating " +
                        "FROM job_events e JOIN jobs j ON j.id = e.job_id " +
                        "WHERE e.kind = 'rating' AND j.technician_id = :tid " +
                        "AND j.created_at >= :f AND j.created_at < :t",
                        ":tid", vTechs[vI].Id, ":f", vFrom, ":t", vTo);

                    vRow.Utilisation = (vRow.Hours / vCapacity) * 100;
                    if (vRow.Utilisation < 0)
                        vRow.Utilisation = 0;
                    if (vRow.Utilisation > 100)
                        vRow.Utilisation = 100;

                    vResult[vI] = vRow;
                }
            }

            // The sparkline column, filled after the connection is back in the pool.
            for (int vI = 0; vI < vResult.Length; vI++)
            {
                double[] vTrend = GetTechWeeklySeries(vResult[vI].TechnicianId, 8);
                string vTrendText = "";
                for (int vJ = 0; vJ < vTrend.Length; vJ++)
                {
                    if (vTrendText.Length != 0)
                        vTrendText = vTrendText + ",";
                    vTrendText = vTrendText + ((int)Math.Round(vTrend[vJ],
                        MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
                }
                vResult[vI].Trend = vTrendText;
            }
            return vResult;
        }

        // One point per day in [aFrom, aTo): Value = created, Value2 = completed.
        public TFieldPoint[] GetJobsPerDaySeries(DateTime aFrom, DateTime aTo)
        {
            if (IsZeroDate(aFrom) || (aTo <= aFrom))
                return new TFieldPoint[0];
            DateTime vBase = aFrom.Date;
            int vDays = (int)(aTo.Date - vBase).TotalDays;
            if (vDays <= 0)
                return new TFieldPoint[0];
            if (vDays > 400)
                vDays = 400;

            TFieldPoint[] vResult = new TFieldPoint[vDays];
            for (int vI = 0; vI < vDays; vI++)
            {
                vResult[vI] = new TFieldPoint();
                vResult[vI].Label_ = vBase.AddDays(vI).ToString("MM-dd",
                    CultureInfo.InvariantCulture);
            }

            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT created_at FROM jobs WHERE created_at >= :f AND created_at < :t"))
                {
                    oCmd.Parameters.AddWithValue(":f", FormatFieldTimestamp(vBase));
                    oCmd.Parameters.AddWithValue(":t",
                        FormatFieldTimestamp(vBase.AddDays(vDays)));
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            DateTime vAt = ParseFieldTimestamp(
                                oReader.IsDBNull(0) ? "" : oReader.GetString(0));
                            if (!IsZeroDate(vAt))
                            {
                                int vIdx = (int)(vAt.Date - vBase).TotalDays;
                                if ((vIdx >= 0) && (vIdx < vDays))
                                    vResult[vIdx].Value = vResult[vIdx].Value + 1;
                            }
                        }
                    }
                }

                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT actual_end FROM jobs WHERE status = 'complete' " +
                    "AND actual_end >= :f AND actual_end < :t"))
                {
                    oCmd.Parameters.AddWithValue(":f", FormatFieldTimestamp(vBase));
                    oCmd.Parameters.AddWithValue(":t",
                        FormatFieldTimestamp(vBase.AddDays(vDays)));
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            DateTime vAt = ParseFieldTimestamp(
                                oReader.IsDBNull(0) ? "" : oReader.GetString(0));
                            if (!IsZeroDate(vAt))
                            {
                                int vIdx = (int)(vAt.Date - vBase).TotalDays;
                                if ((vIdx >= 0) && (vIdx < vDays))
                                    vResult[vIdx].Value2 = vResult[vIdx].Value2 + 1;
                            }
                        }
                    }
                }
            }
            return vResult;
        }

        // One point per status, Value = job count.
        public TFieldPoint[] GetStatusBreakdown()
        {
            string[] vAll = FieldConst.FieldStatusList();
            TFieldPoint[] vResult = new TFieldPoint[vAll.Length];
            for (int vI = 0; vI < vAll.Length; vI++)
            {
                vResult[vI] = new TFieldPoint();
                vResult[vI].Label_ = FieldConst.FieldStatusLabel(vAll[vI]);
            }

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT status, COUNT(*) AS n FROM jobs GROUP BY status"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    string vStatus = RStr(oReader, "status").Trim().ToLowerInvariant();
                    for (int vI = 0; vI < vAll.Length; vI++)
                        if (vAll[vI] == vStatus)
                        {
                            vResult[vI].Value = RInt(oReader, "n");
                            break;
                        }
                }
            }
            return vResult;
        }

        // Jobs by weekday (0 = Monday) and hour of scheduled_start.
        public TFieldHeatCell[] GetHeatmap(DateTime aFrom, DateTime aTo)
        {
            int[,] vGrid = new int[7, 24];

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT scheduled_start FROM jobs " +
                "WHERE scheduled_start >= :f AND scheduled_start < :t"))
            {
                oCmd.Parameters.AddWithValue(":f", LowerBoundText(aFrom));
                oCmd.Parameters.AddWithValue(":t", UpperBoundText(aTo));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        DateTime vAt = ParseFieldTimestamp(
                            oReader.IsDBNull(0) ? "" : oReader.GetString(0));
                        if (!IsZeroDate(vAt))
                        {
                            // DayOfTheWeek is 1 = Monday, and the record wants 0 = Monday.
                            int vDay = DayOfTheWeek(vAt) - 1;
                            int vHour = vAt.Hour;
                            if ((vDay >= 0) && (vDay <= 6) && (vHour >= 0) && (vHour <= 23))
                                vGrid[vDay, vHour]++;
                        }
                    }
                }
            }

            // The full grid comes back, zeroes included, so the renderer can index it
            // straight instead of hunting for missing cells.
            TFieldHeatCell[] vResult = new TFieldHeatCell[7 * 24];
            int vIdx = 0;
            for (int vDay = 0; vDay <= 6; vDay++)
                for (int vHour = 0; vHour <= 23; vHour++)
                {
                    vResult[vIdx] = new TFieldHeatCell();
                    vResult[vIdx].Weekday = vDay;
                    vResult[vIdx].Hour = vHour;
                    vResult[vIdx].Count = vGrid[vDay, vHour];
                    vIdx++;
                }
            return vResult;
        }

        // Parts revenue per customer, biggest first, at most aTop rows.
        public TFieldPoint[] GetRevenueByCustomer(DateTime aFrom, DateTime aTo, int aTop)
        {
            List<TFieldPoint> vResult = new List<TFieldPoint>();
            if (aTop <= 0)
                aTop = 10;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT COALESCE(c.name, '(unknown)') AS nm, " +
                "COALESCE(SUM(jp.qty * jp.unit_price), 0) AS rev " +
                "FROM job_parts jp JOIN jobs j ON j.id = jp.job_id " +
                "LEFT JOIN customers c ON c.id = j.customer_id " +
                "WHERE j.created_at >= :f AND j.created_at < :t " +
                "GROUP BY j.customer_id, c.name ORDER BY rev DESC" + LimitSQL(aTop, 0)))
            {
                oCmd.Parameters.AddWithValue(":f", LowerBoundText(aFrom));
                oCmd.Parameters.AddWithValue(":t", UpperBoundText(aTo));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        TFieldPoint oRow = new TFieldPoint();
                        oRow.Label_ = RStr(oReader, "nm");
                        oRow.Value = RFloat(oReader, "rev");
                        vResult.Add(oRow);
                    }
                }
            }
            return vResult.ToArray();
        }

        // Weekly completed-job counts for one technician, oldest first, aWeeks long.
        // Feeds the Sparkline on the manager report.
        public double[] GetTechWeeklySeries(long aTechnicianId, int aWeeks)
        {
            if (aWeeks <= 0)
                aWeeks = 8;
            if (aWeeks > 104)
                aWeeks = 104;
            double[] vResult = new double[aWeeks];
            if (aTechnicianId <= 0)
                return vResult;

            DateTime vEndBound = DateTime.Now.Date.AddDays(1);
            DateTime vStartBound = vEndBound.AddDays(-aWeeks * 7);

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT actual_end FROM jobs " +
                "WHERE technician_id = :tid AND status = 'complete' " +
                "AND actual_end >= :f AND actual_end < :t"))
            {
                oCmd.Parameters.AddWithValue(":tid", aTechnicianId);
                oCmd.Parameters.AddWithValue(":f", FormatFieldTimestamp(vStartBound));
                oCmd.Parameters.AddWithValue(":t", FormatFieldTimestamp(vEndBound));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        DateTime vAt = ParseFieldTimestamp(
                            oReader.IsDBNull(0) ? "" : oReader.GetString(0));
                        if (!IsZeroDate(vAt))
                        {
                            double vOffset = (vEndBound - vAt).TotalDays;
                            if (vOffset >= 0)
                            {
                                int vIdx = aWeeks - 1 - (int)Math.Truncate(vOffset / 7);
                                if ((vIdx >= 0) && (vIdx < aWeeks))
                                    vResult[vIdx] = vResult[vIdx] + 1;
                            }
                        }
                    }
                }
            }
            return vResult;
        }
    }
}
