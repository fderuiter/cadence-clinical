using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Ctms.Endpoints;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Compliance;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

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
        var result = await StartupGreenlightEndpoints.GetCountryMatrixAsync("PROTOCOL-2026-01", "DE", _db, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var dtos = result.Value;
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
        await StartupGreenlightEndpoints.GetCountryMatrixAsync("PROTOCOL-2026-01", "US", _db, TestContext.Current.CancellationToken);

        var nonCompliantSite = await _db.CtmsSiteGreenlightGates
            .FirstAsync(g => !g.FacetDoaLog || !g.FacetIpRelease, TestContext.Current.CancellationToken);

        var request = new BatchCertifyRequestDto(
            StudyId: "PROTOCOL-2026-01",
            CountryCode: "US",
            SiteIds: new List<string> { nonCompliantSite.SiteId },
            SignerName: "Dr. Eleanor Vance",
            SignerRole: "VP of Regulatory & Clinical Operations",
            Meaning: "Batch certifying greenlight activation"
        );

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "21 CFR Part 11 Batch Certification", _db, _clock, _config, TestContext.Current.CancellationToken);

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
        var matrix = await StartupGreenlightEndpoints.GetCountryMatrixAsync("PROTOCOL-2026-01", "GB", _db, TestContext.Current.CancellationToken);
        var compliantSiteIds = matrix.Value!
            .Where(s => s.IsCompliant)
            .Select(s => s.SiteId)
            .ToList();

        compliantSiteIds.Should().HaveCountGreaterThan(0);

        var request = new BatchCertifyRequestDto(
            StudyId: "PROTOCOL-2026-01",
            CountryCode: "GB",
            SiteIds: compliantSiteIds,
            SignerName: "Dr. Eleanor Vance",
            SignerRole: "VP of Regulatory & Clinical Operations",
            Meaning: "21 CFR Part 11 e-signature batch greenlight certification"
        );

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Batch activation approved", _db, _clock, _config, TestContext.Current.CancellationToken);

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
        auditLogs.Select(a => a.UserId).Distinct().Single().Should().Be("Dr. Eleanor Vance");
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
            SignerName: "Benchmark Manager",
            SignerRole: "Global Startup Director",
            Meaning: "Performance test batch certification"
        );

        var sw = Stopwatch.StartNew();

        // Act
        var result = await StartupGreenlightEndpoints.BatchCertifyCountrySitesAsync(
            request, "Performance test batch certification", _db, _clock, _config, TestContext.Current.CancellationToken);

        sw.Stop();

        // Assert
        result.Result.Should().BeOfType<Ok<BatchCertifyResponseDto>>();
        sw.ElapsedMilliseconds.Should().BeLessThan(5000); // Constraint < 5 seconds
    }
}
