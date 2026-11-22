using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Security;

/// <summary>
/// Cryptographic and expiry validation tests for CTMS startup greenlight tokens (<c>PRD-SSU-003</c>).
/// </summary>
public sealed class GreenlightTokenValidatorTests
{
    private const string TestHmacMaterial = "unit-test-greenlight-hmac-material-32b";
    private static readonly DateTimeOffset IssuedAt = new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly long IssuedUnix = IssuedAt.ToUnixTimeSeconds();
    private static readonly long ExpiresUnix = IssuedAt.AddHours(24).ToUnixTimeSeconds();

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public void Validate_ValidSignedTokenMatchingSiteAndStudy_Succeeds()
    {
        var clock = new FakeClock(IssuedAt.AddHours(2));
        var validator = new GreenlightTokenValidator(TestHmacMaterial, clock);
        var token = GreenlightTokenValidator.GenerateToken(
            "SITE-101", "CADENCE-101", "US", "ctms-dir-1", TestHmacMaterial, IssuedUnix, ExpiresUnix);

        var result = validator.Validate(token, "SITE-101", "CADENCE-101");

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.SiteCode.Should().Be("SITE-101");
        result.StudyCode.Should().Be("CADENCE-101");
        result.CountryCode.Should().Be("US");
        result.CertifiedBy.Should().Be("ctms-dir-1");
        result.IssuedAt.Should().Be(IssuedUnix);
        result.ExpiresAt.Should().Be(ExpiresUnix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-valid-token")]
    [InlineData("a.b.c")]
    [GxPRequirement("PRD-SSU-003")]
    public void Validate_MissingOrMalformedToken_FailsClosed(string? token)
    {
        var clock = new FakeClock(IssuedAt);
        var validator = new GreenlightTokenValidator(TestHmacMaterial, clock);

        var result = validator.Validate(token, "SITE-101", "CADENCE-101");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public void Validate_TamperedSignatureOrWrongSecret_FailsSignatureVerification()
    {
        var clock = new FakeClock(IssuedAt.AddMinutes(5));
        var validator = new GreenlightTokenValidator(TestHmacMaterial, clock);
        var token = GreenlightTokenValidator.GenerateToken(
            "SITE-101", "CADENCE-101", "US", "ctms-dir-1", TestHmacMaterial, IssuedUnix, ExpiresUnix);
        var wrongKeyToken = GreenlightTokenValidator.GenerateToken(
            "SITE-101", "CADENCE-101", "US", "ctms-dir-1", TestHmacMaterial + "-wrong", IssuedUnix, ExpiresUnix);

        validator.Validate(wrongKeyToken, "SITE-101", "CADENCE-101").IsValid.Should().BeFalse();
        string flippedLastHex = token[^1] == '0' ? "1" : "0";
        validator.Validate(token[..^1] + flippedLastHex, "SITE-101", "CADENCE-101").IsValid.Should().BeFalse();
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public void Validate_ExpiredToken_FailsWithExpiredReason()
    {
        var clock = new FakeClock(IssuedAt.AddHours(24).AddSeconds(1));
        var validator = new GreenlightTokenValidator(TestHmacMaterial, clock);
        var token = GreenlightTokenValidator.GenerateToken(
            "SITE-101", "CADENCE-101", "US", "ctms-dir-1", TestHmacMaterial, IssuedUnix, ExpiresUnix);

        var result = validator.Validate(token, "SITE-101", "CADENCE-101");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("expired");
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public void Validate_MismatchedSiteOrStudy_FailsScopeBinding()
    {
        var clock = new FakeClock(IssuedAt.AddMinutes(10));
        var validator = new GreenlightTokenValidator(TestHmacMaterial, clock);
        var token = GreenlightTokenValidator.GenerateToken(
            "SITE-101", "CADENCE-101", "US", "ctms-dir-1", TestHmacMaterial, IssuedUnix, ExpiresUnix);

        var wrongSite = validator.Validate(token, "SITE-102", "CADENCE-101");
        wrongSite.IsValid.Should().BeFalse();
        wrongSite.FailureReason.Should().Contain("SITE-101");

        var wrongStudy = validator.Validate(token, "SITE-101", "CADENCE-999");
        wrongStudy.IsValid.Should().BeFalse();
        wrongStudy.FailureReason.Should().Contain("CADENCE-101");
    }

    [Fact]
    [GxPRequirement("PRD-SSU-003")]
    public void Validate_WrongTokenTypeSignedWithSameSecret_FailsTokenTypeCheck()
    {
        var clock = new FakeClock(IssuedAt.AddMinutes(5));
        var validator = new GreenlightTokenValidator(TestHmacMaterial, clock);
        var forgedPayload = JsonSerializer.Serialize(new
        {
            token_type = "other_token",
            site_id = "SITE-101",
            study_id = "CADENCE-101",
            country_code = "US",
            certified_by = "ctms-dir-1",
            issued_at = IssuedUnix,
            expires_at = ExpiresUnix,
        });
        var payloadB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(forgedPayload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var sig = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(TestHmacMaterial),
            Encoding.UTF8.GetBytes(payloadB64)));

        var result = validator.Validate($"{payloadB64}.{sig}", "SITE-101", "CADENCE-101");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("token_type");
    }
}
