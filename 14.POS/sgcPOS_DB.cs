// ***************************************************************************
//   sgcPOS - retail point of sale web-app demo (managed port)
//
//   written by eSeGeCe
//   copyright (c) 2026
//   Email : info@esegece.com
//   Web : https://www.esegece.com
// ***************************************************************************
//
//   Port of delphi\Demos\60.HTML\01.RunTime\14.POS\sgcPOS_DB.pas
//
//   FireDAC (TFDConnection / FDManager / TFDQuery) is replaced by
//   Microsoft.Data.Sqlite. The pool builds a connection string once from the
//   absolute DB file path; Microsoft.Data.Sqlite pools the underlying
//   connections automatically by connection string. Acquire() returns an open
//   SqliteConnection the caller disposes, which returns it to the pool. There
//   is therefore no FDManager connection definition to register / unregister.
//
//   The Delphi TPOSQuery hands an OPEN TDataSet to an sgcHTML component's
//   LoadFromDataSet. The managed components bind to System.Data.DataTable,
//   which is disconnected, so the managed TPOSQuery materialises the reader
//   into a DataTable and hands the connection straight back to the pool.
//
//   Every timestamp column is written and read as 'yyyy-MM-ddTHH:mm:ss' (the
//   Delphi 'yyyy-mm-ddThh:nn:ss'), because the analytics SQL buckets by
//   substr() on that exact shape and compares period boundaries
//   lexicographically.

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace POS
{
    /// <summary>POS database error. Mirrors Delphi EPOSDBError.</summary>
    public class EPOSDBError : Exception
    {
        public EPOSDBError(string message) : base(message) { }
    }

    // The module-level routines of sgcPOS_DB.pas. They are the ONLY conversion
    // points between the stored text and the in-memory values, which is what
    // keeps the till locale-independent.
    public static class POSDB
    {
        // 'yyyy-mm-ddThh:nn:ss', the one shape this unit writes and reads. The
        // 'T' is quoted so it is never mistaken for a format specifier.
        private const string CS_DT_FMT = "yyyy-MM-dd'T'HH:mm:ss";

        public static string POSTimestamp(DateTime aValue)
        {
            return aValue.ToString(CS_DT_FMT, CultureInfo.InvariantCulture);
        }

        public static string POSNowTimestamp()
        {
            return POSTimestamp(DateTime.Now);
        }

        // Parse a 'yyyy-mm-ddThh:nn:ss' timestamp as stored by this unit.
        // Returns DateTime.MinValue (the Delphi 0) for blank / unparsable
        // values. Parsed by fixed position on purpose: a parse without an
        // explicit format follows the CURRENT SYSTEM LOCALE's short date
        // format, which does not match this ISO shape on a Spanish / German
        // machine, so it fails silently there.
        public static DateTime ParsePOSTimestamp(string aValue)
        {
            string vText = aValue == null ? "" : aValue.Trim();
            if (vText.Length < 19)
                return DateTime.MinValue;
            int vYear = IntDef(vText.Substring(0, 4), -1);
            int vMonth = IntDef(vText.Substring(5, 2), -1);
            int vDay = IntDef(vText.Substring(8, 2), -1);
            int vHour = IntDef(vText.Substring(11, 2), -1);
            int vMin = IntDef(vText.Substring(14, 2), -1);
            int vSec = IntDef(vText.Substring(17, 2), -1);
            if ((vYear < 1) || (vYear > 9999) || (vMonth < 1) || (vMonth > 12) ||
                (vDay < 1) || (vHour < 0) || (vHour > 23) || (vMin < 0) ||
                (vMin > 59) || (vSec < 0) || (vSec > 59))
                return DateTime.MinValue;
            // The Delphi TryEncodeDateTime also rejects an out-of-range day.
            if (vDay > DateTime.DaysInMonth(vYear, vMonth))
                return DateTime.MinValue;
            return new DateTime(vYear, vMonth, vDay, vHour, vMin, vSec);
        }

        // Rounds to whole cents, half away from zero.
        public static decimal POSRoundCents(decimal aValue)
        {
            // Currency is fixed point with 4 decimals, so *100 stays exact and
            // Round gives whole cents with no float involved.
            long vCents = (long)Math.Round(aValue * 100m, MidpointRounding.ToEven);
            return (decimal)vCents / 100m;
        }

        // Locale-independent money text ('1234.50'), always two decimals,
        // always '.'. Currency is a scaled integer, so the cents are taken as
        // integers and never pass through a float formatter.
        public static string POSMoneyStr(decimal aValue)
        {
            long vCents = (long)Math.Round(aValue * 100m, MidpointRounding.ToEven);
            bool vNeg = vCents < 0;
            if (vNeg)
                vCents = -vCents;
            string vResult = (vCents / 100).ToString(CultureInfo.InvariantCulture) +
                "." + (100 + (vCents % 100)).ToString(CultureInfo.InvariantCulture)
                .Substring(1, 2);
            if (vNeg)
                vResult = "-" + vResult;
            return vResult;
        }

        // Locale-independent money input, the mirror of POSMoneyStr.
        //
        // A framework number parser follows the machine's regional settings, so
        // the very same posted '12.50' parses to 12.50 on an English box and to
        // 1250 on a Spanish one, which is exactly how a till ends up charging a
        // hundred times too much. The digits are walked by hand here instead,
        // and BOTH '.' and ',' are accepted as the separator because a European
        // numeric keypad types the comma. Anything that is not a number returns
        // False and the caller decides.
        public static bool POSParseMoney(string aText, out decimal aValue)
        {
            aValue = 0m;
            string vText = aText == null ? "" : aText.Trim();
            if (vText.Length == 0)
                return false;
            bool vNeg = false;
            long vUnits = 0;
            long vFrac = 0;
            long vScale = 1;
            bool vSeen = false;
            int vI = 0;
            char vChar;
            if ((vText[0] == '-') || (vText[0] == '+'))
            {
                vNeg = vText[0] == '-';
                vI++;
            }
            while (vI < vText.Length)
            {
                vChar = vText[vI];
                if ((vChar >= '0') && (vChar <= '9'))
                {
                    // A till never rings up more than a few thousand; refuse the
                    // rest rather than silently overflowing.
                    if (vUnits > 100000000L)
                        return false;
                    vUnits = (vUnits * 10) + (vChar - '0');
                    vSeen = true;
                }
                else if ((vChar == '.') || (vChar == ','))
                    break;
                else
                    return false;
                vI++;
            }
            if (!vSeen)
                return false;
            if (vI < vText.Length)
            {
                vI++;
                while (vI < vText.Length)
                {
                    vChar = vText[vI];
                    if ((vChar < '0') || (vChar > '9'))
                        return false;
                    // Only the first two decimals are money; anything after them
                    // is noise.
                    if (vScale < 100)
                    {
                        vFrac = (vFrac * 10) + (vChar - '0');
                        vScale = vScale * 10;
                    }
                    vI++;
                }
                while (vScale < 100)
                {
                    vFrac = vFrac * 10;
                    vScale = vScale * 10;
                }
            }
            aValue = vUnits + ((decimal)vFrac / 100m);
            if (vNeg)
                aValue = -aValue;
            return true;
        }

        // Same walk for a quantity: whole units, 1 .. 999.
        public static bool POSParseQty(string aText, out double aValue)
        {
            decimal vMoney;
            bool vResult = POSParseMoney(aText, out vMoney);
            aValue = 0;
            if (!vResult)
                return false;
            aValue = (double)decimal.Truncate(vMoney);
            if (aValue < 0)
                aValue = 0;
            if (aValue > 999)
                aValue = 999;
            return true;
        }

        private static int IntDef(string aText, int aDefault)
        {
            int vValue;
            if (int.TryParse(aText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out vValue))
                return vValue;
            return aDefault;
        }
    }

    /// <summary>
    /// A read-only result set handed straight to an sgcHTML component through
    /// its LoadFromDataSet method. This is the whole point of the demo: no REST
    /// tier, no DTO layer, the component reads the very rows the database
    /// handed back.
    ///
    /// The Delphi version owns BOTH the pooled connection and the query, so one
    /// Free returns the connection to the pool. Here the reader is materialised
    /// into a disconnected DataTable and the connection goes back to the pool
    /// as soon as Open() returns, so nothing has to be held across a request.
    /// </summary>
    public class TPOSQuery : IDisposable
    {
        private readonly TPOSDBPool FPool;
        private readonly string FSQL;
        private readonly List<KeyValuePair<string, object>> FParams;
        private DataTable FDataSet;

        public TPOSQuery(TPOSDBPool aPool, string aSQL)
        {
            if (aPool == null)
                throw new EPOSDBError("TPOSQuery: pool is nil");
            FPool = aPool;
            FSQL = aSQL;
            FParams = new List<KeyValuePair<string, object>>();
        }

        public void Dispose()
        {
            if (FDataSet != null)
            {
                FDataSet.Dispose();
                FDataSet = null;
            }
        }

        // Null until Open() has run.
        public DataTable DataSet
        {
            get { return FDataSet; }
        }

        // The statement text, so the /sql page can print exactly what ran.
        public string SQL
        {
            get { return FSQL; }
        }

        public void SetParam(string aName, object aValue)
        {
            SetValue(aName, aValue == null ? DBNull.Value : aValue);
        }

        public void SetParamInt(string aName, long aValue)
        {
            SetValue(aName, aValue);
        }

        public void Open()
        {
            using (SqliteConnection oConn = FPool.Acquire())
            using (SqliteCommand oCmd = oConn.CreateCommand())
            {
                oCmd.CommandText = FSQL;
                for (int vI = 0; vI < FParams.Count; vI++)
                    oCmd.Parameters.AddWithValue(FParams[vI].Key, FParams[vI].Value);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    FDataSet = Materialize(oReader);
            }
        }

        // The Delphi call sites name the parameter without its ':' prefix, the
        // way TFDQuery.ParamByName does. Normalise both spellings onto the ':'
        // form the SQL text actually carries.
        private void SetValue(string aName, object aValue)
        {
            string vName = aName == null ? "" : aName.Trim();
            if ((vName.Length > 0) && ((vName[0] == ':') || (vName[0] == '@') ||
                (vName[0] == '$')))
                vName = vName.Substring(1);
            vName = ":" + vName;
            for (int vI = 0; vI < FParams.Count; vI++)
                if (string.Equals(FParams[vI].Key, vName, StringComparison.Ordinal))
                {
                    FParams[vI] = new KeyValuePair<string, object>(vName, aValue);
                    return;
                }
            FParams.Add(new KeyValuePair<string, object>(vName, aValue));
        }

        // Copy the reader into a DataTable, keeping the SQL aliases verbatim as
        // the column names and keeping the column ORDER, because the page
        // components bind by field name and render in column order.
        private static DataTable Materialize(SqliteDataReader aReader)
        {
            DataTable oTable = new DataTable();
            oTable.Locale = CultureInfo.InvariantCulture;
            int vCount = aReader.FieldCount;
            string[] vNames = new string[vCount];
            for (int vI = 0; vI < vCount; vI++)
                vNames[vI] = aReader.GetName(vI);

            List<object[]> oRows = new List<object[]>();
            while (aReader.Read())
            {
                object[] vRow = new object[vCount];
                for (int vI = 0; vI < vCount; vI++)
                    vRow[vI] = aReader.IsDBNull(vI) ? DBNull.Value : aReader.GetValue(vI);
                oRows.Add(vRow);
            }

            // SQLite is dynamically typed, so the column type is decided from
            // the values that actually came back: a REAL column whose first row
            // happens to hold an integer must still be a numeric column, or the
            // grid would align it wrongly and a later 12.5 would be truncated on
            // the way into the table.
            for (int vI = 0; vI < vCount; vI++)
                oTable.Columns.Add(vNames[vI], ColumnTypeOf(oRows, vI));

            for (int vR = 0; vR < oRows.Count; vR++)
            {
                DataRow oRow = oTable.NewRow();
                for (int vI = 0; vI < vCount; vI++)
                {
                    object vValue = oRows[vR][vI];
                    if (vValue == DBNull.Value)
                        oRow[vI] = DBNull.Value;
                    else if (oTable.Columns[vI].DataType == typeof(string))
                        oRow[vI] = Convert.ToString(vValue, CultureInfo.InvariantCulture);
                    else
                        oRow[vI] = vValue;
                }
                oTable.Rows.Add(oRow);
            }
            oTable.AcceptChanges();
            return oTable;
        }

        private static Type ColumnTypeOf(List<object[]> aRows, int aIndex)
        {
            bool vAnyReal = false;
            bool vAnyInt = false;
            bool vAnyText = false;
            bool vAnyOther = false;
            for (int vR = 0; vR < aRows.Count; vR++)
            {
                object vValue = aRows[vR][aIndex];
                if (vValue == DBNull.Value)
                    continue;
                if ((vValue is double) || (vValue is float) || (vValue is decimal))
                    vAnyReal = true;
                else if ((vValue is long) || (vValue is int) || (vValue is short) ||
                    (vValue is byte) || (vValue is bool))
                    vAnyInt = true;
                else if (vValue is string)
                    vAnyText = true;
                else
                    vAnyOther = true;
            }
            if (vAnyOther)
                return typeof(object);
            if (vAnyText && (vAnyReal || vAnyInt))
                return typeof(object);
            if (vAnyText)
                return typeof(string);
            if (vAnyReal)
                return typeof(double);
            if (vAnyInt)
                return typeof(long);
            // No rows, or every row NULL: text is the safe, printable choice.
            return typeof(string);
        }
    }

    public class TPOSDBPool : IDisposable
    {
        public const string CS_POS_DB_DEF_NAME = "sgcPOS";

        private readonly string FDatabaseFile;
        private readonly string FConnStr;

        // aDatabaseFile is resolved to an absolute path internally.
        public TPOSDBPool(string aDatabaseFile)
        {
            string vFile = aDatabaseFile;
            if (string.IsNullOrEmpty(vFile))
                vFile = Path.Combine("data", "pos.db");
            // Resolve relative paths against the current directory (the EXE dir,
            // which the launcher sets).
            if (!Path.IsPathRooted(vFile))
                vFile = Path.Combine(Directory.GetCurrentDirectory(), vFile);
            FDatabaseFile = vFile;
            EnsureDatabaseDir();

            // Build the connection string once. Microsoft.Data.Sqlite pools the
            // underlying connections by connection string, which is what the
            // Delphi FDManager connection definition did.
            FConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = FDatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();
        }

        public void Dispose()
        {
            // No FDManager connection definition to close and delete.
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

        // Open aSQL on its own pooled connection and hand the rows back for a
        // component's LoadFromDataSet. Caller disposes the result.
        public TPOSQuery OpenQuery(string aSQL)
        {
            return new TPOSQuery(this, aSQL);
        }

        // ------------------------------------------------------------------ //
        //  low-level helpers                                                 //
        // ------------------------------------------------------------------ //

        // Every command carries its transaction explicitly: Microsoft.Data.Sqlite
        // refuses to run a command with no transaction while its connection has
        // one pending, which is the FireDAC behaviour the Delphi relies on
        // implicitly (a TFDQuery joins the connection's transaction on its own).
        private static SqliteCommand NewCmd(SqliteConnection aConn,
            SqliteTransaction aTx, string aSQL)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.CommandText = aSQL;
            oCmd.Transaction = aTx;
            return oCmd;
        }

        private static SqliteCommand NewCmd(SqliteConnection aConn, string aSQL)
        {
            return NewCmd(aConn, null, aSQL);
        }

        private static void AddParam(SqliteCommand aCmd, string aName, object aValue)
        {
            aCmd.Parameters.AddWithValue(aName, aValue == null ? DBNull.Value : aValue);
        }

        // Money reaches SQLite as a REAL, exactly like the Delphi
        // ParamByName().AsFloat of a Currency. A decimal parameter would be
        // stored as TEXT by the provider and would break every SUM().
        private static void AddParamCur(SqliteCommand aCmd, string aName, decimal aValue)
        {
            aCmd.Parameters.AddWithValue(aName, (double)aValue);
        }

        private static int ExecNonQuery(SqliteConnection aConn, SqliteTransaction aTx,
            string aSQL, params object[] aArgs)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aTx, aSQL))
            {
                ApplyArgs(oCmd, aArgs);
                return oCmd.ExecuteNonQuery();
            }
        }

        // Apply (name, value) argument pairs onto a command.
        private static void ApplyArgs(SqliteCommand aCmd, object[] aArgs)
        {
            if (aArgs == null)
                return;
            int vI = 0;
            while (vI + 1 < aArgs.Length)
            {
                AddParam(aCmd, (string)aArgs[vI], aArgs[vI + 1]);
                vI += 2;
            }
        }

        // last_insert_rowid() on the same connection, which is the only safe way
        // to read back an AUTOINCREMENT id: the value is per-connection, so a
        // pooled sibling can never hand back somebody else's row.
        private static long POSLastId(SqliteConnection aConn, SqliteTransaction aTx)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aTx, "SELECT last_insert_rowid() AS id"))
            {
                object vValue = oCmd.ExecuteScalar();
                if ((vValue == null) || (vValue == DBNull.Value))
                    return 0;
                return Convert.ToInt64(vValue, CultureInfo.InvariantCulture);
            }
        }

        private static long POSLastId(SqliteConnection aConn)
        {
            return POSLastId(aConn, null);
        }

        private decimal ScalarCurrency(SqliteConnection aConn, SqliteTransaction aTx,
            string aSQL, string[] aParamNames, long[] aParamValues)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aTx, aSQL))
            {
                for (int vI = 0; vI < aParamNames.Length; vI++)
                    AddParam(oCmd, ":" + aParamNames[vI], aParamValues[vI]);
                object vValue = oCmd.ExecuteScalar();
                if ((vValue == null) || (vValue == DBNull.Value))
                    return 0m;
                return ToCurrency(vValue);
            }
        }

        private decimal ScalarCurrency(SqliteConnection aConn, string aSQL,
            string[] aParamNames, long[] aParamValues)
        {
            return ScalarCurrency(aConn, null, aSQL, aParamNames, aParamValues);
        }

        private int ScalarInt(SqliteConnection aConn, string aSQL,
            string[] aParamNames, long[] aParamValues)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, null, aSQL))
            {
                for (int vI = 0; vI < aParamNames.Length; vI++)
                    AddParam(oCmd, ":" + aParamNames[vI], aParamValues[vI]);
                object vValue = oCmd.ExecuteScalar();
                if ((vValue == null) || (vValue == DBNull.Value))
                    return 0;
                return Convert.ToInt32(vValue, CultureInfo.InvariantCulture);
            }
        }

        // ------------------------------------------------------------------ //
        //  reader column helpers (map DBNull / ordinals)                     //
        // ------------------------------------------------------------------ //

        // The Delphi field accessors never raise on a type mismatch, and SQLite
        // has no static column types, so every read goes through Convert.
        private static decimal ToCurrency(object aValue)
        {
            // Currency carries 4 decimals, so a REAL column is snapped to that
            // scale exactly like the Delphi AsCurrency does.
            return Math.Round(Convert.ToDecimal(aValue, CultureInfo.InvariantCulture),
                4, MidpointRounding.ToEven);
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

        private static double RFloat(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return 0;
            return Convert.ToDouble(aReader.GetValue(vOrd), CultureInfo.InvariantCulture);
        }

        private static decimal RCur(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return 0m;
            return ToCurrency(aReader.GetValue(vOrd));
        }

        private static string RStr(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return "";
            object vValue = aReader.GetValue(vOrd);
            string vText = vValue as string;
            if (vText != null)
                return vText;
            return Convert.ToString(vValue, CultureInfo.InvariantCulture);
        }

        private static DateTime RDate(SqliteDataReader aReader, string aName)
        {
            return POSDB.ParsePOSTimestamp(RStr(aReader, aName));
        }

        // The Delphi FindField guard: a shared Fill routine is used by SELECTs
        // that do and do not join the extra display columns.
        private static bool HasField(SqliteDataReader aReader, string aName)
        {
            for (int vI = 0; vI < aReader.FieldCount; vI++)
                if (string.Equals(aReader.GetName(vI), aName,
                    StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // Escape LIKE metacharacters before wrapping a search term in '%...%'.
        // Paired with an explicit ESCAPE '\' clause at every call site.
        private static string EscapeLikeValue(string aValue)
        {
            string vResult = aValue.Replace("\\", "\\\\");
            vResult = vResult.Replace("%", "\\%");
            vResult = vResult.Replace("_", "\\_");
            return vResult;
        }

        // Whitelist the product-list sort column. Anything unrecognized falls
        // back to the product name, so a hand-crafted ?sort= can never reach the
        // SQL.
        private static string ProductSortSQL(string aSort)
        {
            if (string.Equals(aSort, "sku", StringComparison.OrdinalIgnoreCase))
                return "p.sku";
            if (string.Equals(aSort, "price", StringComparison.OrdinalIgnoreCase))
                return "p.price";
            if (string.Equals(aSort, "stock", StringComparison.OrdinalIgnoreCase))
                return "p.stock";
            if (string.Equals(aSort, "category", StringComparison.OrdinalIgnoreCase))
                return "c.name";
            return "p.name";
        }

        // Whitelist the direction to ASC / DESC.
        private static string DirSQL(string aDir)
        {
            if (string.Equals((aDir == null ? "" : aDir).Trim(), "desc",
                StringComparison.OrdinalIgnoreCase))
                return "DESC";
            return "ASC";
        }

        private static string CoerceRole(string aRole)
        {
            string vRole = (aRole == null ? "" : aRole).Trim().ToLowerInvariant();
            if (vRole == POSConst.CS_ROLE_ADMIN)
                return POSConst.CS_ROLE_ADMIN;
            if (vRole == POSConst.CS_ROLE_MANAGER)
                return POSConst.CS_ROLE_MANAGER;
            return POSConst.CS_ROLE_CASHIER;
        }

        private static string CoerceMethod(string aMethod)
        {
            string vMethod = (aMethod == null ? "" : aMethod).Trim().ToLowerInvariant();
            if ((vMethod == POSConst.CS_PAY_CARD) || (vMethod == POSConst.CS_PAY_VOUCHER) ||
                (vMethod == POSConst.CS_PAY_LOYALTY))
                return vMethod;
            return POSConst.CS_PAY_CASH;
        }

        private static void FillUserFromQuery(SqliteDataReader aReader, out TPOSUser aUser)
        {
            aUser = new TPOSUser();
            aUser.Id = RInt64(aReader, "id");
            aUser.Username = RStr(aReader, "username");
            aUser.PasswordHash = RStr(aReader, "password_hash");
            aUser.Role = RStr(aReader, "role");
            aUser.DisplayName = RStr(aReader, "display_name");
            aUser.PinHash = RStr(aReader, "pin");
            aUser.CreatedAt = RDate(aReader, "created_at");
        }

        private static void FillProductFromQuery(SqliteDataReader aReader,
            out TPOSProduct aProd)
        {
            aProd = new TPOSProduct();
            aProd.Id = RInt64(aReader, "id");
            aProd.Sku = RStr(aReader, "sku");
            aProd.Barcode = RStr(aReader, "barcode");
            aProd.Name = RStr(aReader, "name");
            aProd.CategoryId = RInt64(aReader, "category_id");
            aProd.Price = RCur(aReader, "price");
            aProd.Cost = RCur(aReader, "cost");
            aProd.TaxRate = RFloat(aReader, "tax_rate");
            aProd.Stock = RInt(aReader, "stock");
            aProd.ImageSVG = RStr(aReader, "image_svg");
            aProd.Active = RInt(aReader, "active") != 0;
            if (HasField(aReader, "category_name"))
                aProd.CategoryName = RStr(aReader, "category_name");
            else
                aProd.CategoryName = "";
        }

        private static void FillSaleFromQuery(SqliteDataReader aReader, out TPOSSale aSale)
        {
            aSale = new TPOSSale();
            aSale.Id = RInt64(aReader, "id");
            aSale.Reference = RStr(aReader, "reference");
            aSale.UserId = RInt64(aReader, "user_id");
            aSale.CustomerId = RInt64(aReader, "customer_id");
            aSale.ShiftId = RInt64(aReader, "shift_id");
            aSale.Subtotal = RCur(aReader, "subtotal");
            aSale.Discount = RCur(aReader, "discount");
            aSale.Tax = RCur(aReader, "tax");
            aSale.Total = RCur(aReader, "total");
            aSale.Status = RStr(aReader, "status");
            aSale.CreatedAt = RDate(aReader, "created_at");
            if (HasField(aReader, "user_name"))
                aSale.UserName = RStr(aReader, "user_name");
            else
                aSale.UserName = "";
            if (HasField(aReader, "customer_name"))
                aSale.CustomerName = RStr(aReader, "customer_name");
            else
                aSale.CustomerName = "";
            if (HasField(aReader, "line_count"))
                aSale.LineCount = RInt(aReader, "line_count");
            else
                aSale.LineCount = 0;
        }

        private static void FillShiftFromQuery(SqliteDataReader aReader,
            out TPOSShift aShift)
        {
            aShift = new TPOSShift();
            aShift.Id = RInt64(aReader, "id");
            aShift.UserId = RInt64(aReader, "user_id");
            aShift.OpenedAt = RDate(aReader, "opened_at");
            aShift.ClosedAt = RDate(aReader, "closed_at");
            aShift.OpeningFloat = RCur(aReader, "opening_float");
            aShift.CountedCash = RCur(aReader, "counted_cash");
            aShift.ExpectedCash = RCur(aReader, "expected_cash");
            aShift.Variance = RCur(aReader, "variance");
            aShift.Status = RStr(aReader, "status");
            if (HasField(aReader, "user_name"))
                aShift.UserName = RStr(aReader, "user_name");
            else
                aShift.UserName = "";
        }

        private static void FillPasskeyFromQuery(SqliteDataReader aReader,
            out TPOSPasskey aPk)
        {
            aPk = new TPOSPasskey();
            aPk.Id = RInt64(aReader, "id");
            aPk.UserId = RInt64(aReader, "user_id");
            aPk.CredentialId = RStr(aReader, "credential_id");
            aPk.PublicKey = RStr(aReader, "public_key");
            aPk.SignCount = RInt64(aReader, "sign_count");
            aPk.DeviceName = RStr(aReader, "device_name");
            aPk.CreatedAt = RDate(aReader, "created_at");
            aPk.LastUsedAt = RDate(aReader, "last_used_at");
        }

        // ================================================================== //
        //  schema + seeding                                                  //
        // ================================================================== //

        // Create every table + index (IF NOT EXISTS). Idempotent.
        public void EnsureSchema()
        {
            string[] vTables = new string[]
            {
                "CREATE TABLE IF NOT EXISTS users (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "username TEXT UNIQUE, " +
                "password_hash TEXT, " + "role TEXT, " + "display_name TEXT, " +
                "pin TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS passkeys (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "user_id INTEGER, " +
                "credential_id TEXT, " + "public_key TEXT, " + "sign_count INTEGER, " +
                "device_name TEXT, " + "created_at TEXT, " + "last_used_at TEXT)",

                "CREATE TABLE IF NOT EXISTS categories (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "name TEXT, " + "color TEXT, " +
                "sort_order INTEGER)",

                "CREATE TABLE IF NOT EXISTS products (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "sku TEXT, " +
                "barcode TEXT, " + "name TEXT, " + "category_id INTEGER, " +
                "price REAL, " + "cost REAL, " + "tax_rate REAL, " + "stock INTEGER, " +
                "image_svg TEXT, " + "active INTEGER)",

                "CREATE TABLE IF NOT EXISTS customers (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "name TEXT, " + "email TEXT, " +
                "phone TEXT, " + "loyalty_points INTEGER, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS promotions (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "name TEXT, " + "kind TEXT, " +
                "value REAL, " + "product_id INTEGER, " + "category_id INTEGER, " +
                "starts_at TEXT, " + "ends_at TEXT, " + "active INTEGER)",

                "CREATE TABLE IF NOT EXISTS sales (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "reference TEXT, " +
                "user_id INTEGER, " + "customer_id INTEGER, " + "shift_id INTEGER, " +
                "subtotal REAL, " + "discount REAL, " + "tax REAL, " + "total REAL, " +
                "status TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS sale_lines (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "sale_id INTEGER, " +
                "product_id INTEGER, " + "qty REAL, " + "unit_price REAL, " +
                "discount REAL, " + "tax_rate REAL, " + "line_total REAL)",

                "CREATE TABLE IF NOT EXISTS payments (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "sale_id INTEGER, " +
                "method TEXT, " + "amount REAL, " + "change_given REAL, " +
                "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS shifts (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "user_id INTEGER, " +
                "opened_at TEXT, " + "closed_at TEXT, " + "opening_float REAL, " +
                "counted_cash REAL, " + "expected_cash REAL, " + "variance REAL, " +
                "status TEXT)",

                "CREATE TABLE IF NOT EXISTS audit_log (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "user_id INTEGER, " +
                "action TEXT, " + "entity TEXT, " + "entity_id INTEGER, " +
                "detail TEXT, " + "ip TEXT, " + "created_at TEXT)"
            };

            string[] vIndexes = new string[]
            {
                "CREATE INDEX IF NOT EXISTS ix_products_cat ON products(category_id)",
                "CREATE INDEX IF NOT EXISTS ix_products_barcode ON products(barcode)",
                "CREATE INDEX IF NOT EXISTS ix_lines_sale ON sale_lines(sale_id)",
                "CREATE INDEX IF NOT EXISTS ix_pay_sale ON payments(sale_id)",
                "CREATE INDEX IF NOT EXISTS ix_sales_status ON sales(status)",
                "CREATE INDEX IF NOT EXISTS ix_sales_created ON sales(created_at)",
                "CREATE INDEX IF NOT EXISTS ix_sales_shift ON sales(shift_id)"
            };

            using (SqliteConnection oConn = Acquire())
            {
                for (int vI = 0; vI < vTables.Length; vI++)
                    ExecNonQuery(oConn, null, vTables[vI]);
                for (int vI = 0; vI < vIndexes.Length; vI++)
                    ExecNonQuery(oConn, null, vIndexes[vI]);
            }
        }

        // Demo PINs, bcrypt-hashed exactly like the passwords. They are the only
        // thing standing between a cashier and a refund, so they are never
        // stored in the clear.
        private const string CS_ADMIN_PIN = "4242";
        private const string CS_MANAGER_PIN = "1379";
        private const string CS_CASHIER_PIN = "1111";

        // INSERT the admin / manager / cashier demo accounts only when the users
        // table has no admin yet. aPasswordHash is the bcrypt hash of the
        // configured password; every seeded account gets a bcrypt-hashed PIN.
        public void SeedAdmin(string aUser, string aPasswordHash)
        {
            using (SqliteConnection oConn = Acquire())
            {
                int vCount = ScalarInt(oConn,
                    "SELECT COUNT(*) FROM users WHERE role = 'admin'",
                    new string[0], new long[0]);
                if (vCount > 0)
                    return;

                AddSeedUser(oConn, aUser, aPasswordHash, POSConst.CS_ROLE_ADMIN,
                    "Store Owner", Bcrypt.BcryptHash(CS_ADMIN_PIN));
                AddSeedUser(oConn, "manager", Bcrypt.BcryptHash("manager"),
                    POSConst.CS_ROLE_MANAGER, "Dana Reyes",
                    Bcrypt.BcryptHash(CS_MANAGER_PIN));
                AddSeedUser(oConn, "cashier", Bcrypt.BcryptHash("cashier"),
                    POSConst.CS_ROLE_CASHIER, "Sam Ortiz",
                    Bcrypt.BcryptHash(CS_CASHIER_PIN));
                AddSeedUser(oConn, "cashier2", Bcrypt.BcryptHash("cashier"),
                    POSConst.CS_ROLE_CASHIER, "Nia Patel", Bcrypt.BcryptHash("2222"));
            }
        }

        private static void AddSeedUser(SqliteConnection aConn, string aName,
            string aHash, string aRole, string aDisplay, string aPin)
        {
            ExecNonQuery(aConn, null,
                "INSERT INTO users " +
                "(username, password_hash, role, display_name, pin, created_at) " +
                "VALUES (:u, :p, :r, :d, :n, :c)",
                ":u", aName, ":p", aHash, ":r", aRole, ":d", aDisplay, ":n", aPin,
                ":c", POSDB.POSNowTimestamp());
        }

        // ----- demo data seeding ----- //

        private static readonly string[][] CS_SEED_CATEGORIES = new string[][]
        {
            new string[] { "Bakery", "#F59E0B" },
            new string[] { "Beverages", "#0EA5E9" },
            new string[] { "Dairy", "#8B5CF6" },
            new string[] { "Produce", "#22C55E" },
            new string[] { "Snacks", "#EF4444" },
            new string[] { "Household", "#64748B" },
            new string[] { "Frozen", "#06B6D4" },
            new string[] { "Alcohol", "#A16207" }
        };

        // 15 articles per category = 120 products, the catalogue size the till
        // grid and the back office list are meant to show.
        private static readonly string[][] CS_SEED_ITEMS = new string[][]
        {
            new string[] { "Sourdough Loaf", "Baguette", "Croissant",
                "Pain au Chocolat", "Ciabatta Roll", "Rye Bread", "Bagel",
                "Cinnamon Bun", "Focaccia", "Brioche", "Multigrain Loaf",
                "Pretzel", "Danish Pastry", "Muffin", "Scone" },

            new string[] { "Still Water 1L", "Sparkling Water 1L",
                "Orange Juice 1L", "Apple Juice 1L", "Cola 330ml",
                "Lemonade 330ml", "Iced Tea 500ml", "Energy Drink 250ml",
                "Ground Coffee 250g", "Tea Bags 40s", "Tonic Water 200ml",
                "Cold Brew 330ml", "Coconut Water 330ml", "Sports Drink 500ml",
                "Kombucha 330ml" },

            new string[] { "Whole Milk 1L", "Semi Skimmed 1L",
                "Greek Yoghurt 500g", "Butter 250g", "Cheddar 200g",
                "Mozzarella 125g", "Cream Cheese 200g", "Double Cream 300ml",
                "Feta 200g", "Parmesan 150g", "Skyr 450g", "Kefir 500ml",
                "Brie 200g", "Goat Cheese 120g", "Cottage Cheese 300g" },

            new string[] { "Bananas 1kg", "Gala Apples 1kg", "Tomatoes 500g",
                "Cucumber", "Baby Spinach 200g", "Carrots 1kg",
                "Red Onions 500g", "Avocado", "Lemons 500g", "Blueberries 250g",
                "Strawberries 400g", "Broccoli", "Bell Peppers 3s",
                "New Potatoes 1kg", "Mushrooms 250g" },

            new string[] { "Salted Crisps 150g", "Tortilla Chips 200g",
                "Salted Peanuts 200g", "Dark Chocolate 100g",
                "Milk Chocolate 100g", "Granola Bars 6s", "Popcorn 100g",
                "Mixed Nuts 250g", "Rice Crackers 100g", "Pretzel Sticks 200g",
                "Wine Gums 180g", "Shortbread 150g", "Oat Cookies 200g",
                "Dried Mango 100g", "Trail Mix 200g" },

            new string[] { "Kitchen Roll 2s", "Toilet Tissue 4s",
                "Washing Up Liquid 500ml", "Laundry Pods 20s",
                "Surface Spray 750ml", "Bin Liners 20s", "Aluminium Foil 20m",
                "Cling Film 30m", "Sponge Scourers 3s", "Glass Cleaner 500ml",
                "Fabric Softener 1L", "Dishwasher Tabs 30s",
                "Air Freshener 300ml", "Rubber Gloves", "Floor Cleaner 1L" },

            new string[] { "Garden Peas 750g", "Sweetcorn 500g",
                "Fish Fingers 12s", "Margherita Pizza", "Vanilla Ice Cream 500ml",
                "Chocolate Ice Cream 500ml", "Oven Chips 1kg",
                "Mixed Berries 400g", "Spring Rolls 8s", "Chicken Nuggets 500g",
                "Prawns 300g", "Falafel 400g", "Waffles 6s", "Green Beans 750g",
                "Sorbet 500ml" },

            new string[] { "House Red 75cl", "House White 75cl",
                "Rioja Reserva 75cl", "Prosecco 75cl", "Pale Ale 330ml",
                "Lager 330ml", "Stout 440ml", "Wheat Beer 500ml", "Cider 500ml",
                "Gin 70cl", "Vodka 70cl", "Single Malt 70cl", "Spiced Rum 70cl",
                "Vermouth 75cl", "Cava 75cl" }
        };

        private static readonly string[] CS_SEED_FIRST = new string[]
        {
            "Ana", "Ben", "Clara", "Diego", "Elena", "Farid", "Greta", "Hugo",
            "Ines", "Jonas", "Kira", "Liam", "Marta", "Nils", "Olga", "Pablo",
            "Rosa", "Sven", "Tomas", "Vera"
        };

        private static readonly string[] CS_SEED_LAST = new string[]
        {
            "Alvarez", "Bauer", "Costa", "Duarte", "Engel", "Ferrer", "Gruber",
            "Haas", "Iglesias", "Jansen", "Klein", "Lorenzo", "Moreau", "Novak",
            "Oliveira"
        };

        // Hour-of-day weights, 08:00 to 21:00 (index 0 is hour 8). Two peaks,
        // lunch and evening: the dashboard's "sales by hour" chart and the
        // weekday x hour heatmap are only worth showing when the seeded data
        // actually has a shape.
        private static readonly int[] CS_HOUR_WEIGHT = new int[]
        {
            3, 5, 7, 9, 16, 18, 11, 6, 7, 9, 14, 17, 12, 6
        };

        // Small deterministic PRNG (xorshift32) so a rebuilt database always
        // seeds the same catalogue and the same 12 months of history. The
        // framework Random is deliberately not used: it would make the seed
        // irreproducible.
        private sealed class TPOSRandom
        {
            private uint FState;

            public void Init(uint aSeed)
            {
                FState = aSeed;
                if (FState == 0)
                    FState = 0x2545F491;
            }

            public int Next(int aRange)
            {
                FState = FState ^ (FState << 13);
                FState = FState ^ (FState >> 17);
                FState = FState ^ (FState << 5);
                if (aRange <= 0)
                    return 0;
                return (int)((FState >> 1) % (uint)aRange);
            }
        }

        private void SeedCatalogue(SqliteConnection aConn, SqliteTransaction aTx)
        {
            TPOSRandom oRnd = new TPOSRandom();
            oRnd.Init(0x51ED2701);

            for (int vCat = 0; vCat < CS_SEED_CATEGORIES.Length; vCat++)
                ExecNonQuery(aConn, aTx,
                    "INSERT INTO categories (id, name, color, sort_order) " +
                    "VALUES (:i, :n, :c, :s)",
                    ":i", vCat + 1, ":n", CS_SEED_CATEGORIES[vCat][0],
                    ":c", CS_SEED_CATEGORIES[vCat][1], ":s", vCat + 1);

            int vSeq = 0;
            for (int vCat = 0; vCat < CS_SEED_ITEMS.Length; vCat++)
                for (int vItem = 0; vItem <= 14; vItem++)
                {
                    vSeq++;
                    string vName = CS_SEED_ITEMS[vCat][vItem];
                    // 0.55 .. 24.44, built from whole cents so no float literal
                    // is ever rounded into the price column.
                    decimal vPrice = (decimal)(55 + oRnd.Next(2390)) / 100m;
                    // Food is taxed low, household and alcohol at the standard
                    // rate.
                    double vTax;
                    if ((vCat == 5) || (vCat == 7))
                        vTax = 21;
                    else if ((vCat == 0) || (vCat == 3))
                        vTax = 4;
                    else
                        vTax = 10;
                    string vSku = CS_SEED_CATEGORIES[vCat][0].Substring(0, 3)
                        .ToUpperInvariant() + "-" +
                        vSeq.ToString("D4", CultureInfo.InvariantCulture);
                    // 13-digit EAN-shaped barcode, deterministic per product.
                    string vBarcode = "84" + (5000000 + (vSeq * 7919))
                        .ToString("D11", CultureInfo.InvariantCulture);
                    int vStock = 20 + oRnd.Next(180);
                    ExecNonQuery(aConn, aTx,
                        "INSERT INTO products (sku, barcode, name, " +
                        "category_id, price, cost, tax_rate, stock, image_svg, active) " +
                        "VALUES (:sk, :bc, :nm, :ct, :pr, :co, :tx, :st, :im, 1)",
                        ":sk", vSku, ":bc", vBarcode, ":nm", vName, ":ct", vCat + 1,
                        ":pr", (double)vPrice,
                        ":co", (double)POSDB.POSRoundCents(vPrice * 62m / 100m),
                        ":tx", vTax, ":st", vStock, ":im", "");
                }

            for (int vI = 0; vI <= 59; vI++)
            {
                string vName = CS_SEED_FIRST[vI % CS_SEED_FIRST.Length] + " " +
                    CS_SEED_LAST[(vI * 7) % CS_SEED_LAST.Length];
                string vEmail = vName.Replace(" ", ".").ToLowerInvariant() +
                    vI.ToString(CultureInfo.InvariantCulture) + "@example.com";
                int vPhoneA = 100 + oRnd.Next(899);
                int vPhoneB = 100 + oRnd.Next(899);
                string vPhone = "+34 6" +
                    (vI % 100).ToString("D2", CultureInfo.InvariantCulture) + " " +
                    vPhoneA.ToString("D3", CultureInfo.InvariantCulture) + " " +
                    vPhoneB.ToString("D3", CultureInfo.InvariantCulture);
                int vPoints = oRnd.Next(900);
                string vCreated = POSDB.POSTimestamp(
                    DateTime.Now.AddDays(-(30 + oRnd.Next(700))));
                ExecNonQuery(aConn, aTx,
                    "INSERT INTO customers (name, email, phone, " +
                    "loyalty_points, created_at) VALUES (:n, :e, :p, :l, :c)",
                    ":n", vName, ":e", vEmail, ":p", vPhone, ":l", vPoints,
                    ":c", vCreated);
            }

            AddPromo(aConn, aTx, "Bakery Happy Hour", "percent", 15, 0, 1, -20, 40);
            AddPromo(aConn, aTx, "Two off every Spirit", "amount", 2, 0, 8, -10, 20);
            AddPromo(aConn, aTx, "Snack Bundle 3 for 2", "bundle", 3, 0, 5, -5, 25);
            AddPromo(aConn, aTx, "Dairy Week", "percent", 10, 0, 3, -2, 12);
            AddPromo(aConn, aTx, "Frozen Friday", "percent", 20, 0, 7, 0, 60);
        }

        private static void AddPromo(SqliteConnection aConn, SqliteTransaction aTx,
            string aName, string aKind, double aValue, long aProductId,
            long aCategoryId, int aDaysFrom, int aDaysTo)
        {
            ExecNonQuery(aConn, aTx,
                "INSERT INTO promotions (name, kind, value, " +
                "product_id, category_id, starts_at, ends_at, active) " +
                "VALUES (:n, :k, :v, :p, :c, :s, :e, 1)",
                ":n", aName, ":k", aKind, ":v", aValue, ":p", aProductId,
                ":c", aCategoryId,
                ":s", POSDB.POSTimestamp(DateTime.Today.AddDays(aDaysFrom)),
                ":e", POSDB.POSTimestamp(DateTime.Today.AddDays(aDaysTo)));
        }

        private struct TSeedProduct
        {
            public long Id;
            public decimal Price;
            public double TaxRate;
        }

        private void SeedHistory(SqliteConnection aConn, SqliteTransaction aTx)
        {
            TPOSRandom oRnd = new TPOSRandom();
            oRnd.Init(0x9E3779B9);

            List<TSeedProduct> oProducts = new List<TSeedProduct>();
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "SELECT id, price, tax_rate FROM products ORDER BY id"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
                while (oReader.Read())
                {
                    TSeedProduct vProd = new TSeedProduct();
                    vProd.Id = RInt64(oReader, "id");
                    vProd.Price = RCur(oReader, "price");
                    vProd.TaxRate = RFloat(oReader, "tax_rate");
                    oProducts.Add(vProd);
                }
            if (oProducts.Count == 0)
                return;

            List<long> oCashiers = new List<long>();
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "SELECT id FROM users WHERE role IN " +
                "('cashier', 'manager') ORDER BY id"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
                while (oReader.Read())
                    oCashiers.Add(RInt64(oReader, "id"));
            if (oCashiers.Count == 0)
                return;

            // Expand the hour weights into a flat pick table, so one Next() call
            // picks an hour with the right shape (lunch and evening peaks).
            int vTotalWeight = 0;
            for (int vI = 0; vI < CS_HOUR_WEIGHT.Length; vI++)
                vTotalWeight += CS_HOUR_WEIGHT[vI];
            int[] vHourPick = new int[vTotalWeight];
            int vJ = 0;
            for (int vI = 0; vI < CS_HOUR_WEIGHT.Length; vI++)
                for (int vWeight = 1; vWeight <= CS_HOUR_WEIGHT[vI]; vWeight++)
                {
                    vHourPick[vJ] = 8 + vI;
                    vJ++;
                }

            List<long> oShiftIds = new List<long>();
            List<decimal> oShiftFloats = new List<decimal>();
            int vSeq = 0;

            for (int vDay = 364; vDay >= 0; vDay--)
            {
                long vShiftId = 0;
                // The last 30 trading days each get their own closed shift,
                // which is the ~30 the spec asks for and gives /shift real
                // history to list.
                if (vDay < 30)
                {
                    decimal vOpeningFloat = 100 + (oRnd.Next(5) * 20);
                    ExecNonQuery(aConn, aTx,
                        "INSERT INTO shifts (user_id, opened_at, closed_at, " +
                        "opening_float, counted_cash, expected_cash, variance, status) " +
                        "VALUES (:u, :o, :c, :f, 0, 0, 0, 'closed')",
                        ":u", oCashiers[vDay % oCashiers.Count],
                        ":o", POSDB.POSTimestamp(DateTime.Today.AddDays(-vDay).AddHours(8)),
                        ":c", POSDB.POSTimestamp(DateTime.Today.AddDays(-vDay)
                            .AddHours(21).AddMinutes(30)),
                        ":f", (double)vOpeningFloat);
                    vShiftId = POSLastId(aConn, aTx);
                    oShiftIds.Add(vShiftId);
                    oShiftFloats.Add(vOpeningFloat);
                }

                // ~900 sales in total, weighted towards the recent weeks so the
                // dashboard's "today" and "this week" buckets are never empty.
                int vSalesToday;
                if (vDay < 7)
                    vSalesToday = 5 + oRnd.Next(4);
                else if (vDay < 30)
                    vSalesToday = 4 + oRnd.Next(3);
                else if (vDay < 90)
                    vSalesToday = 2 + oRnd.Next(3);
                else
                    vSalesToday = 1 + oRnd.Next(3);

                for (int vI = 1; vI <= vSalesToday; vI++)
                {
                    vSeq++;
                    int vHour = vHourPick[oRnd.Next(vHourPick.Length)];
                    int vMinute = oRnd.Next(60);
                    DateTime vSaleWhen = DateTime.Today.AddDays(-vDay)
                        .AddHours(vHour).AddMinutes(vMinute);
                    long vUserId = oCashiers[oRnd.Next(oCashiers.Count)];
                    long vCustomerId;
                    if (oRnd.Next(100) < 35)
                        vCustomerId = 1 + oRnd.Next(60);
                    else
                        vCustomerId = 0;

                    ExecNonQuery(aConn, aTx,
                        "INSERT INTO sales (reference, user_id, customer_id, " +
                        "shift_id, subtotal, discount, tax, total, status, created_at) " +
                        "VALUES (:r, :u, :c, :s, 0, 0, 0, 0, 'open', :d)",
                        ":r", "", ":u", vUserId, ":c", vCustomerId, ":s", vShiftId,
                        ":d", POSDB.POSTimestamp(vSaleWhen));
                    long vSaleId = POSLastId(aConn, aTx);

                    int vLineCount = 1 + oRnd.Next(6);
                    decimal[] vNetLine = new decimal[vLineCount];
                    double[] vRateLine = new double[vLineCount];
                    decimal vSubtotal = 0;
                    for (vJ = 0; vJ <= vLineCount - 1; vJ++)
                    {
                        int vIdx = oRnd.Next(oProducts.Count);
                        int vQty = 1 + oRnd.Next(3);
                        decimal vLineDisc = 0;
                        vNetLine[vJ] = POSDB.POSRoundCents(oProducts[vIdx].Price * vQty)
                            - vLineDisc;
                        vRateLine[vJ] = oProducts[vIdx].TaxRate;
                        vSubtotal = vSubtotal + vNetLine[vJ];
                        ExecNonQuery(aConn, aTx,
                            "INSERT INTO sale_lines (sale_id, product_id, qty, " +
                            "unit_price, discount, tax_rate, line_total) " +
                            "VALUES (:s, :p, :q, :u, :d, :t, :l)",
                            ":s", vSaleId, ":p", oProducts[vIdx].Id, ":q", (double)vQty,
                            ":u", (double)oProducts[vIdx].Price, ":d", (double)vLineDisc,
                            ":t", vRateLine[vJ], ":l", (double)vNetLine[vJ]);
                    }

                    // One sale in seven carries a whole-sale discount, small
                    // enough to stay under the manager-PIN threshold.
                    decimal vDiscount = 0;
                    if (oRnd.Next(7) == 0)
                    {
                        vDiscount = (decimal)(50 + oRnd.Next(250)) / 100m;
                        if (vDiscount > vSubtotal)
                            vDiscount = vSubtotal;
                    }

                    // Spread the sale discount across the lines in proportion to
                    // their net, so every line is taxed on what the customer
                    // actually paid for it. The last line absorbs the rounding
                    // remainder, which is what makes lines + tax - discount =
                    // total exact to the cent.
                    decimal vTax = 0;
                    decimal vAllocated = 0;
                    for (vJ = 0; vJ <= vLineCount - 1; vJ++)
                    {
                        decimal vLineDisc;
                        if ((vDiscount == 0) || (vSubtotal == 0))
                            vLineDisc = 0;
                        else if (vJ == vLineCount - 1)
                            vLineDisc = vDiscount - vAllocated;
                        else
                        {
                            vLineDisc = POSDB.POSRoundCents(vDiscount *
                                (vNetLine[vJ] / vSubtotal));
                            vAllocated = vAllocated + vLineDisc;
                        }
                        vTax = vTax + POSDB.POSRoundCents((vNetLine[vJ] - vLineDisc) *
                            (decimal)vRateLine[vJ] / 100m);
                    }
                    decimal vTotal = vSubtotal - vDiscount + vTax;

                    bool vRefunded = oRnd.Next(50) == 0;
                    ExecNonQuery(aConn, aTx,
                        "UPDATE sales SET reference = :r, subtotal = :s, " +
                        "discount = :d, tax = :t, total = :o, status = :st WHERE id = :id",
                        ":r", "R" + vSaleWhen.ToString("yyyyMMdd",
                            CultureInfo.InvariantCulture) + "-" +
                            vSeq.ToString("D5", CultureInfo.InvariantCulture),
                        ":s", (double)vSubtotal, ":d", (double)vDiscount,
                        ":t", (double)vTax, ":o", (double)vTotal,
                        ":st", vRefunded ? POSConst.CS_SALE_REFUNDED :
                            POSConst.CS_SALE_COMPLETED,
                        ":id", vSaleId);

                    int vWeight = oRnd.Next(100);
                    string vMethod;
                    if (vWeight < 55)
                        vMethod = POSConst.CS_PAY_CASH;
                    else if (vWeight < 90)
                        vMethod = POSConst.CS_PAY_CARD;
                    else if (vWeight < 97)
                        vMethod = POSConst.CS_PAY_VOUCHER;
                    else
                        vMethod = POSConst.CS_PAY_LOYALTY;

                    decimal vChange = 0;
                    decimal vTendered = vTotal;
                    if (vMethod == POSConst.CS_PAY_CASH)
                    {
                        // Round the tender up to the next 5, the way a customer
                        // hands over a note. amount - change_given is still
                        // exactly the sale total.
                        vTendered = POSDB.POSRoundCents(
                            ((long)decimal.Truncate(vTotal / 5m) + 1) * 5m);
                        vChange = vTendered - vTotal;
                    }
                    ExecNonQuery(aConn, aTx,
                        "INSERT INTO payments (sale_id, method, amount, " +
                        "change_given, created_at) VALUES (:s, :m, :a, :c, :d)",
                        ":s", vSaleId, ":m", vMethod, ":a", (double)vTendered,
                        ":c", (double)vChange, ":d", POSDB.POSTimestamp(vSaleWhen));

                    // A refunded sale keeps its original payment and gains a
                    // negative one, so the shift's expected cash still nets out
                    // correctly.
                    if (vRefunded)
                        ExecNonQuery(aConn, aTx,
                            "INSERT INTO payments (sale_id, method, amount, " +
                            "change_given, created_at) VALUES (:s, :m, :a, :c, :d)",
                            ":s", vSaleId, ":m", vMethod,
                            ":a", (double)(-(vTendered - vChange)), ":c", 0.0,
                            ":d", POSDB.POSTimestamp(vSaleWhen.AddHours(1)));
                }
            }

            // Close every seeded shift on its recorded payments, never on a
            // guess.
            for (int vI = 0; vI < oShiftIds.Count; vI++)
            {
                decimal vExpected = oShiftFloats[vI] + ScalarCurrency(aConn, aTx,
                    "SELECT COALESCE(SUM(p.amount - p.change_given), 0) FROM payments p " +
                    "INNER JOIN sales s ON s.id = p.sale_id " +
                    "WHERE s.shift_id = :sid AND p.method = 'cash'",
                    new string[] { "sid" }, new long[] { oShiftIds[vI] });
                // A believable till drift of -3.50 .. +3.45.
                decimal vCounted = POSDB.POSRoundCents(vExpected +
                    ((decimal)(oRnd.Next(140) - 70) / 20m));
                ExecNonQuery(aConn, aTx,
                    "UPDATE shifts SET counted_cash = :c, " +
                    "expected_cash = :e, variance = :v WHERE id = :id",
                    ":c", (double)vCounted, ":e", (double)vExpected,
                    ":v", (double)(vCounted - vExpected), ":id", oShiftIds[vI]);
            }
        }

        // One-time demo-data seeding: 8 categories, ~120 products, ~60
        // customers, 5 promotions, ~900 completed sales spread over the last 12
        // months with a lunch / evening hour-of-day shape, their lines and
        // payments, and ~30 closed shifts. Runs ONLY when the products table is
        // empty, so it is safe to call on every startup.
        public void SeedDemoData()
        {
            using (SqliteConnection oConn = Acquire())
            {
                int vCount = ScalarInt(oConn, "SELECT COUNT(*) FROM products",
                    new string[0], new long[0]);
                // Guarded so it runs exactly once: a till with real takings must
                // never be re-seeded on a restart.
                if (vCount > 0)
                    return;

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                {
                    SeedCatalogue(oConn, oTx);
                    SeedHistory(oConn, oTx);
                    oTx.Commit();
                }
            }
        }

        // ================================================================== //
        //  users                                                             //
        // ================================================================== //

        public bool GetUserByUsername(string aUsername, out TPOSUser aUser)
        {
            aUser = null;
            if ((aUsername == null) || (aUsername.Trim().Length == 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, " +
                "display_name, pin, created_at FROM users " +
                "WHERE LOWER(username) = LOWER(:u) LIMIT 1"))
            {
                AddParam(oCmd, ":u", aUsername.Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillUserFromQuery(oReader, out aUser);
                    return true;
                }
            }
        }

        public bool GetUserById(long aId, out TPOSUser aUser)
        {
            aUser = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, " +
                "display_name, pin, created_at FROM users WHERE id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillUserFromQuery(oReader, out aUser);
                    return true;
                }
            }
        }

        public bool AuthenticateUser(string aUsername, string aPassword,
            out TPOSUser aUser)
        {
            aUser = null;
            TPOSUser oUser;
            if (!GetUserByUsername(aUsername, out oUser))
                return false;
            if (!Bcrypt.BcryptVerify(aPassword, oUser.PasswordHash))
                return false;
            aUser = oUser;
            return true;
        }

        public TPOSUser[] ListUsers()
        {
            List<TPOSUser> oResult = new List<TPOSUser>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, " +
                "display_name, pin, created_at FROM users ORDER BY role, username"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
                while (oReader.Read())
                {
                    TPOSUser vRow;
                    FillUserFromQuery(oReader, out vRow);
                    oResult.Add(vRow);
                }
            return oResult.ToArray();
        }

        // INSERT or UPDATE. A blank aPassword / aPin leaves the stored hash
        // alone on an update. Returns the row id, or 0 when the username is
        // taken.
        public long SaveUser(long aId, string aUsername, string aDisplayName,
            string aRole, string aPassword, string aPin)
        {
            string vName = (aUsername == null ? "" : aUsername).Trim();
            if (vName.Length == 0)
                return 0;
            string vDisplay = (aDisplayName == null ? "" : aDisplayName).Trim();
            string vPassword = (aPassword == null ? "" : aPassword);
            string vPin = (aPin == null ? "" : aPin);
            using (SqliteConnection oConn = Acquire())
            {
                int vTaken;
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) FROM users WHERE " +
                    "LOWER(username) = LOWER(:u) AND id <> :i"))
                {
                    AddParam(oCmd, ":u", vName);
                    AddParam(oCmd, ":i", aId);
                    object vValue = oCmd.ExecuteScalar();
                    vTaken = (vValue == null) || (vValue == DBNull.Value) ? 0 :
                        Convert.ToInt32(vValue, CultureInfo.InvariantCulture);
                }
                if (vTaken > 0)
                    return 0;

                if (aId > 0)
                {
                    ExecNonQuery(oConn, null,
                        "UPDATE users SET username = :u, " +
                        "display_name = :d, role = :r WHERE id = :i",
                        ":u", vName, ":d", vDisplay, ":r", CoerceRole(aRole), ":i", aId);
                    // A blank password / PIN on an edit means "leave it alone",
                    // so a manager can rename an account without resetting its
                    // credentials.
                    if (vPassword.Trim().Length != 0)
                        ExecNonQuery(oConn, null,
                            "UPDATE users SET password_hash = :p WHERE id = :i",
                            ":p", Bcrypt.BcryptHash(vPassword), ":i", aId);
                    if (vPin.Trim().Length != 0)
                        ExecNonQuery(oConn, null,
                            "UPDATE users SET pin = :n WHERE id = :i",
                            ":n", Bcrypt.BcryptHash(vPin.Trim()), ":i", aId);
                    return aId;
                }

                ExecNonQuery(oConn, null,
                    "INSERT INTO users (username, password_hash, " +
                    "role, display_name, pin, created_at) " +
                    "VALUES (:u, :p, :r, :d, :n, :c)",
                    ":u", vName,
                    ":p", vPassword.Trim().Length == 0 ? Bcrypt.BcryptHash(vName) :
                        Bcrypt.BcryptHash(vPassword),
                    ":r", CoerceRole(aRole), ":d", vDisplay,
                    ":n", vPin.Trim().Length == 0 ? "" : Bcrypt.BcryptHash(vPin.Trim()),
                    ":c", POSDB.POSNowTimestamp());
                return POSLastId(oConn);
            }
        }

        // Server-side manager gate. Walks every manager / admin account and
        // bcrypt-verifies aPin against the stored hash. Returns False for a
        // blank PIN, an unknown PIN, or a PIN that belongs to a cashier.
        public bool VerifyManagerPin(string aPin, out TPOSUser aManager)
        {
            aManager = null;
            string vPin = (aPin == null ? "" : aPin).Trim();
            // A blank PIN must never authorise anything, which is exactly the
            // hole a client-side gate leaves open.
            if (vPin.Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            // Only a manager or an admin can authorise: a cashier PIN that
            // happens to match is not a manager approval.
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, " +
                "display_name, pin, created_at FROM users " +
                "WHERE role IN ('manager', 'admin') ORDER BY id"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
                while (oReader.Read())
                {
                    string vHash = RStr(oReader, "pin");
                    if ((vHash != "") && Bcrypt.BcryptVerify(vPin, vHash))
                    {
                        FillUserFromQuery(oReader, out aManager);
                        return true;
                    }
                }
            return false;
        }

        // ================================================================== //
        //  passkeys (WebAuthn)                                               //
        // ================================================================== //

        public void AddPasskey(long aUserId, string aCredentialId, string aPublicKey,
            long aSignCount, string aDeviceName)
        {
            using (SqliteConnection oConn = Acquire())
                ExecNonQuery(oConn, null,
                    "INSERT INTO passkeys " +
                    "(user_id, credential_id, public_key, sign_count, device_name, " +
                    "created_at, last_used_at) VALUES (:uid, :cid, :pk, :sc, :dn, :ca, :lu)",
                    ":uid", aUserId, ":cid", aCredentialId, ":pk", aPublicKey,
                    ":sc", aSignCount, ":dn", aDeviceName,
                    ":ca", POSDB.POSNowTimestamp(), ":lu", "");
        }

        public TPOSPasskey[] GetPasskeysByUser(long aUserId)
        {
            List<TPOSPasskey> oResult = new List<TPOSPasskey>();
            if (aUserId <= 0)
                return oResult.ToArray();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, " +
                "sign_count, device_name, created_at, last_used_at FROM passkeys " +
                "WHERE user_id = :uid ORDER BY id DESC"))
            {
                AddParam(oCmd, ":uid", aUserId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSPasskey vRow;
                        FillPasskeyFromQuery(oReader, out vRow);
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        public bool GetPasskeyByCredentialId(string aCredId, out TPOSPasskey aPk)
        {
            aPk = null;
            if ((aCredId == null) || (aCredId.Trim().Length == 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, " +
                "sign_count, device_name, created_at, last_used_at FROM passkeys " +
                "WHERE credential_id = :cid LIMIT 1"))
            {
                AddParam(oCmd, ":cid", aCredId.Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillPasskeyFromQuery(oReader, out aPk);
                    return true;
                }
            }
        }

        public void UpdatePasskeySignCount(long aId, long aSignCount)
        {
            using (SqliteConnection oConn = Acquire())
                ExecNonQuery(oConn, null,
                    "UPDATE passkeys SET sign_count = :sc, " +
                    "last_used_at = :lu WHERE id = :id",
                    ":sc", aSignCount, ":lu", POSDB.POSNowTimestamp(), ":id", aId);
        }

        public bool DeletePasskey(long aId, long aUserId)
        {
            if ((aId <= 0) || (aUserId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
                // Scoped to user_id so a caller can only delete their own
                // credentials.
                return ExecNonQuery(oConn, null,
                    "DELETE FROM passkeys WHERE id = :id AND user_id = :uid",
                    ":id", aId, ":uid", aUserId) > 0;
        }

        // ================================================================== //
        //  catalogue                                                         //
        // ================================================================== //

        public TPOSCategory[] ListCategories()
        {
            List<TPOSCategory> oResult = new List<TPOSCategory>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, name, color, sort_order FROM categories " +
                "ORDER BY sort_order, name"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
                while (oReader.Read())
                {
                    TPOSCategory vRow = new TPOSCategory();
                    vRow.Id = RInt64(oReader, "id");
                    vRow.Name = RStr(oReader, "name");
                    vRow.Color = RStr(oReader, "color");
                    vRow.SortOrder = RInt(oReader, "sort_order");
                    oResult.Add(vRow);
                }
            return oResult.ToArray();
        }

        public bool GetCategory(long aId, out TPOSCategory aCat)
        {
            aCat = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, name, color, sort_order FROM categories " +
                "WHERE id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aCat = new TPOSCategory();
                    aCat.Id = RInt64(oReader, "id");
                    aCat.Name = RStr(oReader, "name");
                    aCat.Color = RStr(oReader, "color");
                    aCat.SortOrder = RInt(oReader, "sort_order");
                    return true;
                }
            }
        }

        public long SaveCategory(long aId, string aName, string aColor, int aSortOrder)
        {
            string vName = (aName == null ? "" : aName).Trim();
            if (vName.Length == 0)
                return 0;
            string vColor = (aColor == null ? "" : aColor).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                if (aId > 0)
                {
                    ExecNonQuery(oConn, null,
                        "UPDATE categories SET name = :n, color = :c, " +
                        "sort_order = :s WHERE id = :i",
                        ":n", vName, ":c", vColor, ":s", aSortOrder, ":i", aId);
                    return aId;
                }
                ExecNonQuery(oConn, null,
                    "INSERT INTO categories (name, color, sort_order) " +
                    "VALUES (:n, :c, :s)",
                    ":n", vName, ":c", vColor, ":s", aSortOrder);
                return POSLastId(oConn);
            }
        }

        public TPOSProduct[] ListProducts(string aSearch, long aCategoryId,
            bool aActiveOnly, string aSort = "", string aDir = "")
        {
            List<TPOSProduct> oResult = new List<TPOSProduct>();
            string vSearch = (aSearch == null ? "" : aSearch).Trim();
            string vSQL = "SELECT p.id, p.sku, p.barcode, p.name, p.category_id, " +
                "p.price, p.cost, p.tax_rate, p.stock, p.image_svg, p.active, " +
                "c.name AS category_name FROM products p " +
                "LEFT JOIN categories c ON c.id = p.category_id WHERE 1 = 1";
            if (aActiveOnly)
                vSQL = vSQL + " AND p.active = 1";
            if (aCategoryId > 0)
                vSQL = vSQL + " AND p.category_id = :cat";
            if (vSearch != "")
                vSQL = vSQL + " AND (p.name LIKE :q ESCAPE '\\' OR " +
                    "p.sku LIKE :q ESCAPE '\\' OR p.barcode LIKE :q ESCAPE '\\')";
            // Both halves come from a whitelist, never from the raw query string.
            vSQL = vSQL + " ORDER BY " + ProductSortSQL(aSort) + " " + DirSQL(aDir);
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (aCategoryId > 0)
                    AddParam(oCmd, ":cat", aCategoryId);
                if (vSearch != "")
                    AddParam(oCmd, ":q", "%" + EscapeLikeValue(vSearch) + "%");
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSProduct vRow;
                        FillProductFromQuery(oReader, out vRow);
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        public bool GetProduct(long aId, out TPOSProduct aProd)
        {
            aProd = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT p.id, p.sku, p.barcode, p.name, " +
                "p.category_id, p.price, p.cost, p.tax_rate, p.stock, p.image_svg, " +
                "p.active, c.name AS category_name FROM products p " +
                "LEFT JOIN categories c ON c.id = p.category_id " +
                "WHERE p.id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillProductFromQuery(oReader, out aProd);
                    return true;
                }
            }
        }

        public bool GetProductByBarcode(string aBarcode, out TPOSProduct aProd)
        {
            aProd = null;
            if ((aBarcode == null) || (aBarcode.Trim().Length == 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            // A scanner reads the barcode, an operator with a damaged label
            // types the SKU: both resolve here, which is why manual entry is
            // always on.
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT p.id, p.sku, p.barcode, p.name, " +
                "p.category_id, p.price, p.cost, p.tax_rate, p.stock, p.image_svg, " +
                "p.active, c.name AS category_name FROM products p " +
                "LEFT JOIN categories c ON c.id = p.category_id " +
                "WHERE p.active = 1 AND (p.barcode = :b OR UPPER(p.sku) = UPPER(:b)) " +
                "LIMIT 1"))
            {
                AddParam(oCmd, ":b", aBarcode.Trim());
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillProductFromQuery(oReader, out aProd);
                    return true;
                }
            }
        }

        public long SaveProduct(TPOSProduct aProd)
        {
            if ((aProd == null) || (aProd.Name == null) ||
                (aProd.Name.Trim().Length == 0))
                return 0;
            string vSQL;
            if (aProd.Id > 0)
                vSQL = "UPDATE products SET sku = :sk, barcode = :bc, " +
                    "name = :nm, category_id = :ct, price = :pr, cost = :co, " +
                    "tax_rate = :tx, stock = :st, active = :ac WHERE id = :id";
            else
                vSQL = "INSERT INTO products (sku, barcode, name, " +
                    "category_id, price, cost, tax_rate, stock, image_svg, active) " +
                    "VALUES (:sk, :bc, :nm, :ct, :pr, :co, :tx, :st, '', :ac)";
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (aProd.Id > 0)
                        AddParam(oCmd, ":id", aProd.Id);
                    AddParam(oCmd, ":sk", aProd.Sku == null ? "" : aProd.Sku.Trim());
                    AddParam(oCmd, ":bc", aProd.Barcode == null ? "" :
                        aProd.Barcode.Trim());
                    AddParam(oCmd, ":nm", aProd.Name.Trim());
                    AddParam(oCmd, ":ct", aProd.CategoryId);
                    AddParamCur(oCmd, ":pr", POSDB.POSRoundCents(aProd.Price));
                    AddParamCur(oCmd, ":co", POSDB.POSRoundCents(aProd.Cost));
                    AddParam(oCmd, ":tx", aProd.TaxRate);
                    AddParam(oCmd, ":st", aProd.Stock);
                    AddParam(oCmd, ":ac", aProd.Active ? 1 : 0);
                    oCmd.ExecuteNonQuery();
                }
                if (aProd.Id > 0)
                    return aProd.Id;
                return POSLastId(oConn);
            }
        }

        public bool DeleteProduct(long aId)
        {
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
                // Soft delete: a product that appears on a historical receipt
                // must stay resolvable, so it is deactivated rather than removed.
                return ExecNonQuery(oConn, null,
                    "UPDATE products SET active = 0 WHERE id = :i", ":i", aId) > 0;
        }

        // Last 12 weekly unit totals of aProductId, for the list Sparkline.
        public double[] GetProductTrend(long aProductId)
        {
            double[] vResult = new double[12];
            for (int vI = 0; vI <= 11; vI++)
                vResult[vI] = 0;
            if (aProductId <= 0)
                return vResult;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT COALESCE(SUM(l.qty), 0) AS q " +
                "FROM sale_lines l INNER JOIN sales s ON s.id = l.sale_id " +
                "WHERE l.product_id = :p AND s.status = 'completed' " +
                "AND s.created_at >= :f AND s.created_at < :t"))
            {
                AddParam(oCmd, ":p", aProductId);
                AddParam(oCmd, ":f", "");
                AddParam(oCmd, ":t", "");
                for (int vI = 0; vI <= 11; vI++)
                {
                    DateTime vFrom = DateTime.Today.AddDays(-7 * (12 - vI));
                    DateTime vTo = DateTime.Today.AddDays(-7 * (11 - vI));
                    oCmd.Parameters[":f"].Value = POSDB.POSTimestamp(vFrom);
                    oCmd.Parameters[":t"].Value = POSDB.POSTimestamp(vTo);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        if (oReader.Read())
                            vResult[vI] = RFloat(oReader, "q");
                }
            }
            return vResult;
        }

        // ================================================================== //
        //  customers                                                         //
        // ================================================================== //

        public TPOSCustomer[] ListCustomers(string aSearch)
        {
            List<TPOSCustomer> oResult = new List<TPOSCustomer>();
            string vSearch = (aSearch == null ? "" : aSearch).Trim();
            string vSQL;
            if (vSearch == "")
                vSQL = "SELECT id, name, email, phone, loyalty_points, " +
                    "created_at FROM customers ORDER BY name";
            else
                vSQL = "SELECT id, name, email, phone, loyalty_points, " +
                    "created_at FROM customers WHERE name LIKE :q ESCAPE '\\' " +
                    "OR email LIKE :q ESCAPE '\\' OR phone LIKE :q ESCAPE '\\' " +
                    "ORDER BY name";
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (vSearch != "")
                    AddParam(oCmd, ":q", "%" + EscapeLikeValue(vSearch) + "%");
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSCustomer vRow = new TPOSCustomer();
                        vRow.Id = RInt64(oReader, "id");
                        vRow.Name = RStr(oReader, "name");
                        vRow.Email = RStr(oReader, "email");
                        vRow.Phone = RStr(oReader, "phone");
                        vRow.LoyaltyPoints = RInt(oReader, "loyalty_points");
                        vRow.CreatedAt = RDate(oReader, "created_at");
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        public bool GetCustomer(long aId, out TPOSCustomer aCust)
        {
            aCust = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, name, email, phone, loyalty_points, " +
                "created_at FROM customers WHERE id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aCust = new TPOSCustomer();
                    aCust.Id = RInt64(oReader, "id");
                    aCust.Name = RStr(oReader, "name");
                    aCust.Email = RStr(oReader, "email");
                    aCust.Phone = RStr(oReader, "phone");
                    aCust.LoyaltyPoints = RInt(oReader, "loyalty_points");
                    aCust.CreatedAt = RDate(oReader, "created_at");
                    return true;
                }
            }
        }

        public long SaveCustomer(long aId, string aName, string aEmail, string aPhone,
            int aPoints)
        {
            string vName = (aName == null ? "" : aName).Trim();
            if (vName.Length == 0)
                return 0;
            string vEmail = (aEmail == null ? "" : aEmail).Trim();
            string vPhone = (aPhone == null ? "" : aPhone).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                if (aId > 0)
                {
                    ExecNonQuery(oConn, null,
                        "UPDATE customers SET name = :n, email = :e, " +
                        "phone = :p, loyalty_points = :l WHERE id = :i",
                        ":n", vName, ":e", vEmail, ":p", vPhone, ":l", aPoints,
                        ":i", aId);
                    return aId;
                }
                ExecNonQuery(oConn, null,
                    "INSERT INTO customers (name, email, phone, " +
                    "loyalty_points, created_at) VALUES (:n, :e, :p, :l, :c)",
                    ":n", vName, ":e", vEmail, ":p", vPhone, ":l", aPoints,
                    ":c", POSDB.POSNowTimestamp());
                return POSLastId(oConn);
            }
        }

        // ================================================================== //
        //  promotions                                                        //
        // ================================================================== //

        public TPOSPromotion[] ListPromotions()
        {
            List<TPOSPromotion> oResult = new List<TPOSPromotion>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, name, kind, value, product_id, " +
                "category_id, starts_at, ends_at, active FROM promotions " +
                "ORDER BY active DESC, starts_at DESC"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
                while (oReader.Read())
                {
                    TPOSPromotion vRow = new TPOSPromotion();
                    vRow.Id = RInt64(oReader, "id");
                    vRow.Name = RStr(oReader, "name");
                    vRow.Kind = RStr(oReader, "kind");
                    vRow.Value = RFloat(oReader, "value");
                    vRow.ProductId = RInt64(oReader, "product_id");
                    vRow.CategoryId = RInt64(oReader, "category_id");
                    vRow.StartsAt = RDate(oReader, "starts_at");
                    vRow.EndsAt = RDate(oReader, "ends_at");
                    vRow.Active = RInt(oReader, "active") != 0;
                    oResult.Add(vRow);
                }
            return oResult.ToArray();
        }

        public long SavePromotion(TPOSPromotion aPromo)
        {
            if ((aPromo == null) || (aPromo.Name == null) ||
                (aPromo.Name.Trim().Length == 0))
                return 0;
            string vKind = (aPromo.Kind == null ? "" : aPromo.Kind).Trim()
                .ToLowerInvariant();
            if ((vKind != "percent") && (vKind != "amount") && (vKind != "bundle"))
                vKind = "percent";
            string vSQL;
            if (aPromo.Id > 0)
                vSQL = "UPDATE promotions SET name = :n, kind = :k, " +
                    "value = :v, product_id = :p, category_id = :c, starts_at = :s, " +
                    "ends_at = :e, active = :a WHERE id = :i";
            else
                vSQL = "INSERT INTO promotions (name, kind, value, " +
                    "product_id, category_id, starts_at, ends_at, active) " +
                    "VALUES (:n, :k, :v, :p, :c, :s, :e, :a)";
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (aPromo.Id > 0)
                        AddParam(oCmd, ":i", aPromo.Id);
                    AddParam(oCmd, ":n", aPromo.Name.Trim());
                    AddParam(oCmd, ":k", vKind);
                    AddParam(oCmd, ":v", aPromo.Value);
                    AddParam(oCmd, ":p", aPromo.ProductId);
                    AddParam(oCmd, ":c", aPromo.CategoryId);
                    AddParam(oCmd, ":s", POSDB.POSTimestamp(aPromo.StartsAt));
                    AddParam(oCmd, ":e", POSDB.POSTimestamp(aPromo.EndsAt));
                    AddParam(oCmd, ":a", aPromo.Active ? 1 : 0);
                    oCmd.ExecuteNonQuery();
                }
                if (aPromo.Id > 0)
                    return aPromo.Id;
                return POSLastId(oConn);
            }
        }

        // ================================================================== //
        //  the till: cart lifecycle                                          //
        // ================================================================== //

        // The money engine, and the only place the sale header is ever written.
        //
        // lines + tax - discount = total, to the cent, on every locale:
        //
        // - subtotal is the sum of the line totals, each of which is already
        //   round(qty * unit_price) minus its own line discount.
        // - the whole-sale discount is spread across the lines in proportion to
        //   their net, with the LAST line absorbing the rounding remainder.
        //   Without that remainder step the allocated parts can miss the
        //   discount by a cent and the identity above stops holding.
        // - each line is then taxed on what the customer actually pays for it.
        // - total = subtotal - discount + tax, by construction.
        //
        // Every value is decimal, so none of this ever meets a binary float and
        // none of it depends on the thread locale. It runs on a caller-owned
        // connection and transaction, so a mutation and its recalculation are
        // one atomic step.
        private void RecalcSaleConn(SqliteConnection aConn, SqliteTransaction aTx,
            long aSaleId)
        {
            if (aSaleId <= 0)
                return;

            decimal vDiscount;
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "SELECT discount FROM sales WHERE id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aSaleId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return;
                    vDiscount = POSDB.POSRoundCents(RCur(oReader, "discount"));
                }
            }
            if (vDiscount < 0)
                vDiscount = 0;

            List<decimal> oNet = new List<decimal>();
            List<double> oRate = new List<double>();
            using (SqliteCommand oCmd = NewCmd(aConn, aTx,
                "SELECT line_total, tax_rate FROM sale_lines " +
                "WHERE sale_id = :i ORDER BY id"))
            {
                AddParam(oCmd, ":i", aSaleId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        oNet.Add(POSDB.POSRoundCents(RCur(oReader, "line_total")));
                        oRate.Add(RFloat(oReader, "tax_rate"));
                    }
            }

            int vCount = oNet.Count;
            decimal vSubtotal = 0;
            for (int vI = 0; vI < vCount; vI++)
                vSubtotal = vSubtotal + oNet[vI];

            // A discount can never exceed the basket, whatever the browser
            // posted.
            if (vDiscount > vSubtotal)
                vDiscount = vSubtotal;

            decimal vTax = 0;
            decimal vAllocated = 0;
            for (int vI = 0; vI < vCount; vI++)
            {
                decimal vLineDisc;
                if ((vDiscount == 0) || (vSubtotal == 0))
                    vLineDisc = 0;
                else if (vI == vCount - 1)
                    vLineDisc = vDiscount - vAllocated;
                else
                {
                    vLineDisc = POSDB.POSRoundCents(vDiscount * (oNet[vI] / vSubtotal));
                    vAllocated = vAllocated + vLineDisc;
                }
                vTax = vTax + POSDB.POSRoundCents((oNet[vI] - vLineDisc) *
                    (decimal)oRate[vI] / 100m);
            }

            decimal vTotal = vSubtotal - vDiscount + vTax;

            ExecNonQuery(aConn, aTx,
                "UPDATE sales SET subtotal = :s, discount = :d, " +
                "tax = :t, total = :o WHERE id = :i",
                ":s", (double)vSubtotal, ":d", (double)vDiscount,
                ":t", (double)vTax, ":o", (double)vTotal, ":i", aSaleId);
        }

        // Re-derives subtotal / tax / total from the lines. Every mutation
        // calls it, so the header can never drift from the lines.
        public void RecalcSale(long aSaleId)
        {
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                RecalcSaleConn(oConn, oTx, aSaleId);
                oTx.Commit();
            }
        }

        // The row the cashier is building right now (status 'open'), created on
        // first use. One per user, so two cashiers never share a basket.
        public long GetOrCreateOpenSale(long aUserId, long aShiftId)
        {
            if (aUserId <= 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
            {
                long vResult = 0;
                // Scoped to the caller's own user id: one basket per cashier,
                // and never somebody else's.
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT id FROM sales WHERE user_id = :u AND " +
                    "status = 'open' ORDER BY id DESC LIMIT 1"))
                {
                    AddParam(oCmd, ":u", aUserId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        if (oReader.Read())
                            vResult = RInt64(oReader, "id");
                }
                if (vResult > 0)
                {
                    // A basket started before the drawer was opened has shift_id
                    // 0. Attach it now, otherwise its cash would never reach the
                    // shift's expected total and the count would come up short.
                    if (aShiftId > 0)
                        ExecNonQuery(oConn, null,
                            "UPDATE sales SET shift_id = :s " +
                            "WHERE id = :i AND (shift_id IS NULL OR shift_id = 0)",
                            ":s", aShiftId, ":i", vResult);
                    return vResult;
                }

                ExecNonQuery(oConn, null,
                    "INSERT INTO sales (reference, user_id, " +
                    "customer_id, shift_id, subtotal, discount, tax, total, status, " +
                    "created_at) VALUES ('', :u, 0, :s, 0, 0, 0, 0, 'open', :c)",
                    ":u", aUserId, ":s", aShiftId, ":c", POSDB.POSNowTimestamp());
                return POSLastId(oConn);
            }
        }

        public bool GetSale(long aId, out TPOSSale aSale)
        {
            aSale = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT s.id, s.reference, s.user_id, " +
                "s.customer_id, s.shift_id, s.subtotal, s.discount, s.tax, s.total, " +
                "s.status, s.created_at, u.display_name AS user_name, " +
                "c.name AS customer_name, " +
                "(SELECT COUNT(*) FROM sale_lines l WHERE l.sale_id = s.id) " +
                "AS line_count FROM sales s " +
                "LEFT JOIN users u ON u.id = s.user_id " +
                "LEFT JOIN customers c ON c.id = s.customer_id " +
                "WHERE s.id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillSaleFromQuery(oReader, out aSale);
                    return true;
                }
            }
        }

        public TPOSSaleLine[] GetSaleLines(long aSaleId)
        {
            List<TPOSSaleLine> oResult = new List<TPOSSaleLine>();
            if (aSaleId <= 0)
                return oResult.ToArray();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT l.id, l.sale_id, l.product_id, l.qty, " +
                "l.unit_price, l.discount, l.tax_rate, l.line_total, " +
                "p.name AS product_name, p.sku AS sku FROM sale_lines l " +
                "LEFT JOIN products p ON p.id = l.product_id " +
                "WHERE l.sale_id = :i ORDER BY l.id"))
            {
                AddParam(oCmd, ":i", aSaleId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSSaleLine vRow = new TPOSSaleLine();
                        vRow.Id = RInt64(oReader, "id");
                        vRow.SaleId = RInt64(oReader, "sale_id");
                        vRow.ProductId = RInt64(oReader, "product_id");
                        vRow.Qty = RFloat(oReader, "qty");
                        vRow.UnitPrice = RCur(oReader, "unit_price");
                        vRow.Discount = RCur(oReader, "discount");
                        vRow.TaxRate = RFloat(oReader, "tax_rate");
                        vRow.LineTotal = RCur(oReader, "line_total");
                        vRow.ProductName = RStr(oReader, "product_name");
                        vRow.Sku = RStr(oReader, "sku");
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        public TPOSSale[] ListSales(string aStatus, long aUserId, DateTime aFrom,
            DateTime aTo, int aLimit)
        {
            List<TPOSSale> oResult = new List<TPOSSale>();
            string vStatus = (aStatus == null ? "" : aStatus).Trim().ToLowerInvariant();
            int vLimit = aLimit;
            if (vLimit <= 0)
                vLimit = 200;
            bool vHasStatus = (vStatus == POSConst.CS_SALE_OPEN) ||
                (vStatus == POSConst.CS_SALE_PARKED) ||
                (vStatus == POSConst.CS_SALE_COMPLETED) ||
                (vStatus == POSConst.CS_SALE_REFUNDED);
            string vSQL = "SELECT s.id, s.reference, s.user_id, s.customer_id, " +
                "s.shift_id, s.subtotal, s.discount, s.tax, s.total, s.status, " +
                "s.created_at, u.display_name AS user_name, " +
                "c.name AS customer_name, " +
                "(SELECT COUNT(*) FROM sale_lines l WHERE l.sale_id = s.id) " +
                "AS line_count FROM sales s " +
                "LEFT JOIN users u ON u.id = s.user_id " +
                "LEFT JOIN customers c ON c.id = s.customer_id WHERE 1 = 1";
            // Whitelisted: only the four known states can reach the SQL.
            if (vHasStatus)
                vSQL = vSQL + " AND s.status = :st";
            if (aUserId > 0)
                vSQL = vSQL + " AND s.user_id = :us";
            if (aFrom > DateTime.MinValue)
                vSQL = vSQL + " AND s.created_at >= :fr";
            if (aTo > DateTime.MinValue)
                vSQL = vSQL + " AND s.created_at < :to";
            vSQL = vSQL + " ORDER BY s.created_at DESC, s.id DESC LIMIT :lm";
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
            {
                if (vHasStatus)
                    AddParam(oCmd, ":st", vStatus);
                if (aUserId > 0)
                    AddParam(oCmd, ":us", aUserId);
                if (aFrom > DateTime.MinValue)
                    AddParam(oCmd, ":fr", POSDB.POSTimestamp(aFrom));
                if (aTo > DateTime.MinValue)
                    AddParam(oCmd, ":to", POSDB.POSTimestamp(aTo));
                AddParam(oCmd, ":lm", vLimit);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSSale vRow;
                        FillSaleFromQuery(oReader, out vRow);
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        // Add aQty of aProductId, merging into the existing line when present.
        public bool AddLine(long aSaleId, long aProductId, double aQty)
        {
            if ((aSaleId <= 0) || (aProductId <= 0))
                return false;
            double vAdd = aQty;
            if (vAdd <= 0)
                vAdd = 1;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                decimal vPrice;
                double vRate;
                // The price is read from the catalogue, never from the request:
                // a browser must not be able to name its own price.
                using (SqliteCommand oCmd = NewCmd(oConn, oTx,
                    "SELECT price, tax_rate FROM products " +
                    "WHERE id = :p AND active = 1 LIMIT 1"))
                {
                    AddParam(oCmd, ":p", aProductId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (!oReader.Read())
                            return false;
                        vPrice = RCur(oReader, "price");
                        vRate = RFloat(oReader, "tax_rate");
                    }
                }

                long vLineId;
                double vQty;
                decimal vDisc;
                using (SqliteCommand oCmd = NewCmd(oConn, oTx,
                    "SELECT id, qty, discount FROM sale_lines " +
                    "WHERE sale_id = :s AND product_id = :p ORDER BY id LIMIT 1"))
                {
                    AddParam(oCmd, ":s", aSaleId);
                    AddParam(oCmd, ":p", aProductId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        if (!oReader.Read())
                        {
                            vLineId = 0;
                            vQty = 0;
                            vDisc = 0;
                        }
                        else
                        {
                            vLineId = RInt64(oReader, "id");
                            vQty = RFloat(oReader, "qty");
                            vDisc = RCur(oReader, "discount");
                        }
                }

                vQty = vQty + vAdd;
                decimal vLineTotal = POSDB.POSRoundCents(vPrice * (decimal)vQty) - vDisc;
                if (vLineId > 0)
                    ExecNonQuery(oConn, oTx,
                        "UPDATE sale_lines SET qty = :q, unit_price = :u, " +
                        "tax_rate = :t, line_total = :l WHERE id = :i AND sale_id = :s",
                        ":i", vLineId, ":s", aSaleId, ":q", vQty, ":u", (double)vPrice,
                        ":t", vRate, ":l", (double)vLineTotal);
                else
                    ExecNonQuery(oConn, oTx,
                        "INSERT INTO sale_lines (sale_id, product_id, " +
                        "qty, unit_price, discount, tax_rate, line_total) " +
                        "VALUES (:s, :p, :q, :u, :d, :t, :l)",
                        ":s", aSaleId, ":p", aProductId, ":d", 0.0, ":q", vQty,
                        ":u", (double)vPrice, ":t", vRate, ":l", (double)vLineTotal);

                RecalcSaleConn(oConn, oTx, aSaleId);
                oTx.Commit();
                return true;
            }
        }

        public bool SetLineQty(long aSaleId, long aLineId, double aQty)
        {
            if ((aSaleId <= 0) || (aLineId <= 0))
                return false;
            if (aQty <= 0)
                return RemoveLine(aSaleId, aLineId);
            double vQty = aQty;
            if (vQty > 999)
                vQty = 999;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                decimal vPrice;
                decimal vDisc;
                // Every lookup is scoped by sale_id as well as line id, so a
                // guessed line id from another basket resolves to nothing.
                using (SqliteCommand oCmd = NewCmd(oConn, oTx,
                    "SELECT unit_price, discount FROM sale_lines " +
                    "WHERE id = :i AND sale_id = :s LIMIT 1"))
                {
                    AddParam(oCmd, ":i", aLineId);
                    AddParam(oCmd, ":s", aSaleId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (!oReader.Read())
                            return false;
                        vPrice = RCur(oReader, "unit_price");
                        vDisc = RCur(oReader, "discount");
                    }
                }

                bool vResult = ExecNonQuery(oConn, oTx,
                    "UPDATE sale_lines SET qty = :q, line_total = :l " +
                    "WHERE id = :i AND sale_id = :s",
                    ":q", vQty,
                    ":l", (double)(POSDB.POSRoundCents(vPrice * (decimal)vQty) - vDisc),
                    ":i", aLineId, ":s", aSaleId) > 0;
                RecalcSaleConn(oConn, oTx, aSaleId);
                oTx.Commit();
                return vResult;
            }
        }

        public bool RemoveLine(long aSaleId, long aLineId)
        {
            if ((aSaleId <= 0) || (aLineId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                bool vResult = ExecNonQuery(oConn, oTx,
                    "DELETE FROM sale_lines WHERE id = :i AND sale_id = :s",
                    ":i", aLineId, ":s", aSaleId) > 0;
                RecalcSaleConn(oConn, oTx, aSaleId);
                oTx.Commit();
                return vResult;
            }
        }

        public bool SetLineDiscount(long aSaleId, long aLineId, decimal aAmount)
        {
            if ((aSaleId <= 0) || (aLineId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                decimal vPrice;
                double vQty;
                using (SqliteCommand oCmd = NewCmd(oConn, oTx,
                    "SELECT unit_price, qty FROM sale_lines " +
                    "WHERE id = :i AND sale_id = :s LIMIT 1"))
                {
                    AddParam(oCmd, ":i", aLineId);
                    AddParam(oCmd, ":s", aSaleId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (!oReader.Read())
                            return false;
                        vPrice = RCur(oReader, "unit_price");
                        vQty = RFloat(oReader, "qty");
                    }
                }

                decimal vGross = POSDB.POSRoundCents(vPrice * (decimal)vQty);
                decimal vDisc = POSDB.POSRoundCents(aAmount);
                if (vDisc < 0)
                    vDisc = 0;
                // A line discount can never make the line negative.
                if (vDisc > vGross)
                    vDisc = vGross;

                bool vResult = ExecNonQuery(oConn, oTx,
                    "UPDATE sale_lines SET discount = :d, " +
                    "line_total = :l WHERE id = :i AND sale_id = :s",
                    ":d", (double)vDisc, ":l", (double)(vGross - vDisc),
                    ":i", aLineId, ":s", aSaleId) > 0;
                RecalcSaleConn(oConn, oTx, aSaleId);
                oTx.Commit();
                return vResult;
            }
        }

        public bool SetSaleDiscount(long aSaleId, decimal aAmount)
        {
            if (aSaleId <= 0)
                return false;
            decimal vDisc = POSDB.POSRoundCents(aAmount);
            if (vDisc < 0)
                vDisc = 0;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                bool vResult = ExecNonQuery(oConn, oTx,
                    "UPDATE sales SET discount = :d WHERE id = :i AND status = 'open'",
                    ":d", (double)vDisc, ":i", aSaleId) > 0;
                // RecalcSaleConn caps the discount at the basket subtotal and
                // rewrites the header, so an over-large value posted here cannot
                // survive.
                RecalcSaleConn(oConn, oTx, aSaleId);
                oTx.Commit();
                return vResult;
            }
        }

        public bool SetSaleCustomer(long aSaleId, long aCustomerId)
        {
            if (aSaleId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
                return ExecNonQuery(oConn, null,
                    "UPDATE sales SET customer_id = :c WHERE id = :i " +
                    "AND status = 'open'",
                    ":c", aCustomerId, ":i", aSaleId) > 0;
        }

        public bool ParkSale(long aSaleId)
        {
            if (aSaleId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                int vLines = ScalarInt(oConn,
                    "SELECT COUNT(*) FROM sale_lines WHERE sale_id = :s",
                    new string[] { "s" }, new long[] { aSaleId });
                // An empty basket is not worth parking, and a parked empty sale
                // would clutter the recall list forever.
                if (vLines == 0)
                    return false;
                return ExecNonQuery(oConn, null,
                    "UPDATE sales SET status = 'parked' " +
                    "WHERE id = :i AND status = 'open'", ":i", aSaleId) > 0;
            }
        }

        public TPOSSale[] ListParkedSales(long aUserId)
        {
            return ListSales(POSConst.CS_SALE_PARKED, aUserId, DateTime.MinValue,
                DateTime.MinValue, 50);
        }

        public bool RecallSale(long aSaleId, long aUserId)
        {
            if ((aSaleId <= 0) || (aUserId <= 0))
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                // Park whatever is on the till first, so recalling never
                // silently discards a basket the cashier already started.
                ExecNonQuery(oConn, oTx,
                    "UPDATE sales SET status = 'parked' " +
                    "WHERE user_id = :u AND status = 'open' AND " +
                    "id IN (SELECT sale_id FROM sale_lines)", ":u", aUserId);
                // An open basket with no lines is just noise; drop it.
                ExecNonQuery(oConn, oTx,
                    "DELETE FROM sales WHERE user_id = :u AND status = 'open'",
                    ":u", aUserId);

                bool vResult = ExecNonQuery(oConn, oTx,
                    "UPDATE sales SET status = 'open', user_id = :u " +
                    "WHERE id = :i AND status = 'parked'",
                    ":u", aUserId, ":i", aSaleId) > 0;
                if (vResult)
                    RecalcSaleConn(oConn, oTx, aSaleId);
                oTx.Commit();
                return vResult;
            }
        }

        // ================================================================== //
        //  payment                                                           //
        // ================================================================== //

        public void AddPayment(long aSaleId, string aMethod, decimal aAmount,
            decimal aChange)
        {
            if (aSaleId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
                ExecNonQuery(oConn, null,
                    "INSERT INTO payments (sale_id, method, amount, " +
                    "change_given, created_at) VALUES (:s, :m, :a, :c, :d)",
                    ":s", aSaleId, ":m", CoerceMethod(aMethod),
                    ":a", (double)POSDB.POSRoundCents(aAmount),
                    ":c", (double)POSDB.POSRoundCents(aChange),
                    ":d", POSDB.POSNowTimestamp());
        }

        public TPOSPayment[] ListPayments(long aSaleId)
        {
            List<TPOSPayment> oResult = new List<TPOSPayment>();
            if (aSaleId <= 0)
                return oResult.ToArray();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, sale_id, method, amount, change_given, " +
                "created_at FROM payments WHERE sale_id = :i ORDER BY id"))
            {
                AddParam(oCmd, ":i", aSaleId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSPayment vRow = new TPOSPayment();
                        vRow.Id = RInt64(oReader, "id");
                        vRow.SaleId = RInt64(oReader, "sale_id");
                        vRow.Method = RStr(oReader, "method");
                        vRow.Amount = RCur(oReader, "amount");
                        vRow.ChangeGiven = RCur(oReader, "change_given");
                        vRow.CreatedAt = RDate(oReader, "created_at");
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        public decimal SumPayments(long aSaleId)
        {
            using (SqliteConnection oConn = Acquire())
                // amount - change_given is what the drawer actually keeps, which
                // is the number that has to equal the sale total.
                return ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(amount - change_given), 0) FROM payments " +
                    "WHERE sale_id = :s",
                    new string[] { "s" }, new long[] { aSaleId });
        }

        // Marks the sale completed, stamps its reference, decrements stock and
        // credits loyalty points. Returns the reference.
        public string CompleteSale(long aSaleId)
        {
            if (aSaleId <= 0)
                return "";
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                decimal vTotal;
                long vCustomerId;
                using (SqliteCommand oCmd = NewCmd(oConn, oTx,
                    "SELECT total, customer_id FROM sales " +
                    "WHERE id = :i AND status = 'open' LIMIT 1"))
                {
                    AddParam(oCmd, ":i", aSaleId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (!oReader.Read())
                        {
                            oTx.Rollback();
                            return "";
                        }
                        vTotal = RCur(oReader, "total");
                        vCustomerId = RInt64(oReader, "customer_id");
                    }
                }

                string vRef = "R" + DateTime.Now.ToString("yyyyMMdd",
                    CultureInfo.InvariantCulture) + "-" +
                    aSaleId.ToString("D5", CultureInfo.InvariantCulture);
                ExecNonQuery(oConn, oTx,
                    "UPDATE sales SET status = 'completed', " +
                    "reference = :r, created_at = :c WHERE id = :i",
                    ":r", vRef, ":c", POSDB.POSNowTimestamp(), ":i", aSaleId);

                ExecNonQuery(oConn, oTx,
                    "UPDATE products SET stock = stock - " +
                    "(SELECT COALESCE(SUM(l.qty), 0) FROM sale_lines l " +
                    "WHERE l.sale_id = :s AND l.product_id = products.id) " +
                    "WHERE id IN (SELECT product_id FROM sale_lines WHERE sale_id = :s)",
                    ":s", aSaleId);

                // One loyalty point per whole unit of currency spent.
                if (vCustomerId > 0)
                    ExecNonQuery(oConn, oTx,
                        "UPDATE customers SET loyalty_points = " +
                        "loyalty_points + :p WHERE id = :i",
                        ":p", (int)decimal.Truncate(vTotal), ":i", vCustomerId);

                oTx.Commit();
                return vRef;
            }
        }

        // Manager-authorised refund: flips the status, restores stock and writes
        // a negative cash payment row so the shift's expected cash stays correct.
        public bool RefundSale(long aSaleId, long aManagerId)
        {
            if (aSaleId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                decimal vTotal;
                // Only a completed sale can be refunded, and only once.
                using (SqliteCommand oCmd = NewCmd(oConn, oTx,
                    "SELECT total FROM sales WHERE id = :i AND " +
                    "status = 'completed' LIMIT 1"))
                {
                    AddParam(oCmd, ":i", aSaleId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (!oReader.Read())
                        {
                            oTx.Rollback();
                            return false;
                        }
                        vTotal = RCur(oReader, "total");
                    }
                }

                // Money goes back the way it came in, and cash wins a split: if
                // any of it was cash, the customer gets cash back, which is what
                // actually leaves the drawer.
                string vMethod;
                using (SqliteCommand oCmd = NewCmd(oConn, oTx,
                    "SELECT method FROM payments WHERE sale_id = :i " +
                    "AND amount > 0 ORDER BY CASE WHEN method = 'cash' THEN 0 " +
                    "ELSE 1 END, id LIMIT 1"))
                {
                    AddParam(oCmd, ":i", aSaleId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        if (!oReader.Read())
                            vMethod = POSConst.CS_PAY_CASH;
                        else
                            vMethod = RStr(oReader, "method");
                }

                ExecNonQuery(oConn, oTx,
                    "UPDATE sales SET status = 'refunded' WHERE id = :i",
                    ":i", aSaleId);

                // The refund is a negative payment row, so the shift's expected
                // cash stays derivable from the payments table alone.
                ExecNonQuery(oConn, oTx,
                    "INSERT INTO payments (sale_id, method, amount, " +
                    "change_given, created_at) VALUES (:s, :m, :a, 0, :d)",
                    ":s", aSaleId, ":m", CoerceMethod(vMethod), ":a", (double)(-vTotal),
                    ":d", POSDB.POSNowTimestamp());

                ExecNonQuery(oConn, oTx,
                    "UPDATE products SET stock = stock + " +
                    "(SELECT COALESCE(SUM(l.qty), 0) FROM sale_lines l " +
                    "WHERE l.sale_id = :s AND l.product_id = products.id) " +
                    "WHERE id IN (SELECT product_id FROM sale_lines WHERE sale_id = :s)",
                    ":s", aSaleId);

                oTx.Commit();
                return true;
            }
        }

        // ================================================================== //
        //  shifts                                                            //
        // ================================================================== //

        public bool GetOpenShift(long aUserId, out TPOSShift aShift)
        {
            aShift = null;
            if (aUserId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT s.id, s.user_id, s.opened_at, s.closed_at, " +
                "s.opening_float, s.counted_cash, s.expected_cash, s.variance, " +
                "s.status, u.display_name AS user_name FROM shifts s " +
                "LEFT JOIN users u ON u.id = s.user_id " +
                "WHERE s.user_id = :u AND s.status = 'open' ORDER BY s.id DESC " +
                "LIMIT 1"))
            {
                AddParam(oCmd, ":u", aUserId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillShiftFromQuery(oReader, out aShift);
                    return true;
                }
            }
        }

        public bool GetShift(long aId, out TPOSShift aShift)
        {
            aShift = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT s.id, s.user_id, s.opened_at, s.closed_at, " +
                "s.opening_float, s.counted_cash, s.expected_cash, s.variance, " +
                "s.status, u.display_name AS user_name FROM shifts s " +
                "LEFT JOIN users u ON u.id = s.user_id WHERE s.id = :i LIMIT 1"))
            {
                AddParam(oCmd, ":i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    FillShiftFromQuery(oReader, out aShift);
                    return true;
                }
            }
        }

        public long OpenShift(long aUserId, decimal aFloat)
        {
            if (aUserId <= 0)
                return 0;
            decimal vFloat = aFloat;
            if (vFloat < 0)
                vFloat = 0;
            using (SqliteConnection oConn = Acquire())
            {
                int vOpen = ScalarInt(oConn,
                    "SELECT COUNT(*) FROM shifts WHERE user_id = :u AND status = 'open'",
                    new string[] { "u" }, new long[] { aUserId });
                // One open shift per cashier: opening a second one would split
                // the takings across two drawers that both claim the same cash.
                if (vOpen > 0)
                    return 0;
                ExecNonQuery(oConn, null,
                    "INSERT INTO shifts (user_id, opened_at, " +
                    "closed_at, opening_float, counted_cash, expected_cash, variance, " +
                    "status) VALUES (:u, :o, '', :f, 0, 0, 0, 'open')",
                    ":u", aUserId, ":o", POSDB.POSNowTimestamp(),
                    ":f", (double)POSDB.POSRoundCents(vFloat));
                return POSLastId(oConn);
            }
        }

        // Expected cash is computed from the RECORDED payments of the shift,
        // never from anything the browser sends: opening float + cash in -
        // change out.
        public decimal ComputeExpectedCash(long aShiftId)
        {
            if (aShiftId <= 0)
                return 0m;
            using (SqliteConnection oConn = Acquire())
                // Opening float plus the cash the drawer actually kept. Derived
                // from the recorded payment rows, never from anything the
                // browser sends, which is the entire point of a cash count.
                return ScalarCurrency(oConn,
                    "SELECT COALESCE(opening_float, 0) FROM shifts WHERE id = :sid",
                    new string[] { "sid" }, new long[] { aShiftId }) +
                    ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(p.amount - p.change_given), 0) FROM payments p " +
                    "INNER JOIN sales s ON s.id = p.sale_id " +
                    "WHERE s.shift_id = :sid AND p.method = 'cash'",
                    new string[] { "sid" }, new long[] { aShiftId });
        }

        public bool CloseShift(long aShiftId, decimal aCounted)
        {
            if (aShiftId <= 0)
                return false;
            decimal vExpected = ComputeExpectedCash(aShiftId);
            using (SqliteConnection oConn = Acquire())
                return ExecNonQuery(oConn, null,
                    "UPDATE shifts SET closed_at = :c, " +
                    "counted_cash = :n, expected_cash = :e, variance = :v, " +
                    "status = 'closed' WHERE id = :i AND status = 'open'",
                    ":c", POSDB.POSNowTimestamp(),
                    ":n", (double)POSDB.POSRoundCents(aCounted),
                    ":e", (double)vExpected,
                    ":v", (double)(POSDB.POSRoundCents(aCounted) - vExpected),
                    ":i", aShiftId) > 0;
        }

        public TPOSShift[] ListShifts(int aLimit)
        {
            List<TPOSShift> oResult = new List<TPOSShift>();
            int vLimit = aLimit;
            if (vLimit <= 0)
                vLimit = 40;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT s.id, s.user_id, s.opened_at, s.closed_at, " +
                "s.opening_float, s.counted_cash, s.expected_cash, s.variance, " +
                "s.status, u.display_name AS user_name FROM shifts s " +
                "LEFT JOIN users u ON u.id = s.user_id " +
                "ORDER BY s.id DESC LIMIT :lm"))
            {
                AddParam(oCmd, ":lm", vLimit);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSShift vRow;
                        FillShiftFromQuery(oReader, out vRow);
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        // The cash a shift took by payment method, straight from the payment
        // rows that belong to its sales.
        private static decimal ShiftPaySum(SqliteConnection aConn, long aShiftId,
            string aMethod)
        {
            using (SqliteCommand oCmd = NewCmd(aConn,
                "SELECT COALESCE(SUM(p.amount - p.change_given), 0) FROM payments p " +
                "INNER JOIN sales s ON s.id = p.sale_id " +
                "WHERE s.shift_id = :sid AND p.method = :m"))
            {
                AddParam(oCmd, ":sid", aShiftId);
                AddParam(oCmd, ":m", aMethod);
                object vValue = oCmd.ExecuteScalar();
                if ((vValue == null) || (vValue == DBNull.Value))
                    return 0m;
                return ToCurrency(vValue);
            }
        }

        public TPOSTotals ShiftTotals(long aShiftId)
        {
            TPOSTotals oResult = new TPOSTotals();
            if (aShiftId <= 0)
                return oResult;
            string[] vNames = new string[] { "sid" };
            long[] vValues = new long[] { aShiftId };
            using (SqliteConnection oConn = Acquire())
            {
                oResult.SaleCount = ScalarInt(oConn, "SELECT COUNT(*) FROM sales " +
                    "WHERE shift_id = :sid AND status = 'completed'", vNames, vValues);
                oResult.Gross = ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(total), 0) FROM sales WHERE shift_id = :sid " +
                    "AND status = 'completed'", vNames, vValues);
                oResult.NetSubtotal = ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(subtotal), 0) FROM sales WHERE shift_id = :sid " +
                    "AND status = 'completed'", vNames, vValues);
                oResult.Discount = ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(discount), 0) FROM sales WHERE shift_id = :sid " +
                    "AND status = 'completed'", vNames, vValues);
                oResult.Tax = ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(tax), 0) FROM sales WHERE shift_id = :sid " +
                    "AND status = 'completed'", vNames, vValues);
                oResult.RefundCount = ScalarInt(oConn, "SELECT COUNT(*) FROM sales " +
                    "WHERE shift_id = :sid AND status = 'refunded'", vNames, vValues);
                oResult.Refunded = ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(total), 0) FROM sales WHERE shift_id = :sid " +
                    "AND status = 'refunded'", vNames, vValues);
                oResult.CashTaken = ShiftPaySum(oConn, aShiftId, POSConst.CS_PAY_CASH);
                oResult.CardTaken = ShiftPaySum(oConn, aShiftId, POSConst.CS_PAY_CARD);
                oResult.VoucherTaken = ShiftPaySum(oConn, aShiftId,
                    POSConst.CS_PAY_VOUCHER);
                oResult.LoyaltyTaken = ShiftPaySum(oConn, aShiftId,
                    POSConst.CS_PAY_LOYALTY);
                oResult.ChangeGiven = ScalarCurrency(oConn,
                    "SELECT COALESCE(SUM(p.change_given), 0) FROM payments p " +
                    "INNER JOIN sales s ON s.id = p.sale_id WHERE s.shift_id = :sid",
                    vNames, vValues);
            }
            return oResult;
        }

        // ================================================================== //
        //  analytics                                                         //
        // ================================================================== //

        // One aggregate over the sales of a period, in a given status.
        private static void RunSalesTotals(SqliteConnection aConn, string aSelect,
            string aStatus, DateTime aFrom, DateTime aTo, ref decimal aValue,
            ref int aCount)
        {
            using (SqliteCommand oCmd = NewCmd(aConn,
                "SELECT " + aSelect + " AS v, COUNT(*) AS n " +
                "FROM sales WHERE status = :st AND created_at >= :f AND created_at < :t"))
            {
                AddParam(oCmd, ":st", aStatus);
                AddParam(oCmd, ":f", POSDB.POSTimestamp(aFrom));
                AddParam(oCmd, ":t", POSDB.POSTimestamp(aTo));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    if (oReader.Read())
                    {
                        aValue = RCur(oReader, "v");
                        aCount = RInt(oReader, "n");
                    }
            }
        }

        private static decimal RangePaySum(SqliteConnection aConn, string aMethod,
            DateTime aFrom, DateTime aTo)
        {
            using (SqliteCommand oCmd = NewCmd(aConn,
                "SELECT COALESCE(SUM(p.amount - p.change_given), 0) AS v FROM payments p " +
                "INNER JOIN sales s ON s.id = p.sale_id " +
                "WHERE p.method = :m AND s.created_at >= :f AND s.created_at < :t"))
            {
                AddParam(oCmd, ":m", aMethod);
                AddParam(oCmd, ":f", POSDB.POSTimestamp(aFrom));
                AddParam(oCmd, ":t", POSDB.POSTimestamp(aTo));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    if (oReader.Read())
                        return RCur(oReader, "v");
            }
            return 0m;
        }

        public TPOSTotals TotalsBetween(DateTime aFrom, DateTime aTo)
        {
            TPOSTotals oResult = new TPOSTotals();
            using (SqliteConnection oConn = Acquire())
            {
                int vDummy = 0;
                RunSalesTotals(oConn, "COALESCE(SUM(total), 0)",
                    POSConst.CS_SALE_COMPLETED, aFrom, aTo, ref oResult.Gross,
                    ref oResult.SaleCount);
                RunSalesTotals(oConn, "COALESCE(SUM(subtotal), 0)",
                    POSConst.CS_SALE_COMPLETED, aFrom, aTo, ref oResult.NetSubtotal,
                    ref vDummy);
                RunSalesTotals(oConn, "COALESCE(SUM(discount), 0)",
                    POSConst.CS_SALE_COMPLETED, aFrom, aTo, ref oResult.Discount,
                    ref vDummy);
                RunSalesTotals(oConn, "COALESCE(SUM(tax), 0)",
                    POSConst.CS_SALE_COMPLETED, aFrom, aTo, ref oResult.Tax,
                    ref vDummy);
                RunSalesTotals(oConn, "COALESCE(SUM(total), 0)",
                    POSConst.CS_SALE_REFUNDED, aFrom, aTo, ref oResult.Refunded,
                    ref oResult.RefundCount);
                oResult.CashTaken = RangePaySum(oConn, POSConst.CS_PAY_CASH, aFrom, aTo);
                oResult.CardTaken = RangePaySum(oConn, POSConst.CS_PAY_CARD, aFrom, aTo);
                oResult.VoucherTaken = RangePaySum(oConn, POSConst.CS_PAY_VOUCHER,
                    aFrom, aTo);
                oResult.LoyaltyTaken = RangePaySum(oConn, POSConst.CS_PAY_LOYALTY,
                    aFrom, aTo);
            }
            return oResult;
        }

        public TPOSSeriesPoint[] SalesByHour(DateTime aFrom, DateTime aTo)
        {
            // Fixed 08:00 .. 21:00 buckets so an empty hour still charts as a
            // zero.
            TPOSSeriesPoint[] vResult = new TPOSSeriesPoint[14];
            for (int vI = 0; vI <= 13; vI++)
            {
                vResult[vI] = new TPOSSeriesPoint();
                vResult[vI].BucketKey = 8 + vI;
                vResult[vI].BucketLabel = (8 + vI).ToString("D2",
                    CultureInfo.InvariantCulture) + ":00";
                vResult[vI].Amount = 0;
                vResult[vI].Count = 0;
            }
            using (SqliteConnection oConn = Acquire())
            // substr on the stored 'yyyy-mm-ddThh:nn:ss' shape is the cheapest
            // way to bucket by hour without a date function that varies by
            // engine.
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT CAST(substr(created_at, 12, 2) AS INTEGER) " +
                "AS h, COALESCE(SUM(total), 0) AS v, COUNT(*) AS n FROM sales " +
                "WHERE status = 'completed' AND created_at >= :f " +
                "AND created_at < :t GROUP BY h ORDER BY h"))
            {
                AddParam(oCmd, ":f", POSDB.POSTimestamp(aFrom));
                AddParam(oCmd, ":t", POSDB.POSTimestamp(aTo));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        int vHour = RInt(oReader, "h");
                        if ((vHour >= 8) && (vHour <= 21))
                        {
                            vResult[vHour - 8].Amount = RCur(oReader, "v");
                            vResult[vHour - 8].Count = RInt(oReader, "n");
                        }
                    }
            }
            return vResult;
        }

        public TPOSSeriesPoint[] SalesByMonth(int aMonths)
        {
            int vMonths = aMonths;
            if (vMonths <= 0)
                vMonths = 12;
            TPOSSeriesPoint[] vResult = new TPOSSeriesPoint[vMonths];
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT COALESCE(SUM(total), 0) AS v, " +
                "COUNT(*) AS n FROM sales WHERE status = 'completed' " +
                "AND created_at >= :f AND created_at < :t"))
            {
                AddParam(oCmd, ":f", "");
                AddParam(oCmd, ":t", "");
                for (int vI = 0; vI <= vMonths - 1; vI++)
                {
                    DateTime vBase = DateTime.Today.AddMonths(-(vMonths - 1 - vI));
                    DateTime vStart = new DateTime(vBase.Year, vBase.Month, 1);
                    DateTime vEnd = vStart.AddMonths(1);
                    vResult[vI] = new TPOSSeriesPoint();
                    vResult[vI].BucketKey = vI;
                    vResult[vI].BucketLabel = vStart.ToString("MMM yyyy",
                        CultureInfo.InvariantCulture);
                    oCmd.Parameters[":f"].Value = POSDB.POSTimestamp(vStart);
                    oCmd.Parameters[":t"].Value = POSDB.POSTimestamp(vEnd);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        if (oReader.Read())
                        {
                            vResult[vI].Amount = RCur(oReader, "v");
                            vResult[vI].Count = RInt(oReader, "n");
                        }
                }
            }
            return vResult;
        }

        public TPOSHeatPoint[] SalesHeat(DateTime aFrom, DateTime aTo)
        {
            // 7 weekdays x 14 hours, always fully populated so the Heatmap draws
            // a complete grid instead of a ragged one.
            TPOSHeatPoint[] vResult = new TPOSHeatPoint[7 * 14];
            for (int vDay = 0; vDay <= 6; vDay++)
                for (int vHour = 0; vHour <= 13; vHour++)
                {
                    int vIdx = (vDay * 14) + vHour;
                    vResult[vIdx] = new TPOSHeatPoint();
                    vResult[vIdx].Weekday = vDay;
                    vResult[vIdx].Hour = 8 + vHour;
                    vResult[vIdx].Amount = 0;
                }
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT created_at, total FROM sales " +
                "WHERE status = 'completed' AND created_at >= :f " +
                "AND created_at < :t"))
            {
                AddParam(oCmd, ":f", POSDB.POSTimestamp(aFrom));
                AddParam(oCmd, ":t", POSDB.POSTimestamp(aTo));
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        DateTime vWhen = RDate(oReader, "created_at");
                        if (vWhen > DateTime.MinValue)
                        {
                            // ISO weekday: 0 = Monday .. 6 = Sunday.
                            int vDay = ((int)vWhen.DayOfWeek + 6) % 7;
                            int vHour = vWhen.Hour;
                            if ((vDay >= 0) && (vDay <= 6) && (vHour >= 8) &&
                                (vHour <= 21))
                            {
                                int vIdx = (vDay * 14) + (vHour - 8);
                                vResult[vIdx].Amount = vResult[vIdx].Amount +
                                    RCur(oReader, "total");
                            }
                        }
                    }
            }
            return vResult;
        }

        public TPOSSeriesPoint[] PaymentMix(DateTime aFrom, DateTime aTo)
        {
            string[] vMethods = new string[] { POSConst.CS_PAY_CASH,
                POSConst.CS_PAY_CARD, POSConst.CS_PAY_VOUCHER,
                POSConst.CS_PAY_LOYALTY };
            string[] vLabels = new string[] { "Cash", "Card", "Voucher", "Loyalty" };
            TPOSSeriesPoint[] vResult = new TPOSSeriesPoint[vMethods.Length];
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT COALESCE(SUM(p.amount - p.change_given), 0) AS v, " +
                "COUNT(*) AS n FROM payments p " +
                "INNER JOIN sales s ON s.id = p.sale_id " +
                "WHERE p.method = :m AND s.created_at >= :f AND s.created_at < :t"))
            {
                AddParam(oCmd, ":m", "");
                AddParam(oCmd, ":f", POSDB.POSTimestamp(aFrom));
                AddParam(oCmd, ":t", POSDB.POSTimestamp(aTo));
                for (int vI = 0; vI < vMethods.Length; vI++)
                {
                    vResult[vI] = new TPOSSeriesPoint();
                    vResult[vI].BucketKey = vI;
                    vResult[vI].BucketLabel = vLabels[vI];
                    oCmd.Parameters[":m"].Value = vMethods[vI];
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                        if (oReader.Read())
                        {
                            vResult[vI].Amount = RCur(oReader, "v");
                            vResult[vI].Count = RInt(oReader, "n");
                        }
                }
            }
            return vResult;
        }

        public TPOSSeriesPoint[] TopProducts(DateTime aFrom, DateTime aTo, int aLimit)
        {
            List<TPOSSeriesPoint> oResult = new List<TPOSSeriesPoint>();
            int vLimit = aLimit;
            if (vLimit <= 0)
                vLimit = 10;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT p.name AS nm, " +
                "COALESCE(SUM(l.line_total), 0) AS v, " +
                "CAST(COALESCE(SUM(l.qty), 0) AS INTEGER) AS n " +
                "FROM sale_lines l INNER JOIN sales s ON s.id = l.sale_id " +
                "INNER JOIN products p ON p.id = l.product_id " +
                "WHERE s.status = 'completed' AND s.created_at >= :f " +
                "AND s.created_at < :t GROUP BY p.id, p.name " +
                "ORDER BY v DESC LIMIT :lm"))
            {
                AddParam(oCmd, ":f", POSDB.POSTimestamp(aFrom));
                AddParam(oCmd, ":t", POSDB.POSTimestamp(aTo));
                AddParam(oCmd, ":lm", vLimit);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSSeriesPoint vRow = new TPOSSeriesPoint();
                        vRow.BucketKey = oResult.Count;
                        vRow.BucketLabel = RStr(oReader, "nm");
                        vRow.Amount = RCur(oReader, "v");
                        vRow.Count = RInt(oReader, "n");
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }

        // ================================================================== //
        //  audit                                                             //
        // ================================================================== //

        public void AddAudit(long aUserId, string aAction, string aEntity,
            long aEntityId, string aDetail, string aIP)
        {
            using (SqliteConnection oConn = Acquire())
                ExecNonQuery(oConn, null,
                    "INSERT INTO audit_log (user_id, action, entity, " +
                    "entity_id, detail, ip, created_at) " +
                    "VALUES (:u, :a, :e, :i, :d, :p, :c)",
                    ":u", aUserId, ":a", aAction == null ? "" : aAction,
                    ":e", aEntity == null ? "" : aEntity, ":i", aEntityId,
                    ":d", aDetail == null ? "" : aDetail,
                    ":p", aIP == null ? "" : aIP, ":c", POSDB.POSNowTimestamp());
        }

        public TPOSAuditEntry[] ListAudit(int aLimit)
        {
            List<TPOSAuditEntry> oResult = new List<TPOSAuditEntry>();
            int vLimit = aLimit;
            if (vLimit <= 0)
                vLimit = 200;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT a.id, a.user_id, a.action, a.entity, " +
                "a.entity_id, a.detail, a.ip, a.created_at, " +
                "COALESCE(u.display_name, u.username, 'system') AS user_name " +
                "FROM audit_log a LEFT JOIN users u ON u.id = a.user_id " +
                "ORDER BY a.id DESC LIMIT :lm"))
            {
                AddParam(oCmd, ":lm", vLimit);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    while (oReader.Read())
                    {
                        TPOSAuditEntry vRow = new TPOSAuditEntry();
                        vRow.Id = RInt64(oReader, "id");
                        vRow.UserId = RInt64(oReader, "user_id");
                        vRow.UserName = RStr(oReader, "user_name");
                        vRow.Action = RStr(oReader, "action");
                        vRow.Entity = RStr(oReader, "entity");
                        vRow.EntityId = RInt64(oReader, "entity_id");
                        vRow.Detail = RStr(oReader, "detail");
                        vRow.IP = RStr(oReader, "ip");
                        vRow.CreatedAt = RDate(oReader, "created_at");
                        oResult.Add(vRow);
                    }
            }
            return oResult.ToArray();
        }
    }
}
