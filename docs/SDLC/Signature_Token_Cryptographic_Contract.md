# Gateway-to-Execution Cryptographic Signature-Token Contract

## 1. Overview & Regulatory Rationale
Pursuant to **FDA 21 CFR Part 11 (§ 11.50 & § 11.200)** and **EU Annex 11**, executing critical clinical mutations (such as subject randomization, form approvals, unblinding actions, query management, or trial state changes) requires explicit, double-keying identity confirmation immediately prior to applying electronic signatures.

To achieve this without compromising security, the API Gateway operates as the central authentication authority. It challenges and verifies the user's credentials against Keycloak, and issues a cryptographically signed, short-lived **Signature Authorization Token (`X-Sig-Token`)**. Downstream microservices verify this token using a shared symmetric key (`GATEWAY_SECRET`) to ensure that:
1. The signer is active and fully re-authenticated.
2. The signature action is strictly bound to the target REST endpoint/action.
3. The token cannot be reused (single-use replay prevention).
4. Credentials/passwords never leak to downstream services, logs, or databases.

---

## 2. Token Claims Schema (JWT Payload)
The `X-Sig-Token` is a JSON Web Token (JWT) signed with the **HS256** algorithm. The payload contains the following standard and custom claims:

| Claim | Type | Description | Mandatory |
| :--- | :--- | :--- | :--- |
| `sub` | `string` | The unique authenticated identity/User ID. | Yes |
| `username` | `string` | The preferred username of the signer. | Yes |
| `roles` | `list[string]` | Lowercase list of canonical roles possessed by the user. | Yes |
| `action` | `string` | The specific API path/action the token is authorized to sign. | Yes |
| `batch_id` | `string` | Optional identifier for batch signature bindings (e.g., multi-form approvals). | No |
| `semantic_action` | `string` | The stable, namespaced semantic action identifier (e.g. quality.capa.close). | No (Yes in v3+) |
| `sig_ver` | `string` | The contract version claim (e.g., `"v3"`). | No (Yes in v3+) |
| `iat` | `float` | Unix timestamp of when the token was issued. | Yes |
| `exp` | `float` | Unix timestamp of token expiration (strictly set to `iat + 60.0`). | Yes |
| `jti` | `string` | Cryptographically random unique identifier (UUIDv4) for single-use tracking. | Yes |

### Example Payload JSON:
```json
{
  "sub": "usr_9a4f21b8c0d9",
  "username": "dr_robert_investigator",
  "roles": ["investigator", "site_investigator"],
  "action": "/api/v1/execution/form-submissions/123/approve",
  "batch_id": "batch_2026_08_a",
  "semantic_action": "execution.form.approve",
  "sig_ver": "v3",
  "iat": 1785931200.0,
  "exp": 1785931260.0,
  "jti": "mock-jti-uuid-value-123456"
}
```

---

## 3. Cryptographic & Verification Contract

Downstream microservices (via `GatewayAuthMiddleware` or specific verification components) MUST enforce the following validation pipeline upon receiving a signature-gated mutation:

1. **Header Presence:** Extract `X-Sig-Token` from the HTTP request headers. If absent, reject with `401 Unauthorized` and payload:
   ```json
   {
     "detail": "REAUTHENTICATION_REQUIRED",
     "error": "REAUTHENTICATION_REQUIRED",
     "message": "21 CFR Part 11 mandate: Re-authentication is required."
   }
   ```
2. **Signature Verification:** Decode and verify the HS256 signature using the shared `GATEWAY_SECRET`. Any JWTError (such as altered token, invalid signature, or wrong secret) must trigger a `401 Unauthorized` rejection.
3. **Temporal Validity:** Verify that `exp > time.time()`. Tokens older than 60 seconds from issuance must be rejected.
4. **Signer Identity Binding:** Extract `sub` from the signature token. Verify that it matches the authenticated user ID (`X-User-Id` header injected by the Gateway). Mismatches must be rejected.
5. **Action/Endpoint Binding:** Retrieve `action` and `semantic_action` from the token. Verify that the incoming request matches the semantic action + concrete target resource/path. This prevents a token generated for form "A" approval from being maliciously re-routed to approve form "B" or authorize unblinding.
6. **Single-Use Verification (Replay Prevention):**
   - Extract the unique `jti` claim.
   - Query the distributed or in-memory `ReplayPreventionCache`. If `jti` is found, reject the request as a replay attack.
   - If `jti` is not found, record the `jti` alongside its `exp` timestamp in the cache to automatically prune it post-expiration.

