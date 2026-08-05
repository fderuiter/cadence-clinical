# ADR-256: Implement Git-checked static exemption ledger & bypass validation

- **Status:** Accepted
- **Date:** 2026-08-05
- **Authors:** @google-labs-jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To prevent unauthorized, silent bypasses of static security analysis (e.g. using comments like `# nosec` or `pragma: allowlist`), we need an auditable and reviewable process. Developers previously could bypass scans locally or inline without peer oversight.

## 2. Decision Drivers & Constraints

- Ensure compliance with standard audit logging requirements under **PRD-SYS-001**.
- Facilitate reviewable, git-auditable exemptions.
- Maintain developer velocity with high performance (<2-second SLA).

## 3. Options Considered

### Option 1: Ad-hoc inline bypass comments without a centralized ledger

- **Overview:** Allow standard inline exclusions silently.
- **Pros:**
  - ✅ Simple.
- **Cons:**
  - ❌ Non-auditable.
  - ❌ Violates **PRD-SYS-001** and GxP standards.

### Option 2: Centralized Git-checked JSON ledger with automated verification (Selected)

- **Overview:** Maintain all exemptions in `security_exemptions.json` with justification and verify them in hooks and CI.
- **Pros:**
  - ✅ Audit trail.
  - ✅ Enforced peer review during PR.
  - ✅ Blocks unauthorized bypasses.
- **Cons:**
  - ❌ Slight overhead for adding exemptions.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Centralizing exemptions in a git-checked registry (`security_exemptions.json`) satisfies **PRD-SYS-001** by enforcing strict peer review and auditable justification, completely closing the loop on silent security bypasses.

## 5. Consequences & Trade-offs

- **Positive Impact:** Full auditability and safety.
- **Negative Impact / Technical Debt:** Small overhead to register bypasses.
- **Mitigation Strategy:** Provide friendly, actionable errors with instructions on how to add entries.

## 6. Implementation & Verification

- **Affected Repositories / Services:** Entire clinical platform codebase, specifically `scripts/audit_security.py` and `tests/test_compliance_security.py`.
- **Verification Plan:** Validated with custom unit tests verifying ledger resolution and pattern matches, as well as pre-commit hooks.
