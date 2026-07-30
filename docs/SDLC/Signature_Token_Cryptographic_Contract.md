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
