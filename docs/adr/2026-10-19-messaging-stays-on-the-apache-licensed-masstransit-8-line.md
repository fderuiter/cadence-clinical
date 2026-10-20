# ADR-2524: Messaging Stays on the Apache-Licensed MassTransit 8 Line

- **Status:** Accepted
- **Date:** 2026-10-19
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-053 (development guardrails: a dependency update must not change what the platform needs
  to start)
- **Related:** [ADR-2814](2026-10-07-standardize-masstransit-ef-core-transactional-outbox-across-core-services.md)

---

## 1. Context & Problem Statement

Execution, eTMF, eConsent and Notifications publish integration events through MassTransit and its EF Core
transactional outbox. Dependabot proposed MassTransit 9.2.3 (#5513), and it merged without a local CI run, because
GitHub Actions is off until 2027. MassTransit 9 is commercially licensed: a bus without `SetLicense`, `MT_LICENSE` or
`MT_LICENSE_PATH` fails configuration with "License must be specified". Every bus host then failed to start, and with
them the OpenAPI export, the integration tests and local development. MassTransit 8 stays under Apache-2.0.

## 2. Decision Drivers & Constraints

- **Open, reproducible stack:** anyone can clone, build and run the reference implementation with no paid key.
- **No secrets in the repository:** a license key cannot be committed, and none is provisioned.
- **Least change:** the outbox, consumers and migrations are written against MassTransit 8 and work today.

## 3. Options Considered

### Option 1: Stay on MassTransit 8 and stop major-version proposals (Selected)

- **Pros:**
  - ✅ No license, no new configuration, and no code changes.
  - ✅ Dependabot still proposes 8.x minor and patch releases.
- **Cons:**
  - ❌ MassTransit 8 gets no new features. Its support window must be reviewed before the 2027 production target.

### Option 2: Buy a MassTransit 9 license

- **Pros:**
  - ✅ The current major, with vendor support.
- **Cons:**
  - ❌ It costs money and is a business decision. It needs `MT_LICENSE` in every environment, CI and agent session.

### Option 3: Replace MassTransit

- **Pros:**
  - ✅ Removes the dependency (for example, Wolverine, or a hand-written outbox relay like the kernel's).
- **Cons:**
  - ❌ It rewrites the outbox and consumers of four services, and that belongs with the module fold-in (ADR-2610), not
    with a dependency update.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
  - `Directory.Packages.props` pins `MassTransit` and `MassTransit.EntityFrameworkCore` at 8.3.6.
  - `.github/dependabot.yml` ignores semver-major updates for `MassTransit*`.
- **Revisit:** before production, or when the fold-in (ADR-2610) replaces the per-service buses. Then choose between
  Options 2 and 3.

## 5. Consequences & Trade-offs

- **Positive Impact:** every bus host starts again without a license, and Dependabot cannot propose the break again.
- **Negative Impact:** MassTransit 8 gets no new features, and its end of support becomes a tracked risk.
- **Mitigation Strategy:** the fold-in (ADR-2610) revisits the per-service buses before the 2027 production target.

## 6. Implementation & Verification

- **Affected:** `Directory.Packages.props`, `.github/dependabot.yml`, `AGENTS.md`, `ARCHITECTURE.md`.
- **Verification:** the `auto-stage-openapi` gate boots every bus host to export its OpenAPI document, and the
  integration and E2E tests start the execution host with its bus; under MassTransit 9 without a license all of them
  fail at bus configuration.
