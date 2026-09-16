using System;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Fileshare.Infrastructure.Persistence;
using Cadence.Apps.Quality.Domain;
using Cadence.Apps.Quality.Infrastructure.Persistence;
using Cadence.Database;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Audit;

/// <summary>
/// Automated xUnit test suite verifying GxP 21 CFR Part 11 audit trail generation and SHA-256 Merkle block chaining
/// across CTMS, eTMF, Quality, and Fileshare microservice DbContexts.
/// @req:PRD-SYS-053
/// </summary>
public class GxPAuditTrailGenerationTests
{
    private static readonly string TestUser = "compliance.auditor@cadence.clinical";

    [Fact]
    public async Task CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new GxPAuditInterceptor(() => TestUser))
            .Options;

        await using var context = new CtmsDbContext(options);

        // Act 1: Add new CTMS study
        var study = new CtmsStudy
        {
            StudyId = "ST-1001",
            Name = "Phase III Clinical Trial",
            Status = "ACTIVE",
            ReasonForChange = "Initial study creation"
        };
        context.CtmsStudies.Add(study);
        await context.SaveChangesAsync();

        // Assert 1
        study.CreatedBy.Should().Be(TestUser);
        study.VersionIndex.Should().Be(1);

        var auditEntries1 = await context.AuditLogEntries.Where(x => x.EntityName == nameof(CtmsStudy)).ToListAsync();
        auditEntries1.Should().HaveCount(1);
        var firstAudit = auditEntries1.First();
        firstAudit.Action.Should().Be("Added");
        firstAudit.UserId.Should().Be(TestUser);
        firstAudit.PredecessorHash.Should().BeEmpty();
        firstAudit.BlockHash.Should().NotBeNullOrEmpty();

        // Act 2: Update CTMS study
        study.Status = "COMPLETED";
        study.ReasonForChange = "Study protocol completed";
        await context.SaveChangesAsync();

        // Assert 2
        study.VersionIndex.Should().Be(2);

        var auditEntries2 = await context.AuditLogEntries.Where(x => x.EntityName == nameof(CtmsStudy)).OrderBy(x => x.Timestamp).ToListAsync();
        auditEntries2.Should().HaveCount(2);
        var secondAudit = auditEntries2.Last();
        secondAudit.Action.Should().Be("Modified");
        secondAudit.PredecessorHash.Should().Be(firstAudit.BlockHash);
        secondAudit.BlockHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EtmfDbContext_GivenDocumentMutations_GeneratesMerkleChainedAuditLogEntries()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new GxPAuditInterceptor(() => TestUser))
            .Options;

        await using var context = new EtmfDbContext(options);

        // Act 1: Add new TMF document
        var doc = new TmfDocument
        {
            StudyId = "ST-2002",
            Zone = 1,
            Section = "Trial Management",
            ArtifactType = "Protocol",
            Filename = "Protocol_v1.pdf",
            Content = "binary_payload_hash",
            MimeType = "application/pdf",
            TaxonomyVersion = "v2.0",
            ArtifactCode = "ART-01",
            ReasonForChange = "Uploading trial protocol"
        };
        context.TmfDocuments.Add(doc);
        await context.SaveChangesAsync();

        // Assert 1
        doc.CreatedBy.Should().Be(TestUser);
        doc.VersionIndex.Should().Be(1);

        var auditEntries = await context.AuditLogEntries.Where(x => x.EntityName == nameof(TmfDocument)).ToListAsync();
        auditEntries.Should().HaveCount(1);
        auditEntries.First().UserId.Should().Be(TestUser);
        auditEntries.First().BlockHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task QualityDbContext_GivenDeviationMutations_GeneratesMerkleChainedAuditLogEntries()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<QualityDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new GxPAuditInterceptor(() => TestUser))
            .Options;

        await using var context = new QualityDbContext(options);

        // Act 1: Add Quality deviation
        var deviation = new QualityDeviation
        {
            StudyId = "ST-3003",
            Title = "Temperature Excursion in Storage",
            Description = "Refrigerated medicine stored at 12C for 2 hours",
            Severity = "MAJOR",
            Status = "OPEN",
            Type = "STORAGE_EXCURSION",
            IsProtocolViolation = true,
            ReasonForChange = "Logging temperature deviation"
        };
        context.QualityDeviations.Add(deviation);
        await context.SaveChangesAsync();

        // Assert 1
        deviation.CreatedBy.Should().Be(TestUser);
        deviation.VersionIndex.Should().Be(1);

        var auditEntries = await context.AuditLogEntries.Where(x => x.EntityName == nameof(QualityDeviation)).ToListAsync();
        auditEntries.Should().HaveCount(1);
        auditEntries.First().UserId.Should().Be(TestUser);
        auditEntries.First().BlockHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FileshareDbContext_GivenFileRecordMutations_GeneratesMerkleChainedAuditLogEntries()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FileshareDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new GxPAuditInterceptor(() => TestUser))
            .Options;

        await using var context = new FileshareDbContext(options);

        // Act 1: Add FileRecord
        var file = new FileRecord
        {
            StudyId = "ST-4004",
            Filename = "Lab_Results_001.pdf",
            MimeType = "application/pdf",
            SizeBytes = 1024567,
            ObjectKey = "s3://clinical-files/lab_results_001.pdf",
            UploadedBy = TestUser,
            ReasonForChange = "Uploading lab results"
        };
        context.FileRecords.Add(file);
        await context.SaveChangesAsync();

        // Assert 1
        file.CreatedBy.Should().Be(TestUser);
        file.VersionIndex.Should().Be(1);

        var auditEntries = await context.AuditLogEntries.Where(x => x.EntityName == nameof(FileRecord)).ToListAsync();
        auditEntries.Should().HaveCount(1);
        auditEntries.First().UserId.Should().Be(TestUser);
        auditEntries.First().BlockHash.Should().NotBeNullOrEmpty();
    }
}
