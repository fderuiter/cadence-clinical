using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel.Time;
using Cadence.Modules.Intelligence.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Intelligence;

/// <summary>
/// Unit tests for biometric liveness verification and liveness token issuance/validation.
/// </summary>
public sealed class BiometricLivenessTests
{
    private const string DefaultTenant = "tenant-alpha";
    private const string DefaultIssuer = "crc-alpha-01";
    private const string ValidPngFrame1 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGNQSX4NRAwQCgAlagXJu/VFxAAAAABJRU5ErkJggg==";
    private const string ValidPngFrame2 = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGOQc1gPRAwQCgAbNgQ16pJthAAAAABJRU5ErkJggg==";
    private const string ValidJpegFrame = "/9j/4AAQSkZJRgABAQAAAQABAAD/wAALCAACAAIBAREA/9k=";
    private const string ZeroHeightJpegFrame = "/9j/4AAQSkZJRgABAQAAAQABAAD/wAALCAAAAAIBAREA/9k=";
    private const string ValidWebPVp8Frame = "UklGRhYAAABXRUJQVlA4IAoAAAAAAACdASoCAAIA";
    private const string ZeroWidthWebPVp8Frame = "UklGRhYAAABXRUJQVlA4IAoAAAAAAACdASoAAAIA";
    private const string ValidWebPVp8LFrame = "UklGRhEAAABXRUJQVlA4TAUAAAAvAUAAAA==";
    private const string ValidWebPVp8XFrame = "UklGRhYAAABXRUJQVlA4WAoAAAAAAAAAAQAAAQAA";

    private static byte[] TestSecret => TestSecurityContext.BiometricLivenessKeyBytes;

    /// <summary>Tests that IssueToken generates a valid HMAC-SHA256 token verified by VerifyToken.</summary>
    [Fact]
    public void BiometricLivenessTokenService_ValidToken_VerifiesSuccessfully()
    {
        var clock = new FakeClock();
        string subject = "SUBJ-TEST-001";
        string token = BiometricLivenessTokenService.IssueToken(
            subject,
            0.98m,
            true,
            "camera",
            secret: TestSecret,
            now: clock.UtcNow,
            tenantId: DefaultTenant,
            issuerId: DefaultIssuer,
            studyId: "STUDY-ALPHA",
            siteId: "SITE-ALPHA");

        bool isValid = BiometricLivenessTokenService.VerifyToken(
            token,
            subject,
            out var data,
            secret: TestSecret,
            now: clock.UtcNow,
            expectedTenantId: DefaultTenant,
            expectedStudyId: "STUDY-ALPHA",
            expectedSiteId: "SITE-ALPHA");

        isValid.Should().BeTrue();
        data.Should().NotBeNull();
        data!.SubjectPseudonym.Should().Be(subject);
        data.AntiSpoofScore.Should().Be(0.98m);
        data.IsLive.Should().BeTrue();
        data.Mode.Should().Be("camera");
        data.Jti.Should().NotBeNullOrWhiteSpace();
        data.TenantScope.Should().Be(DefaultTenant);
        data.IssuerId.Should().Be(DefaultIssuer);
        data.StudyScope.Should().Be("STUDY-ALPHA");
        data.SiteScope.Should().Be("SITE-ALPHA");
    }

