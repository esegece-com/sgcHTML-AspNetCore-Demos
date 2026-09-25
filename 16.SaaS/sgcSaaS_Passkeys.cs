// ***************************************************************************
//  sgcSaaS - multi-tenant SaaS control plane demo
//
//  written by eSeGeCe
//  copyright (c) 2026
//  Email : info@esegece.com
//  Web : https://www.esegece.com
//
//  Port of delphi\Demos\60.HTML\50.SaaS\Source\sgcSaaS_Passkeys.pas
//
//  This is a faithful ADAPTATION, not a 1:1 port. The Delphi unit wraps the
//  lower-level TsgcWebAuthn_Server, which fires structured-record events. The
//  managed equivalent (TsgcWSServer_API_WebAuthn) fires JSON-string + ref-param
//  events instead, so the bridge to the DB is wired through those signatures.
//
//  Like the Delphi unit, every passkeys-table statement lives HERE (the SaaS
//  DB pool exposes no passkey helpers); the SQL text matches the Delphi
//  statements verbatim, with FireDAC ':name' binds rewritten as SQLite '@name'.
// ***************************************************************************

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
// sgcWebSockets WebAuthn
using esegece.sgcWebSockets;

namespace SaaS
{
    /// <summary>Passkey / WebAuthn error for the SaaS Demo. Mirrors Delphi ESaaSPasskeysError.</summary>
    public class ESaaSPasskeysError : Exception
    {
        public ESaaSPasskeysError(string message) : base(message) { }
    }

    // WebAuthn / passkey core for the SaaS Demo. Adapts the sgcWebsite template:
    // it owns a single TsgcWSServer_API_WebAuthn (which performs ALL the WebAuthn
    // crypto - challenge generation, CBOR/COSE parsing, base64url, SHA-256 and
    // ECDSA/RSA signature verification) and bridges its events to the local
    // SQLite 'passkeys' table via TSaaSDBPool.
    //
    // The public Begin*/Finish* methods are serialized by FLock so the per-call
    // scratch slots (FCurrentUserID / FCurrentDeviceName / FResolvedUserID) are
    // not clobbered by a concurrent request.
    public class TSaaSPasskeys : IDisposable
    {
        private readonly TSaaSDBPool FDB;
        private readonly TsgcWSServer_API_WebAuthn FAuthn;
        private readonly object FLock = new object();
        private readonly string FRPID;
        private readonly string FRPName;
        private readonly string FOrigin;
        private bool FDisposed;

        // Per-call scratch slots; only valid while FLock is held.
        private long FCurrentUserID;
        private string FCurrentDeviceName;
        private long FResolvedUserID;

        /// <summary>The relying-party id (e.g. 'localhost'). Mirrors Delphi RPID.</summary>
        public string RPID
        {
            get { return FRPID; }
        }

        /// <summary>The accepted origin (e.g. 'http://localhost:5709'). Mirrors Delphi Origin.</summary>
        public string Origin
        {
            get { return FOrigin; }
        }

        /// <summary>
        /// aRPID defaults to 'localhost', aRPName to 'sgcSaaS', aOrigin to
        /// 'http://localhost:5701'. Throws ESaaSPasskeysError when aDB is null.
        /// </summary>
        public TSaaSPasskeys(TSaaSDBPool aDB, string aRPID = "localhost",
            string aRPName = "sgcSaaS", string aOrigin = "http://localhost:5701")
        {
            if (aDB == null)
            {
                throw new ESaaSPasskeysError("TSaaSPasskeys: DB pool is nil");
            }

            FDB = aDB;

            FRPID = string.IsNullOrEmpty(aRPID) ? "localhost" : aRPID;
            FRPName = string.IsNullOrEmpty(aRPName) ? "sgcSaaS" : aRPName;
            FOrigin = string.IsNullOrEmpty(aOrigin) ? "http://localhost:5701" : aOrigin;

            FCurrentUserID = 0;
            FCurrentDeviceName = "";
            FResolvedUserID = 0;

            FAuthn = new TsgcWSServer_API_WebAuthn();
            ConfigureAuthn();
        }

