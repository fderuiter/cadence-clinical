using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Kernel.Trust;
using Cadence.Security;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Xunit;

using HmacSigner = Cadence.Security.Authentication.HmacSigner;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Signed callers acting on stored queries and forms through the real composition root on PostgreSQL: callers
/// without the needed role get 403, a PI signature records the authenticated caller rather than whatever the
/// request body claims, and an approval needs a fresh single-use e-signature token (#4990 moved these from the
/// unit tests, whose host ran on the EF Core in-memory provider). The pre-persistence checks (anonymous and
/// forged callers get 401) stay in <c>Cadence.UnitTests.Execution.Endpoints.ExecutionAuthenticationTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionWriteAuthorizationTests(ExecutionPostgresApiFactory factory) : IClassFixture<ExecutionPostgresApiFactory>
{
    private const string QueriesPath = "/api/v1/execution/queries/";
    private const string FormsPath = "/api/v1/execution/forms/";
    private const string SigningReason = "PI approval and sign-off.";

    private static readonly GatewayIdentity Cra = TestSecurityContext.Identity("cra-01", "CRA") with { ChangeReason = "Authorization check" };
    private static readonly GatewayIdentity Investigator = TestSecurityContext.Identity("pi-01", "Site Investigator") with { ChangeReason = "Authorization check" };

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task CloseQuery_AsSignedCallerWithoutCraOrDmRole_Returns403()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string queryId = await CreateQueryAsync();
        using var investigator = factory.CreateSignedClient(Investigator);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{QueriesPath}{queryId}/close")
        {
            Content = JsonContent.Create(new { resolver = "pi-01" }),
        };
        request.Headers.Add("X-User-Role", "CRA");
        var response = await investigator.SendAsync(request, ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task CloseQuery_AsSignedCra_Succeeds()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        string queryId = await CreateQueryAsync();
        using var investigator = factory.CreateSignedClient(Investigator);
        var answered = await investigator.PostAsJsonAsync($"{QueriesPath}{queryId}/respond", new { response = "Confirmed with source" }, ct);
        answered.StatusCode.Should().Be(HttpStatusCode.OK);
        using var cra = factory.CreateSignedClient(Cra);

        var response = await cra.PostAsJsonAsync($"{QueriesPath}{queryId}/close", new { resolver = "cra-01" }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_ByACra_Returns403()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        string formId = await CreateCompletedFormAsync(payload);
        using var cra = factory.CreateSignedClient(Cra);

        var response = await ApproveAsync(cra, "cra-01", formId, payload, signerId: "cra-01");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_NamingAnotherSigner_Returns403()
    {
        factory.SkipIfUnavailable();
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 121}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await ApproveAsync(investigator, "pi-01", formId, payload, signerId: "someone-else");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_AsSignedInvestigator_RecordsCallerAsSigner()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 122}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await ApproveAsync(investigator, "pi-01", formId, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var manifest = body.RootElement.GetProperty("signature_manifest");
        manifest.GetProperty("signer_id").GetString().Should().Be("pi-01");
        manifest.GetProperty("signer_role").GetString().Should().Be("investigator");
    }

    /// <summary>
    /// The server seals the manifest with its own key and clock (#5403). A body that still carries a signature or a
    /// signing time of its own has both ignored.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ApproveForm_SealsTheManifestOnTheServer_IgnoringAnyClientSignature()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 128}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        string hash = JsonCanonicalization.Sha256Hex(payload);
        var body = new
        {
            signing_reason = SigningReason,
            signature_manifest = new { payload_hash = hash, hmac_signature = "client-chosen", signed_at = "1999-01-01T00:00:00Z" },
        };

        var response = await SendApprovalAsync(investigator, $"{FormsPath}{formId}/approve", TokenFor("pi-01", formId, hash), body);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var approved = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var manifest = approved.RootElement.GetProperty("signature_manifest");
        manifest.GetProperty("hmac_signature").GetString()
            .Should().Be(Cadence.Security.HmacSigner.ComputeSignature(hash, TestSecurityContext.GatewaySecret));
        manifest.GetProperty("signed_at").GetDateTime().Year.Should().NotBe(1999);
    }

    /// <summary>
    /// A signer hashes the payload text it submitted. PostgreSQL returns that payload with its keys reordered, its
    /// spacing changed and <c>1.2e2</c> written as <c>120</c>, and the approval still verifies, because both sides hash
    /// the RFC 8785 canonical form (#5023).
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_WithTheHashOfTheSubmittedText_VerifiesAgainstTheStoredJsonb()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{ \"vssbp\" : 1.2e2,\n  \"observation_date\":\"2026-01-01T08:00:00Z\" }";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        using var stored = JsonDocument.Parse(await investigator.GetStringAsync($"{FormsPath}{formId}", ct));
        stored.RootElement.GetProperty("payload").GetString().Should().NotBe(payload, "the database rewrites jsonb text");

        var response = await ApproveAsync(investigator, "pi-01", formId, payload);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
    }

    /// <summary>A hash of other content is refused and the form stays unsigned.</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_WithTheHashOfOtherContent_Returns400InvalidPayloadHash()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 126}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await ApproveAsync(investigator, "pi-01", formId, "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 127}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain("INVALID_PAYLOAD_HASH");
        using var form = JsonDocument.Parse(await investigator.GetStringAsync($"{FormsPath}{formId}", ct));
        form.RootElement.GetProperty("status").GetString().Should().NotBe("APPROVED");
    }

    /// <summary>
    /// A PI approval needs a fresh e-signature step-up token; without one nothing is written.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ApproveForm_WithoutESignatureToken_Returns401AndLeavesTheFormUnsigned()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 123}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        string before = await investigator.GetStringAsync($"{FormsPath}{formId}", ct);

        var response = await investigator.PostAsJsonAsync($"{FormsPath}{formId}/approve", ApproveBody(payload), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain(SigTokenVerifier.ReauthenticationRequired);
        (await investigator.GetStringAsync($"{FormsPath}{formId}", ct)).Should().Be(before);
    }

    /// <summary>
    /// A token signs one form, one action, one version of its content, for one signer: it cannot approve form B,
    /// stand in for another action, sign other content, or be used by someone else (#5403).
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData("another-form")]
    [InlineData("another-action")]
    [InlineData("another-signer")]
    [InlineData("another-content")]
    [InlineData("no-content")]
    public async Task ApproveForm_WithATokenIssuedForSomethingElse_Returns401(string mismatch)
    {
        factory.SkipIfUnavailable();
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 124}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        string hash = JsonCanonicalization.Sha256Hex(payload);
        string token = mismatch switch
        {
            "another-form" => TokenFor("pi-01", "some-other-form", hash),
            "another-action" => TestSecurityContext.CreateSigToken("pi-01", FormEndpoints.ApproveAction(formId), "quality.capa.close", batchId: hash),
            "another-content" => TokenFor("pi-01", formId, JsonCanonicalization.Sha256Hex("{\"vssbp\": 999}")),
            "no-content" => TestSecurityContext.CreateSigToken("pi-01", FormEndpoints.ApproveAction(formId), FormEndpoints.ApproveSemanticAction),
            _ => TokenFor("cra-01", formId, hash),
        };
        string before = await investigator.GetStringAsync($"{FormsPath}{formId}", TestContext.Current.CancellationToken);

        var response = await SendApprovalAsync(investigator, $"{FormsPath}{formId}/approve", token, ApproveBody(payload));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await investigator.GetStringAsync($"{FormsPath}{formId}", TestContext.Current.CancellationToken)).Should().Be(before);
    }

    /// <summary>
    /// Each e-signature token is single use.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ApproveForm_ReplayingAUsedToken_Returns401()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 125}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        string token = TokenFor("pi-01", formId, JsonCanonicalization.Sha256Hex(payload));

        var first = await SendApprovalAsync(investigator, $"{FormsPath}{formId}/approve", token, ApproveBody(payload));
        var replay = await SendApprovalAsync(investigator, $"{FormsPath}{formId}/approve", token, ApproveBody(payload));

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(ct));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Signing has one route: the undocumented legacy alias no longer approves a form (#5403).</summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_OnTheLegacyAlias_IsNotARoute()
    {
        factory.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 129}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await SendApprovalAsync(
            investigator, $"/api/v1/forms/{formId}/approve", TokenFor("pi-01", formId, JsonCanonicalization.Sha256Hex(payload)), ApproveBody(payload));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        using var form = JsonDocument.Parse(await investigator.GetStringAsync($"{FormsPath}{formId}", ct));
        form.RootElement.GetProperty("status").GetString().Should().NotBe("APPROVED");
    }

    private async Task<string> CreateQueryAsync()
    {
        using var cra = factory.CreateSignedClient(Cra);
        var response = await cra.PostAsJsonAsync(
            QueriesPath,
            new { study_id = "STUDY-AUTH", subject_id = $"SUBJ-{Guid.NewGuid():N}", test_code = "SYSBP", explanation = "Value out of range" },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadIdAsync(response);
    }

    /// <summary>
    /// Creates and completes a form and returns its id. A signer hashes the payload it submitted: the hash is over the
    /// RFC 8785 canonical form, so PostgreSQL's <c>jsonb</c> rewriting of key order and spacing does not matter (#5023).
    /// </summary>
    private async Task<string> CreateCompletedFormAsync(string payload)
    {
        var ct = TestContext.Current.CancellationToken;
        using var investigator = factory.CreateSignedClient(Investigator);
        var created = await investigator.PostAsJsonAsync(
            FormsPath,
            new { study_id = "STUDY-AUTH", subject_id = $"SUBJ-{Guid.NewGuid():N}", form_id = "VS_FORM", payload },
            ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        string formId = await ReadIdAsync(created);

        var completed = await investigator.PostAsync($"{FormsPath}{formId}/complete", content: null, ct);
        completed.StatusCode.Should().Be(HttpStatusCode.OK);
        return formId;
    }

    /// <summary>Approves <paramref name="formId"/> as <paramref name="tokenUser"/>, attesting to <paramref name="payload"/>.</summary>
    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, string tokenUser, string formId, string payload, string signerId = "") =>
        SendApprovalAsync(
            client,
            $"{FormsPath}{formId}/approve",
            TokenFor(tokenUser, formId, JsonCanonicalization.Sha256Hex(payload)),
            ApproveBody(payload, signerId));

    /// <summary>A step-up token bound to one form's approval and one payload hash (#5403).</summary>
    private static string TokenFor(string userId, string formId, string payloadHash) =>
        TestSecurityContext.CreateSigToken(userId, FormEndpoints.ApproveAction(formId), FormEndpoints.ApproveSemanticAction, batchId: payloadHash);

    private static async Task<HttpResponseMessage> SendApprovalAsync(HttpClient client, string path, string sigToken, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(GatewayHeaders.SigToken, sigToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static object ApproveBody(string payload, string signerId = "") => new
    {
        signing_reason = SigningReason,
        signature_manifest = new
        {
            signer_id = signerId,
            signer_name = "Dr Test",
            payload_hash = JsonCanonicalization.Sha256Hex(payload),
        },
    };

    private static async Task<string> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
