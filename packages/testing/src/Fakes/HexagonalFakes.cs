using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Ddf;
using Cadence.Storage;

namespace Cadence.Testing.Fakes;

public class InMemoryStoragePort : IStoragePort<byte[]>
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string Sha256, string? ContentType, IReadOnlyDictionary<string, string>? Metadata)> _objects = new();
    private readonly ConcurrentDictionary<string, (string Key, string? ContentType, IReadOnlyDictionary<string, string>? Metadata)> _multipartSessions = new();

    public Task<string> PutObjectAsync(
        string key,
        byte[] data,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var calculatedHash = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!string.IsNullOrEmpty(expectedSha256) && !calculatedHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageIntegrityException($"Storage integrity verification failed! Expected {expectedSha256}, got {calculatedHash}");
        }

        _objects[key] = (data, calculatedHash, contentType, metadata);
        return Task.FromResult(calculatedHash);
    }

    public Task<(byte[] Data, string Sha256)> GetObjectAsync(string key, CancellationToken ct = default)
    {
        if (!_objects.TryGetValue(key, out var item))
        {
            throw new StorageObjectNotFoundException($"Object not found: {key}");
        }

        return Task.FromResult((item.Data, item.Sha256));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        return Task.FromResult(_objects.ContainsKey(key));
    }

    public Task DeleteObjectAsync(string key, CancellationToken ct = default)
    {
        if (!_objects.TryRemove(key, out _))
        {
            throw new StorageObjectNotFoundException($"Object not found: {key}");
        }

        return Task.CompletedTask;
    }

    public Task<string> GeneratePresignedGetUrlAsync(
        string key,
        int expiresIn = 3600,
        string? responseContentDisposition = null,
        CancellationToken ct = default)
    {
        var dispParam = !string.IsNullOrEmpty(responseContentDisposition)
            ? $"&response-content-disposition={responseContentDisposition}"
            : "";
        return Task.FromResult($"https://mock-storage.local/get/{key}?expires_in={expiresIn}{dispParam}");
    }

    public Task<string> GeneratePresignedPutUrlAsync(
        string key,
        int expiresIn = 3600,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var ctParam = !string.IsNullOrEmpty(contentType) ? $"&content_type={contentType}" : "";
        return Task.FromResult($"https://mock-storage.local/put/{key}?expires_in={expiresIn}{ctParam}");
    }

    public Task<string> CreateMultipartUploadAsync(
        string key,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var uploadId = $"mpu-{Guid.NewGuid():N}"[..16];
        _multipartSessions[uploadId] = (key, contentType, metadata);
        return Task.FromResult(uploadId);
    }

    public Task<IReadOnlyDictionary<int, string>> GeneratePresignedMultipartUrlsAsync(
        string key,
        string uploadId,
        IReadOnlyList<int> partNumbers,
        int expiresIn = 3600,
        CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();
        if (partNumbers != null)
        {
            foreach (var p in partNumbers)
            {
                result[p] = $"https://mock-storage.local/mpu/{uploadId}/part/{p}?key={key}&expires_in={expiresIn}";
            }
        }

        return Task.FromResult<IReadOnlyDictionary<int, string>>(result);
    }

    public Task<string> CompleteMultipartUploadAsync(
        string key,
        string uploadId,
        IReadOnlyList<IReadOnlyDictionary<string, object>> parts,
        CancellationToken ct = default)
    {
        if (!_multipartSessions.TryRemove(uploadId, out var session))
        {
            throw new StorageObjectNotFoundException($"Multipart upload session {uploadId} not found");
        }

        if (!_objects.ContainsKey(key))
        {
            _objects[key] = (Array.Empty<byte>(), Convert.ToHexStringLower(SHA256.HashData(Array.Empty<byte>())), session.ContentType, session.Metadata);
        }

        return Task.FromResult($"\"{Guid.NewGuid():N}"[..18] + "\"");
    }

    public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default)
    {
        _multipartSessions.TryRemove(uploadId, out _);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _objects.Clear();
        _multipartSessions.Clear();
    }
}

public class FakeAccessibilityValidator
{
    public bool ShouldFail { get; set; }
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Violations { get; set; }

    public FakeAccessibilityValidator(
        bool shouldFail = false,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? violations = null)
    {
        ShouldFail = shouldFail;
        Violations = violations ?? (shouldFail ? new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>
            {
                ["id"] = "color-contrast",
                ["description"] = "Elements must have sufficient color contrast",
                ["nodes"] = new List<object>()
            }
        } : new List<IReadOnlyDictionary<string, object?>>());
    }

    public Task<AccessibilityReport> ValidateAsync(string openRosaXml, CancellationToken ct = default)
    {
        var formHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(openRosaXml ?? "")));
        if (ShouldFail)
        {
            return Task.FromResult(new AccessibilityReport(
                Status: "FAIL",
                FormHash: formHash,
                Violations: Violations
            ));
        }

        return Task.FromResult(new AccessibilityReport(
            Status: "PASS",
            FormHash: formHash,
            Passes: new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["id"] = "valid-layout" }
            }
        ));
    }
}

public record NotificationMessage(
    string Recipient,
    string Subject,
    string Body,
    IReadOnlyDictionary<string, string>? Metadata = null
);

public class InMemoryNotificationDispatcher
{
    private readonly ConcurrentBag<NotificationMessage> _dispatched = new();

    public IReadOnlyList<NotificationMessage> DispatchedMessages => _dispatched.ToList();

    public Task DispatchAsync(
        string recipient,
        string subject,
        string body,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        _dispatched.Add(new NotificationMessage(recipient, subject, body, metadata));
        return Task.CompletedTask;
    }

    public void Clear() => _dispatched.Clear();
}
