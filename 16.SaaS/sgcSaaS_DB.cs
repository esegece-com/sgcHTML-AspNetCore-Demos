// ***************************************************************************
//  sgcSaaS - multi-tenant SaaS control plane demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\16.SaaS\sgcSaaS_DB.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
//  FireDAC (TFDConnection / FDManager / TFDQuery) is replaced by
//  Microsoft.Data.Sqlite. The pool builds one connection string from the
//  absolute database file path; Microsoft.Data.Sqlite pools the underlying
//  connections automatically per connection string, so the Delphi
//  RegisterDefinition / UnregisterDefinition pair has no managed counterpart
//  (see the comment on DefName).
//
//  Timestamps are stored exactly the way the Delphi stores them: TEXT in the
//  'yyyy-MM-ddTHH:mm:ss' shape written by SaaSTypes.FormatSaaSTimestamp and
//  read back by SaaSTypes.ParseSaaSTimestamp, so the SUBSTR(x, 1, 7)
//  month-bucket grouping of MRRSeries / SignupSeries / ChurnSeries and the
//  lexicographic ORDER BY of the usage series keep working unchanged.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using static SaaS.SaaSTypes;
using static SaaS.SaaSSessions;

namespace SaaS
{
    /// <summary>SaaS demo database error. Mirrors Delphi ESaaSDBError.</summary>
    public class ESaaSDBError : Exception
    {
        public ESaaSDBError(string message) : base(message) { }
    }

    // The unit-level constants and functions of sgcSaaS_DB.pas. The other files
    // pull them in with 'using static SaaS.SaaSDB;' so the call sites read
    // exactly like the Delphi ones.
    public static class SaaSDB
    {
        public const string CS_SAAS_DB_DEF_NAME = "sgcSaaS";

        // --------------------------------------------------------------------
        // The tenant-scoped SQL of the workspace, as named constants.
        //
        // These are the literal statements the workspace runs. /app/isolation
        // shows them verbatim, so what a sceptical reader sees on that page is
        // the same string the server executes, not a prettified copy. Every one
        // of them carries a ":tenant_id" bind, and TSaaSQuery.CreateScoped
        // refuses to run a statement that does not.
        // --------------------------------------------------------------------
        public const string CS_SQL_PROJECTS = "SELECT p.id, p.name, p.description, p.status, " +
            "p.owner_id, p.created_at, " +
            "COALESCE(u.display_name, '') AS owner_name, " +
            "(SELECT COUNT(*) FROM tasks t WHERE t.tenant_id = p.tenant_id " +
            "AND t.project_id = p.id) AS task_count, " +
            "(SELECT COUNT(*) FROM tasks t WHERE t.tenant_id = p.tenant_id " +
            "AND t.project_id = p.id AND t.status = 'done') AS done_count " +
            "FROM projects p LEFT JOIN users u ON u.id = p.owner_id " +
            "AND u.tenant_id = p.tenant_id " + "WHERE p.tenant_id = :tenant_id " +
            "ORDER BY p.created_at DESC";

        public const string CS_SQL_PROJECT_BY_ID = "SELECT id, name, description, status, owner_id, " +
            "created_at FROM projects WHERE tenant_id = :tenant_id AND id = :id";

        public const string CS_SQL_TASKS = "SELECT t.id, t.project_id, t.title, t.status, " +
            "t.assignee_id, t.due_at, t.created_at, " +
            "COALESCE(p.name, '') AS project_name, " +
            "COALESCE(u.display_name, '') AS assignee_name " +
            "FROM tasks t LEFT JOIN projects p ON p.id = t.project_id " +
            "AND p.tenant_id = t.tenant_id " +
            "LEFT JOIN users u ON u.id = t.assignee_id AND u.tenant_id = t.tenant_id " +
            "WHERE t.tenant_id = :tenant_id ORDER BY t.due_at";

        public const string CS_SQL_TEAM = "SELECT id, username, display_name, email, role, status, " +
            "last_login_at FROM users WHERE tenant_id = :tenant_id " +
            "ORDER BY CASE role WHEN 'owner' THEN 0 WHEN 'admin' THEN 1 " +
            "WHEN 'member' THEN 2 ELSE 3 END, display_name";

        public const string CS_SQL_INVOICES = "SELECT id, number, period_start, period_end, subtotal, " +
            "tax, total, status, issued_at, paid_at FROM invoices " +
            "WHERE tenant_id = :tenant_id ORDER BY issued_at DESC";

        public const string CS_SQL_AUDIT = "SELECT a.id, a.created_at, a.action, a.entity, " +
            "a.entity_id, a.detail, a.ip, COALESCE(u.display_name, 'system') " +
            "AS user_name FROM audit_log a LEFT JOIN users u ON u.id = a.user_id " +
            "WHERE a.tenant_id = :tenant_id ORDER BY a.id DESC LIMIT :lim OFFSET :off";

        public const string CS_SQL_NOTIFICATIONS = "SELECT id, title, body, kind, read_at, created_at " +
            "FROM notifications WHERE tenant_id = :tenant_id " +
            "AND (user_id = :user_id OR user_id = 0) ORDER BY id DESC";

        public const string CS_SQL_USAGE = "SELECT metric, value, recorded_at FROM usage_metrics " +
            "WHERE tenant_id = :tenant_id AND metric = :metric " +
            "ORDER BY recorded_at";

        public const string CS_SQL_COUNT_PROJECTS = "SELECT COUNT(*) AS c FROM projects " +
            "WHERE tenant_id = :tenant_id";

        // Escape the LIKE metacharacters so a user search string cannot widen
        // the match. Pair with ESCAPE '\' in the statement.
        public static string SaaSEscapeLike(string aValue)
        {
            string vResult = aValue == null ? "" : aValue;
            vResult = vResult.Replace("\\", "\\\\");
            vResult = vResult.Replace("%", "\\%");
            vResult = vResult.Replace("_", "\\_");
            return vResult;
        }

        // Whitelisted ORDER BY column for a list page. Anything unknown falls
        // back to the first allowed column, so a crafted ?sort= can never reach
        // the SQL.
        public static string SaaSSortColumn(string aValue, string[] aAllowed)
        {
            string vResult = "";
            if ((aAllowed == null) || (aAllowed.Length == 0))
                return vResult;
            vResult = aAllowed[0];
            for (int vI = 0; vI < aAllowed.Length; vI++)
                if (string.Equals(aValue, aAllowed[vI], StringComparison.OrdinalIgnoreCase))
                {
                    vResult = aAllowed[vI];
                    return vResult;
                }
            return vResult;
        }

        // Whitelisted ORDER BY direction: 'ASC' or 'DESC', nothing else.
        public static string SaaSSortDir(string aValue)
        {
            string vValue = aValue == null ? "" : aValue.Trim();
            if (string.Equals(vValue, "desc", StringComparison.OrdinalIgnoreCase))
                return "DESC";
            return "ASC";
        }

        // A URL-safe slug built from a display name.
        public static string SaaSSlugify(string aValue)
        {
            string vResult = "";
            char vLast = '-';
            string vValue = aValue == null ? "" : aValue;
            for (int vI = 0; vI < vValue.Length; vI++)
            {
                char vCh = vValue[vI];
                if ((vCh >= 'A') && (vCh <= 'Z'))
                    vCh = (char)(vCh + 32);
                if (((vCh >= 'a') && (vCh <= 'z')) || ((vCh >= '0') && (vCh <= '9')))
                {
                    vResult = vResult + vCh;
                    vLast = vCh;
                }
                else if (vLast != '-')
                {
                    vResult = vResult + '-';
                    vLast = '-';
                }
            }
            while ((vResult.Length > 0) && (vResult[vResult.Length - 1] == '-'))
                vResult = vResult.Substring(0, vResult.Length - 1);
            if (vResult.Length > 40)
                vResult = vResult.Substring(0, 40);
            return vResult;
        }
    }

    // ======================================================================= //
    //  TSaaSQuery                                                             //
    // ======================================================================= //

    // A pooled connection + statement pair with a lifetime the caller controls.
    //
    // CreateScoped is the only factory the tenant workspace ever uses. It
    // REFUSES a statement without a ":tenant_id" placeholder and binds the value
    // itself from the session, so "forgot the WHERE clause" cannot compile into
    // a working page: it raises on first use instead of quietly leaking rows.
    //
    // Delphi has named constructors; C# does not, so the Delphi
    // 'TSaaSQuery.CreateScoped(...)' becomes the static factory CreateScoped.
    // The FireDAC ':name' binds stay ':name' in the stored SQL text (the /sql
    // and /app/isolation pages display it verbatim) - Microsoft.Data.Sqlite
    // binds ':name' parameters natively.
    public class TSaaSQuery : IDisposable
    {
        private SqliteConnection FConn;
        private string FSQL;
        private long FTenantId;
        private bool FScoped;
        private DataTable FTable;
        private int FRowIndex;
        private int FRowsAffected;
        private readonly Dictionary<string, object> FParams =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public TSaaSQuery(TSaaSDBPool aPool, string aSQL)
        {
            FScoped = false;
            FTenantId = 0;
            Init(aPool, aSQL);
        }

        // Tenant-scoped. aTenantId must be > 0 and aSQL must contain :tenant_id.
        public static TSaaSQuery CreateScoped(TSaaSDBPool aPool, string aSQL,
            long aTenantId)
        {
            // Two invariants, both enforced before a single row can be read.
            if (aTenantId <= 0)
                throw new ESaaSDBError(
                    "Tenant-scoped query without a tenant: refusing to run.");
            if ((aSQL == null) || (aSQL.ToLowerInvariant().IndexOf(":tenant_id",
                StringComparison.Ordinal) < 0))
                throw new ESaaSDBError(
                    "Tenant-scoped query has no :tenant_id bind: refusing to run.");
            TSaaSQuery oResult = new TSaaSQuery(aPool, aSQL);
            oResult.FScoped = true;
            oResult.FTenantId = aTenantId;
            oResult.ParamInt("tenant_id", aTenantId);
            return oResult;
        }

        private void Init(TSaaSDBPool aPool, string aSQL)
        {
            if (aPool == null)
                throw new ESaaSDBError("TSaaSQuery: pool is nil");
            FSQL = aSQL == null ? "" : aSQL;
            FConn = aPool.Acquire();
        }

        public void Dispose()
        {
            try
            {
                if (FTable != null)
                {
                    FTable.Dispose();
                    FTable = null;
                }
            }
            catch
            {
                // A dataset teardown failure must never mask the real error.
            }
            if (FConn != null)
            {
                FConn.Dispose();
                FConn = null;
            }
        }

        // The live result set, for LoadFromDataSet bindings. Valid after Open.
        public DataTable DataSet
        {
            get { return FTable; }
        }

        // The exact statement text, for the /sql and /app/isolation pages.
        public string SQL
        {
            get { return FSQL; }
        }

        public long TenantId
        {
            get { return FTenantId; }
        }

        public bool Scoped
        {
            get { return FScoped; }
        }

        // Rows touched by the last Exec. Mirrors TFDQuery.RowsAffected, which
        // the Delphi reads through 'oQ.DataSet.RowsAffected'.
        public int RowsAffected
        {
            get { return FRowsAffected; }
        }

        // Replace the statement text on the SAME pooled connection. Mirrors the
        // Delphi 'oQ.DataSet.SQL.Text := ...', which the unit uses to follow an
        // INSERT with 'SELECT last_insert_rowid()'. Keeping the connection is
        // what makes last_insert_rowid() return the row just inserted.
        // Assigning SQL.Text re-parses the binds in FireDAC, so the parameter
        // values are dropped here too.
        public void SetSQL(string aSQL)
        {
            FSQL = aSQL == null ? "" : aSQL;
            FParams.Clear();
            CloseTable();
        }

        public void ParamInt(string aName, long aValue)
        {
            FParams[ParamName(aName)] = aValue;
        }

        public void ParamStr(string aName, string aValue)
        {
            FParams[ParamName(aName)] = aValue == null ? "" : aValue;
        }

        public void ParamFloat(string aName, double aValue)
        {
            FParams[ParamName(aName)] = aValue;
        }

        public void Open()
        {
            CloseTable();
            using (SqliteCommand oCmd = NewCommand())
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                FTable = LoadTable(oReader);
            }
            FRowIndex = 0;
        }

        public void Exec()
        {
            using (SqliteCommand oCmd = NewCommand())
            {
                FRowsAffected = oCmd.ExecuteNonQuery();
            }
        }

        public bool Eof
        {
            get { return (FTable == null) || (FRowIndex >= FTable.Rows.Count); }
        }

        public void Next()
        {
            FRowIndex++;
        }

        public string Str(string aField)
        {
            return AsStr(FieldValue(aField));
        }

        public long Int(string aField)
        {
            return AsInt(FieldValue(aField));
        }

        public double Flt(string aField)
        {
            return AsFlt(FieldValue(aField));
        }

        public DateTime Stamp(string aField)
        {
            return ParseSaaSTimestamp(Str(aField));
        }

        // Positional overloads for the Delphi 'oQ.DataSet.Fields[0].AsXxx'
        // scalar helpers.
        public string Str(int aIndex)
        {
            return AsStr(FieldValue(aIndex));
        }

        public long Int(int aIndex)
        {
            return AsInt(FieldValue(aIndex));
        }

        public double Flt(int aIndex)
        {
            return AsFlt(FieldValue(aIndex));
        }

        // --- internals ------------------------------------------------------ //

        private void CloseTable()
        {
            if (FTable != null)
            {
                FTable.Dispose();
                FTable = null;
            }
            FRowIndex = 0;
        }

        // ':name' is what the SQL constants carry, and what SQLite binds
        // natively. A caller that passes the bare name ('tenant_id') gets the
        // colon added here so both spellings work.
        private static string ParamName(string aName)
        {
            string vName = aName == null ? "" : aName;
            if (vName.Length == 0)
                return vName;
            if ((vName[0] == ':') || (vName[0] == '@') || (vName[0] == '$'))
                return vName;
            return ":" + vName;
        }