        private void ConfigureAuthn()
        {
            // Configure the underlying WebAuthn server with our RP id / origin.
            FAuthn.WebAuthnOptions.RelyingParty = FRPID;
            FAuthn.WebAuthnOptions.Origins.Origins.Clear();
            FAuthn.WebAuthnOptions.Origins.Origins.Add(FOrigin);
            FAuthn.WebAuthnOptions.Origins.AllowCrossOrigins = false;

            // Discoverable: server never sends allowCredentials; the browser picker
            // resolves the account.
            FAuthn.WebAuthnOptions.Credentials.AllowCredentials = false;
            FAuthn.WebAuthnOptions.Credentials.ExcludeCredentials = false;

            // None / self attestation: this demo does not trust-chain authenticators.
            FAuthn.WebAuthnOptions.AttestationPolicy.NoneAttestation = true;
            FAuthn.WebAuthnOptions.AttestationPolicy.SelfAttestation = true;
            FAuthn.WebAuthnOptions.AttestationPolicy.PackedAttestation = true;
            FAuthn.WebAuthnOptions.AttestationPolicy.TPMAttestation = true;
            FAuthn.WebAuthnOptions.AttestationPolicy.AndroidKeyAttestation = true;
            FAuthn.WebAuthnOptions.AttestationPolicy.AppleAttestation = true;
            FAuthn.WebAuthnOptions.AttestationPolicy.FidoU2FAttestation = true;

            // Prefer discoverable resident-key passkeys, user verification preferred,
            // attestation 'none'. NOTE: like Delphi, DiscoverableCredential is read
            // but never emitted on the options; the browser is asked for a passkey
            // via authenticatorSelection.residentKey in the begin-registration
            // payload the client posts.
            FAuthn.WebAuthnOptions.DefaultOptions.Registration.DiscoverableCredential =
                TsgcWebAuthnDiscoverableCredential.waundcRequired;
            FAuthn.WebAuthnOptions.DefaultOptions.Registration.UserVerification =
                TsgcWebAuthnUserVerification.waunuvPreferred;
            FAuthn.WebAuthnOptions.DefaultOptions.Registration.Attestation = TsgcWebAuthnAttestation.waunaNone;

            FAuthn.WebAuthnOptions.DefaultOptions.Authentication.UserVerification =
                TsgcWebAuthnUserVerification.waunuvPreferred;

            FAuthn.WebAuthnOptions.Timeout = 300000;

            // Wire events. The managed events pass JSON strings + ref params (not the
            // Delphi structured records), so the handlers parse/rewrite JSON.
            FAuthn.OnWebAuthnRegistrationOptionsResponse += HandleRegistrationOptionsResponse;
            FAuthn.OnWebAuthnRegistrationSuccessful += HandleRegistrationSuccessful;
            FAuthn.OnWebAuthnAuthenticationSuccessful += HandleAuthenticationSuccessful;
        }

        private string EncodeUserHandle(long aUserID)
        {
            // The WebAuthn library treats User.Id as a base64url-encoded byte string:
            // it base64url-decodes the value we set when sending registration options
            // to the browser, and the browser later returns the SAME base64url string
            // as response.userHandle on authentication. So the value MUST itself be
            // valid base64url. Encode the ASCII-decimal user_id to base64url (no
            // padding, url-safe alphabet).
            byte[] vBytes = Encoding.ASCII.GetBytes(aUserID.ToString(CultureInfo.InvariantCulture));
            string vResult = Convert.ToBase64String(vBytes);
            vResult = vResult.Replace('+', '-');
            vResult = vResult.Replace('/', '_');
            vResult = vResult.Replace("=", "");
            return vResult;
        }

        private bool DecodeUserHandle(string aValue, out long aUserID)
        {
            aUserID = 0;
            if (string.IsNullOrEmpty(aValue))
            {
                return false;
            }

            // Reverse the base64url tweaks before standard-base64 decode.
            string vPadded = aValue.Trim();
            vPadded = vPadded.Replace('-', '+');
            vPadded = vPadded.Replace('_', '/');
            while ((vPadded.Length % 4) != 0)
            {
                vPadded += "=";
            }

            try
            {
                byte[] vBytes = Convert.FromBase64String(vPadded);
                string vText = Encoding.ASCII.GetString(vBytes).Trim();
                return long.TryParse(vText, NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out aUserID) && (aUserID > 0);
            }
            catch (Exception)
            {
                aUserID = 0;
                return false;
            }
        }

