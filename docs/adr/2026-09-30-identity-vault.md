# ADR-2458: The IdentityVault

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-SYS-001 (identity reveals and changes are audited, naming the actor), PRD-CRF-013 (direct
  identifiers are encrypted at rest and readable by the tenant only)

---

## 1. Context & Problem Statement

Spec §9.2 (ADR-2253) separates a participant's identity into layers. The kernel holds the pseudonymous layers
(`ParticipantId`, `SubjectCode`). Direct identifiers (name, birth date, contact details, a site's screening log
number) live in an IdentityVault: a separate schema with its own role and envelope encryption. K-IDN-1 says resolving
any other layer needs an `identity.reveal.<layer>` capability and is audited. K-IDN-2 says linking a person across
studies is a vault operation with its own capability and audit. Erasure is crypto-shredding.

Before this decision the kernel had no vault. `kernel.people` carried `email`, `first_name` and `last_name` columns in
the clear, which no command wrote, and no tenant had a residency. This ADR decides how the vault is stored, keyed,
reached and audited (#4782).

## 2. Decision Drivers & Constraints

- **K-IDN-1:** no kernel table or kernel read returns a direct identifier. Revealing one is a command, so the
  pipeline's authorization and audit apply to it like any other act.
- **Least privilege in the database:** the application role must not be able to read vault rows with a plain
  `SELECT`, even through an injection bug in another query.
- **K-ID-1, K-ID-2:** vault rows are tenant-scoped with composite foreign keys and row-level security, like every kernel
  table.
- **Spec §13:** residency is a tenant property. A vault deployed in one region must not seal or open the identities
  of a tenant placed in another.
- **Plaintext stays in memory:** the idempotency request hash, the audit entry and the events must never hold an
  identifier.
- **Right to erasure:** erasing an identity must make it unreadable everywhere, backups included, while the
  pseudonymous kernel record and the audit chain remain.

## 3. Options Considered

1. **Encrypted columns on `people` and `participants`.** Simple, but the ciphertext sits in tables the application
   role reads, so every kernel query can reach it, and erasure means rewriting kernel rows.
2. **A separate identity service with its own database.** The strongest separation, but it adds a network hop and a
   second transaction to every registration, and ADR-2244 keeps kernel concepts in-process.
3. **A separate schema reached only through `SECURITY DEFINER` functions, with envelope encryption in the
   application (chosen).** The kernel's transaction, audit and authorization apply unchanged, the application role
   holds no table privilege on the vault, and the ciphertext never decrypts inside the database.

## 4. Decision Outcome

**Storage.** The `identity_vault` schema holds two tables, outside the EF model:

- `identity_records`, keyed by `(tenant_id, layer, subject_id)`. The layer is `person` (subject: `PersonId`) or
  `site_local` (subject: `ParticipantId`). A row holds the key id, the wrapped data key, the ciphertext, an optional
  blind index, `recorded_at` and `erased_at`. A check keeps the sealed columns present until the row is erased.
- `participant_links`, one row per participant naming the person it belongs to (K-IDN-2). Composite foreign keys tie
  both ids to the tenant.

Both tables force row-level security on `kernel.current_tenant_id()`. The application role has `USAGE` on the schema
and `EXECUTE` on six functions (`put_record`, `shred_record`, `get_record`, `put_link`, `linked_person`,
`linked_participants`) and no privilege on the tables. Each function refuses a call without a tenant context
(SQLSTATE 42501) and acts on the current tenant only.

**Encryption.** `IdentitySealer` seals the details with AES-256-GCM under a fresh 32-byte data key per record. The data
key is wrapped under the tenant's key-encryption key, also with AES-256-GCM. The associated data binds the tenant,
layer and subject, so sealed bytes copied to another record do not open. The blind index is HMAC-SHA256 of the
normalized lookup value (the e-mail for a person, the screening number for a site-local record) under a per-tenant
blind-index key. Keys come from an `IVaultKeyProvider` port. The development provider, `LocalVaultKeyProvider`,
derives both per-tenant keys from one 256-bit master key with HKDF-SHA256 salted by the tenant id. Production replaces
it with a KMS-backed provider behind the same port.

**Commands.** Every vault act is a kernel command with a capability, so the pipeline authorizes it, writes it to the
tenant's audit chain and raises a Trust event:

| Command                                           | Capability                                             | Verb class | Event                 |
| ------------------------------------------------- | ------------------------------------------------------ | ---------- | --------------------- |
| `RecordPersonIdentity`, `RecordSiteLocalIdentity` | `identity.record.person`, `identity.record.site_local` | Execute    | `IdentityRecorded`    |
| `RevealPersonIdentity`, `RevealSiteLocalIdentity` | `identity.reveal.person`, `identity.reveal.site_local` | Execute    | `IdentityRevealed`    |
| `ErasePersonIdentity`, `EraseSiteLocalIdentity`   | `identity.erase.person`, `identity.erase.site_local`   | Approve    | `IdentityErased`      |
| `LinkParticipantToPerson`                         | `identity.link.cross_study`                            | Execute    | `ParticipantLinked`   |
| `RevealPersonLinks`                               | `identity.reveal.links`                                | Execute    | `PersonLinksRevealed` |

Reveals are Execute, not Read: disclosing an identity is an act a person performs, and the evaluator refuses
automation agents Execute and Approve, so an agent cannot decrypt an identity even when it holds a reveal grant.

The details on a record command are `[JsonIgnore]` and print no values, so they never reach the request hash, the
audit entry or an event. Events name the layer and subject only; `ParticipantLinked` does not name the person.

**Writes join the command's transaction.** A handler stages a vault write on the unit of work. The command transaction
applies it through the functions after the kernel rows are saved and before it commits, so a record, erasure or link
and its audit entry commit or roll back together. A write that races an erasure or a conflicting link fails in the
database (SQLSTATE `KV001`, `KV002`) and the command returns a concurrency conflict.

**Reveals are audited before anything is decrypted.** `IIdentityVault.RevealAsync` sends the reveal command, reads the
sealed record only once the command has committed, and unseals it in memory. A replayed reveal (same idempotency key)
returns `RevealReplayed` and no details, so a retried request cannot read an identity without a new audit entry.

**Residency.** `ProvisionTenant` takes an optional residency (`^[A-Z]{2,8}$`, for example `EU`), fixed at
provisioning. The sealer serves tenants with no residency and tenants of its own region. Recording and revealing for a
tenant of another region returns `ResidencyMismatch`. Erasure is always allowed.

**Erasure.** `shred_record` clears the key id, wrapped data key, ciphertext and blind index and stamps `erased_at`. The
row stays as the record of the erasure. An erased identity cannot be recorded again or linked, and a reveal returns
`Erased`.

**Pseudonymous people.** `kernel.people` loses `email`, `first_name` and `last_name`. A person is an id and an active
flag. A unit test fails if any kernel column is named like a direct identifier. The columns hold nothing to move into
the vault: no kernel command wrote them, and the vault seals in the application, where a migration has no key. The
migration proves this instead of assuming it. Before dropping anything it checks every row, with row security off, and
fails with SQLSTATE `KV003` if any row holds an e-mail or a name. Only the placeholders its own `Down` writes are
accepted, so a rollback can be re-applied.

## 5. Consequences & Trade-offs

- The kernel satisfies K-IDN-1 and K-IDN-2 in the schema, not only by convention. An application that needs a name or
  a screening number asks the vault with a capability, and the reveal is in the audit chain.
- Hosts opt in. `AddKernelIdentityVault()` and `AddKernelIdentityVaultPersistence(masterKey, region)` are separate from
  the command pipeline, so a host without a vault key has no vault and cannot reveal. The kernel API host does not
  register it yet; wiring it, with the key from configuration, is the web thread's follow-up.
- A person is tenant-independent (K-ID-4), but the details recorded about them are per tenant. Each tenant holds
  its own record under its own key, so residency, reveal and erasure are the tenant's decisions and no tenant can read
  what another recorded. The login identity of a person stays with the OIDC provider (§9.1).
- Reveal authorization is tenant-wide: a person and a participant are authorized at the tenant scope today. Revealing
  a site-local identity with a study-site grant needs the participant's study-site path in command authorization,
  which arrives with the rest of #4781.
- The kernel has no person registration command yet. Persons are seeded by hosts and tests; a registration command
  that records the identity in the same transaction is follow-up work.
- Execution's `EncryptedDemographics` and eConsent identities are not migrated. Spec §9.2 says the vault subsumes
  them; that migration belongs to the execution work under #4787.
- The blind index is stored but no lookup command exists yet. Matching by e-mail or screening number is a reveal of a
  kind and needs its own capability; it is follow-up work.
- Rotating a tenant's key-encryption key means re-wrapping data keys. Records name their key id, so old and new keys
  can coexist during a rotation.

## 6. Implementation & Verification

- Code:
  - `Cadence.Kernel.Abstractions/Identity/IdentityVault.cs`, `Commands/IdentityVaultCommands.cs` and
    `Events/IdentityVaultEvents.cs` hold the contract.
  - `Cadence.Kernel.Application/Identity/` holds `IdentitySealer`, the ports, the handlers, `IdentityVaultService`
    and `AddKernelIdentityVault`.
  - `Cadence.Kernel.Persistence/IdentityVault/` holds the schema and functions (`IdentityVaultSql`), the store and
    `LocalVaultKeyProvider`. `KernelCommandTransaction` applies staged vault writes.
  - Migration `20260930110610_IdentityVault` creates the schema, checks that the identifier columns of `people` are
    empty, drops them and adds `tenants.residency`.
- Tests:
  - `tests/Cadence.UnitTests/Kernel/IdentityVaultTests.cs` covers sealing, the associated-data binding, the blind
    index, the key hierarchy failing closed, residency, the layer rules for details, that commands never serialize or
    print details, and that the kernel model has no identifier column.
  - `tests/Cadence.IntegrationTests/Kernel/KernelIdentityVaultMigrationPostgresTests.cs` shows the migration
    refusing to drop a person's e-mail, running once the columns are empty, and reverting and re-applying with people
    present.
  - `tests/Cadence.IntegrationTests/Kernel/KernelIdentityVaultPostgresTests.cs` runs record, reveal, erase and link on
    PostgreSQL. It checks that no kernel row holds the identifiers, that a reveal without the capability is denied and
    audited, that the application role cannot read the vault tables, that tenants are isolated, and that residency is
    enforced.
