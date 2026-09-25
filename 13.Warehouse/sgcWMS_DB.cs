// ***************************************************************************
//  sgcWMS - warehouse management web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\13.Warehouse\sgcWMS_DB.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
//  Data layer. There is NO REST tier in this demo on purpose: every page binds
//  a live query straight into a TsgcHTMLComponent_* through LoadFromDataSet.
//  TWMSDataSet below is the whole plumbing - it runs the SQL on a pooled
//  connection into a System.Data.DataTable, carries the SQL text (so /sql can
//  print it next to the rendered component) and returns the connection to the
//  pool the moment the rows are read.
//
//  FireDAC mapping:
//    TFDConnection            -> Microsoft.Data.Sqlite.SqliteConnection
//    TFDQuery                 -> SqliteCommand + SqliteDataReader -> DataTable
//    FDManager.AddConnectionDef / DeleteConnectionDef (RegisterDefinition /
//    UnregisterDefinition / FDefName)
//                             -> one connection string built in the
//                                constructor: the ADO.NET provider pools the
//                                underlying connections itself, keyed by that
//                                string, so the per-instance definition name
//                                that kept two servers in the same process
//                                apart is no longer needed.
//    TDataSet cursor (Eof / First / Next) -> a row index over DataTable.Rows.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace WMS
{
    /// <summary>WMS demo data-layer error. Mirrors Delphi EWMSDBError.</summary>
    public class EWMSDBError : Exception
    {
        public EWMSDBError(string message) : base(message) { }
    }

    // Delphi unit-level functions of sgcWMS_DB.
    public static class WMSTimestamp
    {
        // Parse the 'yyyy-mm-ddThh:nn:ss' timestamps this unit stores. Fixed
        // position, never a locale-aware parse (which honours the system
        // settings and fails silently outside US date formats).
        public static DateTime ParseWMSTimestamp(string aValue)
        {
            string vText = (aValue == null ? "" : aValue.Trim());
            if (vText.Length < 19)
                return DateTime.MinValue;
            int vYear = PartInt(vText, 1, 4);
            int vMonth = PartInt(vText, 6, 2);
            int vDay = PartInt(vText, 9, 2);
            int vHour = PartInt(vText, 12, 2);
            int vMin = PartInt(vText, 15, 2);
            int vSec = PartInt(vText, 18, 2);
            if ((vYear < 1) || (vMonth < 1) || (vMonth > 12) || (vDay < 1) ||
                (vHour < 0) || (vHour > 23) || (vMin < 0) || (vMin > 59) ||
                (vSec < 0) || (vSec > 59))
                return DateTime.MinValue;
            try
            {
                return new DateTime(vYear, vMonth, vDay, vHour, vMin, vSec);
            }
            catch (ArgumentOutOfRangeException)
            {
                // TryEncodeDateTime returning False (e.g. 31 February).
                return DateTime.MinValue;
            }
        }

        public static string FormatWMSTimestamp(DateTime aValue)
        {
            return aValue.ToString("yyyy-MM-dd'T'HH:mm:ss",
                CultureInfo.InvariantCulture);
        }

        public static string NowTimestamp()
        {
            return FormatWMSTimestamp(DateTime.Now);
        }

        // Delphi StrToIntDef(Copy(aText, aStart, aLen), -1); aStart is 1-based.
        private static int PartInt(string aText, int aStart, int aLen)
        {
            if ((aStart - 1) + aLen > aText.Length)
                return -1;
            int vValue;
            if (int.TryParse(aText.Substring(aStart - 1, aLen),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out vValue))
                return vValue;
            return -1;
        }
    }

    /// <summary>
    /// Owns a pooled connection plus one statement. The caller MUST dispose it.
    ///
    /// This is the object every page receives and feeds to LoadFromDataSet. SQL
    /// exposes the exact statement that produced the rows, which is what the
    /// /sql page prints beside the live component.
    ///
    /// Unlike the Delphi TFDQuery, the rows are read into a DataTable inside
    /// Open() and the connection goes back to the pool right away, so a page
    /// that holds the dataset while it renders never pins a pooled connection.
    /// </summary>
    public class TWMSDataSet : IDisposable
    {
        private SqliteConnection FConn;
        private DataTable FDataSet;
        private readonly string FSQL;
        private readonly Dictionary<string, object> FParams =
            new Dictionary<string, object>(StringComparer.Ordinal);
        private int FRow;

        public TWMSDataSet(SqliteConnection aConn, string aSQL)
        {
            FConn = aConn;
            FSQL = aSQL;
            FDataSet = null;
            FRow = 0;
        }

        public void Dispose()
        {
            // Defensive: Open() already released the connection. This only bites
            // when the caller built the dataset and never opened it.
            ReleaseConnection();
        }

        /// <summary>The rows, null until Open().</summary>
        public DataTable DataSet
        {
            get { return FDataSet; }
        }

        /// <summary>The exact statement that produced the rows.</summary>
        public string SQL
        {
            get { return FSQL; }
        }

        // Delphi ParamByName(aName).AsLargeInt / AsString / AsFloat. The values
        // are held until Open() binds them onto the command.
        public void SetInt(string aName, long aValue)
        {
            FParams[ParamName(aName)] = aValue;
        }

        public void SetStr(string aName, string aValue)
        {
            FParams[ParamName(aName)] = (aValue == null ? "" : aValue);
        }

        public void SetFloat(string aName, double aValue)
        {
            FParams[ParamName(aName)] = aValue;
        }

        public void Open()
        {
            if (FDataSet != null)
                return;
            if (FConn == null)
                throw new EWMSDBError("The dataset has no connection to open.");
            try
            {
                using (SqliteCommand oCmd = FConn.CreateCommand())
                {
                    oCmd.CommandText = FSQL;
                    foreach (KeyValuePair<string, object> oParam in FParams)
                        oCmd.Parameters.AddWithValue(oParam.Key,
                            oParam.Value == null ? DBNull.Value : oParam.Value);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        FDataSet = ReadTable(oReader);
                }
                FRow = 0;
            }
            finally
            {
                ReleaseConnection();
            }
        }

        public bool IsEmpty()
        {
            return (FDataSet == null) || (FDataSet.Rows.Count == 0);
        }

        public int RecordCount()
        {
            if (IsEmpty())
                return 0;
            return FDataSet.Rows.Count;
        }

        // --- field values of the CURRENT row, blank-safe (never throw) --- //

        public string AsStr(string aField)
        {
            return ValueToStr(CurrentValue(aField));
        }

        public long AsInt(string aField)
        {
            return ValueToInt64(CurrentValue(aField));
        }

        public double AsFloat(string aField)
        {
            return ValueToFloat(CurrentValue(aField));
        }

        // --- row cursor, replacing the Delphi TDataSet cursor --- //

        public bool Eof
        {
            get { return (FDataSet == null) || (FRow >= FDataSet.Rows.Count); }
        }

        public void First()
        {
            FRow = 0;
        }

        public void Next()
        {
            FRow++;
        }

        // --- internals --- //

        private void ReleaseConnection()
        {
            if (FConn == null)
                return;
            try
            {
                FConn.Dispose();
            }
            catch
            {
            }
            FConn = null;
        }

        // Delphi params are written without the ':' prefix (ParamByName('q'));
        // Microsoft.Data.Sqlite binds them by their SQL spelling, so keep the
        // ':' the statements use.
        private static string ParamName(string aName)
        {
            if (string.IsNullOrEmpty(aName))
                return aName;
            if ((aName[0] == ':') || (aName[0] == '@') || (aName[0] == '$'))
                return aName;
            return ":" + aName;
        }

        private object CurrentValue(string aField)
        {
            if ((FDataSet == null) || (FRow < 0) || (FRow >= FDataSet.Rows.Count))
                return null;
            if (!FDataSet.Columns.Contains(aField))
                return null;
            return FDataSet.Rows[FRow][aField];
        }

        internal static string ValueToStr(object aValue)
        {
            if ((aValue == null) || (aValue == DBNull.Value))
                return "";
            if (aValue is string)
                return (string)aValue;
            if (aValue is byte[])
                return Convert.ToBase64String((byte[])aValue);
            try
            {
                return Convert.ToString(aValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                return "";
            }
        }

        internal static long ValueToInt64(object aValue)
        {
            if ((aValue == null) || (aValue == DBNull.Value))
                return 0;
            try
            {
                // A float field answers AsLargeInt with Trunc() in Delphi.
                if (aValue is double)
                    return (long)Math.Truncate((double)aValue);
                if (aValue is float)
                    return (long)Math.Truncate((float)aValue);
                if (aValue is decimal)
                    return (long)Math.Truncate((decimal)aValue);
                return Convert.ToInt64(aValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        internal static double ValueToFloat(object aValue)
        {
            if ((aValue == null) || (aValue == DBNull.Value))
                return 0;
            try
            {
                return Convert.ToDouble(aValue, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
        }

        // Read the whole reader into a DataTable. The column types come from the
        // values SQLite hands back (INTEGER -> long, REAL -> double, TEXT ->
        // string), so the numeric columns still right-align in the Grid.
        private static DataTable ReadTable(SqliteDataReader aReader)
        {
            DataTable oTable = new DataTable();
            oTable.Locale = CultureInfo.InvariantCulture;

            int vCount = aReader.FieldCount;
            string[] vNames = new string[vCount];
            bool[] vHasInt = new bool[vCount];
            bool[] vHasFloat = new bool[vCount];
            bool[] vHasText = new bool[vCount];
            bool[] vHasOther = new bool[vCount];
            for (int vI = 0; vI < vCount; vI++)
                vNames[vI] = aReader.GetName(vI);

            List<object[]> oRows = new List<object[]>();
            while (aReader.Read())
            {
                object[] vRow = new object[vCount];
                for (int vI = 0; vI < vCount; vI++)
                {
                    object vValue = aReader.IsDBNull(vI)
                        ? DBNull.Value : aReader.GetValue(vI);
                    vRow[vI] = vValue;
                    if (vValue == DBNull.Value)
                        continue;
                    if ((vValue is long) || (vValue is int) || (vValue is short) ||
                        (vValue is byte) || (vValue is bool))
                        vHasInt[vI] = true;
                    else if ((vValue is double) || (vValue is float) ||
                        (vValue is decimal))
                        vHasFloat[vI] = true;
                    else if (vValue is string)
                        vHasText[vI] = true;
                    else
                        vHasOther[vI] = true;
                }
                oRows.Add(vRow);
            }

            Type[] vTypes = new Type[vCount];
            for (int vI = 0; vI < vCount; vI++)
            {
                if (vHasOther[vI])
                    vTypes[vI] = typeof(object);
                else if (vHasText[vI])
                    vTypes[vI] = typeof(string);
                else if (vHasFloat[vI])
                    vTypes[vI] = typeof(double);
                else if (vHasInt[vI])
                    vTypes[vI] = typeof(long);
                else
                    vTypes[vI] = typeof(string);

                string vName = vNames[vI];
                if (string.IsNullOrEmpty(vName))
                    vName = "Column" + (vI + 1).ToString(CultureInfo.InvariantCulture);
                // A DataTable rejects duplicate column names where the Delphi
                // TDataSet renames them; keep the first and suffix the rest.
                if (oTable.Columns.Contains(vName))
                    vName = vName + "_" + vI.ToString(CultureInfo.InvariantCulture);
                vNames[vI] = vName;
                oTable.Columns.Add(vName, vTypes[vI]);
            }

            for (int vR = 0; vR < oRows.Count; vR++)
            {
                object[] vRow = oRows[vR];
                object[] vOut = new object[vCount];
                for (int vI = 0; vI < vCount; vI++)
                {
                    object vValue = vRow[vI];
                    if (vValue == DBNull.Value)
                    {
                        vOut[vI] = DBNull.Value;
                        continue;
                    }
                    if (vTypes[vI] == typeof(string))
                        vOut[vI] = ValueToStr(vValue);
                    else if (vTypes[vI] == typeof(double))
                        vOut[vI] = ValueToFloat(vValue);
                    else if (vTypes[vI] == typeof(long))
                        vOut[vI] = ValueToInt64(vValue);
                    else
                        vOut[vI] = vValue;
                }
                oTable.Rows.Add(vOut);
            }
            oTable.AcceptChanges();
            return oTable;
        }
    }

    // Tiny deterministic LCG. Seeding must produce the same warehouse on every
    // machine so screenshots and the docs stay in step. Ported as a class (the
    // Delphi record is only ever used as a local), with 32-bit unsigned
    // wraparound so the sequence matches the Delphi one exactly.
    internal sealed class TWMSRandom
    {
        public uint State;

        public void Init(uint aSeed)
        {
            State = aSeed;
        }

        public int Next(int aMax)
        {
            unchecked
            {
                State = (State * 1103515245u) + 12345u;
            }
            if (aMax <= 0)
                return 0;
            return (int)((State >> 16) & 0x7FFF) % aMax;
        }

        public int Range(int aMin, int aMax)
        {
            if (aMax <= aMin)
                return aMin;
            return aMin + Next(aMax - aMin + 1);
        }
    }

    /// <summary>
    /// Pooled SQLite access. Microsoft.Data.Sqlite pools the connections by
    /// connection string, so the per-instance FireDAC connection definition
    /// name that kept two servers in the same process apart collapses into the
    /// connection string built once in the constructor.
    /// </summary>
    public class TWMSDBPool : IDisposable
    {
        private readonly string FDatabaseFile;
        private readonly string FConnStr;
        private readonly object FSeedLock = new object();

        public TWMSDBPool(string aDatabaseFile)
        {
            string vFile = aDatabaseFile;
            if (string.IsNullOrEmpty(vFile))
                vFile = "data\\wms.db";
            if (!Path.IsPathRooted(vFile))
                vFile = Path.Combine(Directory.GetCurrentDirectory(), vFile);
            FDatabaseFile = vFile;
            EnsureDatabaseDir();
            FConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = FDatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = true
            }.ToString();
        }

        public void Dispose()
        {
            // Nothing to unregister: there is no FDManager connection definition.
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

        // Acquire a pooled connection. The caller MUST dispose it, which hands
        // the underlying connection back to the pool.
        public SqliteConnection Acquire()
        {
            SqliteConnection oConn = new SqliteConnection(FConnStr);
            try
            {
                oConn.Open();
                return oConn;
            }
            catch (Exception E)
            {
                oConn.Dispose();
                throw new EWMSDBError(string.Format(CultureInfo.InvariantCulture,
                    "Failed to open the SQLite database \"{0}\": {1}",
                    FDatabaseFile, E.Message));
            }
        }

        // --- generic query plumbing (no REST tier) --- //

        // Build an UNOPENED TWMSDataSet for aSQL. Set the params, then Open.
        public TWMSDataSet Query(string aSQL)
        {
            SqliteConnection oConn = Acquire();
            try
            {
                return new TWMSDataSet(oConn, aSQL);
            }
            catch
            {
                oConn.Dispose();
                throw;
            }
        }

        // Build and Open in one call (for parameterless statements).
        public TWMSDataSet OpenSQL(string aSQL)
        {
            TWMSDataSet oResult = Query(aSQL);
            try
            {
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public long ScalarInt(string aSQL)
        {
            long vResult = 0;
            using (TWMSDataSet oData = OpenSQL(aSQL))
            {
                if (!oData.IsEmpty())
                    vResult = TWMSDataSet.ValueToInt64(oData.DataSet.Rows[0][0]);
            }
            return vResult;
        }

        public double ScalarFloat(string aSQL)
        {
            double vResult = 0;
            using (TWMSDataSet oData = OpenSQL(aSQL))
            {
                if (!oData.IsEmpty())
                    vResult = TWMSDataSet.ValueToFloat(oData.DataSet.Rows[0][0]);
            }
            return vResult;
        }

        // --- low-level command helpers (the TFDQuery plumbing) --- //

        private static SqliteCommand NewCmd(SqliteConnection aConn, string aSQL)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.CommandText = aSQL;
            return oCmd;
        }

        private static SqliteCommand NewCmd(SqliteConnection aConn,
            SqliteTransaction aTx, string aSQL)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.Transaction = aTx;
            oCmd.CommandText = aSQL;
            return oCmd;
        }

        // Delphi ParamByName(aName).AsXXX := aValue on a reused query.
        private static void SetP(SqliteCommand aCmd, string aName, object aValue)
        {
            object vValue = (aValue == null ? DBNull.Value : aValue);
            int vIndex = aCmd.Parameters.IndexOf(aName);
            if (vIndex >= 0)
                aCmd.Parameters[vIndex].Value = vValue;
            else
                aCmd.Parameters.AddWithValue(aName, vValue);
        }

        // Delphi TFDConnection.ExecSQL.
        private static int ExecSQL(SqliteConnection aConn, string aSQL)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
                return oCmd.ExecuteNonQuery();
        }

        private static int ExecSQL(SqliteConnection aConn, SqliteTransaction aTx,
            string aSQL)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aTx, aSQL))
                return oCmd.ExecuteNonQuery();
        }

        // Run a scalar SELECT on aConn. Seeding runs inside a transaction, so it
        // must never reach for a second pooled connection: SQLite would answer
        // 'database table is locked' the moment the two collide.
        private static long ScalarOn(SqliteConnection aConn, string aSQL)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
            {
                object vValue = oCmd.ExecuteScalar();
                return TWMSDataSet.ValueToInt64(vValue);
            }
        }

        private static long ScalarOn(SqliteConnection aConn, SqliteTransaction aTx,
            string aSQL)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aTx, aSQL))
            {
                object vValue = oCmd.ExecuteScalar();
                return TWMSDataSet.ValueToInt64(vValue);
            }
        }

        // The rowid of the most recently inserted row on this connection.
        private static long LastInsertRowId(SqliteConnection aConn)
        {
            return ScalarOn(aConn, "SELECT last_insert_rowid()");
        }

        private static long LastInsertRowId(SqliteConnection aConn,
            SqliteTransaction aTx)
        {
            return ScalarOn(aConn, aTx, "SELECT last_insert_rowid()");
        }

        // Delphi QuotedStr: wrap in single quotes, doubling the inner ones.
        private static string QuotedStr(string aValue)
        {
            return "'" + (aValue == null ? "" : aValue).Replace("'", "''") + "'";
        }

        private static string IntToStr(long aValue)
        {
            return aValue.ToString(CultureInfo.InvariantCulture);
        }

        // Escape LIKE metacharacters before wrapping a user term in '%...%'.
        // Every call site pairs this with an explicit ESCAPE '\' clause.
        private static string EscapeLikeValue(string aValue)
        {
            string vResult = (aValue == null ? "" : aValue);
            vResult = vResult.Replace("\\", "\\\\");
            vResult = vResult.Replace("%", "\\%");
            vResult = vResult.Replace("_", "\\_");
            return vResult;
        }

        // Whitelist aDir to ASC/DESC.
        private static string DirSQL(string aDir)
        {
            if (string.Equals((aDir == null ? "" : aDir).Trim(), "desc",
                StringComparison.OrdinalIgnoreCase))
                return "DESC";
            return "ASC";
        }

        private static bool SameText(string aLeft, string aRight)
        {
            return string.Equals(aLeft == null ? "" : aLeft,
                aRight == null ? "" : aRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string Trim(string aValue)
        {
            return (aValue == null ? "" : aValue).Trim();
        }

        // Whitelist of the /products sortable columns.
        private static string ProductSortSQL(string aSort)
        {
            if (SameText(aSort, "name"))
                return "p.name";
            else if (SameText(aSort, "category"))
                return "p.category";
            else if (SameText(aSort, "cost"))
                return "p.unit_cost";
            else if (SameText(aSort, "onhand"))
                return "onhand";
            else if (SameText(aSort, "barcode"))
                return "p.barcode";
            else
                return "p.sku";
        }

        // Whitelist of the /stock sortable columns.
        private static string StockSortSQL(string aSort)
        {
            if (SameText(aSort, "name"))
                return "p.name";
            else if (SameText(aSort, "location"))
                return "l.code";
            else if (SameText(aSort, "qty"))
                return "s.qty";
            else if (SameText(aSort, "value"))
                return "value";
            else if (SameText(aSort, "zone"))
                return "l.zone";
            else
                return "p.sku";
        }

        // Whitelist of the /stock/movements sortable columns.
        private static string MovementSortSQL(string aSort)
        {
            if (SameText(aSort, "sku"))
                return "p.sku";
            else if (SameText(aSort, "kind"))
                return "m.kind";
            else if (SameText(aSort, "qty"))
                return "m.qty";
            else if (SameText(aSort, "reference"))
                return "m.reference";
            else
                return "m.created_at";
        }

        private static void FillUserFromQuery(TWMSDataSet aData, TWMSUser aUser)
        {
            aUser.Id = aData.AsInt("id");
            aUser.Username = aData.AsStr("username");
            aUser.PasswordHash = aData.AsStr("password_hash");
            aUser.Role = aData.AsStr("role");
            aUser.DisplayName = aData.AsStr("display_name");
            aUser.CreatedAt = WMSTimestamp.ParseWMSTimestamp(
                aData.AsStr("created_at"));
        }

        private static void FillProductFromQuery(TWMSDataSet aData,
            TWMSProduct aProduct)
        {
            aProduct.Id = aData.AsInt("id");
            aProduct.SKU = aData.AsStr("sku");
            aProduct.Barcode = aData.AsStr("barcode");
            aProduct.Name = aData.AsStr("name");
            aProduct.Description = aData.AsStr("description");
            aProduct.UOM = aData.AsStr("uom");
            aProduct.UnitCost = aData.AsFloat("unit_cost");
            aProduct.MinStock = (int)aData.AsInt("min_stock");
            aProduct.Category = aData.AsStr("category");
        }

        // ----- schema ----- //

        private static readonly string[] CS_TABLES = new string[]
        {
            "CREATE TABLE IF NOT EXISTS users (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT UNIQUE, " +
            "password_hash TEXT, role TEXT, display_name TEXT, created_at TEXT)",

            "CREATE TABLE IF NOT EXISTS passkeys (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, " +
            "credential_id TEXT, public_key TEXT, device_name TEXT, " +
            "sign_count INTEGER, created_at TEXT, last_used_at TEXT)",

            "CREATE TABLE IF NOT EXISTS products (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, sku TEXT, barcode TEXT, " +
            "name TEXT, description TEXT, uom TEXT, unit_cost REAL, " +
            "min_stock INTEGER, category TEXT)",

            "CREATE TABLE IF NOT EXISTS locations (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, code TEXT, parent_id INTEGER, " +
            "zone TEXT, aisle TEXT, rack TEXT, bin TEXT, kind TEXT, " +
            "capacity INTEGER)",

            "CREATE TABLE IF NOT EXISTS suppliers (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, contact TEXT, " +
            "email TEXT, phone TEXT)",

            "CREATE TABLE IF NOT EXISTS customers (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, address TEXT, " +
            "city TEXT, country TEXT, contact TEXT)",

            "CREATE TABLE IF NOT EXISTS purchase_orders (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, supplier_id INTEGER, " +
            "reference TEXT, status TEXT, expected_at TEXT, created_at TEXT)",

            "CREATE TABLE IF NOT EXISTS purchase_order_lines (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, po_id INTEGER, " +
            "product_id INTEGER, qty_ordered INTEGER, qty_received INTEGER)",

            "CREATE TABLE IF NOT EXISTS sales_orders (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER, " +
            "reference TEXT, status TEXT, priority TEXT, created_at TEXT, " +
            "shipped_at TEXT)",

            "CREATE TABLE IF NOT EXISTS sales_order_lines (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, so_id INTEGER, " +
            "product_id INTEGER, qty_ordered INTEGER, qty_picked INTEGER)",

            "CREATE TABLE IF NOT EXISTS stock (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER, " +
            "location_id INTEGER, qty INTEGER)",

            "CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_prod_loc " +
            "ON stock (product_id, location_id)",

            "CREATE TABLE IF NOT EXISTS movements (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER, " +
            "from_location_id INTEGER, to_location_id INTEGER, qty INTEGER, " +
            "kind TEXT, user_id INTEGER, reference TEXT, created_at TEXT)",

            "CREATE TABLE IF NOT EXISTS stock_counts (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, reference TEXT, status TEXT, " +
            "created_at TEXT, closed_at TEXT)",

            "CREATE TABLE IF NOT EXISTS stock_count_lines (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, count_id INTEGER, " +
            "product_id INTEGER, location_id INTEGER, qty_expected INTEGER, " +
            "qty_counted INTEGER)",

            "CREATE TABLE IF NOT EXISTS audit_log (" +
            "id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, action TEXT, " +
            "entity TEXT, entity_id INTEGER, detail TEXT, ip TEXT, created_at TEXT)",

            "CREATE INDEX IF NOT EXISTS ix_mov_created ON movements (created_at)",
            "CREATE INDEX IF NOT EXISTS ix_mov_product ON movements (product_id)",
            "CREATE INDEX IF NOT EXISTS ix_stock_loc ON stock (location_id)",
            "CREATE INDEX IF NOT EXISTS ix_sol_so ON sales_order_lines (so_id)",
            "CREATE INDEX IF NOT EXISTS ix_pol_po ON purchase_order_lines (po_id)"
        };

        public void EnsureSchema()
        {
            using (SqliteConnection oConn = Acquire())
            {
                for (int vI = 0; vI < CS_TABLES.Length; vI++)
                    ExecSQL(oConn, CS_TABLES[vI]);
            }
        }

        public void SeedAdmin(string aUser, string aPasswordHash)
        {
            using (SqliteConnection oConn = Acquire())
            {
                long vCount = ScalarOn(oConn,
                    "SELECT COUNT(*) FROM users WHERE role = 'admin'");
                if (vCount > 0)
                    return;
                using (SqliteCommand oQuery = NewCmd(oConn, "INSERT INTO users " +
                    "(username, password_hash, role, display_name, created_at) " +
                    "VALUES (:u, :p, :r, :d, :c)"))
                {
                    SetP(oQuery, ":u", aUser);
                    SetP(oQuery, ":p", aPasswordHash);
                    SetP(oQuery, ":r", WMSConst.CS_ROLE_ADMIN);
                    SetP(oQuery, ":d", "Warehouse Manager");
                    SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                    oQuery.ExecuteNonQuery();
                }
            }
        }

        // ----- seeding ----- //

        // EAN-13 check digit over the first 12 digits.
        private static string EAN13(string aBase12)
        {
            int vSum = 0;
            for (int vI = 1; vI <= 12; vI++)
            {
                int vDigit = 0;
                if (vI <= aBase12.Length)
                {
                    int vParsed;
                    if (int.TryParse(aBase12.Substring(vI - 1, 1),
                        NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out vParsed))
                        vDigit = vParsed;
                }
                if ((vI % 2) == 0)
                    vSum = vSum + (vDigit * 3);
                else
                    vSum = vSum + vDigit;
            }
            int vCheck = (10 - (vSum % 10)) % 10;
            return aBase12 + IntToStr(vCheck);
        }

        // Add aDelta to a stock row, creating the row when the product has never
        // been in that bin. Written as UPDATE-then-INSERT rather than as an
        // UPSERT because the SQLite engine FireDAC links in the Delphi demo
        // rejects ON CONFLICT ... DO UPDATE ('near "ON": syntax error'), and an
        // UPSERT that only works on newer engines is not something a demo should
        // depend on.
        private static void StockAdd(SqliteConnection aConn, SqliteTransaction aTx,
            long aProductId, long aLocationId, int aDelta)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "UPDATE stock SET qty = qty + :q " +
                "WHERE product_id = :p AND location_id = :l"))
            {
                SetP(oCmd, ":q", aDelta);
                SetP(oCmd, ":p", aProductId);
                SetP(oCmd, ":l", aLocationId);
                if (oCmd.ExecuteNonQuery() > 0)
                    return;
            }
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "INSERT INTO stock (product_id, location_id, qty) " +
                "VALUES (:p, :l, :q)"))
            {
                SetP(oCmd, ":p", aProductId);
                SetP(oCmd, ":l", aLocationId);
                SetP(oCmd, ":q", aDelta);
                oCmd.ExecuteNonQuery();
            }
        }

        // Set a stock row to an absolute quantity, creating it when absent.
        private static void StockSet(SqliteConnection aConn, SqliteTransaction aTx,
            long aProductId, long aLocationId, int aQty)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "UPDATE stock SET qty = :q " +
                "WHERE product_id = :p AND location_id = :l"))
            {
                SetP(oCmd, ":q", aQty);
                SetP(oCmd, ":p", aProductId);
                SetP(oCmd, ":l", aLocationId);
                if (oCmd.ExecuteNonQuery() > 0)
                    return;
            }
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "INSERT INTO stock (product_id, location_id, qty) " +
                "VALUES (:p, :l, :q)"))
            {
                SetP(oCmd, ":p", aProductId);
                SetP(oCmd, ":l", aLocationId);
                SetP(oCmd, ":q", aQty);
                oCmd.ExecuteNonQuery();
            }
        }

        private static readonly string[] CS_CATEGORY = new string[]
        {
            "Fasteners", "Bearings", "Seals", "Hydraulics", "Electrical",
            "Hand Tools", "Safety", "Packaging", "Lubricants", "Filters"
        };

        private static readonly string[,] CS_BASE = new string[,]
        {
            { "Hex Bolt", "Socket Screw", "Washer", "Hex Nut", "Threaded Rod",
              "Anchor Bolt" },
            { "Deep Groove Bearing", "Tapered Roller Bearing", "Needle Bearing",
              "Thrust Bearing", "Pillow Block", "Linear Bushing" },
            { "O-Ring", "Shaft Seal", "Gasket Sheet", "V-Ring", "Lip Seal",
              "Flange Gasket" },
            { "Hydraulic Hose", "Quick Coupler", "Gear Pump", "Control Valve",
              "Cylinder Seal Kit", "Pressure Gauge" },
            { "Contactor", "Circuit Breaker", "Terminal Block", "Cable Gland",
              "Signal Relay", "Motor Starter" },
            { "Torque Wrench", "Socket Set", "Circlip Pliers", "Rubber Mallet",
              "Feeler Gauge", "Bearing Puller" },
            { "Safety Glasses", "Nitrile Gloves", "Ear Defenders", "Hard Hat",
              "Hi-Vis Vest", "Safety Boots" },
            { "Carton", "Stretch Wrap", "Pallet", "Strapping Band", "Bubble Wrap",
              "Shipping Label" },
            { "Gear Oil", "Grease Cartridge", "Chain Lubricant", "Cutting Fluid",
              "Penetrating Spray", "Hydraulic Fluid" },
            { "Air Filter", "Oil Filter", "Return Line Filter", "Breather Cap",
              "Water Separator", "Filter Element" }
        };

        private static readonly string[] CS_VARIANT = new string[]
        {
            "Standard", "Heavy Duty", "Stainless"
        };

        private static readonly string[] CS_UOM = new string[]
        {
            "EA", "BOX", "PK", "KG", "M", "L"
        };

        private static readonly string[] CS_ZONE_NAME = new string[]
        {
            "A", "B", "C", "D"
        };

        private static readonly string[,] CS_SUPPLIER = new string[,]
        {
            { "Vorne Industrieteile GmbH", "Klaus Berger",
              "sales@vorne-industrie.example", "+49 211 555 0142" },
            { "Atlantic Bearing Supply", "Marie Lefevre",
              "orders@atlanticbearing.example", "+33 1 55 55 0198" },
            { "Iberia Sealing S.L.", "Nuria Campos",
              "ventas@iberiasealing.example", "+34 91 555 0177" },
            { "Northgate Hydraulics Ltd", "Alan Whitfield",
              "purchasing@northgatehyd.example", "+44 161 555 0123" },
            { "Baltic Safety OU", "Kristjan Saar", "info@balticsafety.example",
              "+372 555 0166" },
            { "Meridian Packaging BV", "Sanne de Vries",
              "sales@meridianpack.example", "+31 20 555 0155" }
        };

        private static readonly string[,] CS_CUSTOMER = new string[,]
        {
            { "Delta Machinery Works", "Hafenstrasse 14", "Hamburg", "Germany",
              "Jonas Krueger" },
            { "Rivera Agro Services", "Camino del Norte 8", "Valencia", "Spain",
              "Pilar Rivera" },
            { "Northline Rail Maintenance", "Depot Road 3", "Leeds",
              "United Kingdom", "Gary Thompson" },
            { "Ateliers Montmartre", "Rue des Forges 22", "Lyon", "France",
              "Camille Roux" },
            { "Van Dijk Logistiek", "Havenweg 91", "Rotterdam", "Netherlands",
              "Bram van Dijk" },
            { "Nordvik Marine AS", "Kaigata 5", "Bergen", "Norway",
              "Ingrid Nordvik" },
            { "Trentino Impianti SRL", "Via Industria 40", "Verona", "Italy",
              "Luca Trentino" },
            { "Baltica Energia", "Portowa 17", "Gdansk", "Poland",
              "Marek Kowalski" },
            { "Helvetia Precision AG", "Werkstrasse 6", "Winterthur",
              "Switzerland", "Andrea Meier" },
            { "Douro Metalworks", "Rua do Cais 12", "Porto", "Portugal",
              "Rui Carvalho" },
            { "Kiel Cold Chain", "Speicherweg 2", "Kiel", "Germany",
              "Nina Hartmann" },
            { "Sligo Plant Hire", "Quay Street 30", "Sligo", "Ireland",
              "Declan Murphy" }
        };

        private void SeedMasterData(SqliteConnection aConn, SqliteTransaction aTx)
        {
            TWMSRandom vRnd = new TWMSRandom();
            vRnd.Init(20260822);

            // ---- products: 10 categories x 6 base names x 3 variants = 180 ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx, "INSERT INTO products " +
                "(sku, barcode, name, description, uom, unit_cost, min_stock, category) " +
                "VALUES (:sku, :bc, :nm, :ds, :um, :uc, :ms, :ct)"))
            {
                int vSeq = 0;
                for (int vCat = 0; vCat <= 9; vCat++)
                    for (int vBase = 0; vBase <= 5; vBase++)
                        for (int vVar = 0; vVar <= 2; vVar++)
                        {
                            vSeq++;
                            SetP(oIns, ":sku", string.Format(
                                CultureInfo.InvariantCulture, "NW-{0}-{1:D4}",
                                1000 + vCat, vSeq));
                            SetP(oIns, ":bc", EAN13("84" +
                                (4100000 + vSeq * 7).ToString("D10",
                                CultureInfo.InvariantCulture)));
                            SetP(oIns, ":nm", CS_BASE[vCat, vBase] + " " +
                                CS_VARIANT[vVar]);
                            SetP(oIns, ":ds", string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}, {1} finish. Supplied for the {2} range.",
                                CS_BASE[vCat, vBase],
                                CS_VARIANT[vVar].ToLowerInvariant(),
                                CS_CATEGORY[vCat].ToLowerInvariant()));
                            SetP(oIns, ":um", CS_UOM[vRnd.Next(6)]);
                            SetP(oIns, ":uc", vRnd.Range(150, 48000) / 100.0);
                            SetP(oIns, ":ms", vRnd.Range(5, 60));
                            SetP(oIns, ":ct", CS_CATEGORY[vCat]);
                            oIns.ExecuteNonQuery();
                        }
            }

            // ---- locations: 4 zones / 8 aisles / 16 racks / 64 bins ---- //
            using (SqliteCommand oAisle = NewCmd(aConn, aTx,
                "INSERT INTO locations " +
                "(code, parent_id, zone, aisle, rack, bin, kind, capacity) " +
                "VALUES (:c, :p, :z, :a, '', '', 'aisle', 0)"))
            using (SqliteCommand oRack = NewCmd(aConn, aTx,
                "INSERT INTO locations " +
                "(code, parent_id, zone, aisle, rack, bin, kind, capacity) " +
                "VALUES (:c, :p, :z, :a, :r, '', 'rack', 0)"))
            using (SqliteCommand oBin = NewCmd(aConn, aTx,
                "INSERT INTO locations " +
                "(code, parent_id, zone, aisle, rack, bin, kind, capacity) " +
                "VALUES (:c, :p, :z, :a, :r, :b, 'bin', :cap)"))
            {
                for (int vZ = 0; vZ <= 3; vZ++)
                {
                    string vCode = CS_ZONE_NAME[vZ];
                    ExecSQL(aConn, aTx, "INSERT INTO locations " +
                        "(code, parent_id, zone, aisle, rack, bin, kind, capacity) " +
                        "VALUES (" + QuotedStr(vCode) + ", 0, " + QuotedStr(vCode) +
                        ", '', '', '', 'zone', 0)");
                    long vZoneId = ScalarOn(aConn, aTx,
                        "SELECT last_insert_rowid()");
                    for (int vA = 1; vA <= 2; vA++)
                    {
                        vCode = string.Format(CultureInfo.InvariantCulture,
                            "{0}-{1:D2}", CS_ZONE_NAME[vZ], vA);
                        SetP(oAisle, ":c", vCode);
                        SetP(oAisle, ":p", vZoneId);
                        SetP(oAisle, ":z", CS_ZONE_NAME[vZ]);
                        SetP(oAisle, ":a", vA.ToString("D2",
                            CultureInfo.InvariantCulture));
                        oAisle.ExecuteNonQuery();
                        long vAisleId = ScalarOn(aConn, aTx,
                            "SELECT last_insert_rowid()");
                        for (int vR = 1; vR <= 2; vR++)
                        {
                            vCode = string.Format(CultureInfo.InvariantCulture,
                                "{0}-{1:D2}-{2}", CS_ZONE_NAME[vZ], vA, vR);
                            SetP(oRack, ":c", vCode);
                            SetP(oRack, ":p", vAisleId);
                            SetP(oRack, ":z", CS_ZONE_NAME[vZ]);
                            SetP(oRack, ":a", vA.ToString("D2",
                                CultureInfo.InvariantCulture));
                            SetP(oRack, ":r", IntToStr(vR));
                            oRack.ExecuteNonQuery();
                            long vRackId = ScalarOn(aConn, aTx,
                                "SELECT last_insert_rowid()");
                            for (int vB = 1; vB <= 4; vB++)
                            {
                                vCode = string.Format(CultureInfo.InvariantCulture,
                                    "{0}-{1:D2}-{2}-{3:D2}", CS_ZONE_NAME[vZ], vA,
                                    vR, vB);
                                SetP(oBin, ":c", vCode);
                                SetP(oBin, ":p", vRackId);
                                SetP(oBin, ":z", CS_ZONE_NAME[vZ]);
                                SetP(oBin, ":a", vA.ToString("D2",
                                    CultureInfo.InvariantCulture));
                                SetP(oBin, ":r", IntToStr(vR));
                                SetP(oBin, ":b", vB.ToString("D2",
                                    CultureInfo.InvariantCulture));
                                SetP(oBin, ":cap", vRnd.Range(400, 1200));
                                oBin.ExecuteNonQuery();
                            }
                        }
                    }
                }
            }

            // ---- suppliers ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx,
                "INSERT INTO suppliers (name, contact, email, phone) " +
                "VALUES (:n, :c, :e, :p)"))
            {
                for (int vI = 0; vI <= 5; vI++)
                {
                    SetP(oIns, ":n", CS_SUPPLIER[vI, 0]);
                    SetP(oIns, ":c", CS_SUPPLIER[vI, 1]);
                    SetP(oIns, ":e", CS_SUPPLIER[vI, 2]);
                    SetP(oIns, ":p", CS_SUPPLIER[vI, 3]);
                    oIns.ExecuteNonQuery();
                }
            }

            // ---- customers ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx, "INSERT INTO customers " +
                "(name, address, city, country, contact) VALUES (:n, :a, :ci, :co, :ct)"))
            {
                for (int vI = 0; vI <= 11; vI++)
                {
                    SetP(oIns, ":n", CS_CUSTOMER[vI, 0]);
                    SetP(oIns, ":a", CS_CUSTOMER[vI, 1]);
                    SetP(oIns, ":ci", CS_CUSTOMER[vI, 2]);
                    SetP(oIns, ":co", CS_CUSTOMER[vI, 3]);
                    SetP(oIns, ":ct", CS_CUSTOMER[vI, 4]);
                    oIns.ExecuteNonQuery();
                }
            }
        }

        private static readonly string[] CS_PO_STATUS = new string[]
        {
            "closed", "closed", "receiving", "sent"
        };

        private static readonly string[] CS_MOV_KIND = new string[]
        {
            "receipt", "putaway", "pick", "adjust", "count"
        };

        private void SeedStockAndOrders(SqliteConnection aConn,
            SqliteTransaction aTx)
        {
            TWMSRandom vRnd = new TWMSRandom();
            vRnd.Init(776655);

            // Load the id lists once, on the SAME connection the transaction is
            // open on.
            long[] vProductIds;
            long[] vBinIds;

            List<long> oIds = new List<long>();
            using (SqliteCommand oSel = NewCmd(aConn, aTx,
                "SELECT id FROM products ORDER BY id"))
            using (SqliteDataReader oReader = oSel.ExecuteReader())
            {
                while (oReader.Read())
                    oIds.Add(oReader.GetInt64(0));
            }
            vProductIds = oIds.ToArray();

            oIds = new List<long>();
            using (SqliteCommand oSel = NewCmd(aConn, aTx,
                "SELECT id FROM locations WHERE kind = 'bin' " + "ORDER BY code"))
            using (SqliteDataReader oReader = oSel.ExecuteReader())
            {
                while (oReader.Read())
                    oIds.Add(oReader.GetInt64(0));
            }
            vBinIds = oIds.ToArray();

            if ((vProductIds.Length == 0) || (vBinIds.Length == 0))
                return;

            long vUserId = ScalarOn(aConn, aTx,
                "SELECT COALESCE((SELECT id FROM users WHERE role = 'admin' " +
                "LIMIT 1), 0)");

            // ---- stock: every product in 8..12 bins -> ~1800 rows ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx,
                "INSERT OR IGNORE INTO stock (product_id, location_id, " +
                "qty) VALUES (:p, :l, :q)"))
            {
                for (int vI = 0; vI <= vProductIds.Length - 1; vI++)
                {
                    int vLines = vRnd.Range(8, 12);
                    // Walk the bin list with a stride coprime to its length, from
                    // a random start. That gives vLines DISTINCT bins per product,
                    // so the unique (product_id, location_id) index never swallows
                    // a row and the grid ends up genuinely large (~1800 rows)
                    // instead of collapsing to a third of that through collisions.
                    int vStart = vRnd.Next(vBinIds.Length);
                    for (int vJ = 0; vJ <= vLines - 1; vJ++)
                    {
                        int vBid = (vStart + (vJ * 5)) % vBinIds.Length;
                        SetP(oIns, ":p", vProductIds[vI]);
                        SetP(oIns, ":l", vBinIds[vBid]);
                        SetP(oIns, ":q", vRnd.Range(0, 240));
                        oIns.ExecuteNonQuery();
                    }
                }
            }

            // ---- purchase orders: 40, spread over the last 12 months ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx,
                "INSERT INTO purchase_orders " +
                "(supplier_id, reference, status, expected_at, created_at) " +
                "VALUES (:s, :r, :st, :e, :c)"))
            using (SqliteCommand oLine = NewCmd(aConn, aTx,
                "INSERT INTO purchase_order_lines " +
                "(po_id, product_id, qty_ordered, qty_received) " +
                "VALUES (:o, :p, :qo, :qr)"))
            {
                for (int vI = 1; vI <= 40; vI++)
                {
                    int vDays = vRnd.Range(0, 364);
                    if (vI <= 3)
                        vDays = vI - 1; // guarantee today / yesterday / this week content
                    string vWhen = WMSTimestamp.FormatWMSTimestamp(
                        DateTime.Now.AddDays(-vDays).AddMinutes(-vRnd.Range(0, 600)));
                    string vStatus = CS_PO_STATUS[vRnd.Next(4)];
                    if (vDays < 7)
                        vStatus = CS_PO_STATUS[2 + vRnd.Next(2)];
                    string vRef = string.Format(CultureInfo.InvariantCulture,
                        "PO-{0}-{1:D4}", DateTime.Now.AddDays(-vDays).ToString("yyyy",
                        CultureInfo.InvariantCulture), 1000 + vI);
                    SetP(oIns, ":s", 1 + vRnd.Next(6));
                    SetP(oIns, ":r", vRef);
                    SetP(oIns, ":st", vStatus);
                    SetP(oIns, ":e", WMSTimestamp.FormatWMSTimestamp(
                        DateTime.Now.AddDays(-vDays + vRnd.Range(3, 21))));
                    SetP(oIns, ":c", vWhen);
                    oIns.ExecuteNonQuery();
                    long vPOId = ScalarOn(aConn, aTx, "SELECT last_insert_rowid()");

                    int vLines = vRnd.Range(2, 6);
                    for (int vJ = 0; vJ <= vLines - 1; vJ++)
                    {
                        int vQty = vRnd.Range(20, 400);
                        SetP(oLine, ":o", vPOId);
                        SetP(oLine, ":p",
                            vProductIds[vRnd.Next(vProductIds.Length)]);
                        SetP(oLine, ":qo", vQty);
                        if (vStatus == "closed")
                            SetP(oLine, ":qr", vQty);
                        else if (vStatus == "receiving")
                            SetP(oLine, ":qr", vRnd.Range(0, vQty));
                        else
                            SetP(oLine, ":qr", 0);
                        oLine.ExecuteNonQuery();
                    }
                }
            }

            // ---- sales orders: 90, spread over the last 12 months ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx,
                "INSERT INTO sales_orders " +
                "(customer_id, reference, status, priority, created_at, shipped_at) " +
                "VALUES (:c, :r, :st, :p, :cr, :sh)"))
            using (SqliteCommand oLine = NewCmd(aConn, aTx,
                "INSERT INTO sales_order_lines " +
                "(so_id, product_id, qty_ordered, qty_picked) VALUES (:o, :p, :qo, :qp)"))
            {
                for (int vI = 1; vI <= 90; vI++)
                {
                    int vDays = vRnd.Range(0, 364);
                    if (vI <= 6)
                        vDays = (vI - 1) / 2;
                    string vWhen = WMSTimestamp.FormatWMSTimestamp(
                        DateTime.Now.AddDays(-vDays).AddMinutes(-vRnd.Range(0, 600)));
                    string vStatus;
                    if (vDays > 21)
                        vStatus = "shipped";
                    else
                        switch (vRnd.Next(4))
                        {
                            case 0:
                                vStatus = "new";
                                break;
                            case 1:
                                vStatus = "picking";
                                break;
                            case 2:
                                vStatus = "packed";
                                break;
                            default:
                                vStatus = "shipped";
                                break;
                        }
                    string vShipped;
                    if (vStatus == "shipped")
                        vShipped = WMSTimestamp.FormatWMSTimestamp(
                            DateTime.Now.AddDays(-vDays).AddDays(1));
                    else
                        vShipped = "";
                    string vRef = string.Format(CultureInfo.InvariantCulture,
                        "SO-{0}-{1:D4}", DateTime.Now.AddDays(-vDays).ToString("yyyy",
                        CultureInfo.InvariantCulture), 5000 + vI);
                    SetP(oIns, ":c", 1 + vRnd.Next(12));
                    SetP(oIns, ":r", vRef);
                    SetP(oIns, ":st", vStatus);
                    switch (vRnd.Next(5))
                    {
                        case 0:
                            SetP(oIns, ":p", "urgent");
                            break;
                        case 1:
                        case 2:
                            SetP(oIns, ":p", "high");
                            break;
                        default:
                            SetP(oIns, ":p", "normal");
                            break;
                    }
                    SetP(oIns, ":cr", vWhen);
                    SetP(oIns, ":sh", vShipped);
                    oIns.ExecuteNonQuery();
                    long vSOId = ScalarOn(aConn, aTx, "SELECT last_insert_rowid()");

                    int vLines = vRnd.Range(1, 8);
                    for (int vJ = 0; vJ <= vLines - 1; vJ++)
                    {
                        int vQty = vRnd.Range(1, 60);
                        SetP(oLine, ":o", vSOId);
                        SetP(oLine, ":p",
                            vProductIds[vRnd.Next(vProductIds.Length)]);
                        SetP(oLine, ":qo", vQty);
                        if ((vStatus == "shipped") || (vStatus == "packed"))
                            SetP(oLine, ":qp", vQty);
                        else if (vStatus == "picking")
                            SetP(oLine, ":qp", vRnd.Range(0, vQty));
                        else
                            SetP(oLine, ":qp", 0);
                        oLine.ExecuteNonQuery();
                    }
                }
            }

            // ---- movements: 1200 rows across the last 12 months ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx,
                "INSERT INTO movements (product_id, from_location_id, " +
                "to_location_id, qty, kind, user_id, reference, created_at) " +
                "VALUES (:p, :f, :t, :q, :k, :u, :r, :c)"))
            {
                for (int vI = 1; vI <= 1200; vI++)
                {
                    int vDays = vRnd.Range(0, 364);
                    if (vI <= 40)
                        vDays = vI % 3; // today / yesterday / two days ago
                    int vPid = vRnd.Next(vProductIds.Length);
                    int vBid = vRnd.Next(vBinIds.Length);
                    string vStatus = CS_MOV_KIND[vRnd.Next(5)];
                    SetP(oIns, ":p", vProductIds[vPid]);
                    if (vStatus == "pick")
                    {
                        SetP(oIns, ":f", vBinIds[vBid]);
                        SetP(oIns, ":t", 0L);
                    }
                    else if (vStatus == "receipt")
                    {
                        SetP(oIns, ":f", 0L);
                        SetP(oIns, ":t", vBinIds[vBid]);
                    }
                    else
                    {
                        SetP(oIns, ":f", vBinIds[vBid]);
                        SetP(oIns, ":t", vBinIds[
                            (vBid + 1 + vRnd.Next(7)) % vBinIds.Length]);
                    }
                    SetP(oIns, ":q", vRnd.Range(1, 120));
                    SetP(oIns, ":k", vStatus);
                    SetP(oIns, ":u", vUserId);
                    SetP(oIns, ":r", "MV-" + (100000 + vI).ToString("D6",
                        CultureInfo.InvariantCulture));
                    SetP(oIns, ":c", WMSTimestamp.FormatWMSTimestamp(
                        DateTime.Now.AddDays(-vDays).AddMinutes(-vRnd.Range(0, 700))));
                    oIns.ExecuteNonQuery();
                }
            }

            // ---- cycle counts: 5 closed + 1 open sheet ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx,
                "INSERT INTO stock_counts " +
                "(reference, status, created_at, closed_at) VALUES (:r, :s, :c, :cl)"))
            using (SqliteCommand oLine = NewCmd(aConn, aTx,
                "INSERT INTO stock_count_lines " +
                "(count_id, product_id, location_id, qty_expected, qty_counted) " +
                "VALUES (:c, :p, :l, :e, :n)"))
            {
                for (int vI = 1; vI <= 6; vI++)
                {
                    int vDays = (6 - vI) * 21;
                    string vStatus;
                    if (vI == 6)
                        vStatus = "open";
                    else
                        vStatus = "closed";
                    SetP(oIns, ":r", "CC-" + (2000 + vI).ToString("D4",
                        CultureInfo.InvariantCulture));
                    SetP(oIns, ":s", vStatus);
                    SetP(oIns, ":c", WMSTimestamp.FormatWMSTimestamp(
                        DateTime.Now.AddDays(-vDays)));
                    if (vStatus == "closed")
                        SetP(oIns, ":cl", WMSTimestamp.FormatWMSTimestamp(
                            DateTime.Now.AddDays(-vDays).AddDays(0.4)));
                    else
                        SetP(oIns, ":cl", "");
                    oIns.ExecuteNonQuery();
                    long vCountId = ScalarOn(aConn, aTx,
                        "SELECT last_insert_rowid()");

                    int vLines = vRnd.Range(8, 14);
                    for (int vJ = 0; vJ <= vLines - 1; vJ++)
                    {
                        int vQty = vRnd.Range(0, 200);
                        SetP(oLine, ":c", vCountId);
                        SetP(oLine, ":p",
                            vProductIds[vRnd.Next(vProductIds.Length)]);
                        SetP(oLine, ":l", vBinIds[vRnd.Next(vBinIds.Length)]);
                        SetP(oLine, ":e", vQty);
                        if (vStatus == "closed")
                            SetP(oLine, ":n", vQty + vRnd.Range(-4, 4));
                        else
                            SetP(oLine, ":n", -1); // -1 = not counted yet
                        oLine.ExecuteNonQuery();
                    }
                }
            }

            // ---- audit trail seed ---- //
            using (SqliteCommand oIns = NewCmd(aConn, aTx, "INSERT INTO audit_log " +
                "(user_id, action, entity, entity_id, detail, ip, created_at) " +
                "VALUES (:u, :a, :e, :i, :d, :ip, :c)"))
            {
                for (int vI = 1; vI <= 24; vI++)
                {
                    SetP(oIns, ":u", vUserId);
                    switch (vI % 4)
                    {
                        case 0:
                            SetP(oIns, ":a", "receive");
                            SetP(oIns, ":e", "purchase_order");
                            break;
                        case 1:
                            SetP(oIns, ":a", "ship");
                            SetP(oIns, ":e", "sales_order");
                            break;
                        case 2:
                            SetP(oIns, ":a", "adjust");
                            SetP(oIns, ":e", "stock");
                            break;
                        default:
                            SetP(oIns, ":a", "login");
                            SetP(oIns, ":e", "user");
                            break;
                    }
                    SetP(oIns, ":i", (long)vI);
                    SetP(oIns, ":d", string.Format(CultureInfo.InvariantCulture,
                        "Seeded audit entry {0}", vI));
                    SetP(oIns, ":ip", "127.0.0.1");
                    SetP(oIns, ":c", WMSTimestamp.FormatWMSTimestamp(
                        DateTime.Now.AddDays(-(vI * 5))));
                    oIns.ExecuteNonQuery();
                }
            }
        }

        // One-time demo data: ~180 products, a 4-zone/92-row location tree,
        // 6 suppliers, 12 customers, 40 purchase orders, 90 sales orders, ~1800
        // stock rows and ~1200 movements spread across the last 12 months so
        // every dashboard bucket has content. Runs only when products is empty.
        public void SeedDemoData()
        {
            lock (FSeedLock)
            {
                using (SqliteConnection oConn = Acquire())
                {
                    // Guard: seed exactly once. Anything already in products means
                    // a real (or previously seeded) warehouse, so leave it alone.
                    if (ScalarOn(oConn, "SELECT COUNT(*) FROM products") > 0)
                        return;
                    // A transaction that is disposed without a Commit rolls back,
                    // which is the Delphi try/except Rollback + raise.
                    using (SqliteTransaction oTx = oConn.BeginTransaction())
                    {
                        SeedMasterData(oConn, oTx);
                        oTx.Commit();
                    }
                    using (SqliteTransaction oTx = oConn.BeginTransaction())
                    {
                        SeedStockAndOrders(oConn, oTx);
                        oTx.Commit();
                    }
                }
            }
        }

        // ----- users ----- //

        public bool GetUserByUsername(string aUsername, out TWMSUser aUser)
        {
            aUser = new TWMSUser();
            using (TWMSDataSet oData = Query(
                "SELECT * FROM users WHERE LOWER(username) = LOWER(:u)"))
            {
                oData.SetStr("u", aUsername);
                oData.Open();
                if (!oData.IsEmpty())
                {
                    FillUserFromQuery(oData, aUser);
                    return true;
                }
            }
            return false;
        }

        public bool GetUserById(long aId, out TWMSUser aUser)
        {
            aUser = new TWMSUser();
            using (TWMSDataSet oData = Query("SELECT * FROM users WHERE id = :i"))
            {
                oData.SetInt("i", aId);
                oData.Open();
                if (!oData.IsEmpty())
                {
                    FillUserFromQuery(oData, aUser);
                    return true;
                }
            }
            return false;
        }

        public bool AuthenticateUser(string aUsername, string aPassword,
            out TWMSUser aUser)
        {
            aUser = new TWMSUser();
            TWMSUser oUser;
            if (!GetUserByUsername(aUsername, out oUser))
                return false;
            if (oUser.PasswordHash == "")
                return false;
            if (!Bcrypt.BcryptVerify(aPassword, oUser.PasswordHash))
                return false;
            aUser = oUser;
            return true;
        }

        public bool UsernameExists(string aUsername, long aExceptId = 0)
        {
            using (TWMSDataSet oData = Query("SELECT COUNT(*) AS n FROM users " +
                "WHERE LOWER(username) = LOWER(:u) AND id <> :i"))
            {
                oData.SetStr("u", aUsername);
                oData.SetInt("i", aExceptId);
                oData.Open();
                return (!oData.IsEmpty()) && (oData.AsInt("n") > 0);
            }
        }

        public long RegisterUser(string aUsername, string aPasswordHash,
            string aRole, string aDisplayName)
        {
            return SaveUser(0, aUsername, aPasswordHash, aRole, aDisplayName);
        }

        // INSERT or UPDATE. aPasswordHash '' on an update keeps the current hash.
        public long SaveUser(long aId, string aUsername, string aPasswordHash,
            string aRole, string aDisplayName)
        {
            if (Trim(aUsername) == "")
                return 0;
            if (UsernameExists(aUsername, aId))
                return 0;
            long vResult = 0;
            using (SqliteConnection oConn = Acquire())
            {
                if (aId > 0)
                {
                    string vSQL;
                    if (aPasswordHash != "")
                        vSQL = "UPDATE users SET username = :u, " +
                            "password_hash = :p, role = :r, display_name = :d WHERE id = :i";
                    else
                        vSQL = "UPDATE users SET username = :u, role = :r, " +
                            "display_name = :d WHERE id = :i";
                    using (SqliteCommand oQuery = NewCmd(oConn, vSQL))
                    {
                        if (aPasswordHash != "")
                            SetP(oQuery, ":p", aPasswordHash);
                        SetP(oQuery, ":u", aUsername);
                        SetP(oQuery, ":r", WMSConst.WMSNormalizeRole(aRole));
                        SetP(oQuery, ":d", aDisplayName);
                        SetP(oQuery, ":i", aId);
                        oQuery.ExecuteNonQuery();
                    }
                    vResult = aId;
                }
                else
                {
                    using (SqliteCommand oQuery = NewCmd(oConn, "INSERT INTO users " +
                        "(username, password_hash, role, display_name, created_at) " +
                        "VALUES (:u, :p, :r, :d, :c)"))
                    {
                        SetP(oQuery, ":u", aUsername);
                        SetP(oQuery, ":p", aPasswordHash);
                        SetP(oQuery, ":r", WMSConst.WMSNormalizeRole(aRole));
                        SetP(oQuery, ":d", aDisplayName);
                        SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                        oQuery.ExecuteNonQuery();
                    }
                    vResult = LastInsertRowId(oConn);
                }
            }
            return vResult;
        }

        public bool DeleteUser(long aId)
        {
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                ExecSQL(oConn, "DELETE FROM passkeys WHERE user_id = " + IntToStr(aId));
                ExecSQL(oConn, "DELETE FROM users WHERE id = " + IntToStr(aId));
                return true;
            }
        }

        public int CountAdmins(long aExceptId = 0)
        {
            return (int)ScalarInt("SELECT COUNT(*) FROM users WHERE role = 'admin' " +
                "AND id <> " + IntToStr(aExceptId));
        }

        public TWMSDataSet OpenUsers()
        {
            return OpenSQL("SELECT id, username, display_name, role, created_at " +
                "FROM users ORDER BY role, username");
        }

        // ----- passkeys ----- //

        public void AddPasskey(long aUserId, string aCredentialId,
            string aPublicKey, long aSignCount, string aDeviceName)
        {
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oQuery = NewCmd(oConn,
                "INSERT INTO passkeys (user_id, credential_id, " +
                "public_key, device_name, sign_count, created_at, last_used_at) " +
                "VALUES (:u, :c, :p, :d, :s, :cr, :lu)"))
            {
                SetP(oQuery, ":u", aUserId);
                SetP(oQuery, ":c", aCredentialId);
                SetP(oQuery, ":p", aPublicKey);
                SetP(oQuery, ":d", aDeviceName);
                SetP(oQuery, ":s", aSignCount);
                SetP(oQuery, ":cr", WMSTimestamp.NowTimestamp());
                SetP(oQuery, ":lu", "");
                oQuery.ExecuteNonQuery();
            }
        }

        public TWMSPasskey[] GetPasskeysByUser(long aUserId)
        {
            List<TWMSPasskey> oResult = new List<TWMSPasskey>();
            using (TWMSDataSet oData = Query(
                "SELECT * FROM passkeys WHERE user_id = :u ORDER BY id DESC"))
            {
                oData.SetInt("u", aUserId);
                oData.Open();
                while (!oData.Eof)
                {
                    TWMSPasskey vPk = new TWMSPasskey();
                    vPk.Id = oData.AsInt("id");
                    vPk.UserId = oData.AsInt("user_id");
                    vPk.CredentialId = oData.AsStr("credential_id");
                    vPk.PublicKey = oData.AsStr("public_key");
                    vPk.DeviceName = oData.AsStr("device_name");
                    vPk.SignCount = oData.AsInt("sign_count");
                    vPk.CreatedAt = WMSTimestamp.ParseWMSTimestamp(
                        oData.AsStr("created_at"));
                    vPk.LastUsedAt = WMSTimestamp.ParseWMSTimestamp(
                        oData.AsStr("last_used_at"));
                    oResult.Add(vPk);
                    oData.Next();
                }
            }
            return oResult.ToArray();
        }

        public bool GetPasskeyByCredentialId(string aCredId, out TWMSPasskey aPk)
        {
            aPk = new TWMSPasskey();
            using (TWMSDataSet oData = Query(
                "SELECT * FROM passkeys WHERE credential_id = :c"))
            {
                oData.SetStr("c", aCredId);
                oData.Open();
                if (oData.IsEmpty())
                    return false;
                aPk.Id = oData.AsInt("id");
                aPk.UserId = oData.AsInt("user_id");
                aPk.CredentialId = oData.AsStr("credential_id");
                aPk.PublicKey = oData.AsStr("public_key");
                aPk.DeviceName = oData.AsStr("device_name");
                aPk.SignCount = oData.AsInt("sign_count");
                aPk.CreatedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("created_at"));
                aPk.LastUsedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("last_used_at"));
                return true;
            }
        }

        public void UpdatePasskeySignCount(long aId, long aSignCount)
        {
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oQuery = NewCmd(oConn,
                "UPDATE passkeys SET sign_count = :s, " +
                "last_used_at = :l WHERE id = :i"))
            {
                SetP(oQuery, ":s", aSignCount);
                SetP(oQuery, ":l", WMSTimestamp.NowTimestamp());
                SetP(oQuery, ":i", aId);
                oQuery.ExecuteNonQuery();
            }
        }

        public bool DeletePasskey(long aId, long aUserId)
        {
            if ((aId <= 0) || (aUserId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                ExecSQL(oConn, "DELETE FROM passkeys WHERE id = " + IntToStr(aId) +
                    " AND user_id = " + IntToStr(aUserId));
                return true;
            }
        }

        // ----- products ----- //

        // Server-side search + sort + page. aSearch matches sku/barcode/name,
        // aCategory '' = all. aSort/aDir are whitelisted before ORDER BY.
        public TWMSDataSet OpenProducts(string aSearch, string aCategory,
            string aSort, string aDir, int aPage, int aPageSize)
        {
            string vWhere = " WHERE 1=1";
            if (Trim(aSearch) != "")
                vWhere = vWhere + " AND (p.sku LIKE :q ESCAPE '\\' OR " +
                    "p.name LIKE :q ESCAPE '\\' OR p.barcode LIKE :q ESCAPE '\\')";
            if (Trim(aCategory) != "")
                vWhere = vWhere + " AND p.category = :cat";

            string vSQL = "SELECT p.id, p.sku, p.barcode, p.name, p.category, p.uom, " +
                "p.unit_cost, p.min_stock, " +
                "COALESCE((SELECT SUM(s.qty) FROM stock s WHERE s.product_id = p.id), 0) " +
                "AS onhand " + "FROM products p" + vWhere + " ORDER BY " +
                ProductSortSQL(aSort) + " " + DirSQL(aDir) + " LIMIT :lim OFFSET :off";

            TWMSDataSet oResult = Query(vSQL);
            try
            {
                if (Trim(aSearch) != "")
                    oResult.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                if (Trim(aCategory) != "")
                    oResult.SetStr("cat", Trim(aCategory));
                oResult.SetInt("lim", aPageSize);
                oResult.SetInt("off", (aPage - 1) * aPageSize);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public int CountProducts(string aSearch, string aCategory)
        {
            string vSQL = "SELECT COUNT(*) AS n FROM products p WHERE 1=1";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (p.sku LIKE :q ESCAPE '\\' OR " +
                    "p.name LIKE :q ESCAPE '\\' OR p.barcode LIKE :q ESCAPE '\\')";
            if (Trim(aCategory) != "")
                vSQL = vSQL + " AND p.category = :cat";
            using (TWMSDataSet oData = Query(vSQL))
            {
                if (Trim(aSearch) != "")
                    oData.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                if (Trim(aCategory) != "")
                    oData.SetStr("cat", Trim(aCategory));
                oData.Open();
                return (int)oData.AsInt("n");
            }
        }

        public bool GetProduct(long aId, out TWMSProduct aProduct)
        {
            aProduct = new TWMSProduct();
            using (TWMSDataSet oData = Query("SELECT * FROM products WHERE id = :i"))
            {
                oData.SetInt("i", aId);
                oData.Open();
                if (!oData.IsEmpty())
                {
                    FillProductFromQuery(oData, aProduct);
                    return true;
                }
            }
            return false;
        }

        public bool GetProductByBarcode(string aBarcode, out TWMSProduct aProduct)
        {
            aProduct = new TWMSProduct();
            if (Trim(aBarcode) == "")
                return false;
            using (TWMSDataSet oData = Query("SELECT * FROM products WHERE barcode = :b " +
                "OR LOWER(sku) = LOWER(:b) LIMIT 1"))
            {
                oData.SetStr("b", Trim(aBarcode));
                oData.Open();
                if (!oData.IsEmpty())
                {
                    FillProductFromQuery(oData, aProduct);
                    return true;
                }
            }
            return false;
        }

        public long SaveProduct(long aId, string aSKU, string aBarcode, string aName,
            string aDescription, string aUOM, double aUnitCost, int aMinStock,
            string aCategory)
        {
            if (Trim(aSKU) == "")
                return 0;
            long vResult = 0;
            using (SqliteConnection oConn = Acquire())
            {
                string vSQL;
                if (aId > 0)
                    vSQL = "UPDATE products SET sku = :sku, barcode = :bc, " +
                        "name = :nm, description = :ds, uom = :um, unit_cost = :uc, " +
                        "min_stock = :ms, category = :ct WHERE id = :id";
                else
                    vSQL = "INSERT INTO products (sku, barcode, name, " +
                        "description, uom, unit_cost, min_stock, category) " +
                        "VALUES (:sku, :bc, :nm, :ds, :um, :uc, :ms, :ct)";
                using (SqliteCommand oQuery = NewCmd(oConn, vSQL))
                {
                    SetP(oQuery, ":sku", Trim(aSKU));
                    SetP(oQuery, ":bc", Trim(aBarcode));
                    SetP(oQuery, ":nm", Trim(aName));
                    SetP(oQuery, ":ds", aDescription);
                    SetP(oQuery, ":um", Trim(aUOM));
                    SetP(oQuery, ":uc", aUnitCost);
                    SetP(oQuery, ":ms", aMinStock);
                    SetP(oQuery, ":ct", Trim(aCategory));
                    if (aId > 0)
                        SetP(oQuery, ":id", aId);
                    oQuery.ExecuteNonQuery();
                }
                if (aId > 0)
                    vResult = aId;
                else
                    vResult = LastInsertRowId(oConn);
            }
            return vResult;
        }

        public bool DeleteProduct(long aId)
        {
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                ExecSQL(oConn, "DELETE FROM stock WHERE product_id = " + IntToStr(aId));
                ExecSQL(oConn, "DELETE FROM products WHERE id = " + IntToStr(aId));
                return true;
            }
        }

        public TWMSDataSet OpenCategories()
        {
            return OpenSQL("SELECT category, COUNT(*) AS n FROM products " +
                "GROUP BY category ORDER BY category");
        }

        // On-hand rows of one product, per bin.
        public TWMSDataSet OpenProductStock(long aProductId)
        {
            TWMSDataSet oResult = Query("SELECT l.code AS location, l.zone, s.qty, " +
                "l.capacity, ROUND(s.qty * p.unit_cost, 2) AS value " +
                "FROM stock s JOIN locations l ON l.id = s.location_id " +
                "JOIN products p ON p.id = s.product_id " +
                "WHERE s.product_id = :p AND s.qty > 0 ORDER BY l.code");
            try
            {
                oResult.SetInt("p", aProductId);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // Last movements of one product.
        public TWMSDataSet OpenProductMovements(long aProductId, int aLimit)
        {
            TWMSDataSet oResult = Query("SELECT m.created_at, m.kind, m.qty, m.reference, " +
                "COALESCE(f.code, '-') AS from_code, COALESCE(t.code, '-') AS to_code " +
                "FROM movements m " +
                "LEFT JOIN locations f ON f.id = m.from_location_id " +
                "LEFT JOIN locations t ON t.id = m.to_location_id " +
                "WHERE m.product_id = :p ORDER BY m.created_at DESC LIMIT :lim");
            try
            {
                oResult.SetInt("p", aProductId);
                oResult.SetInt("lim", aLimit);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // 12 monthly movement totals of a product, for the detail sparkline.
        public TWMSDataSet OpenProductMonthly(long aProductId)
        {
            TWMSDataSet oResult = Query("SELECT substr(m.created_at, 1, 7) AS ym, " +
                "SUM(m.qty) AS units FROM movements m WHERE m.product_id = :p " +
                "GROUP BY ym ORDER BY ym");
            try
            {
                oResult.SetInt("p", aProductId);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // ----- locations ----- //

        public TWMSDataSet OpenLocationTree()
        {
            return OpenSQL("SELECT l.id, l.parent_id, l.code, l.kind, l.zone, " +
                "l.capacity, " +
                "COALESCE((SELECT SUM(s.qty) FROM stock s WHERE s.location_id = l.id), 0) "
                + "AS units FROM locations l ORDER BY l.code");
        }

        public TWMSDataSet OpenLocationOccupancy()
        {
            return OpenSQL("SELECT l.id, l.code, l.zone, l.capacity, " +
                "COALESCE(SUM(s.qty), 0) AS units, " +
                "CASE WHEN l.capacity > 0 THEN " +
                "ROUND(COALESCE(SUM(s.qty), 0) * 100.0 / l.capacity, 1) ELSE 0 END " +
                "AS pct FROM locations l LEFT JOIN stock s ON s.location_id = l.id " +
                "WHERE l.kind = 'bin' GROUP BY l.id ORDER BY pct DESC LIMIT 40");
        }

        public bool GetLocation(long aId, out TWMSLocation aLocation)
        {
            aLocation = new TWMSLocation();
            using (TWMSDataSet oData = Query("SELECT * FROM locations WHERE id = :i"))
            {
                oData.SetInt("i", aId);
                oData.Open();
                if (oData.IsEmpty())
                    return false;
                aLocation.Id = oData.AsInt("id");
                aLocation.Code = oData.AsStr("code");
                aLocation.ParentId = oData.AsInt("parent_id");
                aLocation.Zone = oData.AsStr("zone");
                aLocation.Aisle = oData.AsStr("aisle");
                aLocation.Rack = oData.AsStr("rack");
                aLocation.Bin = oData.AsStr("bin");
                aLocation.Kind = oData.AsStr("kind");
                aLocation.Capacity = (int)oData.AsInt("capacity");
                return true;
            }
        }

        public TWMSDataSet OpenLocationStock(long aLocationId)
        {
            TWMSDataSet oResult = Query("SELECT p.id AS product_id, p.sku, p.name, p.barcode, " +
                "s.qty, ROUND(s.qty * p.unit_cost, 2) AS value " +
                "FROM stock s JOIN products p ON p.id = s.product_id " +
                "WHERE s.location_id IN (SELECT id FROM locations " +
                "WHERE id = :l OR parent_id = :l OR parent_id IN " +
                "(SELECT id FROM locations WHERE parent_id = :l)) " +
                "AND s.qty > 0 ORDER BY p.sku");
            try
            {
                oResult.SetInt("l", aLocationId);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // Bin utilisation as a rack x bin grid, for the Heatmap.
        public TWMSDataSet OpenBinUtilisation(string aZone)
        {
            TWMSDataSet oResult = Query("SELECT l.aisle || '-' || l.rack AS rack_label, " +
                "l.bin AS bin_label, " + "CASE WHEN l.capacity > 0 THEN " +
                "ROUND(COALESCE(SUM(s.qty), 0) * 100.0 / l.capacity, 0) ELSE 0 END " +
                "AS pct FROM locations l LEFT JOIN stock s ON s.location_id = l.id " +
                "WHERE l.kind = 'bin' AND l.zone = :z " +
                "GROUP BY l.id ORDER BY rack_label, bin_label");
            try
            {
                oResult.SetStr("z", aZone);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public TWMSDataSet OpenZones()
        {
            return OpenSQL("SELECT zone, COUNT(*) AS bins FROM locations " +
                "WHERE kind = 'bin' GROUP BY zone ORDER BY zone");
        }

        // ----- inbound ----- //

        public TWMSDataSet OpenPurchaseOrders(string aStatus, string aSearch)
        {
            string vSQL = "SELECT o.id, o.reference, o.status, o.expected_at, o.created_at, " +
                "s.name AS supplier, " +
                "(SELECT COUNT(*) FROM purchase_order_lines pl WHERE pl.po_id = o.id) " +
                "AS lines, " + "(SELECT COALESCE(SUM(pl.qty_ordered), 0) " +
                "FROM purchase_order_lines pl WHERE pl.po_id = o.id) AS qty_ordered, " +
                "(SELECT COALESCE(SUM(pl.qty_received), 0) " +
                "FROM purchase_order_lines pl WHERE pl.po_id = o.id) AS qty_received " +
                "FROM purchase_orders o LEFT JOIN suppliers s ON s.id = o.supplier_id " +
                "WHERE 1=1";
            if ((Trim(aStatus) != "") && (!SameText(aStatus, "all")))
                vSQL = vSQL + " AND o.status = :st";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (o.reference LIKE :q ESCAPE '\\' " +
                    "OR s.name LIKE :q ESCAPE '\\')";
            vSQL = vSQL + " ORDER BY o.created_at DESC LIMIT 200";
            TWMSDataSet oResult = Query(vSQL);
            try
            {
                if ((Trim(aStatus) != "") && (!SameText(aStatus, "all")))
                    oResult.SetStr("st", Trim(aStatus));
                if (Trim(aSearch) != "")
                    oResult.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public bool GetPurchaseOrder(long aId, out TWMSPurchaseOrder aOrder)
        {
            aOrder = new TWMSPurchaseOrder();
            using (TWMSDataSet oData = Query("SELECT o.*, COALESCE(s.name, '') AS supplier " +
                "FROM purchase_orders o LEFT JOIN suppliers s ON s.id = o.supplier_id " +
                "WHERE o.id = :i"))
            {
                oData.SetInt("i", aId);
                oData.Open();
                if (oData.IsEmpty())
                    return false;
                aOrder.Id = oData.AsInt("id");
                aOrder.SupplierId = oData.AsInt("supplier_id");
                aOrder.SupplierName = oData.AsStr("supplier");
                aOrder.Reference = oData.AsStr("reference");
                aOrder.Status = oData.AsStr("status");
                aOrder.ExpectedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("expected_at"));
                aOrder.CreatedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("created_at"));
                return true;
            }
        }

        public TWMSDataSet OpenPurchaseOrderLines(long aPOId)
        {
            TWMSDataSet oResult = Query("SELECT pl.id, p.sku, p.name, p.barcode, p.uom, " +
                "pl.qty_ordered, pl.qty_received, " +
                "(pl.qty_ordered - pl.qty_received) AS outstanding, " +
                "pl.product_id FROM purchase_order_lines pl " +
                "JOIN products p ON p.id = pl.product_id " +
                "WHERE pl.po_id = :o ORDER BY pl.id");
            try
            {
                oResult.SetInt("o", aPOId);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // Deterministic put-away rule: the lowest-coded bin, in the zone where the
        // product already holds the most stock (zone A when it holds none), whose
        // current occupancy leaves room for aQty. 0 when nothing fits.
        public long SuggestPutawayBin(long aProductId, int aQty)
        {
            long vResult = 0;
            // Zone the product already lives in, most stock first; zone A as a fallback.
            string vZone = "A";
            using (TWMSDataSet oData = Query("SELECT l.zone, SUM(s.qty) AS units FROM stock s " +
                "JOIN locations l ON l.id = s.location_id WHERE s.product_id = :p " +
                "GROUP BY l.zone ORDER BY units DESC LIMIT 1"))
            {
                oData.SetInt("p", aProductId);
                oData.Open();
                if (!oData.IsEmpty())
                    if (oData.AsStr("zone") != "")
                        vZone = oData.AsStr("zone");
            }

            // Lowest-coded bin in that zone with room for aQty.
            using (TWMSDataSet oData = Query("SELECT l.id FROM locations l " +
                "LEFT JOIN stock s ON s.location_id = l.id " +
                "WHERE l.kind = 'bin' AND l.zone = :z " +
                "GROUP BY l.id HAVING (l.capacity - COALESCE(SUM(s.qty), 0)) >= :q " +
                "ORDER BY l.code LIMIT 1"))
            {
                oData.SetStr("z", vZone);
                oData.SetInt("q", aQty);
                oData.Open();
                if (!oData.IsEmpty())
                    vResult = oData.AsInt("id");
            }

            // Nothing free in the preferred zone: take the emptiest bin anywhere.
            if (vResult == 0)
            {
                using (TWMSDataSet oData = Query("SELECT l.id FROM locations l " +
                    "LEFT JOIN stock s ON s.location_id = l.id WHERE l.kind = 'bin' " +
                    "GROUP BY l.id ORDER BY COALESCE(SUM(s.qty), 0) ASC, l.code LIMIT 1"))
                {
                    oData.Open();
                    if (!oData.IsEmpty())
                        vResult = oData.AsInt("id");
                }
            }
            return vResult;
        }

        // Record a receipt against a PO line: bump qty_received, add the units to
        // aLocationId and write a 'receipt' movement. Returns the new on-hand qty.
        public int ReceivePOLine(long aPOId, long aLineId, long aLocationId,
            int aQty, long aUserId, string aReference)
        {
            if ((aQty <= 0) || (aLineId <= 0) || (aLocationId <= 0))
                return 0;

            using (SqliteConnection oConn = Acquire())
            {
                long vProductId;
                int vOutstanding;
                using (SqliteCommand oQuery = NewCmd(oConn,
                    "SELECT product_id, (qty_ordered - qty_received) " +
                    "AS outstanding FROM purchase_order_lines WHERE id = :i AND po_id = :o"))
                {
                    SetP(oQuery, ":i", aLineId);
                    SetP(oQuery, ":o", aPOId);
                    using (SqliteDataReader oReader = oQuery.ExecuteReader())
                    {
                        if (!oReader.Read())
                            return 0;
                        vProductId = TWMSDataSet.ValueToInt64(oReader.GetValue(0));
                        vOutstanding = (int)TWMSDataSet.ValueToInt64(oReader.GetValue(1));
                    }
                }
                if (vOutstanding <= 0)
                    return 0;
                if (aQty > vOutstanding)
                    aQty = vOutstanding;

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                {
                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "UPDATE purchase_order_lines " +
                        "SET qty_received = qty_received + :q WHERE id = :i"))
                    {
                        SetP(oQuery, ":q", aQty);
                        SetP(oQuery, ":i", aLineId);
                        oQuery.ExecuteNonQuery();
                    }

                    StockAdd(oConn, oTx, vProductId, aLocationId, aQty);

                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "INSERT INTO movements (product_id, " +
                        "from_location_id, to_location_id, qty, kind, user_id, reference, " +
                        "created_at) VALUES (:p, 0, :l, :q, 'receipt', :u, :r, :c)"))
                    {
                        SetP(oQuery, ":p", vProductId);
                        SetP(oQuery, ":l", aLocationId);
                        SetP(oQuery, ":q", aQty);
                        SetP(oQuery, ":u", aUserId);
                        SetP(oQuery, ":r", aReference);
                        SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                        oQuery.ExecuteNonQuery();
                    }

                    // Close the PO once every line is complete, otherwise mark receiving.
                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "UPDATE purchase_orders SET status = " +
                        "CASE WHEN (SELECT COUNT(*) FROM purchase_order_lines " +
                        "WHERE po_id = :o AND qty_received < qty_ordered) = 0 " +
                        "THEN 'closed' ELSE 'receiving' END WHERE id = :o"))
                    {
                        SetP(oQuery, ":o", aPOId);
                        oQuery.ExecuteNonQuery();
                    }

                    oTx.Commit();
                }
                return aQty;
            }
        }

        public bool SetPurchaseOrderStatus(long aId, string aStatus)
        {
            string vStatus = Trim(aStatus).ToLowerInvariant();
            if ((vStatus != "draft") && (vStatus != "sent") && (vStatus != "receiving")
                && (vStatus != "closed"))
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                ExecSQL(oConn, "UPDATE purchase_orders SET status = " + QuotedStr(vStatus) +
                    " WHERE id = " + IntToStr(aId));
                return true;
            }
        }

        // ----- outbound ----- //

        public TWMSDataSet OpenSalesOrders(string aStatus, string aSearch)
        {
            string vSQL = "SELECT o.id, o.reference, o.status, o.priority, o.created_at, " +
                "o.shipped_at, c.name AS customer, c.city, " +
                "(SELECT COUNT(*) FROM sales_order_lines sl WHERE sl.so_id = o.id) " +
                "AS lines, " + "(SELECT COALESCE(SUM(sl.qty_ordered), 0) " +
                "FROM sales_order_lines sl WHERE sl.so_id = o.id) AS qty_ordered, " +
                "(SELECT COALESCE(SUM(sl.qty_picked), 0) " +
                "FROM sales_order_lines sl WHERE sl.so_id = o.id) AS qty_picked " +
                "FROM sales_orders o LEFT JOIN customers c ON c.id = o.customer_id " +
                "WHERE 1=1";
            if ((Trim(aStatus) != "") && (!SameText(aStatus, "all")))
                vSQL = vSQL + " AND o.status = :st";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (o.reference LIKE :q ESCAPE '\\' " +
                    "OR c.name LIKE :q ESCAPE '\\')";
            vSQL = vSQL + " ORDER BY CASE o.priority WHEN 'urgent' THEN 0 " +
                "WHEN 'high' THEN 1 ELSE 2 END, o.created_at DESC LIMIT 200";
            TWMSDataSet oResult = Query(vSQL);
            try
            {
                if ((Trim(aStatus) != "") && (!SameText(aStatus, "all")))
                    oResult.SetStr("st", Trim(aStatus));
                if (Trim(aSearch) != "")
                    oResult.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public bool GetSalesOrder(long aId, out TWMSSalesOrder aOrder)
        {
            aOrder = new TWMSSalesOrder();
            using (TWMSDataSet oData = Query("SELECT o.*, COALESCE(c.name, '') AS customer, " +
                "COALESCE(c.city, '') AS city FROM sales_orders o " +
                "LEFT JOIN customers c ON c.id = o.customer_id WHERE o.id = :i"))
            {
                oData.SetInt("i", aId);
                oData.Open();
                if (oData.IsEmpty())
                    return false;
                aOrder.Id = oData.AsInt("id");
                aOrder.CustomerId = oData.AsInt("customer_id");
                aOrder.CustomerName = oData.AsStr("customer");
                aOrder.CustomerCity = oData.AsStr("city");
                aOrder.Reference = oData.AsStr("reference");
                aOrder.Status = oData.AsStr("status");
                aOrder.Priority = oData.AsStr("priority");
                aOrder.CreatedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("created_at"));
                aOrder.ShippedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("shipped_at"));
                return true;
            }
        }

        public TWMSDataSet OpenSalesOrderLines(long aSOId)
        {
            TWMSDataSet oResult = Query("SELECT sl.id, p.sku, p.name, p.barcode, p.uom, " +
                "sl.qty_ordered, sl.qty_picked, " +
                "(sl.qty_ordered - sl.qty_picked) AS outstanding, sl.product_id, " +
                "ROUND(sl.qty_ordered * p.unit_cost, 2) AS value " +
                "FROM sales_order_lines sl JOIN products p ON p.id = sl.product_id " +
                "WHERE sl.so_id = :o ORDER BY sl.id");
            try
            {
                oResult.SetInt("o", aSOId);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // Pick list: the bins to take each outstanding line from, in walk order.
        public TWMSDataSet OpenPickList(long aSOId)
        {
            TWMSDataSet oResult = Query("SELECT sl.id AS line_id, p.sku, p.name, p.barcode, " +
                "l.id AS location_id, l.code AS location, l.zone, s.qty AS bin_qty, " +
                "(sl.qty_ordered - sl.qty_picked) AS outstanding " +
                "FROM sales_order_lines sl JOIN products p ON p.id = sl.product_id " +
                "JOIN stock s ON s.product_id = sl.product_id AND s.qty > 0 " +
                "JOIN locations l ON l.id = s.location_id " +
                "WHERE sl.so_id = :o AND sl.qty_picked < sl.qty_ordered " +
                "ORDER BY l.code, sl.id");
            try
            {
                oResult.SetInt("o", aSOId);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // Pick aQty of a line out of aLocationId. Returns the qty actually picked.
        public int PickSOLine(long aSOId, long aLineId, long aLocationId, int aQty,
            long aUserId, string aReference)
        {
            if ((aQty <= 0) || (aLineId <= 0))
                return 0;

            using (SqliteConnection oConn = Acquire())
            {
                long vProductId;
                int vOutstanding;
                int vAvailable;
                using (SqliteCommand oQuery = NewCmd(oConn,
                    "SELECT product_id, (qty_ordered - qty_picked) " +
                    "AS outstanding FROM sales_order_lines WHERE id = :i AND so_id = :o"))
                {
                    SetP(oQuery, ":i", aLineId);
                    SetP(oQuery, ":o", aSOId);
                    using (SqliteDataReader oReader = oQuery.ExecuteReader())
                    {
                        if (!oReader.Read())
                            return 0;
                        vProductId = TWMSDataSet.ValueToInt64(oReader.GetValue(0));
                        vOutstanding = (int)TWMSDataSet.ValueToInt64(oReader.GetValue(1));
                    }
                }
                if (vOutstanding <= 0)
                    return 0;
                if (aQty > vOutstanding)
                    aQty = vOutstanding;

                // Resolve the bin when the caller did not name one: the fullest bin.
                if (aLocationId <= 0)
                {
                    using (SqliteCommand oQuery = NewCmd(oConn,
                        "SELECT location_id FROM stock " +
                        "WHERE product_id = :p AND qty > 0 ORDER BY qty DESC LIMIT 1"))
                    {
                        SetP(oQuery, ":p", vProductId);
                        using (SqliteDataReader oReader = oQuery.ExecuteReader())
                        {
                            if (oReader.Read())
                                aLocationId = TWMSDataSet.ValueToInt64(
                                    oReader.GetValue(0));
                        }
                    }
                }
                if (aLocationId <= 0)
                    return 0;

                using (SqliteCommand oQuery = NewCmd(oConn,
                    "SELECT qty FROM stock WHERE product_id = :p " +
                    "AND location_id = :l"))
                {
                    SetP(oQuery, ":p", vProductId);
                    SetP(oQuery, ":l", aLocationId);
                    using (SqliteDataReader oReader = oQuery.ExecuteReader())
                    {
                        if (!oReader.Read())
                            vAvailable = 0;
                        else
                            vAvailable = (int)TWMSDataSet.ValueToInt64(
                                oReader.GetValue(0));
                    }
                }
                if (vAvailable <= 0)
                    return 0;
                if (aQty > vAvailable)
                    aQty = vAvailable;

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                {
                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "UPDATE sales_order_lines " +
                        "SET qty_picked = qty_picked + :q WHERE id = :i"))
                    {
                        SetP(oQuery, ":q", aQty);
                        SetP(oQuery, ":i", aLineId);
                        oQuery.ExecuteNonQuery();
                    }

                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "UPDATE stock SET qty = qty - :q " +
                        "WHERE product_id = :p AND location_id = :l"))
                    {
                        SetP(oQuery, ":q", aQty);
                        SetP(oQuery, ":p", vProductId);
                        SetP(oQuery, ":l", aLocationId);
                        oQuery.ExecuteNonQuery();
                    }

                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "INSERT INTO movements (product_id, " +
                        "from_location_id, to_location_id, qty, kind, user_id, reference, " +
                        "created_at) VALUES (:p, :l, 0, :q, 'pick', :u, :r, :c)"))
                    {
                        SetP(oQuery, ":p", vProductId);
                        SetP(oQuery, ":l", aLocationId);
                        SetP(oQuery, ":q", aQty);
                        SetP(oQuery, ":u", aUserId);
                        SetP(oQuery, ":r", aReference);
                        SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                        oQuery.ExecuteNonQuery();
                    }

                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "UPDATE sales_orders SET status = 'picking' " +
                        "WHERE id = :o AND status = 'new'"))
                    {
                        SetP(oQuery, ":o", aSOId);
                        oQuery.ExecuteNonQuery();
                    }

                    oTx.Commit();
                }
                return aQty;
            }
        }

        public bool SalesOrderIsFullyPicked(long aSOId)
        {
            return ScalarInt("SELECT COUNT(*) FROM sales_order_lines " +
                "WHERE so_id = " + IntToStr(aSOId) + " AND qty_picked < qty_ordered") == 0;
        }

        public bool SetSalesOrderStatus(long aId, string aStatus, long aUserId)
        {
            string vStatus = Trim(aStatus).ToLowerInvariant();
            if ((vStatus != "new") && (vStatus != "picking") && (vStatus != "packed") &&
                (vStatus != "shipped"))
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                string vSQL;
                if (vStatus == "shipped")
                    vSQL = "UPDATE sales_orders SET status = :s, " +
                        "shipped_at = :d WHERE id = :i";
                else
                    vSQL = "UPDATE sales_orders SET status = :s WHERE id = :i";
                using (SqliteCommand oQuery = NewCmd(oConn, vSQL))
                {
                    if (vStatus == "shipped")
                        SetP(oQuery, ":d", WMSTimestamp.NowTimestamp());
                    SetP(oQuery, ":s", vStatus);
                    SetP(oQuery, ":i", aId);
                    oQuery.ExecuteNonQuery();
                }
                return true;
            }
        }

        // ----- stock ----- //

        public TWMSDataSet OpenStock(string aSearch, string aZone, string aSort,
            string aDir, bool aBelowMinOnly, int aPage, int aPageSize)
        {
            string vSQL = "SELECT s.id, p.id AS product_id, p.sku, p.name, p.category, " +
                "l.code AS location, l.zone, s.qty, p.min_stock, " +
                "ROUND(s.qty * p.unit_cost, 2) AS value " +
                "FROM stock s JOIN products p ON p.id = s.product_id " +
                "JOIN locations l ON l.id = s.location_id WHERE 1=1";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (p.sku LIKE :q ESCAPE '\\' OR " +
                    "p.name LIKE :q ESCAPE '\\' OR l.code LIKE :q ESCAPE '\\')";
            if (Trim(aZone) != "")
                vSQL = vSQL + " AND l.zone = :z";
            if (aBelowMinOnly)
                vSQL = vSQL + " AND s.qty < p.min_stock";
            vSQL = vSQL + " ORDER BY " + StockSortSQL(aSort) + " " + DirSQL(aDir) +
                " LIMIT :lim OFFSET :off";
            TWMSDataSet oResult = Query(vSQL);
            try
            {
                if (Trim(aSearch) != "")
                    oResult.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                if (Trim(aZone) != "")
                    oResult.SetStr("z", Trim(aZone));
                oResult.SetInt("lim", aPageSize);
                oResult.SetInt("off", (aPage - 1) * aPageSize);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public int CountStock(string aSearch, string aZone, bool aBelowMinOnly)
        {
            string vSQL = "SELECT COUNT(*) AS n FROM stock s " +
                "JOIN products p ON p.id = s.product_id " +
                "JOIN locations l ON l.id = s.location_id WHERE 1=1";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (p.sku LIKE :q ESCAPE '\\' OR " +
                    "p.name LIKE :q ESCAPE '\\' OR l.code LIKE :q ESCAPE '\\')";
            if (Trim(aZone) != "")
                vSQL = vSQL + " AND l.zone = :z";
            if (aBelowMinOnly)
                vSQL = vSQL + " AND s.qty < p.min_stock";
            using (TWMSDataSet oData = Query(vSQL))
            {
                if (Trim(aSearch) != "")
                    oData.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                if (Trim(aZone) != "")
                    oData.SetStr("z", Trim(aZone));
                oData.Open();
                return (int)oData.AsInt("n");
            }
        }

        // aFrom / aTo are 'yyyy-mm-dd' day bounds, inclusive; either may be
        // blank. The stored timestamps are 'yyyy-mm-ddThh:nn:ss', so a plain
        // lexical comparison against the day string is exact.
        public TWMSDataSet OpenMovements(string aSearch, string aKind, string aFrom,
            string aTo, string aSort, string aDir, int aPage, int aPageSize)
        {
            string vSQL = "SELECT m.id, m.created_at, m.kind, m.qty, m.reference, " +
                "p.sku, p.name, COALESCE(f.code, '-') AS from_code, " +
                "COALESCE(t.code, '-') AS to_code, COALESCE(u.username, 'system') " +
                "AS user_name FROM movements m JOIN products p ON p.id = m.product_id " +
                "LEFT JOIN locations f ON f.id = m.from_location_id " +
                "LEFT JOIN locations t ON t.id = m.to_location_id " +
                "LEFT JOIN users u ON u.id = m.user_id WHERE 1=1";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (p.sku LIKE :q ESCAPE '\\' OR " +
                    "p.name LIKE :q ESCAPE '\\' OR m.reference LIKE :q ESCAPE '\\')";
            if ((Trim(aKind) != "") && (!SameText(aKind, "all")))
                vSQL = vSQL + " AND m.kind = :k";
            if (Trim(aFrom) != "")
                vSQL = vSQL + " AND m.created_at >= :df";
            if (Trim(aTo) != "")
                vSQL = vSQL + " AND m.created_at <= :dt";
            vSQL = vSQL + " ORDER BY " + MovementSortSQL(aSort) + " " + DirSQL(aDir) +
                " LIMIT :lim OFFSET :off";
            TWMSDataSet oResult = Query(vSQL);
            try
            {
                if (Trim(aSearch) != "")
                    oResult.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                if ((Trim(aKind) != "") && (!SameText(aKind, "all")))
                    oResult.SetStr("k", Trim(aKind));
                if (Trim(aFrom) != "")
                    oResult.SetStr("df", Trim(aFrom) + "T00:00:00");
                if (Trim(aTo) != "")
                    oResult.SetStr("dt", Trim(aTo) + "T23:59:59");
                oResult.SetInt("lim", aPageSize);
                oResult.SetInt("off", (aPage - 1) * aPageSize);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public int CountMovements(string aSearch, string aKind, string aFrom,
            string aTo)
        {
            string vSQL = "SELECT COUNT(*) AS n FROM movements m " +
                "JOIN products p ON p.id = m.product_id WHERE 1=1";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (p.sku LIKE :q ESCAPE '\\' OR " +
                    "p.name LIKE :q ESCAPE '\\' OR m.reference LIKE :q ESCAPE '\\')";
            if ((Trim(aKind) != "") && (!SameText(aKind, "all")))
                vSQL = vSQL + " AND m.kind = :k";
            if (Trim(aFrom) != "")
                vSQL = vSQL + " AND m.created_at >= :df";
            if (Trim(aTo) != "")
                vSQL = vSQL + " AND m.created_at <= :dt";
            using (TWMSDataSet oData = Query(vSQL))
            {
                if (Trim(aSearch) != "")
                    oData.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                if ((Trim(aKind) != "") && (!SameText(aKind, "all")))
                    oData.SetStr("k", Trim(aKind));
                if (Trim(aFrom) != "")
                    oData.SetStr("df", Trim(aFrom) + "T00:00:00");
                if (Trim(aTo) != "")
                    oData.SetStr("dt", Trim(aTo) + "T23:59:59");
                oData.Open();
                return (int)oData.AsInt("n");
            }
        }

        // Signed adjustment. Writes an 'adjust' movement. Returns the new qty.
        public int AdjustStock(long aProductId, long aLocationId, int aDelta,
            long aUserId, string aReference)
        {
            if ((aProductId <= 0) || (aLocationId <= 0) || (aDelta == 0))
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                int vCurrent;
                using (SqliteCommand oQuery = NewCmd(oConn,
                    "SELECT qty FROM stock WHERE product_id = :p " +
                    "AND location_id = :l"))
                {
                    SetP(oQuery, ":p", aProductId);
                    SetP(oQuery, ":l", aLocationId);
                    using (SqliteDataReader oReader = oQuery.ExecuteReader())
                    {
                        if (!oReader.Read())
                            vCurrent = 0;
                        else
                            vCurrent = (int)TWMSDataSet.ValueToInt64(
                                oReader.GetValue(0));
                    }
                }
                if ((vCurrent + aDelta) < 0)
                    aDelta = -vCurrent;
                if (aDelta == 0)
                    return vCurrent;

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                {
                    StockAdd(oConn, oTx, aProductId, aLocationId, aDelta);

                    using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                        "INSERT INTO movements (product_id, " +
                        "from_location_id, to_location_id, qty, kind, user_id, reference, " +
                        "created_at) VALUES (:p, :f, :t, :q, 'adjust', :u, :r, :c)"))
                    {
                        SetP(oQuery, ":p", aProductId);
                        if (aDelta > 0)
                        {
                            SetP(oQuery, ":f", 0L);
                            SetP(oQuery, ":t", aLocationId);
                        }
                        else
                        {
                            SetP(oQuery, ":f", aLocationId);
                            SetP(oQuery, ":t", 0L);
                        }
                        SetP(oQuery, ":q", Math.Abs(aDelta));
                        SetP(oQuery, ":u", aUserId);
                        SetP(oQuery, ":r", aReference);
                        SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                        oQuery.ExecuteNonQuery();
                    }

                    oTx.Commit();
                }
                return vCurrent + aDelta;
            }
        }

        // ----- cycle counts ----- //

        public TWMSDataSet OpenStockCounts()
        {
            return OpenSQL("SELECT c.id, c.reference, c.status, c.created_at, " +
                "c.closed_at, " + "(SELECT COUNT(*) FROM stock_count_lines cl " +
                "WHERE cl.count_id = c.id) AS lines, " +
                "(SELECT COUNT(*) FROM stock_count_lines cl " +
                "WHERE cl.count_id = c.id AND cl.qty_counted >= 0) AS counted, " +
                "(SELECT COALESCE(SUM(cl.qty_counted - cl.qty_expected), 0) " +
                "FROM stock_count_lines cl WHERE cl.count_id = c.id " +
                "AND cl.qty_counted >= 0) AS variance " +
                "FROM stock_counts c ORDER BY c.created_at DESC");
        }

        public bool GetStockCount(long aId, out TWMSStockCount aCount)
        {
            aCount = new TWMSStockCount();
            using (TWMSDataSet oData = Query("SELECT * FROM stock_counts WHERE id = :i"))
            {
                oData.SetInt("i", aId);
                oData.Open();
                if (oData.IsEmpty())
                    return false;
                aCount.Id = oData.AsInt("id");
                aCount.Reference = oData.AsStr("reference");
                aCount.Status = oData.AsStr("status");
                aCount.CreatedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("created_at"));
                aCount.ClosedAt = WMSTimestamp.ParseWMSTimestamp(
                    oData.AsStr("closed_at"));
                return true;
            }
        }

        public TWMSDataSet OpenStockCountLines(long aCountId)
        {
            TWMSDataSet oResult = Query("SELECT cl.id, p.sku, p.name, l.code AS location, " +
                "cl.qty_expected, cl.qty_counted, " +
                "CASE WHEN cl.qty_counted >= 0 THEN (cl.qty_counted - cl.qty_expected) " +
                "ELSE 0 END AS variance, cl.product_id, cl.location_id " +
                "FROM stock_count_lines cl JOIN products p ON p.id = cl.product_id " +
                "JOIN locations l ON l.id = cl.location_id " +
                "WHERE cl.count_id = :c ORDER BY l.code, p.sku");
            try
            {
                oResult.SetInt("c", aCountId);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public long CreateStockCount(string aReference)
        {
            long vResult;
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oQuery = NewCmd(oConn, "INSERT INTO stock_counts " +
                    "(reference, status, created_at, closed_at) " +
                    "VALUES (:r, 'open', :c, '')"))
                {
                    SetP(oQuery, ":r", aReference);
                    SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                    oQuery.ExecuteNonQuery();
                }
                vResult = LastInsertRowId(oConn);

                // Seed the sheet with the 20 fullest bins so it is workable at once.
                using (SqliteCommand oQuery = NewCmd(oConn,
                    "INSERT INTO stock_count_lines " +
                    "(count_id, product_id, location_id, qty_expected, qty_counted) " +
                    "SELECT :c, s.product_id, s.location_id, s.qty, -1 FROM stock s " +
                    "WHERE s.qty > 0 ORDER BY s.qty DESC LIMIT 20"))
                {
                    SetP(oQuery, ":c", vResult);
                    oQuery.ExecuteNonQuery();
                }
            }
            return vResult;
        }

        // Record a counted quantity on a line. Returns the variance.
        public int SaveCountLine(long aCountId, long aLineId, int aCounted)
        {
            int vResult = 0;
            if (aCounted < 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oQuery = NewCmd(oConn,
                    "UPDATE stock_count_lines SET qty_counted = :n " +
                    "WHERE id = :i AND count_id = :c"))
                {
                    SetP(oQuery, ":n", aCounted);
                    SetP(oQuery, ":i", aLineId);
                    SetP(oQuery, ":c", aCountId);
                    oQuery.ExecuteNonQuery();
                }

                using (SqliteCommand oQuery = NewCmd(oConn,
                    "SELECT (qty_counted - qty_expected) AS v " +
                    "FROM stock_count_lines WHERE id = :i"))
                {
                    SetP(oQuery, ":i", aLineId);
                    using (SqliteDataReader oReader = oQuery.ExecuteReader())
                    {
                        if (oReader.Read())
                            vResult = (int)TWMSDataSet.ValueToInt64(
                                oReader.GetValue(0));
                    }
                }
            }
            return vResult;
        }

        // Post every counted line to stock (writing 'count' movements) and close.
        public int CloseStockCount(long aCountId, long aUserId)
        {
            int vResult = 0;
            using (TWMSDataSet oLines = Query("SELECT product_id, location_id, qty_expected, " +
                "qty_counted FROM stock_count_lines WHERE count_id = :c " +
                "AND qty_counted >= 0"))
            {
                oLines.SetInt("c", aCountId);
                oLines.Open();

                using (SqliteConnection oConn = Acquire())
                {
                    string vRef = "CC-" + IntToStr(aCountId);
                    using (SqliteTransaction oTx = oConn.BeginTransaction())
                    {
                        while (!oLines.Eof)
                        {
                            long vProductId = oLines.AsInt("product_id");
                            long vLocationId = oLines.AsInt("location_id");
                            int vExpected = (int)oLines.AsInt("qty_expected");
                            int vCounted = (int)oLines.AsInt("qty_counted");
                            int vDelta = vCounted - vExpected;
                            if (vDelta != 0)
                            {
                                StockSet(oConn, oTx, vProductId, vLocationId, vCounted);

                                using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                                    "INSERT INTO movements (product_id, " +
                                    "from_location_id, to_location_id, qty, kind, user_id, " +
                                    "reference, created_at) " +
                                    "VALUES (:p, :f, :t, :q, 'count', :u, :r, :c)"))
                                {
                                    SetP(oQuery, ":p", vProductId);
                                    if (vDelta > 0)
                                    {
                                        SetP(oQuery, ":f", 0L);
                                        SetP(oQuery, ":t", vLocationId);
                                    }
                                    else
                                    {
                                        SetP(oQuery, ":f", vLocationId);
                                        SetP(oQuery, ":t", 0L);
                                    }
                                    SetP(oQuery, ":q", Math.Abs(vDelta));
                                    SetP(oQuery, ":u", aUserId);
                                    SetP(oQuery, ":r", vRef);
                                    SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                                    oQuery.ExecuteNonQuery();
                                }
                                vResult++;
                            }
                            oLines.Next();
                        }

                        using (SqliteCommand oQuery = NewCmd(oConn, oTx,
                            "UPDATE stock_counts SET status = 'closed', " +
                            "closed_at = :d WHERE id = :i"))
                        {
                            SetP(oQuery, ":d", WMSTimestamp.NowTimestamp());
                            SetP(oQuery, ":i", aCountId);
                            oQuery.ExecuteNonQuery();
                        }

                        oTx.Commit();
                    }
                }
            }
            return vResult;
        }

        // Handheld count: count one product in one bin without a count sheet.
        public int QuickCount(long aProductId, long aLocationId, int aCounted,
            long aUserId)
        {
            if ((aProductId <= 0) || (aLocationId <= 0) || (aCounted < 0))
                return 0;
            int vCurrent = 0;
            using (TWMSDataSet oData = Query("SELECT qty FROM stock WHERE product_id = :p " +
                "AND location_id = :l"))
            {
                oData.SetInt("p", aProductId);
                oData.SetInt("l", aLocationId);
                oData.Open();
                if (!oData.IsEmpty())
                    vCurrent = (int)oData.AsInt("qty");
            }
            int vResult = aCounted - vCurrent;
            if (vResult != 0)
                AdjustStock(aProductId, aLocationId, vResult, aUserId, "HH-COUNT");
            return vResult;
        }

        // ----- reports ----- //

        public TWMSDataSet OpenValuation()
        {
            return OpenSQL("SELECT p.sku, p.name, p.category, p.uom, " +
                "p.unit_cost, COALESCE(SUM(s.qty), 0) AS onhand, " +
                "ROUND(COALESCE(SUM(s.qty), 0) * p.unit_cost, 2) AS value " +
                "FROM products p LEFT JOIN stock s ON s.product_id = p.id " +
                "GROUP BY p.id ORDER BY value DESC");
        }

        public TWMSDataSet OpenABC()
        {
            // Twelve-month movement value per product. Repeated verbatim in the two
            // cut-off lookups below so all three see exactly the same number.
            const string CS_VALUE_SQL = "ROUND(COALESCE((SELECT SUM(m.qty) FROM movements m " +
                "WHERE m.product_id = p.id), 0) * p.unit_cost, 2)";

            // ABC banding without a window function: the SQLite engine FireDAC links
            // in the Delphi demo has no ROW_NUMBER() OVER (), so the two band
            // cut-offs are read first with LIMIT / OFFSET and then compared in a
            // plain CASE. Two cheap scalar queries instead of a correlated rank over
            // every row.
            int vCount = (int)ScalarInt("SELECT COUNT(*) FROM products");
            if (vCount < 5)
                vCount = 5;
            double vCutA = ScalarFloat("SELECT " + CS_VALUE_SQL + " AS v FROM products p " +
                "ORDER BY v DESC LIMIT 1 OFFSET " +
                IntToStr((long)Math.Truncate(vCount * 0.2)));
            double vCutB = ScalarFloat("SELECT " + CS_VALUE_SQL + " AS v FROM products p " +
                "ORDER BY v DESC LIMIT 1 OFFSET " +
                IntToStr((long)Math.Truncate(vCount * 0.5)));

            TWMSDataSet oResult = Query("SELECT p.sku, p.name, p.category, " +
                "COALESCE((SELECT SUM(m.qty) FROM movements m " +
                "WHERE m.product_id = p.id), 0) AS moves, " + CS_VALUE_SQL + " AS value, " +
                "CASE WHEN " + CS_VALUE_SQL + " >= :ca THEN 'A' " + "WHEN " +
                CS_VALUE_SQL + " >= :cb THEN 'B' ELSE 'C' END AS band " +
                "FROM products p ORDER BY value DESC LIMIT 60");
            try
            {
                oResult.SetFloat("ca", vCutA);
                oResult.SetFloat("cb", vCutB);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        public TWMSDataSet OpenSlowMovers()
        {
            return OpenSQL("SELECT p.sku, p.name, p.category, " +
                "COALESCE(SUM(s.qty), 0) AS onhand, " +
                "ROUND(COALESCE(SUM(s.qty), 0) * p.unit_cost, 2) AS value, " +
                "COALESCE((SELECT MAX(m.created_at) FROM movements m " +
                "WHERE m.product_id = p.id AND m.kind = 'pick'), '') AS last_pick " +
                "FROM products p LEFT JOIN stock s ON s.product_id = p.id " +
                "GROUP BY p.id HAVING onhand > 0 ORDER BY last_pick ASC, value DESC " +
                "LIMIT 40");
        }

        public TWMSDataSet OpenValueByCategory()
        {
            return OpenSQL("SELECT p.category, " +
                "ROUND(SUM(s.qty * p.unit_cost), 2) AS value " +
                "FROM stock s JOIN products p ON p.id = s.product_id " +
                "GROUP BY p.category ORDER BY value DESC");
        }

        // ----- dashboard ----- //

        public TWMSDashboardStats GetDashboardStats()
        {
            TWMSDashboardStats vResult = new TWMSDashboardStats();
            string vToday = DateTime.Today.ToString("yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            vResult.Products = (int)ScalarInt("SELECT COUNT(*) FROM products");
            vResult.StockUnits = (int)ScalarInt("SELECT COALESCE(SUM(qty), 0) FROM stock");
            vResult.StockValue = ScalarFloat("SELECT COALESCE(SUM(s.qty * " +
                "p.unit_cost), 0) FROM stock s JOIN products p ON p.id = s.product_id");
            vResult.BelowMin = (int)ScalarInt("SELECT COUNT(*) FROM (SELECT p.id " +
                "FROM products p LEFT JOIN stock s ON s.product_id = p.id " +
                "GROUP BY p.id HAVING COALESCE(SUM(s.qty), 0) < p.min_stock)");
            vResult.OpenPOs = (int)ScalarInt("SELECT COUNT(*) FROM purchase_orders " +
                "WHERE status IN ('sent', 'receiving')");
            vResult.OpenSOs = (int)ScalarInt("SELECT COUNT(*) FROM sales_orders " +
                "WHERE status IN ('new', 'picking', 'packed')");
            vResult.PickedToday = (int)ScalarInt("SELECT COALESCE(SUM(qty), 0) " +
                "FROM movements WHERE kind = 'pick' AND created_at LIKE " +
                QuotedStr(vToday + "%"));
            vResult.ReceivedToday = (int)ScalarInt("SELECT COALESCE(SUM(qty), 0) " +
                "FROM movements WHERE kind = 'receipt' AND created_at LIKE " +
                QuotedStr(vToday + "%"));
            vResult.BinsTotal = (int)ScalarInt("SELECT COUNT(*) FROM locations " +
                "WHERE kind = 'bin'");
            vResult.BinsUsed = (int)ScalarInt("SELECT COUNT(DISTINCT location_id) " +
                "FROM stock WHERE qty > 0");
            return vResult;
        }

        public TWMSActivityPoint[] GetActivitySeries(int aMonths)
        {
            if (aMonths <= 0)
                aMonths = 12;

            Dictionary<string, int> oReceived = new Dictionary<string, int>(
                StringComparer.Ordinal);
            Dictionary<string, int> oShipped = new Dictionary<string, int>(
                StringComparer.Ordinal);

            using (TWMSDataSet oData = OpenSQL("SELECT substr(created_at, 1, 7) AS ym, " +
                "SUM(qty) AS units FROM movements WHERE kind = 'receipt' " +
                "GROUP BY ym"))
            {
                while (!oData.Eof)
                {
                    oReceived[oData.AsStr("ym")] = (int)oData.AsInt("units");
                    oData.Next();
                }
            }

            using (TWMSDataSet oData = OpenSQL("SELECT substr(created_at, 1, 7) AS ym, " +
                "SUM(qty) AS units FROM movements WHERE kind = 'pick' GROUP BY ym"))
            {
                while (!oData.Eof)
                {
                    oShipped[oData.AsStr("ym")] = (int)oData.AsInt("units");
                    oData.Next();
                }
            }

            TWMSActivityPoint[] vResult = new TWMSActivityPoint[aMonths];
            for (int vI = 0; vI <= aMonths - 1; vI++)
            {
                DateTime vStart = DateTime.Today.AddMonths(-(aMonths - 1 - vI));
                string vKey = vStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                TWMSActivityPoint vPoint = new TWMSActivityPoint();
                vPoint.BucketLabel = vStart.ToString("MMM", CultureInfo.InvariantCulture);
                vPoint.BucketStart = vStart;
                int vValue;
                vPoint.Received = oReceived.TryGetValue(vKey, out vValue) ? vValue : 0;
                vPoint.Shipped = oShipped.TryGetValue(vKey, out vValue) ? vValue : 0;
                vResult[vI] = vPoint;
            }
            return vResult;
        }

        public TWMSDataSet OpenTopProducts(int aLimit)
        {
            TWMSDataSet oResult = Query("SELECT p.sku, p.name, SUM(m.qty) AS units " +
                "FROM movements m JOIN products p ON p.id = m.product_id " +
                "WHERE m.kind = 'pick' GROUP BY p.id ORDER BY units DESC LIMIT :lim");
            try
            {
                oResult.SetInt("lim", aLimit);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }

        // Rows for the Ctrl+K command palette: the open sales orders plus the open
        // purchase orders, straight out of SQL and into LoadFromDataSet.
        public TWMSDataSet OpenCommands()
        {
            return OpenSQL("SELECT reference AS caption, " +
                "'Sales order - ' || status AS description, " +
                "'/outbound/' || id AS href, 'Outbound' AS category FROM sales_orders "
                + "WHERE status <> 'shipped' " + "UNION ALL " +
                "SELECT reference, 'Purchase order - ' || status, " +
                "'/inbound/' || id, 'Inbound' FROM purchase_orders " +
                "WHERE status <> 'closed' LIMIT 60");
        }

        // ----- audit ----- //

        public void AddAudit(long aUserId, string aAction, string aEntity,
            long aEntityId, string aDetail, string aIP)
        {
            try
            {
                using (SqliteConnection oConn = Acquire())
                using (SqliteCommand oQuery = NewCmd(oConn,
                    "INSERT INTO audit_log (user_id, action, entity, " +
                    "entity_id, detail, ip, created_at) " +
                    "VALUES (:u, :a, :e, :i, :d, :ip, :c)"))
                {
                    SetP(oQuery, ":u", aUserId);
                    SetP(oQuery, ":a", aAction);
                    SetP(oQuery, ":e", aEntity);
                    SetP(oQuery, ":i", aEntityId);
                    SetP(oQuery, ":d", aDetail);
                    SetP(oQuery, ":ip", aIP);
                    SetP(oQuery, ":c", WMSTimestamp.NowTimestamp());
                    oQuery.ExecuteNonQuery();
                }
            }
            catch
            {
                // Auditing must never break a request.
            }
        }

        public TWMSDataSet OpenAudit(string aSearch, int aLimit)
        {
            string vSQL = "SELECT a.created_at, COALESCE(u.username, 'system') AS user_name, "
                + "a.action, a.entity, a.entity_id, a.detail, a.ip FROM audit_log a " +
                "LEFT JOIN users u ON u.id = a.user_id WHERE 1=1";
            if (Trim(aSearch) != "")
                vSQL = vSQL + " AND (a.action LIKE :q ESCAPE '\\' OR " +
                    "a.entity LIKE :q ESCAPE '\\' OR a.detail LIKE :q ESCAPE '\\')";
            vSQL = vSQL + " ORDER BY a.created_at DESC LIMIT :lim";
            TWMSDataSet oResult = Query(vSQL);
            try
            {
                if (Trim(aSearch) != "")
                    oResult.SetStr("q", "%" + EscapeLikeValue(Trim(aSearch)) + "%");
                oResult.SetInt("lim", aLimit);
                oResult.Open();
            }
            catch
            {
                oResult.Dispose();
                throw;
            }
            return oResult;
        }
    }
}
