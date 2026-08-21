# ADR-2186: Object Storage Port and Envelope Security Architecture

* **Status:** Accepted
* **Date:** 2026-08-21
* **Authors:** @fderuiter
* **Deciders:** @fderuiter

---

## 1. Context & Problem Statement

Historically, trial documents, eTMF artifacts, and eISF binders were stored as raw binary blobs (`BYTEA`) directly inside PostgreSQL database tables. While acceptable in early prototypes, storing high-volume binary files (site delegation logs, medical licenses, laboratory certifications, eCRF audio/video attachments, medical imaging) inside the relational database engine presents severe scalability, backup, and performance bottlenecks:
1. Massive database bloat causing slow vacuuming, bloated WAL files, and degraded query throughput on transaction tables.
2. Inability to leverage pre-signed streaming or multi-part uploads directly to cloud storage (MinIO/AWS S3/GCS).
3. Risk of timeout during large file uploads over synchronous HTTP request cycles.

We must decouple physical binary storage from relational metadata without compromising 21 CFR Part 11 cryptographic compliance, auditability, tenant isolation, and legal retention hold mandates (`PRD-DOC-001`, `PRD-DOC-002`, `PRD-DOC-003`).

## 2. Decision Drivers & Constraints

* **21 CFR Part 11 & Annex 11 Compliance:** Every document upload, revision, redaction, and electronic signature must have an immutable audit trail and verifiable SHA-256 Merkle root hash.
* **Strict Tenant & Study Isolation:** Binary object paths must enforce strict tenant-level namespace scoping (`/{tenant_id}/{study_id}/{doc_id}`).
* **High-Performance Direct Transfer:** Heavy binary transfers must bypass application memory using pre-signed upload/download URLs issued by the Gateway.
* **Local & Production Portability:** The solution must seamlessly operate against local Docker MinIO instances in local development and standard S3/Azure Blob/GCS in production without code alterations.

## 3. Options Considered

1. **Option 1 (Selected): StoragePort Protocol & Relational Envelope Pattern**
   - Abstract physical blob operations behind a generic PEP 695 `StoragePort[T]` protocol in `packages/storage`.
   - Store binary payloads in tenant-isolated S3/MinIO buckets.
   - Maintain all regulatory metadata (taxonomy codes, version indices, virus scan certifications, SHA-256 checksums, 21 CFR Part 11 signature manifests, and legal hold status) in PostgreSQL as a metadata envelope.
   - Use two-phase commits for uploads: 1) Allocate draft document envelope and issue pre-signed URL; 2) Client completes upload and triggers SHA-256 integrity verification to transition state to `COMMITTED`.

2. **Option 2: Direct Gateway Streaming with In-DB Storage**
   - Retain binary blobs in PostgreSQL but stream chunks via FastAPI streaming responses.
   - *Rejected:* Does not solve relational database bloat, WAL scaling, or high backup latencies.

3. **Option 3: Pure Object Store with Object Tags (No Relational Envelope)**
   - Store metadata in S3 object metadata tags.
   - *Rejected:* S3 object tagging lacks ACID transactional guarantees, foreign key constraints to clinical trials/sites, and fast querying across millions of documents.

## 4. Decision Outcome

Chosen option: **Option 1 (StoragePort Protocol & Relational Envelope Pattern)** because it cleanly separates binary storage throughput from relational data integrity. It satisfies `PRD-DOC-001`, `PRD-DOC-002`, and `PRD-DOC-003` while maintaining strict GxP compliance.

### Architectural Flow

```
+------------------------------------------------------------------------------------+
|                                  CLIENT BROWSER                                    |
+------------------------------------------------------------------------------------+
          | (1) POST /api/v1/documents/draft             ^ (4) PUT binary payload
          v                                              |     via pre-signed URL
+-----------------------+                                |
|   GATEWAY / EDC API   |                                |
| - Allocates draft row |                                |
| - Generates pre-signed|                                |
|   upload URL          |                                |
+-----------------------+                                |
          | (2) Save draft envelope                      |
          v                                              v
+-----------------------+                     +-----------------------+
|  POSTGRESQL DATABASE  |                     |     OBJECT STORE      |
|  (Metadata Envelope)  |                     |    (MinIO / AWS S3)   |
| - Document ID         |                     | - /{tenant}/{study}/  |
| - Status: DRAFT       |                     |   {doc_id}            |
+-----------------------+                     +-----------------------+
          ^                                              |
          | (5) Verify Checksum & Status -> COMMITTED    |
          +----------------------------------------------+
```

## 5. Consequences & Trade-offs

* **Positive:**
  - Fast, non-blocking pre-signed uploads directly to object storage.
  - Zero database bloat; PostgreSQL tables remain lightweight and indexed.
  - 100% testable via local MinIO Docker containers and in-memory mock storage fakes in `packages/testing`.
  - Immutable SHA-256 verification guarantees zero tamper risk during transit.
* **Negative:**
  - Requires a two-step upload lifecycle (draft allocation followed by upload confirmation/verification).
  - Requires automated garbage collection for orphaned draft envelopes that never receive binary payloads within the TTL.

## 6. Implementation & Verification

* **Packages:**
  - `packages/storage/`: Defines `StoragePort[T]`, `S3StorageAdapter`, and `MinioStorageAdapter`.
  - `apps/execution/` and `apps/fileshare/`: Consume `StoragePort` for eTMF, eISF, and general file transfers.
* **Verification:**
  - Unit tests with `packages.testing.fakes.InMemoryStoragePort`.
  - Integration tests verifying SHA-256 checksum mismatch rejection and MinIO pre-signed URL generation.
  - Traceability linked via docstrings `@req:PRD-DOC-001`, `@req:PRD-DOC-002`.

