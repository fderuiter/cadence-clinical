---
title: "Keeping Cadence in One Repository: Consolidating on a Modular Monolith"
date: "2026-11-19"
authors:
  - "@fderuiter"
summary:
  "Why ADR-2603 ended our multi-repository split plan so kernel contracts,
  application modules, and end-to-end GxP evidence evolve atomically in one
  repository."
tags:
  - "architecture"
  - "modular-monolith"
  - "kernel"
  - "quality-gates"
related:
  - text: "ADR-2603: Keep Cadence in One Repository"
    link: "../adr/2026-11-19-keep-cadence-in-one-repository.md"
  - text: "ADR-2601: One Composition Host for the Kernel and Its Modules"
    link: "../adr/2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md"
  - text: "ADR-2600: Modules Write Through the Kernel Command Path"
    link: "../adr/2026-10-19-modules-write-through-the-kernel-command-path.md"
  - text: "Project Plan"
    link: "../PROJECT_PLAN.md"
draft: false
---

# Keeping Cadence in One Repository: Consolidating on a Modular Monolith

As Cadence's module catalog grew across study design, EDC capture, CTMS, eTMF,
safety, and intelligence, we previously explored splitting the codebase into ten
separate product repositories consuming published kernel packages. Before
executing that split, we audited where real delivery work was happening: every
end-to-end clinical slice spanned the kernel, study build, capture, and
qualification suites simultaneously.

In [ADR-2603](../adr/2026-11-19-keep-cadence-in-one-repository.md), we formally
retired the multi-repository extraction program and committed to developing,
integrating, and qualifying Cadence in one repository
(`fderuiter/cadence-clinical`).

## Why Premature Repository Splits Slow Regulated Software

Splitting an actively evolving pre-release platform across nine satellite
repositories introduces friction without adding architectural safety:

- **Cross-repo contract tax:** Evolving a kernel abstraction or USDM compiler
  contract would require sequenced package releases, version bumps, and multi-PR
  choreography before a single integration test could verify the change.
- **Fragmented qualification evidence:** Under 21 CFR Part 11 and GAMP 5,
  proving clinical integrity from protocol version activation through database
  lock is strongest when one immutable commit SHA pins the kernel, every module,
  the schema, and the test suite together.
- **Commercial packaging is not source topology:** Product editions and tenant
  entitlements can be licensed cleanly over one composition host
  ([ADR-2601](../adr/2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md))
  without duplicating CI pipelines or splitting git history.

## Enforcing Strict Boundaries Inside One Tree

Keeping one repository does not mean tolerating tangled dependencies. Module
isolation is enforced mechanically on every change:

1. **SDK-only module dependencies (`K-XS-2`):** Application modules reference
   only `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules`—never kernel
   `Domain`, `Application`, `Persistence`, or `Api` internals
   ([ADR-2600](../adr/2026-10-19-modules-write-through-the-kernel-command-path.md)).
2. **Single composition host:** Legacy scaffold microservices are dispositioned
   into in-process modules, merged, or retired under
   [ADR-2610](../adr/2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md)
   and hosted together in `apps/kernel-api`.
3. **Automated architecture sentinels:** ArchUnitNET suites and
   `pnpm cadence check` verify import boundaries, OpenAPI client types, EF Core
   schema documentation, and GxP traceability on a single branch before
   merge—keeping velocity high and boundaries crisp, as tracked in our
   [Project Plan](../PROJECT_PLAN.md) and
   [Goals and Priorities](../GOALS_AND_PRIORITIES.md).
