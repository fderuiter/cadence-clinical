using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// Form signatures through the real kernel host on PostgreSQL (#5463): an investigator signs the exact revisions of a
/// form's values with a gateway step-up token, a correction leaves the signature as it was and shows it superseded beside
/// the corrected value, and a second signature then signs the new revisions. Every unusable token is refused before
/// anything is written. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormSignatureHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@execution-signatures.test";
    private const string Investigator = "pi@execution-signatures.test";
    private const string SignReason = "I approve the data on this form";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowOutOfOrderMetadataProperties = true };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// The investigator signs revision 1 of the systolic value; the coordinator corrects it to revision 2, which leaves the
    /// signature bound to revision 1 and shows it superseded; signing again with the hash read before the correction is
    /// refused with 409; signing the current hash records a second signature. The kernel's audit chain holds both.
    /// </summary>
    [Fact(DisplayName = "HostSignature_BindsTheRevisions_IsSupersededByACorrection_AndIsSignedAgain")]
    [GxPRequirement("SRS-PRD-EDC-003")]
    public async Task HostSignatureBindsTheRevisionsIsSupersededByACorrectionAndIsSignedAgain()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (tenant, code, form) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var investigator = Client(code, Investigator);

        var unsigned = await ReadAsync(investigator, form, ct);
        unsigned.Signatures.Should().BeEmpty();
        var first = await SignAsync(investigator, form, Token(Investigator, form), "approved", unsigned.ContentHash!, HttpStatusCode.Created, ct);
        first.Should().Match<FormSignatureResponse>(s => s.Meaning == "Approved" && s.SignedBy == Investigator && !s.Superseded && s.ContentHash == unsigned.ContentHash);
        first.SignedValues.Should().ContainSingle().Which.Should().Match<FormSignedValueResponse>(v => v.ItemId == Systolic && v.Revision == 1);

        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", SaveReason, null, new
        {
            values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 128m, unit = "mm[Hg]" }, expected_revision = 1 } },
        }, HttpStatusCode.OK, ct);

        var corrected = await ReadAsync(investigator, form, ct);
        corrected.ContentHash.Should().NotBe(unsigned.ContentHash, "a correction is a new revision");
        corrected.Items.Single(i => i.ItemId == Systolic).Revision.Should().Be(2);
        var superseded = corrected.Signatures.Should().ContainSingle().Subject;
        superseded.Should().Match<FormSignatureResponse>(s => s.AttestationId == first.AttestationId && s.Superseded && s.ContentHash == unsigned.ContentHash);
        superseded.SignedValues.Should().ContainSingle().Which.Revision.Should().Be(1, "the signature still names the revision it signed");

        using (var stale = await investigator.SendAsync(SignRequest(form, Token(Investigator, form), "approved", unsigned.ContentHash!), ct))
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(stale, ct)).Should().Be(TrustErrors.TargetContentChanged.Code);
        }

        var second = await SignAsync(investigator, form, Token(Investigator, form), "approved", corrected.ContentHash!, HttpStatusCode.Created, ct);
        second.SignedValues.Should().ContainSingle().Which.Revision.Should().Be(2);
        var resigned = await ReadAsync(investigator, form, ct);
        resigned.Signatures.Select(s => (s.AttestationId, s.Superseded)).Should().Equal((first.AttestationId, true), (second.AttestationId, false));

        await using var db = fixture.CreateAdminContext();
        var signedIds = new[] { first.AttestationId.ToString(), second.AttestationId.ToString() };
        (await db.AuditEntries.AsNoTracking()
                .Where(a => a.TenantId == tenant && a.AggregateType == "Attestation" && signedIds.Contains(a.AggregateId!))
                .Select(a => a.ActorId)
                .ToListAsync(ct))
            .Should().Equal([Investigator, Investigator], "both signatures are on the kernel's audit chain, by their signer");
    }

    /// <summary>
    /// The route refuses, with 401 and nothing recorded, a missing token, an expired one, one bound to another form or to
    /// another action, one issued to someone else and one already spent; a coordinator with a valid token but no
    /// <c>collection.sign</c> grant gets 403, and a meaning other than Approved or Responsibility gets 422.
    /// </summary>
    [Fact(DisplayName = "HostSignature_RefusesEveryUnusableStepUpToken_AndCallersWithoutTheGrant")]
    [GxPRequirement("SRS-SYS-005")]
    public async Task HostSignatureRefusesEveryUnusableStepUpTokenAndCallersWithoutTheGrant()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (_, code, form) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var investigator = Client(code, Investigator);
        var hash = (await ReadAsync(investigator, form, ct)).ContentHash!;

        var unusable = new (string Case, string? Token)[]
        {
            ("missing", null),
            ("expired", TestSecurityContext.CreateSigToken(
                Investigator, FormSignatureEndpoints.SignAction(form), FormSignatureEndpoints.SignSemanticAction, issuedAt: SystemClock.Instance.UtcNow.AddMinutes(-3))),
            ("another form", Token(Investigator, Guid.NewGuid())),
            ("another action", TestSecurityContext.CreateSigToken(Investigator, FormSignatureEndpoints.SignAction(form), "execution.form.approve")),
            ("someone else's", Token(Crc, form)),
        };
        foreach (var (name, token) in unusable)
        {
            using var refused = await investigator.SendAsync(SignRequest(form, token, "approved", hash), ct);
            refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized, name);
        }

        var spent = Token(Investigator, form);
        await SignAsync(investigator, form, spent, "responsibility", hash, HttpStatusCode.Created, ct);
        using (var replay = await investigator.SendAsync(SignRequest(form, spent, "responsibility", hash), ct))
        {
            replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a step-up token signs once");
        }

        using (var ungranted = await crc.SendAsync(SignRequest(form, Token(Crc, form), "approved", hash), ct))
        {
            ungranted.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(ungranted, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        using (var reviewed = await investigator.SendAsync(SignRequest(form, Token(Investigator, form), "reviewed", hash), ct))
        {
            reviewed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(reviewed, ct)).Should().Be(CollectionErrors.SignatureMeaningInvalid.Code);
        }

        (await ReadAsync(investigator, form, ct)).Signatures.Should().ContainSingle().Which.Meaning.Should().Be("Responsibility");
    }

    /// <summary>
    /// A tenant whose study has the vital signs form in force, one screened participant with a form whose systolic value is
    /// entered at revision 1, the coordinator granted to enter values and the investigator granted to sign at the
    /// participant's study site.
    /// </summary>
    private async Task<(TenantId Tenant, string Code, Guid Form)> SeedAsync(CancellationToken ct)
    {
        var code = $"T-SIG-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution signature host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        foreach (var (person, capability) in new[] { (Crc, "collection.record"), (Investigator, "collection.sign") })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(person), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Appointed"),
                ct);
        }

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        await _scenario.Protocol.OkAsync(Save(new Form(subject, version, opened.CollectionInstanceId), Value(Systolic, Mmhg(120))), ct);
        return (tenant, code, opened.CollectionInstanceId.Value);
    }

    private static string Token(string signer, Guid form) =>
        TestSecurityContext.CreateSigToken(signer, FormSignatureEndpoints.SignAction(form), FormSignatureEndpoints.SignSemanticAction);

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)

            // Every error the signature route returns must be one its command declares (ADR-2600 §4.5).
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static Task<FormResponse> ReadAsync(HttpClient client, Guid form, CancellationToken ct) =>
        SendAsync<FormResponse>(client, HttpMethod.Get, $"/{form:D}", null, null, null, HttpStatusCode.OK, ct);

    private static async Task<FormSignatureResponse> SignAsync(
        HttpClient client, Guid form, string token, string meaning, string hash, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(SignRequest(form, token, meaning, hash), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<FormSignatureResponse>(text, Json)!;
    }

    private static HttpRequestMessage SignRequest(Guid form, string? token, string meaning, string hash) =>
        Request(HttpMethod.Post, $"/{form:D}/signatures", SignReason, token, new { meaning, content_hash = hash });

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, string? token, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, token, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, string? token, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{path}", UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (token is not null)
        {
            request.Headers.Add(GatewayHeaders.SigToken, token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}
