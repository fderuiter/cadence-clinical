using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Util;

namespace Cadence.Storage;

/// <summary>
/// Object storage on Amazon S3 or an S3-compatible server such as MinIO. Every object carries its SHA-256 in the
/// <c>sha256</c> metadata entry and as an S3 checksum, so the server rejects a corrupted upload and a read whose bytes
/// do not match the stored hash fails with <see cref="StorageIntegrityException"/>. Objects are written with
/// server-side encryption unless <see cref="S3StorageOptions.ServerSideEncryption"/> is empty.
/// </summary>
public sealed class S3BlobStore : IStoragePort<byte[]>
{
    /// <summary>The metadata entry that holds an object's SHA-256 (lower-case hex).</summary>
    public const string Sha256MetadataKey = "sha256";

    /// <summary>The longest lifetime S3 accepts for a presigned URL (seven days).</summary>
    public const int MaxPresignedUrlSeconds = 7 * 24 * 60 * 60;

    private readonly IAmazonS3 _client;
    private readonly S3StorageOptions _options;

    /// <summary>Creates the store over an S3 client and the bucket settings.</summary>
    public S3BlobStore(IAmazonS3 client, S3StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            throw new ArgumentException("The bucket name is required.", nameof(options));
        }

        _client = client;
        _options = options;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(data);

        byte[] hash = SHA256.HashData(data);
        string sha256 = Convert.ToHexStringLower(hash);
        if (!string.IsNullOrEmpty(expectedSha256) && !sha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageIntegrityException($"Precalculated SHA256 '{expectedSha256}' does not match actual '{sha256}'");
        }

        using var body = new MemoryStream(data, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
            ChecksumSHA256 = Convert.ToBase64String(hash),
        };
        if (!string.IsNullOrEmpty(contentType))
        {
            request.ContentType = contentType;
        }

        AddMetadata(request.Metadata, metadata);
        request.Metadata.Add(Sha256MetadataKey, sha256);
        if (Encryption is { } encryption)
        {
            request.ServerSideEncryptionMethod = encryption;
        }

