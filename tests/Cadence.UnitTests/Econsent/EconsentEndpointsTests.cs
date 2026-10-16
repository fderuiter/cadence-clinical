using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Endpoints;
using Cadence.Apps.Econsent.Infrastructure.Persistence;
using Cadence.Contracts;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;
using Cadence.Testing;
using Cadence.Testing.Security;

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
/// @req:PRD-SYS-053
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
    [GxPRequirement("PRD-SYS-053")]
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
    [GxPRequirement("PRD-SYS-053")]
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
    [GxPRequirement("PRD-SYS-053")]
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
    public async Task ProcessCaptureConsentAsync_IncompletePayload_ReturnsBadRequestValidationErrors(
        string? subjectPseudonym,
        string? studyId,
        string? siteId,
        string? digestSha256,
        string? signatureData,
        string expectedErrorKey)
    {
        using var context = CreateContext();
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
            DeviceTimestamp: SystemClock.Instance.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, context, Caller(), TimeProvider.System, TestContext.Current.CancellationToken);

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
        string signerName = "Jane \"The Doctor\" O'Connor\tSpecialist";
        string meaning = "Reason with \"quotes\", newlines:\nLine 2\ttab and \\backslash.";

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
            DigestSha256: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            SignatureData: "SIG_BINDING_DATA_001",
            DeviceTimestamp: SystemClock.Instance.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 2, request, context, Caller(), TimeProvider.System, TestContext.Current.CancellationToken);

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
        doc.RootElement.GetProperty("digest").GetString().Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }

    [Fact]
    public async Task ProcessCaptureConsentAsync_ValidRequest_PersistsConsentAndSignatureRecords()
    {
        using var context = CreateContext();
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
            DigestSha256: "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289",
            SignatureData: "DATA_BINDING_V1_SUBJ-123",
            DeviceTimestamp: SystemClock.Instance.UtcNow);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, context, Caller(), TimeProvider.System, TestContext.Current.CancellationToken);

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
            "tpl-reconsent-01", 1, request, context, publishEndpoint, Caller(), TimeProvider.System, TestContext.Current.CancellationToken);

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
            "tpl-minor-01", 1, request, context, publishEndpoint, Caller(), TimeProvider.System, TestContext.Current.CancellationToken);

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
        var request = new CaptureConsentRequest(
            "SUBJ-1", "STUDY-1", "SITE-1", "Signer", null, "Subject", "I agree", null, "v1.0", "digest123", "sigdata123", null);

        var result = await EconsentEndpoints.ProcessCaptureConsentAsync(
            "tpl-001", 1, request, context, Caller("STUDY-2"), TimeProvider.System, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await context.SubjectConsents.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        (await context.ConsentSignatures.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    /// <summary>Publishing a template of a study the caller is not assigned to is refused and announces nothing (#5361).</summary>
    [Fact]
    public async Task ProcessPublishTemplateAsync_ForAStudyOutsideTheCallersStudies_IsRefusedAndPublishesNothing()
    {
        using var context = CreateContext();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var request = new PublishTemplateRequest("STUDY-101", "v2.0", "Change", "Amendment", true, null);

        var result = await EconsentEndpoints.ProcessPublishTemplateAsync(
            "tpl-outside", 1, request, context, publishEndpoint, Caller("STUDY-2"), TimeProvider.System, TestContext.Current.CancellationToken);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        (await context.ConsentTemplates.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        await publishEndpoint.DidNotReceiveWithAnyArgs().Publish(default(EconsentReconsentRequired)!, TestContext.Current.CancellationToken);
    }

    private static DefaultHttpContext Caller(params string[] studies)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[GatewayAuthenticationHandler.PrincipalItemKey] = new Principal
        {
            UserId = "crc-1",
            Roles = ["crc"],
            AssignedStudies = studies.Length == 0 ? ["*"] : studies,
        };
        return httpContext;
    }

    private EconsentDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<EconsentDbContext>().UseSqlite(_connection).Options, TestFieldProtection.CreateEncryptor());
}
