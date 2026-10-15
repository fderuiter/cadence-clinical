using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.IntegrationTests.Kernel;

/// <summary>
/// The eCRF correction through the real kernel host on PostgreSQL (ADR-2620): a gateway-signed caller, the host's
/// tenant directory and authority evaluator, the command pipeline, and the reason from the signed identity, read back
/// from the kernel audit chain. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class KernelObservationHostPostgresTests(KernelPostgresFixture fixture) : IClassFixture<KernelPostgresFixture>, IDisposable
{
    private const string Crc = "crc@observation-host.test";
    private const string Stranger = "stranger@observation-host.test";
    private const string CorrectionReason = "Transcription error – source reads 128";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly KernelProtocolScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// A coordinator granted record, correct and lock at their study site records a value, corrects it with a reason, and
    /// reads the correction back with the reason in the kernel audit trail. A stale version is 409, a missing reason 422,
    /// and after the lock a correction is 423. Each refusal carries its stable code.
    /// </summary>
    [Fact(DisplayName = "HostCorrection_RecordsCorrectsAndAuditsTheReason_AndRefusesStaleMissingReasonAndLocked")]
    [GxPRequirement("PRD-SYS-001")]
    public async Task HostCorrectionRecordsCorrectsAndAuditsTheReasonAndRefusesStaleMissingReasonAndLocked()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment) = await SeedAsync(grant: true, ct);
        using var crc = Client(code, Crc);

        var recorded = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, "observations", "Initial entry", new
        {
            enrollment_ref = NodeRef.For(enrollment).ToString(),
            concept = $"{ConceptId.New()}@1",
            value = "182",
            observed_at = SystemClock.Instance.UtcNow.AddHours(-1),
        }, HttpStatusCode.Created, ct);
        recorded.Version.Should().Be(1);
        var observation = NodeRef.Parse(recorded.Ref).Unversioned();

        var corrected = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, $"observations/{observation}/corrections", CorrectionReason,
            Correction(enrollment, "128", 1), HttpStatusCode.OK, ct);
        corrected.Value.Should().Be("128");
        corrected.Version.Should().Be(2);

        var read = await SendAsync<KernelObservationResponse>(crc, HttpMethod.Get, $"observations/{observation}", null, null, HttpStatusCode.OK, ct);
        read.Value.Should().Be("128");
        read.Revisions.Select(r => (r.Number, r.Value, r.ReasonForChange)).Should().Equal((1, "182", "Initial entry"), (2, "128", CorrectionReason));
        read.Audit.Should().Contain(a => a.Action == "Updated" && a.Version == 2 && a.ReasonForChange == CorrectionReason && a.ActorId == Crc);

        (await ProblemAsync(crc, $"observations/{observation}/corrections", CorrectionReason, Correction(enrollment, "129", 1), ct))
            .Should().Be((HttpStatusCode.Conflict, ObservationErrors.VersionStale.Code));
        (await ProblemAsync(crc, $"observations/{observation}/corrections", null, Correction(enrollment, "129", 2), ct))
            .Should().Be(((HttpStatusCode)422, KernelErrors.ReasonForChangeRequired.Code));

        await SendAsync<KernelObservationLockResponse>(crc, HttpMethod.Post, $"observations/{observation}/lock", "Data review complete",
            new { enrollment_ref = NodeRef.For(enrollment).ToString() }, HttpStatusCode.OK, ct);
        (await ProblemAsync(crc, $"observations/{observation}/corrections", CorrectionReason, Correction(enrollment, "129", 2), ct))
            .Should().Be(((HttpStatusCode)423, ObservationErrors.Locked.Code));
    }

    /// <summary>
    /// A caller with no grant at the observation's study site can neither correct it (403, the denial audited) nor read
    /// it (403); the value is unchanged.
    /// </summary>
    [Fact(DisplayName = "HostCorrection_WithoutAGrant_IsDeniedAndNotVisible")]
    public async Task HostCorrectionWithoutAGrantIsDeniedAndNotVisible()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment) = await SeedAsync(grant: true, ct);
        using var crc = Client(code, Crc);
        using var stranger = Client(code, Stranger);
        var recorded = await SendAsync<KernelObservationStateResponse>(crc, HttpMethod.Post, "observations", "Initial entry", new
        {
            enrollment_ref = NodeRef.For(enrollment).ToString(),
            concept = $"{ConceptId.New()}@1",
            value = "182",
            observed_at = SystemClock.Instance.UtcNow.AddHours(-1),
        }, HttpStatusCode.Created, ct);
        var observation = NodeRef.Parse(recorded.Ref).Unversioned();

        (await ProblemAsync(stranger, $"observations/{observation}/corrections", CorrectionReason, Correction(enrollment, "128", 1), ct))
            .Should().Be((HttpStatusCode.Forbidden, KernelErrors.AuthorizationDenied.Code));
        using (var hidden = await stranger.GetAsync(new Uri($"/api/v1/kernel/observations/{observation}", UriKind.Relative), ct))
        {
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var read = await SendAsync<KernelObservationResponse>(crc, HttpMethod.Get, $"observations/{observation}", null, null, HttpStatusCode.OK, ct);
        read.Value.Should().Be("182");
        read.Version.Should().Be(1);
    }

    private static object Correction(EnrollmentId enrollment, string value, long expectedVersion) =>
        new { enrollment_ref = NodeRef.For(enrollment).ToString(), value, expected_version = expectedVersion };

    /// <summary>A tenant with a study, a study site and one screened participant; the coordinator granted at the site.</summary>
    private async Task<(string Code, EnrollmentId Enrollment)> SeedAsync(bool grant, CancellationToken ct)
    {
        var code = $"T-OBS-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.OkAsync(new ProvisionTenant(tenant, code, "Observation host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.NewStudyAsync(tenant, ct);
        StudySiteId studySite;
        await using (var db = fixture.CreateContext(tenant))
        {
            var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
            var suffix = Guid.NewGuid().ToString("N")[..10];
            var site = new Site(SiteId.New(), tenant, sponsor, $"SITE-{suffix}", "Memorial Hospital", "US", KernelPostgresFixture.ActorId, "Seeded for test");
            var link = new StudySite(StudySiteId.New(), tenant, study, site.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded for test");
            await fixture.SeedAsync(site, link);
            studySite = link.Id;
        }

        var participant = await _scenario.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        SubjectCode.TryFrom("OBS-001", out var subject);
        var enrollment = await _scenario.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participant, studySite, subject, Tick(), KernelProtocolScenario.Key(), "Screening visit"), ct);
        if (grant)
        {
            foreach (var capability in new[] { "observation.record", "observation.correct", "observation.lock" })
            {
                await _scenario.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), capability, null, AuthorityScope.StudySite(studySite), Tick(), null, false,
                        KernelProtocolScenario.Key(), "Appointed"),
                    ct);
            }
        }

        Tick();
        return (code, enrollment.EnrollmentId);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode? expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected ?? HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<(HttpStatusCode Status, string? Code)> ProblemAsync(
        HttpClient client, string path, string? reason, object body, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(HttpMethod.Post, path, reason, body), ct);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return (response.StatusCode, problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"/api/v1/kernel/{path}", UriKind.Relative));
        request.Headers.Add(KernelObservationEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
        if (reason is not null)
        {
            request.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode(reason));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private DateTimeOffset Tick() => fixture.Clock.Advance(TimeSpan.FromSeconds(1));
}
