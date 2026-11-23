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

<!-- signed-endpoints:begin (generated from RequireElectronicSignature call sites by `pnpm cadence generate-cli-docs`; do not edit) -->

| Host        | Endpoint                                                                                            | `semantic_action`                      | `action`                                                                            |
| ----------- | --------------------------------------------------------------------------------------------------- | -------------------------------------- | ----------------------------------------------------------------------------------- |
| `econsent`  | `POST /api/v1/econsent/templates/{template_id}/capture-consent`                                     | `econsent.consent.capture`             | `/api/v1/econsent/templates/{template_id}/capture-consent`                          |
| `econsent`  | `POST /api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent`            | `econsent.consent.capture`             | `/api/v1/econsent/templates/{template_id}/versions/{version_index}/capture-consent` |
| `execution` | `POST /api/v1/execution/cec/dossiers/{id}/chair-adjudicate`                                         | `execution.cec.chair_adjudicate`       | `/api/v1/execution/cec/dossiers/{id}/chair-adjudicate`                              |
| `execution` | `POST /api/v1/execution/labs/toxicity/review/{review_id}/action`                                    | `execution.lab_toxicity_review.action` | `/api/v1/execution/labs/toxicity/review/{review_id}/action`                         |
| `execution` | `POST /api/v1/execution/randomization/schemes/{schemeId}/approve`                                   | `execution.form.approve`               | `/api/v1/execution/randomization/schemes/{schemeId}/approve`                        |
| `execution` | `POST /api/v1/execution/subjects/{subjectId}/randomize`                                             | `execution.subject.randomize`          | `/api/v1/execution/subjects/{subjectId}/randomize`                                  |
| `kernel`    | `POST /api/v1/execution/forms/{formId}/signatures`                                                  | `execution.form.sign`                  | `/api/v1/execution/forms/{formId}/signatures`                                       |
| `kernel`    | `POST /api/v1/kernel/library-items/{itemRef}/approve`                                               | `kernel.library_item.approve`          | `/api/v1/kernel/library-items/{itemRef}/approve`                                    |
| `kernel`    | `POST /api/v1/kernel/locks`                                                                         | `kernel.lock.apply`                    | `/api/v1/kernel/locks`                                                              |
| `kernel`    | `POST /api/v1/kernel/locks/{lockRef}/release`                                                       | `kernel.lock.release`                  | `/api/v1/kernel/locks/{lockRef}/release`                                            |
| `kernel`    | `POST /api/v1/kernel/protocol-versions/{versionRef}/approve`                                        | `kernel.protocol_version.approve`      | `/api/v1/kernel/protocol-versions/{versionRef}/approve`                             |
| `knowledge` | `POST /api/v1/training/delegations/bulk-signoff`                                                    | `knowledge.delegation.sign_off`        | `/api/v1/training/delegations/bulk-signoff` (`batch_id` SHA-256)                    |
| `safety`    | `POST /api/v1/safety/dsur/{id}/approve` (alias `POST /api/v1/safety/periodic-reports/{id}/approve`) | `safety.periodic_report.approve`       | `/api/v1/safety/dsur/{reportId}/approve`                                            |
| `tickets`   | `POST /api/v1/tickets/{ticket_id}/sign`                                                             | `tickets.ticket.sign`                  | `/api/v1/tickets/{ticket_id}/sign`                                                  |

<!-- signed-endpoints:end -->

---

## 3. Cryptographic & Verification Contract

Downstream microservices (via `GatewayAuthMiddleware` or specific verification components) MUST enforce the following validation pipeline upon receiving a signature-gated mutation.

