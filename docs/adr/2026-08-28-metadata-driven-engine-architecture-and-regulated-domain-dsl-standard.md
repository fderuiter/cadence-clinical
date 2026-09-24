# ADR-2228: Metadata-Driven Engine Architecture and Regulated Domain DSL Standard

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
* **Requirements:** `PRD-SYS-001`, `PRD-SYS-003`, `PRD-MDR-001`, `PRD-CRF-004`, `PRD-CRF-009`, `PRD-CRF-010`, `PRD-CRF-012`, `PRD-SYS-050`

---

## 1. Context & Problem Statement

Historically, clinical trial execution platforms rely on hardcoded Python code and custom scripts for critical domain logic: cross-form edit checks, visit window tolerances, CTCAE lab toxicity grading, randomization stratification, site payment milestone triggers, PV reporting countdowns, and SDTM ETL transforms. 

Every protocol-specific rule change or amendment currently requires code modifications, redeployments, and regression testing cycles. To eliminate hardcoding while maintaining strict compliance with **FDA 21 CFR Part 11**, **ICH GCP E6(R2)**, and **GAMP 5 Category 4 (Configured Software)**, the platform is transitioning to a **Metadata-Driven Engine Architecture** where domain logic is declared via versioned Domain-Specific Languages (DSLs) and evaluated by stateless runtime engines.

## 2. Decision Drivers & Constraints

* **Regulated Configuration as Data (21 CFR Part 11 § 11.10(e))**: In a GxP environment, moving logic from code to configuration means configurations are legally regulated records. Every DSL change must generate a new version index with full audit trails (`created_at`, `created_by`, `reason_for_change`, SHA-256 digest).
* **Non-Destructive Historical Retention & Version Pinning (`PRD-CRF-012`)**: Observations captured under earlier protocol amendments must permanently evaluate against their historical pinned DSL version (`Rule_v1`), while newly enrolled subjects evaluate against `Rule_v2`.
* **Zero-Side-Effect Stateless Evaluators**: Evaluator runtimes in Python must be sandboxed, deterministic, and pure functions without database or network I/O during evaluation.
* **The Engine Triad Architecture**:
  1. **The Builder (Frontend)**: Low-code Vue 3 interface with semantic design tokens (no Tailwind) for clinical data managers and study biostatisticians.
  2. **The Schema (Database)**: Strongly typed Pydantic v2 DSL schemas stored in PostgreSQL JSONB with optimistic locking and Merkle audit block integration.
  3. **The Evaluator (Backend)**: High-performance, sandboxed Python 3.14+ runtime ingesting the JSON DSL and live clinical data payloads.

## 3. Options Considered

### Option 1: **Option A (Selected) - The Standardized Engine Triad with Domain DSLs & Immutable Versioning**:

- **Overview:** - Establish a universal engine framework across all 7 functional domains (Core Platform, Execution EDC, RTSM, Financials/CTMS, Quality/Safety, Interop/ETL, AI Gateway).
   - Enforce the Builder-Schema-Evaluator triad with immutable version pinning and 21 CFR Part 11 electronic signature publication gates.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option B - Ad-Hoc Python Script Execution (Embedded Python/Lua Scripts)**:

- **Overview:** - Allow users to write raw Python or Lua scripts in the UI and execute them via `eval()` or `exec()`.
   - *Rejected*: Massive security vulnerability (RCE), impossible to statically validate, and non-compliant with GxP sandbox standards.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option C - Status Quo Code-Driven Hardcoding**:

- **Overview:** - Continue implementing study-specific rules in Python source code files.
   - *Rejected*: Inflexible, high operational cost per study amendment, and slow time-to-market.
- ***Pros:***
  - ✅ Satisfies architectural requirements
- ***Cons:***
  - ❌ Requires implementation and maintenance overhead


## 4. Decision Outcome

**Chosen Option:** **Option A**. 

All 7 clinical engine families (#4533 – #4539) must implement the standardized Engine Triad:
1. **Core Platform Engines (#4533)**: Universal Rules & Expression Engine (AST/CEL/JSONLogic), Finite State Machine DAG Lifecycle Engine, Event-Driven SLA Escalation Engine.
2. **Clinical Execution Engines (#4534)**: Schedule of Activities Vector Timeline Engine, Derived Variable Math Engine, CTCAE Lab Reference & Toxicity Grading Engine.
3. **RTSM & Supply Chain Engines (#4535)**: Stochastic Randomization Engine (BOIN, Minimization, Stratified Permuted Block), Predictive Supply & DTP Logistics Engine.
4. **Financial & Operations Engines (#4536)**: Event-Triggered Payable Ledger & Fee Matrix Engine, Site Startup (SSU) Greenlight Engine.
5. **Quality, Safety, & Risk Engines (#4537)**: PV Regulatory Reporting Decision Matrix Engine, RBQM / KRI Surveillance Engine, Statistical Forensics Engine (Benford's Law).
6. **Data Standards & Interoperability Engines (#4538)**: Semantic Schema Mapping & ETL Engine (Polars/Arrow), MedDRA / WHODrug Auto-Coding Engine.
7. **AI & Agent Orchestration Engine (#4539)**: Dynamic Prompt & Tool Registry Engine, Visual Clinical Agent DAG Pipeline Engine.

## 5. Consequences & Trade-offs

* **Positive**:
  - Eliminates developer PRs and deployments for protocol amendments and study configuration updates.
  - Full GxP traceability: all rule mutations are auditable, diffable, and version-pinned.
  - Unified developer and data manager experience across all 7 platform domains.
* **Negative**:
  - Requires maintaining DSL parsers and visual low-code builder interfaces.

## 6. Implementation & Verification

* **Core Packages & Services**:
  - `packages/ddf/` (shared DSL parsers and AST evaluators)
  - `apps/execution/`, `apps/payments/`, `apps/ctms/`, `apps/quality/`, `apps/safety/`, `apps/interop/`, `apps/ai_gateway/`
  - `apps/web/src/views/` (low-code builder workspaces)
* **Verification Suite**:
  - Unit and property-based tests for all DSL evaluators.
  - In-memory repository doubles testing version pinning and historical retention.
  - GxP compliance synced via `uv run python scripts/sync_gxp.py` for `@req:PRD-SYS-001`.

