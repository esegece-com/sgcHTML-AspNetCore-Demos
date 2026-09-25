// ***************************************************************************
//  sgcField - field service management web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\17.FieldService\sgcField_Types.pas
//
//  Delphi records are ported as mutable reference classes (the demo passes
//  them around and mutates fields), with public fields matching 1:1.
// ***************************************************************************

using System;
using System.Collections.Generic;

namespace FieldService
{
    /// <summary>Field service domain error. Mirrors Delphi EFieldError.</summary>
    public class EFieldError : Exception
    {
        public EFieldError(string message) : base(message) { }
    }

    public static class FieldConst
    {
        // Default listen port for the standalone server. 5700-5709 are taken by
        // the other 60.HTML runtime demos.
        public const int CS_FIELD_DEFAULT_PORT = 5710;

        // Roles.
        public const string CS_ROLE_DISPATCHER = "dispatcher";
        public const string CS_ROLE_TECHNICIAN = "technician";
        public const string CS_ROLE_MANAGER = "manager";
        public const string CS_ROLE_CUSTOMER = "customer";

        // Job status state machine. A job never leaves this set; every transition
        // goes through FieldCanTransition (see below), which the server enforces.
        public const string CS_JOB_NEW = "new";
        public const string CS_JOB_SCHEDULED = "scheduled";
        public const string CS_JOB_ENROUTE = "enroute";
        public const string CS_JOB_ONSITE = "onsite";
        public const string CS_JOB_COMPLETE = "complete";
        public const string CS_JOB_CANCELLED = "cancelled";

        // Priorities, lowest to highest.
        public const string CS_PRIORITY_LOW = "low";
        public const string CS_PRIORITY_NORMAL = "normal";
        public const string CS_PRIORITY_HIGH = "high";
        public const string CS_PRIORITY_URGENT = "urgent";

        // ----- job status state machine ----- //

        // Every status the machine knows, in lifecycle order.
        public static string[] FieldStatusList()
        {
            return new string[]
            {
                CS_JOB_NEW, CS_JOB_SCHEDULED, CS_JOB_ENROUTE, CS_JOB_ONSITE,
                CS_JOB_COMPLETE, CS_JOB_CANCELLED
            };
        }

        // True when aStatus is one of the six known values.
        public static bool FieldIsStatus(string aStatus)
        {
            string vS = (aStatus ?? "").Trim().ToLowerInvariant();
            return (vS == CS_JOB_NEW) || (vS == CS_JOB_SCHEDULED) ||
                (vS == CS_JOB_ENROUTE) || (vS == CS_JOB_ONSITE) ||
                (vS == CS_JOB_COMPLETE) || (vS == CS_JOB_CANCELLED);
        }

        // The whole machine in one place:
        //
        //   new -------> scheduled -------> enroute -------> onsite -------> complete
        //    |               |                  |               |
        //    +---------------+------------------+---------------+----> cancelled
        //                    |                  |
        //                    +<-----------------+   (technician sent back to the queue)
        //    +<--------------+                      (job unassigned again)
        //
        // complete and cancelled are terminal. Anything not drawn above is refused.
        public static bool FieldCanTransition(string aFrom, string aTo)
        {
            bool vResult = false;
            string vFrom = (aFrom ?? "").Trim().ToLowerInvariant();
            string vTo = (aTo ?? "").Trim().ToLowerInvariant();
            if ((!FieldIsStatus(vFrom)) || (!FieldIsStatus(vTo)))
                return vResult;
            if (vFrom == vTo)
                return vResult;
            // Terminal states never move again.
            if ((vFrom == CS_JOB_COMPLETE) || (vFrom == CS_JOB_CANCELLED))
                return vResult;
            // Anything still live can be cancelled.
            if (vTo == CS_JOB_CANCELLED)
                return true;
            if (vFrom == CS_JOB_NEW)
                vResult = (vTo == CS_JOB_SCHEDULED);
            else if (vFrom == CS_JOB_SCHEDULED)
                vResult = (vTo == CS_JOB_ENROUTE) || (vTo == CS_JOB_NEW);
            else if (vFrom == CS_JOB_ENROUTE)
                vResult = (vTo == CS_JOB_ONSITE) || (vTo == CS_JOB_SCHEDULED);
            else if (vFrom == CS_JOB_ONSITE)
                vResult = (vTo == CS_JOB_COMPLETE);
            return vResult;
        }

        // The transitions legal from aStatus, for rendering only the allowed actions.
        public static string[] FieldNextStatuses(string aStatus)
        {
            string[] vAll = FieldStatusList();
            List<string> vOut = new List<string>();
            for (int vI = 0; vI < vAll.Length; vI++)
                if (FieldCanTransition(aStatus, vAll[vI]))
                    vOut.Add(vAll[vI]);
            return vOut.ToArray();
        }

