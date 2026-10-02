using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public sealed class EtmfBulkTagEndpointsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly EtmfDbContext _dbContext;
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    public EtmfBulkTagEndpointsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new EtmfDbContext(options);
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private static HttpContext CreateHttpContext(string username = "document.manager")
    {
        var context = new DefaultHttpContext();
        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
        return context;
    }

    [Fact]
    [GxPRequirement("PRD-SYS-001")]
    public async Task BulkTagDocuments_UpdatesTargetDocumentsAndWritesAuditTrail()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        var doc1 = new TmfDocument("DOC-BULK-001")
        {
            StudyId = "STUDY-001",
            Filename = "doc1.pdf",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Status = "ACTIVE",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "test_user"
        };
        var doc2 = new TmfDocument("DOC-BULK-002")
        {
            StudyId = "STUDY-001",
            Filename = "doc2.pdf",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Status = "ACTIVE",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "test_user"
        };
        _dbContext.TmfDocuments.AddRange(doc1, doc2);
        await _dbContext.SaveChangesAsync(ct);

        var request = new BulkTagDocumentsRequest(
            DocumentIds: ["DOC-BULK-001", "DOC-BULK-002"],
            ReasonForChange: "Bulk taxonomy migration for study onboarding",
            Zone: 2,
            Section: "02.01",
            ArtifactCode: "02.01.01",
            ArtifactType: "Investigator Brochure",
            SiteId: "SITE-101",
            Milestone: "SITE_INITIATION"
        );

        var httpContext = CreateHttpContext("doc.manager.user");

        // Act
        var result = await EtmfIntelligenceEndpoints.BulkTagDocuments(
            request, _dbContext, _clock, httpContext, ct);

        // Assert
        var okResult = result.Should().BeOfType<Ok<BulkTagDocumentsResponse>>().Subject;
        var response = okResult.Value!;

        response.TotalRequested.Should().Be(2);
        response.TotalUpdated.Should().Be(2);
        response.TotalExcluded.Should().Be(0);

        var updated1 = await _dbContext.TmfDocuments.FindAsync(["DOC-BULK-001"], ct);
        updated1!.Zone.Should().Be(2);
        updated1.Section.Should().Be("02.01");
        updated1.ArtifactCode.Should().Be("02.01.01");
        updated1.ArtifactType.Should().Be("Investigator Brochure");
        updated1.SiteId.Should().Be("SITE-101");
        updated1.MetadataJson.Should().Contain("SITE_INITIATION");

        var updated2 = await _dbContext.TmfDocuments.FindAsync(["DOC-BULK-002"], ct);
        updated2!.Zone.Should().Be(2);

        // Verify Audit Trail Entries (PRD-SYS-001)
        var auditLogs = await _dbContext.TmfAuditLogs
            .Where(a => a.Action == "BULK_METADATA_TAG")
            .ToListAsync(ct);
        auditLogs.Should().HaveCount(2);
        auditLogs.Select(a => a.DocumentId).Should().Contain(["DOC-BULK-001", "DOC-BULK-002"]);
        auditLogs[0].UserId.Should().Be("doc.manager.user");
        auditLogs[0].ReasonForChange.Should().Be("Bulk taxonomy migration for study onboarding");
    }

    [Fact]
    public async Task BulkTagDocuments_RejectsReasonForChangeUnder10Characters()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new BulkTagDocumentsRequest(
            DocumentIds: ["DOC-BULK-001"],
            ReasonForChange: "Too short",
            Zone: 2
        );

        var result = await EtmfIntelligenceEndpoints.BulkTagDocuments(
            request, _dbContext, _clock, CreateHttpContext(), ct);

        var badRequest = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task BulkTagDocuments_RejectsMoreThan50Documents()
    {
        var ct = TestContext.Current.CancellationToken;
        var docIds = Enumerable.Range(1, 51).Select(i => $"DOC-{i}").ToList();
        var request = new BulkTagDocumentsRequest(
            DocumentIds: docIds,
            ReasonForChange: "Bulk tagging 51 documents which exceeds safety limit",
            Zone: 2
        );

        var result = await EtmfIntelligenceEndpoints.BulkTagDocuments(
            request, _dbContext, _clock, CreateHttpContext(), ct);

        var badRequest = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        badRequest.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task BulkTagDocuments_ExcludesDocumentsOnRetentionHold()
    {
        var ct = TestContext.Current.CancellationToken;
        var activeDoc = new TmfDocument("DOC-ACTIVE")
        {
            StudyId = "STUDY-001",
            Filename = "active.pdf",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Status = "ACTIVE",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "test_user"
        };
        var holdDoc = new TmfDocument("DOC-HOLD")
        {
            StudyId = "STUDY-001",
            Filename = "hold.pdf",
            Zone = 1,
            Section = "01.01",
            ArtifactCode = "01.01.01",
            ArtifactType = "Protocol",
            Status = "ON_HOLD",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "test_user"
        };
        _dbContext.TmfDocuments.AddRange(activeDoc, holdDoc);
        await _dbContext.SaveChangesAsync(ct);

        var request = new BulkTagDocumentsRequest(
            DocumentIds: ["DOC-ACTIVE", "DOC-HOLD"],
            ReasonForChange: "Updating zone for active documents while skipping hold documents",
            Zone: 3
        );

        var result = await EtmfIntelligenceEndpoints.BulkTagDocuments(
            request, _dbContext, _clock, CreateHttpContext(), ct);

        var okResult = result.Should().BeOfType<Ok<BulkTagDocumentsResponse>>().Subject;
        var response = okResult.Value!;

        response.TotalRequested.Should().Be(2);
        response.TotalUpdated.Should().Be(1);
        response.TotalExcluded.Should().Be(1);

        var updatedActive = await _dbContext.TmfDocuments.FindAsync(["DOC-ACTIVE"], ct);
        updatedActive!.Zone.Should().Be(3);

        var updatedHold = await _dbContext.TmfDocuments.FindAsync(["DOC-HOLD"], ct);
        updatedHold!.Zone.Should().Be(1); // unchanged
    }
}
