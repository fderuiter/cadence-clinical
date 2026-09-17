using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Storage;

public class InMemoryBlobStore : IStoragePort<byte[]>
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string Sha256, string? ContentType, IReadOnlyDictionary<string, string>? Metadata)> _store = new();

    public Task<string> PutObjectAsync(
        string key,
        byte[] data,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!string.IsNullOrEmpty(expectedSha256) && !sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageIntegrityException($"Precalculated SHA256 '{expectedSha256}' does not match actual '{sha256}'");
        }

        _store[key] = (data, sha256, contentType, metadata);
        return Task.FromResult(sha256);
    }

    public Task<(byte[] Data, string Sha256)> GetObjectAsync(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var item))
        {
            throw new StorageObjectNotFoundException($"Key '{key}' not found in blob store.");
        }
        return Task.FromResult((item.Data, item.Sha256));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        return Task.FromResult(_store.ContainsKey(key));
    }

    public Task DeleteObjectAsync(string key, CancellationToken ct = default)
    {
        if (!_store.TryRemove(key, out _))
        {
            throw new StorageObjectNotFoundException($"Key '{key}' not found in blob store.");
        }
        return Task.CompletedTask;
    }

    public Task<string> GeneratePresignedGetUrlAsync(
        string key,
        int expiresIn = 3600,
        string? responseContentDisposition = null,
        CancellationToken ct = default)
    {
        return Task.FromResult($"https://storage.local/{key}?token=mock-get-token&expires={expiresIn}");
    }

    public Task<string> GeneratePresignedPutUrlAsync(
        string key,
        int expiresIn = 3600,
        string? contentType = null,
        CancellationToken ct = default)
    {
        return Task.FromResult($"https://storage.local/{key}?token=mock-put-token&expires={expiresIn}");
    }

    public Task<string> CreateMultipartUploadAsync(
        string key,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(Guid.NewGuid().ToString());
    }

    public Task<IReadOnlyDictionary<int, string>> GeneratePresignedMultipartUrlsAsync(
        string key,
        string uploadId,
        IReadOnlyList<int> partNumbers,
        int expiresIn = 3600,
        CancellationToken ct = default)
    {
        var dict = new Dictionary<int, string>();
        foreach (var p in partNumbers)
        {
            dict[p] = $"https://storage.local/{key}?part={p}&uploadId={uploadId}";
        }
        return Task.FromResult<IReadOnlyDictionary<int, string>>(dict);
    }

    public Task<string> CompleteMultipartUploadAsync(
        string key,
        string uploadId,
        IReadOnlyList<IReadOnlyDictionary<string, object>> parts,
        CancellationToken ct = default)
    {
        return Task.FromResult(key);
    }

    public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
