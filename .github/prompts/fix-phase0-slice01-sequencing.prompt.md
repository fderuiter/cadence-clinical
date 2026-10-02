# Fix Prompt B — Resolve the Execution↔Designer Same-Tier Cycle (MIG-03 / Gate 4 Remediation)

## Role & Execution Protocol

You are a Principal Systems Architect responsible for closing an open sequencing decision in the Phase 0 migration plan before Phase 1 (Slice 01) work is staffed. This is a **decision-and-documentation task with a small, tightly-scoped contract-freeze deliverable** — it is not a request to transcribe Execution or Designer's business logic to .NET. Do not start writing domain code for either service.

**Source of truth for this task:**

- `docs/migration/phase0/00_validation_gate_report.md` §4 ("Gate 4 — Dependency Graph & Topological Sort Audit"), which independently confirmed the finding below.
- `docs/migration/phase0/02_bounded_contexts.md` §4.2 ("Circular dependencies"), which first identified it.
- `docs/migration/phase0/03_migration_plan.md`, blocker table entry **MIG-03** and the SLICE-01/SLICE-02 rows.

## The problem, restated precisely

`Cadence.Apps.Execution` (SLICE-01) and `Cadence.Apps.Designer` (SLICE-02) are both scheduled in the same tier of the migration plan, and each has a live runtime HTTP dependency on the other:

- Execution → Designer: `apps/execution/designer_client.py:31` (`os.getenv("DESIGNER_URL")`), consumed by `apps/execution/eligibility_service.py` to fetch eligibility criteria for a study.
- Designer → Execution: `apps/designer/presentation/routers/designer_routes.py:2618` (`os.getenv("EXECUTION_SERVICE_URL")`), part of the protocol-amendment publish flow.

Neither service is a true leaf. `03_migration_plan.md` currently names SLICE-01 as the lead Phase 1 slice without this cycle being resolved — MIG-03 is listed as an _open_ blocker, not a decision. That is the gap this prompt closes.

## Step 1 — Re-verify the finding still holds

Before deciding anything, confirm the coupling is still live and check whether either edge has grown new call sites since the audit:

```bash
rg -n "DESIGNER_URL" apps/execution --type py
rg -n "EXECUTION_SERVICE_URL|EXECUTION_URL" apps/designer --type py
```

Read both call sites in full (`apps/execution/designer_client.py`, `apps/execution/eligibility_service.py`, `apps/designer/presentation/routers/designer_routes.py` around line 2618) to understand exactly what data crosses each edge — you need this to scope the contract freeze in Step 3.

## Step 2 — Make the sequencing decision

Choose one of the two options `03_migration_plan.md` already names, or propose a third if neither fits once you've read the actual call sites:

- **(a) Parallel migration with contract-first coordination.** Both Execution and Designer are staffed as Slice 01 and Slice 02 together (not sequentially), with both directions' HTTP contracts frozen in `Cadence.Contracts` _before_ either team starts writing service code, so each side can build against a stable interface without waiting on the other's implementation.
- **(b) Explicit sub-sequencing.** One of the two ships first, with the other side stubbed/mocked for the duration (e.g. Execution ships first, with a stub Designer client that returns a fixed eligibility-criteria fixture until real Designer is live; or vice versa). If you choose this, you must justify _which_ one goes first — the validation report notes Execution has "the highest fan-in/fan-out of any context" and downstream Tier 3 orchestrators are sequenced behind it, which is a real argument for shipping it first, but the eligibility-criteria dependency on Designer is a real argument against — weigh both explicitly rather than picking by default.

Do not leave this as "either is fine" — the deliverable is a specific, justified choice a Phase 1 team can act on without re-litigating it.

## Step 3 — Freeze the crossing contracts

Whichever option you choose, both directions' HTTP contracts need to exist as concrete C# types in `packages/contracts/src` (`Cadence.Contracts`), so that whichever side is built first (or both, in parallel) has something stable to code against:

- The Execution→Designer eligibility-criteria contract: base it on the existing Python DTOs in `apps/execution/domain/acl/designer_eligibility_dto.py` (`DesignerExpressionNodeDTO`, `DesignerEligibilityCriterionDTO`) — these already function as an anti-corruption layer and are a reasonable starting shape for the C# contract, not a from-scratch design.
- The Designer→Execution protocol-amendment-publish contract: derive it from the actual request/response shape at `apps/designer/presentation/routers/designer_routes.py:2618` and whatever Execution endpoint it calls.

Keep these as contract/DTO types only (records/interfaces in `Cadence.Contracts`), consistent with Phase 0's existing scope boundary of "project skeletons and shared contracts, not business logic transcription." Do not implement the calling or serving logic in `Cadence.Apps.Execution`/`Cadence.Apps.Designer` — that's Phase 1 work.

## Step 4 — Update the planning documents

- `docs/migration/phase0/03_migration_plan.md`: update the MIG-03 row from an open question to a recorded decision (what was chosen, why, and what it implies for staffing). Update the SLICE-01 and SLICE-02 rows' "Blockers" column to reflect the resolution instead of the open question.
- `docs/migration/phase0/02_bounded_contexts.md` §4.2: add a short note that this cycle now has a recorded resolution, with a pointer to the updated `03_migration_plan.md` section — don't duplicate the full reasoning in two places.
- `docs/migration/phase0/00_validation_gate_report.md`: append a short dated addendum noting Gate 4's Slice-01-leaf-test finding has been addressed, with a pointer to the updated plan section. Do not edit the original audit findings — append, don't rewrite history.

## Step 5 — Verify no other same-tier cycles were missed

While you're in this area, re-run the same check the audit used, to confirm Execution↔Designer is genuinely the only same-tier cycle (the audit separately found Execution↔CTMS and Execution↔Etmf, but classified those as lower-risk because they span tiers — confirm that classification still holds and hasn't silently become same-tier due to any plan changes since the audit):

```bash
# For every pair of apps/<X> and apps/<Y>, look for X calling Y's *_URL and Y calling X's *_URL
rg -n "os\.getenv\(\"[A-Z_]+_URL\"" apps --type py -g '!*/tests/*'
```

Cross-reference the tier assignment of any bidirectional pair you find against `03_migration_plan.md`'s Phase/Slice table.

## Deliverable

- An explicit, justified MIG-03 decision recorded in `03_migration_plan.md`.
- Frozen contract types for both directions of the Execution↔Designer edge in `packages/contracts/src`, with the projects still building (run `dotnet build Cadence.sln` — or whatever the build state is after Fix Prompt A lands — to confirm the new contract types compile cleanly and don't introduce new warnings).
- Updated cross-references in `02_bounded_contexts.md` and an addendum to `00_validation_gate_report.md`.
- No business logic added to `Cadence.Apps.Execution` or `Cadence.Apps.Designer` beyond what's needed to reference the new contract types, if anything.
