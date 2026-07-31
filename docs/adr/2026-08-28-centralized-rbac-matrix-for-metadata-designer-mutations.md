# ADR-120: Centralized RBAC Matrix for Metadata Designer Mutations

* **Status:** Accepted
* **Date:** 2026-08-28
* **Authors:** @jules
* **Deciders:** @lead-architect, @qa-validator

---

## 1. Context & Problem Statement
The Metadata Designer (MDR/SDR) microservice exposes several critical operations, including Global Library object authoring, Biomedical Concept management, controlled terminology cache clearing, and clinical protocol exports. Previously, these mutation families were protected by ad-hoc, hardcoded role allowlists and had inconsistent resource-to-action permission boundaries. To guarantee strict regulatory compliance under FDA 21 CFR Part 11 and ensure robust role-based access control (RBAC), a centralized, declarative, and granular permission mapping matrix must be defined and enforced uniformly.

This ADR specifically relates to requirements under **PRD-SYS-001**.

## 2. Decision Drivers & Constraints
* **Driver 1:** Enforcing the principle of least privilege across all user personas.
* **Driver 2:** Compliance with FDA 21 CFR Part 11 and GxP audit trail regulations.
* **Driver 3:** High availability and programmatic testability of authorization logic.
* **Constraint:** Must reuse the existing unified central gateway and rbac packages without introducing breaking dependencies.

## 3. Options Considered
### Option 1: Ad-hoc Hardcoded Role Gates in Endpoints
* **Overview:** Keep using `get_normalized_roles` and check specific role list membership directly in each route handler.
* **Pros:**
  * ✅ Extremely quick to implement locally for individual endpoints.
* **Cons:**
  * ❌ Leads to policy fragmentation and high maintenance overhead.
  * ❌ Difficult to audit or keep consistent with SDLC spec sheets.

### Option 2: Declarative Resource-Action Permision Gate with Role mapping [Selected]
* **Overview:** Expand the centralized `ROLE_PERMISSIONS` matrix in `packages/security/rbac.py` with granular resource keys and actions:
  * `global_library` (actions: `create`, `update`, `amend`, `transition`, `instantiate`, `read`)
  * `mdr_concept` (actions: `create`, `update`, `rename`, `delete`, `read`)
  * `protocol_export` (actions: `generate`, `read`)
  * `designer_cache` (action: `admin`)
  * `study_design` (actions extended with `approve` for atomic study version sign-offs)
* **Pros:**
  * ✅ Single, auditable source of truth for all platform-wide permissions.
  * ✅ Clear separation of concerns between roles and granular permissions.
  * ✅ Simplifies automated integration testing of positive/negative authorization paths.
* **Cons:**
  * ❌ Requires upfront effort to map all roles and write comprehensive matrix assertions.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 completely centralizes role-to-permission mappings into a single declarative file, allowing transparent auditing, robust programmatic testing, and robust regulatory compliance with Least Privilege guidelines.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * ✅ Programmatically testable security boundaries for all developer roles.
  * ✅ 1:1 mapping with the SDLC Spec sheets.
  * ✅ Clearer authorization failure reasonings returned as HTTP 403 Forbidden.
* **Negative Impact / Technical Debt:**
  * ❌ Minor configuration expansion overhead when adding new resources.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * `packages/security/rbac.py` (extended permission matrix configurations)
  * `tests/test_designer_rbac.py` (added unit tests asserting matrix permissions across roles)
* **Verification Plan:**
  * Run pytest: `uv run --extra dev pytest tests/test_designer_rbac.py`
