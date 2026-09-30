# Cadence.Storage

Object storage with integrity hashes and presigned URLs: an S3 and MinIO store, a filesystem store, an in-memory store for tests and Development, and the registration that picks one from configuration.

## Project

- Project: `packages/storage/src/Cadence.Storage.csproj` (assembly `Cadence.Storage`, namespace `Cadence.Storage`).
- `StoragePort.cs`: `IStoragePort<T>` (`PutObjectAsync`, `GetObjectAsync` returning data and SHA-256, `ExistsAsync`, `DeleteObjectAsync`, `GeneratePresignedGetUrlAsync`, `GeneratePresignedPutUrlAsync`), `StorageObjectNotFoundException`, `StorageIntegrityException`.
- `S3BlobStore.cs`: `S3BlobStore`, the store for Amazon S3 and S3-compatible servers (MinIO). Each object carries its SHA-256 as `sha256` metadata and as an S3 checksum, so the server rejects a corrupted upload and a read whose bytes do not match fails with `StorageIntegrityException`. Writes use server-side encryption (`AES256` unless configured otherwise). Presigned GET, PUT and multipart part URLs are Signature Version 4, last 1 second to 7 days, and are dated by the AWS SDK's signing clock, which S3 checks. `X-Amz-Expires` is always exactly the requested lifetime, even when signing is slow. `DeleteObjectAsync` reports a missing object, as the port requires, although S3 deletes are idempotent.
- `FileSystemBlobStore.cs`: `FileSystemBlobStore`, the store for one server with a durable volume, or for development without an S3 server. An object's bytes go in `objects/{key}` and its SHA-256, content type and metadata in `meta/{key}.json`. Writes go to a temporary file and are renamed into place. A read whose bytes do not match the recorded hash, or that has no readable hash, fails with `StorageIntegrityException`. Keys are relative `/`-separated paths that cannot leave the root (no `.`, `..` or empty segment, no backslash or control character). Presigned URLs and multipart uploads throw `NotSupportedException`.
- `StorageServiceCollectionExtensions.cs`: `AddCadenceStorage(configuration, environment)` registers `IStoragePort<byte[]>` from the `Storage` section (`Provider` = `s3`, `minio`, `filesystem` or, in Development only, `memory`) and fails at startup on a missing bucket or absolute root path, a half-set access key pair or an unknown provider; `FileSystemStorageOptions`; `S3StorageOptions`; `CreateClient` and `CreateConfig`. Settings: [docs/ENVIRONMENT_VARIABLES.md](../../docs/ENVIRONMENT_VARIABLES.md#object-storage).
- `BlobStore.cs`: `InMemoryBlobStore` (its presigned URLs are placeholders, so it is for tests and Development only).
- `DocumentModels.cs`: `DocumentMetadata`, `StorageLocation`.
- There is no Google Drive store: recording evidence goes through `IStoragePort<byte[]>` like any other file ([ADR-2505](../../docs/adr/2026-09-30-recording-evidence-goes-to-controlled-object-storage.md)).
- Python reference sources remain until a maintainer approves their deletion: `adapters/`, `factory.py`, `local_store.py` and `s3_store.py` are ported and tested, and `gdrive.py` is not carried forward (ADR-2505). They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

Packages: `AWSSDK.S3`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`.

## Referenced by

The three test projects only. No service uses it yet (including [apps/fileshare](../../apps/fileshare/README.md)).

## Tests

`S3BlobStoreTests` run the real AWS SDK client against a stub HTTP handler and check the requests S3 would receive; `FileSystemBlobStoreTests` cover the filesystem store and its registration; `StorageRegistrationTests` cover the configuration; `PortedPackagesUnitTests` cover the in-memory store:

```bash
dotnet test tests/Cadence.UnitTests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~S3BlobStoreTests|FullyQualifiedName~FileSystemBlobStoreTests|FullyQualifiedName~StorageRegistrationTests|FullyQualifiedName~PortedPackagesUnitTests"
```
