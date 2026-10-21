using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Cadence.Modules.Intelligence.Endpoints;
using Cadence.Security.Authentication;

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
    /// <summary>Tests that IssueToken generates a valid HMAC-SHA256 token verified by VerifyToken.</summary>
    [Fact]
    public void BiometricLivenessTokenService_ValidToken_VerifiesSuccessfully()
    {
        string subject = "SUBJ-TEST-001";
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.98m, true, "camera");

        bool isValid = BiometricLivenessTokenService.VerifyToken(token, subject, out var data);

        isValid.Should().BeTrue();
        data.Should().NotBeNull();
        data!.SubjectPseudonym.Should().Be(subject);
        data.AntiSpoofScore.Should().Be(0.98m);
        data.IsLive.Should().BeTrue();
        data.Mode.Should().Be("camera");
    }

    /// <summary>Tests that VerifyToken fails when given an expired token.</summary>
    [Fact]
    public void BiometricLivenessTokenService_ExpiredToken_FailsVerification()
    {
        string subject = "SUBJ-TEST-002";
        var past = TimeProvider.System.GetUtcNow().AddMinutes(-30);
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.95m, true, "camera", now: past);

        bool isValid = BiometricLivenessTokenService.VerifyToken(token, subject, out _);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests that VerifyToken fails when subject pseudonym does not match.</summary>
    [Fact]
    public void BiometricLivenessTokenService_MismatchedSubject_FailsVerification()
    {
        string subject = "SUBJ-TEST-003";
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.95m, true, "camera");

        bool isValid = BiometricLivenessTokenService.VerifyToken(token, "SUBJ-WRONG", out _);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests that VerifyToken fails when token signature is tampered.</summary>
    [Fact]
    public void BiometricLivenessTokenService_TamperedToken_FailsVerification()
    {
        string subject = "SUBJ-TEST-004";
        string token = BiometricLivenessTokenService.IssueToken(subject, 0.95m, true, "camera");
        string tamperedToken = token + "modified";

        bool isValid = BiometricLivenessTokenService.VerifyToken(tamperedToken, subject, out _);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests VerifyLivenessAsync endpoint with valid video frames returns OK and VERIFIED token.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_ValidFrames_ReturnsOkWithVerifiedToken()
    {
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: "SUBJ-100",
            VideoFrames: new List<string> { "frame1_base64_data", "frame2_base64_data" },
            ChallengeId: "ch-001",
            FallbackMode: false);

        var result = await BiometricEndpoints.VerifyLivenessAsync(request, TimeProvider.System, TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<VerifyLivenessResponse>>();
        var okResult = (Ok<VerifyLivenessResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.IsLive.Should().BeTrue();
        okResult.Value.Status.Should().Be("VERIFIED");
        okResult.Value.AntiSpoofScore.Should().Be(0.98m);

        bool isVerified = BiometricLivenessTokenService.VerifyToken(okResult.Value.LivenessToken, "SUBJ-100", out var data);
        isVerified.Should().BeTrue();
        data!.Mode.Should().Be("camera");
    }

    /// <summary>Tests VerifyLivenessAsync with fallback mode returns OK with coordinator_fallback token.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_CoordinatorFallback_ReturnsOkWithFallbackToken()
    {
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: "SUBJ-101",
            VideoFrames: null,
            ChallengeId: "ch-002",
            FallbackMode: true);

        var result = await BiometricEndpoints.VerifyLivenessAsync(request, TimeProvider.System, TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<VerifyLivenessResponse>>();
        var okResult = (Ok<VerifyLivenessResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.IsLive.Should().BeTrue();
        okResult.Value.Status.Should().Be("VERIFIED");

        bool isVerified = BiometricLivenessTokenService.VerifyToken(okResult.Value.LivenessToken, "SUBJ-101", out var data);
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

        var result = await BiometricEndpoints.VerifyLivenessAsync(request, TimeProvider.System, TestContext.Current.CancellationToken);

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

        var result = await BiometricEndpoints.VerifyLivenessAsync(request, TimeProvider.System, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ValidationProblem>();
    }
}
