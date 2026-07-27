# Lab Reference Range Management - Operational & Technical Guide

**Document ID:** CC-OPS-LAB-001
**Version:** 1.0.0
**Status:** Approved
**Classification:** Restricted (GxP / Confidential)
**Applicability:** DevOps, SRE, Data Managers, Clinical Operators, System Administrators

---

## 1. Executive Summary

This guide describes the operational, technical, and regulatory mechanisms governing laboratory reference range management within the Cadence Clinical platform. Laboratory parameters are key safety endpoints in clinical research. This guide enables a system operator or data manager to safely load, revise, deactivate, and recalculate reference ranges, and details how the platform maintains 21 CFR Part 11 / EU Annex 11 compliance throughout these workflows.

---

## 2. Reference Range Authoring Rules & Validation

Laboratory reference ranges are structured rules matched against a subject's demographic profile (age, biological sex) and clinical trial parameters (study identifier, clinical site).

### 2.1 Schema Attributes

A `LabReferenceRange` consists of the following parameters:

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `study_id` | String | Yes | Unique clinical trial study identifier (e.g., `STUDY-123`). |
| `test_code` | String | Yes | Standard laboratory parameter code (e.g., `WBC`, `HEMOGLOBIN`, `ALT`). |
| `test_name` | String | Yes | Descriptive parameter name (e.g., `White Blood Cell Count`). |
| `source` | Enum | Yes | Reference range source: `CENTRAL` or `LOCAL`. |
| `site_id` | String | No | Target local site identifier. Must be `None` when `source = CENTRAL`. |
| `unit` | String | Yes | Original reporting unit (e.g., `10^9/L`, `g/dL`). |
| `normalized_unit` | String | Yes | Unified standard unit of measurement mapped under UCUM. |
| `sex_applicability`| Enum | Yes | Sex applicability constraint: `M`, `F`, `ALL`, or `U`. |
| `age_low` | Float | No | Inclusive minimum age bound in completed years. Null signifies no lower limit. |
| `age_high` | Float | No | Inclusive maximum age bound in completed years. Null signifies no upper limit. |
| `low_bound` | Float | No | Inclusive lower limit of normal. |
| `high_bound` | Float | No | Inclusive upper limit of normal. |
| `critical_low` | Float | No | Exclusive critical low alert boundary. |
| `critical_high` | Float | No | Exclusive critical high alert boundary. |

### 2.2 Logical Constraint Enforcements

The API Gateway and Execution service strictly enforce logical and arithmetic boundary constraints during range creation (`POST`) or updates (`PUT`):

1. **Non-Blank Identity Fields:** `study_id`, `test_code`, `test_name`, and `normalized_unit` cannot be empty or contain only whitespace.
2. **Valid Sources:** `source` must be exactly `CENTRAL` or `LOCAL`.
3. **Valid Sex Codes:** `sex_applicability` must be exactly `M`, `F`, `ALL`, or `U`.
4. **Non-Negative Age:** `age_low` and `age_high` must be $\ge 0.0$.
5. **Age Range Consistency:** `age_low` must be $\le$ `age_high` when both are specified.
6. **Normal Range Consistency:** `low_bound` must be $\le$ `high_bound` when both are specified.
7. **Critical Range Consistency:** `critical_low` must be $\le$ `critical_high` when both are specified.
8. **Boundary Alignment Constraints:**
   - If present, `critical_low` must be $\le$ `low_bound`.
   - If present, `critical_high` must be $\ge$ `high_bound`.

Any violation of these constraints will result in an immediate `HTTP 400 Bad Request` with structured validation detail logs.

---

## 3. Specificity Matching & Precedence Resolution

When a new laboratory observation is captured, the platform's selection engine matches the observation with the most specific reference range rule using a deterministic, multi-dimensional score.

### 3.1 Multi-Dimensional Specificity Scoring

A candidate rule is assigned scores along three dimensions. If any dimension scores $0$, the candidate is incompatible and discarded.

#### 1. Site Specificity Score
- **Score 3 (Exact Site Match):** Observation's `lab_source` is `LOCAL`, candidate range is `LOCAL` and candidate `site_id` matches the observation's `site_id` exactly.
- **Score 2 (Generic Local Match):** Observation's `lab_source` is `LOCAL`, candidate range is `LOCAL` but has no specific `site_id` (applies to all sites utilizing local labs).
- **Score 1 (Central Fallback):** Candidate range is `CENTRAL` (applies globally).
- **Score 0 (Incompatible):** In any other condition (e.g., LOCAL ranges never match CENTRAL observations).

