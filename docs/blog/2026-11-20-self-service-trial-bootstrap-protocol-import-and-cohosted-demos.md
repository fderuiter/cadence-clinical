---
title: "Self-Service Trial Bootstrap, Standards Import, and Co-Hosted Clinical Demos"
date: "2026-11-20T16:00:00Z"
authors:
  - "@fderuiter"
summary: >-
  How we opened Cadence for zero-friction evaluation and study onboarding—combining
  Keycloak PKCE self-registration, synchronous CDISC ODM v1.3.2 and USDM v4 draft
  imports, deterministic demo manifests, co-hosted GitHub Pages SPAs, and a focused
  kernel-backed eCRF workspace.
tags:
  - "kernel"
  - "standards"
  - "onboarding"
  - "edc"
  - "demo"
related:
  - text: "ADR-2547: Keycloak Self-Registration and Trial Bootstrap"
    link: "../adr/2026-11-20-keycloak-self-registration-and-clinical-kernel-trial-bootstrap.md"
  - text: "ADR-2476: Synchronous ODM v1.3.2 & USDM v4 Draft Import"
    link: "../adr/2026-11-20-synchronous-odm-usdm-protocol-draft-import.md"
  - text: "ADR-2549: Canonical Demo Manifest from Kernel Seed"
    link: "../adr/2026-11-20-export-canonical-demo-manifest-from-kernel-seed.md"
  - text: "ADR-2550: Co-Host Interactive Demos on GitHub Pages"
    link: "../adr/2026-11-20-co-host-interactive-demos-on-github-pages-subpaths.md"
  - text: "ADR-2819: Focus Active eCRF View on Backed Kernel Forms"
    link: "../adr/2026-11-20-focus-active-ecrf-view-on-backed-kernel-forms-outside-demo-mode.md"
  - text: "Companion: PostgreSQL RLS, IClock, and GxP Evidence"
    link: "./2026-11-20-postgres-rls-deterministic-iclock-and-gxp-release-evidence.md"
  - text: "Cadence Architecture"
    link: "../../ARCHITECTURE.md#1-target-architecture"
  - text: "Interactive Staff Demo"
    link: "/demo/"
draft: false
---

# Self-Service Trial Bootstrap, Standards Import, and Co-Hosted Clinical Demos

Evaluating a regulated clinical research platform should not require manual database seeding, bespoke identity provisioning, or installing an entire local container stack just to click through a study workflow. At the same time, opening self-service evaluation paths must never weaken fail-closed tenant isolation, 21 CFR Part 11 auditability, or the boundary between synthetic walkthroughs and backed clinical data capture.

On November 20, 2026, five architectural decisions landed together to connect public browser evaluation, self-service tenant onboarding, standards-driven study design, and focused case report form execution.

## Self-Service OIDC Registration Without Weakening Tenant Isolation

Previously, Keycloak sign-in was limited to pre-seeded realm accounts, and `KernelTenantFilter` rejected any authenticated caller whose token lacked an active `tenant_id` claim. Evaluators had no public `/plans` catalog to compare modular editions (`Individual`, `Select`, `Complete`) and no self-service route to bootstrap a trial workspace.

In [ADR-2547](../adr/2026-11-20-keycloak-self-registration-and-clinical-kernel-trial-bootstrap.md), we enabled Keycloak OIDC self-registration with PKCE `S256` and introduced an explicit onboarding path (`POST /api/v1/kernel/onboarding/trial`) executed through `IKernelCommandSender` under a dedicated platform operator (`svc:kernel-trial-onboarding`):

- **Commercial vs. clinical authority separation:** `Subscription` and `ProductEntitlement` are tenant-isolated kernel entities that govern workspace packaging and trial mutation boundaries only. They never grant study visibility or bypass `AuthorityEvaluator`, blinding, or participant consent.
- **Permanent regulated baseline:** Commands marked `[BaselineKernelCommand]`—including audit inspection, regulated exports, and electronic signatures (`Attest`)—remain active across every edition and even after a trial expires (`403 K.Subscription.Expired`).
- **Idempotent Keycloak writeback:** Provisioning derives deterministic UUIDv8 entity IDs per OIDC `sub`, writes `tenant_id` plus initial `Sponsor Admin` and `Sponsor Designer` roles back to Keycloak, and refreshes the client token so every subsequent kernel and standalone request passes fail-closed Row-Level Security.

## Synchronous CDISC ODM v1.3.2 and USDM v4 Protocol Draft Import

