# ADR-112: Developer-Centric Architecture Alignment and Automated Drift Gating

- **Status:** Accepted
- **Date:** 2026-08-01
- **Authors:** @jules
- **Deciders:** @engineering_leads, @qa_lead

---

## 1. Context & Problem Statement

The local runtime development environment uses 16 active services mapped via Docker Compose, including a variety of relational database configurations, graph databases, and local file-based SQLite boundaries. However, existing developer architecture diagrams only mapped 3 of these active services. This gap results in considerable confusion during onboarding, masks the modular microservices architecture, and risks presenting dynamic SQLite local instances as clustered production equivalents.

To eliminate this onboarding friction and prevent future divergence, we need to update the developer guides and technical design documents to accurately represent all 16 active services, while establishing an automated gating validation framework to block pull request merges if service definitions or architecture diagrams diverge.

This decision implements requirements under **Trace-16** and **Trace-7**.

## 2. Decision Drivers & Constraints

- **Driver 1:** Frictionless onboarding for new developers.
- **Driver 2:** High fidelity of system documentation compared to the running orchestrator.
- **Driver 3:** Continuous integration safety and prevention of future documentation drift.
- **Constraint:** No infrastructure migrations or runtime database changes are permitted in local development. Local file SQLite boundaries and in-memory key-value stores must be accurately contrasted with production topologies.

## 3. Options Considered

### Option 1: Manual Documentation Updates and Peer Review

- **Overview:** Rely strictly on developer discipline and peer-review checks to ensure documentation and diagrams match compose configurations.
- **Pros:**
  - ✅ No scripting overhead.
- **Cons:**
  - ❌ Highly error-prone and vulnerable to developer oversight.
  - ❌ Does not prevent documentation drift in continuous integration.

### Option 2: Automated Validation Gating via a Custom Python Linter

- **Overview:** Update ARCHITECTURE.md and the Technical Design Document with a dedicated local topology section, and add a custom static validation script that extracts actual services from `docker-compose.yml` and cross-references them against standard Mermaid diagrams inside our documentation.
- **Pros:**
  - ✅ Eradicates manual validation errors and completely blocks drift.
  - ✅ Instantaneous execution in pre-commit hooks and CI/CD pipelines (<0.1 seconds).
  - ✅ Requires no external heavy dependencies, ensuring portability.
- **Cons:**
  - ❌ Requires maintaining a lightweight parsing script.

## 4. Decision Outcome

- **Chosen Option:** Option 2
- **Justification:** Implementing an automated linter prevents drift by acting as a dynamic quality gate. It ensures that 100% of the active local services are represented correctly inside of standard Markdown-integrated Mermaid diagrams in both ARCHITECTURE.md and docs/SDLC/02_Technical_Design_Document_TDD.md.

## 5. Consequences & Trade-offs

- **Positive Impact:** New developers can confidently inspect the local topology, running exactly the 16 active services without hidden structures.
- **Negative Impact / Technical Debt:** Future service additions in the local orchestrator will require a corresponding update to the Mermaid diagrams to pass the integration validation.
- **Mitigation Strategy:** The gating linter's failure message is highly descriptive, explicitly listing any omitted services and providing immediate remediation guidance.

## 6. Implementation & Verification

- **Affected Repositories / Services:**
  - `docker/docker-compose.yml` (Source of truth)
  - `ARCHITECTURE.md` (Updated)
  - `docs/SDLC/02_Technical_Design_Document_TDD.md` (Updated)
  - `scripts/validate_architecture_drift.py` (Created)
  - `.pre-commit-config.yaml` (Updated)
  - `package.json` (Updated)
- **Verification Plan:**
  - Execute `uv run python scripts/validate_architecture_drift.py` locally and as part of `pnpm check`.
  - Verify that omitting a service from either document's Mermaid diagram triggers a clean exit code `1` failure, and successful representation returns `0`.
