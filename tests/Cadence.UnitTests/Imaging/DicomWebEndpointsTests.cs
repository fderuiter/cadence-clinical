using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Deid;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Imaging;

public sealed class DicomWebEndpointsTests
{
    [Fact(DisplayName = "StoreInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes")]
    [GxPRequirement("PRD-IMG-002")]
    public async Task StoreInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Array.Empty<byte>());

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreInstancesAsync(
            httpContext.Request,
            mockIngestionService,
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var value = ((IValueHttpResult)result).Value;
        string json = System.Text.Json.JsonSerializer.Serialize(value);
        json.Should().Contain("\"EMPTY_PAYLOAD\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestInstanceAsync(
            default!, default!, default!, default!, default!, Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact(DisplayName = "StoreBatchInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes")]
    [GxPRequirement("PRD-IMG-002")]
    public async Task StoreBatchInstancesAsync_ReturnsBadRequestWithEmptyPayloadCode_WhenRequestPayloadIsZeroBytes()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(Array.Empty<byte>());

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(
            httpContext.Request,
            mockIngestionService,
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var value = ((IValueHttpResult)result).Value;
        string json = System.Text.Json.JsonSerializer.Serialize(value);
        json.Should().Contain("\"EMPTY_PAYLOAD\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestBatchAsync(
            default!, Arg.Any<System.Threading.CancellationToken>());
    }

    /// <summary>
    /// <c>GET /certificates/{id}</c> verifies a stored certificate with the configured key (#5399): an untouched
    /// certificate is valid, and one whose stored fields were altered, or that another key signed, is not.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-IMG-002")]
    public async Task GetCertificateByIdAsync_VerifiesWithTheConfiguredKey_AndRejectsAlteredCertificates()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingCertificateTest_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var deidentifier = DicomIngestionServiceTests.TestDeidentifier();
        var ingested = await new DicomIngestionService(dbContext, deidentifier, SystemClock.Instance).IngestInstanceAsync(
            DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("60001"), "SUBJ-6001", "SITE-601", "STUDY-6001", "tester", ct);
        var caller = new DefaultHttpContext();
        caller.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal { UserId = "reader", AssignedStudies = ["STUDY-6001"] };

        async Task<bool> IsValidAsync(DicomDeidentifier key)
        {
            var result = await DicomWebEndpoints.GetCertificateByIdAsync(ingested.CertificateId, dbContext, key, caller, ct);
            return result.Should().BeOfType<Ok<CertificateResponse>>().Which.Value!.IsSignatureValid;
        }

        (await IsValidAsync(deidentifier)).Should().BeTrue();
        (await IsValidAsync(new DicomDeidentifier(RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32)))).Should().BeFalse();

        var stored = await dbContext.DeidentificationCertificates.SingleAsync(c => c.Id == ingested.CertificateId, ct);
        stored.SubjectId = "SUBJ-OTHER";
        await dbContext.SaveChangesAsync(ct);

        (await IsValidAsync(deidentifier)).Should().BeFalse();
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_StreamsMultipartFolderUpload_AndPersistsDeidentifiedStudy()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"BatchMultipartEndpoint_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, DicomIngestionServiceTests.TestDeidentifier(), SystemClock.Instance);

        string boundary = "----CadenceMultipartBoundary" + Guid.NewGuid().ToString("N");
        byte[] multipartBytes = BuildMultipartPayload(
            boundary,
            leadingFields:
            [
                ("subjectId", "SUBJ-MP-01"),
                ("timepointName", "BASELINE"),
                ("studyId", "STUDY-MP-01"),
                ("siteId", "SITE-MP-01")
            ],
            files:
            [
                ("scan/slice_001.dcm", "application/dicom", DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6101")),
                ("scan/slice_002.dcm", "application/dicom", DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6102")),
                ("scan/readme.txt", "text/plain", Encoding.UTF8.GetBytes("scanner export readme"))
            ],
            trailingFields: []);

        await using var innerMs = new MemoryStream(multipartBytes, writable: false);
        await using var bodyStream = new DicomIngestionServiceTests.NonSeekableTestReadStream(innerMs);

        var httpContext = CreateAuthorizedHttpContext("STUDY-MP-01", "SITE-MP-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        httpContext.Request.Body = bodyStream;

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, service, ct);

        var created = result.Should().BeOfType<Created<BatchIngestionResult>>().Subject;
        created.Value.Should().NotBeNull();
        created.Value!.TotalFilesReceived.Should().Be(3);
        created.Value.IngestedSliceCount.Should().Be(2);
        created.Value.SkippedNonDicomCount.Should().Be(1);
        created.Value.SkippedNonDicomFiles.Should().ContainSingle().Which.Should().Be("scan/readme.txt");

        (await dbContext.ImagingInstanceEnvelopes.CountAsync(ct)).Should().Be(2);
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_StreamsRawZipBodyWithoutContentLength_AndCleansTemporaryFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"BatchRawZipEndpoint_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, DicomIngestionServiceTests.TestDeidentifier(), SystemClock.Instance);

        byte[] zipBytes = DicomIngestionServiceTests.CreateArchiveBytes(
        [
            ("DICOM/slice_01.dcm", DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6201")),
            ("DICOM/slice_02.dcm", DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6202")),
            ("DICOM/notes.pdf", Encoding.UTF8.GetBytes("%PDF-1.4 note"))
        ]);

        var spoolBefore = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-*").ToHashSet(StringComparer.Ordinal);

        await using var innerMs = new MemoryStream(zipBytes, writable: false);
        await using var nonSeekable = new DicomIngestionServiceTests.NonSeekableTestReadStream(innerMs);

        var httpContext = CreateAuthorizedHttpContext("STUDY-RAWZIP-01", "SITE-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.QueryString = new QueryString("?studyId=STUDY-RAWZIP-01&siteId=SITE-01&subjectId=SUBJ-RAWZIP-01&timepointName=WEEK_8");
        httpContext.Request.ContentType = "application/zip";
        httpContext.Request.Body = nonSeekable;

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, service, ct);

        var created = result.Should().BeOfType<Created<BatchIngestionResult>>().Subject;
        created.Value!.IngestedSliceCount.Should().Be(2);
        created.Value.SkippedNonDicomCount.Should().Be(1);

        var leakedTempFiles = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-*")
            .Where(path => !spoolBefore.Contains(path))
            .ToArray();
        leakedTempFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_SupportsTrailingMultipartMetadataFields_AndCleansTemporarySectionFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"BatchDeferredMultipartEndpoint_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, DicomIngestionServiceTests.TestDeidentifier(), SystemClock.Instance);

        string boundary = "----CadenceDeferredBoundary" + Guid.NewGuid().ToString("N");
        byte[] multipartBytes = BuildMultipartPayload(
            boundary,
            leadingFields: [],
            files:
            [
                ("slice_001.dcm", "application/dicom", DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6301"))
            ],
            trailingFields:
            [
                ("subjectId", "SUBJ-TRAIL-01"),
                ("studyId", "STUDY-TRAIL-01"),
                ("siteId", "SITE-TRAIL-01")
            ]);

        var spoolBefore = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-part-*.tmp").ToHashSet(StringComparer.Ordinal);

        await using var innerMs = new MemoryStream(multipartBytes, writable: false);
        await using var bodyStream = new DicomIngestionServiceTests.NonSeekableTestReadStream(innerMs);

        var httpContext = CreateAuthorizedHttpContext("STUDY-TRAIL-01", "SITE-TRAIL-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        httpContext.Request.Body = bodyStream;

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, service, ct);

        var created = result.Should().BeOfType<Created<BatchIngestionResult>>().Subject;
        created.Value!.IngestedSliceCount.Should().Be(1);

        var leakedPartFiles = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-part-*.tmp")
            .Where(path => !spoolBefore.Contains(path))
            .ToArray();
        leakedPartFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_Returns413PayloadTooLarge_WhenContentLengthExceedsLimit()
    {
        var httpContext = CreateAuthorizedHttpContext("STUDY-413-01", "SITE-01");
        httpContext.Request.ContentLength = DicomWebEndpoints.MaxBatchSizeBytes + 1;
        httpContext.Request.Body = new MemoryStream([0x01]);

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(
            httpContext.Request,
            mockIngestionService,
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestBatchAsync(
            default!,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_Returns413PayloadTooLarge_WhenStreamedRawBodyExceedsTwoGigabytesWithoutContentLength()
    {
        var ct = TestContext.Current.CancellationToken;
        var httpContext = CreateAuthorizedHttpContext("STUDY-413-RAW", "SITE-01");
        httpContext.Request.QueryString = new QueryString("?studyId=STUDY-413-RAW&siteId=SITE-01&subjectId=SUBJ-413");
        httpContext.Request.ContentType = "application/octet-stream";
        await using var oversizedStream = new SyntheticZeroReadStream(DicomWebEndpoints.MaxBatchSizeBytes + 65_536L);
        httpContext.Request.Body = oversizedStream;

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, new DrainingBatchIngestionStub(), ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(
                StatusCodes.Status413PayloadTooLarge,
                because: result is IValueHttpResult val ? JsonSerializer.Serialize(val.Value) : string.Empty);
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_Returns413PayloadTooLarge_WhenStreamedMultipartExceedsTwoGigabytesWithoutContentLength()
    {
        var ct = TestContext.Current.CancellationToken;
        string boundary = "----CadenceOversizedBoundary";
        byte[] prefix = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"studyId\"\r\n\r\nSTUDY-413-MP\r\n" +
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"siteId\"\r\n\r\nSITE-01\r\n" +
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"subjectId\"\r\n\r\nSUBJ-413\r\n" +
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"huge.zip\"\r\nContent-Type: application/zip\r\n\r\n");

        await using var bodyStream = new SyntheticZeroReadStream(DicomWebEndpoints.MaxBatchSizeBytes + 65_536L, prefix);

        var httpContext = CreateAuthorizedHttpContext("STUDY-413-MP", "SITE-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        httpContext.Request.Body = bodyStream;

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, new DrainingBatchIngestionStub(), ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(
                StatusCodes.Status413PayloadTooLarge,
                because: result is IValueHttpResult val ? JsonSerializer.Serialize(val.Value) : string.Empty);
    }

    [Fact]
    [GxPRequirement("PRD-IMG-002")]
    public async Task StoreBatchInstancesAsync_Returns400EmptyPayload_WhenMultipartHasOnlyFormFields()
    {
        var ct = TestContext.Current.CancellationToken;
        string boundary = "----CadenceEmptyFilesBoundary";
        byte[] multipartBytes = BuildMultipartPayload(
            boundary,
            leadingFields:
            [
                ("subjectId", "SUBJ-EMPTY-01"),
                ("studyId", "STUDY-EMPTY-01"),
                ("siteId", "SITE-01")
            ],
            files: [],
            trailingFields: []);

        var httpContext = CreateAuthorizedHttpContext("STUDY-EMPTY-01", "SITE-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        httpContext.Request.Body = new MemoryStream(multipartBytes, writable: false);

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, mockIngestionService, ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        string json = JsonSerializer.Serialize(((IValueHttpResult)result).Value);
        json.Should().Contain("\"EMPTY_PAYLOAD\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestBatchAsync(
            default!,
            Arg.Any<CancellationToken>());
    }

    private static DefaultHttpContext CreateAuthorizedHttpContext(string studyScope, string siteScope)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc.uploader@cadence.org",
            AssignedStudies = [studyScope],
            AssignedSites = [siteScope]
        };
        return httpContext;
    }

    private static byte[] BuildMultipartPayload(
        string boundary,
        (string Name, string Value)[] leadingFields,
        (string FileName, string ContentType, byte[] Data)[] files,
        (string Name, string Value)[] trailingFields)
    {
        using var ms = new MemoryStream();
        foreach (var (name, value) in leadingFields)
        {
            byte[] header = Encoding.UTF8.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n");
            ms.Write(header, 0, header.Length);
        }

        foreach (var (fileName, contentType, data) in files)
        {
            byte[] header = Encoding.UTF8.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\n");
            ms.Write(header, 0, header.Length);
            ms.Write(data, 0, data.Length);
            ms.Write("\r\n"u8);
        }

        foreach (var (name, value) in trailingFields)
        {
            byte[] header = Encoding.UTF8.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}\r\n");
            ms.Write(header, 0, header.Length);
        }

        byte[] closing = Encoding.UTF8.GetBytes($"--{boundary}--\r\n");
        ms.Write(closing, 0, closing.Length);
        return ms.ToArray();
    }

    private sealed class DrainingBatchIngestionStub : IDicomIngestionService
    {
        public Task<IngestionResult> IngestInstanceAsync(
            byte[] dicomBytes,
            string subjectId,
            string siteId,
            string studyId,
            string? operatorIdentity,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<BatchIngestionResult> IngestBatchAsync(
            BatchIngestionRequest request,
            CancellationToken cancellationToken = default)
        {
            byte[] scratch = new byte[65_536];
            await foreach (var item in request.StreamFiles!.WithCancellation(cancellationToken))
            {
                while (await item.ContentStream.ReadAsync(scratch, cancellationToken) > 0)
                {
                    // Drain stream until MaxBytesReadStream trips 413 limit.
                }
            }

            throw new InvalidOperationException("Expected MaxBytesReadStream to abort before completion.");
        }
    }

    private sealed class SyntheticZeroReadStream(long zeroTailBytes, byte[]? headerPrefix = null) : Stream
    {
        private readonly byte[] _prefix = headerPrefix ?? [];
        private int _prefixOffset;
        private long _remainingZeroBytes = zeroTailBytes;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            if (_prefixOffset < _prefix.Length)
            {
                int fromPrefix = Math.Min(buffer.Length, _prefix.Length - _prefixOffset);
                _prefix.AsSpan(_prefixOffset, fromPrefix).CopyTo(buffer);
                _prefixOffset += fromPrefix;
                return fromPrefix;
            }

            if (_remainingZeroBytes <= 0)
            {
                return 0;
            }

            int toRead = (int)Math.Min(buffer.Length, _remainingZeroBytes);
            buffer[..toRead].Clear();
            _remainingZeroBytes -= toRead;
            return toRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            new(Read(buffer.Span));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
