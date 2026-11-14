using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Kernel.Time;
using Cadence.Templating;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class VisitLetterPdfPipelineTests
{
    private readonly CtmsDbContext _ctmsDb;
    private readonly EtmfDbContext _etmfDb;
    private readonly NotificationsDbContext _notifDb;
    private readonly FakeClock _clock;
    private readonly IServiceProvider _serviceProvider;

    public VisitLetterPdfPipelineTests()
    {
        var ctmsOptions = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _ctmsDb = new CtmsDbContext(ctmsOptions);

        var etmfOptions = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _etmfDb = new EtmfDbContext(etmfOptions);

        var notifOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _notifDb = new NotificationsDbContext(notifOptions);

        _clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<CtmsDbContext>(_ctmsDb);
        services.AddSingleton<EtmfDbContext>(_etmfDb);
        services.AddSingleton<NotificationsDbContext>(_notifDb);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task ConfirmationLetter_RendersHtmlTemplate_AndCompilesDraftPdf()
    {
        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            ScheduledDate: "2026-10-15");

        var result = await GxpPdfCompiler.CompileLetterPdfAsync(
            "confirmation_letter",
            model,
            FluidTemplateRenderer.Instance,
            LetterTemplateEndpoints.Catalog,
            _clock);

        result.IsSigned.Should().BeFalse();
        result.RenderedHtml.Should().Contain("CONFIRMATION OF CLINICAL MONITORING VISIT")
            .And.Contain("DRAFT - UNSIGNED WORKING COPY")
            .And.Contain("STUDY-101")
            .And.Contain("SITE-017")
            .And.Contain("CRA-42");

        result.PdfBytes.Should().NotBeEmpty();
        result.Sha256Hash.Should().HaveLength(64);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task FollowUpLetter_WithFindings_RendersHtmlTable()
    {
        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            ActualDate: "2026-09-30",
            Findings: new List<SiteVisitFindingModel>
            {
                new("Informed consent missing date stamp", "MAJOR", "OPEN"),
                new("Temperature log missing for Oct 01", "MINOR", "RESOLVED")
            });

        var result = await GxpPdfCompiler.CompileLetterPdfAsync(
            "follow_up_letter",
            model,
            FluidTemplateRenderer.Instance,
            LetterTemplateEndpoints.Catalog,
            _clock);

        result.RenderedHtml.Should().Contain("CLINICAL MONITORING VISIT FOLLOW-UP LETTER")
            .And.Contain("Informed consent missing date stamp")
            .And.Contain("MAJOR")
            .And.Contain("Temperature log missing for Oct 01");
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task FinalizedLetter_AppliesPart11Pkcs7Signature_AndPassesVerification()
    {
        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            ScheduledDate: "2026-10-15",
            Finalize: true,
            SignerName: "Jane Doe CRA",
            SigningPurpose: "Visit Letter Sign-off");

        var pdfResult = await GxpPdfCompiler.CompileLetterPdfAsync(
            "confirmation_letter",
            model,
            FluidTemplateRenderer.Instance,
            LetterTemplateEndpoints.Catalog,
            _clock);

        pdfResult.IsSigned.Should().BeTrue();
        pdfResult.RenderedHtml.Should().Contain("21 CFR Part 11 Electronic Signature Manifestation")
            .And.Contain("Jane Doe CRA")
            .And.Contain("Visit Letter Sign-off");

        var verifier = new ESignatureVerifier();
        var verification = verifier.VerifyPdf(pdfResult.PdfBytes);
        verification.IsValid.Should().BeTrue();
        verification.Status.Should().Be("VALID");
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task CompileEndpoint_FinalizesAndFilesToEtmf_AndDispatchesEmail()
    {
        var visitId = "VISIT-017-001";
        var visit = new CtmsMonitoringVisit(visitId, MonitoringVisitStatus.Completed)
        {
            StudyId = "STUDY-101",
            SiteId = "SITE-017",
            CraId = "CRA-42",
            VisitType = "Monitoring",
            ScheduledDate = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero),
            ActualDate = new DateTimeOffset(2026, 10, 15, 17, 0, 0, TimeSpan.Zero),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "CRA-42",
            ReasonForChange = "Completed monitoring visit",
            VersionIndex = 1
        };
        _ctmsDb.CtmsMonitoringVisits.Add(visit);
        await _ctmsDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            ScheduledDate: "2026-10-15",
            VisitId: visitId,
            Finalize: true,
            SignerName: "Jane Doe CRA",
            SigningPurpose: "Visit Letter Approval",
            InvestigatorEmail: "investigator@site017.org",
            ReasonForChange: "Final visit letter approval and automated dispatch");

        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "jane.cra")]));

        var ct = TestContext.Current.CancellationToken;

        var response = await LetterTemplateEndpoints.CompileAndDispatchLetterAsync(
            "confirmation_letter",
            model,
            FluidTemplateRenderer.Instance,
            _ctmsDb,
            _clock,
            _serviceProvider,
            httpContext,
            ct);

        response.Should().NotBeNull();
        var okResult = response.Should().BeOfType<Ok<VisitLetterCompilationResultDto>>().Subject;
        var dto = okResult.Value!;

        dto.IsSigned.Should().BeTrue();
        dto.Status.Should().Be("FINALIZED");
        dto.EtmfDocumentId.Should().NotBeNullOrEmpty();
        dto.DispatchedAt.Should().NotBeNull();
        dto.PdfAccessUrl.Should().Contain(dto.LetterId);

        // Verify CTMS persistence
        var letterInDb = await _ctmsDb.CtmsGeneratedLetters.FirstOrDefaultAsync(l => l.Id == dto.LetterId, ct);
        letterInDb.Should().NotBeNull();
        letterInDb!.Status.Should().Be("FINALIZED");
        letterInDb.PdfSha256Hash.Should().Be(dto.PdfSha256Hash);

        // Verify CTMS Audit Log
        var auditEntry = await _ctmsDb.CtmsAuditLogs.FirstOrDefaultAsync(a => a.Action == "FINALIZE_VISIT_LETTER_PDF", ct);
        auditEntry.Should().NotBeNull();
        auditEntry!.Details.Should().Contain(dto.PdfSha256Hash);

        // Verify eTMF filing
        var etmfDoc = await _etmfDb.TmfDocuments.FirstOrDefaultAsync(d => d.Id == dto.EtmfDocumentId, ct);
        etmfDoc.Should().NotBeNull();
        etmfDoc!.Zone.Should().Be(5);
        etmfDoc.Section.Should().Be("05.02");
        etmfDoc.ArtifactCode.Should().Be("05.02.06");
        etmfDoc.Status.Should().Be("APPROVED");

        // Verify Email Notification Dispatch
        var notif = await _notifDb.NotificationRecords.FirstOrDefaultAsync(n => n.RelatedEntityId == dto.LetterId, ct);
        notif.Should().NotBeNull();
        notif!.RecipientUserId.Should().Be("investigator@site017.org");
        notif.Category.Should().Be("VISIT_LETTER_DISPATCH");
        notif.MessageContent.Should().Contain(dto.PdfAccessUrl);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task CompileEndpoint_WhenVisitIdMissing_Returns400BadRequest()
    {
        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            VisitId: null);

        var httpContext = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var response = await LetterTemplateEndpoints.CompileAndDispatchLetterAsync(
            "confirmation_letter",
            model,
            FluidTemplateRenderer.Instance,
            _ctmsDb,
            _clock,
            _serviceProvider,
            httpContext,
            ct);

        response.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task CompileEndpoint_WhenVisitNotFound_Returns404NotFound()
    {
        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            VisitId: "NON-EXISTENT-VISIT");

        var httpContext = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var response = await LetterTemplateEndpoints.CompileAndDispatchLetterAsync(
            "confirmation_letter",
            model,
            FluidTemplateRenderer.Instance,
            _ctmsDb,
            _clock,
            _serviceProvider,
            httpContext,
            ct);

        response.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task CompileEndpoint_WhenVisitStatusIsScheduled_Returns400BadRequest()
    {
        var visitId = "VISIT-SCHEDULED-001";
        var visit = new CtmsMonitoringVisit(visitId, MonitoringVisitStatus.Scheduled)
        {
            StudyId = "STUDY-101",
            SiteId = "SITE-017",
            CraId = "CRA-42",
            VisitType = "Monitoring",
            ScheduledDate = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "CRA-42",
            ReasonForChange = "Scheduled visit",
            VersionIndex = 1
        };
        _ctmsDb.CtmsMonitoringVisits.Add(visit);
        await _ctmsDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            VisitId: visitId);

        var httpContext = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var response = await LetterTemplateEndpoints.CompileAndDispatchLetterAsync(
            "confirmation_letter",
            model,
            FluidTemplateRenderer.Instance,
            _ctmsDb,
            _clock,
            _serviceProvider,
            httpContext,
            ct);

        var problem = response.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Detail.Should().Contain("COMPLETED").And.Contain("APPROVED");
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-002")]
    public async Task CompileEndpoint_WhenVisitStatusIsApproved_Succeeds()
    {
        var visitId = "VISIT-APPROVED-001";
        var visit = new CtmsMonitoringVisit(visitId, MonitoringVisitStatus.Approved)
        {
            StudyId = "STUDY-101",
            SiteId = "SITE-017",
            CraId = "CRA-42",
            VisitType = "Monitoring",
            ScheduledDate = new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero),
            ActualDate = new DateTimeOffset(2026, 10, 15, 17, 0, 0, TimeSpan.Zero),
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "CRA-42",
            ReasonForChange = "Approved visit",
            VersionIndex = 2
        };
        _ctmsDb.CtmsMonitoringVisits.Add(visit);
        await _ctmsDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            ScheduledDate: "2026-10-15",
            VisitId: visitId,
            Finalize: false,
            ReasonForChange: "Draft letter for approved visit");

        var httpContext = new DefaultHttpContext();
        var ct = TestContext.Current.CancellationToken;

        var response = await LetterTemplateEndpoints.CompileAndDispatchLetterAsync(
            "confirmation_letter",
            model,
            FluidTemplateRenderer.Instance,
            _ctmsDb,
            _clock,
            _serviceProvider,
            httpContext,
            ct);

        response.Should().BeOfType<Ok<VisitLetterCompilationResultDto>>();
    }
}
