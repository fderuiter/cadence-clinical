# Functional Specification: Participant Compensation

**Slice:** `participant-compensation`
**Bounded Context:** `Cadence.Apps.Payments` (Site Payments & Split-Payee Engine)
**Migration Tier:** Not yet sequenced in `BOUNDED_CONTEXT_MIGRATION_MATRIX.md`; flagged **BLOCKED** by `Migration_Phase0_Readiness_Audit.md` (AUD-NET10-002) pending the Universal Decimal Precision Standard remediation.

**Source Python Files:**

- `apps/payments/domain/models.py` (domain-model fields: `ParticipantWalletModel`, `ParticipantStipendRuleModel`, `ParticipantExpenseClaimModel`, `ParticipantDisbursementModel`, `ParticipantTransactionModel`, `LaySummaryModel`, `LaySummaryReceiptModel`, `ReceiptOcrResult`, `AuditAction`)
- `apps/payments/services/participant_compensation_service.py` (`ParticipantCompensationService` — business-rule engine)
- `apps/payments/presentation/routers/participant.py` (FastAPI HTTP surface, response mapping, authorization gates)

**Supporting files read for full contract fidelity (not primary transcription targets, but referenced below):** `apps/payments/domain/exceptions.py`, `apps/payments/domain/ports.py`, `apps/payments/adapters/models.py` (SQLAlchemy column/constraint definitions), `apps/payments/presentation/dtos.py` (request/response validators), `apps/payments/services/audit.py`, `apps/payments/main.py`, `packages/hexagonal/__init__.py` (RFC 7807 error envelope), `packages/database/datetime_helpers.py` (`UTCDateTime`), `packages/security/rbac.py` and `packages/security/middleware.py` (authorization boundary).

**Governing Regulatory Standards:** 21 CFR Part 11 (audit trail, immutability), IRS Form 1099/W-9 reporting ($600 annual threshold, 26 U.S.C. § 6041), 45 CFR 46 / IRB anti-coercion stipend rules, EU CTR 536/2014 Article 37 (plain-language trial result summaries).

---

## 0. Scope Note on Financial Precision

Every monetary field in this slice — `ParticipantWalletModel.current_balance/lifetime_*/taxable_ytd/anti_coercion_cap`, `ParticipantStipendRuleModel.amount/anti_coercion_cap`, `ParticipantExpenseClaimModel.gross_amount/tax_deduction/net_amount/per_diem_cap`, `ParticipantDisbursementModel.amount`, `ParticipantTransactionModel.amount/balance_after` — is a Python `float`, persisted as SQLAlchemy `Float` (double precision), and every arithmetic accumulation in the service uses Python's built-in `round(x, 2)`. `round()` operates on IEEE‑754 binary floats, not decimal values, and is **not** equivalent to `MidpointRounding.ToEven` on an exact decimal — it inherits binary representation error (e.g. `round(2.675, 2)` evaluates to `2.67`, not `2.68`, because `2.675` is not exactly representable in binary). This is the exact defect flagged as **Gap C1/C2** in `Migration_Phase0_Readiness_Audit.md` (AUD-NET10-002), concretely instantiated at every accumulation site in this file (§2.2, §2.4). Per that audit, `CONTEXT.md`'s Universal Decimal Precision Standard requires `System.Decimal` for this domain; the Parity Test Matrix (§5) below explicitly separates "bit-for-bit Python parity" cases from "corrected-per-ADR-2239" cases because the two will diverge on penny-boundary inputs, and that divergence must be a tracked, reviewed decision — not an incidental side effect of the transcription.

---

## 1. Exact Data Schemas & Invariants

### 1.1 `ParticipantWalletModel` (table `payments_participant_wallets`)

| Field | Type | Nullable | Default | Invariants |
|---|---|:---:|---|---|
| `id` | `string(36)` | No | `uuid4()` | PK. |
| `subject_id`, `study_id`, `site_id` | `string(255)` | No | — | Indexed individually. **No composite unique constraint on `(study_id, subject_id)`** — see §4.1 (race condition). |
| `current_balance` | `float` | No | `0.0` | Never validated `>= 0` at the DB or Pydantic layer; only individual mutation call sites (`disburse_funds`) prevent it from going negative. |
| `lifetime_stipends`, `lifetime_reimbursements`, `lifetime_disbursements` | `float` | No | `0.0` | Monotonically increasing counters; never decremented anywhere in this file. |
| `currency` | `string(10)` | No | `"USD"` | **Free text.** No ISO-4217 enum/pattern validation anywhere in the stack (DTO, domain model, or DB column). |
| `status` | `string(50)` | No | `"ACTIVE"` | No enum constraint; no transition logic exists in this service (status is never changed after creation). |
| `taxable_ytd` | `float` | No | `0.0` | IRS 1099 running total; reset logic (year rollover) does not exist anywhere in this file — see §4.2. |
| `is_1099_threshold_exceeded` | `bool` | No | `False` | One-way flag: set `True` at `taxable_ytd >= 600.0`, never reset to `False`. |
| `w9_status` | `string(50)` | No | `"NOT_REQUIRED"` | Informal 4-value state machine: `NOT_REQUIRED → REQUIRED → SUBMITTED → VERIFIED`. **Not enforced** — `submit_w9_form`/`verify_w9_form` do not check the current value before transitioning (§2.1). |
| `tax_id_last_four` | `string(4)` | Yes | `None` | Exactly 4 chars enforced by DTO (`min_length=4, max_length=4`), not by the DB column type. |
| `card_type` | `string(50)` | No | `"RELOADABLE_DEBIT"` | Free text. |
| `card_status` | `string(50)` | No | `"ACTIVE"` | Free text. |
| `virtual_card_token` | `string(100)` | Yes | synthesized `f"VTOK-{uuid4().hex[:8].upper()}"` if omitted at creation | Non-cryptographic; not validated as a real card-issuer token — this is demo/stub data. |
| `virtual_card_mask` | `string(50)` | Yes | literal `"****-****-****-4821"` if omitted | **Hardcoded fallback**, not derived from any real card issuance system. |
| `virtual_card_expiry` | `string(20)` | Yes | literal `"12/29"` if omitted | Hardcoded. |
| `anti_coercion_cap` | `float` | Yes | `None` | Optional wallet-level override of the per-visit IRB cap; validated `>= 0.0` at the DTO layer only. |
| `ach_routing_number` | `string(50)` | Yes | `None` | No routing-number checksum/format validation. |
| `ach_account_mask` | `string(50)` | Yes | literal `"ACH-***4321"` if omitted | Hardcoded fallback. |
| `ach_account_holder` | `string(255)` | Yes | `f"Participant {subject_id}"` if omitted | — |
| `created_at`/`updated_at` | `UTCDateTime` (tz-aware, DB-enforced) | No | `func.now()` | See §1.6 for the `UTCDateTime` contract. |
| `version_index` | `int` | No | `1` | Incremented on every mutating operation; **not used for optimistic concurrency control** (no `WHERE version_index = ?` guard anywhere — see §4.1). |