Once inside a study workspace, study designers need to register studies and import external protocol definitions without leaving the Clinical Canvas. Earlier slices parsed CDISC USDM v4 JSON in memory, but sponsors migrating from CDISC ODM v1.3.2 XML systems—or assembling forms from the 36 bundled CDISC Library eCRF templates—had no persisted kernel import path, and the legacy `/ecrf` upload client still targeted an unrouted designer path.

[ADR-2476](../adr/2026-11-20-synchronous-odm-usdm-protocol-draft-import.md) keeps standards parsing inside `Cadence.Ddf` behind a pure kernel port (`IProtocolDesignImporter`) while exposing synchronous study registration and protocol draft import endpoints directly on `Cadence.Kernel.Api`:

1. **Standards translation at the edge (`K-PROJ-1`):** `OdmDesignImporter`, `UsdmDesignImporter`, and `CdiscLibraryTemplateCatalog` translate `<FormDef>`, `<ItemGroupDef>`, `<ItemDef>`, mandatory flags, item prompts, and unmapped terminology notes into `ProtocolDesign` without leaking CDISC XML or JSON details into `packages/kernel/src`.
2. **Hardened synchronous ingestion:** Payloads over 10 MB return RFC 9457 `413 Payload Too Large` responses, XML DTD/XXE processing is prohibited (`DtdProcessing.Prohibit`, `XmlResolver = null`), and every import enters `Draft` status with a gateway-signed `X-Change-Reason` and `Idempotency-Key` replay safety.
3. **Unified study directory UI:** Both `StudyCanvasView.vue` and `ProtocolImportPanel.vue` now browse tenant-visible studies (including `CADENCE-101`) and surface structured import notes inline with zero `window.alert` calls.

## One Canonical Seed Across Local Stacks and Co-Hosted GitHub Pages Demos

For stakeholders evaluating Cadence before signing in, static demo builds of the staff workspace (`apps/web`) and participant portal (`apps/subject-portal`) previously drifted from the local Aspire/Docker stack: hand-written study IDs (`CAD-101`, `STUDY-USDM-001`) diverged from `CADENCE-101`, wall-clock timestamps produced different Genesis SHA-256 ledger hashes on every reload, and GitHub Pages published only Markdown documentation.

Two companion decisions unified our public demo architecture:

- **Canonical demo manifest ([ADR-2549](../adr/2026-11-20-export-canonical-demo-manifest-from-kernel-seed.md)):** `packages/demo-fixtures/demo-seed.json` is now the single source of truth for the `CADENCE-101` cardiovascular outcomes study, sites (`SITE-101`, `SITE-102`), screened participants, and eight synthetic role personas (`crc.site101` through `auditor.lead`). `pnpm cadence generate-demo-manifest` cross-checks `ExecutionDbSeeder.cs` and `docker/cadence-realm.json`, derives RFC 9562 UUIDv8 identifiers anchored at `2026-01-05T09:00:00Z`, and enforces zero drift in `pnpm cadence check demo-manifest`—while `scripts/vite-strip-demo-fixtures.mjs` strips synthetic fixtures out of non-demo production bundles.
- **Co-hosted subpath SPAs ([ADR-2550](../adr/2026-11-20-co-host-interactive-demos-on-github-pages-subpaths.md)):** Superseding ADR-071, `deploy-docs.yml` stages the interactive staff demo at `/cadence-clinical/demo/` and the participant portal at `/cadence-clinical/subject-portal/` inside one SHA-256 checksummed, SLSA-attested Pages artifact alongside the staged repository [`README.md`](../../README.md). Demo builds switch to `createWebHashHistory` so deep links survive browser refreshes on static hosting while production builds retain `createWebHistory` for OIDC PKCE callbacks.

## Focusing `/ecrf` on Backed Kernel Forms Outside Demo Mode

Finally, separating demo evaluation from production clinical work let us clean up the active data-entry surface. Outside demo mode, `/ecrf` previously mounted five unbacked prototype panels next to `EcrfFormPanel.vue` and asked coordinators to paste raw enrollment UUIDs.

Under [ADR-2819](../adr/2026-11-20-focus-active-ecrf-view-on-backed-kernel-forms-outside-demo-mode.md), non-demo builds render only backed clinical controls on `/ecrf`: the authenticated role badge, `StudySubjectsBar`, automatic participant-to-enrollment resolution via `findKernelEnrollmentRef`, Clinical Canvas query handoffs that preserve `enrollmentId`, and `EcrfFormPanel.vue` route-leave guards for unsaved edits. Demo builds (`VITE_DEMO_MODE=true`) keep guided persona walkthroughs while replacing blocking alerts with accessible toast notifications.
