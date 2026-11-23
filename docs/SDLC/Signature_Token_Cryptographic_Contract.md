# Gateway-to-Execution Cryptographic Signature-Token Contract

## 1. Overview & Regulatory Rationale

Pursuant to **FDA 21 CFR Part 11 (§ 11.50 & § 11.200)** and **EU Annex 11**, executing critical clinical mutations (such as electronic consent signatures, subject randomization, form approvals, unblinding actions, query management, or trial state changes) requires explicit, double-keying identity confirmation immediately prior to applying electronic signatures.

To achieve this without compromising security, the API Gateway operates as the central authentication authority. It challenges and verifies the user's credentials against Keycloak, and issues a cryptographically signed, short-lived **Signature Authorization Token (`X-Sig-Token`)**. Downstream microservices verify this token using a shared symmetric key (`GATEWAY_SECRET`) to ensure that:

1. The signer is active and fully re-authenticated.
2. The signature action is strictly bound to the target REST endpoint/action.
3. The token cannot be reused (single-use replay prevention).
4. Credentials/passwords never leak to downstream services, logs, or databases.

---

## 2. Token Claims Schema (JWT Payload)

The `X-Sig-Token` is a JSON Web Token (JWT) signed with the **HS256** algorithm. The payload contains the following standard and custom claims:

| Claim             | Type           | Description                                                                    | Mandatory       |
| :---------------- | :------------- | :----------------------------------------------------------------------------- | :-------------- |
| `sub`             | `string`       | The unique authenticated identity/User ID.                                     | Yes             |
| `username`        | `string`       | The preferred username of the signer.                                          | Yes             |
| `roles`           | `list[string]` | Lowercase list of canonical roles possessed by the user.                       | Yes             |
| `action`          | `string`       | The specific API path/action the token is authorized to sign.                  | Yes             |
| `batch_id`        | `string`       | Optional identifier for batch signature bindings (e.g., multi-form approvals). | No              |
| `semantic_action` | `string`       | The stable, namespaced semantic action identifier (e.g. quality.capa.close).   | No (Yes in v3+) |
| `sig_ver`         | `string`       | The contract version claim (e.g., `"v3"`).                                     | No (Yes in v3+) |
| `iat`             | `float`        | Unix timestamp of when the token was issued.                                   | Yes             |
| `exp`             | `float`        | Unix timestamp of token expiration (strictly set to `iat + 60.0`).             | Yes             |
| `jti`             | `string`       | Cryptographically random unique identifier (UUIDv4) for single-use tracking.   | Yes             |

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

### Issuance

