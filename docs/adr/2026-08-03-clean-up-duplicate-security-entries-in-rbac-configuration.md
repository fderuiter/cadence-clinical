# ADR-255: Clean up duplicate security entries in rbac configuration

- **Status:** Accepted
- **Date:** 2026-08-03
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

In previous development iterations, the RBAC configuration map `ROLE_PERMISSIONS` in `packages/security/rbac.py` accumulated duplicate dictionary keys for several roles (such as duplicate `"soa"` keys). In Python, duplicate keys in a dictionary literal are overwritten, but they create static analysis noise, linter warnings, and confusion during security audits.

## 2. Decision Drivers & Constraints

- Ensure clean static analysis and zero linter warnings.
- Maintain complete backward compatibility of the centralized permission system.
- Business/GxP requirement (PRD-SYS-001)

## 3. Options Considered

1. Clean up duplicate entries directly by removing duplicated keys and standardizing formatting (Selected).
2. Maintain duplicate keys with static analysis suppression comments (Rejected - leads to architectural debt).

## 4. Decision Outcome

Chosen option: Option 1 because it removes redundant duplicate entries directly and simplifies security configuration audits while ensuring complete maintainability of RBAC rules.

## 5. Consequences & Trade-offs

- Positive: Clear operational boundaries, simplified audits, and error-free static analysis.
- Negative: Requires updating the codebase's permission dictionary structure.

## 6. Implementation & Verification

- Target files/packages modified: `packages/security/rbac.py`.
- Verification: Verified that all unit tests and permission checks pass without issue.