        await _client.PutObjectAsync(request, ct).ConfigureAwait(false);
        return sha256;
    }

    /// <inheritdoc />
    public async Task<(byte[] Data, string Sha256)> GetObjectAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        try
        {
            using var response = await _client.GetObjectAsync(_options.BucketName, key, ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            byte[] data = buffer.ToArray();
            string sha256 = Convert.ToHexStringLower(SHA256.HashData(data));

            string? stored = response.Metadata[Sha256MetadataKey];
            if (!string.IsNullOrEmpty(stored) && !sha256.Equals(stored, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageIntegrityException($"Object '{key}' has SHA256 '{sha256}' but was stored with '{stored}'.");
            }

            return (data, sha256);
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw new StorageObjectNotFoundException($"Key '{key}' not found in bucket '{_options.BucketName}'.");
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        try
        {
            await _client.GetObjectMetadataAsync(_options.BucketName, key, ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>S3 deletes are idempotent, so the store checks first and reports a missing object.</remarks>
    public async Task DeleteObjectAsync(string key, CancellationToken ct = default)
    {
        if (!await ExistsAsync(key, ct).ConfigureAwait(false))
        {
            throw new StorageObjectNotFoundException($"Key '{key}' not found in bucket '{_options.BucketName}'.");
        }

        await _client.DeleteObjectAsync(_options.BucketName, key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> GeneratePresignedGetUrlAsync(
        string key,
        int expiresIn = 3600,
        string? responseContentDisposition = null,
        CancellationToken ct = default)
    {
        var request = Presign(key, HttpVerb.GET, expiresIn);
        if (!string.IsNullOrEmpty(responseContentDisposition))
        {
            request.ResponseHeaderOverrides.ContentDisposition = responseContentDisposition;
        }

        return _client.GetPreSignedURLAsync(request);
    }

    /// <inheritdoc />
    public Task<string> GeneratePresignedPutUrlAsync(
        string key,
        int expiresIn = 3600,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var request = Presign(key, HttpVerb.PUT, expiresIn);
        if (!string.IsNullOrEmpty(contentType))
        {
            request.ContentType = contentType;
        }

        return _client.GetPreSignedURLAsync(request);
    }

    /// <inheritdoc />
    public async Task<string> CreateMultipartUploadAsync(
        string key,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var request = new InitiateMultipartUploadRequest { BucketName = _options.BucketName, Key = key };
        if (!string.IsNullOrEmpty(contentType))
        {
            request.ContentType = contentType;
        }

        AddMetadata(request.Metadata, metadata);
        if (Encryption is { } encryption)
        {
            request.ServerSideEncryptionMethod = encryption;
        }

        var response = await _client.InitiateMultipartUploadAsync(request, ct).ConfigureAwait(false);
        return response.UploadId;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, string>> GeneratePresignedMultipartUrlsAsync(
        string key,
        string uploadId,
        IReadOnlyList<int> partNumbers,
        int expiresIn = 3600,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        ArgumentNullException.ThrowIfNull(partNumbers);

        var urls = new Dictionary<int, string>();
        foreach (int part in partNumbers)
        {
            if (part is < 1 or > 10_000)
            {
                throw new ArgumentOutOfRangeException(nameof(partNumbers), part, "S3 part numbers run from 1 to 10000.");
            }

            var request = Presign(key, HttpVerb.PUT, expiresIn);
            request.UploadId = uploadId;
            request.PartNumber = part;
            urls[part] = await _client.GetPreSignedURLAsync(request).ConfigureAwait(false);
        }

        return urls;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each part names its <c>PartNumber</c> and <c>ETag</c>, the keys S3 and the Python reference use. S3 needs the
    /// parts in order, so they are sorted. Returns the ETag of the assembled object.
    /// </remarks>
    public async Task<string> CompleteMultipartUploadAsync(
        string key,
        string uploadId,
        IReadOnlyList<IReadOnlyDictionary<string, object>> parts,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("A multipart upload needs at least one part.", nameof(parts));
        }

        var request = new CompleteMultipartUploadRequest { BucketName = _options.BucketName, Key = key, UploadId = uploadId };
        request.AddPartETags(parts
            .Select(p => new PartETag(Convert.ToInt32(Required(p, "PartNumber"), System.Globalization.CultureInfo.InvariantCulture), Required(p, "ETag").ToString()))
            .OrderBy(p => p.PartNumber));

        var response = await _client.CompleteMultipartUploadAsync(request, ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(response.ETag) ? key : response.ETag;
    }

    /// <inheritdoc />
    public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        return _client.AbortMultipartUploadAsync(_options.BucketName, key, uploadId, ct);
    }

    private ServerSideEncryptionMethod? Encryption =>
        string.IsNullOrWhiteSpace(_options.ServerSideEncryption) ? null : ServerSideEncryptionMethod.FindValue(_options.ServerSideEncryption);

    private GetPreSignedUrlRequest Presign(string key, HttpVerb verb, int expiresIn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (expiresIn is < 1 or > MaxPresignedUrlSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresIn), expiresIn, $"A presigned URL lives from 1 to {MaxPresignedUrlSeconds} seconds.");
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Verb = verb,
            // S3 checks a presigned URL against its own time, so the lifetime is counted from the SDK's signing clock
            // (the one that writes X-Amz-Date), not from the application's IClock.
            Expires = AWSSDKUtils.CorrectedUtcNow.AddSeconds(expiresIn),
        };

        // The SDK signs https URLs unless told otherwise, which breaks a plain-http MinIO endpoint.
        if (_options.ServiceUrl is { } serviceUrl && serviceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            request.Protocol = Protocol.HTTP;
        }

        return request;
    }

    private static void AddMetadata(MetadataCollection target, IReadOnlyDictionary<string, string>? metadata)
    {
        foreach (var (name, value) in metadata ?? new Dictionary<string, string>())
        {
            if (string.Equals(name, Sha256MetadataKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"The '{Sha256MetadataKey}' metadata entry is written by the store.", nameof(metadata));
            }

            target.Add(name, value);
        }
    }

    private static object Required(IReadOnlyDictionary<string, object> part, string name) =>
        part.TryGetValue(name, out object? value) && value is not null
            ? value
            : throw new ArgumentException($"Every part needs a {name}.", nameof(part));

    private static bool IsNotFound(AmazonS3Exception ex) =>
        ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode is "NoSuchKey" or "NoSuchBucket" or "NotFound";
}
