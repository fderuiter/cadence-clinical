using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// Unit tests for the bitemporal <see cref="FormQuery"/> aggregate (<c>PRD-QRY-001</c>, <c>PRD-QRY-002</c>):
/// kernel assertion mirroring steps, separation of duties on closure, 14-day SLA due date and escalation,
/// and point-in-time <see cref="FormQuery.StateAsOf"/> reconstruction.
/// </summary>
public sealed class ExecutionFormQueryTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero); // deid-ignore: fixed test instant

    [Fact(DisplayName = "FormQuery_Steps_StateTheMatchingKernelAssertions")]
    public void FormQueryStepsStateTheMatchingKernelAssertions()
    {
        var (query, raised) = Raise();
        raised.Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Raise, query.KernelQueryId, null, "Confirm the value"));
        query.Origin.Should().Be(FormQueryOrigin.Manual);
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Site);
        query.DueAt.Should().Be(At.AddDays(14));
        var firstQuery = query.KernelQueryId;

        var answerId = AssertionId.New();
        query.Answer(" Confirmed ", answerId, "crc", At.AddHours(1), "Checked chart")
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Answer, answerId, firstQuery, "Confirmed"));
        query.KernelAnswerId.Should().Be(answerId);
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Monitor);
        query.DueAt.Should().BeNull();
        query.LastAnsweredBy.Should().Be("crc");

        var withdrawal = AssertionId.New();
        query.Reopen("Source reads 128", withdrawal, "cra", At.AddHours(2), "Source discrepancy")
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Retract, withdrawal, answerId, null));
        query.Should().Match<FormQuery>(q =>
            q.Status == FormQueryStatus.Open
            && q.OwnerRole == FormQueryOwnerRole.Site
            && q.DueAt == At.AddHours(2).AddDays(14)
            && q.KernelQueryId == firstQuery
            && q.KernelAnswerId == null);

        var secondAnswer = AssertionId.New();
        query.Answer("Updated to 128", secondAnswer, "crc", At.AddHours(3), "Corrected")
            .Should().NotBeNull();

        var close = AssertionId.New();
        query.Close("Corrected", close, "cra", At.AddHours(4), "Verified against source")
            .Step.Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Retract, close, firstQuery, null));

        var second = AssertionId.New();
        query.Reopen("Still wrong", second, "cra", At.AddHours(5), "Follow-up audit")
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Raise, second, null, "Still wrong"));
        query.KernelQueryId.Should().Be(second);
        query.Messages.Select(m => m.Kind).Should().Equal(
            FormQueryMessageKind.Raised,
            FormQueryMessageKind.Answered,
            FormQueryMessageKind.Reopened,
            FormQueryMessageKind.Answered,
            FormQueryMessageKind.Closed,
            FormQueryMessageKind.Reopened);
    }

    [Fact(DisplayName = "FormQuery_RefusesStepsItsStatusDoesNotAllow_AndEnforcesSeparationOfDuties")]
    public void FormQueryRefusesStepsItsStatusDoesNotAllowAndEnforcesSeparationOfDuties()
    {
        var (query, _) = Raise();
        query.Reopen("Why", AssertionId.New(), "cra", At).Should().BeNull();

        // Open manual queries cannot be closed directly.
        var openManualClose = query.Close("Premature close", AssertionId.New(), "cra", At);
        openManualClose.Step.Should().BeNull();
        openManualClose.Denial.Should().Be(FormQueryCloseDenial.InvalidTransition);

        // Answered manual queries cannot be closed by the same user who answered them.
        query.Answer("Answered by site", AssertionId.New(), "crc", At.AddMinutes(5)).Should().NotBeNull();
        var selfClose = query.Close("Self close", AssertionId.New(), "crc", At.AddMinutes(10));
        selfClose.Step.Should().BeNull();
        selfClose.Denial.Should().Be(FormQueryCloseDenial.SeparationOfDuties);

        // A distinct monitor closes successfully; once closed, no further answer/close/cancel is allowed.
        query.Close("Done", AssertionId.New(), "cra", At.AddMinutes(15)).Step.Should().NotBeNull();
        query.Answer("Late", AssertionId.New(), "crc", At.AddMinutes(20)).Should().BeNull();
        query.Close("Again", AssertionId.New(), "cra", At.AddMinutes(25)).Step.Should().BeNull();
        query.Cancel("Error", AssertionId.New(), "cra", At.AddMinutes(30)).Should().BeNull();

        // Open system queries can be auto-closed directly when resolved.
        var (systemQuery, _) = Raise(origin: FormQueryOrigin.System);
        systemQuery.Close("Rule passed", AssertionId.New(), "svc:execution-edit-checks", At.AddMinutes(5)).Step.Should().NotBeNull();

        // Open or answered queries can be cancelled as raised in error.
        var (cancelled, _) = Raise();
        cancelled.Cancel("Raised on the wrong value", AssertionId.New(), "cra", At.AddMinutes(5)).Should().NotBeNull();
        cancelled.Answer("Late", AssertionId.New(), "crc", At.AddMinutes(10)).Should().BeNull();
        cancelled.Reopen("Late", AssertionId.New(), "cra", At.AddMinutes(15)).Should().BeNull();
        cancelled.Status.Should().Be(FormQueryStatus.Cancelled);

        var empty = () => Raise(text: " ");
        var overlong = () => Raise(text: new string('x', 4001));
        empty.Should().Throw<ArgumentException>();
        overlong.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "FormQuery_EscalationAndStateAsOf_ReconstructsBitemporalHistory")]
    public void FormQueryEscalationAndStateAsOfReconstructsBitemporalHistory()
    {
        var (query, _) = Raise();

        // Before 14 days, escalation is refused.
        query.Escalate("cra", At.AddDays(13).AddHours(23)).Should().BeFalse();

        // At 14 days, escalation elevates priority to High and sets EscalatedAt.
        var escalateAt = At.AddDays(14).AddMinutes(1);
        query.Escalate("cra", escalateAt, "14-day SLA reached").Should().BeTrue();
        query.Priority.Should().Be(FormQueryPriority.High);
        query.EscalatedAt.Should().Be(escalateAt);

        // Answering clears EscalatedAt and moves ownership to Monitor.
        var answerAt = At.AddDays(15);
        query.Answer("Confirmed", AssertionId.New(), "crc", answerAt, "Answered after escalation").Should().NotBeNull();

        // Point-in-time reconstruction across bitemporal coordinates:
        query.StateAsOf(At.AddMinutes(-1), answerAt).Should().BeNull("query did not exist before OpenedAt");

        var atDay5 = query.StateAsOf(At.AddDays(5), answerAt)!;
        atDay5.Status.Should().Be(FormQueryStatus.Open);
        atDay5.Priority.Should().Be(FormQueryPriority.Normal);
        atDay5.EscalatedAt.Should().BeNull();
        atDay5.OwnerRole.Should().Be(FormQueryOwnerRole.Site);

        var atEscalated = query.StateAsOf(escalateAt.AddHours(2), answerAt)!;
        atEscalated.Status.Should().Be(FormQueryStatus.Open);
        atEscalated.Priority.Should().Be(FormQueryPriority.High);
        atEscalated.EscalatedAt.Should().Be(escalateAt);

        var atAnswered = query.StateAsOf(answerAt.AddHours(1), answerAt.AddHours(1))!;
        atAnswered.Status.Should().Be(FormQueryStatus.Answered);
        atAnswered.OwnerRole.Should().Be(FormQueryOwnerRole.Monitor);
        atAnswered.LastAnsweredBy.Should().Be("crc");
    }

    [Fact(DisplayName = "FormQueryResponse_ShowsTheValueChanged_AndTheQuestionAsItStands")]
    public void FormQueryResponseShowsTheValueChangedAndTheQuestionAsItStands()
    {
        var (query, _) = Raise();
        query.Answer("Confirmed", AssertionId.New(), "crc", At.AddMinutes(5));
        query.Reopen("Source reads 128", AssertionId.New(), "cra", At.AddMinutes(10));
        var state = FormQueryState.Of(query, 3);

        var changed = FormQueryResponse.From(state, new Dictionary<ObservationId, int> { [query.ObservationId] = 2 });
        changed.Should().Match<FormQueryResponse>(r =>
            r.Status == "open"
            && r.Origin == "manual"
            && r.OwnerRole == "site"
            && r.Priority == "normal"
            && r.CurrentRevision == 2
            && r.ValueChanged
            && r.Text == "Source reads 128"
            && r.RaisedBy == "cra"
            && r.Version == 3);
        changed.Messages.Select(m => m.Kind).Should().Equal("raised", "answered", "reopened");

        FormQueryResponse.From(state, new Dictionary<ObservationId, int> { [query.ObservationId] = 1 }).ValueChanged.Should().BeFalse();
        FormQueryResponse.From(state, new Dictionary<ObservationId, int>()).Should().Match<FormQueryResponse>(r => r.CurrentRevision == null && !r.ValueChanged);
    }

    [Fact(DisplayName = "FormQueryEndpoints_ListQueries_ValidatesPaginationAndBatchesStudyVisibility")]
    public async Task FormQueryEndpointsListQueriesValidatesPaginationAndBatchesStudyVisibility()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(At);
        var tenant = TenantId.New();
        var study = StudyId.New();
        var enrollmentA = EnrollmentId.New();
        var enrollmentB = EnrollmentId.New();
        var http = await ContextWithTenantAsync(tenant);
        var forms = new StubQueryForms();
        var contexts = new UnreachableContextFactory();
        var navigator = new StubStudyNavigator(
            study,
            [
                new StudyEnrollmentNode(enrollmentA, "017-0001", StudySiteId.New(), "017"),
                new StudyEnrollmentNode(enrollmentB, "022-0001", StudySiteId.New(), "022"),
            ]);
        var access = new StubBatchReadAccess([NodeRef.For(study), NodeRef.For(enrollmentA)]);

        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(EnrollmentId: enrollmentA.Value, StudyId: study.Value), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Limit: 0), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Limit: FormQueryEndpoints.MaxQueryLimit + 1), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Offset: -1), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Status: "bogus"), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Owner: "bogus"), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Origin: "bogus"), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Priority: "bogus"), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var unknownStudy = StudyId.New();
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: unknownStudy.Value), http, forms, contexts, access, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var hiddenAccess = new StubBatchReadAccess([]);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value), http, forms, contexts, hiddenAccess, navigator, clock, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        hiddenAccess.FilterVisibleCalls.Should().Be(1);

        // Filtering to site 022 (whose enrollmentB is not visible to caller) short-circuits with 200 [] after 1 batched visibility check.
        var filteredOut = await FormQueryEndpoints.ListQueriesAsync(
            new FormQueryListFilter(StudyId: study.Value, Site: "022"), http, forms, contexts, access, navigator, clock, ct);
        filteredOut.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Which.Value.Should().BeEmpty();
        navigator.GetStudyEnrollmentsCalls.Should().Be(3);
        access.FilterVisibleCalls.Should().Be(1);

        // Filtering visible enrollmentA out via participant or site short-circuits BEFORE opening ExecutionModuleDbContext or calling forms.
        var participantFilteredOut = await FormQueryEndpoints.ListQueriesAsync(
            new FormQueryListFilter(StudyId: study.Value, Participant: "999-9999"), http, forms, contexts, access, navigator, clock, ct);
        participantFilteredOut.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Which.Value.Should().BeEmpty();
        forms.FindCalls.Should().Be(0);
        forms.FindHeadersCalls.Should().Be(0);
    }

    [Fact(DisplayName = "FormQueryScopes_ShareAsync_LocksWidestFirstUsingHeader_AndSkipsFindAsync")]
    public async Task FormQueryScopesShareAsyncLocksWidestFirstUsingHeaderAndSkipsFindAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = TenantId.New();
        var study = StudyId.New();
        var site = StudySiteId.New();
        var enrollment = EnrollmentId.New();
        var formId = CollectionInstanceId.New();
        var obsId = ObservationId.New();
        var header = new CollectionInstanceHeader(formId, study, site, enrollment, "cs.vitals", "act.week12");
        var target = new CollectionQueryTarget(
            formId,
            study,
            site,
            enrollment,
            "cs.vitals",
            "act.week12",
            Version: 2,
            EnrollmentVersion: 1,
            IsLocked: false,
            ItemId: "sbp",
            ObservationId: obsId,
            Revision: 2);
        var forms = new RecordingQueryForms(
            new Dictionary<CollectionInstanceId, CollectionInstanceHeader> { [formId] = header },
            new Dictionary<(CollectionInstanceId, ObservationId), CollectionQueryTarget> { [(formId, obsId)] = target },
            new Dictionary<ObservationId, CollectionObservationProjection>());
        var sharedScopes = new List<NodeRef>();
        var work = Substitute.For<IKernelUnitOfWork>();
        work.ShareScopesAsync(Arg.Any<IReadOnlyList<NodeRef>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sharedScopes.AddRange(call.ArgAt<IReadOnlyList<NodeRef>>(0));
                forms.MarkScopesShared();
                return Task.CompletedTask;
            });
        var context = Substitute.For<ICommandContext>();
        context.TenantId.Returns(tenant);
        context.RecordedAt.Returns(At);
        context.Work.Returns(work);

        CollectionQueryTarget? shared;
        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            shared = await FormQueryScopes.ShareAsync(forms, context, formId, obsId, ct);
            reads.Count.Should().Be(0);
        }

        shared.Should().Be(target);
        forms.FindCalls.Should().Be(0);
        forms.FindHeaderCalls.Should().Be(1);
        forms.FindQueryTargetCalls.Should().Be(1);
        forms.QueryTargetCheckedUnderLock.Should().BeTrue();
        sharedScopes.Should().Equal(
            NodeRef.For(study),
            NodeRef.For(site),
            NodeRef.For(enrollment),
            NodeRef.For(formId),
            NodeRef.For(obsId));
    }

    [Fact(DisplayName = "FormQueryEndpoints_EnrollmentAndFormLists_BatchHeadersProjectionsAndNavigationWithoutFindAsync")]
    public async Task FormQueryEndpointsEnrollmentAndFormListsBatchHeadersProjectionsAndNavigationWithoutFindAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(At);
        var tenant = TenantId.New();
        var study = StudyId.New();
        var site = StudySiteId.New();
        var enrollment = EnrollmentId.New();
        var formA = CollectionInstanceId.New();
        var formB = CollectionInstanceId.New();
        var obsA1 = ObservationId.New();
        var obsA2 = ObservationId.New();
        var obsB1 = ObservationId.New();

        var (q1, _) = FormQuery.Raise(FormQueryId.New(), tenant, formA, enrollment, "sbp", obsA1, 1, "Confirm SBP", AssertionId.New(), "cra", At, "SDV");
        var (q2, _) = FormQuery.Raise(FormQueryId.New(), tenant, formA, enrollment, "dbp", obsA2, 1, "Confirm DBP", AssertionId.New(), "cra", At.AddMinutes(1), "SDV");
        var (q3, _) = FormQuery.Raise(FormQueryId.New(), tenant, formB, enrollment, "hr", obsB1, 1, "Confirm HR", AssertionId.New(), "cra", At.AddMinutes(2), "SDV");

        var dbName = $"exec-queries-{Guid.NewGuid():N}";
        var contexts = new InMemoryExecutionContextFactory(dbName);
        await using (var seed = contexts.Create(tenant))
        {
            using var _ = PermitWrites(seed);
            seed.FormQueries.AddRange(q1, q2, q3);
            await seed.SaveChangesAsync(ct);
        }

        var headers = new Dictionary<CollectionInstanceId, CollectionInstanceHeader>
        {
            [formA] = new(formA, study, site, enrollment, "cs.vitals", "act.week12"),
            [formB] = new(formB, study, site, enrollment, "cs.ecg", "act.week12"),
        };
        var projections = new Dictionary<ObservationId, CollectionObservationProjection>
        {
            [obsA1] = new(2, false),
            [obsA2] = new(1, false),
            [obsB1] = new(1, false),
        };
        var forms = new RecordingQueryForms(headers, new Dictionary<(CollectionInstanceId, ObservationId), CollectionQueryTarget>(), projections);
        var navigator = new RecordingNavigator(
            new Dictionary<NodeRef, NodeSummary>
            {
                [NodeRef.For(site)] = new(NodeRef.For(site), "Site 017", "017"),
                [NodeRef.For(enrollment)] = new(NodeRef.For(enrollment), "017-0001", "017-0001", "Screened"),
            },
            study,
            [new StudyEnrollmentNode(enrollment, "017-0001", site, "017")]);
        var access = new StubBatchReadAccess([NodeRef.For(study), NodeRef.For(enrollment), NodeRef.For(obsA1), NodeRef.For(obsA2), NodeRef.For(obsB1)]);
        var http = await ContextWithTenantAsync(tenant);

        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            var byStudy = await FormQueryEndpoints.ListQueriesAsync(
                new FormQueryListFilter(StudyId: study.Value), http, forms, contexts, access, navigator, clock, ct);
            var studyOk = byStudy.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Subject;
            studyOk.Value.Should().HaveCount(3);
            studyOk.Value.Single(q => q.QueryId == q1.Id.Value).Should().Match<FormQueryResponse>(r =>
                r.SiteCode == "017" && r.SubjectCode == "017-0001" && r.CollectionSpecId == "cs.vitals" && r.CurrentRevision == 2 && r.ValueChanged);
            reads.Count.Should().Be(0);
        }

        forms.FindCalls.Should().Be(0);
        forms.FindHeadersCalls.Should().Be(1);
        forms.FindObservationProjectionsCalls.Should().Be(1);
        navigator.ResolveCalls.Should().Be(0);
        navigator.ResolveManyCalls.Should().Be(0, "study worklist reuses site and subject codes from StudyEnrollmentNode");

        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            var byEnrollment = await FormQueryEndpoints.ListQueriesAsync(
                new FormQueryListFilter(EnrollmentId: enrollment.Value), http, forms, contexts, access, navigator, clock, ct);
            var ok = byEnrollment.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Subject;
            ok.Value.Should().HaveCount(3);
            ok.Value.Single(q => q.QueryId == q1.Id.Value).Should().Match<FormQueryResponse>(r =>
                r.SiteCode == "017" && r.SubjectCode == "017-0001" && r.CollectionSpecId == "cs.vitals" && r.CurrentRevision == 2 && r.ValueChanged);
            reads.Count.Should().Be(0);
        }

        forms.FindCalls.Should().Be(0);
        forms.FindHeadersCalls.Should().Be(2);
        forms.FindObservationProjectionsCalls.Should().Be(2);
        navigator.ResolveCalls.Should().Be(0);
        navigator.ResolveManyCalls.Should().Be(1);
        navigator.LastResolvedBatch.Should().BeEquivalentTo([NodeRef.For(site), NodeRef.For(enrollment)]);

        using (var reads = ProtocolDesignDocument.MeasureReads())
        {
            var byForm = await FormQueryEndpoints.ListForFormAsync(
                formA.Value, new FormQueryTemporalFilter(), http, forms, contexts, access, navigator, clock, ct);
            var ok = byForm.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Subject;
            ok.Value.Should().HaveCount(2);
            reads.Count.Should().Be(0);
        }

        forms.FindCalls.Should().Be(0);
        forms.FindHeaderCalls.Should().Be(1);
        forms.FindObservationProjectionsCalls.Should().Be(3);
        navigator.ResolveCalls.Should().Be(0);
        navigator.ResolveManyCalls.Should().Be(2);
    }

    private static IDisposable PermitWrites(ModuleDbContext context) =>
        (IDisposable)typeof(ModuleDbContext)
            .GetMethod("PermitCommandWrites", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, null)!;

    private static (FormQuery Query, KernelAssertionStep Step) Raise(
        string text = "Confirm the value",
        FormQueryOrigin origin = FormQueryOrigin.Manual) =>
        FormQuery.Raise(
            FormQueryId.New(),
            TenantId.New(),
            CollectionInstanceId.New(),
            EnrollmentId.New(),
            "item.sbp",
            ObservationId.New(),
            1,
            text,
            AssertionId.New(),
            "cra",
            At,
            "Source data verification",
            origin);

    private static async Task<HttpContext> ContextWithTenantAsync(TenantId tenant)
    {
        var http = new DefaultHttpContext();
        var filter = new KernelTenantFilter(new FixedTenantResolver(tenant));
        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), static _ => ValueTask.FromResult<object?>(null));
        return http;
    }

    private sealed class FixedTenantResolver(TenantId tenant) : IKernelTenantResolver
    {
        public ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(KernelTenantResolution.Resolved(tenant));
    }

    private sealed class UnreachableContextFactory : IModuleTenantContextFactory<ExecutionModuleDbContext>
    {
        public ExecutionModuleDbContext Create(TenantId tenant) =>
            throw new InvalidOperationException("Database should not be queried when validation or visibility short-circuits.");
    }

    private sealed class StubQueryForms : ICollectionInstanceRecords
    {
        public int FindCalls { get; private set; }
        public int FindHeadersCalls { get; private set; }

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult<CollectionInstanceRecord?>(null);
        }

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>> FindHeadersAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            CancellationToken cancellationToken = default)
        {
            FindHeadersCalls++;
            return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>>(
                new Dictionary<CollectionInstanceId, CollectionInstanceHeader>());
        }

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);
    }

    private sealed class RecordingQueryForms(
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader> headers,
        IReadOnlyDictionary<(CollectionInstanceId, ObservationId), CollectionQueryTarget> targets,
        IReadOnlyDictionary<ObservationId, CollectionObservationProjection> projections) : ICollectionInstanceRecords
    {
        public int FindCalls { get; private set; }
        public int FindHeaderCalls { get; private set; }
        public int FindHeadersCalls { get; private set; }
        public int FindQueryTargetCalls { get; private set; }
        public int FindObservationProjectionsCalls { get; private set; }
        public bool QueryTargetCheckedUnderLock { get; private set; }
        private bool _scopesShared;

        public void MarkScopesShared() => _scopesShared = true;

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
        {
            FindCalls++;
            throw new InvalidOperationException("Full FindAsync must not be called on slim query paths.");
        }

        public Task<CollectionInstanceHeader?> FindHeaderAsync(
            TenantId tenant,
            CollectionInstanceId instance,
            CancellationToken cancellationToken = default)
        {
            FindHeaderCalls++;
            return Task.FromResult(headers.TryGetValue(instance, out var header) ? header : null);
        }

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>> FindHeadersAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            CancellationToken cancellationToken = default)
        {
            FindHeadersCalls++;
            IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader> found = instances
                .Distinct()
                .Where(headers.ContainsKey)
                .ToDictionary(id => id, id => headers[id]);
            return Task.FromResult(found);
        }

        public Task<CollectionQueryTarget?> FindQueryTargetAsync(
            TenantId tenant,
            CollectionInstanceId instance,
            ObservationId? observation = null,
            string? itemId = null,
            bool checkLocks = true,
            CancellationToken cancellationToken = default)
        {
            FindQueryTargetCalls++;
            QueryTargetCheckedUnderLock = _scopesShared && checkLocks;
            return Task.FromResult(
                observation is { } obs && targets.TryGetValue((instance, obs), out var target) ? target : null);
        }

        public Task<IReadOnlyDictionary<ObservationId, CollectionObservationProjection>> FindObservationProjectionsAsync(
            TenantId tenant,
            IReadOnlyCollection<ObservationId> observations,
            IReadOnlyCollection<CollectionInstanceId>? instances = null,
            CancellationToken cancellationToken = default)
        {
            FindObservationProjectionsCalls++;
            IReadOnlyDictionary<ObservationId, CollectionObservationProjection> found = observations
                .Distinct()
                .Where(projections.ContainsKey)
                .ToDictionary(id => id, id => projections[id]);
            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);
    }

    private sealed class InMemoryExecutionContextFactory(string databaseName) : IModuleTenantContextFactory<ExecutionModuleDbContext>
    {
        public ExecutionModuleDbContext Create(TenantId tenant)
        {
            var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;
            return new ExecutionModuleDbContext(options);
        }
    }

    private sealed class RecordingNavigator(
        IReadOnlyDictionary<NodeRef, NodeSummary> summaries,
        StudyId? knownStudy = null,
        IReadOnlyList<StudyEnrollmentNode>? enrollments = null) : IKernelNavigator
    {
        public int ResolveCalls { get; private set; }
        public int ResolveManyCalls { get; private set; }
        public IReadOnlyList<NodeRef> LastResolvedBatch { get; private set; } = [];

        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default)
        {
            ResolveCalls++;
            return Task.FromResult(summaries.GetValueOrDefault(node.Unversioned()));
        }

        public Task<IReadOnlyDictionary<NodeRef, NodeSummary>> ResolveManyAsync(
            TenantId tenant,
            IReadOnlyCollection<NodeRef> nodes,
            CancellationToken cancellationToken = default)
        {
            ResolveManyCalls++;
            var distinct = nodes.Select(n => n.Unversioned()).Distinct().ToList();
            LastResolvedBatch = distinct;
            IReadOnlyDictionary<NodeRef, NodeSummary> found = distinct
                .Where(summaries.ContainsKey)
                .ToDictionary(n => n, n => summaries[n]);
            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<StudyEnrollmentNode>?> GetStudyEnrollmentsAsync(
            TenantId tenant,
            StudyId study,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudyEnrollmentNode>?>(knownStudy == study ? enrollments ?? [] : null);

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ZoomStep>?>(null);

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScopeChild>?>(null);
    }

    private sealed class StubBatchReadAccess(IEnumerable<NodeRef> visible) : IKernelReadAccess
    {
        private readonly HashSet<NodeRef> _visible = [.. visible.Select(n => n.Unversioned())];
        public int FilterVisibleCalls { get; private set; }

        public ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_visible.Contains(node.Unversioned()));

        public ValueTask<IReadOnlySet<NodeRef>> FilterVisibleAsync(TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            FilterVisibleCalls++;
            return ValueTask.FromResult<IReadOnlySet<NodeRef>>(
                nodes.Select(n => n.Unversioned()).Where(_visible.Contains).ToHashSet());
        }
    }

    private sealed class StubStudyNavigator(StudyId knownStudy, IReadOnlyList<StudyEnrollmentNode> enrollments) : IKernelNavigator
    {
        public int GetStudyEnrollmentsCalls { get; private set; }

        public Task<NodeSummary?> ResolveAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<NodeSummary?>(null);

        public Task<IReadOnlyList<ZoomStep>?> GetZoomPathAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ZoomStep>?>(null);

        public Task<IReadOnlyList<ScopeChild>?> GetChildrenAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScopeChild>?>(null);

        public Task<IReadOnlyList<StudyEnrollmentNode>?> GetStudyEnrollmentsAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken = default)
        {
            GetStudyEnrollmentsCalls++;
            return Task.FromResult<IReadOnlyList<StudyEnrollmentNode>?>(study == knownStudy ? enrollments : null);
        }
    }
}
