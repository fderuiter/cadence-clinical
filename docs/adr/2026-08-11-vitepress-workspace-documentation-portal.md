# ADR-066: VitePress Workspace Documentation Portal

* **Status:** Accepted
* **Date:** 2026-08-11
* **Authors:** @jules
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement
Developers and compliance auditors are currently required to read raw, uncompiled markdown documentation files directly in the Git repository because the system lacks a unified web interface or a client-side search portal. This causes friction in onboarding developers and slows down the process of regulatory compliance auditing. We need a centralized, searchable documentation portal integrated directly into the workspace build pipeline.

## 2. Decision Drivers & Constraints
* **Driver 1:** Compile 100% of architectural decision records (ADRs) and SDLC compliance guidelines into clean web pages.
* **Driver 2:** Support instant client-side search without introducing external database or backend search runtimes.
* **Driver 3:** Integrate into the existing pnpm and JS monorepo workspace environment.
* **Driver 4:** Execute all pre-build automated verification scripts (link checking, ADR formatting, compliance compilation) before static compilation.
* **Driver 5:** Nest the built portal directly under the main web client's build output (`/cadence-clinical/docs/`).

## 3. Options Considered
### Option 1: Separate Docsy/Hugo Portal
* **Pros:** Standard documentation setup.
* **Cons:** Introduces redundant runtime environments (Go, Hugo) and misses out on JS workspace integration.

### Option 2: VitePress Workspace Documentation Portal
* **Pros:** Highly performant, uses Vite under the hood, registers cleanly as a workspace dependency, has native client-side search, compiles directly to static assets, and integrates seamlessly with Vite's route structure.
* **Cons:** Requires explicit configuration of VitePress base path and post-build directory structure.

## 4. Decision Outcome
* **Chosen Option:** Option 2
* **Justification:** Option 2 aligns perfectly with existing tooling (Vite, pnpm, node) and meets all guardrails and success criteria.

## 5. Consequences & Trade-offs
* **Positive Impact:**
  * Fast, interactive client-side search.
  * Centralized and polished view of SDLC guidelines, ADRs, and system design documents.
* **Negative Impact:**
  * Must ensure base paths and build copying are kept in sync during build pipeline updates.

## 6. Implementation & Verification
* **Affected Repositories / Services:**
  * Root `package.json`, `apps/web` build configurations, VitePress config files, and pre-build automation hooks.
* **Verification Plan:**
  * Verify by running local validations, compiling, and checking built assets under `apps/web/dist/[docs]/`.
