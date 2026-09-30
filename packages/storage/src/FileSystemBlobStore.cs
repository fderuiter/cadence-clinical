using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Storage;

/// <summary>Settings for <see cref="FileSystemBlobStore"/>, bound from the <c>Storage</c> configuration section.</summary>
public sealed record FileSystemStorageOptions
{
    /// <summary>The absolute directory the store keeps its objects under. Required; there is no default.</summary>
    public string RootPath { get; init; } = "";
}

/// <summary>
/// Object storage in a local directory, for a single server with a durable volume or for development without an S3
/// server (the C# port of the Python <c>LocalStorageProvider</c>). An object's bytes are at <c>objects/{key}</c> and its
/// SHA-256, content type and metadata at <c>meta/{key}.json</c>, so every read is checked against the hash recorded
/// when it was written, as <see cref="S3BlobStore"/> does. Writes go to a temporary file in the same directory and are
/// renamed into place, so a reader never sees half an object.
/// </summary>
/// <remarks>
/// <para>A key is a relative path of <c>/</c>-separated segments: no empty, <c>.</c> or <c>..</c> segment, no
/// backslash, no control character and at most <see cref="MaxKeyLength"/> characters. The resolved path must also stay
/// under the root. An invalid key is refused with <see cref="ArgumentException"/>, never silently rewritten.</para>
/// <para>Clients cannot reach a directory, so presigned URLs and multipart uploads throw
/// <see cref="NotSupportedException"/>. Use the <c>s3</c> or <c>minio</c> provider when clients upload or download
/// directly.</para>
/// </remarks>
public sealed partial class FileSystemBlobStore : IStoragePort<byte[]>
{
    /// <summary>The longest key the store accepts.</summary>
    public const int MaxKeyLength = 1024;

    private const string ObjectsFolder = "objects";
    private const string MetadataFolder = "meta";
    private const string MetadataSuffix = ".json";

    private const string UrlsNotSupported =
        "The filesystem store cannot issue URLs or multipart uploads; use the 's3' or 'minio' provider when clients upload or download directly.";

    private readonly string _objects;
    private readonly string _metadata;

