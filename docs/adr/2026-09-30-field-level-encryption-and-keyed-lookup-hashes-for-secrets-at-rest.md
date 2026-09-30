# ADR-2494: Field-Level Encryption and Keyed Lookup Hashes for Secrets at Rest

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter, Claude Code
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001

---

## 1. Context & Problem Statement

Issue #4770 found payment, device and signature secrets stored in plaintext columns:

- `apps/payments`: `ParticipantWallet.VirtualCardToken` and `ParticipantWallet.AchRoutingNumber`
  (`payments_participant_wallets`).
- `apps/interop`: `DeviceToken` on `DeviceRegistrationRecord`, `SensorTelemetryStreamRecord`,
  `SensorDailySummaryRecord` and `SensorSafetyAlertRecord`, each indexed for lookup.
- `apps/econsent`: `ConsentSignature.SignerEmail` and the raw `ConsentSignature.SignatureData` capture
  (`consent_signatures`).

A database dump, a backup, a read replica or anyone with the application's database role could read them. 21 CFR
Part 11 §11.10(c)–(d) (record protection, limited access), PCI DSS requirement 3 (card data at rest), HIPAA §164.312
and GDPR Article 32 all expect these to be unreadable at rest. The kernel specification (§5.1, ADR-2253) moves direct
identifiers into an Identity Vault, but no `IIdentityVault` exists in code yet.

No endpoint or service reads or writes these columns today: the three .NET services expose health endpoints only.
The fix therefore sets the storage rule before any feature starts filling the columns.

## 2. Decision Drivers & Constraints

- **Driver 1 (minimise):** do not store a secret the platform does not need.
- **Driver 2 (fit the access pattern):** values read back need reversible encryption; values only matched need a
  one-way keyed hash that still supports an index.