---

## 4. GxP Security Boundaries
- **No Password Persistence:** The user's re-submitted password is strictly used to authenticate against Keycloak's token endpoint and is immediately discarded. It is never logged in gateway logs, downstream logs, nor is it written to the JWT token claims.
- **Auditor Restrictions:** Users with auditor/inspector roles are strictly prohibited from generating signature tokens or executing signature-gated mutations.
- **Unverified Token Rejection:** The system ensures that failed credential checks (e.g., wrong password, expired session, invalid MFA/TOTP) never produce a valid signed token.

---

## 5. Versioning & Backward Compatibility
To prevent service disruptions during upgrades, the API gateway and middleware accept v3 tokens (containing `semantic_action` and `sig_ver`) as well as older v1/v2 tokens. When verifying older tokens without the `semantic_action` claim, the system automatically falls back to loose path-only matching, maintaining complete backward compatibility.

---

## 6. Batch Electronic Sign-Off & Token Binding Contract (Trace-14 & Trace-15)

Executing batch electronic sign-offs for multiple clinical observations/form submissions in a single transaction requires strict cryptographic and database constraints to satisfy 21 CFR Part 11 and GxP standards.

### 6.1 Role Authorization & Scopes
* **Role Names:** Strictly restricted to the Principal Investigator (`pi` or `principal investigator`, mapped downstream to `ROLE_PI` or `ROLE_INVESTIGATOR`). Non-PI personas (e.g., CRC, CRA, Data Manager) are forbidden from generating batch tokens or executing batch sign-offs.
* **Site Scopes:** The PI is site-isolated, meaning their authorization is strictly validated against their assigned site.

### 6.2 Target Semantics & Eligibility
The `/api/v1/execution/batch-sign-off` endpoint resolves eligible targets via a `target_type` parameter:
* **`FORM`**: Direct list of specific `FormSubmission` primary key UUIDs.
* **`VISIT`**: Target visit IDs (e.g. `VISIT-001`), resolving to all nested submissions for those visits.
* **`SUBJECT`**: Subject pseudonyms, resolving to all submissions associated with those subjects.

* **Eligibility Rule:** Only form submissions in the `COMPLETED` status can be approved. Submissions in `DRAFT` or already in `APPROVED` status are skipped.

### 6.3 Cryptographic Token-to-Payload Binding (`batch_id`)
To prevent target swapping or interception attacks, the single-use `X-Sig-Token` JWT contains a custom `batch_id` claim.
1. The client-side application serializes the sign-off payload into a canonical, colon-delimited string:
   `{study_id}:{target_type}:{sorted_target_ids_comma_separated}:{signing_reason}`
2. The SHA-256 hash of this serialized string is calculated and embedded as the `batch_id` claim in the JWT.
3. Upon receiving the mutation request, the downstream Execution service recalculates this canonical hash from the HTTP request body and verifies that it matches the token's `batch_id` exactly.
4. Any mismatch results in immediate signature verification failure (rejection with HTTP 401 Unauthorized), and **no writes/mutations are made** to the database.

### 6.4 Single-Use Replay Prevention (`jti`)
The `jti` UUID claim is verified against active in-memory single-use caches at both the Gateway layer and the downstream Execution service level. Once verified, the token is recorded in the replay cache and any replay attempts are rejected.

### 6.5 Individual Record Manifestation & Version Binding
While the PI signs once for the batch, 21 CFR § 11.50 mandates a distinct, verifiable manifestation generated per approved submission record. The system generates and embeds the following block into each record's `signature_manifest` column and its corresponding `AuditLog` entry:
* **Signer Identity:** `signer_username` and `signer_full_name`.
* **UTC Datetime:** Precise timestamp format ending in `Z`.
* **Meaning / Reason:** Code `PI_APPROVAL` and text `"I approve this clinical record and confirm medical responsibility."`.
* **Record ID & Version:** Specifically binds the unique record UUID and its incremented `record_version` (e.g., from 1 to 2).
* **Hash:** Captures the unique `canonical_signature_hash` for the record.

### 6.6 Lock Checks & Transactional Atomicity Guarantees
To prevent state contamination or partial-approval corruptions:
1. The Execution service checks active locks at all hierarchical levels: trial, site, visit, subject, and form-level locks.
2. If any target is locked, a `PermissionError` is raised.
3. The entire batch is executed inside a single transaction nested scope (`session.begin_nested()`). On any lock breach or exception, the transaction is completely rolled back, leaving all targeted submissions unchanged in their original status. No partial approvals are permitted.
