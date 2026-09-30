using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Amazon.Runtime;
using Amazon.S3;

using Cadence.Storage;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// The S3 object store, ported from the Python reference adapter (<c>packages/storage/adapters/s3_adapter.py</c>).
/// The real AWS SDK client runs against a stub HTTP handler, so the tests see the requests S3 or MinIO would receive.
/// </summary>
public sealed class S3BlobStoreTests : IDisposable
{
    private const string Bucket = "cadence-test";
    private const string Endpoint = "http://minio.test:9000";

    private readonly StubS3 _s3 = new();
    private readonly AmazonS3Client _client;

    public S3BlobStoreTests()
    {
        // The production client settings, with the HTTP layer swapped for the stub.
        var config = StorageServiceCollectionExtensions.CreateConfig(Options());
        config.HttpClientFactory = new StubFactory(_s3);
        config.MaxErrorRetry = 0;
        _client = new AmazonS3Client(new BasicAWSCredentials("test-access", "test-secret"), config);
    }

    public void Dispose() => _client.Dispose();

    private static S3StorageOptions Options(string? encryption = "AES256") =>
        new() { BucketName = Bucket, ServiceUrl = Endpoint, ServerSideEncryption = encryption };

    private S3BlobStore Store(string? encryption = "AES256") => new(_client, Options(encryption));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Put_SendsTheHashAsMetadataAndChecksum_WithEncryption_AndReturnsIt()
    {
        byte[] data = Encoding.UTF8.GetBytes("synthetic document");
        string expected = Convert.ToHexStringLower(SHA256.HashData(data));

        string sha256 = await Store().PutObjectAsync("docs/a.pdf", data, "application/pdf", new Dictionary<string, string> { ["study"] = "S-1" }, ct: Ct);

        sha256.Should().Be(expected);
        var request = _s3.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be("PUT");
        request.Path.Should().Be($"/{Bucket}/docs/a.pdf");
        request.Headers["x-amz-meta-sha256"].Should().Be(expected);
        request.Headers["x-amz-meta-study"].Should().Be("S-1");
        request.Headers["x-amz-checksum-sha256"].Should().Be(Convert.ToBase64String(SHA256.HashData(data)));
        request.Headers["x-amz-server-side-encryption"].Should().Be("AES256");
        request.Headers["Content-Type"].Should().Be("application/pdf");
        StubS3.Payload(request).Should().Equal(data);
    }

    [Fact]
    public async Task Put_WithEncryptionOff_SendsNoEncryptionHeader()
    {
        await Store(encryption: "").PutObjectAsync("a", [1], ct: Ct);

        _s3.Requests.Single().Headers.Should().NotContainKey("x-amz-server-side-encryption");
    }