- **Driver 3 (fail closed):** no hardcoded or default keys, and a missing key stops the service at startup (as
  #4765 did for the gateway secret).
- **Driver 4 (one implementation):** one shared, tested primitive in `packages/security` rather than per-service code.
- **Constraint:** SQL migrations cannot see the keys, and must never embed one.

## 3. Options Considered

### Option 1: PostgreSQL `pgcrypto` column encryption

- **Overview:** Encrypt in SQL with `pgp_sym_encrypt` and a key passed per session.
- **Pros:**
  - ✅ Existing rows could be encrypted inside the migration.
- **Cons:**
  - ❌ The key travels to the database server and can appear in logs, `pg_stat_statements` and the migration itself.
  - ❌ Requires an extension and provider-specific SQL in every query.

### Option 2: ASP.NET Core Data Protection with EF Core value converters

- **Overview:** Use `IDataProtector` per column behind a value converter.
- **Pros:**
  - ✅ Built-in key ring with rotation.
- **Cons:**
  - ❌ The key ring needs its own persisted, shared, encrypted store for every replica; by default it is generated
    and kept per machine, which is a silent fallback key.
  - ❌ No keyed-hash primitive for lookups.

### Option 3: Shared AES-256-GCM encryptor and HMAC-SHA256 lookup hasher with configured keys (Selected)

- **Overview:** `Cadence.Security.FieldProtection` provides `IFieldEncryptor` (AES-256-GCM, 96-bit random nonce,
  128-bit tag, key id and column purpose as associated data, stored as `enc:v1:<keyId>:<base64>`), `ILookupHasher`
  (HMAC-SHA256 over `purpose ‖ 0x00 ‖ value`, 64 hex characters), an EF Core `EncryptedStringConverter`, and DI
  registration that validates `FIELD_ENCRYPTION_KEY` / `FIELD_LOOKUP_HASH_KEY` (base64, exactly 32 bytes) on start.
- **Pros:**
  - ✅ Keys never leave the application; the database only ever sees ciphertext or hashes.
  - ✅ Ciphertext moved to another column, or relabelled with another key id, fails authentication.
  - ✅ Blind-index hashes keep equality lookups and indexes working without the raw value.
  - ✅ The key id in each value makes a later rotation (or a KMS-wrapped key) possible without a format change.
- **Cons:**
  - ❌ Existing plaintext rows cannot be encrypted by a SQL migration.
  - ❌ Encrypted columns cannot be searched; rotation needs a re-encryption job (not built yet).

## 4. Decision Outcome

- **Chosen Option:** Option 3, applied per field according to what the platform needs:

| Field | Need | Stored as |
| --- | --- | --- |
| `VirtualCardToken` | processor handles the card | replaced by `VirtualCardProcessorReference`, encrypted; `VirtualCardMask` for display |
| `AchRoutingNumber` | processor holds bank details | not stored; `AchProcessorReference` (encrypted) plus `AchRoutingMask` (last four digits) |
| `DeviceToken` (4 tables) | match a presented token | `DeviceTokenHash`, HMAC-SHA256 via `DeviceTokenHasher`, indexed; no encrypted copy because nothing reads the raw token back |
| `SignerEmail` | contact the signer | encrypted in place (moves to the Identity Vault, ADR-2253, when it exists) |
| `SignatureData` | prove which capture signed which content | not stored; `SignatureBindingSha256` = SHA-256 of `cadence.econsent.signature-binding.v1\n<DigestSha256>\n<capture>` (`ConsentSignatureBinding`) |

- **Justification:** Option 3 is the only option that keeps keys out of the database, has no implicit fallback key
  and covers both reversible and lookup-only secrets with one tested implementation.
- **EF Core model caching:** converters capture the context's encryptor, so each encrypting context implements
  `IFieldEncryptingDbContext` and replaces `IModelCacheKeyFactory` with `FieldEncryptionModelCacheKeyFactory`,
  which keys the cached model by encryptor key id. Design-time factories use `DesignTimeFieldEncryptor`, which
  builds the model but throws on any conversion.

### Existing rows (migrations)

| Migration | Transformed | Cleared |
| --- | --- | --- |
| payments `StoreProcessorReferencesInsteadOfPaymentSecrets` | routing number → `AchRoutingMask` (`*****` + last four digits) | `VirtualCardToken`, `AchRoutingNumber` (processor references start empty and are re-linked from the processor) |
| interop `HashDeviceTokensAtRest` | none | `DeviceToken` in all four tables; `DeviceTokenHash` is `''` until the device is re-registered. Clinical content of every row is kept. |
| econsent `ProtectSignerEmailAndBindSignatureData` | `SignatureData` → `SignatureBindingSha256` in SQL (same expression as `ConsentSignatureBinding.Compute`) | `SignatureData`; plaintext `SignerEmail` (contact data, not part of the Part 11 manifest, which keeps printed name, time and meaning) |

Rationale for clearing instead of transforming: encryption and HMAC need the keys, which SQL migrations must not
have; an unkeyed hash of a token would be brute-forceable and could not be matched by the application; and plaintext
left in an encrypted column would fail to decrypt on read. No code path wrote these columns, so no production rows
are expected. Each migration blanks the plaintext before dropping or retyping the column, so no live row version
still holds it. `Down` restores the old columns empty.

## 5. Consequences & Trade-offs

- **Positive Impact:** a database dump, replica or backup taken after the migration holds no usable card
  reference, bank number, device token, signer e-mail or signature capture. The keys are required configuration and
  are validated at startup.
- **Negative Impact / Technical Debt:**
  - Rotating `FIELD_ENCRYPTION_KEY` makes existing ciphertext unreadable until a re-encryption job exists;
    rotating `FIELD_LOOKUP_HASH_KEY` orphans stored hashes. Both are documented as such.
  - Keys come from configuration, not a KMS; per-record data keys (as ADR-2253 plans for the vault) are future work.
  - `apps/execution` keeps its own `AesGcmDemographicsEncryptionService`; it can move onto this package later.
- **Mitigation Strategy:**
  - **Operators, after applying the migrations:** run `VACUUM FULL` on `payments_participant_wallets`,
    `consent_signatures`, `device_registrations`, `sensor_telemetry_streams`, `sensor_daily_summaries` and
    `sensor_safety_alerts` so dead row versions holding the old values are removed from the heap files. WAL segments,
    base backups and replicas taken before the migration still contain the plaintext and must be expired under the
    backup retention policy.
  - Keys are supplied from the secret store in every non-development environment; `docker/docker-compose.yml`
    carries clearly labelled local-development-only values.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/security` (`FieldProtection`), `packages/testing`
  (`TestFieldProtection`, `StoredRowReader`), `apps/payments`, `apps/interop`, `apps/econsent`, `.env.example`,
  `docs/ENVIRONMENT_VARIABLES.md`, `docker/docker-compose.yml`.
- **Verification Plan:**
  - `tests/Packages/FieldProtectionTests.cs`: round trip, randomised ciphertext, wrong purpose/key/tampering/plaintext
    rejected, invalid keys rejected, keyed and purpose-separated hashes, host start fails without keys.
  - `tests/Packages/FieldProtectionHostWiringTests.cs`: the real payments, econsent and interop composition roots
    resolve the configured encryptor/hasher and register fail-closed validation for their key.
  - `tests/Payments/ParticipantWalletSecretsAtRestTests.cs`, `tests/Econsent/ConsentSignatureSecretsAtRestTests.cs`,
    `tests/Interop/DeviceTokenAtRestTests.cs`: rows read back with raw SQL (bypassing converters) never contain the
    plaintext; the model has no raw-secret column; wrong-key reads fail; hashes find rows across tables; the
    signature binding matches an independently computed SHA-256 vector.
  - The three migrations were applied to PostgreSQL 16 over seeded plaintext rows, rolled back and re-applied; after
    `VACUUM FULL` the table files contained none of the seeded secrets, and the SQL-computed signature bindings
    matched `ConsentSignatureBinding.Compute`.
  - The tests carry no requirement tag: PRD-SYS-053 covers agent lifecycle hooks, and no PRD requirement names
    field-level protection yet.
