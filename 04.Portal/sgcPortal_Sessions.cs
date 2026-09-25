// ***************************************************************************
//  sgcPortal - mini ERP web-app demo (walking skeleton)
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
// ***************************************************************************

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Portal
{
    // In-memory session record. Stored by reference in the dictionary; the store
    // itself owns synchronization.
    public class TERPSession
    {
        public string Token = "";
        public long UserId;
        public string Username = "";
        public string Role = "";
        public string DisplayName = "";
        // For role 'customer' this is the linked customers.id; 0 otherwise. Every
        // customer-facing route scopes its queries to this id server-side.
        public long CustomerId;
        public DateTime CreatedAt;
        public DateTime ExpiresAt;
        public string IP = "";
    }

    // Thread-safe in-memory session store. A single lock object guards the
    // dictionary; every public method enters/leaves it.
    public class TERPSessionStore
    {
        private readonly object FLock = new object();
        private readonly Dictionary<string, TERPSession> FSessions;
        private readonly int FTTLMinutes;
        private readonly bool FSlidingExpiry;

        // aTTLMinutes <= 0 defaults to 8 hours (480 minutes).
        public TERPSessionStore(int aTTLMinutes = 480, bool aSlidingExpiry = true)
        {
            FTTLMinutes = aTTLMinutes;
            if (FTTLMinutes <= 0)
                FTTLMinutes = 480;
            FSlidingExpiry = aSlidingExpiry;
            FSessions = new Dictionary<string, TERPSession>();
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
        public string CreateSession(TERPUser aUser, string aIP)
        {
            DateTime vNow = DateTime.Now;
            TERPSession oSession = new TERPSession();
            oSession.Token = GenerateToken();
            oSession.UserId = aUser.Id;
            oSession.Username = aUser.Username;
            oSession.Role = aUser.Role;
            oSession.DisplayName = aUser.DisplayName;
            oSession.CustomerId = aUser.CustomerId;
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
        public bool TryGet(string aToken, out TERPSession aSession)
        {
            aSession = null;
            if (string.IsNullOrEmpty(aToken))
                return false;
            lock (FLock)
            {
                TERPSession oSession;
                if (!FSessions.TryGetValue(aToken, out oSession))
                    return false;
                if (DateTime.Now > oSession.ExpiresAt)
                {
                    FSessions.Remove(aToken);
                    return false;
                }
                // Sliding expiry: refresh the window and persist it back into the store.
                if (FSlidingExpiry)
                {
                    oSession.ExpiresAt = DateTime.Now.AddMinutes(FTTLMinutes);
                    FSessions[aToken] = oSession;
                }
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
                foreach (KeyValuePair<string, TERPSession> oPair in FSessions)
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
