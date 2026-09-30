using System;

using Cadence.Compliance;
using Cadence.Security.Audit;
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
    public void ReplayCache_ForgetsAConsumedTokenOnlyOnceTheInjectedClockPassesItsExpiry()
    {
        var clock = new FakeClock();
        var cache = new ReplayCache(clock);
        double expiry = clock.UtcNow.AddSeconds(10).ToUnixTimeSeconds();

        cache.Consume("token", jti: "jti-1", expiry).Should().BeTrue();
        cache.Consume("token", jti: "jti-1", expiry).Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(9));
        cache.Consume("token", jti: "jti-1", expiry).Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(2));
        cache.Consume("token", jti: "jti-1", expiry).Should().BeTrue();
    }

    [Fact]
    public void AuditDigestEngine_StampsRecordsFromTheInjectedClock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero));
        var engine = new AuditDigestEngine(new InMemoryAuditStore(), secretKey: "clock-test-audit-key", clock: clock);

        var record = engine.LogEvent(new AuditLogPayload
        {
            ServiceName = "execution",
            ActionType = "CREATE",
            EntityName = "ClinicalObservation",
            EntityId = "OBS-1",
            UserId = "crc-01",
            ReasonForChange = "Initial entry",
        });

        record.Timestamp.Should().Be(clock.UtcNow.UtcDateTime);
        record.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
        engine.VerifyChainIntegrity().Should().BeTrue();
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

    [Fact]
    public void ComplianceAuditLogRecord_WithoutATimestamp_KeepsNone()
    {
        var record = new ComplianceAuditLogRecord(RecordId: "rec-1", Action: "CREATE", UserId: "user-1", ChangeReason: "Test");

        record.Timestamp.Should().BeNull();
    }
}