        // Human label / Bootstrap colour suffix for a status.
        public static string FieldStatusLabel(string aStatus)
        {
            string vS = (aStatus ?? "").Trim().ToLowerInvariant();
            if (vS == CS_JOB_SCHEDULED)
                return "Scheduled";
            if (vS == CS_JOB_ENROUTE)
                return "En route";
            if (vS == CS_JOB_ONSITE)
                return "On site";
            if (vS == CS_JOB_COMPLETE)
                return "Complete";
            if (vS == CS_JOB_CANCELLED)
                return "Cancelled";
            return "New";
        }

        public static string FieldStatusColorName(string aStatus)
        {
            string vS = (aStatus ?? "").Trim().ToLowerInvariant();
            if (vS == CS_JOB_SCHEDULED)
                return "primary";
            if (vS == CS_JOB_ENROUTE)
                return "info";
            if (vS == CS_JOB_ONSITE)
                return "warning";
            if (vS == CS_JOB_COMPLETE)
                return "success";
            if (vS == CS_JOB_CANCELLED)
                return "secondary";
            return "dark";
        }

        // True once the job can no longer move.
        public static bool FieldStatusIsFinal(string aStatus)
        {
            string vS = (aStatus ?? "").Trim().ToLowerInvariant();
            return (vS == CS_JOB_COMPLETE) || (vS == CS_JOB_CANCELLED);
        }

        // 0-based lifecycle index (new=0 .. complete=4); cancelled returns -1.
        public static int FieldStatusIndex(string aStatus)
        {
            string vS = (aStatus ?? "").Trim().ToLowerInvariant();
            if (vS == CS_JOB_NEW)
                return 0;
            if (vS == CS_JOB_SCHEDULED)
                return 1;
            if (vS == CS_JOB_ENROUTE)
                return 2;
            if (vS == CS_JOB_ONSITE)
                return 3;
            if (vS == CS_JOB_COMPLETE)
                return 4;
            return -1;
        }

        // ----- priority helpers ----- //

        public static bool FieldIsPriority(string aValue)
        {
            string vS = (aValue ?? "").Trim().ToLowerInvariant();
            return (vS == CS_PRIORITY_LOW) || (vS == CS_PRIORITY_NORMAL) ||
                (vS == CS_PRIORITY_HIGH) || (vS == CS_PRIORITY_URGENT);
        }

        public static string FieldPriorityLabel(string aValue)
        {
            string vS = (aValue ?? "").Trim().ToLowerInvariant();
            if (vS == CS_PRIORITY_LOW)
                return "Low";
            if (vS == CS_PRIORITY_HIGH)
                return "High";
            if (vS == CS_PRIORITY_URGENT)
                return "Urgent";
            return "Normal";
        }

        public static string FieldPriorityColorName(string aValue)
        {
            string vS = (aValue ?? "").Trim().ToLowerInvariant();
            if (vS == CS_PRIORITY_LOW)
                return "secondary";
            if (vS == CS_PRIORITY_HIGH)
                return "warning";
            if (vS == CS_PRIORITY_URGENT)
                return "danger";
            return "info";
        }

        public static int FieldPriorityRank(string aValue)
        {
            string vS = (aValue ?? "").Trim().ToLowerInvariant();
            if (vS == CS_PRIORITY_URGENT)
                return 3;
            if (vS == CS_PRIORITY_HIGH)
                return 2;
            if (vS == CS_PRIORITY_LOW)
                return 0;
            return 1;
        }
    }

    // role = "dispatcher" | "technician" | "manager" | "customer"
    public class TFieldUser
    {
        public long Id;
        public string Username = "";
        public string PasswordHash = "";
        public string Role = "";
        public string DisplayName = "";
        public string Phone = "";
        public string AvatarInitials = "";
        public DateTime CreatedAt;
    }

    public class TFieldPasskey
    {
        public long Id;
        public long UserId;
        public string CredentialId = "";
        public string PublicKey = "";
        public string DeviceName = "";
        public long SignCount;
        public DateTime CreatedAt;
        public DateTime LastUsedAt;
    }

    // A technician row joined to its user row. CurrentLat/CurrentLng come from
    // the most recent job_event carrying coordinates, falling back to the home
    // base; that is what the live map plots.
    public class TFieldTechnician
    {
        public long Id;
        public long UserId;
        public string Username = "";
        public string DisplayName = "";
        public string AvatarInitials = "";
        public string Phone = "";
        public string Skills = "";
        public double HomeLat;
        public double HomeLng;
        public bool Active;
        public double HourlyRate;
        public double CurrentLat;
        public double CurrentLng;
        // Derived, not stored: "onsite" / "enroute" / "idle" / "off".
        public string Presence = "";
        public int OpenJobs;
    }

    public class TFieldCustomer
    {
        public long Id;
        public string Name = "";
        public string Contact = "";
        public string Email = "";
        public string Phone = "";
        public string Address = "";
        public string City = "";
        public double Lat;
        public double Lng;
        public DateTime CreatedAt;
        // Derived (list view only).
        public int SiteCount;
        public int JobCount;
    }

