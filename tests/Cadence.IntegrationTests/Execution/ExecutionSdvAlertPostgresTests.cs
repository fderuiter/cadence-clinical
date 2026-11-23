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
using Cadence.Kernel.Api;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// End-to-end PostgreSQL integration tests for CRA SDV modification alerts (<c>PRD-QRY-006</c>):
/// <see cref="AlertMonitorOnVerifiedObservationCorrected"/> consumes <see cref="ObservationCorrected"/> via the kernel outbox,
/// reads <see cref="IObservationRecords"/> (<c>K-XS-2</c>), records an idempotent <c>FormSdvAlert</c> with kernel
/// <see cref="StudyId"/> (<c>K-MOD-1</c>) only when the superseded revision carried an un-retracted <c>Verified</c> assertion,
/// exposes <c>study_id</c> and <c>enrollment_id</c> filters at <c>GET /api/v1/execution/forms/sdv-alerts</c> (<c>K-XS-1</c>),
/// and omits observations once a monitor verifies the latest revision.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionSdvAlertPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@sdv-alerts.test";
    private const string Monitor = "cra@sdv-alerts.test";
    private const string Stranger = "stranger@sdv-alerts.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    [Fact(DisplayName = "ObservationCorrected_AlertsMonitorOnlyWhenVerified_IsIdempotent_FiltersByStudyAndEnrollment_AndClearsOnReVerification")]
    [GxPRequirement("PRD-QRY-006")]
    public async Task ObservationCorrectedAlertsMonitorOnlyWhenVerifiedIsIdempotentFiltersByStudyAndEnrollmentAndClearsOnReVerification()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, study, enrollment, form, systolicObs, diastolicObs) = await SeedAsync(ct);
        await RelayOutboxAsync(tenant, ct);

        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);
        using var stranger = Client(code, Stranger);

        // 1. Editing an UNVERIFIED value (Systolic rev 1 -> rev 2) emits ObservationCorrected, which must NOT record an SDV alert.
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            "Corrected unverified entry",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 124m, unit = "mm[Hg]" }, expected_revision = 1 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await RelayOutboxAsync(tenant, ct);

        var alertsAfterUnverifiedEdit = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        alertsAfterUnverifiedEdit.Should().BeEmpty("correcting an unverified revision must not alert CRAs");

        // 2. Monitor verifies Systolic (rev 2) and Diastolic (rev 1).
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{systolicObs:D}/verifications",
            "Verified systolic BP against nursing flowsheet",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 2 },
            HttpStatusCode.Created,
            ct);
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{diastolicObs:D}/verifications",
            "Verified diastolic BP against nursing flowsheet",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 1 },
            HttpStatusCode.Created,
            ct);

        // 3. Coordinator modifies Systolic via eCRF PUT /values (rev 2 -> rev 3) AND corrects Diastolic via kernel POST /corrections (rev 1 -> rev 2).
        await SendAsync<FormSavedResponse>(
            crc,
            HttpMethod.Put,
            $"{FormEndpoints.RoutePrefix}/{form:D}/values",
            "Late source addendum on systolic BP",
            new
            {
                values = new object[]
                {
                    new { item_id = Systolic, value = new { type = "quantity", value = 132m, unit = "mm[Hg]" }, expected_revision = 2 },
                },
            },
            HttpStatusCode.OK,
            ct);
        await SendAsync<KernelObservationStateResponse>(
            crc,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{diastolicObs:D}/corrections",
            "Late source addendum on diastolic BP",
            new
            {
                enrollment_ref = $"enrollment:{enrollment.Value:D}",
                value = new { type = "quantity", value = 84m, unit = "mm[Hg]" },
                expected_version = 1,
            },
            HttpStatusCode.OK,
            ct);

        await RelayOutboxAsync(tenant, ct);

        // 4. Replaying outbox deliveries is idempotent: no duplicate FormSdvAlert rows are created.
        await fixture.ExecuteAdminAsync(
            $"UPDATE kernel.outbox_messages SET dispatched_at = NULL WHERE tenant_id = '{tenant.Value:D}'", ct);
        await RelayOutboxAsync(tenant, ct);

        var studyAlerts = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        studyAlerts.Should().HaveCount(2);

        var systolicAlert = studyAlerts.Single(a => a.ObservationId == systolicObs);
        systolicAlert.StudyId.Should().Be(study.Value);
        systolicAlert.EnrollmentId.Should().Be(enrollment.Value);
        systolicAlert.FormId.Should().Be(form);
        systolicAlert.ItemId.Should().Be(Systolic);
        systolicAlert.SupersededRevision.Should().Be(2);
        systolicAlert.CurrentRevision.Should().Be(3);
        systolicAlert.VerifiedBy.Should().Be(Monitor);
        systolicAlert.ModifiedBy.Should().Be(Crc);
        systolicAlert.Message.Should().Contain("Previously verified field modified on Subject")
            .And.Contain($"Visit {VisitId} ({Systolic})");

        var diastolicAlert = studyAlerts.Single(a => a.ObservationId == diastolicObs);
        diastolicAlert.ItemId.Should().Be(Diastolic);
        diastolicAlert.SupersededRevision.Should().Be(1);
        diastolicAlert.CurrentRevision.Should().Be(2);

        // Enrollment filter returns the same active alerts; missing filter returns 400; unauthorized caller gets 403.
        var enrollmentAlerts = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        enrollmentAlerts.Select(a => a.AlertId).Should().BeEquivalentTo(studyAlerts.Select(a => a.AlertId));

        using (var badFilter = await monitor.SendAsync(Request(HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts", null, null), ct))
        {
            badFilter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var denied = await stranger.SendAsync(Request(HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null), ct))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 5. Once the monitor re-verifies Systolic at revision 3, GET /sdv-alerts omits Systolic while keeping Diastolic active.
        await SendAsync<KernelAssertionStateResponse>(
            monitor,
            HttpMethod.Post,
            $"/api/v1/kernel/observations/observation:{systolicObs:D}/verifications",
            "Re-verified systolic BP revision 3 against addendum",
            new { enrollment_ref = $"enrollment:{enrollment.Value:D}", revision = 3 },
            HttpStatusCode.Created,
            ct);

        var remainingAlerts = await SendAsync<FormSdvAlertResponse[]>(
            monitor, HttpMethod.Get, $"{FormEndpoints.RoutePrefix}/sdv-alerts?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        remainingAlerts.Should().ContainSingle().Which.ObservationId.Should().Be(diastolicObs);
    }

    private async Task<(string Code, TenantId Tenant, StudyId Study, EnrollmentId Enrollment, Guid Form, Guid SystolicObs, Guid DiastolicObs)> SeedAsync(
        CancellationToken ct)
    {
        fixture.Clock.Set(SystemClock.Instance.UtcNow.AddHours(-2));
        var code = $"T-SDV-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution SDV alerts tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);

        foreach (var (actor, capabilities, scope) in new (Actor, string[], AuthorityScope)[]
        {
            (Actor.Person(Crc), ["collection.open", "collection.record", "observation.correct"], AuthorityScope.StudySite(studySite)),
            (Actor.Person(Monitor), ["observation.verify", "assertion.retract", "execution.query.raise", "execution.query.manage"], AuthorityScope.Study(study)),
            (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(AlertMonitorOnVerifiedObservationCorrected.Name), ["execution.query.manage"], AuthorityScope.Tenant()),
        })
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, capability, null, scope, _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Appointed"),
                    ct);
            }
        }

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var saved = await _scenario.Protocol.OkAsync(
            Save(new Form(subject, version, opened.CollectionInstanceId), Value(Systolic, Mmhg(120)), Value(Diastolic, Mmhg(80))), ct);
        var sbp = saved.Values.Single(v => v.ItemId == Systolic).ObservationId.Value;
        var dbp = saved.Values.Single(v => v.ItemId == Diastolic).ObservationId.Value;
        return (code, tenant, study, enrollment, opened.CollectionInstanceId.Value, sbp, dbp);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.AddDeclaredKernelErrorsCheck();
                services.RemoveAll<KernelBackgroundOptions>();
                services.AddSingleton(new KernelBackgroundOptions
                {
                    RelayInterval = TimeSpan.FromHours(1),
                    ActivityInterval = TimeSpan.FromHours(1),
                });
            }));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private async Task RelayOutboxAsync(TenantId tenant, CancellationToken ct)
    {
        var now = SystemClock.Instance.UtcNow;
        fixture.Clock.Set(now);
        for (var pass = 0; pass < 40; pass++)
        {
            await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
            await using var db = fixture.CreateAdminContext();
            var errors = await db.OutboxMessages
                .Where(m => m.TenantId == tenant && m.LastError != null)
                .Select(m => m.LastError!)
                .ToListAsync(ct);
            errors.Should().BeEmpty(string.Join(" | ", errors));

            var remaining = await db.OutboxMessages
                .AnyAsync(m => m.TenantId == tenant && m.DispatchedAt == null, ct);
            if (!remaining)
            {
                break;
            }

            await Task.Delay(25, ct);
        }
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
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
}
