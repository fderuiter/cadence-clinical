# ADR 2026-07-22: Signature Re-Authentication

## Status
Accepted

## Context
Pursuant to 21 CFR Part 11.50 (Signature Manifestation) and Part 11.200 (Signature Requirements), clinical actions requiring non-repudiation (e.g., PI form sign-offs, emergency unblinding) cannot rely solely on standard web session tokens. Double-keying re-authentication is mandated to definitively verify user intent.

## Decision
We will enforce a re-authentication gate strategy for critical clinical actions. Users must provide their active credentials (e.g., password and TOTP) to a dedicated `/api/v1/auth/signature-verification` endpoint, which issues a one-time cryptographic `sig_token` bound to the subsequent transactional payload.

## Alternatives Considered
- Re-using standard JWT session tokens: Rejected as it violates regulatory mandates for explicit re-authentication at the moment of electronic signing.
- Biometric-only prompts: Rejected due to platform-agnostic rollout requirements, though WebAuthn may be explored as a supplementary factor later.

## Trade-offs
- **Positive:** Achieves full compliance with FDA non-repudiation mandates; provides a legally binding signature manifestation tied specifically to an explicit action and timestamp.
- **Negative:** Increased user friction during high-stress workflows; requires additional frontend logic to handle asynchronous verification challenges.

## Traceability
| **Reference** | **Description** |
| :--- | :--- |
| `CAD-SDLC-SEC-005` (05_Security_Compliance_Audit_Spec.md) | Security, Compliance & Audit Trail Spec - Section 7.2 (Re-Authentication Gate Challenge) |
