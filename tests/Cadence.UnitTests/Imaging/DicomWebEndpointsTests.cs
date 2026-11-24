using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Endpoints;
using Cadence.Apps.Imaging.Infrastructure.Persistence;
using Cadence.Apps.Imaging.Services;
using Cadence.Database;
using Cadence.Database.Tenancy;
using Cadence.Deid;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.UnitTests.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;

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

    [Theory]
    [InlineData("?siteId=SITE-01&subjectId=SUBJ-01")]
    [InlineData("?studyId=%20%20&siteId=SITE-01&subjectId=SUBJ-01")]
    [InlineData("?studyId=STUDY-01&subjectId=SUBJ-01")]
    [InlineData("?studyId=STUDY-01&siteId=%20&subjectId=SUBJ-01")]
    [InlineData("?studyId=STUDY-01&siteId=SITE-01")]
    [InlineData("?studyId=STUDY-01&siteId=SITE-01&subjectId=%20")]
    public async Task StoreInstancesAsync_ReturnsBadRequestWithMissingScopeCode_WhenScopeParametersAreBlank(string query)
    {
        var ct = TestContext.Current.CancellationToken;
        var httpContext = CreateAuthorizedHttpContext("STUDY-01", "SITE-01");
        httpContext.Request.QueryString = new QueryString(query);
        httpContext.Request.Body = new MemoryStream([0x44, 0x49, 0x43, 0x4D]);

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreInstancesAsync(httpContext.Request, mockIngestionService, ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        JsonSerializer.Serialize(((IValueHttpResult)result).Value).Should().Contain("\"MISSING_SCOPE\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestInstanceAsync(
            default!, default!, default!, default!, default!, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("?siteId=SITE-01&subjectId=SUBJ-01")]
    [InlineData("?studyId=STUDY-01&subjectId=SUBJ-01")]
    [InlineData("?studyId=STUDY-01&siteId=SITE-01")]
    [InlineData("?studyId=STUDY-01&siteId=SITE-01&subjectId=%20%20")]
    public async Task StoreBatchInstancesAsync_ReturnsBadRequestWithMissingScopeCode_WhenRawUploadScopeIsBlank(string query)
    {
        var ct = TestContext.Current.CancellationToken;
        var httpContext = CreateAuthorizedHttpContext("STUDY-01", "SITE-01");
        httpContext.Request.QueryString = new QueryString(query);
        httpContext.Request.ContentType = "application/dicom";
        httpContext.Request.Body = new MemoryStream(DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6050"));

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, mockIngestionService, ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        JsonSerializer.Serialize(((IValueHttpResult)result).Value).Should().Contain("\"MISSING_SCOPE\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestBatchAsync(
            default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_ReturnsBadRequestWithMissingScopeCode_WhenMultipartOrPrePopulatedFormLacksScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        string boundary = "----CadenceMissingScopeBoundary" + Guid.NewGuid().ToString("N");
        byte[] multipartBytes = BuildMultipartPayload(
            boundary,
            leadingFields: [("studyId", "STUDY-01"), ("subjectId", "SUBJ-01")],
            files: [("slice_001.dcm", "application/dicom", DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6051"))],
            trailingFields: [("siteId", "   ")]);

        var spoolBefore = Directory.GetFiles(Path.GetTempPath(), "cadence-multipart-*.tmp").ToHashSet(StringComparer.Ordinal);
        var multipartContext = CreateAuthorizedHttpContext("STUDY-01", "SITE-01");
        multipartContext.Request.Method = "POST";
        multipartContext.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        multipartContext.Request.Body = new MemoryStream(multipartBytes, writable: false);

        var multipartResult = await DicomWebEndpoints.StoreBatchInstancesAsync(multipartContext.Request, mockIngestionService, ct);
        multipartResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        JsonSerializer.Serialize(((IValueHttpResult)multipartResult).Value).Should().Contain("\"MISSING_SCOPE\"");

        Directory.GetFiles(Path.GetTempPath(), "cadence-multipart-*.tmp")
            .Where(path => !spoolBefore.Contains(path))
            .Should().BeEmpty();

        byte[] dicomBytes = DicomIngestionServiceTests.CreateSampleDicomBytesWithPhi("6052");
        using var formFileStream = new MemoryStream(dicomBytes, writable: false);
        var formFiles = new FormFileCollection
        {
            new FormFile(formFileStream, 0, dicomBytes.Length, "files", "slice_002.dcm")
        };
        var formContext = CreateAuthorizedHttpContext("STUDY-01", "SITE-01");
        formContext.Features.Set<IFormFeature>(new FormFeature(new FormCollection(
            new Dictionary<string, StringValues>
            {
                ["studyId"] = "STUDY-01",
                ["siteId"] = "SITE-01",
                ["subjectId"] = " "
            },
            formFiles)));

        var formResult = await DicomWebEndpoints.StoreBatchInstancesAsync(formContext.Request, mockIngestionService, ct);
        formResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        JsonSerializer.Serialize(((IValueHttpResult)formResult).Value).Should().Contain("\"MISSING_SCOPE\"");

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestBatchAsync(
            default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetInstanceFramesAsync_RequiresMatchingParentStudyAndSeriesEnvelopes_AndCallerSiteScope()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"ImagingWadoParentCheck_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, DicomIngestionServiceTests.TestDeidentifier(), SystemClock.Instance);

        var site1Ingested = await service.IngestInstanceAsync(
            DicomIngestionServiceTests.CreateSampleDicomBytesWithStudyAndModality(
                "1.2.840.113619.2.55.9301", // deid-ignore: synthetic test DICOM UID
                "1.2.840.113619.2.55.9301.1", // deid-ignore: synthetic test DICOM UID
                "1.2.840.113619.2.55.9301.1.1", // deid-ignore: synthetic test DICOM UID
                "CT"),
            subjectId: "SUBJ-WADO-01",
            siteId: "SITE-WADO-01",
            studyId: "STUDY-WADO-01",
            operatorIdentity: "crc1@site.org",
            cancellationToken: ct);

        var site2Ingested = await service.IngestInstanceAsync(
            DicomIngestionServiceTests.CreateSampleDicomBytesWithStudyAndModality(
                "1.2.840.113619.2.55.9301", // deid-ignore: synthetic test DICOM UID
                "1.2.840.113619.2.55.9301.1", // deid-ignore: synthetic test DICOM UID
                "1.2.840.113619.2.55.9301.1.1", // deid-ignore: synthetic test DICOM UID
                "MR"),
            subjectId: "SUBJ-WADO-02",
            siteId: "SITE-WADO-02",
            studyId: "STUDY-WADO-01",
            operatorIdentity: "crc2@site.org",
            cancellationToken: ct);

        var site1Caller = CreateAuthorizedHttpContext("STUDY-WADO-01", "SITE-WADO-01");
        site1Caller.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc1@site.org",
            Roles = [RoleNormalization.ROLE_CRC],
            AssignedStudies = ["STUDY-WADO-01"],
            AssignedSites = ["SITE-WADO-01"]
        };

        var validFrame = await DicomWebEndpoints.GetInstanceFramesAsync(
            site1Ingested.AnonymizedStudyUid,
            site1Ingested.AnonymizedSeriesUid,
            site1Ingested.AnonymizedSopUid,
            "1",
            dbContext,
            site1Caller,
            ct);
        validFrame.Should().BeOfType<FileContentHttpResult>();

        var mismatchedStudy = await DicomWebEndpoints.GetInstanceFramesAsync(
            site2Ingested.AnonymizedStudyUid,
            site1Ingested.AnonymizedSeriesUid,
            site1Ingested.AnonymizedSopUid,
            "1",
            dbContext,
            site1Caller,
            ct);
        mismatchedStudy.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var mismatchedSeries = await DicomWebEndpoints.GetInstanceFramesAsync(
            site1Ingested.AnonymizedStudyUid,
            site2Ingested.AnonymizedSeriesUid,
            site1Ingested.AnonymizedSopUid,
            "1",
            dbContext,
            site1Caller,
            ct);
        mismatchedSeries.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var crossSiteFrame = await DicomWebEndpoints.GetInstanceFramesAsync(
            site2Ingested.AnonymizedStudyUid,
            site2Ingested.AnonymizedSeriesUid,
            site2Ingested.AnonymizedSopUid,
            "1",
            dbContext,
            site1Caller,
            ct);
        crossSiteFrame.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task CanaryCrossSiteDicomIsolationProof_SeparatesStudyUidsAndEnvelopes_AndBlocksCrossSiteFrameReads()
    {
        var ct = TestContext.Current.CancellationToken;
        string databaseName = $"ImagingCanaryProof_{Guid.NewGuid():N}";

        await using var booted = ServiceHosts.All["imaging"](services =>
        {
            var inMemoryServices = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.RemoveAll<DbContextOptions<ImagingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ImagingDbContext>>();
            services.AddDbContext<ImagingDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(databaseName)
                    .UseCadenceStandaloneTenancy(sp.GetService<ICurrentTenantAccessor>(), isRelational: false)
                    .AddInterceptors(StandaloneTenantSaveChangesInterceptor.Instance);
                options.UseInternalServiceProvider(inMemoryServices);
            });
        });

        using var crcSite1 = booted.CreateSignedClient(new GatewayIdentity(
            UserId: "crc-site-1",
            Roles: [RoleNormalization.ROLE_CRC],
            SiteIds: ["SITE-CANARY-01"],
            TenantId: "tenant-canary",
            StudyIds: ["STUDY-CANARY-01"]));

        using var crcSite2 = booted.CreateSignedClient(new GatewayIdentity(
            UserId: "crc-site-2",
            Roles: [RoleNormalization.ROLE_CRC],
            SiteIds: ["SITE-CANARY-02"],
            TenantId: "tenant-canary",
            StudyIds: ["STUDY-CANARY-01"]));

        const string sharedRawStudyUid = "1.2.840.113619.2.55.9001"; // deid-ignore: synthetic test DICOM UID
        byte[] site1CtBytes = DicomIngestionServiceTests.CreateSampleDicomBytesWithStudyAndModality(
            sharedRawStudyUid,
            "1.2.840.113619.2.55.9001.10", // deid-ignore: synthetic test DICOM UID
            "1.2.840.113619.2.55.9001.10.1", // deid-ignore: synthetic test DICOM UID
            "CT");
        byte[] site2MrBytes = DicomIngestionServiceTests.CreateSampleDicomBytesWithStudyAndModality(
            sharedRawStudyUid,
            "1.2.840.113619.2.55.9001.20", // deid-ignore: synthetic test DICOM UID
            "1.2.840.113619.2.55.9001.20.1", // deid-ignore: synthetic test DICOM UID
            "MR");
        byte[] site2CtBytes = DicomIngestionServiceTests.CreateSampleDicomBytesWithStudyAndModality(
            sharedRawStudyUid,
            "1.2.840.113619.2.55.9001.30", // deid-ignore: synthetic test DICOM UID
            "1.2.840.113619.2.55.9001.30.1", // deid-ignore: synthetic test DICOM UID
            "CT");

        // 1. POST /api/v1/imaging/studies as crc at SITE-CANARY-01 (Modality=CT) -> 201 Created
        using var site1PostContent = new ByteArrayContent(site1CtBytes);
        site1PostContent.Headers.ContentType = new MediaTypeHeaderValue("application/dicom");
        using var site1PostResp = await crcSite1.PostAsync(
            "/api/v1/imaging/studies?studyId=STUDY-CANARY-01&siteId=SITE-CANARY-01&subjectId=SUBJ-CANARY-01",
            site1PostContent,
            ct);
        site1PostResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var site1Ingested = await site1PostResp.Content.ReadFromJsonAsync<IngestionResult>(ct);
        site1Ingested.Should().NotBeNull();

        // 2. POST /api/v1/imaging/studies/batch as crc at SITE-CANARY-02 (Modality=MR) -> 201 Created with distinct AnonymizedStudyUid and StudyEnvelopeId
        using var site2BatchContent = new ByteArrayContent(site2MrBytes);
        site2BatchContent.Headers.ContentType = new MediaTypeHeaderValue("application/dicom");
        using var site2BatchResp = await crcSite2.PostAsync(
            "/api/v1/imaging/studies/batch?studyId=STUDY-CANARY-01&siteId=SITE-CANARY-02&subjectId=SUBJ-CANARY-02",
            site2BatchContent,
            ct);
        site2BatchResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var site2BatchIngested = await site2BatchResp.Content.ReadFromJsonAsync<BatchIngestionResult>(ct);
        site2BatchIngested.Should().NotBeNull();
        site2BatchIngested!.AnonymizedStudyUid.Should().NotBe(site1Ingested!.AnonymizedStudyUid);
        site2BatchIngested.StudyEnvelopeId.Should().NotBe(site1Ingested.StudyEnvelopeId);

        // 3. POST /api/v1/imaging/studies as crc at SITE-CANARY-02 (Modality=CT) -> 201 Created with distinct AnonymizedStudyUid and StudyEnvelopeId
        using var site2SingleContent = new ByteArrayContent(site2CtBytes);
        site2SingleContent.Headers.ContentType = new MediaTypeHeaderValue("application/dicom");
        using var site2SingleResp = await crcSite2.PostAsync(
            "/api/v1/imaging/studies?studyId=STUDY-CANARY-01&siteId=SITE-CANARY-02&subjectId=SUBJ-CANARY-02",
            site2SingleContent,
            ct);
        site2SingleResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var site2SingleIngested = await site2SingleResp.Content.ReadFromJsonAsync<IngestionResult>(ct);
        site2SingleIngested.Should().NotBeNull();
        site2SingleIngested!.AnonymizedStudyUid.Should().NotBe(site1Ingested.AnonymizedStudyUid);
        site2SingleIngested.StudyEnvelopeId.Should().NotBe(site1Ingested.StudyEnvelopeId);
        site2SingleIngested.AnonymizedStudyUid.Should().Be(site2BatchIngested.AnonymizedStudyUid);
        site2SingleIngested.StudyEnvelopeId.Should().Be(site2BatchIngested.StudyEnvelopeId);

        // 4. GET /api/v1/imaging/studies/{site1AnonymizedStudyUid} as crc at SITE-CANARY-01 -> 200 OK with InstanceCount == 1 and ModalitiesJson == ["CT"]
        using var site1GetStudyResp = await crcSite1.GetAsync(
            $"/api/v1/imaging/studies/{site1Ingested.AnonymizedStudyUid}",
            ct);
        site1GetStudyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var site1Study = await site1GetStudyResp.Content.ReadFromJsonAsync<ImagingStudyEnvelope>(ct);
        site1Study.Should().NotBeNull();
        site1Study!.InstanceCount.Should().Be(1);
        site1Study.ModalitiesJson.Should().Be("[\"CT\"]");

        // 5. GET /api/v1/imaging/studies/{site2AnonymizedStudyUid} as crc at SITE-CANARY-02 -> 200 OK
        using var site2GetStudyResp = await crcSite2.GetAsync(
            $"/api/v1/imaging/studies/{site2BatchIngested.AnonymizedStudyUid}",
            ct);
        site2GetStudyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var site2Study = await site2GetStudyResp.Content.ReadFromJsonAsync<ImagingStudyEnvelope>(ct);
        site2Study.Should().NotBeNull();
        site2Study!.InstanceCount.Should().Be(2);
        site2Study.ModalitiesJson.Should().Contain("MR").And.Contain("CT");

        // 6. GET /api/v1/imaging/studies/{site2AnonymizedStudyUid}/series/{site2SeriesUid}/instances/{site2SopUid}/frames/1 as crc at SITE-CANARY-01 -> 404 NotFound
        using var crossSiteFrameResp = await crcSite1.GetAsync(
            $"/api/v1/imaging/studies/{site2SingleIngested.AnonymizedStudyUid}/series/{site2SingleIngested.AnonymizedSeriesUid}/instances/{site2SingleIngested.AnonymizedSopUid}/frames/1",
            ct);
        crossSiteFrameResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // And route-swapped parent study/series UIDs also return 404 NotFound even for an authorized study UID
        using var swappedParentFrameResp = await crcSite1.GetAsync(
            $"/api/v1/imaging/studies/{site1Ingested.AnonymizedStudyUid}/series/{site1Ingested.AnonymizedSeriesUid}/instances/{site2SingleIngested.AnonymizedSopUid}/frames/1",
            ct);
        swappedParentFrameResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Whereas crc at SITE-CANARY-02 can read its own frame -> 200 OK
        using var ownSiteFrameResp = await crcSite2.GetAsync(
            $"/api/v1/imaging/studies/{site2SingleIngested.AnonymizedStudyUid}/series/{site2SingleIngested.AnonymizedSeriesUid}/instances/{site2SingleIngested.AnonymizedSopUid}/frames/1",
            ct);
        ownSiteFrameResp.StatusCode.Should().Be(HttpStatusCode.OK);
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

    [Fact]
    public async Task StoreInstancesAsync_Returns400BadRequest_WhenHeaderOnlyDicomHasMaxUshortDimensions()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"StoreInstancesOverflowDims_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, DicomIngestionServiceTests.TestDeidentifier(), SystemClock.Instance);

        byte[] headerOnlyDicom = DicomIngestionServiceTests.CreateHeaderOnlyDicomBytes(
            rows: ushort.MaxValue,
            columns: ushort.MaxValue);

        var httpContext = CreateAuthorizedHttpContext("STUDY-DIMS-EP", "SITE-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.QueryString = new QueryString("?studyId=STUDY-DIMS-EP&siteId=SITE-01&subjectId=SUBJ-DIMS-EP");
        httpContext.Request.ContentType = "application/dicom";
        httpContext.Request.Body = new MemoryStream(headerOnlyDicom, writable: false);

        var result = await DicomWebEndpoints.StoreInstancesAsync(httpContext.Request, service, ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        string json = JsonSerializer.Serialize(((IValueHttpResult)result).Value);
        json.Should().Contain("\"DICOM_INGESTION_FAILED\"");
        (await dbContext.ImagingInstanceEnvelopes.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task StoreInstancesAsync_Returns413PayloadTooLarge_WhenStreamExceedsSingleInstanceLimitWithoutContentLength()
    {
        var ct = TestContext.Current.CancellationToken;
        var httpContext = CreateAuthorizedHttpContext("STUDY-SINGLE-413", "SITE-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.QueryString = new QueryString("?studyId=STUDY-SINGLE-413&siteId=SITE-01&subjectId=SUBJ-SINGLE-413");
        httpContext.Request.ContentType = "application/dicom";
        await using var oversizedStream = new SyntheticZeroReadStream(DicomWebEndpoints.MaxSingleInstanceSizeBytes + 65_536L);
        httpContext.Request.Body = oversizedStream;

        var mockIngestionService = Substitute.For<IDicomIngestionService>();

        var result = await DicomWebEndpoints.StoreInstancesAsync(httpContext.Request, mockIngestionService, ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);

        await mockIngestionService.DidNotReceiveWithAnyArgs().IngestInstanceAsync(
            default!,
            default!,
            default!,
            default!,
            default!,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(65_535, 65_535)]
    [InlineData(8_192, 4_097)]
    [InlineData(0, 512)]
    [InlineData(512, 0)]
    [InlineData(-1, 512)]
    public async Task GetInstanceFramesAsync_Returns422UnprocessableEntity_WhenDimensionsOverflowOrExceed64Megabytes(
        int rows,
        int columns)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"FrameEndpointInvalidDims_{rows}_{columns}_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);

        var (studyUid, seriesUid, instanceUid) = await SeedStudySeriesAndInstanceAsync(dbContext, "STUDY-FRAME-422", "SITE-01", rows, columns, ct);
        var caller = CreateAuthorizedHttpContext("STUDY-FRAME-422", "SITE-01");

        var result = await DicomWebEndpoints.GetInstanceFramesAsync(
            studyUid: studyUid,
            seriesUid: seriesUid,
            instanceUid: instanceUid,
            frameList: "1",
            dbContext: dbContext,
            httpContext: caller,
            cancellationToken: ct);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        problem.ProblemDetails.Title.Should().Be("Invalid DICOM frame dimensions");
    }

    [Fact]
    public async Task GetInstanceFramesAsync_Returns200OctetStream_WhenDimensionsAreWithinBounds()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"FrameEndpointValidDims_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);

        var (studyUid, seriesUid, instanceUid) = await SeedStudySeriesAndInstanceAsync(dbContext, "STUDY-FRAME-200", "SITE-01", rows: 64, columns: 64, ct);
        var caller = CreateAuthorizedHttpContext("STUDY-FRAME-200", "SITE-01");

        var result = await DicomWebEndpoints.GetInstanceFramesAsync(
            studyUid: studyUid,
            seriesUid: seriesUid,
            instanceUid: instanceUid,
            frameList: "1",
            dbContext: dbContext,
            httpContext: caller,
            cancellationToken: ct);

        var fileResult = result.Should().BeOfType<FileContentHttpResult>().Subject;
        fileResult.ContentType.Should().Be("application/octet-stream");
        fileResult.FileContents.Length.Should().Be(64 * 64 * 2);
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_Returns413PayloadTooLarge_WhenHighRatioZeroFilledZipUploadedAsRawBody()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"BatchZipBombRawEndpoint_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, DicomIngestionServiceTests.TestDeidentifier(), SystemClock.Instance);

        byte[] highRatioZip = DicomIngestionServiceTests.CreateZeroFilledZipBytes(entryCount: 8, bytesPerEntry: 16 * 1024 * 1024);
        var spoolBefore = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-*").ToHashSet(StringComparer.Ordinal);

        await using var innerMs = new MemoryStream(highRatioZip, writable: false);
        await using var nonSeekable = new DicomIngestionServiceTests.NonSeekableTestReadStream(innerMs);

        var httpContext = CreateAuthorizedHttpContext("STUDY-BOMB-EP", "SITE-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.QueryString = new QueryString("?studyId=STUDY-BOMB-EP&siteId=SITE-01&subjectId=SUBJ-BOMB-EP");
        httpContext.Request.ContentType = "application/zip";
        httpContext.Request.Body = nonSeekable;

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, service, ct);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        problem.ProblemDetails.Detail.Should().Contain("decompression ratio limit");

        (await dbContext.ImagingInstanceEnvelopes.CountAsync(ct)).Should().Be(0);

        var leakedTempFiles = Directory.GetFiles(Path.GetTempPath(), "cadence-dicom-*")
            .Where(path => !spoolBefore.Contains(path))
            .ToArray();
        leakedTempFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreBatchInstancesAsync_Returns413PayloadTooLarge_WhenHighRatioZeroFilledZipUploadedViaMultipart()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<ImagingDbContext>()
            .UseInMemoryDatabase($"BatchZipBombMultipartEndpoint_{Guid.NewGuid():N}")
            .Options;
        await using var dbContext = new ImagingDbContext(options);
        var service = new DicomIngestionService(dbContext, DicomIngestionServiceTests.TestDeidentifier(), SystemClock.Instance);

        byte[] highRatioZip = DicomIngestionServiceTests.CreateZeroFilledZipBytes(entryCount: 8, bytesPerEntry: 16 * 1024 * 1024);
        string boundary = "----CadenceZipBombBoundary" + Guid.NewGuid().ToString("N");
        byte[] multipartBytes = BuildMultipartPayload(
            boundary,
            leadingFields:
            [
                ("studyId", "STUDY-BOMB-MP"),
                ("siteId", "SITE-01"),
                ("subjectId", "SUBJ-BOMB-MP")
            ],
            files:
            [
                ("bomb_8x16mb.zip", "application/zip", highRatioZip)
            ],
            trailingFields: []);

        await using var innerMs = new MemoryStream(multipartBytes, writable: false);
        await using var bodyStream = new DicomIngestionServiceTests.NonSeekableTestReadStream(innerMs);

        var httpContext = CreateAuthorizedHttpContext("STUDY-BOMB-MP", "SITE-01");
        httpContext.Request.Method = "POST";
        httpContext.Request.ContentType = $"multipart/form-data; boundary={boundary}";
        httpContext.Request.Body = bodyStream;

        var result = await DicomWebEndpoints.StoreBatchInstancesAsync(httpContext.Request, service, ct);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);
        problem.ProblemDetails.Detail.Should().Contain("decompression ratio limit");
        (await dbContext.ImagingInstanceEnvelopes.CountAsync(ct)).Should().Be(0);
    }

    private static async Task<(string StudyUid, string SeriesUid, string InstanceUid)> SeedStudySeriesAndInstanceAsync(
        ImagingDbContext dbContext,
        string studyId,
        string siteId,
        int rows,
        int columns,
        CancellationToken ct)
    {
        string studyEnvelopeId = Guid.NewGuid().ToString("N");
        string seriesEnvelopeId = Guid.NewGuid().ToString("N");
        string studyInstanceUid = $"1.2.826.0.1.3680043.10.99.100.{Guid.NewGuid():N}";
        string seriesInstanceUid = $"1.2.826.0.1.3680043.10.99.200.{Guid.NewGuid():N}";
        string sopInstanceUid = $"1.2.826.0.1.3680043.10.99.{Guid.NewGuid():N}";

        dbContext.ImagingStudyEnvelopes.Add(new ImagingStudyEnvelope
        {
            Id = studyEnvelopeId,
            StudyInstanceUid = studyInstanceUid,
            StudyId = studyId,
            SiteId = siteId,
            SubjectId = "SUBJ-FRAME-01",
            ModalitiesJson = "[\"CT\"]",
            StudyDate = "20260301",
            StudyDescription = "Frame Bounds Test",
            SeriesCount = 1,
            InstanceCount = 1,
            Status = "STORED",
            AdjudicationStatus = "UNASSIGNED",
            CreatedAt = DateTimeOffset.UnixEpoch,
            CreatedBy = "tester",
            UpdatedAt = DateTimeOffset.UnixEpoch,
            VersionIndex = 1
        });

        dbContext.ImagingSeriesEnvelopes.Add(new ImagingSeriesEnvelope
        {
            Id = seriesEnvelopeId,
            StudyEnvelopeId = studyEnvelopeId,
            SeriesInstanceUid = seriesInstanceUid,
            SeriesNumber = 1,
            Modality = "CT",
            SeriesDescription = "CT Series",
            InstanceCount = 1
        });

        dbContext.ImagingInstanceEnvelopes.Add(new ImagingInstanceEnvelope
        {
            Id = Guid.NewGuid().ToString("N"),
            SeriesEnvelopeId = seriesEnvelopeId,
            SopInstanceUid = sopInstanceUid,
            SopClassUid = "1.2.840.10008.5.1.4.1.1.2",
            InstanceNumber = 1,
            Rows = rows,
            Columns = columns,
            BitsAllocated = 16,
            PixelSpacingJson = "[0.5,0.5]",
            ObjectKey = $"dicom/{studyEnvelopeId}/{seriesEnvelopeId}/{sopInstanceUid}.dcm",
            ChecksumSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            SizeBytes = 1_024,
            BurnedInPhiMasked = true
        });

        await dbContext.SaveChangesAsync(ct);
        return (studyInstanceUid, seriesInstanceUid, sopInstanceUid);
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
