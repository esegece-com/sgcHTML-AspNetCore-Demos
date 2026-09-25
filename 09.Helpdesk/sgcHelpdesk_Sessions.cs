// ***************************************************************************
//  sgcHelpdesk - support-ticket helpdesk web-app demo (managed port)
//  Port of delphi\Demos\60.HTML\09.Helpdesk\sgcHelpdesk_Sessions.pas
//
//  The Delphi store used BCryptGenRandom for the token; here the managed
//  RandomNumberGenerator provides the 32 secure random bytes.
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Helpdesk
{
    // In-memory session record. Stored by reference in the dictionary; the store
    // itself owns synchronization.
    public class THelpdeskSession
    {
        public string Token = "";
        public long UserId;
        public string Username = "";
        public string Role = "";
        public DateTime CreatedAt;
        public DateTime ExpiresAt;
        public string IP = "";
    }

    // Thread-safe in-memory session store. A single lock object guards the
    // dictionary; every public method enters/leaves it.
    public class THelpdeskSessionStore
    {
        private readonly object FLock = new object();
        private readonly Dictionary<string, THelpdeskSession> FSessions;
        private readonly int FTTLMinutes;
        private readonly bool FSlidingExpiry;

        // aTTLMinutes <= 0 defaults to 8 hours (480 minutes).
        public THelpdeskSessionStore(int aTTLMinutes = 480, bool aSlidingExpiry = true)
        {
            FTTLMinutes = aTTLMinutes;
            if (FTTLMinutes <= 0)
                FTTLMinutes = 480;
            FSlidingExpiry = aSlidingExpiry;
            FSessions = new Dictionary<string, THelpdeskSession>();
        }

        private byte[] RandomBytes(int aLen)
        {
            if (aLen <= 0)
                return new byte[0];
            byte[] vResult = new byte[aLen];
            RandomNumberGenerator.Fill(vResult);
            return vResult;
        }

        private string BytesToHex(byte[] aBytes)
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

        private string GenerateToken()
        {
            return BytesToHex(RandomBytes(32));
        }

        // Create a fresh session for aUser, returning the random token.
        public string CreateSession(THelpdeskUser aUser, string aIP)
        {
            DateTime vNow = DateTime.Now;
            THelpdeskSession oSession = new THelpdeskSession();
            oSession.Token = GenerateToken();
            oSession.UserId = aUser.Id;
            oSession.Username = aUser.Username;
            oSession.Role = aUser.Role;
            oSession.CreatedAt = vNow;
            oSession.ExpiresAt = vNow.AddMinutes(FTTLMinutes);
            oSession.IP = aIP;

            lock (FLock)
            {
                FSessions[oSession.Token] = oSession;
            }

            return oSession.Token;
        }

        // Look up a non-expired session by token. Sliding-expiry refreshes the
        // expiry when enabled. Returns false for missing/expired tokens.
        public bool TryGet(string aToken, out THelpdeskSession aSession)
        {
            aSession = null;
            if (string.IsNullOrEmpty(aToken))
                return false;
            lock (FLock)
            {
                THelpdeskSession oSession;
                if (!FSessions.TryGetValue(aToken, out oSession))
                    return false;
                if (DateTime.Now > oSession.ExpiresAt)
                {
                    FSessions.Remove(aToken);
                    return false;
                }
                // Sliding expiry: refresh the window and persist it back into the store.
                if (FSlidingExpiry)
                    oSession.ExpiresAt = DateTime.Now.AddMinutes(FTTLMinutes);
                aSession = oSession;
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
                foreach (KeyValuePair<string, THelpdeskSession> oPair in FSessions)
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
}
