using System;
using System.Text;

using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Security;

public class GreenlightTokenTests
{
    private static readonly byte[] TestSecret = Encoding.UTF8.GetBytes("test-secret-gateway-key-32-bytes!!");

    [Fact]
    public void Generate_GivenValidInputs_ProducesVerifiableToken()
    {
        string studyId = "STUDY-001";
        string siteId = "SITE-001";
        string certifiedBy = "qa-user-1";

        string token = GreenlightToken.Generate(
            studyId, siteId, certifiedBy, isOverridden: false, countryCode: "US", secret: TestSecret);

        token.Should().NotBeNullOrWhiteSpace();
        token.Split('.').Should().HaveCount(2);

        var payload = GreenlightToken.Verify(token, expectedStudyId: studyId, expectedSiteId: siteId, secret: TestSecret);

        payload.Should().NotBeNull();
        payload!.StudyId.Should().Be(studyId);
        payload.SiteId.Should().Be(siteId);
        payload.CertifiedBy.Should().Be(certifiedBy);
        payload.IsOverridden.Should().BeFalse();
        payload.TokenType.Should().Be("SITE_GREENLIGHT_ACTIVATION");
    }

    [Fact]
    public void Verify_GivenMismatchedStudyId_ReturnsNull()
    {
        string token = GreenlightToken.Generate("STUDY-001", "SITE-001", "user-1", secret: TestSecret);

        var payload = GreenlightToken.Verify(token, expectedStudyId: "WRONG-STUDY", expectedSiteId: "SITE-001", secret: TestSecret);

        payload.Should().BeNull();
    }

    [Fact]
    public void Verify_GivenMismatchedSiteId_ReturnsNull()
    {
        string token = GreenlightToken.Generate("STUDY-001", "SITE-001", "user-1", secret: TestSecret);

        var payload = GreenlightToken.Verify(token, expectedStudyId: "STUDY-001", expectedSiteId: "WRONG-SITE", secret: TestSecret);

        payload.Should().BeNull();
    }

    [Fact]
    public void Verify_GivenTamperedSignature_ReturnsNull()
    {
        string token = GreenlightToken.Generate("STUDY-001", "SITE-001", "user-1", secret: TestSecret);
        string[] parts = token.Split('.');
        string tamperedToken = $"{parts[0]}.bad{parts[1][3..]}";

        var payload = GreenlightToken.Verify(tamperedToken, "STUDY-001", "SITE-001", secret: TestSecret);

        payload.Should().BeNull();
    }

    [Fact]
    public void Verify_GivenExpiredToken_ReturnsNull()
    {
        var fixedClock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        string token = GreenlightToken.Generate(
            "STUDY-001", "SITE-001", "user-1", lifetime: TimeSpan.FromSeconds(10), secret: TestSecret, clock: fixedClock);

        DateTimeOffset future = fixedClock.UtcNow.AddSeconds(20);
        var payload = GreenlightToken.Verify(
            token, "STUDY-001", "SITE-001", secret: TestSecret, now: future);

        payload.Should().BeNull();
    }

    [Fact]
    public void Generate_ExecutiveOverride_SetsIsOverriddenTrue()
    {
        string token = GreenlightToken.Generate(
            "STUDY-001", "SITE-001", "exec-sponsor", isOverridden: true, secret: TestSecret);

        var payload = GreenlightToken.Verify(token, "STUDY-001", "SITE-001", secret: TestSecret);

        payload.Should().NotBeNull();
        payload!.IsOverridden.Should().BeTrue();
    }
}
