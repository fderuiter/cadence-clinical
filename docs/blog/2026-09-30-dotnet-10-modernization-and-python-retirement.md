---
title: "From Python Microservices to .NET 10 and Disciplined Python Retirement"
date: "2026-09-30"
authors:
  - "@fderuiter"
summary: "How we migrated Cadence's backend and developer tooling to .NET 10 / C# 14 with Native AOT quality gates and retired legacy Python sources without losing unported domain behavior."
tags:
  - "modernization"
  - "dotnet"
  - "python-retirement"
  - "quality-gates"
related:
  - text: "ADR-2492: Retire Python Reference Sources Once Their C# Ports Are Tested"
    link: "../adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md"
  - text: "Python Reference Inventory"
    link: "../PYTHON_REFERENCE_INVENTORY.md"
  - text: "Migration Workflow Playbook"
    link: "../MIGRATION_WORKFLOW.md"
  - text: "Unified Developer CLI Reference"
    link: "../cli.md"
draft: false
prev: false
next: false
---

# From Python Microservices to .NET 10 and Disciplined Python Retirement

In September 2026 we completed the platform port from Python (FastAPI, Pydantic, and SQLAlchemy) to **.NET 10 and C# 14** with ASP.NET Core Minimal APIs, EF Core 10 on PostgreSQL 18, and compile-time `System.Text.Json` / analyzers where warnings fail the build. Completing the runtime port, however, left a practical hygiene challenge: 148 Python files and 14 `pyproject.toml` manifests remained under `packages/` as behavioral references.

## Why Unmanaged Reference Files Hurt Velocity

Leaving legacy `.py` files scattered across active packages created three recurring problems:

- **Ambiguous source of truth:** Code search, reviewers, and automated coding agents encountered legacy role-string checks or Neo4j helpers that [Clinical Kernel Specification v0.2](../kernel/CLINICAL_KERNEL_SPECIFICATION.md) had already superseded.
- **Noisy static gates:** Duplication scanners, secret baselines, and dependency manifests kept auditing an inactive Python workspace.
- **Risk of silent behavioral loss:** Deleting every `.py` file indiscriminately would have buried unported edge cases—such as specific cryptographic signature verification paths or greenlight evaluators—deep in git history without an explicit checklist.

## Rule-Based Retirement Under ADR-2492

In [ADR-2492](../adr/2026-09-30-retire-python-reference-sources-once-ports-are-tested.md), we adopted a checkable per-file retirement rule rather than a blind sweep. A Python reference file is deleted immediately when any of three conditions holds:

1. Its C# port exists and automated tests verify its primary behavior.
2. The kernel specification or an accepted ADR explicitly supersedes the legacy behavior.
3. The file carries no domain behavior (such as `__init__.py` re-exports, legacy test harnesses, or `pyproject.toml` files).

Every surviving file is cataloged in [docs/PYTHON_REFERENCE_INVENTORY.md](../PYTHON_REFERENCE_INVENTORY.md) with its exact C# counterpart status and the exit condition required to delete it. Across the initial and consolidated passes, we removed every `pyproject.toml`, `__init__.py`, and tested port—narrowing the inventory down to 13 explicit `Port missing` reference files across `packages/ddf`, `packages/engine_core`, and `packages/security`.

## Native AOT Developer Tooling

Alongside the service port, we consolidated repository automation into the Native AOT `cadence` CLI documented in [docs/cli.md](../cli.md). Running `pnpm cadence check` executes 29 deterministic quality gates—spanning formatting, architecture boundaries, workflow and ruleset parity, OpenAPI and EF Core schema drift, de-identification scanning, requirement traceability, and documentation index parity—ensuring legacy shims never creep back into the codebase.
