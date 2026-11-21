# ADR-2476: Synchronous CDISC ODM v1.3.2 and USDM v4 Protocol Draft Import and Self-Service Study Registration

- **Status:** Accepted
- **Date:** 2026-11-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (signed reason for change and per-tenant audit trail), kernel spec K007, K-PROT-1, K-PROJ-1

---

## 1. Context & Problem Statement

[ADR-2472](2026-09-30-executable-protocol-design-document.md) established `ProtocolDesign` as the typed content of a `ProtocolVersion`, and [ADR-2473](2026-09-30-usdm-4-import-into-the-protocol-design.md) introduced `UsdmDesignImporter` in `packages/ddf` to parse CDISC USDM 4 JSON in memory. However, three gaps prevented study designers from onboarding studies or importing external study structures in the web workspace:

1. **No CDISC ODM v1.3.2 XML or bundled CDISC Library eCRF importer:** Sponsors migrating studies from CDISC ODM-XML v1.3.2 systems or assembling drafts from the 36 bundled CDISC Library eCRF templates (`docs/CDISC/Library/Data_Collection/eCRFs`) had no parser to translate `<FormDef>`, `<ItemGroupRef>`/`<ItemGroupDef>`, and `<ItemRef>`/`<ItemDef>` definitions into `ProtocolDesign` while preserving form names, item prompts, display order, mandatory flags, and import notes.
2. **No persisted Kernel HTTP routes for study creation or synchronous draft import:** `apps/designer` runs outside Core without PostgreSQL persistence (`ServiceTopology`), while `Cadence.Kernel.Api` exposed read-only protocol routes without endpoints to list visible tenant studies, register a study (`RegisterStudy`), or synchronously import an ODM/USDM definition into a `Draft` `ProtocolVersion` (`CreateProtocolDraft`, `ReviseProtocolDraft`, `ForkProtocolVersion`).
3. **Unrouted `/ecrf` upload client and missing study directory UI:** `StudyCanvasView.vue` required typing a study code from memory (with a `CAD-101` placeholder mismatching the seeded `CADENCE-101` demo study), and `DesignerSchemaPanel.vue` / `useSchemaIngestion.js` posted uploads to an unrouted `/api/v1/designer/ingestion/upload` path that triggered HTTP 404 errors and `window.alert` dialogs.

## 2. Decision Drivers & Constraints

- **Kernel Governance & Auditability (PRD-SYS-001, ADR-2449, ADR-2452):** Every study registration and protocol draft import must execute through `IKernelCommandSender` with study design authorization (`study.register`, `protocol.draft`, `protocol.revise_draft`, `protocol.amend`), a gateway-signed `X-Change-Reason`, and `Idempotency-Key` replay safety. Imported definitions must always enter `Draft` status and require formal review and electronic attestation before site activation.
- **Standards Boundary (K-PROJ-1):** `packages/kernel/src` must remain free of CDISC ODM/USDM parsing details and domain variable names; standards translation belongs in `packages/ddf` behind a pure kernel port (`IProtocolDesignImporter`).
- **Payload & XML Security:** Synchronous HTTP uploads must reject payloads exceeding 10 MB (`10,485,760` bytes) and prohibit XML DTD / external entity declarations (`DtdProcessing.Prohibit`, `XmlResolver = null`).
- **Lossless Fidelity & Readable Import Notes:** Item prompts (`CollectionItem.Prompt`), display order, mandatory flags, unmapped terminology bindings, and optional vendor attributes/extensions must be preserved on the draft summary (`ProtocolDesign.ImportNotes`) without requiring a database schema migration.

## 3. Options Considered

### Option 1: Synchronous Import via `IProtocolDesignImporter` on the Kernel API Host (Selected)

