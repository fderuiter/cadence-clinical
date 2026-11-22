# ADR-2624: Outbox Collection Edit Checks, Stateless Pre-Commit Form Preview, and ODM Rule Compilation

- **Status:** Accepted
- **Date:** 2026-11-21
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (signed reason for change and per-tenant audit trail), PRD-SYS-192 (explicit route authorization), kernel spec K-RULE-3, K-FIND-1, K-FACT-1, K-XS-2

---

## 1. Context & Problem Statement

[ADR-2620](2026-10-15-ecrf-correction-transaction-boundary.md), [ADR-2622](2026-10-19-ecrf-forms-are-collection-instances-saved-in-one-command.md), and [ADR-2623](2026-10-19-observation-values-are-typed.md) established typed `Observation` values, `CollectionInstance` eCRF forms, and `FormQuery` discrepancies, while `Cadence.Rules` implemented deterministic `RulePack` evaluation and `FindingLedger.Derive` (`K-FIND-1`). However, four gaps kept edit checks disconnected from clinical data capture and protocol imports:

1. **ODM v1.3.2 edit checks were skipped on import:** `OdmDesignImporter` recorded skipped notes for `<RangeCheck>` and `<ConditionDef>` instead of compiling them into governed `RulePack` library items (`LibraryItemKind.RulePack`).
2. **No pre-commit preview or post-commit outbox evaluation:** Saving eCRF values neither previewed pinned study `RulePack` warnings in memory before submission nor triggered post-commit `RecordValidationRun` and automated `FormQuery` opening/closure through the transactional outbox (`K-RULE-3`).
3. **`CorrectObservation` bypassed `CollectionItem.Admits` on `NotAsked` items:** Correcting a blank `NotAsked` observation created for a missing-data query did not enforce the governing `CollectionItem`'s expected `ObservationValueKind`, UCUM `Unit`, or `CodeSystemVersionId`, and `GET /api/v1/kernel/observations/{observationRef}` omitted expected item metadata.
4. **Clinical Canvas query links and action buttons drifted from context and grants:** Query links navigated away from `/studies/:studyCode`, and eCRF sign/query-answer controls did not reflect per-enrollment `collection.sign` and `execution.query.answer` capabilities.

## 2. Decision Drivers & Constraints

- **Post-Commit Rule Invariant (`K-RULE-3`):** Out-of-range and cross-form clinical discrepancies must never block saving or correcting structurally valid clinical observations; only structural/type admissibility (`CollectionItem.Admits`), concurrency versions, and locks may refuse a write.
- **Strict Module Layering (`K-XS-2`):** `Cadence.Modules.Execution` may depend only on `Cadence.Kernel.Abstractions` and `Cadence.Kernel.Modules`, never directly on `Cadence.Rules` or `Cadence.Ddf`.
- **Deterministic Outbox Idempotency (`K-FIND-1`):** Replaying `CollectedValuesSaved` or `ObservationCorrected` outbox deliveries must never create duplicate `ValidationRun`s or duplicate `FormQuery` rows, and resolving a finding must close only the automated query raised for that finding while leaving human queries untouched.

## 3. Options Considered

### Option 1: Kernel Validation Port + Outbox Consumer + Stateless Preview Endpoint (Selected)

- **Overview:** Introduce `ICollectionValidationService` in `Cadence.Kernel.Abstractions` implemented by `CollectionValidationService` in `Cadence.Rules`; compile ODM v1.3.2 `<RangeCheck>` and `<ConditionDef>` (`OdmFormalExpressionParser`) into `ImportedRulePack` JSON drafted as a `RulePack` library item on import; expose stateless `POST /api/v1/execution/forms/{formId}/preview` and `/api/v1/kernel/studies/{studyRef}/rule-packs` + `/validation/findings` endpoints; register `EvaluateEditChecksOnCollectionChanged` (`svc:execution-edit-checks`) on `CollectedValuesSaved` and `ObservationCorrected`; enforce `CollectionItem.Admits` in `CorrectObservationHandler`; and wire `ValidationSection.vue`, inline form warnings, in-place query navigation, and capability-gated controls in `apps/web`.
- **Pros:**
  - ✅ Preserves `K-RULE-3` (all structurally admissible facts commit; discrepancies surface pre-commit as stateless warnings and post-commit as auditable findings + automated queries).
  - ✅ Preserves `K-XS-2` module isolation and `K-ERR-1` OpenAPI error declarations.
  - ✅ Guarantees deterministic UUIDv8 query and run idempotency across outbox replays.
- **Cons:**
  - ❌ Evaluates study collection records per outbox batch rather than incrementally indexing cross-form expressions.

### Option 2: Synchronous Inline Edit-Check Blocking Inside `RecordCollectedValuesHandler`

- **Overview:** Evaluate pinned `RulePack`s inside `RecordCollectedValuesHandler` and reject saves when `Hard` rules trigger.
- **Pros:**
  - ✅ Simpler synchronous control flow without an outbox consumer.
- **Cons:**
  - ❌ Directly violates `K-RULE-3` by refusing to record observed clinical facts when a participant's vital sign or lab value falls outside normal protocol bounds.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Separating stateless pre-commit preview (`PreviewFormAsync`) from post-commit outbox validation (`EvaluateEditChecksOnCollectionChanged`) satisfies `K-RULE-3`, `K-FIND-1`, and `K-XS-2` while giving site staff immediate inline feedback before saving and automated discrepancy query lifecycle management after commit.

## 5. Consequences & Trade-offs

- **Positive Impact:** ODM v1.3.2 imports automatically draft executable `RulePack`s; designers and data managers can inspect, approve, pin, and dry-run rule packs in Clinical Canvas (`Data -> Validation`); site staff see inline pre-commit warnings and expected UCUM units when correcting `NotAsked` items; and automated discrepancy queries open and close deterministically via the kernel outbox.
- **Negative Impact / Technical Debt:** Study-wide collection validation loads all opened `CollectionInstanceRecord`s for the study so `FindingLedger.Derive` covers every active dataset without falsely resolving sibling enrollments' findings.
- **Mitigation Strategy:** `ICollectionInstanceRecords.ListForStudyAsync` batches form and observation reads in two indexed queries ordered deterministically by `(OpenedAt, Id)`.

## 6. Implementation & Verification

- **Affected Packages / Services:** `packages/kernel`, `packages/ddf`, `apps/execution/module`, `apps/kernel-api`, `apps/web`, `packages/demo-fixtures`, `docs/openapi`, `docs/api`.
- **Verification Plan:** Verified by `OdmRuleImportTests`, `ExecutionOutboxEditChecksPostgresTests`, `KernelErrorContractTests`, `RouteAuthorizationTests`, `Cadence.ArchitectureTests`, and `ValidationAndPreview.spec.ts`.