    public class TFieldSite
    {
        public long Id;
        public long CustomerId;
        public string CustomerName = "";
        public string Name = "";
        public string Address = "";
        public double Lat;
        public double Lng;
        public string AccessNotes = "";
    }

    public class TFieldAsset
    {
        public long Id;
        public long SiteId;
        public string SiteName = "";
        public string CustomerName = "";
        public long ParentId;
        public string Name = "";
        public string Model = "";
        public string Serial = "";
        public DateTime InstalledAt;
        public DateTime WarrantyUntil;
        public string Status = "";
    }

    public class TFieldJob
    {
        public long Id;
        public string Reference = "";
        public long CustomerId;
        public string CustomerName = "";
        public long SiteId;
        public string SiteName = "";
        public string SiteAddress = "";
        public double SiteLat;
        public double SiteLng;
        public long AssetId;
        public string AssetName = "";
        public long TechnicianId;
        public string TechnicianName = "";
        public string TechnicianInitials = "";
        public string Title = "";
        public string Description = "";
        public string Priority = "";
        public string Status = "";
        public DateTime ScheduledStart;
        public DateTime ScheduledEnd;
        public DateTime ActualStart;
        public DateTime ActualEnd;
        public DateTime SlaDueAt;
        public DateTime CreatedAt;
    }

    public class TFieldJobEvent
    {
        public long Id;
        public long JobId;
        public long UserId;
        public string UserName = "";
        public string Kind = "";
        public string Detail = "";
        public double Lat;
        public double Lng;
        public DateTime CreatedAt;
    }

    public class TFieldChecklistItem
    {
        public long Id;
        public long JobId;
        public int Position;
        public string Text_ = "";
        public bool Done;
        public DateTime DoneAt;
    }

    public class TFieldPart
    {
        public long Id;
        public string Sku = "";
        public string Name = "";
        public double UnitPrice;
        public int Stock;
    }

    public class TFieldJobPart
    {
        public long Id;
        public long JobId;
        public long PartId;
        public string Sku = "";
        public string Name = "";
        public int Qty;
        public double UnitPrice;
    }

    public class TFieldJobPhoto
    {
        public long Id;
        public long JobId;
        public string Filename = "";
        public string ContentType = "";
        public long SizeBytes;
        public string Caption = "";
        public DateTime CreatedAt;
    }

    public class TFieldSignature
    {
        public long Id;
        public long JobId;
        public string SignerName = "";
        public string SignaturePng = ""; // data:image/png;base64,... as captured by the pad
        public DateTime SignedAt;
    }

    public class TFieldMessage
    {
        public long Id;
        public long JobId;
        public long FromUserId;
        public string FromName = "";
        public string FromInitials = "";
        public string FromRole = "";
        public long ToUserId;
        public string Body = "";
        public DateTime CreatedAt;
        public DateTime ReadAt;
    }

    public class TFieldAuditEntry
    {
        public long Id;
        public long UserId;
        public string UserName = "";
        public string Action = "";
        public string Entity = "";
        public long EntityId;
        public string Detail = "";
        public string IP = "";
        public DateTime CreatedAt;
    }

    // Generic label/value pair used by every chart, sparkline and stat strip.
    public class TFieldPoint
    {
        public string Label_ = "";
        public double Value;
        public double Value2;
    }

    // One cell of the "jobs by weekday / hour" heatmap.
    public class TFieldHeatCell
    {
        public int Weekday; // 0 = Monday .. 6 = Sunday
        public int Hour;    // 0..23
        public int Count;
    }

    // Aggregated manager numbers for /reports.
    public class TFieldReportStats
    {
        public int TotalJobs;
        public int CompletedJobs;
        public int CancelledJobs;
        public int OpenJobs;
        public int SlaMet;
        public int SlaBreached;
        public int FirstTimeFix;
        public int Revisits;
        public double AvgSatisfaction;
        public int RatedJobs;
        public double PartsRevenue;
        public double LabourHours;
    }

    // Per-technician performance row for /reports.
    public class TFieldTechStat
    {
        public long TechnicianId;
        public string DisplayName = "";
        public string AvatarInitials = "";
        public int Completed;
        public int SlaMet;
        public double Hours;
        public double Utilisation;
        public double Satisfaction;
        public string Trend = ""; // comma separated weekly counts, for the sparkline
    }

    // Server configuration. Defaults are applied in the field initialisers so
    // the server runs out-of-the-box even when the JSON config file is absent.
    public class TFieldServerConfig
    {
        public string ListenAddress = "0.0.0.0";
        public int ListenPort = FieldConst.CS_FIELD_DEFAULT_PORT;
        public string DatabaseFile = "data\\field.db";
        public string AdminUser = "dispatch";
        public string AdminPassword = "dispatch";
    }
}
