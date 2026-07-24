# ADR 2026-07-23: Secure API Signatures with Canonical JSON (Version 2 Sole Standard)

## Status
Accepted / Standardized

## Context
Previously, the internal API Gateway used a simple colon-concatenated string to sign user identity headers (`X-User-Id`, `X-User-Roles`, `X-Gateway-Timestamp`) with an HMAC-SHA256 signature. This legacy format was susceptible to parameter injection and signature collision attacks because of the lack of strict boundaries. Furthermore, changes to clinical states were authenticated out-of-band without audit change reasons (`X-Change-Reason`) being cryptographically protected, posing a risk of audit log tampering.

During a rolling upgrade window, both formats were supported. However, active production traffic has completely transitioned, and maintaining legacy validation paths introduces unnecessary developer overhead and security risks. Therefore, we are standardizing the entire ecosystem exclusively on Version 2 signatures.

## Decision
We decided to upgrade and enforce the internal authentication signature mechanism to use a canonical, key-sorted JSON serialization as the sole supported standard, completely deprecating and removing legacy Version 1 signature fallback paths.

Specifically:
1. **Canonical JSON Serialization (Sole Standard):** The signature payload is constructed as a JSON object containing `user_id`, `roles`, `timestamp`, and `change_reason`. The keys are sorted alphabetically, and all unnecessary whitespace is removed (`separators=(',', ':')`) to produce a deterministic string for HMAC-SHA256 calculation.
2. **Version Enforcements (X-Signature-Version):**
   - **Version 2 (Canonical JSON):** Enforces structured JSON signatures and verifies the `X-Change-Reason` header. This is the sole supported signature standard. All requests specifying legacy versions or omitting the Version 2 JSON signature format are immediately rejected by downstream security middleware.
3. **Change Justification Validation:**
   - The `X-Change-Reason` header is required for all state-modifying requests (`POST`, `PUT`, `DELETE`, `PATCH`). It is optional (and defaults to an empty string) for safe read-only methods (`GET`, `HEAD`, `OPTIONS`).
   - The Gateway validates that `X-Change-Reason` length does not exceed 255 characters to fail-fast and protect downstream relational database schemas from truncation/error before signing.
   - Downstream middleware verifies that the raw `X-Change-Reason` header matches the signed version inside the JSON payload exactly.
4. **Replay Protection:** A strict ±300-second validation window is enforced on timestamps.

## Alternatives Considered
- **Direct JWT Propagation:** Passing the end-user's bearer OIDC token directly to downstream microservices. This was rejected because downstream services would need to constantly fetch Keycloak's JWKS public keys, causing additional latency and dependencies.
- **Immediate Hard Upgrade:** Forcing all microservices to upgrade to the new signature format simultaneously. This was rejected initially but has now been successfully executed, with all legacy code completely pruned from the codebase.

## Trade-offs
- **Positive:** Enhances platform security by eliminating signature collision/injection vulnerabilities and guaranteeing the integrity of audit change reasons.
- **Positive:** Eliminates downstream downgrade attacks by completely rejecting legacy Version 1 signature formats.
- **Positive:** Protects downstream databases from malicious inputs with fail-fast character length limits.
- **Negative:** Increased message complexity due to JSON serialization compared to simple string concatenation.
