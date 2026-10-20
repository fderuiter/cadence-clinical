using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Safety.Application;
using Cadence.Apps.Safety.Domain;
using Cadence.Apps.Safety.Endpoints;
using Cadence.Apps.Safety.Infrastructure.Persistence;
using Cadence.Database;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Safety;

/// <summary>
/// DSUR compilation and the lock (#5464): a compile without a compound is refused, exposure and the MedDRA
/// cross-tabulation match a fixture computed by hand, and once approved a report can be neither updated nor approved
/// again, on either route family. Builds on <see cref="DsurApprovalAuthorityTests"/> (#5462), which covers who may sign.
/// </summary>
public sealed class DsurCompilationTests(DsurApprovalAuthorityTests.SafetyFactory factory)
    : IClassFixture<DsurApprovalAuthorityTests.SafetyFactory>
{
    private static readonly GatewayIdentity Compiler = TestSecurityContext.Identity("compile-mm-compiler", "Sponsor Medical Monitor");
    private static readonly GatewayIdentity Approver = TestSecurityContext.Identity("compile-mm-approver", "Sponsor Medical Monitor");

    private static readonly DateTimeOffset PeriodStart = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The compile routes and the compound names (absent, empty or blank) that each must refuse.</summary>
    public static TheoryData<string, string> MissingCompoundNames => new()
    {
        { "/api/v1/safety/dsur/compile", "absent" },
        { "/api/v1/safety/dsur/compile", "\"\"" },
        { "/api/v1/safety/dsur/compile", "\"   \"" },
        { "/api/v1/safety/periodic-reports/compile", "absent" },
        { "/api/v1/safety/periodic-reports/compile", "null" },
    };

    /// <summary>A compile without a compound name is a 400 ProblemDetails naming CompoundName, and stores no report.</summary>
    [Theory]
    [MemberData(nameof(MissingCompoundNames))]
    public async Task Compile_WithoutCompoundName_Returns400AndStoresNothing(string route, string compoundName)
    {
        int before = await CountReportsAsync();
        string compound = compoundName == "absent" ? string.Empty : $"\"compoundName\": {compoundName}, ";
        string json = "{ " + compound + "\"studyId\": \"STUDY-2025\", \"periodStart\": \"2025-01-01T00:00:00Z\", "
            + "\"periodEnd\": \"2025-12-31T23:59:59Z\", \"reasonForChange\": \"Annual DSUR compilation\" }";
        using var client = factory.CreateSignedClient(Compiler);

        using var response = await client.PostAsync(route, new StringContent(json, Encoding.UTF8, "application/json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("CompoundName");
        (await CountReportsAsync()).Should().Be(before);
    }

    /// <summary>
    /// Patient-years exposure and the MedDRA SOC/PT cross-tabulation against a fixture computed by hand over the period
    /// 2025-01-01 to 2026-01-01. Exposure runs from the later of enrolment and period start to the earlier of
    /// discontinuation and period end, in days / 365.25, rounded to four places per subject:
    /// SUBJ-201 enrolled before the period, 365 d = 0.9993; SUBJ-202 from 2025-07-02, 183 d = 0.5010; SUBJ-203 (placebo)
    /// until 2025-04-01, 90 d = 0.2464; SUBJ-204 enrolled after the period, 0 d; SUBJ-205 (placebo) from 2025-10-01,
    /// discontinued after the period, 92 d = 0.2519. Total 1.9986, investigational 1.5003, placebo 0.4983.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public async Task Compile_PatientYearsAndMedDraCrossTabulation_MatchTheHandComputedFixture()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
        await using var db = InMemoryContext(clock);
        var engine = new DsurCompilerEngine(db, clock);

        var result = await engine.CompileDsurAsync(FixtureRequest(), "fixture-compiler", Ct);

        var exposure = result.Payload.ExposureMetrics;
        exposure.TotalSubjectsEnrolled.Should().Be(5);
        exposure.TotalPatientYearsExposure.Should().Be(1.9986);
        exposure.InvestigationalArmPatientYears.Should().Be(1.5003);
        exposure.PlaceboArmPatientYears.Should().Be(0.4983);
        exposure.ExposureDetails.Select(d => (d.SubjectId, d.StartDate, d.EndDate, d.ExposureDays, d.PatientYears)).Should().Equal(
            ("SUBJ-201", PeriodStart, PeriodEnd, 365.0, 0.9993),
            ("SUBJ-202", Utc(2025, 7, 2), PeriodEnd, 183.0, 0.5010),
            ("SUBJ-203", PeriodStart, Utc(2025, 4, 1), 90.0, 0.2464),
            ("SUBJ-204", Utc(2026, 2, 1), PeriodEnd, 0.0, 0.0),
            ("SUBJ-205", Utc(2025, 10, 1), PeriodEnd, 92.0, 0.2519));

        result.Payload.SummaryTabulations.Should().Equal(
            new DsurSummaryTabulationRow("Gastrointestinal disorders", "Nausea", false, null, false, 1, 1, 1, 3, "UNEXPECTED"),
            new DsurSummaryTabulationRow("Gastrointestinal disorders", "Vomiting", false, null, false, 1, 0, 0, 1, "EXPECTED"),
            new DsurSummaryTabulationRow("Hepatobiliary disorders", "Hepatic failure", true, "LIVER_SAFETY", true, 2, 0, 0, 2, "UNEXPECTED"));

        result.Payload.TotalSaes.Should().Be(6);
        result.Payload.TotalFatalCases.Should().Be(1);
        result.Payload.TotalSusars.Should().Be(2, "CASE-03 and CASE-04 are serious, unexpected and causal; CASE-06 is not causal");
        result.Payload.TotalAesi.Should().Be(1);
        result.Payload.LineListings.Select(l => l.CaseId).Should().Equal("CASE-01", "CASE-02", "CASE-03", "CASE-04", "CASE-05", "CASE-06");
        result.Record.TotalSaes.Should().Be(6);
        result.Record.CreatedAt.Should().Be(clock.UtcNow);
    }

    /// <summary>
    /// Once approved (LOCKED), an update on either route is refused with 409, and the report, its version and its audit
    /// trail are unchanged.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/safety/dsur")]
    [InlineData("/api/v1/safety/periodic-reports")]
    [GxPRequirement("PRD-PVG-004")]
    public async Task Update_AfterApproval_Returns409AndChangesNothing(string prefix)
    {
        string id = await CompileAsync();
        using (var approver = factory.CreateSignedClient(Approver))
        {
            using var approval = await ApproveAsync(approver, prefix, id);
            approval.StatusCode.Should().Be(HttpStatusCode.OK, await approval.Content.ReadAsStringAsync(Ct));
        }

        var locked = (await RecordAsync(id))!;
        using var client = factory.CreateSignedClient(Compiler);

        using var response = await client.PutAsJsonAsync(
            $"{prefix}/{id}", new { compoundName = "Renamed-After-Lock", reasonForChange = "Edit after approval" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var stored = (await RecordAsync(id))!;
        stored.Status.Should().Be("LOCKED");
        stored.CompoundName.Should().Be(locked.CompoundName);
        stored.VersionIndex.Should().Be(locked.VersionIndex);
        stored.ReasonForChange.Should().Be(locked.ReasonForChange);
        (await AuditActionsAsync(id)).Should().NotContain(DsurCompilerEngine.UpdatedAuditAction);
    }

    /// <summary>
    /// The same approver approving twice, the second time on the alias route with a fresh token, gets 409 and the first
    /// signature stands: one approval audit entry, the same manifest and approval time.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-PVG-004")]
    public async Task Approve_Twice_Returns409AndKeepsTheFirstSignature()
    {
        string id = await CompileAsync();
        using var client = factory.CreateSignedClient(Approver);
        using var first = await ApproveAsync(client, "/api/v1/safety/dsur", id);
        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(Ct));
        var signed = (await RecordAsync(id))!;

        using var second = await ApproveAsync(client, "/api/v1/safety/periodic-reports", id);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var stored = (await RecordAsync(id))!;
        stored.EsignatureManifestId.Should().Be(signed.EsignatureManifestId);
        stored.ApprovedAt.Should().Be(signed.ApprovedAt);
        stored.VersionIndex.Should().Be(signed.VersionIndex);
        (await AuditActionsAsync(id)).Count(a => a == DsurCompilerEngine.ApprovedAuditAction).Should().Be(1);
    }

    private static DateTimeOffset Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static DsurCompileRequest FixtureRequest()
    {
        var enrollments = new List<SubjectEnrollmentRecord>
        {
            new("SUBJ-201", "STUDY-FIX", "Investigational Drug", Utc(2024, 6, 1), null),
            new("SUBJ-202", "STUDY-FIX", "Investigational Drug", Utc(2025, 7, 2), null),
            new("SUBJ-203", "STUDY-FIX", "Placebo", Utc(2025, 1, 1), Utc(2025, 4, 1)),
            new("SUBJ-204", "STUDY-FIX", "Investigational Drug", Utc(2026, 2, 1), null),
            new("SUBJ-205", "STUDY-FIX", "Placebo", Utc(2025, 10, 1), Utc(2026, 3, 1)),
        };

        const string Gi = "Gastrointestinal disorders";
        const string Hepatobiliary = "Hepatobiliary disorders";
        var cases = new List<SafetyCaseIcsrInput>
        {
            Case("CASE-01", "SUBJ-201", "Nausea", Gi, "Investigational Drug", "CAUSAL", "EXPECTED"),
            Case("CASE-02", "SUBJ-203", "Nausea", Gi, "Placebo", "NOT_CAUSAL", "EXPECTED"),
            Case("CASE-03", "SUBJ-206", "Nausea", Gi, "Active Comparator", "CAUSAL", "UNEXPECTED"),
            Case("CASE-04", "SUBJ-202", "Hepatic failure", Hepatobiliary, "Investigational Drug", "CAUSAL", "UNEXPECTED", isFatal: true, aesiCategory: "LIVER_SAFETY"),
            Case("CASE-05", "SUBJ-201", "Vomiting", Gi, "Investigational Drug", "CAUSAL", "LISTED"),
            Case("CASE-06", "SUBJ-202", "Hepatic failure", Hepatobiliary, "Investigational Drug", "NOT_CAUSAL", "UNEXPECTED"),
        };

        return new DsurCompileRequest("Fixture-Compound", "STUDY-FIX", PeriodStart, PeriodEnd, "Hand-computed fixture", enrollments, cases);
    }

    private static SafetyCaseIcsrInput Case(
        string caseId, string subjectId, string pt, string soc, string arm, string causality, string expectedness,
        bool isFatal = false, string? aesiCategory = null) =>
        new(caseId, subjectId, "STUDY-FIX", Utc(2025, 6, 1), pt, pt, soc, IsSerious: true, isFatal, aesiCategory is not null,
            aesiCategory, causality, expectedness, arm, "DOSE_NOT_CHANGED", Utc(2025, 5, 30), null);

    private static SafetyDbContext InMemoryContext(FakeClock clock) => new(
        new DbContextOptionsBuilder<SafetyDbContext>()
            .UseInMemoryDatabase($"safety-compilation-{Guid.NewGuid():N}")
            .AddInterceptors(new GxPAuditInterceptor(() => "fixture-compiler", clock))
            .Options);

    private static async Task<HttpResponseMessage> ApproveAsync(HttpClient client, string prefix, string id)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{prefix}/{id}/approve")
        {
            Content = JsonContent.Create(new { reasonForChange = "DSUR medical review complete" }),
        };
        request.Headers.Add(
            GatewayHeaders.SigToken,
            TestSecurityContext.CreateSigToken(Approver.UserId, DsurEndpoints.ApproveAction(id), DsurEndpoints.ApproveSemanticAction));
        return await client.SendAsync(request, Ct);
    }

    private async Task<string> CompileAsync()
    {
        using var client = factory.CreateSignedClient(Compiler);
        using var response = await client.PostAsJsonAsync(
            "/api/v1/safety/dsur/compile",
            new
            {
                compoundName = "Lock-Compound",
                studyId = "STUDY-2025",
                periodStart = "2025-01-01T00:00:00Z",
                periodEnd = "2025-12-31T23:59:59Z",
                reasonForChange = "Annual DSUR compilation",
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<PeriodicSafetyReportRecord>(Ct))!.Id;
    }

    private async Task<int> CountReportsAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SafetyDbContext>().PeriodicSafetyReports.CountAsync(Ct);
    }

    private async Task<PeriodicSafetyReportRecord?> RecordAsync(string id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();
        return await db.PeriodicSafetyReports.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, Ct);
    }

    private async Task<List<string>> AuditActionsAsync(string id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyDbContext>();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(e => e.EntityName == DsurCompilerEngine.AuditEntityName && e.EntityId == id)
            .Select(e => e.Action)
            .ToListAsync(Ct);
    }
}