        private void HandleRegistrationOptionsResponse(object Sender, string aRequest,
            ref string aResponse)
        {
            // Override the auto-generated user.id with our DB user_id so verify can
            // compare response.userHandle == credentialRecord.UserId, and force the
            // rp id/name. The managed engine emits the options JSON in aResponse; we
            // rewrite the user.id / rp.id / rp.name fields in place.
            if (FCurrentUserID <= 0)
            {
                return;
            }

            string vHandle = EncodeUserHandle(FCurrentUserID);
            aResponse = RewriteOptions(aResponse, vHandle, FRPID, FRPName);
        }

        // Rewrites the engine-emitted registration options JSON, substituting
        // user.id with our DB user handle and forcing rp.id / rp.name, while
        // preserving every other field (challenge, pubKeyCredParams, timeout, ...).
        // Falls back to the original string if the JSON cannot be parsed.
        private static string RewriteOptions(string aJson, string aUserId, string aRpId,
            string aRpName)
        {
            if (string.IsNullOrEmpty(aJson))
            {
                return aJson;
            }

            try
            {
                using (JsonDocument vDoc = JsonDocument.Parse(aJson))
                {
                    JsonElement vRoot = vDoc.RootElement;
                    if (vRoot.ValueKind != JsonValueKind.Object)
                    {
                        return aJson;
                    }

                    System.IO.MemoryStream vStream = new System.IO.MemoryStream();
                    using (Utf8JsonWriter vWriter = new Utf8JsonWriter(vStream))
                    {
                        vWriter.WriteStartObject();
                        foreach (JsonProperty vProp in vRoot.EnumerateObject())
                        {
                            if (vProp.NameEquals("rp")
                                && vProp.Value.ValueKind == JsonValueKind.Object)
                            {
                                vWriter.WritePropertyName(vProp.Name);
                                WriteObjectWithOverrides(vWriter, vProp.Value, "id", aRpId, "name",
                                    aRpName);
                            }
                            else if (vProp.NameEquals("user")
                                && vProp.Value.ValueKind == JsonValueKind.Object)
                            {
                                vWriter.WritePropertyName(vProp.Name);
                                WriteObjectWithOverrides(vWriter, vProp.Value, "id", aUserId, null,
                                    null);
                            }
                            else
                            {
                                vProp.WriteTo(vWriter);
                            }
                        }

                        vWriter.WriteEndObject();
                    }

                    return Encoding.UTF8.GetString(vStream.ToArray());
                }
            }
            catch (Exception)
            {
                return aJson;
            }
        }

        // Copies aObject, replacing the string value of aKey1 (and aKey2 when given)
        // with the supplied override(s). Existing keys are overwritten in place;
        // missing keys are appended.
        private static void WriteObjectWithOverrides(Utf8JsonWriter aWriter, JsonElement aObject,
            string aKey1, string aValue1, string aKey2, string aValue2)
        {
            bool vWrote1 = false;
            bool vWrote2 = false;
            aWriter.WriteStartObject();
            foreach (JsonProperty vProp in aObject.EnumerateObject())
            {
                if ((aKey1 != null) && vProp.NameEquals(aKey1))
                {
                    aWriter.WriteString(aKey1, aValue1);
                    vWrote1 = true;
                }
                else if ((aKey2 != null) && vProp.NameEquals(aKey2))
                {
                    aWriter.WriteString(aKey2, aValue2);
                    vWrote2 = true;
                }
                else
                {
                    vProp.WriteTo(aWriter);
                }
            }

            if ((aKey1 != null) && !vWrote1)
            {
                aWriter.WriteString(aKey1, aValue1);
            }

            if ((aKey2 != null) && !vWrote2)
            {
                aWriter.WriteString(aKey2, aValue2);
            }

            aWriter.WriteEndObject();
        }

        private void HandleRegistrationSuccessful(object Sender,
            TsgcWebAuthn_CredentialRecord aCredentialRecord, ref bool Accept)
        {
            // Persist the new credential to the passkeys table. The managed engine
            // calls AddCredential() into its own in-memory store when Accept stays
            // true; we keep it true so a follow-on login in the same process resolves
            // without reloading, AND we mirror the DB write the Delphi unit performed.
            Accept = false;
            if (FCurrentUserID <= 0)
            {
                return;
            }

            if (aCredentialRecord == null)
            {
                return;
            }

            InsertPasskey(FCurrentUserID, aCredentialRecord.CredentialId,
                aCredentialRecord.PublicKey, aCredentialRecord.SignCount, FCurrentDeviceName);

            // Tag the in-memory record with our DB user handle so the engine's own
            // store can match response.userHandle during a same-process assertion.
            aCredentialRecord.UserId = EncodeUserHandle(FCurrentUserID);
            Accept = true;
        }

