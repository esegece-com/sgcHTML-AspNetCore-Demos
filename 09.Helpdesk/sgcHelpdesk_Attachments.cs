// ***************************************************************************
//  sgcHelpdeskWeb - Helpdesk support-ticket demo on ASP.NET Core
//  Attachment validation + disk storage (NEW host-layer file).
//
//  The 60.HTML demo parsed multipart/form-data by hand in sgcHelpdesk_Multipart.cs
//  and, in the SAME file, validated + persisted the uploaded files via
//  THelpdeskAttachStore. On ASP.NET Core the hand-rolled PARSER is replaced by the
//  framework's IFormFile (see HelpdeskWebHost.CollectUploads), so
//  sgcHelpdesk_Multipart.cs is NOT copied. Only the STORAGE half is reused here:
//  THelpdeskAttachStore + THelpdeskAttachSaved keep the EXACT storage semantics of
//  the 60.HTML demo (per-ticket directory data\attachments\<ticket_id>\, random
//  16-hex stored-name suffix, the same dangerous-extension block-list, the same
//  size / count caps and the same path-traversal-safe ResolvePath), but they now
//  take a THelpdeskUploadFile (built from IFormFile) instead of the parser's
//  THelpdeskMultipartField. The base directory is anchored to a caller-supplied
//  root (the exe dir) so attachments land in a stable place under Kestrel.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Helpdesk
{
    public static class HelpdeskUploadConst
    {
        // Hardcoded safety caps for this demo (no config-file knobs) - identical
        // to the 60.HTML HelpdeskMultipartConst values.
        public const long CS_HELPDESK_MAX_TOTAL_UPLOAD_BYTES = 10 * 1024 * 1024; // 10 MB / request
        public const int CS_HELPDESK_MAX_FILES_PER_REQUEST = 5;
    }

    // One uploaded file, materialized from an ASP.NET Core IFormFile. Field names
    // match the 60.HTML THelpdeskMultipartField members the store reads, so the
    // ported store bodies stay byte-for-byte identical.
    public class THelpdeskUploadFile
    {
        public string FileName = "";      // original client filename
        public string ContentType = "";   // browser-reported content type
        public byte[] FileBytes = Array.Empty<byte>();
    }

    // Result of persisting one file: the DB row is written from these fields.
    public class THelpdeskAttachSaved
    {
        public string OriginalName = "";
        public string StoredName = "";
        public string ContentType = "";
        public long SizeBytes;
    }

    // Validates and persists uploaded files for a ticket to disk, under
    // <root>\data\attachments\<ticket_id>\<stored_filename>. Ported verbatim from
    // the 60.HTML THelpdeskAttachStore (sgcHelpdesk_Multipart.cs); the only change
    // is the base directory is anchored to the caller-supplied root instead of the
    // process current directory, and the input type is THelpdeskUploadFile.
    public class THelpdeskAttachStore
    {
        private const string CS_HELPDESK_ATTACH_BASEDIR = "data\\attachments";

        private static readonly string[] CS_HELPDESK_DANGEROUS_EXTENSIONS =
        {
            ".exe", ".bat", ".cmd", ".com", ".scr", ".msi", ".dll", ".sh", ".ps1",
            ".js", ".vbs", ".jar", ".php", ".asp", ".aspx", ".py"
        };

        private readonly string FRoot;

        // aRoot anchors the relative data\attachments base (the exe directory under
        // Kestrel). Empty falls back to the process current directory.
        public THelpdeskAttachStore(string aRoot)
        {
            FRoot = string.IsNullOrEmpty(aRoot) ? Directory.GetCurrentDirectory() : aRoot;
        }

        private string ResolveBaseDir()
        {
            string vResult = CS_HELPDESK_ATTACH_BASEDIR;
            if (!Path.IsPathRooted(vResult))
                vResult = Path.Combine(FRoot, vResult);
            return vResult;
        }

        private string SafeBaseName(string aName)
        {
            // Strip any directory components.
            string vClean = aName ?? "";
            for (int vI = vClean.Length - 1; vI >= 0; vI--)
            {
                if (vClean[vI] == '/' || vClean[vI] == '\\')
                {
                    vClean = vClean.Substring(vI + 1);
                    break;
                }
            }

            string vExt = Path.GetExtension(vClean);
            string vBase = vClean.Substring(0, vClean.Length - vExt.Length);

            // Keep alphanumeric, underscore, dash, dot. Replace anything else
            // with '_'. This also prevents Unicode path traversal tricks.
            StringBuilder vResult = new StringBuilder();
            for (int vI = 0; vI < vBase.Length; vI++)
            {
                char vCh = vBase[vI];
                bool vAllowed = (vCh >= 'A' && vCh <= 'Z') || (vCh >= 'a' && vCh <= 'z') ||
                    (vCh >= '0' && vCh <= '9') || vCh == '_' || vCh == '-' || vCh == '.';
                vResult.Append(vAllowed ? vCh : '_');
            }
            string vName = vResult.ToString();
            if (vName.Length == 0)
                vName = "file";
            return vName + vExt.ToLowerInvariant();
        }

        // Random hex prefix via a fresh GUID (avoids collisions and stops
        // original names from ever being used as-is on disk).
        private string GenerateStoredName(string aOriginal)
        {
            string vSafe = SafeBaseName(aOriginal);
            string vExt = Path.GetExtension(vSafe);
            string vBase = vSafe.Substring(0, vSafe.Length - vExt.Length);
            if (vBase.Length == 0)
                vBase = "file";

            string vHex = Guid.NewGuid().ToString("N").ToLowerInvariant();
            return vBase + "_" + vHex.Substring(0, 16) + vExt;
        }

        private bool IsDangerousExtension(string aName)
        {
            string vExt = Path.GetExtension(aName ?? "").ToLowerInvariant();
            if (vExt.Length == 0)
                return false;
            for (int vI = 0; vI < CS_HELPDESK_DANGEROUS_EXTENSIONS.Length; vI++)
                if (vExt == CS_HELPDESK_DANGEROUS_EXTENSIONS[vI])
                    return true;
            return false;
        }

        private bool DoValidate(THelpdeskUploadFile[] aFiles, out string aError)
        {
            aError = "";

            // Count only genuine file parts.
            int vFileCount = 0;
            long vTotalBytes = 0;
            for (int vI = 0; vI < aFiles.Length; vI++)
                if ((aFiles[vI].FileName ?? "").Trim().Length > 0 && aFiles[vI].FileBytes.Length > 0)
                {
                    vFileCount++;
                    vTotalBytes += aFiles[vI].FileBytes.Length;
                }

            if (vFileCount == 0)
                return true;

            if (vFileCount > HelpdeskUploadConst.CS_HELPDESK_MAX_FILES_PER_REQUEST)
            {
                aError = string.Format("Too many files: at most {0} attachments are " +
                    "allowed per submission.",
                    HelpdeskUploadConst.CS_HELPDESK_MAX_FILES_PER_REQUEST);
                return false;
            }

            if (vTotalBytes > HelpdeskUploadConst.CS_HELPDESK_MAX_TOTAL_UPLOAD_BYTES)
            {
                aError = string.Format("Attachments are too large: the total upload size " +
                    "must not exceed {0} MB.",
                    HelpdeskUploadConst.CS_HELPDESK_MAX_TOTAL_UPLOAD_BYTES / (1024 * 1024));
                return false;
            }

            for (int vI = 0; vI < aFiles.Length; vI++)
            {
                if ((aFiles[vI].FileName ?? "").Trim().Length == 0 ||
                    aFiles[vI].FileBytes.Length == 0)
                    continue;
                if (IsDangerousExtension(aFiles[vI].FileName))
                {
                    aError = string.Format("File \"{0}\" has a file type that is not " +
                        "allowed for security reasons.", aFiles[vI].FileName);
                    return false;
                }
            }
            return true;
        }

        // Pure validation (no disk I/O). Call this BEFORE creating the parent
        // ticket/message row so a rejected upload never leaves an orphan behind.
        public bool ValidateFiles(THelpdeskUploadFile[] aFiles, out string aError)
        {
            return DoValidate(aFiles, out aError);
        }

        // Re-validates then persists the files to disk under
        // data\attachments\<ticket_id>\. Call only after the parent ticket exists.
        public THelpdeskAttachSaved[] SaveFiles(long aTicketId,
            THelpdeskUploadFile[] aFiles, out string aError)
        {
            List<THelpdeskAttachSaved> vResult = new List<THelpdeskAttachSaved>();
            if (!DoValidate(aFiles, out aError))
                return vResult.ToArray();

            string vDir = Path.Combine(ResolveBaseDir(),
                aTicketId.ToString(CultureInfo.InvariantCulture));
            if (!Directory.Exists(vDir))
            {
                try
                {
                    Directory.CreateDirectory(vDir);
                }
                catch (Exception E)
                {
                    aError = "Could not create the attachments directory: " + E.Message;
                    return vResult.ToArray();
                }
            }

            for (int vI = 0; vI < aFiles.Length; vI++)
            {
                THelpdeskUploadFile oField = aFiles[vI];
                if ((oField.FileName ?? "").Trim().Length == 0 || oField.FileBytes.Length == 0)
                    continue;

                THelpdeskAttachSaved oSaved = new THelpdeskAttachSaved
                {
                    OriginalName = oField.FileName,
                    StoredName = GenerateStoredName(oField.FileName),
                    ContentType = oField.ContentType,
                    SizeBytes = oField.FileBytes.Length
                };
                string vFullPath = Path.Combine(vDir, oSaved.StoredName);
                File.WriteAllBytes(vFullPath, oField.FileBytes);

                vResult.Add(oSaved);
            }
            return vResult.ToArray();
        }

        // Resolve the on-disk path for a stored attachment. Returns "" when the
        // stored name contains a path separator / traversal token or the file
        // does not exist (path-traversal safe).
        public string ResolvePath(long aTicketId, string aStoredName)
        {
            string vClean = (aStoredName ?? "").Trim();
            if (vClean.Length == 0)
                return "";
            if (vClean.IndexOf("..", StringComparison.Ordinal) >= 0 ||
                vClean.IndexOf('/') >= 0 || vClean.IndexOf('\\') >= 0 ||
                vClean.IndexOf(':') >= 0)
                return "";

            string vPath = Path.Combine(ResolveBaseDir(),
                aTicketId.ToString(CultureInfo.InvariantCulture), vClean);
            if (!File.Exists(vPath))
                return "";
            return vPath;
        }
    }
}
