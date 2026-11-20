# ADR-2226: Programmatic VitePress API Documentation Engine

- **Status:** Accepted
- **Date:** 2026-08-28
- **Authors:** @fderuiter
- **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

The Cadence Clinical platform consists of 20+ specialized microservices and dozens of GxP-regulated endpoints covering protocol authoring, EDC runtime, CTMS, safety reporting, DICOM adjudication, and participant wallets. Previously, the static VitePress documentation portal lacked granular API endpoint documentation, forcing developers, site integrators, and regulatory auditors to inspect raw Python code or rely on live backend Swagger instances. We need an automated static analysis pipeline that programmatically inspects OpenAPI 3.1.0 specifications and FastAPI routes to generate comprehensive, searchable Markdown reference pages within the VitePress documentation portal.

This decision addresses `@req:PRD-SYS-001` and `@req:PRD-SYS-053`.

## 2. Decision Drivers & Constraints

- **Driver 1 (Automated Parity):** API documentation must be programmatically generated directly from OpenAPI 3.1.0 schemas and FastAPI route decorators without manual editing to guarantee zero documentation drift.
- **Driver 2 (VitePress Integration):** Generated documentation must output native Markdown files (`docs/api/*`) that seamlessly integrate with VitePress client-side local search, theme styling, and sidebar groupings.
- **Driver 3 (GxP Metadata Exposure):** Every documented endpoint must capture 21 CFR Part 11 security constraints, required OIDC role permissions, 60-second step-up signature re-auth requirements, and standardized RFC 7807 problem detail response models.
- **Driver 4 (Zero Runtime I/O):** Schema extraction and documentation generation must execute in pure static memory without requiring live database containers or network services.
- **Driver 5 (Multi-Language Code Samples):** Each endpoint reference must provide copy-ready client examples (TypeScript, Python, and cURL).

## 3. Options Considered

### Option 1: **Option 1 (Chosen): Native Static Markdown Generator (`scripts/generate_api_docs.py`)**

- **Overview:** Statically introspects each FastAPI application (`apps/*/main.py`), normalizes route definitions and Pydantic v2 schemas into structured Markdown files under `docs/api/`, and auto-registers them into VitePress sidebar groups.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 2: **Option 2: Embedded Interactive Swagger UI / Scalar Iframe**

- **Overview:** Embeds an interactive OpenAPI visualizer inside a single VitePress page. While interactive, it is opaque to VitePress's client-side text search and fails to index individual endpoints into search results.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

### Option 3: **Option 3: Manual Markdown API Documentation**

- **Overview:** Hand-crafting Markdown tables for each service. High maintenance overhead, prone to immediate drift, and violates CI quality gates.
- _**Pros:**_
  - ✅ Satisfies architectural requirements
- _**Cons:**_
  - ❌ Requires implementation and maintenance overhead

## 4. Decision Outcome

- **Chosen Option:** **Option 1 (Native Static Markdown Generator)** because it delivers fully searchable, deeply structured, and perpetually accurate API documentation across all 20+ microservices while integrating directly with the pre-build pipeline in `scripts/build-docs.js`.
- **Justification:** Selected as primary architectural approach.

## 5. Consequences & Trade-offs

- **Positive:**
  - 100% of microservice endpoints (20+ services) are documented with field-level schemas, constraints, and defaults.
  - Full client-side VitePress local search indexing for all routes, parameters, and error codes.
  - Complete 21 CFR Part 11 visibility: clear tagging of mutating endpoints requiring electronic signature step-up tokens.
- **Negative:**
  - Pre-build documentation step requires executing Python introspection (`uv run python scripts/generate_api_docs.py`) before running `vitepress build`.

## 6. Implementation & Verification

- **Generator Script:** `scripts/generate_api_docs.py` parses all `apps/*/main.py` entrypoints and generates `docs/api/*.md`.
- **VitePress Configuration:** `docs/.vitepress/config.mjs` incorporates `getApiSidebarGroups()` to dynamically catalog the 4 layered functional API domains.
- **Build Pipeline:** `scripts/build-docs.js` executes API doc generation as part of Step 1 pre-build validations.
- **Verification:** Validated via `uv run cadence check --parallel` and `pnpm run build:docs`.
