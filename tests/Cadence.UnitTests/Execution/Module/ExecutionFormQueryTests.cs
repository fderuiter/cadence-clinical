using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.Protocol;
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
using Microsoft.EntityFrameworkCore.Infrastructure;

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

        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult(_records.GetValueOrDefault(instance));

        public Task<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>> FindManyAsync(
            TenantId tenant,
            IReadOnlyCollection<CollectionInstanceId> instances,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<CollectionInstanceId, CollectionInstanceRecord>>(
                instances.Distinct().Where(_records.ContainsKey).ToDictionary(id => id, id => _records[id]));

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);
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
}