### 1.2 `ParticipantStipendRuleModel` (table `payments_participant_stipend_rules`)

| Field | Type | Nullable | Default | Invariants |
|---|---|:---:|---|---|
| `study_id`, `visit_code` | `string` | No | — | Together identify the "active rule" looked up by `create_stipend_rule`/`process_visit_verified_stipend`; **no composite unique DB constraint**, only an application-level "find first active match" query (§4.1). |
| `amount` | `float` | No | `0.0` | DTO enforces `ge=0.0`. Domain invariant: `amount <= anti_coercion_cap` when a cap is set — enforced in `create_stipend_rule` (raises `PaymentsValidationError`, HTTP 422) **and redundantly re-checked** in `process_visit_verified_stipend` (§2.2). |
| `taxable` | `bool` | No | `False` | Gates whether crediting this rule accrues `taxable_ytd`. |
| `anti_coercion_cap` | `float` | Yes | `None` | 45 CFR 46 ethics cap; see above. |
| `is_active` | `bool` | No | `True` | `create_stipend_rule` "soft-upserts" by mutating the existing active row in place rather than superseding it with a new version — **this differs from the supersede/version pattern used elsewhere in the Payments domain** (`BudgetContractModel`/`FeeScheduleModel` conceptually support `SUPERSEDED` status via `ContractStatus`, but `ParticipantStipendRuleModel` has no analogous status and simply overwrites `amount`/`taxable`/etc. in place). Historical stipend amounts are recoverable only via `PaymentAuditLogModel`, not by querying prior rows. |

### 1.3 `ParticipantExpenseClaimModel` (table `payments_participant_expense_claims`)

| Field | Type | Nullable | Default | Invariants |
|---|---|:---:|---|---|
| `expense_date` | `string(50)` | No | — | **Raw string, no date-format validation** at any layer (DTO `str`, DB `VARCHAR`). Arbitrary text is accepted. |
| `category` | `string(50)` | No | `"MILEAGE"` | Free text; the only enum-like source of truth is the OCR keyword matcher (§2.3), which is not invoked by `submit_expense_claim` — a caller may submit any string. |
| `gross_amount` | `float` | No | — | DTO enforces `gt=0.0`. |
| `tax_deduction` | `float` | No | `0.0` | DTO enforces `ge=0.0`. **Doubles as a sentinel**: `0.0` is used by `submit_expense_claim` to mean "caller did not supply a value," not "the deduction is legitimately zero" (§2.3, §4.3). |
| `net_amount` | `float` | No | computed if omitted | If the caller supplies `net_amount` explicitly, it is stored **verbatim with no server-side check that `net_amount == gross_amount - tax_deduction`** (§4.3). |
| `status` | `string(50)` | No | `"PENDING_REVIEW"` | Referenced values in code: `PENDING_REVIEW`, `APPROVED`, `REJECTED`, and a guard against `DISBURSED` (§2.3) that is **never set anywhere** — vestigial state. No enum/pattern constraint at the DTO layer; any string is accepted by `ExpenseClaimReviewRequest.status`. |
| `per_diem_cap` | `float` | Yes | `None` | DTO enforces `ge=0.0`. |

### 1.4 `ParticipantDisbursementModel` (table `payments_participant_disbursements`)

| Field | Type | Nullable | Default | Invariants |
|---|---|:---:|---|---|
| `amount` | `float` | No | `0.0` | Always stored **positive** on this table (the signed negative representation is only on the paired `ParticipantTransactionModel` row — §1.5, §4.4). |
| `reference_number` | `string(100)` | No, **unique** | `f"DISB-{VCARD|ACH}-{uuid4().hex[:8].upper()}"` | The only DB-level uniqueness guard in the disbursement path; it does not prevent double-processing of the same logical request (§4.4). |
| `status` | `string(50)` | No | `"DISPATCHED"` | Set once and never transitioned elsewhere in this file (no settlement/failure callback exists in this slice). |

### 1.5 `ParticipantTransactionModel` (table `payments_participant_transactions`) — immutable ledger

| Field | Type | Nullable | Default | Invariants |
|---|---|:---:|---|---|
| `transaction_type` | `string(50)` | No | — | Observed values: `STIPEND_CREDIT`, `MILESTONE_CREDIT`, `REIMBURSEMENT_CREDIT`, `DISBURSEMENT_DEBIT`. No enum constraint. |
| `amount` | `float` | No | `0.0` | **Sign convention is implicit and type-unsafe**: credits (`STIPEND_CREDIT`, `MILESTONE_CREDIT`, `REIMBURSEMENT_CREDIT`) store a positive value; `DISBURSEMENT_DEBIT` stores a **negative** value (`amount=-payout_amount`). Nothing in the schema or type system enforces this — a caller of `list_transactions` must know the convention externally to interpret sign correctly (§4.4). |
| `balance_after` | `float` | No | `0.0` | Snapshot of `wallet.current_balance` after the mutation — a point-in-time balance audit trail, not independently recomputed/validated. |
| `reference_id` | `string(255)` | No, **indexed but not unique** | — | Used as the idempotency key for stipend credits (`process_visit_verified_stipend` pre-checks `reference_id == event_id`), but **the DB does not enforce uniqueness**, so the guarantee is purely a race-prone application-level check (§4.1). For `DISBURSEMENT_DEBIT`/`REIMBURSEMENT_CREDIT` rows, `reference_id` points at the disbursement/claim id, not an idempotency key at all — same column, two different semantic uses depending on `transaction_type`. |

### 1.6 `LaySummaryModel` / `LaySummaryReceiptModel`

