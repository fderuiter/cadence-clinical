# ADR-2640: Randomisation Allocates from Approved, Encrypted Scheme Versions

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter
- **Deciders:** @fderuiter (approved the regroup plan that assigned the audit defects, 2026-10-20)
- **Issue:** #5607 (regulatory audit epic #5611)
- **Requirements:** PRD-SUB-001 (consent verified before randomisation), PRD-SUB-003 (stratified block randomisation,
  encrypted at rest, locked allocation), PRD-SUB-004 (minimisation, high-entropy random source), PRD-SSU-003 (no
  randomisation at a non-greenlit site, with an audit entry and 403), ICH E6(R3) 2.8 and 4.x (blinding,
  randomisation procedures), 21 CFR 11.10(e)
- **Extends:** ADR-2425 (randomisation and trial supply, decided for the retired Python service), ADR-2516 (route
  permissions)

---

## 1. Context & Problem Statement

The regulatory audit (#5611) found that Execution could not randomise a participant. `BlockRandomizationEngine` and
`MinimizationEngine` existed in `packages/engine_core` but nothing called them. A participant became `RANDOMIZED`
through the generic state-change route, and the arm arrived in the create request (`arm_id`) and was stored on the
subject as `TreatmentGroupCiphertext`, which held clear text despite its name. There was no approved scheme, no list,
no record of which slot a participant took, no consent or site-activation gate, and no link between the arm and the
kit handed to the participant.

## 2. Decision Drivers & Constraints

- **Blinding (ICH E6(R3), PRD-MDR-006):** blinded site staff randomise, so nothing they can read (the response, the
  subject record, the audit trail) may carry the arm. Only an unblinded caller may see it.
- **Allocation concealment:** the next arm must not be predictable from the database or the API, so the list is
  generated from a cryptographic source and stored encrypted.
- **Separation of duties:** a scheme goes live only when a second person approves it, and an approved scheme cannot be
  edited.
- **Integrity:** an allocation, once made, is never rewritten; each list slot is consumed at most once, even under
  concurrent randomisations across sites (PRD-SUB-003 asks for a lock).
- **Gates:** randomisation needs an enrolled participant, a current signed consent (PRD-SUB-001) and an activated site
  (PRD-SSU-003); a refusal at the gate is audited.
- **Pre-deployment:** there are no production records, so the clear-text column can be dropped without back-compat.
- **Kernel spec §7.4:** an arm assignment is an RTSM Assertion with a blinding class, and the kernel Participant
  holds no arm. Subjects are not yet in the kernel host, so this ADR stays in Execution and keeps the arm off the
  subject, ready to move.

## 3. Options Considered

### Option 1: Keep the arm on the subject and gate the state change

- ✅ Smallest change.
- ❌ The subject row is read by every blinded route; the arm sits next to data blinded staff read.
- ❌ No scheme, list or slot record, so there is no allocation evidence for an inspector.

### Option 2: Versioned schemes, an encrypted list and an insert-only allocation in Execution (Selected)

- ✅ Uses the existing engines; adds the scheme, list and allocation records the audit asked for.
- ✅ Keeps the arm out of the subject entirely, which is the shape spec §7.4 needs.
- ❌ A second encryption key to provision.

### Option 3: Build the kernel RTSM Assertion first

- ✅ The target shape.
- ❌ Blocked on moving subjects into the kernel host; the defect would stay open meanwhile.

## 4. Decision Outcome

**Chosen option: Option 2.**

- **Scheme versions.** `randomization_schemes` holds one row per study version: the method (`permuted_block` or
  `minimization`), arms and ratios, stratification factors and levels, block sizes, slots per stratum, the
  minimisation probability and whether a kit is needed. The configuration is hashed (SHA-256 over a canonical form)
  when drafted. `POST /api/v1/execution/randomization/schemes` drafts the next version and
  `POST /{schemeId}/approve` approves it. The approver must differ from the author and the hash must still match. At
  most one version per study is approved (partial unique index); approving a new one retires the previous one.
- **The list.** Approving a permuted-block version generates the whole list: per stratum, blocks drawn from the
  declared sizes and permuted with `RandomNumberGenerator`. Every slot's arm is encrypted
  (`randomization_list_slots.ArmCiphertext`, AES-256-GCM, field format `enc:v1:keyId:…`, bound to the scheme, stratum
  and position). Minimisation has no list; each allocation draws a fresh cryptographic seed and decrypts the study's
  earlier allocations to count imbalance.
- **Allocation.** `POST /api/v1/execution/subjects/{subjectId}/randomize` (site staff, `subject_enrollment:randomize`)
  refuses with 403 and an audit entry when the site is not activated (`BLOCKED_RANDOMIZATION_SITE`) or there is no
  current consent (`BLOCKED_RANDOMIZATION_CONSENT`). Inside one transaction holding a per-study advisory lock, it then
  requires an enrolled, not yet randomised participant, an approved version and declared factor levels. It takes the
  next free slot in the stratum (or the minimisation draw) and, when the version needs a kit, the earliest-expiring
  unexpired kit of that arm at the site. It records `randomization_allocations` (scheme version, stratum, position,
  encrypted arm, kit) and moves the subject to `RANDOMIZED`. The audit entries name the allocation, stratum, position
  and kit, never the arm.
- **No other way in.** The state-change route refuses `RANDOMIZED` (`RANDOMIZATION_REQUIRES_ALLOCATION`), the create
  request no longer takes an arm, and `clinical_subjects.TreatmentGroupCiphertext` is dropped.
- **Integrity in the database.** Triggers make `randomization_allocations` insert-only and let a list slot change only
  its `AllocationId` from null to a value. Unique indexes hold one allocation per participant, per slot and per kit.
- **Kits.** A kit's arm is stored encrypted on the kit (`ip_kit_inventory_items.TreatmentArmCiphertext`, set by
  `treatment_arm` on the seed request), so the kit number on a blinded label reveals nothing.
- **Reading the arm.** Subject detail returns `treatment_group` and `investigational_product_id` only to a caller
  holding the gateway-signed unblinded-access claim; every other response omits or nulls them.
- **Key.** `RANDOMIZATION_ALLOCATION_KEY` (base64, 32 bytes) is required at startup and is separate from the
  demographics key, so a demographics key holder cannot read allocations.

## 5. Consequences & Trade-offs

- **Positive:** randomisation is evidenced end to end (who approved which version, which slot each participant took,
  which kit they received) and the arm is unreadable without the allocation key and an unblinded caller.
- **Negative:** one more secret to provision in every environment (AppHost parameter, compose, `.env`).
- **Follow-up:** the arm assignment moves to a kernel RTSM Assertion with a blinding class (spec §7.4) when subjects
  move to the kernel host; splitting the allocation key between the unblinded statistician and the IDMC (PRD-MDR-005)
  is not done here.

## 6. Implementation & Verification

- `RandomizationSchemeTests` (unit) cover the draft rules, second-person approval and canonical strata.
- `RandomizationPostgresTests` (PostgreSQL, application role) prove the encrypted list, kit and allocation, a raw read
  with no clear-text arm, concurrent randomisations taking distinct slots, list exhaustion, the site and consent
  refusals with persisted audit entries, and the triggers refusing UPDATE and DELETE.
