# Cadence.Storage

Object storage abstraction with integrity hashes and presigned URLs, plus an in-memory implementation.

## Project

- Project: `packages/storage/src/Cadence.Storage.csproj` (assembly `Cadence.Storage`, namespace `Cadence.Storage`).
- `StoragePort.cs`: `IStoragePort<T>` (`PutObjectAsync`, `GetObjectAsync` returning data and SHA-256, `ExistsAsync`, `DeleteObjectAsync`, `GeneratePresignedGetUrlAsync`, `GeneratePresignedPutUrlAsync`), `StorageObjectNotFoundException`, `StorageIntegrityException`.
- `BlobStore.cs`: `InMemoryBlobStore`, the only implementation.
- `DocumentModels.cs`: `DocumentMetadata`, `StorageLocation`.
- The project references `AWSSDK.S3` and `Google.Apis.Drive.v3`, but no S3, MinIO or Google Drive adapter exists in C# yet.
- Python reference sources remain for behaviour the C# port lacks or does not test yet: `adapters/`, `factory.py`, `gdrive.py`, `local_store.py`, `s3_store.py`, with their `__init__.py` files and `pyproject.toml`. They are not the implementation; [docs/PYTHON_REFERENCE_INVENTORY.md](../../docs/PYTHON_REFERENCE_INVENTORY.md) says what each needs before it goes.

## Key dependencies

Packages: `AWSSDK.S3`, `Google.Apis.Drive.v3` (both currently unused).

## Referenced by

The three test projects only. No service uses it yet (including [apps/fileshare](../../apps/fileshare/README.md)).

## Tests

Covered by `tests/Packages/PortedPackagesUnitTests.cs`:

```bash
dotnet test tests/Cadence.UnitTests.csproj --filter "FullyQualifiedName~Cadence.UnitTests.Packages.PortedPackagesUnitTests"
```