        // --- passkeys table access (the Delphi unit owns this SQL too) ------ //

        // ISO timestamp format the demo stores in every TEXT date column.
        private const string CS_PK_DT_FMT = "yyyy-MM-dd'T'HH:mm:ss";

        // INSERT INTO passkeys ... - mirrors the Delphi statement in
        // TSaaSPasskeys.HandleRegistrationSuccessful.
        private void InsertPasskey(long aUserId, string aCredentialId,
            string aPublicKey, long aSignCount, string aDeviceName)
        {
            string vNow = DateTime.Now.ToString(CS_PK_DT_FMT, CultureInfo.InvariantCulture);
            using (SqliteConnection oConn = FDB.Acquire())
            using (SqliteCommand oCmd = oConn.CreateCommand())
            {
                oCmd.CommandText = "INSERT INTO passkeys " +
                    "(user_id, credential_id, public_key, sign_count, device_name, " +
                    "created_at, last_used_at) " +
                    "VALUES (@uid, @cid, @pk, @sc, @dn, @ca, @lu)";
                oCmd.Parameters.AddWithValue("@uid", aUserId);
                oCmd.Parameters.AddWithValue("@cid", aCredentialId ?? "");
                oCmd.Parameters.AddWithValue("@pk", aPublicKey ?? "");
                oCmd.Parameters.AddWithValue("@sc", aSignCount);
                oCmd.Parameters.AddWithValue("@dn", aDeviceName ?? "");
                oCmd.Parameters.AddWithValue("@ca", vNow);
                oCmd.Parameters.AddWithValue("@lu", "");
                oCmd.ExecuteNonQuery();
            }
        }

        // SELECT ... FROM passkeys WHERE credential_id = ... - mirrors the Delphi
        // lookup in TSaaSPasskeys.HandleAuthenticationSuccessful.
        private bool GetPasskeyByCredentialId(string aCredentialId, out TSaaSPasskey aPk)
        {
            aPk = null;
            if (string.IsNullOrEmpty(aCredentialId))
            {
                return false;
            }

            using (SqliteConnection oConn = FDB.Acquire())
            using (SqliteCommand oCmd = oConn.CreateCommand())
            {
                oCmd.CommandText = "SELECT id, user_id, credential_id, public_key, " +
                    "sign_count, device_name, created_at, last_used_at FROM passkeys " +
                    "WHERE credential_id = @cid LIMIT 1";
                oCmd.Parameters.AddWithValue("@cid", aCredentialId);
                using (SqliteDataReader oReader = oCmd.ExecuteReader())
                {
                    if (!oReader.Read())
                    {
                        return false;
                    }

                    TSaaSPasskey oPk = new TSaaSPasskey();
                    oPk.Id = oReader.IsDBNull(0) ? 0 : oReader.GetInt64(0);
                    oPk.UserId = oReader.IsDBNull(1) ? 0 : oReader.GetInt64(1);
                    oPk.CredentialId = oReader.IsDBNull(2) ? "" : oReader.GetString(2);
                    oPk.PublicKey = oReader.IsDBNull(3) ? "" : oReader.GetString(3);
                    oPk.SignCount = oReader.IsDBNull(4) ? 0 : oReader.GetInt64(4);
                    oPk.DeviceName = oReader.IsDBNull(5) ? "" : oReader.GetString(5);
                    oPk.CreatedAt = SaaSTypes.ParseSaaSTimestamp(
                        oReader.IsDBNull(6) ? "" : oReader.GetString(6));
                    oPk.LastUsedAt = SaaSTypes.ParseSaaSTimestamp(
                        oReader.IsDBNull(7) ? "" : oReader.GetString(7));
                    aPk = oPk;
                    return true;
                }
            }
        }

        // UPDATE passkeys SET sign_count, last_used_at - mirrors the Delphi update.
        private void UpdatePasskeySignCount(string aCredentialId, long aSignCount)
        {
            using (SqliteConnection oConn = FDB.Acquire())
            using (SqliteCommand oCmd = oConn.CreateCommand())
            {
                oCmd.CommandText = "UPDATE passkeys SET sign_count = @sc, " +
                    "last_used_at = @lu WHERE credential_id = @cid";
                oCmd.Parameters.AddWithValue("@sc", aSignCount);
                oCmd.Parameters.AddWithValue("@lu",
                    DateTime.Now.ToString(CS_PK_DT_FMT, CultureInfo.InvariantCulture));
                oCmd.Parameters.AddWithValue("@cid", aCredentialId ?? "");
                oCmd.ExecuteNonQuery();
            }
        }