This verification is centralized in `SigTokenVerifier.VerifyAndConsumeAsync(token, userId, SigTokenBinding, secret)` in `packages/security` (`Cadence.Security.Authentication`). Endpoints apply it with `RequireElectronicSignature(semanticAction, resolveAction)`, or `RequireElectronicSignature(resolveBinding)` when the binding (for example a `batch_id`) comes from the request body. The filter runs before the handler and answers `401` on any failure, so no write happens; the handler reads the verified token with `HttpContext.VerifiedSigToken()`. Services add any domain-specific rules on top (such as local action gating in eConsent):

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
   - Single use reaches exactly as far as the store is shared. Every host that maps a `RequireElectronicSignature` route registers `AddCadenceDurableSigTokenStore<TContext>()` (`PostgresSigTokenUseStore<TContext>`, writing `consumed_signature_tokens` in its own schema with a primary key on the token id), which holds across instances and restarts. Outside `Development` a host that maps a signed route over the process-local `ReplayCache` refuses to start (#5434, ADR-2515):

<!-- durable-sig-token-stores:begin (generated from AddCadenceDurableSigTokenStore call sites by `pnpm cadence generate-cli-docs`; do not edit) -->

| Host        | DbContext                          | Table                                 | Registration                               |
| ----------- | ---------------------------------- | ------------------------------------- | ------------------------------------------ |
| `econsent`  | `EconsentDbContext`                | `econsent.consumed_signature_tokens`  | `apps/econsent/src/Program.cs`             |
| `execution` | `ExecutionDbContext`               | `edc.consumed_signature_tokens`       | `apps/execution/src/Program.cs`            |
| `kernel`    | `ExecutionSignatureTokenDbContext` | `execution.consumed_signature_tokens` | `apps/execution/module/ExecutionModule.cs` |
| `knowledge` | `KnowledgeDbContext`               | `knowledge.consumed_signature_tokens` | `apps/knowledge/src/Program.cs`            |
| `safety`    | `SafetyDbContext`                  | `safety.consumed_signature_tokens`    | `apps/safety/src/Program.cs`               |
| `tickets`   | `TicketsDbContext`                 | `tickets.consumed_signature_tokens`   | `apps/tickets/src/Program.cs`              |

<!-- durable-sig-token-stores:end -->

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

Executing single-record and multi-record electronic sign-offs across clinical forms and delegation logs requires strict cryptographic payload binding and atomic database persistence to satisfy 21 CFR Part 11 and GxP standards.

### 6.1 Role Authorization & Scopes

- **Authorization & Grants:** Form signatures (`POST /api/v1/execution/forms/{formId}/signatures` on the `kernel` host's Execution module) authorize `SignCollectionInstance` (`collection.sign`) against the caller's kernel grants at the form's enrollment (`FormSignatureEndpoints.cs`). Bulk delegation sign-off (`POST /api/v1/training/delegations/bulk-signoff` on `knowledge`) requires the `delegation_log:sign` permission (principal investigator, authorized ER physician, or lead investigator) and `RequireCallerStudyAndSiteScope()`.
- **Site & Study Isolation:** Callers are restricted to their gateway-signed study and site scopes or kernel enrollment grants; unauthorized personas receive `403 Forbidden` (`ProblemDetails`).

### 6.2 Target Semantics & Content Binding

- **eCRF Form Signatures (`execution` module in `kernel-api`):** `POST /api/v1/execution/forms/{formId}/signatures` binds the step-up token to `/api/v1/execution/forms/{formId}/signatures` (`execution.form.sign`) and requires the caller to supply the exact `content_hash` they read alongside an `AttestationMeaning` (`Approved` or `Responsibility`). If the form changed after it was read, `SignCollectionInstance` rejects the command with `409 Conflict` (`ProblemDetails`).
- **Bulk Delegation Sign-Off (`knowledge`):** `POST /api/v1/training/delegations/bulk-signoff` resolves the explicit list of delegation IDs in the request body; any unknown or out-of-scope delegation ID fails the request with `404 Not Found` or `403 Forbidden` (`ProblemDetails`) before committing changes.

### 6.3 Cryptographic Token-to-Payload Binding (`batch_id`)

To prevent target swapping or interception attacks on multi-record mutations, the single-use `X-Sig-Token` JWT carries a deterministic `batch_id` claim verified by `RequireElectronicSignature(Func<EndpointFilterInvocationContext, SigTokenBinding>)`:

1. The client canonicalizes the target identifiers (for example, trimmed delegation IDs sorted ordinally and joined with `,`, or a canonical colon-delimited target descriptor).
2. The lower-case hex SHA-256 digest of that canonical string is embedded as the `batch_id` claim when requesting the token from `POST /api/v1/auth/signature-verification`.
3. Upon receiving the mutation request, the downstream .NET endpoint filter recomputes the SHA-256 `batch_id` from the HTTP request body and passes `new SigTokenBinding(action, semanticAction, batchId)` to `SigTokenVerifier.VerifyAndConsumeAsync`.
4. Any mismatch between the request payload and `batch_id` fails before the endpoint handler runs with HTTP `401 Unauthorized`, and **no writes or mutations are made** to PostgreSQL.

### 6.4 Single-Use Replay Prevention (`jti`)

Each host records the verified token's `jti` in its schema-local `consumed_signature_tokens` table (`execution.consumed_signature_tokens` via `ExecutionSignatureTokenDbContext` on `kernel-api`, `edc.consumed_signature_tokens` via `ExecutionDbContext` on `execution`, `knowledge.consumed_signature_tokens` via `KnowledgeDbContext` on `knowledge`, `econsent.consumed_signature_tokens` via `EconsentDbContext` on `econsent`, `safety.consumed_signature_tokens` via `SafetyDbContext` on `safety`, and `tickets.consumed_signature_tokens` via `TicketsDbContext` on `tickets`; section 3, step 6), so a replay is rejected across instances and restarts. Requests missing `X-Sig-Token` or carrying a mismatched `sub`, `action`, `semantic_action`, or `batch_id` are rejected before any database mutation, and HMAC seals are keyed with `GATEWAY_SECRET` (never a placeholder).

### 6.5 Individual Record Manifestation & Version Binding

Even when a signer authorizes multiple records in one step-up flow, 21 CFR § 11.50 mandates an attributable, verifiable manifestation bound to each signed record:

- **Signer Identity:** Verified caller identity (`sub` / actor ID) and re-authentication timestamp (`token.IssuedAt` in UTC, `gateway-step-up:v3`).
- **Meaning:** Explicit regulatory meaning (`AttestationMeaning` or `delegation_log:sign` attestation).
- **Content & Version Binding:** Kernel `Attestation` rows bind to the exact `CollectionInstance` revision and `content_hash`, while standalone service records increment their concurrency/version index and persist an HMAC seal plus hash-chained audit entry (`GxPAuditInterceptor` / kernel audit chain).

### 6.6 Lock Checks & Transactional Atomicity Guarantees

To prevent state contamination or partial-approval corruptions:

1. Kernel commands (`SignCollectionInstance`, `ApplyLock`, `ReleaseLock`, `ApproveLibraryItem`) and standalone signed endpoints evaluate active locks across the study, site, enrollment, form instance, and observation hierarchy before mutating state.
2. When any target is frozen or locked, the service returns a structured HTTP `409 Conflict` or `403 Forbidden` `ProblemDetails` response (`KernelErrorEndpoints.Problem` / `TypedResults.Problem`) without mutating clinical state.
3. Every multi-row write executes atomically inside a single EF Core execution-strategy database transaction (`DbContext.Database.CreateExecutionStrategy()` with `BeginTransactionAsync` / `SaveChangesAsync`, ADR-2449 and ADR-2523). On any lock violation, validation error, or exception, the database transaction rolls back completely, leaving all targeted records unchanged with zero partial approvals.
