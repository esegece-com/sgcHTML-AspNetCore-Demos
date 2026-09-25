// ***************************************************************************
//  sgcSaaS - multi-tenant SaaS control plane demo (managed port)
//  Port of delphi\Demos\60.HTML\01.RunTime\16.SaaS\sgcSaaS_Sessions.pas
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************
//
//  The Delphi unit uses BCryptGenRandom for the token bytes; the managed port
//  uses RandomNumberGenerator.Fill, which is the same CSPRNG source and works
//  on every platform (the Delphi unit raises outside Windows).

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace SaaS
{
    // In-memory session record, stored by reference in the dictionary.
    //
    // TenantId is THE authority for every tenant-scoped query in this demo. It
    // is written once, here, from the authenticated user row; no request path,
    // query string, form field, header or cookie can ever change it.
    //
    // While an impersonation is active, UserId / Role / TenantId describe the
    // IMPERSONATED user, and the Real* fields keep the vendor operator who
    // started it, so "stop impersonating" restores the original identity on the
    // same cookie and every audit row can name both parties.
    public class TSaaSSession
    {
        public string Token = "";
        public long UserId;
        public string Username = "";
        public string Role = "";
        public string DisplayName = "";
        public string Email = "";
        public long TenantId;
        public string TenantName = "";
        public string TenantSlug = "";
        // Impersonation bookkeeping. Impersonating = RealUserId <> 0.
        public long RealUserId;
        public string RealUsername = "";
        public string RealRole = "";
        public DateTime CreatedAt;
        public DateTime ExpiresAt;
        public string IP = "";

        // Value copy, so a stored session handed to a caller cannot be mutated
        // in place the way the Delphi record copy semantics prevent.
        public TSaaSSession Clone()
        {
            return (TSaaSSession)MemberwiseClone();
        }
    }

    // Thread-safe in-memory session store. A single lock guards the dictionary;
    // every public method enters/leaves it.
    public class TSaaSSessionStore
    {
        private readonly object FLock = new object();
        private readonly Dictionary<string, TSaaSSession> FSessions;
        private readonly int FTTLMinutes;
        private readonly bool FSlidingExpiry;

        // aTTLMinutes <= 0 defaults to 8 hours (480 minutes).
        public TSaaSSessionStore(int aTTLMinutes = 480, bool aSlidingExpiry = true)
        {
            FTTLMinutes = aTTLMinutes;
            if (FTTLMinutes <= 0)
                FTTLMinutes = 480;
            FSlidingExpiry = aSlidingExpiry;
            FSessions = new Dictionary<string, TSaaSSession>();
        }

        private string GenerateToken()
        {
            return SaaSSessions.SaaSRandomToken(32);
        }

        // Create a fresh session for aUser, returning the random token. The tenant
        // identity comes from the caller (resolved from the users row), never from
        // the request.
        public string CreateSession(TSaaSUser aUser, string aTenantName,
            string aTenantSlug, string aIP)
        {
            DateTime vNow = DateTime.Now;
            TSaaSSession oSession = new TSaaSSession();
            oSession.Token = GenerateToken();
            oSession.UserId = aUser.Id;
            oSession.Username = aUser.Username;
            oSession.Role = aUser.Role;
            oSession.DisplayName = aUser.DisplayName;
            oSession.Email = aUser.Email;
            oSession.TenantId = aUser.TenantId;
            oSession.TenantName = aTenantName;
            oSession.TenantSlug = aTenantSlug;
            oSession.RealUserId = 0;
            oSession.RealUsername = "";
            oSession.RealRole = "";
            oSession.CreatedAt = vNow;
            oSession.ExpiresAt = vNow.AddMinutes(FTTLMinutes);
            oSession.IP = aIP;

            lock (FLock)
            {
                FSessions[oSession.Token] = oSession;
            }

            return oSession.Token;
        }

        // Look up a non-expired session by token. Sliding expiry refreshes the
        // window when enabled. Returns false for missing/expired tokens.
        public bool TryGet(string aToken, out TSaaSSession aSession)
        {
            aSession = null;
            if (string.IsNullOrEmpty(aToken))
                return false;
            lock (FLock)
            {
                TSaaSSession oSession;
                if (!FSessions.TryGetValue(aToken, out oSession))
                    return false;
                if (DateTime.Now > oSession.ExpiresAt)
                {
                    FSessions.Remove(aToken);
                    return false;
                }
                if (FSlidingExpiry)
                {
                    oSession.ExpiresAt = DateTime.Now.AddMinutes(FTTLMinutes);
                    FSessions[aToken] = oSession;
                }
                aSession = oSession.Clone();
                return true;
            }
        }

        // Switch an existing session to the impersonated identity, keeping the
        // vendor operator in the Real* fields. Returns false when the token is
        // unknown or an impersonation is already active.
        public bool BeginImpersonation(string aToken, TSaaSUser aTarget,
            string aTenantName, string aTenantSlug, out TSaaSSession aSession)
        {
            aSession = null;
            if (string.IsNullOrEmpty(aToken))
                return false;
            lock (FLock)
            {
                TSaaSSession oSession;
                if (!FSessions.TryGetValue(aToken, out oSession))
                    return false;
                // An already-impersonating session must stop first: chaining would
                // lose the original operator identity.
                if (oSession.RealUserId != 0)
                    return false;
                oSession.RealUserId = oSession.UserId;
                oSession.RealUsername = oSession.Username;
                oSession.RealRole = oSession.Role;
                oSession.UserId = aTarget.Id;
                oSession.Username = aTarget.Username;
                oSession.Role = aTarget.Role;
                oSession.DisplayName = aTarget.DisplayName;
                oSession.Email = aTarget.Email;
                oSession.TenantId = aTarget.TenantId;
                oSession.TenantName = aTenantName;
                oSession.TenantSlug = aTenantSlug;
                FSessions[aToken] = oSession;
                aSession = oSession.Clone();
                return true;
            }
        }

        // Restore the vendor operator identity on the same token. Returns false
        // when the token is unknown or no impersonation is active.
        public bool EndImpersonation(string aToken, TSaaSUser aReal,
            out TSaaSSession aSession)
        {
            aSession = null;
            if (string.IsNullOrEmpty(aToken))
                return false;
            lock (FLock)
            {
                TSaaSSession oSession;
                if (!FSessions.TryGetValue(aToken, out oSession))
                    return false;
                if (oSession.RealUserId == 0)
                    return false;
                oSession.UserId = aReal.Id;
                oSession.Username = aReal.Username;
                oSession.Role = aReal.Role;
                oSession.DisplayName = aReal.DisplayName;
                oSession.Email = aReal.Email;
                oSession.TenantId = aReal.TenantId;
                oSession.TenantName = "";
                oSession.TenantSlug = "";
                oSession.RealUserId = 0;
                oSession.RealUsername = "";
                oSession.RealRole = "";
                FSessions[aToken] = oSession;
                aSession = oSession.Clone();
                return true;
            }
        }

        // Drop a session by token. No-op when absent. Named Destroy_ so it does
        // not collide with anything.
        public void Destroy_(string aToken)
        {
            if (string.IsNullOrEmpty(aToken))
                return;
            lock (FLock)
            {
                FSessions.Remove(aToken);
            }
        }

        // Remove all expired sessions.
        public void PurgeExpired()
        {
            DateTime vNow = DateTime.Now;
            lock (FLock)
            {
                List<string> vExpired = new List<string>();
                foreach (KeyValuePair<string, TSaaSSession> oPair in FSessions)
                    if (vNow > oPair.Value.ExpiresAt)
                        vExpired.Add(oPair.Key);
                for (int vI = 0; vI < vExpired.Count; vI++)
                    FSessions.Remove(vExpired[vI]);
            }
        }

        public int ActiveCount()
        {
            lock (FLock)
            {
                return FSessions.Count;
            }
        }
    }

    // The unit-level helper routines of sgcSaaS_Sessions.pas.
    public static class SaaSSessions
    {
        // True when the session currently carries an active impersonation.
        public static bool SaaSIsImpersonating(TSaaSSession aSession)
        {
            return (aSession != null) && (aSession.RealUserId != 0);
        }

        // True for the two vendor-side roles (accounts with no tenant).
        public static bool SaaSIsVendorRole(string aRole)
        {
            return string.Equals(aRole, SaaSTypes.CS_ROLE_SUPERADMIN,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(aRole, SaaSTypes.CS_ROLE_SUPPORT,
                       StringComparison.OrdinalIgnoreCase);
        }

        // True for the four tenant-side roles.
        public static bool SaaSIsTenantRole(string aRole)
        {
            return string.Equals(aRole, SaaSTypes.CS_ROLE_OWNER,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(aRole, SaaSTypes.CS_ROLE_ADMIN,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(aRole, SaaSTypes.CS_ROLE_MEMBER,
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(aRole, SaaSTypes.CS_ROLE_READONLY,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static byte[] SaaSRandomBytes(int aLen)
        {
            if (aLen <= 0)
                return new byte[0];
            byte[] vResult = new byte[aLen];
            RandomNumberGenerator.Fill(vResult);
            return vResult;
        }

        private static string SaaSBytesToHex(byte[] aBytes)
        {
            const string CS_HEX = "0123456789abcdef";
            int vLen = aBytes.Length;
            StringBuilder vResult = new StringBuilder(vLen * 2);
            for (int vI = 0; vI < vLen; vI++)
            {
                vResult.Append(CS_HEX[(aBytes[vI] >> 4) & 0x0F]);
                vResult.Append(CS_HEX[aBytes[vI] & 0x0F]);
            }
            return vResult.ToString();
        }

        // Cryptographically random hex string of aBytes bytes (2 chars per byte).
        // Used for session tokens, invitation tokens and verification tokens.
        public static string SaaSRandomToken(int aBytes = 32)
        {
            return SaaSBytesToHex(SaaSRandomBytes(aBytes));
        }
    }
}
