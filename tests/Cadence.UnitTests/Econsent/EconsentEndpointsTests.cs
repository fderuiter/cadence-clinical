using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Application;
using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using MassTransit;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Econsent;

/// <summary>
/// 21 CFR Part 11 step-up electronic signature verification tests for eConsent capture endpoints.
/// </summary>
public sealed class EconsentEndpointsTests : IDisposable
{
    private const string ActionUrl = "/api/v1/econsent/templates/icf-001/versions/1/capture-consent";
    private const string SemanticAction = "econsent.consent.capture";
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("test-econsent-secret-key-32-bytes-long!!");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public EconsentEndpointsTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task SigTokenVerifier_ValidTokenForEconsent_VerifiesAndConsumesSuccessfully()
    {
        var verifier = new SigTokenVerifier();
        string token = SigTokenIssuer.Issue(
            new SigTokenGrant("subj-001", "participant.jane", ["Subject"], ActionUrl, SemanticAction),
            SystemClock.Instance.UtcNow,
            Secret);

        var binding = new SigTokenBinding(ActionUrl, SemanticAction);
        var verifiedJwt = await verifier.VerifyAndConsumeAsync(token, "subj-001", binding, Secret, TestContext.Current.CancellationToken);

        verifiedJwt.Should().NotBeNull();
        verifiedJwt.Subject.Should().Be("subj-001");
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SigTokenVerifier_ReusedTokenForEconsent_FailsClosedWithReauthenticationRequired()
    {
        var verifier = new SigTokenVerifier();
        string token = SigTokenIssuer.Issue(
            new SigTokenGrant("subj-001", "participant.jane", ["Subject"], ActionUrl, SemanticAction),
            SystemClock.Instance.UtcNow,
            Secret);

        var binding = new SigTokenBinding(ActionUrl, SemanticAction);
        await verifier.VerifyAndConsumeAsync(token, "subj-001", binding, Secret, TestContext.Current.CancellationToken);

        Func<Task> replay = () => verifier.VerifyAndConsumeAsync(token, "subj-001", binding, Secret, TestContext.Current.CancellationToken);
        await replay.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task SigTokenVerifier_TokenForDifferentActionUrl_FailsClosedWithReauthenticationRequired()
    {
        var verifier = new SigTokenVerifier();
        string token = SigTokenIssuer.Issue(
            new SigTokenGrant("subj-001", "participant.jane", ["Subject"], "/api/v1/econsent/templates/icf-001/versions/2/capture-consent", SemanticAction),
            SystemClock.Instance.UtcNow,
            Secret);

        var binding = new SigTokenBinding(ActionUrl, SemanticAction);
        Func<Task> verify = () => verifier.VerifyAndConsumeAsync(token, "subj-001", binding, Secret, TestContext.Current.CancellationToken);
        await verify.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage(SigTokenVerifier.ReauthenticationRequired);
    }

    [Theory]
    [InlineData(null, "STUDY-1", "SITE-1", "digest123", "sigdata123", "subject_pseudonym")]
    [InlineData("", "STUDY-1", "SITE-1", "digest123", "sigdata123", "subject_pseudonym")]
    [InlineData("SUBJ-1", null, "SITE-1", "digest123", "sigdata123", "study_id")]
    [InlineData("SUBJ-1", "  ", "SITE-1", "digest123", "sigdata123", "study_id")]
    [InlineData("SUBJ-1", "STUDY-1", null, "digest123", "sigdata123", "site_id")]
    [InlineData("SUBJ-1", "STUDY-1", "SITE-1", null, "sigdata123", "digest_sha256")]
    [InlineData("SUBJ-1", "STUDY-1", "SITE-1", "digest123", null, "signature_data")]
    [InlineData("SUBJ-1", "STUDY-1", "SITE-1", "digest123", "sigdata123", "liveness_token")]
    public async Task ProcessCaptureConsentAsync_IncompletePayload_ReturnsBadRequestValidationErrors(
        string? subjectPseudonym,
        string? studyId,
        string? siteId,
        string? digestSha256,
        string? signatureData,
        string expectedErrorKey)
    {
        using var context = CreateContext();
        var clock = new FakeClock();
        string? livenessToken = null;
        if (expectedErrorKey != "liveness_token")
        {
            string tokenSubject = string.IsNullOrWhiteSpace(subjectPseudonym) ? "SUBJ-1" : subjectPseudonym;
            livenessToken = BiometricLivenessTokenService.IssueToken(
                tokenSubject, 0.95m, true, secret: TestSecurityContext.BiometricLivenessKeyBytes, now: clock.UtcNow);
        }

        var request = new CaptureConsentRequest(
            SubjectPseudonym: subjectPseudonym,
            StudyId: studyId,
            SiteId: siteId,
            SignerName: "Test Signer",
            SignerEmail: "test@example.com",
            SignerRole: "Subject",
            Meaning: "I agree",
            SigningReason: null,
            ProtocolVersion: "v1.0",
            DigestSha256: digestSha256,
            SignatureData: signatureData,
            LivenessToken: livenessToken,
            DeviceTimestamp: clock.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, CreateService(context, clock, new ReplayCache(clock)), Caller(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ValidationProblem>();
        var valProblem = (ValidationProblem)result;
        valProblem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        valProblem.ProblemDetails.Errors.Should().ContainKey(expectedErrorKey);

        var consentsCount = await context.SubjectConsents.CountAsync(TestContext.Current.CancellationToken);
        consentsCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessCaptureConsentAsync_SpecialCharactersInSignerNameAndReason_SerializesValidJsonManifest()
    {
        using var context = CreateContext();
        var clock = new FakeClock();
        string signerName = "Jane \"The Doctor\" O'Connor\tSpecialist";
        string meaning = "Reason with \"quotes\", newlines:\nLine 2\ttab and \\backslash.";
        string livenessToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-999", 0.98m, true, secret: TestSecurityContext.BiometricLivenessKeyBytes, now: clock.UtcNow);

        var request = new CaptureConsentRequest(
            SubjectPseudonym: "SUBJ-999",
            StudyId: "STUDY-100",
            SiteId: "SITE-05",
            SignerName: signerName,
            SignerEmail: "jane@example.com",
            SignerRole: "Subject",
            Meaning: meaning,
            SigningReason: null,
            ProtocolVersion: "v2.0",
            DigestSha256: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
            SignatureData: "SIG_BINDING_DATA_001",
            LivenessToken: livenessToken,
            DeviceTimestamp: clock.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 2, request, CreateService(context, clock, new ReplayCache(clock)), Caller(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<CaptureConsentResponse>>();

        var consentRecord = await context.SubjectConsents.FirstOrDefaultAsync(c => c.SubjectPseudonym == "SUBJ-999", TestContext.Current.CancellationToken);
        consentRecord.Should().NotBeNull();

        // Verify SignatureManifestJson is valid JSON and preserves original unescaped strings when parsed
        Action parseAction = () => JsonDocument.Parse(consentRecord!.SignatureManifestJson);
        parseAction.Should().NotThrow();

        using var doc = JsonDocument.Parse(consentRecord!.SignatureManifestJson);
        doc.RootElement.GetProperty("signer_name").GetString().Should().Be(signerName);
        doc.RootElement.GetProperty("signer_role").GetString().Should().Be("Subject");
        doc.RootElement.GetProperty("meaning").GetString().Should().Be(meaning);
        doc.RootElement.GetProperty("digest").GetString().Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"); // pragma: allowlist secret
    }

    [Fact]
    public async Task ProcessCaptureConsentAsync_ValidRequest_PersistsConsentAndSignatureRecords()
    {
        using var context = CreateContext();
        var clock = new FakeClock();
        string livenessToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-123", 0.98m, true, secret: TestSecurityContext.BiometricLivenessKeyBytes, now: clock.UtcNow);
        var request = new CaptureConsentRequest(
            SubjectPseudonym: "SUBJ-123",
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            SignerName: "John Doe",
            SignerEmail: "john@example.com",
            SignerRole: "Subject",
            Meaning: "I agree to participate",
            SigningReason: null,
            ProtocolVersion: "v1.0",
            DigestSha256: "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
            SignatureData: "DATA_BINDING_V1_SUBJ-123",
            LivenessToken: livenessToken,
            DeviceTimestamp: clock.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, CreateService(context, clock, new ReplayCache(clock)), Caller(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<CaptureConsentResponse>>();
        var okResult = (Ok<CaptureConsentResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.SubjectPseudonym.Should().Be("SUBJ-123");

        var consentRecord = await context.SubjectConsents.FirstOrDefaultAsync(c => c.SubjectPseudonym == "SUBJ-123", TestContext.Current.CancellationToken);
        consentRecord.Should().NotBeNull();
        consentRecord!.StudyId.Should().Be("STUDY-101");
        consentRecord.SiteId.Should().Be("SITE-01");

        var signatureRecord = await context.ConsentSignatures.FirstOrDefaultAsync(s => s.SubjectPseudonym == "SUBJ-123", TestContext.Current.CancellationToken);
        signatureRecord.Should().NotBeNull();
        signatureRecord!.SignerName.Should().Be("John Doe");

        var livenessRecord = await context.BiometricLivenessVerifications.FirstOrDefaultAsync(l => l.SubjectPseudonym == "SUBJ-123", TestContext.Current.CancellationToken);
        livenessRecord.Should().NotBeNull();
        livenessRecord!.IsLive.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessCaptureConsentAsync_ExpiredLivenessToken_ReturnsBadRequestValidationErrors()
    {
        using var context = CreateContext();
        var clock = new FakeClock();
        var past = clock.UtcNow.AddMinutes(-30);
        string expiredToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-EXP", 0.95m, true, secret: TestSecurityContext.BiometricLivenessKeyBytes, now: past);

        var request = new CaptureConsentRequest(
            SubjectPseudonym: "SUBJ-EXP",
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            SignerName: "John Doe",
            SignerEmail: "john@example.com",
            SignerRole: "Subject",
            Meaning: "I agree to participate",
            SigningReason: null,
            ProtocolVersion: "v1.0",
            DigestSha256: "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
            SignatureData: "DATA_BINDING_V1_SUBJ-EXP",
            LivenessToken: expiredToken,
            DeviceTimestamp: clock.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, CreateService(context, clock, new ReplayCache(clock)), Caller(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ValidationProblem>();
        var valProblem = (ValidationProblem)result;
        valProblem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        valProblem.ProblemDetails.Errors.Should().ContainKey("liveness_token");
    }

    [Fact]
    public async Task ProcessPublishTemplateAsync_WhenRequiresReconsentIsTrue_PublishesReconsentRequiredEventAndSavesTemplate()
    {
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var request = new PublishTemplateRequest(
            StudyId: "STUDY-101",
            ProtocolVersion: "v2.0",
            ChangeSummary: "Updated safety risks and procedure schedule.",
            ChangeReason: "Protocol amendment 2",
            RequiresReconsent: true,
            Channels: new[] { "SMS", "Mobile Push", "Email" });

        var result = await EconsentEndpoints.ProcessPublishTemplateAsync(
            "tpl-reconsent-01", 1, request, CreateService(context, new FakeClock(), publishEndpoint: publishEndpoint), Caller(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<PublishTemplateResponse>>();
        var okResult = (Ok<PublishTemplateResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.TemplateId.Should().Be("tpl-reconsent-01");
        okResult.Value.IsPublished.Should().BeTrue();
        okResult.Value.RequiresReconsent.Should().BeTrue();

        var dbTemplate = await context.ConsentTemplates.FirstOrDefaultAsync(t => t.TemplateId == "tpl-reconsent-01", TestContext.Current.CancellationToken);
        dbTemplate.Should().NotBeNull();
        dbTemplate!.IsPublished.Should().BeTrue();
        dbTemplate.RequiresReconsent.Should().BeTrue();

        await publishEndpoint.Received(1).Publish(
            Arg.Is<EconsentReconsentRequired>(e =>
                e.StudyId == "STUDY-101" &&
                e.TemplateId == "tpl-reconsent-01" &&
                e.VersionIndex == 1 &&
                e.ChangeSummary == "Updated safety risks and procedure schedule." &&
                e.ProtocolVersion == "v2.0"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessPublishTemplateAsync_WhenRequiresReconsentIsFalse_DoesNotPublishReconsentEvent()
    {
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        var request = new PublishTemplateRequest(
            StudyId: "STUDY-101",
            ProtocolVersion: "v1.1",
            ChangeSummary: "Typo fix in clause 3.",
            ChangeReason: "Administrative correction",
            RequiresReconsent: false,
            Channels: null);

        var result = await EconsentEndpoints.ProcessPublishTemplateAsync(
            "tpl-minor-01", 1, request, CreateService(context, new FakeClock(), publishEndpoint: publishEndpoint), Caller(), TestContext.Current.CancellationToken);

        result.Should().BeOfType<Ok<PublishTemplateResponse>>();
        var okResult = (Ok<PublishTemplateResponse>)result;
        okResult.Value.Should().NotBeNull();
        okResult.Value!.RequiresReconsent.Should().BeFalse();

        var dbTemplate = await context.ConsentTemplates.FirstOrDefaultAsync(t => t.TemplateId == "tpl-minor-01", TestContext.Current.CancellationToken);
        dbTemplate.Should().NotBeNull();
        dbTemplate!.RequiresReconsent.Should().BeFalse();

        await publishEndpoint.DidNotReceive().Publish(
            Arg.Any<EconsentReconsentRequired>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A consent captured for a study the caller is not assigned to is refused and stores nothing (#5361).</summary>
    [Fact]
    public async Task ProcessCaptureConsentAsync_ForAStudyOutsideTheCallersStudies_IsRefusedAndStoresNothing()
    {
        using var context = CreateContext();
        var clock = new FakeClock();
        var useStore = new ReplayCache(clock);
        string livenessToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-1", 0.95m, true, secret: TestSecurityContext.BiometricLivenessKeyBytes, now: clock.UtcNow);
        var request = new CaptureConsentRequest(
            "SUBJ-1", "STUDY-1", "SITE-1", "Signer", null, "Subject", "I agree", null, "v1.0", "digest123", "sigdata123", livenessToken, clock.UtcNow);

        var service = CreateService(context, clock, useStore);
        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, service, Caller("STUDY-2"), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await context.SubjectConsents.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await context.ConsentSignatures.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);

        // Study mismatch must not burn the subject's fresh liveness check; retrying with authorized caller succeeds.
        var retry = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, service, Caller("STUDY-1"), TestContext.Current.CancellationToken);
        retry.Should().BeOfType<Ok<CaptureConsentResponse>>();
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ProcessCaptureConsentAsync_UnkeyedMintingThrowsAndOversizedTtlTokenIsRejected()
    {
        using var context = CreateContext();
        var clock = new FakeClock();

        Action unkeyedMint = () => BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01", 0.99m, true, "camera", secret: null, lifetime: TimeSpan.FromDays(3650));
        unkeyedMint.Should().Throw<ArgumentException>();

        string oversizedTtlToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01",
            0.99m,
            true,
            "camera",
            secret: TestSecurityContext.BiometricLivenessKeyBytes,
            now: clock.UtcNow,
            lifetime: TimeSpan.FromDays(3650));

        var request = new CaptureConsentRequest(
            SubjectPseudonym: "SUBJ-VERIFY-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            SignerName: "Jane Participant",
            SignerEmail: "jane@example.com",
            SignerRole: "Subject",
            Meaning: "I agree to participate",
            SigningReason: null,
            ProtocolVersion: "v1.0",
            DigestSha256: "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
            SignatureData: "DATA_BINDING_V1_SUBJ-VERIFY-01",
            LivenessToken: oversizedTtlToken,
            DeviceTimestamp: clock.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-verify-01",
            1,
            request,
            CreateService(context, clock, new ReplayCache(clock)),
            Caller(),
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<ValidationProblem>();
        var problem = (ValidationProblem)result;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Errors.Should().ContainKey("liveness_token");
        (await context.SubjectConsents.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ProcessCaptureConsentAsync_ReplayedLivenessTokenAcrossVersions_FailsClosed()
    {
        using var context = CreateContext();
        var clock = new FakeClock();
        var sharedUseStore = new ReplayCache(clock);
        string livenessToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-VERIFY-01",
            0.99m,
            true,
            "camera",
            secret: TestSecurityContext.BiometricLivenessKeyBytes,
            now: clock.UtcNow);

        var firstRequest = new CaptureConsentRequest(
            SubjectPseudonym: "SUBJ-VERIFY-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            SignerName: "Jane Participant",
            SignerEmail: "jane@example.com",
            SignerRole: "Subject",
            Meaning: "I agree to participate",
            SigningReason: null,
            ProtocolVersion: "v1.0",
            DigestSha256: "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
            SignatureData: "DATA_BINDING_V1_SUBJ-VERIFY-01",
            LivenessToken: livenessToken,
            DeviceTimestamp: clock.UtcNow);

        var service = CreateService(context, clock, sharedUseStore);
        var firstResult = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-verify-01",
            1,
            firstRequest,
            service,
            Caller(),
            TestContext.Current.CancellationToken);
        firstResult.Should().BeOfType<Ok<CaptureConsentResponse>>();

        var secondRequest = firstRequest with
        {
            ProtocolVersion = "v2.0",
            SignatureData = "DATA_BINDING_V2_SUBJ-VERIFY-01",
        };
        var replayResult = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-verify-01",
            2,
            secondRequest,
            service,
            Caller(),
            TestContext.Current.CancellationToken);

        replayResult.Should().BeOfType<ValidationProblem>();
        var problem = (ValidationProblem)replayResult;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Errors.Should().ContainKey("liveness_token");
        (await context.SubjectConsents.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task ProcessCaptureConsentAsync_ValidationErrorBeforeConsume_DoesNotBurnLivenessToken()
    {
        using var context = CreateContext();
        var clock = new FakeClock();
        var sharedUseStore = new ReplayCache(clock);
        string livenessToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-RETRY-01",
            0.98m,
            true,
            "camera",
            secret: TestSecurityContext.BiometricLivenessKeyBytes,
            now: clock.UtcNow);

        var incompleteRequest = new CaptureConsentRequest(
            SubjectPseudonym: "SUBJ-RETRY-01",
            StudyId: "STUDY-101",
            SiteId: "SITE-01",
            SignerName: "Jane Participant",
            SignerEmail: "jane@example.com",
            SignerRole: "Subject",
            Meaning: "I agree",
            SigningReason: null,
            ProtocolVersion: "v1.0",
            DigestSha256: "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
            SignatureData: null,
            LivenessToken: livenessToken,
            DeviceTimestamp: clock.UtcNow);

        var service = CreateService(context, clock, sharedUseStore);
        var badResult = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-retry-01",
            1,
            incompleteRequest,
            service,
            Caller(),
            TestContext.Current.CancellationToken);
        badResult.Should().BeOfType<ValidationProblem>();

        var validRequest = incompleteRequest with { SignatureData = "SIG-RETRY-01" };
        var retryResult = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-retry-01",
            1,
            validRequest,
            service,
            Caller(),
            TestContext.Current.CancellationToken);
        retryResult.Should().BeOfType<Ok<CaptureConsentResponse>>();
    }

    [Fact]
    public async Task BiometricLivenessVerification_DuplicateTokenHash_RejectedByUniqueIndex()
    {
        using var context = CreateContext();
        var now = new FakeClock().UtcNow;
        const string duplicateHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"; // pragma: allowlist secret

        context.BiometricLivenessVerifications.Add(new BiometricLivenessVerification
        {
            Id = Guid.NewGuid().ToString(),
            ConsentId = Guid.NewGuid().ToString(),
            SubjectPseudonym = "SUBJ-IDX-1",
            LivenessToken = "tok-1",
            LivenessTokenHash = duplicateHash,
            AntiSpoofScore = 0.99m,
            IsLive = true,
            Status = "VERIFIED",
            VerifiedAt = now,
            CreatedAt = now,
            CreatedBy = "crc-1",
            ReasonForChange = "Initial liveness verification",
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.BiometricLivenessVerifications.Add(new BiometricLivenessVerification
        {
            Id = Guid.NewGuid().ToString(),
            ConsentId = Guid.NewGuid().ToString(),
            SubjectPseudonym = "SUBJ-IDX-1",
            LivenessToken = "tok-1",
            LivenessTokenHash = duplicateHash,
            AntiSpoofScore = 0.99m,
            IsLive = true,
            Status = "VERIFIED",
            VerifiedAt = now,
            CreatedAt = now,
            CreatedBy = "crc-1",
            ReasonForChange = "Duplicate liveness verification",
        });

        Func<Task> saveDuplicate = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await saveDuplicate.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>Publishing a template of a study the caller is not assigned to is refused and announces nothing (#5361).</summary>
    [Fact]
    public async Task ProcessPublishTemplateAsync_ForAStudyOutsideTheCallersStudies_IsRefusedAndPublishesNothing()
    {
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var request = new PublishTemplateRequest("STUDY-101", "v2.0", "Change", "Amendment", true, null);

        var result = await EconsentEndpoints.ProcessPublishTemplateAsync(
            "tpl-outside", 1, request, CreateService(context, new FakeClock(), publishEndpoint: publishEndpoint), Caller("STUDY-2"), TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await context.ConsentTemplates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        await publishEndpoint.DidNotReceiveWithAnyArgs().Publish(default(EconsentReconsentRequired)!, TestContext.Current.CancellationToken);
    }

    [Fact]
    [GxPRequirement("PRD-CDR-006")]
    public async Task ReplicatedScopeLocks_RejectConsentCaptureAndTemplatePublishWith423_WithoutSpendingBiometricLivenessToken()
    {
        var ct = TestContext.Current.CancellationToken;
        using var context = CreateContext();
        var clock = new FakeClock();
        var sharedUseStore = new ReplayCache(clock);
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var service = CreateService(context, clock, sharedUseStore, publishEndpoint);
        var consumer = new Cadence.Apps.Econsent.Infrastructure.Consumers.KernelScopeLockEconsentConsumer(
            context,
            new EconsentUnitOfWork(context),
            clock: clock);

        string studyLockId = Guid.NewGuid().ToString("D");
        string studyNodeRef = $"study:{Guid.NewGuid():D}";
        var appliedContext = Substitute.For<ConsumeContext<KernelScopeLockApplied>>();
        appliedContext.CancellationToken.Returns(ct);
        appliedContext.Message.Returns(new KernelScopeLockApplied
        {
            TenantCode = "demo",
            LockId = $"assertion:{studyLockId}",
            StudyRef = studyNodeRef,
            StudyCode = "STUDY-LOCK-1",
            ScopeKind = "study",
            ScopeRef = "study:STUDY-LOCK-1",
            LockKind = "Frozen",
            Milestone = "InterimCut",
            AppliedBy = "dm.lead",
            AppliedAt = clock.UtcNow,
        });
        await consumer.Consume(appliedContext);

        // 1. Publishing a template under an active study lock returns HTTP 423 and emits no re-consent event.
        var publishRequest = new PublishTemplateRequest("STUDY-LOCK-1", "v2.0", "Protocol amendment", "Amendment", true, null);
        var lockedPublish = await EconsentEndpoints.ProcessPublishTemplateAsync(
            "tpl-locked-01", 2, publishRequest, service, Caller("STUDY-LOCK-1"), ct);
        var lockedPublishProblem = lockedPublish.Should().BeOfType<ProblemHttpResult>().Subject;
        lockedPublishProblem.StatusCode.Should().Be(StatusCodes.Status423Locked);
        (await context.ConsentTemplates.CountAsync(ct)).Should().Be(0);
        await publishEndpoint.DidNotReceiveWithAnyArgs().Publish(default(EconsentReconsentRequired)!, ct);

        // 2. Capturing participant consent under an active scope lock returns HTTP 423 BEFORE spending the liveness token!
        string singleUseLivenessToken = BiometricLivenessTokenService.IssueToken(
            "SUBJ-LOCKED-01",
            0.99m,
            true,
            "camera",
            secret: TestSecurityContext.BiometricLivenessKeyBytes,
            now: clock.UtcNow);
        var captureRequest = new CaptureConsentRequest(
            SubjectPseudonym: "SUBJ-LOCKED-01",
            StudyId: "STUDY-LOCK-1",
            SiteId: "SITE-01",
            SignerName: "Elena Participant",
            SignerEmail: "elena@example.com",
            SignerRole: "Subject",
            Meaning: "I agree to participate",
            SigningReason: null,
            ProtocolVersion: "v2.0",
            DigestSha256: "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
            SignatureData: "SIG-DATA-LOCKED-01",
            LivenessToken: singleUseLivenessToken,
            DeviceTimestamp: clock.UtcNow);

        var lockedCapture = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-locked-01", 2, captureRequest, service, Caller("STUDY-LOCK-1"), ct);
        lockedCapture.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status423Locked);
        (await context.SubjectConsents.CountAsync(ct)).Should().Be(0);

        // 3. Releasing the study lock allows both template publish AND retrying consent capture with the SAME liveness token!
        var releasedContext = Substitute.For<ConsumeContext<KernelScopeLockReleased>>();
        releasedContext.CancellationToken.Returns(ct);
        var releasedMsg = new KernelScopeLockReleased
        {
            TenantCode = "demo",
            ReleaseAssertionId = Guid.NewGuid().ToString("D"),
            LockId = $"assertion:{studyLockId}",
            StudyRef = studyNodeRef,
            StudyCode = "STUDY-LOCK-1",
            ScopeKind = "study",
            ScopeRef = "study:STUDY-LOCK-1",
            LockKind = "Frozen",
            ReleasedBy = "sponsor.admin",
            ReleasedAt = clock.UtcNow.AddSeconds(10),
            ReleaseReason = "Reopened for amendment consent capture",
        };
        releasedContext.Message.Returns(releasedMsg);
        await consumer.Consume(releasedContext);

        var unlockedPublish = await EconsentEndpoints.ProcessPublishTemplateAsync(
            "tpl-locked-01", 2, publishRequest, service, Caller("STUDY-LOCK-1"), ct);
        unlockedPublish.Should().BeOfType<Ok<PublishTemplateResponse>>();

        var unlockedCapture = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-locked-01", 2, captureRequest, service, Caller("STUDY-LOCK-1"), ct);
        unlockedCapture.Should().BeOfType<Ok<CaptureConsentResponse>>(
            "rejecting consent capture under a lock must not spend the participant's single-use biometric liveness token");
        (await context.SubjectConsents.CountAsync(ct)).Should().Be(1);
    }

    private static DefaultHttpContext Caller(params string[] studies)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc-1",
            Roles = ["crc"],
            AssignedStudies = studies.Length == 0 ? ["*"] : studies,
            AssignedSites = ["SITE-01", "SITE-05", "SITE-1"],
        };
        return httpContext;
    }

    private EconsentDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<EconsentDbContext>().UseSqlite(_connection).Options, TestFieldProtection.CreateEncryptor());

    private static IEconsentApplicationService CreateService(
        EconsentDbContext context,
        IClock clock,
        ISigTokenUseStore? useStore = null,
        IPublishEndpoint? publishEndpoint = null) =>
        new EconsentApplicationService(
            context,
            new EconsentUnitOfWork(context),
            publishEndpoint ?? Substitute.For<IPublishEndpoint>(),
            TestSecurityContext.BiometricLivenessOptions,
            useStore ?? new ReplayCache(clock),
            clock);
}