    [Fact]
    public async Task Put_RefusesAMismatchedExpectedHash_WithoutCallingS3()
    {
        var put = () => Store().PutObjectAsync("a", [1, 2, 3], expectedSha256: new string('0', 64), ct: Ct);

        await put.Should().ThrowAsync<StorageIntegrityException>();
        _s3.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Put_RefusesACallerSuppliedHashMetadataEntry()
    {
        var put = () => Store().PutObjectAsync("a", [1], metadata: new Dictionary<string, string> { ["SHA256"] = "forged" }, ct: Ct);

        await put.Should().ThrowAsync<ArgumentException>();
        _s3.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ReturnsTheBytesAndHash_WhenTheyMatchTheStoredHash()
    {
        byte[] data = [1, 2, 3, 4];
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(data));
        _s3.Respond = _ => StubS3.Object(data, sha256);

        var (content, hash) = await Store().GetObjectAsync("a", Ct);

        content.Should().Equal(data);
        hash.Should().Be(sha256);
    }

    [Fact]
    public async Task Get_FailsWhenTheBytesDoNotMatchTheStoredHash()
    {
        _s3.Respond = _ => StubS3.Object([9, 9], Convert.ToHexStringLower(SHA256.HashData([1, 2])));

        var get = () => Store().GetObjectAsync("a", Ct);

        await get.Should().ThrowAsync<StorageIntegrityException>();
    }

    [Theory]
    [InlineData("NoSuchKey")]
    [InlineData("NoSuchBucket")]
    public async Task Get_ReportsAMissingObject(string code)
    {
        _s3.Respond = _ => StubS3.Error(HttpStatusCode.NotFound, code);

        var get = () => Store().GetObjectAsync("missing", Ct);

        await get.Should().ThrowAsync<StorageObjectNotFoundException>();
    }

    [Fact]
    public async Task Get_LetsOtherErrorsThrough()
    {
        _s3.Respond = _ => StubS3.Error(HttpStatusCode.Forbidden, "AccessDenied");

        var get = () => Store().GetObjectAsync("a", Ct);

        await get.Should().ThrowAsync<AmazonS3Exception>().Where(e => e.ErrorCode == "AccessDenied");
    }

    [Fact]
    public async Task Exists_AsksWithAHeadRequest()
    {
        _s3.Respond = r => r.Path.EndsWith("/present", StringComparison.Ordinal) ? new HttpResponseMessage(HttpStatusCode.OK) : new HttpResponseMessage(HttpStatusCode.NotFound);

        (await Store().ExistsAsync("present", Ct)).Should().BeTrue();
        (await Store().ExistsAsync("absent", Ct)).Should().BeFalse();
        _s3.Requests.Select(r => r.Method).Should().Equal("HEAD", "HEAD");
    }

    [Fact]
    public async Task Delete_ReportsAMissingObject_AndDeletesAPresentOne()
    {
        _s3.Respond = r =>
        {
            if (r.Method == "DELETE")
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(r.Path.EndsWith("/present", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        };

        var missing = () => Store().DeleteObjectAsync("absent", Ct);
        await missing.Should().ThrowAsync<StorageObjectNotFoundException>();

        await Store().DeleteObjectAsync("present", Ct);
        _s3.Requests.Select(r => $"{r.Method} {r.Path}").Should().Equal($"HEAD /{Bucket}/absent", $"HEAD /{Bucket}/present", $"DELETE /{Bucket}/present");
    }

    [Fact]
    public async Task PresignedUrls_AreSignatureVersion4OnTheEndpoint_WithTheRequestedLifetime()
    {
        string get = await Store().GeneratePresignedGetUrlAsync("docs/a.pdf", 600, "attachment; filename=\"a.pdf\"", Ct);
        string put = await Store().GeneratePresignedPutUrlAsync("docs/a.pdf", 900, "application/pdf", Ct);

        get.Should().StartWith($"{Endpoint}/{Bucket}/docs/a.pdf?").And.Contain("X-Amz-Signature=").And.Contain("response-content-disposition=")
            .And.Contain("X-Amz-Algorithm=AWS4-HMAC-SHA256").And.Contain("X-Amz-Expires=600&");
        put.Should().StartWith($"{Endpoint}/{Bucket}/docs/a.pdf?").And.Contain("X-Amz-Signature=").And.Contain("X-Amz-Expires=900&");
        _s3.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(S3BlobStore.MaxPresignedUrlSeconds)]
    public async Task PresignedUrls_KeepTheExactLifetime_AtTheLimits(int seconds)
    {
        string get = await Store().GeneratePresignedGetUrlAsync("a", seconds, ct: Ct);

        get.Should().Contain($"X-Amz-Expires={seconds}&");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(S3BlobStore.MaxPresignedUrlSeconds + 1)]
    public async Task PresignedUrls_RefuseALifetimeS3Rejects(int seconds)
    {
        var get = () => Store().GeneratePresignedGetUrlAsync("a", seconds, ct: Ct);

        await get.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Multipart_StartsEncrypted_PresignsEachPart_AndCompletesInPartOrder()
    {
        _s3.Respond = r => r.Query.Contains("uploads", StringComparison.Ordinal)
            ? StubS3.Xml($"<InitiateMultipartUploadResult><Bucket>{Bucket}</Bucket><Key>big.bin</Key><UploadId>upload-1</UploadId></InitiateMultipartUploadResult>")
            : StubS3.Xml($"<CompleteMultipartUploadResult><Bucket>{Bucket}</Bucket><Key>big.bin</Key><ETag>\"final\"</ETag></CompleteMultipartUploadResult>");
        var store = Store();

        string uploadId = await store.CreateMultipartUploadAsync("big.bin", "application/octet-stream", ct: Ct);
        var urls = await store.GeneratePresignedMultipartUrlsAsync("big.bin", uploadId, [1, 2], ct: Ct);
        string etag = await store.CompleteMultipartUploadAsync(
            "big.bin",
            uploadId,
            [
                new Dictionary<string, object> { ["PartNumber"] = 2, ["ETag"] = "\"b\"" },
                new Dictionary<string, object> { ["PartNumber"] = 1, ["ETag"] = "\"a\"" },
            ],
            Ct);

        uploadId.Should().Be("upload-1");
        _s3.Requests[0].Headers["x-amz-server-side-encryption"].Should().Be("AES256");
        urls.Keys.Should().Equal(1, 2);
        urls[2].Should().Contain("partNumber=2").And.Contain("uploadId=upload-1");
        string body = Encoding.UTF8.GetString(_s3.Requests[1].Body);
        body.IndexOf("<PartNumber>1</PartNumber>", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("<PartNumber>2</PartNumber>", StringComparison.Ordinal));
        etag.Should().Be("\"final\"");
    }

    [Fact]
    public async Task Multipart_RefusesPartNumbersS3Rejects_AndAnEmptyPartList()
    {
        var presign = () => Store().GeneratePresignedMultipartUrlsAsync("a", "u", [0], ct: Ct);
        var complete = () => Store().CompleteMultipartUploadAsync("a", "u", [], Ct);

        await presign.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await complete.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Abort_DeletesTheUpload()
    {
        _s3.Respond = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        await Store().AbortMultipartUploadAsync("big.bin", "upload-1", Ct);

        var request = _s3.Requests.Single();
        request.Method.Should().Be("DELETE");
        request.Query.Should().Contain("uploadId=upload-1");
    }

    /// <summary>A request the stub received.</summary>
    private sealed record Received(string Method, string Path, string Query, Dictionary<string, string> Headers, byte[] Body);

    /// <summary>Records each request and answers with <see cref="Respond"/> (200 with an ETag by default).</summary>
    private sealed class StubS3 : HttpMessageHandler
    {
        public List<Received> Requests { get; } = [];

        public Func<Received, HttpResponseMessage> Respond { get; set; } = _ =>
        {
            var ok = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            ok.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"etag\"");
            return ok;
        };

        public static HttpResponseMessage Object(byte[] data, string sha256)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            response.Headers.Add("x-amz-meta-sha256", sha256);
            return response;
        }

        /// <summary>The object bytes of a PUT, unwrapping the SDK's signed <c>aws-chunked</c> framing when it used it.</summary>
        public static byte[] Payload(Received request)
        {
            if (!request.Headers.TryGetValue("x-amz-content-sha256", out string? mode) || !mode.StartsWith("STREAMING-", StringComparison.Ordinal))
            {
                return request.Body;
            }

            var payload = new List<byte>();
            int position = 0;
            while (true)
            {
                int lineEnd = IndexOfCrLf(request.Body, position);
                string header = Encoding.ASCII.GetString(request.Body, position, lineEnd - position);
                int size = Convert.ToInt32(header.Split(';')[0], 16);
                if (size == 0)
                {
                    return [.. payload];
                }

                payload.AddRange(request.Body.AsSpan(lineEnd + 2, size).ToArray());
                position = lineEnd + 2 + size + 2;
            }
        }

        private static int IndexOfCrLf(byte[] bytes, int start) => start + bytes.AsSpan(start).IndexOf("\r\n"u8);

        public static HttpResponseMessage Xml(string xml) =>
            new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

        public static HttpResponseMessage Error(HttpStatusCode status, string code) =>
            new(status) { Content = new StringContent($"<Error><Code>{code}</Code><Message>{code}</Message></Error>", Encoding.UTF8, "application/xml") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            var received = new Received(request.Method.Method, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath), request.RequestUri.Query, headers, body);
            Requests.Add(received);
            return Respond(received);
        }
    }

    private sealed class StubFactory(StubS3 handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }
}
