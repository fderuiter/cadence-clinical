using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
using Cadence.Testing.Security;
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
        services.AddSingleton<Cadence.Apps.Ctms.Services.ICtmsEtmfFilingClient>(new FakeCtmsEtmfFilingClient(_etmfDb, _clock));
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
            Findings:
            [
                new("Informed consent missing date stamp", "MAJOR", "OPEN"),
                new("Temperature log missing for Oct 01", "MINOR", "RESOLVED")
            ]);

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
    public async Task VisitLetterEndpoints_EscapeHtmlInjectionCanary_AcrossRenderCompileAndStoredRead()
    {
        const string Canary = "<div data-canary=\"ctms-finding-marker\">finding-marker</div>";
        var ct = TestContext.Current.CancellationToken;

        await using var factory = new CtmsStudySiteScopeIsolationTests.CtmsInMemoryWebFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            var now = _clock.UtcNow;
            db.CtmsMonitoringVisits.Add(new CtmsMonitoringVisit("VISIT-VERIFY-001")
            {
                StudyId = "STUDY-101",
                SiteId = "SITE-01",
                CraId = "CRA-01",
                VisitType = "IMV",
                ScheduledDate = now,
                CreatedAt = now.UtcDateTime,
                CreatedBy = "CRA-01",
                ReasonForChange = "Seed monitoring visit for HTML encoding verification",
                VersionIndex = 1,
            });
            await db.SaveChangesAsync(ct);
        }

        using var craClient = factory.CreateSignedClient("CRA", "CRA-01", TestSecurityContext.AllStudies, ["SITE-01"]);
        using var auditorClient = factory.CreateSignedClient("Auditor", "AUD-01", ["STUDY-101"], []);

        var renderResp = await craClient.PostAsJsonAsync(
            "/api/v1/ctms/letters/render?templateName=confirmation_letter",
            new { craId = "CRA-01", createdAt = "2026-11-19", studyId = $"STUDY-101 {Canary}", siteId = "SITE-01" },
            ct);
        renderResp.StatusCode.Should().Be(HttpStatusCode.OK);
        string renderedHtml = await renderResp.Content.ReadAsStringAsync(ct);
        renderedHtml.Should().NotContain(Canary).And.Contain("&lt;div data-canary=&quot;ctms-finding-marker&quot;&gt;");

        using var compileReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ctms/letters/compile?templateName=follow_up_letter")
        {
            Content = JsonContent.Create(new
            {
                craId = "CRA-01",
                createdAt = "2026-11-19",
                studyId = "STUDY-101",
                siteId = "SITE-01",
                visitId = "VISIT-VERIFY-001",
                findings = new[] { new { text = Canary, severity = "MINOR", resolutionStatus = "OPEN" } },
            }),
        };
        compileReq.Headers.Add(
            "X-Sig-Token",
            TestSecurityContext.CreateSigToken("CRA-01", "/api/v1/ctms/letters/compile", "ctms.letter.compile", ["CRA"]));
        var compileResp = await craClient.SendAsync(compileReq, ct);
        compileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var compiled = await compileResp.Content.ReadFromJsonAsync<VisitLetterCompilationResultDto>(ct);
        Encoding.UTF8.GetString(Convert.FromBase64String(compiled!.PdfBase64))
            .Should().NotContain(Canary).And.Contain("&lt;div data-canary=&quot;ctms-finding-marker&quot;&gt;");

        var stored = await auditorClient.GetFromJsonAsync<GeneratedLetterResponseDto>(
            "/api/v1/ctms/monitoring-visits/VISIT-VERIFY-001/letters/follow_up_letter", // pragma: allowlist secret
            ct);
        stored!.RenderedContent.Should().NotContain(Canary).And.Contain("&lt;div data-canary=&quot;ctms-finding-marker&quot;&gt;");
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

        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "jane.cra")]))
        };

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
        letterInDb!.Status.Should().Be(CtmsGeneratedLetterStatus.Finalized);
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
    public async Task FinalizedLetterThroughHttp_RequiresSigToken_AndServesExactSha256BytesToMonitorInvestigatorAndAuditor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new CtmsStudySiteScopeIsolationTests.CtmsInMemoryWebFactory();
        var clock = factory.Services.GetRequiredService<IClock>();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CtmsDbContext>();
            db.CtmsMonitoringVisits.Add(new CtmsMonitoringVisit("VISIT-SHA-001", CtmsMonitoringVisitStatus.Completed)
            {
                StudyId = "STUDY-SHA",
                SiteId = "SITE-101",
                CraId = "cra-sha",
                VisitType = "IMV",
                ScheduledDate = clock.UtcNow.AddDays(-1),
                ActualDate = clock.UtcNow,
                CreatedAt = clock.UtcNow.UtcDateTime,
                CreatedBy = "cra-sha",
                ReasonForChange = "Completed IMV",
            });
            await db.SaveChangesAsync(ct);
        }

        using var craClient = factory.CreateSignedClient("CRA", "cra-sha", ["STUDY-SHA"], ["SITE-101"]);
        var payload = new
        {
            craId = "cra-sha",
            createdAt = "2026-11-23",
            studyId = "STUDY-SHA",
            siteId = "SITE-101",
            visitId = "VISIT-SHA-001",
            visitType = "IMV",
            finalize = true,
            signerName = "cra-sha",
            signingPurpose = "Finalize follow-up letter",
            reasonForChange = "21 CFR Part 11 finalized letter compilation",
        };

        // 1. Finalize without X-Sig-Token fails closed with 401
        var unsignedRes = await craClient.PostAsJsonAsync(
            "/api/v1/ctms/letters/compile?templateName=follow_up_letter",
            payload,
            ct);
        unsignedRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Finalize with X-Sig-Token succeeds and stores immutable PdfBytes
        using var signedCompileReq = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            "/api/v1/ctms/letters/compile?templateName=follow_up_letter")
        {
            Content = JsonContent.Create(payload),
        };
        signedCompileReq.Headers.Add(
            Cadence.Security.Authentication.GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(
                "cra-sha",
                LetterTemplateEndpoints.CompileLetterAction,
                LetterTemplateEndpoints.CompileLetterSemanticAction,
                ["CRA"],
                issuedAt: clock.UtcNow));
        var signedRes = await craClient.SendAsync(signedCompileReq, ct);
        signedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var compiled = await signedRes.Content.ReadFromJsonAsync<VisitLetterCompilationResultDto>(ct);
        compiled.Should().NotBeNull();
        compiled!.PdfSha256Hash.Should().HaveLength(64);

        // 3. Monitor, Principal Investigator, Investigator, and Auditor all download exact bytes matching PdfSha256Hash
        foreach (var (role, sites) in new (string Role, string[] Sites)[]
        {
            ("monitor", ["SITE-101"]),
            ("Principal Investigator", ["SITE-101"]),
            ("Site Investigator", ["SITE-101"]),
            ("Auditor", []),
        })
        {
            using var readerClient = factory.CreateSignedClient(role, $"reader-{role}", ["STUDY-SHA"], sites);
            var pdfRes = await readerClient.GetAsync($"/api/v1/ctms/letters/{compiled.LetterId}/pdf", ct);
            pdfRes.StatusCode.Should().Be(HttpStatusCode.OK, $"role '{role}' must have read access to compiled letter PDFs");
            byte[] pdfBytes = await pdfRes.Content.ReadAsByteArrayAsync(ct);
            string actualHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pdfBytes));
            actualHash.Should().Be(compiled.PdfSha256Hash);
        }
    }

    private sealed class FakeCtmsEtmfFilingClient(EtmfDbContext etmfDb, IClock clock) : Cadence.Apps.Ctms.Services.ICtmsEtmfFilingClient
    {
        public async Task<string?> FileSignedArtifactAsync(
            Cadence.Apps.Ctms.Services.CtmsEtmfFilingRequest filing,
            CancellationToken ct)
        {
            var now = clock.UtcNow;
            string docId = "DOC-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            string reason = string.IsNullOrWhiteSpace(filing.ReasonForChange)
                ? "Automated eTMF filing for finalized monitoring visit letter."
                : filing.ReasonForChange;
            var doc = Cadence.Apps.Etmf.Domain.TmfDocument.CreateDraft(
                id: docId,
                studyId: filing.StudyCode,
                filename: filing.Filename,
                createdBy: filing.Signer,
                createdAt: now.UtcDateTime,
                reasonForChange: reason,
                siteId: filing.SiteCode,
                zone: 5,
                section: "05.02",
                artifactCode: filing.ArtifactCode,
                artifactType: filing.ArtifactCode == "05.02.04" ? "Delegation of Authority Log" : "Monitoring Visit Follow-up Letter",
                content: "Signed GxP PDF",
                mimeType: "application/pdf",
                taxonomyVersion: "v3.2.0-complete");
            doc.Signer = filing.Signer;
            doc.SigningTimestamp = filing.SigningTimestamp;
            doc.SignatureManifestation = filing.SignatureManifestation;
            doc.StageForQc(filing.Signer, "ctms_service", reason, now, sequenceNumber: 1);
            doc.Approve(filing.Signer, "ctms_service", reason, now, sequenceNumber: 2);
            etmfDb.TmfDocuments.Add(doc);
            await etmfDb.SaveChangesAsync(ct);
            return docId;
        }
    }

}
