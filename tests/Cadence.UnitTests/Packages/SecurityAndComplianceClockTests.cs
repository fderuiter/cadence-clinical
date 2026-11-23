using System;

using Cadence.Compliance;
using Cadence.Security.Authentication;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

/// <summary>
/// #4784 (K-TIME-3): <c>packages/security</c> and <c>packages/compliance</c> take time from the injected
/// <see cref="Cadence.Kernel.Time.IClock"/>, never the wall clock.
/// </summary>
public sealed class SecurityAndComplianceClockTests
{
    [Fact]
    public void ReplayCache_ForgetsAUsedTokenOnlyOnceTheInjectedClockPassesItsRetention()
    {
        var clock = new FakeClock();
        var cache = new ReplayCache(clock);
        var retainUntil = clock.UtcNow.AddSeconds(10);

        cache.TryRecordFirstUse("jti-1", retainUntil).Should().BeTrue();
        cache.TryRecordFirstUse("jti-1", retainUntil).Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(9));
        cache.TryRecordFirstUse("jti-1", retainUntil).Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(2));
        cache.TryRecordFirstUse("jti-1", retainUntil).Should().BeTrue();
    }

    [Fact]
    public void GxPSigner_TakesTheSignatureTimestampFromTheInjectedClock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));

        string signed = GxPSigner.SignGxpMarkdown("Qualification report body.", signerId: "qa-lead", clock: clock);

        signed.Should().Contain("- **Timestamp:** 2026-03-04 05:06:07 UTC");
    }

    [Fact]
    public void Pkcs7Signer_SelfSignedCredentials_AreValidFromTheInjectedClock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero));

        var (certificate, key) = Pkcs7Signer.GenerateSigningCredentials("qa-lead", clock);
        using (certificate)
        using (key)
        {
            certificate.NotBefore.ToUniversalTime().Should().Be(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc));
            certificate.NotAfter.ToUniversalTime().Should().Be(new DateTime(2027, 3, 4, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
