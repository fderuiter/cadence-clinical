using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Apps.Execution.Infrastructure.Normalization;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;
using Cadence.TestSupport.Execution.Fakes;
using Cadence.UnitTests.Fixtures.Execution;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Batch PI sign-off in the application service (#5311). The endpoint filter verifies and consumes the step-up
/// token; the service signs only with that verified token, for its own signer and batch, and keys every manifest
/// with the gateway secret from the validated options. The HTTP path, replay and cross-instance single use run on
/// PostgreSQL in <c>Cadence.IntegrationTests.Execution.BatchSignOffSignatureTests</c>.
/// </summary>
public sealed class BatchSignOffEndpointsTests
{
    private const string SigningReason = "Principal Investigator Approval";

    private static DefaultHttpContext CreateHttpContext(string role = "pi", string username = "dr_investigator", JwtSecurityToken? verifiedToken = null)
    {
        var context = new DefaultHttpContext { User = TestSecurityContext.CreatePrincipal(username, role) };
        if (verifiedToken is not null)
        {
            context.Items[ElectronicSignatureEndpointFilter.VerifiedTokenItemKey] = verifiedToken;
        }

        return context;
    }

    private static FormSubmissionApplicationService CreateService(InMemoryFormSubmissionRepository repo, string? secret = TestSecurityContext.GatewaySecret) =>
        new(
            formSubmissionRepository: repo,
            observationRepository: new InMemoryObservationRepository(),
            ucumService: new UcumNormalizationService(),
            auditContextAccessor: new FakeCurrentAuditContextAccessor(),
            gatewayAuthentication: TestSecurityContext.GatewayOptions(secret),
            clock: new FakeClock());

    private static JwtSecurityToken TokenFor(string userId, BatchSignOffRequest request) =>
        new JwtSecurityTokenHandler().ReadJwtToken(TestSecurityContext.CreateSigToken(
            userId, FormEndpoints.BatchSignOffAction, FormEndpoints.ApproveSemanticAction, batchId: request.BatchBindingId()));

    private static async Task<(string Id1, string Id2)> TwoCompletedSubjectsAsync(FormSubmissionApplicationService svc)
    {
        var sub1 = (await svc.CreateAsync(new FormSubmissionCreateRequest("STUDY-001", "SUBJ-101", "FORM-VS", "SITE-01", "VISIT-01", "1.0.0", "{\"vssbp\": 120, \"observation_date\": \"2026-10-06T10:00:00Z\"}"), CancellationToken.None)).Value!;
        var sub2 = (await svc.CreateAsync(new FormSubmissionCreateRequest("STUDY-001", "SUBJ-102", "FORM-VS", "SITE-01", "VISIT-01", "1.0.0", "{\"vssbp\": 118, \"observation_date\": \"2026-10-06T10:00:00Z\"}"), CancellationToken.None)).Value!;
        await svc.CompleteAsync(sub1.Id, CancellationToken.None);
        await svc.CompleteAsync(sub2.Id, CancellationToken.None);
        return (sub1.Id, sub2.Id);
    }

    private static BatchSignOffRequest SubjectBatch(params string[] subjects) => new("STUDY-001", "SUBJECT", subjects, SigningReason);

    /// <summary>A verified token for this signer and batch signs every completed form with a keyed HMAC.</summary>
    [Fact]
    [GxPRequirement("SRS-EDC-003")]
    public async Task BatchSignOff_WithVerifiedTokenForTheBatch_ApprovesWithKeyedManifests()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var svc = CreateService(repo);
        var (id1, id2) = await TwoCompletedSubjectsAsync(svc);
        var request = SubjectBatch("SUBJ-101", "SUBJ-102");

        var result = await FormEndpoints.BatchSignOffAsync(
            request, CreateHttpContext(verifiedToken: TokenFor("dr_investigator", request)), svc, CancellationToken.None);

        var response = result.Should().BeOfType<Ok<BatchSignOffResponse>>().Subject.Value!;
        response.ApprovedSubmissionIds.Should().BeEquivalentTo(id1, id2);
        response.SignedFormsCount.Should().Be(2);
        foreach (string id in (string[])[id1, id2])
        {
            var form = (await svc.GetByIdAsync(id, CancellationToken.None)).Value!;
            string expected = Cadence.Security.HmacSigner.ComputeSignature(
                FormSubmissionApplicationService.ComputePayloadHash(form.Payload!)!, TestSecurityContext.GatewaySecret);
            form.SignatureManifest!.HmacSignature.Should().Be(expected).And.NotBe("mock-sig");
        }
    }

    /// <summary>
    /// Without a verified token the service signs nothing, whatever reaches it. Before #5311 a request with no token
    /// skipped verification and was signed.
    /// </summary>
    [Fact]
    [GxPRequirement("SRS-SYS-005")]
    public async Task BatchSignOff_WithoutVerifiedToken_Returns401AndSignsNothing()
    {
        var repo = new InMemoryFormSubmissionRepository();
        var svc = CreateService(repo);
        var (id1, _) = await TwoCompletedSubjectsAsync(svc);

        var result = await FormEndpoints.BatchSignOffAsync(SubjectBatch("SUBJ-101", "SUBJ-102"), CreateHttpContext(), svc, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        (await svc.GetByIdAsync(id1, CancellationToken.None)).Value!.SignatureManifest.Should().BeNull();
    }

    /// <summary>A token for another batch, or another signer, signs nothing.</summary>
    [Theory]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData("another-batch")]
    [InlineData("another-signer")]
    public async Task BatchSignOff_WithTokenForSomethingElse_Returns401(string mismatch)
    {
        var repo = new InMemoryFormSubmissionRepository();
        var svc = CreateService(repo);
        await TwoCompletedSubjectsAsync(svc);
        var request = SubjectBatch("SUBJ-101", "SUBJ-102");
        var token = mismatch == "another-batch"
            ? TokenFor("dr_investigator", SubjectBatch("SUBJ-101"))
            : TokenFor("someone-else", request);

        var result = await FormEndpoints.BatchSignOffAsync(request, CreateHttpContext(verifiedToken: token), svc, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    /// <summary>
    /// With no gateway secret bound the service throws instead of writing a placeholder signature. Before #5311 it
    /// signed every manifest <c>"mock-sig"</c>.
    /// </summary>
    [Theory]
    [GxPRequirement("SRS-EDC-003")]
    [InlineData(null)]
    [InlineData("")]
    public async Task BatchSignOff_WithoutGatewaySecret_ThrowsAndSignsNothing(string? secret)
    {
        var repo = new InMemoryFormSubmissionRepository();
        var svc = CreateService(repo, secret);
        var (id1, _) = await TwoCompletedSubjectsAsync(svc);
        var request = SubjectBatch("SUBJ-101", "SUBJ-102");

        var act = () => FormEndpoints.BatchSignOffAsync(request, CreateHttpContext(verifiedToken: TokenFor("dr_investigator", request)), svc, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*GATEWAY_SECRET is not configured*");
        (await svc.GetByIdAsync(id1, CancellationToken.None)).Value!.SignatureManifest.Should().BeNull();
    }

    [Fact]
    public async Task BatchSignOff_NonPiRole_RejectsWithForbidden()
    {
        var svc = CreateService(new InMemoryFormSubmissionRepository());
        var request = SubjectBatch("SUBJ-101");

        var result = await FormEndpoints.BatchSignOffAsync(
            request, CreateHttpContext(role: "crc", verifiedToken: TokenFor("dr_investigator", request)), svc, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task BatchSignOff_Exceeds100SubjectsCap_ReturnsBadRequest()
    {
        var svc = CreateService(new InMemoryFormSubmissionRepository());
        var request = SubjectBatch(Enumerable.Range(0, 101).Select(i => $"SUBJ-{i:D3}").ToArray());

        var result = await FormEndpoints.BatchSignOffAsync(
            request, CreateHttpContext(verifiedToken: TokenFor("dr_investigator", request)), svc, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    /// <summary>The batch binding matches the web client's: trimmed, type upper-cased, ids sorted ordinally.</summary>
    [Fact]
    public void BatchBindingId_IsTheSha256OfTheNormalisedBinding()
    {
        var request = new BatchSignOffRequest(" STUDY-001 ", "subject", new List<string> { "B", " A" }, " Reason ");

        request.BatchBindingId().Should().Be(Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("STUDY-001:SUBJECT:A,B:Reason"))));
    }
}
