// ***************************************************************************
//  sgcReports - reporting and BI portal web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\15.Reports\sgcReports_DB.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
//
//  The whole data layer of the demo. There is no REST tier and no ORM: a query
//  is opened here, in this process, against the database, and the resulting
//  table is handed straight to an sgcHTML component through LoadFromDataSet.
//  TReportsQuery is the small RAII wrapper that makes that read the same way on
//  every page.
//
//  FireDAC (TFDConnection / FDManager / TFDQuery) is replaced by
//  Microsoft.Data.Sqlite, and the Delphi TDataSet handed to the sgcHTML
//  components becomes the System.Data.DataTable the managed components take
//  (the migration's established TDataSet mapping). The read-only connection
//  definition becomes SqliteOpenMode.ReadOnly, so admin-authored report SQL is
//  still executed by a driver that cannot write.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Reports
{
    /// <summary>Reports demo DB error. Mirrors Delphi EReportsDBError.</summary>
    public class EReportsDBError : Exception
    {
        public EReportsDBError(string message) : base(message) { }
    }

    /// <summary>
    /// Free helpers the Delphi unit declares at unit level.
    /// </summary>
    public static class ReportsDBHelpers
    {
        // How many rows a single interactive report run is allowed to materialise.
        // Exports are not capped, the grid on screen is.
        public const int CS_REPORT_MAX_ROWS = 5000;

        /// <summary>
        /// Parse a 'yyyy-MM-ddTHH:mm:ss' timestamp as stored by this unit. Never use
        /// a locale-sensitive parse on these.
        /// </summary>
        public static DateTime ParseReportsTimestamp(string aValue)
        {
            string vText = (aValue ?? "").Trim();
            if (vText.Length == 0)
                return DateTime.MinValue;
            // A date-only value ('yyyy-MM-dd') is accepted too, with a midnight time.
            if (vText.Length == 10)
                vText = vText + "T00:00:00";
            if (vText.Length < 19)
                return DateTime.MinValue;

            int vYear = IntOf(vText, 0, 4);
            int vMonth = IntOf(vText, 5, 2);
            int vDay = IntOf(vText, 8, 2);
            int vHour = IntOf(vText, 11, 2);
            int vMin = IntOf(vText, 14, 2);
            int vSec = IntOf(vText, 17, 2);
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

        private static int IntOf(string aText, int aStart, int aLen)
        {
            if (aStart + aLen > aText.Length)
                return -1;
            int vResult;
            if (int.TryParse(aText.Substring(aStart, aLen), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return -1;
        }

        public static string FormatReportsTimestamp(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public static string FormatReportsDate(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        public static string NowTimestamp()
        {
            return FormatReportsTimestamp(DateTime.Now);
        }

        /// <summary>
        /// Escape LIKE metacharacters before wrapping a search term in '%...%'.
        /// Paired with an explicit ESCAPE '\' clause at every call site.
        /// </summary>
        public static string EscapeLikeValue(string aValue)
        {
            string vResult = (aValue ?? "").Replace("\\", "\\\\");
            vResult = vResult.Replace("%", "\\%");
            vResult = vResult.Replace("_", "\\_");
            return vResult;
        }

        /// <summary>
        /// True when aValue looks like 'yyyy-MM-dd'. Anything else is treated as
        /// "no bound" rather than being pushed into the query.
        /// </summary>
        public static bool IsISODate(string aValue)
        {
            string vValue = aValue ?? "";
            if (vValue.Length != 10)
                return false;
            for (int vI = 0; vI < 10; vI++)
            {
                if ((vI == 4) || (vI == 7))
                {
                    if (vValue[vI] != '-')
                        return false;
                }
                else if ((vValue[vI] < '0') || (vValue[vI] > '9'))
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// An open query plus the pooled connection it rides on, so a page can say
    /// <code>
    ///   using (TReportsQuery oQ = FDB.NewQuery(CS_SQL))
    ///   {
    ///       oQ.SetParamStr("region", vRegion);
    ///       oQ.Open();
    ///       oGrid.LoadFromDataSet(oQ.DataSet);
    ///   }
    /// </code>
    /// and never leak a connection. Mirrors Delphi TReportsQuery.
    /// </summary>
    public class TReportsQuery : IDisposable
    {
        private SqliteConnection FConn;
        private SqliteCommand FCommand;
        private DataTable FTable;
        private readonly string FSQLText;
        private readonly bool FReadOnly;
        private int FElapsedMS;
        private bool FDisposed;

        /// <summary>
        /// aReadOnly picks the driver-level read-only connection, used for
        /// everything that executes admin-authored report SQL.
        /// </summary>
        public TReportsQuery(TReportsDBPool aPool, string aSQL, bool aReadOnly = false)
        {
            if (aPool == null)
                throw new EReportsDBError("TReportsQuery: pool is nil");
            FSQLText = aSQL;
            FReadOnly = aReadOnly;
            FElapsedMS = 0;
            FTable = null;
            FConn = aReadOnly ? aPool.AcquireReadOnly() : aPool.Acquire();
            try
            {
                FCommand = FConn.CreateCommand();
                FCommand.CommandText = aSQL;
            }
            catch (Exception)
            {
                if (FCommand != null) { try { FCommand.Dispose(); } catch { } FCommand = null; }
                try { FConn.Dispose(); } catch { }
                FConn = null;
                throw;
            }
        }

        public void Dispose()
        {
            if (FDisposed)
                return;
            FDisposed = true;
            if (FCommand != null) { try { FCommand.Dispose(); } catch { } FCommand = null; }
            if (FConn != null) { try { FConn.Dispose(); } catch { } FConn = null; }
            // The DataTable stays alive for whoever still holds it (the components
            // read it after the connection is gone, exactly like the Delphi demo
            // reads a fetched-all TFDQuery).
        }

        /// <summary>The materialised result. Mirrors Delphi DataSet (a TFDQuery).</summary>
        public DataTable DataSet
        {
            get { return FTable; }
        }

        public int RecordCount
        {
            get { return FTable == null ? 0 : FTable.Rows.Count; }
        }

        public string SQLText
        {
            get { return FSQLText; }
        }

        /// <summary>
        /// How long the database took, in ms. This is the number the throughput
        /// pages display.
        /// </summary>
        public int ElapsedMS
        {
            get { return FElapsedMS; }
        }

        public bool ReadOnly
        {
            get { return FReadOnly; }
        }

        /// <summary>
        /// True when the SQL actually declares aName, so a caller can bind an
        /// optional parameter without knowing whether this report uses it.
        /// </summary>
        public bool HasParam(string aName)
        {
            return SQLDeclaresParam(FSQLText, aName);
        }

        public void SetParamStr(string aName, string aValue)
        {
            if (FCommand == null)
                return;
            if (!HasParam(aName))
                return;
            SetParam(aName, aValue == null ? (object)DBNull.Value : aValue);
        }

        public void SetParamInt(string aName, long aValue)
        {
            if (FCommand == null)
                return;
            if (!HasParam(aName))
                return;
            SetParam(aName, aValue);
        }

        public void SetParamFloat(string aName, double aValue)
        {
            if (FCommand == null)
                return;
            if (!HasParam(aName))
                return;
            SetParam(aName, aValue);
        }

        private void SetParam(string aName, object aValue)
        {
            int vIndex = FCommand.Parameters.IndexOf(aName);
            if (vIndex >= 0)
                FCommand.Parameters[vIndex].Value = aValue;
            else
                FCommand.Parameters.AddWithValue(aName, aValue);
        }

        /// <summary>
        /// Opens the query and records how long the database took, in ms.
        /// </summary>
        public void Open()
        {
            if (FCommand == null)
                return;
            // Anything the SQL declares but the caller never bound is NULL, exactly
            // like an unassigned TFDParam. Microsoft.Data.Sqlite refuses to execute a
            // statement with an unbound parameter, so they are filled in here.
            BindMissingParams();
            DateTime vStart = DateTime.Now;
            FTable = ReadTable(FCommand);
            FElapsedMS = (int)Math.Round((DateTime.Now - vStart).TotalMilliseconds);
        }

        private void BindMissingParams()
        {
            List<string> vNames = ParamNamesOf(FSQLText);
            for (int vI = 0; vI < vNames.Count; vI++)
                if (FCommand.Parameters.IndexOf(vNames[vI]) < 0)
                    FCommand.Parameters.AddWithValue(vNames[vI], DBNull.Value);
        }

        // ----- SQL parameter scanning ----- //

        // Collects every ':name' token of a statement, ignoring the ones inside a
        // string literal or a comment (which StripSQLLiterals blanks out).
        internal static List<string> ParamNamesOf(string aSQL)
        {
            List<string> vResult = new List<string>();
            string vText = TReportsDBPool.StripSQLLiterals(aSQL ?? "");
            int vI = 0;
            while (vI < vText.Length)
            {
                if (vText[vI] == ':')
                {
                    int vJ = vI + 1;
                    while ((vJ < vText.Length) && (char.IsLetterOrDigit(vText[vJ]) ||
                        (vText[vJ] == '_')))
                        vJ++;
                    if (vJ > vI + 1)
                    {
                        string vName = vText.Substring(vI + 1, vJ - vI - 1);
                        if (!vResult.Contains(vName))
                            vResult.Add(vName);
                    }
                    vI = vJ;
                }
                else
                    vI++;
            }
            return vResult;
        }

        internal static bool SQLDeclaresParam(string aSQL, string aName)
        {
            List<string> vNames = ParamNamesOf(aSQL);
            for (int vI = 0; vI < vNames.Count; vI++)
                if (string.Equals(vNames[vI], aName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // ----- reader -> DataTable ----- //

        // SQLite is dynamically typed, so the column CLR type is promoted from the
        // values actually returned: integers only -> long, any real -> double,
        // anything else (or nothing at all) -> string. That is what makes the
        // numeric/text split the pages rely on match what the database returned.
        internal static DataTable ReadTable(SqliteCommand aCommand)
        {
            DataTable oTable = new DataTable();
            oTable.Locale = CultureInfo.InvariantCulture;

            using (SqliteDataReader oReader = aCommand.ExecuteReader())
            {
                int vCount = oReader.FieldCount;
                string[] vNames = new string[vCount];
                bool[] vSawInt = new bool[vCount];
                bool[] vSawFloat = new bool[vCount];
                bool[] vSawOther = new bool[vCount];
                for (int vI = 0; vI < vCount; vI++)
                    vNames[vI] = oReader.GetName(vI);

                List<object[]> vRows = new List<object[]>();
                while (oReader.Read())
                {
                    object[] vRow = new object[vCount];
                    for (int vI = 0; vI < vCount; vI++)
                    {
                        if (oReader.IsDBNull(vI))
                        {
                            vRow[vI] = DBNull.Value;
                            continue;
                        }
                        object vValue = oReader.GetValue(vI);
                        vRow[vI] = vValue;
                        if ((vValue is long) || (vValue is int) || (vValue is short) ||
                            (vValue is byte) || (vValue is bool))
                            vSawInt[vI] = true;
                        else if ((vValue is double) || (vValue is float) ||
                            (vValue is decimal))
                            vSawFloat[vI] = true;
                        else
                            vSawOther[vI] = true;
                    }
                    vRows.Add(vRow);
                }

                for (int vI = 0; vI < vCount; vI++)
                {
                    Type vType;
                    if (vSawOther[vI] || (!vSawInt[vI] && !vSawFloat[vI]))
                        vType = typeof(string);
                    else if (vSawFloat[vI])
                        vType = typeof(double);
                    else
                        vType = typeof(long);
                    string vName = vNames[vI];
                    // Duplicated column captions (two expressions aliased the same)
                    // would throw; disambiguate the way a dataset would.
                    if (oTable.Columns.Contains(vName))
                        vName = vName + "_" + vI.ToString(CultureInfo.InvariantCulture);
                    oTable.Columns.Add(vName, vType);
                }

                for (int vR = 0; vR < vRows.Count; vR++)
                {
                    object[] vSource = vRows[vR];
                    object[] vTarget = new object[vCount];
                    for (int vI = 0; vI < vCount; vI++)
                    {
                        if (vSource[vI] == DBNull.Value)
                        {
                            vTarget[vI] = DBNull.Value;
                            continue;
                        }
                        Type vType = oTable.Columns[vI].DataType;
                        if (vType == typeof(long))
                            vTarget[vI] = Convert.ToInt64(vSource[vI],
                                CultureInfo.InvariantCulture);
                        else if (vType == typeof(double))
                            vTarget[vI] = Convert.ToDouble(vSource[vI],
                                CultureInfo.InvariantCulture);
                        else
                            vTarget[vI] = ValueToString(vSource[vI]);
                    }
                    oTable.Rows.Add(vTarget);
                }
            }

            oTable.AcceptChanges();
            return oTable;
        }

        private static string ValueToString(object aValue)
        {
            if (aValue is byte[])
                return Convert.ToBase64String((byte[])aValue);
            if (aValue is double)
                return ((double)aValue).ToString("G15", CultureInfo.InvariantCulture);
            if (aValue is DateTime)
                return ((DateTime)aValue).ToString("yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture);
            return Convert.ToString(aValue, CultureInfo.InvariantCulture) ?? "";
        }
    }

    /// <summary>The pooled data layer. Mirrors Delphi TReportsDBPool.</summary>
    public class TReportsDBPool : IDisposable
    {
        // Sort keys the /explore grid accepts, mapped to a qualified column. An
        // unrecognised key falls back to the primary key, so nothing a caller sends
        // can reach the ORDER BY unfiltered.
        private static readonly string[,] CS_EXPLORE_SORTS = new string[11, 2]
        {
            { "date", "o.order_date" }, { "customer", "c.name" },
            { "region", "r.name" }, { "salesperson", "s.name" },
            { "product", "p.name" }, { "sku", "p.sku" },
            { "category", "cat.name" }, { "qty", "ol.qty" },
            { "price", "ol.unit_price" }, { "discount", "ol.discount" },
            { "total", "ol.line_total" }
        };

        // Words that must never appear in an admin-authored report body. Checked as
        // whole words after the statement has already been required to start with
        // SELECT / WITH.
        private static readonly string[] CS_SQL_FORBIDDEN = new string[]
        {
            "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "REPLACE",
            "TRUNCATE", "ATTACH", "DETACH", "PRAGMA", "VACUUM", "REINDEX",
            "TRIGGER", "BEGIN", "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE"
        };

        internal const string CS_EXPLORE_SELECT =
            "SELECT ol.id AS line_id, o.order_date AS order_date, " +
            "o.status AS status, c.name AS customer, r.name AS region, " +
            "c.segment AS segment, s.name AS salesperson, p.sku AS sku, " +
            "p.name AS product, cat.name AS category, ol.qty AS qty, " +
            "ol.unit_price AS unit_price, ol.discount AS discount, " +
            "ol.line_total AS line_total";

        // orders leads the join on purpose: with idx_orders_date in place SQLite
        // can walk the date index and stop at LIMIT instead of sorting the whole
        // 27,000-row join, which is what makes the default page of /explore fast.
        internal const string CS_EXPLORE_FROM = " FROM orders o " +
            "JOIN order_lines ol ON ol.order_id = o.id " +
            "JOIN customers c ON c.id = o.customer_id " +
            "JOIN regions r ON r.id = c.region_id " +
            "JOIN salespeople s ON s.id = o.salesperson_id " +
            "JOIN products p ON p.id = ol.product_id " +
            "JOIN categories cat ON cat.id = p.category_id";

        private readonly string FDatabaseFile;
        private readonly string FConnStr;
        private readonly string FConnStrRO;
        // Deterministic pseudo-random source, so the seeded dataset (and therefore
        // every number this demo prints) is reproducible run after run.
        private uint FSeed;

        public TReportsDBPool(string aDatabaseFile)
        {
            string vFile = aDatabaseFile;
            if (string.IsNullOrEmpty(vFile))
                vFile = Path.Combine("data", "reports.db");
            if (!Path.IsPathRooted(vFile))
                vFile = Path.Combine(Directory.GetCurrentDirectory(), vFile);
            FDatabaseFile = vFile;
            FSeed = 20260822;
            EnsureDatabaseDir();

            FConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = FDatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            // The read-only twin. Report SQL authored by an admin only ever runs on
            // this connection string, so read-only is enforced by the SQLite driver
            // itself and not only by the SELECT-shape check in ValidateReportSQL.
            FConnStrRO = new SqliteConnectionStringBuilder
            {
                DataSource = FDatabaseFile,
                Mode = SqliteOpenMode.ReadOnly
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

        /// <summary>Pooled read/write connection. Caller MUST dispose it.</summary>
        public SqliteConnection Acquire()
        {
            SqliteConnection oConn = new SqliteConnection(FConnStr);
            try
            {
                oConn.Open();
                using (SqliteCommand oPragma = oConn.CreateCommand())
                {
                    oPragma.CommandText = "PRAGMA journal_mode=WAL;";
                    oPragma.ExecuteNonQuery();
                    oPragma.CommandText = "PRAGMA synchronous=NORMAL;";
                    oPragma.ExecuteNonQuery();
                }
                return oConn;
            }
            catch (Exception)
            {
                oConn.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Pooled connection the SQLite driver opened in read-only mode. Every
        /// execution of admin-authored report SQL runs on one of these, so even a
        /// statement that slipped past ValidateReportSQL cannot write.
        /// </summary>
        public SqliteConnection AcquireReadOnly()
        {
            SqliteConnection oConn = new SqliteConnection(FConnStrRO);
            try
            {
                oConn.Open();
                return oConn;
            }
            catch (Exception)
            {
                oConn.Dispose();
                throw;
            }
        }

        /// <summary>A query on a pooled connection. Caller disposes the result.</summary>
        public TReportsQuery NewQuery(string aSQL, bool aReadOnly = false)
        {
            return new TReportsQuery(this, aSQL, aReadOnly);
        }

        // ----- deterministic pseudo-random ----- //

        private int NextRandom(int aRange)
        {
            unchecked
            {
                FSeed = (FSeed * 1103515245 + 12345) & 0x7FFFFFFF;
            }
            if (aRange <= 0)
                return 0;
            return (int)((FSeed >> 8) % (uint)aRange);
        }

        private double NextRandomFloat()
        {
            return NextRandom(1000000) / 1000000.0;
        }

        // ----- low-level helpers ----- //

        private static SqliteCommand NewCmd(SqliteConnection aConn, string aSQL)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.CommandText = aSQL;
            return oCmd;
        }

        private static void ExecNonQuery(SqliteConnection aConn, string aSQL)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
                oCmd.ExecuteNonQuery();
        }

        private static long LastInsertRowId(SqliteConnection aConn)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, "SELECT last_insert_rowid()"))
            {
                object vValue = oCmd.ExecuteScalar();
                if ((vValue == null) || (vValue == DBNull.Value))
                    return 0;
                return Convert.ToInt64(vValue, CultureInfo.InvariantCulture);
            }
        }

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

        private static string RStr(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return "";
            return Convert.ToString(aReader.GetValue(vOrd),
                CultureInfo.InvariantCulture) ?? "";
        }

        private static void FillUserFromReader(SqliteDataReader aReader,
            TReportsUser aUser)
        {
            aUser.Id = RInt64(aReader, "id");
            aUser.Username = RStr(aReader, "username");
            aUser.PasswordHash = RStr(aReader, "password_hash");
            aUser.Role = RStr(aReader, "role");
            aUser.DisplayName = RStr(aReader, "display_name");
            aUser.CreatedAt = ReportsDBHelpers.ParseReportsTimestamp(
                RStr(aReader, "created_at"));
        }

        private static void FillPasskeyFromReader(SqliteDataReader aReader,
            TReportsPasskey aPk)
        {
            aPk.Id = RInt64(aReader, "id");
            aPk.UserId = RInt64(aReader, "user_id");
            aPk.CredentialId = RStr(aReader, "credential_id");
            aPk.PublicKey = RStr(aReader, "public_key");
            aPk.SignCount = RInt64(aReader, "sign_count");
            aPk.DeviceName = RStr(aReader, "device_name");
            aPk.CreatedAt = ReportsDBHelpers.ParseReportsTimestamp(
                RStr(aReader, "created_at"));
            aPk.LastUsedAt = ReportsDBHelpers.ParseReportsTimestamp(
                RStr(aReader, "last_used_at"));
        }

        private static void FillReportDefFromReader(SqliteDataReader aReader,
            TReportsReportDef aDef)
        {
            aDef.Id = RInt64(aReader, "id");
            aDef.Name = RStr(aReader, "name");
            aDef.Description = RStr(aReader, "description");
            aDef.Category = RStr(aReader, "category");
            aDef.SqlText = RStr(aReader, "sql_text");
            aDef.ParamsJSON = RStr(aReader, "params_json");
            aDef.ChartKind = RStr(aReader, "chart_kind");
            aDef.OwnerId = RInt64(aReader, "owner_id");
            aDef.Shared = RInt(aReader, "shared") != 0;
            aDef.CreatedAt = ReportsDBHelpers.ParseReportsTimestamp(
                RStr(aReader, "created_at"));
        }

        // ----- SQL text helpers ----- //

        /// <summary>
        /// Whitelist aSort against the /explore sort keys; anything else falls back
        /// to the primary key so the grid still has a stable order.
        /// </summary>
        internal static string ExploreSortSQL(string aSort)
        {
            for (int vI = 0; vI < CS_EXPLORE_SORTS.GetLength(0); vI++)
                if (string.Equals(aSort, CS_EXPLORE_SORTS[vI, 0],
                    StringComparison.OrdinalIgnoreCase))
                    return CS_EXPLORE_SORTS[vI, 1];
            return "ol.id";
        }

        internal static string DirSQL(string aDir)
        {
            if (string.Equals((aDir ?? "").Trim(), "asc",
                StringComparison.OrdinalIgnoreCase))
                return "ASC";
            return "DESC";
        }

        // True when aWord appears in aUpperSQL delimited by non-identifier chars.
        internal static bool ContainsWord(string aUpperSQL, string aWord)
        {
            int vFrom = 0;
            while (vFrom <= aUpperSQL.Length - aWord.Length)
            {
                int vPos = aUpperSQL.IndexOf(aWord, vFrom, StringComparison.Ordinal);
                if (vPos < 0)
                    return false;
                char vBefore = (vPos == 0) ? ' ' : aUpperSQL[vPos - 1];
                char vAfter = (vPos + aWord.Length >= aUpperSQL.Length)
                    ? ' ' : aUpperSQL[vPos + aWord.Length];
                if (!IsIdentChar(vBefore) && !IsIdentChar(vAfter))
                    return true;
                vFrom = vPos + 1;
            }
            return false;
        }

        private static bool IsIdentChar(char aCh)
        {
            return ((aCh >= 'A') && (aCh <= 'Z')) || ((aCh >= 'a') && (aCh <= 'z')) ||
                ((aCh >= '0') && (aCh <= '9')) || (aCh == '_');
        }

        /// <summary>
        /// Blank out quoted literals and comments so ValidateReportSQL cannot be
        /// fooled by a keyword that only appears inside a string.
        /// </summary>
        internal static string StripSQLLiterals(string aSQL)
        {
            char[] vResult = (aSQL ?? "").ToCharArray();
            int vLen = vResult.Length;
            bool vInQuote = false;
            char vQuote = '\0';
            int vI = 0;
            while (vI < vLen)
            {
                if (vInQuote)
                {
                    if (vResult[vI] == vQuote)
                        vInQuote = false;
                    vResult[vI] = ' ';
                }
                else if ((vResult[vI] == '\'') || (vResult[vI] == '"'))
                {
                    vQuote = vResult[vI];
                    vInQuote = true;
                    vResult[vI] = ' ';
                }
                else if ((vResult[vI] == '-') && (vI < vLen - 1) &&
                    (vResult[vI + 1] == '-'))
                {
                    while ((vI < vLen) && (vResult[vI] != '\n') && (vResult[vI] != '\r'))
                    {
                        vResult[vI] = ' ';
                        vI++;
                    }
                    continue;
                }
                else if ((vResult[vI] == '/') && (vI < vLen - 1) &&
                    (vResult[vI + 1] == '*'))
                {
                    while (vI < vLen)
                    {
                        if ((vResult[vI] == '*') && (vI < vLen - 1) &&
                            (vResult[vI + 1] == '/'))
                        {
                            vResult[vI] = ' ';
                            vResult[vI + 1] = ' ';
                            vI += 2;
                            break;
                        }
                        vResult[vI] = ' ';
                        vI++;
                    }
                    continue;
                }
                vI++;
            }
            return new string(vResult);
        }

        // ==================================================================== //
        //  schema + seeding                                                    //
        // ==================================================================== //

        public void EnsureSchema()
        {
            string[] vTables = new string[]
            {
                "CREATE TABLE IF NOT EXISTS users (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "username TEXT UNIQUE, " +
                "password_hash TEXT, " + "role TEXT, " + "display_name TEXT, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS passkeys (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "user_id INTEGER, " +
                "credential_id TEXT, " + "public_key TEXT, " + "sign_count INTEGER, " +
                "device_name TEXT, " + "created_at TEXT, " + "last_used_at TEXT)",

                "CREATE TABLE IF NOT EXISTS regions (" + "id INTEGER PRIMARY KEY, " +
                "name TEXT)",

                "CREATE TABLE IF NOT EXISTS salespeople (" + "id INTEGER PRIMARY KEY, " +
                "name TEXT, " + "region_id INTEGER, " + "hired_at TEXT, " +
                "target_monthly REAL)",

                "CREATE TABLE IF NOT EXISTS customers (" + "id INTEGER PRIMARY KEY, " +
                "name TEXT, " + "region_id INTEGER, " + "segment TEXT, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS categories (" + "id INTEGER PRIMARY KEY, " +
                "name TEXT)",

                "CREATE TABLE IF NOT EXISTS products (" + "id INTEGER PRIMARY KEY, " +
                "sku TEXT, " + "name TEXT, " + "category_id INTEGER, " +
                "unit_cost REAL, " + "list_price REAL)",

                "CREATE TABLE IF NOT EXISTS orders (" + "id INTEGER PRIMARY KEY, " +
                "customer_id INTEGER, " + "salesperson_id INTEGER, " +
                "order_date TEXT, " + "status TEXT, " + "ship_date TEXT, " +
                "total REAL)",

                "CREATE TABLE IF NOT EXISTS order_lines (" + "id INTEGER PRIMARY KEY, " +
                "order_id INTEGER, " + "product_id INTEGER, " + "qty INTEGER, " +
                "unit_price REAL, " + "discount REAL, " + "line_total REAL)",

                "CREATE TABLE IF NOT EXISTS report_defs (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "name TEXT, " +
                "description TEXT, " + "category TEXT, " + "sql_text TEXT, " +
                "params_json TEXT, " + "chart_kind TEXT, " + "owner_id INTEGER, " +
                "shared INTEGER, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS report_runs (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "report_id INTEGER, " +
                "user_id INTEGER, " + "params_json TEXT, " + "row_count INTEGER, " +
                "duration_ms INTEGER, " + "status TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS saved_views (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "user_id INTEGER, " +
                "report_id INTEGER, " + "name TEXT, " + "state_json TEXT, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS schedules (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "report_id INTEGER, " +
                "cron_text TEXT, " + "format TEXT, " + "recipients TEXT, " +
                "last_run_at TEXT, " + "next_run_at TEXT, " + "active INTEGER)"
            };

            using (SqliteConnection oConn = Acquire())
            {
                for (int vI = 0; vI < vTables.Length; vI++)
                    ExecNonQuery(oConn, vTables[vI]);
            }
        }

        private void CreateIndexes(SqliteConnection aConn)
        {
            string[] vIdx = new string[]
            {
                "CREATE INDEX IF NOT EXISTS idx_orders_date ON orders(order_date)",
                "CREATE INDEX IF NOT EXISTS idx_orders_cust ON orders(customer_id)",
                "CREATE INDEX IF NOT EXISTS idx_orders_sp ON orders(salesperson_id)",
                "CREATE INDEX IF NOT EXISTS idx_orders_status ON orders(status)",
                "CREATE INDEX IF NOT EXISTS idx_lines_order ON order_lines(order_id)",
                "CREATE INDEX IF NOT EXISTS idx_lines_product ON order_lines(product_id)",
                "CREATE INDEX IF NOT EXISTS idx_cust_region ON customers(region_id)",
                "CREATE INDEX IF NOT EXISTS idx_prod_cat ON products(category_id)",
                "CREATE INDEX IF NOT EXISTS idx_orders_date_id ON orders(order_date, id)",
                "CREATE INDEX IF NOT EXISTS idx_lines_order_id ON order_lines(order_id, id)",
                // One index per whitelisted /explore sort key, so no sort key forces
                // a full sort of the 27,000-row join.
                "CREATE INDEX IF NOT EXISTS idx_lines_total ON order_lines(line_total)",
                "CREATE INDEX IF NOT EXISTS idx_lines_qty ON order_lines(qty)",
                "CREATE INDEX IF NOT EXISTS idx_lines_price ON order_lines(unit_price)",
                "CREATE INDEX IF NOT EXISTS idx_lines_disc ON order_lines(discount)",
                "CREATE INDEX IF NOT EXISTS idx_cust_name ON customers(name)",
                "CREATE INDEX IF NOT EXISTS idx_prod_name ON products(name)",
                "CREATE INDEX IF NOT EXISTS idx_prod_sku ON products(sku)"
            };
            for (int vI = 0; vI < vIdx.Length; vI++)
                ExecNonQuery(aConn, vIdx[vI]);
            // Give the planner real statistics. Without them SQLite guesses at the
            // seven-table /explore join and sorts the whole set instead of walking
            // the date index, which is the difference between a fast page and a slow
            // one.
            try
            {
                ExecNonQuery(aConn, "ANALYZE");
            }
            catch (Exception)
            {
            }
        }

        public void SeedAdmin(string aUser, string aPasswordHash)
        {
            string vUser = (aUser ?? "").Trim();
            if (vUser.Length == 0)
                vUser = "admin";

            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) AS n FROM users WHERE role = :r"))
                {
                    oCmd.Parameters.AddWithValue("r", ReportsConst.CS_ROLE_ADMIN);
                    object vValue = oCmd.ExecuteScalar();
                    if ((vValue != null) && (vValue != DBNull.Value) &&
                        (Convert.ToInt64(vValue, CultureInfo.InvariantCulture) > 0))
                        return;
                }
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "INSERT INTO users (username, password_hash, role, " +
                    "display_name, created_at) VALUES (:u, :p, :r, :d, :c)"))
                {
                    oCmd.Parameters.AddWithValue("u", vUser);
                    oCmd.Parameters.AddWithValue("p", aPasswordHash ?? "");
                    oCmd.Parameters.AddWithValue("r", ReportsConst.CS_ROLE_ADMIN);
                    oCmd.Parameters.AddWithValue("d", "Report Administrator");
                    oCmd.Parameters.AddWithValue("c", ReportsDBHelpers.NowTimestamp());
                    oCmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Demo accounts (analyst / viewer), the sales dataset, the report catalogue
        /// and the schedule rows. Guarded: each part runs only when its table is
        /// still empty, so a restart never re-seeds or duplicates.
        /// </summary>
        public void SeedDemoData()
        {
            using (SqliteConnection oConn = Acquire())
            {
                AddUser(oConn, "analyst", "Ana Lyst", ReportsConst.CS_ROLE_ANALYST,
                    "demo1234");
                AddUser(oConn, "viewer", "Vic Viewer", ReportsConst.CS_ROLE_VIEWER,
                    "demo1234");

                if (TableIsEmpty(oConn, "orders"))
                    SeedSalesData(oConn);
                CreateIndexes(oConn);

                if (TableIsEmpty(oConn, "report_defs"))
                    SeedReportDefs(oConn);
                if (TableIsEmpty(oConn, "schedules"))
                    SeedSchedules(oConn);
            }
        }

        private static bool TableIsEmpty(SqliteConnection aConn, string aTable)
        {
            using (SqliteCommand oCmd = NewCmd(aConn,
                "SELECT COUNT(*) AS n FROM " + aTable))
            {
                object vValue = oCmd.ExecuteScalar();
                if ((vValue == null) || (vValue == DBNull.Value))
                    return true;
                return Convert.ToInt64(vValue, CultureInfo.InvariantCulture) == 0;
            }
        }

        private static void AddUser(SqliteConnection aConn, string aName,
            string aDisplay, string aRole, string aPlain)
        {
            using (SqliteCommand oCmd = NewCmd(aConn,
                "SELECT COUNT(*) AS n FROM users WHERE username = :u"))
            {
                oCmd.Parameters.AddWithValue("u", aName);
                object vValue = oCmd.ExecuteScalar();
                if ((vValue != null) && (vValue != DBNull.Value) &&
                    (Convert.ToInt64(vValue, CultureInfo.InvariantCulture) > 0))
                    return;
            }
            using (SqliteCommand oCmd = NewCmd(aConn,
                "INSERT INTO users (username, password_hash, role, " +
                "display_name, created_at) VALUES (:u, :p, :r, :d, :c)"))
            {
                oCmd.Parameters.AddWithValue("u", aName);
                oCmd.Parameters.AddWithValue("p", Bcrypt.BcryptHash(aPlain));
                oCmd.Parameters.AddWithValue("r", aRole);
                oCmd.Parameters.AddWithValue("d", aDisplay);
                oCmd.Parameters.AddWithValue("c", ReportsDBHelpers.NowTimestamp());
                oCmd.ExecuteNonQuery();
            }
        }

        // ~25,000 order lines across ~6,000 orders spanning three years, generated
        // in ONE transaction so a first start stays quick. The volume is the point:
        // this is also the answer to "the grid is slow with 1000 records".
        private void SeedSalesData(SqliteConnection aConn)
        {
            string[] CS_REGIONS = new string[]
                { "North", "South", "East", "West", "Central" };
            string[] CS_CATEGORIES = new string[]
            {
                "Servers", "Networking", "Storage", "Workstations", "Peripherals",
                "Software Licences", "Cabling", "Power", "Displays", "Support Plans"
            };
            string[] CS_SEGMENTS = new string[] { "SMB", "MidMarket", "Enterprise" };
            string[] CS_STATUS = new string[]
                { "Delivered", "Shipped", "Open", "Cancelled" };
            string[] CS_FIRST = new string[]
            {
                "Marta", "Tom", "Aiko", "Paul", "Sergio", "Ines", "Lars", "Nadia",
                "Diego", "Klara", "Owen", "Priya", "Hugo", "Elena", "Mateo", "Sofia",
                "Ivan", "Ruth"
            };
            string[] CS_LAST = new string[]
            {
                "Ruiz", "Becker", "Tanaka", "Dupont", "Garcia", "Moreau", "Nilsson",
                "Haddad", "Alvarez", "Novak", "Whelan", "Sharma", "Baumann", "Kovac",
                "Ferrer", "Lindqvist", "Petrov", "Okafor"
            };
            string[] CS_CO_A = new string[]
            {
                "Northwind", "Blue Harbour", "Vertex", "Ironbridge", "Lakeside",
                "Silverline", "Copperfield", "Granite", "Meridian", "Kestrel",
                "Aurora", "Foxglove", "Harborview", "Redwood", "Stonegate",
                "Brightwater"
            };
            string[] CS_CO_B = new string[]
            {
                "Systems", "Logistics", "Industries", "Solutions", "Trading", "Group",
                "Partners", "Technologies", "Supplies", "Services", "Holdings", "Labs"
            };
            string[] CS_PROD_A = new string[]
            {
                "Rack", "Edge", "Core", "Fusion", "Titan", "Nimbus", "Atlas", "Vector",
                "Quantum", "Helix", "Orion", "Pulsar"
            };
            string[] CS_PROD_B = new string[]
                { "1U", "2U", "Pro", "Max", "Lite", "Plus", "XR", "S2", "M4", "HD" };

            const int CS_ORDER_COUNT = 6000;
            const int CS_CUSTOMER_COUNT = 400;
            const int CS_PRODUCT_COUNT = 200;
            const int CS_SALESPERSON_COUNT = 18;
            const int CS_YEARS_BACK = 3;

            DateTime vToday = DateTime.Today;
            int vSpanDays = CS_YEARS_BACK * 365;
            int vLineId = 0;
            double[] vListPrice = new double[CS_PRODUCT_COUNT + 1];
            int[] vLineQty = new int[8];
            int[] vLineProd = new int[8];
            double[] vLinePrice = new double[8];
            double[] vLineDisc = new double[8];
            double[] vLineAmt = new double[8];

            using (SqliteTransaction oTx = aConn.BeginTransaction())
            {
                try
                {
                    // The line INSERT lives on its own command so the order loop never
                    // has to reassign CommandText, which would reprepare it 27,000
                    // times.
                    using (SqliteCommand oLines = NewCmd(aConn,
                        "INSERT INTO order_lines (id, order_id, product_id, qty, " +
                        "unit_price, discount, line_total) VALUES (:i, :o, :p, :q, " +
                        ":u, :d, :t)"))
                    {
                        oLines.Transaction = oTx;
                        SqliteParameter vLi = oLines.Parameters.Add("i", SqliteType.Integer);
                        SqliteParameter vLo = oLines.Parameters.Add("o", SqliteType.Integer);
                        SqliteParameter vLp = oLines.Parameters.Add("p", SqliteType.Integer);
                        SqliteParameter vLq = oLines.Parameters.Add("q", SqliteType.Integer);
                        SqliteParameter vLu = oLines.Parameters.Add("u", SqliteType.Real);
                        SqliteParameter vLd = oLines.Parameters.Add("d", SqliteType.Real);
                        SqliteParameter vLt = oLines.Parameters.Add("t", SqliteType.Real);

                        // ----- regions ----- //
                        using (SqliteCommand oIns = NewCmd(aConn,
                            "INSERT INTO regions (id, name) VALUES (:i, :n)"))
                        {
                            oIns.Transaction = oTx;
                            for (int vI = 0; vI < CS_REGIONS.Length; vI++)
                            {
                                oIns.Parameters.Clear();
                                oIns.Parameters.AddWithValue("i", vI + 1);
                                oIns.Parameters.AddWithValue("n", CS_REGIONS[vI]);
                                oIns.ExecuteNonQuery();
                            }
                        }

                        // ----- categories ----- //
                        using (SqliteCommand oIns = NewCmd(aConn,
                            "INSERT INTO categories (id, name) VALUES (:i, :n)"))
                        {
                            oIns.Transaction = oTx;
                            for (int vI = 0; vI < CS_CATEGORIES.Length; vI++)
                            {
                                oIns.Parameters.Clear();
                                oIns.Parameters.AddWithValue("i", vI + 1);
                                oIns.Parameters.AddWithValue("n", CS_CATEGORIES[vI]);
                                oIns.ExecuteNonQuery();
                            }
                        }

                        // ----- salespeople ----- //
                        using (SqliteCommand oIns = NewCmd(aConn,
                            "INSERT INTO salespeople (id, name, region_id, hired_at, " +
                            "target_monthly) VALUES (:i, :n, :r, :h, :t)"))
                        {
                            oIns.Transaction = oTx;
                            for (int vI = 0; vI < CS_SALESPERSON_COUNT; vI++)
                            {
                                oIns.Parameters.Clear();
                                oIns.Parameters.AddWithValue("i", vI + 1);
                                oIns.Parameters.AddWithValue("n",
                                    CS_FIRST[vI] + " " + CS_LAST[vI]);
                                oIns.Parameters.AddWithValue("r",
                                    1 + (vI % CS_REGIONS.Length));
                                oIns.Parameters.AddWithValue("h",
                                    ReportsDBHelpers.FormatReportsDate(
                                        vToday.AddDays(-(400 + NextRandom(2200)))));
                                oIns.Parameters.AddWithValue("t",
                                    (double)(40000 + NextRandom(60) * 1000));
                                oIns.ExecuteNonQuery();
                            }
                        }

                        // ----- customers ----- //
                        using (SqliteCommand oIns = NewCmd(aConn,
                            "INSERT INTO customers (id, name, region_id, segment, " +
                            "created_at) VALUES (:i, :n, :r, :s, :c)"))
                        {
                            oIns.Transaction = oTx;
                            for (int vI = 0; vI < CS_CUSTOMER_COUNT; vI++)
                            {
                                oIns.Parameters.Clear();
                                oIns.Parameters.AddWithValue("i", vI + 1);
                                oIns.Parameters.AddWithValue("n",
                                    CS_CO_A[NextRandom(CS_CO_A.Length)] + " " +
                                    CS_CO_B[NextRandom(CS_CO_B.Length)] + " " +
                                    (100 + (vI % 900)).ToString(
                                        CultureInfo.InvariantCulture));
                                oIns.Parameters.AddWithValue("r",
                                    1 + NextRandom(CS_REGIONS.Length));
                                oIns.Parameters.AddWithValue("s",
                                    CS_SEGMENTS[NextRandom(CS_SEGMENTS.Length)]);
                                oIns.Parameters.AddWithValue("c",
                                    ReportsDBHelpers.FormatReportsTimestamp(
                                        vToday.AddDays(-(30 + NextRandom(1800)))));
                                oIns.ExecuteNonQuery();
                            }
                        }

                        // ----- products ----- //
                        using (SqliteCommand oIns = NewCmd(aConn,
                            "INSERT INTO products (id, sku, name, category_id, " +
                            "unit_cost, list_price) VALUES (:i, :k, :n, :c, :u, :l)"))
                        {
                            oIns.Transaction = oTx;
                            for (int vI = 0; vI < CS_PRODUCT_COUNT; vI++)
                            {
                                // Money is rounded to two decimals at the source, so
                                // nothing downstream has to print a fifteen-digit float.
                                double vCost = Math.Round(
                                    (25 + NextRandom(2400) + NextRandomFloat()) * 100) / 100;
                                vListPrice[vI + 1] = Math.Round(
                                    vCost * (1.25 + NextRandom(45) / 100.0) * 100) / 100;
                                oIns.Parameters.Clear();
                                oIns.Parameters.AddWithValue("i", vI + 1);
                                oIns.Parameters.AddWithValue("k", string.Format(
                                    CultureInfo.InvariantCulture, "PRD-{0:0000}", vI + 1));
                                oIns.Parameters.AddWithValue("n",
                                    CS_PROD_A[NextRandom(CS_PROD_A.Length)] + " " +
                                    CS_PROD_B[NextRandom(CS_PROD_B.Length)] + " " +
                                    (1000 + vI).ToString(CultureInfo.InvariantCulture));
                                oIns.Parameters.AddWithValue("c",
                                    1 + (vI % CS_CATEGORIES.Length));
                                oIns.Parameters.AddWithValue("u", vCost);
                                oIns.Parameters.AddWithValue("l", vListPrice[vI + 1]);
                                oIns.ExecuteNonQuery();
                            }
                        }

                        // ----- orders + order lines ----- //
                        // Two prepared statements reused for every row; the whole loop
                        // runs inside the single transaction opened above.
                        using (SqliteCommand oIns = NewCmd(aConn,
                            "INSERT INTO orders (id, customer_id, salesperson_id, " +
                            "order_date, status, ship_date, total) VALUES (:i, :c, " +
                            ":s, :d, :t, :p, :o)"))
                        {
                            oIns.Transaction = oTx;
                            SqliteParameter vOi = oIns.Parameters.Add("i", SqliteType.Integer);
                            SqliteParameter vOc = oIns.Parameters.Add("c", SqliteType.Integer);
                            SqliteParameter vOs = oIns.Parameters.Add("s", SqliteType.Integer);
                            SqliteParameter vOd = oIns.Parameters.Add("d", SqliteType.Text);
                            SqliteParameter vOt = oIns.Parameters.Add("t", SqliteType.Text);
                            SqliteParameter vOp = oIns.Parameters.Add("p", SqliteType.Text);
                            SqliteParameter vOo = oIns.Parameters.Add("o", SqliteType.Real);

                            for (int vI = 1; vI <= CS_ORDER_COUNT; vI++)
                            {
                                // The last 60 orders are pinned into the past week
                                // (today and yesterday included) so every dashboard
                                // bucket has something to show.
                                DateTime vOrderDate;
                                if (vI > CS_ORDER_COUNT - 60)
                                    vOrderDate = vToday.AddDays(-NextRandom(7));
                                else
                                    vOrderDate = vToday.AddDays(
                                        -(7 + NextRandom(vSpanDays - 7)));

                                int vLineCount = 3 + NextRandom(4); // 3..6 lines
                                double vTotal = 0;
                                for (int vJ = 0; vJ < vLineCount; vJ++)
                                {
                                    int vProductId = 1 + NextRandom(CS_PRODUCT_COUNT);
                                    int vQty = 1 + NextRandom(20);
                                    double vDiscount;
                                    int vRoll = NextRandom(10);
                                    if (vRoll <= 4)
                                        vDiscount = 0;
                                    else if (vRoll <= 7)
                                        vDiscount = 0.05;
                                    else if (vRoll == 8)
                                        vDiscount = 0.1;
                                    else
                                        vDiscount = 0.15;
                                    double vUnitPrice = vListPrice[vProductId];
                                    double vLineTotal = Math.Round(
                                        vQty * vUnitPrice * (1 - vDiscount) * 100) / 100;
                                    vLineProd[vJ] = vProductId;
                                    vLineQty[vJ] = vQty;
                                    vLinePrice[vJ] = vUnitPrice;
                                    vLineDisc[vJ] = vDiscount;
                                    vLineAmt[vJ] = vLineTotal;
                                    vTotal = vTotal + vLineTotal;
                                }

                                string vStatus;
                                int vStatusRoll = NextRandom(20);
                                if (vStatusRoll == 0)
                                    vStatus = CS_STATUS[3]; // Cancelled
                                else if (vStatusRoll <= 3)
                                    vStatus = CS_STATUS[2]; // Open
                                else if (vStatusRoll <= 7)
                                    vStatus = CS_STATUS[1]; // Shipped
                                else
                                    vStatus = CS_STATUS[0]; // Delivered
                                // A recent order has usually not shipped yet.
                                if (vOrderDate > vToday.AddDays(-5))
                                    vStatus = CS_STATUS[2];

                                string vShipDate;
                                if ((vStatus == CS_STATUS[0]) || (vStatus == CS_STATUS[1]))
                                    vShipDate = ReportsDBHelpers.FormatReportsDate(
                                        vOrderDate.AddDays(1 + NextRandom(12)));
                                else
                                    vShipDate = "";

                                vOi.Value = vI;
                                vOc.Value = 1 + NextRandom(CS_CUSTOMER_COUNT);
                                vOs.Value = 1 + NextRandom(CS_SALESPERSON_COUNT);
                                vOd.Value = ReportsDBHelpers.FormatReportsDate(vOrderDate);
                                vOt.Value = vStatus;
                                vOp.Value = vShipDate;
                                vOo.Value = Math.Round(vTotal * 100) / 100;
                                oIns.ExecuteNonQuery();

                                for (int vJ = 0; vJ < vLineCount; vJ++)
                                {
                                    vLineId++;
                                    vLi.Value = vLineId;
                                    vLo.Value = vI;
                                    vLp.Value = vLineProd[vJ];
                                    vLq.Value = vLineQty[vJ];
                                    vLu.Value = vLinePrice[vJ];
                                    vLd.Value = vLineDisc[vJ];
                                    vLt.Value = vLineAmt[vJ];
                                    oLines.ExecuteNonQuery();
                                }
                            }
                        }
                    }

                    oTx.Commit();
                }
                catch (Exception E)
                {
                    try { oTx.Rollback(); } catch { }
                    throw new EReportsDBError(string.Format(
                        "Seeding the sales dataset failed: {0}", E.Message));
                }
            }
        }

        private void SeedReportDefs(SqliteConnection aConn)
        {
            const string CS_P_DATES = "{\"params\":[" +
                "{\"name\":\"dfrom\",\"caption\":\"From date\",\"kind\":\"date\",\"default\":\"-365\"}," +
                "{\"name\":\"dto\",\"caption\":\"To date\",\"kind\":\"date\",\"default\":\"0\"}]}";
            const string CS_P_DATES_LIMIT = "{\"params\":[" +
                "{\"name\":\"dfrom\",\"caption\":\"From date\",\"kind\":\"date\",\"default\":\"-365\"}," +
                "{\"name\":\"dto\",\"caption\":\"To date\",\"kind\":\"date\",\"default\":\"0\"}," +
                "{\"name\":\"maxrows\",\"caption\":\"Max rows\",\"kind\":\"int\",\"default\":\"25\"," +
                "\"min\":\"5\",\"max\":\"500\"}]}";
            const string CS_P_DATES_CAT = "{\"params\":[" +
                "{\"name\":\"dfrom\",\"caption\":\"From date\",\"kind\":\"date\",\"default\":\"-365\"}," +
                "{\"name\":\"dto\",\"caption\":\"To date\",\"kind\":\"date\",\"default\":\"0\"}," +
                "{\"name\":\"category\",\"caption\":\"Category\",\"kind\":\"list\"," +
                "\"source\":\"categories\",\"default\":\"\"}," +
                "{\"name\":\"maxrows\",\"caption\":\"Max rows\",\"kind\":\"int\",\"default\":\"25\"," +
                "\"min\":\"5\",\"max\":\"500\"}]}";
            const string CS_P_DATES_REGION = "{\"params\":[" +
                "{\"name\":\"dfrom\",\"caption\":\"From date\",\"kind\":\"date\",\"default\":\"-365\"}," +
                "{\"name\":\"dto\",\"caption\":\"To date\",\"kind\":\"date\",\"default\":\"0\"}," +
                "{\"name\":\"region\",\"caption\":\"Region\",\"kind\":\"list\",\"source\":\"regions\"," +
                "\"default\":\"\"}]}";
            const string CS_P_DATES_DISC = "{\"params\":[" +
                "{\"name\":\"dfrom\",\"caption\":\"From date\",\"kind\":\"date\",\"default\":\"-365\"}," +
                "{\"name\":\"dto\",\"caption\":\"To date\",\"kind\":\"date\",\"default\":\"0\"}," +
                "{\"name\":\"mindisc\",\"caption\":\"Minimum discount %\",\"kind\":\"range\"," +
                "\"default\":\"0\",\"min\":\"0\",\"max\":\"15\"}]}";
            const string CS_P_DATES_SEG = "{\"params\":[" +
                "{\"name\":\"dfrom\",\"caption\":\"From date\",\"kind\":\"date\",\"default\":\"-730\"}," +
                "{\"name\":\"dto\",\"caption\":\"To date\",\"kind\":\"date\",\"default\":\"0\"}," +
                "{\"name\":\"segment\",\"caption\":\"Segment\",\"kind\":\"list\"," +
                "\"source\":\"segments\",\"default\":\"\"}]}";

            string[,] vDefs = new string[12, 6]
            {
                {
                    "Monthly revenue",
                    "Net revenue and order count per calendar month.",
                    "Sales",
                    "SELECT substr(o.order_date, 1, 7) AS month, " +
                    "COUNT(DISTINCT o.id) AS orders, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue " + "FROM orders o " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "GROUP BY substr(o.order_date, 1, 7) ORDER BY month",
                    CS_P_DATES, "bar"
                },
                {
                    "Revenue by region",
                    "Where the money came from, by customer region.",
                    "Sales",
                    "SELECT r.name AS region, " + "COUNT(DISTINCT o.id) AS orders, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue " + "FROM orders o " +
                    "JOIN customers c ON c.id = o.customer_id " +
                    "JOIN regions r ON r.id = c.region_id " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "GROUP BY r.name ORDER BY revenue DESC",
                    CS_P_DATES, "pie"
                },
                {
                    "Top customers",
                    "Highest spending accounts in the window.",
                    "Customers",
                    "SELECT c.name AS customer, r.name AS region, " +
                    "c.segment AS segment, COUNT(DISTINCT o.id) AS orders, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue " + "FROM orders o " +
                    "JOIN customers c ON c.id = o.customer_id " +
                    "JOIN regions r ON r.id = c.region_id " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "GROUP BY c.id, c.name, r.name, c.segment " +
                    "ORDER BY revenue DESC LIMIT :maxrows",
                    CS_P_DATES_LIMIT, "bar"
                },
                {
                    "Top products by revenue",
                    "Best sellers, optionally narrowed to one category.",
                    "Sales",
                    "SELECT p.sku AS sku, p.name AS product, " +
                    "cat.name AS category, SUM(ol.qty) AS units, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue " + "FROM order_lines ol " +
                    "JOIN orders o ON o.id = ol.order_id " +
                    "JOIN products p ON p.id = ol.product_id " +
                    "JOIN categories cat ON cat.id = p.category_id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "AND (:category = '' OR cat.id = :category) " +
                    "GROUP BY p.id, p.sku, p.name, cat.name " +
                    "ORDER BY revenue DESC LIMIT :maxrows",
                    CS_P_DATES_CAT, "bar"
                },
                {
                    "Salesperson performance",
                    "Revenue per rep against the monthly target.",
                    "Sales",
                    "SELECT s.name AS salesperson, r.name AS region, " +
                    "COUNT(DISTINCT o.id) AS orders, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                    "ROUND(s.target_monthly, 2) AS monthly_target " + "FROM orders o " +
                    "JOIN salespeople s ON s.id = o.salesperson_id " +
                    "JOIN regions r ON r.id = s.region_id " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "AND (:region = '' OR r.id = :region) " +
                    "GROUP BY s.id, s.name, r.name, s.target_monthly " +
                    "ORDER BY revenue DESC",
                    CS_P_DATES_REGION, "bar"
                },
                {
                    "Margin by category",
                    "Revenue, cost and gross margin per product category.",
                    "Margin",
                    "SELECT cat.name AS category, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                    "ROUND(SUM(ol.qty * p.unit_cost), 2) AS cost, " +
                    "ROUND(SUM(ol.line_total) - SUM(ol.qty * p.unit_cost), 2) AS margin, " +
                    "ROUND(100.0 * (SUM(ol.line_total) - SUM(ol.qty * p.unit_cost)) / " +
                    "SUM(ol.line_total), 1) AS margin_pct " + "FROM order_lines ol " +
                    "JOIN orders o ON o.id = ol.order_id " +
                    "JOIN products p ON p.id = ol.product_id " +
                    "JOIN categories cat ON cat.id = p.category_id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "GROUP BY cat.name ORDER BY margin DESC",
                    CS_P_DATES, "bar"
                },
                {
                    "Discount analysis",
                    "How much revenue was given away, grouped by discount band.",
                    "Margin",
                    "SELECT CAST(ROUND(ol.discount * 100) AS INTEGER) AS " +
                    "discount_pct, COUNT(*) AS lines, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                    "ROUND(SUM(ol.qty * ol.unit_price * ol.discount), 2) AS given_away " +
                    "FROM order_lines ol JOIN orders o ON o.id = ol.order_id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "AND ol.discount * 100 >= :mindisc " +
                    "GROUP BY discount_pct ORDER BY discount_pct",
                    CS_P_DATES_DISC, "bar"
                },
                {
                    "Order status mix",
                    "Open, shipped, delivered and cancelled order counts.",
                    "Operations",
                    "SELECT o.status AS status, COUNT(*) AS orders, " +
                    "ROUND(SUM(o.total), 2) AS value " + "FROM orders o " +
                    "WHERE o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "GROUP BY o.status ORDER BY orders DESC",
                    CS_P_DATES, "doughnut"
                },
                {
                    "Fulfilment lead time",
                    "Average days from order to shipment, per month.",
                    "Operations",
                    "SELECT substr(o.order_date, 1, 7) AS month, " +
                    "COUNT(*) AS shipped_orders, " +
                    "ROUND(AVG(julianday(o.ship_date) - julianday(o.order_date)), 2) AS " +
                    "avg_days " + "FROM orders o " + "WHERE o.ship_date <> '' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "GROUP BY substr(o.order_date, 1, 7) ORDER BY month",
                    CS_P_DATES, "line"
                },
                {
                    "Customer segment breakdown",
                    "Revenue and average order value per segment.",
                    "Customers",
                    "SELECT c.segment AS segment, " +
                    "COUNT(DISTINCT c.id) AS customers, " +
                    "COUNT(DISTINCT o.id) AS orders, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                    "ROUND(SUM(ol.line_total) / COUNT(DISTINCT o.id), 2) AS avg_order " +
                    "FROM orders o JOIN customers c ON c.id = o.customer_id " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.status <> 'Cancelled' " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "AND (:segment = '' OR c.segment = :segment) " +
                    "GROUP BY c.segment ORDER BY revenue DESC",
                    CS_P_DATES_SEG, "pie"
                },
                {
                    "Order line detail",
                    "Line-level extract. Deliberately large, so the PDF has real pages.",
                    "Operations",
                    "SELECT o.order_date AS order_date, o.status AS status, " +
                    "c.name AS customer, p.sku AS sku, p.name AS product, ol.qty AS qty, " +
                    "ROUND(ol.unit_price, 2) AS unit_price, " +
                    "ROUND(ol.discount * 100) AS disc_pct, " +
                    "ROUND(ol.line_total, 2) AS line_total " + "FROM order_lines ol " +
                    "JOIN orders o ON o.id = ol.order_id " +
                    "JOIN customers c ON c.id = o.customer_id " +
                    "JOIN products p ON p.id = ol.product_id " +
                    "WHERE o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "ORDER BY o.order_date DESC, ol.id DESC LIMIT :maxrows",
                    CS_P_DATES_LIMIT, ""
                },
                {
                    "Slow movers",
                    "Products with the fewest units sold in the window.",
                    "Operations",
                    "SELECT p.sku AS sku, p.name AS product, " + "cat.name AS category, " +
                    "COALESCE(SUM(ol.qty), 0) AS units, " +
                    "ROUND(COALESCE(SUM(ol.line_total), 0), 2) AS revenue " +
                    "FROM products p " + "JOIN categories cat ON cat.id = p.category_id " +
                    "LEFT JOIN order_lines ol ON ol.product_id = p.id " +
                    "LEFT JOIN orders o ON o.id = ol.order_id " +
                    "AND o.order_date >= :dfrom AND o.order_date <= :dto " +
                    "GROUP BY p.id, p.sku, p.name, cat.name " +
                    "ORDER BY units ASC, revenue ASC LIMIT :maxrows",
                    CS_P_DATES_LIMIT, "bar"
                }
            };

            using (SqliteCommand oIns = NewCmd(aConn,
                "INSERT INTO report_defs (name, description, category, sql_text, " +
                "params_json, chart_kind, owner_id, shared, created_at) " +
                "VALUES (:n, :d, :c, :s, :p, :k, :o, :h, :t)"))
            {
                for (int vI = 0; vI < vDefs.GetLength(0); vI++)
                {
                    oIns.Parameters.Clear();
                    oIns.Parameters.AddWithValue("n", vDefs[vI, 0]);
                    oIns.Parameters.AddWithValue("d", vDefs[vI, 1]);
                    oIns.Parameters.AddWithValue("c", vDefs[vI, 2]);
                    oIns.Parameters.AddWithValue("s", vDefs[vI, 3]);
                    oIns.Parameters.AddWithValue("p", vDefs[vI, 4]);
                    oIns.Parameters.AddWithValue("k", vDefs[vI, 5]);
                    oIns.Parameters.AddWithValue("o", 1);
                    oIns.Parameters.AddWithValue("h", 1);
                    oIns.Parameters.AddWithValue("t", ReportsDBHelpers.NowTimestamp());
                    oIns.ExecuteNonQuery();
                }
            }
        }

        private void SeedSchedules(SqliteConnection aConn)
        {
            int[] vReportId = new int[] { 1, 5, 11, 7 };
            string[] vCron = new string[]
                { "0 7 1 * *", "0 6 * * 1", "30 5 * * *", "0 8 * * 5" };
            string[] vFmt = new string[] { "pdf", "xlsx", "csv", "pdf" };
            string[] vRcpt = new string[]
            {
                "board@example.com", "sales-leads@example.com",
                "ops@example.com,warehouse@example.com", "finance@example.com"
            };
            int[] vActive = new int[] { 1, 1, 1, 0 };

            using (SqliteCommand oIns = NewCmd(aConn,
                "INSERT INTO schedules (report_id, cron_text, format, recipients, " +
                "last_run_at, next_run_at, active) VALUES (:r, :c, :f, :p, :l, :n, :a)"))
            {
                for (int vI = 0; vI < vReportId.Length; vI++)
                {
                    oIns.Parameters.Clear();
                    oIns.Parameters.AddWithValue("r", vReportId[vI]);
                    oIns.Parameters.AddWithValue("c", vCron[vI]);
                    oIns.Parameters.AddWithValue("f", vFmt[vI]);
                    oIns.Parameters.AddWithValue("p", vRcpt[vI]);
                    oIns.Parameters.AddWithValue("l",
                        ReportsDBHelpers.FormatReportsTimestamp(
                            DateTime.Now.AddDays(-(1 + vI * 2))));
                    oIns.Parameters.AddWithValue("n",
                        ReportsDBHelpers.FormatReportsTimestamp(
                            DateTime.Now.AddDays(1 + vI)));
                    oIns.Parameters.AddWithValue("a", vActive[vI]);
                    oIns.ExecuteNonQuery();
                }
            }
        }

        // ==================================================================== //
        //  users                                                               //
        // ==================================================================== //

        public bool GetUserByUsername(string aUsername, out TReportsUser aUser)
        {
            aUser = null;
            if (string.IsNullOrEmpty((aUsername ?? "").Trim()))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, display_name, created_at " +
                "FROM users WHERE username = :u COLLATE NOCASE"))
            {
                oCmd.Parameters.AddWithValue("u", aUsername.Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aUser = new TReportsUser();
                    FillUserFromReader(oReader, aUser);
                    return true;
                }
            }
        }

        public bool GetUserById(long aId, out TReportsUser aUser)
        {
            aUser = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, display_name, created_at " +
                "FROM users WHERE id = :i"))
            {
                oCmd.Parameters.AddWithValue("i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aUser = new TReportsUser();
                    FillUserFromReader(oReader, aUser);
                    return true;
                }
            }
        }

        public bool AuthenticateUser(string aUsername, string aPassword,
            out TReportsUser aUser)
        {
            aUser = null;
            TReportsUser oUser;
            if (!GetUserByUsername(aUsername, out oUser))
                return false;
            if (!Bcrypt.BcryptVerify(aPassword, oUser.PasswordHash))
                return false;
            aUser = oUser;
            return true;
        }

        public List<TReportsUser> ListUsers()
        {
            List<TReportsUser> vResult = new List<TReportsUser>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, display_name, created_at " +
                "FROM users ORDER BY role, username"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    TReportsUser vRow = new TReportsUser();
                    FillUserFromReader(oReader, vRow);
                    vResult.Add(vRow);
                }
            }
            return vResult;
        }

        /// <summary>
        /// Insert (aId = 0) or update a user. Returns the row id, or 0 when the
        /// username collides with another account.
        /// </summary>
        public long SaveUser(long aId, string aUsername, string aDisplayName,
            string aRole, string aPasswordHash)
        {
            if (string.IsNullOrEmpty((aUsername ?? "").Trim()))
                return 0;
            string vRole = (aRole ?? "").Trim().ToLowerInvariant();
            if ((vRole != ReportsConst.CS_ROLE_ADMIN) &&
                (vRole != ReportsConst.CS_ROLE_ANALYST) &&
                (vRole != ReportsConst.CS_ROLE_VIEWER))
                vRole = ReportsConst.CS_ROLE_VIEWER;

            using (SqliteConnection oConn = Acquire())
            {
                // Username collision check, scoped so an update of the same row passes.
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT id FROM users WHERE username = :u COLLATE NOCASE AND id <> :i"))
                {
                    oCmd.Parameters.AddWithValue("u", aUsername.Trim());
                    oCmd.Parameters.AddWithValue("i", aId);
                    object vValue = oCmd.ExecuteScalar();
                    if ((vValue != null) && (vValue != DBNull.Value))
                        return 0;
                }

                if (aId > 0)
                {
                    string vSQL;
                    if (!string.IsNullOrEmpty(aPasswordHash))
                        vSQL = "UPDATE users SET username = :u, display_name = :d, " +
                            "role = :r, password_hash = :p WHERE id = :i";
                    else
                        vSQL = "UPDATE users SET username = :u, display_name = :d, " +
                            "role = :r WHERE id = :i";
                    using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                    {
                        if (!string.IsNullOrEmpty(aPasswordHash))
                            oCmd.Parameters.AddWithValue("p", aPasswordHash);
                        oCmd.Parameters.AddWithValue("u", aUsername.Trim());
                        oCmd.Parameters.AddWithValue("d", (aDisplayName ?? "").Trim());
                        oCmd.Parameters.AddWithValue("r", vRole);
                        oCmd.Parameters.AddWithValue("i", aId);
                        oCmd.ExecuteNonQuery();
                    }
                    return aId;
                }

                using (SqliteCommand oCmd = NewCmd(oConn,
                    "INSERT INTO users (username, password_hash, role, display_name, " +
                    "created_at) VALUES (:u, :p, :r, :d, :c)"))
                {
                    oCmd.Parameters.AddWithValue("u", aUsername.Trim());
                    oCmd.Parameters.AddWithValue("p", aPasswordHash ?? "");
                    oCmd.Parameters.AddWithValue("r", vRole);
                    oCmd.Parameters.AddWithValue("d", (aDisplayName ?? "").Trim());
                    oCmd.Parameters.AddWithValue("c", ReportsDBHelpers.NowTimestamp());
                    oCmd.ExecuteNonQuery();
                }
                return LastInsertRowId(oConn);
            }
        }

        // ==================================================================== //
        //  passkeys (WebAuthn)                                                 //
        // ==================================================================== //

        public void AddPasskey(long aUserId, string aCredId, string aPublicKey,
            long aSignCount, string aDeviceName)
        {
            if ((aUserId <= 0) || string.IsNullOrEmpty((aCredId ?? "").Trim()))
                return;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "INSERT INTO passkeys (user_id, credential_id, public_key, " +
                "sign_count, device_name, created_at, last_used_at) " +
                "VALUES (:u, :c, :p, :s, :d, :t, :l)"))
            {
                oCmd.Parameters.AddWithValue("u", aUserId);
                oCmd.Parameters.AddWithValue("c", aCredId);
                oCmd.Parameters.AddWithValue("p", aPublicKey ?? "");
                oCmd.Parameters.AddWithValue("s", aSignCount);
                oCmd.Parameters.AddWithValue("d", aDeviceName ?? "");
                oCmd.Parameters.AddWithValue("t", ReportsDBHelpers.NowTimestamp());
                oCmd.Parameters.AddWithValue("l", "");
                oCmd.ExecuteNonQuery();
            }
        }

        public List<TReportsPasskey> GetPasskeysByUser(long aUserId)
        {
            List<TReportsPasskey> vResult = new List<TReportsPasskey>();
            if (aUserId <= 0)
                return vResult;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, sign_count, " +
                "device_name, created_at, last_used_at FROM passkeys " +
                "WHERE user_id = :u ORDER BY id DESC"))
            {
                oCmd.Parameters.AddWithValue("u", aUserId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        TReportsPasskey vRow = new TReportsPasskey();
                        FillPasskeyFromReader(oReader, vRow);
                        vResult.Add(vRow);
                    }
                }
            }
            return vResult;
        }

        public bool GetPasskeyByCredentialId(string aCredId, out TReportsPasskey aPk)
        {
            aPk = null;
            if (string.IsNullOrEmpty((aCredId ?? "").Trim()))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, sign_count, " +
                "device_name, created_at, last_used_at FROM passkeys " +
                "WHERE credential_id = :c"))
            {
                oCmd.Parameters.AddWithValue("c", aCredId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aPk = new TReportsPasskey();
                    FillPasskeyFromReader(oReader, aPk);
                    return true;
                }
            }
        }

        public void UpdatePasskeySignCount(long aId, long aSignCount)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "UPDATE passkeys SET sign_count = :s, last_used_at = :l WHERE id = :i"))
            {
                oCmd.Parameters.AddWithValue("s", aSignCount);
                oCmd.Parameters.AddWithValue("l", ReportsDBHelpers.NowTimestamp());
                oCmd.Parameters.AddWithValue("i", aId);
                oCmd.ExecuteNonQuery();
            }
        }

        public bool DeletePasskey(long aId, long aUserId)
        {
            if ((aId <= 0) || (aUserId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "DELETE FROM passkeys WHERE id = :i AND user_id = :u"))
            {
                oCmd.Parameters.AddWithValue("i", aId);
                oCmd.Parameters.AddWithValue("u", aUserId);
                return oCmd.ExecuteNonQuery() > 0;
            }
        }

        // ==================================================================== //
        //  report definitions                                                  //
        // ==================================================================== //

        public List<TReportsReportDef> ListReportDefs(string aCategory = "")
        {
            List<TReportsReportDef> vResult = new List<TReportsReportDef>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, name, description, category, sql_text, params_json, " +
                "chart_kind, owner_id, shared, created_at FROM report_defs " +
                "WHERE (:c = '' OR category = :c) ORDER BY category, name"))
            {
                oCmd.Parameters.AddWithValue("c", (aCategory ?? "").Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        TReportsReportDef vRow = new TReportsReportDef();
                        FillReportDefFromReader(oReader, vRow);
                        vResult.Add(vRow);
                    }
                }
            }
            return vResult;
        }

        public List<string> ListReportCategories()
        {
            List<string> vResult = new List<string>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT DISTINCT category FROM report_defs ORDER BY category"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                    vResult.Add(oReader.IsDBNull(0) ? "" : oReader.GetString(0));
            }
            return vResult;
        }

        public bool GetReportDef(long aId, out TReportsReportDef aDef)
        {
            aDef = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, name, description, category, sql_text, params_json, " +
                "chart_kind, owner_id, shared, created_at FROM report_defs WHERE id = :i"))
            {
                oCmd.Parameters.AddWithValue("i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aDef = new TReportsReportDef();
                    FillReportDefFromReader(oReader, aDef);
                    return true;
                }
            }
        }

        public long SaveReportDef(long aId, string aName, string aDescription,
            string aCategory, string aSQL, string aParamsJSON, string aChartKind,
            long aOwnerId, bool aShared)
        {
            if (string.IsNullOrEmpty((aName ?? "").Trim()))
                throw new EReportsDBError("The report needs a name.");
            // The gate: a stored definition is never accepted unless it is a single
            // read-only SELECT.
            string vError;
            if (!ValidateReportSQL(aSQL, out vError))
                throw new EReportsDBError(vError);

            using (SqliteConnection oConn = Acquire())
            {
                string vSQL;
                if (aId > 0)
                    vSQL = "UPDATE report_defs SET name = :n, description = :d, " +
                        "category = :c, sql_text = :s, params_json = :p, " +
                        "chart_kind = :k, shared = :h WHERE id = :i";
                else
                    vSQL = "INSERT INTO report_defs (name, description, category, " +
                        "sql_text, params_json, chart_kind, owner_id, shared, " +
                        "created_at) VALUES (:n, :d, :c, :s, :p, :k, :o, :h, :t)";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (aId > 0)
                        oCmd.Parameters.AddWithValue("i", aId);
                    oCmd.Parameters.AddWithValue("n", aName.Trim());
                    oCmd.Parameters.AddWithValue("d", (aDescription ?? "").Trim());
                    oCmd.Parameters.AddWithValue("c", (aCategory ?? "").Trim());
                    oCmd.Parameters.AddWithValue("s", (aSQL ?? "").Trim());
                    oCmd.Parameters.AddWithValue("p", (aParamsJSON ?? "").Trim());
                    oCmd.Parameters.AddWithValue("k", (aChartKind ?? "").Trim());
                    oCmd.Parameters.AddWithValue("h", aShared ? 1 : 0);
                    if (aId <= 0)
                    {
                        oCmd.Parameters.AddWithValue("o", aOwnerId);
                        oCmd.Parameters.AddWithValue("t",
                            ReportsDBHelpers.NowTimestamp());
                    }
                    oCmd.ExecuteNonQuery();
                }
                if (aId > 0)
                    return aId;
                return LastInsertRowId(oConn);
            }
        }

        public bool DeleteReportDef(long aId)
        {
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                bool vResult;
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "DELETE FROM report_defs WHERE id = :i"))
                {
                    oCmd.Parameters.AddWithValue("i", aId);
                    vResult = oCmd.ExecuteNonQuery() > 0;
                }
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "DELETE FROM schedules WHERE report_id = :i"))
                {
                    oCmd.Parameters.AddWithValue("i", aId);
                    oCmd.ExecuteNonQuery();
                }
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "DELETE FROM saved_views WHERE report_id = :i"))
                {
                    oCmd.Parameters.AddWithValue("i", aId);
                    oCmd.ExecuteNonQuery();
                }
                return vResult;
            }
        }

        /// <summary>
        /// THE security gate on admin-authored SQL. True only for a single read-only
        /// SELECT (or a WITH ... SELECT). Everything else - a second statement, any
        /// DML/DDL, PRAGMA, ATTACH - is rejected with a reason.
        /// </summary>
        public static bool ValidateReportSQL(string aSQL, out string aError)
        {
            aError = "";
            string vBody = (aSQL ?? "").Trim();
            if (vBody.Length == 0)
            {
                aError = "The report SQL is empty.";
                return false;
            }
            if (vBody.Length > 8000)
            {
                aError = "The report SQL is longer than the 8000 character limit.";
                return false;
            }

            // Comments and string literals are blanked first, so neither a keyword
            // hidden in a literal nor one commented out can change the verdict.
            string vUpper = StripSQLLiterals(vBody).ToUpperInvariant();

            // A single statement only: a trailing semicolon is tolerated, an inner
            // one is not.
            vBody = vUpper.TrimEnd();
            while ((vBody.Length > 0) && (vBody[vBody.Length - 1] == ';'))
                vBody = vBody.Substring(0, vBody.Length - 1).TrimEnd();
            if (vBody.IndexOf(';') >= 0)
            {
                aError = "Only ONE statement is allowed. Remove the extra semicolon.";
                return false;
            }

            vBody = vBody.TrimStart();
            if (!vBody.StartsWith("SELECT", StringComparison.Ordinal) &&
                !vBody.StartsWith("WITH ", StringComparison.Ordinal))
            {
                aError = "Only a SELECT (or WITH ... SELECT) is allowed here.";
                return false;
            }

            for (int vI = 0; vI < CS_SQL_FORBIDDEN.Length; vI++)
                if (ContainsWord(vBody, CS_SQL_FORBIDDEN[vI]))
                {
                    aError = "The keyword " + CS_SQL_FORBIDDEN[vI] +
                        " is not allowed in a report body.";
                    return false;
                }

            return true;
        }

        /// <summary>
        /// Parse report_defs.params_json ({"params":[{...}]}) into declarations.
        /// </summary>
        public static List<TReportsParamDef> ParseParamDefs(string aJSON)
        {
            List<TReportsParamDef> vResult = new List<TReportsParamDef>();
            if (string.IsNullOrEmpty((aJSON ?? "").Trim()))
                return vResult;

            System.Text.Json.JsonDocument oDoc;
            try
            {
                oDoc = System.Text.Json.JsonDocument.Parse(aJSON);
            }
            catch (Exception)
            {
                return vResult;
            }

            using (oDoc)
            {
                System.Text.Json.JsonElement vRoot = oDoc.RootElement;
                if (vRoot.ValueKind != System.Text.Json.JsonValueKind.Object)
                    return vResult;
                System.Text.Json.JsonElement vParams;
                if (!vRoot.TryGetProperty("params", out vParams) ||
                    (vParams.ValueKind != System.Text.Json.JsonValueKind.Array))
                    return vResult;

                foreach (System.Text.Json.JsonElement vItem in vParams.EnumerateArray())
                {
                    if (vItem.ValueKind != System.Text.Json.JsonValueKind.Object)
                        continue;
                    TReportsParamDef vDef = new TReportsParamDef();
                    vDef.Name = NodeStr(vItem, "name");
                    if (vDef.Name.Trim().Length == 0)
                        continue;
                    vDef.Caption = NodeStr(vItem, "caption");
                    if (vDef.Caption.Length == 0)
                        vDef.Caption = vDef.Name;
                    vDef.Kind = NodeStr(vItem, "kind").ToLowerInvariant();
                    if (vDef.Kind.Length == 0)
                        vDef.Kind = "text";
                    vDef.DefaultValue = NodeStr(vItem, "default");
                    // 'source' names a lookup the page fills at render time
                    // (categories, regions, segments, statuses); 'options' is a
                    // literal list.
                    vDef.Options = NodeStr(vItem, "source");
                    if (vDef.Options.Length == 0)
                        vDef.Options = NodeStr(vItem, "options");
                    vDef.MinValue = StrToIntDef(NodeStr(vItem, "min"), 0);
                    vDef.MaxValue = StrToIntDef(NodeStr(vItem, "max"), 0);
                    vResult.Add(vDef);
                }
            }
            return vResult;
        }

        private static string NodeStr(System.Text.Json.JsonElement aParent, string aName)
        {
            System.Text.Json.JsonElement vNode;
            if (!aParent.TryGetProperty(aName, out vNode))
                return "";
            switch (vNode.ValueKind)
            {
                case System.Text.Json.JsonValueKind.String:
                    return vNode.GetString() ?? "";
                case System.Text.Json.JsonValueKind.Number:
                    return vNode.GetRawText();
                case System.Text.Json.JsonValueKind.True:
                    return "True";
                case System.Text.Json.JsonValueKind.False:
                    return "False";
                default:
                    return "";
            }
        }

        internal static int StrToIntDef(string aValue, int aDefault)
        {
            int vResult;
            if (int.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        internal static long StrToInt64Def(string aValue, long aDefault)
        {
            long vResult;
            if (long.TryParse((aValue ?? "").Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out vResult))
                return vResult;
            return aDefault;
        }

        /// <summary>Re-encode the values a run was executed with, for the history.</summary>
        public static string EncodeParamValues(List<TReportsParamValue> aValues)
        {
            StringBuilder vBuf = new StringBuilder();
            vBuf.Append('{');
            if (aValues != null)
            {
                for (int vI = 0; vI < aValues.Count; vI++)
                {
                    if (vI > 0)
                        vBuf.Append(',');
                    vBuf.Append('"').Append(JsonEscape(aValues[vI].Name)).Append("\":\"")
                        .Append(JsonEscape(aValues[vI].Value)).Append('"');
                }
            }
            vBuf.Append('}');
            return vBuf.ToString();
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

        /// <summary>
        /// Validate, bind and open a report. Raises EReportsDBError when the SQL is
        /// rejected. The returned query rides a read-only connection.
        /// </summary>
        public TReportsQuery NewReportQuery(TReportsReportDef aDef,
            List<TReportsParamValue> aValues)
        {
            // Re-validate at execution time, not just at save time: a definition
            // edited straight in the database file must not become a way in.
            string vError;
            if (!ValidateReportSQL(aDef.SqlText, out vError))
                throw new EReportsDBError(vError);

            // ReadOnly = true -> the SQLite driver itself opens the file read-only.
            TReportsQuery oQuery = new TReportsQuery(this, aDef.SqlText, true);
            try
            {
                if (aValues != null)
                {
                    for (int vI = 0; vI < aValues.Count; vI++)
                    {
                        if (!oQuery.HasParam(aValues[vI].Name))
                            continue;
                        // Every value is BOUND. Nothing a user types is ever
                        // concatenated into the statement text.
                        if ((aValues[vI].Kind == "int") || (aValues[vI].Kind == "range"))
                            oQuery.SetParamInt(aValues[vI].Name,
                                StrToInt64Def(aValues[vI].Value, 0));
                        else
                            oQuery.SetParamStr(aValues[vI].Name, aValues[vI].Value);
                    }
                }
                oQuery.Open();
                return oQuery;
            }
            catch (Exception)
            {
                oQuery.Dispose();
                throw;
            }
        }

        // ==================================================================== //
        //  run history                                                         //
        // ==================================================================== //

        public long LogRun(long aReportId, long aUserId, string aParamsJSON,
            int aRowCount, int aDurationMS, string aStatus)
        {
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "INSERT INTO report_runs (report_id, user_id, params_json, " +
                    "row_count, duration_ms, status, created_at) VALUES " +
                    "(:r, :u, :p, :n, :d, :s, :c)"))
                {
                    oCmd.Parameters.AddWithValue("r", aReportId);
                    oCmd.Parameters.AddWithValue("u", aUserId);
                    oCmd.Parameters.AddWithValue("p", aParamsJSON ?? "");
                    oCmd.Parameters.AddWithValue("n", aRowCount);
                    oCmd.Parameters.AddWithValue("d", aDurationMS);
                    oCmd.Parameters.AddWithValue("s", aStatus ?? "");
                    oCmd.Parameters.AddWithValue("c", ReportsDBHelpers.NowTimestamp());
                    oCmd.ExecuteNonQuery();
                }
                return LastInsertRowId(oConn);
            }
        }

        public List<TReportsRun> ListRuns(int aLimit)
        {
            List<TReportsRun> vResult = new List<TReportsRun>();
            if (aLimit <= 0)
                aLimit = 50;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT rr.id, rr.report_id, rr.user_id, rr.params_json, " +
                "rr.row_count, rr.duration_ms, rr.status, rr.created_at, " +
                "COALESCE(rd.name, '(deleted)') AS report_name, " +
                "COALESCE(u.username, 'system') AS username FROM report_runs rr " +
                "LEFT JOIN report_defs rd ON rd.id = rr.report_id " +
                "LEFT JOIN users u ON u.id = rr.user_id " +
                "ORDER BY rr.id DESC LIMIT :l"))
            {
                oCmd.Parameters.AddWithValue("l", aLimit);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        TReportsRun vRow = new TReportsRun();
                        vRow.Id = RInt64(oReader, "id");
                        vRow.ReportId = RInt64(oReader, "report_id");
                        vRow.ReportName = RStr(oReader, "report_name");
                        vRow.UserId = RInt64(oReader, "user_id");
                        vRow.Username = RStr(oReader, "username");
                        vRow.ParamsJSON = RStr(oReader, "params_json");
                        vRow.RowCount = RInt(oReader, "row_count");
                        vRow.DurationMS = RInt(oReader, "duration_ms");
                        vRow.Status = RStr(oReader, "status");
                        vRow.CreatedAt = ReportsDBHelpers.ParseReportsTimestamp(
                            RStr(oReader, "created_at"));
                        vResult.Add(vRow);
                    }
                }
            }
            return vResult;
        }

        // ==================================================================== //
        //  saved views                                                         //
        // ==================================================================== //

        public List<TReportsSavedView> ListSavedViews(long aUserId)
        {
            List<TReportsSavedView> vResult = new List<TReportsSavedView>();
            using (SqliteConnection oConn = Acquire())
            // Scoped to the session's own user: an id from the request is never
            // trusted to widen the set.
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT sv.id, sv.user_id, sv.report_id, sv.name, sv.state_json, " +
                "sv.created_at, COALESCE(rd.name, '(deleted)') AS report_name, " +
                "COALESCE(u.username, '') AS username FROM saved_views sv " +
                "LEFT JOIN report_defs rd ON rd.id = sv.report_id " +
                "LEFT JOIN users u ON u.id = sv.user_id " +
                "WHERE sv.user_id = :u ORDER BY sv.id DESC"))
            {
                oCmd.Parameters.AddWithValue("u", aUserId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        TReportsSavedView vRow = new TReportsSavedView();
                        vRow.Id = RInt64(oReader, "id");
                        vRow.UserId = RInt64(oReader, "user_id");
                        vRow.Username = RStr(oReader, "username");
                        vRow.ReportId = RInt64(oReader, "report_id");
                        vRow.ReportName = RStr(oReader, "report_name");
                        vRow.Name = RStr(oReader, "name");
                        vRow.StateJSON = RStr(oReader, "state_json");
                        vRow.CreatedAt = ReportsDBHelpers.ParseReportsTimestamp(
                            RStr(oReader, "created_at"));
                        vResult.Add(vRow);
                    }
                }
            }
            return vResult;
        }

        public long SaveSavedView(long aUserId, long aReportId, string aName,
            string aStateJSON)
        {
            if ((aUserId <= 0) || string.IsNullOrEmpty((aName ?? "").Trim()))
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "INSERT INTO saved_views (user_id, report_id, name, state_json, " +
                    "created_at) VALUES (:u, :r, :n, :s, :c)"))
                {
                    oCmd.Parameters.AddWithValue("u", aUserId);
                    oCmd.Parameters.AddWithValue("r", aReportId);
                    oCmd.Parameters.AddWithValue("n", aName.Trim());
                    oCmd.Parameters.AddWithValue("s", aStateJSON ?? "");
                    oCmd.Parameters.AddWithValue("c", ReportsDBHelpers.NowTimestamp());
                    oCmd.ExecuteNonQuery();
                }
                return LastInsertRowId(oConn);
            }
        }

        public bool DeleteSavedView(long aId, long aUserId)
        {
            if ((aId <= 0) || (aUserId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            // The user_id predicate is the IDOR guard: another user's view id
            // deletes nothing.
            using (SqliteCommand oCmd = NewCmd(oConn,
                "DELETE FROM saved_views WHERE id = :i AND user_id = :u"))
            {
                oCmd.Parameters.AddWithValue("i", aId);
                oCmd.Parameters.AddWithValue("u", aUserId);
                return oCmd.ExecuteNonQuery() > 0;
            }
        }

        // ==================================================================== //
        //  schedules                                                           //
        // ==================================================================== //

        public List<TReportsSchedule> ListSchedules()
        {
            List<TReportsSchedule> vResult = new List<TReportsSchedule>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT s.id, s.report_id, s.cron_text, s.format, s.recipients, " +
                "s.last_run_at, s.next_run_at, s.active, " +
                "COALESCE(rd.name, '(deleted)') AS report_name FROM schedules s " +
                "LEFT JOIN report_defs rd ON rd.id = s.report_id ORDER BY s.id"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    TReportsSchedule vRow = new TReportsSchedule();
                    vRow.Id = RInt64(oReader, "id");
                    vRow.ReportId = RInt64(oReader, "report_id");
                    vRow.ReportName = RStr(oReader, "report_name");
                    vRow.CronText = RStr(oReader, "cron_text");
                    vRow.Format = RStr(oReader, "format");
                    vRow.Recipients = RStr(oReader, "recipients");
                    vRow.LastRunAt = ReportsDBHelpers.ParseReportsTimestamp(
                        RStr(oReader, "last_run_at"));
                    vRow.NextRunAt = ReportsDBHelpers.ParseReportsTimestamp(
                        RStr(oReader, "next_run_at"));
                    vRow.Active = RInt(oReader, "active") != 0;
                    vResult.Add(vRow);
                }
            }
            return vResult;
        }

        public bool GetSchedule(long aId, out TReportsSchedule aRow)
        {
            aRow = null;
            List<TReportsSchedule> vRows = ListSchedules();
            for (int vI = 0; vI < vRows.Count; vI++)
                if (vRows[vI].Id == aId)
                {
                    aRow = vRows[vI];
                    return true;
                }
            return false;
        }

        public long SaveSchedule(long aId, long aReportId, string aCronText,
            string aFormat, string aRecipients, bool aActive)
        {
            string vFormat = (aFormat ?? "").Trim().ToLowerInvariant();
            if ((vFormat != "pdf") && (vFormat != "xlsx") && (vFormat != "csv"))
                vFormat = "pdf";

            using (SqliteConnection oConn = Acquire())
            {
                string vSQL;
                if (aId > 0)
                    vSQL = "UPDATE schedules SET report_id = :r, cron_text = :c, " +
                        "format = :f, recipients = :p, active = :a WHERE id = :i";
                else
                    vSQL = "INSERT INTO schedules (report_id, cron_text, format, " +
                        "recipients, last_run_at, next_run_at, active) VALUES " +
                        "(:r, :c, :f, :p, :l, :n, :a)";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (aId > 0)
                        oCmd.Parameters.AddWithValue("i", aId);
                    oCmd.Parameters.AddWithValue("r", aReportId);
                    oCmd.Parameters.AddWithValue("c", (aCronText ?? "").Trim());
                    oCmd.Parameters.AddWithValue("f", vFormat);
                    oCmd.Parameters.AddWithValue("p", (aRecipients ?? "").Trim());
                    oCmd.Parameters.AddWithValue("a", aActive ? 1 : 0);
                    if (aId <= 0)
                    {
                        oCmd.Parameters.AddWithValue("l", "");
                        oCmd.Parameters.AddWithValue("n",
                            ReportsDBHelpers.FormatReportsTimestamp(
                                DateTime.Now.AddDays(1)));
                    }
                    oCmd.ExecuteNonQuery();
                }
                if (aId > 0)
                    return aId;
                return LastInsertRowId(oConn);
            }
        }

        public void TouchSchedule(long aId)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "UPDATE schedules SET last_run_at = :l, next_run_at = :n WHERE id = :i"))
            {
                oCmd.Parameters.AddWithValue("l", ReportsDBHelpers.NowTimestamp());
                oCmd.Parameters.AddWithValue("n",
                    ReportsDBHelpers.FormatReportsTimestamp(DateTime.Now.AddDays(1)));
                oCmd.Parameters.AddWithValue("i", aId);
                oCmd.ExecuteNonQuery();
            }
        }

        // ==================================================================== //
        //  explore (the throughput page)                                       //
        // ==================================================================== //

        // Shared WHERE clause of the count and the page query, so both always see
        // the same set. Every predicate is a bound parameter.
        private static string ExploreWhereSQL(TReportsExploreFilter aFilter)
        {
            string vResult = " WHERE 1 = 1";
            if (aFilter.Search.Trim().Length > 0)
                vResult = vResult + " AND (p.name LIKE :search ESCAPE '\\' " +
                    "OR p.sku LIKE :search ESCAPE '\\' " +
                    "OR c.name LIKE :search ESCAPE '\\')";
            if (aFilter.Region.Trim().Length > 0)
                vResult = vResult + " AND r.id = :region";
            if (aFilter.Segment.Trim().Length > 0)
                vResult = vResult + " AND c.segment = :segment";
            if (aFilter.Category.Trim().Length > 0)
                vResult = vResult + " AND cat.id = :category";
            // One bound parameter per selected status, never a value spliced into
            // the IN list.
            if (aFilter.Statuses.Length > 0)
            {
                string vIn = "";
                for (int vI = 0; vI < aFilter.Statuses.Length; vI++)
                    vIn = vIn + ", :st" + vI.ToString(CultureInfo.InvariantCulture);
                vResult = vResult + " AND o.status IN (" + vIn.Substring(2) + ")";
            }
            if (ReportsDBHelpers.IsISODate(aFilter.DateFrom))
                vResult = vResult + " AND o.order_date >= :dfrom";
            if (ReportsDBHelpers.IsISODate(aFilter.DateTo))
                vResult = vResult + " AND o.order_date <= :dto";
            if (aFilter.MinQty > 0)
                vResult = vResult + " AND ol.qty >= :minqty";
            if (aFilter.MaxQty > 0)
                vResult = vResult + " AND ol.qty <= :maxqty";
            return vResult;
        }

        private static void BindExploreParams(TReportsQuery aQuery,
            TReportsExploreFilter aFilter)
        {
            if (aFilter.Search.Trim().Length > 0)
                aQuery.SetParamStr("search",
                    "%" + ReportsDBHelpers.EscapeLikeValue(aFilter.Search.Trim()) + "%");
            if (aFilter.Region.Trim().Length > 0)
                aQuery.SetParamStr("region", aFilter.Region.Trim());
            if (aFilter.Segment.Trim().Length > 0)
                aQuery.SetParamStr("segment", aFilter.Segment.Trim());
            if (aFilter.Category.Trim().Length > 0)
                aQuery.SetParamStr("category", aFilter.Category.Trim());
            for (int vI = 0; vI < aFilter.Statuses.Length; vI++)
                aQuery.SetParamStr("st" + vI.ToString(CultureInfo.InvariantCulture),
                    aFilter.Statuses[vI]);
            if (ReportsDBHelpers.IsISODate(aFilter.DateFrom))
                aQuery.SetParamStr("dfrom", aFilter.DateFrom);
            if (ReportsDBHelpers.IsISODate(aFilter.DateTo))
                aQuery.SetParamStr("dto", aFilter.DateTo);
            if (aFilter.MinQty > 0)
                aQuery.SetParamInt("minqty", aFilter.MinQty);
            if (aFilter.MaxQty > 0)
                aQuery.SetParamInt("maxqty", aFilter.MaxQty);
        }

        /// <summary>Total rows matching aFilter, computed in SQL.</summary>
        public int ExploreCount(TReportsExploreFilter aFilter)
        {
            using (TReportsQuery oQuery = NewQuery("SELECT COUNT(*) AS n" +
                CS_EXPLORE_FROM + ExploreWhereSQL(aFilter)))
            {
                BindExploreParams(oQuery, aFilter);
                oQuery.Open();
                if (oQuery.DataSet.Rows.Count == 0)
                    return 0;
                return Convert.ToInt32(oQuery.DataSet.Rows[0][0],
                    CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The literal SQL the page runs, for display next to the grid.</summary>
        public string ExploreSQLText(TReportsExploreFilter aFilter)
        {
            return CS_EXPLORE_SELECT + CS_EXPLORE_FROM + ExploreWhereSQL(aFilter) +
                " ORDER BY " + ExploreSortSQL(aFilter.Sort) + " " +
                DirSQL(aFilter.Dir) + " LIMIT :lim OFFSET :off";
        }

        /// <summary>One page of order lines, paged / sorted / filtered in SQL.</summary>
        public TReportsQuery NewExploreQuery(TReportsExploreFilter aFilter,
            int aOffset, int aLimit)
        {
            if (aLimit <= 0)
                aLimit = 50;
            // Ceiling for the whole-set exports. The interactive paths clamp
            // themselves much lower before they get here.
            if (aLimit > 25000)
                aLimit = 25000;
            if (aOffset < 0)
                aOffset = 0;
            TReportsQuery oQuery = NewQuery(ExploreSQLText(aFilter));
            try
            {
                BindExploreParams(oQuery, aFilter);
                oQuery.SetParamInt("lim", aLimit);
                oQuery.SetParamInt("off", aOffset);
                oQuery.Open();
                return oQuery;
            }
            catch (Exception)
            {
                oQuery.Dispose();
                throw;
            }
        }

        // ==================================================================== //
        //  lookups for the parameter and filter forms                          //
        // ==================================================================== //

        private List<string> PairList(string aSQL, bool aSingleColumn)
        {
            List<string> vResult = new List<string>();
            using (TReportsQuery oQuery = NewQuery(aSQL))
            {
                oQuery.Open();
                DataTable oTable = oQuery.DataSet;
                for (int vI = 0; vI < oTable.Rows.Count; vI++)
                {
                    DataRow oRow = oTable.Rows[vI];
                    string vFirst = Convert.ToString(oRow[0],
                        CultureInfo.InvariantCulture) ?? "";
                    if (aSingleColumn)
                        vResult.Add(vFirst + "|" + vFirst);
                    else
                        vResult.Add(vFirst + "|" + (Convert.ToString(oRow[1],
                            CultureInfo.InvariantCulture) ?? ""));
                }
            }
            return vResult;
        }

        public List<string> ListRegions()
        {
            return PairList("SELECT id, name FROM regions ORDER BY name", false);
        }

        public List<string> ListCategories()
        {
            return PairList("SELECT id, name FROM categories ORDER BY name", false);
        }

        public List<string> ListSegments()
        {
            return PairList("SELECT DISTINCT segment FROM customers ORDER BY segment",
                true);
        }

        public List<string> ListStatuses()
        {
            return PairList("SELECT DISTINCT status FROM orders ORDER BY status", true);
        }

        // ==================================================================== //
        //  dashboards                                                          //
        // ==================================================================== //

        public TReportsKPIs GetKPIs()
        {
            TReportsKPIs vResult = new TReportsKPIs();
            DateTime vToday = DateTime.Today;
            using (TReportsQuery oQuery = NewQuery("SELECT " +
                "(SELECT COALESCE(SUM(ol.line_total), 0) FROM order_lines ol " +
                "JOIN orders o ON o.id = ol.order_id WHERE o.status <> 'Cancelled' " +
                "AND o.order_date >= :d12) AS rev12, " +
                "(SELECT COALESCE(SUM(ol.line_total), 0) FROM order_lines ol " +
                "JOIN orders o ON o.id = ol.order_id WHERE o.status <> 'Cancelled' " +
                "AND o.order_date >= :d24 AND o.order_date < :d12) AS rev24, " +
                "(SELECT COUNT(*) FROM orders o WHERE o.status <> 'Cancelled' " +
                "AND o.order_date >= :d12) AS ord12, " +
                "(SELECT COALESCE(SUM(ol.qty * p.unit_cost), 0) FROM order_lines ol " +
                "JOIN orders o ON o.id = ol.order_id " +
                "JOIN products p ON p.id = ol.product_id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d12) AS cost12, " +
                "(SELECT COUNT(*) FROM orders WHERE status = 'Open') AS openord, " +
                "(SELECT COUNT(*) FROM customers) AS custs, " +
                "(SELECT COUNT(*) FROM order_lines) AS lines"))
            {
                oQuery.SetParamStr("d12",
                    ReportsDBHelpers.FormatReportsDate(vToday.AddDays(-365)));
                oQuery.SetParamStr("d24",
                    ReportsDBHelpers.FormatReportsDate(vToday.AddDays(-730)));
                oQuery.Open();
                if (oQuery.DataSet.Rows.Count == 0)
                    return vResult;
                DataRow oRow = oQuery.DataSet.Rows[0];
                vResult.Revenue12M = CellDouble(oRow, "rev12");
                vResult.RevenuePrev12M = CellDouble(oRow, "rev24");
                vResult.Orders12M = (int)CellDouble(oRow, "ord12");
                vResult.OpenOrders = (int)CellDouble(oRow, "openord");
                vResult.Customers = (int)CellDouble(oRow, "custs");
                vResult.LinesTotal = (int)CellDouble(oRow, "lines");
                if (vResult.Revenue12M > 0)
                    vResult.Margin12MPct = 100.0 *
                        (vResult.Revenue12M - CellDouble(oRow, "cost12")) /
                        vResult.Revenue12M;
                if (vResult.Orders12M > 0)
                    vResult.AvgOrderValue = vResult.Revenue12M / vResult.Orders12M;
            }
            return vResult;
        }

        internal static double CellDouble(DataRow aRow, string aColumn)
        {
            object vValue = aRow[aColumn];
            if ((vValue == null) || (vValue == DBNull.Value))
                return 0;
            try
            {
                return Convert.ToDouble(vValue, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        internal static string CellString(DataRow aRow, string aColumn)
        {
            object vValue = aRow[aColumn];
            if ((vValue == null) || (vValue == DBNull.Value))
                return "";
            return Convert.ToString(vValue, CultureInfo.InvariantCulture) ?? "";
        }

        private List<TReportsSeriesPoint> Series(string aSQL, string aLabelField,
            string aValue1Field, string aValue2Field, string aDateParam,
            DateTime aDateValue, string aIntParam, long aIntValue)
        {
            List<TReportsSeriesPoint> vResult = new List<TReportsSeriesPoint>();
            using (TReportsQuery oQuery = NewQuery(aSQL))
            {
                if (aDateParam != null)
                    oQuery.SetParamStr(aDateParam,
                        ReportsDBHelpers.FormatReportsDate(aDateValue));
                if (aIntParam != null)
                    oQuery.SetParamInt(aIntParam, aIntValue);
                oQuery.Open();
                DataTable oTable = oQuery.DataSet;
                for (int vI = 0; vI < oTable.Rows.Count; vI++)
                {
                    DataRow oRow = oTable.Rows[vI];
                    TReportsSeriesPoint vPoint = new TReportsSeriesPoint();
                    vPoint.BucketLabel = CellString(oRow, aLabelField);
                    vPoint.Value1 = CellDouble(oRow, aValue1Field);
                    vPoint.Value2 = CellDouble(oRow, aValue2Field);
                    vResult.Add(vPoint);
                }
            }
            return vResult;
        }

        public List<TReportsSeriesPoint> GetRevenueByMonth(int aMonths)
        {
            if (aMonths <= 0)
                aMonths = 24;
            return Series("SELECT substr(o.order_date, 1, 7) AS m, " +
                "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                "COUNT(DISTINCT o.id) AS orders FROM orders o " +
                "JOIN order_lines ol ON ol.order_id = o.id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY m ORDER BY m",
                "m", "revenue", "orders", "d",
                DateTime.Today.AddDays(-aMonths * 31), null, 0);
        }

        public List<TReportsSeriesPoint> GetRevenueByRegion()
        {
            return Series("SELECT r.name AS region, " +
                "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                "COUNT(DISTINCT o.id) AS orders FROM orders o " +
                "JOIN customers c ON c.id = o.customer_id " +
                "JOIN regions r ON r.id = c.region_id " +
                "JOIN order_lines ol ON ol.order_id = o.id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY r.name ORDER BY revenue DESC",
                "region", "revenue", "orders", "d",
                DateTime.Today.AddDays(-365), null, 0);
        }

        public List<TReportsSeriesPoint> GetMarginByCategory()
        {
            return Series("SELECT cat.name AS category, " +
                "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                "ROUND(SUM(ol.line_total) - SUM(ol.qty * p.unit_cost), 2) AS margin " +
                "FROM order_lines ol JOIN orders o ON o.id = ol.order_id " +
                "JOIN products p ON p.id = ol.product_id " +
                "JOIN categories cat ON cat.id = p.category_id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY cat.name ORDER BY margin DESC",
                "category", "revenue", "margin", "d",
                DateTime.Today.AddDays(-365), null, 0);
        }

        public List<TReportsSeriesPoint> GetTopSalespeople(int aLimit)
        {
            if (aLimit <= 0)
                aLimit = 10;
            return Series("SELECT s.name AS sp, " +
                "ROUND(SUM(ol.line_total), 2) AS revenue, " +
                "ROUND(s.target_monthly * 12, 2) AS target FROM orders o " +
                "JOIN salespeople s ON s.id = o.salesperson_id " +
                "JOIN order_lines ol ON ol.order_id = o.id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY s.id, s.name, s.target_monthly " +
                "ORDER BY revenue DESC LIMIT :l",
                "sp", "revenue", "target", "d",
                DateTime.Today.AddDays(-365), "l", aLimit);
        }

        public List<TReportsSeriesPoint> GetStatusMix()
        {
            return Series("SELECT status, COUNT(*) AS n, " +
                "ROUND(SUM(total), 2) AS value FROM orders " +
                "WHERE order_date >= :d GROUP BY status ORDER BY n DESC",
                "status", "n", "value", "d", DateTime.Today.AddDays(-365), null, 0);
        }

        /// <summary>Region x month revenue, for the Heatmap.</summary>
        public void GetRevenueHeatmap(out List<string> aRowLabels,
            out List<string> aColLabels, out double[] aValues)
        {
            aRowLabels = new List<string>();
            aColLabels = new List<string>();
            aValues = new double[0];

            // Two passes over one result set: first collect the axes, then fill the
            // dense value matrix the Heatmap wants.
            using (TReportsQuery oQuery = NewQuery("SELECT r.name AS region, " +
                "substr(o.order_date, 1, 7) AS m, " +
                "ROUND(SUM(ol.line_total), 0) AS revenue FROM orders o " +
                "JOIN customers c ON c.id = o.customer_id " +
                "JOIN regions r ON r.id = c.region_id " +
                "JOIN order_lines ol ON ol.order_id = o.id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY r.name, m ORDER BY r.name, m"))
            {
                oQuery.SetParamStr("d", ReportsDBHelpers.FormatReportsDate(
                    DateTime.Today.AddDays(-365)));
                oQuery.Open();
                DataTable oTable = oQuery.DataSet;
                for (int vI = 0; vI < oTable.Rows.Count; vI++)
                {
                    string vRegion = CellString(oTable.Rows[vI], "region");
                    string vMonth = CellString(oTable.Rows[vI], "m");
                    if (!aRowLabels.Contains(vRegion))
                        aRowLabels.Add(vRegion);
                    if (!aColLabels.Contains(vMonth))
                        aColLabels.Add(vMonth);
                }

                aValues = new double[aRowLabels.Count * aColLabels.Count];
                for (int vI = 0; vI < oTable.Rows.Count; vI++)
                {
                    int vRowIdx = aRowLabels.IndexOf(CellString(oTable.Rows[vI], "region"));
                    int vColIdx = aColLabels.IndexOf(CellString(oTable.Rows[vI], "m"));
                    if ((vRowIdx >= 0) && (vColIdx >= 0))
                        aValues[vRowIdx * aColLabels.Count + vColIdx] =
                            CellDouble(oTable.Rows[vI], "revenue");
                }
            }
        }

        public List<TReportsSeriesPoint> GetPriceHistory(int aMonths)
        {
            List<TReportsSeriesPoint> vResult = new List<TReportsSeriesPoint>();
            List<string> vLabels;
            double[] vOpen, vHigh, vLow, vClose, vVol;
            int vCount = GetPriceHistoryOHLC(aMonths, out vLabels, out vOpen,
                out vHigh, out vLow, out vClose, out vVol);
            for (int vI = 0; vI < vCount; vI++)
            {
                TReportsSeriesPoint vPoint = new TReportsSeriesPoint();
                vPoint.BucketLabel = vLabels[vI];
                vPoint.Value1 = vClose[vI];
                vPoint.Value2 = vVol[vI];
                vResult.Add(vPoint);
            }
            return vResult;
        }

        /// <summary>
        /// Monthly open / high / low / close of the average selling price, computed
        /// in SQL from the order lines themselves. No separate price table is
        /// needed: the candlesticks are the real trading history of the catalogue.
        /// </summary>
        public int GetPriceHistoryOHLC(int aMonths, out List<string> aLabels,
            out double[] aOpen, out double[] aHigh, out double[] aLow,
            out double[] aClose, out double[] aVolume)
        {
            aLabels = new List<string>();
            List<double> vOpen = new List<double>();
            List<double> vHigh = new List<double>();
            List<double> vLow = new List<double>();
            List<double> vClose = new List<double>();
            List<double> vVolume = new List<double>();
            if (aMonths <= 0)
                aMonths = 18;

            using (TReportsQuery oQuery = NewQuery("WITH daily AS (" +
                "SELECT o.order_date AS d, substr(o.order_date, 1, 7) AS m, " +
                "AVG(ol.unit_price * (1 - ol.discount)) AS avg_price, " +
                "COUNT(*) AS lines FROM order_lines ol " +
                "JOIN orders o ON o.id = ol.order_id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY o.order_date) " + "SELECT m, " +
                "ROUND(MAX(avg_price), 2) AS hi, ROUND(MIN(avg_price), 2) AS lo, " +
                "ROUND(SUM(lines), 0) AS vol, " +
                "ROUND((SELECT avg_price FROM daily d2 WHERE d2.m = daily.m " +
                "ORDER BY d2.d ASC LIMIT 1), 2) AS op, " +
                "ROUND((SELECT avg_price FROM daily d3 WHERE d3.m = daily.m " +
                "ORDER BY d3.d DESC LIMIT 1), 2) AS cl " +
                "FROM daily GROUP BY m ORDER BY m"))
            {
                oQuery.SetParamStr("d", ReportsDBHelpers.FormatReportsDate(
                    DateTime.Today.AddDays(-aMonths * 31)));
                oQuery.Open();
                DataTable oTable = oQuery.DataSet;
                for (int vI = 0; vI < oTable.Rows.Count; vI++)
                {
                    DataRow oRow = oTable.Rows[vI];
                    aLabels.Add(CellString(oRow, "m"));
                    vOpen.Add(CellDouble(oRow, "op"));
                    vHigh.Add(CellDouble(oRow, "hi"));
                    vLow.Add(CellDouble(oRow, "lo"));
                    vClose.Add(CellDouble(oRow, "cl"));
                    vVolume.Add(CellDouble(oRow, "vol"));
                }
            }

            aOpen = vOpen.ToArray();
            aHigh = vHigh.ToArray();
            aLow = vLow.ToArray();
            aClose = vClose.ToArray();
            aVolume = vVolume.ToArray();
            return aLabels.Count;
        }

        public double[] GetDailyRevenueSparkline(int aDays)
        {
            if (aDays <= 0)
                aDays = 30;
            List<double> vResult = new List<double>();
            using (TReportsQuery oQuery = NewQuery("SELECT o.order_date AS d, " +
                "ROUND(SUM(ol.line_total), 0) AS revenue FROM orders o " +
                "JOIN order_lines ol ON ol.order_id = o.id " +
                "WHERE o.status <> 'Cancelled' AND o.order_date >= :d " +
                "GROUP BY o.order_date ORDER BY d"))
            {
                oQuery.SetParamStr("d", ReportsDBHelpers.FormatReportsDate(
                    DateTime.Today.AddDays(-aDays)));
                oQuery.Open();
                DataTable oTable = oQuery.DataSet;
                for (int vI = 0; vI < oTable.Rows.Count; vI++)
                    vResult.Add(CellDouble(oTable.Rows[vI], "revenue"));
            }
            return vResult.ToArray();
        }

        // ==================================================================== //
        //  drilldown                                                           //
        // ==================================================================== //

        /// <summary>
        /// aDim is 'region' | 'category' | 'salesperson' | 'customer'. Returns an
        /// open query shaped for the TreeGrid (id / parent_id / label / measures).
        /// </summary>
        public TReportsQuery NewDrilldownQuery(string aDim, long aId)
        {
            // aDim picks one of four fixed statements. It is never interpolated, so
            // a hostile dimension name is simply not one of the four.
            string vSQL;
            if (string.Equals(aDim, "region", StringComparison.OrdinalIgnoreCase))
                vSQL = "SELECT 'c' || c.id AS node_id, '' AS parent_id, " +
                    "c.name AS label, c.segment AS detail, " +
                    "COUNT(DISTINCT o.id) AS orders, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue FROM customers c " +
                    "JOIN orders o ON o.customer_id = c.id " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE c.region_id = :i AND o.status <> 'Cancelled' " +
                    "GROUP BY c.id, c.name, c.segment ORDER BY revenue DESC LIMIT 200";
            else if (string.Equals(aDim, "category", StringComparison.OrdinalIgnoreCase))
                vSQL = "SELECT 'p' || p.id AS node_id, '' AS parent_id, " +
                    "p.name AS label, p.sku AS detail, SUM(ol.qty) AS orders, " +
                    "ROUND(SUM(ol.line_total), 2) AS revenue FROM products p " +
                    "JOIN order_lines ol ON ol.product_id = p.id " +
                    "JOIN orders o ON o.id = ol.order_id " +
                    "WHERE p.category_id = :i AND o.status <> 'Cancelled' " +
                    "GROUP BY p.id, p.name, p.sku ORDER BY revenue DESC LIMIT 200";
            else if (string.Equals(aDim, "salesperson", StringComparison.OrdinalIgnoreCase))
                vSQL = "SELECT 'o' || o.id AS node_id, '' AS parent_id, " +
                    "c.name AS label, o.order_date AS detail, " +
                    "COUNT(ol.id) AS orders, ROUND(SUM(ol.line_total), 2) AS revenue " +
                    "FROM orders o JOIN customers c ON c.id = o.customer_id " +
                    "JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.salesperson_id = :i AND o.status <> 'Cancelled' " +
                    "GROUP BY o.id, c.name, o.order_date " +
                    "ORDER BY o.order_date DESC LIMIT 200";
            else
                vSQL = "SELECT 'o' || o.id AS node_id, '' AS parent_id, " +
                    "'Order ' || o.id AS label, o.order_date AS detail, " +
                    "COUNT(ol.id) AS orders, ROUND(SUM(ol.line_total), 2) AS revenue " +
                    "FROM orders o JOIN order_lines ol ON ol.order_id = o.id " +
                    "WHERE o.customer_id = :i GROUP BY o.id, o.order_date " +
                    "ORDER BY o.order_date DESC LIMIT 200";

            TReportsQuery oQuery = NewQuery(vSQL);
            try
            {
                oQuery.SetParamInt("i", aId);
                oQuery.Open();
                return oQuery;
            }
            catch (Exception)
            {
                oQuery.Dispose();
                throw;
            }
        }

        public string DrilldownTitle(string aDim, long aId)
        {
            string vSQL;
            if (string.Equals(aDim, "region", StringComparison.OrdinalIgnoreCase))
                vSQL = "SELECT name FROM regions WHERE id = :i";
            else if (string.Equals(aDim, "category", StringComparison.OrdinalIgnoreCase))
                vSQL = "SELECT name FROM categories WHERE id = :i";
            else if (string.Equals(aDim, "salesperson", StringComparison.OrdinalIgnoreCase))
                vSQL = "SELECT name FROM salespeople WHERE id = :i";
            else
                vSQL = "SELECT name FROM customers WHERE id = :i";

            using (TReportsQuery oQuery = NewQuery(vSQL))
            {
                oQuery.SetParamInt("i", aId);
                oQuery.Open();
                if (oQuery.DataSet.Rows.Count == 0)
                    return "";
                return Convert.ToString(oQuery.DataSet.Rows[0][0],
                    CultureInfo.InvariantCulture) ?? "";
            }
        }
    }
}