- **Overview:** Define `IProtocolDesignImporter` and `IStudyDirectory` in `Cadence.Kernel.Abstractions`; implement `OdmDesignImporter`, `CdiscLibraryTemplateCatalog` (covering all 36 bundled CDISC Library eCRF files), and `ProtocolDesignImporterService` in `Cadence.Ddf`; expose tenant-visible study/organization directory, `POST /api/v1/kernel/studies`, `GET /api/v1/kernel/protocol-templates`, `GET /api/v1/kernel/protocol-versions/{versionRef}/design-summary`, and `POST /api/v1/kernel/studies/{studyRef}/protocol-imports` on `Cadence.Kernel.Api`; and wire both `StudyCanvasView.vue` / `ProtocolImportPanel.vue` and `DesignerSchemaPanel.vue` / `useSchemaIngestion.js` to the kernel endpoints with inline banners and zero `window.alert` calls.
- **Pros:**
  - ✅ Uses the kernel's transactional command pipeline, per-tenant audit chain, capability checks, and `Idempotency-Key` guarantees directly.
  - ✅ Preserves K-PROJ-1 layering (`Cadence.Kernel.Api` depends only on `Cadence.Kernel.Abstractions`; `apps/kernel-api` composes `Cadence.Ddf`).
  - ✅ Eliminates unrouted `/api/v1/designer/ingestion/upload` 404s and supports one-step study creation + ODM v1.3.2 / USDM v4 import.
- **Cons:**
  - ❌ Caps synchronous HTTP uploads at 10 MB rather than streaming multi-hundred-megabyte archives asynchronously.

### Option 2: Route Uploads Through `apps/designer` with Cross-Service Calls to Kernel

- **Overview:** Add ODM parsing and multipart upload endpoints to `apps/designer` and forward translated designs over internal HTTP to `apps/kernel-api`.
- **Pros:**
  - ✅ Keeps `Cadence.Ddf` out of `apps/kernel-api`.
- **Cons:**
  - ❌ Splits study registration, concept resolution, and draft creation across two network hops without a single transactional or idempotent boundary, and requires new gateway routing and designer-to-kernel service credentials.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Executing synchronous ODM v1.3.2 XML, USDM v4 JSON, and bundled CDISC Library eCRF template imports on the kernel API host behind `IProtocolDesignImporter` keeps standards parsing isolated in `Cadence.Ddf` while guaranteeing transactional `Draft` creation, signed `X-Change-Reason` auditing, and deterministic `Idempotency-Key` replay.

## 5. Consequences & Trade-offs

- **Positive Impact:** Study designers can browse visible studies (including `CADENCE-101`), register new studies, and import ODM v1.3.2 XML, USDM v4 JSON, or any of the 36 bundled CDISC Library eCRF templates in one flow with inline validation feedback and readable import notes.
- **Negative Impact / Technical Debt:** Payloads larger than 10 MB are rejected with HTTP `413 Payload Too Large` on the synchronous import route.
- **Mitigation Strategy:** Both client file pickers and server endpoints enforce the 10 MB ceiling with actionable RFC 9457 `ProblemDetails` messages, and `ProtocolDesignDocument` stores optional `Prompt` and `ImportNotes` with `JsonIgnoreCondition.WhenWritingNull` so existing protocol content hashes remain unchanged.

## 6. Implementation & Verification

- **Affected Packages / Services:** `packages/kernel/src/Cadence.Kernel.Abstractions`, `packages/kernel/src/Cadence.Kernel.Persistence`, `packages/kernel/src/Cadence.Kernel.Api`, `packages/ddf`, `apps/kernel-api`, `apps/web`.
- **Verification Plan:** Validated by `OdmDesignImporterTests` (all 36 bundled CDISC Library eCRF files, DTD/XXE blocking, 10 MB limit, vendor/terminology import notes), `KernelStudyImportEndpointsTests` (visibility filtering, permissions, signed `X-Change-Reason`, `Idempotency-Key` replay, `Draft`-only status), `Cadence.ArchitectureTests` (`StandardsBoundaryTests`, `KernelLayeringTests`), and Vitest specs (`StudyCanvasImport.spec.js`, `EcrfSchemaImport.spec.js`).