        private SqliteCommand NewCommand()
        {
            SqliteCommand oCmd = FConn.CreateCommand();
            oCmd.CommandText = FSQL;
            foreach (KeyValuePair<string, object> vPair in FParams)
                oCmd.Parameters.AddWithValue(vPair.Key,
                    vPair.Value == null ? DBNull.Value : vPair.Value);
            return oCmd;
        }

        private object FieldValue(string aField)
        {
            if (FTable == null)
                throw new ESaaSDBError("TSaaSQuery: dataset is not open");
            if (!FTable.Columns.Contains(aField))
                throw new ESaaSDBError("TSaaSQuery: field not found: " + aField);
            if (Eof)
                return DBNull.Value;
            return FTable.Rows[FRowIndex][aField];
        }

        private object FieldValue(int aIndex)
        {
            if (FTable == null)
                throw new ESaaSDBError("TSaaSQuery: dataset is not open");
            if ((aIndex < 0) || (aIndex >= FTable.Columns.Count))
                throw new ESaaSDBError("TSaaSQuery: field index out of range: " +
                    aIndex.ToString(CultureInfo.InvariantCulture));
            if (Eof)
                return DBNull.Value;
            return FTable.Rows[FRowIndex][aIndex];
        }

        private static string AsStr(object aValue)
        {
            if ((aValue == null) || (aValue == DBNull.Value))
                return "";
            if (aValue is string)
                return (string)aValue;
            return Convert.ToString(aValue, CultureInfo.InvariantCulture);
        }

