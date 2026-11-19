# ADR-2603: Keep Cadence in One Repository

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** @fderuiter
- **Issues:** #5310, #5699
- **Requirements:** PRD-SYS-001
- **Partially supersedes:** [ADR-2602](2026-10-28-multi-repository-ownership-and-kernel-package-boundaries.md), for source repository topology and cross-repository extraction planning

## 1. Context & Problem Statement

Cadence is in active development with no deployment or live clinical records. The prior plan initialized nine destination repositories while application implementation, the composition host, and system verification remained in `cadence-clinical`. Current delivery work crosses the kernel, study build, capture, and other domains in one integrated workflow. The project needs a source layout that supports that work and keeps module ownership clear.

## 2. Decision Drivers & Constraints

- Prove clinical integrity and the reference workflow on a single, reproducible source revision.
- Keep module dependencies, owned data, and clinical transaction boundaries enforceable.
- Keep review, integration, and verification together while shared contracts are evolving.

## 3. Options Considered

| Option                                                  | Effect                                                                                                                                               |
| ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------- |
| Single source repository and modular monolith           | One integrated change can update a public contract, its consumers, and its tests together. Architecture tests enforce boundaries within the tree.    |
| Separate product source repositories with a shared host | Independent source ownership and publishing would be possible, with version compatibility, provenance, and release coordination across repositories. |

## 4. Decision Outcome

Keep `fderuiter/cadence-clinical` as the active repository for the kernel, product modules, workspace, and release verification. The project plan governs in-repository work. Treat the former repository migration plan and extraction inventory as historical. Any future source separation requires a new decision based on a concrete ownership, access, or independent release need and verified module boundaries.

[ADR-2601](2026-10-19-one-composition-host-for-the-kernel-and-its-modules.md) and [ADR-2610](2026-10-15-scaffold-services-become-modules-merge-or-are-deleted.md) govern composition and service disposition. [ADR-2600](2026-10-19-modules-write-through-the-kernel-command-path.md) governs clinical transactions. Keep ADR-2602's kernel API boundaries, single authoritative clinical state, and accepted domain ownership decisions. Build modules against the supported kernel abstractions and module SDK in this repository. Use architecture tests, versioned external contracts, and system tests to prove the boundaries.

## 5. Consequences & Trade-offs

Contributors can use one integration branch and reference fixture. Shared checks may run across multiple modules. A single source tree makes architecture checks and explicit ownership important. Product packaging and entitlements are commercial design decisions that can use the same runtime and codebase.

## 6. Implementation & Verification

Use the [project plan](../PROJECT_PLAN.md), [goals and priorities](../GOALS_AND_PRIORITIES.md), and [five-gates epic](https://github.com/fderuiter/cadence-clinical/issues/5310) to sequence implementation. Verify module boundaries, transaction behavior, and the reference journey within `cadence-clinical`. The development and evidence gates apply to this runtime at its tested revision.