        // COSE algorithm identifier -> TsgcWebAuthnAlgorithm. Repeats the library's
        // internal sgcWebAuthn.GetAlgorithmFromInteger (not reachable from demo
        // code) and, like it, THROWS on an unrecognised identifier instead of
        // defaulting to ES256.
        private static TsgcWebAuthnAlgorithm AlgorithmFromCOSE(int aValue)
        {
            switch (aValue)
            {
                case -7: return TsgcWebAuthnAlgorithm.waunalgES256;
                case -35: return TsgcWebAuthnAlgorithm.waunalgES384;
                case -36: return TsgcWebAuthnAlgorithm.waunalgES512;
                case -257: return TsgcWebAuthnAlgorithm.waunalgRS256;
                case -258: return TsgcWebAuthnAlgorithm.waunalgRS384;
                case -259: return TsgcWebAuthnAlgorithm.waunalgRS512;
                case -65535: return TsgcWebAuthnAlgorithm.waunalgRS1;
                case -37: return TsgcWebAuthnAlgorithm.waunalgPS256;
                case -38: return TsgcWebAuthnAlgorithm.waunalgPS384;
                case -39: return TsgcWebAuthnAlgorithm.waunalgPS512;
                case -8: return TsgcWebAuthnAlgorithm.waunalgEdDSA;
                default:
                    throw new ESaaSPasskeysError("Unknown Algorithm");
            }
        }

        // Builds the engine credential record for a stored DB passkey so the engine
        // can resolve + signature-verify a discoverable assertion against it.
        private TsgcWebAuthn_CredentialRecord BuildCredentialRecord(TSaaSPasskey aPk)
        {
            TsgcWebAuthn_CredentialRecord vRec = new TsgcWebAuthn_CredentialRecord();
            vRec.CredentialId = aPk.CredentialId;
            vRec.CredentialType = "public-key";
            vRec.PublicKey = aPk.PublicKey;
            vRec.SignCount = (int)aPk.SignCount;
            // The COSE public key (stored JSON) carries alg in key "3"; the engine
            // reads PublicKeyAlgorithm to pick the verify path. The library maps the
            // COSE integer internally (sgcWebAuthn.GetAlgorithmFromInteger) but that
            // helper is 'internal', so the mapping is repeated here. Like the library
            // it REJECTS an unrecognised value instead of substituting ES256: a
            // relying party must never reinterpret an algorithm it does not support.
            vRec.PublicKeyAlgorithm = AlgorithmFromCOSE(ReadJsonInt(aPk.PublicKey, "3", 0));
            // The library compares response.userHandle to this UserId.
            vRec.UserId = EncodeUserHandle(aPk.UserId);
            vRec.UserName = "";
            return vRec;
        }

        private void HandleAuthenticationSuccessful(object Sender, string aRequest, ref bool Accept)
        {
            // The managed engine has already verified the signature, run the
            // monotonic sign-count check and updated the in-memory record's SignCount
            // before firing this event. aRequest is the raw finish-authentication
            // payload JSON. We resolve the owning DB user by credential_id, sanity-
            // check the returned userHandle, persist the new sign_count and stash the
            // resolved user id for FinishLogin.
            Accept = false;

            string vCid = ReadJsonString(aRequest, "id");
            if (string.IsNullOrEmpty(vCid))
            {
                return;
            }

            TSaaSPasskey vPk;
            if (!GetPasskeyByCredentialId(vCid, out vPk) || (vPk == null))
            {
                return;
            }

            long vUid = vPk.UserId;
            if (vUid <= 0)
            {
                return;
            }

            // Sanity: the UserHandle the authenticator returned must match. The
            // managed payload carries it as "userHandle" at the top level.
            string vUserHandle = ReadJsonString(aRequest, "userHandle");
            if (!string.IsNullOrEmpty(vUserHandle))
            {
                long vHandleUid;
                if (!DecodeUserHandle(vUserHandle, out vHandleUid) || (vHandleUid != vUid))
                {
                    // Mismatch: refuse.
                    return;
                }
            }

            // Recover the new sign_count the engine parsed from authenticatorData so
            // we can persist it. (Clone detection itself was performed by the engine.)
            long vNewSign = vPk.SignCount;
            try
            {
                string vAuthDataB64 = ReadJsonString(aRequest, "authenticatorData");
                if (!string.IsNullOrEmpty(vAuthDataB64))
                {
                    byte[] vAuthData = DecodeBase64Url(vAuthDataB64);
                    if (vAuthData.Length >= 37)
                    {
                        vNewSign = ((long)vAuthData[33] << 24) | ((long)vAuthData[34] << 16)
                            | ((long)vAuthData[35] << 8) | vAuthData[36];
                    }
                }
            }
            catch (Exception)
            {
                vNewSign = vPk.SignCount;
            }

            try
            {
                UpdatePasskeySignCount(vCid, vNewSign);
            }
            catch (Exception)
            {
                // Best-effort sign-count persistence; do not fail the login on a write
                // error (mirrors the Delphi swallow).
            }

            FResolvedUserID = vUid;
            Accept = true;
        }

