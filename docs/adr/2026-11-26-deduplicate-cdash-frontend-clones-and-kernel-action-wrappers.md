# ADR-2691: Split Generated CDASH Variable Union, Enforce Intra-File Frontend Clone Detection, and Delegate Lazy Kernel Action Wrappers

- **Status:** Accepted
- **Date:** 2026-11-26
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-CRF-015, PRD-SYS-001

---

## 1. Context & Problem Statement

Across `packages/ui`, `packages/tooling_core`, `apps/web`, and `apps/subject-portal`, four structural duplication patterns had accumulated:

1. **Duplicate Static CDASH Interfaces & Scanner Exemption:** `packages/ui/scripts/generate-types.js` appended a second copy of `CDASHModelVariable`, `CDASHIGVariable`, and `CdashField` plus the generated `CdashVariableCode` union into `packages/ui/src/types/cdash.ts`, requiring a hardcoded `/packages/ui/src/types/cdash.ts` path exemption in `DuplicationDetectorService.IsTargetFile`.
2. **Unchecked Intra-File JS/TS/Vue Clones:** `DuplicationDetectorService.DetectDuplication` skipped `prev.File == filepath`, allowing 24 JavaScript, TypeScript, and Vue source files to accumulate intra-file 15-line normalized clones across templates, Pinia stores, router redirects, SOP catalogs, and offline sync runners.
3. **Sub-15-Line Cross-File Helpers & Study Workspace Chrome:** Vite `manualChunks` / `theme-bootstrap.js` wiring in `apps/web/vite.config.js` and `apps/subject-portal/vite.config.js`, overlay props/lifecycle hooks in `ClinicalDrawer.vue` and `ClinicalModal.vue`, document `formatDate` helpers across four eTMF/fileshare modals and drawers, consolidated study headers in `DataLockView.vue` and `ExportWizardView.vue`, and PIN modal focus-trap/escape guards in `PinSetupModal.vue` and `PinUnlockModal.vue` were maintained in parallel copies.
4. **Hand-Repeated Lazy Kernel Action Wrappers:** All 50 `execute*` wrappers across the 8 slice modules in `apps/web/src/api/kernel/` repeated manual parameter and return type annotations alongside `const mod = await loadProtocolImport(); return mod.executeX(...);`.

## 2. Decision Drivers & Constraints

- **Preserve CDASH Key Formatting (`ADR-328`) & Generated File Hygiene (`PRD-CRF-015`):** Static CDASH metadata interfaces in `packages/ui/src/types/cdash.ts` must keep their exact quoted property keys protected by `.prettierignore`, while generated TypeScript unions must be Prettier-compliant and marked `linguist-generated=true` in `.gitattributes`.
- **Fail-Closed Duplication Guardrails Without C#/CSS False Positives (`PRD-SYS-001`):** `DuplicationDetectorService` must enforce both cross-file and intra-file 15-line clone detection on `.js`, `.jsx`, `.mjs`, `.cjs`, `.ts`, `.tsx`, `.mts`, `.cts`, and `.vue` files with zero exemptions for `cdash.ts`, while keeping `.cs` and `.css` on cross-file-only scanning.
- **Preserve Cold-Boot Code-Splitting & Live Consumer Reachability (`ADR-2677`):** Lazy Kernel action delegation in `apps/web/src/api/kernel/shared.ts` must use `import type` (`typeof import("./protocolImport")`) so `protocolImport.ts` remains out of the eager cold-boot bundle (`cold_boot_bundle.spec.ts`), and `packages/ui` internal helpers must not introduce unexported `.vue` or `src/composables/` files (`ui_consumer_reachability.test.js`).

## 3. Options Considered

### Option 1: Dedicated `generated/cdashVariables.ts` Module, Same-File JS/TS/Vue Clone Enforcement, Shared Primitives, and Type-Derived `lazyKernelAction` (Selected)

