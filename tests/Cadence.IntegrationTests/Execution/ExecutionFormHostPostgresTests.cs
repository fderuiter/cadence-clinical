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
using Cadence.Kernel.Evidence;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

using Xunit;

using static Cadence.IntegrationTests.Kernel.KernelCollectionScenario;

namespace Cadence.IntegrationTests.Execution;

/// <summary>
/// The eCRF form endpoints through the real kernel host on PostgreSQL (ADR-2601, ADR-2622): a gateway-signed coordinator
/// opens a form, saves pages of values and reads the form back in layout order with its completeness; a stale page is
/// refused whole, and a caller without a grant can neither read nor save. Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@execution-forms.test";
    private const string Stranger = "stranger@execution-forms.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowOutOfOrderMetadataProperties = true };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// The coordinator opens the week 12 vital signs form, which reads back empty and incomplete with its items in the
    /// specification's order; a page with the systolic value saves at revision 1 and leaves the diastolic value missing;
    /// a page edited from a stale revision is refused whole with its stable code; the next page completes the form.
    /// </summary>
    [Fact(DisplayName = "HostForm_OpensSavesPagesAndDerivesCompleteness_AndRefusesAStalePage")]
    public async Task HostFormOpensSavesPagesAndDerivesCompletenessAndRefusesAStalePage()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment, collectedAt) = await SeedAsync(ct);
        using var crc = Client(code, Crc);

        var opened = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Week 12 visit performed", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);
        opened.Version.Should().Be(1);

        var empty = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        empty.CollectionTitle.Should().Be("Vital signs");
        empty.Items.Select(i => (i.ItemId, i.Required, i.Revision)).Should().Equal((Systolic, true, 0), (Diastolic, true, 0), (Comment, false, 0));
        empty.Complete.Should().BeFalse();
        empty.MissingRequired.Should().Equal(Systolic, Diastolic);

        var saved = await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason,
            Page((Systolic, Quantity(128), 0)), HttpStatusCode.OK, ct);
        saved.Values.Should().ContainSingle().Which.Should().Match<FormSavedValueResponse>(v => v.ItemId == Systolic && v.Revision == 1 && v.Changed);

        var partial = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        partial.Items[0].Value.Should().Be(new QuantityValue(128m, "mm[Hg]"));
        partial.Items[0].RecordedBy.Should().Be(Crc);
        partial.MissingRequired.Should().Equal(Diastolic);

        using (var stale = await crc.SendAsync(Request(HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason, Page((Systolic, Quantity(130), 0), (Diastolic, Quantity(80), 0))), ct))
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(stale, ct)).Should().Be(CollectionErrors.RevisionStale.Code);
        }

        await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason,
            Page((Diastolic, Quantity(82), 0)), HttpStatusCode.OK, ct);
        var complete = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        complete.Complete.Should().BeTrue();
        complete.MissingRequired.Should().BeEmpty();
        complete.Items.Select(i => (i.ItemId, i.Revision)).Should().Equal((Systolic, 1), (Diastolic, 1), (Comment, 0));
        complete.Version.Should().Be(3);
    }

    /// <summary>A caller with no grant at the form's study site can neither read it (403) nor save to it (403, audited as denied).</summary>
    [Fact(DisplayName = "HostForm_WithoutAGrant_IsNotVisibleAndNotSaved")]
    public async Task HostFormWithoutAGrantIsNotVisibleAndNotSaved()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment, collectedAt) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var stranger = Client(code, Stranger);
        var opened = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Week 12 visit performed", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);

        using (var hidden = await stranger.SendAsync(Request(HttpMethod.Get, $"/{opened.FormId:D}", null, null), ct))
        {
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var denied = await stranger.SendAsync(Request(HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason, Page((Systolic, Quantity(128), 0))), ct))
        {
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ProblemCodeAsync(denied, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code);
        }

        var read = await SendAsync<FormResponse>(crc, HttpMethod.Get, $"/{opened.FormId:D}", null, null, HttpStatusCode.OK, ct);
        read.Items.Should().OnlyContain(i => i.Value == null);
    }

    /// <summary>
    /// Listing an enrollment's forms returns opened form summaries in opening order with completeness and recorded counts
    /// for a caller who may see the enrollment, refuses a missing enrollment_id with 400, and refuses an unauthorized
    /// caller with 403 without exposing whether forms exist.
    /// </summary>
    [Fact(DisplayName = "HostForm_ListForEnrollment_ReturnsAuthorizedSummaries_AndRefusesUnauthorizedCaller")]
    public async Task HostFormListForEnrollmentReturnsAuthorizedSummariesAndRefusesUnauthorizedCaller()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, enrollment, collectedAt) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var stranger = Client(code, Stranger);

        var initial = await SendAsync<FormSummaryResponse[]>(crc, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        initial.Should().BeEmpty();

        var opened = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Week 12 visit performed", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);

        var afterOpen = await SendAsync<FormSummaryResponse[]>(crc, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        afterOpen.Should().ContainSingle().Which.Should().Match<FormSummaryResponse>(s =>
            s.FormId == opened.FormId
            && s.EnrollmentId == enrollment.Value
            && s.CollectionSpecId == SpecId
            && s.CollectionTitle == "Vital signs"
            && s.PlannedActivityId == VisitId
            && !s.Complete
            && s.ItemCount == 3
            && s.RecordedCount == 0
            && s.Version == 1);

        await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{opened.FormId:D}/values", SaveReason,
            Page((Systolic, Quantity(124), 0), (Diastolic, Quantity(78), 0)), HttpStatusCode.OK, ct);

        var afterSave = await SendAsync<FormSummaryResponse[]>(crc, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        afterSave.Should().ContainSingle().Which.Should().Match<FormSummaryResponse>(s =>
            s.FormId == opened.FormId
            && s.Complete
            && s.ItemCount == 3
            && s.RecordedCount == 2
            && s.Version == 2);

        using (var missingQuery = await crc.SendAsync(Request(HttpMethod.Get, string.Empty, null, null), ct))
        {
            missingQuery.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using var hidden = await stranger.SendAsync(Request(HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null), ct);
        hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Casebook form summaries report missing-required counts (including when a blank item holds a <c>NotAsked</c>
    /// missing-data query), open and answered query counts, source verification counts, signature and superseded-signature
    /// status, and scoped freeze/lock coverage across scheduled, repeating, and unscheduled forms.
    /// </summary>
    [Fact(DisplayName = "HostForm_ListForEnrollment_ReportsCompletenessQueryVerificationLockAndSignatureSummaries")]
    public async Task HostFormListForEnrollmentReportsCompletenessQueryVerificationLockAndSignatureSummaries()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        const string monitorUser = "cra@execution-forms.test";
        const string investigatorUser = "pi@execution-forms.test";

        var code = $"T-CASE-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution casebook summary tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        var collectedAt = _scenario.Tick();

        foreach (var capability in new[] { "collection.open", "collection.record", "execution.query.answer" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Coordinator appointed"),
                ct);
        }

        foreach (var capability in new[] { "execution.query.raise", "execution.query.manage", "observation.verify", "collection.record_unasked" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(monitorUser), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Monitor appointed"),
                ct);
        }

        await _scenario.Protocol.OkAsync(
            new IssueGrant(tenant, GrantId.New(), Actor.Person(investigatorUser), "collection.sign", null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                KernelProtocolScenario.Key(), "Investigator appointed"),
            ct);
        foreach (var (consumer, capabilities) in new (string Consumer, string[] Capabilities)[]
        {
            ("execution-forms", ["execution.form.open"]),
            ("execution-queries", ["observation.query", "observation.query.answer", "assertion.retract"]),
        })
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.ServiceAccount(consumer), capability, null, AuthorityScope.Tenant(), _scenario.Tick(), null, false,
                        KernelProtocolScenario.Key(), "Module consumer grant"),
                    ct);
            }
        }

        using var crc = Client(code, Crc);
        using var monitor = Client(code, monitorUser);
        using var investigator = Client(code, investigatorUser);

        var scheduled = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Scheduled visit #1", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = collectedAt,
        }, HttpStatusCode.Created, ct);

        var repeating = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Repeating measurement #2", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = VisitId,
            collected_at = collectedAt.AddMinutes(15),
        }, HttpStatusCode.Created, ct);

        var unscheduled = await SendAsync<FormStateResponse>(crc, HttpMethod.Post, string.Empty, "Unscheduled vital check", new
        {
            enrollment_id = enrollment.Value,
            collection_spec_id = SpecId,
            planned_activity_id = (string?)null,
            collected_at = collectedAt.AddHours(1),
        }, HttpStatusCode.Created, ct);

        // Scheduled form #1: save both required items, verify one, sign while complete, then raise + answer query on one and raise open query on the other.
        var savedScheduled = await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{scheduled.FormId:D}/values", SaveReason,
            Page((Systolic, Quantity(122), 0), (Diastolic, Quantity(80), 0)), HttpStatusCode.OK, ct);
        var systolicObs = ObservationId.FromGuid(savedScheduled.Values.Single(v => v.ItemId == Systolic).ObservationId);
        var diastolicObs = ObservationId.FromGuid(savedScheduled.Values.Single(v => v.ItemId == Diastolic).ObservationId);

        await _scenario.Protocol.OkAsync(
            new VerifyObservation(tenant, AssertionId.New(), enrollment, systolicObs, 1, KernelProtocolScenario.Key(), "Verified against chart"),
            ct);

        var detailBeforeSign = await SendAsync<FormResponse>(investigator, HttpMethod.Get, $"/{scheduled.FormId:D}", null, null, HttpStatusCode.OK, ct);
        using (var signReq = Request(HttpMethod.Post, $"/{scheduled.FormId:D}/signatures", "I approve these values", new
        {
            meaning = "approved",
            content_hash = detailBeforeSign.ContentHash,
        }))
        {
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(investigatorUser, FormSignatureEndpoints.SignAction(scheduled.FormId), FormSignatureEndpoints.SignSemanticAction));
            using var signRes = await investigator.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

        var answeredQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{scheduled.FormId:D}/queries", "Check cuff size", new
        {
            observation_id = systolicObs.Value,
            revision = 1,
            text = "Confirm sitting posture",
        }, HttpStatusCode.Created, ct);
        await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{scheduled.FormId:D}/queries/{answeredQuery.QueryId:D}/answer", "Answered query", new
        {
            text = "Sitting after 5 minutes rest",
        }, HttpStatusCode.OK, ct);

        var openQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{scheduled.FormId:D}/queries", "Confirm diastolic", new
        {
            observation_id = diastolicObs.Value,
            revision = 1,
            text = "Verify against source printout",
        }, HttpStatusCode.Created, ct);

        // Repeating form #2: save only Systolic and raise a missing-data query on blank Diastolic (NotAsked).
        await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{repeating.FormId:D}/values", SaveReason,
            Page((Systolic, Quantity(130), 0)), HttpStatusCode.OK, ct);
        await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{repeating.FormId:D}/queries", "Missing diastolic", new
        {
            item_id = Diastolic,
            text = "Diastolic value missing on repeat check",
        }, HttpStatusCode.Created, ct);

        var summariesBeforeCorrection = await SendAsync<FormSummaryResponse[]>(monitor, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        summariesBeforeCorrection.Should().HaveCount(3);

        var s1 = summariesBeforeCorrection.Single(s => s.FormId == scheduled.FormId);
        s1.Complete.Should().BeFalse("active queries block form completeness until answered and closed");
        s1.MissingRequiredCount.Should().Be(0);
        s1.RecordedCount.Should().Be(2);
        s1.OpenQueryCount.Should().Be(1);
        s1.AnsweredQueryCount.Should().Be(1);
        s1.VerifiedCount.Should().Be(1);
        s1.UnverifiedCount.Should().Be(1);
        s1.Signed.Should().BeTrue();
        s1.SignatureSuperseded.Should().BeFalse();
        s1.SignatureCount.Should().Be(1);
        s1.Locked.Should().BeFalse();
        s1.Frozen.Should().BeFalse();

        var s2 = summariesBeforeCorrection.Single(s => s.FormId == repeating.FormId);
        s2.Complete.Should().BeFalse();
        s2.MissingRequiredCount.Should().Be(1);
        s2.RecordedCount.Should().Be(1, "a NotAsked placeholder from a missing-data query is not counted as recorded");
        s2.OpenQueryCount.Should().Be(1);

        var s3 = summariesBeforeCorrection.Single(s => s.FormId == unscheduled.FormId);
        s3.PlannedActivityId.Should().BeNull();
        s3.Complete.Should().BeFalse();
        s3.MissingRequiredCount.Should().Be(2);
        s3.RecordedCount.Should().Be(0);

        // Resolve queries and correct diastolic on scheduled form #1 (supersedes signature), then freeze scheduled form #1.
        await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{scheduled.FormId:D}/queries/{openQuery.QueryId:D}/answer", "Answered diastolic query", new
        {
            text = "Printout reads 82 mmHg",
        }, HttpStatusCode.OK, ct);
        await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{scheduled.FormId:D}/queries/{answeredQuery.QueryId:D}/close", "Posture confirmed", null, HttpStatusCode.OK, ct);
        await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{scheduled.FormId:D}/queries/{openQuery.QueryId:D}/close", "Diastolic updated", null, HttpStatusCode.OK, ct);
        var savedClock = fixture.Clock.UtcNow;
        try
        {
            fixture.Clock.Set(Cadence.Kernel.Time.SystemClock.Instance.UtcNow);
            var relayed = await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
            relayed.Failed.Should().Be(0);
        }
        finally
        {
            fixture.Clock.Set(savedClock);
        }

        await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{scheduled.FormId:D}/values", SaveReason,
            Page((Diastolic, Quantity(82), 1)), HttpStatusCode.OK, ct);
        await _scenario.Protocol.OkAsync(
            await _scenario.Observations.LockAsync(tenant, study, Cadence.Kernel.References.NodeRef.For(CollectionInstanceId.FromGuid(scheduled.FormId)), ct, LockKind.Frozen),
            ct);

        var summariesAfterFreeze = await SendAsync<FormSummaryResponse[]>(monitor, HttpMethod.Get, $"?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        var s1After = summariesAfterFreeze.Single(s => s.FormId == scheduled.FormId);
        s1After.Signed.Should().BeFalse();
        s1After.SignatureSuperseded.Should().BeTrue();
        s1After.SignatureCount.Should().Be(1);
        s1After.Frozen.Should().BeTrue();
        s1After.Locked.Should().BeFalse();
        s1After.LockCount.Should().Be(1);
    }

    private static object Page(params (string Item, object Value, int ExpectedRevision)[] values) =>
        new { values = values.Select(v => new { item_id = v.Item, value = v.Value, expected_revision = v.ExpectedRevision }).ToArray() };

    // A typed value as a client sends it (ADR-2623).
    private static object Quantity(decimal value) => new { type = "quantity", value, unit = "mm[Hg]" };

    /// <summary>
    /// A tenant whose study has the vital signs form in force, one screened participant, and the coordinator granted to
    /// open and save forms at the participant's study site.
    /// </summary>
    private async Task<(string Code, EnrollmentId Enrollment, DateTimeOffset CollectedAt)> SeedAsync(CancellationToken ct)
    {
        var code = $"T-FORM-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution form host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        foreach (var capability in new[] { "collection.open", "collection.record" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Appointed"),
                ct);
        }

        return (code, enrollment, _scenario.Tick());
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)

            // Every error a form endpoint returns must be one its command declares (ADR-2600 §4.5).
            .ConfigureTestServices(services => services.AddDeclaredKernelErrorsCheck()));
        return _host.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client, HttpMethod method, string path, string? reason, object? body, HttpStatusCode expected, CancellationToken ct)
    {
        using var response = await client.SendAsync(Request(method, path, reason, body), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? reason, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri($"{FormEndpoints.RoutePrefix}{path}", UriKind.Relative));
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
