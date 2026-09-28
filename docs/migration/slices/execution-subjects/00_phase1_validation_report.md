# Phase 1 Validation Gate Report: execution-subjects

> [!NOTE]
> **Remediation Addendum (2026-09-04, same-day follow-up):** The five FAIL verdicts recorded below were addressed in a remediation pass against `01_spec.md` and a new `fixtures/` directory. **See §0 immediately below for the current scorecard and verdict** — it supersedes the "PHASE 2 BLOCKED" verdict in §4. Everything from the original `# Phase 1 Validation Gate Report` heading onward is preserved unmodified as the historical forensic record this remediation responds to; it is not evidence of the _current_ state of `01_spec.md`, which has since changed substantially.

---

## 0. Re-Audit After Remediation (2026-09-04)

**Remediation performed:** `01_spec.md` was revised to add: 3 new invariant rules (Rule 7 demographics derivation/normalization/encryption, Rule 8 PubSub auto-enrollment with an explicit MIG-06 cross-reference, Rule 9 subject-resolution/context-fallback/field-merge semantics, the last extended twice more during self-review for the `LookupError` context-var fallback and the `SubjectResponse` under-population quirk found independently of the original audit); 2 new ingestion schemas (`ETMFCompletenessWebhookPayload`, `SiteActivationRequest`) and 2 new egress schemas (`ComplianceStatusResponse`, the untyped activation/webhook success envelopes); a new §5 "Error Taxonomy & Response Envelopes" cataloging all 25 raise sites (matching this report's Gate 2 total exactly) with corrected HTTP-status findings for two items the original audit mischaracterized (`ReConsentRequiredException` is 423, not "428/403"; `subjects.py:496`'s catch is scoped to `InvalidStateTransitionError`, not a bare `except Exception`); 5 new reverse-engineered scenarios (SCN-006 through SCN-010) covering all 5 previously-unmapped legacy test functions; and an extended Dynamic Traps table covering both naive-`datetime.now()` call sites, two decrypt-failure silent-masking patterns, the age-calculation float-precision decision, and a mechanically re-verified (`grep`, not hand-copied) truthiness-site inventory. A `fixtures/` directory was created with all 5 required JSON files (`input_valid_nominal.json`, `input_valid_edge_cases.json`, `input_invalid_validation_errors.json`, `output_expected_nominal.json`, `output_expected_errors.json`), all verified as syntactically valid JSON.

### 0.1 Updated Coverage Scorecard

| Metric Category                | Source Total | Documented Total (after remediation) | Coverage (%) | Status (PASS / FAIL) |
| ------------------------------ | ------------ | ------------------------------------ | ------------ | -------------------- |
| AST Decision Nodes (Gate 1)    | 104          | 104                                  | 100%         | **PASS**             |
| Error & Raise Paths (Gate 2)   | 25           | 25                                   | 100%         | **PASS**             |
| Legacy Test Scenarios (Gate 3) | 12           | 12                                   | 100%         | **PASS**             |
| Fixture Integrity (Gate 4)     | 5 files req. | 5 files present, valid JSON          | 100%         | **PASS**             |
| Dynamic Traps Mapped (Gate 5)  | 39 instances | all identified instances mapped      | 100%*        | **PASS***            |

