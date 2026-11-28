using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Application.Evidence;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Events;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Execution;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Modules.Execution.Application;
using Cadence.Modules.Execution.Domain;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

using Xunit;

namespace Cadence.UnitTests.Kernel.Evidence;

/// <summary>
/// Verifies that caller-facing collection and observation commands reject reserved internal
/// <see cref="NullFlavor.Masked"/> and <see cref="NullFlavor.NotAsked"/> values with HTTP <c>422</c>,
/// <see cref="RecordUnaskedCollectionItemHandler"/> remains authorized to record internal
/// <see cref="NullFlavor.NotAsked"/> placeholders, and SDV target queries and CRA modification alerts
/// exclude <see cref="NullFlavor.NotAsked"/> placeholders.
/// </summary>
[Trait("Category", "Unit")]
public sealed class ReservedNullFlavorAndSdvTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset T0 = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Clinician = Actor.Person("00u-crc-101");

    [Theory(DisplayName = "RecordCollectedValues_RejectsReservedNullFlavorsWith422_AndPreservesContentHash")]
    [InlineData(NullFlavor.Masked)]
    [InlineData(NullFlavor.NotAsked)]
    public async Task RecordCollectedValuesRejectsReservedNullFlavorsWith422AndPreservesContentHash(NullFlavor reservedFlavor)
    {
        var ct = TestContext.Current.CancellationToken;
        var studyId = StudyId.New();
        var studySiteId = StudySiteId.New();
        var enrollment = CreateEnrolled(studyId, studySiteId, "SUBJ-101");

        var sbpConcept = new ConceptRef(ConceptId.New(), 1);
        var design = new ProtocolDesign
        {
            SchemaVersion = 1,
            Title = "Vital Signs Guard Study",
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [new CollectionItem("sysbp", sbpConcept, Unit: "mm[Hg]", Required: true)]),
            ],
        };
        var version = CreateApprovedProtocolVersion(studyId, 1, design);
        var instance = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment, version.Id, "cs.vitals", null, T0.AddMinutes(2), Clinician.Id, T0.AddMinutes(2));

        var work = new InMemoryEvidenceWork(enrollment, version, [instance], []);
        var context = new StubCommandContext(Tenant, Clinician, T0.AddMinutes(5), work);
        var handler = new RecordCollectedValuesHandler();

        // First commit a valid baseline value so the form has a recorded ContentHash.
        var initial = await handler.HandleAsync(
            new RecordCollectedValues(
                Tenant,
                instance.Id,
                enrollment.Id,
                [new CollectedValue("sysbp", new QuantityValue(120m, "mm[Hg]"), ExpectedRevision: 0)],
                "key-initial",
                "Initial vital signs entry"),
            context,
            ct);
        initial.IsSuccess.Should().BeTrue();
        var baselineHash = CollectionInstance.ContentHashOf(
            work.Observations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)));
        baselineHash.Should().NotBeNullOrWhiteSpace();
        instance.Saves.Should().Be(1);

        // Attempting to overwrite with Masked or NotAsked fails with 422 K.Collection.ValuesInvalid and leaves observation untouched.
        var rejected = await handler.HandleAsync(
            new RecordCollectedValues(
                Tenant,
                instance.Id,
                enrollment.Id,
                [new CollectedValue("sysbp", new MissingValue(reservedFlavor), ExpectedRevision: 1)],
                "key-reserved",
                "Attempt reserved null flavor"),
            context,
            ct);

        rejected.IsSuccess.Should().BeFalse();
        rejected.Error!.Code.Should().Be(CollectionErrors.ValuesInvalid.Code);
        rejected.Error.HttpStatus.Should().Be(422);
        rejected.Error.Errors.Should().ContainKey("sysbp");
        work.Observations.Single().Value.Should().Be(new QuantityValue(120m, "mm[Hg]"));
        work.Observations.Single().Revision.Should().Be(1);
        instance.Saves.Should().Be(1);
        CollectionInstance.ContentHashOf(
            work.Observations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)))
            .Should().Be(baselineHash);

        // Recording an allowed MissingValue with Detail succeeds and updates the observation cleanly.
        var notDone = await handler.HandleAsync(
            new RecordCollectedValues(
                Tenant,
                instance.Id,
                enrollment.Id,
                [new CollectedValue("sysbp", new MissingValue(NullFlavor.NotDone, "Equipment cuff failure"), ExpectedRevision: 1)],
                "key-notdone",
                "Blood pressure cuff failed during visit"),
            context,
            ct);

        notDone.IsSuccess.Should().BeTrue();
        CollectionInstance.ContentHashOf(
            work.Observations.Select(o => new CollectionContentEntry(o.Id, o.Revision, o.Value)))
            .Should().NotBe(baselineHash);
        work.Observations.Single().Value.Should().Be(new MissingValue(NullFlavor.NotDone, "Equipment cuff failure"));
    }

    [Fact(DisplayName = "RecordUnaskedCollectionItem_RemainsAuthorizedToCreateNotAskedPlaceholder")]
    public async Task RecordUnaskedCollectionItemRemainsAuthorizedToCreateNotAskedPlaceholder()
    {
        var ct = TestContext.Current.CancellationToken;
        var studyId = StudyId.New();
        var studySiteId = StudySiteId.New();
        var enrollment = CreateEnrolled(studyId, studySiteId, "SUBJ-102");
        var design = new ProtocolDesign
        {
            SchemaVersion = 1,
            Title = "Unasked Placeholder Study",
            CollectionSpecs =
            [
                new CollectionSpec(
                    "cs.vitals",
                    "Vital Signs",
                    [new CollectionItem("diabp", new ConceptRef(ConceptId.New(), 1), Unit: "mm[Hg]", Required: true)]),
            ],
        };
        var version = CreateApprovedProtocolVersion(studyId, 1, design);
        var instance = CollectionInstance.Open(
            CollectionInstanceId.New(), enrollment, version.Id, "cs.vitals", null, T0.AddMinutes(2), Clinician.Id, T0.AddMinutes(2));

        var work = new InMemoryEvidenceWork(enrollment, version, [instance], []);
        var context = new StubCommandContext(Tenant, Clinician, T0.AddMinutes(3), work);
        var handler = new RecordUnaskedCollectionItemHandler();

        var result = await handler.HandleAsync(
            new RecordUnaskedCollectionItem(
                Tenant,
                instance.Id,
                enrollment.Id,
                "diabp",
                null,
                "key-unasked",
                "Raised missing data query on blank item"),
            context,
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be(new MissingValue(NullFlavor.NotAsked));
        result.Value.Revision.Should().Be(1);
    }

    [Theory(DisplayName = "ObservationHandlers_RejectReservedNullFlavorsWith422")]
    [InlineData(NullFlavor.Masked)]
    [InlineData(NullFlavor.NotAsked)]
    public async Task ObservationHandlersRejectReservedNullFlavorsWith422(NullFlavor reservedFlavor)
    {
        var ct = TestContext.Current.CancellationToken;
        var studyId = StudyId.New();
        var studySiteId = StudySiteId.New();
        var enrollment = CreateEnrolled(studyId, studySiteId, "SUBJ-103");

        var concept = new ConceptRef(ConceptId.New(), 1);
        var existing = Observation.Record(
            ObservationId.New(),
            Tenant,
            enrollment.Id,
            enrollment.ParticipantId,
            studyId,
            concept,
            new QuantityValue(78m, "mm[Hg]"),
            T0.AddMinutes(2),
            "Initial observation entry",
            Clinician.Id,
            T0.AddMinutes(2)).Value!;

        var version = CreateApprovedProtocolVersion(studyId, 1, new ProtocolDesign { SchemaVersion = 1, Title = "Standalone Obs Study" });
        var work = new InMemoryEvidenceWork(enrollment, version, [], [existing]);
        var context = new StubCommandContext(Tenant, Clinician, T0.AddMinutes(5), work);

        var recordResult = await new RecordObservationHandler().HandleAsync(
            new RecordObservation(
                Tenant,
                ObservationId.New(),
                enrollment.Id,
                concept,
                new MissingValue(reservedFlavor),
                T0.AddMinutes(4),
                "key-rec",
                "Attempt reserved observation record"),
            context,
            ct);

        recordResult.IsSuccess.Should().BeFalse();
        recordResult.Error!.Code.Should().Be(ObservationErrors.ValueInvalid.Code);
        recordResult.Error.HttpStatus.Should().Be(422);

        var correctResult = await new CorrectObservationHandler().HandleAsync(
            new CorrectObservation(
                Tenant,
                existing.Id,
                enrollment.Id,
                new MissingValue(reservedFlavor),
                ExpectedVersion: existing.VersionIndex,
                IdempotencyKey: "key-cor",
                ReasonForChange: "Attempt reserved observation correction"),
            context,
            ct);

        correctResult.IsSuccess.Should().BeFalse();
        correctResult.Error!.Code.Should().Be(ObservationErrors.ValueInvalid.Code);
        correctResult.Error.HttpStatus.Should().Be(422);
        existing.Value.Should().Be(new QuantityValue(78m, "mm[Hg]"));
        existing.Revision.Should().Be(1);
    }

    [Fact(DisplayName = "KernelObservationRecords_FindUnverifiedSdvTargets_ExcludesNotAskedPlaceholders")]
    public async Task KernelObservationRecordsFindUnverifiedSdvTargetsExcludesNotAskedPlaceholders()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = new DbContextOptionsBuilder<KernelDbContext>()
            .UseInMemoryDatabase($"sdv-notasked-{Guid.NewGuid():N}")
            .Options;
        var factory = new SimpleKernelDbContextFactory(options);
        await using var seed = new KernelDbContext(options);
        seed.BindTenant(Tenant);
        var studyId = StudyId.New();
        var enrollmentId = EnrollmentId.New();
        var participantId = ParticipantId.New();
        var concept = new ConceptRef(ConceptId.New(), 1);

        var unasked = Observation.Record(
            ObservationId.New(), Tenant, enrollmentId, participantId, studyId, concept,
            new MissingValue(NullFlavor.NotAsked), T0, "Placeholder for missing query", Clinician.Id, T0).Value!;
        var notDone = Observation.Record(
            ObservationId.New(), Tenant, enrollmentId, participantId, studyId, concept,
            new MissingValue(NullFlavor.NotDone, "Subject refused"), T0, "Recorded missing value", Clinician.Id, T0).Value!;
        var measured = Observation.Record(
            ObservationId.New(), Tenant, enrollmentId, participantId, studyId, concept,
            new QuantityValue(122m, "mm[Hg]"), T0, "Recorded systolic pressure", Clinician.Id, T0).Value!;

        seed.Observations.AddRange(unasked, notDone, measured);
        using ((IDisposable)typeof(KernelDbContext)
            .GetMethod("PermitPipelineWrites", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(seed, null)!)
        {
            await seed.SaveChangesAsync(ct);
        }

        IObservationRecords records = new KernelObservationRecords(new KernelTenantDbContextFactory(factory));
        var targets = await records.FindUnverifiedSdvTargetsAsync(
            Tenant,
            [unasked.Id, notDone.Id, measured.Id],
            (_, _) => ValueTask.FromResult(true),
            ct);

        targets.Keys.Should().BeEquivalentTo([notDone.Id, measured.Id]);
        targets.Keys.Should().NotContain(unasked.Id);
    }

    [Fact(DisplayName = "AlertMonitorOnVerifiedObservationCorrected_SkipsNotAskedPlaceholders")]
    public async Task AlertMonitorOnVerifiedObservationCorrectedSkipsNotAskedPlaceholders()
    {
        var ct = TestContext.Current.CancellationToken;
        var studyId = StudyId.New();
        var enrollmentId = EnrollmentId.New();
        var obsId = ObservationId.New();
        var concept = new ConceptRef(ConceptId.New(), 1);

        var notAskedRecord = new ObservationRecord(
            obsId,
            enrollmentId,
            ParticipantId.New(),
            studyId,
            concept,
            new MissingValue(NullFlavor.NotAsked),
            T0,
            1,
            [],
            [new ObservationRevisionView(1, new MissingValue(NullFlavor.NotAsked), "Placeholder created", Clinician.Id, T0, null)],
            [new AssertionView(AssertionId.New(), AssertionKind.Verified, NodeRef.For(obsId), 0, null, "cra@test", T0, "Verified rev 0", null, null)],
            [],
            CollectionInstanceId.New(),
            "diabp");

        var observations = Substitute.For<IObservationRecords>();
        observations.FindAsync(Tenant, obsId, Arg.Any<CancellationToken>()).Returns(notAskedRecord);
        var forms = Substitute.For<ICollectionInstanceRecords>();
        var sender = Substitute.For<IKernelCommandSender>();

        var consumer = new AlertMonitorOnVerifiedObservationCorrected(observations, forms, sender);
        var attr = typeof(ObservationCorrected).GetCustomAttribute<KernelEventAttribute>()!;
        var payload = JsonSerializer.SerializeToElement(
            new ObservationCorrected(Tenant, obsId, enrollmentId, 1, 0, new MissingValue(NullFlavor.NotAsked), "Placeholder created"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var envelope = new EventEnvelope(
            Guid.CreateVersion7(), attr.Type, attr.SchemaVersion, Tenant, null, T0, Clinician.Id, Guid.CreateVersion7(), Guid.CreateVersion7(), null, payload);

        await consumer.ConsumeAsync(new KernelEventDelivery("sdv-alert-probe", envelope), ct);

        await sender.DidNotReceiveWithAnyArgs().SendAsync<FormSdvAlertState>(default!, default, ct);
    }

    private static Enrollment CreateEnrolled(StudyId studyId, StudySiteId studySiteId, string subjectCode)
    {
        var enrollment = Enrollment.Screen(
            EnrollmentId.New(),
            Tenant,
            ParticipantId.New(),
            studyId,
            studySiteId,
            SubjectCode.From(subjectCode),
            T0,
            T0).Value!;
        enrollment.ChangeStatus(EnrollmentStatus.Enrolled, T0.AddMinutes(1), T0.AddMinutes(1), enrollmentNumber: 1).Should().BeNull();
        return enrollment;
    }

    private static ProtocolVersion CreateApprovedProtocolVersion(StudyId studyId, int number, ProtocolDesign design)
    {
        var json = ProtocolDesignDocument.Write(design);
        var pv = (ProtocolVersion)Activator.CreateInstance(typeof(ProtocolVersion), nonPublic: true)!;
        SetBackingField(pv, "Id", ProtocolVersionId.New());
        SetBackingField(pv, "TenantId", Tenant);
        SetBackingField(pv, "StudyId", studyId);
        SetBackingField(pv, "Number", number);
        SetBackingField(pv, "Status", ProtocolVersionStatus.Approved);

        for (var t = typeof(ProtocolVersion); t is not null; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(ProtocolDesign))
                {
                    field.SetValue(pv, design);
                }
                else if (field.FieldType == typeof(string)
                    && (field.Name.Contains("Content", StringComparison.OrdinalIgnoreCase)
                        || field.Name.Contains("Design", StringComparison.OrdinalIgnoreCase)
                        || field.Name.Contains("Json", StringComparison.OrdinalIgnoreCase)
                        || field.Name.Contains("Document", StringComparison.OrdinalIgnoreCase))
                    && !field.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase)
                    && !field.Name.Contains("Intent", StringComparison.OrdinalIgnoreCase)
                    && !field.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase))
                {
                    field.SetValue(pv, json);
                }
            }
        }

        return pv;
    }

    private static void SetBackingField<T>(T instance, string name, object? value)
    {
        for (var type = typeof(T); type is not null; type = type.BaseType)
        {
            var field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                field.SetValue(instance, value);
                return;
            }
        }
    }

    private sealed class SimpleKernelDbContextFactory(DbContextOptions<KernelDbContext> options) : IDbContextFactory<KernelDbContext>
    {
        public KernelDbContext CreateDbContext() => new(options);
    }

    private sealed class StubCommandContext(TenantId tenantId, Actor actor, DateTimeOffset recordedAt, IKernelUnitOfWork work) : ICommandContext
    {
        public TenantId TenantId => tenantId;
        public Actor Actor => actor;
        public DateTimeOffset RecordedAt => recordedAt;
        public IKernelUnitOfWork Work => work;
    }

    private sealed class InMemoryEvidenceWork(
        Enrollment enrollment,
        ProtocolVersion version,
        List<CollectionInstance> instances,
        List<Observation> observations) : IKernelUnitOfWork
    {
        public List<Observation> Observations { get; } = observations;

        public Task<TAggregate?> FindAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase =>
            ReadAsync<TAggregate>(id, cancellationToken);

        public Task<bool> ExistsAsync<TAggregate>(object id, CancellationToken cancellationToken)
            where TAggregate : AggregateRootBase
        {
            if (typeof(TAggregate) == typeof(Observation) && id is ObservationId oid)
            {
                return Task.FromResult(Observations.Any(o => o.Id == oid));
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

            if (typeof(TAggregate) == typeof(ProtocolVersion) && id is ProtocolVersionId vid && vid == version.Id)
            {
                return Task.FromResult<TAggregate?>((TAggregate)(object)version);
            }

            if (typeof(TAggregate) == typeof(CollectionInstance) && id is CollectionInstanceId cid)
            {
                var found = instances.FirstOrDefault(i => i.Id == cid);
                return Task.FromResult(found is null ? null : (TAggregate?)(object)found);
            }

            if (typeof(TAggregate) == typeof(Observation) && id is ObservationId oid)
            {
                var found = Observations.FirstOrDefault(o => o.Id == oid);
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
            if (typeof(TAggregate) == typeof(CollectionInstance))
            {
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. instances.Cast<TAggregate>().Where(compiled)]);
            }

            if (typeof(TAggregate) == typeof(Observation))
            {
                return Task.FromResult<IReadOnlyList<TAggregate>>([.. Observations.Cast<TAggregate>().Where(compiled)]);
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
            if (aggregate is Observation obs)
            {
                Observations.Add(obs);
            }
            else if (aggregate is CollectionInstance ci)
            {
                instances.Add(ci);
            }
        }

        public void Raise(object domainEvent, DateTimeOffset? occurredValid = null)
        {
        }

        public void LinkInput(NodeRef input, LineageEdgeKind kind, DateTimeOffset? validFrom = null, NodeRef? target = null)
        {
        }

        public void IncludePart(NodeRef part, bool newVersion)
        {
        }

        public void LinkPart(NodeRef from, NodeRef to, LineageEdgeKind kind)
        {
        }
    }
}
