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
using Cadence.Kernel.Application.Navigation;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.Activity;
using Cadence.Kernel.Persistence.Background;
using Cadence.Kernel.Persistence.Navigation;
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
    private const string Investigator = "pi@execution-queries.test";
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
    /// Raising a missing-data query on a blank required item (<see cref="Diastolic"/>) stores an atomic module query at
    /// revision 0 with <c>observation_id = null</c> without writing any <c>NotAsked</c> observation (including when shape
    /// validation fails with <c>422</c>); when the site coordinator subsequently records a reasoned missing status
    /// (<c>NotDone</c>) at <c>expected_revision = 0</c>, the form becomes complete and the query reports
    /// <c>value_changed = true</c>.
    /// </summary>
    [Fact(DisplayName = "HostQuery_BlankRequiredItem_KeepsFormIncompleteUntilCoordinatorRecordsValueOrReasonedMissing")]
    public async Task HostQueryBlankRequiredItemKeepsFormIncompleteUntilCoordinatorRecordsValueOrReasonedMissing()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, _, form, _) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var monitor = Client(code, Monitor);

        // 422 validation failure on blank query text leaves zero orphan observations on the form or in kernel tables.
        using (var invalidBlank = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries", "Invalid empty blank query", new
        {
            item_id = Diastolic,
            text = "   ",
        }), ct))
        {
            invalidBlank.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(invalidBlank, ct)).Should().Be(KernelErrors.CommandInvalid.Code);
        }

        using (var unknownItem = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries", "Unknown blank item", new
        {
            item_id = "item.unknown",
            text = "Unknown item check",
        }), ct))
        {
            unknownItem.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await ProblemCodeAsync(unknownItem, ct)).Should().Be(CollectionErrors.ValuesInvalid.Code);
        }

        await using (var db = fixture.CreateAdminContext())
        {
            (await db.Observations.CountAsync(o => o.TenantId == tenant, ct))
                .Should().Be(1, "a 422 blank-item query failure must not leave orphan observations beyond the seeded Systolic observation");
        }

        var raised = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Missing diastolic BP on source worksheet", new
        {
            item_id = Diastolic,
            text = "Diastolic BP is blank; please enter the source value or why it was not done.",
        }, HttpStatusCode.Created, ct);

        raised.ItemId.Should().Be(Diastolic);
        raised.ObservationId.Should().BeNull();
        raised.Revision.Should().Be(0);
        raised.CurrentRevision.Should().BeNull();
        raised.ValueChanged.Should().BeFalse();

        // Form remains incomplete and holds zero observation rows for the blank item!
        var formAfterBlankQuery = await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct);
        formAfterBlankQuery.Complete.Should().BeFalse();
        formAfterBlankQuery.MissingRequired.Should().Equal(Diastolic);
        var diastolicItem = formAfterBlankQuery.Items.Single(i => i.ItemId == Diastolic);
        diastolicItem.ObservationId.Should().BeNull();
        diastolicItem.Revision.Should().Be(0);
        diastolicItem.Value.Should().BeNull();

        await using (var db = fixture.CreateAdminContext())
        {
            (await db.Observations.CountAsync(o => o.TenantId == tenant, ct))
                .Should().Be(1, "raising a blank-item query must not synthesize a NotAsked observation");
        }

        // Coordinator records a reasoned missing status (NotDone) at expected_revision = 0 -> missing_required clears and value_changed = true, while open query keeps Complete false until closed!
        await SendAsync<JsonElement>(crc, HttpMethod.Put, $"/{form:D}/values", "Confirmed not measured at visit", new
        {
            values = new[]
            {
                new { item_id = Diastolic, value = new { type = "missing", reason = "NotDone" }, expected_revision = 0 },
            },
        }, HttpStatusCode.OK, ct);

        var formAfterMissing = await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct);
        formAfterMissing.MissingRequired.Should().BeEmpty();
        formAfterMissing.Complete.Should().BeFalse("the open query blocks completeness until answered and closed");

        var queriesAfterMissing = await SendAsync<FormQueryResponse[]>(monitor, HttpMethod.Get, $"/{form:D}/queries", null, null, HttpStatusCode.OK, ct);
        queriesAfterMissing.Should().ContainSingle().Which.Should().Match<FormQueryResponse>(q =>
            q.QueryId == raised.QueryId && q.ObservationId == null && q.Revision == 0 && q.CurrentRevision == 1 && q.ValueChanged);

        await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/answer", "Confirmed not measured", new
        {
            text = "Cuff not available; marked NotDone",
        }, HttpStatusCode.OK, ct);
        await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries/{raised.QueryId:D}/close", "Missingness documented", null, HttpStatusCode.OK, ct);
        (await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct)).Complete.Should().BeTrue();
    }

    /// <summary>
    /// <c>Frozen</c> blocks form value saves (<c>423</c>) while still allowing blank-item queries and query transitions
    /// without mutating <c>ContentHash</c> or superseding Part 11 signatures; <c>Locked</c> (including an observation-level
    /// lock on an item saved after the blank-item query) refuses blank-item queries, recorded-item queries, and query
    /// transitions with <c>423 K.Assertion.Locked</c>.
    /// </summary>
    [Fact(DisplayName = "HostQuery_FrozenAllowsBlankAndTransitions_WhileLockedRefusesWith423")]
    public async Task HostQueryFrozenAllowsBlankAndTransitionsWhileLockedRefusesWith423()
    {
        fixture.SkipIfUnavailable();
        var ct = TestContext.Current.CancellationToken;
        var (code, tenant, enrollment, form, observation) = await SeedAsync(ct);
        using var crc = Client(code, Crc);
        using var pi = Client(code, Investigator);
        using var monitor = Client(code, Monitor);

        // Complete required Diastolic while leaving optional Comment blank, then sign the form as Investigator.
        await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{form:D}/values", SaveReason, new
        {
            values = new[] { new { item_id = Diastolic, value = new { type = "quantity", value = 80m, unit = "mm[Hg]" }, expected_revision = 0 } },
        }, HttpStatusCode.OK, ct);
        var beforeSign = await SendAsync<FormResponse>(pi, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct);
        using (var signReq = Request(HttpMethod.Post, $"/{form:D}/signatures", "I approve these values", new
        {
            meaning = "approved",
            content_hash = beforeSign.ContentHash,
        }))
        {
            signReq.Headers.Add(
                GatewayHeaders.SigToken,
                TestSecurityContext.CreateSigToken(Investigator, FormSignatureEndpoints.SignAction(form), FormSignatureEndpoints.SignSemanticAction));
            using var signRes = await pi.SendAsync(signReq, ct);
            signRes.StatusCode.Should().Be(HttpStatusCode.Created, await signRes.Content.ReadAsStringAsync(ct));
        }

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

        // ... while allowing blank-item query raise, answer, and closure without mutating ContentHash or superseding the signature!
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

        var blankQuery = await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries", "Interim freeze review", new
        {
            item_id = Comment,
            text = "Please confirm whether any cuff comment applies",
        }, HttpStatusCode.Created, ct);
        blankQuery.ObservationId.Should().BeNull();
        blankQuery.Revision.Should().Be(0);

        var answered = await SendAsync<FormQueryResponse>(crc, HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/answer", "Answering under freeze", new
        {
            text = "Will enter cuff note once freeze is lifted",
        }, HttpStatusCode.OK, ct);
        answered.Status.Should().Be("answered");

        var afterFrozenBlankQuery = await SendAsync<FormResponse>(monitor, HttpMethod.Get, $"/{form:D}", null, null, HttpStatusCode.OK, ct);
        afterFrozenBlankQuery.ContentHash.Should().Be(beforeSign.ContentHash);
        afterFrozenBlankQuery.Signatures.Should().ContainSingle().Which.Superseded.Should().BeFalse();

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

        // Release Frozen, record a value on Comment at expected_revision = 0, and place an Observation-level Locked lock on it:
        // FormQueryScopes.StepAsync resolves the current observation by ItemId and refuses transitions with 423 K.Assertion.Locked!
        await RelayHostOutboxAsync();
        await _scenario.Protocol.OkAsync(await _scenario.Observations.ReleaseAsync(tenant, study, frozen.LockId, ct), ct);
        var savedComment = await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{form:D}/values", SaveReason, new
        {
            values = new[] { new { item_id = Comment, value = new { type = "text", value = "Standard adult cuff" }, expected_revision = 0 } },
        }, HttpStatusCode.OK, ct);
        var commentObsId = ObservationId.FromGuid(savedComment.Values.Single(v => v.ItemId == Comment).ObservationId);
        var obsLock = await _scenario.Protocol.OkAsync(
            await _scenario.Observations.LockAsync(tenant, study, NodeRef.For(commentObsId), ct, LockKind.Locked), ct);

        using (var obsLockedClose = await monitor.SendAsync(Request(HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/close", "Attempted close under observation lock", null), ct))
        {
            obsLockedClose.StatusCode.Should().Be((HttpStatusCode)423);
            (await ProblemCodeAsync(obsLockedClose, ct)).Should().Be(AssertionErrors.Locked.Code);
        }

        // Release observation lock, open an unscheduled form whose required items are not yet recorded, and verify Locked
        // is refused with ScopeNotReady (#5466) until required items are completed and open queries are closed.
        await _scenario.Protocol.OkAsync(await _scenario.Observations.ReleaseAsync(tenant, study, obsLock.LockId, ct), ct);
        var unrecordedForm = CollectionInstanceId.New();
        await _scenario.Protocol.OkAsync(
            new OpenCollectionInstance(tenant, unrecordedForm, enrollment, KernelCollectionScenario.SpecId, null, _scenario.Tick(), KernelProtocolScenario.Key(), "Unscheduled vitals"), ct);

        var prematureLock = await fixture.Sender.SendAsync(
            await _scenario.Observations.LockAsync(tenant, study, NodeRef.For(enrollment), ct, LockKind.Locked),
            cancellationToken: ct);
        prematureLock.Error!.Code.Should().Be(AssertionErrors.ScopeNotReady.Code);

        // Complete required Systolic & Diastolic on unrecordedForm (leaving optional Comment blank), close blankQuery, relay outbox, then apply Locked.
        await SendAsync<FormSavedResponse>(crc, HttpMethod.Put, $"/{unrecordedForm.Value:D}/values", SaveReason, new
        {
            values = new[]
            {
                new { item_id = Systolic, value = new { type = "quantity", value = 118m, unit = "mm[Hg]" }, expected_revision = 0 },
                new { item_id = Diastolic, value = new { type = "quantity", value = 76m, unit = "mm[Hg]" }, expected_revision = 0 },
            },
        }, HttpStatusCode.OK, ct);
        await SendAsync<FormQueryResponse>(monitor, HttpMethod.Post, $"/{form:D}/queries/{blankQuery.QueryId:D}/close", "Resolved blank cuff comment", null, HttpStatusCode.OK, ct);
        await RelayHostOutboxAsync();

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

        using var lockedBlankRaise = await monitor.SendAsync(Request(HttpMethod.Post, $"/{unrecordedForm.Value:D}/queries", "Attempted blank raise under lock", new
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

        foreach (var capability in new[] { "execution.query.raise", "execution.query.manage" })
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
            (Investigator, new[] { "collection.sign" }),
            (Monitor, new[] { "execution.query.raise", "execution.query.manage" }),
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
            (EvaluateEditChecksOnCollectionChanged.Name, new[] { "validation_run.record", "execution.query.raise", "execution.query.manage" }),
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
}
