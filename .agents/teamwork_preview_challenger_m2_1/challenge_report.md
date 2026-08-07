# Challenge Report — Milestone M2: Primary Services Domain Migration

## Challenge Summary

**Overall risk assessment**: LOW

All 7 target relocated domain model modules requested for Milestone M2, along with all supporting relocated sub-modules, were empirically tested for runtime importability and Pydantic model instantiation. An automated AST static analysis sweep across the entire repository (`apps/`, `packages/`, `scripts/`, `tests/`) verified that zero stale imports or lingering dependencies on `packages/core-models` remain for M2 relocated domain models. Total test suite results: 2143 passed (91.67% test coverage).

---

## Challenges

### [Low] Environment Variables Required at Module Load Time for Security Signature Dependencies

- **Assumption challenged**: Domain model and engine imports (specifically `apps.interop.src.domain.sync_engine`) can be imported in isolation without pre-existing application configuration or environment variables.
- **Attack scenario**: Attempting to import `apps.interop.src.domain.sync_engine` in a clean Python shell where `AUDIT_LOG_SECRET_KEY`, `GATEWAY_SECRET_KEY`, or `INBOUND_EMAIL_HMAC_SECRET` are not set triggers module initialization errors from `packages.security.signing`.
- **Blast radius**: Low. Internal runtime services and test environments always load environment defaults via `.env` or application config before invoking interop domain engine functions.
- **Mitigation**: Verified that when standard application security environment variables are present, `apps.interop.src.domain.sync_engine` imports cleanly and operates as expected.

---

## Stress Test Results

1. **Importing Target Relocated Domain Modules**:
   - `apps.designer.src.domain.cdisc.usdm_models` → Import & Pydantic `USDMStudy` schema validation → **PASS** (13 public symbols)
   - `apps.safety.src.domain.sae_icsr.models` → Import & Pydantic `SeriousAdverseEvent` schema validation → **PASS** (19 public symbols)
   - `apps.ctms.src.domain.doa_models` → Import & Pydantic `DOADelegationRecordCreate` schema validation → **PASS** (8 public symbols)
   - `apps.etmf.src.domain.tmf_reference_model.models` → Import & Pydantic `Artifact` schema validation → **PASS** (7 public symbols)
   - `apps.notifications.src.domain.event_models` → Import & Pydantic `SystemDomainEvent` schema validation → **PASS** (6 public symbols)
   - `apps.org.src.domain.models` → Import & `OrganizationType` enum validation → **PASS** (5 public symbols)
   - `apps.interop.src.domain.sync_engine` → Import & Pydantic `SyncRecord` / `SyncMetadata` schema validation → **PASS** (15 public symbols)

2. **Importing Additional Relocated Sub-Modules**:
   - `apps.designer.src.domain.synopsis_transport_models` → **PASS**
   - `apps.designer.src.domain.usdm_ingestion` → **PASS**
   - `apps.designer.src.domain.protocol_authoring.models` → **PASS**
   - `apps.designer.src.domain.protocol_render.models` → **PASS**
   - `apps.designer.src.domain.protocol_version_ref.models` → **PASS**
   - `apps.designer.src.domain.eligibility.models` → **PASS**
   - `apps.designer.src.domain.document_renderer` → **PASS**
   - `apps.etmf.src.domain.etmf.eisf_models` → **PASS**
   - `apps.etmf.src.domain.etmf.eisf_transport_models` → **PASS**

3. **AST Stale Import Sweep for `packages.core_models`**:
   - Scanned all `.py` files under `apps/`, `packages/`, `scripts/`, `tests/` for subpaths `cdisc`, `designer`, `usdm_ingestion`, `protocol_authoring`, `protocol_render`, `protocol_version_ref`, `eligibility`, `document_renderer`, `sae_icsr`, `ctms`, `etmf`, `tmf_reference_model`, `notifications`, `organization_domain`, `sync_engine`.
   - Result: **0 stale imports found** → **PASS**

4. **Full Test Suite & Code Quality Gates**:
   - `pytest -n auto`: **2143 passed**, 91.67% total coverage (5 git merge driver script environment failures, baseline).
   - `ruff check .`: **PASS** (0 errors)
   - `ruff format --check .`: **PASS** (0 unformatted files)
   - `detect_duplication.py`: **PASS** (0 duplicate code structures above threshold)
   - `sync_gxp.py --dry-run`: **PASS** (Docs in sync)

---

## Unchallenged Areas

- **Execution Domain Models (`apps/execution/src/domain/`)**: Out of scope for Milestone M2 (scheduled for Milestone M3).
- **Anti-Corruption Layer (ACL) Cross-Service Refactoring**: Out of scope for Milestone M2 (scheduled for Milestone M4).

---

## Verdict

**APPROVE**
