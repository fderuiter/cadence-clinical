using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Background;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Tenancy;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
        var queryTargets = new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>
        {
            [formA] = new(
                formA,
                enrollment,
                study,
                site,
                ProtocolVersionId.Empty,
                "cs.vitals",
                "act.week12",
                new Dictionary<ObservationId, int> { [obsA1] = 2, [obsA2] = 1 },
                new Dictionary<string, int>(StringComparer.Ordinal) { ["sbp"] = 2, ["dbp"] = 1 },
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<ObservationId>()),
            [formB] = new(
                formB,
                enrollment,
                study,
                site,
                ProtocolVersionId.Empty,
                "cs.ecg",
                "act.week12",
                new Dictionary<ObservationId, int> { [obsB1] = 1 },
                new Dictionary<string, int>(StringComparer.Ordinal) { ["hr"] = 1 },
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<ObservationId>()),
        };
        var forms = new RecordingQueryForms(
            headers,
            new Dictionary<(CollectionInstanceId, ObservationId), CollectionQueryTarget>(),
            projections,
            queryTargets);
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
        forms.FindQueryTargetsCalls.Should().Be(1);
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
        forms.FindQueryTargetsCalls.Should().Be(2);
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
        forms.FindQueryTargetsCalls.Should().Be(3);
        navigator.ResolveCalls.Should().Be(0);
        navigator.ResolveManyCalls.Should().Be(2);
    }

    [Fact(DisplayName = "FormQueryEndpoints_ListQueries_SkipsUnblindProbeForNonBlindedQueries_AndBatchesOnlyBlindedEnrollments")]
    public async Task FormQueryEndpointsListQueriesSkipsUnblindProbeForNonBlindedQueriesAndBatchesOnlyBlindedEnrollments()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(At);
        var tenant = TenantId.New();
        var study = StudyId.New();
        var siteA = StudySiteId.New();
        var siteB = StudySiteId.New();
        var enrollmentNonBlinded = EnrollmentId.New();
        var enrollmentMasked = EnrollmentId.New();
        var enrollmentUnblinded = EnrollmentId.New();
        var http = await ContextWithTenantAsync(tenant);
        var dbName = $"form-queries-blinding-{Guid.NewGuid():N}";
        var contexts = new InMemoryQueryContextFactory(dbName);

        var formNonBlindedId = CollectionInstanceId.New();
        var formMaskedId = CollectionInstanceId.New();
        var formUnblindedId = CollectionInstanceId.New();
        var obsNonBlinded1 = ObservationId.New();
        var obsNonBlinded2 = ObservationId.New();
        var obsMasked = ObservationId.New();
        var obsUnblinded = ObservationId.New();
        var sbpConcept = ConceptRef.New();
        var armConcept = ConceptRef.New();

        CollectionItem[] items =
        [
            new("item.sbp", sbpConcept, Required: true, ValueKind: ObservationValueKind.Quantity, Unit: "mmHg"),
            new("item.trt_arm", armConcept, Required: true, ValueKind: ObservationValueKind.Text),
        ];

        CollectionInstanceRecord BuildForm(
            CollectionInstanceId id,
            EnrollmentId enrollment,
            StudySiteId site,
            ObservationId sbpObs,
            ObservationId? armObs,
            IReadOnlySet<string> blindedItems,
            IReadOnlySet<ObservationId> blindedObs) =>
            new(
                id,
                enrollment,
                ParticipantId.New(),
                study,
                site,
                ProtocolVersionId.New(),
                "spec.day1",
                "act.day1",
                At,
                "crc@site.test",
                At,
                1,
                items,
                armObs is { } armId
                    ? [
                        new CollectedValueView("item.sbp", sbpObs, sbpConcept, new QuantityValue(122m, "mmHg"), At, 1, "crc@site.test", At),
                        new CollectedValueView("item.trt_arm", armId, armConcept, new TextValue("ARM-ACTIVE-CANARY"), At, 1, "irt@system.test", At),
                    ]
                    : [new CollectedValueView("item.sbp", sbpObs, sbpConcept, new QuantityValue(122m, "mmHg"), At, 1, "crc@site.test", At)],
                [],
                [],
                [],
                null,
                "Day 1 Visit",
                BlindedItemIds: blindedItems,
                BlindedObservationIds: blindedObs);

        var forms = new StubQueryForms(
        [
            BuildForm(
                formNonBlindedId,
                enrollmentNonBlinded,
                siteA,
                obsNonBlinded1,
                null,
                new HashSet<string>(StringComparer.Ordinal),
                new HashSet<ObservationId>()),
            BuildForm(
                formMaskedId,
                enrollmentMasked,
                siteA,
                obsNonBlinded2,
                obsMasked,
                new HashSet<string>(StringComparer.Ordinal) { "item.trt_arm" },
                new HashSet<ObservationId> { obsMasked }),
            BuildForm(
                formUnblindedId,
                enrollmentUnblinded,
                siteB,
                ObservationId.New(),
                obsUnblinded,
                new HashSet<string>(StringComparer.Ordinal) { "item.trt_arm" },
                new HashSet<ObservationId> { obsUnblinded }),
        ]);

        var navigator = new StubStudyNavigator(
            study,
            [
                new StudyEnrollmentNode(enrollmentNonBlinded, "017-0001", siteA, "017"),
                new StudyEnrollmentNode(enrollmentMasked, "017-0002", siteA, "017"),
                new StudyEnrollmentNode(enrollmentUnblinded, "022-0001", siteB, "022"),
            ]);
        var access = new StubBatchReadAccess(
            [
                NodeRef.For(study),
                NodeRef.For(enrollmentNonBlinded),
                NodeRef.For(enrollmentMasked),
                NodeRef.For(enrollmentUnblinded),
            ],
            [enrollmentUnblinded]);

        var nonBlindedQueryA = FormQuery.Raise(
            FormQueryId.New(), tenant, formNonBlindedId, enrollmentNonBlinded, "item.sbp", obsNonBlinded1, 1,
            "Confirm systolic reading", AssertionId.New(), "cra", At, "SDV").Query;
        var nonBlindedQueryB = FormQuery.Raise(
            FormQueryId.New(), tenant, formMaskedId, enrollmentMasked, "item.sbp", obsNonBlinded2, 1,
            "Verify cuff size", AssertionId.New(), "cra", At.AddMinutes(1), "SDV").Query;

        await using (var db = contexts.Create(tenant))
        {
            using var _ = db.PermitCommandWrites();
            db.FormQueries.AddRange(nonBlindedQueryA, nonBlindedQueryB);
            await db.SaveChangesAsync(ct);
        }

        // 1. Worklist containing only non-blinded queries must issue zero unblind checks.
        var nonBlindedList = await FormQueryEndpoints.ListQueriesAsync(
            new FormQueryListFilter(StudyId: study.Value), http, forms, contexts, access, navigator, clock, ct);
        var nonBlindedResponses = nonBlindedList.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Which.Value!;
        nonBlindedResponses.Should().HaveCount(2);
        nonBlindedResponses.Select(r => r.Text).Should().Contain(["Confirm systolic reading", "Verify cuff size"]);
        access.CanUnblindCalls.Should().Be(0);
        access.CanUnblindManyCalls.Should().Be(0);

        // 2. Adding blinded queries on two enrollments batches CanUnblindManyAsync once for ONLY those two enrollments.
        var maskedQuery = FormQuery.Raise(
            FormQueryId.New(), tenant, formMaskedId, enrollmentMasked, "item.trt_arm", obsMasked, 1,
            "Masked arm canary text", AssertionId.New(), "unblinded-dm", At.AddMinutes(2), "Kit check").Query;
        var unblindedQuery = FormQuery.Raise(
            FormQueryId.New(), tenant, formUnblindedId, enrollmentUnblinded, "item.trt_arm", obsUnblinded, 1,
            "Unblinded arm canary text", AssertionId.New(), "unblinded-dm", At.AddMinutes(3), "Kit check").Query;

        await using (var db = contexts.Create(tenant))
        {
            using var _ = db.PermitCommandWrites();
            db.FormQueries.AddRange(maskedQuery, unblindedQuery);
            await db.SaveChangesAsync(ct);
        }

        var mixedList = await FormQueryEndpoints.ListQueriesAsync(
            new FormQueryListFilter(StudyId: study.Value), http, forms, contexts, access, navigator, clock, ct);
        var mixedResponses = mixedList.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Which.Value!;
        mixedResponses.Should().HaveCount(4);
        access.CanUnblindCalls.Should().Be(0);
        access.CanUnblindManyCalls.Should().Be(1);
        access.LastUnblindEnrollmentBatch.Should().BeEquivalentTo([enrollmentMasked, enrollmentUnblinded]);

        var shapedMasked = mixedResponses.Single(r => r.QueryId == maskedQuery.Id.Value);
        shapedMasked.Text.Should().Be("masked");
        shapedMasked.RaisedBy.Should().Be("masked");
        shapedMasked.Messages.Should().OnlyContain(m => m.Text == "masked" && m.By == "masked");

        var shapedUnblinded = mixedResponses.Single(r => r.QueryId == unblindedQuery.Id.Value);
        shapedUnblinded.Text.Should().Be("Unblinded arm canary text");
        shapedUnblinded.ObservationId.Should().Be(obsUnblinded.Value);
        shapedUnblinded.RaisedBy.Should().Be("unblinded-dm");

        mixedResponses.Single(r => r.QueryId == nonBlindedQueryB.Id.Value).Text.Should().Be("Verify cuff size");
    }

    [Fact(DisplayName = "FormQuery_RoleFlipTransitions_ClearStaleAssigneeUnlessReplacementSupplied")]
    public void FormQueryRoleFlipTransitionsClearStaleAssigneeUnlessReplacementSupplied()
    {
        var (query, _) = Raise(assignee: " crc.alice ");
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Site);
        query.Assignee.Should().Be("crc.alice");

        // Answering flips OwnerRole from Site -> Monitor and clears stale site assignee when no replacement is supplied.
        query.Answer("Confirmed on source", AssertionId.New(), "crc.alice", At.AddHours(1), "Site chart check")
            .Should().NotBeNull();
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Monitor);
        query.Assignee.Should().BeNull();

        // Reopening with an explicit replacement assignee sets the new site assignee.
        query.Reopen("Source flowsheet reads 128", AssertionId.New(), "cra", At.AddHours(2), "Follow-up SDV", assignee: " crc.bob ")
            .Should().NotBeNull();
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Site);
        query.Assignee.Should().Be("crc.bob");

        // Answering with an explicit monitor assignee routes directly to that reviewer.
        query.Answer("Updated to 128", AssertionId.New(), "crc.bob", At.AddHours(3), "Corrected", assignee: " cra.lead ")
            .Should().NotBeNull();
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Monitor);
        query.Assignee.Should().Be("cra.lead");

        // Reopening without a replacement assignee clears the stale monitor assignee on Monitor -> Site flip.
        query.Reopen("Please attach ECG strip reference", AssertionId.New(), "cra.lead", At.AddHours(4), "Missing source note")
            .Should().NotBeNull();
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Site);
        query.Assignee.Should().BeNull();
    }

    [Fact(DisplayName = "FormQuery_Reassign_RecordsBitemporalPeriodsAndMessages_AndRefusesTerminalStates")]
    public void FormQueryReassignRecordsBitemporalPeriodsAndMessagesAndRefusesTerminalStates()
    {
        var (query, _) = Raise(assignee: "crc.alice");
        var tReassign1 = At.AddHours(2);
        var tReassign2 = At.AddHours(4);

        query.Reassign(" crc.bob ", null, "cra", tReassign1, "Coordinator shift handoff", "Handed off to evening coordinator")
            .Should().BeTrue();
        query.OwnerRole.Should().Be(FormQueryOwnerRole.Site);
        query.Assignee.Should().Be("crc.bob");
        query.Messages[^1].Should().Match<FormQueryMessage>(m =>
            m.Kind == FormQueryMessageKind.Reassigned
            && m.Text == "Handed off to evening coordinator"
            && m.ReasonForChange == "Coordinator shift handoff"
            && m.By == "cra");

        // Clearing assignee (unassigning) and changing OwnerRole to DataManager generates a default note and preserves bitemporal history.
        query.Reassign("   ", FormQueryOwnerRole.DataManager, "cra", tReassign2, "Escalated to data management review")
            .Should().BeTrue();
        query.OwnerRole.Should().Be(FormQueryOwnerRole.DataManager);
        query.Assignee.Should().BeNull();
        query.Messages[^1].Kind.Should().Be(FormQueryMessageKind.Reassigned);
        query.Messages[^1].ReasonForChange.Should().Be("Escalated to data management review");

        // Bitemporal StateAsOf reconstructs each prior assignee and owner role accurately.
        var asOfInitial = query.StateAsOf(At.AddHours(1), tReassign2)!;
        asOfInitial.Assignee.Should().Be("crc.alice");
        asOfInitial.OwnerRole.Should().Be(FormQueryOwnerRole.Site);

        var asOfFirstReassign = query.StateAsOf(tReassign1.AddMinutes(30), tReassign2)!;
        asOfFirstReassign.Assignee.Should().Be("crc.bob");
        asOfFirstReassign.OwnerRole.Should().Be(FormQueryOwnerRole.Site);

        var asOfSecondReassign = query.StateAsOf(tReassign2.AddMinutes(30), tReassign2.AddMinutes(30))!;
        asOfSecondReassign.Assignee.Should().BeNull();
        asOfSecondReassign.OwnerRole.Should().Be(FormQueryOwnerRole.DataManager);

        // Missing reason throws; terminal Cancelled/Closed queries refuse Reassign.
        var missingReason = () => query.Reassign("crc.carol", null, "cra", tReassign2.AddHours(1), " ");
        missingReason.Should().Throw<ArgumentException>();

        query.Cancel("Withdrawn", AssertionId.New(), "cra", tReassign2.AddHours(2), "Duplicate query").Should().NotBeNull();
        query.Reassign("crc.carol", null, "cra", tReassign2.AddHours(3), "Attempt after cancel").Should().BeFalse();
    }

    [Fact(DisplayName = "FormQueryEscalationHostedService_SkipsWhenLeaderLockBusy_AndSweepsActiveCaptureTenantsAsDedicatedServiceAccount")]
    public async Task FormQueryEscalationHostedServiceSkipsWhenLeaderLockBusyAndSweepsActiveCaptureTenantsAsDedicatedServiceAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = new FakeClock(new DateTimeOffset(2026, 6, 15, 8, 30, 0, TimeSpan.Zero)); // deid-ignore: fixed test instant
        var activeCaptureTenant = TenantId.New();
        var expiredTenant = TenantId.New();
        var nonCaptureTenant = TenantId.New();
        var studyId = StudyId.New();

        var tenants = new StubTenantDirectory([activeCaptureTenant, expiredTenant, nonCaptureTenant]);
        var studies = new StubStudyDirectory(new Dictionary<TenantId, IReadOnlyList<StudyDirectoryEntry>>
        {
            [activeCaptureTenant] =
            [
                new StudyDirectoryEntry(studyId, 1, "CAD-101", "Cadence 101", "Phase II", OrganizationId.New(), 1, "SPON", "Sponsor", 2),
            ],
        });
        var entitlements = new StubEntitlementReader(new Dictionary<TenantId, TenantEntitlementSnapshot>
        {
            [activeCaptureTenant] = new(
                activeCaptureTenant, SubscriptionId.New(), CommercialTier.Select, SubscriptionStatus.Active,
                At, null, IsReadOnly: false, [ProductFamily.Capture]),
            [expiredTenant] = new(
                expiredTenant, SubscriptionId.New(), CommercialTier.Individual, SubscriptionStatus.Expired,
                At, At.AddDays(14), IsReadOnly: true, [ProductFamily.Capture]),
            [nonCaptureTenant] = new(
                nonCaptureTenant, SubscriptionId.New(), CommercialTier.Select, SubscriptionStatus.Active,
                At, null, IsReadOnly: false, [ProductFamily.Vigil]),
        });

        var calls = new List<(TenantId Tenant, StudyId Study, Actor? ServiceActor, string Reason)>();
        var services = new ServiceCollection()
            .AddScoped<ModuleServiceActor>()
            .AddScoped<IFormQueryEscalationService>(sp => new RecordingEscalationService(
                sp.GetRequiredService<ModuleServiceActor>(),
                calls))
            .BuildServiceProvider();

        // 1. When another replica holds the advisory leader lock, no tenant or study is touched.
        var contended = new FormQueryEscalationHostedService(
            new StubLeaderLock(acquire: false),
            tenants,
            studies,
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            new FormQueryEscalationOptions(),
            NullLogger<FormQueryEscalationHostedService>.Instance,
            entitlements);

        var contendedSummary = await contended.RunSweepOnceAsync(ct);
        contendedSummary.Should().Be(new FormQueryEscalationSweepSummary(false, 0, 0, 0, 0));
        calls.Should().BeEmpty();

        // 2. Leader replica evaluates only active Capture-entitled tenants and executes under svc:execution-query-sla.
        var leader = new FormQueryEscalationHostedService(
            new StubLeaderLock(acquire: true),
            tenants,
            studies,
            services.GetRequiredService<IServiceScopeFactory>(),
            clock,
            new FormQueryEscalationOptions(),
            NullLogger<FormQueryEscalationHostedService>.Instance,
            entitlements);

        var summary = await leader.RunSweepOnceAsync(ct);
        summary.Should().Be(new FormQueryEscalationSweepSummary(
            AcquiredLeaderLock: true,
            TenantsVisited: 1,
            StudiesEvaluated: 1,
            EscalatedQueries: 2,
            DigestsGenerated: 2));

        calls.Should().ContainSingle().Which.Should().Be((
            activeCaptureTenant,
            studyId,
            Actor.ServiceAccount(FormQueryEscalationHostedService.ServiceAccountName),
            "Scheduled 14-day query SLA sweep for CAD-101 on 2026-06-15"));
    }

    [Fact(DisplayName = "FormQueryAgingDigestAlerts_MaskBlindedEntries_AndDeduplicateByIdempotencyKey")]
    public async Task FormQueryAgingDigestAlertsMaskBlindedEntriesAndDeduplicateByIdempotencyKey()
    {
        var ct = TestContext.Current.CancellationToken;
        var entry = new FormQueryAgingDigestEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "RAND_ARM",
            "SITE-01",
            "01-0001",
            "site",
            "high",
            At,
            At.AddDays(14),
            16);

        var masked = entry.MaskBlinded();
        masked.QueryId.Should().Be(Guid.Empty);
        masked.FormId.Should().Be(Guid.Empty);
        masked.ItemId.Should().Be(FormQueryAgingDigestEntry.MaskedLiteral);
        masked.SubjectCode.Should().Be("01-0001");

        var dispatcher = new InMemoryFormQueryAgingDigestAlertDispatcher(
            NullLogger<InMemoryFormQueryAgingDigestAlertDispatcher>.Instance);
        var tenant = TenantId.New();
        var study = StudyId.New();
        var digestId = Guid.NewGuid();
        var alert = new FormQueryAgingDigestAlert(
            IdempotencyKey: $"query-digest-alert:{tenant.Value:D}:{digestId:D}",
            TenantId: tenant,
            DigestId: digestId,
            StudyId: study,
            StudySiteId: null,
            ScopeKey: "study",
            RecipientRole: "study_lead",
            RecipientActorIds: ["lead-1", "lead-2"],
            DigestDate: new DateOnly(2026, 6, 15),
            GeneratedAt: At.AddDays(15),
            StudyCode: "CAD-101",
            SiteCode: null,
            EscalatedCount: 1,
            Entries: [masked]);

        (await dispatcher.SendAsync(alert, ct)).Should().BeTrue();
        (await dispatcher.SendAsync(alert, ct)).Should().BeFalse("replayed outbox deliveries with the same idempotency key must be suppressed");
        dispatcher.GetSentAlerts(tenant, study).Should().ContainSingle().Which.Entries.Single().ItemId.Should().Be("masked");
    }

    private static IDisposable PermitWrites(ModuleDbContext context) =>
        (IDisposable)typeof(ModuleDbContext)
            .GetMethod("PermitCommandWrites", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(context, null)!;

    private static (FormQuery Query, KernelAssertionStep Step) Raise(
        string text = "Confirm the value",
        FormQueryOrigin origin = FormQueryOrigin.Manual,
        string? assignee = null) =>
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
            origin,
            assignee: assignee);

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

    private sealed class InMemoryQueryContextFactory(string dbName) : IModuleTenantContextFactory<ExecutionModuleDbContext>
    {
        public ExecutionModuleDbContext Create(TenantId tenant)
        {
            var options = new DbContextOptionsBuilder<ExecutionModuleDbContext>()
                .UseInMemoryDatabase($"{dbName}-{tenant.Value:N}")
                .ReplaceService<IModelCustomizer, InMemoryQueryModelCustomizer>()
                .Options;
            return new ExecutionModuleDbContext(options);
        }
    }

    private sealed class InMemoryQueryModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            modelBuilder.Entity<FormQuery>().Ignore(q => q.Periods);
            modelBuilder.Ignore<FormQueryPeriod>();
        }
    }

    private sealed class StubQueryForms(IEnumerable<CollectionInstanceRecord>? records = null) : ICollectionInstanceRecords
    {
        private readonly Dictionary<CollectionInstanceId, CollectionInstanceRecord> _records =
            records?.ToDictionary(r => r.CollectionInstanceId) ?? [];

        public int FindCalls { get; private set; }

        public int FindHeadersCalls { get; private set; }

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult(_records.GetValueOrDefault(instance));
        }

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>>(
                instances.Distinct().Where(_records.ContainsKey).ToDictionary(id => id, id => _records[id]));

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>> FindHeadersAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            CancellationToken cancellationToken = default)
        {
            FindHeadersCalls++;
            return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader>>(
                instances
                    .Distinct()
                    .Where(_records.ContainsKey)
                    .ToDictionary(
                        id => id,
                        id => new CollectionInstanceHeader(
                            id,
                            _records[id].StudyId,
                            _records[id].StudySiteId,
                            _records[id].EnrollmentId,
                            _records[id].CollectionSpecId,
                            _records[id].PlannedActivityId)));
        }

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);
    }

    private sealed class RecordingQueryForms(
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceHeader> headers,
        IReadOnlyDictionary<(CollectionInstanceId, ObservationId), CollectionQueryTarget> targets,
        IReadOnlyDictionary<ObservationId, CollectionObservationProjection> projections,
        IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>? batchTargets = null) : ICollectionInstanceRecords
    {
        public int FindCalls { get; private set; }
        public int FindHeaderCalls { get; private set; }
        public int FindHeadersCalls { get; private set; }
        public int FindQueryTargetCalls { get; private set; }
        public int FindQueryTargetsCalls { get; private set; }
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

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>> FindQueryTargetsAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            IReadOnlySet<EnrollmentId> unblindedEnrollments,
            CancellationToken cancellationToken = default)
        {
            FindQueryTargetsCalls++;
            if (batchTargets is null)
            {
                return Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget>>(
                    new Dictionary<CollectionInstanceId, CollectionInstanceQueryTarget>());
            }

            IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceQueryTarget> found = instances
                .Distinct()
                .Where(batchTargets.ContainsKey)
                .ToDictionary(id => id, id => batchTargets[id]);
            return Task.FromResult(found);
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

    private sealed class StubBatchReadAccess(
        IEnumerable<NodeRef> visible,
        IEnumerable<EnrollmentId>? unblindedEnrollments = null) : IKernelReadAccess
    {
        private readonly HashSet<NodeRef> _visible = [.. visible.Select(n => n.Unversioned())];
        private readonly HashSet<EnrollmentId> _unblinded = [.. unblindedEnrollments ?? []];

        public int FilterVisibleCalls { get; private set; }

        public int CanUnblindCalls { get; private set; }

        public int CanUnblindManyCalls { get; private set; }

        public IReadOnlyList<EnrollmentId> LastUnblindEnrollmentBatch { get; private set; } = [];

        public ValueTask<bool> CanSeeAsync(TenantId tenant, NodeRef node, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_visible.Contains(node.Unversioned()));

        public ValueTask<IReadOnlySet<NodeRef>> FilterVisibleAsync(TenantId tenant, IReadOnlyCollection<NodeRef> nodes, CancellationToken cancellationToken)
        {
            FilterVisibleCalls++;
            return ValueTask.FromResult<IReadOnlySet<NodeRef>>(
                nodes.Select(n => n.Unversioned()).Where(_visible.Contains).ToHashSet());
        }

        public ValueTask<bool> CanUnblindAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken)
        {
            CanUnblindCalls++;
            return ValueTask.FromResult(_unblinded.Contains(enrollment));
        }

        public ValueTask<IReadOnlySet<EnrollmentId>> CanUnblindManyAsync(
            TenantId tenant,
            IReadOnlyCollection<EnrollmentId> enrollments,
            CancellationToken cancellationToken)
        {
            CanUnblindManyCalls++;
            LastUnblindEnrollmentBatch = [.. enrollments];
            return ValueTask.FromResult<IReadOnlySet<EnrollmentId>>(
                enrollments.Where(_unblinded.Contains).ToHashSet());
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

    private sealed class StubLeaderLock(bool acquire) : IKernelLeaderLock
    {
        public async Task<bool> TryRunAsync(string job, Func<CancellationToken, Task> pass, CancellationToken cancellationToken)
        {
            if (!acquire)
            {
                return false;
            }

            await pass(cancellationToken);
            return true;
        }
    }

    private sealed class StubTenantDirectory(IReadOnlyList<TenantId> active) : ITenantDirectory
    {
        public Task<TenantId?> FindActiveByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult<TenantId?>(null);

        public Task<IReadOnlyList<TenantId>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(active);
    }

    private sealed class StubStudyDirectory(IReadOnlyDictionary<TenantId, IReadOnlyList<StudyDirectoryEntry>> byTenant) : IStudyDirectory
    {
        public Task<IReadOnlyList<StudyDirectoryEntry>> ListStudiesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult(byTenant.TryGetValue(tenant, out var studies) ? studies : (IReadOnlyList<StudyDirectoryEntry>)[]);

        public Task<StudyDirectoryEntry?> FindStudyAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudyDirectoryEntry?>(null);

        public Task<StudyDirectoryEntry?> FindStudyByCodeAsync(TenantId tenant, string studyCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudyDirectoryEntry?>(null);

        public Task<IReadOnlyList<OrganizationDirectoryEntry>> ListOrganizationsAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrganizationDirectoryEntry>>([]);

        public Task<OrganizationDirectoryEntry?> FindOrganizationAsync(TenantId tenant, OrganizationId organizationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<OrganizationDirectoryEntry?> FindOrganizationByCodeAsync(TenantId tenant, string code, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrganizationDirectoryEntry?>(null);

        public Task<IReadOnlyList<SiteDirectoryEntry>> ListSitesAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SiteDirectoryEntry>>([]);

        public Task<SiteDirectoryEntry?> FindSiteAsync(TenantId tenant, SiteId siteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SiteDirectoryEntry?>(null);

        public Task<SiteDirectoryEntry?> FindSiteByCodeAsync(
            TenantId tenant,
            string siteCode,
            OrganizationId? institutionId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SiteDirectoryEntry?>(null);

        public Task<IReadOnlyList<StudySiteDirectoryEntry>> ListStudySitesAsync(TenantId tenant, StudyId studyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudySiteDirectoryEntry>>([]);

        public Task<StudySiteDirectoryEntry?> FindStudySiteAsync(TenantId tenant, StudySiteId studySiteId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudySiteDirectoryEntry?>(null);

        public Task<StudySiteDirectoryEntry?> FindStudySiteBySiteAsync(
            TenantId tenant,
            StudyId studyId,
            SiteId siteId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StudySiteDirectoryEntry?>(null);
    }

    private sealed class StubEntitlementReader(IReadOnlyDictionary<TenantId, TenantEntitlementSnapshot> byTenant) : ITenantEntitlementReader
    {
        public Task<TenantEntitlementSnapshot> GetEntitlementsAsync(
            TenantId tenantId,
            DateTimeOffset asOf,
            CancellationToken cancellationToken,
            DateTimeOffset? knownAt = null) =>
            Task.FromResult(byTenant[tenantId]);
    }

    private sealed class RecordingEscalationService(
        ModuleServiceActor serviceActor,
        List<(TenantId Tenant, StudyId Study, Actor? ServiceActor, string Reason)> calls) : IFormQueryEscalationService
    {
        public Task<CommandResult<FormQueryEscalationPassResult>> RunStudyPassAsync(
            TenantId tenantId,
            StudyId studyId,
            string? reasonForChange = null,
            CancellationToken cancellationToken = default)
        {
            calls.Add((tenantId, studyId, serviceActor.Actor, reasonForChange ?? string.Empty));
            var digests = new[]
            {
                new FormQueryAgingDigestState(Guid.NewGuid(), studyId, null, "study", "study_lead", new DateOnly(2026, 6, 15), At, "CAD-101", null, 2, []),
                new FormQueryAgingDigestState(Guid.NewGuid(), studyId, StudySiteId.New(), "site:1", "site_investigator", new DateOnly(2026, 6, 15), At, "CAD-101", "01", 2, []),
            };
            return Task.FromResult(CommandResult<FormQueryEscalationPassResult>.Success(new FormQueryEscalationPassResult(2, digests)));
        }
    }
}