        /// <summary>
        /// Registration (logged-in user). Returns publicKey.create options JSON:
        /// rp, user, challenge, pubKeyCredParams. The DB user id is encoded into
        /// options.user.id so FinishRegister can match it.
        /// </summary>
        public string BeginRegister(long aUserID, string aUsername, string aDisplayName)
        {
            if (aUserID <= 0)
            {
                throw new ESaaSPasskeysError("Invalid user.");
            }

            string vUser = (aUsername ?? "").Trim();
            if (vUser == "")
            {
                throw new ESaaSPasskeysError("Username is required.");
            }

            string vDisplay = (aDisplayName ?? "").Trim();
            if (vDisplay == "")
            {
                vDisplay = vUser;
            }

            lock (FLock)
            {
                FCurrentUserID = aUserID;
                FCurrentDeviceName = "";
                try
                {
                    string vPayload = "{\"username\":\"" + JsonEscape(vUser)
                        + "\",\"displayName\":\"" + JsonEscape(vDisplay) + "\"}";
                    return FAuthn.GetRegistrationOptionsResponse(vPayload);
                }
                finally
                {
                    FCurrentUserID = 0;
                    FCurrentDeviceName = "";
                }
            }
        }

        /// <summary>
        /// Registration verify (logged-in user): verifies the attestation and
        /// persists the new credential to the passkeys table. aDeviceName is the
        /// user-supplied label. Raises ESaaSPasskeysError on failure.
        /// </summary>
        public string FinishRegister(long aUserID, string aAttestationJSON, string aDeviceName)
        {
            if (aUserID <= 0)
            {
                throw new ESaaSPasskeysError("Invalid user.");
            }

            string vName = (aDeviceName ?? "").Trim();
            if (vName == "")
            {
                vName = "Passkey";
            }

            if (vName.Length > 200)
            {
                vName = vName.Substring(0, 200);
            }

            lock (FLock)
            {
                FCurrentUserID = aUserID;
                FCurrentDeviceName = vName;
                try
                {
                    FAuthn.ValidateRegistrationOptions(aAttestationJSON);
                    return "{\"verified\":\"ok\"}";
                }
                catch (Exception E)
                {
                    throw new ESaaSPasskeysError(E.Message);
                }
                finally
                {
                    FCurrentUserID = 0;
                    FCurrentDeviceName = "";
                }
            }
        }

        /// <summary>
        /// Authentication (usernameless / discoverable). Returns assertion options
        /// JSON: challenge, rpId, allowCredentials (empty for discoverable so the
        /// browser picker resolves the account). Seeds the engine with the stored
        /// credentials so the later verify can find the matching public key.
        /// aUsername is accepted for API symmetry but the demo is discoverable.
        /// </summary>
        public string BeginLogin(string aUsername = "")
        {
            lock (FLock)
            {
                // Empty payload: discoverable login, no username known. Because
                // WebAuthnOptions.Credentials.AllowCredentials is false the engine
                // emits an empty allowCredentials array (the browser picker resolves
                // the account). The matching stored credential is seeded into the
                // engine at FinishLogin time, looked up by the assertion's credential
                // id, so the signature can be verified against the stored public key.
                // (aUsername is accepted for API symmetry; the demo is usernameless.)
                return FAuthn.GetAuthenticationOptionsResponse("{}");
            }
        }

