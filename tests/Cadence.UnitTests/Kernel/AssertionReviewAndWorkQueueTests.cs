using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Api;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Application.WorkQueues;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence.WorkQueues;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.WorkQueues;
using Cadence.Security.Rbac;
using Cadence.TestSupport.Kernel.Protocol;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Cadence.UnitTests.Kernel;

/// <summary>
/// Unit tests for medical review (<see cref="AssertionKind.Reviewed"/>), retraction of reviews, HTTP review endpoint
/// behavior, and targeted verification / medical review work queue candidate evaluation with open-query and hard-lock exclusions.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AssertionReviewAndWorkQueueTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly StudyId Study = StudyId.New();
    private static readonly StudySiteId StudySite = StudySiteId.New();
    private static readonly ConceptId SystolicId = ConceptId.New();
    private static readonly ConceptId PulseId = ConceptId.New();
    private static readonly ConceptId NotesId = ConceptId.New();
    private static readonly ConceptRef Systolic = new(SystolicId, 1);
    private static readonly ConceptRef Pulse = new(PulseId, 1);
    private static readonly ConceptRef Notes = new(NotesId, 1);
    private static readonly DateTimeOffset BaseInstant = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReviewObservationAndRetract_EnforcesRevisionOpenQueryDuplicateAndHardLockInvariants()
    {
        var enrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            Study,
            StudySite,
            SubjectCode.From("SUBJ-REV-01"),
            BaseInstant,
            BaseInstant).Value!;
        var observation = Observation.Record(
            ObservationId.New(),
            Tenant,
            enrollment.Id,
            enrollment.ParticipantId,
            Study,
            Pulse,
            new QuantityValue(112m, "/min"),
            BaseInstant,
            "Initial pulse entry",
            "crc@test.org",
            BaseInstant).Value!;

        var work = new InMemoryAssertionWork(enrollment, observation);
        var context = new StubCommandContext(
            Tenant,
            Actor.Person("mm@sponsor.org"),
            BaseInstant.AddMinutes(10),
            work);
        var reviewHandler = new ReviewObservationHandler();
        var retractHandler = new RetractAssertionHandler();

        // 1. Stale revision is refused.
        var staleResult = await reviewHandler.HandleAsync(
            new ReviewObservation(Tenant, AssertionId.New(), enrollment.Id, observation.Id, Revision: 2, "idem-stale", "Medically reviewed"),
            context,
            TestContext.Current.CancellationToken);
        staleResult.IsSuccess.Should().BeFalse();
        staleResult.Error!.Code.Should().Be(AssertionErrors.RevisionStale.Code);

        // 2. Active open query refuses medical review until retracted.
        var openQuery = Assertion.Query(
            AssertionId.New(),
            observation,
            "Confirm whether pulse was measured supine.",
            "cra@test.org",
            BaseInstant.AddMinutes(5)).Value!;
        work.Add(openQuery);

        var blockedByQuery = await reviewHandler.HandleAsync(
            new ReviewObservation(Tenant, AssertionId.New(), enrollment.Id, observation.Id, Revision: 1, "idem-query", "Medically reviewed"),
            context,
            TestContext.Current.CancellationToken);
        blockedByQuery.IsSuccess.Should().BeFalse();
        blockedByQuery.Error!.Code.Should().Be(AssertionErrors.QueryOpen.Code);

        // Retract (close) the query so review can proceed.
        work.Add(Assertion.Retract(AssertionId.New(), openQuery, attestation: null, "cra@test.org", BaseInstant.AddMinutes(6)));

        // 3. Frozen scope STILL permits medical review during soft freeze cleanup.
        work.Add(Assertion.Lock(
            AssertionId.New(),
            Tenant,
            LockKind.Frozen,
            Study,
            enrollment.Id,
            NodeRef.For(enrollment.Id),
            AttestationId.New(),
            "dm@sponsor.org",
            BaseInstant.AddMinutes(7)));

        var reviewId = AssertionId.New();
        var reviewed = await reviewHandler.HandleAsync(
            new ReviewObservation(Tenant, reviewId, enrollment.Id, observation.Id, Revision: 1, "idem-rev-1", "Medically reviewed"),
            context,
            TestContext.Current.CancellationToken);
        reviewed.IsSuccess.Should().BeTrue(reviewed.Error?.Message);
        reviewed.Value!.AssertionId.Should().Be(reviewId);
        reviewed.Value.Kind.Should().Be(AssertionKind.Reviewed);
        reviewed.Value.Revision.Should().Be(1);
        work.LineageEdges.Should().ContainSingle(e =>
            e.Input == NodeRef.For(observation.Id, 1)
            && e.Kind == LineageEdgeKind.DerivedFrom
            && e.Target == NodeRef.For(reviewId));
        work.Raised.OfType<AssertionMade>().Should().ContainSingle(e =>
            e.AssertionId == reviewId && e.Kind == AssertionKind.Reviewed && e.Revision == 1);

        // 4. Duplicate active review at the same revision is refused.
        var duplicate = await reviewHandler.HandleAsync(
            new ReviewObservation(Tenant, AssertionId.New(), enrollment.Id, observation.Id, Revision: 1, "idem-dup", "Medically reviewed"),
            context,
            TestContext.Current.CancellationToken);
        duplicate.IsSuccess.Should().BeFalse();
        duplicate.Error!.Code.Should().Be(AssertionErrors.AlreadyReviewed.Code);

        // 5. Retracting the review succeeds once, refuses duplicate retraction, and allows re-review.
        var retractId = AssertionId.New();
        var retracted = await retractHandler.HandleAsync(
            new RetractAssertion(Tenant, retractId, enrollment.Id, reviewId, "idem-retract-1", "Withdrawn after chart clarification"),
            context,
            TestContext.Current.CancellationToken);
        retracted.IsSuccess.Should().BeTrue(retracted.Error?.Message);
        retracted.Value!.Kind.Should().Be(AssertionKind.Reviewed);
        retracted.Value.Retracts.Should().Be(reviewId);
        work.Raised.OfType<AssertionRetracted>().Should().ContainSingle(e =>
            e.AssertionId == retractId && e.RetractedId == reviewId && e.Kind == AssertionKind.Reviewed);

        var retractTwice = await retractHandler.HandleAsync(
            new RetractAssertion(Tenant, AssertionId.New(), enrollment.Id, reviewId, "idem-retract-2", "Duplicate withdraw"),
            context,
            TestContext.Current.CancellationToken);
        retractTwice.IsSuccess.Should().BeFalse();
        retractTwice.Error!.Code.Should().Be(AssertionErrors.AlreadyRetracted.Code);

        var reReviewed = await reviewHandler.HandleAsync(
            new ReviewObservation(Tenant, AssertionId.New(), enrollment.Id, observation.Id, Revision: 1, "idem-rev-2", "Re-reviewed"),
            context,
            TestContext.Current.CancellationToken);
        reReviewed.IsSuccess.Should().BeTrue(reReviewed.Error?.Message);

        // 6. Hard lock (Locked) refuses review and retraction.
        work.Add(Assertion.Lock(
            AssertionId.New(),
            Tenant,
            LockKind.Locked,
            Study,
            enrollment: null,
            NodeRef.For(Study),
            AttestationId.New(),
            "dm@sponsor.org",
            BaseInstant.AddMinutes(20)));

        observation.Correct(
            new QuantityValue(108m, "/min"),
            "Source chart update",
            "crc@test.org",
            BaseInstant.AddMinutes(15)).Should().BeNull();

        var refusedUnderHardLock = await reviewHandler.HandleAsync(
            new ReviewObservation(Tenant, AssertionId.New(), enrollment.Id, observation.Id, Revision: 2, "idem-locked", "Medically reviewed"),
            context,
            TestContext.Current.CancellationToken);
        refusedUnderHardLock.IsSuccess.Should().BeFalse();
        refusedUnderHardLock.Error!.Code.Should().Be(AssertionErrors.Locked.Code);
    }

    [Fact]
    public async Task PostObservationReviewsEndpoint_AsSponsorMedicalMonitor_RecordsActiveRevisionAndReturns409OnStaleRevision()
    {
        PermissionMatrix.RolesHolding("observation:review").Should().Contain("sponsor_mm");
        PermissionMatrix.RolesHolding("observation:review").Should().NotContain("crc");

        var enrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            Study,
            StudySite,
            SubjectCode.From("SUBJ-HTTP-01"),
            BaseInstant,
            BaseInstant).Value!;
        var observation = Observation.Record(
            ObservationId.New(),
            Tenant,
            enrollment.Id,
            enrollment.ParticipantId,
            Study,
            Pulse,
            new QuantityValue(98m, "/min"),
            BaseInstant,
            "Baseline pulse",
            "crc@test.org",
            BaseInstant).Value!;

        var work = new InMemoryAssertionWork(enrollment, observation);
        var commandContext = new StubCommandContext(
            Tenant,
            Actor.Person("sponsor_mm@cadence.test"),
            BaseInstant.AddMinutes(5),
            work);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton(NSubstitute.Substitute.For<IObservationRecords>());
        builder.Services.AddSingleton(NSubstitute.Substitute.For<INodeVisibility>());
        builder.Services.AddSingleton(NSubstitute.Substitute.For<ICurrentActor>());
        builder.Services.AddSingleton<IKernelTenantResolver>(new FixedTenantResolver(Tenant));
        builder.Services.AddSingleton<IKernelChangeReason>(new HeaderChangeReason());
        builder.Services.AddSingleton<IKernelCommandSender>(new DirectReviewCommandSender(commandContext));

        await using var app = builder.Build();
        app.MapGroup(KernelApiEndpointRouteBuilderExtensions.RoutePrefix).MapKernelObservations();
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        var observationRef = NodeRef.For(observation.Id).ToString();
        var enrollmentRef = NodeRef.For(enrollment.Id).ToString();

        // Active revision 1 -> 201 Created with Kind == "Reviewed"
        using var validRequest = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            $"/api/v1/kernel/observations/{observationRef}/reviews")
        {
            Content = JsonContent.Create(new KernelReviewObservationRequest(enrollmentRef, Revision: 1)),
        };
        validRequest.Headers.Add(KernelObservationEndpoints.IdempotencyKeyHeader, "idem-http-review-1");
        validRequest.Headers.Add("X-Reason-For-Change", "Clinically consistent with screening vitals");

        using var createdResponse = await client.SendAsync(validRequest, TestContext.Current.CancellationToken);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var state = await createdResponse.Content.ReadFromJsonAsync<KernelAssertionStateResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        state.Should().NotBeNull();
        state!.Kind.Should().Be(AssertionKind.Reviewed.ToString());
        state.Revision.Should().Be(1);
        state.AssertedBy.Should().Be("sponsor_mm@cadence.test");

        // Stale revision 2 -> 409 Conflict with K.Assertion.RevisionStale
        using var staleRequest = new System.Net.Http.HttpRequestMessage(
            System.Net.Http.HttpMethod.Post,
            $"/api/v1/kernel/observations/{observationRef}/reviews")
        {
            Content = JsonContent.Create(new KernelReviewObservationRequest(enrollmentRef, Revision: 2)),
        };
        staleRequest.Headers.Add(KernelObservationEndpoints.IdempotencyKeyHeader, "idem-http-review-stale");
        staleRequest.Headers.Add("X-Reason-For-Change", "Attempted review on stale revision");

        using var conflictResponse = await client.SendAsync(staleRequest, TestContext.Current.CancellationToken);
        conflictResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problemJson = JsonDocument.Parse(
            await conflictResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        problemJson.RootElement.GetProperty("code").GetString().Should().Be(AssertionErrors.RevisionStale.Code);
    }

    [Fact]
    public void ObservationReviewQueueLoader_FiltersByTargetedSdvAndMedicalReviewFlagsOpenQueriesAndHardLocks()
    {
        var design = ProtocolDesignSamples.MinimalDesign() with
        {
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [
                        new CollectionItem("item.sbp", Systolic, "mm[Hg]", Prompt: "Systolic BP", Required: true, SdvRequired: true, MedicalReviewRequired: false),
                        new CollectionItem("item.pulse", Pulse, "/min", Prompt: "Pulse Rate", Required: true, SdvRequired: true, MedicalReviewRequired: true),
                        new CollectionItem("item.notes", Notes, Prompt: "Assessment Comments", Required: false, SdvRequired: false, MedicalReviewRequired: false),
                    ]),
            ],
        };
        var version = ProtocolVersion.CreateFirst(
            ProtocolVersionId.New(),
            Tenant,
            Study,
            ProtocolDesignDocument.Write(design)).Value!;

        var enrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            Study,
            StudySite,
            SubjectCode.From("SUBJ-QUEUE-01"),
            BaseInstant,
            BaseInstant).Value!;
        var form = CollectionInstance.Open(
            CollectionInstanceId.New(),
            enrollment,
            version.Id,
            "cs.vitals",
            "act.day1",
            BaseInstant.AddMinutes(1),
            "crc@test.org",
            BaseInstant.AddMinutes(1));

        var sbpObs = Observation.Record(
            ObservationId.New(),
            Tenant,
            enrollment.Id,
            enrollment.ParticipantId,
            Study,
            Systolic,
            new QuantityValue(144m, "mm[Hg]"),
            BaseInstant.AddMinutes(2),
            "Initial SBP",
            "crc@test.org",
            BaseInstant.AddMinutes(2)).Value!;
        var pulseObs = Observation.Record(
            ObservationId.New(),
            Tenant,
            enrollment.Id,
            enrollment.ParticipantId,
            Study,
            Pulse,
            new QuantityValue(106m, "/min"),
            BaseInstant.AddMinutes(3),
            "Initial Pulse",
            "crc@test.org",
            BaseInstant.AddMinutes(3)).Value!;
        var notesObs = Observation.Record(
            ObservationId.New(),
            Tenant,
            enrollment.Id,
            enrollment.ParticipantId,
            Study,
            Notes,
            new TextValue("No acute distress"),
            BaseInstant.AddMinutes(4),
            "Initial comment",
            "crc@test.org",
            BaseInstant.AddMinutes(4)).Value!;

        form.Join("item.sbp", sbpObs.Id, BaseInstant.AddMinutes(2));
        form.Join("item.pulse", pulseObs.Id, BaseInstant.AddMinutes(3));
        form.Join("item.notes", notesObs.Id, BaseInstant.AddMinutes(4));

        var concepts = new Dictionary<ConceptId, string>
        {
            [SystolicId] = "Systolic BP",
            [PulseId] = "Pulse Rate",
            [NotesId] = "Assessment Comments",
        };
        var enrollments = new Dictionary<EnrollmentId, Enrollment> { [enrollment.Id] = enrollment };
        var observations = new[] { sbpObs, pulseObs, notesObs };

        // Raise an open query on sbpObs and apply a soft Freeze on the enrollment:
        // - sbpObs is excluded from SDV queue while queried;
        // - notesObs is excluded from SDV queue because SdvRequired == false;
        // - pulseObs appears in BOTH SDV queue and Medical Review queue even while Frozen!
        var sbpQuery = Assertion.Query(
            AssertionId.New(),
            sbpObs,
            "Confirm elevated systolic blood pressure.",
            "cra@test.org",
            BaseInstant.AddMinutes(5)).Value!;
        var softFreeze = Assertion.Lock(
            AssertionId.New(),
            Tenant,
            LockKind.Frozen,
            Study,
            enrollment.Id,
            NodeRef.For(enrollment.Id),
            AttestationId.New(),
            "dm@sponsor.org",
            BaseInstant.AddMinutes(6));

        var sdvWhileQueried = ObservationReviewQueueLoader.EvaluateCandidates(
            Tenant,
            observations,
            [sbpQuery, softFreeze],
            [form],
            [version],
            enrollments,
            concepts,
            AssertionKind.Verified,
            actionVerb: "Verify",
            requireMedicalReviewFlag: false,
            take: 25);
        sdvWhileQueried.Should().ContainSingle()
            .Which.Should().Match<WorkItemCandidate>(c =>
                c.Subject.Id == pulseObs.Id.Value
                && c.Title == "Verify Pulse Rate for SUBJ-QUEUE-01");

        var medicalReviewsDue = ObservationReviewQueueLoader.EvaluateCandidates(
            Tenant,
            observations,
            [sbpQuery, softFreeze],
            [form],
            [version],
            enrollments,
            concepts,
            AssertionKind.Reviewed,
            actionVerb: "Review",
            requireMedicalReviewFlag: true,
            take: 25);
        medicalReviewsDue.Should().ContainSingle()
            .Which.Should().Match<WorkItemCandidate>(c =>
                c.Subject.Id == pulseObs.Id.Value
                && c.Title == "Review Pulse Rate for SUBJ-QUEUE-01");

        // Close query on sbpObs and record medical review on pulseObs:
        // - sbpObs re-enters SDV queue;
        // - medical review queue is now empty;
        // - notesObs (SdvRequired: false) never enters SDV queue.
        var closeSbpQuery = Assertion.Retract(AssertionId.New(), sbpQuery, attestation: null, "cra@test.org", BaseInstant.AddMinutes(7));
        var reviewPulse = Assertion.Review(AssertionId.New(), pulseObs, 1, "mm@sponsor.org", BaseInstant.AddMinutes(8));

        var sdvAfterQueryClosed = ObservationReviewQueueLoader.EvaluateCandidates(
            Tenant,
            observations,
            [sbpQuery, closeSbpQuery, reviewPulse, softFreeze],
            [form],
            [version],
            enrollments,
            concepts,
            AssertionKind.Verified,
            actionVerb: "Verify",
            requireMedicalReviewFlag: false,
            take: 25);
        sdvAfterQueryClosed.Select(c => c.Subject.Id).Should().Equal(sbpObs.Id.Value, pulseObs.Id.Value);

        var medicalReviewsAfterReview = ObservationReviewQueueLoader.EvaluateCandidates(
            Tenant,
            observations,
            [sbpQuery, closeSbpQuery, reviewPulse, softFreeze],
            [form],
            [version],
            enrollments,
            concepts,
            AssertionKind.Reviewed,
            actionVerb: "Review",
            requireMedicalReviewFlag: true,
            take: 25);
        medicalReviewsAfterReview.Should().BeEmpty();

        // Hard lock on the study excludes all remaining candidates.
        var hardLock = Assertion.Lock(
            AssertionId.New(),
            Tenant,
            LockKind.Locked,
            Study,
            enrollment: null,
            NodeRef.For(Study),
            AttestationId.New(),
            "dm@sponsor.org",
            BaseInstant.AddMinutes(9));
        var sdvUnderHardLock = ObservationReviewQueueLoader.EvaluateCandidates(
            Tenant,
            observations,
            [sbpQuery, closeSbpQuery, hardLock],
            [form],
            [version],
            enrollments,
            concepts,
            AssertionKind.Verified,
            actionVerb: "Verify",
            requireMedicalReviewFlag: false,
            take: 25);
        sdvUnderHardLock.Should().BeEmpty();
    }

    private sealed class FixedTenantResolver(TenantId tenant) : IKernelTenantResolver
    {
        public ValueTask<KernelTenantResolution> ResolveAsync(ClaimsPrincipal caller, CancellationToken cancellationToken) =>
            ValueTask.FromResult(KernelTenantResolution.Resolved(tenant));
    }

    private sealed class HeaderChangeReason : IKernelChangeReason
    {
        public string? Read(HttpContext context) =>
            context.Request.Headers.TryGetValue("X-Reason-For-Change", out var value)
                ? value.ToString()
                : null;
    }

    private sealed class DirectReviewCommandSender(ICommandContext context) : IKernelCommandSender
    {
        private readonly ReviewObservationHandler _reviewHandler = new();

        public async Task<CommandResult<TResult>> SendAsync<TResult>(
            ICommand<TResult> command,
            CommandMetadata? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (command is ReviewObservation review)
            {
                var outcome = await _reviewHandler.HandleAsync(review, context, cancellationToken);
                return outcome.Value is { } state
                    ? CommandResult<TResult>.Success((TResult)(object)state)
                    : CommandResult<TResult>.Failure(outcome.Error!);
            }

            throw new NotSupportedException($"Unexpected command {command.GetType().Name}.");
        }
    }

    private sealed class StubCommandContext(
        TenantId tenantId,
        Actor actor,
        DateTimeOffset recordedAt,
        IKernelUnitOfWork work) : ICommandContext
    {
        public TenantId TenantId => tenantId;
        public Actor Actor => actor;
        public DateTimeOffset RecordedAt => recordedAt;
        public IKernelUnitOfWork Work => work;
    }

    private sealed class InMemoryAssertionWork(Enrollment enrollment, Observation observation) : IKernelUnitOfWork
    {
        private readonly List<Assertion> _assertions = [];
        private readonly List<CollectionInstance> _instances = [];

        public List<object> Raised { get; } = [];
        public List<(NodeRef Input, LineageEdgeKind Kind, NodeRef? Target)> LineageEdges { get; } = [];

        public Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            ReadAsync<TAggregate>(id, cancellationToken);

        public Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase
        {
            if (typeof(TAggregate) == typeof(Assertion) && id is AssertionId aid)
            {
                return Task.FromResult(_assertions.Any(a => a.Id == aid));
            }

            return Task.FromResult(false);
        }

        public Task<TAggregate?> ReadAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase
        {
            if (typeof(TAggregate) == typeof(Enrollment) && id is EnrollmentId eid && eid == enrollment.Id)
            {
                return Task.FromResult<TAggregate?>((TAggregate)(object)enrollment);
            }

            if (typeof(TAggregate) == typeof(Observation) && id is ObservationId oid && oid == observation.Id)
            {
                return Task.FromResult<TAggregate?>((TAggregate)(object)observation);
            }

            if (typeof(TAggregate) == typeof(Assertion) && id is AssertionId aid)
            {
                var found = _assertions.FirstOrDefault(a => a.Id == aid);
                return Task.FromResult(found is null ? null : (TAggregate?)(object)found);
            }

            return Task.FromResult<TAggregate?>(null);
        }

        public Task<IReadOnlyList<TAggregate>> ReadAllAsync<TAggregate>(
            Expression<Func<TAggregate, bool>> filter,
            CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase
        {
            var compiled = filter.Compile();
            if (typeof(TAggregate) == typeof(Assertion))
            {
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. _assertions.Cast<TAggregate>().Where(compiled)]);
            }

            if (typeof(TAggregate) == typeof(CollectionInstance))
            {
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. _instances.Cast<TAggregate>().Where(compiled)]);
            }

            return Task.FromResult<IReadOnlyList<TAggregate>>([]);
        }

        public Task LockAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            Task.CompletedTask;

        public Task LockCollectionAsync(CollectionInstanceId instance, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShareScopesAsync(IReadOnlyList<NodeRef> scopes, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task LockScopeAsync(NodeRef scope, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Add(AggregateRootBase aggregate)
        {
            if (aggregate is Assertion assertion)
            {
                _assertions.Add(assertion);
            }
            else if (aggregate is CollectionInstance instance)
            {
                _instances.Add(instance);
            }
        }

        public void Raise(object domainEvent, DateTimeOffset? occurredValid = null) => Raised.Add(domainEvent);

        public void LinkInput(NodeRef input, LineageEdgeKind kind, DateTimeOffset? validFrom = null, NodeRef? target = null) =>
            LineageEdges.Add((input, kind, target));

        public void IncludePart(NodeRef part, bool newVersion)
        {
        }

        public void LinkPart(NodeRef from, NodeRef to, LineageEdgeKind kind)
        {
        }
    }
}
