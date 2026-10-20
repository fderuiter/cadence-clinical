# ADR-2519: Safety Signature Manifests Are Stored as Text

- **Status:** Accepted
- **Date:** 2026-10-20
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-PVG-008 (physician e-signatures on safety packages), PRD-SYS-052 (signed safety narratives)
- **Issues:** #5482
- **Extends:** [ADR-2518](2026-10-19-dsur-approval-is-its-own-capability-signed-by-the-verified-caller.md) (which
  widened the DSUR column only)

---

## 1. Context & Problem Statement

Five safety approval records keep an `EsignatureManifestId` column: `safety_reporting_evaluations`,
`safety_narratives`, `safety_signal_alerts`, `periodic_safety_reports` and `device_safety_investigations`. The only
signing path today, DSUR approval (ADR-2518), stores the whole signed `GxPSigner` manifest there: the signed content,
the signer, the reason, the timestamp and the RSA signature with its certificate. A manifest is thousands of characters
long. [ADR-2518](2026-10-19-dsur-approval-is-its-own-capability-signed-by-the-verified-caller.md) widened
`periodic_safety_reports` to `text` after approval returned 500 on PostgreSQL; the other four columns were still
`character varying(255)`, so the first signing path on any of them would truncate or fail the same way (21 CFR
11.10(e), 11.70: the signature must stay linked to its record, whole).

## 2. Decision Drivers & Constraints

- A signed manifest must be stored and read back byte for byte, or it no longer verifies.
- One storage convention per module: the DSUR column already holds the manifest, not an id.
- No column today stores a bare id: no code writes the four other columns yet.
- Keep the change to column types; renaming the column touches the DSUR engine, its API response and tests.

## 3. Options Considered

### Option 1: Every manifest column is `text` (Selected)

- **Overview:** the four remaining columns become `text`, like the DSUR one, in one migration
  (`WidenSafetySignatureManifests`).
- **Pros:**
  - ✅ Any signing path added on these records stores the whole manifest, as DSUR does.
  - ✅ Changing `varchar` to `text` on PostgreSQL rewrites no data.
- **Cons:**
  - ❌ The name `EsignatureManifestId` still says "id" for a column that holds the manifest.

### Option 2: Store manifests in a separate table and keep an id in each record

- **Rejected:** no shared signature-manifest store exists for safety (execution's `SignatureManifest` is execution's
  own persistence, ADR-2518 option 2), and building one is a larger change than the defect.

### Option 3: Rename the columns to `EsignatureManifest`

- **Deferred:** it changes the DSUR API response and the record shape the DSUR and ICSR work depends on; it can follow
  once those paths settle.

## 4. Decision Outcome

- **Chosen Option:** Option 1.
- `SafetyConfigurations.cs` maps all five `EsignatureManifestId` properties with `HasColumnType("text")`; migration
  `20261020101549_WidenSafetySignatureManifests` alters the four that were `varchar(255)`.
- The column holds a whole signed manifest, never an id. A future column that holds only an id says so in its name.

## 5. Consequences & Trade-offs

- **Positive Impact:** signing a narrative, a reporting evaluation, a signal review or a device adjudication cannot fail
  or truncate on the column length.
- **Negative Impact / Technical Debt:** the "Id" suffix is misleading until the columns are renamed.
- **Other modules:** the other signature columns found (imaging `Signature`, interop `AuditSignature`, org `Signature`,
  execution `DigitalSignature` and signature tokens) hold HMAC hex digests or tokens, which fit their limits; none
  holds a `GxPSigner` manifest.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `apps/safety` (configuration, migration, schema doc).
- **Verification Plan:** `tests/Cadence.IntegrationTests/Safety/SafetySignatureManifestPostgresTests.cs` on PostgreSQL
  as the application role: all five columns are unbounded `text`, a DSUR approved through the host stores and reads
  back its full manifest, and a full signed manifest round-trips through each of the other four records and still
  verifies. `SafetyPersistencePostgresTests.Migrations_AllApply_AndMatchTheModel` checks the migration matches the
  model.
