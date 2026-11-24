using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// Unit tests for biometric liveness verification and liveness token issuance/validation.
/// </summary>
public sealed class BiometricLivenessTests
{
    private static byte[] TestSecret => TestSecurityContext.BiometricLivenessKeyBytes;

    /// <summary>Tests that IssueToken generates a valid HMAC-SHA256 token verified by VerifyToken.</summary>
    [Fact]
    public void BiometricLivenessTokenService_ValidToken_VerifiesSuccessfully()
    {
        var clock = new FakeClock();
        string subject = "SUBJ-TEST-001";
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow);

        bool isValid = BiometricLivenessTokenService.VerifyToken(token, subject, out var data, secret: TestSecret, now: clock.UtcNow);

        isValid.Should().BeTrue();
        data.Should().NotBeNull();
        data!.SubjectPseudonym.Should().Be(subject);
        data.AntiSpoofScore.Should().Be(0.98m);
        data.IsLive.Should().BeTrue();
        data.Mode.Should().Be("camera");
        data.Jti.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Tests that IssueToken and VerifyToken fail closed when secret is null or not 32 bytes.</summary>
    [Fact]
    public void BiometricLivenessTokenService_NullOrInvalidLengthSecret_ThrowsArgumentException()
    {
        Action issueNull = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: null, lifetime: TimeSpan.FromDays(3650));
        Action issueShort = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: new byte[16]);
        Action issueLong = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: new byte[64]);

        issueNull.Should().Throw<ArgumentException>();
        issueShort.Should().Throw<ArgumentException>();
        issueLong.Should().Throw<ArgumentException>();

        var clock = new FakeClock();
        string validToken = BiometricLivenessTokenService.IssueToken("SUBJ-VERIFY-01", 0.99m, true, "camera", secret: TestSecret, now: clock.UtcNow);

        Action verifyNull = () => BiometricLivenessTokenService.VerifyToken(validToken, "SUBJ-VERIFY-01", out _, secret: null, now: clock.UtcNow);
        Action verifyShort = () => BiometricLivenessTokenService.VerifyToken(validToken, "SUBJ-VERIFY-01", out _, secret: new byte[16], now: clock.UtcNow);

        verifyNull.Should().Throw<ArgumentException>();
        verifyShort.Should().Throw<ArgumentException>();
    }

    /// <summary>Tests that VerifyToken fails when verified with a different 32-byte HMAC secret.</summary>
    [Fact]
    public void BiometricLivenessTokenService_WrongSecret_FailsVerification()
    {
        var clock = new FakeClock();
        string token = BiometricLivenessTokenService.IssueToken("SUBJ-KEY-01", 0.97m, true, "camera", secret: TestSecret, now: clock.UtcNow);
        byte[] otherSecret = Encoding.ASCII.GetBytes("different-32-byte-liveness-key!!"); // pragma: allowlist secret

        bool isValid = BiometricLivenessTokenService.VerifyToken(token, "SUBJ-KEY-01", out var data, secret: otherSecret, now: clock.UtcNow);

        isValid.Should().BeFalse();
        data.Should().BeNull();
    }

    /// <summary>Tests that VerifyToken rejects tokens whose lifetime exceeds 15 minutes.</summary>
    [Fact]
    public void BiometricLivenessTokenService_OversizedLifetime_FailsVerification()
    {
        var clock = new FakeClock();
        string sixteenMinToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-TTL-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow, lifetime: TimeSpan.FromMinutes(16));
        string tenYearToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-TTL-01", 0.99m, true, "camera", secret: TestSecret, now: clock.UtcNow, lifetime: TimeSpan.FromDays(3650));

        BiometricLivenessTokenService.VerifyToken(sixteenMinToken, "SUBJ-TTL-01", out var d1, secret: TestSecret, now: clock.UtcNow)
            .Should().BeFalse();
        d1.Should().BeNull();

        BiometricLivenessTokenService.VerifyToken(tenYearToken, "SUBJ-TTL-01", out var d2, secret: TestSecret, now: clock.UtcNow)
            .Should().BeFalse();
        d2.Should().BeNull();

        Action issueZeroTtl = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-TTL-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow, lifetime: TimeSpan.Zero);
        issueZeroTtl.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Tests that VerifyToken enforces 5-second clock skew on IssuedAtUnix and ExpiresAtUnix.</summary>
    [Fact]
    public void BiometricLivenessTokenService_FutureIssuedAtBeyondClockSkew_FailsVerification()
    {
        var clock = new FakeClock();
        string withinSkew = BiometricLivenessTokenService.IssueToken(
            "SUBJ-SKEW-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow.AddSeconds(4));
        string beyondSkew = BiometricLivenessTokenService.IssueToken(
            "SUBJ-SKEW-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow.AddSeconds(6));

        BiometricLivenessTokenService.VerifyToken(withinSkew, "SUBJ-SKEW-01", out _, secret: TestSecret, now: clock.UtcNow)
            .Should().BeTrue();
        BiometricLivenessTokenService.VerifyToken(beyondSkew, "SUBJ-SKEW-01", out var rejected, secret: TestSecret, now: clock.UtcNow)
            .Should().BeFalse();
        rejected.Should().BeNull();
    }

    /// <summary>Tests that VerifyToken rejects payloads signed with the valid key when Jti is blank.</summary>
    [Fact]
    public void BiometricLivenessTokenService_MissingJti_FailsVerification()
    {
        var clock = new FakeClock();
        long iat = clock.UtcNow.ToUnixTimeSeconds();
        var payload = new BiometricLivenessTokenData("SUBJ-JTI-01", 0.99m, true, "camera", iat, iat + 600, " ");
        string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var hmac = new HMACSHA256(TestSecret);
        string base64Sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        bool isValid = BiometricLivenessTokenService.VerifyToken(
            $"{base64Payload}.{base64Sig}", "SUBJ-JTI-01", out var data, secret: TestSecret, now: clock.UtcNow);

        isValid.Should().BeFalse();
        data.Should().BeNull();
    }

    /// <summary>Tests that VerifyToken fails when given an expired token.</summary>
    [Fact]
    public void BiometricLivenessTokenService_ExpiredToken_FailsVerification()
    {
        string subject = "SUBJ-TEST-002";
        var clock = new FakeClock();
        var past = clock.UtcNow.AddMinutes(-30);
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.95m, true, "camera", secret: TestSecret, now: past);

        bool isValid = BiometricLivenessTokenService.VerifyToken(token, subject, out _, secret: TestSecret, now: clock.UtcNow);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests that VerifyToken fails when subject pseudonym does not match.</summary>
    [Fact]
    public void BiometricLivenessTokenService_MismatchedSubject_FailsVerification()
    {
        var clock = new FakeClock();
        string subject = "SUBJ-TEST-003";
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.95m, true, "camera", secret: TestSecret, now: clock.UtcNow);

        bool isValid = BiometricLivenessTokenService.VerifyToken(token, "SUBJ-WRONG", out _, secret: TestSecret, now: clock.UtcNow);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests that VerifyToken fails when token signature is tampered.</summary>
    [Fact]
    public void BiometricLivenessTokenService_TamperedToken_FailsVerification()
    {
        var clock = new FakeClock();
        string subject = "SUBJ-TEST-004";
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.95m, true, "camera", secret: TestSecret, now: clock.UtcNow);
        string tamperedToken = token + "modified";

        bool isValid = BiometricLivenessTokenService.VerifyToken(tamperedToken, subject, out _, secret: TestSecret, now: clock.UtcNow);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests VerifyLivenessAsync endpoint with valid video frames returns OK and VERIFIED token.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_ValidFrames_ReturnsOkWithVerifiedToken()
    {
        var clock = new FakeClock();
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: "SUBJ-100",
            VideoFrames: new List<string> { "frame1_base64_data", "frame2_base64_data" },
            ChallengeId: "ch-001",
            FallbackMode: false);

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request, TestSecurityContext.BiometricLivenessOptions, clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<VerifyLivenessResponse>>();
        var okResult = (Ok<VerifyLivenessResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.IsLive.Should().BeTrue();
        okResult.Value.Status.Should().Be("VERIFIED");
        okResult.Value.AntiSpoofScore.Should().Be(0.98m);

        bool isVerified = BiometricLivenessTokenService.VerifyToken(
            okResult.Value.LivenessToken, "SUBJ-100", out var data, secret: TestSecret, now: clock.UtcNow);
        isVerified.Should().BeTrue();
        data!.Mode.Should().Be("camera");
    }

    /// <summary>Tests VerifyLivenessAsync with fallback mode returns OK with coordinator_fallback token.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_CoordinatorFallback_ReturnsOkWithFallbackToken()
    {
        var clock = new FakeClock();
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: "SUBJ-101",
            VideoFrames: null,
            ChallengeId: "ch-002",
            FallbackMode: true);

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request, TestSecurityContext.BiometricLivenessOptions, clock, TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<VerifyLivenessResponse>>();
        var okResult = (Ok<VerifyLivenessResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.IsLive.Should().BeTrue();
        okResult.Value.Status.Should().Be("VERIFIED");

        bool isVerified = BiometricLivenessTokenService.VerifyToken(
            okResult.Value.LivenessToken, "SUBJ-101", out var data, secret: TestSecret, now: clock.UtcNow);
        isVerified.Should().BeTrue();
        data!.Mode.Should().Be("coordinator_fallback");
    }

    /// <summary>Tests VerifyLivenessAsync with empty frames returns BadRequest with REJECTED status.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_EmptyFrames_ReturnsBadRequestRejected()
    {
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: "SUBJ-102",
            VideoFrames: new List<string>(),
            ChallengeId: "ch-003",
            FallbackMode: false);

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request, TestSecurityContext.BiometricLivenessOptions, new FakeClock(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<BadRequest<VerifyLivenessResponse>>();
        var badResult = (BadRequest<VerifyLivenessResponse>)result;
        badResult.Value.Should().NotBeNull();
        badResult.Value!.IsLive.Should().BeFalse();
        badResult.Value.Status.Should().Be("REJECTED");
    }

    /// <summary>Tests VerifyLivenessAsync without subject pseudonym returns ValidationProblem.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_MissingSubjectPseudonym_ReturnsValidationProblem()
    {
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: null,
            VideoFrames: new List<string> { "frame1" },
            ChallengeId: null,
            FallbackMode: false);

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request, TestSecurityContext.BiometricLivenessOptions, new FakeClock(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ValidationProblem>();
    }
}