The gateway issues tokens at `POST /api/v1/auth/signature-verification` to a caller already signed in with a Keycloak bearer token. The request carries `password`, optional `totp`, `action`, `semantic_action` and optional `batch_id` (and optionally `username`, which must be the caller's own). The gateway re-checks the password with a Keycloak direct-access grant on a client reserved for signing (`OIDC_SIGNATURE_CLIENT_ID`, default `cadence-esignature`), ends the session that grant opened, and answers `{ "sig_token", "expires_in": 60 }` with `Cache-Control: no-store`. Attempts are limited to five per signer per minute, in addition to the realm's brute-force lockout. Implementation: `apps/gateway/src/Signatures/`, `SigTokenIssuer` in `packages/security`; configuration in `apps/gateway/README.md`.

The client then repeats the signature-gated request with the `X-Sig-Token` header. The gateway includes the header in the HMAC-signed identity it forwards, so it cannot be swapped between the gateway and the service.

Signature-gated endpoints today:

| Endpoint                                                                                  | `semantic_action`                       | `action`                                                     |
| :---------------------------------------------------------------------------------------- | :-------------------------------------- | :----------------------------------------------------------- |
| `POST /api/v1/execution/forms/{id}/approve` (and the legacy `/api/v1/forms/{id}/approve`) | `execution.form.approve`                | `/api/v1/execution/forms/{id}/approve`                       |
| `PATCH /api/v1/execution/subjects/{subjectId}/state` (when `target` is `UNBLINDED`)       | `execution.subject.unblind`             | `/api/v1/execution/subjects/{subjectId}/state`               |
| `POST /api/v1/ctms/doa/sign-off`                                                          | `ctms.doa.sign_off`                     | `/api/v1/ctms/doa/sign-off`                                  |
| `POST /api/v1/ctms/monitoring-visits/{visit_id}/sign-off`                                 | `ctms.monitoring_visit.sign_off`        | `/api/v1/ctms/monitoring-visits/{visit_id}/sign-off`         |
| `POST /api/v1/ctms/letters/compile` (when `Finalize` is `true`)                           | `ctms.letter.compile`                   | `/api/v1/ctms/letters/compile`                               |
| `POST /api/v1/ctms/startup/sites/{siteId}/greenlight/certify`                             | `ctms.greenlight.certify`               | `/api/v1/ctms/startup/sites/{siteId}/greenlight/certify`     |
| `POST /api/v1/ctms/startup/matrix/batch-certify`                                          | `ctms.greenlight.batch_certify`         | `/api/v1/ctms/startup/matrix/batch-certify`                  |
| `POST /api/v1/payments/invoices/{id}/remittance-advice/sign`                              | `payments.remittance.sign`              | `/api/v1/payments/invoices/{id}/remittance-advice/sign`      |
| `POST /api/v1/imaging/adjudications/{sessionId}/chair-adjudicate`                         | `imaging.adjudication.chair_adjudicate` | `/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate` |
| `POST /api/v1/interop/esource/reconcile/accept`                                           | `interop.esource.accept`                | `/api/v1/interop/esource/reconcile/accept`                   |
| `POST /api/v1/interop/esource/reconcile/batch-accept`                                     | `interop.esource.batch_accept`          | `/api/v1/interop/esource/reconcile/batch-accept`             |
| `POST /api/v1/interop/esource/reconcile/reject`                                           | `interop.esource.reject`                | `/api/v1/interop/esource/reconcile/reject`                   |
| `POST /api/v1/interop/esource/reconcile/batch-reject`                                     | `interop.esource.batch_reject`          | `/api/v1/interop/esource/reconcile/batch-reject`             |

---

## 3. Cryptographic & Verification Contract

Downstream microservices (via `GatewayAuthMiddleware` or specific verification components) MUST enforce the following validation pipeline upon receiving a signature-gated mutation.

This verification is centralized in `SigTokenVerifier.VerifyAndConsumeAsync(token, userId, SigTokenBinding, secret)` in `packages/security` (`Cadence.Security.Authentication`). Endpoints apply it with `RequireElectronicSignature(semanticAction, resolveAction)`, `RequireElectronicSignature(resolveBinding)` when the binding (for example a `batch_id` computed via `SigTokenBatchId.FromIds(...)`) comes from the request body, or `RequireElectronicSignatureWhen(resolveBindingIfRequired)` on mixed-mode endpoints where only regulated transitions require step-up verification (such as `UNBLINDED` subject state changes or `Finalize: true` letter compilation). The filter runs before the handler and answers `401` on any failure, so no write happens; the handler reads the verified token with `HttpContext.VerifiedSigToken()`. Services add any domain-specific rules on top (such as local action gating in eConsent):

1. **Header Presence:** Extract `X-Sig-Token` from the HTTP request headers. If absent, reject with `401 Unauthorized` and payload:
   ```json
   {
     "detail": "REAUTHENTICATION_REQUIRED",
     "error": "REAUTHENTICATION_REQUIRED",
     "message": "21 CFR Part 11 mandate: Re-authentication is required."
   }
   ```
2. **Signature Verification:** Decode and verify the HS256 signature using the shared `GATEWAY_SECRET`. Any JWTError (such as altered token, invalid signature, or wrong secret) must trigger a `401 Unauthorized` rejection.
3. **Temporal Validity (acceptance window):** on the injected `IClock`, accept only while `iat - 5s <= now < exp + 5s` (and `now >= nbf - 5s`), and only if `exp - iat <= 60s`. The 5 seconds (`SigTokenVerifier.ClockSkew`) absorbs clock drift between the issuing gateway and the verifying service, so a token is usable for at most 70 seconds of verifier time (ADR-2515).
4. **Signer Identity Binding:** Extract `sub` from the signature token. Verify that it matches the authenticated user ID (`X-User-Id` header injected by the Gateway). Mismatches must be rejected.
5. **Action/Endpoint Binding:** Retrieve `action` and `semantic_action` from the token. Verify that the incoming request matches the semantic action + concrete target resource/path. This prevents a token generated for form "A" approval from being maliciously re-routed to approve form "B" or authorize unblinding.
6. **Single-Use Verification (Replay Prevention):**
   - Extract the unique `jti` claim.
   - Only after every other check passes, record it with `ISigTokenUseStore.TryRecordFirstUseAsync(jti, exp + 5s)`. A second use, including a concurrent one, is rejected.
   - The record is kept until `exp + 5s`, the end of the acceptance window, never only until `exp`; otherwise the token could be replayed in the skew after expiry.
   - Single use reaches exactly as far as the store is shared. Every service with an e-signature route (Execution, eConsent, Knowledge, Safety, Tickets, CTMS, Payments, Imaging, and Interop) uses `PostgresSigTokenUseStore<TContext>` (`consumed_signature_tokens` in its own schema, primary key on the token id), which holds across instances and restarts. Outside `Development` a host that maps a signed route over the process-local `ReplayCache` refuses to start (#5434, ADR-2515, [ADR-2660](../adr/2026-11-23-enforce-step-up-signature-filters-and-durable-stores.md)).

---

## 4. GxP Security Boundaries

- **No Password Persistence:** The user's re-submitted password is strictly used to authenticate against Keycloak's token endpoint and is immediately discarded. It is never logged in gateway logs, downstream logs, nor is it written to the JWT token claims.
- **Auditor Restrictions:** Users with auditor/inspector roles are strictly prohibited from generating signature tokens or executing signature-gated mutations.
- **Unverified Token Rejection:** The system ensures that failed credential checks (e.g., wrong password, expired session, invalid MFA/TOTP) never produce a valid signed token.

---

## 5. Versioning

Only v3 tokens (with `semantic_action` and `sig_ver: "v3"`) are issued and accepted. The .NET verifier rejects older v1/v2 tokens, tokens valid for longer than 60 seconds, and tokens carrying an auditor or inspector role. A token that fails any check is not consumed.

---

## 6. Batch Electronic Sign-Off & Token Binding Contract (Trace-14 & Trace-15)

Executing batch electronic sign-offs for multiple clinical observations/form submissions in a single transaction requires strict cryptographic and database constraints to satisfy 21 CFR Part 11 and GxP standards.

### 6.1 Role Authorization & Scopes

- **Role Names:** Strictly restricted to the Principal Investigator (`pi` or `principal investigator`, mapped downstream to `ROLE_PI` or `ROLE_INVESTIGATOR`). Non-PI personas (e.g., CRC, CRA, Data Manager) are forbidden from generating batch tokens or executing batch sign-offs.
- **Site Scopes:** The PI is site-isolated, meaning their authorization is strictly validated against their assigned site.

### 6.2 Target Semantics & Eligibility

The `/api/v1/execution/batch-sign-off` endpoint resolves eligible targets via a `target_type` parameter:

- **`FORM`**: Direct list of specific `FormSubmission` primary key UUIDs.
- **`VISIT`**: Target visit IDs (e.g. `VISIT-001`), resolving to all nested submissions for those visits.
- **`SUBJECT`**: Subject pseudonyms, resolving to all submissions associated with those subjects.

- **Eligibility Rule:** Only form submissions in the `COMPLETED` status can be approved. Submissions in `DRAFT` or already in `APPROVED` status are skipped.

### 6.3 Cryptographic Token-to-Payload Binding (`batch_id`)

To prevent target swapping or interception attacks, the single-use `X-Sig-Token` JWT contains a custom `batch_id` claim.

1. The client-side application serializes the sign-off payload into a canonical, colon-delimited string:
   `{study_id}:{target_type}:{sorted_target_ids_comma_separated}:{signing_reason}`
2. The SHA-256 hash of this serialized string is calculated and embedded as the `batch_id` claim in the JWT.
3. Upon receiving the mutation request, the downstream Execution service recalculates this canonical hash from the HTTP request body and verifies that it matches the token's `batch_id` exactly.
4. Any mismatch results in immediate signature verification failure (rejection with HTTP 401 Unauthorized), and **no writes/mutations are made** to the database.

### 6.4 Single-Use Replay Prevention (`jti`)

The Execution service records the `jti` in `execution.consumed_signature_tokens` (section 3, step 6), so a replay is rejected on any Execution instance and after a restart. Both batch routes, `/api/v1/execution/batch-sign-off` and its `/api/v1/execution/signatures/batch-sign-off` alias, are bound to the action `/api/v1/execution/batch-sign-off` and the semantic action `execution.form.approve`, and a request without a token is rejected before any write. The service also checks that the token's `sub` is the caller and its `batch_id` is the request's, and keys every manifest HMAC with `GATEWAY_SECRET`; with no secret it refuses to sign rather than writing a placeholder.

### 6.4.1 Delegation-log sign-off

`/api/v1/training/delegations/bulk-signoff` (knowledge), `/api/v1/ctms/startup/matrix/batch-certify` (CTMS, `ctms.greenlight.batch_certify`), and `/api/v1/interop/esource/reconcile/batch-accept` / `batch-reject` (Interop, `interop.esource.batch_accept` and `interop.esource.batch_reject`) follow the same contract with `batch_id` = lower-case hex SHA-256 (`SigTokenBatchId.FromIds` / `computeBatchTargetHash`) of the trimmed target ids, sorted ordinally and joined with `,`. Delegation sign-off also requires the `delegation_log:sign` permission (principal investigator, authorized ER physician, lead investigator). The signer is the verified caller, an unknown id fails the request with `404` (or `400` on missing greenlight gates), and seals are HMACs keyed with `GATEWAY_SECRET`.

### 6.5 Individual Record Manifestation & Version Binding

While the PI signs once for the batch, 21 CFR § 11.50 mandates a distinct, verifiable manifestation generated per approved submission record. The system generates and embeds the following block into each record's `signature_manifest` column and its corresponding `AuditLog` entry:

- **Signer Identity:** `signer_username` and `signer_full_name`.
- **UTC Datetime:** Precise timestamp format ending in `Z`.
- **Meaning / Reason:** Code `PI_APPROVAL` and text `"I approve this clinical record and confirm medical responsibility."`.
- **Record ID & Version:** Specifically binds the unique record UUID and its incremented `record_version` (e.g., from 1 to 2).
- **Hash:** Captures the unique `canonical_signature_hash` for the record.

### 6.6 Lock Checks & Transactional Atomicity Guarantees

To prevent state contamination or partial-approval corruptions:

1. The Execution service checks active locks at all hierarchical levels: trial, site, visit, subject, and form-level locks.
2. If any target is locked, a `PermissionError` is raised.
3. The entire batch is executed inside a single transaction nested scope (`session.begin_nested()`). On any lock breach or exception, the transaction is completely rolled back, leaving all targeted submissions unchanged in their original status. No partial approvals are permitted.
