using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Infrastructure.Consumers;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Apps.Notifications.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Templating;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;
using MassTransit.Testing;

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
        services.AddScoped(_ => new EtmfDbContext(etmfOptions));
        services.AddSingleton<NotificationsDbContext>(_notifDb);
        services.AddSingleton<IClock>(_clock);
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<EtmfDocumentFilingRequestedConsumer>()
                .Endpoint(e => e.ConcurrentMessageLimit = 1);
        });
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
        var model = new SiteVisitLetterModel(
            CraId: "CRA-42",
            CreatedAt: "2026-10-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-017",
            ScheduledDate: "2026-10-15",
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
        var harness = _serviceProvider.GetRequiredService<ITestHarness>();
        await harness.Start();
        try
        {
            using var scope = _serviceProvider.CreateScope();

            var response = await LetterTemplateEndpoints.CompileAndDispatchLetterAsync(
                "confirmation_letter",
                model,
                FluidTemplateRenderer.Instance,
                _ctmsDb,
                _clock,
                scope.ServiceProvider,
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

            // Wait for EtmfDocumentFilingRequestedConsumer to process the outbox event
            (await harness.Consumed.Any<EtmfDocumentFilingRequested>(ct)).Should().BeTrue();

            // Verify CTMS persistence including study and site codes
            var letterInDb = await _ctmsDb.CtmsGeneratedLetters.FirstOrDefaultAsync(l => l.Id == dto.LetterId, ct);
            letterInDb.Should().NotBeNull();
            letterInDb!.Status.Should().Be(CtmsGeneratedLetterStatus.Finalized);
            letterInDb.StudyId.Should().Be("STUDY-101");
            letterInDb.SiteId.Should().Be("SITE-017");
            letterInDb.PdfSha256Hash.Should().Be(dto.PdfSha256Hash);

            // Verify GetLetterPdfAsync renders persisted Study/Site rather than placeholder STUDY/SITE
            var pdfResponse = await LetterTemplateEndpoints.GetLetterPdfAsync(
                dto.LetterId, _ctmsDb, FluidTemplateRenderer.Instance, _clock, ct, httpContext);
            var pdfFile = pdfResponse.Should().BeOfType<FileContentHttpResult>().Subject;
            string pdfText = System.Text.Encoding.Latin1.GetString(pdfFile.FileContents.ToArray());
            pdfText.Should().Contain("STUDY-101").And.Contain("SITE-017");

            // Verify CTMS Audit Log
            var auditEntry = await _ctmsDb.CtmsAuditLogs.FirstOrDefaultAsync(a => a.Action == "FINALIZE_VISIT_LETTER_PDF", ct);
            auditEntry.Should().NotBeNull();
            auditEntry!.Details.Should().Contain(dto.PdfSha256Hash);

            // Verify eTMF filing via EtmfDocumentFilingRequestedConsumer
            var etmfDoc = await _etmfDb.TmfDocuments.FirstOrDefaultAsync(d => d.Id == dto.EtmfDocumentId, ct);
            etmfDoc.Should().NotBeNull();
            etmfDoc!.Zone.Should().Be(5);
            etmfDoc.Section.Should().Be("05.02");
            etmfDoc.ArtifactCode.Should().Be("05.02.06");
            etmfDoc.ArtifactType.Should().Be("Monitoring Visit Correspondence");
            etmfDoc.Status.Should().Be("FILED");

            // Verify Email Notification Dispatch
            var notif = await _notifDb.NotificationRecords.FirstOrDefaultAsync(n => n.RelatedEntityId == dto.LetterId, ct);
            notif.Should().NotBeNull();
            notif!.RecipientUserId.Should().Be("investigator@site017.org");
            notif.Category.Should().Be("VISIT_LETTER_DISPATCH");
            notif.MessageContent.Should().Contain(dto.PdfAccessUrl);
        }
        finally
        {
            await harness.Stop(ct);
        }
    }
}
