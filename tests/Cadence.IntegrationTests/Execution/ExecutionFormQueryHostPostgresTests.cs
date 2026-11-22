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
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
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
/// Execution's query endpoints through the real kernel host on PostgreSQL (#5461): a gateway-signed monitor raises a query
/// on the value a coordinator entered, the coordinator answers and corrects it, and the monitor sees that the value changed
/// and closes the query. Each role is held to its grants, so a coordinator cannot close a query and a stranger sees none.
/// Skipped unless <c>CADENCE_TEST_POSTGRES</c> is set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExecutionFormQueryHostPostgresTests(ExecutionFormModulePostgresFixture fixture)
    : IClassFixture<ExecutionFormModulePostgresFixture>, IDisposable
{
    private const string Crc = "crc@execution-queries.test";
    private const string CrcSiteB = "crc-b@execution-queries.test";
    private const string Monitor = "cra@execution-queries.test";
    private const string Stranger = "stranger@execution-queries.test";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowOutOfOrderMetadataProperties = true };

    private readonly KernelCollectionScenario _scenario = new(fixture);
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _host;

    public void Dispose() => _host?.Dispose();

    /// <summary>
    /// The monitor raises a query on revision 1 of the systolic value; the coordinator answers and then corrects the value
    /// to revision 2, so the query shows the value changed; the monitor closes it. The enrollment's list shows the same
    /// query, and a query on the stale revision is refused with its stable code.
    /// </summary>
    [Fact(DisplayName = "HostQuery_RaiseAnswerCorrectClose_ShowsTheValueChanged")]
    public async Task HostQueryRaiseAnswerCorrectCloseShowsTheValueChanged()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, _, enrollment, form, observation) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        var raised = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Source data verification", new
        {
            observation_id = observation,
            revision = 1,
            text = "Please confirm against the source",
        }, HttpStatusCode.Created, ct);
        raised.Should().Match<FormQueryResponse>(q =>
            q.Status == "open" && q.ItemId == Systolic && q.Revision == 1 && q.CurrentRevision == 1 && !q.ValueChanged && q.RaisedBy == Monitor);

        await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/answer", "Answering the query", new
        {
            text = "Transcription error; corrected to 128",
        }, HttpStatusCode.OK, ct);
        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", SaveReason, new
        {
            values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 128m, unit = "mm[Hg]" }, expected_revision = 1 } },
        }, HttpStatusCode.OK, ct);

        var listed = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        listed.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q =>
            q.Status == "answered" && q.Revision == 1 && q.CurrentRevision == 2 && q.ValueChanged);

        using (var stale = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries", "Source data verification", new
        {
            observation_id = observation,
            revision = 1,
            text = "Please confirm again",
        }), ct))
        {
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ProblemCodeAsync(stale, ct)).Should().Be("M.Execution.Query.RevisionStale");
        }

        var closed = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", "The correction settles it", null, HttpStatusCode.OK, ct);
        closed.Status.Should().Be("closed");
        closed.Messages.Select(m => m.Kind).Should().Equal("raised", "answered", "closed");
        closed.Messages[^1].Text.Should().Be("The correction settles it");

        var forEnrollment = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?enrollment_id={enrollment.Value:D}", null, null, HttpStatusCode.OK, ct);
        forEnrollment.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q => q.QueryId == raised.QueryId && q.Status == "closed" && q.ValueChanged);
    }

    /// <summary>
    /// Each role keeps to its grants: the coordinator may answer but neither raise nor close (403), and a caller with no
    /// grant at the study site can neither list the form's queries nor the enrollment's (403) nor answer one (403).
    /// </summary>
    [Fact(DisplayName = "HostQuery_RolesFailClosed")]
    public async Task HostQueryRolesFailClosed()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, _, enrollment, form, observation) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);
        using var stranger = Client(code, Stranger);
        var raised = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Source data verification", new
        {
            observation_id = observation,
            revision = 1,
            text = "Please confirm against the source",
        }, HttpStatusCode.Created, ct);

        await ExpectDeniedAsync(crc, HttpMethod.Post, $"/{form:D}/queries", new { observation_id = observation, revision = 1, text = "Raised by the site" }, ct);
        await ExpectDeniedAsync(crc, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", null, ct);
        await ExpectDeniedAsync(stranger, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/answer", new { text = "Not mine to answer" }, ct);
        foreach (var path in new[] { $"/{form:D}/queries", $"/queries?enrollment_id={enrollment.Value:D}" })
        {
            using var hidden = await stranger.SendAsync(Request(HttpMethod.Get, path, null, null), ct);
            hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        }

        var unchanged = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        unchanged.Should().ContainSingle().Which.Status.Should().Be("open");
    }

    /// <summary>
    /// Raising a missing-data query on a blank required item (<see cref="Diastolic"/>) records a <c>NotAsked</c> observation at
    /// revision 1 without marking the form complete; when the site coordinator subsequently records either a reasoned missing
    /// status (<c>NotDone</c>) or a numeric value, the form becomes complete and the query reports <c>value_changed = true</c>.
    /// </summary>
    [Fact(DisplayName = "HostQuery_BlankRequiredItem_KeepsFormIncompleteUntilCoordinatorRecordsValueOrReasonedMissing")]
    public async Task HostQueryBlankRequiredItemKeepsFormIncompleteUntilCoordinatorRecordsValueOrReasonedMissing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, _, _, form, _) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        var raised = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Missing diastolic BP on source worksheet", new
        {
            item_id = Diastolic,
            text = "Diastolic BP is blank; please enter the source value or why it was not done.",
        }, HttpStatusCode.Created, ct);

        raised.ItemId.Should().Be(Diastolic);
        raised.ObservationId.Should().NotBeEmpty();
        raised.Revision.Should().Be(1);
        raised.CurrentRevision.Should().Be(1);
        raised.ValueChanged.Should().BeFalse();

        // Form must remain incomplete because NotAsked is not a recorded value or reasoned missing status!
        var formAfterBlankQuery = await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct);
        formAfterBlankQuery.Complete.Should().BeFalse();
        formAfterBlankQuery.MissingRequired.Should().Equal(Diastolic);
        var diastolicItem = formAfterBlankQuery.Items.Single(i => i.ItemId == Diastolic);
        diastolicItem.ObservationId.Should().Be(raised.ObservationId);
        diastolicItem.Revision.Should().Be(1);
        diastolicItem.Value.Should().Be(new MissingValue(NullFlavor.NotAsked));

        // Coordinator records a reasoned missing status (NotDone) at expected_revision = 1 -> form becomes complete and value_changed = true!
        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", "Confirmed not measured at visit", new
        {
            values = new[]
            {
                new { item_id = Diastolic, value = new { type = "missing", reason = "NotDone" }, expected_revision = 1 },
            },
        }, HttpStatusCode.OK, ct);

        var formAfterMissing = await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct);
        formAfterMissing.Complete.Should().BeTrue();
        formAfterMissing.MissingRequired.Should().BeEmpty();

        var queriesAfterMissing = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        queriesAfterMissing.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q =>
            q.QueryId == raised.QueryId && q.Revision == 1 && q.CurrentRevision == 2 && q.ValueChanged);
    }

    /// <summary>
    /// <c>Frozen</c> blocks form value saves (<c>423</c>) while still allowing blank-item queries and query transitions;
    /// <c>Locked</c> refuses blank-item queries, recorded-item queries, and query transitions with <c>423 K.Assertion.Locked</c>.
    /// </summary>
    [Fact(DisplayName = "HostQuery_FrozenAllowsBlankAndTransitions_WhileLockedRefusesWith423")]
    public async Task HostQueryFrozenAllowsBlankAndTransitionsWhileLockedRefusesWith423()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, enrollment, form, observation) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        var study = (await fixture.ObservationRecords.FindAsync(tenant, ObservationId.FromGuid(observation), ct))!.StudyId;
        var frozen = await _scenario.Protocol.OkAsync(
            await _scenario.Observations.LockAsync(tenant, study, NodeRef.For(enrollment), ct, LockKind.Frozen), ct);

        // Frozen blocks value saves (423) ...
        using (var frozenSave = await crc.SendAsync(Request(HttpMethod.Put, $"/{form:D}/values", SaveReason, new
        {
            values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 126m, unit = "mm[Hg]" }, expected_revision = 1 } },
        }), ct))
        {
            frozenSave.StatusCode.Should().Be((HttpStatusCode)423);
        }

        // ... while allowing blank-item query raise and answer!
        var blankQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Interim freeze review", new
        {
            item_id = Diastolic,
            text = "Please explain missing diastolic BP",
        }, HttpStatusCode.Created, ct);
        var answered = await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/answer", "Answering under freeze", new
        {
            text = "Cuff malfunctioned; will update once unlocked",
        }, HttpStatusCode.OK, ct);
        answered.Status.Should().Be("answered");

        // Switch from Frozen to Locked: Locked refuses query transitions AND new queries with 423 K.Assertion.Locked!
        await _scenario.Protocol.OkAsync(await _scenario.Observations.ReleaseAsync(tenant, study, frozen.LockId, ct), ct);
        await _scenario.Protocol.OkAsync(await _scenario.Observations.LockAsync(tenant, study, NodeRef.For(enrollment), ct, LockKind.Locked), ct);

        using (var lockedClose = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/close", "Attempted close under lock", null), ct))
        {
            lockedClose.StatusCode.Should().Be((HttpStatusCode)423);
            (await ProblemCodeAsync(lockedClose, ct)).Should().Be(AssertionErrors.Locked.Code);
        }

        using (var lockedRaise = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries", "Attempted raise under lock", new
        {
            observation_id = observation,
            revision = 1,
            text = "Late query",
        }), ct))
        {
            lockedRaise.StatusCode.Should().Be((HttpStatusCode)423);
            (await ProblemCodeAsync(lockedRaise, ct)).Should().Be(AssertionErrors.Locked.Code);
        }

        using var lockedBlankRaise = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries", "Attempted blank raise under lock", new
        {
            item_id = Comment,
            text = "Late blank query",
        }), ct);
        lockedBlankRaise.StatusCode.Should().Be((HttpStatusCode)423);
        (await ProblemCodeAsync(lockedBlankRaise, ct)).Should().Be(AssertionErrors.Locked.Code);
    }

    /// <summary>
    /// Study-wide query worklist (<c>GET /api/v1/execution/forms/queries?study_id=…</c>) returns queries across enrollments the
    /// caller may see, enriches site/participant metadata, supports <c>status</c>, <c>site</c>, <c>participant</c>, and
    /// <c>value_changed</c> filters, masks out enrollments at other sites for site-scoped callers, and refuses strangers (403).
    /// </summary>
    [Fact(DisplayName = "HostQuery_StudyWorklist_FiltersAndEnforcesPerEnrollmentVisibility")]
    public async Task HostQueryStudyWorklistFiltersAndEnforcesPerEnrollmentVisibility()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var code = $"T-SQ-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Study query worklist tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct);

        StudySiteId siteA;
        StudySiteId siteB;
        await using (var db = fixture.CreateContext(tenant))
        {
            var sponsor = await db.Studies.AsNoTracking().Where(s => s.Id == study).Select(s => s.SponsorId).SingleAsync(ct);
            var sA = new Site(SiteId.New(), tenant, sponsor, "SITE-A", "Alpha Clinic", "US", KernelPostgresFixture.ActorId, "Seeded");
            var sB = new Site(SiteId.New(), tenant, sponsor, "SITE-B", "Beta Clinic", "US", KernelPostgresFixture.ActorId, "Seeded");
            var linkA = new StudySite(StudySiteId.New(), tenant, study, sA.Id, "Active", KernelPostgresFixture.ActorId, "Seeded");
            var linkB = new StudySite(StudySiteId.New(), tenant, study, sB.Id, "Active", KernelPostgresFixture.ActorId, "Seeded");
            await fixture.SeedAsync(sA, sB, linkA, linkB);
            siteA = linkA.Id;
            siteB = linkB.Id;
        }

        var partA = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        var partB = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented"), ct);
        SubjectCode.TryFrom("SUBJ-A1", out var codeA);
        SubjectCode.TryFrom("SUBJ-B1", out var codeB);
        var enrA = (await _scenario.Protocol.OkAsync(new ScreenParticipant(tenant, EnrollmentId.New(), partA, siteA, codeA, _scenario.Tick(), KernelProtocolScenario.Key(), "Screened"), ct)).EnrollmentId;
        var enrB = (await _scenario.Protocol.OkAsync(new ScreenParticipant(tenant, EnrollmentId.New(), partB, siteB, codeB, _scenario.Tick(), KernelProtocolScenario.Key(), "Screened"), ct)).EnrollmentId;

        // Grant CRC at Site A only, CrcSiteB at Site B only, and Monitor across the whole Study.
        foreach (var capability in new[] { "collection.record", "execution.query.answer" })
        {
            await _scenario.Protocol.OkAsync(new IssueGrant(tenant, GrantId.New(), Actor.Person(Crc), capability, null, AuthorityScope.StudySite(siteA), _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Site A CRC"), ct);
            await _scenario.Protocol.OkAsync(new IssueGrant(tenant, GrantId.New(), Actor.Person(CrcSiteB), capability, null, AuthorityScope.StudySite(siteB), _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Site B CRC"), ct);
        }

        foreach (var capability in new[] { "collection.record_unasked", "execution.query.raise", "execution.query.manage" })
        {
            await _scenario.Protocol.OkAsync(new IssueGrant(tenant, GrantId.New(), Actor.Person(Monitor), capability, null, AuthorityScope.Study(study), _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Study monitor"), ct);
        }

        var subjA = new KernelObservationScenario.Subject(tenant, study, siteA, enrA);
        var subjB = new KernelObservationScenario.Subject(tenant, study, siteB, enrB);
        var formA = (await _scenario.Protocol.OkAsync(Open(subjA, CollectionInstanceId.New(), _scenario.Tick()), ct)).CollectionInstanceId;
        var formB = (await _scenario.Protocol.OkAsync(Open(subjB, CollectionInstanceId.New(), _scenario.Tick()), ct)).CollectionInstanceId;
        var obsA = (await _scenario.Protocol.OkAsync(Save(new Form(subjA, version, formA), Value(Systolic, Mmhg(150))), ct)).Values.Single().ObservationId.Value;
        var obsB = (await _scenario.Protocol.OkAsync(Save(new Form(subjB, version, formB), Value(Systolic, Mmhg(118))), ct)).Values.Single().ObservationId.Value;

        using var monitor = Client(code, Monitor);
        using var crcA = Client(code, Crc);
        using var stranger = Client(code, Stranger);

        var qA = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{formA.Value:D}/queries", "Check Site A", new
        {
            observation_id = obsA,
            revision = 1,
            text = "Confirm elevated systolic BP",
        }, HttpStatusCode.Created, ct);

        var qB = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{formB.Value:D}/queries", "Check Site B", new
        {
            observation_id = obsB,
            revision = 1,
            text = "Confirm systolic BP",
        }, HttpStatusCode.Created, ct);

        // Site A CRC answers and corrects Site A's value -> qA becomes status=answered, value_changed=true.
        await SendAsync<FormQueryResponse>(crcA, HttpMethod.Post, $"/{formA.Value:D}/queries/{qA.QueryId:D}/answer", "Answered", new { text = "Updated to 132" }, HttpStatusCode.OK, ct);
        await SendAsync<JsonElement>(crcA, HttpMethod.Put, $"/{formA.Value:D}/values", SaveReason, new
        {
            values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 132m, unit = "mm[Hg]" }, expected_revision = 1 } },
        }, HttpStatusCode.OK, ct);

        // Study-wide monitor sees both queries with enriched metadata.
        var allForMonitor = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        allForMonitor.Select(q => (q.QueryId, q.SiteCode, q.SubjectCode, q.Status, q.ValueChanged))
            .Should().Equal(
                (qA.QueryId, "SITE-A", "SUBJ-A1", "answered", true),
                (qB.QueryId, "SITE-B", "SUBJ-B1", "open", false));

        // Server-side filters: status, site, participant, value_changed.
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&status=open", null, null, HttpStatusCode.OK, ct))
            .Select(q => q.QueryId).Should().Equal(qB.QueryId);
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&site=SITE-A", null, null, HttpStatusCode.OK, ct))
            .Select(q => q.QueryId).Should().Equal(qA.QueryId);
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&participant=SUBJ-B1", null, null, HttpStatusCode.OK, ct))
            .Select(q => q.QueryId).Should().Equal(qB.QueryId);
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&value_changed=true", null, null, HttpStatusCode.OK, ct))
            .Select(q => q.QueryId).Should().Equal(qA.QueryId);
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&value_changed=false", null, null, HttpStatusCode.OK, ct))
            .Select(q => q.QueryId).Should().Equal(qB.QueryId);

        // Site-scoped CRC sees ONLY Site A's enrollment queries — Site B is completely invisible!
        var visibleToSiteACrc = await SendAsync<FormQueryResponse[]>(crcA, HttpMethod.Get, $"/queries?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        visibleToSiteACrc.Select(q => q.QueryId).Should().Equal(qA.QueryId);

        // Stranger with no grant in the study gets 403 Forbidden.
        using var denied = await stranger.SendAsync(Request(HttpMethod.Get, $"/queries?study_id={study.Value:D}", null, null), ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task ExpectDeniedAsync(HttpClient client, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var denied = await client.SendAsync(Request(method, path, "Attempted step", body), ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
        (await ProblemCodeAsync(denied, ct)).Should().Be(KernelErrors.AuthorizationDenied.Code, path);
    }

    /// <summary>
    /// A tenant whose study has the vital signs form in force, one screened participant with a form whose systolic value is
    /// entered at revision 1, the coordinator granted to enter values and answer queries at the participant's study site,
    /// and the monitor granted to raise and manage them there.
    /// </summary>
    private async Task<(string Code, TenantId Tenant, EnrollmentId Enrollment, Guid Form, Guid Observation)> SeedAsync(CancellationToken ct)
    {
        var code = $"T-QRY-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Execution query host tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);
        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var version = await _scenario.EffectiveVersionAsync(tenant, study, ct);
        var studySite = await _scenario.Observations.AddStudySiteAsync(tenant, study, ct);
        var enrollment = await _scenario.Observations.ScreenAsync(tenant, studySite, ct);
        foreach (var (person, capabilities) in new[]
        {
            (Crc, new[] { "collection.record", "execution.query.answer" }),
            (Monitor, new[] { "collection.record_unasked", "execution.query.raise", "execution.query.manage" }),
        })
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.Person(person), capability, null, AuthorityScope.StudySite(studySite), _scenario.Tick(), null, false,
                        KernelProtocolScenario.Key(), "Appointed"),
                    ct);
            }
        }

        var subject = new KernelObservationScenario.Subject(tenant, study, studySite, enrollment);
        var opened = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        var value = (await _scenario.Protocol.OkAsync(Save(new Form(subject, version, opened.CollectionInstanceId), Value(Systolic, Mmhg(120))), ct)).Values.Single();
        return (code, tenant, enrollment, opened.CollectionInstanceId.Value, value.ObservationId.Value);
    }

    private HttpClient Client(string tenantCode, string userId)
    {
        _host ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)

            // Every error a query endpoint returns must be one its command declares (ADR-2600 §4.5).
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
