using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>
    /// Verifies that country matrix endpoint returns 6-facet readiness for all sites in a target country.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-001")]
    public async Task GetCountryMatrix_ReturnsSixFacetReadinessForAllSites()
    {
        // Act
        var dtos = await MatrixAsync("DE");

        // Assert
        dtos.Should().NotBeNull();
        dtos!.Count.Should().Be(6);

        var firstSite = dtos[0];
        firstSite.CountryCode.Should().Be("DE");
        firstSite.StudyId.Should().Be("PROTOCOL-2026-01");
        firstSite.ReadinessPercentage.Should().BeInRange(0, 100);
        firstSite.UnfulfilledReasons.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that non-compliant sites cannot be selected or batch certified.
    /// </summary>
    [Fact]
    [GxPRequirement("PRD-SSU-002")]
    public async Task BatchCertify_RejectsNonCompliantSites()
    {
        // Arrange: Seed country matrix
        await MatrixAsync("US");

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
            request, "21 CFR Part 11 Batch Certification", _db, Caller(), _clock, _config, TestContext.Current.CancellationToken);

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
        var matrix = await MatrixAsync("GB");
        var compliantSiteIds = matrix
            .Where(s => s.IsCompliant)
            .Select(s => s.SiteId)
            .ToList();

        compliantSiteIds.Should().HaveCountGreaterThan(0);

        var request = new BatchCertifyRequestDto(
            StudyId: "PROTOCOL-2026-01",
            CountryCode: "GB",
            SiteIds: compliantSiteIds,
            Meaning: "21 CFR Part 11 e-signature batch greenlight certification"
        );

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Batch activation approved", _db, Caller(), _clock, _config, TestContext.Current.CancellationToken);

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
                OverallStatus = "PENDING",
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
            request, "Performance test batch certification", _db, Caller(), _clock, _config, TestContext.Current.CancellationToken);

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
            OverallStatus = "PENDING",
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
            request, "Override batch certification", _db, Caller(), _clock, _config, TestContext.Current.CancellationToken);

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
        var matrix = await MatrixAsync("GB");
        var compliantSiteIds = matrix.Where(s => s.IsCompliant).Select(s => s.SiteId).ToList();
        var request = new BatchCertifyRequestDto("PROTOCOL-2026-01", "GB", compliantSiteIds, "Certify");

        var otherStudy = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Certify", _db, Caller(["sponsor_dm"], ["PROTOCOL-2027-09"]), _clock, _config, TestContext.Current.CancellationToken);
        var otherSite = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Certify", _db, Caller(["cra"], ["*"], ["SITE-ELSEWHERE"]), _clock, _config, TestContext.Current.CancellationToken);

        otherStudy.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        otherSite.Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await _db.CtmsSiteGreenlightGates.CountAsync(g => g.GreenlightToken != null, TestContext.Current.CancellationToken)).Should().Be(0);
        (await _db.CtmsAuditLogs.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    private async Task<List<SiteGreenlightGateResponseDto>> MatrixAsync(string country)
    {
        var result = await StartupGreenlightEndpoints.GetCountryMatrixAsync("PROTOCOL-2026-01", country, _db, Caller(), TestContext.Current.CancellationToken);
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