- **Overview:** Emit Prettier-formatted `CdashVariableCode` to `packages/ui/src/generated/cdashVariables.ts` (`linguist-generated=true`) and re-export it from `packages/ui/src/types/cdash.ts` while retaining each static CDASH interface once; remove the `cdash.ts` exemption and enable intra-file 15-line clone detection for JS/TS/Vue extensions in `DuplicationDetectorService`; refactor all 24 intra-file clone files; extract shared helpers in `scripts/vite-hoist-csp.mjs`, `packages/ui/src/components/clinical/sharedProps.js`, `apps/web/src/utils/formatDate.ts`, `ConsolidatedStudyHeader.vue`, and `usePinModalGuard`; and introduce `lazyKernelAction<K>` in `apps/web/src/api/kernel/shared.ts` deriving `Parameters<ProtocolImportModule[K]>` and `ReturnType<ProtocolImportModule[K]>` across all 50 `execute*` wrappers.
- **Pros:**
  - Eliminates duplicate CDASH interface declarations and removes the `/packages/ui/src/types/cdash.ts` scanner exemption while preserving `ADR-328` quoted keys.
  - Prevents intra-file 15-line JS/TS/Vue clones from returning via `pnpm cadence check duplication` without destabilizing C# or CSS files.
  - Keeps `protocolImport.ts` behind a dynamic `import("./protocolImport")` boundary while removing 50 hand-copied function signatures.
- **Cons:**
  - Adds a generated module path (`packages/ui/src/generated/cdashVariables.ts`) produced by `packages/ui/scripts/generate-types.js` prior to `ui` builds and typechecks.

### Option 2: Expand `.duplication-baseline.txt` or Keep Path Exemptions

- **Overview:** Record intra-file clones in `.duplication-baseline.txt` and retain the `cdash.ts` scanner exclusion.
- **Pros:**
  - Requires no source refactoring.
- **Cons:**
  - Violates shrink-only baseline governance (`ADR-2832`) and leaves duplicated type definitions, UI logic, and API wrappers susceptible to drift.

## 4. Decision Outcome

- **Chosen Option:** Option 1
- **Justification:** Separating generated CDASH union output from static `ADR-328` interfaces, tightening `DuplicationDetectorService` to enforce intra-file JS/TS/Vue clone detection with zero `cdash.ts` exemption, consolidating shared Vite/UI/study-header/PIN helpers, and typing `lazyKernelAction` from `typeof import("./protocolImport")` eliminates existing clones and structurally prevents regressions.

## 5. Consequences & Trade-offs

- **Positive Impact:** `packages/ui/src/types/cdash.ts` defines each static CDASH interface once and re-exports `CdashVariableCode` from `packages/ui/src/generated/cdashVariables.ts`; `pnpm cadence check duplication` passes with 0 clone windows across both cross-file and intra-file JS/TS/Vue checks; shared Vite, overlay, date-formatting, study-header, and PIN-modal helpers have a single source of truth; and all 50 Kernel `execute*` wrappers delegate through `lazyKernelAction`.
- **Negative Impact / Technical Debt:** `packages/ui/scripts/generate-types.js` now writes both `packages/ui/index.d.ts` and `packages/ui/src/generated/cdashVariables.ts`, requiring `pnpm --filter ui build` whenever `ALL_CDASH_DOMAINS` changes.
- **Mitigation Strategy:** `packages/ui/tests/cdash-types.test.js`, `DomainEngineUnitTests`, `cold_boot_bundle.spec.ts`, `chunkSplitAndBoot.spec.ts`, and `pnpm cadence check duplication` continuously verify generated union parity, intra-file clone enforcement, and lazy chunk boundaries in CI and pre-commit gates.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/ui`, `packages/tooling_core`, `packages/cli`, `scripts/vite-hoist-csp.mjs`, `apps/web`, `apps/subject-portal`, `docs/.vitepress`, `eslint.config.mjs`, `tests/Cadence.UnitTests`, `docs/adr/`, `docs/DOCUMENTATION_INDEX.md`.
- **Verification Plan:** Verified via `pnpm --filter ui build`, `pnpm --filter ui test`, `pnpm --filter ui typecheck`, `pnpm --filter subject-portal test`, `pnpm --filter web typecheck`, `pnpm --filter web test`, `dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj`, and `pnpm cadence check duplication adrs no-emojis eslint prettier`.
