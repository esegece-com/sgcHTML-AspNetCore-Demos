// ***************************************************************************
//  sgcField - multipart/form-data request body parser + job-photo
//  save/validate helper (managed port)
//  Port of delphi\Demos\60.HTML\17.FieldService\sgcField_Multipart.pas
//
//  In this ASP.NET Core mirror the multipart body is parsed by ASP.NET Core
//  (the web host fills the field records), so the hand-written parser class of
//  the 60.HTML version is not included; only the field record, the limits and
//  the file save/validate store are kept.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
// sgc
using esegece.sgcWebSockets;

namespace FieldService
{
    public static class FieldMultipartConst
    {
        // Hardcoded safety caps for this demo (no config-file knobs).
        public const long CS_FIELD_MAX_TOTAL_UPLOAD_BYTES = 10 * 1024 * 1024; // 10 MB / request
        public const int CS_FIELD_MAX_FILES_PER_REQUEST = 5;
        // Slack on top of the total upload cap for boundaries/headers and the
        // non-file form fields sharing the same body.
        public const long CS_FIELD_MAX_BODY_BYTES =
            CS_FIELD_MAX_TOTAL_UPLOAD_BYTES + (1 * 1024 * 1024);
    }

    public class TFieldMultipartField
    {
        public string Name = "";          // form field name
        public string FileName = "";      // empty for non-file fields
        public string ContentType = "";   // empty for non-file fields
        public string TextValue = "";     // for non-file fields
        public byte[] FileBytes = Array.Empty<byte>(); // for file fields
    }

    public class TFieldPhotoSaved
    {
        public string OriginalName = "";
        public string StoredName = "";
        public string ContentType = "";
        public long SizeBytes;
    }

    // Validates and persists uploaded files for a ticket to disk, under
    // data\photos\<ticket_id>\<stored_filename> (relative to the exe dir).
    public class TFieldPhotoStore
    {
        private const string CS_FIELD_PHOTO_BASEDIR = "data\\photos";

        private static readonly string[] CS_FIELD_DANGEROUS_EXTENSIONS =
        {
            ".exe", ".bat", ".cmd", ".com", ".scr", ".msi", ".dll", ".sh", ".ps1",
            ".js", ".vbs", ".jar", ".php", ".asp", ".aspx", ".py"
        };

        private string ResolveBaseDir()
        {
            string vResult = CS_FIELD_PHOTO_BASEDIR;
            if (!Path.IsPathRooted(vResult))
                vResult = Path.Combine(Directory.GetCurrentDirectory(), vResult);
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
            for (int vI = 0; vI < CS_FIELD_DANGEROUS_EXTENSIONS.Length; vI++)
                if (vExt == CS_FIELD_DANGEROUS_EXTENSIONS[vI])
                    return true;
            return false;
        }

        private bool DoValidate(TFieldMultipartField[] aFiles, out string aError)
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

            if (vFileCount > FieldMultipartConst.CS_FIELD_MAX_FILES_PER_REQUEST)
            {
                aError = string.Format("Too many files: at most {0} attachments are " +
                    "allowed per submission.",
                    FieldMultipartConst.CS_FIELD_MAX_FILES_PER_REQUEST);
                return false;
            }

            if (vTotalBytes > FieldMultipartConst.CS_FIELD_MAX_TOTAL_UPLOAD_BYTES)
            {
                aError = string.Format("Attachments are too large: the total upload size " +
                    "must not exceed {0} MB.",
                    FieldMultipartConst.CS_FIELD_MAX_TOTAL_UPLOAD_BYTES / (1024 * 1024));
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
        public bool ValidateFiles(TFieldMultipartField[] aFiles, out string aError)
        {
            return DoValidate(aFiles, out aError);
        }

        // Re-validates then persists the files to disk under
        // data\photos\<ticket_id>\. Call only after the parent ticket exists.
        public TFieldPhotoSaved[] SaveFiles(long aTicketId,
            TFieldMultipartField[] aFiles, out string aError)
        {
            List<TFieldPhotoSaved> vResult = new List<TFieldPhotoSaved>();
            if (!DoValidate(aFiles, out aError))
                return vResult.ToArray();

            string vDir = Path.Combine(ResolveBaseDir(),
                aTicketId.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
                TFieldMultipartField oField = aFiles[vI];
                if ((oField.FileName ?? "").Trim().Length == 0 || oField.FileBytes.Length == 0)
                    continue;

                TFieldPhotoSaved oSaved = new TFieldPhotoSaved
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
                aTicketId.ToString(System.Globalization.CultureInfo.InvariantCulture), vClean);
            if (!File.Exists(vPath))
                return "";
            return vPath;
        }
    }
}