| Field | Type | Nullable | Default | Invariants |
|---|---|:---:|---|---|
| `status` | `string(50)` | No | `"PUBLISHED"` | DTO docstring claims 3 states (`DRAFT`, `IRB_APPROVED`, `PUBLISHED`); only `DRAFT`/`PUBLISHED` are ever set by code. No transition guard: `publish_lay_summary` can be called on a summary already `PUBLISHED` (idempotent no-op re-publish) with no error. |
| `plain_language_sections` | `JSON` (`dict\|None`) | Yes | 6-key boilerplate template on **create only** | **Data-loss defect**: on **update** (`summary_id` supplied), the code executes `existing.plain_language_sections = plain_language_sections or {}` — omitting the field on an update call overwrites existing content with an **empty dict**, it does *not* preserve the prior value and does *not* re-apply the boilerplate default. This is a genuine bug in the reference implementation (§4.5) that must be an explicit, reviewed decision in the .NET port, not a silent carry-over. |
| `receipt_hash` (on `LaySummaryReceiptModel`) | `string(64)` | No | SHA-256 hex digest of `f"{summary_id}|{subject_id}|{study_id}|{comprehension_score}|{now.isoformat()}|{statement}|{user_id}"` | The hash input embeds `datetime.isoformat()` of a Python `tz=UTC` `datetime`, which renders as `...+00:00` (not `Z`) with up to 6 fractional digits. This is the same cross-runtime timestamp-format hazard identified as **Gap 7 / Canonical Timestamp Normalization Invariant** in the platform Phase 0 audit — concretely instantiated here: reproducing this exact hash in .NET for any historical-record verification requires byte-identical `isoformat()` emulation, not `DateTimeOffset.ToString("o")`. |

### 1.7 `UTCDateTime` Contract (applies to every timestamp column in this slice)

`packages/database/datetime_helpers.UTCDateTime` (SQLAlchemy `TypeDecorator`) **rejects naive `datetime` values on write** (`ValueError: Naive datetimes are not allowed.`) and normalizes any read value to `tzinfo=UTC`. Every write site in this service uses `datetime.now(UTC)`, so this constraint is never actually exercised as a failure path in current code — but a .NET port must preserve the "always tz-aware, always UTC-normalized" invariant, since nothing else in the domain layer enforces it (Pydantic domain models type these fields as `Any | None`, not `datetime`, at `apps/payments/domain/models.py` — see §4.6).

---

## 2. Control Flow & Execution Sequence

### 2.1 Wallet & Tax Compliance (`get_or_create_wallet`, `submit_w9_form`, `verify_w9_form`, `issue_reloadable_debit_card`, `disburse_funds`)

**`get_or_create_wallet(subject_id, study_id, site_id, ...)`**
1. Query `ParticipantWalletModel` where `study_id == study_id AND subject_id == subject_id` (eager-loads `transactions`, `expense_claims`, `disbursements`).
2. If found → return immediately. **No audit log written on the "found" path.**
3. If not found → construct a new wallet with all the hardcoded/synthesized defaults in §1.1, `session.add`, `commit`, `refresh`, then write a `PARTICIPANT_WALLET_CREATED` audit entry. Return the new wallet.
4. **Failure branches:** none explicit — any SQLAlchemy exception propagates unhandled to the RFC 7807 `DomainError` handler only if it happens to be a `DomainError` subclass; a raw `IntegrityError` (e.g. from a concurrent duplicate — see §4.1) is **not** caught here and will surface as an unmapped 500.

**`submit_w9_form(wallet_id, tax_id_last_four, full_legal_name, certify_accurate=True, ...)`**
1. Load wallet by id → `WalletNotFoundError` (404) if missing.
2. If `certify_accurate is False` → `PaymentsValidationError` (422), **no state mutated, no audit log written**.
3. Else: set `tax_id_last_four`, `w9_status = "SUBMITTED"`, `w9_submitted_at = now`, `version_index += 1`, commit, refresh.
4. Write `W9_FORM_SUBMITTED` audit entry with `previous_state={"w9_status": "REQUIRED"}` — **this is a hardcoded literal, not the wallet's actual prior `w9_status`**. If the wallet was `NOT_REQUIRED` (never yet exceeded the threshold) or already `SUBMITTED` (a legitimate re-submission, which the code allows unconditionally — no guard against re-submitting), the audit trail will misreport the prior state. Flag for §4.2/§5.
5. Return a response dict (not the ORM entity).

**`verify_w9_form(wallet_id, verified_by, ...)`**
1. Load wallet → 404 if missing.
2. **No check that `w9_status == "SUBMITTED"`** before verifying — a wallet in any state (including `NOT_REQUIRED`) can be "verified."
3. Set `w9_status = "VERIFIED"`, `w9_verified_at = now`, `version_index += 1`, commit, refresh.
4. Audit entry `W9_FORM_VERIFIED` with `previous_state={"w9_status": "SUBMITTED"}` — same hardcoded-literal defect as step 4 above.

