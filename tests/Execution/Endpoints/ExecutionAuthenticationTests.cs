using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Application.Forms;
using Cadence.Apps.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Hosting;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

using Xunit;

namespace Cadence.UnitTests.Execution.Endpoints;

/// <summary>
/// Boots the real execution composition root and checks that it only acts on identity the gateway signed:
/// anonymous and forged callers get 401, callers without the needed role get 403, and a PI signature always
/// records the authenticated caller rather than whatever the request body claims.
/// </summary>
public sealed class ExecutionAuthenticationTests(ExecutionApiFactory factory) : IClassFixture<ExecutionApiFactory>
{
    private const string QueriesPath = "/api/v1/execution/queries/";
    private const string FormsPath = "/api/v1/execution/forms/";
    private const string SigningReason = "PI approval and sign-off.";

    private static readonly GatewayIdentity Cra = TestSecurityContext.Identity("cra-01", "CRA");
    private static readonly GatewayIdentity Investigator = TestSecurityContext.Identity("pi-01", "Site Investigator");

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task Healthz_WithoutCredentials_IsAnonymous()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithoutGatewaySignature_Returns401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, QueriesPath);
        request.Headers.Add(GatewayHeaders.UserId, "cra-01");
        request.Headers.Add(GatewayHeaders.Roles, "CRA");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_SignedWithWrongSecret_Returns401()
    {
        using var client = factory.CreateClient();
        var forged = TestSecurityContext.CreateHmacHeaders(Cra, secret: "not-the-gateway-secret-0123456789abcdef");

        var response = await SendWithHeadersAsync(client, forged);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithRolesAddedAfterSigning_Returns401()
    {
        using var client = factory.CreateClient();
        var headers = new System.Collections.Generic.Dictionary<string, string>(TestSecurityContext.CreateHmacHeaders(Cra))
        {
            [GatewayHeaders.Roles] = "CRA,Data Manager",
        };

        var response = await SendWithHeadersAsync(client, headers);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithStaleSignature_Returns401()
    {
        using var client = factory.CreateClient();
        var stale = TestSecurityContext.CreateHmacHeaders(Cra, DateTimeOffset.UtcNow.AddMinutes(-10));

        var response = await SendWithHeadersAsync(client, stale);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApiRequest_WithValidSignature_IsAuthenticated()
    {
        using var client = factory.CreateSignedClient(Cra);

        var response = await client.GetAsync(QueriesPath, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task CloseQuery_AsSignedCallerWithoutCraOrDmRole_Returns403()
    {
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
    public async Task ApproveForm_WithInvestigatorRoleOnlyInBody_Returns403()
    {
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 120}";
        string formId = await CreateCompletedFormAsync(payload);
        using var cra = factory.CreateSignedClient(Cra);

        var response = await ApproveAsync(cra, "cra-01", formId, ApproveBody(signerId: "cra-01", signerRole: "Site Investigator", payload));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_NamingAnotherSigner_Returns403()
    {
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 121}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await ApproveAsync(investigator, "pi-01", formId, ApproveBody(signerId: "someone-else", signerRole: "Site Investigator", payload));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("Category", "GxP_OQ")]
    public async Task ApproveForm_AsSignedInvestigator_RecordsCallerAsSigner()
    {
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 122}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);

        var response = await ApproveAsync(investigator, "pi-01", formId, ApproveBody(signerId: "", signerRole: "whatever the client says", payload));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(ct));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var manifest = body.RootElement.GetProperty("signature_manifest");
        manifest.GetProperty("signer_id").GetString().Should().Be("pi-01");
        manifest.GetProperty("signer_role").GetString().Should().Be("investigator");
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
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 123}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        string before = await investigator.GetStringAsync($"{FormsPath}{formId}", ct);

        var response = await investigator.PostAsJsonAsync($"{FormsPath}{formId}/approve", ApproveBody("", "", payload), ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(ct)).Should().Contain(SigTokenVerifier.ReauthenticationRequired);
        (await investigator.GetStringAsync($"{FormsPath}{formId}", ct)).Should().Be(before);
    }

    /// <summary>
    /// A token signs one form, one action, for one signer: it cannot approve form B, stand in for another
    /// action, or be used by someone else.
    /// @req:SRS-SYS-005
    /// </summary>
    [Theory]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    [InlineData("another-form")]
    [InlineData("another-action")]
    [InlineData("another-signer")]
    public async Task ApproveForm_WithATokenIssuedForSomethingElse_Returns401(string mismatch)
    {
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 124}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        string token = mismatch switch
        {
            "another-form" => TestSecurityContext.CreateSigToken("pi-01", FormEndpoints.ApproveAction("some-other-form"), FormEndpoints.ApproveSemanticAction),
            "another-action" => TestSecurityContext.CreateSigToken("pi-01", FormEndpoints.ApproveAction(formId), "quality.capa.close"),
            _ => TestSecurityContext.CreateSigToken("cra-01", FormEndpoints.ApproveAction(formId), FormEndpoints.ApproveSemanticAction),
        };

        var response = await SendApprovalAsync(investigator, $"{FormsPath}{formId}/approve", token, ApproveBody("", "", payload));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Each e-signature token is single use, including across the legacy route alias.
    /// @req:SRS-SYS-005
    /// </summary>
    [Fact]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task ApproveForm_ReplayingAUsedToken_Returns401()
    {
        var ct = TestContext.Current.CancellationToken;
        const string payload = "{\"observation_date\": \"2026-01-01T08:00:00Z\", \"vssbp\": 125}";
        string formId = await CreateCompletedFormAsync(payload);
        using var investigator = factory.CreateSignedClient(Investigator);
        string token = TestSecurityContext.CreateSigToken("pi-01", FormEndpoints.ApproveAction(formId), FormEndpoints.ApproveSemanticAction);

        var first = await SendApprovalAsync(investigator, $"/api/v1/forms/{formId}/approve", token, ApproveBody("", "", payload));
        var replay = await SendApprovalAsync(investigator, $"{FormsPath}{formId}/approve", token, ApproveBody("", "", payload));

        first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(ct));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("Category", "GxP_IQ")]
    public void HostStartup_WithoutGatewaySecret_FailsFast()
    {
        using var unconfigured = new WebApplicationFactory<Cadence.Apps.Execution.Program>()
            .WithWebHostBuilder(builder => builder
                .UseTestPostgresConnectionString()
                .UseTestDemographicsKey()
                .UseSetting("GATEWAY_SECRET", string.Empty));

        Action start = () => unconfigured.CreateClient();

        start.Should().Throw<OptionsValidationException>().WithMessage("*GATEWAY_SECRET*");
    }

    private static async Task<HttpResponseMessage> SendWithHeadersAsync(
        HttpClient client,
        System.Collections.Generic.IReadOnlyDictionary<string, string> headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, QueriesPath);
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
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

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient client, string signerId, string formId, object body) =>
        SendApprovalAsync(
            client,
            $"{FormsPath}{formId}/approve",
            TestSecurityContext.CreateSigToken(signerId, FormEndpoints.ApproveAction(formId), FormEndpoints.ApproveSemanticAction),
            body);

    private static async Task<HttpResponseMessage> SendApprovalAsync(HttpClient client, string path, string sigToken, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(GatewayHeaders.SigToken, sigToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static object ApproveBody(string signerId, string signerRole, string payload) => new
    {
        signing_reason = SigningReason,
        signature_manifest = new
        {
            signer_id = signerId,
            signer_name = "Dr Test",
            signer_role = signerRole,
            signing_reason = SigningReason,
            payload_hash = FormSubmissionApplicationService.ComputeSha256(payload),
            signed_at = DateTime.UtcNow,
        },
    };

    private static async Task<string> ReadIdAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return body.RootElement.GetProperty("id").GetString()!;
    }
}
