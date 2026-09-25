// ***************************************************************************
//  sgcReports - reporting and BI portal web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\15.Reports\sgcReports_Types.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************

using System;
using System.Collections.Generic;

namespace Reports
{
    /// <summary>Reports demo domain error. Mirrors Delphi EReportsError.</summary>
    public class EReportsError : Exception
    {
        public EReportsError(string message) : base(message) { }
    }

    /// <summary>Constants shared by every unit of the demo.</summary>
    public static class ReportsConst
    {
        // Default listen port for the standalone server. 5700-5707 are taken by the
        // sibling 60.HTML demos, so this one lands on 5708.
        public const int CS_REPORTS_DEFAULT_PORT = 5708;

        // Roles. 'admin' builds and schedules reports and manages users, 'analyst'
        // runs reports, saves views and exports, 'viewer' runs and reads only (no
        // raw-data export).
        public const string CS_ROLE_ADMIN = "admin";
        public const string CS_ROLE_ANALYST = "analyst";
        public const string CS_ROLE_VIEWER = "viewer";

        // Brand accent of this demo (indigo), used by the page CSS and by the
        // server-side PDF writer so an exported report matches the screen.
        public const string CS_BRAND_ACCENT = "#4F46E5";
    }

    // Delphi records are ported as mutable reference classes (the demo passes
    // them around and mutates fields), with public fields matching 1:1.

    public class TReportsUser
    {
        public long Id;
        public string Username = "";
        public string PasswordHash = "";
        public string Role = "";
        public string DisplayName = "";
        public DateTime CreatedAt;
    }

    public class TReportsPasskey
    {
        public long Id;
        public long UserId;
        public string CredentialId = "";
        public string PublicKey = "";
        public long SignCount;
        public string DeviceName = "";
        public DateTime CreatedAt;
        public DateTime LastUsedAt;
    }

    // A stored report definition. SqlText is the ONE place this demo accepts
    // raw SQL, and only from an admin: TReportsDBPool.ValidateReportSQL rejects
    // anything that is not a single read-only SELECT before it is ever run, and
    // every parameter is bound, never concatenated.
    public class TReportsReportDef
    {
        public long Id;
        public string Name = "";
        public string Description = "";
        public string Category = "";
        public string SqlText = "";
        public string ParamsJSON = "";
        public string ChartKind = "";
        public long OwnerId;
        public bool Shared;
        public DateTime CreatedAt;
    }

    // One row of the run history. DurationMS / RowCount are what make the
    // throughput claim checkable instead of merely asserted.
    public class TReportsRun
    {
        public long Id;
        public long ReportId;
        public string ReportName = "";
        public long UserId;
        public string Username = "";
        public string ParamsJSON = "";
        public int RowCount;
        public int DurationMS;
        public string Status = "";
        public DateTime CreatedAt;
    }

    public class TReportsSavedView
    {
        public long Id;
        public long UserId;
        public string Username = "";
        public long ReportId;
        public string ReportName = "";
        public string Name = "";
        public string StateJSON = "";
        public DateTime CreatedAt;
    }

    public class TReportsSchedule
    {
        public long Id;
        public long ReportId;
        public string ReportName = "";
        public string CronText = "";
        public string Format = ""; // 'pdf' | 'xlsx' | 'csv'
        public string Recipients = "";
        public DateTime LastRunAt;
        public DateTime NextRunAt;
        public bool Active;
    }

    // A declared report parameter, parsed out of report_defs.params_json. The
    // parameter form is rendered from these and the values are bound by name.
    // Kind: 'date' | 'int' | 'text' | 'list' | 'range'.
    public class TReportsParamDef
    {
        public string Name = "";
        public string Caption = "";
        public string Kind = "";
        public string DefaultValue = "";
        public string Options = ""; // 'value|caption,value|caption' for Kind='list'
        public int MinValue;
        public int MaxValue;
    }

    // One bound parameter value on its way into a command parameter.
    public class TReportsParamValue
    {
        public string Name = "";
        public string Kind = "";
        public string Value = "";
    }

    // Everything the /explore page filters on. Each field maps to one bound
    // parameter in the SQL; Sort / Dir are whitelisted in the DB layer before
    // they are allowed anywhere near an ORDER BY.
    public class TReportsExploreFilter
    {
        public string Search = "";
        public string Region = "";
        public string Segment = "";
        public string Category = "";
        // Several statuses may be selected at once (the MultiSelect on the filter
        // bar). Each one becomes its own bound parameter in an IN list.
        public string[] Statuses = new string[0];
        public string DateFrom = ""; // 'yyyy-MM-dd', '' = no bound
        public string DateTo = "";
        public int MinQty;           // 0 = no lower bound
        public int MaxQty;           // 0 = no upper bound
        public string Sort = "";
        public string Dir = "";
    }

    // One bucket of a dashboard time series.
    public class TReportsSeriesPoint
    {
        public string BucketLabel = "";
        public double Value1;
        public double Value2;
    }

    // Aggregated headline numbers for the dashboard home.
    public class TReportsKPIs
    {
        public double Revenue12M;
        public double RevenuePrev12M;
        public int Orders12M;
        public double Margin12MPct;
        public double AvgOrderValue;
        public int OpenOrders;
        public int Customers;
        public int LinesTotal;
    }

    // Server configuration. Defaults are applied in the field initializers so the
    // server runs out-of-the-box even when the JSON config file is absent.
    public class TReportsServerConfig
    {
        public string ListenAddress = "0.0.0.0";
        public int ListenPort = ReportsConst.CS_REPORTS_DEFAULT_PORT;
        public string DatabaseFile = "data\\reports.db";
        public string AdminUser = "admin";
        public string AdminPassword = "admin";
    }
}
