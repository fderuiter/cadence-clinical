using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Kernel.Trust;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;

using Xunit;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Batch PI sign-off on PostgreSQL through the real composition root (#5311). A batch is signed only with a fresh
/// e-signature step-up token bound to that batch, every manifest carries a real keyed signature, and a token is
/// single use across host instances because its use is recorded in the database, not in process memory.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BatchSignOffSignatureTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string BatchPath = "/api/v1/execution/batch-sign-off";
    private const string LegacyBatchPath = "/api/v1/execution/signatures/batch-sign-off";
    private const string FormsPath = "/api/v1/execution/forms/";
    private const string StudyId = "STUDY-BATCH";
    private const string SigningReason = "Principal Investigator Approval";
    private const string SemanticAction = "execution.form.approve";

    private static readonly GatewayIdentity Investigator =
        TestSecurityContext.Identity("pi-batch", "Site Investigator") with { ChangeReason = "Batch sign-off" };

    /// <summary>
    /// Without a step-up token the batch is refused and no form is signed. Before #5311 the service skipped
    /// verification when no token was sent and approved every form.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task BatchSignOff_WithoutSigToken_Returns401AndSignsNothing()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string formId = await CreateCompletedFormAsync(factory);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await investigator.PostAsJsonAsync(BatchPath, Body(formId), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(ct));
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain(SigTokenVerifier.ReauthenticationRequired);
        (await StatusOfAsync(factory, formId)).Should().Be("COMPLETED");
    }

    /// <summary>
    /// A token bound to this batch signs it, and each manifest carries the keyed HMAC of the form's canonical
    /// payload hash, never a placeholder such as <c>"mock-sig"</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-EDC-003")]
    public async Task BatchSignOff_WithTokenForTheBatch_SignsWithAKeyedSignature()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string formId = await CreateCompletedFormAsync(factory);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await SendAsync(investigator, BatchPath, TokenFor(formId), Body(formId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var form = JsonDocument.Parse(await investigator.GetStringAsync($"{FormsPath}{formId}", ct));
        form.RootElement.GetProperty("status").GetString().Should().Be("APPROVED");
        var manifest = form.RootElement.GetProperty("signature_manifest");
        manifest.GetProperty("signer_id").GetString().Should().Be(Investigator.UserId);
        string expected = Cadence.Security.HmacSigner.ComputeSignature(
            JsonCanonicalization.Sha256Hex(form.RootElement.GetProperty("payload").GetString()!), TestSecurityContext.GatewaySecret);
        manifest.GetProperty("hmac_signature").GetString().Should().Be(expected);
    }

    /// <summary>A token bound to another batch, or issued to someone else, signs nothing.</summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData("another-batch")]
    [InlineData("another-signer")]
    [InlineData("no-batch")]
    public async Task BatchSignOff_WithATokenIssuedForSomethingElse_Returns401(string mismatch)
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string formId = await CreateCompletedFormAsync(factory);
        using var investigator = factory.CreateSignedClient(Investigator);
        string token = mismatch switch
        {
            "another-batch" => TestSecurityContext.CreateSigToken(Investigator.UserId, BatchPath, SemanticAction, batchId: BatchId("some-other-form")),
            "another-signer" => TestSecurityContext.CreateSigToken("someone-else", BatchPath, SemanticAction, batchId: BatchId(formId)),
            _ => TestSecurityContext.CreateSigToken(Investigator.UserId, BatchPath, SemanticAction),
        };

        var response = await SendAsync(investigator, BatchPath, token, Body(formId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync(ct));
        (await StatusOfAsync(factory, formId)).Should().Be("COMPLETED");
    }

    /// <summary>
    /// A used token is refused on the next request, including through the legacy alias. Before #5311 the service
    /// built a new verifier, with an empty replay cache, for every request.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task BatchSignOff_ReplayingAUsedToken_Returns401()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string formId = await CreateCompletedFormAsync(factory);
        using var investigator = factory.CreateSignedClient(Investigator);
        string token = TokenFor(formId);

        var first = await SendAsync(investigator, BatchPath, token, Body(formId));
        var replay = await SendAsync(investigator, LegacyBatchPath, token, Body(formId));

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(ct));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await replay.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// Single use holds across host instances: a token used on one instance is refused by a second instance with
    /// its own process memory on the same database, which is also what a restarted host sees. This proves the
    /// use record lives in PostgreSQL; it does not by itself prove anything about a load balancer in front.
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task BatchSignOff_TokenUsedOnOneInstance_IsRefusedByAnother()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string formId = await CreateCompletedFormAsync(factory);
        string token = TokenFor(formId);
        await using var secondInstance = factory.WithWebHostBuilder(_ => { });
        using var onFirst = factory.CreateSignedClient(Investigator);
        using var onSecond = secondInstance.CreateSignedClient(Investigator);

        var first = await SendAsync(onFirst, BatchPath, token, Body(formId));
        var replay = await SendAsync(onSecond, BatchPath, token, Body(formId));

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(ct));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await replay.Content.ReadAsStringAsync(ct));
    }

    /// <summary>The same token sent concurrently to two instances is accepted exactly once.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task BatchSignOff_SameTokenRacedAcrossInstances_IsAcceptedOnce()
    {
        factory.SkipIfUnavailable();
        string formId = await CreateCompletedFormAsync(factory);
        string token = TokenFor(formId);
        await using var secondInstance = factory.WithWebHostBuilder(_ => { });
        using var onFirst = factory.CreateSignedClient(Investigator);
        using var onSecond = secondInstance.CreateSignedClient(Investigator);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => SendAsync(i % 2 == 0 ? onFirst : onSecond, BatchPath, token, Body(formId))));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(responses.Length - 1);
    }

    private static object Body(string formId) => new
    {
        study_id = StudyId,
        target_type = "FORM",
        target_ids = new[] { formId },
        signing_reason = SigningReason,
    };

    private static string TokenFor(string formId) =>
        TestSecurityContext.CreateSigToken(Investigator.UserId, BatchPath, SemanticAction, batchId: BatchId(formId));

    /// <summary>
    /// The batch binding the web client computes before re-authenticating: the lower-case hex SHA-256 of
    /// <c>study:TYPE:sorted ids:reason</c>.
    /// </summary>
    private static string BatchId(string formId) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{StudyId}:FORM:{formId}:{SigningReason}")));

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string sigToken, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(GatewayHeaders.SigToken, sigToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> CreateCompletedFormAsync(WebApplicationFactory<Cadence.Apps.Execution.Program> host)
    {
        var ct = TestContext.Current.CancellationToken;
        using var investigator = host.CreateSignedClient(Investigator);
        var created = await investigator.PostAsJsonAsync(
            FormsPath,
            new
            {
                study_id = StudyId,
                subject_id = $"SUBJ-{Guid.NewGuid():N}",
                form_id = "VS_FORM",
                payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}",
            },
            ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));
        string formId = body.RootElement.GetProperty("id").GetString()!;

        var completed = await investigator.PostAsync($"{FormsPath}{formId}/complete", content: null, ct);
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        return formId;
    }

    private static async Task<string?> StatusOfAsync(WebApplicationFactory<Cadence.Apps.Execution.Program> host, string formId)
    {
        using var client = host.CreateSignedClient(Investigator);
        using var form = JsonDocument.Parse(await client.GetStringAsync($"{FormsPath}{formId}", TestContext.Current.CancellationToken));
        return form.RootElement.GetProperty("status").GetString();
    }
}
