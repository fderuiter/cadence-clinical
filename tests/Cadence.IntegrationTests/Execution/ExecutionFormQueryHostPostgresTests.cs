using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.IntegrationTests.Kernel;
using Cadence.Kernel;
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Activity;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Kernel.Time;
using Cadence.Kernel.Trust;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Security.Authentication;
using Cadence.Testing;
using Cadence.Testing.Commands;
using Cadence.Testing.Security;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
    private WebApplicationFactory<Cadence.Kernel.Host.Program>? _sharedClockHost;

    public void Dispose()
    {
        _host?.Dispose();
        _sharedClockHost?.Dispose();
    }

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

        // Coordinator records a reasoned missing status (NotDone) at expected_revision = 1 -> missing_required clears and value_changed = true, while open query keeps Complete false until closed!
        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", "Confirmed not measured at visit", new
        {
            values = new[]
            {
                new { item_id = Diastolic, value = new { type = "missing", reason = "NotDone" }, expected_revision = 1 },
            },
        }, HttpStatusCode.OK, ct);

        var formAfterMissing = await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct);
        formAfterMissing.MissingRequired.Should().BeEmpty();
        formAfterMissing.Complete.Should().BeFalse("the open query blocks completeness until answered and closed");

        var queriesAfterMissing = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        queriesAfterMissing.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q =>
            q.QueryId == raised.QueryId && q.Revision == 1 && q.CurrentRevision == 2 && q.ValueChanged);

        await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/answer", "Confirmed not measured", new
        {
            text = "Cuff not available; marked NotDone",
        }, HttpStatusCode.OK, ct);
        await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", "Missingness documented", null, HttpStatusCode.OK, ct);
        (await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct)).Complete.Should().BeTrue();
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

        // Open a second complete form whose optional Comment remains blank so blank-item query refusal under Locked is also tested.
        var obsRecord = (await fixture.ObservationRecords.FindAsync(tenant, ObservationId.FromGuid(observation), ct))!;
        var study = obsRecord.StudyId;
        var formRecord = (await fixture.CollectionInstanceRecords.FindAsync(tenant, CollectionInstanceId.FromGuid(form), ct))!;
        var subject = new KernelObservationScenario.Subject(tenant, study, formRecord.StudySiteId, enrollment);
        var openedSecond = await _scenario.Protocol.OkAsync(Open(subject, CollectionInstanceId.New(), _scenario.Tick()), ct);
        await _scenario.Protocol.OkAsync(
            Save(new Form(subject, formRecord.ProtocolVersionId, openedSecond.CollectionInstanceId), Value(Systolic, Mmhg(118)), Value(Diastolic, Mmhg(76))),
            ct);
        var secondForm = openedSecond.CollectionInstanceId.Value;

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

        // ... while allowing blank-item query raise, answer, and closure!
        var blankQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Interim freeze review", new
        {
            item_id = Diastolic,
            text = "Please record diastolic blood pressure or document missingness",
        }, HttpStatusCode.Created, ct);
        var answered = await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/answer", "Answering under freeze", new
        {
            text = "Will mark NotDone once freeze is lifted",
        }, HttpStatusCode.OK, ct);
        answered.Status.Should().Be("answered");

        var commentQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Optional note check", new
        {
            item_id = Comment,
            text = "Please confirm whether a positional note applies",
        }, HttpStatusCode.Created, ct);
        await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{commentQuery.QueryId:D}/answer", "Answering comment under freeze", new
        {
            text = "No positional note recorded on worksheet",
        }, HttpStatusCode.OK, ct);
        var closedUnderFreeze = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries/{commentQuery.QueryId:D}/close", "Closing under freeze", null, HttpStatusCode.OK, ct);
        closedUnderFreeze.Status.Should().Be("closed");

        async Task RelayHostOutboxAsync()
        {
            var savedClock = fixture.Clock.UtcNow;
            try
            {
                if (Cadence.Kernel.Time.SystemClock.Instance.UtcNow > savedClock)
                {
                    fixture.Clock.Set(Cadence.Kernel.Time.SystemClock.Instance.UtcNow);
                }

                var relayed = await fixture.OutboxRelay.RelayBatchAsync(10_000, ct);
                relayed.Failed.Should().Be(0);
            }
            finally
            {
                fixture.Clock.Set(savedClock);
            }
        }

        // Switch from Frozen to Locked: while Diastolic is still missing and blankQuery is still open, Locked is refused with ScopeNotReady (#5466).
        await RelayHostOutboxAsync();
        await _scenario.Protocol.OkAsync(await _scenario.Observations.ReleaseAsync(tenant, study, frozen.LockId, ct), ct);
        var prematureLock = await fixture.Sender.SendAsync(
            await _scenario.Observations.LockAsync(tenant, study, NodeRef.For(enrollment), ct, LockKind.Locked),
            cancellationToken: ct);
        prematureLock.Error!.Code.Should().Be(AssertionErrors.ScopeNotReady.Code);

        // Record required Diastolic and close the answered query, relay to kernel, then apply Locked.
        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", SaveReason, new
        {
            values = new[] { new { item_id = Diastolic, value = new { type = "missing", reason = "NotDone" }, expected_revision = 1 } },
        }, HttpStatusCode.OK, ct);
        await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/close", "Resolved missing diastolic", null, HttpStatusCode.OK, ct);
        await RelayHostOutboxAsync();
        await _scenario.VerifyAndSignAsync(new Form(subject, formRecord.ProtocolVersionId, CollectionInstanceId.FromGuid(form)), ct);
        await _scenario.VerifyAndSignAsync(new Form(subject, formRecord.ProtocolVersionId, openedSecond.CollectionInstanceId), ct);

        await _scenario.Protocol.OkAsync(await _scenario.Observations.LockAsync(tenant, study, NodeRef.For(enrollment), ct, LockKind.Locked), ct);

        using (var lockedReopen = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/reopen", "Attempted reopen under lock", new
        {
            text = "Attempted reopen after lock",
        }), ct))
        {
            lockedReopen.StatusCode.Should().Be((HttpStatusCode)423);
            (await ProblemCodeAsync(lockedReopen, ct)).Should().Be(AssertionErrors.Locked.Code);
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

        using var lockedBlankRaise = await monitor.SendAsync(Request(HttpMethod.Post, $"/{secondForm:D}/queries", "Attempted blank raise under lock", new
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
            var linkA = new StudySite(StudySiteId.New(), tenant, study, sA.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded");
            var linkB = new StudySite(StudySiteId.New(), tenant, study, sB.Id, "Selected", KernelPostgresFixture.ActorId, "Seeded");
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

        // Database pagination: limit and offset slice deterministically by CreatedAt, Id.
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&limit=1&offset=0", null, null, HttpStatusCode.OK, ct))
            .Select(q => q.QueryId).Should().Equal(qA.QueryId);
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&limit=1&offset=1", null, null, HttpStatusCode.OK, ct))
            .Select(q => q.QueryId).Should().Equal(qB.QueryId);
        (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&limit=1&offset=2", null, null, HttpStatusCode.OK, ct))
            .Should().BeEmpty();
        using (var invalidLimit = await monitor.SendAsync(Request(HttpMethod.Get, $"/queries?study_id={study.Value:D}&limit=0", null, null), ct))
        {
            invalidLimit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // Site-scoped CRC sees ONLY Site A's enrollment queries — Site B is completely invisible!
        var visibleToSiteACrc = await SendAsync<FormQueryResponse[]>(crcA, HttpMethod.Get, $"/queries?study_id={study.Value:D}", null, null, HttpStatusCode.OK, ct);
        visibleToSiteACrc.Select(q => q.QueryId).Should().Equal(qA.QueryId);

        // Stranger with no grant in the study gets 403 Forbidden.
        using var denied = await stranger.SendAsync(Request(HttpMethod.Get, $"/queries?study_id={study.Value:D}", null, null), ct);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// End-to-end verification of <c>PRD-QRY-001</c> and <c>PRD-QRY-002</c>:
    /// <list type="number">
    /// <item>Manual query opens with <c>owner_role=site</c> and 14-day <c>due_at</c>; advancing <c>IClock</c> past 14 days
    /// escalates it to <c>high</c> priority, records <c>escalated_at</c>, surfaces it in <c>owner</c>/<c>origin</c>/<c>priority</c>/<c>escalated</c>
    /// filters, and emits a daily aging digest containing zero participant PII.</item>
    /// <item>Open manual query cannot be closed directly (<c>409</c>); editing the queried value auto-answers the open manual
    /// query, moves ownership to <c>monitor</c>, and mirrors <c>AnswerQuery</c> with the editing user, <c>TargetRevision</c>,
    /// and verbatim <c>X-Change-Reason</c>; closing as the answering user fails (<c>409</c>); closing as a distinct monitor succeeds.</item>
    /// <item>Bitemporal <c>as_of</c> reads reconstruct historical states; lineage links <c>form_query:{id}</c> to enrollment and
    /// observation; <c>execution.FormQueryChanged</c> events project into enrollment and study activity feeds.</item>
    /// </list>
    /// </summary>
    [Fact(DisplayName = "HostQuery_EndToEnd_EscalationAutoAnswerSeparationOfDutiesAsOfLineageAndActivity")]
    [GxPRequirement("PRD-QRY-001")]
    [GxPRequirement("PRD-QRY-002")]
    public async Task HostQueryEndToEndEscalationAutoAnswerSeparationOfDutiesAsOfLineageAndActivity()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var previousClock = fixture.Clock.UtcNow;
        var baseInstant = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero); // deid-ignore: fixed historical test instant
        fixture.Clock.Set(baseInstant);
        try
        {

            var (code, tenant, enrollment, form, observation) = await SeedWithClockAndServiceGrantsAsync(ct);
            var study = (await fixture.ObservationRecords.FindAsync(tenant, ObservationId.FromGuid(observation), ct))!.StudyId;

            using var crc = ClientWithSharedClock(code, Crc);
            using var monitor = ClientWithSharedClock(code, Monitor);
            using var crcDualMonitor = ClientWithSharedClock(code, CrcSiteB);

            // Grant CrcSiteB both answer and manage at this study so we can test self-close 409 SeparationOfDuties.
            foreach (var capability in new[] { "collection.record", "execution.query.answer", "execution.query.manage", "observation.query" })
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), Actor.Person(CrcSiteB), capability, null, AuthorityScope.Study(study), _scenario.Tick(), null, false,
                        KernelProtocolScenario.Key(), "Dual-hatted test user"),
                    ct);
            }

            // 1. Monitor raises manual query at T0 -> owner_role=site, due_at = T0 + 14d.
            var t0 = baseInstant.AddMinutes(5);
            fixture.Clock.Set(t0);
            var raised = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Monitor chart audit reason", new
            {
                observation_id = observation,
                revision = 1,
                text = "Verify systolic reading against bedside monitor strip.",
            }, HttpStatusCode.Created, ct);

            raised.Origin.Should().Be("manual");
            raised.OwnerRole.Should().Be("site");
            raised.Priority.Should().Be("normal");
            raised.DueAt.Should().Be(t0.AddDays(14));
            raised.EscalatedAt.Should().BeNull();

            // Attempting to close an Open manual query fails with 409 InvalidTransition.
            using (var openClose = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", "Premature close", null), ct))
            {
                openClose.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await ProblemCodeAsync(openClose, ct)).Should().Be(FormQueryErrors.InvalidTransition.Code);
            }

            // 2. Advance IClock past 14 calendar days -> escalate and emit daily aging digest with zero PII.
            var tEscalated = t0.AddDays(14).AddHours(2);
            fixture.Clock.Set(tEscalated);
            var escalationPass = await SendAsync<FormQueryEscalationPassResponse>(
                monitor, HttpMethod.Post, $"/queries/escalate?study_id={study.Value:D}", "Daily 14-day SLA check", null, HttpStatusCode.OK, ct);
            escalationPass.EscalatedQueries.Should().Be(1);
            escalationPass.Digests.Should().HaveCount(2, "one study-wide lead digest and one site investigator digest");

            var digestsRaw = await (await monitor.SendAsync(Request(HttpMethod.Get, $"/queries/digests?study_id={study.Value:D}", null, null), ct))
                .Content.ReadAsStringAsync(ct);
            digestsRaw.Should().NotContain("participant_id");
            digestsRaw.Should().Contain(raised.QueryId.ToString("D"));

            // Worklist filters by owner, origin, priority, and escalated.
            (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&owner=site&origin=manual&priority=high&escalated=true", null, null, HttpStatusCode.OK, ct))
                .Select(q => q.QueryId).Should().Equal(raised.QueryId);
            (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={study.Value:D}&origin=system", null, null, HttpStatusCode.OK, ct))
                .Should().BeEmpty();

            // 3. CrcSiteB edits the queried observation with a verbatim change reason -> auto-answers the open manual query!
            var tEdited = t0.AddDays(15);
            fixture.Clock.Set(tEdited);
            const string verbatimEditReason = "Corrected from signed vitals flowsheet line 4";
            await SendAsync<JsonElement>(crcDualMonitor, HttpMethod.Put, $"/{form:D}/values", verbatimEditReason, new
            {
                values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 126m, unit = "mm[Hg]" }, expected_revision = 1 } },
            }, HttpStatusCode.OK, ct);

            await RelayOutboxAsync(tenant, ct);

            var afterAutoAnswer = (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct)).Single();
            afterAutoAnswer.Status.Should().Be("answered");
            afterAutoAnswer.OwnerRole.Should().Be("monitor");
            afterAutoAnswer.LastAnsweredBy.Should().Be(CrcSiteB);
            afterAutoAnswer.ValueChanged.Should().BeTrue();

            // Attempting to close as CrcSiteB (the same user whose edit answered it) fails with 409 SeparationOfDuties!
            var tCloseAttempt = tEdited.AddMinutes(10);
            fixture.Clock.Set(tCloseAttempt);
            using (var selfClose = await crcDualMonitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", "Self-close attempt", null), ct))
            {
                selfClose.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await ProblemCodeAsync(selfClose, ct)).Should().Be(FormQueryErrors.SeparationOfDuties.Code);
            }

            // Distinct monitor closes with separate closing note and change reason.
            var tClosed = tEdited.AddMinutes(20);
            fixture.Clock.Set(tClosed);
            var closed = await SendAsync<FormQueryResponse>(
                monitor,
                HttpMethod.Post,
                $"/{form:D}/queries/{raised.QueryId:D}/close",
                "SDV completed against flowsheet",
                new { text = "Accepted flowsheet correction to 126 mm[Hg]." },
                HttpStatusCode.OK,
                ct);
            closed.Status.Should().Be("closed");
            closed.Messages[^1].Text.Should().Be("Accepted flowsheet correction to 126 mm[Hg].");
            closed.Messages[^1].ReasonForChange.Should().Be("SDV completed against flowsheet");

            // Cancelling an open manual query raised in error succeeds for authorized monitor.
            var raisedInError = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Raised on wrong row", new
            {
                observation_id = observation,
                revision = 2,
                text = "Accidental duplicate query",
            }, HttpStatusCode.Created, ct);
            fixture.Clock.Set(tClosed.AddMinutes(5));
            var cancelled = await SendAsync<FormQueryResponse>(
                monitor,
                HttpMethod.Post,
                $"/{form:D}/queries/{raisedInError.QueryId:D}/cancel",
                "Withdrawn as duplicate",
                new { text = "Raised on wrong item row." },
                HttpStatusCode.OK,
                ct);
            cancelled.Status.Should().Be("cancelled");

            await RelayOutboxAsync(tenant, ct);

            // 4. Bitemporal as_of reads reconstruct exact historical states!
            var asOfDay5 = await SendAsync<FormQueryResponse[]>(
                monitor,
                HttpMethod.Get,
                $"/{form:D}/queries?as_of={Uri.EscapeDataString(t0.AddDays(5).ToString("O"))}",
                null,
                null,
                HttpStatusCode.OK,
                ct);
            asOfDay5.Should().ContainSingle(q => q.QueryId == raised.QueryId && q.Status == "open" && q.Priority == "normal" && q.EscalatedAt == null);

            var asOfEscalated = await SendAsync<FormQueryResponse[]>(
                monitor,
                HttpMethod.Get,
                $"/{form:D}/queries?as_of={Uri.EscapeDataString(tEscalated.AddMinutes(30).ToString("O"))}",
                null,
                null,
                HttpStatusCode.OK,
                ct);
            asOfEscalated.Should().ContainSingle(q => q.QueryId == raised.QueryId && q.Status == "open" && q.Priority == "high" && q.EscalatedAt != null);

            // 5. Kernel assertions carry TargetRevision, real AssertedBy, and verbatim ReasonForChange.
            await using (var db = fixture.CreateAdminContext())
            {
                var assertions = await db.Assertions.AsNoTracking().Where(a => a.TenantId == tenant).ToListAsync(ct);
                assertions.Should().Contain(a =>
                    a.Kind == AssertionKind.Queried
                    && a.TargetId == observation
                    && a.TargetRevision == 1
                    && a.AssertedBy == Monitor
                    && a.ReasonForChange == "Monitor chart audit reason");
                assertions.Should().Contain(a =>
                    a.Kind == AssertionKind.QueryAnswered
                    && a.TargetRevision == 2
                    && a.AssertedBy == CrcSiteB
                    && a.ReasonForChange == verbatimEditReason);
            }

            // 6. Casebook HTML export renders revision, real actor, and verbatim ReasonForChange.
            using (var casebookRes = await monitor.GetAsync(
                new Uri($"/api/v1/kernel/enrollments/enrollment:{enrollment.Value:D}/casebook?format=html", UriKind.Relative), ct))
            {
                var casebookHtml = await casebookRes.Content.ReadAsStringAsync(ct);
                casebookRes.StatusCode.Should().Be(HttpStatusCode.OK, casebookHtml);
                casebookHtml.Should().Contain("(r1)");
                casebookHtml.Should().Contain(Monitor);
                casebookHtml.Should().Contain("Monitor chart audit reason");
                casebookHtml.Should().Contain("(r2)");
                casebookHtml.Should().Contain(CrcSiteB);
                casebookHtml.Should().Contain(verbatimEditReason);
            }

            // 7. Lineage for form_query:{id} links to Enrollment and Observation, and Activity feeds include execution.FormQueryChanged!
            await _scenario.Protocol.OkAsync(
                new IssueGrant(tenant, GrantId.New(), Actor.Person("auditor@execution-queries.test"), "study.read", null, AuthorityScope.Tenant(), _scenario.Tick(), null, false,
                    KernelProtocolScenario.Key(), "Tenant auditor for lineage trace"),
                ct);
            using var auditor = ClientWithSharedClock(code, "auditor@execution-queries.test");
            using (var lineageRes = await auditor.GetAsync(
                new Uri($"/api/v1/kernel/nodes/form_query:{raised.QueryId:D}/lineage?direction=Backward", UriKind.Relative), ct))
            {
                var lineageJson = await lineageRes.Content.ReadAsStringAsync(ct);
                lineageRes.StatusCode.Should().Be(HttpStatusCode.OK, lineageJson);
                lineageJson.Should().Contain($"enrollment:{enrollment.Value:D}");
                lineageJson.Should().Contain($"observation:{observation:D}");
            }

            using (var enrActivityRes = await monitor.GetAsync(
                new Uri($"/api/v1/kernel/activity?scope=enrollment:{enrollment.Value:D}", UriKind.Relative), ct))
            {
                var activityJson = await enrActivityRes.Content.ReadAsStringAsync(ct);
                enrActivityRes.StatusCode.Should().Be(HttpStatusCode.OK, activityJson);
                activityJson.Should().Contain("execution.FormQueryChanged");
            }

            using var studyActivityRes = await monitor.GetAsync(
                new Uri($"/api/v1/kernel/activity?scope=study:{study.Value:D}&family=execution", UriKind.Relative), ct);
            var studyActivityJson = await studyActivityRes.Content.ReadAsStringAsync(ct);
            studyActivityRes.StatusCode.Should().Be(HttpStatusCode.OK, studyActivityJson);
            studyActivityJson.Should().Contain("execution.FormQueryChanged");
        }
        finally
        {
            fixture.Clock.Set(previousClock);
        }
    }

    /// <summary>
    /// End-to-end verification of query assignee lifecycle, audited reassignment (<c>POST /{formId}/queries/{queryId}/reassign</c>),
    /// role-flip stale assignee clearing (manual answer, auto-answer on value edit, and reopen), bitemporal <c>assignee</c>
    /// filtering (<c>GET /queries?study_id=…&amp;assignee=…</c> current and <c>as_of</c>), and zero side-channel leakage on
    /// blinded treatment items (<c>K-AUTH-4</c>).
    /// </summary>
    [Fact(DisplayName = "HostQuery_ReassignRoleFlipAndAssigneeFilter_EnforceAuditTemporalHistoryAndBlindedSafety")]
    [GxPRequirement("PRD-QRY-001")]
    [GxPRequirement("PRD-QRY-002")]
    public async Task HostQueryReassignRoleFlipAndAssigneeFilterEnforceAuditTemporalHistoryAndBlindedSafety()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var previousClock = fixture.Clock.UtcNow;
        var baseInstant = new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero); // deid-ignore: fixed historical test instant
        fixture.Clock.Set(baseInstant);
        try
        {
            var world = await SeedBlindedMultiSiteWorldAsync(ct);
            using var crcA = ClientWithSharedClock(world.Code, Crc);
            using var monitor = ClientWithSharedClock(world.Code, Monitor);
            using var unblindedPharm = ClientWithSharedClock(world.Code, "pharm.unblinded@execution-queries.test");

            // 1. Raise a standard query with initial assignee crc.alice and a blinded treatment query with assignee pharm.secret.
            var tRaise = baseInstant.AddMinutes(10);
            fixture.Clock.Set(tRaise);
            var sbpQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{world.FormA:D}/queries", "Monitor baseline SDV", new
            {
                observation_id = world.SystolicObservationA,
                revision = 1,
                text = "Confirm baseline systolic BP.",
                assignee = "crc.alice",
            }, HttpStatusCode.Created, ct);
            sbpQuery.Assignee.Should().Be("crc.alice");
            sbpQuery.OwnerRole.Should().Be("site");

            var blindedQuery = await SendAsync<FormQueryResponse>(unblindedPharm, HttpMethod.Post, $"/{world.FormA:D}/queries", "Pharmacy kit check", new
            {
                observation_id = world.BlindedArmObservationA,
                revision = 1,
                text = "Confirm randomized arm kit.",
                assignee = "pharm.secret",
            }, HttpStatusCode.Created, ct);
            blindedQuery.Assignee.Should().Be("pharm.secret");

            // 2. Reassignment requires execution.query.manage, Idempotency-Key (422), and X-Change-Reason (422).
            await ExpectDeniedAsync(crcA, HttpMethod.Post, $"/{world.FormA:D}/queries/{sbpQuery.QueryId:D}/reassign", new { assignee = "crc.bob" }, ct);

            using (var missingKeyReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{world.FormA:D}/queries/{sbpQuery.QueryId:D}/reassign", UriKind.Relative))
            {
                Content = JsonContent.Create(new { assignee = "crc.bob" }),
            })
            {
                missingKeyReq.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Handoff"));
                using var missingKeyRes = await monitor.SendAsync(missingKeyReq, ct);
                missingKeyRes.StatusCode.Should().Be((HttpStatusCode)422);
            }

            using (var missingReasonReq = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/{world.FormA:D}/queries/{sbpQuery.QueryId:D}/reassign", UriKind.Relative))
            {
                Content = JsonContent.Create(new { assignee = "crc.bob" }),
            })
            {
                missingReasonReq.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
                using var missingReasonRes = await monitor.SendAsync(missingReasonReq, ct);
                missingReasonRes.StatusCode.Should().Be((HttpStatusCode)422);
            }

            // 3. Valid audited reassignment updates assignee and appends bitemporal period + Reassigned message.
            var tReassign = tRaise.AddHours(1);
            fixture.Clock.Set(tReassign);
            var reassigned = await SendAsync<FormQueryResponse>(
                monitor,
                HttpMethod.Post,
                $"/{world.FormA:D}/queries/{sbpQuery.QueryId:D}/reassign",
                "Coordinator shift handoff",
                new { assignee = "crc.bob", text = "Handed off to evening coordinator." },
                HttpStatusCode.OK,
                ct);
            reassigned.Assignee.Should().Be("crc.bob");
            reassigned.Messages[^1].Kind.Should().Be("reassigned");
            reassigned.Messages[^1].Text.Should().Be("Handed off to evening coordinator.");
            reassigned.Messages[^1].ReasonForChange.Should().Be("Coordinator shift handoff");

            // 4. Server-side assignee filter works on current and historical as_of reads WITHOUT leaking blinded assignees.
            (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={world.Study.Value:D}&assignee=crc.bob", null, null, HttpStatusCode.OK, ct))
                .Select(q => q.QueryId).Should().Equal(sbpQuery.QueryId);
            (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={world.Study.Value:D}&assignee=crc.alice", null, null, HttpStatusCode.OK, ct))
                .Should().BeEmpty();
            (await SendAsync<FormQueryResponse[]>(
                monitor,
                HttpMethod.Get,
                $"/queries?study_id={world.Study.Value:D}&assignee=crc.alice&as_of={Uri.EscapeDataString(tRaise.AddMinutes(15).ToString("O"))}",
                null,
                null,
                HttpStatusCode.OK,
                ct))
                .Select(q => q.QueryId).Should().Equal(sbpQuery.QueryId);

            // Blinded monitor probing assignee=pharm.secret gets 0 rows (both current and as_of), whereas unblinded pharmacist gets blindedQuery!
            (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/queries?study_id={world.Study.Value:D}&assignee=pharm.secret", null, null, HttpStatusCode.OK, ct))
                .Should().BeEmpty();
            (await SendAsync<FormQueryResponse[]>(
                monitor,
                HttpMethod.Get,
                $"/queries?study_id={world.Study.Value:D}&assignee=pharm.secret&as_of={Uri.EscapeDataString(tRaise.AddMinutes(15).ToString("O"))}",
                null,
                null,
                HttpStatusCode.OK,
                ct))
                .Should().BeEmpty();
            (await SendAsync<FormQueryResponse[]>(unblindedPharm, HttpMethod.Get, $"/queries?study_id={world.Study.Value:D}&assignee=pharm.secret", null, null, HttpStatusCode.OK, ct))
                .Select(q => q.QueryId).Should().Equal(blindedQuery.QueryId);

            // 5. Answering without replacement assignee flips Site -> Monitor and clears stale crc.bob.
            var tAnswer = tReassign.AddHours(1);
            fixture.Clock.Set(tAnswer);
            var answered = await SendAsync<FormQueryResponse>(
                crcA,
                HttpMethod.Post,
                $"/{world.FormA:D}/queries/{sbpQuery.QueryId:D}/answer",
                "Checked source chart",
                new { text = "Confirmed on source." },
                HttpStatusCode.OK,
                ct);
            answered.OwnerRole.Should().Be("monitor");
            answered.Assignee.Should().BeNull();

            // Reopening with replacement assignee crc.carol sets Assignee = crc.carol on Monitor -> Site flip.
            var tReopen = tAnswer.AddHours(1);
            fixture.Clock.Set(tReopen);
            var reopened = await SendAsync<FormQueryResponse>(
                monitor,
                HttpMethod.Post,
                $"/{world.FormA:D}/queries/{sbpQuery.QueryId:D}/reopen",
                "Please re-check cuff log",
                new { text = "Cuff log lists 134 mm[Hg].", assignee = "crc.carol" },
                HttpStatusCode.OK,
                ct);
            reopened.OwnerRole.Should().Be("site");
            reopened.Assignee.Should().Be("crc.carol");

            // Editing the queried value auto-answers via outbox and clears stale site assignee crc.carol!
            var tEdit = tReopen.AddHours(1);
            fixture.Clock.Set(tEdit);
            await SendAsync<JsonElement>(crcA, HttpMethod.Put, $"/{world.FormA:D}/values", "Corrected from cuff log", new
            {
                values = new[] { new { item_id = Systolic, value = new { type = "quantity", value = 134m, unit = "mm[Hg]" }, expected_revision = 1 } },
            }, HttpStatusCode.OK, ct);
            await RelayOutboxAsync(world.Tenant, ct);

            var afterAutoAnswer = (await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{world.FormA:D}/queries", null, null, HttpStatusCode.OK, ct))
                .Single(q => q.QueryId == sbpQuery.QueryId);
            afterAutoAnswer.Status.Should().Be("answered");
            afterAutoAnswer.OwnerRole.Should().Be("monitor");
            afterAutoAnswer.Assignee.Should().BeNull();
        }
        finally
        {
            fixture.Clock.Set(previousClock);
        }
    }

    /// <summary>
    /// End-to-end verification of the leader-locked daily scheduler (<see cref="FormQueryEscalationHostedService"/>) running as
    /// <c>svc:execution-query-sla</c>, outbox <c>execution.FormQueryAgingDigestGenerated</c> idempotent de-identified alerts to
    /// <c>study_lead</c> and <c>site_investigator</c> recipients (unconditionally redacting blinded items), scoped digest reads
    /// (<c>GET /queries/digests</c>) redacting blinded items unless <c>enrollment.unblind</c> is held and hiding study-wide
    /// digests from site-only callers, and manual <c>POST /queries/escalate</c> gating on <c>Idempotency-Key</c>,
    /// <c>X-Change-Reason</c>, <c>execution.query.manage</c>, and active <c>Capture</c> subscription entitlement.
    /// </summary>
    [Fact(DisplayName = "HostQuery_ScheduledSweepDigestBlindingScopeVisibilityAlertsAndSubscriptionGate")]
    [GxPRequirement("PRD-QRY-002")]
    public async Task HostQueryScheduledSweepDigestBlindingScopeVisibilityAlertsAndSubscriptionGate()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var previousClock = fixture.Clock.UtcNow;
        var baseInstant = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero); // deid-ignore: fixed historical test instant
        fixture.Clock.Set(baseInstant);
        try
        {
            var world = await SeedBlindedMultiSiteWorldAsync(ct);
            using var crcA = ClientWithSharedClock(world.Code, Crc);
            using var siteAInvestigator = ClientWithSharedClock(world.Code, "pi.sitea@execution-queries.test");
            using var monitor = ClientWithSharedClock(world.Code, Monitor);
            using var unblindedPharm = ClientWithSharedClock(world.Code, "pharm.unblinded@execution-queries.test");

            // 1. Open both a normal Systolic query and a blinded treatment-arm query at Site A.
            var t0 = baseInstant.AddMinutes(5);
            fixture.Clock.Set(t0);
            var sbpQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{world.FormA:D}/queries", "Baseline SDV", new
            {
                observation_id = world.SystolicObservationA,
                revision = 1,
                text = "Verify baseline systolic value.",
                assignee = "crc.alice",
            }, HttpStatusCode.Created, ct);

            var blindedQuery = await SendAsync<FormQueryResponse>(unblindedPharm, HttpMethod.Post, $"/{world.FormA:D}/queries", "Pharmacy reconciliation", new
            {
                observation_id = world.BlindedArmObservationA,
                revision = 1,
                text = "Verify blinded treatment arm kit.",
            }, HttpStatusCode.Created, ct);

            // 2. Manual POST /queries/escalate enforces execution.query.manage (403), Idempotency-Key (422), and X-Change-Reason (422).
            var tOverdue = t0.AddDays(15);
            fixture.Clock.Set(tOverdue);
            await ExpectDeniedAsync(crcA, HttpMethod.Post, $"/queries/escalate?study_id={world.Study.Value:D}", null, ct);

            using (var escalateNoKey = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/queries/escalate?study_id={world.Study.Value:D}", UriKind.Relative)))
            {
                escalateNoKey.Headers.Add(GatewayHeaders.ChangeReason, ChangeReasonHeader.Encode("Manual SLA check"));
                using var res = await monitor.SendAsync(escalateNoKey, ct);
                res.StatusCode.Should().Be((HttpStatusCode)422);
            }

            using (var escalateNoReason = new HttpRequestMessage(HttpMethod.Post, new Uri($"{FormEndpoints.RoutePrefix}/queries/escalate?study_id={world.Study.Value:D}", UriKind.Relative)))
            {
                escalateNoReason.Headers.Add(FormEndpoints.IdempotencyKeyHeader, KernelProtocolScenario.Key());
                using var res = await monitor.SendAsync(escalateNoReason, ct);
                res.StatusCode.Should().Be((HttpStatusCode)422);
            }

            // 3. Leader-lock contention skips the scheduled sweep when another replica holds the PostgreSQL advisory lock.
            var scheduler = _sharedClockHost!.Services.GetRequiredService<FormQueryEscalationHostedService>();
            var peerLock = new KernelLeaderLock(new KernelConnectionString(fixture.ConnectionString!));
            var contendedResult = default(FormQueryEscalationSweepSummary);
            var acquiredByPeer = await peerLock.TryRunAsync(
                FormQueryEscalationHostedService.JobName,
                async innerCt =>
                {
                    contendedResult = await scheduler.RunSweepOnceAsync(innerCt);
                },
                ct);
            acquiredByPeer.Should().BeTrue();
            contendedResult!.AcquiredLeaderLock.Should().BeFalse();

            // 4. Once released, the scheduled daily sweep executes under svc:execution-query-sla, escalates both queries, and saves digests!
            var sweepSummary = await scheduler.RunSweepOnceAsync(ct);
            sweepSummary.AcquiredLeaderLock.Should().BeTrue();
            sweepSummary.EscalatedQueries.Should().BeGreaterThanOrEqualTo(2);
            sweepSummary.DigestsGenerated.Should().BeGreaterThanOrEqualTo(2);

            var escalatedRows = await SendAsync<FormQueryResponse[]>(
                unblindedPharm,
                HttpMethod.Get,
                $"/queries?study_id={world.Study.Value:D}&escalated=true&assignee=crc.alice",
                null,
                null,
                HttpStatusCode.OK,
                ct);
            escalatedRows.Select(q => q.QueryId).Should().Equal(sbpQuery.QueryId);

            // 5. Outbox relay delivers execution.FormQueryAgingDigestGenerated -> idempotent de-identified alerts to study_lead & site_investigator,
            // unconditionally redacting the blinded treatment item even when unblindedPharm is among study leads!
            await RelayOutboxAsync(world.Tenant, ct);
            var alerts = fixture.AlertDispatcher.GetSentAlerts(world.Tenant, world.Study);
            alerts.Should().HaveCount(2);

            var studyAlert = alerts.Single(a => a.StudySiteId is null);
            studyAlert.RecipientRole.Should().Be("study_lead");
            studyAlert.RecipientActorIds.Should().Contain([Monitor, "pharm.unblinded@execution-queries.test"]);
            studyAlert.Entries.Should().HaveCount(2);
            studyAlert.Entries.Should().ContainSingle(e => e.QueryId == sbpQuery.QueryId && e.ItemId == Systolic);
            studyAlert.Entries.Should().ContainSingle(e =>
                e.ItemId == FormQueryAgingDigestEntry.MaskedLiteral
                && e.QueryId == Guid.Empty
                && e.FormId == Guid.Empty);
            studyAlert.Entries.Select(e => e.ItemId).Should().NotContain(world.BlindedItemId);

            var siteAlert = alerts.Single(a => a.StudySiteId == world.SiteA);
            siteAlert.RecipientRole.Should().Be("site_investigator");
            siteAlert.RecipientActorIds.Should().Contain("pi.sitea@execution-queries.test");
            siteAlert.Entries.Should().ContainSingle(e => e.ItemId == FormQueryAgingDigestEntry.MaskedLiteral && e.QueryId == Guid.Empty);

            // Re-running the sweep on the same UTC date is idempotent and dispatches zero duplicate alerts.
            await scheduler.RunSweepOnceAsync(ct);
            await RelayOutboxAsync(world.Tenant, ct);
            fixture.AlertDispatcher.GetSentAlerts(world.Tenant, world.Study).Should().HaveCount(2);

            // 6. Scoped GET /queries/digests:
            // - Site A Investigator (site-only grant) sees ONLY Site A's digest (never the study-wide StudySiteId == null digest), with blinded entry redacted.
            var siteInvestigatorDigests = await SendAsync<FormQueryAgingDigestResponse[]>(
                siteAInvestigator, HttpMethod.Get, $"/queries/digests?study_id={world.Study.Value:D}", null, null, HttpStatusCode.OK, ct);
            siteInvestigatorDigests.Should().ContainSingle(d => d.StudySiteId == world.SiteA.Value);
            siteInvestigatorDigests.Should().NotContain(d => d.StudySiteId == null);
            siteInvestigatorDigests.Single().Entries.Should().ContainSingle(e => e.ItemId == FormQueryAgingDigestEntry.MaskedLiteral && e.QueryId == Guid.Empty);

            // - Blinded Study Monitor sees BOTH study-wide and site digests, with blinded entry redacted.
            var monitorDigests = await SendAsync<FormQueryAgingDigestResponse[]>(
                monitor, HttpMethod.Get, $"/queries/digests?study_id={world.Study.Value:D}", null, null, HttpStatusCode.OK, ct);
            monitorDigests.Should().HaveCount(2);
            monitorDigests.Single(d => d.StudySiteId == null).Entries
                .Should().ContainSingle(e => e.ItemId == FormQueryAgingDigestEntry.MaskedLiteral && e.QueryId == Guid.Empty);

            // - Unblinded Pharmacist (holds enrollment.unblind) sees unredacted blinded entry in digests!
            var unblindedDigests = await SendAsync<FormQueryAgingDigestResponse[]>(
                unblindedPharm, HttpMethod.Get, $"/queries/digests?study_id={world.Study.Value:D}", null, null, HttpStatusCode.OK, ct);
            unblindedDigests.Single(d => d.StudySiteId == null).Entries
                .Should().ContainSingle(e => e.QueryId == blindedQuery.QueryId && e.ItemId == world.BlindedItemId);

            // 7. Expiring the tenant's Capture subscription blocks manual POST /queries/escalate with 403 K.Subscription.Expired,
            // while GET /queries/digests remains readable (200 OK).
            await _scenario.Protocol.OkAsync(
                new ProvisionSubscription(
                    world.Tenant,
                    SubscriptionId.New(),
                    CommercialTier.Individual,
                    SubscriptionStatus.Expired,
                    tOverdue.AddDays(-1),
                    KernelProtocolScenario.Key(),
                    "Simulate expired trial"),
                ct);

            using (var expiredEscalate = await monitor.SendAsync(
                Request(HttpMethod.Post, $"/queries/escalate?study_id={world.Study.Value:D}", "Manual SLA check after expiry", null), ct))
            {
                expiredEscalate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                (await ProblemCodeAsync(expiredEscalate, ct)).Should().Be(KernelErrors.SubscriptionExpired.Code);
            }

            (await SendAsync<FormQueryAgingDigestResponse[]>(
                monitor, HttpMethod.Get, $"/queries/digests?study_id={world.Study.Value:D}", null, null, HttpStatusCode.OK, ct))
                .Should().HaveCount(2);
        }
        finally
        {
            fixture.Clock.Set(previousClock);
        }
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

    private HttpClient ClientWithSharedClock(string tenantCode, string userId)
    {
        _sharedClockHost ??= new WebApplicationFactory<Cadence.Kernel.Host.Program>().WithWebHostBuilder(builder => builder
            .UseTestGatewaySecret()
            .UseSetting("ConnectionStrings:cadence", fixture.ConnectionString)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IClock>();
                services.AddSingleton<IClock>(fixture.Clock);
                services.AddDeclaredKernelErrorsCheck();
                services.RemoveAll<KernelBackgroundOptions>();
                services.AddSingleton(new KernelBackgroundOptions
                {
                    RelayInterval = TimeSpan.FromHours(1),
                    ActivityInterval = TimeSpan.FromHours(1),
                });
            }));
        return _sharedClockHost.CreateSignedClient(new GatewayIdentity(userId, [], [], TenantId: tenantCode));
    }

    private async Task<(string Code, TenantId Tenant, EnrollmentId Enrollment, Guid Form, Guid Observation)> SeedWithClockAndServiceGrantsAsync(CancellationToken ct)
    {
        var seeded = await SeedAsync(ct);
        var study = (await fixture.ObservationRecords.FindAsync(seeded.Tenant, ObservationId.FromGuid(seeded.Observation), ct))!.StudyId;
        foreach (var monitorStudyCapability in new[] { "execution.query.manage", "enrollment.export" })
        {
            await _scenario.Protocol.OkAsync(
                new IssueGrant(
                    seeded.Tenant,
                    GrantId.New(),
                    Actor.Person(Monitor),
                    monitorStudyCapability,
                    null,
                    AuthorityScope.Study(study),
                    _scenario.Tick(),
                    null,
                    false,
                    KernelProtocolScenario.Key(),
                    "Study monitor lead grant for SLA escalation pass and casebook verification"),
                ct);
        }
        foreach (var (serviceAccount, capabilities) in new[]
        {
            ("execution-forms", new[] { "execution.form.open" }),
            ("execution-queries", new[] { "observation.query", "observation.query.answer", "assertion.retract" }),
            (EvaluateEditChecksOnCollectionChanged.Name, new[] { "validation_run.record", "collection.record_unasked", "execution.query.raise", "execution.query.manage" }),
            (FormQueryEscalationHostedService.ServiceAccountName, new[] { "execution.query.manage" }),
        })
        {
            foreach (var capability in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(
                        seeded.Tenant,
                        GrantId.New(),
                        Actor.ServiceAccount(serviceAccount),
                        capability,
                        null,
                        AuthorityScope.Tenant(),
                        _scenario.Tick(),
                        null,
                        false,
                        KernelProtocolScenario.Key(),
                        "Service account grant for end-to-end test"),
                    ct);
            }
        }

        return seeded;
    }

    private async Task RelayOutboxAsync(TenantId tenant, CancellationToken ct)
    {
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

        await using (var verifyDb = fixture.CreateAdminContext())
        {
            var undispatched = await verifyDb.OutboxMessages
                .CountAsync(m => m.TenantId == tenant && m.DispatchedAt == null, ct);
            undispatched.Should().Be(0);
        }

        await new KernelActivityProjector(
            fixture.TenantContexts,
            new KernelNavigator(new KernelScopeGraph(fixture.TenantContexts)),
            new KernelBackgroundOptions())
            .ProjectAsync(tenant, ct);
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

    private sealed record BlindedMultiSiteWorld(
        string Code,
        TenantId Tenant,
        StudyId Study,
        StudySiteId SiteA,
        StudySiteId SiteB,
        Guid FormA,
        Guid SystolicObservationA,
        Guid BlindedArmObservationA,
        string BlindedItemId);

    private async Task<BlindedMultiSiteWorld> SeedBlindedMultiSiteWorldAsync(CancellationToken ct)
    {
        const string blindedItem = "RAND_ARM";
        const string unblindedPharm = "pharm.unblinded@execution-queries.test";
        const string siteAInvestigator = "pi.sitea@execution-queries.test";

        var code = $"T-BSLA-{Guid.NewGuid():N}"[..16];
        var tenant = TenantId.New();
        await _scenario.Protocol.OkAsync(
            new ProvisionTenant(tenant, code, "Blinded SLA and assignee query tenant", KernelProtocolScenario.Key(), KernelProtocolScenario.Reason), ct);

        var (_, study) = await _scenario.Protocol.NewStudyAsync(tenant, ct);
        var systolicConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Systolic BP", "Systolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var diastolicConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(tenant, ConceptId.New(), ConceptOrigin.Sponsor, null, ConceptKind.BiomedicalConcept, "Diastolic BP", "Diastolic BP.", KernelProtocolScenario.Key(), "Defined"), ct);
        var treatmentConcept = await _scenario.Protocol.OkAsync(
            new DefineConcept(
                tenant,
                ConceptId.New(),
                ConceptOrigin.Sponsor,
                null,
                ConceptKind.BiomedicalConcept,
                "Randomized arm (randomization_treatment_assignment)",
                "Randomized treatment allocation.",
                KernelProtocolScenario.Key(),
                "Defined"),
            ct);

        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            Epochs = [new Epoch("ep.treatment", "Treatment", 1)],
            PlannedActivities =
            [
                new PlannedActivity(
                    VisitId,
                    "Baseline visit",
                    "ep.treatment",
                    [ActivityLocation.Site],
                    [SpecId],
                    new ActivityTiming(1, 0, 2)),
            ],
            CollectionSpecs =
            [
                new CollectionSpec(
                    SpecId,
                    "Vital signs",
                    [
                        new CollectionItem(Systolic, systolicConcept, "mm[Hg]", Required: true),
                        new CollectionItem(Diastolic, diastolicConcept, "mm[Hg]", Required: false),
                        new CollectionItem(blindedItem, treatmentConcept, ValueKind: ObservationValueKind.Text, Required: false),
                    ]),
            ],
        };

        var versionId = ProtocolVersionId.New();
        var draft = await _scenario.Protocol.CreateDraftAsync(tenant, study, versionId, ct, ProtocolDesignDocument.Write(design));
        draft.IsSuccess.Should().BeTrue(draft.Error?.Message);
        var inReview = await _scenario.Protocol.OkAsync(new SubmitProtocolVersionForReview(tenant, versionId, KernelProtocolScenario.Key(), "Ready"), ct);
        var signature = await _scenario.Protocol.OkAsync(_scenario.Protocol.AttestCommand(tenant, AttestationMeaning.Approved, KernelProtocolScenario.Target(inReview)), ct);
        await _scenario.Protocol.OkAsync(new ApproveProtocolVersion(tenant, versionId, signature, KernelProtocolScenario.Key(), "Approved"), ct);

        var effectivity = EffectivityId.New();
        await _scenario.Protocol.OkAsync(
            new DeclareEffectivity(tenant, effectivity, versionId, EffectivityScope.Study(), _scenario.Tick(), KernelProtocolScenario.Key(), "Study-wide"), ct);
        var ethicsBytes = Encoding.UTF8.GetBytes($"ethics-{Guid.NewGuid():N}");
        var ethicsVersion = ArtifactVersionId.New();
        var ethicsContent = new ArtifactContent(ContentHash.OfBytes(ethicsBytes), ethicsBytes.Length, "application/pdf", "blob://evidence/ethics.pdf");
        await _scenario.Protocol.OkAsync(
            new RegisterArtifact(tenant, ArtifactId.New(), ethicsVersion, ArtifactKind.EthicsApproval, study, "Ethics approval", ethicsContent, KernelProtocolScenario.Key(), "Filed"), ct);
        await _scenario.Protocol.OkAsync(
            new ChangeEffectivityStatus(tenant, effectivity, EffectivityStatus.Effective, _scenario.Tick(), [ethicsVersion], KernelProtocolScenario.Key(), "Effective"), ct);

        StudySiteId siteA;
        StudySiteId siteB;
        await using (var db = fixture.CreateContext(tenant))
        {
            var studyRow = await db.Studies.AsNoTracking().SingleAsync(s => s.Id == study, ct);
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var rawSiteA = new Site(SiteId.New(), tenant, studyRow.SponsorId, $"SITE-A-{suffix}", "Site Alpha", "US", KernelPostgresFixture.ActorId, "Seeded Site A");
            var rawSiteB = new Site(SiteId.New(), tenant, studyRow.SponsorId, $"SITE-B-{suffix}", "Site Beta", "US", KernelPostgresFixture.ActorId, "Seeded Site B");
            var linkA = new StudySite(StudySiteId.New(), tenant, study, rawSiteA.Id, "Active", KernelPostgresFixture.ActorId, "Seeded Site A");
            var linkB = new StudySite(StudySiteId.New(), tenant, study, rawSiteB.Id, "Active", KernelPostgresFixture.ActorId, "Seeded Site B");
            await fixture.SeedAsync(rawSiteA, rawSiteB, linkA, linkB);
            siteA = linkA.Id;
            siteB = linkB.Id;
        }

        var participantA = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented A"), ct);
        SubjectCode.TryFrom("SUBJ-A1", out var subjA);
        var screenedA = await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantA, siteA, subjA, _scenario.Tick(), KernelProtocolScenario.Key(), "Screened A"), ct);

        var participantB = await _scenario.Protocol.OkAsync(new RegisterParticipant(tenant, ParticipantId.New(), KernelProtocolScenario.Key(), "Consented B"), ct);
        SubjectCode.TryFrom("SUBJ-B1", out var subjB);
        await _scenario.Protocol.OkAsync(
            new ScreenParticipant(tenant, EnrollmentId.New(), participantB, siteB, subjB, _scenario.Tick(), KernelProtocolScenario.Key(), "Screened B"), ct);

        var subjectA = new KernelObservationScenario.Subject(tenant, study, siteA, screenedA.EnrollmentId);
        var openedA = await _scenario.Protocol.OkAsync(Open(subjectA, CollectionInstanceId.New(), _scenario.Tick()), ct);
        await RelayOutboxAsync(tenant, ct);

        var savedA = await _scenario.Protocol.OkAsync(
            Save(
                new Form(subjectA, versionId, openedA.CollectionInstanceId),
                Value(Systolic, Mmhg(128)),
                Value(blindedItem, new TextValue("ACTIVE_DRUG_200MG"))),
            ct);
        var sbpObs = savedA.Values.Single(v => v.ItemId == Systolic).ObservationId.Value;
        var armObs = savedA.Values.Single(v => v.ItemId == blindedItem).ObservationId.Value;

        foreach (var (actor, capabilities, scope) in new (Actor, string[], AuthorityScope)[]
        {
            (Actor.Person(Crc), ["collection.record", "execution.query.answer"], AuthorityScope.StudySite(siteA)),
            (Actor.Person(siteAInvestigator), ["collection.record", "collection.sign", "execution.query.answer"], AuthorityScope.StudySite(siteA)),
            (Actor.Person(Monitor), ["collection.record_unasked", "execution.query.raise", "execution.query.manage"], AuthorityScope.Study(study)),
            (Actor.Person(unblindedPharm), ["collection.record_unasked", "execution.query.raise", "execution.query.manage", "enrollment.unblind"], AuthorityScope.Study(study)),
            (Actor.ServiceAccount("execution-forms"), ["execution.form.open"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount("execution-queries"), ["observation.query", "observation.query.answer", "assertion.retract"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(EvaluateEditChecksOnCollectionChanged.Name), ["validation_run.record", "collection.record_unasked", "execution.query.raise", "execution.query.manage"], AuthorityScope.Tenant()),
            (Actor.ServiceAccount(FormQueryEscalationHostedService.ServiceAccountName), ["execution.query.manage"], AuthorityScope.Tenant()),
        })
        {
            foreach (var cap in capabilities)
            {
                await _scenario.Protocol.OkAsync(
                    new IssueGrant(tenant, GrantId.New(), actor, cap, null, scope, _scenario.Tick(), null, false, KernelProtocolScenario.Key(), "Test grant"),
                    ct);
            }
        }

        return new BlindedMultiSiteWorld(code, tenant, study, siteA, siteB, openedA.CollectionInstanceId.Value, sbpObs, armObs, blindedItem);
    }
}