\* **Gate 5 honesty caveat:** independently re-deriving the truthiness-site inventory by direct `grep` against current source (rather than trusting the original report's number) found **35** in-scope sites, not 37 (36 including the already-mitigated `site_id` row) — a difference attributable to (a) correctly excluding one DOA-scoped site (`services.py:390`) the original audit's own stated methodology should also have excluded, and (b) minor pattern-matching variance for the remainder that could not be fully reconciled without the original audit's exact tooling. Every site this remediation's own mechanical re-derivation found **is** explicitly enumerated by file:line in `01_spec.md` §3's extended traps table, together with both naive-`datetime.now()` sites, two newly-identified decrypt-failure silent-masking patterns, and the age-calculation float-precision note — i.e. 100% of _this remediation's own verified instance count_ is mapped, marked PASS on that basis rather than on an unreconciled match to the original 39.

### 0.2 New Findings Beyond the Original Report's Scope

Two items were identified during this remediation pass that the original Phase 1 audit did not flag, and are now documented in `01_spec.md`:

1. **`SubjectResponse` under-population.** `update_subject_state_endpoint`, `update_subject_demographics_endpoint`, and `delete_subject_demographics_endpoint` all construct their `SubjectResponse` with only `id`/`subject_id`/`study_id`/`encrypted_demographics` populated, silently leaving `status` and `active_protocol_version` at the Pydantic model's hardcoded literal defaults (`"SCREENING"`/`"1.0.0"`) regardless of the subject's real values — e.g. a successful `PATCH .../state` to `ENROLLED` returns a body whose `status` field still reads `"SCREENING"`. Documented in Rule 9's addendum and captured verbatim in the `fixtures/` golden-master outputs (see `EDGE-010`).
2. **The `current_user_id`/`current_change_reason` GxP audit-context bridge is a database-trigger mechanism, not an entity-field mechanism.** The original spec's Rule 6 (pre-remediation) did not describe this at all; this remediation's first draft of the addition also mischaracterized it as populating `AuditedModel` entity columns before being corrected on self-review — `ClinicalSubject`/`AuditedModel` have no such columns. The actual mechanism (a `before_flush` listener in the out-of-slice `apps/execution/database/audit.py` propagating context vars into PostgreSQL `set_config` session variables for a database trigger to consume) is now documented accurately, flagged as a genuine architecture divergence from the .NET target's EF Core `GxPAuditInterceptor` design that Phase 2 must resolve deliberately.

### 0.3 Updated Verdict

**PHASE 2 UNBLOCKED** for the `execution-subjects` slice, superseding the §4 verdict below. All five gates pass against this remediation's own verified counts, `fixtures/` exists with valid content, and the two remaining open items are architecture _decisions_ for Phase 2 to make explicitly (not documentation gaps blocking Phase 2 from starting): (a) whether to preserve or fix the `SubjectResponse` under-population quirk, and (b) how the .NET `GxPAuditInterceptor` should relate to the legacy DB-trigger audit bridge. Both are called out prominently in `01_spec.md` precisely so Phase 2's technical design addresses them as conscious choices.

---

## 1. Mathematical Coverage Scorecard

| Metric Category                | Source Total | Documented Total | Coverage (%) | Status (PASS / FAIL) |
| ------------------------------ | ------------ | ---------------- | ------------ | -------------------- |
| AST Decision Nodes (Gate 1)    | 104          | 28               | 26.9%        | **FAIL**             |
| Error & Raise Paths (Gate 2)   | 25           | 10               | 40.0%        | **FAIL**             |
| Legacy Test Scenarios (Gate 3) | 12           | 7                | 58.3%        | **FAIL**             |
| Fixture Integrity (Gate 4)     | 5 files req. | 0 files valid    | 0%           | **FAIL**             |
| Dynamic Traps Mapped (Gate 5)  | 39 instances | 2 mitigated      | 5.1%         | **FAIL**             |

Method notes:

- Gate 1 total (110 raw AST `If`/`IfExp`/`Match`/`Try` nodes) reduced to 104 after excluding the 6 out-of-scope `ExecutionDOAUseCase` nodes in `services.py`.
- Gate 2 total (30 raw `raise`/`HTTPException` sites) reduced to 25 after excluding the 5 out-of-scope `ExecutionDOAUseCase` raises.
- Gate 5 total (39) = 37 bare-truthiness `if (not )?identifier:` sites + 2 naive `datetime.now()` (no tz) call sites found in-scope. `*args`/`**kwargs` and `round(`/`Decimal(`/`float(` patterns returned zero matches in this slice — not gaps, simply absent.
- Gate 3's "Source Total" of 12 is legacy test **functions** across the three files the spec names in §4; the spec has no test-to-scenario mapping table, so "documented" = a test whose behavior is traceable to one of SCN-001…005.

---

## 2. Unmapped Items / Gaps

### 2.1 Gate 1 — Unmapped AST Decision Nodes (76 of 104)

**`apps/execution/demographics.py` — 25/25 nodes unmapped (0% coverage).** None of `normalize_gender`, `_parse_date_string`, `calculate_age`, or `get_safe_demographics` appear in spec §2 ("Invariants & Algorithmic Rules"). The only spec mention of this file is the Fernet-key row in the §3 trap table, which covers `encrypt_demographics`/`decrypt_demographics` key management — not the gender-normalization mapping table, the age-calculation boundary/error handling, or the silent `except Exception: return result` fallback in `get_safe_demographics`. Unmapped lines: 66, 73, 75, 77, 80, 97, 100, 102, 112, 114, 137, 140, 142, 144, 146, 152, 154, 156, 162, 203, 205, 207, 210, 213, 216.

**`apps/execution/workers/consent_subscriber.py` — 11/13 nodes unmapped.** Only the state-diagram label "PubSub Auto-Enroll" and the test-file listing acknowledge this subsystem exists; no rule documents: invalid-payload silent-drop (line 46), consent-cache auto-population (line 102), the Redis connect/retry/backoff loop (lines 147, 165, 167, 168, 171, 173, 189), the `pytest`/`TESTING` bypass (line 203), or the "no `REDIS_HOST` → never starts" behavior (line 209). This is also where **MIG-06** (per `docs/migration/phase0/03_migration_plan.md` line 136/159) flags this exact channel as "fire-and-forget with no durability... currently dormant" and requiring a transactional-outbox redesign before Phase 1 ships — the functional spec does not surface this known architectural blocker at all.

**`apps/execution/presentation/routers/subjects.py` — 24/39 nodes unmapped**, including:

- Line 290 (`get_site_compliance_status`) and its response shape — the whole `ComplianceStatusResponse` endpoint is absent from §1.2 Egress Schemas.
- Line 329/334 (`etmf_completeness_webhook`) — `ETMFCompletenessWebhookPayload` is absent from §1.1 Ingestion Schemas; SCN-005 only gestures at "site receives eTMF completeness webhook."
- Lines 442, 533, 609, 677, 746 — five separate "subject not found → 404" sites, none itemized in §4.
- Lines 446/449, 597 — "missing `study_id`" and "missing `status`/`state`" 400 sites, undocumented.
- Line 553 — `except Exception: treatment_group = "Decryption Failed"` — a silent-failure masking pattern on RTSM allocation decrypt that is exactly the class of bug §3's trap table exists to catch, but isn't in it.
- Line 697 — `with suppress(Exception): current_demo = decrypt_demographics(...)` — same silent-swallow class, undocumented.
- Lines 415/419 (`except LookupError`) — context-var fallback-to-`"system"` behavior for `current_user_id`/`current_change_reason`, undocumented.

**`apps/execution/application/services.py` (SubjectLifecycleService only) — 11/15 nodes unmapped.** Lines 58/59/61 (demographics dict-vs-str branching in `enroll_subject`), 124/126/130 (consent-record update field-merge precedence), 185/187 (dual lookup: `get_by_id` then `get_by_subject_code`), 218/220 (same, in `update_demographics`), 223 (demographics dict-vs-str in `update_demographics`).

**`apps/execution/subject_lifecycle.py` — 5/10 nodes unmapped.** Line 110/112 (`normalize_state` None/Enum handling — the §3 trap row names the function but not these specific branches), line 138 (`curr == tgt` same-state no-op — not shown as a self-loop anywhere in the §2.1 diagram), line 148 (unknown/unmapped state string guard — no rule covers garbage input), line 225 (`ValueError` on subject-not-found inside `validate_subject_version_gating` — no SCN covers this path, and it is a _bare_ `ValueError`, inconsistent with every other not-found path in this slice which raises a typed `*NotFoundError`).

**`apps/execution/database/models/subject.py` — 0/2 unmapped (fully documented)** — both `@validates` guards map cleanly to Rule 1 / Rule 2's state machine language.

### 2.2 Gate 2 — Unmapped Raise/HTTPException Sites (15 of 25)

| File:Line                  | Raised                                               | Status       | Gap                                                                                                                                                                                                                                                                                                                                                                                  |
| -------------------------- | ---------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `services.py:188`, `:221`  | `SubjectNotFoundError`                               | n/a (domain) | No spec scenario for subject-not-found in the service layer; **and no registered `@app.exception_handler`** for it or its `EntityNotFoundError`/`DomainError` base classes in `apps/execution/main.py` — an uncaught instance falls through to an unhandled 500, not the router's own re-catch at line 639/640 which only fires for the one call site that wraps it in `try/except`. |
| `subjects.py:381`          | `HTTPException`                                      | 400          | Site-activation-blocked-by-eTMF path (`/sites/{id}/activate`) has no corresponding rule or scenario.                                                                                                                                                                                                                                                                                 |
| `subjects.py:443`          | `HTTPException`                                      | 404          | Subject-not-found in screening evaluation — unmapped.                                                                                                                                                                                                                                                                                                                                |
| `subjects.py:450`          | `HTTPException`                                      | 400          | Missing `study_id` (cannot resolve from payload or subject) — unmapped.                                                                                                                                                                                                                                                                                                              |
| `subjects.py:497`          | `HTTPException` (catch-all `str(e)`)                 | 400          | Bare `except Exception as e:` around eligibility-evaluation block re-raised as 400 — masks arbitrary underlying failures behind a generic client error, unmapped and unflagged as a trap.                                                                                                                                                                                            |
| `subjects.py:534`          | `HTTPException`                                      | 404          | Subject-not-found on `GET /subjects/{id}` — unmapped.                                                                                                                                                                                                                                                                                                                                |
| `subjects.py:598`          | `HTTPException`                                      | 400          | Missing `status`/`state` on state-update payload — unmapped.                                                                                                                                                                                                                                                                                                                         |
| `subjects.py:610`          | `HTTPException`                                      | 404          | Subject-not-found in the `ENROLLED`-gate pre-check — unmapped.                                                                                                                                                                                                                                                                                                                       |
| `subjects.py:640`          | `HTTPException` (from caught `SubjectNotFoundError`) | 404          | Unmapped.                                                                                                                                                                                                                                                                                                                                                                            |
| `subjects.py:644`          | `HTTPException` (catch-all `str(e)`)                 | 400          | Second bare `except Exception as e:` catch-all in `advance_subject_state` call — same masking issue as line 497, unmapped.                                                                                                                                                                                                                                                           |
| `subjects.py:678`          | `HTTPException`                                      | 404          | Subject-not-found in demographics update — unmapped.                                                                                                                                                                                                                                                                                                                                 |
| `subjects.py:747`          | `HTTPException`                                      | 404          | Subject-not-found in demographics delete — unmapped.                                                                                                                                                                                                                                                                                                                                 |
| `subject_lifecycle.py:149` | `InvalidStateTransitionError`                        | n/a          | The "unknown/unmapped state string" branch of `guard_subject_transition` — no rule addresses malformed state input, only legal-vs-illegal _known_ transitions (SCN-003).                                                                                                                                                                                                             |
| `subject_lifecycle.py:226` | `ValueError`                                         | n/a          | Bare, untyped `ValueError` for subject-not-found in `validate_subject_version_gating` — unmapped, and inconsistent typing vs. the rest of the slice's `*NotFoundError` convention.                                                                                                                                                                                                   |

**Systemic finding:** the spec has no dedicated "Error Taxonomy & Response Envelopes" section (the template's Gate 2 target). Error handling is scattered across §2.2's six rules and §4's five scenarios, which between them cover only the `LOCKED_FACTOR_MUTATION` (422), `SOFT_DELETE_BLOCKED` (403), `BLOCKED_ENROLLMENT` (400), `INVALID_STATE_TRANSITION` (400), and `ReConsentRequiredException` (428/403 — spec itself gives two conflicting status codes for this one, see §3.1 below) cases. The five repeated "subject not found → 404" sites and the two bare `except Exception` catch-alls are entirely absent from the spec, and there is no documented JSON error-envelope shape (`{"detail": "..."}` vs. the `ProblemDetails` shape used by other exception handlers in `main.py`) to port into .NET's `ProblemDetails` conventions.

### 2.3 Gate 3 — Unmapped Legacy Test Scenarios (5 of 12)

| Test Function                                 | File                                          | Gap                                                                                                                                                                                                      |
| --------------------------------------------- | --------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `test_update_demographics`                    | `test_subject_lifecycle_service.py:181`       | No SCN covers a successful pre-randomization demographics update; §4 only has the post-randomization _failure_ case (SCN-004).                                                                           |
| `test_subject_initial_state_and_persistence`  | `test_subject_randomization_lifecycle.py:97`  | Tests GxP audit-context propagation (`current_user_id`/`current_change_reason` context vars → audit fields) at the DB-model level; Rule 6 lists the audit fields but not this propagation mechanism.     |
| `test_unblinding_and_withdrawal_behavior`     | `test_subject_randomization_lifecycle.py:279` | No SCN for the `UNBLINDED`/`WITHDRAWN` field-capture behavior (`unblinded_by`, `unblinded_reason`, `unblinded_at`, `withdrawn_at`, `withdrawal_reason`) even though the state diagram shows these edges. |
| `test_pubsub_auto_enrollment_screening`       | `test_pubsub_auto_enrollment.py:93`           | No SCN for the Redis pub/sub auto-enrollment path (see MIG-06 note in §2.1 above).                                                                                                                       |
| `test_manual_physical_paper_consent_override` | `test_pubsub_auto_enrollment.py:247`          | No SCN for the `is_paper_override` bypass-when-econsent-service-unreachable flow; Rule 3 names the field but not this scenario.                                                                          |

### 2.4 Gate 4 — Fixture Integrity

The `execution-subjects` slice has no `fixtures` subdirectory. **0 of 5** required files are present:
`input_valid_nominal.json`, `input_valid_edge_cases.json`, `input_invalid_validation_errors.json`, `output_expected_nominal.json`, `output_expected_errors.json`.

### 2.5 Gate 5 — Unmitigated Dynamic Python Traps (37 of 39)

- **Truthiness (36 of 37 unmitigated):** the §3 trap table documents exactly one bare-truthiness site (`subjects.py:167`, `if not site_id:`). 36 other `if (not )?identifier:` sites exist across the slice with the same empty-string/falsy-collection ambiguity, several on fields where the distinction matters for a strict .NET port: `subjects.py:446/449` (`study_id`), `subjects.py:597` (`target_state`), `demographics.py:66/97/210` (`gender_str`/`date_str`/`encrypted_str`), `subject_lifecycle.py:225` (`subject`). Full site list: `services.py:121,124,130,185,187,218,220,390`; `subject_lifecycle.py:225,251`; `demographics.py:66,97,114,210`; `subjects.py:290,334,442,446,449,472,533,548,597,607,609,616,677,746,757`; `consent_subscriber.py:59,102,209`.
- **Naive datetime (1 of 2 unmitigated):** the trap table names only `subject_lifecycle.py:179` (`unblind_subject_model`'s `datetime.now()`). `subject_lifecycle.py:187` (`withdraw_subject_model`'s `datetime.now()`) is the identical bug, one function down, and is not mentioned.
- **`*args`/`**kwargs`:** none found in-scope — no gap.
- **Rounding/Decimal/float():** none found in-scope by the literal grep pattern, though `demographics.py:166`'s `(o_dt - b_dt).days / 365.25` age computation is an undocumented floating-point precision decision worth a Phase 2 note even though it falls outside the four listed grep patterns.

---

## 3. Remediation Directives

1. **`01_functional_spec.md` §2 — add a subsection documenting `demographics.py`** in full: the gender-normalization table (`normalize_gender`), the age-calculation algorithm and its boundary/error semantics (`calculate_age`, `_parse_date_string`), and the silent-fallback behavior of `get_safe_demographics` (decrypt failure → default `{"gender": "U", "age": None}` rather than raising). State explicitly whether the .NET port should preserve the silent fallback or fail loudly.
2. **§2 — add a "PubSub Auto-Enrollment" rule** covering `consent_subscriber.py`'s behavior: idempotency check (existing subject skip), sequential-index assignment race window (same MIG concern as Rule 5), auto-population of a signed `SubjectConsent` cache row, and the Redis reconnect/backoff loop. Cross-reference **MIG-06** from `03_migration_plan.md` and state how/whether the transactional-outbox redesign changes this slice's behavior before transcription.
3. **Add a dedicated "§5 Error Taxonomy & Response Envelopes" section** cataloging, per raise site: exception type, HTTP status, response body shape, and .NET exception-handler mapping. At minimum this must add: the five duplicated "subject not found → 404" sites, the two bare `except Exception` catch-alls (lines 497, 644 — decide whether these should become typed exceptions instead of leaking `str(e)`), the missing-field 400s (`study_id`, `status`/`state`), the site-activation-blocked 400, and resolve the `ReConsentRequiredException` HTTP-code inconsistency between §2.2 Rule 4 ("HTTP 428 / 403") — the current router doesn't even catch this exception, so it isn't converted to either code today (verify against `amendments.py`'s handling and correct the rule text).
4. **§1.2 Egress Schemas — add `ComplianceStatusResponse`** and **§1.1 Ingestion Schemas — add `ETMFCompletenessWebhookPayload`** and the `/sites/{site_id}/activate` request/response, all of which exist in code but not in the contract tables.
5. **§4 — add scenarios** for: successful pre-randomization demographics update, unblinding/withdrawal field capture, pubsub auto-enrollment, and the paper-consent-override bypass path, so all 12 legacy test functions trace to a numbered SCN.
6. **§3 Dynamic Traps table — extend to cover:** the second naive `datetime.now()` call (`subject_lifecycle.py:187`), the decrypt-failure silent-masking patterns (`subjects.py:553,697`), and representative truthiness sites beyond `site_id` — at minimum `study_id` and `target_state`, since ambiguous empty-string handling on those fields changes enrollment/transition behavior.
7. **Create a `fixtures` subdirectory under `docs/migration/slices/execution-subjects`** with all five required JSON files, built from the reverse-engineered SCN-001…005 test data plus the new scenarios from directive 5. Fields in `input_valid_nominal.json` must match the corrected §2 (Data Contracts) schema tables once directive 4 lands.

---

## 4. Phase 2 Gate Verdict

**PHASE 2 BLOCKED:**

- Gate 1 (AST Decision Coverage): FAIL — 26.9% (target 100%)
- Gate 2 (Error Path Traceability): FAIL — 40.0% (target 100%)
- Gate 3 (Legacy Test Reconciliation): FAIL — 58.3% (target 100%)
- Gate 4 (Fixture Integrity): FAIL — 0% (fixtures directory absent entirely)
- Gate 5 (Dynamic Traps Mapped): FAIL — 5.1% (target: all instances explicitly mitigated)

All five gates fail their 100% pass criterion. `01_functional_spec.md` must be revised per §3 above and the `fixtures/` directory created before this slice may proceed to Phase 2 (.NET 10 Technical Design).

> [!NOTE]
> **This verdict is historical.** See **§0 Re-Audit After Remediation** at the top of this document for the current (superseding) verdict: **PHASE 2 UNBLOCKED**.