    /// <summary>Tests that IssueToken and VerifyToken fail closed when secret is null or not 32 bytes.</summary>
    [Fact]
    public void BiometricLivenessTokenService_NullOrInvalidLengthSecret_ThrowsArgumentException()
    {
        Action issueNull = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: null, lifetime: TimeSpan.FromDays(3650), tenantId: DefaultTenant, issuerId: DefaultIssuer);
        Action issueShort = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: new byte[16], tenantId: DefaultTenant, issuerId: DefaultIssuer);
        Action issueLong = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: new byte[64], tenantId: DefaultTenant, issuerId: DefaultIssuer);

        issueNull.Should().Throw<ArgumentException>();
        issueShort.Should().Throw<ArgumentException>();
        issueLong.Should().Throw<ArgumentException>();

        var clock = new FakeClock();
        string validToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: TestSecret, now: clock.UtcNow, tenantId: DefaultTenant, issuerId: DefaultIssuer);

        Action verifyNull = () => BiometricLivenessTokenService.VerifyToken(
            validToken, "SUBJ-VERIFY-01", out _, secret: null, now: clock.UtcNow, expectedTenantId: DefaultTenant);
        Action verifyShort = () => BiometricLivenessTokenService.VerifyToken(
            validToken, "SUBJ-VERIFY-01", out _, secret: new byte[16], now: clock.UtcNow, expectedTenantId: DefaultTenant);

        verifyNull.Should().Throw<ArgumentException>();
        verifyShort.Should().Throw<ArgumentException>();
    }

    /// <summary>Tests that VerifyToken fails when verified with a different 32-byte HMAC secret.</summary>
    [Fact]
    public void BiometricLivenessTokenService_WrongSecret_FailsVerification()
    {
        var clock = new FakeClock();
        string token = BiometricLivenessTokenService.IssueToken(
            "SUBJ-KEY-01", 0.97m, true, "camera", secret: TestSecret, now: clock.UtcNow, tenantId: DefaultTenant, issuerId: DefaultIssuer);
        byte[] otherSecret = Encoding.ASCII.GetBytes("different-32-byte-liveness-key!!"); // pragma: allowlist secret

        bool isValid = BiometricLivenessTokenService.VerifyToken(
            token, "SUBJ-KEY-01", out var data, secret: otherSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant);

        isValid.Should().BeFalse();
        data.Should().BeNull();
    }

    /// <summary>Tests that VerifyToken rejects tokens whose lifetime exceeds 15 minutes.</summary>
    [Fact]
    public void BiometricLivenessTokenService_OversizedLifetime_FailsVerification()
    {
        var clock = new FakeClock();
        string sixteenMinToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-TTL-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow, lifetime: TimeSpan.FromMinutes(16), tenantId: DefaultTenant, issuerId: DefaultIssuer);
        string tenYearToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-TTL-01", 0.99m, true, "camera", secret: TestSecret, now: clock.UtcNow, lifetime: TimeSpan.FromDays(3650), tenantId: DefaultTenant, issuerId: DefaultIssuer);

        BiometricLivenessTokenService.VerifyToken(sixteenMinToken, "SUBJ-TTL-01", out var d1, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant)
            .Should().BeFalse();
        d1.Should().BeNull();

        BiometricLivenessTokenService.VerifyToken(tenYearToken, "SUBJ-TTL-01", out var d2, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant)
            .Should().BeFalse();
        d2.Should().BeNull();

        Action issueZeroTtl = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-TTL-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow, lifetime: TimeSpan.Zero, tenantId: DefaultTenant, issuerId: DefaultIssuer);
        issueZeroTtl.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Tests that VerifyToken enforces 5-second clock skew on IssuedAtUnix and ExpiresAtUnix.</summary>
    [Fact]
    public void BiometricLivenessTokenService_FutureIssuedAtBeyondClockSkew_FailsVerification()
    {
        var clock = new FakeClock();
        string withinSkew = BiometricLivenessTokenService.IssueToken(
            "SUBJ-SKEW-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow.AddSeconds(4), tenantId: DefaultTenant, issuerId: DefaultIssuer);
        string beyondSkew = BiometricLivenessTokenService.IssueToken(
            "SUBJ-SKEW-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow.AddSeconds(6), tenantId: DefaultTenant, issuerId: DefaultIssuer);

        BiometricLivenessTokenService.VerifyToken(withinSkew, "SUBJ-SKEW-01", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant)
            .Should().BeTrue();
        BiometricLivenessTokenService.VerifyToken(beyondSkew, "SUBJ-SKEW-01", out var rejected, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant)
            .Should().BeFalse();
        rejected.Should().BeNull();
    }

    /// <summary>Tests that VerifyToken rejects payloads signed with the valid key when Jti is blank.</summary>
    [Fact]
    public void BiometricLivenessTokenService_MissingJti_FailsVerification()
    {
        var clock = new FakeClock();
        long iat = clock.UtcNow.ToUnixTimeSeconds();
        var payload = new BiometricLivenessTokenData("SUBJ-JTI-01", 0.99m, true, "camera", iat, iat + 600, " ", DefaultTenant, DefaultIssuer);
        string signedToken = SignRawPayload(payload);

        bool isValid = BiometricLivenessTokenService.VerifyToken(
            signedToken, "SUBJ-JTI-01", out var data, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant);

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
        string token = BiometricLivenessTokenService.IssueToken(
            subject, 0.95m, true, "camera", secret: TestSecret, now: past, tenantId: DefaultTenant, issuerId: DefaultIssuer);

        bool isValid = BiometricLivenessTokenService.VerifyToken(
            token, subject, out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests that VerifyToken fails when subject pseudonym does not match.</summary>
    [Fact]
    public void BiometricLivenessTokenService_MismatchedSubject_FailsVerification()
    {
        var clock = new FakeClock();
        string subject = "SUBJ-TEST-003";
        string token = BiometricLivenessTokenService.IssueToken(
            subject, 0.95m, true, "camera", secret: TestSecret, now: clock.UtcNow, tenantId: DefaultTenant, issuerId: DefaultIssuer);

        bool isValid = BiometricLivenessTokenService.VerifyToken(
            token, "SUBJ-WRONG", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests that VerifyToken fails when token signature is tampered.</summary>
    [Fact]
    public void BiometricLivenessTokenService_TamperedToken_FailsVerification()
    {
        var clock = new FakeClock();
        string subject = "SUBJ-TEST-004";
        string token = BiometricLivenessTokenService.IssueToken(
            subject, 0.95m, true, "camera", secret: TestSecret, now: clock.UtcNow, tenantId: DefaultTenant, issuerId: DefaultIssuer);
        string tamperedToken = token + "modified";

        bool isValid = BiometricLivenessTokenService.VerifyToken(
            tamperedToken, subject, out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant);

        isValid.Should().BeFalse();
    }

    /// <summary>Tests that IssueToken and VerifyToken fail closed on blank or mismatched tenant, study, or site claims.</summary>
    [Fact]
    public void BiometricLivenessTokenService_BlankOrMismatchedTenantOrScope_FailsClosed()
    {
        var clock = new FakeClock();
        long iat = clock.UtcNow.ToUnixTimeSeconds();

        Action blankTenantIssue = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-SCOPE-01", 0.98m, true, "camera", secret: TestSecret, now: clock.UtcNow, tenantId: " ", issuerId: DefaultIssuer);
        blankTenantIssue.Should().Throw<ArgumentException>();

        Action blankFallbackScopeIssue = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-SCOPE-01", 0.92m, true, "coordinator_fallback", secret: TestSecret, now: clock.UtcNow, tenantId: DefaultTenant, issuerId: DefaultIssuer);
        blankFallbackScopeIssue.Should().Throw<ArgumentException>();

        string blankTenantSigned = SignRawPayload(new BiometricLivenessTokenData(
            "SUBJ-SCOPE-01", 0.98m, true, "camera", iat, iat + 600, Guid.NewGuid().ToString(), " ", DefaultIssuer));
        BiometricLivenessTokenService.VerifyToken(
                blankTenantSigned, "SUBJ-SCOPE-01", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: DefaultTenant)
            .Should().BeFalse();

        string scopedToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-SCOPE-01",
            0.92m,
            true,
            "coordinator_fallback",
            secret: TestSecret,
            now: clock.UtcNow,
            tenantId: "tenant-beta",
            issuerId: "crc-beta",
            studyId: "STUDY-BETA",
            siteId: "SITE-BETA");

        // Serialized payload must carry canonical wire claim names.
        string rawPayloadBase64 = scopedToken.Split('.')[0].Replace('-', '+').Replace('_', '/');
        rawPayloadBase64 += new string('=', (4 - (rawPayloadBase64.Length % 4)) % 4);
        using (var doc = JsonDocument.Parse(Convert.FromBase64String(rawPayloadBase64)))
        {
            doc.RootElement.GetProperty("tenant_id").GetString().Should().Be("tenant-beta");
            doc.RootElement.GetProperty("issuer_id").GetString().Should().Be("crc-beta");
            doc.RootElement.GetProperty("study_id").GetString().Should().Be("STUDY-BETA");
            doc.RootElement.GetProperty("site_id").GetString().Should().Be("SITE-BETA");
        }

        BiometricLivenessTokenService.VerifyToken(
                scopedToken, "SUBJ-SCOPE-01", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: " ")
            .Should().BeFalse();
        BiometricLivenessTokenService.VerifyToken(
                scopedToken, "SUBJ-SCOPE-01", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: "tenant-alpha", expectedStudyId: "STUDY-BETA", expectedSiteId: "SITE-BETA")
            .Should().BeFalse();
        BiometricLivenessTokenService.VerifyToken(
                scopedToken, "SUBJ-SCOPE-01", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: "tenant-beta", expectedStudyId: "STUDY-ALPHA", expectedSiteId: "SITE-BETA")
            .Should().BeFalse();
        BiometricLivenessTokenService.VerifyToken(
                scopedToken, "SUBJ-SCOPE-01", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: "tenant-beta", expectedStudyId: "STUDY-BETA", expectedSiteId: "SITE-ALPHA")
            .Should().BeFalse();
        BiometricLivenessTokenService.VerifyToken(
                scopedToken, "SUBJ-SCOPE-01", out _, secret: TestSecret, now: clock.UtcNow, expectedTenantId: "tenant-beta", expectedStudyId: "STUDY-BETA", expectedSiteId: "SITE-BETA")
            .Should().BeTrue();
    }

    /// <summary>Tests VerifyLivenessAsync endpoint with valid video frames returns OK and VERIFIED token.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_ValidFrames_ReturnsOkWithVerifiedToken()
    {
        var clock = new FakeClock();
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: "SUBJ-100",
            VideoFrames: new List<string> { ValidPngFrame1, ValidPngFrame2 },
            ChallengeId: "ch-001",
            FallbackMode: false,
            StudyId: "STUDY-ALPHA",
            SiteId: "SITE-ALPHA");

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request,
            CreateCallerContext(userId: "SUBJ-100", roles: ["subject"], tenantId: DefaultTenant, studies: ["STUDY-ALPHA"], sites: ["SITE-ALPHA"]),
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<VerifyLivenessResponse>>();
        var okResult = (Ok<VerifyLivenessResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.IsLive.Should().BeTrue();
        okResult.Value.Status.Should().Be("VERIFIED");
        okResult.Value.AntiSpoofScore.Should().Be(0.98m);

        bool isVerified = BiometricLivenessTokenService.VerifyToken(
            okResult.Value.LivenessToken,
            "SUBJ-100",
            out var data,
            secret: TestSecret,
            now: clock.UtcNow,
            expectedTenantId: DefaultTenant,
            expectedStudyId: "STUDY-ALPHA",
            expectedSiteId: "SITE-ALPHA");
        isVerified.Should().BeTrue();
        data!.Mode.Should().Be("camera");
        data.TenantScope.Should().Be(DefaultTenant);
        data.IssuerId.Should().Be("SUBJ-100");
        data.StudyScope.Should().Be("STUDY-ALPHA");
        data.SiteScope.Should().Be("SITE-ALPHA");
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
            FallbackMode: true,
            StudyId: "STUDY-ALPHA",
            SiteId: "SITE-ALPHA");

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request,
            CreateCallerContext(userId: DefaultIssuer, roles: ["crc"], tenantId: DefaultTenant, studies: ["STUDY-ALPHA"], sites: ["SITE-ALPHA"]),
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<VerifyLivenessResponse>>();
        var okResult = (Ok<VerifyLivenessResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.IsLive.Should().BeTrue();
        okResult.Value.Status.Should().Be("VERIFIED");

        bool isVerified = BiometricLivenessTokenService.VerifyToken(
            okResult.Value.LivenessToken,
            "SUBJ-101",
            out var data,
            secret: TestSecret,
            now: clock.UtcNow,
            expectedTenantId: DefaultTenant,
            expectedStudyId: "STUDY-ALPHA",
            expectedSiteId: "SITE-ALPHA");
        isVerified.Should().BeTrue();
        data!.Mode.Should().Be("coordinator_fallback");
        data.TenantScope.Should().Be(DefaultTenant);
        data.IssuerId.Should().Be(DefaultIssuer);
        data.StudyScope.Should().Be("STUDY-ALPHA");
        data.SiteScope.Should().Be("SITE-ALPHA");
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
            request,
            CreateCallerContext(userId: "SUBJ-102", roles: ["subject"], tenantId: DefaultTenant),
            TestSecurityContext.BiometricLivenessOptions,
            new FakeClock(),
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<BadRequest<VerifyLivenessResponse>>();
        var badResult = (BadRequest<VerifyLivenessResponse>)result;
        badResult.Value.Should().NotBeNull();
        badResult.Value!.IsLive.Should().BeFalse();
        badResult.Value.Status.Should().Be("REJECTED");
        badResult.Value.LivenessToken.Should().BeEmpty();
    }

    /// <summary>Tests VerifyLivenessAsync without subject pseudonym returns ValidationProblem.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_MissingSubjectPseudonym_ReturnsValidationProblem()
    {
        var request = new VerifyLivenessRequest(
            SubjectPseudonym: null,
            VideoFrames: new List<string> { ValidPngFrame1 },
            ChallengeId: null,
            FallbackMode: false);

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request,
            CreateCallerContext(userId: DefaultIssuer, roles: ["crc"], tenantId: DefaultTenant),
            TestSecurityContext.BiometricLivenessOptions,
            new FakeClock(),
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<ValidationProblem>();
    }

    /// <summary>Tests that subject and non-clinical roles are denied fallback_mode: true and cross-subject camera minting with HTTP 403.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_SubjectFallbackCrossSubjectOrUnauthorizedScope_ReturnsForbidden()
    {
        var clock = new FakeClock();
        var subjectBetaCaller = CreateCallerContext(
            userId: "SUBJ-BETA-SELF",
            roles: ["subject"],
            tenantId: "tenant-beta",
            studies: ["STUDY-BETA"],
            sites: ["SITE-BETA"]);

        // 1. Subject attempting coordinator_fallback -> 403
        var fallbackAttempt = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-CANARY-ALPHA", [], null, FallbackMode: true, StudyId: "STUDY-BETA", SiteId: "SITE-BETA"),
            subjectBetaCaller,
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        fallbackAttempt.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 2. Subject attempting camera mode for another participant's subject_pseudonym -> 403
        var otherSubjectAttempt = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-CANARY-ALPHA-CAM", [ValidPngFrame1], null, FallbackMode: false),
            subjectBetaCaller,
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        otherSubjectAttempt.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 3. Caller missing verified tenant -> 403
        var missingTenantAttempt = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-BETA-SELF", [ValidPngFrame1], null, FallbackMode: false),
            CreateCallerContext(userId: "SUBJ-BETA-SELF", roles: ["subject"], tenantId: null),
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        missingTenantAttempt.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 4. CRC attempting coordinator_fallback for unassigned study or missing site -> 403
        var crcAlphaCaller = CreateCallerContext(
            userId: DefaultIssuer,
            roles: ["crc"],
            tenantId: DefaultTenant,
            studies: ["STUDY-ALPHA"],
            sites: ["SITE-ALPHA"]);
        var wrongStudyFallback = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-101", [], null, FallbackMode: true, StudyId: "STUDY-OTHER", SiteId: "SITE-ALPHA"),
            crcAlphaCaller,
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        wrongStudyFallback.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var missingSiteFallback = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-101", [], null, FallbackMode: true, StudyId: "STUDY-ALPHA", SiteId: null),
            crcAlphaCaller,
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        missingSiteFallback.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // 5. Investigator with study and site scope succeeds on coordinator_fallback -> 200
        var investigatorCaller = CreateCallerContext(
            userId: "pi-alpha-01",
            roles: ["investigator"],
            tenantId: DefaultTenant,
            studies: ["STUDY-ALPHA"],
            sites: ["SITE-ALPHA"]);
        var investigatorFallback = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-101", [], null, FallbackMode: true, StudyId: "STUDY-ALPHA", SiteId: "SITE-ALPHA"),
            investigatorCaller,
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        investigatorFallback.Should().BeOfType<Ok<VerifyLivenessResponse>>();
    }

    /// <summary>Tests that camera mode accepts valid PNG, JPEG, and WebP headers and rejects non-image, zero-dimension, and oversized frames with HTTP 400.</summary>
    [Theory]
    [InlineData(ValidPngFrame1, true)]
    [InlineData("data:image/png;base64," + ValidPngFrame1, true)]
    [InlineData(ValidJpegFrame, true)]
    [InlineData(ValidWebPVp8Frame, true)]
    [InlineData(ValidWebPVp8LFrame, true)]
    [InlineData(ValidWebPVp8XFrame, true)]
    [InlineData("inert-non-image-marker", false)]
    [InlineData("%%%not-base64%%%", false)]
    [InlineData("data:text/plain;base64,aGVsbG8=", false)]
    [InlineData(ZeroHeightJpegFrame, false)]
    [InlineData(ZeroWidthWebPVp8Frame, false)]
    public async Task VerifyLivenessAsync_CameraImageFormatsAndInvalidHeaders_EnforcesImageVerification(
        string framePayload,
        bool expectedValid)
    {
        var clock = new FakeClock();
        var caller = CreateCallerContext(userId: "SUBJ-IMG-01", roles: ["subject"], tenantId: DefaultTenant);
        var request = new VerifyLivenessRequest("SUBJ-IMG-01", [framePayload], "ch-img", FallbackMode: false);

        var result = await BiometricEndpoints.VerifyLivenessAsync(
            request, caller, TestSecurityContext.BiometricLivenessOptions, clock, TestContext.Current.CancellationToken);

        if (expectedValid)
        {
            result.Should().BeOfType<Ok<VerifyLivenessResponse>>();
        }
        else
        {
            result.Should().BeOfType<ValidationProblem>()
                .Which.ProblemDetails.Errors.Should().ContainKey("video_frames");
        }
    }

    /// <summary>Tests that camera mode enforces frame count and byte size upper bounds before scoring liveness.</summary>
    [Fact]
    public async Task VerifyLivenessAsync_ExcessiveFrameCountOrZeroDimensionPng_ReturnsValidationProblem()
    {
        var clock = new FakeClock();
        var caller = CreateCallerContext(userId: "SUBJ-LIMIT-01", roles: ["subject"], tenantId: DefaultTenant);

        var tooManyFrames = new List<string>();
        for (int i = 0; i <= BiometricEndpoints.MaxFrameCount; i++)
        {
            tooManyFrames.Add(ValidPngFrame1);
        }

        var countResult = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-LIMIT-01", tooManyFrames, null, FallbackMode: false),
            caller,
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        countResult.Should().BeOfType<ValidationProblem>()
            .Which.ProblemDetails.Errors.Should().ContainKey("video_frames");

        // Craft a 33-byte PNG header with width = 0, height = 2.
        byte[] zeroWidthPng =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x00, // width = 0
            0x00, 0x00, 0x00, 0x02, // height = 2
            0x08, 0x02, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        var zeroPngResult = await BiometricEndpoints.VerifyLivenessAsync(
            new VerifyLivenessRequest("SUBJ-LIMIT-01", [Convert.ToBase64String(zeroWidthPng)], null, FallbackMode: false),
            caller,
            TestSecurityContext.BiometricLivenessOptions,
            clock,
            TestContext.Current.CancellationToken);
        zeroPngResult.Should().BeOfType<ValidationProblem>()
            .Which.ProblemDetails.Errors.Should().ContainKey("video_frames");
    }

    /// <summary>Tests HTTP route pipeline (RequireTenantScope, snake_case study_id/site_id wire binding, fallback RBAC, and non-image rejection).</summary>
    [Fact]
    public async Task MapBiometricEndpoints_OverHttp_EnforcesTenantScopeSnakeCaseBindingAndCanaryGuards()
    {
        var clock = new FakeClock();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["GATEWAY_SECRET"] = TestSecurityContext.GatewaySecret;
        builder.Configuration[BiometricLivenessOptions.ConfigurationKey] = TestSecurityContext.BiometricLivenessKeyBase64;
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
        builder.Services.AddSingleton<IClock>(clock);
        builder.Services.AddCadenceSecurity();
        builder.Services.AddCadenceBiometricLiveness();

        await using var app = builder.Build();
        app.UseCadenceSecurity();
        app.MapBiometricEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);

        HttpClient CreateSignedClient(GatewayIdentity identity)
        {
            var client = app.GetTestClient();
            foreach (var (name, value) in TestSecurityContext.CreateHmacHeaders(identity with
            {
                ProductFamilies = string.IsNullOrWhiteSpace(identity.TenantId)
                    ? null
                    : TestSecurityContext.AllProductFamilies,
            }))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
            }

            return client;
        }

        // 0. Signed-in caller without X-Tenant-Id is blocked by RequireTenantScope() -> 403
        using var noTenantClient = CreateSignedClient(new GatewayIdentity(
            UserId: "SUBJ-NO-TENANT",
            Roles: ["subject"],
            SiteIds: ["SITE-ALPHA"],
            TenantId: null,
            StudyIds: ["STUDY-ALPHA"]));
        using var noTenantResp = await noTenantClient.PostAsJsonAsync(
            "/api/v1/ai/biometrics/verify-liveness",
            new { subject_pseudonym = "SUBJ-NO-TENANT", video_frames = new[] { ValidPngFrame1 }, fallback_mode = false },
            TestContext.Current.CancellationToken);
        noTenantResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 1. Unprivileged 'subject' in tenant-beta attempts coordinator_fallback for SUBJ-CANARY-ALPHA -> 403
        using var subjectBetaKernelClient = CreateSignedClient(new GatewayIdentity(
            UserId: "SUBJ-CANARY-BETA",
            Roles: ["subject"],
            SiteIds: ["SITE-BETA"],
            TenantId: "tenant-beta",
            StudyIds: ["STUDY-BETA"]));
        using var fallbackResp = await subjectBetaKernelClient.PostAsJsonAsync(
            "/api/v1/ai/biometrics/verify-liveness",
            new { subject_pseudonym = "SUBJ-CANARY-ALPHA", video_frames = Array.Empty<string>(), fallback_mode = true },
            TestContext.Current.CancellationToken);
        fallbackResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2a. Cross-subject camera request -> 403
        using var crossSubjectMarkerResp = await subjectBetaKernelClient.PostAsJsonAsync(
            "/api/v1/ai/biometrics/verify-liveness",
            new { subject_pseudonym = "SUBJ-CANARY-ALPHA-CAM", video_frames = new[] { "inert-non-image-marker" }, fallback_mode = false },
            TestContext.Current.CancellationToken);
        crossSubjectMarkerResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2b. Own-subject non-image marker string in camera mode -> 400
        using var ownSubjectMarkerResp = await subjectBetaKernelClient.PostAsJsonAsync(
            "/api/v1/ai/biometrics/verify-liveness",
            new { subject_pseudonym = "SUBJ-CANARY-BETA", video_frames = new[] { "inert-non-image-marker" }, fallback_mode = false },
            TestContext.Current.CancellationToken);
        ownSubjectMarkerResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Authorized CRC in tenant-alpha with STUDY-ALPHA + SITE-ALPHA issues coordinator_fallback over snake_case JSON -> 200
        using var crcAlphaClient = CreateSignedClient(new GatewayIdentity(
            UserId: DefaultIssuer,
            Roles: ["crc"],
            SiteIds: ["SITE-ALPHA"],
            TenantId: DefaultTenant,
            StudyIds: ["STUDY-ALPHA"]));
        using var crcFallbackResp = await crcAlphaClient.PostAsJsonAsync(
            "/api/v1/ai/biometrics/verify-liveness",
            new
            {
                subject_pseudonym = "SUBJ-CANARY-ALPHA",
                video_frames = Array.Empty<string>(),
                fallback_mode = true,
                study_id = "STUDY-ALPHA",
                site_id = "SITE-ALPHA",
            },
            TestContext.Current.CancellationToken);
        crcFallbackResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await crcFallbackResp.Content.ReadFromJsonAsync<VerifyLivenessResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        verifyBody.Should().NotBeNull();
        BiometricLivenessTokenService.VerifyToken(
                verifyBody!.LivenessToken,
                "SUBJ-CANARY-ALPHA",
                out var verifiedClaims,
                secret: TestSecret,
                now: clock.UtcNow,
                expectedTenantId: DefaultTenant,
                expectedStudyId: "STUDY-ALPHA",
                expectedSiteId: "SITE-ALPHA")
            .Should().BeTrue();
        verifiedClaims!.Mode.Should().Be("coordinator_fallback");
        verifiedClaims.StudyScope.Should().Be("STUDY-ALPHA");
        verifiedClaims.SiteScope.Should().Be("SITE-ALPHA");
    }

    private static string SignRawPayload(BiometricLivenessTokenData payload)
    {
        string base64Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        using var hmac = new HMACSHA256(TestSecret);
        string base64Sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(base64Payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{base64Payload}.{base64Sig}";
    }

    private static DefaultHttpContext CreateCallerContext(
        string userId,
        string[] roles,
        string? tenantId,
        string[]? studies = null,
        string[]? sites = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Cadence.Security.Rbac.Principal
        {
            UserId = userId,
            TenantId = tenantId,
            Roles = roles,
            AssignedStudies = studies ?? ["*"],
            AssignedSites = sites ?? ["SITE-ALPHA"],
        };
        return httpContext;
    }
}
