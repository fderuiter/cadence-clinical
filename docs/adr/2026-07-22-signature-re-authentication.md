# ADR 2026-07-22: Signature Re-Authentication

## Status
Accepted

## Context
Pursuant to FDA 21 CFR Part 11 and EU Annex 11, applying electronic signatures to clinical or regulatory records (such as documents in eTMF or approved protocols in the Metadata Designer) requires explicit proof of active user re-authentication immediately prior to signing, regardless of whether a valid user session is already active.

## Decision
We enforce a secure, double-keying re-authentication flow upon performing any e-signature operation.
1. **Reusable Web Capture Component**: A centralized, reusable Vue 3 component (`apps/web/src/components/SignatureCaptureModal.vue`) is used to prompt the user for their username, password, and optional TOTP. Password and TOTP fields are immediately cleared upon cancel or submit to prevent credential leaks.
2. **Gateway-issued Step-up Signature Token**: The captured credentials are validated via the API Gateway, which issues a short-lived, 60-second, action-bound signature token (`X-Sig-Token`) containing a unique `jti` to prevent replay attacks.
3. **Downstream Certificate-bound Manifestation**: Downstream services (eTMF and Designer) parse the `X-Sig-Token` and generate a transient RSA private key and self-signed X.509 certificate on-the-fly to canonically sign the record's payload, producing a permanent, cryptographically-linked `SignatureManifestation` block persisted directly in the database.

## Alternatives Considered
- **Direct token reuse / Long-lived session tokens**: Rejected because they do not satisfy Part 11 requirements for active re-authentication immediately before applying a signature.
- **Transmitting raw credentials downstream**: Rejected due to high risk of credential exposure in microservice logs, databases, or transport layers.

## Trade-offs
- **Positive**: Complete 21 CFR Part 11 and GxP compliance. Absolute non-repudiation via certificate-bound signing. Minimal attack surface with 60-second token limits.
- **Negative**: Minor prompt interaction required for users immediately before performing signature actions.

This decision implements requirements under Trace-15, Trace-13, and PRD-SYS-001.