    /// <summary>Creates the store under <see cref="FileSystemStorageOptions.RootPath"/>, creating the folders it needs.</summary>
    /// <exception cref="ArgumentException">The root path is missing or not absolute.</exception>
    public FileSystemBlobStore(FileSystemStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.RootPath) || !Path.IsPathFullyQualified(options.RootPath))
        {
            throw new ArgumentException("The storage root must be an absolute path.", nameof(options));
        }

        string root = Path.GetFullPath(options.RootPath);
        _objects = Directory.CreateDirectory(Path.Combine(root, ObjectsFolder)).FullName;
        _metadata = Directory.CreateDirectory(Path.Combine(root, MetadataFolder)).FullName;
    }

    /// <inheritdoc />
    public async Task<string> PutObjectAsync(
        string key,
        byte[] data,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var (objectPath, metadataPath) = Paths(key);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!string.IsNullOrEmpty(expectedSha256) && !sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageIntegrityException($"Precalculated SHA256 '{expectedSha256}' does not match actual '{sha256}'");
        }

        if (Directory.Exists(objectPath))
        {
            throw new InvalidOperationException($"Key '{key}' names a folder of other objects.");
        }

        var record = new StoredObjectMetadata(sha256, contentType, metadata is null ? null : new Dictionary<string, string>(metadata, StringComparer.Ordinal));
        byte[] recordBytes = JsonSerializer.SerializeToUtf8Bytes(record, StoredObjectMetadataJson.Default.StoredObjectMetadata); // deid-ignore: a type name, not a device serial

        // The object is renamed into place first: a reader between the two renames sees new bytes with the old hash
        // and fails with StorageIntegrityException, never with a silently wrong object.
        await WriteAtomicallyAsync(objectPath, data, ct).ConfigureAwait(false);
        await WriteAtomicallyAsync(metadataPath, recordBytes, ct).ConfigureAwait(false);
        return sha256;
    }

    /// <inheritdoc />
    public async Task<(byte[] Data, string Sha256)> GetObjectAsync(string key, CancellationToken ct = default)
    {
        var (objectPath, metadataPath) = Paths(key);
        if (!File.Exists(objectPath))
        {
            throw new StorageObjectNotFoundException($"Key '{key}' not found in the storage root.");
        }

        byte[] data = await File.ReadAllBytesAsync(objectPath, ct).ConfigureAwait(false);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
        string? stored = await ReadStoredHashAsync(metadataPath, ct).ConfigureAwait(false);
        if (stored is null)
        {
            throw new StorageIntegrityException($"Object '{key}' has no readable recorded SHA256, so it cannot be verified.");
        }

        if (!sha256.Equals(stored, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageIntegrityException($"Object '{key}' has SHA256 '{sha256}' but was stored with '{stored}'.");
        }

        return (data, sha256);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(Paths(key).Object));

    /// <inheritdoc />
    public Task DeleteObjectAsync(string key, CancellationToken ct = default)
    {
        var (objectPath, metadataPath) = Paths(key);
        if (!File.Exists(objectPath))
        {
            throw new StorageObjectNotFoundException($"Key '{key}' not found in the storage root.");
        }

        File.Delete(objectPath);
        File.Delete(metadataPath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: a directory has no URLs.</exception>
    public Task<string> GeneratePresignedGetUrlAsync(string key, int expiresIn = 3600, string? responseContentDisposition = null, CancellationToken ct = default) =>
        throw new NotSupportedException(UrlsNotSupported);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: a directory has no URLs.</exception>
    public Task<string> GeneratePresignedPutUrlAsync(string key, int expiresIn = 3600, string? contentType = null, CancellationToken ct = default) =>
        throw new NotSupportedException(UrlsNotSupported);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: multipart uploads go through presigned part URLs.</exception>
    public Task<string> CreateMultipartUploadAsync(string key, string? contentType = null, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        throw new NotSupportedException(UrlsNotSupported);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: a directory has no URLs.</exception>
    public Task<IReadOnlyDictionary<int, string>> GeneratePresignedMultipartUrlsAsync(string key, string uploadId, IReadOnlyList<int> partNumbers, int expiresIn = 3600, CancellationToken ct = default) =>
        throw new NotSupportedException(UrlsNotSupported);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: multipart uploads go through presigned part URLs.</exception>
    public Task<string> CompleteMultipartUploadAsync(string key, string uploadId, IReadOnlyList<IReadOnlyDictionary<string, object>> parts, CancellationToken ct = default) =>
        throw new NotSupportedException(UrlsNotSupported);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: multipart uploads go through presigned part URLs.</exception>
    public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default) =>
        throw new NotSupportedException(UrlsNotSupported);

    /// <summary>Whether <paramref name="key"/> is a key the store accepts.</summary>
    public static bool IsValidKey(string? key) =>
        !string.IsNullOrEmpty(key)
        && key.Length <= MaxKeyLength
        && !key.Any(c => char.IsControl(c) || c == '\\')
        && key.Split('/').All(segment => segment.Length > 0 && segment is not ("." or ".."))
        && !Path.IsPathRooted(key);

    private (string Object, string Metadata) Paths(string key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException(
                $"'{key}' is not a storage key: use a relative path of '/'-separated segments with no empty, '.' or '..' segment, no backslash and at most {MaxKeyLength} characters.",
                nameof(key));
        }

        string objectPath = Path.GetFullPath(Path.Combine(_objects, key));
        string metadataPath = Path.GetFullPath(Path.Combine(_metadata, key + MetadataSuffix));
        if (!IsUnder(objectPath, _objects) || !IsUnder(metadataPath, _metadata))
        {
            throw new ArgumentException($"'{key}' resolves outside the storage root.", nameof(key));
        }

        return (objectPath, metadataPath);
    }

    private static bool IsUnder(string path, string folder) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes, CancellationToken ct)
    {
        string folder = Path.GetDirectoryName(path)!;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"'{path}' cannot be written: part of its key is already an object.", ex);
        }

        string temporary = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous)) // deid-ignore: a buffer size, not a ZIP code
            {
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static async Task<string?> ReadStoredHashAsync(string metadataPath, CancellationToken ct)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(metadataPath);
        try
        {
            var record = await JsonSerializer.DeserializeAsync(stream, StoredObjectMetadataJson.Default.StoredObjectMetadata, ct).ConfigureAwait(false);
            return string.IsNullOrEmpty(record?.Sha256) ? null : record.Sha256;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record StoredObjectMetadata(string Sha256, string? ContentType, Dictionary<string, string>? Metadata);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(StoredObjectMetadata))]
    internal sealed partial class StoredObjectMetadataJson : JsonSerializerContext;
}
