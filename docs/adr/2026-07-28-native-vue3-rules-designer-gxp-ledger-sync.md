# ADR 2026-07-28: Native Vue 3 Rules Designer with GxP Ledger Sync

## Status
Accepted

## Context
The legacy rules designer relied on parallel entry scripts and static HTML. This setup caused critical race conditions during initialization, as Vue 3 would completely wipe out the static layout upon mounting. Additionally, this architecture disconnected the rules engine from secure routing, role-based access controls, live production endpoints, and compliant audit tracking.

To achieve 21 CFR Part 11 and GxP compliance, clinical designers must be able to securely author logical validation checks without risk of DOM-destruction errors or un-audited state changes.

This decision implements requirements under Trace-11.

## Decision
1. Re-implement the rules designer as a native, recursive Vue 3 component inside the SPA web application.
2. Integrate a Keycloak-aware role-verification guard with the router, restricting access to the STUDY_DESIGNER role.
3. Map the dynamic visual tree builder directly to backend Pydantic expression node schemas to ensure reliable serialization.
4. Enforce mandatory GxP justification capture via a blocking modal for all state mutations, coupled with cryptographic signing and ledger synchronization via addLedgerBlock().

## Alternatives Considered
* **Legacy Static HTML with Parallel Initialization Scripts:** Keeps code unchanged but fails on DOM stability, lacks Keycloak integration, secure client-side role guards, and dynamic ledger synchronization.
* **Non-integrated Vue Applet:** Keeps initialization separate but fails to leverage shared SPA store/session contexts and secure unified routing paths.

## Trade-offs
### Positive Impact
* Completely eliminates initialization blocks and DOM-destruction errors.
* Enforces defense-in-depth role access to study rules configurations.
* Fully compliant 21 CFR Part 11 and GxP audit trails (PRD-SYS-001) with cryptographic verification (PRD-SYS-003).

### Negative Impact / Technical Debt
* Increases compiled SPA bundle size and layout complexity of the web application.