#### 2. Sex Specificity Score
- **Score 2 (Exact Sex Match):** Subject's biological sex matches the range's `sex_applicability` exactly (e.g., subject is Male, range is Male).
- **Score 1 (Generic Fallback):** Candidate `sex_applicability` is `ALL`, `U`, `None`, or empty.
- **Score 0 (Incompatible):** Range is for `F` but subject is `M`, or vice-versa.

#### 3. Age Specificity Score
- **Score 3 (Both Bounds Matched):** Subject age is inclusive of both `age_low` and `age_high`.
- **Score 2 (Single Bound Matched):** Subject age matches single-bounded range (e.g., only `age_low` is defined, and subject age $\ge$ `age_low`).
- **Score 1 (No Bounds Matched):** Candidate has no age bounds defined (`age_low = None`, `age_high = None`).
- **Score 0 (Incompatible):** Subject age falls outside of candidate age range.

### 3.2 Deterministic Tie-Breaking Protocol

If multiple candidate ranges obtain identical scores across all three dimensions, the selection engine resolves ties deterministically using the following order of precedence:

1. **Narrowest Age Span:** Choose the range with the smallest age span (`age_high - age_low`).
2. **Highest Lower Age Bound:** Choose the range with the highest `age_low` value.
3. **Lowest Lower Normal Bound:** Choose the range with the lowest `low_bound`.
4. **Alphabetical ID String Sort:** Choose the range that sorts first alphabetically by its unique UUID string.

---

## 4. Laboratory Value Evaluation & Range Indicators

Laboratory values are evaluated against the resolved reference range boundaries using the following inclusion policy:
- **Normal Boundaries (`low_bound`, `high_bound`):** Inclusive.
- **Critical Boundaries (`critical_low`, `critical_high`):** Exclusive.

### 4.1 Indicator State Machine

The derived indicators and out-of-range flag combinations are summarized below:

```
                  Value < critical_low
                  ┌───────────────────────►  LOW LOW (Out-of-Range)
                  │
                  │   Value < low_bound
                  ├───┬───────────────────►  LOW (Out-of-Range)
                  │   │
                  │   │   low_bound <= Value <= high_bound
 Value Input ─────┼───┼───┬───────────────►  NORMAL (In-Range)
                  │   │   │
                  │   │   │   Value > high_bound
                  ├───┼───┼───┬───────────►  HIGH (Out-of-Range)
                  │   │   │   │
                  │   │   │   │   Value > critical_high
                  └───┴───┴───┴───┬───────►  HIGH HIGH (Out-of-Range)
                                  │
                                  ▼
                            No Matched Range
                                  │
                                  ▼
                             None (In-Range)
```

1. **`LOW LOW` (Out-of-Range):** Value is strictly less than `critical_low`.
2. **`HIGH HIGH` (Out-of-Range):** Value is strictly greater than `critical_high`.
3. **`LOW` (Out-of-Range):** Value is strictly less than `low_bound` but $\ge$ `critical_low`.
4. **`HIGH` (Out-of-Range):** Value is strictly greater than `high_bound` but $\le$ `critical_high`.
5. **`NORMAL` (In-Range):** Value falls within normal bounds (inclusive).
6. **`None` (In-Range):** No matching reference range exists. The `lab_indicator` is recorded as `None` and `lab_out_of_range` is set to `False` to prevent false positive clinical alarms.

---

## 5. API Workflows & Operations

All reference range API requests are authenticated and require appropriate GxP headers.

### 5.1 API Headers

```http
X-User-Id: usr_operator01
X-User-Roles: cra
X-Gateway-Timestamp: 1782293812
X-Gateway-Signature: c2ab839f99bc7b2b8109d93eefcf23c91a039bcf...
X-Signature-Version: 2
X-Change-Reason: Manual revision of Hemoglobin bounds for STUDY-123
```

- **Authorized Roles:** Only roles with write privileges (e.g. `cra`, `data_manager`, `admin`) can perform write mutations (`POST`, `PUT`, `DELETE`). Read actions are available to all authorized users.
- **Change Reasons:** Every write action requires a non-empty `X-Change-Reason` header to comply with 21 CFR Part 11 auditing requirements.

### 5.2 API Operations

#### 1. Create a Reference Range
- **Endpoint:** `POST /api/v1/execution/lab-ranges`
- **Request Body:** Standard `LabReferenceRange` JSON.
- **Response Status:** `201 Created`

#### 2. Update an Existing Range
- **Endpoint:** `PUT /api/v1/execution/lab-ranges/{id}`
- **Request Body:** Partial update payload (e.g. `{"low_bound": 4.2}`).
- **Response Status:** `200 OK`
- **Audit Behavior:** Increment range version index. Writes an `UPDATE` record into the relational audit ledger.

