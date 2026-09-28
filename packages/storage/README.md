# Storage Package

## Overview

Storage utilities package

---

## Architectural Boundaries

The `storage` package operates as a modular shared library within the Cadence Clinical ecosystem:
- **Code-Adjacent Domain:** Located strictly within `packages/storage/`.
- **Decoupled Architecture:** Provides explicit interface contracts and reusable domain models without imposing direct database or cross-service runtime bindings.
- **GxP Audit Compliance:** Maintained under strict versioning and change control standards.

---

## Public APIs / Exports

Key exported interfaces and components:
- ArchiveJobResponse
- BlobStorageProvider
- DocumentMetadataResponse
- DocumentUploadResponse
- LocalStorageProvider
- MinioStorageAdapter
- S3StorageAdapter
- S3StorageProvider

---

## Dependencies

This package relies on the following standard dependencies:
- `aioboto3>=13.0.0`
- `boto3>=1.34.0`
- `google-api-python-client>=2.120.0`
- `google-auth-httplib2>=0.2.0`
- `google-auth>=2.28.0`
- `pydantic>=2.6.0`

---

## Local Development & Testing

Run unit and contract tests for this package:

```bash
uv run pytest packages/storage
```
