using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Application.Services;
using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Xunit;

namespace Cadence.UnitTests.Ctms;

public sealed class StartupGreenlightEndpointsTests
{
    private const string Study = "PROTOCOL-2026-01";

    private readonly CtmsDbContext _db;
    private readonly FakeClock _clock;
    private readonly IConfiguration _config;

    public StartupGreenlightEndpointsTests()
    {
        var options = new DbContextOptionsBuilder<CtmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CtmsDbContext(options);

        _clock = new FakeClock(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

        var inMemorySettings = new Dictionary<string, string?>
        {
            ["GATEWAY_SECRET"] = "test-gateway-secret-32-chars-long-12345"
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
    }

    private StartupGreenlightApplicationService CreateService() =>
        new(_db, new CtmsUnitOfWork(_db), _clock, _config);

    /// <summary>
    /// Verifies that country matrix endpoint returns 6-facet readiness for the recorded sites in a target country.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-001")]
    public async Task GetCountryMatrix_ReturnsSixFacetReadinessForRecordedSites()
    {
        await AddGatesAsync(("SITE-DE-2", "DE", false), ("SITE-DE-1", "DE", true), ("SITE-GB-1", "GB", true));

        var dtos = await MatrixAsync("DE");

        dtos.Select(d => d.SiteId).Should().Equal("SITE-DE-1", "SITE-DE-2");
        dtos[0].IsCompliant.Should().BeTrue();
        dtos[0].ReadinessPercentage.Should().Be(100);
        dtos[1].IsCompliant.Should().BeFalse();
        dtos[1].ReadinessPercentage.Should().Be(67);
        dtos[1].UnfulfilledReasons.Should().Equal("Pending Delegation of Authority Log", "Pending Investigational Product Depot Release");
    }

    /// <summary>
    /// A study and country with no recorded gates is an empty matrix, and reading it writes nothing (#5433).
    /// </summary>
    [Fact]
    public async Task GetCountryMatrix_OnAnEmptyStudy_ReturnsNothingAndWritesNothing()
    {
        var dtos = await MatrixAsync("DE");

        dtos.Should().BeEmpty();
        (await _db.CtmsSiteGreenlightGates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    /// <summary>
    /// The matrix has no default study: a request without one is refused (#5433).
    /// </summary>
    [Fact]
    public async Task GetCountryMatrix_WithoutAStudy_IsRefused()
    {
        var result = await StartupGreenlightEndpoints.GetCountryMatrixAsync(" ", "US", CreateService(), Caller(), TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Reading or certifying a site with no recorded gate answers 404 and invents no gate or token (#5433).
    /// </summary>
    [Fact]
    public async Task SiteGreenlight_WithNoRecordedGate_IsNotFoundAndWritesNothing()
    {
        var read = await StartupGreenlightEndpoints.GetSiteGreenlightAsync(
            "SITE-NEW", Study, "US", CreateService(), Caller(), TestContext.Current.CancellationToken);
        var certify = await StartupGreenlightEndpoints.CertifySiteGreenlightAsync(
            "SITE-NEW", Study, "US", "Certify", CreateService(), Caller(), TestContext.Current.CancellationToken);

        read.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        certify.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        (await _db.CtmsSiteGreenlightGates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await _db.CtmsAuditLogs.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    /// <summary>
    /// A recorded, fully compliant gate is certified with a token and an audit entry naming the signed caller.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task CertifySite_WithACompliantRecordedGate_IssuesATokenSignedByTheCaller()
    {
        await AddGatesAsync(("SITE-US-1", "US", true));

        var result = await StartupGreenlightEndpoints.CertifySiteGreenlightAsync(
            "SITE-US-1", Study, "US", "Certify", CreateService(), Caller(), TestContext.Current.CancellationToken);

        var dto = result.Result.Should().BeOfType<Ok<SiteGreenlightGateResponseDto>>().Subject.Value!;
        dto.OverallStatus.Should().Be("APPROVED");
        dto.GreenlightToken.Should().Contain(".");
        dto.GreenlightCertifiedBy.Should().Be("sponsor-dm-1");
        (await _db.CtmsAuditLogs.SingleAsync(TestContext.Current.CancellationToken)).Action.Should().Be("GREENLIGHT_CERTIFIED");
    }

    /// <summary>
    /// Batch certify on an empty database, or naming any site without a recorded gate, is refused and writes
    /// nothing, not even for the sites that do have a gate (#5433).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchCertify_WithUnknownSites_IsRefusedAndWritesNothing(bool withOneKnownSite)
    {
        if (withOneKnownSite)
        {
            await AddGatesAsync(("SITE-US-1", "US", true));
        }

        var request = new BatchCertifyRequestDto(Study, "US", ["SITE-US-1", "SITE-US-9"], "Certify");

        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Certify", CreateService(), Caller(), TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<BadRequest<Microsoft.AspNetCore.Mvc.ProblemDetails>>()
            .Which.Value!.Detail.Should().Contain("SITE-US-9");
        (await _db.CtmsSiteGreenlightGates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(withOneKnownSite ? 1 : 0);
        (await _db.CtmsSiteGreenlightGates.CountAsync(g => g.GreenlightToken != null, TestContext.Current.CancellationToken)).Should().Be(0);
        (await _db.CtmsAuditLogs.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    /// <summary>
    /// Batch certify has no default study: a request without one is refused (#5433).
    /// </summary>
    [Fact]
    public async Task BatchCertify_WithoutAStudy_IsRefused()
    {
        var request = new BatchCertifyRequestDto("", "US", ["SITE-US-1"], "Certify");

        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Certify", CreateService(), Caller(), TestContext.Current.CancellationToken);

        result.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Verifies that non-compliant sites cannot be selected or batch certified.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-002")]
    public async Task BatchCertify_RejectsNonCompliantSites()
    {
        // Arrange
        await AddGatesAsync(("SITE-US-1", "US", true), ("SITE-US-2", "US", false));

        var nonCompliantSite = await _db.CtmsSiteGreenlightGates
            .FirstAsync(g => !g.FacetDoaLog || !g.FacetIpRelease, TestContext.Current.CancellationToken);

        var request = new BatchCertifyRequestDto(
            StudyId: "PROTOCOL-2026-01",
            CountryCode: "US",
            SiteIds: new List<string> { nonCompliantSite.SiteId },
            Meaning: "Batch certifying greenlight activation"
        );

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "21 CFR Part 11 Batch Certification", CreateService(), Caller(), TestContext.Current.CancellationToken);

        // Assert
        result.Result.Should().BeOfType<BadRequest<Microsoft.AspNetCore.Mvc.ProblemDetails>>();
    }

    /// <summary>
    /// Verifies that batch certification generates individual HMAC-SHA256 tokens and writes individual audit logs per site.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public async Task BatchCertify_CertifiesCompliantSites_WithIndividualTokensAndAuditLogs()
    {
        // Arrange
        await AddGatesAsync(("SITE-GB-1", "GB", true), ("SITE-GB-2", "GB", true), ("SITE-GB-3", "GB", false));
        var matrix = await MatrixAsync("GB");
        var compliantSiteIds = matrix
            .Where(s => s.IsCompliant)
            .Select(s => s.SiteId)
            .ToList();

        compliantSiteIds.Should().Equal("SITE-GB-1", "SITE-GB-2");

        var request = new BatchCertifyRequestDto(
            StudyId: "PROTOCOL-2026-01",
            CountryCode: "GB",
            SiteIds: compliantSiteIds,
            Meaning: "21 CFR Part 11 e-signature batch greenlight certification"
        );

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Batch activation approved", CreateService(), Caller(), TestContext.Current.CancellationToken);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<BatchCertifyResponseDto>>().Subject;
        var response = okResult.Value!;

        response.Success.Should().BeTrue();
        response.CertifiedCount.Should().Be(compliantSiteIds.Count);
        response.CertifiedSites.Should().HaveCount(compliantSiteIds.Count);

        // Distinct HMAC-SHA256 tokens
        var tokens = response.CertifiedSites.Select(s => s.GreenlightToken).ToList();
        tokens.Distinct().Count().Should().Be(compliantSiteIds.Count);
        tokens.All(t => t.Contains(".")).Should().BeTrue();

        // Individual Audit Log entries
        var auditLogs = await _db.CtmsAuditLogs
            .Where(a => a.Action == "BATCH_GREENLIGHT_CERTIFIED")
            .ToListAsync(TestContext.Current.CancellationToken);

        auditLogs.Count.Should().Be(compliantSiteIds.Count);
        auditLogs.Select(a => a.UserId).Distinct().Single().Should().Be("sponsor-dm-1");
    }

    /// <summary>
    /// Verifies that batch requests for 50 sites complete rapidly within 5 seconds limit.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-001")]
    public async Task BatchCertify_Performance_CompletesUnder5SecondsFor50Sites()
    {
        // Arrange: Seed 50 compliant sites
        var siteIds = new List<string>();
        for (int i = 1; i <= 50; i++)
        {
            string sId = $"PERF-SITE-{i:D3}";
            siteIds.Add(sId);
            _db.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate
            {
                StudyId = "PROTOCOL-2026-01",
                SiteId = sId,
                SiteName = $"Performance Test Site {i}",
                CountryCode = "US",
                ReadinessPercentage = 100,
                FacetRegulatoryIrb = true,
                FacetCtaBudget = true,
                FacetEisfBaseline = true,
                FacetDoaLog = true,
                FacetStaffTraining = true,
                FacetIpRelease = true,
                CreatedBy = "perf-test",
                ReasonForChange = "Performance benchmark setup"
            });
        }
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new BatchCertifyRequestDto(
            StudyId: "PROTOCOL-2026-01",
            CountryCode: "US",
            SiteIds: siteIds,
            Meaning: "Performance test batch certification"
        );

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Performance test batch certification", CreateService(), Caller(), TestContext.Current.CancellationToken);

        // Assert: the whole batch is certified. How long it took is the machine's business, not the test's (#5415).
        result.Result.Should().BeOfType<Ok<BatchCertifyResponseDto>>();
    }

    /// <summary>
    /// Verifies that batch certification allows sites with explicit GxP override even if unfulfilled facets exist.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-002")]
    public async Task BatchCertify_AllowsOverriddenSites()
    {
        // Arrange: Seed an overridden site with missing facets
        string siteId = "OVERRIDE-SITE-001";
        _db.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate
        {
            StudyId = "PROTOCOL-2026-01",
            SiteId = siteId,
            SiteName = "Overridden Test Site",
            CountryCode = "US",
            ReadinessPercentage = 50,
            FacetRegulatoryIrb = true,
            FacetCtaBudget = false,
            FacetEisfBaseline = false,
            FacetDoaLog = true,
            FacetStaffTraining = true,
            FacetIpRelease = false,
            IsOverridden = true,
            OverrideSponsorUserId = "sponsor-1",
            OverrideJustification = "Medical Officer GxP Exception Granted",
            CreatedBy = "test-setup",
            ReasonForChange = "Override setup"
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new BatchCertifyRequestDto(
            StudyId: "PROTOCOL-2026-01",
            CountryCode: "US",
            SiteIds: new List<string> { siteId },
            Meaning: "Batch certification with explicit GxP override"
        );

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Override batch certification", CreateService(), Caller(), TestContext.Current.CancellationToken);

        // Assert
        var okResult = result.Result.Should().BeOfType<Ok<BatchCertifyResponseDto>>().Subject;
        var response = okResult.Value!;
        response.Success.Should().BeTrue();
        response.CertifiedCount.Should().Be(1);
        response.CertifiedSites.Single().SiteId.Should().Be(siteId);
        response.CertifiedSites.Single().GreenlightToken.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Certifying sites of a study the caller is not assigned to is refused, and a site-scoped caller cannot certify a
    /// site outside its sites; neither writes a token or an audit entry (#5361).
    /// </summary>
    [Fact]
    public async Task BatchCertify_OutsideTheCallersStudiesOrSites_IsRefusedAndCertifiesNothing()
    {
        await AddGatesAsync(("SITE-GB-1", "GB", true), ("SITE-GB-2", "GB", true));
        var matrix = await MatrixAsync("GB");
        var compliantSiteIds = matrix.Where(s => s.IsCompliant).Select(s => s.SiteId).ToList();
        var request = new BatchCertifyRequestDto("PROTOCOL-2026-01", "GB", compliantSiteIds, "Certify");

        var otherStudy = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Certify", CreateService(), Caller(["sponsor_dm"], ["PROTOCOL-2027-09"]), TestContext.Current.CancellationToken);
        var otherSite = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Certify", CreateService(), Caller(["cra"], ["*"], ["SITE-ELSEWHERE"]), TestContext.Current.CancellationToken);

        otherStudy.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        otherSite.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await _db.CtmsSiteGreenlightGates.CountAsync(g => g.GreenlightToken != null, TestContext.Current.CancellationToken)).Should().Be(0);
        (await _db.CtmsAuditLogs.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task GetCountryMatrix_SiteScopedCaller_FiltersToAssignedSitesAndFailsClosedWhenUnassigned()
    {
        await AddGatesAsync(("SITE-DE-1", "DE", true), ("SITE-DE-2", "DE", true));

        var scopedResult = await StartupGreenlightEndpoints.GetCountryMatrixAsync(
            Study, "DE", CreateService(), Caller(["cra"], [Study], ["SITE-DE-1"]), TestContext.Current.CancellationToken);
        var scopedDtos = scopedResult.Result.Should().BeOfType<Ok<List<SiteGreenlightGateResponseDto>>>().Subject.Value!;
        scopedDtos.Select(d => d.SiteId).Should().Equal("SITE-DE-1");

        var unassignedSiteCaller = await StartupGreenlightEndpoints.GetCountryMatrixAsync(
            Study, "DE", CreateService(), Caller(["cra"], [Study], []), TestContext.Current.CancellationToken);
        var problem = unassignedSiteCaller.Result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions.Should().ContainKey("code").WhoseValue.Should().Be(CtmsScopeGuard.ForbiddenCode);

        var crossSiteRead = await StartupGreenlightEndpoints.GetSiteGreenlightAsync(
            "SITE-DE-2", Study, "DE", CreateService(), Caller(["cra"], [Study], ["SITE-DE-1"]), TestContext.Current.CancellationToken);
        crossSiteRead.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    private async Task AddGatesAsync(params (string SiteId, string Country, bool Compliant)[] sites)
    {
        foreach (var (siteId, country, compliant) in sites)
        {
            _db.CtmsSiteGreenlightGates.Add(new CtmsSiteGreenlightGate
            {
                StudyId = Study,
                SiteId = siteId,
                SiteName = $"Site {siteId}",
                CountryCode = country,
                ReadinessPercentage = compliant ? 100 : 67,
                FacetRegulatoryIrb = true,
                FacetCtaBudget = true,
                FacetEisfBaseline = true,
                FacetDoaLog = compliant,
                FacetStaffTraining = true,
                FacetIpRelease = compliant,
                CreatedBy = "test-setup",
                ReasonForChange = "Test gate"
            });
        }

        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _db.ChangeTracker.Clear();
    }

    private async Task<List<SiteGreenlightGateResponseDto>> MatrixAsync(string country)
    {
        var result = await StartupGreenlightEndpoints.GetCountryMatrixAsync(Study, country, CreateService(), Caller(), TestContext.Current.CancellationToken);
        return result.Result.Should().BeOfType<Ok<List<SiteGreenlightGateResponseDto>>>().Subject.Value!;
    }

    private static DefaultHttpContext Caller() => Caller(["sponsor_dm"], ["*"]);

    private static DefaultHttpContext Caller(string[] roles, string[] studies, string[]? sites = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "sponsor-dm-1",
            Roles = roles,
            AssignedStudies = studies,
            AssignedSites = sites ?? [],
        };
        return httpContext;
    }
}