#### 3. Soft-Delete a Range
- **Endpoint:** `DELETE /api/v1/execution/lab-ranges/{id}`
- **Response Status:** `200 OK`
- **Internal Action:** Sets `is_deleted = True`. This range is immediately excluded from subsequent active selection matching. Hard deletion is blocked at the database trigger layer.

#### 4. Cohort Recalculation
- **Endpoint:** `POST /api/v1/execution/lab-ranges/recalculate`
- **Request Body:**
  ```json
  {
    "study_id": "STUDY-123",
    "test_code": "HEMOGLOBIN"
  }
  ```
- **Response Status:** `200 OK`
- **Workflow:** For all active observations in the cohort, the engine decrypts subject demographics, resolves the best matching active range, evaluates the value, and updates the observation's snapshot fields (`lab_indicator`, `lab_out_of_range`, `matched_normal_bounds`) and increments the observation's `version` index if changes are detected.

---

## 6. Coexistence with Statistical Outliers

The platform maintains two distinct classification layers on `ClinicalObservation` records:
1. **Lab Indicators:** Clinical flags indicating out-of-range status relative to physiological/demographic norms (`lab_indicator`, `lab_out_of_range`).
2. **Statistical Outlier Flags (`is_outlier`):** Statistical flags indicating that the value lies outside of standard statistical boundaries relative to the cohort's statistical distribution.

These flags **coexist independently**:
- An observation may be physiologically out of range (e.g. `lab_indicator = HIGH`) but not classified as a statistical outlier if the cohort's variance is wide.
- Conversely, an observation may be within normal physiological ranges but still flagged as a statistical outlier if it lies far from the mean of that specific cohort.
- Updates or recalculations of physiological reference ranges **never** interfere with or overwrite `is_outlier` flags, which are managed independently by statistical cohort recalculation endpoints.

---

## 7. Schema Upgrades & Pre-Boot Deployment Steps

When upgrading a legacy platform instance to support reference range features, database schemas must evolve without interrupting live clinical trial operations.

### 7.1 Pre-Boot Schema Expansion

The migration runner executing inside `apps/execution/database/migrate.py` utilizes the safe `upgrade_existing_tables` pre-boot helper to inspect the relational schema and add new snapshot columns to the pre-existing `clinical_observations` table dynamically:

```python
async def upgrade_existing_tables(conn):
    """
    Safely alter the database schema without full-table recreates.
    Checks for column presence using database reflection and runs safe ALTER TABLE commands.
    """
    from sqlalchemy import inspect

    def get_cols(sync_conn):
        insp = inspect(sync_conn)
        return [col["name"] for col in insp.get_columns("clinical_observations")]

    existing_cols = await conn.run_sync(get_cols)

    added_cols = {
        "lab_source": "VARCHAR(50) DEFAULT 'CENTRAL'",
        "lab_site_id": "VARCHAR(255) NULL",
        "lab_indicator": "VARCHAR(50) NULL",
        "lab_out_of_range": "BOOLEAN DEFAULT FALSE",
        "matched_normal_bounds": "TEXT NULL"
    }

    for col, ddl_type in added_cols.items():
        if col not in existing_cols:
            await conn.execute(text(f"ALTER TABLE clinical_observations ADD COLUMN {col} {ddl_type};"))
```

### 7.2 Post-Migration Verification

Verify that migrations were completed correctly by querying the columns metadata in the target environment:

```bash
# Verify schema evolution has succeeded
uv run pytest tests/test_lab_reference_range_persistence.py -k test_schema_evolution_migration_upgrade --no-cov
```

---

## 8. Clinical-Policy Decisions & Configuration Backlogs

Several policy constraints require local operational guidance or are configured as follow-up requirements:

### 8.1 Biological Sex Mapping & Code Normalization
- **Current Behavior:** The engine maps biological sex string variations (such as `MALE`, `BOY`, `MAN`, `M`) to `"M"`, and (`FEMALE`, `GIRL`, `WOMAN`, `F`) to `"F"`.
- **Policy Decision/Risk:** Intersex, non-binary, or transgender subjects may not align cleanly with traditional binary biological reference ranges.
- **Operator Guideline:** For transgender or gender-diverse patients, operators must consult clinical monitoring protocols and register biological sex matching the physiological target values or configure specific gender-neutral (`ALL`) ranges.

### 8.2 Ambiguous Overlapping Ranges
- **Current Behavior:** The selection engine resolves overlapping ranges of equal specificity using deterministic tie-breakers (e.g., narrowest age span, lowest `low_bound`).
- **Policy Decision/Risk:** Overlapping configurations are usually data entry errors. The platform does not reject overlapping rules but warns the data manager.
- **Operator Guideline:** Avoid uploading overlapping range ranges for the same study-site-age-sex cohort. Audit range lists regularly to ensure unique matches are intended.