        private static long AsInt(object aValue)
        {
            if ((aValue == null) || (aValue == DBNull.Value))
                return 0;
            if (aValue is long)
                return (long)aValue;
            try
            {
                return Convert.ToInt64(aValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        private static double AsFlt(object aValue)
        {
            if ((aValue == null) || (aValue == DBNull.Value))
                return 0;
            if (aValue is double)
                return (double)aValue;
            try
            {
                return Convert.ToDouble(aValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        // Materialise the reader into a DataTable. SQLite is dynamically typed,
        // so the column type is taken from the values actually returned: that is
        // what gives the sgcHTML grid bindings the same numeric-vs-text column
        // typing FireDAC hands them.
        private static DataTable LoadTable(SqliteDataReader aReader)
        {
            int vCount = aReader.FieldCount;
            string[] vNames = new string[vCount];
            for (int vI = 0; vI < vCount; vI++)
                vNames[vI] = aReader.GetName(vI);

            List<object[]> oRows = new List<object[]>();
            while (aReader.Read())
            {
                object[] vValues = new object[vCount];
                aReader.GetValues(vValues);
                for (int vI = 0; vI < vCount; vI++)
                    if (vValues[vI] == null)
                        vValues[vI] = DBNull.Value;
                oRows.Add(vValues);
            }

            DataTable oTable = new DataTable();
            oTable.Locale = CultureInfo.InvariantCulture;
            for (int vI = 0; vI < vCount; vI++)
            {
                string vName = vNames[vI];
                int vDup = 1;
                // FireDAC renames a duplicated result column rather than failing;
                // a DataTable refuses two columns with the same name.
                while (oTable.Columns.Contains(vName))
                {
                    vName = vNames[vI] + "_" +
                        vDup.ToString(CultureInfo.InvariantCulture);
                    vDup++;
                }
                oTable.Columns.Add(vName, ColumnType(oRows, vI));
            }

            for (int vR = 0; vR < oRows.Count; vR++)
            {
                object[] vValues = oRows[vR];
                DataRow oRow = oTable.NewRow();
                for (int vI = 0; vI < vCount; vI++)
                {
                    if (vValues[vI] == DBNull.Value)
                        oRow[vI] = DBNull.Value;
                    else if (oTable.Columns[vI].DataType == typeof(string))
                        oRow[vI] = Convert.ToString(vValues[vI],
                            CultureInfo.InvariantCulture);
                    else
                        oRow[vI] = vValues[vI];
                }
                oTable.Rows.Add(oRow);
            }
            return oTable;
        }

        // The common CLR type of one result column; string when the column is
        // empty, all NULL, or holds mixed storage classes.
        private static Type ColumnType(List<object[]> aRows, int aIndex)
        {
            Type vType = null;
            for (int vI = 0; vI < aRows.Count; vI++)
            {
                object vValue = aRows[vI][aIndex];
                if (vValue == DBNull.Value)
                    continue;
                Type vRowType = vValue.GetType();
                if (vType == null)
                    vType = vRowType;
                else if (vType != vRowType)
                    return typeof(string);
            }
            if (vType == null)
                vType = typeof(string);
            return vType;
        }
    }

    // ======================================================================= //
    //  TSaaSDBPool                                                            //
    // ======================================================================= //

    public class TSaaSDBPool : IDisposable
    {
        private readonly string FDatabaseFile;
        private readonly string FConnStr;
        private readonly string FDefName;

        public TSaaSDBPool(string aDatabaseFile)
        {
            string vFile = aDatabaseFile;
            if (string.IsNullOrEmpty(vFile))
                vFile = Path.Combine("data", "saas.db");
            // Resolve relative paths against the current directory (the EXE dir,
            // which the launcher sets).
            if (!Path.IsPathRooted(vFile))
                vFile = Path.Combine(Directory.GetCurrentDirectory(), vFile);
            FDatabaseFile = vFile;
            FDefName = SaaSDB.CS_SAAS_DB_DEF_NAME;
            EnsureDatabaseDir();

            // Build the connection string once. Microsoft.Data.Sqlite pools the
            // connections automatically per connection string, which is what the
            // Delphi FDManager.AddConnectionDef(Pooled=True, POOL_MaximumItems=8)
            // call buys there. RegisterDefinition / UnregisterDefinition /
            // FDefRegistered therefore have no managed counterpart and are NOT
            // ported: there is no FDManager to register with and nothing to
            // unregister on teardown.
            FConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = FDatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }

        public void Dispose()
        {
            // No FDManager connection definition to close/delete: see the note
            // in the constructor.
        }

        public string DatabaseFile
        {
            get { return FDatabaseFile; }
        }

        // The Delphi appends an instance-unique hex suffix here because every
        // pool instance registers its own FDManager definition. There is no
        // registry in the managed port, so the plain definition name is what the
        // diagnostics pages show.
        public string DefName
        {
            get { return FDefName; }
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

        private long ScalarInt(string aSQL, string[] aNames, long[] aValues)
        {
            long vResult = 0;
            using (TSaaSQuery oQ = new TSaaSQuery(this, aSQL))
            {
                for (int vI = 0; vI < aNames.Length; vI++)
                    oQ.ParamInt(aNames[vI], aValues[vI]);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = oQ.Int(0);
            }
            return vResult;
        }

        private double ScalarFloat(string aSQL, string[] aNames, long[] aValues)
        {
            double vResult = 0;
            using (TSaaSQuery oQ = new TSaaSQuery(this, aSQL))
            {
                for (int vI = 0; vI < aNames.Length; vI++)
                    oQ.ParamInt(aNames[vI], aValues[vI]);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = oQ.Flt(0);
            }
            return vResult;
        }

        // Empty open-array literals of the Delphi 'ScalarInt(SQL, [], [])' calls.
        private static readonly string[] CS_NO_NAMES = new string[0];
        private static readonly long[] CS_NO_VALUES = new long[0];

        // ==================================================================== //
        //  schema                                                              //
        // ==================================================================== //

        public void EnsureSchema()
        {
            string[] CS_TABLES = new string[]
            {
                "CREATE TABLE IF NOT EXISTS tenants (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "slug TEXT UNIQUE, " +
                "name TEXT, " + "plan_id INTEGER, " + "status TEXT, " +
                "trial_ends_at TEXT, " + "onboarding_step INTEGER DEFAULT 0, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS users (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "username TEXT UNIQUE, " + "email TEXT, " + "password_hash TEXT, " +
                "role TEXT, " + "display_name TEXT, " + "status TEXT, " +
                "last_login_at TEXT, " + "verify_token TEXT, " +
                "verify_expires_at TEXT, " + "verified_at TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS passkeys (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "user_id INTEGER, " +
                "credential_id TEXT, " + "public_key TEXT, " + "sign_count INTEGER, " +
                "device_name TEXT, " + "created_at TEXT, " + "last_used_at TEXT)",

                "CREATE TABLE IF NOT EXISTS plans (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "code TEXT UNIQUE, " +
                "name TEXT, " + "price_monthly REAL, " + "max_users INTEGER, " +
                "max_projects INTEGER, " + "max_storage_mb INTEGER, " +
                "features_json TEXT, " + "sort_order INTEGER)",

                "CREATE TABLE IF NOT EXISTS subscriptions (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "plan_id INTEGER, " + "started_at TEXT, " + "renews_at TEXT, " +
                "cancelled_at TEXT, " + "status TEXT)",

                "CREATE TABLE IF NOT EXISTS invoices (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "number TEXT, " + "period_start TEXT, " + "period_end TEXT, " +
                "subtotal REAL, " + "tax REAL, " + "total REAL, " + "status TEXT, " +
                "issued_at TEXT, " + "paid_at TEXT)",

                "CREATE TABLE IF NOT EXISTS invoice_lines (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "invoice_id INTEGER, " +
                "description TEXT, " + "qty REAL, " + "unit_price REAL, " +
                "line_total REAL)",

                "CREATE TABLE IF NOT EXISTS usage_metrics (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "metric TEXT, " + "value REAL, " + "recorded_at TEXT)",

                "CREATE TABLE IF NOT EXISTS invitations (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "email TEXT, " + "role TEXT, " + "token TEXT UNIQUE, " +
                "expires_at TEXT, " + "accepted_at TEXT, " + "invited_by INTEGER, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS projects (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "name TEXT, " + "description TEXT, " + "status TEXT, " +
                "owner_id INTEGER, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS tasks (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "project_id INTEGER, " + "title TEXT, " + "status TEXT, " +
                "assignee_id INTEGER, " + "due_at TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS feature_flags (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "flag TEXT, " + "enabled INTEGER)",

                "CREATE TABLE IF NOT EXISTS notifications (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "user_id INTEGER, " + "title TEXT, " + "body TEXT, " + "kind TEXT, " +
                "read_at TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS audit_log (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "tenant_id INTEGER, " +
                "user_id INTEGER, " + "action TEXT, " + "entity TEXT, " +
                "entity_id INTEGER, " + "detail TEXT, " + "ip TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS tenant_settings (" +
                "tenant_id INTEGER, " + "skey TEXT, " + "svalue TEXT, " +
                "PRIMARY KEY (tenant_id, skey))",

                "CREATE TABLE IF NOT EXISTS role_grants (" + "tenant_id INTEGER, " +
                "role TEXT, " + "permission TEXT, " + "granted INTEGER, " +
                "PRIMARY KEY (tenant_id, role, permission))"
            };

            string[] CS_INDEXES = new string[]
            {
                "CREATE INDEX IF NOT EXISTS ix_projects_tenant ON projects(tenant_id)",
                "CREATE INDEX IF NOT EXISTS ix_tasks_tenant ON tasks(tenant_id)",
                "CREATE INDEX IF NOT EXISTS ix_users_tenant ON users(tenant_id)",
                "CREATE INDEX IF NOT EXISTS ix_invoices_tenant ON invoices(tenant_id)",
                "CREATE INDEX IF NOT EXISTS ix_usage_tenant ON usage_metrics(tenant_id)",
                "CREATE INDEX IF NOT EXISTS ix_audit_tenant ON audit_log(tenant_id)",
                "CREATE INDEX IF NOT EXISTS ix_notif_tenant ON notifications(tenant_id)"
            };

            using (SqliteConnection oConn = Acquire())
            {
                for (int vI = 0; vI < CS_TABLES.Length; vI++)
                    using (SqliteCommand oCmd = oConn.CreateCommand())
                    {
                        oCmd.CommandText = CS_TABLES[vI];
                        oCmd.ExecuteNonQuery();
                    }
                for (int vI = 0; vI < CS_INDEXES.Length; vI++)
                    using (SqliteCommand oCmd = oConn.CreateCommand())
                    {
                        oCmd.CommandText = CS_INDEXES[vI];
                        oCmd.ExecuteNonQuery();
                    }
            }
        }

        // Seed the vendor superadmin + a read-only support account. Idempotent.
        public void SeedVendorStaff(string aUser, string aPasswordHash)
        {
            if ((aUser == null) || (aUser.Trim().Length == 0))
                return;
            string vNow = FormatSaaSTimestamp(DateTime.Now);

            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "SELECT COUNT(*) AS c FROM users WHERE tenant_id IS NULL"))
            {
                oQ.Open();
                if ((!oQ.Eof) && (oQ.Int("c") > 0))
                    return;

                AddStaff(oQ, vNow, aUser, aPasswordHash, CS_ROLE_SUPERADMIN,
                    "Platform Owner", "ops@saas.example");
                AddStaff(oQ, vNow, "support", aPasswordHash, CS_ROLE_SUPPORT,
                    "Support Desk", "support@saas.example");
            }
        }

        private static void AddStaff(TSaaSQuery aQ, string aNow, string aName,
            string aHash, string aRole, string aDisplay, string aEmail)
        {
            aQ.SetSQL("INSERT INTO users (tenant_id, username, email, " +
                "password_hash, role, display_name, status, verified_at, created_at) " +
                "VALUES (NULL, :u, :e, :p, :r, :d, 'active', :v, :c)");
            aQ.ParamStr("u", aName);
            aQ.ParamStr("e", aEmail);
            aQ.ParamStr("p", aHash);
            aQ.ParamStr("r", aRole);
            aQ.ParamStr("d", aDisplay);
            aQ.ParamStr("v", aNow);
            aQ.ParamStr("c", aNow);
            aQ.Exec();
        }

        // ==================================================================== //
        //  seeding                                                             //
        // ==================================================================== //

        // Mirrors the seeding TFDQuery of the Delphi: one statement text plus
        // named params that are re-bound between ExecSQL calls, all running on
        // the one seeding connection inside the one transaction. Assigning a new
        // statement drops the bound values, exactly like SQL.Text does.
        private sealed class TSeedCmd : IDisposable
        {
            private readonly SqliteConnection FConn;
            private readonly SqliteTransaction FTx;
            private string FSQL = "";
            private readonly Dictionary<string, object> FParams =
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            public TSeedCmd(SqliteConnection aConn, SqliteTransaction aTx)
            {
                FConn = aConn;
                FTx = aTx;
            }

            public void Dispose()
            {
                FParams.Clear();
            }

            public string SQL
            {
                set
                {
                    FSQL = value == null ? "" : value;
                    FParams.Clear();
                }
            }

            public void ParamInt(string aName, long aValue)
            {
                FParams[":" + aName] = aValue;
            }

            public void ParamStr(string aName, string aValue)
            {
                FParams[":" + aName] = aValue == null ? "" : aValue;
            }

            public void ParamFloat(string aName, double aValue)
            {
                FParams[":" + aName] = aValue;
            }

            private SqliteCommand NewCommand(string aSQL)
            {
                SqliteCommand oCmd = FConn.CreateCommand();
                oCmd.CommandText = aSQL;
                if (FTx != null)
                    oCmd.Transaction = FTx;
                return oCmd;
            }

            public void Exec()
            {
                using (SqliteCommand oCmd = NewCommand(FSQL))
                {
                    foreach (KeyValuePair<string, object> vPair in FParams)
                        oCmd.Parameters.AddWithValue(vPair.Key, vPair.Value);
                    oCmd.ExecuteNonQuery();
                }
            }

            // The scalar of the current statement (used for the seed guard).
            public long ScalarInt()
            {
                using (SqliteCommand oCmd = NewCommand(FSQL))
                {
                    foreach (KeyValuePair<string, object> vPair in FParams)
                        oCmd.Parameters.AddWithValue(vPair.Key, vPair.Value);
                    object vValue = oCmd.ExecuteScalar();
                    if ((vValue == null) || (vValue == DBNull.Value))
                        return 0;
                    return Convert.ToInt64(vValue, CultureInfo.InvariantCulture);
                }
            }

            // The Delphi runs 'SELECT last_insert_rowid() AS id' on the same
            // connection; the rowid is per connection, which is why every seed
            // insert and this read share one.
            public long LastId()
            {
                using (SqliteCommand oCmd = NewCommand("SELECT last_insert_rowid() AS id"))
                {
                    object vValue = oCmd.ExecuteScalar();
                    if ((vValue == null) || (vValue == DBNull.Value))
                        return 0;
                    return Convert.ToInt64(vValue, CultureInfo.InvariantCulture);
                }
            }
        }

        // Seed plans, 12 tenants in mixed states, ~70 users, projects, tasks,
        // 12 months of usage + invoices, flags, notifications and audit rows.
        // Runs exactly once (guarded on an empty tenants table).
        public void SeedDemoDataIfEmpty()
        {
            string[] CS_TENANT_NAMES = new string[]
            {
                "Northwind Traders", "Contoso Ltd", "Fabrikam Inc",
                "Adventure Works", "Wide World Importers", "Tailspin Toys",
                "Litware Inc", "Proseware GmbH", "Blue Yonder Airlines",
                "Coho Vineyard", "Alpine Ski House", "Lucerne Publishing"
            };
            string[] CS_TENANT_STATUS = new string[]
            {
                CS_TENANT_ACTIVE, CS_TENANT_ACTIVE, CS_TENANT_TRIAL,
                CS_TENANT_ACTIVE, CS_TENANT_PAST_DUE, CS_TENANT_ACTIVE,
                CS_TENANT_TRIAL, CS_TENANT_ACTIVE, CS_TENANT_SUSPENDED,
                CS_TENANT_ACTIVE, CS_TENANT_TRIAL, CS_TENANT_ACTIVE
            };
            // Index into the seeded plans array (0 Free, 1 Starter, 2 Pro,
            // 3 Enterprise).
            int[] CS_TENANT_PLAN = new int[] { 2, 3, 0, 2, 1, 3, 0, 2, 1, 1, 0, 3 };
            // Different tenants must hold visibly different volumes so the
            // isolation proof is convincing at a glance.
            int[] CS_TENANT_USERS = new int[] { 9, 12, 3, 8, 5, 11, 2, 7, 4, 6, 3, 10 };
            int[] CS_TENANT_PROJECTS = new int[] { 7, 11, 2, 6, 4, 9, 1, 5, 3, 4, 2, 8 };
            string[] CS_ROLES = new string[]
            {
                CS_ROLE_OWNER, CS_ROLE_ADMIN, CS_ROLE_MEMBER, CS_ROLE_READONLY
            };
            string[] CS_FIRST = new string[]
            {
                "Alice", "Bob", "Carol", "David", "Emma", "Frank",
                "Grace", "Henry", "Irene", "Jack", "Karen", "Louis"
            };
            string[] CS_LAST = new string[]
            {
                "Johnson", "Smith", "White", "Brown", "Davis", "Miller",
                "Lee", "Ford", "Novak", "Turner", "Walsh", "Ortiz"
            };
            string[] CS_PROJECT_WORDS = new string[]
            {
                "Onboarding Revamp", "Warehouse Sync", "Mobile Rollout",
                "Billing Migration", "Data Cleanup", "Partner Portal",
                "Cost Review", "Field Service", "Reporting Refresh",
                "Compliance Pack", "Supply Pipeline", "Customer Insights"
            };
            string[] CS_TASK_WORDS = new string[]
            {
                "Draft the specification", "Review the data model",
                "Wire the import job", "Fix the login redirect",
                "Add the audit rows", "Write the migration",
                "Update the price list", "Chase the open tickets",
                "Package the release", "Refresh the dashboard"
            };
            string[] CS_TASK_STATUS = new string[] { "todo", "doing", "review", "done" };
            string[] CS_FLAGS = new string[]
            {
                "beta_dashboard", "api_v2", "sso_saml", "usage_alerts", "export_pdf"
            };

            DateTime vNow = DateTime.Now;
            long vSeed = 20260822;
            long[] vPlanIds = new long[4];

            // Deterministic pseudo-random so a rebuilt demo database looks the
            // same. The Delphi seeds RandSeed by hand with this LCG instead of
            // calling Randomize, so the managed port does the same arithmetic
            // rather than using System.Random.
            int NextSeed(int aMax)
            {
                vSeed = (vSeed * 1103515245 + 12345) & 0x7FFFFFFF;
                if (aMax <= 0)
                    return 0;
                return (int)(vSeed % aMax);
            }

            using (SqliteConnection oConn = Acquire())
            {
                long vCount;
                using (TSeedCmd oGuard = new TSeedCmd(oConn, null))
                {
                    oGuard.SQL = "SELECT COUNT(*) AS c FROM tenants";
                    vCount = oGuard.ScalarInt();
                }
                if (vCount > 0)
                    return;

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                using (TSeedCmd oQ = new TSeedCmd(oConn, oTx))
                {
                    void AddPlan(int aIndex, string aCode, string aName,
                        double aPrice, int aUsers, int aProjects, int aStorage,
                        string aFeatures)
                    {
                        oQ.SQL = "INSERT INTO plans (code, name, price_monthly, max_users, " +
                            "max_projects, max_storage_mb, features_json, sort_order) " +
                            "VALUES (:c, :n, :p, :mu, :mp, :ms, :f, :s)";
                        oQ.ParamStr("c", aCode);
                        oQ.ParamStr("n", aName);
                        oQ.ParamFloat("p", aPrice);
                        oQ.ParamInt("mu", aUsers);
                        oQ.ParamInt("mp", aProjects);
                        oQ.ParamInt("ms", aStorage);
                        oQ.ParamStr("f", aFeatures);
                        oQ.ParamInt("s", aIndex);
                        oQ.Exec();
                        vPlanIds[aIndex] = oQ.LastId();
                    }

                    try
                    {
                        AddPlan(0, "free", "Free", 0, 3, 2, 500,
                            "[\"1 workspace\",\"Community support\"]");
                        AddPlan(1, "starter", "Starter", 29, 10, 10, 5000,
                            "[\"Email support\",\"Daily backups\"]");
                        AddPlan(2, "pro", "Pro", 99, 50, 100, 50000,
                            "[\"Priority support\",\"SSO\",\"Audit log\"]");
                        AddPlan(3, "enterprise", "Enterprise", 399, 500, 1000, 500000,
                            "[\"Dedicated CSM\",\"SAML SSO\",\"99.9% SLA\"]");

                        // Global feature flags (tenant_id NULL).
                        for (int vI = 0; vI < CS_FLAGS.Length; vI++)
                        {
                            oQ.SQL = "INSERT INTO feature_flags (tenant_id, flag, " +
                                "enabled) VALUES (NULL, :f, :e)";
                            oQ.ParamStr("f", CS_FLAGS[vI]);
                            if (vI < 2)
                                oQ.ParamInt("e", 1);
                            else
                                oQ.ParamInt("e", 0);
                            oQ.Exec();
                        }

                        List<long> oUserIds = new List<long>();
                        List<long> oProjectIds = new List<long>();

                        for (int vI = 0; vI < CS_TENANT_NAMES.Length; vI++)
                        {
                            oUserIds.Clear();
                            oProjectIds.Clear();

                            // Spread signups over the last 14 months so every
                            // dashboard bucket has content.
                            DateTime vCreated = vNow.AddMonths(-(13 - vI))
                                .AddDays(-NextSeed(20));
                            oQ.SQL = "INSERT INTO tenants (slug, name, plan_id, " +
                                "status, trial_ends_at, onboarding_step, created_at) " +
                                "VALUES (:s, :n, :p, :st, :t, :o, :c)";
                            oQ.ParamStr("s", SaaSDB.SaaSSlugify(CS_TENANT_NAMES[vI]));
                            oQ.ParamStr("n", CS_TENANT_NAMES[vI]);
                            oQ.ParamInt("p", vPlanIds[CS_TENANT_PLAN[vI]]);
                            oQ.ParamStr("st", CS_TENANT_STATUS[vI]);
                            if (CS_TENANT_STATUS[vI] == CS_TENANT_TRIAL)
                            {
                                // One trial is about to expire (2 days out).
                                if (vI == 10)
                                    oQ.ParamStr("t", FormatSaaSTimestamp(vNow.AddDays(2)));
                                else
                                    oQ.ParamStr("t",
                                        FormatSaaSTimestamp(vNow.AddDays(9 + vI)));
                            }
                            else
                                oQ.ParamStr("t", "");
                            // The two newest tenants are still mid-onboarding.
                            if (vI >= 10)
                                oQ.ParamInt("o", 1 + (vI % 2));
                            else
                                oQ.ParamInt("o", 4);
                            oQ.ParamStr("c", FormatSaaSTimestamp(vCreated));
                            oQ.Exec();
                            long vTenantId = oQ.LastId();

                            // Members. The first is always the owner.
                            long vOwnerId = 0;
                            for (int vJ = 0; vJ <= CS_TENANT_USERS[vI] - 1; vJ++)
                            {
                                oQ.SQL = "INSERT INTO users (tenant_id, username, " +
                                    "email, password_hash, role, display_name, status, " +
                                    "last_login_at, verified_at, created_at) " +
                                    "VALUES (:t, :u, :e, :p, :r, :d, 'active', :l, :v, :c)";
                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamStr("u",
                                    CS_FIRST[vJ % 12].ToLowerInvariant() + "." +
                                    SaaSDB.SaaSSlugify(CS_TENANT_NAMES[vI]) +
                                    vJ.ToString(CultureInfo.InvariantCulture));
                                oQ.ParamStr("e",
                                    CS_FIRST[vJ % 12].ToLowerInvariant() + "@" +
                                    SaaSDB.SaaSSlugify(CS_TENANT_NAMES[vI]) + ".example");
                                // Seeded members sign in through the owner login
                                // only; the hash slot is deliberately left
                                // unusable ('*' never matches bcrypt).
                                oQ.ParamStr("p", "*");
                                if (vJ == 0)
                                    oQ.ParamStr("r", CS_ROLE_OWNER);
                                else
                                    oQ.ParamStr("r", CS_ROLES[1 + (vJ % 3)]);
                                oQ.ParamStr("d", CS_FIRST[vJ % 12] + " " +
                                    CS_LAST[(vJ + vI) % 12]);
                                oQ.ParamStr("l",
                                    FormatSaaSTimestamp(vNow.AddDays(-NextSeed(30))));
                                oQ.ParamStr("v", FormatSaaSTimestamp(vCreated));
                                oQ.ParamStr("c", FormatSaaSTimestamp(vCreated));
                                oQ.Exec();
                                oUserIds.Add(oQ.LastId());
                                if (vJ == 0)
                                    vOwnerId = oUserIds[0];
                            }

                            // Projects, spread across today / this week / this
                            // month / older.
                            for (int vJ = 0; vJ <= CS_TENANT_PROJECTS[vI] - 1; vJ++)
                            {
                                switch (vJ % 6)
                                {
                                    case 0:
                                        vCreated = vNow;
                                        break;
                                    case 1:
                                        vCreated = vNow.AddDays(-1);
                                        break;
                                    case 2:
                                        vCreated = vNow.AddDays(-4);
                                        break;
                                    case 3:
                                        vCreated = vNow.AddDays(-20);
                                        break;
                                    case 4:
                                        vCreated = vNow.AddMonths(-3);
                                        break;
                                    default:
                                        vCreated = vNow.AddMonths(-7);
                                        break;
                                }
                                string vProjName = CS_PROJECT_WORDS[(vJ + vI) % 12];
                                oQ.SQL = "INSERT INTO projects (tenant_id, name, " +
                                    "description, status, owner_id, created_at) " +
                                    "VALUES (:t, :n, :d, :s, :o, :c)";
                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamStr("n", vProjName);
                                oQ.ParamStr("d", vProjName + " for " +
                                    CS_TENANT_NAMES[vI] + ".");
                                if (vJ % 5 == 0)
                                    oQ.ParamStr("s", "on_hold");
                                else if (vJ % 7 == 3)
                                    oQ.ParamStr("s", "archived");
                                else
                                    oQ.ParamStr("s", "active");
                                oQ.ParamInt("o", oUserIds[NextSeed(oUserIds.Count)]);
                                oQ.ParamStr("c", FormatSaaSTimestamp(vCreated));
                                oQ.Exec();
                                oProjectIds.Add(oQ.LastId());
                            }

                            // Tasks per project.
                            for (int vJ = 0; vJ <= oProjectIds.Count - 1; vJ++)
                            {
                                // The Delphi 'for vK := 0 to 3 + NextSeed(4)'
                                // evaluates its bound once per project.
                                int vTaskLast = 3 + NextSeed(4);
                                for (int vK = 0; vK <= vTaskLast; vK++)
                                {
                                    oQ.SQL = "INSERT INTO tasks (tenant_id, project_id, " +
                                        "title, status, assignee_id, due_at, created_at) " +
                                        "VALUES (:t, :p, :ti, :s, :a, :d, :c)";
                                    oQ.ParamInt("t", vTenantId);
                                    oQ.ParamInt("p", oProjectIds[vJ]);
                                    oQ.ParamStr("ti", CS_TASK_WORDS[(vJ + vK) % 10]);
                                    oQ.ParamStr("s", CS_TASK_STATUS[(vJ + vK) % 4]);
                                    oQ.ParamInt("a", oUserIds[NextSeed(oUserIds.Count)]);
                                    oQ.ParamStr("d", FormatSaaSTimestamp(
                                        vNow.AddDays(NextSeed(26) - 5)));
                                    oQ.ParamStr("c", FormatSaaSTimestamp(
                                        vNow.AddDays(-NextSeed(60))));
                                    oQ.Exec();
                                }
                            }

                            // Subscription.
                            oQ.SQL = "INSERT INTO subscriptions (tenant_id, plan_id, " +
                                "started_at, renews_at, cancelled_at, status) " +
                                "VALUES (:t, :p, :s, :r, '', :st)";
                            oQ.ParamInt("t", vTenantId);
                            oQ.ParamInt("p", vPlanIds[CS_TENANT_PLAN[vI]]);
                            oQ.ParamStr("s",
                                FormatSaaSTimestamp(vNow.AddMonths(-(13 - vI))));
                            oQ.ParamStr("r", FormatSaaSTimestamp(vNow.AddMonths(1)));
                            oQ.ParamStr("st", CS_TENANT_STATUS[vI]);
                            oQ.Exec();

                            // 12 months of usage + invoices.
                            double vPlanPrice = 0;
                            switch (CS_TENANT_PLAN[vI])
                            {
                                case 1:
                                    vPlanPrice = 29;
                                    break;
                                case 2:
                                    vPlanPrice = 99;
                                    break;
                                case 3:
                                    vPlanPrice = 399;
                                    break;
                            }
                            int vNumber = 1000 + (vI * 40);
                            for (int vJ = 11; vJ >= 0; vJ--)
                            {
                                DateTime vMonth = vNow.AddMonths(-vJ);
                                string vStamp = FormatSaaSTimestamp(StartOfTheMonth(vMonth));

                                oQ.SQL = "INSERT INTO usage_metrics (tenant_id, metric, " +
                                    "value, recorded_at) VALUES (:t, :m, :v, :r)";
                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamStr("m", "users");
                                oQ.ParamFloat("v",
                                    Math.Max(1, CS_TENANT_USERS[vI] - (vJ / 3)));
                                oQ.ParamStr("r", vStamp);
                                oQ.Exec();

                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamStr("m", "projects");
                                oQ.ParamFloat("v",
                                    Math.Max(1, CS_TENANT_PROJECTS[vI] - (vJ / 4)));
                                oQ.ParamStr("r", vStamp);
                                oQ.Exec();

                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamStr("m", "storage_mb");
                                oQ.ParamFloat("v", 120 + (vI * 37) +
                                    ((11 - vJ) * (18 + vI)));
                                oQ.ParamStr("r", vStamp);
                                oQ.Exec();

                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamStr("m", "api_calls");
                                oQ.ParamFloat("v", 800 + NextSeed(4000) +
                                    ((11 - vJ) * 120));
                                oQ.ParamStr("r", vStamp);
                                oQ.Exec();

                                if (vPlanPrice > 0)
                                {
                                    double vSub = vPlanPrice;
                                    double vTax = Math.Round(vSub * 0.21, 2,
                                        MidpointRounding.ToEven);
                                    vNumber++;
                                    oQ.SQL = "INSERT INTO invoices (tenant_id, number, " +
                                        "period_start, period_end, subtotal, tax, total, status, " +
                                        "issued_at, paid_at) VALUES (:t, :n, :ps, :pe, :s, :x, " +
                                        ":o, :st, :i, :p)";
                                    oQ.ParamInt("t", vTenantId);
                                    oQ.ParamStr("n", "INV-" +
                                        vNumber.ToString(CultureInfo.InvariantCulture));
                                    oQ.ParamStr("ps", vStamp);
                                    oQ.ParamStr("pe",
                                        FormatSaaSTimestamp(EndOfTheMonth(vMonth)));
                                    oQ.ParamFloat("s", vSub);
                                    oQ.ParamFloat("x", vTax);
                                    oQ.ParamFloat("o", vSub + vTax);
                                    if ((vJ == 0) &&
                                        (CS_TENANT_STATUS[vI] == CS_TENANT_PAST_DUE))
                                    {
                                        oQ.ParamStr("st", "overdue");
                                        oQ.ParamStr("p", "");
                                    }
                                    else if (vJ == 0)
                                    {
                                        oQ.ParamStr("st", "open");
                                        oQ.ParamStr("p", "");
                                    }
                                    else
                                    {
                                        oQ.ParamStr("st", "paid");
                                        oQ.ParamStr("p", FormatSaaSTimestamp(
                                            StartOfTheMonth(vMonth).AddDays(3)));
                                    }
                                    oQ.ParamStr("i", vStamp);
                                    oQ.Exec();
                                    long vInvoiceId = oQ.LastId();

                                    oQ.SQL = "INSERT INTO invoice_lines (invoice_id, " +
                                        "description, qty, unit_price, line_total) " +
                                        "VALUES (:i, :d, :q, :u, :l)";
                                    oQ.ParamInt("i", vInvoiceId);
                                    oQ.ParamStr("d", "Subscription - " +
                                        vMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture));
                                    oQ.ParamFloat("q", 1);
                                    oQ.ParamFloat("u", vPlanPrice);
                                    oQ.ParamFloat("l", vPlanPrice);
                                    oQ.Exec();
                                }
                            }

                            // A couple of tenant-scoped notifications.
                            oQ.SQL = "INSERT INTO notifications (tenant_id, user_id, " +
                                "title, body, kind, read_at, created_at) " +
                                "VALUES (:t, 0, :ti, :b, :k, '', :c)";
                            oQ.ParamInt("t", vTenantId);
                            oQ.ParamStr("ti", "Welcome to sgcSaaS");
                            oQ.ParamStr("b", "Your workspace for " +
                                CS_TENANT_NAMES[vI] + " is ready.");
                            oQ.ParamStr("k", "info");
                            oQ.ParamStr("c", FormatSaaSTimestamp(vCreated));
                            oQ.Exec();

                            oQ.ParamInt("t", vTenantId);
                            oQ.ParamStr("ti", "Monthly invoice issued");
                            oQ.ParamStr("b",
                                "The invoice for this period is available under Billing.");
                            oQ.ParamStr("k", "billing");
                            oQ.ParamStr("c", FormatSaaSTimestamp(vNow.AddDays(-2)));
                            oQ.Exec();

                            // Default role grants for the matrix page.
                            for (int vJ = 0; vJ < CS_ROLES.Length; vJ++)
                                for (int vK = 0; vK <= 5; vK++)
                                {
                                    oQ.SQL = "INSERT OR REPLACE INTO role_grants " +
                                        "(tenant_id, role, permission, granted) " +
                                        "VALUES (:t, :r, :p, :g)";
                                    oQ.ParamInt("t", vTenantId);
                                    oQ.ParamStr("r", CS_ROLES[vJ]);
                                    switch (vK)
                                    {
                                        case 0:
                                            oQ.ParamStr("p", "project.view");
                                            break;
                                        case 1:
                                            oQ.ParamStr("p", "project.edit");
                                            break;
                                        case 2:
                                            oQ.ParamStr("p", "task.edit");
                                            break;
                                        case 3:
                                            oQ.ParamStr("p", "team.manage");
                                            break;
                                        case 4:
                                            oQ.ParamStr("p", "billing.manage");
                                            break;
                                        default:
                                            oQ.ParamStr("p", "settings.manage");
                                            break;
                                    }
                                    // owner everything, admin all but billing,
                                    // member work only, readonly view only.
                                    if ((vJ == 0) || ((vJ == 1) && (vK != 4)) ||
                                        ((vJ == 2) && (vK <= 2)) ||
                                        ((vJ == 3) && (vK == 0)))
                                        oQ.ParamInt("g", 1);
                                    else
                                        oQ.ParamInt("g", 0);
                                    oQ.Exec();
                                }

                            // A pending invitation for a few tenants.
                            if (vI % 3 == 0)
                            {
                                oQ.SQL = "INSERT INTO invitations (tenant_id, email, " +
                                    "role, token, expires_at, accepted_at, invited_by, " +
                                    "created_at) VALUES (:t, :e, :r, :k, :x, '', :b, :c)";
                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamStr("e", "newhire@" +
                                    SaaSDB.SaaSSlugify(CS_TENANT_NAMES[vI]) + ".example");
                                oQ.ParamStr("r", CS_ROLE_MEMBER);
                                oQ.ParamStr("k", SaaSRandomToken(24));
                                oQ.ParamStr("x", FormatSaaSTimestamp(vNow.AddDays(6)));
                                oQ.ParamInt("b", vOwnerId);
                                oQ.ParamStr("c", FormatSaaSTimestamp(vNow.AddDays(-1)));
                                oQ.Exec();
                            }

                            // Audit history so /app/audit is never empty.
                            for (int vJ = 0; vJ <= 7; vJ++)
                            {
                                oQ.SQL = "INSERT INTO audit_log (tenant_id, user_id, " +
                                    "action, entity, entity_id, detail, ip, created_at) " +
                                    "VALUES (:t, :u, :a, :e, :i, :d, :p, :c)";
                                oQ.ParamInt("t", vTenantId);
                                oQ.ParamInt("u", oUserIds[NextSeed(oUserIds.Count)]);
                                switch (vJ % 4)
                                {
                                    case 0:
                                        oQ.ParamStr("a", "project.create");
                                        oQ.ParamStr("e", "project");
                                        break;
                                    case 1:
                                        oQ.ParamStr("a", "task.update");
                                        oQ.ParamStr("e", "task");
                                        break;
                                    case 2:
                                        oQ.ParamStr("a", "team.invite");
                                        oQ.ParamStr("e", "invitation");
                                        break;
                                    default:
                                        oQ.ParamStr("a", "auth.login");
                                        oQ.ParamStr("e", "user");
                                        break;
                                }
                                oQ.ParamInt("i", 1 + vJ);
                                oQ.ParamStr("d", "Seeded activity #" +
                                    (vJ + 1).ToString(CultureInfo.InvariantCulture));
                                oQ.ParamStr("p", "127.0.0.1");
                                oQ.ParamStr("c", FormatSaaSTimestamp(
                                    vNow.AddDays(-vJ).AddDays(-NextSeed(3))));
                                oQ.Exec();
                            }
                        }

                        oTx.Commit();
                    }
                    catch
                    {
                        oTx.Rollback();
                        throw;
                    }
                }
            }
        }

        // Delphi System.DateUtils StartOfTheMonth / EndOfTheMonth. EndOfTheMonth
        // is the last day at 23:59:59.999; the seconds-resolution timestamp mask
        // renders both the same way.
        private static DateTime StartOfTheMonth(DateTime aValue)
        {
            return new DateTime(aValue.Year, aValue.Month, 1);
        }

        private static DateTime EndOfTheMonth(DateTime aValue)
        {
            return new DateTime(aValue.Year, aValue.Month,
                DateTime.DaysInMonth(aValue.Year, aValue.Month), 23, 59, 59);
        }

        // ==================================================================== //
        //  users / auth                                                        //
        // ==================================================================== //

        private const string CS_USER_FIELDS = "SELECT id, COALESCE(tenant_id, 0) AS tenant_id, " +
            "username, email, password_hash, role, display_name, status, " +
            "COALESCE(last_login_at, '') AS last_login_at, " +
            "COALESCE(created_at, '') AS created_at, " +
            "COALESCE(verified_at, '') AS verified_at, " +
            "COALESCE(verify_token, '') AS verify_token, " +
            "COALESCE(verify_expires_at, '') AS verify_expires_at FROM users ";

        private static void ReadUser(TSaaSQuery aQ, out TSaaSUser aUser)
        {
            aUser = new TSaaSUser();
            aUser.Id = aQ.Int("id");
            aUser.TenantId = aQ.Int("tenant_id");
            aUser.Username = aQ.Str("username");
            aUser.Email = aQ.Str("email");
            aUser.PasswordHash = aQ.Str("password_hash");
            aUser.Role = aQ.Str("role");
            aUser.DisplayName = aQ.Str("display_name");
            aUser.Status = aQ.Str("status");
            aUser.LastLoginAt = aQ.Stamp("last_login_at");
            aUser.CreatedAt = aQ.Stamp("created_at");
            aUser.VerifiedAt = aQ.Stamp("verified_at");
            aUser.VerifyToken = aQ.Str("verify_token");
            aUser.VerifyExpiresAt = aQ.Stamp("verify_expires_at");
        }

        public bool GetUserByUsername(string aUsername, out TSaaSUser aUser)
        {
            aUser = null;
            if ((aUsername == null) || (aUsername.Trim().Length == 0))
                return false;
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                CS_USER_FIELDS + "WHERE LOWER(username) = LOWER(:u)"))
            {
                oQ.ParamStr("u", aUsername);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadUser(oQ, out aUser);
                return true;
            }
        }

