using System;
using System.Threading.Tasks;

using Cadence.Apps.Etmf.Application;
using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Endpoints;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Etmf;

public class EtmfIntelligenceEndpointsTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    private IEtmfQcService CreateQcService(EtmfDbContext dbContext) =>
        new EtmfQcService(dbContext, new EtmfUnitOfWork(dbContext), _clock);

    private IEtmfDocumentService CreateDocumentService(EtmfDbContext dbContext) =>
        new EtmfDocumentService(dbContext, new EtmfUnitOfWork(dbContext), _clock);

    private static DefaultHttpContext Caller(string studyId = "STUDY-01", string siteId = "SITE-01")
    {
        var context = new DefaultHttpContext();
        context.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "cra-1",
            Roles = ["cra"],
            AssignedStudies = [studyId],
            AssignedSites = [siteId],
        };
        return context;
    }

    [Theory]
    [InlineData("protocol_v1_draft.pdf", 1, "01.01", "01.01.01", "Clinical Trial Protocol", "HIGH")]
    [InlineData("FDA_Form_1572_Signed.pdf", 5, "05.02", "05.02.01", "FDA Form 1572", "HIGH")]
    [InlineData("Financial_Disclosure_Statement.pdf", 5, "05.02", "05.02.02", "Financial Disclosure", "HIGH")]
    [InlineData("Investigator_CV_2026.pdf", 5, "05.02", "05.02.03", "Investigator CV", "HIGH")]
    [InlineData("Delegation_of_Authority_Log.pdf", 5, "05.02", "05.02.04", "Delegation of Authority Log", "HIGH")]
    [InlineData("Informed_Consent_ICF_v2.pdf", 5, "05.02", "05.02.05", "Informed Consent Form", "HIGH")]
    [InlineData("Investigator_Brochure_v3.pdf", 2, "02.01", "02.01.01", "Investigator's Brochure", "HIGH")]
    [InlineData("IRB_Approval_Letter.pdf", 4, "04.02", "04.02.01", "IRB/IEC Approval Notification", "HIGH")]
    [InlineData("SAE_Safety_Report_001.pdf", 7, "07.01", "07.01.01", "Serious Adverse Event Report", "HIGH")]
    [InlineData("Central_Lab_Certificate.pdf", 8, "08.01", "08.01.01", "Central Laboratory Certificate", "HIGH")]
    [InlineData("Statistical_Analysis_Plan_v1.pdf", 11, "11.01", "11.01.01", "Statistical Analysis Plan", "HIGH")]
    [InlineData("Unrecognized_Document_Notes.txt", 1, "01.01", "01.01.01", "General Clinical Document", "LOW")]
    public async Task ClassifyDocument_ReturnsPredictedTaxonomy(
        string filename,
        int expectedZone,
        string expectedSection,
        string expectedArtifactCode,
        string expectedArtifactType,
        string expectedTier)
    {
        var request = new ClassifyRequest(filename);
        var result = await EtmfIntelligenceEndpoints.ClassifyDocument(request, _clock);

        var okResult = (Ok<DocumentIntelligenceReport>)result;
        var report = okResult.Value!;

        report.PrimaryClassification.Zone.Should().Be(expectedZone);
        report.PrimaryClassification.Section.Should().Be(expectedSection);
        report.PrimaryClassification.ArtifactCode.Should().Be(expectedArtifactCode);
        report.PrimaryClassification.ArtifactType.Should().Be(expectedArtifactType);
        report.ConfidenceTier.Should().Be(expectedTier);
    }

    /// <summary>The local heuristic gives the same classification every time for the same file (#5415).</summary>
    [Fact]
    public async Task LocalHeuristicFallback_ClassifiesTheSameFileTheSameWay()
    {
        var request = new ClassifyRequest("Site_Regulatory_Batch_Document_001.pdf");
        var first = ((Ok<DocumentIntelligenceReport>)await EtmfIntelligenceEndpoints.ClassifyDocument(request, _clock)).Value!;

        for (int i = 0; i < 100; i++)
        {
            var report = ((Ok<DocumentIntelligenceReport>)await EtmfIntelligenceEndpoints.ClassifyDocument(request, _clock)).Value!;
            report.PrimaryClassification.Should().BeEquivalentTo(first.PrimaryClassification);
            report.ConfidenceTier.Should().Be(first.ConfidenceTier);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../../../canary-outside-binder.txt")]
    [InlineData("..\\..\\manifest.json")]
    [InlineData("../../manifest.json")]
    [InlineData("IRB/IEC Approval.pdf")]
    [InlineData("bad\0name.pdf")]
    [InlineData("bad\nname.pdf")]
    public async Task ClassifyDocument_RejectsInvalidFilenameSegments_With400BadRequest(string invalidFilename)
    {
        var result = await EtmfIntelligenceEndpoints.ClassifyDocument(new ClassifyRequest(invalidFilename), _clock);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../../../canary-outside-binder.txt")]
    [InlineData("..\\..\\manifest.json")]
    [InlineData("../../manifest.json")]
    [InlineData("nested/protocol.pdf")]
    [InlineData("bad\0name.pdf")]
    public async Task StageQcDocument_RejectsInvalidFilenameSegments_With400BadRequestAndPersistsNothing(string invalidFilename)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var request = new StageDocumentQCRequest(
            Content: "{\"canary_manifest_collision\":true}",
            Filename: invalidFilename,
            StudyId: "STUDY-01",
            ReasonForChange: "Staging QC document for binder manifest check");

        var result = await EtmfIntelligenceEndpoints.StageQcDocument(
            request,
            CreateQcService(dbContext),
            Caller("STUDY-01"),
            ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await dbContext.TmfDocuments.CountAsync(ct)).Should().Be(0);
    }

    [Theory]
    [InlineData("../../../canary-outside-binder.txt", "01.01")]
    [InlineData("..\\..\\manifest.json", "01.01")]
    [InlineData("../../manifest.json", "01.01")]
    [InlineData(".", "01.01")]
    [InlineData("..", "01.01")]
    [InlineData("", "01.01")]
    [InlineData("doc\r\n.pdf", "01.01")]
    [InlineData("valid_protocol.pdf", "")]
    [InlineData("valid_protocol.pdf", ".")]
    [InlineData("valid_protocol.pdf", "..")]
    [InlineData("valid_protocol.pdf", "../../manifest.json")]
    [InlineData("valid_protocol.pdf", "..\\..\\01.01")]
    [InlineData("valid_protocol.pdf", "01/01")]
    [InlineData("valid_protocol.pdf", "01\001")]
    public async Task IngestDocument_RejectsInvalidFilenameOrSectionSegments_With400BadRequestAndPersistsNothing(
        string filename,
        string? section)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var request = new IngestDocumentRequest(
            Filename: filename,
            Content: "harmless-canary-marker",
            StudyId: "STUDY-01",
            SiteId: "SITE-01",
            MimeType: "text/plain",
            Zone: 1,
            Section: section,
            ArtifactCode: "01.01.01",
            ArtifactType: "Clinical Trial Protocol",
            TaxonomyVersion: "v3.2.0-complete",
            ReasonForChange: "Ingest intake validation test reason");

        var result = await EtmfDocumentEndpoints.IngestDocument(
            request,
            CreateDocumentService(dbContext),
            Caller("STUDY-01", "SITE-01"),
            ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await dbContext.TmfDocuments.CountAsync(ct)).Should().Be(0);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../../manifest.json")]
    [InlineData("..\\..\\05.02")]
    [InlineData("05/02")]
    public async Task UpdateDocumentTaxonomy_RejectsInvalidSectionSegments_With400BadRequest(string invalidSection)
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new EtmfDbContext(options);

        var doc = TmfDocument.CreateDraft(
            id: "DOC-TAX-1",
            studyId: "STUDY-01",
            filename: "Protocol.pdf",
            createdBy: "cra-1",
            createdAt: _clock.UtcNow.UtcDateTime,
            reasonForChange: "Initial protocol draft upload",
            siteId: "SITE-01");
        dbContext.TmfDocuments.Add(doc);
        await dbContext.SaveChangesAsync(ct);

        var request = new UpdateDocumentTaxonomyRequest(
            Zone: 5,
            Section: invalidSection,
            ArtifactType: "FDA Form 1572",
            ReasonForChange: "Attempting taxonomy section update");

        var result = await EtmfDocumentEndpoints.UpdateDocumentTaxonomy(
            "DOC-TAX-1",
            request,
            CreateDocumentService(dbContext),
            Caller("STUDY-01", "SITE-01"),
            ct);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void TmfDocumentFactories_RejectTraversalSegmentsAndSanitizeHelperNormalizesLegacyValues()
    {
        var act = () => TmfDocument.CreateDraft(
            id: "DOC-BAD",
            studyId: "STUDY-01",
            filename: "../../../canary-outside-binder.txt",
            createdBy: "cra-1",
            createdAt: _clock.UtcNow.UtcDateTime,
            reasonForChange: "Testing domain factory segment validation");

        act.Should().Throw<ArgumentException>().WithParameterName("filename");

        TmfDocument.SanitizePathSegment("IRB/IEC Approval.pdf", "document.pdf")
            .Should().Be("IRB_IEC Approval.pdf");
        TmfDocument.SanitizePathSegment("../../../canary-outside-binder.txt", "document.pdf")
            .Should().Match(s => TmfDocument.IsValidPathSegment(s) && !s.Contains("..", StringComparison.Ordinal));
        TmfDocument.SanitizePathSegment("..", "01.01").Should().Be("01.01");
    }
}
