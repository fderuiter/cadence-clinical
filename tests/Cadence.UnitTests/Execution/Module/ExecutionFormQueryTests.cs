using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Endpoints;
using Cadence.Modules.Execution.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Xunit;

namespace Cadence.UnitTests.Execution.Module;

/// <summary>
/// The query workflow without a database (#5461): which steps each status allows, the kernel assertion each step states,
/// and how a query read shows that its value changed.
/// </summary>
public sealed class ExecutionFormQueryTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Raising states a kernel query; answering answers it; reopening an answered query retracts the answer and keeps the
    /// kernel query; closing retracts the kernel query; reopening a closed one raises a new kernel query.
    /// </summary>
    [Fact(DisplayName = "FormQuery_Steps_StateTheMatchingKernelAssertions")]
    public void FormQueryStepsStateTheMatchingKernelAssertions()
    {
        var (query, raised) = Raise();
        raised.Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Raise, query.KernelQueryId, null, "Confirm the value"));
        var firstQuery = query.KernelQueryId;

        var answerId = AssertionId.New();
        query.Answer(" Confirmed ", answerId, "crc", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Answer, answerId, firstQuery, "Confirmed"));
        query.KernelAnswerId.Should().Be(answerId);

        var withdrawal = AssertionId.New();
        query.Reopen("Source reads 128", withdrawal, "cra", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Retract, withdrawal, answerId, null));
        query.Should().Match<FormQuery>(q => q.Status == FormQueryStatus.Open && q.KernelQueryId == firstQuery && q.KernelAnswerId == null);

        var close = AssertionId.New();
        query.Close("Corrected", close, "cra", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Retract, close, firstQuery, null));

        var second = AssertionId.New();
        query.Reopen("Still wrong", second, "cra", At)
            .Should().Be(new KernelAssertionStep(KernelAssertionStepKind.Raise, second, null, "Still wrong"));
        query.KernelQueryId.Should().Be(second);
        query.Messages.Select(m => m.Kind).Should().Equal(
            FormQueryMessageKind.Raised, FormQueryMessageKind.Answered, FormQueryMessageKind.Reopened, FormQueryMessageKind.Closed, FormQueryMessageKind.Reopened);
    }

    /// <summary>An open query cannot be reopened, a closed one cannot be answered or closed again, and a cancelled one allows nothing.</summary>
    [Fact(DisplayName = "FormQuery_RefusesStepsItsStatusDoesNotAllow")]
    public void FormQueryRefusesStepsItsStatusDoesNotAllow()
    {
        var (query, _) = Raise();
        query.Reopen("Why", AssertionId.New(), "cra", At).Should().BeNull();

        query.Close("Done", AssertionId.New(), "cra", At).Should().NotBeNull();
        query.Answer("Late", AssertionId.New(), "crc", At).Should().BeNull();
        query.Close("Again", AssertionId.New(), "cra", At).Should().BeNull();
        query.Cancel("Error", AssertionId.New(), "cra", At).Should().BeNull();

        var (cancelled, _) = Raise();
        cancelled.Cancel("Raised on the wrong value", AssertionId.New(), "cra", At).Should().NotBeNull();
        cancelled.Answer("Late", AssertionId.New(), "crc", At).Should().BeNull();
        cancelled.Reopen("Late", AssertionId.New(), "cra", At).Should().BeNull();
        cancelled.Status.Should().Be(FormQueryStatus.Cancelled);

        var empty = () => Raise(text: " ");
        var overlong = () => Raise(text: new string('x', 4001));
        empty.Should().Throw<ArgumentException>();
        overlong.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A query read shows the value changed once its current revision passed the queried one, the question as last
    /// reopened, and no current revision when the form no longer shows the value.
    /// </summary>
    [Fact(DisplayName = "FormQueryResponse_ShowsTheValueChanged_AndTheQuestionAsItStands")]
    public void FormQueryResponseShowsTheValueChangedAndTheQuestionAsItStands()
    {
        var (query, _) = Raise();
        query.Answer("Confirmed", AssertionId.New(), "crc", At);
        query.Reopen("Source reads 128", AssertionId.New(), "cra", At);
        var state = FormQueryState.Of(query, 3);

        var changed = FormQueryResponse.From(state, new Dictionary<ObservationId, int> { [query.ObservationId] = 2 });
        changed.Should().Match<FormQueryResponse>(r =>
            r.Status == "open" && r.CurrentRevision == 2 && r.ValueChanged && r.Text == "Source reads 128" && r.RaisedBy == "cra" && r.Version == 3);
        changed.Messages.Select(m => m.Kind).Should().Equal("raised", "answered", "reopened");

        FormQueryResponse.From(state, new Dictionary<ObservationId, int> { [query.ObservationId] = 1 }).ValueChanged.Should().BeFalse();
        FormQueryResponse.From(state, new Dictionary<ObservationId, int>()).Should().Match<FormQueryResponse>(r => r.CurrentRevision == null && !r.ValueChanged);
    }

    [Fact(DisplayName = "FormQueryEndpoints_ListQueries_ValidatesPaginationAndBatchesStudyVisibility")]
    public async Task FormQueryEndpointsListQueriesValidatesPaginationAndBatchesStudyVisibility()
    {
        var ct = TestContext.Current.CancellationToken;
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

        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(), http, forms, contexts, access, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(EnrollmentId: enrollmentA.Value, StudyId: study.Value), http, forms, contexts, access, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Limit: 0), http, forms, contexts, access, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Limit: FormQueryEndpoints.MaxQueryLimit + 1), http, forms, contexts, access, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Offset: -1), http, forms, contexts, access, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value, Status: "bogus"), http, forms, contexts, access, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var unknownStudy = StudyId.New();
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: unknownStudy.Value), http, forms, contexts, access, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        var hiddenAccess = new StubBatchReadAccess([]);
        (await FormQueryEndpoints.ListQueriesAsync(new FormQueryListFilter(StudyId: study.Value), http, forms, contexts, hiddenAccess, navigator, ct))
            .Result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        hiddenAccess.FilterVisibleCalls.Should().Be(1);

        // Filtering to site 022 (whose enrollmentB is not visible to caller) short-circuits with 200 [] after 1 batched visibility check.
        var filteredOut = await FormQueryEndpoints.ListQueriesAsync(
            new FormQueryListFilter(StudyId: study.Value, Site: "022"), http, forms, contexts, access, navigator, ct);
        filteredOut.Result.Should().BeOfType<Ok<IReadOnlyList<FormQueryResponse>>>().Which.Value.Should().BeEmpty();
        navigator.GetStudyEnrollmentsCalls.Should().Be(3);
        access.FilterVisibleCalls.Should().Be(1);
    }

    private static (FormQuery Query, KernelAssertionStep Step) Raise(string text = "Confirm the value") =>
        FormQuery.Raise(
            Guid.CreateVersion7(), TenantId.New(), CollectionInstanceId.New(), EnrollmentId.New(), "item.sbp", ObservationId.New(), 1, text, AssertionId.New(), "cra", At);

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
        public Task<CollectionInstanceRecord?> FindAsync(TenantId tenant, CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.FromResult<CollectionInstanceRecord?>(null);

        public Task<IReadOnlyList<CollectionInstanceSummary>> ListForEnrollmentAsync(TenantId tenant, EnrollmentId enrollment, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CollectionInstanceSummary>>([]);
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