**`issue_reloadable_debit_card(wallet_id, cardholder_name=None, card_type="RELOADABLE_DEBIT", initial_load=0.0, ...)`**
1. Load wallet → 404 if missing.
2. Synthesize `card_token`, `last_four = f"{uuid4().int % 9000 + 1000}"` (derived from a UUID's integer value, not a CSPRNG draw over `[1000,9999]` — statistically fine but not the same source as real card issuance), `card_expiry = "{now.month:02d}/{(now.year+3)%100:02d}"`.
3. **`initial_load` is accepted by both the DTO (`ge=0.0`) and the service signature but is never applied to `wallet.current_balance` or recorded as a transaction** — a caller supplying `initial_load=100.0` gets no funds credited and no error. Dead/no-op parameter (§4.7).
4. Mutate wallet card fields, `version_index += 1`, commit, refresh, write `RELOADABLE_CARD_ISSUED` audit entry, return a dict.

**`disburse_funds(wallet_id, disbursement_type="VIRTUAL_CARD", amount=None, destination_mask=None, ...)`**
1. Load wallet → 404 if missing.
2. `payout_amount = round(amount if amount is not None else wallet.current_balance, 2)`.
3. `payout_amount <= 0.0` → `PaymentsValidationError` (422).
4. `payout_amount > wallet.current_balance` → `PaymentsValidationError` (422, "Insufficient funds").
5. Debit `current_balance`, credit `lifetime_disbursements`, `version_index += 1`.
6. Resolve `dest_mask` (explicit param → card/ACH mask by type → literal `"**** 4821"`), synthesize `ref_number`.
7. Insert `ParticipantDisbursementModel` (positive `amount`) and `ParticipantTransactionModel` (**negative** `amount = -payout_amount`, `reference_id = disbursement.id`).
8. Commit once (both rows in the same transaction), refresh disbursement, write `DIGITAL_DISBURSEMENT_DISPATCHED` audit entry.
9. **No idempotency guard of any kind** — see §4.4.

### 2.2 SoA Stipend Rules & Automated Visit Crediting

**`create_stipend_rule(study_id, visit_code, visit_name, amount, ..., anti_coercion_cap=None, ...)`**
1. If `anti_coercion_cap is not None and amount > anti_coercion_cap` → `PaymentsValidationError` (422), formatted with both values to 2 decimals. No mutation, no audit log.
2. Query for an existing **active** rule matching `(study_id, visit_code)`.
3. If found → mutate in place (all fields overwritten), `version_index += 1`.
4. If not found → insert new row, `version_index = 1`.
5. Commit, refresh, write `PARTICIPANT_STIPEND_RULE_CREATED` audit entry (same action constant used for both create and update — no distinct "updated" audit action, unlike the analogous wallet-update code path elsewhere which does differentiate).

**`process_visit_verified_stipend(study_id, site_id, subject_id, visit_code, event_id, verified_by="crc_coordinator", ...)`** — the EDC-triggered auto-credit entrypoint, called from `POST /participant/events/visit-verified`.

1. **Idempotency pre-check:** query `ParticipantTransactionModel` where `reference_id == event_id`. If found → return `{"status": "ALREADY_PROCESSED", ...}` immediately with **no new writes**. (Race-prone; see §4.1.)
2. Query for an active stipend rule on `(study_id, visit_code)`. If none → return `{"status": "SKIPPED", "reason": "No active stipend rule found..."}` — **this is a normal 200 response, not an error**, even though no compensation was applied.
3. **Rule-level anti-coercion re-check:** if `rule.anti_coercion_cap is not None and rule.amount > rule.anti_coercion_cap` → write an `ANTI_COERCION_CAP_EXCEEDED` audit entry **and then** raise `PaymentsValidationError` (422). (This should be logically unreachable given step 1 of `create_stipend_rule` already enforces the same invariant at write time — it only fires if the rule row was mutated outside this service, e.g. direct DB write or a future admin tool that bypasses `create_stipend_rule`.)
4. `get_or_create_wallet(...)` — **may create a new wallet as a side effect of this trigger**, itself writing a `PARTICIPANT_WALLET_CREATED` audit entry before this method's own logic proceeds.
5. **Wallet-level anti-coercion re-check:** if `wallet.anti_coercion_cap is not None and rule.amount > wallet.anti_coercion_cap` → raise `PaymentsValidationError` (422) **with no audit log entry** — asymmetric with step 3, which does log before raising.
6. Compute `stipend_amount = rule.amount`; `new_balance = round(current_balance + stipend_amount, 2)`; update `current_balance`, `lifetime_stipends`.
7. **If `rule.taxable`:** `taxable_ytd = round(taxable_ytd + stipend_amount, 2)`. If the *new* `taxable_ytd >= 600.0`: set `is_1099_threshold_exceeded = True`; if `w9_status == "NOT_REQUIRED"` → `w9_status = "REQUIRED"`; write an `IRS_1099_THRESHOLD_EXCEEDED` audit entry using `version_index = wallet.version_index + 1` (anticipating the increment that happens in the next step — order-dependent, fragile if refactored).
8. `wallet.version_index += 1`; `session.add(wallet)` (not yet committed).
9. Determine `transaction_type`/description branching on `rule.is_milestone`.
10. Insert `ParticipantTransactionModel` with `reference_id = event_id` (the idempotency key checked in step 1).
11. **Single commit** for the wallet + transaction (and the 1099-threshold audit log from step 7, which itself commits independently inside `record_payment_audit_log` — meaning step 7's audit write and the wallet/transaction commit in step 11 are **two separate transactions**, not one atomic unit: if the process crashes between them, the audit log can exist without the corresponding balance update having been durably committed, or vice versa).
12. Write the final `PARTICIPANT_STIPEND_CREDITED` audit entry.
13. Return `{"status": "CREDITED", ...}`.

### 2.3 Travel Expense Claims & OCR

**`extract_receipt_ocr(image_base64=None, raw_text=None, category_hint=None, filename=None)`** — synchronous, not a real OCR/vision call; a rules-based text parser.
1. `text = raw_text or ""`.
2. If `image_base64` given and no `raw_text`: base64-decode, UTF-8 decode with `errors="ignore"`; if decoded length > 10, use it as `text`. Any exception during decode is swallowed (`except Exception: text = ""`).
3. If `text` is still empty → substitute a **hardcoded demo receipt** ("METRO TRANSIT AUTHORITY... Total: $28.50...").
4. Extract `expense_date` by trying three regexes in order (`YYYY-MM-DD`, then `M/D/YY(YY)`, then `D-Mon-YYYY`); first match wins; **no format normalization** — the raw matched substring is stored as-is, so the three patterns can yield differently-shaped date strings for otherwise-equivalent dates. If none match → `datetime.now(UTC).strftime("%Y-%m-%d")`.
5. Extract `gross_amount`: regex `(?:Total|Amount|Balance|Paid|Charge|Fare|Due)?\s*[:$]?\s*(\d+\.\d{2})` (case-insensitive) over the whole text, take the **last** match (`amount_matches[-1]`); default `25.0` on no match or parse failure.
6. Determine `merchant_name`: first non-blank line of `text`, title-cased, if its length is strictly between 3 and 40 chars; else a category-specific hardcoded name (assigned inside the category `elif` chain below) or `"Metro Transit Authority"`.
7. Category detection — **first-match-wins, in this fixed order**: `MILEAGE` (shell/chevron/bp/fuel/gas/mileage) → `PARKING` (parking/garage/valet) → `MEAL` (cafe/restaurant/starbucks/diner/meal/food/dinner/lunch/breakfast) → `LODGING` (hotel/inn/marriott/hilton/lodging/motel/suites) → `TRANSIT` (uber/lyft/taxi/transit/metro/cab/train/bus); default `category_hint or "TRANSIT"` if none match.
8. **Meal-excess tax rule:** if `category == "MEAL"` and `gross_amount > 75.0` → `tax_deduction = round((gross_amount - 75.0) * 0.15, 2)` (both `75.0` and `0.15` are undocumented magic numbers, not sourced to a PRD/IRS citation in code).
9. `net_amount = round(gross_amount - tax_deduction, 2)`.
10. Returns a `ReceiptOcrResult` (not persisted — this method does not touch the DB).

**`submit_expense_claim(subject_id, study_id, site_id, expense_date, category, merchant_name, gross_amount, tax_deduction=0.0, net_amount=None, ..., is_meal_allowance=False, per_diem_cap=None, wallet_id=None, ...)`**
1. If `wallet_id` not supplied → `get_or_create_wallet(...)` and use its id.
2. `meal_flag = is_meal_allowance or category == "MEAL"`.
3. **Per-diem override rule:** if `meal_flag` and `per_diem_cap is not None` and `gross_amount > per_diem_cap` and **`tax_deduction == 0.0`** (used as a "not explicitly set" sentinel — a caller-supplied `tax_deduction=0.0` is indistinguishable from "omitted" and will still trigger this recompute) → `effective_tax = round((gross_amount - per_diem_cap) * 0.15, 2)` — same `0.15` constant as the OCR meal-excess rule (§2.3 step 8), duplicated rather than shared.
4. `calculated_net = net_amount if net_amount is not None else round(gross_amount - effective_tax, 2)` — **if the caller supplies `net_amount`, it is trusted verbatim; the server never validates `net_amount == gross_amount - effective_tax`.**
5. Insert `ParticipantExpenseClaimModel` with `status = "PENDING_REVIEW"`, commit, refresh, write `EXPENSE_CLAIM_SUBMITTED` audit entry, return the claim.

**`review_expense_claim(claim_id, review_status, reviewer_id, ..., adjusted_amount=None, adjusted_category=None, adjusted_merchant=None, ...)`**
1. Load claim by id → `ClaimNotFoundError` (404) if missing.
2. If `claim.status in ("APPROVED", "DISBURSED")` → `PaymentsValidationError` (422) — **`"DISBURSED"` is never a reachable status anywhere in this codebase**; the guard is effectively dead code against that branch today but is part of the documented contract.
3. Set `status = review_status` (**any string is accepted — no enum validation**), `reviewed_by`, `reviewed_at = now`, `review_notes`.
4. If `adjusted_amount is not None`: `gross_amount = adjusted_amount`; `net_amount = round(adjusted_amount - claim.tax_deduction, 2)` — **reuses the claim's original `tax_deduction` computed at submission time; does not re-run the meal-excess/per-diem recompute against the new amount**, so an adjustment can produce a `net_amount` inconsistent with the meal/per-diem tax rules that would apply to the adjusted total.
5. `version_index += 1`, `session.add(claim)` (not yet committed).
6. **Branch strictly on the literal string `"APPROVED"`** (not on any status enum):
   - **Approved branch:** load wallet by `claim.wallet_id`; if found, credit `current_balance += net_amount`, `lifetime_reimbursements += net_amount`, `version_index += 1`, insert `ParticipantTransactionModel(transaction_type="REIMBURSEMENT_CREDIT", amount=claim.net_amount, reference_id=claim.id)`. **No idempotency check of any kind before crediting** — calling this endpoint twice for the same `claim_id` with `review_status="APPROVED"` both times will credit the wallet twice, because step 2's guard only blocks a *third* call (once `status` is already `"APPROVED"`), not a concurrent *second* call racing the first before either commits (§4.1, §4.4). Single commit, refresh claim, write `EXPENSE_CLAIM_APPROVED` audit entry.
   - **Any other value:** commit, refresh claim, write `EXPENSE_CLAIM_REJECTED` audit entry (the action constant is always `REJECTED` regardless of what string `review_status` actually was).

### 2.4 Plain-Language Lay Summaries (EU CTR 536/2014)

**`create_or_update_lay_summary(study_id, title, ..., summary_id=None, plain_language_sections=None, status="PUBLISHED", ...)`**
1. If `summary_id` given: load it → `LaySummaryNotFoundError` (404) if missing; overwrite all fields, **`plain_language_sections = plain_language_sections or {}`** (data-loss defect, §1.6/§4.5); `version_index += 1`.
2. Else: build `default_sections` = caller value or the hardcoded 6-key boilerplate template; insert new row with `published_at = now if status == "PUBLISHED" else None`; `version_index = 1`.
3. Commit, refresh, write audit entry — action is `LAY_SUMMARY_CREATED` if this was a create, or the **string literal** `"LAY_SUMMARY_UPDATED"` if an update (note: `"LAY_SUMMARY_UPDATED"` is **not** a member of the `AuditAction` enum defined in `domain/models.py` — it is a raw string that happens to match no declared constant, a drift risk if the enum is ever used as the source of truth for allowed actions elsewhere).

**`publish_lay_summary(summary_id, irb_approval_number=None, irb_approval_date=None, ...)`**
1. Load → `LaySummaryNotFoundError` (404) if missing.
2. **No guard on current status** — publishing an already-published summary is a silent no-op state-wise (fields are simply reassigned to the same/overridden values) that still writes a new audit entry and bumps `version_index`.
3. Set `status = "PUBLISHED"`, `published_at = now`; optionally override `irb_approval_number`/`irb_approval_date` if supplied.

**`confirm_lay_summary_receipt(summary_id, subject_id, study_id, comprehension_score=5, comprehension_feedback=None, confirmation_statement=None, ...)`**
1. Load summary → `LaySummaryNotFoundError` (404) if missing. **No check that `summary.status == "PUBLISHED"`** — a receipt can be confirmed against a `DRAFT` summary.
2. Default `statement` if not supplied (fixed EU CTR Article 37 attestation sentence).
3. `now = datetime.now(UTC)`; build pipe-delimited `manifest` string (§1.6) and SHA-256 hex digest.
4. Insert `LaySummaryReceiptModel`, commit, refresh, write `LAY_SUMMARY_RECEIPT_CONFIRMED` audit entry.

### 2.5 HTTP Error Envelope (applies to every endpoint in `participant.py`)

Domain exceptions raised by the service are **not caught in the router** — they propagate to the app-level `DomainError` handler registered in `apps/payments/main.py` via `register_rfc7807_handlers`, which returns an **RFC 7807 `application/problem+json`** body:

```json
{
  "type": "https://cadence.clinical/errors/<slug>",
  "title": "<Title>",
  "status": <int>,
  "detail": "<exception message>",
  "instance": "<request URL>",
  "extensions": {}
}
```

| Domain Exception | HTTP Status | `type` slug | `title` |
|---|:---:|---|---|
| `WalletNotFoundError`, `ClaimNotFoundError`, `LaySummaryNotFoundError` (all `EntityNotFoundError`) | 404 | `entity-not-found` | Entity Not Found |
| `PaymentsValidationError` (`ValidationError`) | 422 | `validation-error` | Validation Error |
| `PaymentsConflictError` (`ConflictError`) — not raised anywhere in this slice today | 409 | `conflict` | Conflict |
| `PaymentsDatabaseError` (`DatabaseError`) — not explicitly raised in this slice; a raw SQLAlchemy error is **not** a `DomainError` and is **not** caught by this handler | 500 (FastAPI default, unstructured) | — | — |

Two router-local `HTTPException` calls bypass the RFC 7807 shape entirely and return FastAPI's default `{"detail": "..."}` body instead: 404 for `get_participant_wallet_by_id`/`get_participant_wallet_by_subject`/`get_lay_summary` when the service returns `None` rather than raising, and 403 for `check_coordinator_or_financial_write_access`/`check_financial_write_access`. **This is a real inconsistency to preserve or deliberately resolve**: some not-found paths raise a typed domain exception (RFC 7807 body), others return `None` and the router raises a bare `HTTPException` (plain `{"detail": ...}` body) — a .NET client cannot assume one error shape for all 404s from this slice.

---

## 3. Side Effects & External Calls

| Operation | DB Writes (table, and whether same-transaction) | Other Side Effects |
|---|---|---|
| `get_or_create_wallet` (new) | `payments_participant_wallets` insert + `payments_audit_logs` insert — **two separate commits** (wallet commit happens before the audit-log helper's own `commit()`) | None |
| `update_wallet_payout_methods` | wallet update + audit insert — two separate commits | None |
| `issue_reloadable_debit_card` | wallet update + audit insert — two separate commits | None |
| `submit_w9_form` / `verify_w9_form` | wallet update + audit insert — two separate commits | None |
| `create_stipend_rule` | rule insert/update + audit insert — two separate commits | None |
| `process_visit_verified_stipend` | up to: wallet insert (via `get_or_create_wallet`, its own commit) → optional `ANTI_COERCION_CAP_EXCEEDED` audit insert (own commit, only on the failure path) → optional `IRS_1099_THRESHOLD_EXCEEDED` audit insert (own commit) → **single combined commit** of wallet update + transaction insert → final `PARTICIPANT_STIPEND_CREDITED` audit insert (own commit) | Up to **5 separate DB transactions** across one logical operation — no overarching transaction/isolation boundary; a crash mid-sequence leaves partial state (e.g., wallet created but no transaction, or 1099-threshold audit logged but balance not yet committed). |
| `submit_expense_claim` | optional wallet insert (own commit) + claim insert + audit insert — separate commits | None |
| `review_expense_claim` (approved) | claim update + wallet update + transaction insert (**single commit**) + audit insert (separate commit) | None |
| `review_expense_claim` (other) | claim update (commit) + audit insert (separate commit) | None |
| `disburse_funds` | wallet update + disbursement insert + transaction insert (**single commit**) + audit insert (separate commit) | None |
| `create_or_update_lay_summary` | summary insert/update + audit insert — separate commits | None |
| `publish_lay_summary` | summary update + audit insert — separate commits | None |
| `confirm_lay_summary_receipt` | receipt insert + audit insert — separate commits | None |
| `extract_receipt_ocr` | **None** — pure function, no DB access | None (regex-only, no external OCR/vision service call) |

**No outbound HTTP calls, no message-broker events, no cache reads/writes, and no third-party integrations exist anywhere in this slice.** (Contrast with `apps/payments/services/billing_and_erp_gateway.py`, which is out of scope for this spec but does own the ERP webhook/export side effects for the sibling `Cadence.Payments` sub-domains.) Every "commit" listed above uses SQLAlchemy's default `AsyncSession` isolation (no explicit `isolation_level=` override anywhere in this file), and **no row is ever locked with `SELECT ... FOR UPDATE`** — every read-then-write sequence in this file is optimistic/unguarded (§4.1, §4.4).

---

## 4. Python Runtime Quirks & Risk Flags

**4.1 — Read-check-then-write races with no DB-level guard.** None of `(study_id, subject_id)` on wallets, `(study_id, visit_code)` on active stipend rules, or `reference_id` on transactions carries a unique constraint. Every "idempotency" or "get-or-create" behavior in this service (`get_or_create_wallet`, the stipend-rule upsert, and the `process_visit_verified_stipend` dedup-by-`event_id` check) is a plain SELECT followed by an INSERT/UPDATE with no `SELECT ... FOR UPDATE`, no unique index, and no retry/conflict handling. Two concurrent requests (e.g., a webhook redelivery) can both pass the "not found"/"not yet processed" check and both write, producing duplicate wallets or double-credited stipends. A .NET port must decide whether to add real uniqueness/locking (recommended) or knowingly preserve the race for behavioral parity during transition.

**4.2 — Hardcoded, not-actually-observed `previous_state` in W-9 audit entries.** `submit_w9_form` always audits `previous_state={"w9_status": "REQUIRED"}` and `verify_w9_form` always audits `previous_state={"w9_status": "SUBMITTED"}`, regardless of the wallet's actual prior value. Under 21 CFR Part 11, an audit trail is expected to record what *actually changed*; this is a latent audit-integrity defect that pre-exists the .NET migration and should be explicitly flagged to product/compliance rather than silently reproduced.

**4.3 — Falsy-zero sentinel usage.** `submit_expense_claim` treats caller-supplied `tax_deduction == 0.0` as "not provided" to decide whether to run the per-diem-overage recompute (§2.3 step 3). A legitimately-zero client-computed tax deduction is indistinguishable from "omitted," silently triggering a server-side recompute the caller did not ask for. In .NET this requires an explicit nullable (`decimal?`) rather than relying on a zero-value sentinel — note the DTO layer already allows `null` (`net_amount: float | None`) for a different field but not for `tax_deduction`, which has a real default of `0.0`, not `None`.

**4.4 — Implicit, type-unsafe sign convention on the transaction ledger.** `ParticipantTransactionModel.amount` is positive for credits and negative for `DISBURSEMENT_DEBIT` rows. Nothing enforces this beyond the two call sites that happen to construct it correctly today. A .NET `Cadence.Payments` transaction ledger should make the direction an explicit, exhaustively-matched concept (e.g., a `TransactionDirection` enum plus an unsigned magnitude) rather than reproducing an implicit sign convention.

**4.5 — Silent data loss on lay-summary update.** Confirmed defect (§1.6, §2.4): updating a `LaySummaryModel` without supplying `plain_language_sections` overwrites existing rich content with `{}`. This must be an explicit, reviewed fix-or-replicate decision, not something the .NET implementation inherits by accident of a literal line-by-line transcription.

**4.6 — Loosely-typed domain model layer vs. strictly-typed DB layer.** `apps/payments/domain/models.py`'s Pydantic mirrors of these entities (e.g. `ParticipantWalletModel` in the *domain* module, distinct from the *infrastructure* SQLAlchemy class of the same name) type every timestamp as `Any | None` and allow `extra="allow"` on most entities — meaning the domain-layer Pydantic model imposes essentially no structural validation beyond what's explicitly declared, and will silently accept and round-trip arbitrary extra keys. The DTOs in `presentation/dtos.py` (which actually gate the HTTP boundary) are the real source of validation truth for this slice, not the domain models — a naming/layering trap worth flagging explicitly since "domain model" ordinarily implies the authoritative invariant layer.

**4.7 — Dead/no-op API parameter.** `issue_reloadable_debit_card`'s `initial_load` (and the corresponding `IssueReloadableCardRequest.initial_load` DTO field, validated `ge=0.0`) is accepted end-to-end through the HTTP layer but never applied to wallet balance or recorded as a transaction anywhere in the service. A client relying on this parameter to fund a newly-issued card will silently get no funds and no error.

**4.8 — Authorization gaps on money-moving endpoints.** `GatewayAuthMiddleware` (`packages/security/middleware.py`) only verifies that a request carries a valid HMAC-signed gateway identity (`X-User-Id`/`X-User-Roles`/`X-Gateway-Signature`/`X-Gateway-Timestamp`); it performs **no per-endpoint permission check**. Endpoint-level authorization in this router is opt-in via `check_financial_write_access`/`check_coordinator_or_financial_write_access`, called only on `create_participant_stipend_rule` and `verify_participant_w9_form`. **`disburse_participant_funds` (dispatches real money) and `review_participant_expense_claim` (can credit a wallet on approval) call neither** — any authenticated caller that reaches the gateway with any role can invoke them. This should be confirmed with product/security as intentional (e.g., enforced further upstream by role-scoped gateway routing not visible in this file) before the .NET port either replicates or tightens it — flagged here as the single highest-severity behavioral question in this slice.

**4.9 — Magic numbers with no cited source, duplicated across two call sites.** The `$75` meal-receipt excess threshold, the `15%` excess/overage tax rate (appears identically in both `extract_receipt_ocr` and `submit_expense_claim`), and the `$600`/`$500` IRS 1099 threshold/warning-approach values are all inline literals with no shared constant and no PRD/regulation citation in-line (contrast with the anti-coercion cap, which cites `PRD-SYS-182/45 CFR 46` directly in its error message). Confirm the correct authoritative source for each before transcription; do not assume they're current IRS/protocol figures without verification.

**4.10 — Timezone handling is correct and should be preserved as the target-state contract**, unlike the float-precision issue: every write uses `datetime.now(UTC)`, and `UTCDateTime` rejects naive datetimes at the DB boundary. The one exception is `expense_date`/`irb_approval_date`/`period_start`/`period_end`-style fields, which are raw, unvalidated strings, not `datetime`/`date` — a deliberate scope note for the .NET schema (`DateOnly` vs `string`) rather than a defect.

---

## 5. Parity Test Matrix

Legend: **P** = must match Python's exact (including flawed) behavior bit-for-bit for cutover-safety regression testing; **D** = behavior that should be corrected per ADR-2239/product decision, so the .NET test asserts the *corrected* outcome and the divergence from Python must be logged as a known, reviewed difference — not silently matched.

| # | Scenario | Python Reference Behavior | .NET Test Assertion | Class |
|---|---|---|---|---|
| 1 | `get_or_create_wallet` called twice with identical `(study_id, subject_id)` sequentially | 2nd call returns the same wallet, no 2nd audit entry | Same | P |
| 2 | Two concurrent `get_or_create_wallet` calls, identical `(study_id, subject_id)`, no existing row | Python: race can create 2 wallets (no unique constraint) | .NET: exactly 1 wallet created; 2nd caller gets the existing row or a well-defined conflict | D |
| 3 | `create_stipend_rule` with `amount=100, anti_coercion_cap=50` | 422 `PaymentsValidationError`, message includes both values formatted `.2f` | Same message content and 422 | P |
| 4 | `process_visit_verified_stipend` called twice with the same `event_id` | 2nd call returns `{"status": "ALREADY_PROCESSED", ...}`, no new transaction, no balance change | Same | P |
| 5 | `process_visit_verified_stipend` for a `visit_code` with no active rule | 200 response `{"status": "SKIPPED", "reason": "..."}` — **not an error** | Same (not an exception/4xx) | P |
| 6 | `process_visit_verified_stipend` with `rule.taxable=True`, pushes `taxable_ytd` from `550.0` to `650.0` | `is_1099_threshold_exceeded=True`, `w9_status` flips `NOT_REQUIRED → REQUIRED`, `IRS_1099_THRESHOLD_EXCEEDED` audit entry written | Same transition and audit entry | P |
| 7 | Stipend credit where `round(current_balance + stipend_amount, 2)` lands on a binary-float rounding boundary (e.g. `current_balance=10.005`) | Python float `round()` result (verify exact value empirically before writing the assertion) | `decimal` + `MidpointRounding.ToEven` result — **will differ from Python for boundary inputs**; assert the corrected value, and separately record the Python value that would have resulted, for the migration changelog | D |
| 8 | `submit_w9_form(certify_accurate=False)` | 422, no mutation, no audit entry | Same | P |
| 9 | `submit_w9_form` audit `previous_state` | Always `{"w9_status": "REQUIRED"}` regardless of actual prior value (§4.2 defect) | Correct actual prior `w9_status` in the audit record | D |
| 10 | `verify_w9_form` called on a wallet whose `w9_status` is still `NOT_REQUIRED` | Succeeds unconditionally, sets `VERIFIED`, no error | Decide: replicate (no guard) or add a state-machine guard — must be an explicit product decision, tested either way | D (decision required) |
| 11 | `issue_reloadable_debit_card(initial_load=500.0)` | Card issued, wallet balance **unchanged** (§4.7 dead parameter) | Same, unless product decides to wire it up — if wired up, this becomes a D case | P (as documented; flag for product review) |
| 12 | `disburse_funds(amount=0)` | 422 "Disbursement amount must be greater than zero." | Same | P |
| 13 | `disburse_funds(amount=balance + 0.01)` | 422 "Insufficient funds: Requested $X but available balance is $Y." (both `.2f`) | Same message shape | P |
| 14 | Two concurrent `disburse_funds` calls for the same wallet, each individually valid against the balance but not against each other combined | Python: no lock — both can succeed, over-drawing the wallet (§4.4/§4.8) | .NET: second call must fail with insufficient funds (transactionally serialized) | D |
| 15 | `extract_receipt_ocr(raw_text="...Total: $12.34...Balance: $56.78")` | `gross_amount = 56.78` (last regex match wins) | Same extraction result | P |
| 16 | `extract_receipt_ocr` with both "gas" and "parking" keywords present in text | `category = "MILEAGE"` (fuel/gas checked before parking in the `elif` chain) | Same category resolution order | P |
| 17 | `extract_receipt_ocr(raw_text=None, image_base64=None)` | Returns the hardcoded demo receipt result (`$28.50`, Metro Transit Authority, TRANSIT) | Same fallback | P |
| 18 | `extract_receipt_ocr` meal receipt, `gross_amount=100.0` | `tax_deduction = round((100-75)*0.15, 2) = 3.75`, `net_amount = 96.25` | Same (or corrected-decimal equivalent per ADR-2239 — flag if diverges) | P/D |
| 19 | `submit_expense_claim(category="MEAL", is_meal_allowance=False, gross_amount=100, per_diem_cap=75, tax_deduction=0.0)` | Per-diem override fires (since `tax_deduction==0.0` sentinel), `effective_tax=3.75` | Confirm intended semantics with product; if `0.0` should mean "explicitly zero," this is a D case requiring a nullable field | D (decision required) |
| 20 | `submit_expense_claim(gross_amount=100, tax_deduction=10, net_amount=999)` | Stored verbatim as `net_amount=999` — **no consistency validation** against `gross - tax` | Decide: replicate (no validation) or reject/recompute — explicit decision required | D (decision required) |
| 21 | `review_expense_claim` called twice concurrently with `status="APPROVED"` for the same `claim_id` | Python: no idempotency guard — both can credit the wallet (double credit) | .NET: second concurrent call must not double-credit | D |
| 22 | `review_expense_claim(claim_id, status="APPROVED")` on a claim already `"APPROVED"` | 422 "already APPROVED" | Same | P |
| 23 | `review_expense_claim(..., adjusted_amount=X)` where `X` would trigger a different meal-excess tax than the original submission | `net_amount = round(X - claim.tax_deduction, 2)` — original `tax_deduction` is **not** recomputed for the new amount (§2.3 step 4 defect) | Decide: replicate or recompute tax on adjustment — explicit decision required | D (decision required) |
| 24 | `review_expense_claim(status="SOME_ARBITRARY_STRING")` | Accepted; takes the "rejected" audit branch; `claim.status` stored as the literal arbitrary string | Decide: replicate free-text status or constrain to an enum (`Approved`/`Rejected`) — explicit decision required | D (decision required) |
| 25 | `create_or_update_lay_summary(summary_id=X, ...)` omitting `plain_language_sections` on an existing summary that has content | Existing content **overwritten with `{}`** (§4.5 defect) | Fix: preserve existing content when the field is omitted on update | D |
| 26 | `publish_lay_summary` called on an already-`PUBLISHED` summary | Succeeds silently, re-sets `published_at=now`, writes a new audit entry | Same, unless product wants a guard (decision) | P |
| 27 | `confirm_lay_summary_receipt` against a `DRAFT` (unpublished) summary | Succeeds — no status guard | Decide: replicate or reject — explicit decision required | D (decision required) |
| 28 | `confirm_lay_summary_receipt` hash reproducibility | SHA-256 of a pipe-delimited string including Python `datetime.isoformat()` (`...+00:00`, up to 6 fractional digits) | If any code path needs to reproduce/verify a *Python-era* receipt hash, the .NET implementation must format the embedded timestamp identically to Python's `isoformat()`, not `DateTimeOffset:"o"` | P (for historical-record verification only; new records use whatever canonical format ADR's Timestamp Normalization Invariant settles on) |
| 29 | `get_1099_tax_status` at `taxable_ytd=599.99` vs `600.00` vs `500.00` | `warning_level` = `APPROACHING`, `EXCEEDED`, `APPROACHING` respectively (boundary is `>=`, not `>`) | Same boundary semantics | P |
| 30 | `disburse_participant_funds` / `review_participant_expense_claim` invoked by a principal with no financial-write permission (e.g., role `Subject`) | Succeeds (no permission check present — §4.8) | Decide with product/security: replicate the current (permissive) behavior or require `payments:write`/equivalent — explicit decision required before go-live, not an incidental transcription outcome | D (decision required, high priority) |
| 31 | GET endpoints returning `None` from the service (`get_participant_wallet_by_id`, `get_participant_wallet_by_subject`, `get_lay_summary`) | Router raises bare `HTTPException(404, detail=...)` → plain `{"detail": "..."}` body, **not** RFC 7807 shape | Same inconsistency, or standardize on RFC 7807 everywhere — explicit decision required (affects any client parsing error bodies) | D (decision required) |

---

## 6. Open Questions for Product/Compliance Before Transcription

1. Should `disburse_funds` and `review_expense_claim` (approve path) require an explicit financial-write permission, given they are the two real money-movement endpoints in this slice and currently have none (§4.8, Matrix #30)?
2. What is the intended behavior when `plain_language_sections` is omitted on a lay-summary update — preserve existing content, or is the current wipe-to-`{}` actually desired (§4.5, Matrix #25)?
3. Should `net_amount` supplied by the client on an expense claim be validated against `gross_amount - tax_deduction`, or is client-trusted override intentional (§4.3, Matrix #20)?
4. Is `tax_deduction=0.0` ever expected to mean "explicitly zero, do not recompute," and if so should the field become nullable (§4.3, Matrix #19)?
5. Confirm authoritative sourcing for the `$75`/`15%` meal-excess constants and the `$500`/`$600` IRS-1099 thresholds (§4.9) — are these current, and should they be configuration rather than code constants?
6. Per ADR-2239's Universal Decimal Precision Standard: confirm this entire slice (wallets, stipends, expense claims, disbursements, transactions) is in scope for `decimal` conversion (it is squarely "dosage/lab" adjacent language does not apply here, but the standard's title and `CONTEXT.md` wording should be checked against this slice explicitly, since AUD-NET10-002 found the standard's actual code remediation was scoped only to `apps/payments/domain/models.py`'s already-existing fields, not verified against this specific service's arithmetic).