        public bool GetUserByEmail(string aEmail, out TSaaSUser aUser)
        {
            aUser = null;
            if ((aEmail == null) || (aEmail.Trim().Length == 0))
                return false;
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                CS_USER_FIELDS + "WHERE LOWER(email) = LOWER(:e) ORDER BY id LIMIT 1"))
            {
                oQ.ParamStr("e", aEmail);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadUser(oQ, out aUser);
                return true;
            }
        }

        public bool GetUserById(long aId, out TSaaSUser aUser)
        {
            aUser = null;
            if (aId <= 0)
                return false;
            using (TSaaSQuery oQ = new TSaaSQuery(this, CS_USER_FIELDS + "WHERE id = :i"))
            {
                oQ.ParamInt("i", aId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadUser(oQ, out aUser);
                return true;
            }
        }

        public bool GetUserByVerifyToken(string aToken, out TSaaSUser aUser)
        {
            aUser = null;
            if ((aToken == null) || (aToken.Trim().Length == 0))
                return false;
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                CS_USER_FIELDS + "WHERE verify_token = :t"))
            {
                oQ.ParamStr("t", aToken);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadUser(oQ, out aUser);
                return true;
            }
        }

        public void MarkUserVerified(long aId)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this, "UPDATE users SET verified_at = :v, " +
                "verify_token = '', verify_expires_at = '', status = 'active' " +
                "WHERE id = :i"))
            {
                oQ.ParamStr("v", FormatSaaSTimestamp(DateTime.Now));
                oQ.ParamInt("i", aId);
                oQ.Exec();
            }
        }

        public void TouchLastLogin(long aId)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "UPDATE users SET last_login_at = :l WHERE id = :i"))
            {
                oQ.ParamStr("l", FormatSaaSTimestamp(DateTime.Now));
                oQ.ParamInt("i", aId);
                oQ.Exec();
            }
        }

        public bool EmailExists(string aEmail)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "SELECT COUNT(*) AS c FROM users WHERE LOWER(email) = LOWER(:e)"))
            {
                oQ.ParamStr("e", aEmail);
                oQ.Open();
                return (!oQ.Eof) && (oQ.Int("c") > 0);
            }
        }

        public bool UsernameExists(string aUsername)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "SELECT COUNT(*) AS c FROM users WHERE LOWER(username) = LOWER(:u)"))
            {
                oQ.ParamStr("u", aUsername);
                oQ.Open();
                return (!oQ.Eof) && (oQ.Int("c") > 0);
            }
        }

        // INSERT a user. aTenantId = 0 stores NULL (vendor staff).
        public long InsertUser(long aTenantId, string aUsername, string aEmail,
            string aPasswordHash, string aRole, string aDisplayName, string aStatus,
            string aVerifyToken, DateTime aVerifyExpires)
        {
            string vSQL;
            if (aTenantId > 0)
                vSQL = "INSERT INTO users (tenant_id, username, email, password_hash, " +
                    "role, display_name, status, verify_token, verify_expires_at, " +
                    "created_at) VALUES (:t, :u, :e, :p, :r, :d, :s, :k, :x, :c)";
            else
                vSQL = "INSERT INTO users (tenant_id, username, email, password_hash, " +
                    "role, display_name, status, verify_token, verify_expires_at, " +
                    "created_at) VALUES (NULL, :u, :e, :p, :r, :d, :s, :k, :x, :c)";
            using (TSaaSQuery oQ = new TSaaSQuery(this, vSQL))
            {
                if (aTenantId > 0)
                    oQ.ParamInt("t", aTenantId);
                oQ.ParamStr("u", aUsername);
                oQ.ParamStr("e", aEmail);
                oQ.ParamStr("p", aPasswordHash);
                oQ.ParamStr("r", aRole);
                oQ.ParamStr("d", aDisplayName);
                oQ.ParamStr("s", aStatus);
                oQ.ParamStr("k", aVerifyToken);
                oQ.ParamStr("x", FormatSaaSTimestamp(aVerifyExpires));
                oQ.ParamStr("c", FormatSaaSTimestamp(DateTime.Now));
                oQ.Exec();
                oQ.SetSQL("SELECT last_insert_rowid() AS id");
                oQ.Open();
                return oQ.Int("id");
            }
        }

        // ==================================================================== //
        //  tenant-scoped user management                                       //
        // ==================================================================== //

        public TSaaSUser[] ListTenantUsers(long aTenantId)
        {
            List<TSaaSUser> oList = new List<TSaaSUser>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, SaaSDB.CS_SQL_TEAM,
                aTenantId))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSUser vRow = new TSaaSUser();
                    vRow.PasswordHash = "";
                    vRow.VerifyToken = "";
                    vRow.CreatedAt = DateTime.MinValue;
                    vRow.VerifiedAt = DateTime.MinValue;
                    vRow.VerifyExpiresAt = DateTime.MinValue;
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = aTenantId;
                    vRow.Username = oQ.Str("username");
                    vRow.DisplayName = oQ.Str("display_name");
                    vRow.Email = oQ.Str("email");
                    vRow.Role = oQ.Str("role");
                    vRow.Status = oQ.Str("status");
                    vRow.LastLoginAt = oQ.Stamp("last_login_at");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public int CountTenantUsers(long aTenantId)
        {
            int vResult = 0;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT COUNT(*) AS c FROM users WHERE tenant_id = :tenant_id",
                aTenantId))
            {
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        public bool GetTenantUser(long aTenantId, long aUserId, out TSaaSUser aUser)
        {
            aUser = null;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                CS_USER_FIELDS + "WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamInt("i", aUserId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadUser(oQ, out aUser);
                return true;
            }
        }

        public bool UpdateTenantUserRole(long aTenantId, long aUserId, string aRole)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "UPDATE users SET role = :r " +
                "WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamStr("r", aRole);
                oQ.ParamInt("i", aUserId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public bool RemoveTenantUser(long aTenantId, long aUserId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "DELETE FROM users WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamInt("i", aUserId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public int CountTenantOwners(long aTenantId)
        {
            int vResult = 0;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "SELECT COUNT(*) AS c FROM users " +
                "WHERE tenant_id = :tenant_id AND role = 'owner'", aTenantId))
            {
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        // ==================================================================== //
        //  tenants                                                             //
        // ==================================================================== //

        private const string CS_TENANT_FIELDS = "SELECT t.id, t.slug, t.name, " +
            "COALESCE(t.plan_id, 0) AS plan_id, t.status, " +
            "COALESCE(t.trial_ends_at, '') AS trial_ends_at, " +
            "COALESCE(t.onboarding_step, 0) AS onboarding_step, " +
            "COALESCE(t.created_at, '') AS created_at, " +
            "COALESCE(p.code, '') AS plan_code, " +
            "COALESCE(p.name, '') AS plan_name, " +
            "COALESCE(p.price_monthly, 0) AS plan_price, " +
            "(SELECT COUNT(*) FROM users u WHERE u.tenant_id = t.id) AS user_count, " +
            "(SELECT COUNT(*) FROM projects r WHERE r.tenant_id = t.id) " +
            "AS project_count FROM tenants t LEFT JOIN plans p ON p.id = t.plan_id ";

        private static void ReadTenant(TSaaSQuery aQ, out TSaaSTenant aTenant)
        {
            aTenant = new TSaaSTenant();
            aTenant.Id = aQ.Int("id");
            aTenant.Slug = aQ.Str("slug");
            aTenant.Name = aQ.Str("name");
            aTenant.PlanId = aQ.Int("plan_id");
            aTenant.Status = aQ.Str("status");
            aTenant.TrialEndsAt = aQ.Stamp("trial_ends_at");
            aTenant.OnboardingStep = (int)aQ.Int("onboarding_step");
            aTenant.CreatedAt = aQ.Stamp("created_at");
            aTenant.PlanCode = aQ.Str("plan_code");
            aTenant.PlanName = aQ.Str("plan_name");
            aTenant.PlanPrice = aQ.Flt("plan_price");
            aTenant.UserCount = (int)aQ.Int("user_count");
            aTenant.ProjectCount = (int)aQ.Int("project_count");
        }

        public bool GetTenant(long aId, out TSaaSTenant aTenant)
        {
            aTenant = null;
            if (aId <= 0)
                return false;
            using (TSaaSQuery oQ = new TSaaSQuery(this, CS_TENANT_FIELDS + "WHERE t.id = :i"))
            {
                oQ.ParamInt("i", aId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadTenant(oQ, out aTenant);
                return true;
            }
        }

        public bool GetTenantBySlug(string aSlug, out TSaaSTenant aTenant)
        {
            aTenant = null;
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                CS_TENANT_FIELDS + "WHERE LOWER(t.slug) = LOWER(:s)"))
            {
                oQ.ParamStr("s", aSlug);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadTenant(oQ, out aTenant);
                return true;
            }
        }

        public bool SlugExists(string aSlug)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "SELECT COUNT(*) AS c FROM tenants WHERE LOWER(slug) = LOWER(:s)"))
            {
                oQ.ParamStr("s", aSlug);
                oQ.Open();
                return (!oQ.Eof) && (oQ.Int("c") > 0);
            }
        }

        public long InsertTenant(string aSlug, string aName, long aPlanId,
            string aStatus, DateTime aTrialEnds)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this, "INSERT INTO tenants (slug, name, plan_id, " +
                "status, trial_ends_at, onboarding_step, created_at) " +
                "VALUES (:s, :n, :p, :st, :t, 0, :c)"))
            {
                oQ.ParamStr("s", aSlug);
                oQ.ParamStr("n", aName);
                oQ.ParamInt("p", aPlanId);
                oQ.ParamStr("st", aStatus);
                oQ.ParamStr("t", FormatSaaSTimestamp(aTrialEnds));
                oQ.ParamStr("c", FormatSaaSTimestamp(DateTime.Now));
                oQ.Exec();
                oQ.SetSQL("SELECT last_insert_rowid() AS id");
                oQ.Open();
                return oQ.Int("id");
            }
        }

        public TSaaSTenant[] ListTenants(string aSearch, string aStatus)
        {
            bool vFilterStatus = (!string.IsNullOrEmpty(aStatus)) &&
                (!string.Equals(aStatus, "all", StringComparison.OrdinalIgnoreCase));
            bool vFilterSearch = (aSearch != null) && (aSearch.Trim().Length > 0);
            string vSQL = CS_TENANT_FIELDS + "WHERE 1 = 1 ";
            if (vFilterStatus)
                vSQL = vSQL + "AND t.status = :st ";
            if (vFilterSearch)
                vSQL = vSQL + "AND (t.name LIKE :q ESCAPE '\\' " +
                    "OR t.slug LIKE :q ESCAPE '\\') ";
            vSQL = vSQL + "ORDER BY t.name";

            List<TSaaSTenant> oList = new List<TSaaSTenant>();
            using (TSaaSQuery oQ = new TSaaSQuery(this, vSQL))
            {
                if (vFilterStatus)
                    oQ.ParamStr("st", aStatus);
                if (vFilterSearch)
                    oQ.ParamStr("q", "%" + SaaSDB.SaaSEscapeLike(aSearch.Trim()) + "%");
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSTenant vRow;
                    ReadTenant(oQ, out vRow);
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public void SetTenantStatus(long aId, string aStatus)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "UPDATE tenants SET status = :s WHERE id = :i"))
            {
                oQ.ParamStr("s", aStatus);
                oQ.ParamInt("i", aId);
                oQ.Exec();
            }
        }

        public void SetTenantPlan(long aId, long aPlanId)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "UPDATE tenants SET plan_id = :p WHERE id = :i"))
            {
                oQ.ParamInt("p", aPlanId);
                oQ.ParamInt("i", aId);
                oQ.Exec();
            }
        }

        public void SetTenantOnboardingStep(long aId, int aStep)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "UPDATE tenants SET onboarding_step = :s WHERE id = :i"))
            {
                oQ.ParamInt("s", aStep);
                oQ.ParamInt("i", aId);
                oQ.Exec();
            }
        }

        // ==================================================================== //
        //  plans / subscriptions                                               //
        // ==================================================================== //

        private const string CS_PLAN_FIELDS = "SELECT id, code, name, price_monthly, max_users, " +
            "max_projects, max_storage_mb, COALESCE(features_json, '') " +
            "AS features_json, sort_order FROM plans ";

        private static void ReadPlan(TSaaSQuery aQ, out TSaaSPlan aPlan)
        {
            aPlan = new TSaaSPlan();
            aPlan.Id = aQ.Int("id");
            aPlan.Code = aQ.Str("code");
            aPlan.Name = aQ.Str("name");
            aPlan.PriceMonthly = aQ.Flt("price_monthly");
            aPlan.MaxUsers = (int)aQ.Int("max_users");
            aPlan.MaxProjects = (int)aQ.Int("max_projects");
            aPlan.MaxStorageMB = (int)aQ.Int("max_storage_mb");
            aPlan.FeaturesJSON = aQ.Str("features_json");
            aPlan.SortOrder = (int)aQ.Int("sort_order");
        }

        public TSaaSPlan[] ListPlans()
        {
            List<TSaaSPlan> oList = new List<TSaaSPlan>();
            using (TSaaSQuery oQ = new TSaaSQuery(this, CS_PLAN_FIELDS + "ORDER BY sort_order"))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSPlan vRow;
                    ReadPlan(oQ, out vRow);
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public bool GetPlan(long aId, out TSaaSPlan aPlan)
        {
            aPlan = null;
            using (TSaaSQuery oQ = new TSaaSQuery(this, CS_PLAN_FIELDS + "WHERE id = :i"))
            {
                oQ.ParamInt("i", aId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadPlan(oQ, out aPlan);
                return true;
            }
        }

        public bool GetPlanByCode(string aCode, out TSaaSPlan aPlan)
        {
            aPlan = null;
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                CS_PLAN_FIELDS + "WHERE LOWER(code) = LOWER(:c)"))
            {
                oQ.ParamStr("c", aCode);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                ReadPlan(oQ, out aPlan);
                return true;
            }
        }

        public void UpdatePlan(TSaaSPlan aPlan)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this, "UPDATE plans SET name = :n, " +
                "price_monthly = :p, max_users = :mu, max_projects = :mp, " +
                "max_storage_mb = :ms, features_json = :f WHERE id = :i"))
            {
                oQ.ParamStr("n", aPlan.Name);
                oQ.ParamFloat("p", aPlan.PriceMonthly);
                oQ.ParamInt("mu", aPlan.MaxUsers);
                oQ.ParamInt("mp", aPlan.MaxProjects);
                oQ.ParamInt("ms", aPlan.MaxStorageMB);
                oQ.ParamStr("f", aPlan.FeaturesJSON);
                oQ.ParamInt("i", aPlan.Id);
                oQ.Exec();
            }
        }

        public bool CurrentSubscription(long aTenantId, out TSaaSSubscription aSub)
        {
            aSub = null;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT id, plan_id, COALESCE(started_at, '') AS started_at, " +
                "COALESCE(renews_at, '') AS renews_at, " +
                "COALESCE(cancelled_at, '') AS cancelled_at, status " +
                "FROM subscriptions WHERE tenant_id = :tenant_id " +
                "ORDER BY id DESC LIMIT 1", aTenantId))
            {
                oQ.Open();
                if (oQ.Eof)
                    return false;
                aSub = new TSaaSSubscription();
                aSub.Id = oQ.Int("id");
                aSub.TenantId = aTenantId;
                aSub.PlanId = oQ.Int("plan_id");
                aSub.StartedAt = oQ.Stamp("started_at");
                aSub.RenewsAt = oQ.Stamp("renews_at");
                aSub.CancelledAt = oQ.Stamp("cancelled_at");
                aSub.Status = oQ.Str("status");
                return true;
            }
        }

        public void ChangeSubscription(long aTenantId, long aPlanId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "UPDATE subscriptions SET cancelled_at = :x, status = 'replaced' " +
                "WHERE tenant_id = :tenant_id AND status <> 'replaced'", aTenantId))
            {
                oQ.ParamStr("x", FormatSaaSTimestamp(DateTime.Now));
                oQ.Exec();
            }
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT INTO subscriptions (tenant_id, plan_id, started_at, renews_at, " +
                "cancelled_at, status) VALUES (:tenant_id, :p, :s, :r, '', 'active')",
                aTenantId))
            {
                oQ.ParamInt("p", aPlanId);
                oQ.ParamStr("s", FormatSaaSTimestamp(DateTime.Now));
                oQ.ParamStr("r", FormatSaaSTimestamp(DateTime.Now.AddMonths(1)));
                oQ.Exec();
            }
            SetTenantPlan(aTenantId, aPlanId);
        }

        // ==================================================================== //
        //  invoices                                                            //
        // ==================================================================== //

        public TSaaSInvoice[] ListInvoices(long aTenantId)
        {
            List<TSaaSInvoice> oList = new List<TSaaSInvoice>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, SaaSDB.CS_SQL_INVOICES,
                aTenantId))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSInvoice vRow = new TSaaSInvoice();
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = aTenantId;
                    vRow.Number = oQ.Str("number");
                    vRow.PeriodStart = oQ.Stamp("period_start");
                    vRow.PeriodEnd = oQ.Stamp("period_end");
                    vRow.Subtotal = oQ.Flt("subtotal");
                    vRow.Tax = oQ.Flt("tax");
                    vRow.Total = oQ.Flt("total");
                    vRow.Status = oQ.Str("status");
                    vRow.IssuedAt = oQ.Stamp("issued_at");
                    vRow.PaidAt = oQ.Stamp("paid_at");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public bool GetInvoice(long aTenantId, long aId, out TSaaSInvoice aInvoice)
        {
            aInvoice = null;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT id, number, COALESCE(period_start, '') AS period_start, " +
                "COALESCE(period_end, '') AS period_end, subtotal, tax, total, " +
                "status, COALESCE(issued_at, '') AS issued_at, " +
                "COALESCE(paid_at, '') AS paid_at FROM invoices " +
                "WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamInt("i", aId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                aInvoice = new TSaaSInvoice();
                aInvoice.Id = oQ.Int("id");
                aInvoice.TenantId = aTenantId;
                aInvoice.Number = oQ.Str("number");
                aInvoice.PeriodStart = oQ.Stamp("period_start");
                aInvoice.PeriodEnd = oQ.Stamp("period_end");
                aInvoice.Subtotal = oQ.Flt("subtotal");
                aInvoice.Tax = oQ.Flt("tax");
                aInvoice.Total = oQ.Flt("total");
                aInvoice.Status = oQ.Str("status");
                aInvoice.IssuedAt = oQ.Stamp("issued_at");
                aInvoice.PaidAt = oQ.Stamp("paid_at");
                return true;
            }
        }

        public TSaaSInvoiceLine[] ListInvoiceLines(long aTenantId, long aInvoiceId)
        {
            // invoice_lines has no tenant_id of its own, so the tenant bind is
            // applied to the parent invoice through an EXISTS clause. The line
            // rows of another tenant's invoice are unreachable from here.
            List<TSaaSInvoiceLine> oList = new List<TSaaSInvoiceLine>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT l.id, l.invoice_id, l.description, l.qty, l.unit_price, " +
                "l.line_total FROM invoice_lines l " +
                "WHERE l.invoice_id = :i AND EXISTS (SELECT 1 FROM invoices v " +
                "WHERE v.id = l.invoice_id AND v.tenant_id = :tenant_id) " +
                "ORDER BY l.id", aTenantId))
            {
                oQ.ParamInt("i", aInvoiceId);
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSInvoiceLine vRow = new TSaaSInvoiceLine();
                    vRow.Id = oQ.Int("id");
                    vRow.InvoiceId = oQ.Int("invoice_id");
                    vRow.Description = oQ.Str("description");
                    vRow.Qty = oQ.Flt("qty");
                    vRow.UnitPrice = oQ.Flt("unit_price");
                    vRow.LineTotal = oQ.Flt("line_total");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public long InsertInvoice(long aTenantId, string aNumber,
            DateTime aPeriodStart, DateTime aPeriodEnd, double aSubtotal, double aTax,
            double aTotal, string aStatus, DateTime aIssuedAt, DateTime aPaidAt)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT INTO invoices (tenant_id, number, period_start, period_end, " +
                "subtotal, tax, total, status, issued_at, paid_at) " +
                "VALUES (:tenant_id, :n, :ps, :pe, :s, :x, :o, :st, :i, :p)", aTenantId))
            {
                oQ.ParamStr("n", aNumber);
                oQ.ParamStr("ps", FormatSaaSTimestamp(aPeriodStart));
                oQ.ParamStr("pe", FormatSaaSTimestamp(aPeriodEnd));
                oQ.ParamFloat("s", aSubtotal);
                oQ.ParamFloat("x", aTax);
                oQ.ParamFloat("o", aTotal);
                oQ.ParamStr("st", aStatus);
                oQ.ParamStr("i", FormatSaaSTimestamp(aIssuedAt));
                oQ.ParamStr("p", FormatSaaSTimestamp(aPaidAt));
                oQ.Exec();
                oQ.SetSQL("SELECT last_insert_rowid() AS id");
                oQ.Open();
                return oQ.Int("id");
            }
        }

        public void InsertInvoiceLine(long aInvoiceId, string aDescription,
            double aQty, double aUnitPrice)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this, "INSERT INTO invoice_lines (invoice_id, " +
                "description, qty, unit_price, line_total) VALUES (:i, :d, :q, :u, :l)"))
            {
                oQ.ParamInt("i", aInvoiceId);
                oQ.ParamStr("d", aDescription);
                oQ.ParamFloat("q", aQty);
                oQ.ParamFloat("u", aUnitPrice);
                oQ.ParamFloat("l", aQty * aUnitPrice);
                oQ.Exec();
            }
        }

        // ==================================================================== //
        //  usage                                                               //
        // ==================================================================== //

        public TSaaSMonthPoint[] UsageSeries(long aTenantId, string aMetric,
            int aMonths)
        {
            List<TSaaSMonthPoint> oList = new List<TSaaSMonthPoint>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, SaaSDB.CS_SQL_USAGE,
                aTenantId))
            {
                oQ.ParamStr("metric", aMetric);
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSMonthPoint vRow = new TSaaSMonthPoint();
                    vRow.MonthLabel = CopyLeft(oQ.Str("recorded_at"), 7);
                    vRow.Value = oQ.Flt("value");
                    vRow.Cnt = 1;
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            while (oList.Count > aMonths)
                oList.RemoveAt(0);
            return oList.ToArray();
        }

        public double LatestUsage(long aTenantId, string aMetric)
        {
            double vResult = 0;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT value FROM usage_metrics WHERE tenant_id = :tenant_id " +
                "AND metric = :m ORDER BY recorded_at DESC LIMIT 1", aTenantId))
            {
                oQ.ParamStr("m", aMetric);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = oQ.Flt("value");
            }
            return vResult;
        }

        public TSaaSPlanUsage PlanUsage(long aTenantId)
        {
            TSaaSPlanUsage vResult = new TSaaSPlanUsage();
            vResult.PlanCode = "";
            vResult.PlanName = "";
            vResult.MaxUsers = 0;
            vResult.MaxProjects = 0;
            vResult.MaxStorageMB = 0;
            vResult.Users = CountTenantUsers(aTenantId);
            vResult.Projects = CountProjects(aTenantId);
            vResult.StorageMB = LatestUsage(aTenantId, "storage_mb");
            vResult.ApiCalls = LatestUsage(aTenantId, "api_calls");
            TSaaSTenant oTenant;
            TSaaSPlan oPlan;
            if (GetTenant(aTenantId, out oTenant) && GetPlan(oTenant.PlanId, out oPlan))
            {
                vResult.PlanCode = oPlan.Code;
                vResult.PlanName = oPlan.Name;
                vResult.MaxUsers = oPlan.MaxUsers;
                vResult.MaxProjects = oPlan.MaxProjects;
                vResult.MaxStorageMB = oPlan.MaxStorageMB;
            }
            return vResult;
        }

        // Delphi Copy(S, 1, aCount) never raises when the string is shorter.
        private static string CopyLeft(string aValue, int aCount)
        {
            if (aValue == null)
                return "";
            if (aValue.Length <= aCount)
                return aValue;
            return aValue.Substring(0, aCount);
        }

        // ==================================================================== //
        //  invitations                                                         //
        // ==================================================================== //

        public TSaaSInvitation[] ListInvitations(long aTenantId)
        {
            List<TSaaSInvitation> oList = new List<TSaaSInvitation>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT i.id, i.email, i.role, i.token, " +
                "COALESCE(i.expires_at, '') AS expires_at, " +
                "COALESCE(i.accepted_at, '') AS accepted_at, " +
                "COALESCE(i.invited_by, 0) AS invited_by, " +
                "COALESCE(i.created_at, '') AS created_at, " +
                "COALESCE(u.display_name, '') AS invited_by_name " +
                "FROM invitations i LEFT JOIN users u ON u.id = i.invited_by " +
                "WHERE i.tenant_id = :tenant_id ORDER BY i.id DESC", aTenantId))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSInvitation vRow = new TSaaSInvitation();
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = aTenantId;
                    vRow.Email = oQ.Str("email");
                    vRow.Role = oQ.Str("role");
                    vRow.Token = oQ.Str("token");
                    vRow.ExpiresAt = oQ.Stamp("expires_at");
                    vRow.AcceptedAt = oQ.Stamp("accepted_at");
                    vRow.InvitedBy = oQ.Int("invited_by");
                    vRow.InvitedByName = oQ.Str("invited_by_name");
                    vRow.CreatedAt = oQ.Stamp("created_at");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public long InsertInvitation(long aTenantId, string aEmail, string aRole,
            string aToken, DateTime aExpires, long aInvitedBy)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT INTO invitations (tenant_id, email, role, token, expires_at, " +
                "accepted_at, invited_by, created_at) " +
                "VALUES (:tenant_id, :e, :r, :k, :x, '', :b, :c)", aTenantId))
            {
                oQ.ParamStr("e", aEmail);
                oQ.ParamStr("r", aRole);
                oQ.ParamStr("k", aToken);
                oQ.ParamStr("x", FormatSaaSTimestamp(aExpires));
                oQ.ParamInt("b", aInvitedBy);
                oQ.ParamStr("c", FormatSaaSTimestamp(DateTime.Now));
                oQ.Exec();
                oQ.SetSQL("SELECT last_insert_rowid() AS id");
                oQ.Open();
                return oQ.Int("id");
            }
        }

        public bool GetInvitationByToken(string aToken, out TSaaSInvitation aInvite)
        {
            // Deliberately NOT tenant scoped: the invited person has no session
            // yet, so the opaque single-use token IS the credential. The tenant
            // is then taken from the row, never from the request.
            aInvite = null;
            if ((aToken == null) || (aToken.Trim().Length == 0))
                return false;
            using (TSaaSQuery oQ = new TSaaSQuery(this, "SELECT id, tenant_id, email, role, token, " +
                "COALESCE(expires_at, '') AS expires_at, " +
                "COALESCE(accepted_at, '') AS accepted_at, " +
                "COALESCE(invited_by, 0) AS invited_by, " +
                "COALESCE(created_at, '') AS created_at " +
                "FROM invitations WHERE token = :k"))
            {
                oQ.ParamStr("k", aToken);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                aInvite = new TSaaSInvitation();
                aInvite.Id = oQ.Int("id");
                aInvite.TenantId = oQ.Int("tenant_id");
                aInvite.Email = oQ.Str("email");
                aInvite.Role = oQ.Str("role");
                aInvite.Token = oQ.Str("token");
                aInvite.ExpiresAt = oQ.Stamp("expires_at");
                aInvite.AcceptedAt = oQ.Stamp("accepted_at");
                aInvite.InvitedBy = oQ.Int("invited_by");
                aInvite.InvitedByName = "";
                aInvite.CreatedAt = oQ.Stamp("created_at");
                return true;
            }
        }

        public void AcceptInvitation(long aId)
        {
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "UPDATE invitations SET accepted_at = :a WHERE id = :i"))
            {
                oQ.ParamStr("a", FormatSaaSTimestamp(DateTime.Now));
                oQ.ParamInt("i", aId);
                oQ.Exec();
            }
        }

        public bool RevokeInvitation(long aTenantId, long aId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "DELETE FROM invitations WHERE tenant_id = :tenant_id AND id = :i",
                aTenantId))
            {
                oQ.ParamInt("i", aId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public int PendingInviteCount(long aTenantId)
        {
            int vResult = 0;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "SELECT COUNT(*) AS c FROM invitations " +
                "WHERE tenant_id = :tenant_id AND COALESCE(accepted_at, '') = ''",
                aTenantId))
            {
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        // ==================================================================== //
        //  projects                                                            //
        // ==================================================================== //

        public TSaaSProject[] ListProjects(long aTenantId)
        {
            List<TSaaSProject> oList = new List<TSaaSProject>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, SaaSDB.CS_SQL_PROJECTS,
                aTenantId))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSProject vRow = new TSaaSProject();
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = aTenantId;
                    vRow.Name = oQ.Str("name");
                    vRow.Description = oQ.Str("description");
                    vRow.Status = oQ.Str("status");
                    vRow.OwnerId = oQ.Int("owner_id");
                    vRow.OwnerName = oQ.Str("owner_name");
                    vRow.CreatedAt = oQ.Stamp("created_at");
                    vRow.TaskCount = (int)oQ.Int("task_count");
                    vRow.DoneCount = (int)oQ.Int("done_count");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public bool GetProject(long aTenantId, long aId, out TSaaSProject aProject)
        {
            aProject = null;
            if (aId <= 0)
                return false;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                SaaSDB.CS_SQL_PROJECT_BY_ID, aTenantId))
            {
                oQ.ParamInt("id", aId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                aProject = new TSaaSProject();
                aProject.OwnerName = "";
                aProject.TaskCount = 0;
                aProject.DoneCount = 0;
                aProject.Id = oQ.Int("id");
                aProject.TenantId = aTenantId;
                aProject.Name = oQ.Str("name");
                aProject.Description = oQ.Str("description");
                aProject.Status = oQ.Str("status");
                aProject.OwnerId = oQ.Int("owner_id");
                aProject.CreatedAt = oQ.Stamp("created_at");
                return true;
            }
        }

        public long InsertProject(long aTenantId, string aName, string aDescription,
            string aStatus, long aOwnerId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT INTO projects (tenant_id, name, description, status, owner_id, " +
                "created_at) VALUES (:tenant_id, :n, :d, :s, :o, :c)", aTenantId))
            {
                oQ.ParamStr("n", aName);
                oQ.ParamStr("d", aDescription);
                oQ.ParamStr("s", aStatus);
                oQ.ParamInt("o", aOwnerId);
                oQ.ParamStr("c", FormatSaaSTimestamp(DateTime.Now));
                oQ.Exec();
                oQ.SetSQL("SELECT last_insert_rowid() AS id");
                oQ.Open();
                return oQ.Int("id");
            }
        }

        public bool UpdateProject(long aTenantId, long aId, string aName,
            string aDescription, string aStatus, long aOwnerId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "UPDATE projects SET name = :n, " +
                "description = :d, status = :s, owner_id = :o " +
                "WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamStr("n", aName);
                oQ.ParamStr("d", aDescription);
                oQ.ParamStr("s", aStatus);
                oQ.ParamInt("o", aOwnerId);
                oQ.ParamInt("i", aId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public bool DeleteProject(long aTenantId, long aId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "DELETE FROM tasks WHERE tenant_id = :tenant_id AND project_id = :i",
                aTenantId))
            {
                oQ.ParamInt("i", aId);
                oQ.Exec();
            }
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "DELETE FROM projects WHERE tenant_id = :tenant_id AND id = :i",
                aTenantId))
            {
                oQ.ParamInt("i", aId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public int CountProjects(long aTenantId)
        {
            int vResult = 0;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                SaaSDB.CS_SQL_COUNT_PROJECTS, aTenantId))
            {
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        // --- isolation proof helpers (vendor-level, used ONLY by /app/isolation
        // to show the difference between "rows this tenant sees" and "rows in
        // the table"; they never feed a workspace page) --- //

        public int CountProjectsAllTenants()
        {
            return (int)ScalarInt("SELECT COUNT(*) AS c FROM projects", CS_NO_NAMES,
                CS_NO_VALUES);
        }

        public int CountTasksAllTenants()
        {
            return (int)ScalarInt("SELECT COUNT(*) AS c FROM tasks", CS_NO_NAMES,
                CS_NO_VALUES);
        }

        // The lowest project id that belongs to a DIFFERENT tenant, so the page
        // can attempt a real cross-tenant fetch instead of an invented one.
        public long FindForeignProjectId(long aTenantId)
        {
            return ScalarInt("SELECT COALESCE(MIN(id), 0) AS c FROM projects " +
                "WHERE tenant_id <> :t", new string[] { "t" },
                new long[] { aTenantId });
        }

        // Which tenant owns aProjectId. Returns False when no such row exists.
        public bool ProbeProjectOwner(long aProjectId, out long aOwnerTenantId,
            out string aOwnerTenantName)
        {
            aOwnerTenantId = 0;
            aOwnerTenantName = "";
            if (aProjectId <= 0)
                return false;
            using (TSaaSQuery oQ = new TSaaSQuery(this, "SELECT p.tenant_id, " +
                "COALESCE(t.name, '') AS tenant_name FROM projects p " +
                "LEFT JOIN tenants t ON t.id = p.tenant_id WHERE p.id = :i"))
            {
                oQ.ParamInt("i", aProjectId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                aOwnerTenantId = oQ.Int("tenant_id");
                aOwnerTenantName = oQ.Str("tenant_name");
                return true;
            }
        }

        // ==================================================================== //
        //  tasks                                                               //
        // ==================================================================== //

        public TSaaSTask[] ListTasks(long aTenantId, long aProjectId)
        {
            string vSQL;
            if (aProjectId > 0)
                vSQL = SaaSDB.CS_SQL_TASKS.Replace("WHERE t.tenant_id = :tenant_id",
                    "WHERE t.tenant_id = :tenant_id AND t.project_id = :pid");
            else
                vSQL = SaaSDB.CS_SQL_TASKS;

            List<TSaaSTask> oList = new List<TSaaSTask>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, vSQL, aTenantId))
            {
                if (aProjectId > 0)
                    oQ.ParamInt("pid", aProjectId);
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSTask vRow = new TSaaSTask();
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = aTenantId;
                    vRow.ProjectId = oQ.Int("project_id");
                    vRow.ProjectName = oQ.Str("project_name");
                    vRow.Title = oQ.Str("title");
                    vRow.Status = oQ.Str("status");
                    vRow.AssigneeId = oQ.Int("assignee_id");
                    vRow.AssigneeName = oQ.Str("assignee_name");
                    vRow.DueAt = oQ.Stamp("due_at");
                    vRow.CreatedAt = oQ.Stamp("created_at");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public bool GetTask(long aTenantId, long aId, out TSaaSTask aTask)
        {
            aTask = null;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "SELECT id, project_id, title, status, " +
                "COALESCE(assignee_id, 0) AS assignee_id, " +
                "COALESCE(due_at, '') AS due_at, " +
                "COALESCE(created_at, '') AS created_at FROM tasks " +
                "WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamInt("i", aId);
                oQ.Open();
                if (oQ.Eof)
                    return false;
                aTask = new TSaaSTask();
                aTask.ProjectName = "";
                aTask.AssigneeName = "";
                aTask.Id = oQ.Int("id");
                aTask.TenantId = aTenantId;
                aTask.ProjectId = oQ.Int("project_id");
                aTask.Title = oQ.Str("title");
                aTask.Status = oQ.Str("status");
                aTask.AssigneeId = oQ.Int("assignee_id");
                aTask.DueAt = oQ.Stamp("due_at");
                aTask.CreatedAt = oQ.Stamp("created_at");
                return true;
            }
        }

        public long InsertTask(long aTenantId, long aProjectId, string aTitle,
            string aStatus, long aAssigneeId, DateTime aDueAt)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT INTO tasks (tenant_id, project_id, title, status, assignee_id, " +
                "due_at, created_at) VALUES (:tenant_id, :p, :t, :s, :a, :d, :c)",
                aTenantId))
            {
                oQ.ParamInt("p", aProjectId);
                oQ.ParamStr("t", aTitle);
                oQ.ParamStr("s", aStatus);
                oQ.ParamInt("a", aAssigneeId);
                oQ.ParamStr("d", FormatSaaSTimestamp(aDueAt));
                oQ.ParamStr("c", FormatSaaSTimestamp(DateTime.Now));
                oQ.Exec();
                oQ.SetSQL("SELECT last_insert_rowid() AS id");
                oQ.Open();
                return oQ.Int("id");
            }
        }

        public bool UpdateTask(long aTenantId, long aId, long aProjectId,
            string aTitle, string aStatus, long aAssigneeId, DateTime aDueAt)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "UPDATE tasks SET project_id = :p, " +
                "title = :t, status = :s, assignee_id = :a, due_at = :d " +
                "WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamInt("p", aProjectId);
                oQ.ParamStr("t", aTitle);
                oQ.ParamStr("s", aStatus);
                oQ.ParamInt("a", aAssigneeId);
                oQ.ParamStr("d", FormatSaaSTimestamp(aDueAt));
                oQ.ParamInt("i", aId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public bool MoveTask(long aTenantId, long aId, string aStatus)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "UPDATE tasks SET status = :s " +
                "WHERE tenant_id = :tenant_id AND id = :i", aTenantId))
            {
                oQ.ParamStr("s", aStatus);
                oQ.ParamInt("i", aId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public int CountTasks(long aTenantId, string aStatus)
        {
            int vResult = 0;
            string vSQL = "SELECT COUNT(*) AS c FROM tasks WHERE tenant_id = :tenant_id";
            if (!string.IsNullOrEmpty(aStatus))
                vSQL = vSQL + " AND status = :s";
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, vSQL, aTenantId))
            {
                if (!string.IsNullOrEmpty(aStatus))
                    oQ.ParamStr("s", aStatus);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        // ==================================================================== //
        //  feature flags                                                       //
        // ==================================================================== //

        public TSaaSFeatureFlag[] ListFlags(long aTenantId)
        {
            List<TSaaSFeatureFlag> oList = new List<TSaaSFeatureFlag>();
            using (TSaaSQuery oQ = new TSaaSQuery(this, "SELECT id, COALESCE(tenant_id, 0) " +
                "AS tenant_id, flag, COALESCE(enabled, 0) AS enabled " +
                "FROM feature_flags WHERE tenant_id IS NULL OR tenant_id = :t " +
                "ORDER BY flag, tenant_id"))
            {
                oQ.ParamInt("t", aTenantId);
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSFeatureFlag vRow = new TSaaSFeatureFlag();
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = oQ.Int("tenant_id");
                    vRow.Flag = oQ.Str("flag");
                    vRow.Enabled = oQ.Int("enabled") != 0;
                    if (vRow.TenantId == 0)
                        vRow.Scope = "global";
                    else
                        vRow.Scope = "tenant";
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public void SetFlag(long aTenantId, string aFlag, bool aEnabled)
        {
            string vSQL;
            int vEnabled = 0;
            if (aEnabled)
                vEnabled = 1;
            if (aTenantId > 0)
                vSQL = "UPDATE feature_flags SET enabled = :e " +
                    "WHERE tenant_id = :t AND flag = :f";
            else
                vSQL = "UPDATE feature_flags SET enabled = :e " +
                    "WHERE tenant_id IS NULL AND flag = :f";
            using (TSaaSQuery oQ = new TSaaSQuery(this, vSQL))
            {
                oQ.ParamInt("e", vEnabled);
                if (aTenantId > 0)
                    oQ.ParamInt("t", aTenantId);
                oQ.ParamStr("f", aFlag);
                oQ.Exec();
                if (oQ.RowsAffected > 0)
                    return;
            }
            if (aTenantId > 0)
                vSQL = "INSERT INTO feature_flags (tenant_id, flag, enabled) " +
                    "VALUES (:t, :f, :e)";
            else
                vSQL = "INSERT INTO feature_flags (tenant_id, flag, enabled) " +
                    "VALUES (NULL, :f, :e)";
            using (TSaaSQuery oQ = new TSaaSQuery(this, vSQL))
            {
                if (aTenantId > 0)
                    oQ.ParamInt("t", aTenantId);
                oQ.ParamStr("f", aFlag);
                oQ.ParamInt("e", vEnabled);
                oQ.Exec();
            }
        }

        public bool FlagEnabled(long aTenantId, string aFlag)
        {
            bool vResult = false;
            using (TSaaSQuery oQ = new TSaaSQuery(this, "SELECT COALESCE(enabled, 0) AS enabled " +
                "FROM feature_flags WHERE flag = :f AND (tenant_id = :t " +
                "OR tenant_id IS NULL) ORDER BY tenant_id DESC LIMIT 1"))
            {
                oQ.ParamStr("f", aFlag);
                oQ.ParamInt("t", aTenantId);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = oQ.Int("enabled") != 0;
            }
            return vResult;
        }

        // ==================================================================== //
        //  notifications                                                       //
        // ==================================================================== //

        public TSaaSNotificationRow[] ListNotifications(long aTenantId, long aUserId)
        {
            List<TSaaSNotificationRow> oList = new List<TSaaSNotificationRow>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                SaaSDB.CS_SQL_NOTIFICATIONS, aTenantId))
            {
                oQ.ParamInt("user_id", aUserId);
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSNotificationRow vRow = new TSaaSNotificationRow();
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = aTenantId;
                    vRow.UserId = aUserId;
                    vRow.Title = oQ.Str("title");
                    vRow.Body = oQ.Str("body");
                    vRow.Kind = oQ.Str("kind");
                    vRow.ReadAt = oQ.Stamp("read_at");
                    vRow.CreatedAt = oQ.Stamp("created_at");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public int CountUnreadNotifications(long aTenantId, long aUserId)
        {
            int vResult = 0;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT COUNT(*) AS c FROM notifications WHERE tenant_id = :tenant_id " +
                "AND (user_id = :u OR user_id = 0) AND COALESCE(read_at, '') = ''",
                aTenantId))
            {
                oQ.ParamInt("u", aUserId);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        public bool MarkNotificationRead(long aTenantId, long aUserId, long aId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "UPDATE notifications SET read_at = :r WHERE tenant_id = :tenant_id " +
                "AND id = :i AND (user_id = :u OR user_id = 0)", aTenantId))
            {
                oQ.ParamStr("r", FormatSaaSTimestamp(DateTime.Now));
                oQ.ParamInt("i", aId);
                oQ.ParamInt("u", aUserId);
                oQ.Exec();
                return oQ.RowsAffected > 0;
            }
        }

        public void InsertNotification(long aTenantId, long aUserId, string aTitle,
            string aBody, string aKind)
        {
            if (aTenantId <= 0)
                return;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT INTO notifications (tenant_id, user_id, title, body, kind, " +
                "read_at, created_at) VALUES (:tenant_id, :u, :t, :b, :k, '', :c)",
                aTenantId))
            {
                oQ.ParamInt("u", aUserId);
                oQ.ParamStr("t", aTitle);
                oQ.ParamStr("b", aBody);
                oQ.ParamStr("k", aKind);
                oQ.ParamStr("c", FormatSaaSTimestamp(DateTime.Now));
                oQ.Exec();
            }
        }

        // ==================================================================== //
        //  audit                                                               //
        // ==================================================================== //

        // Best-effort: a logging failure never breaks the audited action.
        public void AddAudit(long aTenantId, long aUserId, string aAction,
            string aEntity, long aEntityId, string aDetail, string aIP)
        {
            try
            {
                using (TSaaSQuery oQ = new TSaaSQuery(this, "INSERT INTO audit_log (tenant_id, " +
                    "user_id, action, entity, entity_id, detail, ip, created_at) " +
                    "VALUES (:t, :u, :a, :e, :i, :d, :p, :c)"))
                {
                    oQ.ParamInt("t", aTenantId);
                    oQ.ParamInt("u", aUserId);
                    oQ.ParamStr("a", aAction);
                    oQ.ParamStr("e", aEntity);
                    oQ.ParamInt("i", aEntityId);
                    oQ.ParamStr("d", aDetail);
                    oQ.ParamStr("p", aIP);
                    oQ.ParamStr("c", FormatSaaSTimestamp(DateTime.Now));
                    oQ.Exec();
                }
            }
            catch
            {
                // Audit logging is best-effort: never break the audited action.
            }
        }

        public TSaaSAuditRow[] ListTenantAudit(long aTenantId, int aLimit, int aOffset)
        {
            List<TSaaSAuditRow> oList = new List<TSaaSAuditRow>();
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, SaaSDB.CS_SQL_AUDIT,
                aTenantId))
            {
                oQ.ParamInt("lim", aLimit);
                oQ.ParamInt("off", aOffset);
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSAuditRow vRow = new TSaaSAuditRow();
                    vRow.TenantName = "";
                    vRow.UserId = 0;
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = aTenantId;
                    vRow.CreatedAt = oQ.Stamp("created_at");
                    vRow.Action = oQ.Str("action");
                    vRow.Entity = oQ.Str("entity");
                    vRow.EntityId = oQ.Int("entity_id");
                    vRow.Detail = oQ.Str("detail");
                    vRow.IP = oQ.Str("ip");
                    vRow.UserName = oQ.Str("user_name");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public int CountTenantAudit(long aTenantId)
        {
            int vResult = 0;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT COUNT(*) AS c FROM audit_log WHERE tenant_id = :tenant_id",
                aTenantId))
            {
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        public TSaaSAuditRow[] ListVendorAudit(string aAction, int aLimit, int aOffset)
        {
            string vSQL = "SELECT a.id, COALESCE(a.tenant_id, 0) AS tenant_id, " +
                "COALESCE(a.created_at, '') AS created_at, a.action, a.entity, " +
                "COALESCE(a.entity_id, 0) AS entity_id, COALESCE(a.detail, '') " +
                "AS detail, COALESCE(a.ip, '') AS ip, " +
                "COALESCE(u.display_name, 'system') AS user_name, " +
                "COALESCE(t.name, '(platform)') AS tenant_name FROM audit_log a " +
                "LEFT JOIN users u ON u.id = a.user_id " +
                "LEFT JOIN tenants t ON t.id = a.tenant_id ";
            bool vFilterAction = (!string.IsNullOrEmpty(aAction)) &&
                (!string.Equals(aAction, "all", StringComparison.OrdinalIgnoreCase));
            if (vFilterAction)
                vSQL = vSQL + "WHERE a.action = :ac ";
            vSQL = vSQL + "ORDER BY a.id DESC LIMIT :lim OFFSET :off";

            List<TSaaSAuditRow> oList = new List<TSaaSAuditRow>();
            using (TSaaSQuery oQ = new TSaaSQuery(this, vSQL))
            {
                if (vFilterAction)
                    oQ.ParamStr("ac", aAction);
                oQ.ParamInt("lim", aLimit);
                oQ.ParamInt("off", aOffset);
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSAuditRow vRow = new TSaaSAuditRow();
                    vRow.Id = oQ.Int("id");
                    vRow.TenantId = oQ.Int("tenant_id");
                    vRow.UserId = 0;
                    vRow.CreatedAt = oQ.Stamp("created_at");
                    vRow.Action = oQ.Str("action");
                    vRow.Entity = oQ.Str("entity");
                    vRow.EntityId = oQ.Int("entity_id");
                    vRow.Detail = oQ.Str("detail");
                    vRow.IP = oQ.Str("ip");
                    vRow.UserName = oQ.Str("user_name");
                    vRow.TenantName = oQ.Str("tenant_name");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            return oList.ToArray();
        }

        public int CountVendorAudit(string aAction)
        {
            int vResult = 0;
            string vSQL = "SELECT COUNT(*) AS c FROM audit_log";
            bool vFilterAction = (!string.IsNullOrEmpty(aAction)) &&
                (!string.Equals(aAction, "all", StringComparison.OrdinalIgnoreCase));
            if (vFilterAction)
                vSQL = vSQL + " WHERE action = :ac";
            using (TSaaSQuery oQ = new TSaaSQuery(this, vSQL))
            {
                if (vFilterAction)
                    oQ.ParamStr("ac", aAction);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = (int)oQ.Int("c");
            }
            return vResult;
        }

        // ==================================================================== //
        //  tenant settings + role grants                                       //
        // ==================================================================== //

        public string GetSetting(long aTenantId, string aKey, string aDefault)
        {
            string vResult = aDefault;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this, "SELECT svalue FROM tenant_settings " +
                "WHERE tenant_id = :tenant_id AND skey = :k", aTenantId))
            {
                oQ.ParamStr("k", aKey);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = oQ.Str("svalue");
            }
            return vResult;
        }

        public void SetSetting(long aTenantId, string aKey, string aValue)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT OR REPLACE INTO tenant_settings (tenant_id, skey, svalue) " +
                "VALUES (:tenant_id, :k, :v)", aTenantId))
            {
                oQ.ParamStr("k", aKey);
                oQ.ParamStr("v", aValue);
                oQ.Exec();
            }
        }

        public bool GrantEnabled(long aTenantId, string aRole, string aPermission)
        {
            bool vResult = false;
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "SELECT COALESCE(granted, 0) AS granted FROM role_grants " +
                "WHERE tenant_id = :tenant_id AND role = :r AND permission = :p",
                aTenantId))
            {
                oQ.ParamStr("r", aRole);
                oQ.ParamStr("p", aPermission);
                oQ.Open();
                if (!oQ.Eof)
                    vResult = oQ.Int("granted") != 0;
            }
            return vResult;
        }

        public void SetGrant(long aTenantId, string aRole, string aPermission,
            bool aGranted)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "INSERT OR REPLACE INTO role_grants (tenant_id, role, permission, " +
                "granted) VALUES (:tenant_id, :r, :p, :g)", aTenantId))
            {
                oQ.ParamStr("r", aRole);
                oQ.ParamStr("p", aPermission);
                if (aGranted)
                    oQ.ParamInt("g", 1);
                else
                    oQ.ParamInt("g", 0);
                oQ.Exec();
            }
        }

        public void ClearGrants(long aTenantId)
        {
            using (TSaaSQuery oQ = TSaaSQuery.CreateScoped(this,
                "UPDATE role_grants SET granted = 0 WHERE tenant_id = :tenant_id",
                aTenantId))
            {
                oQ.Exec();
            }
        }

        // ==================================================================== //
        //  vendor KPIs                                                         //
        // ==================================================================== //

        public TSaaSPlatformKPI PlatformKPI()
        {
            TSaaSPlatformKPI vResult = new TSaaSPlatformKPI();
            vResult.Tenants = (int)ScalarInt("SELECT COUNT(*) AS c FROM tenants",
                CS_NO_NAMES, CS_NO_VALUES);
            vResult.ActiveTenants = (int)ScalarInt("SELECT COUNT(*) AS c FROM tenants " +
                "WHERE status = 'active'", CS_NO_NAMES, CS_NO_VALUES);
            vResult.TrialTenants = (int)ScalarInt("SELECT COUNT(*) AS c FROM tenants " +
                "WHERE status = 'trial'", CS_NO_NAMES, CS_NO_VALUES);
            vResult.PastDueTenants = (int)ScalarInt("SELECT COUNT(*) AS c FROM tenants " +
                "WHERE status = 'past_due'", CS_NO_NAMES, CS_NO_VALUES);
            vResult.SuspendedTenants = (int)ScalarInt("SELECT COUNT(*) AS c FROM tenants " +
                "WHERE status = 'suspended'", CS_NO_NAMES, CS_NO_VALUES);
            vResult.Users = (int)ScalarInt("SELECT COUNT(*) AS c FROM users " +
                "WHERE tenant_id IS NOT NULL", CS_NO_NAMES, CS_NO_VALUES);
            vResult.Projects = (int)ScalarInt("SELECT COUNT(*) AS c FROM projects",
                CS_NO_NAMES, CS_NO_VALUES);
            vResult.MRR = ScalarFloat("SELECT COALESCE(SUM(p.price_monthly), 0) AS c " +
                "FROM tenants t JOIN plans p ON p.id = t.plan_id " +
                "WHERE t.status IN ('active', 'past_due')", CS_NO_NAMES, CS_NO_VALUES);
            return vResult;
        }

        public TSaaSMonthPoint[] MRRSeries(int aMonths)
        {
            List<TSaaSMonthPoint> oList = new List<TSaaSMonthPoint>();
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "SELECT SUBSTR(issued_at, 1, 7) AS ym, SUM(total) AS v, " +
                "COUNT(*) AS c FROM invoices GROUP BY ym ORDER BY ym"))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSMonthPoint vRow = new TSaaSMonthPoint();
                    vRow.MonthLabel = oQ.Str("ym");
                    vRow.Value = oQ.Flt("v");
                    vRow.Cnt = (int)oQ.Int("c");
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            while (oList.Count > aMonths)
                oList.RemoveAt(0);
            return oList.ToArray();
        }

        public TSaaSMonthPoint[] SignupSeries(int aMonths)
        {
            List<TSaaSMonthPoint> oList = new List<TSaaSMonthPoint>();
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "SELECT SUBSTR(created_at, 1, 7) AS ym, COUNT(*) AS c FROM tenants " +
                "GROUP BY ym ORDER BY ym"))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSMonthPoint vRow = new TSaaSMonthPoint();
                    vRow.MonthLabel = oQ.Str("ym");
                    vRow.Cnt = (int)oQ.Int("c");
                    vRow.Value = vRow.Cnt;
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            while (oList.Count > aMonths)
                oList.RemoveAt(0);
            return oList.ToArray();
        }

        public TSaaSMonthPoint[] ChurnSeries(int aMonths)
        {
            List<TSaaSMonthPoint> oList = new List<TSaaSMonthPoint>();
            using (TSaaSQuery oQ = new TSaaSQuery(this,
                "SELECT SUBSTR(issued_at, 1, 7) AS ym, " +
                "SUM(CASE WHEN status = 'overdue' THEN 1 ELSE 0 END) AS c " +
                "FROM invoices GROUP BY ym ORDER BY ym"))
            {
                oQ.Open();
                while (!oQ.Eof)
                {
                    TSaaSMonthPoint vRow = new TSaaSMonthPoint();
                    vRow.MonthLabel = oQ.Str("ym");
                    vRow.Cnt = (int)oQ.Int("c");
                    vRow.Value = vRow.Cnt;
                    oList.Add(vRow);
                    oQ.Next();
                }
            }
            while (oList.Count > aMonths)
                oList.RemoveAt(0);
            return oList.ToArray();
        }

        public double[] TenantActivitySeries(long aTenantId, int aMonths)
        {
            TSaaSMonthPoint[] vSeries = UsageSeries(aTenantId, "api_calls", aMonths);
            double[] vResult = new double[vSeries.Length];
            for (int vI = 0; vI < vSeries.Length; vI++)
                vResult[vI] = vSeries[vI].Value;
            return vResult;
        }
    }
}
