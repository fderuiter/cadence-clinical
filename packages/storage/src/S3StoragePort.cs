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

using Cadence.Config;

namespace Cadence.Storage;

/// <summary>
/// S3 object storage port implementation with inline SHA-256 integrity validation and server-side encryption.
/// @req:PRD-SYS-001
/// @req:PRD-DOC-001
/// @req:PRD-DOC-002
/// </summary>
public class S3StoragePort : IStoragePort<byte[]>
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _sseAlgorithm;

    public S3StoragePort(IAmazonS3 s3Client, string bucketName = "cadence-fileshare", string sseAlgorithm = "AES256")
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = string.IsNullOrWhiteSpace(bucketName) ? "cadence-fileshare" : bucketName;
        _sseAlgorithm = sseAlgorithm;
    }

    public S3StoragePort(IAmazonS3 s3Client, StorageOptions options)
        : this(s3Client, options?.StorageBucket ?? options?.S3BucketName ?? "cadence-fileshare", options?.StorageSseAlgorithm ?? "AES256")
    {
    }

    public async Task<string> PutObjectAsync(
        string key,
        byte[] data,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? expectedSha256 = null,
        CancellationToken ct = default)
    {
        string calculatedHash = Convert.ToHexStringLower(SHA256.HashData(data));
        if (!string.IsNullOrEmpty(expectedSha256) && !calculatedHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageIntegrityException($"Storage integrity verification failed! Expected {expectedSha256}, got {calculatedHash}");
        }

        using var ms = new MemoryStream(data);
        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = ms,
        };

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            request.ContentType = contentType;
        }

        if (!string.IsNullOrWhiteSpace(_sseAlgorithm) && _sseAlgorithm.Equals("AES256", StringComparison.OrdinalIgnoreCase))
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }

        request.Metadata["sha256"] = calculatedHash;
        if (metadata is not null)
        {
            foreach (var (k, v) in metadata)
            {
                request.Metadata[k] = v;
            }
        }

        await _s3Client.PutObjectAsync(request, ct).ConfigureAwait(false);
        return calculatedHash;
    }

    public async Task<(byte[] Data, string Sha256)> GetObjectAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
            };

            using var response = await _s3Client.GetObjectAsync(request, ct).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
            byte[] data = ms.ToArray();

            string calculatedHash = Convert.ToHexStringLower(SHA256.HashData(data));

            string? expectedSha256 = response.Metadata["sha256"] ?? response.Metadata["x-amz-meta-sha256"];
            if (!string.IsNullOrEmpty(expectedSha256) && !calculatedHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new StorageIntegrityException($"Storage integrity verification failed! Expected {expectedSha256}, got {calculatedHash}");
            }

            return (data, calculatedHash);
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            throw new StorageObjectNotFoundException($"Object not found: {key}");
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = key,
            };

            await _s3Client.GetObjectMetadataAsync(request, ct).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    public async Task DeleteObjectAsync(string key, CancellationToken ct = default)
    {
        bool exists = await ExistsAsync(key, ct).ConfigureAwait(false);
        if (!exists)
        {
            throw new StorageObjectNotFoundException($"Object not found: {key}");
        }

        var request = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
        };

        await _s3Client.DeleteObjectAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<string> GeneratePresignedGetUrlAsync(
        string key,
        int expiresIn = 3600,
        string? responseContentDisposition = null,
        CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddSeconds(expiresIn),
            Verb = HttpVerb.GET,
        };

        if (!string.IsNullOrWhiteSpace(responseContentDisposition))
        {
            request.ResponseHeaderOverrides.ContentDisposition = responseContentDisposition;
        }

        return await _s3Client.GetPreSignedURLAsync(request).ConfigureAwait(false);
    }

    public async Task<string> GeneratePresignedPutUrlAsync(
        string key,
        int expiresIn = 3600,
        string? contentType = null,
        CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.AddSeconds(expiresIn),
            Verb = HttpVerb.PUT,
        };

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            request.ContentType = contentType;
        }

        return await _s3Client.GetPreSignedURLAsync(request).ConfigureAwait(false);
    }

    public async Task<string> CreateMultipartUploadAsync(
        string key,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
        };

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            request.ContentType = contentType;
        }

        if (!string.IsNullOrWhiteSpace(_sseAlgorithm) && _sseAlgorithm.Equals("AES256", StringComparison.OrdinalIgnoreCase))
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }

        if (metadata is not null)
        {
            foreach (var (k, v) in metadata)
            {
                request.Metadata[k] = v;
            }
        }

        var response = await _s3Client.InitiateMultipartUploadAsync(request, ct).ConfigureAwait(false);
        return response.UploadId;
    }

    public async Task<IReadOnlyDictionary<int, string>> GeneratePresignedMultipartUrlsAsync(
        string key,
        string uploadId,
        IReadOnlyList<int> partNumbers,
        int expiresIn = 3600,
        CancellationToken ct = default)
    {
        var result = new Dictionary<int, string>();
        foreach (int partNumber in partNumbers)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                UploadId = uploadId,
                PartNumber = partNumber,
                Expires = DateTime.UtcNow.AddSeconds(expiresIn),
                Verb = HttpVerb.PUT,
            };

            result[partNumber] = await _s3Client.GetPreSignedURLAsync(request).ConfigureAwait(false);
        }

        return result;
    }

    public async Task<string> CompleteMultipartUploadAsync(
        string key,
        string uploadId,
        IReadOnlyList<IReadOnlyDictionary<string, object>> parts,
        CancellationToken ct = default)
    {
        var partETags = parts
            .Select(p => new PartETag
            {
                PartNumber = Convert.ToInt32(p["PartNumber"]),
                ETag = p["ETag"]?.ToString() ?? string.Empty,
            })
            .OrderBy(p => p.PartNumber)
            .ToList();

        var request = new CompleteMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            UploadId = uploadId,
            PartETags = partETags,
        };

        var response = await _s3Client.CompleteMultipartUploadAsync(request, ct).ConfigureAwait(false);
        return response.ETag ?? key;
    }

    public async Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken ct = default)
    {
        var request = new AbortMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key = key,
            UploadId = uploadId,
        };

        await _s3Client.AbortMultipartUploadAsync(request, ct).ConfigureAwait(false);
    }

    private static bool IsNotFound(AmazonS3Exception ex)
    {
        return ex.StatusCode == HttpStatusCode.NotFound ||
               ex.ErrorCode is "NoSuchKey" or "NotFound" or "404" or "NoSuchBucket";
    }
}
