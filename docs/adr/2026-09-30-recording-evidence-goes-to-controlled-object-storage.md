# ADR-2505: Recording Evidence Goes to Controlled Object Storage, Not Google Drive

- **Status:** Accepted
- **Date:** 2026-09-30
- **Authors:** @fderuiter
- **Deciders:** @fderuiter
- **Requirements:** PRD-DOC-001 (object storage adapter and metadata envelope); PRD-DOC-002 (SHA-256 integrity of stored files)
- **Amends:** [ADR-2210](2026-08-23-automated-golden-path-video-recording-and-gdrive-sync.md) (its Google Drive destination)
- **Issue:** #4789

---

## 1. Context & Problem Statement

[ADR-2210](2026-08-23-automated-golden-path-video-recording-and-gdrive-sync.md) records persona-based Playwright
golden-path runs and uploads the videos, the HTML verification report and a SHA-256 run manifest to a Google Drive
folder, using a Google Cloud service account. The upload lived in the Python reference `packages/storage/gdrive.py`.

No C# code uploads anything to Drive. `pnpm cadence record setup-gdrive` only prints "not implemented yet", and
`Cadence.Storage` references `Google.Apis.Drive.v3` without using it. Since ADR-2210, the platform has gained its own
object stores: `S3BlobStore` for S3 and MinIO, and `FileSystemBlobStore`. Both record a SHA-256 for every object and
check it on every read.

## 2. Decision Drivers & Constraints

- Validation evidence is a GxP record. It belongs in storage the sponsor controls, with integrity checked on read
  (PRD-DOC-002), not in a folder shared through a third-party consumer service.
- A second storage path needs its own credentials (`GOOGLE_APPLICATION_CREDENTIALS`), its own qualification and its
  own access review. None of that exists in C#.
- A Python reference file leaves the inventory when a tested port exists or an ADR records that the behaviour is not
  carried forward (ADR-2492).

## 3. Options Considered

### Option 1: Port the Google Drive client to C#

- **Cons:**
  - ❌ A second evidence store outside the platform, with service-account credentials to manage and qualify.
  - ❌ Drive does not verify content against a recorded hash on read.

### Option 2: Store recording evidence through the platform's object storage port (Selected)

- **Pros:**
  - ✅ One qualified store, configured once (`Storage:Provider`), with SHA-256 checked on every read.
  - ✅ No Google credentials and one less third-party dependency.
- **Cons:**
  - ❌ Stakeholders who watched recordings in Drive need a link or a download from the store instead.

## 4. Decision Outcome

- **Chosen Option:** Option 2. `gdrive.py` is not ported.
- When `pnpm cadence record run` is implemented, it writes each video, the HTML report and the run manifest through
  `IStoragePort<byte[]>` under one key prefix per run. The manifest lists each file's name, size and SHA-256, and the
  store records and checks each object's hash. Sharing a recording is a presigned GET URL from the S3 or MinIO store.
- Until then, Playwright writes recordings to `RECORDING_OUTPUT_DIR` (default `apps/web/test-results`), as it does
  today.
- `pnpm cadence record setup-gdrive` is removed, and `Cadence.Storage` no longer references `Google.Apis.Drive.v3`.
- The rest of ADR-2210 stands: persona-partitioned suites, the Triple Sentinel gate, demonstration pacing and the
  `record run` and `record list` commands.

## 5. Consequences & Trade-offs

- **Positive Impact:**
  - Recording evidence and clinical documents share one store, one configuration and one integrity check.
  - `gdrive.py` can be deleted once a maintainer approves.
- **Negative Impact / Technical Debt:**
  - `record run` and `record list` are still not implemented.
- **Mitigation Strategy:** the Python reference inventory tracks them with the CLI's other stubs.

## 6. Implementation & Verification

- **Affected Repositories / Services:** `packages/storage` (package reference removed), `packages/cli` (the
  `setup-gdrive` stub removed), `Directory.Packages.props`, `docs/cli.md`, the ADR-2210 note, CONTEXT.md, the storage
  README and `docs/PYTHON_REFERENCE_INVENTORY.md`.
- **Verification Plan:** the build, the `cli-docs` gate and `pnpm cadence check adrs`. The object stores' integrity
  checks are covered by `S3BlobStoreTests` and `FileSystemBlobStoreTests`.
