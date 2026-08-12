# ADR-123: Decommissioning Client-Side TS Wrapper SDK and Enforcing Frontend Dependency Gates

- **Status:** Accepted
- **Date:** 2026-08-11
- **Authors:** @jules
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

To comply with GxP regulatory requirements and 21 CFR Part 11 standards (PRD-SYS-001), electronic signature generation and verification must occur strictly within secure backend microservices. Allowing client-side execution or verification of public-key / asymmetric signatures introduces severe security risks, including the potential for local-state bypass, client-side certificate store manipulation, and exposure of key verification paths in the Javascript runtime.

Historically, a client-side TypeScript Wrapper SDK (`packages/ui/sdk.ts`) was proposed to handle signature generation and validation checks in frontend packages. This approach violates the core system requirement of strict backend isolation for cryptographic operations. We need to decommission the legacy client-side TS Wrapper SDK and establish a robust, fail-fast dependency gating mechanism in the monorepo to guarantee that no asymmetric cryptographic packages are introduced into frontend application bundles.

## 2. Decision Drivers & Constraints

- **Driver 1:** Enforce absolute isolation of public-key / asymmetric signature logic inside secure Python backend microservices.
- **Driver 2:** Prevent local/client-side bypass attempts on signature verification to satisfy FDA 21 CFR Part 11 regulations.
- **Driver 3:** Implement automated, fail-fast CI checks to prevent developers from pulling asymmetric crypto npm packages into the frontend packages.

## 3. Options Considered

### Option 1: Keep Client-Side TS SDK with Strict Review Guidelines

- **Overview:** Maintain `packages/ui/sdk.ts` and rely on code review processes to ensure that developers do not introduce client-side verification vulnerabilities or local bypass logic.
- **Pros:**
  - ✅ Keeps a unified frontend utility class for developers.
- **Cons:**
  - ❌ Violates regulatory compliance requirements by allowing signature validation logic to exist in the JS runtime.
  - ❌ Highly prone to human error and difficult to audit.

### Option 2: Decommission Client-Side TS SDK and Implement Dependency Linter

- **Overview:** Completely delete the frontend TS SDK Wrapper and its corresponding tests, and implement a custom workspace dependency analyzer (`scripts/validate_dependencies.py`) that scans all `package.json` configurations in the monorepo and rejects forbidden asymmetric cryptographic npm packages.
- **Pros:**
  - ✅ Guarantees that all cryptographic verification occurs on the secure backend.
  - ✅ Fail-fast CI/CD gates automatically block the introduction of untrusted client-side crypto packages.
  - ✅ Ensures strict GxP and Part 11 compliance.
- **Cons:**
  - ❌ Requires client code to interface directly with secure backend signature endpoints rather than a helper class.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Choosing Option 2 guarantees compliance with PRD-SYS-001 by completely eliminating the client-side TS wrapper SDK and enforcing an automated gate to block any client-side public-key / asymmetric cryptographic dependencies.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Secure-by-design architecture with clear separation of concerns (backend-only cryptography).
  - Reduced bundle size on the frontend due to the deletion of unnecessary helper logic.
  - Automated compliance verification in the build pipeline.
- **Negative Impact / Technical Debt:**
  - Frontend services must make direct fetch calls to backend signature/verify routes, which is slightly more verbose but much more secure.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `packages/ui/sdk.ts` (deleted)
  - `packages/ui/tests/sdk.test.ts` (deleted)
  - `packages/ui/index.js` (updated exports)
  - `scripts/validate_dependencies.py` (new dependency validation linter)
  - `scripts/tests/test_validate_dependencies.py` (new tests)
- **Verification Plan:**
  - Run `uv run python scripts/validate_dependencies.py` to verify workspace compliance.
  - Execute `pytest scripts/tests/test_validate_dependencies.py` to ensure correct package detection.
