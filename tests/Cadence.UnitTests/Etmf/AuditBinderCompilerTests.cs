using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Etmf;

[SuppressMessage("SonarAnalyzer.CSharp", "S6966", Justification = "ZipArchiveEntry.Open does not provide an asynchronous overload in System.IO.Compression.")]
public class AuditBinderCompilerTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 6, 14, 0, 0, TimeSpan.Zero));

    private static EtmfDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(databaseName: $"EtmfTestDb_{Guid.NewGuid()}")
            .Options;

        return new EtmfDbContext(options);
    }

    /// <summary>
    /// Validates that AuditBinderCompiler produces a complete regulatory binder ZIP archive containing
    /// DIA TMF documents, manifest.json, audit_chain_verification.json, signature_manifestations.json,
    /// and checksums.sha256.
    /// @req:PRD-QLT-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-QLT-008")]
    public async Task CompileBinderAsync_CreatesValidZipArchiveWithManifestsAndChecksums()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();

        // Arrange
        var doc1 = new TmfDocument("DOC-001")
        {
            StudyId = "STUDY-001",
            SiteId = "SITE-101",
            Filename = "Clinical_Protocol_v1.pdf",
            Content = "Clinical Trial Protocol Content Sample",
            MimeType = "application/pdf",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Clinical Trial Protocol",
            Status = "SIGNED",
            ApprovalStatus = "APPROVED",
            Signer = "Dr. Alice Smith",
            SigningTimestamp = _clock.UtcNow,
            ReasonForChange = "Initial protocol approval",
            SignatureManifestation = "21 CFR Part 11 Validated Signature",
            ContentChecksum = "a1b2c3d4e5f6",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "alice.smith",
            VersionIndex = 1
        };

        var doc2 = new TmfDocument("DOC-002")
        {
            StudyId = "STUDY-001",
            SiteId = "SITE-101",
            Filename = "FDA_Form_1572.pdf",
            Content = "FDA Form 1572 Statement of Investigator Content",
            MimeType = "application/pdf",
            Zone = 5,
            Section = "05.02",
            ArtifactCode = "05.02.01",
            ArtifactType = "FDA Form 1572",
            Status = "APPROVED",
            ApprovalStatus = "APPROVED",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "bob.jones",
            VersionIndex = 1
        };

        var auditLog = new TmfAuditLog
        {
            Timestamp = _clock.UtcNow,
            UserId = "alice.smith",
            UserRole = "Sponsor Admin",
            Action = "SIGN",
            DocumentId = "DOC-001",
            Details = "Signed protocol v1",
            CryptographicSeal = "seal-001"
        };

        dbContext.TmfDocuments.AddRange(doc1, doc2);
        dbContext.TmfAuditLogs.Add(auditLog);
        await dbContext.SaveChangesAsync(ct);

        var compiler = new AuditBinderCompiler(dbContext, _clock);

        // Act
        var result = await compiler.CompileBinderAsync("STUDY-001", includeHistory: false, ct);

        // Assert
        result.Should().NotBeNull();
        result.StudyId.Should().Be("STUDY-001");
        result.TotalDocuments.Should().Be(2);
        result.TotalAuditLogs.Should().Be(1);
        result.ZipArchiveBytes.Should().NotBeEmpty();
        result.PackageSha256Checksum.Should().HaveLength(64);

        using var zipStream = new MemoryStream(result.ZipArchiveBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var entryNames = archive.Entries.Select(e => e.FullName).ToList();
        entryNames.Should().Contain("manifest.json");
        entryNames.Should().Contain("audit_chain_verification.json");
        entryNames.Should().Contain("signature_manifestations.json");
        entryNames.Should().Contain("checksums.sha256");

        entryNames.Should().ContainMatch("documents/Zone_01_Trial_Management/Section_01.01/DOC-001_Clinical_Protocol_v1.pdf");
        entryNames.Should().ContainMatch("documents/Zone_05_Site_Management/Section_05.02/DOC-002_FDA_Form_1572.pdf");

        var checksumsEntry = archive.GetEntry("checksums.sha256");
        checksumsEntry.Should().NotBeNull();
        using var checksumsReader = new StreamReader(checksumsEntry!.Open());
        var checksumsText = await checksumsReader.ReadToEndAsync(ct);
        checksumsText.Should().Contain("manifest.json");
        checksumsText.Should().Contain("audit_chain_verification.json");
        checksumsText.Should().Contain("signature_manifestations.json");
    }

    /// <summary>
    /// Validates bitemporal HMAC verification and Merkle root hash computation during binder compilation.
    /// @req:PRD-QLT-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-QLT-008")]
    public async Task CompileBinderAsync_VerifiesAuditChainAndComputesMerkleRoot()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();

        var secretKey = "Cadence_Etmf_Audit_Secret_Key_32bytes!";
        Environment.SetEnvironmentVariable("AUDIT_LOG_SECRET_KEY", secretKey);
        var ts1 = _clock.UtcNow.AddMinutes(-10);
        var ts2 = _clock.UtcNow;

        var digest1 = AuditDigestCalculator.ComputeAuditDigest(
            eventId: "evt-1",
            serviceName: "Cadence.Apps.Etmf",
            actionType: "INGEST",
            entityName: "TmfDocument",
            entityId: "DOC-100",
            userId: "user.a",
            tenantId: "tenant_default",
            reasonForChange: "Ingested document DOC-100",
            timestamp: ts1.ToString("o"),
            previousDigest: "GENESIS_BLOCK_0000000000000000000000000000000000000000000000000000000000000000",
            secretKey: secretKey);

        var digest2 = AuditDigestCalculator.ComputeAuditDigest(
            eventId: "evt-2",
            serviceName: "Cadence.Apps.Etmf",
            actionType: "QC_REVIEW_ACCEPT",
            entityName: "TmfDocument",
            entityId: "DOC-100",
            userId: "user.b",
            tenantId: "tenant_default",
            reasonForChange: "Approved document DOC-100",
            timestamp: ts2.ToString("o"),
            previousDigest: digest1,
            secretKey: secretKey);

        var log1 = new TmfAuditLog("evt-1")
        {
            Timestamp = ts1,
            UserId = "user.a",
            UserRole = "CRC",
            Action = "INGEST",
            DocumentId = "DOC-100",
            Details = "Ingested document DOC-100",
            CryptographicSeal = digest1
        };
        var log2 = new TmfAuditLog("evt-2")
        {
            Timestamp = ts2,
            UserId = "user.b",
            UserRole = "CRA",
            Action = "QC_REVIEW_ACCEPT",
            DocumentId = "DOC-100",
            Details = "Approved document DOC-100",
            CryptographicSeal = digest2
        };

        dbContext.TmfAuditLogs.AddRange(log1, log2);
        await dbContext.SaveChangesAsync(ct);

        var compiler = new AuditBinderCompiler(dbContext, _clock);

        var result = await compiler.CompileBinderAsync("STUDY-002", includeHistory: true, ct);

        using var zipStream = new MemoryStream(result.ZipArchiveBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        var auditVerificationEntry = archive.GetEntry("audit_chain_verification.json");
        auditVerificationEntry.Should().NotBeNull();

        using var reader = new StreamReader(auditVerificationEntry!.Open());
        var json = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("valid").GetBoolean().Should().BeTrue();
        root.GetProperty("total_records").GetInt32().Should().Be(2);
        root.GetProperty("merkle_root_hash").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Validates ExportRegulatoryBinder REST endpoint response and audit trail generation.
    /// @req:PRD-QLT-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-QLT-008")]
    public async Task ExportRegulatoryBinder_Endpoint_ReturnsFileAndAuditsAction()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();

        var doc = new TmfDocument("DOC-500")
        {
            StudyId = "STUDY-500",
            Filename = "Protocol.pdf",
            Content = "Sample Protocol",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Status = "APPROVED",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "admin",
            VersionIndex = 1
        };
        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(ct);

        var httpContext = new DefaultHttpContext();

        var result = await EtmfBinderEndpoints.ExportRegulatoryBinder(
            study_id: "STUDY-500",
            include_history: false,
            dbContext: dbContext,
            clock: _clock,
            httpContext: httpContext,
            ct: ct);

        result.Should().BeOfType<FileContentHttpResult>();
        var fileResult = (FileContentHttpResult)result;
        fileResult.ContentType.Should().Be("application/zip");
        fileResult.FileDownloadName.Should().Be("study_STUDY-500_audit_binder.zip");

        var audit = await dbContext.TmfAuditLogs.FirstOrDefaultAsync(a => a.Action == "BINDER_EXPORT", ct);
        audit.Should().NotBeNull();
        audit!.Details.Should().Contain("STUDY-500");
    }

    /// <summary>
    /// Validates GetBinderStructure endpoint returns hierarchical zone/section document trees.
    /// @req:PRD-QLT-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-QLT-008")]
    public async Task GetBinderStructure_Endpoint_ReturnsHierarchicalTree()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();

        dbContext.TmfDocuments.Add(new TmfDocument("DOC-701")
        {
            StudyId = "STUDY-700",
            Filename = "Form1572.pdf",
            Zone = 5,
            Section = "05.02",
            ArtifactCode = "05.02.01",
            ArtifactType = "FDA Form 1572",
            Status = "SIGNED",
            SignatureManifestation = "21 CFR 11 Signed",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra",
            VersionIndex = 1
        });
        await dbContext.SaveChangesAsync(ct);

        var result = await EtmfBinderEndpoints.GetBinderStructure("STUDY-700", dbContext, ct);

        var okResult = (Ok<BinderStructureResponse>)result;
        var response = okResult.Value!;

        response.StudyId.Should().Be("STUDY-700");
        response.TotalDocuments.Should().Be(1);
        response.Zones.Should().HaveCount(1);
        response.Zones[0].Zone.Should().Be(5);
        response.Zones[0].ZoneName.Should().Be("Site Management");
        response.Zones[0].Sections[0].Artifacts[0].HasSignature.Should().BeTrue();
    }

    /// <summary>
    /// Validates GetInspectionReadiness endpoint computes compliance scores and audit integrity.
    /// @req:PRD-QLT-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-QLT-008")]
    public async Task GetInspectionReadiness_Endpoint_ComputesReadinessAndAuditIntegrity()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();

        dbContext.TmfDocuments.Add(new TmfDocument("DOC-801")
        {
            StudyId = "STUDY-800",
            Filename = "Protocol.pdf",
            Zone = 1,
            Section = "01.01",
            Status = "APPROVED",
            ApprovalStatus = "APPROVED",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "admin",
            VersionIndex = 1
        });
        await dbContext.SaveChangesAsync(ct);

        var result = await EtmfBinderEndpoints.GetInspectionReadiness("STUDY-800", dbContext, _clock, ct);

        var okResult = (Ok<InspectionReadinessResponse>)result;
        var readiness = okResult.Value!;

        readiness.StudyId.Should().Be("STUDY-800");
        readiness.InspectionReadinessScore.Should().Be(100.0);
        readiness.AuditChainIntact.Should().BeTrue();
        readiness.OverallComplianceStatus.Should().Be("INSPECTION_READY");
    }

    /// <summary>
    /// Validates SignOffDocument, VerifyDocumentSignature, and GetWatermarkedDocument endpoints.
    /// @req:PRD-QLT-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-QLT-008")]
    public async Task DocumentLifecycle_SignOff_Verify_And_Watermark()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();

        var doc = new TmfDocument("DOC-901")
        {
            StudyId = "STUDY-900",
            Filename = "Protocol_Final.pdf",
            Content = "Final protocol details for inspection.",
            MimeType = "application/pdf",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Status = "DRAFT",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "writer",
            VersionIndex = 1
        };
        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(ct);

        var httpContext = new DefaultHttpContext();

        // 1. Sign off document
        var signRequest = new DocumentSignOffRequest("Formal sign-off for regulatory submission");
        var signResult = await EtmfBinderEndpoints.SignOffDocument("DOC-901", signRequest, dbContext, _clock, httpContext, ct);
        signResult.Should().BeOfType<Ok<TmfDocument>>();

        // 2. Verify signature
        var verifyResult = await EtmfBinderEndpoints.VerifyDocumentSignature("DOC-901", dbContext, ct);
        verifyResult.Should().NotBeNull();

        // 3. Download watermarked document
        var watermarkResult = await EtmfBinderEndpoints.GetWatermarkedDocument("DOC-901", dbContext, _clock, httpContext, ct);
        watermarkResult.Should().BeOfType<FileContentHttpResult>();
        var fileRes = (FileContentHttpResult)watermarkResult;
        fileRes.FileDownloadName.Should().Be("WATERMARKED_Protocol_Final.pdf");
        var watermarkContent = Encoding.UTF8.GetString(fileRes.FileContents.ToArray());
        watermarkContent.Should().Contain("CADENCE CLINICAL REGULATORY INSPECTION COPY");
    }

    /// <summary>
    /// Validates GetCompleteness and GetEmsExport endpoints.
    /// @req:PRD-QLT-008
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-QLT-008")]
    public async Task GetCompleteness_And_GetEmsExport_Endpoints_ReturnExpectedResults()
    {
        var ct = TestContext.Current.CancellationToken;
        using var dbContext = CreateInMemoryDbContext();

        var httpContext = new DefaultHttpContext();

        // 1. GetCompleteness
        var completenessRes = await EtmfBinderEndpoints.GetCompleteness("STUDY-001", "INITIATION", null, dbContext, _clock, httpContext, ct);
        completenessRes.Should().BeOfType<Ok<CompletenessResponse>>();
        var completenessValue = ((Ok<CompletenessResponse>)completenessRes).Value!;
        completenessValue.StudyId.Should().Be("STUDY-001");

        // 2. GetEmsExport
        var emsRes = await EtmfBinderEndpoints.GetEmsExport("STUDY-001", "Study 001 Title", dbContext, _clock, httpContext, ct);
        emsRes.Should().BeOfType<FileContentHttpResult>();
        var fileRes = (FileContentHttpResult)emsRes;
        fileRes.ContentType.Should().Be("application/zip");
        fileRes.FileDownloadName.Should().Be("ems_export_STUDY-001.zip");
    }
}
