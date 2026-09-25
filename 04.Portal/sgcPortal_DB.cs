// ***************************************************************************
//   sgcPortal - mini ERP web-app demo (walking skeleton)
//
//   written by eSeGeCe
//   copyright (c) 2026
//   Email : info@esegece.com
//   Web : https://www.esegece.com
// ***************************************************************************
//
//   Port of delphi\Demos\60.HTML\50.Portal\Source\sgcPortal_DB.pas
//
//   FireDAC (TFDConnection / FDManager / TFDQuery) is replaced by
//   Microsoft.Data.Sqlite. The pool builds a connection string once from the
//   absolute DB file path; Microsoft.Data.Sqlite pools the underlying
//   connections automatically by connection string. Acquire() returns an open
//   SqliteConnection that the caller disposes (which returns it to the pool).
//
//   DateTime storage mirrors the Delphi: created_at / updated_at / ts /
//   last_used_at are written as ISO text the SQLite strftime() can parse, and
//   issue_date / due_date are date-only 'yyyy-MM-dd' so the strftime grouping
//   in InvoiceRevenueByMonth / InvoiceSeries buckets per calendar period.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Portal
{
    /// <summary>Customer Portal DB error. Mirrors Delphi EPortalDBError.</summary>
    public class EPortalDBError : Exception
    {
        public EPortalDBError(string message) : base(message) { }
    }

    public class TERPDBPool : IDisposable
    {
        // ISO formats matching the Delphi FormatDateTime masks. The Delphi unit
        // stores the timestamp columns as 'yyyy-mm-ddThh:nn:ss'; here we store
        // them with a space so SQLite strftime() / lexicographic date-prefix
        // compares all work the same way (a lexicographic compare on the ISO
        // prefix is a correct ordering for the period boundary strings).
        private const string CS_DT_FMT = "yyyy-MM-dd HH:mm:ss";
        private const string CS_DATE_FMT = "yyyy-MM-dd";

        private readonly string FDatabaseFile;
        private readonly string FConnStr;
        private readonly Random FRandom = new Random();

        // aDatabaseFile is resolved to an absolute path internally.
        public TERPDBPool(string aDatabaseFile)
        {
            string vFile = aDatabaseFile;
            if (string.IsNullOrEmpty(vFile))
                vFile = Path.Combine("data", "portal.db");
            // Resolve relative paths against the current directory (the EXE dir,
            // which the launcher sets via SetCurrentDir).
            if (!Path.IsPathRooted(vFile))
                vFile = Path.Combine(Directory.GetCurrentDirectory(), vFile);
            FDatabaseFile = vFile;
            EnsureDatabaseDir();

            // Build the connection string once. Microsoft.Data.Sqlite pools
            // connections automatically by connection string.
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

        // --- low-level helpers ---------------------------------------------- //

        private static void AddParam(SqliteCommand aCmd, string aName, object aValue)
        {
            aCmd.Parameters.AddWithValue(aName, aValue == null ? DBNull.Value : aValue);
        }

        private static SqliteCommand NewCmd(SqliteConnection aConn, string aSQL)
        {
            SqliteCommand oCmd = aConn.CreateCommand();
            oCmd.CommandText = aSQL;
            return oCmd;
        }

        // Execute a non-query statement; returns the number of rows affected.
        private static int ExecNonQuery(SqliteConnection aConn, string aSQL,
            params object[] aArgs)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
            {
                ApplyArgs(oCmd, aArgs);
                return oCmd.ExecuteNonQuery();
            }
        }

        // Apply positional (@name, value) argument pairs onto a command.
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

        // Run a SELECT that yields a single numeric cell and return it as a long
        // (0 on empty / NULL).
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

        // The rowid of the most recently inserted row on this connection.
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
            return aReader.GetInt64(vOrd);
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
            return aReader.GetDouble(vOrd);
        }

        private static string RStr(SqliteDataReader aReader, string aName)
        {
            int vOrd = aReader.GetOrdinal(aName);
            if (aReader.IsDBNull(vOrd))
                return "";
            return aReader.GetString(vOrd);
        }

        private static DateTime RDate(SqliteDataReader aReader, string aName)
        {
            return ParseUserTimestamp(RStr(aReader, aName));
        }

        // Parse an ISO timestamp without raising; returns DateTime.MinValue (0)
        // on failure. The schema may store either a 'T' separator (legacy Delphi)
        // or a space; normalize 'T' to a space so the parser accepts both.
        private static DateTime ParseUserTimestamp(string aValue)
        {
            string vText = aValue == null ? "" : aValue.Trim();
            if (vText.Length == 0)
                return DateTime.MinValue;
            vText = vText.Replace("T", " ");
            DateTime vResult;
            if (DateTime.TryParse(vText, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out vResult))
                return vResult;
            return DateTime.MinValue;
        }

        private static string NowStr()
        {
            return DateTime.Now.ToString(CS_DT_FMT, CultureInfo.InvariantCulture);
        }

        private static string DateStr(DateTime aDate)
        {
            return aDate.ToString(CS_DATE_FMT, CultureInfo.InvariantCulture);
        }

        // ==================================================================== //
        //  schema + seeding                                                    //
        // ==================================================================== //

        // Add the invoice_lines.product_id column when it does not yet exist.
        // SQLite's ALTER TABLE ADD COLUMN raises when the column is already
        // present, so the call is guarded to keep EnsureSchema idempotent.
        private static void EnsureInvoiceLineProductColumn(SqliteConnection aConn)
        {
            bool vHasColumn = false;
            using (SqliteCommand oCmd = NewCmd(aConn, "PRAGMA table_info(invoice_lines)"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    if (string.Equals(RStr(oReader, "name"), "product_id",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        vHasColumn = true;
                        break;
                    }
                }
            }
            if (!vHasColumn)
            {
                try
                {
                    ExecNonQuery(aConn,
                        "ALTER TABLE invoice_lines ADD COLUMN product_id INTEGER");
                }
                catch
                {
                    // Column already added by a concurrent caller: ignore.
                }
            }
        }

        // Add the users.customer_id column when it does not yet exist (older
        // databases predate the customer-portal link). SQLite's ALTER TABLE ADD
        // COLUMN raises when the column is already present, so the call is wrapped
        // to keep EnsureSchema idempotent across restarts.
        private static void EnsureUserCustomerColumn(SqliteConnection aConn)
        {
            bool vHasColumn = false;
            using (SqliteCommand oCmd = NewCmd(aConn, "PRAGMA table_info(users)"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    if (string.Equals(RStr(oReader, "name"), "customer_id",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        vHasColumn = true;
                        break;
                    }
                }
            }
            if (!vHasColumn)
            {
                try
                {
                    ExecNonQuery(aConn,
                        "ALTER TABLE users ADD COLUMN customer_id INTEGER");
                }
                catch
                {
                    // Column already added by a concurrent caller: ignore.
                }
            }
        }

        // Seed a few sample products the first time the products table is empty
        // so the demo has data to show / pick from. No-op once any product exists.
        private static void SeedProductsIfEmpty(SqliteConnection aConn)
        {
            string vNow = NowStr();
            long vCount = ExecScalarLong(aConn, "SELECT COUNT(*) FROM products");
            if (vCount > 0)
                return;

            AddSampleProduct(aConn, vNow, "CONS-01", "Consulting hour", "hour", 90.0, 21.0);
            AddSampleProduct(aConn, vNow, "LIC-01", "Software license", "unit", 499.0, 21.0);
            AddSampleProduct(aConn, vNow, "SUP-01", "Support plan", "month", 49.0, 21.0);
            AddSampleProduct(aConn, vNow, "TRN-01", "Training session", "day", 600.0, 21.0);
        }

        private static void AddSampleProduct(SqliteConnection aConn, string aNow,
            string aCode, string aName, string aUnit, double aPrice, double aTaxRate)
        {
            ExecNonQuery(aConn,
                "INSERT INTO products " +
                "(code, name, description, unit, price, tax_rate, created_at, " +
                "updated_at) VALUES (@code, @name, @desc, @unit, @price, @trate, " +
                "@ca, @ua)",
                "@code", aCode, "@name", aName, "@desc", aName, "@unit", aUnit,
                "@price", aPrice, "@trate", aTaxRate, "@ca", aNow, "@ua", aNow);
        }

        // Create every table (IF NOT EXISTS). Idempotent.
        public void EnsureSchema()
        {
            string[] vTables = new string[]
            {
                "CREATE TABLE IF NOT EXISTS users (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "username TEXT UNIQUE, " +
                "password_hash TEXT, " + "role TEXT, " + "display_name TEXT, " +
                "email TEXT, " + "customer_id INTEGER, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS passkeys (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "user_id INTEGER, " +
                "credential_id TEXT, " + "public_key TEXT, " + "sign_count INTEGER, " +
                "device_name TEXT, " + "created_at TEXT, " + "last_used_at TEXT)",

                "CREATE TABLE IF NOT EXISTS customers (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "code TEXT, " + "name TEXT, " +
                "tax_id TEXT, " + "email TEXT, " + "phone TEXT, " + "address TEXT, " +
                "city TEXT, " + "country TEXT, " + "notes TEXT, " + "created_at TEXT, " +
                "updated_at TEXT)",

                "CREATE TABLE IF NOT EXISTS providers (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "code TEXT, " + "name TEXT, " +
                "tax_id TEXT, " + "email TEXT, " + "phone TEXT, " + "address TEXT, " +
                "city TEXT, " + "country TEXT, " + "notes TEXT, " + "created_at TEXT, " +
                "updated_at TEXT)",

                "CREATE TABLE IF NOT EXISTS invoices (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "number TEXT, " +
                "customer_id INTEGER, " + "issue_date TEXT, " + "due_date TEXT, " +
                "status TEXT, " + "currency TEXT, " + "notes TEXT, " + "subtotal REAL, " +
                "tax_rate REAL, " + "tax_amount REAL, " + "total REAL, " +
                "created_at TEXT, " + "updated_at TEXT)",

                "CREATE TABLE IF NOT EXISTS invoice_lines (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "invoice_id INTEGER, " +
                "description TEXT, " + "quantity REAL, " + "unit_price REAL, " +
                "line_total REAL)",

                "CREATE TABLE IF NOT EXISTS settings (" + "skey TEXT PRIMARY KEY, " +
                "svalue TEXT)",

                "CREATE TABLE IF NOT EXISTS blocked_ips (" + "ip TEXT PRIMARY KEY, " +
                "reason TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS ignored_ips (" + "ip TEXT PRIMARY KEY, " +
                "reason TEXT, " + "created_at TEXT)",

                "CREATE TABLE IF NOT EXISTS products (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "code TEXT, " + "name TEXT, " +
                "description TEXT, " + "unit TEXT, " + "price REAL, " + "tax_rate REAL, " +
                "created_at TEXT, " + "updated_at TEXT)",

                "CREATE TABLE IF NOT EXISTS audit_log (" +
                "id INTEGER PRIMARY KEY AUTOINCREMENT, " + "ts TEXT, " + "user_id INTEGER, "
                + "username TEXT, " + "action TEXT, " + "entity_type TEXT, " +
                "entity_id INTEGER, " + "details TEXT, " + "ip TEXT)"
            };

            using (SqliteConnection oConn = Acquire())
            {
                for (int vI = 0; vI < vTables.Length; vI++)
                    ExecNonQuery(oConn, vTables[vI]);
                // Migrate invoice_lines to carry an optional product_id (older
                // databases predate this column). Wrapped so re-running is safe.
                EnsureInvoiceLineProductColumn(oConn);
                // Migrate users to carry the optional customer_id portal link.
                EnsureUserCustomerColumn(oConn);
                SeedProductsIfEmpty(oConn);
            }
        }

        // INSERT the admin user only when the users table is empty. Role 'admin'.
        public void SeedAdmin(string aUser, string aPasswordHash)
        {
            using (SqliteConnection oConn = Acquire())
            {
                long vCount = ExecScalarLong(oConn, "SELECT COUNT(*) FROM users");
                if (vCount > 0)
                    return;

                ExecNonQuery(oConn,
                    "INSERT INTO users " +
                    "(username, password_hash, role, display_name, email, created_at) " +
                    "VALUES (@u, @p, @r, @d, @e, @c)",
                    "@u", aUser, "@p", aPasswordHash, "@r", "admin",
                    "@d", "Administrator", "@e", "", "@c", NowStr());
            }
        }

        // Seed the demo customer login (role 'customer') the first time it is
        // missing, linking it to the first customer row (lowest id) so the portal
        // can be demonstrated as a real customer. aPasswordHash is the bcrypt hash.
        // Idempotent: a no-op once a user with that username exists.
        public void SeedCustomerUserIfMissing(string aUser, string aPasswordHash)
        {
            if (string.IsNullOrEmpty(aUser) || aUser.Trim().Length == 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                // Idempotency guard: skip when this login already exists.
                long vCount = ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM users WHERE LOWER(username) = LOWER(@u)",
                    "@u", aUser);
                if (vCount > 0)
                    return;

                // Link the login to the first customer (lowest id). Without a
                // customer to attach to there is nothing to demonstrate, so skip
                // seeding the login.
                long vCustomerId = 0;
                string vDisplay = "Customer";
                string vEmail = "";
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT id, name, email FROM customers ORDER BY id LIMIT 1"))
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return;
                    vCustomerId = RInt64(oReader, "id");
                    vDisplay = RStr(oReader, "name");
                    vEmail = RStr(oReader, "email");
                }
                if (vCustomerId <= 0)
                    return;

                ExecNonQuery(oConn,
                    "INSERT INTO users " +
                    "(username, password_hash, role, display_name, email, " +
                    "customer_id, created_at) VALUES (@u, @p, @r, @d, @e, @cid, @c)",
                    "@u", aUser, "@p", aPasswordHash, "@r", "customer",
                    "@d", vDisplay, "@e", vEmail, "@cid", vCustomerId,
                    "@c", NowStr());
            }
        }

        private struct TSeedProduct
        {
            public string Code;
            public string Name;
            public string Unit_;
            public double Price;
            public TSeedProduct(string aCode, string aName, string aUnit, double aPrice)
            {
                Code = aCode;
                Name = aName;
                Unit_ = aUnit;
                Price = aPrice;
            }
        }

        private struct TSeedCustomer
        {
            public string Name;
            public string TaxID;
            public string Email;
            public string Phone;
            public string City;
            public string Country;
            public TSeedCustomer(string aName, string aTaxID, string aEmail,
                string aPhone, string aCity, string aCountry)
            {
                Name = aName;
                TaxID = aTaxID;
                Email = aEmail;
                Phone = aPhone;
                City = aCity;
                Country = aCountry;
            }
        }

        // Seed a rich, realistic set of demo data the first time the customers
        // table is empty so the dashboard / report charts look full out of the
        // box. Idempotent: a no-op once any customer exists. All inserts run
        // inside ONE transaction for speed. Dates are stored exactly as the
        // Insert* code stores them: created_at / updated_at as 'yyyy-MM-dd HH:mm:ss'
        // and issue_date / due_date as 'yyyy-MM-dd' so the strftime grouping works.
        public void SeedDemoDataIfEmpty()
        {
            // The four base products from SeedProductsIfEmpty plus six more, so the
            // products list shows ~10 rows and invoice lines have a varied spread.
            TSeedProduct[] vProducts = new TSeedProduct[]
            {
                new TSeedProduct("CONS-01", "Consulting hour", "hour", 90.0),
                new TSeedProduct("LIC-01", "Software license", "unit", 499.0),
                new TSeedProduct("SUP-01", "Support plan", "month", 49.0),
                new TSeedProduct("TRN-01", "Training session", "day", 600.0),
                new TSeedProduct("CLD-01", "Cloud hosting", "month", 29.0),
                new TSeedProduct("IMP-01", "Implementation", "project", 1500.0),
                new TSeedProduct("DEV-01", "Custom development", "hour", 120.0),
                new TSeedProduct("MNT-01", "Maintenance", "month", 199.0),
                new TSeedProduct("AUD-01", "Audit", "project", 850.0),
                new TSeedProduct("ONB-01", "Onboarding", "project", 350.0)
            };

            TSeedCustomer[] vCustomers = new TSeedCustomer[]
            {
                new TSeedCustomer("Acme Corporation", "US12-3456789", "billing@acme.com", "+1 415 555 0101", "San Francisco", "United States"),
                new TSeedCustomer("Globex SA", "FR40123456789", "compta@globex.fr", "+33 1 55 00 02 02", "Paris", "France"),
                new TSeedCustomer("Initech LLC", "US98-7654321", "ap@initech.com", "+1 512 555 0103", "Austin", "United States"),
                new TSeedCustomer("Umbrella Industries", "DE811234567", "invoices@umbrella.de", "+49 30 5550 0104", "Berlin", "Germany"),
                new TSeedCustomer("Soylent Foods", "US33-2211009", "finance@soylent.com", "+1 212 555 0105", "New York", "United States"),
                new TSeedCustomer("Stark Industries", "US55-1239870", "accounts@stark.com", "+1 310 555 0106", "Los Angeles", "United States"),
                new TSeedCustomer("Wayne Enterprises", "US44-7788990", "billing@wayne.com", "+1 312 555 0107", "Chicago", "United States"),
                new TSeedCustomer("Wonka Co", "GB123456789", "orders@wonka.co.uk", "+44 20 7946 0108", "London", "United Kingdom"),
                new TSeedCustomer("Hooli", "US66-4455667", "ap@hooli.com", "+1 650 555 0109", "Palo Alto", "United States"),
                new TSeedCustomer("Pied Piper", "US77-9988776", "billing@piedpiper.com", "+1 408 555 0110", "San Jose", "United States"),
                new TSeedCustomer("Vandelay Imports", "NL004567891B01", "finance@vandelay.nl", "+31 20 555 0111", "Amsterdam", "Netherlands"),
                new TSeedCustomer("Cyberdyne Systems", "US88-1122334", "invoices@cyberdyne.com", "+1 408 555 0112", "Sunnyvale", "United States")
            };

            TSeedCustomer[] vProviders = new TSeedCustomer[]
            {
                new TSeedCustomer("Northwind Supplies", "US21-0000001", "sales@northwind.com", "+1 206 555 0201", "Seattle", "United States"),
                new TSeedCustomer("Contoso Hardware", "DE822000002", "orders@contoso.de", "+49 89 5550 0202", "Munich", "Germany"),
                new TSeedCustomer("Fabrikam Components", "FR41200000003", "ventes@fabrikam.fr", "+33 4 55 00 02 03", "Lyon", "France"),
                new TSeedCustomer("Adventure Logistics", "US32-0000004", "dispatch@adventure.com", "+1 303 555 0204", "Denver", "United States"),
                new TSeedCustomer("Tailspin Services", "GB200000005", "support@tailspin.co.uk", "+44 161 555 0205", "Manchester", "United Kingdom"),
                new TSeedCustomer("Wingtip Cloud", "NL002000006B01", "billing@wingtip.nl", "+31 10 555 0206", "Rotterdam", "Netherlands")
            };

            DateTime vNowDT = DateTime.Now;
            string vNow = vNowDT.ToString(CS_DT_FMT, CultureInfo.InvariantCulture);

            using (SqliteConnection oConn = Acquire())
            {
                // Idempotency guard: only seed when there are no customers yet.
                long vCount = ExecScalarLong(oConn, "SELECT COUNT(*) FROM customers");
                if (vCount > 0)
                    return;

                using (SqliteTransaction oTx = oConn.BeginTransaction())
                {
                    // --- products --- //
                    // SeedProductsIfEmpty (run from EnsureSchema) already inserted
                    // the four base products. Wipe any existing product rows here
                    // so the codes stay unique and the prices/ids we cache below
                    // are authoritative, then insert the full set of ten.
                    ExecNonQueryTx(oConn, oTx, "DELETE FROM products");

                    long[] vProductIds = new long[vProducts.Length];
                    double[] vProductPrices = new double[vProducts.Length];
                    string[] vProductNames = new string[vProducts.Length];
                    for (int vK = 0; vK < vProducts.Length; vK++)
                    {
                        vProductIds[vK] = InsertSeedProduct(oConn, oTx, vProducts[vK], vNow);
                        vProductPrices[vK] = vProducts[vK].Price;
                        vProductNames[vK] = vProducts[vK].Name;
                    }

                    // --- customers --- //
                    long[] vCustomerIds = new long[vCustomers.Length];
                    for (int vK = 0; vK < vCustomers.Length; vK++)
                        vCustomerIds[vK] = InsertSeedParty(oConn, oTx, "customers",
                            string.Format("CUST-{0:D4}", vK + 1), vCustomers[vK], vNow);

                    // --- providers --- //
                    for (int vK = 0; vK < vProviders.Length; vK++)
                        InsertSeedParty(oConn, oTx, "providers",
                            string.Format("PROV-{0:D4}", vK + 1), vProviders[vK], vNow);

                    // --- invoices --- //
                    int vInvoiceSeq = 0;
                    // ~6..9 invoices per month for each of the last 12 months. A
                    // growth / seasonality factor nudges the per-month count so the
                    // trend chart is not flat: more recent months trend higher.
                    for (int vMonthsAgo = 11; vMonthsAgo >= 0; vMonthsAgo--)
                    {
                        double vGrowth = 1.0 + (11 - vMonthsAgo) * 0.03; // older lighter
                        int vPerMonth = (int)Math.Round((6 + FRandom.Next(4)) * vGrowth);
                        if (vPerMonth < 5)
                            vPerMonth = 5;
                        for (int vK = 1; vK <= vPerMonth; vK++)
                        {
                            DateTime vIssue = RandomDayInMonthsAgo(vNowDT, vMonthsAgo);
                            InsertSeedInvoice(oConn, oTx, ref vInvoiceSeq, vIssue,
                                PickStatus(vMonthsAgo), vNow, vCustomerIds,
                                vProductIds, vProductPrices, vProductNames);
                        }
                    }

                    // Extra ~12 invoices within the last 25 days so the day-
                    // granularity report chart and the "last weeks" buckets are
                    // well populated.
                    for (int vK = 1; vK <= 12; vK++)
                    {
                        DateTime vIssue = vNowDT.Date.AddDays(-FRandom.Next(25));
                        InsertSeedInvoice(oConn, oTx, ref vInvoiceSeq, vIssue,
                            PickStatus(0), vNow, vCustomerIds, vProductIds,
                            vProductPrices, vProductNames);
                    }

                    oTx.Commit();
                }
            }
        }

        private static int ExecNonQueryTx(SqliteConnection aConn, SqliteTransaction aTx,
            string aSQL, params object[] aArgs)
        {
            using (SqliteCommand oCmd = NewCmd(aConn, aSQL))
            {
                oCmd.Transaction = aTx;
                ApplyArgs(oCmd, aArgs);
                return oCmd.ExecuteNonQuery();
            }
        }

        private static long InsertSeedProduct(SqliteConnection aConn,
            SqliteTransaction aTx, TSeedProduct aProd, string aNow)
        {
            ExecNonQueryTx(aConn, aTx,
                "INSERT INTO products " +
                "(code, name, description, unit, price, tax_rate, created_at, " +
                "updated_at) VALUES (@code, @name, @desc, @unit, @price, @trate, " +
                "@ca, @ua)",
                "@code", aProd.Code, "@name", aProd.Name, "@desc", aProd.Name,
                "@unit", aProd.Unit_, "@price", aProd.Price, "@trate", 21.0,
                "@ca", aNow, "@ua", aNow);
            return LastInsertRowId(aConn);
        }

        private static long InsertSeedParty(SqliteConnection aConn,
            SqliteTransaction aTx, string aTable, string aCode, TSeedCustomer aParty,
            string aNow)
        {
            ExecNonQueryTx(aConn, aTx,
                "INSERT INTO " + aTable + " " +
                "(code, name, tax_id, email, phone, address, city, country, notes, " +
                "created_at, updated_at) VALUES " +
                "(@code, @name, @tax, @email, @phone, @addr, @city, @country, " +
                "@notes, @ca, @ua)",
                "@code", aCode, "@name", aParty.Name, "@tax", aParty.TaxID,
                "@email", aParty.Email, "@phone", aParty.Phone, "@addr", "",
                "@city", aParty.City, "@country", aParty.Country, "@notes", "",
                "@ca", aNow, "@ua", aNow);
            return LastInsertRowId(aConn);
        }

        // Insert one invoice + its lines on the given issue date with the given
        // status. Picks a random customer, 1..4 random product lines, and computes
        // the totals (tax_rate 21). Dates use the same formats as InsertInvoice.
        private void InsertSeedInvoice(SqliteConnection aConn, SqliteTransaction aTx,
            ref int aInvoiceSeq, DateTime aIssue, string aStatus, string aNow,
            long[] aCustomerIds, long[] aProductIds, double[] aProductPrices,
            string[] aProductNames)
        {
            aInvoiceSeq++;
            string vNumber = string.Format("INV-{0:D5}", aInvoiceSeq);

            ExecNonQueryTx(aConn, aTx,
                "INSERT INTO invoices " +
                "(number, customer_id, issue_date, due_date, status, currency, " +
                "notes, subtotal, tax_rate, tax_amount, total, created_at, " +
                "updated_at) VALUES " +
                "(@num, @cid, @idate, @ddate, @status, @cur, @notes, @sub, " +
                "@trate, @tamt, @tot, @ca, @ua)",
                "@num", vNumber,
                "@cid", aCustomerIds[FRandom.Next(aCustomerIds.Length)],
                "@idate", DateStr(aIssue), "@ddate", DateStr(aIssue.AddDays(30)),
                "@status", aStatus, "@cur", "EUR", "@notes", "",
                "@sub", 0.0, "@trate", 21.0, "@tamt", 0.0, "@tot", 0.0,
                "@ca", aNow, "@ua", aNow);

            long vInvoiceId = LastInsertRowId(aConn);

            int vLineCount = 1 + FRandom.Next(4); // 1..4 lines
            double vSubtotal = 0;
            for (int vL = 1; vL <= vLineCount; vL++)
            {
                int vP = FRandom.Next(aProductIds.Length);
                int vQty = 1 + FRandom.Next(8); // 1..8
                double vPrice = aProductPrices[vP];
                double vLineTotal = vQty * vPrice;
                vSubtotal += vLineTotal;
                ExecNonQueryTx(aConn, aTx,
                    "INSERT INTO invoice_lines " +
                    "(invoice_id, product_id, description, quantity, unit_price, " +
                    "line_total) VALUES (@iid, @pid, @desc, @qty, @price, @ltotal)",
                    "@iid", vInvoiceId, "@pid", aProductIds[vP],
                    "@desc", aProductNames[vP], "@qty", (double)vQty,
                    "@price", vPrice, "@ltotal", vLineTotal);
            }

            double vTaxAmount = vSubtotal * 21.0 / 100.0;
            double vTotal = vSubtotal + vTaxAmount;
            ExecNonQueryTx(aConn, aTx,
                "UPDATE invoices SET subtotal = @sub, tax_amount = @tamt, " +
                "total = @tot WHERE id = @id",
                "@sub", vSubtotal, "@tamt", vTaxAmount, "@tot", vTotal,
                "@id", vInvoiceId);
        }

        // A weighted-random status, biased towards 'paid' for older invoices.
        // aMonthsAgo = 0 (this month) .. 11 (a year ago).
        private string PickStatus(int aMonthsAgo)
        {
            // Older invoices are more likely settled: paid weight grows with age.
            int vPaidWeight = 45 + aMonthsAgo * 4; // 45..89
            if (vPaidWeight > 80)
                vPaidWeight = 80;
            int vR = FRandom.Next(100);
            if (vR < vPaidWeight)
                return "paid";
            if (vR < vPaidWeight + 30)
                return "sent";
            if (vR < vPaidWeight + 45)
                return "draft";
            return "cancelled";
        }

        // Build a DateTime on a random day of the calendar month that is
        // aMonthsAgo months before the current month.
        private DateTime RandomDayInMonthsAgo(DateTime aNowDT, int aMonthsAgo)
        {
            DateTime vRef = aNowDT.AddMonths(-aMonthsAgo);
            int vY = vRef.Year;
            int vM = vRef.Month;
            int vDaysInMonth = DateTime.DaysInMonth(vY, vM);
            // Cap the current month to today so we never seed future-dated invoices.
            if (aMonthsAgo == 0)
            {
                int vD = aNowDT.Day;
                if (vD < vDaysInMonth)
                    vDaysInMonth = vD;
            }
            return new DateTime(vY, vM, 1 + FRandom.Next(vDaysInMonth));
        }

        // ==================================================================== //
        //  users                                                               //
        // ==================================================================== //

        private static void FillUserFromReader(SqliteDataReader aReader, TERPUser aUser)
        {
            aUser.Id = RInt64(aReader, "id");
            aUser.Username = RStr(aReader, "username");
            aUser.PasswordHash = RStr(aReader, "password_hash");
            aUser.Role = RStr(aReader, "role");
            aUser.DisplayName = RStr(aReader, "display_name");
            aUser.Email = RStr(aReader, "email");
            aUser.CustomerId = RInt64(aReader, "customer_id");
            aUser.CreatedAt = RDate(aReader, "created_at");
        }

        public bool GetUserByUsername(string aUsername, out TERPUser aUser)
        {
            aUser = null;
            if (string.IsNullOrEmpty(aUsername) || aUsername.Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, " +
                "display_name, email, customer_id, created_at FROM users " +
                "WHERE LOWER(username) = LOWER(@u) LIMIT 1"))
            {
                AddParam(oCmd, "@u", aUsername);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aUser = new TERPUser();
                    FillUserFromReader(oReader, aUser);
                    return true;
                }
            }
        }

        public bool GetUserById(long aId, out TERPUser aUser)
        {
            aUser = null;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, username, password_hash, role, " +
                "display_name, email, customer_id, created_at FROM users " +
                "WHERE id = @i LIMIT 1"))
            {
                AddParam(oCmd, "@i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aUser = new TERPUser();
                    FillUserFromReader(oReader, aUser);
                    return true;
                }
            }
        }

        public TERPUser[] ListUsers()
        {
            List<TERPUser> oResult = new List<TERPUser>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                // password_hash is intentionally not selected here.
                "SELECT id, username, role, display_name, email, " +
                "created_at FROM users ORDER BY username COLLATE NOCASE"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    TERPUser vRow = new TERPUser();
                    vRow.Id = RInt64(oReader, "id");
                    vRow.Username = RStr(oReader, "username");
                    vRow.Role = RStr(oReader, "role");
                    vRow.DisplayName = RStr(oReader, "display_name");
                    vRow.Email = RStr(oReader, "email");
                    vRow.CreatedAt = RDate(oReader, "created_at");
                    oResult.Add(vRow);
                }
            }
            return oResult.ToArray();
        }

        // Coerce an arbitrary role string to one of the allowed values
        // ('admin' | 'customer' | 'user').
        private static string NormalizeRole(string aRole)
        {
            string vRole = (aRole == null ? "" : aRole).Trim();
            if (string.Equals(vRole, "admin", StringComparison.OrdinalIgnoreCase))
                return "admin";
            if (string.Equals(vRole, "customer", StringComparison.OrdinalIgnoreCase))
                return "customer";
            return "user";
        }

        public long InsertUser(TERPUser aUser, string aPasswordHash)
        {
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO users " +
                    "(username, password_hash, role, display_name, email, created_at) " +
                    "VALUES (@u, @p, @r, @d, @e, @c)",
                    "@u", aUser.Username, "@p", aPasswordHash,
                    "@r", NormalizeRole(aUser.Role), "@d", aUser.DisplayName,
                    "@e", aUser.Email, "@c", NowStr());
                return LastInsertRowId(oConn);
            }
        }

        public void UpdateUser(TERPUser aUser)
        {
            if (aUser.Id <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                // Update the editable profile fields only (not username / hash).
                ExecNonQuery(oConn,
                    "UPDATE users SET display_name = @d, email = @e, " +
                    "role = @r WHERE id = @id",
                    "@d", aUser.DisplayName, "@e", aUser.Email,
                    "@r", NormalizeRole(aUser.Role), "@id", aUser.Id);
            }
        }

        public void UpdateUserPassword(long aId, string aPasswordHash)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "UPDATE users SET password_hash = @p WHERE id = @id",
                    "@p", aPasswordHash, "@id", aId);
            }
        }

        public void DeleteUser(long aId)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn, "DELETE FROM users WHERE id = @id", "@id", aId);
            }
        }

        public int CountAdmins()
        {
            using (SqliteConnection oConn = Acquire())
            {
                return (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM users WHERE LOWER(role) = 'admin'");
            }
        }

        public bool UsernameExists(string aUsername, long aExceptId = 0)
        {
            if (string.IsNullOrEmpty(aUsername) || aUsername.Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                long vCount = ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM users " +
                    "WHERE LOWER(username) = LOWER(@u) AND id <> @id",
                    "@u", aUsername, "@id", aExceptId);
                return vCount > 0;
            }
        }

        // ==================================================================== //
        //  passkeys (WebAuthn)                                                 //
        // ==================================================================== //

        private static void FillPasskeyFromReader(SqliteDataReader aReader,
            TERPPasskey aPk)
        {
            aPk.Id = RInt64(aReader, "id");
            aPk.UserId = RInt64(aReader, "user_id");
            aPk.CredentialId = RStr(aReader, "credential_id");
            aPk.PublicKey = RStr(aReader, "public_key");
            aPk.SignCount = RInt64(aReader, "sign_count");
            aPk.DeviceName = RStr(aReader, "device_name");
            aPk.CreatedAt = RDate(aReader, "created_at");
            aPk.LastUsedAt = RDate(aReader, "last_used_at");
        }

        public void AddPasskey(long aUserId, string aCredentialId, string aPublicKey,
            long aSignCount, string aDeviceName)
        {
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO passkeys " +
                    "(user_id, credential_id, public_key, sign_count, device_name, " +
                    "created_at, last_used_at) VALUES (@uid, @cid, @pk, @sc, @dn, @ca, @lu)",
                    "@uid", aUserId, "@cid", aCredentialId, "@pk", aPublicKey,
                    "@sc", aSignCount, "@dn", aDeviceName, "@ca", NowStr(), "@lu", "");
            }
        }

        public TERPPasskey[] GetPasskeysByUser(long aUserId)
        {
            List<TERPPasskey> oResult = new List<TERPPasskey>();
            if (aUserId <= 0)
                return oResult.ToArray();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, " +
                "sign_count, device_name, created_at, last_used_at FROM passkeys " +
                "WHERE user_id = @uid ORDER BY id DESC"))
            {
                AddParam(oCmd, "@uid", aUserId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        TERPPasskey vRow = new TERPPasskey();
                        FillPasskeyFromReader(oReader, vRow);
                        oResult.Add(vRow);
                    }
                }
            }
            return oResult.ToArray();
        }

        public bool GetPasskeyByCredentialId(string aCredId, out TERPPasskey aPk)
        {
            aPk = null;
            if (string.IsNullOrEmpty(aCredId) || aCredId.Trim().Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, user_id, credential_id, public_key, " +
                "sign_count, device_name, created_at, last_used_at FROM passkeys " +
                "WHERE credential_id = @cid LIMIT 1"))
            {
                AddParam(oCmd, "@cid", aCredId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aPk = new TERPPasskey();
                    FillPasskeyFromReader(oReader, aPk);
                    return true;
                }
            }
        }

        public void UpdatePasskeySignCount(long aId, long aSignCount)
        {
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "UPDATE passkeys SET sign_count = @sc, last_used_at = @lu " +
                    "WHERE id = @id",
                    "@sc", aSignCount, "@lu", NowStr(), "@id", aId);
            }
        }

        public bool DeletePasskey(long aId, long aUserId)
        {
            if (aId <= 0 || aUserId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            {
                // Scoped to user_id so a user can only delete their own credentials.
                int vRows = ExecNonQuery(oConn,
                    "DELETE FROM passkeys WHERE id = @id AND user_id = @uid",
                    "@id", aId, "@uid", aUserId);
                return vRows > 0;
            }
        }

        // ==================================================================== //
        //  customers                                                           //
        // ==================================================================== //

        private static void FillCustomerFromReader(SqliteDataReader aReader,
            TERPCustomer aCust)
        {
            aCust.Id = RInt64(aReader, "id");
            aCust.Code = RStr(aReader, "code");
            aCust.Name = RStr(aReader, "name");
            aCust.TaxID = RStr(aReader, "tax_id");
            aCust.Email = RStr(aReader, "email");
            aCust.Phone = RStr(aReader, "phone");
            aCust.Address = RStr(aReader, "address");
            aCust.City = RStr(aReader, "city");
            aCust.Country = RStr(aReader, "country");
            aCust.Notes = RStr(aReader, "notes");
            aCust.CreatedAt = RDate(aReader, "created_at");
            aCust.UpdatedAt = RDate(aReader, "updated_at");
        }

        public TERPCustomer[] ListCustomers(string aSearch)
        {
            List<TERPCustomer> oResult = new List<TERPCustomer>();
            string vSearch = (aSearch == null ? "" : aSearch).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                string vSQL;
                if (vSearch.Length == 0)
                    vSQL = "SELECT id, code, name, tax_id, email, phone, " +
                        "address, city, country, notes, created_at, updated_at " +
                        "FROM customers ORDER BY name COLLATE NOCASE";
                else
                    vSQL = "SELECT id, code, name, tax_id, email, phone, " +
                        "address, city, country, notes, created_at, updated_at " +
                        "FROM customers WHERE name LIKE @q OR code LIKE @q OR " +
                        "email LIKE @q OR city LIKE @q ORDER BY name COLLATE NOCASE";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (vSearch.Length != 0)
                        AddParam(oCmd, "@q", "%" + vSearch + "%");
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            TERPCustomer vRow = new TERPCustomer();
                            FillCustomerFromReader(oReader, vRow);
                            oResult.Add(vRow);
                        }
                    }
                }
            }
            return oResult.ToArray();
        }

        public bool GetCustomer(long aId, out TERPCustomer aCust)
        {
            aCust = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, code, name, tax_id, email, phone, " +
                "address, city, country, notes, created_at, updated_at " +
                "FROM customers WHERE id = @i LIMIT 1"))
            {
                AddParam(oCmd, "@i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aCust = new TERPCustomer();
                    FillCustomerFromReader(oReader, aCust);
                    return true;
                }
            }
        }

        public long InsertCustomer(TERPCustomer aCust)
        {
            string vNow = NowStr();
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO customers " +
                    "(code, name, tax_id, email, phone, address, city, country, notes, " +
                    "created_at, updated_at) VALUES " +
                    "(@code, @name, @tax, @email, @phone, @addr, @city, @country, " +
                    "@notes, @ca, @ua)",
                    "@code", aCust.Code, "@name", aCust.Name, "@tax", aCust.TaxID,
                    "@email", aCust.Email, "@phone", aCust.Phone, "@addr", aCust.Address,
                    "@city", aCust.City, "@country", aCust.Country, "@notes", aCust.Notes,
                    "@ca", vNow, "@ua", vNow);
                return LastInsertRowId(oConn);
            }
        }

        public void UpdateCustomer(TERPCustomer aCust)
        {
            if (aCust.Id <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "UPDATE customers SET code = @code, name = @name, " +
                    "tax_id = @tax, email = @email, phone = @phone, address = @addr, " +
                    "city = @city, country = @country, notes = @notes, " +
                    "updated_at = @ua WHERE id = @id",
                    "@code", aCust.Code, "@name", aCust.Name, "@tax", aCust.TaxID,
                    "@email", aCust.Email, "@phone", aCust.Phone, "@addr", aCust.Address,
                    "@city", aCust.City, "@country", aCust.Country, "@notes", aCust.Notes,
                    "@ua", NowStr(), "@id", aCust.Id);
            }
        }

        public void DeleteCustomer(long aId)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn, "DELETE FROM customers WHERE id = @id", "@id", aId);
            }
        }

        // ==================================================================== //
        //  providers                                                           //
        // ==================================================================== //

        private static void FillProviderFromReader(SqliteDataReader aReader,
            TERPProvider aProv)
        {
            aProv.Id = RInt64(aReader, "id");
            aProv.Code = RStr(aReader, "code");
            aProv.Name = RStr(aReader, "name");
            aProv.TaxID = RStr(aReader, "tax_id");
            aProv.Email = RStr(aReader, "email");
            aProv.Phone = RStr(aReader, "phone");
            aProv.Address = RStr(aReader, "address");
            aProv.City = RStr(aReader, "city");
            aProv.Country = RStr(aReader, "country");
            aProv.Notes = RStr(aReader, "notes");
            aProv.CreatedAt = RDate(aReader, "created_at");
            aProv.UpdatedAt = RDate(aReader, "updated_at");
        }

        public TERPProvider[] ListProviders(string aSearch)
        {
            List<TERPProvider> oResult = new List<TERPProvider>();
            string vSearch = (aSearch == null ? "" : aSearch).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                string vSQL;
                if (vSearch.Length == 0)
                    vSQL = "SELECT id, code, name, tax_id, email, phone, " +
                        "address, city, country, notes, created_at, updated_at " +
                        "FROM providers ORDER BY name COLLATE NOCASE";
                else
                    vSQL = "SELECT id, code, name, tax_id, email, phone, " +
                        "address, city, country, notes, created_at, updated_at " +
                        "FROM providers WHERE name LIKE @q OR code LIKE @q OR " +
                        "email LIKE @q OR city LIKE @q ORDER BY name COLLATE NOCASE";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (vSearch.Length != 0)
                        AddParam(oCmd, "@q", "%" + vSearch + "%");
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            TERPProvider vRow = new TERPProvider();
                            FillProviderFromReader(oReader, vRow);
                            oResult.Add(vRow);
                        }
                    }
                }
            }
            return oResult.ToArray();
        }

        public bool GetProvider(long aId, out TERPProvider aProv)
        {
            aProv = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, code, name, tax_id, email, phone, " +
                "address, city, country, notes, created_at, updated_at " +
                "FROM providers WHERE id = @i LIMIT 1"))
            {
                AddParam(oCmd, "@i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aProv = new TERPProvider();
                    FillProviderFromReader(oReader, aProv);
                    return true;
                }
            }
        }

        public long InsertProvider(TERPProvider aProv)
        {
            string vNow = NowStr();
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO providers " +
                    "(code, name, tax_id, email, phone, address, city, country, notes, " +
                    "created_at, updated_at) VALUES " +
                    "(@code, @name, @tax, @email, @phone, @addr, @city, @country, " +
                    "@notes, @ca, @ua)",
                    "@code", aProv.Code, "@name", aProv.Name, "@tax", aProv.TaxID,
                    "@email", aProv.Email, "@phone", aProv.Phone, "@addr", aProv.Address,
                    "@city", aProv.City, "@country", aProv.Country, "@notes", aProv.Notes,
                    "@ca", vNow, "@ua", vNow);
                return LastInsertRowId(oConn);
            }
        }

        public void UpdateProvider(TERPProvider aProv)
        {
            if (aProv.Id <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "UPDATE providers SET code = @code, name = @name, " +
                    "tax_id = @tax, email = @email, phone = @phone, address = @addr, " +
                    "city = @city, country = @country, notes = @notes, " +
                    "updated_at = @ua WHERE id = @id",
                    "@code", aProv.Code, "@name", aProv.Name, "@tax", aProv.TaxID,
                    "@email", aProv.Email, "@phone", aProv.Phone, "@addr", aProv.Address,
                    "@city", aProv.City, "@country", aProv.Country, "@notes", aProv.Notes,
                    "@ua", NowStr(), "@id", aProv.Id);
            }
        }

        public void DeleteProvider(long aId)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn, "DELETE FROM providers WHERE id = @id", "@id", aId);
            }
        }

        // ==================================================================== //
        //  products                                                            //
        // ==================================================================== //

        private static void FillProductFromReader(SqliteDataReader aReader,
            TERPProduct aProd)
        {
            aProd.Id = RInt64(aReader, "id");
            aProd.Code = RStr(aReader, "code");
            aProd.Name = RStr(aReader, "name");
            aProd.Description = RStr(aReader, "description");
            aProd.Unit_ = RStr(aReader, "unit");
            aProd.Price = RFloat(aReader, "price");
            aProd.TaxRate = RFloat(aReader, "tax_rate");
            aProd.CreatedAt = RDate(aReader, "created_at");
            aProd.UpdatedAt = RDate(aReader, "updated_at");
        }

        public TERPProduct[] ListProducts(string aSearch)
        {
            List<TERPProduct> oResult = new List<TERPProduct>();
            string vSearch = (aSearch == null ? "" : aSearch).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                string vSQL;
                if (vSearch.Length == 0)
                    vSQL = "SELECT id, code, name, description, unit, price, " +
                        "tax_rate, created_at, updated_at " +
                        "FROM products ORDER BY name COLLATE NOCASE";
                else
                    vSQL = "SELECT id, code, name, description, unit, price, " +
                        "tax_rate, created_at, updated_at " +
                        "FROM products WHERE name LIKE @q OR code LIKE @q " +
                        "ORDER BY name COLLATE NOCASE";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (vSearch.Length != 0)
                        AddParam(oCmd, "@q", "%" + vSearch + "%");
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            TERPProduct vRow = new TERPProduct();
                            FillProductFromReader(oReader, vRow);
                            oResult.Add(vRow);
                        }
                    }
                }
            }
            return oResult.ToArray();
        }

        public bool GetProduct(long aId, out TERPProduct aProd)
        {
            aProd = null;
            if (aId <= 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT id, code, name, description, unit, price, " +
                "tax_rate, created_at, updated_at " +
                "FROM products WHERE id = @i LIMIT 1"))
            {
                AddParam(oCmd, "@i", aId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                        return false;
                    aProd = new TERPProduct();
                    FillProductFromReader(oReader, aProd);
                    return true;
                }
            }
        }

        public long InsertProduct(TERPProduct aProd)
        {
            string vNow = NowStr();
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT INTO products " +
                    "(code, name, description, unit, price, tax_rate, created_at, " +
                    "updated_at) VALUES " +
                    "(@code, @name, @desc, @unit, @price, @trate, @ca, @ua)",
                    "@code", aProd.Code, "@name", aProd.Name, "@desc", aProd.Description,
                    "@unit", aProd.Unit_, "@price", aProd.Price, "@trate", aProd.TaxRate,
                    "@ca", vNow, "@ua", vNow);
                return LastInsertRowId(oConn);
            }
        }

        public void UpdateProduct(TERPProduct aProd)
        {
            if (aProd.Id <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "UPDATE products SET code = @code, name = @name, " +
                    "description = @desc, unit = @unit, price = @price, " +
                    "tax_rate = @trate, updated_at = @ua WHERE id = @id",
                    "@code", aProd.Code, "@name", aProd.Name, "@desc", aProd.Description,
                    "@unit", aProd.Unit_, "@price", aProd.Price, "@trate", aProd.TaxRate,
                    "@ua", NowStr(), "@id", aProd.Id);
            }
        }

        public void DeleteProduct(long aId)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn, "DELETE FROM products WHERE id = @id", "@id", aId);
            }
        }

        // ==================================================================== //
        //  invoices                                                            //
        // ==================================================================== //

        private static void FillInvoiceFromReader(SqliteDataReader aReader,
            TERPInvoice aInv)
        {
            aInv.Id = RInt64(aReader, "id");
            aInv.Number = RStr(aReader, "number");
            aInv.CustomerId = RInt64(aReader, "customer_id");
            aInv.IssueDate = RDate(aReader, "issue_date");
            aInv.DueDate = RDate(aReader, "due_date");
            aInv.Status = RStr(aReader, "status");
            aInv.Currency = RStr(aReader, "currency");
            aInv.Notes = RStr(aReader, "notes");
            aInv.Subtotal = RFloat(aReader, "subtotal");
            aInv.TaxRate = RFloat(aReader, "tax_rate");
            aInv.TaxAmount = RFloat(aReader, "tax_amount");
            aInv.Total = RFloat(aReader, "total");
            aInv.CreatedAt = RDate(aReader, "created_at");
            aInv.UpdatedAt = RDate(aReader, "updated_at");
        }

        private static TERPInvoiceListRow ReadInvoiceListRow(SqliteDataReader aReader)
        {
            TERPInvoiceListRow vRow = new TERPInvoiceListRow();
            vRow.Id = RInt64(aReader, "id");
            vRow.Number = RStr(aReader, "number");
            vRow.CustomerId = RInt64(aReader, "customer_id");
            vRow.CustomerName = RStr(aReader, "customer_name");
            vRow.IssueDate = RDate(aReader, "issue_date");
            vRow.DueDate = RDate(aReader, "due_date");
            vRow.Status = RStr(aReader, "status");
            vRow.Currency = RStr(aReader, "currency");
            vRow.Total = RFloat(aReader, "total");
            return vRow;
        }

        public TERPInvoiceListRow[] ListInvoices(string aSearch, string aStatus)
        {
            List<TERPInvoiceListRow> oResult = new List<TERPInvoiceListRow>();
            string vSearch = (aSearch == null ? "" : aSearch).Trim();
            string vStatus = (aStatus == null ? "" : aStatus).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                // LEFT JOIN so invoices with a missing/zero customer still list.
                string vSQL = "SELECT i.id AS id, i.number AS number, " +
                    "i.customer_id AS customer_id, i.issue_date AS issue_date, " +
                    "i.due_date AS due_date, i.status AS status, " +
                    "i.currency AS currency, i.total AS total, " +
                    "c.name AS customer_name FROM invoices i " +
                    "LEFT JOIN customers c ON c.id = i.customer_id";
                bool vHasStatus = (vStatus.Length != 0) &&
                    !string.Equals(vStatus, "all", StringComparison.OrdinalIgnoreCase);
                if (vSearch.Length != 0)
                    vSQL += " WHERE (i.number LIKE @q OR c.name LIKE @q)";
                if (vHasStatus)
                {
                    if (vSearch.Length != 0)
                        vSQL += " AND i.status = @st";
                    else
                        vSQL += " WHERE i.status = @st";
                }
                vSQL += " ORDER BY i.issue_date DESC, i.id DESC";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    if (vSearch.Length != 0)
                        AddParam(oCmd, "@q", "%" + vSearch + "%");
                    if (vHasStatus)
                        AddParam(oCmd, "@st", vStatus);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                            oResult.Add(ReadInvoiceListRow(oReader));
                    }
                }
            }
            return oResult.ToArray();
        }

        public bool GetInvoice(long aId, out TERPInvoice aInv, out TERPInvoiceLine[] aLines)
        {
            aInv = null;
            aLines = new TERPInvoiceLine[0];
            if (aId <= 0)
                return false;
            List<TERPInvoiceLine> oLines = new List<TERPInvoiceLine>();
            using (SqliteConnection oConn = Acquire())
            {
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT id, number, customer_id, issue_date, " +
                    "due_date, status, currency, notes, subtotal, tax_rate, tax_amount, " +
                    "total, created_at, updated_at FROM invoices WHERE id = @i LIMIT 1"))
                {
                    AddParam(oCmd, "@i", aId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (!oReader.Read())
                            return false;
                        aInv = new TERPInvoice();
                        FillInvoiceFromReader(oReader, aInv);
                    }
                }

                // Lines for this invoice (preserve insertion order via id).
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT id, invoice_id, product_id, description, " +
                    "quantity, unit_price, line_total FROM invoice_lines " +
                    "WHERE invoice_id = @i ORDER BY id"))
                {
                    AddParam(oCmd, "@i", aId);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            TERPInvoiceLine vLine = new TERPInvoiceLine();
                            vLine.Id = RInt64(oReader, "id");
                            vLine.InvoiceId = RInt64(oReader, "invoice_id");
                            vLine.ProductId = RInt64(oReader, "product_id");
                            vLine.Description = RStr(oReader, "description");
                            vLine.Quantity = RFloat(oReader, "quantity");
                            vLine.UnitPrice = RFloat(oReader, "unit_price");
                            vLine.LineTotal = RFloat(oReader, "line_total");
                            oLines.Add(vLine);
                        }
                    }
                }
            }
            aLines = oLines.ToArray();
            return true;
        }

        // Insert the lines for aInvoiceId using the already-open connection /
        // transaction.
        private static void InsertInvoiceLines(SqliteConnection aConn,
            SqliteTransaction aTx, long aInvoiceId, TERPInvoiceLine[] aLines)
        {
            if (aLines == null)
                return;
            for (int vI = 0; vI < aLines.Length; vI++)
            {
                ExecNonQueryTx(aConn, aTx,
                    "INSERT INTO invoice_lines " +
                    "(invoice_id, product_id, description, quantity, unit_price, " +
                    "line_total) VALUES (@iid, @pid, @desc, @qty, @price, @ltotal)",
                    "@iid", aInvoiceId, "@pid", aLines[vI].ProductId,
                    "@desc", aLines[vI].Description, "@qty", aLines[vI].Quantity,
                    "@price", aLines[vI].UnitPrice, "@ltotal", aLines[vI].LineTotal);
            }
        }

        public long InsertInvoice(TERPInvoice aInv, TERPInvoiceLine[] aLines)
        {
            string vNow = NowStr();
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                ExecNonQueryTx(oConn, oTx,
                    "INSERT INTO invoices " +
                    "(number, customer_id, issue_date, due_date, status, currency, " +
                    "notes, subtotal, tax_rate, tax_amount, total, created_at, " +
                    "updated_at) VALUES " +
                    "(@num, @cid, @idate, @ddate, @status, @cur, @notes, @sub, " +
                    "@trate, @tamt, @tot, @ca, @ua)",
                    "@num", aInv.Number, "@cid", aInv.CustomerId,
                    "@idate", DateStr(aInv.IssueDate), "@ddate", DateStr(aInv.DueDate),
                    "@status", aInv.Status, "@cur", aInv.Currency, "@notes", aInv.Notes,
                    "@sub", aInv.Subtotal, "@trate", aInv.TaxRate,
                    "@tamt", aInv.TaxAmount, "@tot", aInv.Total, "@ca", vNow, "@ua", vNow);

                long vResult = LastInsertRowId(oConn);
                InsertInvoiceLines(oConn, oTx, vResult, aLines);
                oTx.Commit();
                return vResult;
            }
        }

        public void UpdateInvoice(TERPInvoice aInv, TERPInvoiceLine[] aLines)
        {
            if (aInv.Id <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                ExecNonQueryTx(oConn, oTx,
                    "UPDATE invoices SET number = @num, " +
                    "customer_id = @cid, issue_date = @idate, due_date = @ddate, " +
                    "status = @status, currency = @cur, notes = @notes, " +
                    "subtotal = @sub, tax_rate = @trate, tax_amount = @tamt, " +
                    "total = @tot, updated_at = @ua WHERE id = @id",
                    "@num", aInv.Number, "@cid", aInv.CustomerId,
                    "@idate", DateStr(aInv.IssueDate), "@ddate", DateStr(aInv.DueDate),
                    "@status", aInv.Status, "@cur", aInv.Currency, "@notes", aInv.Notes,
                    "@sub", aInv.Subtotal, "@trate", aInv.TaxRate,
                    "@tamt", aInv.TaxAmount, "@tot", aInv.Total, "@ua", NowStr(),
                    "@id", aInv.Id);

                // Replace the line set: delete then re-insert.
                ExecNonQueryTx(oConn, oTx,
                    "DELETE FROM invoice_lines WHERE invoice_id = @iid",
                    "@iid", aInv.Id);

                InsertInvoiceLines(oConn, oTx, aInv.Id, aLines);
                oTx.Commit();
            }
        }

        public void DeleteInvoice(long aId)
        {
            if (aId <= 0)
                return;
            using (SqliteConnection oConn = Acquire())
            using (SqliteTransaction oTx = oConn.BeginTransaction())
            {
                ExecNonQueryTx(oConn, oTx,
                    "DELETE FROM invoice_lines WHERE invoice_id = @id", "@id", aId);
                ExecNonQueryTx(oConn, oTx,
                    "DELETE FROM invoices WHERE id = @id", "@id", aId);
                oTx.Commit();
            }
        }

        // ==================================================================== //
        //  dashboard aggregates                                                //
        // ==================================================================== //

        public int CountCustomers()
        {
            using (SqliteConnection oConn = Acquire())
                return (int)ExecScalarLong(oConn, "SELECT COUNT(*) FROM customers");
        }

        public int CountProviders()
        {
            using (SqliteConnection oConn = Acquire())
                return (int)ExecScalarLong(oConn, "SELECT COUNT(*) FROM providers");
        }

        public int CountProducts()
        {
            using (SqliteConnection oConn = Acquire())
                return (int)ExecScalarLong(oConn, "SELECT COUNT(*) FROM products");
        }

        public int CountInvoices()
        {
            using (SqliteConnection oConn = Acquire())
                return (int)ExecScalarLong(oConn, "SELECT COUNT(*) FROM invoices");
        }

        public int CountInvoicesByStatus(string aStatus)
        {
            using (SqliteConnection oConn = Acquire())
                return (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM invoices WHERE status = @st", "@st", aStatus);
        }

        public double SumInvoiceTotal(string aStatus = "")
        {
            string vStatus = (aStatus == null ? "" : aStatus).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                if (vStatus.Length == 0 ||
                    string.Equals(vStatus, "all", StringComparison.OrdinalIgnoreCase))
                    return ExecScalarDouble(oConn,
                        "SELECT COALESCE(SUM(total), 0) FROM invoices");
                return ExecScalarDouble(oConn,
                    "SELECT COALESCE(SUM(total), 0) FROM invoices WHERE status = @st",
                    "@st", vStatus);
            }
        }

        // ==================================================================== //
        //  customer-portal (scoped to a single customer)                       //
        // ==================================================================== //

        // Invoices belonging to aCustomerId only (joined to the customer name),
        // newest first. When aStatus <> '' (and not 'all') the result is further
        // filtered by status. Used by the customer portal "My Orders" list so a
        // customer never sees another customer's invoices. aCustomerId <= 0 yields
        // an empty list. The customer_id filter is bound server-side from the
        // session, never from a request field.
        public TERPInvoiceListRow[] ListInvoicesByCustomer(long aCustomerId,
            string aStatus)
        {
            List<TERPInvoiceListRow> oResult = new List<TERPInvoiceListRow>();
            if (aCustomerId <= 0)
                return oResult.ToArray();
            string vStatus = (aStatus == null ? "" : aStatus).Trim();
            bool vHasStatus = (vStatus.Length != 0) &&
                !string.Equals(vStatus, "all", StringComparison.OrdinalIgnoreCase);
            using (SqliteConnection oConn = Acquire())
            {
                string vSQL = "SELECT i.id AS id, i.number AS number, " +
                    "i.customer_id AS customer_id, i.issue_date AS issue_date, " +
                    "i.due_date AS due_date, i.status AS status, " +
                    "i.currency AS currency, i.total AS total, " +
                    "c.name AS customer_name FROM invoices i " +
                    "LEFT JOIN customers c ON c.id = i.customer_id " +
                    "WHERE i.customer_id = @cid";
                if (vHasStatus)
                    vSQL += " AND i.status = @st";
                vSQL += " ORDER BY i.issue_date DESC, i.id DESC";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    AddParam(oCmd, "@cid", aCustomerId);
                    if (vHasStatus)
                        AddParam(oCmd, "@st", vStatus);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                            oResult.Add(ReadInvoiceListRow(oReader));
                    }
                }
            }
            return oResult.ToArray();
        }

        // Number of invoices belonging to aCustomerId (any status).
        public int CountInvoicesByCustomer(long aCustomerId)
        {
            if (aCustomerId <= 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
                return (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM invoices WHERE customer_id = @cid",
                    "@cid", aCustomerId);
        }

        // Number of invoices belonging to aCustomerId in the given status.
        public int CountInvoicesByCustomerStatus(long aCustomerId, string aStatus)
        {
            if (aCustomerId <= 0)
                return 0;
            using (SqliteConnection oConn = Acquire())
                return (int)ExecScalarLong(oConn,
                    "SELECT COUNT(*) FROM invoices " +
                    "WHERE customer_id = @cid AND status = @st",
                    "@cid", aCustomerId, "@st", aStatus);
        }

        // Sum of invoice totals for aCustomerId. When aStatus <> '' (and not
        // 'all') only invoices in that status are summed (e.g. 'paid' for spend to
        // date, 'sent' for the outstanding balance).
        public double SumInvoiceTotalByCustomer(long aCustomerId, string aStatus = "")
        {
            if (aCustomerId <= 0)
                return 0;
            string vStatus = (aStatus == null ? "" : aStatus).Trim();
            using (SqliteConnection oConn = Acquire())
            {
                if (vStatus.Length == 0 ||
                    string.Equals(vStatus, "all", StringComparison.OrdinalIgnoreCase))
                    return ExecScalarDouble(oConn,
                        "SELECT COALESCE(SUM(total), 0) FROM invoices " +
                        "WHERE customer_id = @cid", "@cid", aCustomerId);
                return ExecScalarDouble(oConn,
                    "SELECT COALESCE(SUM(total), 0) FROM invoices " +
                    "WHERE customer_id = @cid AND status = @st",
                    "@cid", aCustomerId, "@st", vStatus);
            }
        }

        public TERPInvoiceListRow[] RecentInvoices(int aLimit)
        {
            List<TERPInvoiceListRow> oResult = new List<TERPInvoiceListRow>();
            if (aLimit <= 0)
                aLimit = 8;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                // LEFT JOIN so invoices with a missing/zero customer still list.
                // Order by creation so the truly newest rows surface even with the
                // same issue_date.
                "SELECT i.id AS id, i.number AS number, " +
                "i.customer_id AS customer_id, i.issue_date AS issue_date, " +
                "i.due_date AS due_date, i.status AS status, " +
                "i.currency AS currency, i.total AS total, " +
                "c.name AS customer_name FROM invoices i " +
                "LEFT JOIN customers c ON c.id = i.customer_id " +
                "ORDER BY i.id DESC LIMIT @lim"))
            {
                AddParam(oCmd, "@lim", aLimit);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                        oResult.Add(ReadInvoiceListRow(oReader));
                }
            }
            return oResult.ToArray();
        }

        public TERPRevenueMonth[] InvoiceRevenueByMonth(int aMonths)
        {
            if (aMonths <= 0)
                aMonths = 6;

            Dictionary<string, double> oTotals = new Dictionary<string, double>();
            Dictionary<string, int> oCounts = new Dictionary<string, int>();

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                // Group by the yyyy-mm prefix of issue_date. issue_date is stored
                // as 'yyyy-mm-dd', so strftime('%Y-%m', ...) buckets per month.
                "SELECT strftime('%Y-%m', issue_date) AS ym, " +
                "COALESCE(SUM(total), 0) AS tot, COUNT(*) AS cnt " +
                "FROM invoices WHERE issue_date IS NOT NULL AND issue_date <> '' " +
                "GROUP BY ym"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    string vKey = RStr(oReader, "ym");
                    if (vKey.Length != 0)
                    {
                        oTotals[vKey] = RFloat(oReader, "tot");
                        oCounts[vKey] = RInt(oReader, "cnt");
                    }
                }
            }

            // Build the last aMonths buckets ending with the current month, oldest
            // first, back-filling empty months with zeros from the maps above.
            DateTime vToday = DateTime.Now.Date;
            DateTime vMonth = new DateTime(vToday.Year, vToday.Month, 1);
            vMonth = vMonth.AddMonths(-(aMonths - 1));
            TERPRevenueMonth[] vResult = new TERPRevenueMonth[aMonths];
            for (int vI = 0; vI < aMonths; vI++)
            {
                string vKey = vMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                double vTotal;
                if (!oTotals.TryGetValue(vKey, out vTotal))
                    vTotal = 0;
                int vCnt;
                if (!oCounts.TryGetValue(vKey, out vCnt))
                    vCnt = 0;
                TERPRevenueMonth vBucket = new TERPRevenueMonth();
                vBucket.MonthLabel = vKey;
                vBucket.Total = vTotal;
                vBucket.Cnt = vCnt;
                vResult[vI] = vBucket;
                vMonth = vMonth.AddMonths(1);
            }
            return vResult;
        }

        // ==================================================================== //
        //  reports / stats time-series                                         //
        // ==================================================================== //

        // The SQLite '%W' week-of-year number (00..53) for aDate. SQLite's %W
        // counts weeks with Monday as the first day; days before the year's first
        // Monday are week 00. Computed locally (no SQLite round-trip) so the back-
        // fill keys match the grouped strftime('%Y-%W', ...) bucket keys exactly.
        private static int WeekOfTheYearSQLite(DateTime aDate)
        {
            DateTime vJan1 = new DateTime(aDate.Year, 1, 1);
            int vDayOfYear = (int)(aDate.Date - vJan1.Date).TotalDays; // 0-based
            // DayOfWeek: Sunday = 0 .. Saturday = 6. Convert Jan 1 to 0=Mon..6=Sun.
            int vJan1Dow = ((int)vJan1.DayOfWeek + 6) % 7;
            // Days until (and including) the first Monday are week 00; from the
            // first Monday onward the week index increments every 7 days.
            return (vDayOfYear + vJan1Dow) / 7;
        }

        // The start date (time stripped) of the period containing aDate at the
        // given granularity. day -> that date; week -> the Monday of that week;
        // month -> the first of that month.
        private static DateTime PeriodStart(string aGranularity, DateTime aDate)
        {
            if (aGranularity == "day")
                return aDate.Date;
            if (aGranularity == "week")
            {
                int vDow = ((int)aDate.DayOfWeek + 6) % 7; // 0 = Monday .. 6 = Sunday
                return aDate.Date.AddDays(-vDow);
            }
            return new DateTime(aDate.Year, aDate.Month, 1);
        }

        // Step aStart by aPeriods whole periods (may be negative) at the given
        // granularity: day = +/-N days, week = +/-N*7 days, month = +/-N months.
        private static DateTime PeriodAdd(string aGranularity, DateTime aStart,
            int aPeriods)
        {
            if (aGranularity == "day")
                return aStart.AddDays(aPeriods);
            if (aGranularity == "week")
                return aStart.AddDays(aPeriods * 7);
            return aStart.AddMonths(aPeriods);
        }

        // Normalize a free-text granularity to one of 'day' | 'week' | 'month'.
        // Anything unrecognized falls back to 'month'.
        private static string NormGranularity(string aGranularity)
        {
            string vG = (aGranularity == null ? "" : aGranularity).Trim().ToLowerInvariant();
            if (vG == "day" || vG == "week" || vG == "month")
                return vG;
            return "month";
        }

        // The strftime() format used to bucket issue_date for the granularity.
        private static string GranularityStrftime(string aGranularity)
        {
            if (aGranularity == "day")
                return "%Y-%m-%d";
            if (aGranularity == "week")
                return "%Y-%W";
            return "%Y-%m";
        }

        // The bucket key for aDate at the given granularity, matching the strftime
        // output above so the back-fill keys line up with the grouped rows.
        // week uses 'yyyy-ww' with a zero-padded SQLite-%W week number.
        private static string BucketKey(string aGranularity, DateTime aDate)
        {
            if (aGranularity == "day")
                return aDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (aGranularity == "week")
            {
                int vWeek = WeekOfTheYearSQLite(aDate);
                return string.Format(CultureInfo.InvariantCulture, "{0:D4}-{1:D2}",
                    aDate.Year, vWeek);
            }
            return aDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        // A human-friendly label for a bucket at the given granularity.
        private static string BucketLabel(string aGranularity, DateTime aDate)
        {
            if (aGranularity == "day")
                return aDate.ToString("dd MMM", CultureInfo.InvariantCulture);
            if (aGranularity == "week")
            {
                int vWeek = WeekOfTheYearSQLite(aDate);
                return "W" + vWeek.ToString(CultureInfo.InvariantCulture);
            }
            return aDate.ToString("MMM yyyy", CultureInfo.InvariantCulture);
        }

        public TERPRevenueMonth[] InvoiceSeries(string aGranularity, int aBuckets)
        {
            string vG = NormGranularity(aGranularity);
            if (aBuckets <= 0)
            {
                if (vG == "day")
                    aBuckets = 30;
                else
                    aBuckets = 12;
            }

            Dictionary<string, double> oTotals = new Dictionary<string, double>();
            Dictionary<string, int> oCounts = new Dictionary<string, int>();

            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                // Group invoices by the strftime bucket of issue_date. issue_date
                // is stored as 'yyyy-mm-dd', so the bucket prefix is well-defined.
                "SELECT strftime('" + GranularityStrftime(vG) + "', issue_date) AS bk, " +
                "COALESCE(SUM(total), 0) AS tot, COUNT(*) AS cnt " +
                "FROM invoices WHERE issue_date IS NOT NULL AND issue_date <> '' " +
                "GROUP BY bk"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    string vKey = RStr(oReader, "bk");
                    if (vKey.Length != 0)
                    {
                        oTotals[vKey] = RFloat(oReader, "tot");
                        oCounts[vKey] = RInt(oReader, "cnt");
                    }
                }
            }

            // Build the last aBuckets periods ending with the current one, oldest
            // first, back-filling empty periods with zeros. The cursor steps one
            // period back at a time using a per-granularity step.
            TERPRevenueMonth[] vResult = new TERPRevenueMonth[aBuckets];
            DateTime vCursor = PeriodStart(vG, DateTime.Now.Date);
            vCursor = PeriodAdd(vG, vCursor, -(aBuckets - 1));
            for (int vI = 0; vI < aBuckets; vI++)
            {
                string vKey = BucketKey(vG, vCursor);
                double vTotal;
                if (!oTotals.TryGetValue(vKey, out vTotal))
                    vTotal = 0;
                int vCnt;
                if (!oCounts.TryGetValue(vKey, out vCnt))
                    vCnt = 0;
                TERPRevenueMonth vBucket = new TERPRevenueMonth();
                vBucket.MonthLabel = BucketLabel(vG, vCursor);
                vBucket.Total = vTotal;
                vBucket.Cnt = vCnt;
                vResult[vI] = vBucket;
                vCursor = PeriodAdd(vG, vCursor, 1);
            }
            return vResult;
        }

        public void PeriodSummary(string aGranularity, bool aCurrent,
            out int aCount, out double aRevenue, out int aNewCustomers)
        {
            aCount = 0;
            aRevenue = 0;
            aNewCustomers = 0;
            string vG = NormGranularity(aGranularity);

            // [vStart, vEnd) is the half-open period range. For the current period
            // the range is [start-of-this-period, start-of-next-period); for the
            // previous one it is shifted one period back.
            DateTime vStart = PeriodStart(vG, DateTime.Now.Date);
            DateTime vEnd;
            if (aCurrent)
                vEnd = PeriodAdd(vG, vStart, 1);
            else
            {
                vEnd = vStart;
                vStart = PeriodAdd(vG, vStart, -1);
            }
            // created_at / issue_date are stored as 'yyyy-MM-dd HH:mm:ss'
            // (issue_date is date-only 'yyyy-MM-dd'); a lexicographic compare on
            // the ISO prefix is a correct ordering, so the date boundary strings
            // bound both columns.
            string vStartStr = DateStr(vStart);
            string vEndStr = DateStr(vEnd);

            using (SqliteConnection oConn = Acquire())
            {
                // Invoice count + revenue in the period (by issue_date).
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) AS cnt, COALESCE(SUM(total), 0) " +
                    "AS tot FROM invoices WHERE issue_date >= @s AND issue_date < @e"))
                {
                    AddParam(oCmd, "@s", vStartStr);
                    AddParam(oCmd, "@e", vEndStr);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (oReader.Read())
                        {
                            aCount = RInt(oReader, "cnt");
                            aRevenue = RFloat(oReader, "tot");
                        }
                    }
                }

                // New customers created in the period (by created_at; the time
                // suffix sorts after the date prefix, so the same bounds apply).
                using (SqliteCommand oCmd = NewCmd(oConn,
                    "SELECT COUNT(*) AS cnt FROM customers " +
                    "WHERE created_at >= @s AND created_at < @e"))
                {
                    AddParam(oCmd, "@s", vStartStr);
                    AddParam(oCmd, "@e", vEndStr);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        if (oReader.Read())
                            aNewCustomers = RInt(oReader, "cnt");
                    }
                }
            }
        }

        public TERPInvoiceListRow[] TopCustomersByRevenue(int aLimit)
        {
            List<TERPInvoiceListRow> oResult = new List<TERPInvoiceListRow>();
            if (aLimit <= 0)
                aLimit = 5;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                // Sum each customer's invoice totals + count, highest revenue first.
                // LEFT JOIN so a customer name is always available; customers with
                // no invoices are excluded by the INNER side (we group on invoices).
                "SELECT c.id AS cid, " +
                "COALESCE(c.name, '') AS customer_name, " +
                "COUNT(i.id) AS cnt, COALESCE(SUM(i.total), 0) AS tot " +
                "FROM invoices i LEFT JOIN customers c ON c.id = i.customer_id " +
                "GROUP BY i.customer_id ORDER BY tot DESC LIMIT @lim"))
            {
                AddParam(oCmd, "@lim", aLimit);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    while (oReader.Read())
                    {
                        // CustomerId is repurposed to carry the invoice count;
                        // Total = revenue.
                        TERPInvoiceListRow vRow = new TERPInvoiceListRow();
                        vRow.Id = 0;
                        vRow.Number = "";
                        vRow.CustomerId = RInt64(oReader, "cnt");
                        vRow.CustomerName = RStr(oReader, "customer_name");
                        if (vRow.CustomerName.Trim().Length == 0)
                            vRow.CustomerName = "(" + RInt(oReader, "cid").ToString(
                                CultureInfo.InvariantCulture) + ")";
                        vRow.IssueDate = DateTime.MinValue;
                        vRow.DueDate = DateTime.MinValue;
                        vRow.Status = "";
                        vRow.Currency = "";
                        vRow.Total = RFloat(oReader, "tot");
                        oResult.Add(vRow);
                    }
                }
            }
            return oResult.ToArray();
        }

        // ==================================================================== //
        //  settings (key/value)                                                //
        // ==================================================================== //

        public string GetSetting(string aKey, string aDefault)
        {
            string vResult = aDefault;
            if (string.IsNullOrEmpty(aKey) || aKey.Trim().Length == 0)
                return vResult;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT svalue FROM settings WHERE skey = @k LIMIT 1"))
            {
                AddParam(oCmd, "@k", aKey);
                object vVal = oCmd.ExecuteScalar();
                if (vVal != null && vVal != DBNull.Value)
                {
                    string vStr = Convert.ToString(vVal, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(vStr))
                        vResult = vStr;
                }
            }
            return vResult;
        }

        public void SetSetting(string aKey, string aValue)
        {
            if (string.IsNullOrEmpty(aKey) || aKey.Trim().Length == 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT OR REPLACE INTO settings (skey, svalue) VALUES (@k, @v)",
                    "@k", aKey, "@v", aValue);
            }
        }

        // ==================================================================== //
        //  audit log                                                           //
        // ==================================================================== //

        // Best-effort logging: swallow any failure so a logging problem never
        // breaks the action that is being audited.
        public void AddAuditLog(long aUserId, string aUsername, string aAction,
            string aEntityType, long aEntityId, string aDetails, string aIP)
        {
            try
            {
                using (SqliteConnection oConn = Acquire())
                {
                    ExecNonQuery(oConn,
                        "INSERT INTO audit_log " +
                        "(ts, user_id, username, action, entity_type, entity_id, details, " +
                        "ip) VALUES (@ts, @uid, @un, @ac, @et, @eid, @de, @ip)",
                        "@ts", NowStr(), "@uid", aUserId, "@un", aUsername,
                        "@ac", aAction, "@et", aEntityType, "@eid", aEntityId,
                        "@de", aDetails, "@ip", aIP);
                }
            }
            catch
            {
                // Never propagate a logging failure.
            }
        }

        // Build the optional WHERE clause shared by ListAuditLog / CountAuditLog.
        private static string BuildAuditWhere(string aAction, string aUser)
        {
            string vWhere = "";
            if (aAction.Length != 0 &&
                !string.Equals(aAction, "all", StringComparison.OrdinalIgnoreCase))
                vWhere = "action = @ac";
            if (aUser.Length != 0)
            {
                if (vWhere.Length != 0)
                    vWhere += " AND ";
                vWhere += "username LIKE @un";
            }
            return vWhere;
        }

        private static void ApplyAuditParams(SqliteCommand aCmd, string aAction,
            string aUser)
        {
            if (aAction.Length != 0 &&
                !string.Equals(aAction, "all", StringComparison.OrdinalIgnoreCase))
                AddParam(aCmd, "@ac", aAction);
            if (aUser.Length != 0)
                AddParam(aCmd, "@un", "%" + aUser + "%");
        }

        public TERPAuditRow[] ListAuditLog(string aActionFilter, string aUserFilter,
            int aLimit, int aOffset)
        {
            List<TERPAuditRow> oResult = new List<TERPAuditRow>();
            if (aLimit <= 0)
                aLimit = 50;
            if (aOffset < 0)
                aOffset = 0;
            string vAction = (aActionFilter == null ? "" : aActionFilter).Trim();
            string vUser = (aUserFilter == null ? "" : aUserFilter).Trim();
            string vWhere = BuildAuditWhere(vAction, vUser);

            using (SqliteConnection oConn = Acquire())
            {
                string vSQL = "SELECT id, ts, user_id, username, action, entity_type, " +
                    "entity_id, details, ip FROM audit_log";
                if (vWhere.Length != 0)
                    vSQL += " WHERE " + vWhere;
                vSQL += " ORDER BY id DESC LIMIT @lim OFFSET @off";
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    ApplyAuditParams(oCmd, vAction, vUser);
                    AddParam(oCmd, "@lim", aLimit);
                    AddParam(oCmd, "@off", aOffset);
                    using (SqliteDataReader oReader = oCmd.ExecuteReader())
                    {
                        while (oReader.Read())
                        {
                            TERPAuditRow vRow = new TERPAuditRow();
                            vRow.Id = RInt64(oReader, "id");
                            vRow.Ts = RDate(oReader, "ts");
                            vRow.UserId = RInt64(oReader, "user_id");
                            vRow.Username = RStr(oReader, "username");
                            vRow.Action = RStr(oReader, "action");
                            vRow.EntityType = RStr(oReader, "entity_type");
                            vRow.EntityId = RInt64(oReader, "entity_id");
                            vRow.Details = RStr(oReader, "details");
                            vRow.IP = RStr(oReader, "ip");
                            oResult.Add(vRow);
                        }
                    }
                }
            }
            return oResult.ToArray();
        }

        public int CountAuditLog(string aActionFilter, string aUserFilter)
        {
            string vAction = (aActionFilter == null ? "" : aActionFilter).Trim();
            string vUser = (aUserFilter == null ? "" : aUserFilter).Trim();
            string vWhere = BuildAuditWhere(vAction, vUser);

            using (SqliteConnection oConn = Acquire())
            {
                string vSQL = "SELECT COUNT(*) FROM audit_log";
                if (vWhere.Length != 0)
                    vSQL += " WHERE " + vWhere;
                using (SqliteCommand oCmd = NewCmd(oConn, vSQL))
                {
                    ApplyAuditParams(oCmd, vAction, vUser);
                    object vVal = oCmd.ExecuteScalar();
                    if (vVal == null || vVal == DBNull.Value)
                        return 0;
                    return (int)Convert.ToInt64(vVal, CultureInfo.InvariantCulture);
                }
            }
        }

        // ==================================================================== //
        //  firewall helpers (shared by blocked_ips / ignored_ips)              //
        // ==================================================================== //

        // List every row of the given firewall table, newest first.
        private TERPFirewallEntry[] ListFirewallTable(string aTable)
        {
            List<TERPFirewallEntry> oResult = new List<TERPFirewallEntry>();
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT ip, reason, created_at FROM " + aTable +
                " ORDER BY created_at DESC, ip COLLATE NOCASE"))
            using (SqliteDataReader oReader = oCmd.ExecuteReader())
            {
                while (oReader.Read())
                {
                    TERPFirewallEntry vRow = new TERPFirewallEntry();
                    vRow.IP = RStr(oReader, "ip");
                    vRow.Reason = RStr(oReader, "reason");
                    vRow.CreatedAt = RDate(oReader, "created_at");
                    oResult.Add(vRow);
                }
            }
            return oResult.ToArray();
        }

        // INSERT OR REPLACE a row in the given firewall table (created_at = now).
        private void AddFirewallEntry(string aTable, string aIP, string aReason)
        {
            string vIP = (aIP == null ? "" : aIP).Trim();
            if (vIP.Length == 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "INSERT OR REPLACE INTO " + aTable +
                    " (ip, reason, created_at) VALUES (@ip, @r, @c)",
                    "@ip", vIP, "@r", aReason, "@c", NowStr());
            }
        }

        // DELETE a row from the given firewall table by IP.
        private void RemoveFirewallEntry(string aTable, string aIP)
        {
            string vIP = (aIP == null ? "" : aIP).Trim();
            if (vIP.Length == 0)
                return;
            using (SqliteConnection oConn = Acquire())
            {
                ExecNonQuery(oConn,
                    "DELETE FROM " + aTable + " WHERE ip = @ip", "@ip", vIP);
            }
        }

        // True when aIP exists in the given firewall table.
        private bool FirewallTableContains(string aTable, string aIP)
        {
            string vIP = (aIP == null ? "" : aIP).Trim();
            if (vIP.Length == 0)
                return false;
            using (SqliteConnection oConn = Acquire())
            using (SqliteCommand oCmd = NewCmd(oConn,
                "SELECT 1 FROM " + aTable + " WHERE ip = @ip LIMIT 1"))
            {
                AddParam(oCmd, "@ip", vIP);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    return oReader.Read();
                }
            }
        }

        // --- firewall: blocked IPs --- //

        public TERPFirewallEntry[] ListBlockedIPs()
        {
            return ListFirewallTable("blocked_ips");
        }

        public void AddBlockedIP(string aIP, string aReason)
        {
            AddFirewallEntry("blocked_ips", aIP, aReason);
        }

        public void RemoveBlockedIP(string aIP)
        {
            RemoveFirewallEntry("blocked_ips", aIP);
        }

        public bool IsBlockedIP(string aIP)
        {
            return FirewallTableContains("blocked_ips", aIP);
        }

        // --- firewall: ignored IPs --- //

        public TERPFirewallEntry[] ListIgnoredIPs()
        {
            return ListFirewallTable("ignored_ips");
        }

        public void AddIgnoredIP(string aIP, string aReason)
        {
            AddFirewallEntry("ignored_ips", aIP, aReason);
        }

        public void RemoveIgnoredIP(string aIP)
        {
            RemoveFirewallEntry("ignored_ips", aIP);
        }

        public bool IsIgnoredIP(string aIP)
        {
            return FirewallTableContains("ignored_ips", aIP);
        }
    }
}
