# ADR-2454: Evidence Artifacts as Immutable, Hash-Addressed Versions

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-DOC-002 (SHA-256 integrity of stored files), PRD-DOC-003 (signatures bound to the file's hash), PRD-SYS-001 (audit entries on every artifact change)

---

## 1. Context & Problem Statement

Spec §8.3 makes `Artifact` versioned, hash-addressed evidence content: a signed consent form, an ethics approval,
a lab report, a generated dataset, a protocol rendering. The kernel had no artifact at all, yet several kernel
decisions depend on one:

1. `Effectivity` may only become `Effective` with evidence such as the ethics approval (K-TIME-5, #4777).
2. Consent is evidenced by a signed consent form (§9.4), and local lab ranges are range-set artifacts (G9).
3. `Source = SourceDocument(artifact)` (§8.3) and ingestion keeps the raw batch as an artifact (§8.5).
4. eTMF, eISF and safety intake each kept their own document tables with their own notion of a version and a hash.

This ADR decides what the kernel stores about an artifact, how versions work, and how they are signed.

## 2. Decision Drivers & Constraints

- **K-TRUTH-1:** a filing classification (TMF zone, section, artifact code) is a projection. Re-filing must not
  change the artifact.
- **Part 11:** a signature must bind to the exact bytes signed (PRD-DOC-003), and stored bytes must be verifiable
  against their recorded hash (PRD-DOC-002).
- **K-SCOPE-1:** upload workflows, virus scanning, redaction and retention holds are product concerns; the kernel
  needs only what makes an artifact usable as evidence across products.
- **K-TX-1:** one command changes one aggregate.

## 3. Options Considered

### What the kernel stores

1. **The bytes in the database.** Simple, but large files bloat the audit-chained database and backups.
2. **A description of the bytes (chosen).** Hash, size, media type and an opaque storage reference. The bytes live
   in object storage; the hash lets anyone verify them.

### How content changes

1. **Mutable current content with an audit trail.** Signatures and evidence references would silently point to
   different bytes.
2. **Immutable, numbered versions (chosen).** A new file is a new version. Evidence and signatures name a version.

### Where classification lives

1. **A TMF artifact code on the artifact.** Couples the kernel to one filing model.
2. **A small declared kind on the artifact, filing as an Assertion (chosen).** The kind (`EthicsApproval`,
   `SignedConsent`, `LabReport`, …) is what kernel rules check; the TMF classification is an Assertion (§8.2) that
   eTMF may re-project.

## 4. Decision Outcome

- **Model.** `Artifact(id, tenant, kind, study?, title)` is an aggregate whose `ArtifactVersion(id, number,
content_hash, size_bytes, media_type, storage_ref, recorded_at)` children are numbered from 1. `study` is null for
  tenant-wide evidence such as a lab's range set. `ArtifactContent` describes one version's bytes and validates
  itself: a well-formed `sha256:` hash, at least one byte, a lower-case `type/subtype` media type and a storage
  reference.
- **Commands.** `RegisterArtifact` (capability `artifact.register`) creates the artifact with version 1;
  `AddArtifactVersion` (`artifact.add_version`, resource the artifact) adds the next version. Content that an
  existing version already has is refused (`K.Evidence.ContentUnchanged`), so versions are distinct content.
- **Events.** `ArtifactRegistered` and `ArtifactVersioned` (Evidence family, spec §5.2).
- **Hashing.** `ContentHash.OfBytes` hashes raw bytes the same way (`sha256:` and lower-case hex) the kernel hashes
  canonical JSON. The storage adapter computes it from the bytes it stored.
- **Signing.** `Attest` accepts `artifact_version` targets. A version's hash never changes, so the signature binds to
  those bytes for good (PRD-DOC-003).
- **Database.** `kernel.artifacts` and `kernel.artifact_versions` are tenant-isolated (ADR-2250).
  `artifact_versions` is insert-only (`KA001`), checks the hash pattern, a positive size and number, and keeps
  numbers and hashes unique per artifact.
- **References.** `NodeKinds.Artifact` (`artifact`) and `NodeKinds.ArtifactVersion` (`artifact_version`).

## 5. Consequences & Trade-offs

- Effectivity (#4777), consent, source documents and ingestion can name an `ArtifactVersionId` as evidence.
- The kernel cannot prove the bytes still exist in storage; verification against the hash is the storage adapter's
  job, and a periodic integrity check belongs to the eTMF/document application.
- Deleting a version is impossible, including for retention expiry. Physical purging, legal holds and redaction are
  product workflows that will need their own decision when they arrive.
- Existing product document tables (eTMF, eISF) are not migrated here; they move onto kernel artifacts when their
  applications are dispositioned (#4787).

## 6. Implementation & Verification

- Code: `Cadence.Kernel.Abstractions` (`ArtifactId`, `ArtifactVersionId`, `Evidence/ArtifactKind`,
  `Evidence/ArtifactContent`, `Commands/EvidenceCommands`, `Events/EvidenceEvents`, `Trust/ContentHash.OfBytes`),
  `Cadence.Kernel.Domain/Entities/Artifact.cs`, `Cadence.Kernel.Application/Evidence`,
  `Cadence.Kernel.Persistence/Configurations/EvidenceConfigurations.cs`, migration `ArtifactsAndVersions`.
- Tests: `tests/Cadence.UnitTests/Kernel/ArtifactTests.cs` (content validation, numbering, signable target) and
  `tests/Cadence.IntegrationTests/Kernel/KernelArtifactPostgresTests.cs` (pipeline, attestation, insert-only guard, check
  constraints). The shared insert-only and tenant-isolation tests cover the new tables.
