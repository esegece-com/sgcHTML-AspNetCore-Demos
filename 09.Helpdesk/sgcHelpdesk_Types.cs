// ***************************************************************************
//  sgcHelpdesk - support-ticket helpdesk web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\09.Helpdesk\sgcHelpdesk_Types.pas
//
//  Delphi records are ported as mutable reference classes (the demo passes
//  them around and mutates fields), with public fields matching 1:1.
// ***************************************************************************

using System;

namespace Helpdesk
{
    /// <summary>Helpdesk domain error. Mirrors Delphi EHelpdeskError.</summary>
    public class EHelpdeskError : Exception
    {
        public EHelpdeskError(string message) : base(message) { }
    }

    public static class HelpdeskConst
    {
        // Default listen port for the standalone server.
        public const int CS_HELPDESK_DEFAULT_PORT = 5704;
    }

    // Role = "user" | "admin"
    public class THelpdeskUser
    {
        public long Id;
        public string Username = "";
        public string PasswordHash = "";
        public string Role = "";
        public DateTime CreatedAt;
    }

    // Status = "new" | "pending_resolution" | "pending_feedback" | "closed"
    public class THelpdeskTicket
    {
        public long Id;
        public long UserId;
        public string Username = ""; // owner display name, joined in for list/detail views
        public string Subject = "";
        public string Status = "";
        // "low" | "medium" | "high" | "critical" (Kanban swimlane + SLA due date)
        public string Priority = "";
        // "general" | "account" | "billing" | "technical" (Kanban card tag)
        public string Category = "";
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
    }

    public class THelpdeskAttachment
    {
        public long Id;
        public long MessageId;
        public long TicketId;
        public string OriginalFilename = "";
        public string StoredFilename = "";
        public string ContentType = "";
        public long SizeBytes;
        public DateTime CreatedAt;
    }

    public class THelpdeskMessage
    {
        public long Id;
        public long TicketId;
        public long UserId;
        public string Username = ""; // author display name, joined in for the thread view
        public bool IsAdmin;
        public string Body = "";
        public DateTime CreatedAt;
        public THelpdeskAttachment[] Attachments = Array.Empty<THelpdeskAttachment>();
    }

    // One bucket of the admin dashboard's "tickets opened vs closed" chart.
    // BucketLabel is a short display label (e.g. '07-16', 'Jan 2026', '14:00');
    // BucketStart is the bucket's start instant, kept for callers that need it.
    public class THelpdeskActivityPoint
    {
        public string BucketLabel = "";
        public DateTime BucketStart;
        public int Opened;
        public int Closed;
    }

    // Server configuration. Defaults applied in the constructor so the server
    // runs out-of-the-box even when the JSON config file is absent.
    public class THelpdeskServerConfig
    {
        public string ListenAddress = "0.0.0.0";
        public int ListenPort = HelpdeskConst.CS_HELPDESK_DEFAULT_PORT;
        public string DatabaseFile = "data\\helpdesk.db";
        public string AdminUser = "admin";
        public string AdminPassword = "admin";
    }
}
