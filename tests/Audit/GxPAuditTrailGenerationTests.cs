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
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Audit;

/// <summary>
/// Automated xUnit test suite verifying GxP 21 CFR Part 11 audit trail generation and SHA-256 Merkle block chaining
/// across CTMS, eTMF, Quality, and Fileshare microservice DbContexts.
/// </summary>
public class GxPAuditTrailGenerationTests
{
    private static readonly string TestUser = "compliance.auditor@cadence.clinical";

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task CtmsDbContext_GivenEntityMutations_GeneratesMerkleChainedAuditLogEntries()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new GxPAuditInterceptor(() => TestUser))
            .Options;

        await using var context = new CtmsDbContext(options);

        // Act 1: Add new CTMS milestone
        var milestone = new CtmsSiteMilestone
        {
            SiteId = "SITE-101",
            StudyId = "ST-1001",
            MilestoneType = "INITIATION",
            Status = "ACTIVE",
            ReasonForChange = "Initial milestone creation"
        };
        context.CtmsSiteMilestones.Add(milestone);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert 1
        milestone.CreatedBy.Should().Be(TestUser);
        milestone.VersionIndex.Should().Be(1);

        var auditEntries1 = await context.AuditLogEntries.Where(x => x.EntityName == nameof(CtmsSiteMilestone)).ToListAsync(TestContext.Current.CancellationToken);
        auditEntries1.Should().HaveCount(1);
        var firstAudit = auditEntries1[0];
        firstAudit.Action.Should().Be("Added");
        firstAudit.UserId.Should().Be(TestUser);
        firstAudit.PredecessorHash.Should().BeEmpty();
        firstAudit.BlockHash.Should().NotBeNullOrEmpty();

        // Act 2: Update CTMS milestone
        milestone.Status = "COMPLETED";
        milestone.ReasonForChange = "Site initiation visit completed";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert 2
        milestone.VersionIndex.Should().Be(2);

        var auditEntries2 = await context.AuditLogEntries.Where(x => x.EntityName == nameof(CtmsSiteMilestone)).OrderBy(x => x.Timestamp).ToListAsync(TestContext.Current.CancellationToken);
        auditEntries2.Should().HaveCount(2);
        var secondAudit = auditEntries2[auditEntries2.Count - 1];
        secondAudit.Action.Should().Be("Modified");
        secondAudit.PredecessorHash.Should().Be(firstAudit.BlockHash);
        secondAudit.BlockHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
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
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert 1
        doc.CreatedBy.Should().Be(TestUser);
        doc.VersionIndex.Should().Be(1);

        var auditEntriesEtmf = await context.AuditLogEntries.Where(x => x.EntityName == nameof(TmfDocument)).ToListAsync(TestContext.Current.CancellationToken);
        auditEntriesEtmf.Should().HaveCount(1);
        auditEntriesEtmf[0].UserId.Should().Be(TestUser);
        auditEntriesEtmf[0].BlockHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
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
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert 1
        deviation.CreatedBy.Should().Be(TestUser);
        deviation.VersionIndex.Should().Be(1);

        var auditEntriesQuality = await context.AuditLogEntries.Where(x => x.EntityName == nameof(QualityDeviation)).ToListAsync(TestContext.Current.CancellationToken);
        auditEntriesQuality.Should().HaveCount(1);
        auditEntriesQuality[0].UserId.Should().Be(TestUser);
        auditEntriesQuality[0].BlockHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
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
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert 1
        file.CreatedBy.Should().Be(TestUser);
        file.VersionIndex.Should().Be(1);

        var auditEntriesFileshare = await context.AuditLogEntries.Where(x => x.EntityName == nameof(FileRecord)).ToListAsync(TestContext.Current.CancellationToken);
        auditEntriesFileshare.Should().HaveCount(1);
        auditEntriesFileshare[0].UserId.Should().Be(TestUser);
        auditEntriesFileshare[0].BlockHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task SaveChanges_WithAnAuditedChangeAndNoActor_FailsWithoutWriting()
    {
        // #4767: a write whose actor cannot be resolved must fail, never be recorded against a default.
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new GxPAuditInterceptor(() => throw new InvalidOperationException("no actor")))
            .Options;
        await using var context = new CtmsDbContext(options);
        context.CtmsSiteMilestones.Add(new CtmsSiteMilestone
        {
            SiteId = "SITE-101",
            StudyId = "ST-1001",
            MilestoneType = "INITIATION",
            Status = "ACTIVE",
            ReasonForChange = "Initial milestone creation"
        });

        Func<Task> save = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("no actor");
        await using var verify = new CtmsDbContext(options);
        (await verify.CtmsSiteMilestones.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task SaveChanges_WithNoAuditedChange_DoesNotNeedAnActor()
    {
        // Saves with nothing to audit (e.g. outbox bookkeeping in a background scope) must not demand an actor.
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new GxPAuditInterceptor(() => throw new InvalidOperationException("no actor")))
            .Options;
        await using var context = new CtmsDbContext(options);

        int written = await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        written.Should().Be(0);
    }
}
