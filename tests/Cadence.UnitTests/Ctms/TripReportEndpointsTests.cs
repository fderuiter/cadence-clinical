using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Apps.Etmf.Infrastructure.Persistence;
using Cadence.Compliance;
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

public sealed class TripReportEndpointsTests
{
    private readonly CtmsDbContext _ctmsDb;
    private readonly EtmfDbContext _etmfDb;
    private readonly FakeClock _clock;
    private readonly IServiceProvider _serviceProvider;

    public TripReportEndpointsTests()
    {
        var ctmsOptions = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _ctmsDb = new CtmsDbContext(ctmsOptions);

        var etmfOptions = new DbContextOptionsBuilder<EtmfDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _etmfDb = new EtmfDbContext(etmfOptions);

        _clock = new FakeClock(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<CtmsDbContext>(_ctmsDb);
        services.AddSingleton<EtmfDbContext>(_etmfDb);
        _serviceProvider = services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateHttpContext(string user = "cra_fderuiter")
    {
        var context = new DefaultHttpContext();
        var claims = new System.Security.Claims.Claim[]
        {
            new(System.Security.Claims.ClaimTypes.Name, user),
            new("sub", user)
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Test");
        context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        return context;
    }

    [Theory]
    [InlineData("PSV")]
    [InlineData("SIV")]
    [InlineData("IMV")]
    [InlineData("COV")]
    [GxPRequirement("PRD-CTMS-003")]
    public async Task GetOrInitializeFormAsync_PrePopulatesSections_ForVisitTypes(string visitType)
    {
        var visitId = $"VISIT-{visitType}-001";
        _ctmsDb.CtmsMonitoringVisits.Add(new CtmsMonitoringVisit(visitId)
        {
            StudyId = "STUDY-001",
            SiteId = "Site-01",
            CraId = "cra_fderuiter",
            VisitType = visitType,
            ScheduledDate = _clock.UtcNow,
            Status = "SCHEDULED",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra_fderuiter",
            VersionIndex = 1,
            ReasonForChange = "Scheduled visit"
        });
        await _ctmsDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await TripReportEndpoints.GetOrInitializeFormAsync(visitId, _ctmsDb, _clock, CreateHttpContext(), TestContext.Current.CancellationToken);
        var okResult = result.Should().BeOfType<Ok<TripReportFormDto>>().Subject;

        var form = okResult.Value;
        form.Should().NotBeNull();
        form!.VisitId.Should().Be(visitId);
        form.VisitType.Should().Be(visitType);
        form.Sections.Should().HaveCount(4);
        form.Sections.Select(s => s.SectionKey).Should().Contain(new[] { "protocol_compliance", "ip_accountability", "regulatory_binder", "facilities" });
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-003")]
    public async Task SaveDraftAsync_UpdatesSectionResponsesAndVerification()
    {
        var visitId = "VISIT-IMV-100";
        _ctmsDb.CtmsMonitoringVisits.Add(new CtmsMonitoringVisit(visitId)
        {
            StudyId = "STUDY-001",
            SiteId = "Site-01",
            CraId = "cra_fderuiter",
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow,
            Status = "SCHEDULED",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra_fderuiter",
            VersionIndex = 1,
            ReasonForChange = "Scheduled visit"
        });
        await _ctmsDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Initialize form first
        await TripReportEndpoints.GetOrInitializeFormAsync(visitId, _ctmsDb, _clock, CreateHttpContext(), TestContext.Current.CancellationToken);

        var saveReq = new SaveTripReportRequest(
            TripReportId: visitId,
            Sections: new List<SaveTripReportSectionRequest>
            {
                new(
                    SectionKey: "protocol_compliance",
                    IsVerified: true,
                    Responses: new List<TripReportResponseSaveItem>
                    {
                        new("pc_irb_approval", "Is IRB/IEC approval documentation current?", "boolean", "PASS", "Verified in eISF", null)
                    }
                )
            },
            ReasonForChange: "CRA draft update"
        );

        var result = await TripReportEndpoints.SaveDraftAsync(saveReq, _ctmsDb, _clock, CreateHttpContext(), TestContext.Current.CancellationToken);
        var okResult = result.Should().BeOfType<Ok<TripReportFormDto>>().Subject;

        var form = okResult.Value;
        var pcSection = form!.Sections.First(s => s.SectionKey == "protocol_compliance");
        pcSection.IsVerified.Should().BeTrue();
        pcSection.Responses.First(r => r.QuestionKey == "pc_irb_approval").Commentary.Should().Be("Verified in eISF");
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-003")]
    public async Task AttachFindingAsync_LinksFindingToSection()
    {
        var visitId = "VISIT-SIV-200";
        _ctmsDb.CtmsMonitoringVisits.Add(new CtmsMonitoringVisit(visitId)
        {
            StudyId = "STUDY-001",
            SiteId = "Site-01",
            CraId = "cra_fderuiter",
            VisitType = "SIV",
            ScheduledDate = _clock.UtcNow,
            Status = "SCHEDULED",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra_fderuiter",
            VersionIndex = 1,
            ReasonForChange = "Scheduled visit"
        });
        await _ctmsDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        await TripReportEndpoints.GetOrInitializeFormAsync(visitId, _ctmsDb, _clock, CreateHttpContext(), TestContext.Current.CancellationToken);

        var attachReq = new AttachFindingRequest(
            VisitId: visitId,
            SectionKey: "ip_accountability",
            QuestionKey: "ip_temp_logs",
            Text: "Temperature log missing for July 12.",
            Severity: "MAJOR",
            ResolutionStatus: "OPEN"
        );

        var result = await TripReportEndpoints.AttachFindingAsync(attachReq, _ctmsDb, _clock, CreateHttpContext(), TestContext.Current.CancellationToken);
        var okResult = result.Should().BeOfType<Ok<SiteVisitFindingModel>>().Subject;
        okResult.Value!.Text.Should().Be("Temperature log missing for July 12.");

        var formResult = await TripReportEndpoints.GetOrInitializeFormAsync(visitId, _ctmsDb, _clock, CreateHttpContext(), TestContext.Current.CancellationToken);
        var form = ((Ok<TripReportFormDto>)formResult).Value!;
        var ipSec = form.Sections.First(s => s.SectionKey == "ip_accountability");
        ipSec.Findings.Should().Contain(f => f.Text.Contains("Temperature log missing"));
    }

    [Fact]
    [GxPRequirement("PRD-CTMS-003")]
    public async Task SubmitAndSignAsync_CompilesSignedPdf_AndFilesToEtmf()
    {
        var visitId = "VISIT-IMV-300";
        _ctmsDb.CtmsMonitoringVisits.Add(new CtmsMonitoringVisit(visitId)
        {
            StudyId = "STUDY-001",
            SiteId = "Site-01",
            CraId = "cra_fderuiter",
            VisitType = "IMV",
            ScheduledDate = _clock.UtcNow,
            Status = "COMPLETED",
            CreatedAt = _clock.UtcNow.UtcDateTime,
            CreatedBy = "cra_fderuiter",
            VersionIndex = 1,
            ReasonForChange = "Completed visit"
        });
        await _ctmsDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        await TripReportEndpoints.GetOrInitializeFormAsync(visitId, _ctmsDb, _clock, CreateHttpContext(), TestContext.Current.CancellationToken);

        var submitReq = new SubmitTripReportRequest(
            TripReportId: visitId,
            Finalize: true,
            SignerName: "Frank de Ruiter",
            SigningPurpose: "21 CFR Part 11 Electronic Signature / Visit Report Sign-off",
            ReasonForChange: "Final trip report submission"
        );

        var result = await TripReportEndpoints.SubmitAndSignAsync(
            submitReq,
            _ctmsDb,
            FluidTemplateRenderer.Instance,
            _clock,
            _serviceProvider,
            CreateHttpContext(),
            TestContext.Current.CancellationToken);

        var okResult = result.Should().BeOfType<Ok<TripReportCompilationResultDto>>().Subject;
        var dto = okResult.Value!;

        dto.IsSigned.Should().BeTrue();
        dto.Status.Should().Be("FINALIZED");
        dto.PdfSha256Hash.Should().HaveLength(64);
        dto.SignatureManifestation.Should().Contain("Frank de Ruiter");
        dto.EtmfDocumentId.Should().NotBeNullOrEmpty();

        var dbReport = await _ctmsDb.CtmsTripReports.FirstAsync(tr => tr.VisitId == visitId, TestContext.Current.CancellationToken);
        dbReport.Status.Should().Be("FINALIZED");
        dbReport.IsSigned.Should().BeTrue();
    }
}
