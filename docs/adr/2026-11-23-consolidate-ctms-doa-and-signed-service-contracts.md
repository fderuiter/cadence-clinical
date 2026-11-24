# ADR-2670: Consolidate CTMS Delegation of Authority Ownership and Signed Cross-Service GxP Contracts

- **Status:** Accepted
- **Date:** 2026-11-23
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-192, PRD-SSU-001, PRD-SSU-002, PRD-SSU-003

## 1. Context & Problem Statement

Delegation of Authority (DoA) state, site activation greenlight readiness, protocol training completion, monitoring visit letters, and investigational product (IP) kit batch reconciliation previously spanned split ownership and inconsistent signing boundaries across the clinical platform:

1. Unseeded sites returned `404 Not Found` on `GET /api/v1/ctms/startup/sites/{siteId}/greenlight` instead of evaluating live six-facet readiness across CTMS, eTMF, Knowledge, and Execution.
2. Knowledge maintained standalone delegation assumptions while CTMS held operational site delegation records without signed cross-service headers or canonical eTMF Zone 5 (`05.02.04`) filing.
3. Monitoring visit letter finalization relied on cross-DbContext reflection instead of signed HTTP ingestion to eTMF (`05.02.06`) and regenerated PDF bytes on download rather than serving immutable stored bytes matching `PdfSha256Hash`.
4. Several GxP-critical mutations lacked uniform single-use 60-second `X-Sig-Token` enforcement under 21 CFR Part 11 and EU Annex 11, and Keycloak lacked explicit `Principal Investigator` and `Pharmacist` realm roles.

## 2. Decision Drivers & Constraints

- **21 CFR Part 11 & EU Annex 11 Non-Repudiation:** Every binding electronic signature must re-verify caller credentials and consume a single-use 60-second `X-Sig-Token` cryptographically bound to the caller, method, path, semantic action, and study scope.
- **Single Canonical Operational Owner:** Delegation of Authority logs are study-, site-, and role-scoped operational artifacts owned by CTMS, queried and endorsed by Knowledge via signed service-to-service HTTP contracts rather than shared schemas (`K-XS-1`, `K-XS-2`).
- **Byte-Identical Document Auditability:** Signed monitoring visit follow-up letters must persist exact compiled PDF bytes so downloads always hash identically to `PdfSha256Hash`.
- **Least-Privilege Separation of Duties:** Principal Investigators sign site DoA logs, Monitors/CRAs conduct and sign monitoring visits and letters, Sponsor Data Managers certify greenlight gates, and Unblinded Pharmacists reconcile IP kit batches.

## 3. Options Considered

### Option A: Shared Database Access or Reflection Across CTMS, Knowledge, and eTMF

Rejected because cross-module DbContext access violates `K-XS-1` and `K-XS-2` service isolation, bypasses gateway HMAC authentication and audit logging, and breaks independent deployment.

### Option B: Canonical CTMS DoA Ownership with Gateway-Signed Cross-Service HTTP Contracts and Uniform `X-Sig-Token` Enforcement (Selected)

Selected because `GatewayIdentity.ToSignedHeaders` provides authenticated, tenant- and scope-preserving service-to-service transport across CTMS, Knowledge, eTMF, and Execution while `ISigTokenUseStore` guarantees replay-proof step-up signatures.

## 4. Decision Outcome

1. **Six-Facet Greenlight Readiness & Signed Certification:** `GET /api/v1/ctms/startup/sites/{siteId}/greenlight` initializes and evaluates all six startup facets (`REGULATORY_IRB`, `CTA_BUDGET`, `EISF_BASELINE`, `DOA_LOG`, `STAFF_TRAINING`, `IP_RELEASE`) via signed service queries without returning `404` for authorized sites. Single-site and country-batch certifications enforce single-use `X-Sig-Token` verification.
2. **CTMS as Canonical DoA Operational Owner:** CTMS owns study-, site-, and role-scoped DoA records (`PENDING_PI_SIGNATURE`, `ACTIVE`, `REVOKED`) with explicit staff metadata and duties. PI sign-off (`POST /api/v1/ctms/doa/sign-off`) enforces `X-Sig-Token` and files signed DoA logs (`05.02.04`) to eTMF Zone 5 via signed HTTP requests (`ICtmsEtmfFilingClient`). Knowledge queries and endorses CTMS delegations over signed `GatewayIdentity` service headers (`ICtmsDelegationClient`).
3. **Training Curricula, Module Delivery & Signed Completion:** Knowledge exposes curriculum creation (`POST /api/v1/training/curricula`) with persisted module manifests, nested curriculum payloads on assignments, and authenticated trainee completion (`POST /api/v1/training/assignments/{assignment_id}/complete`). Web training consoles bind caller identity strictly from `authStore.user`.
4. **Immutable Visit Letter PDFs & Findings:** Monitoring visits return persisted findings, finalized visit letters file `05.02.06` artifacts to eTMF via `ICtmsEtmfFilingClient`, and `GET /api/v1/ctms/letters/{letterId}/pdf` serves exact persisted `PdfBytes` matching `PdfSha256Hash` gated on `ctms_monitoring_letter:read`.
5. **Uniform Single-Use `X-Sig-Token` & Realm RBAC:** Greenlight certification, DoA sign-off, finalized visit letter compilation (`Finalize == true`), monitoring visit sign-off, and IP kit batch reconciliation (`POST /api/v1/execution/rtsm/kits/reconcile-batch`) enforce single-use 60-second `X-Sig-Token` tokens bound to verified caller claims. `Principal Investigator` (`pi.site101`) and `Pharmacist` (`pharmacist.site101`) are provisioned in Keycloak and `PermissionMatrix`.

## 5. Consequences & Trade-offs

- **Positive:** Eliminates split DoA truth and cross-service reflection, guarantees byte-identical PDF audit hashes, and enforces uniform 21 CFR Part 11 / EU Annex 11 non-repudiation across all five GxP signing workflows.
- **Negative / Trade-offs:** Cross-service startup readiness and eTMF filing depend on signed service-to-service HTTP availability (`ETMF_SERVICE_URL`, `KNOWLEDGE_SERVICE_URL`, `EXECUTION_SERVICE_URL`, `CTMS_SERVICE_URL`), mitigated by explicit fallback handling and durable signature replay tracking.

## 6. Implementation & Verification

- `apps/ctms/src/Endpoints/StartupGreenlightEndpoints.cs`, `DoaEndpoints.cs`, `MonitoringVisitEndpoints.cs`, `LetterTemplateEndpoints.cs`
- `apps/ctms/src/Services/CtmsGreenlightReadinessEvaluator.cs`, `CtmsEtmfFilingClient.cs`
- `apps/knowledge/src/Endpoints/TrainingEndpoints.cs`, `Application/CtmsDelegationClient.cs`
- `apps/etmf/src/Endpoints/EtmfIntelligenceEndpoints.cs`
- `apps/execution/src/Endpoints/IpKitEndpoints.cs`
- `packages/security/src/Rbac/PermissionMatrix*.cs`, `docker/cadence-realm.json`
- Qualified by automated `.NET` xUnit endpoint/architecture suites, PostgreSQL migration checks, OpenAPI client generation, and Vue Vitest component specs.
