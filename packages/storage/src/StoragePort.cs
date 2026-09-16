using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Storage;

public class StorageObjectNotFoundException : System.Exception
{
    public StorageObjectNotFoundException(string message) : base(message) { }
}

public class StorageIntegrityException : System.Exception
{
    public StorageIntegrityException(string message) : base(message) { }
}

public interface IStoragePort<T>
{
    Task<string> PutObjectAsync(
        string key,
        byte[] data,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? expectedSha256 = null,
        CancellationToken ct = default);

    Task<(byte[] Data, string Sha256)> GetObjectAsync(string key, CancellationToken ct = default);

    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task DeleteObjectAsync(string key, CancellationToken ct = default);

    Task<string> GeneratePresignedGetUrlAsync(
        string key,
        int expiresIn = 3600,
        string? responseContentDisposition = null,
        CancellationToken ct = default);

    Task<string> GeneratePresignedPutUrlAsync(
        string key,
        int expiresIn = 3600,
        string? contentType = null,
        CancellationToken ct = default);

    Task<string> CreateMultipartUploadAsync(
        string key,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, string>> GeneratePresignedMultipartUrlsAsync(
        string key,
        string uploadId,
        IReadOnlyList<int> partNumbers,
        int expiresIn = 3600,
        CancellationToken ct = default);

    Task<string> CompleteMultipartUploadAsync(
        string key,
        string uploadId,
        IReadOnlyList<IReadOnlyDictionary<string, object>> parts,
        CancellationToken ct = default);

    Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default);
}