        /// <summary>
        /// Authentication verify: validates the assertion signature against the
        /// stored public key, updates sign_count, and returns the owning DB user id
        /// via aUserID. Raises ESaaSPasskeysError on failure.
        /// </summary>
        public string FinishLogin(string aAssertionJSON, out long aUserID)
        {
            aUserID = 0;
            lock (FLock)
            {
                FResolvedUserID = 0;

                // Discoverable login: the engine resolves + verifies the assertion
                // against a credential it knows. Seed the matching stored passkey
                // (looked up by the assertion's credential id) into the engine first
                // so a credential registered in a previous run is verifiable.
                try
                {
                    string vCid = ReadJsonString(aAssertionJSON, "id");
                    if (!string.IsNullOrEmpty(vCid))
                    {
                        TSaaSPasskey vPk;
                        if (GetPasskeyByCredentialId(vCid, out vPk) && (vPk != null))
                        {
                            FAuthn.AddCredential(BuildCredentialRecord(vPk));
                        }
                    }
                }
                catch (Exception)
                {
                    // Seeding is best-effort; let the engine produce the canonical
                    // "Unknown credential" / "Invalid Signature" error below.
                }

                try
                {
                    FAuthn.ValidateAuthenticationOptions(aAssertionJSON);
                }
                catch (Exception E)
                {
                    FResolvedUserID = 0;
                    throw new ESaaSPasskeysError(E.Message);
                }

                aUserID = FResolvedUserID;
                if (aUserID <= 0)
                {
                    throw new ESaaSPasskeysError(
                        "Authentication succeeded but user could not be resolved.");
                }

                return "{\"verified\":\"ok\"}";
            }
        }

        private static byte[] DecodeBase64Url(string aValue)
        {
            string vPadded = (aValue ?? "").Trim();
            vPadded = vPadded.Replace('-', '+');
            vPadded = vPadded.Replace('_', '/');
            while ((vPadded.Length % 4) != 0)
            {
                vPadded += "=";
            }

            return Convert.FromBase64String(vPadded);
        }

        private static string JsonEscape(string aValue)
        {
            if (string.IsNullOrEmpty(aValue))
            {
                return "";
            }

            StringBuilder sb = new StringBuilder(aValue.Length);
            foreach (char c in aValue)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        // Reads a top-level string property from a JSON object, returning "" when
        // absent / not a string / unparseable.
        private static string ReadJsonString(string aJson, string aName)
        {
            if (string.IsNullOrEmpty(aJson))
            {
                return "";
            }

            try
            {
                using (JsonDocument vDoc = JsonDocument.Parse(aJson))
                {
                    if (vDoc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return "";
                    }

                    JsonElement vNode;
                    if (vDoc.RootElement.TryGetProperty(aName, out vNode)
                        && (vNode.ValueKind == JsonValueKind.String))
                    {
                        string vStr = vNode.GetString();
                        return vStr ?? "";
                    }
                }
            }
            catch (Exception)
            {
            }

            return "";
        }

        // Reads a top-level integer property from a JSON object (tolerating a
        // numeric string), returning aDefault when absent / unparseable.
        private static int ReadJsonInt(string aJson, string aName, int aDefault)
        {
            if (string.IsNullOrEmpty(aJson))
            {
                return aDefault;
            }

            try
            {
                using (JsonDocument vDoc = JsonDocument.Parse(aJson))
                {
                    if (vDoc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        return aDefault;
                    }

                    JsonElement vNode;
                    if (!vDoc.RootElement.TryGetProperty(aName, out vNode))
                    {
                        return aDefault;
                    }

                    if (vNode.ValueKind == JsonValueKind.Number)
                    {
                        int vInt;
                        if (vNode.TryGetInt32(out vInt))
                        {
                            return vInt;
                        }
                    }
                    else if (vNode.ValueKind == JsonValueKind.String)
                    {
                        int vParsed;
                        if (int.TryParse(vNode.GetString(), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out vParsed))
                        {
                            return vParsed;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return aDefault;
        }

        /// <summary>Frees the wrapped WebAuthn API. Mirrors Delphi Destroy.</summary>
        public void Dispose()
        {
            if (FDisposed)
            {
                return;
            }

            FDisposed = true;
            try
            {
                FAuthn.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
