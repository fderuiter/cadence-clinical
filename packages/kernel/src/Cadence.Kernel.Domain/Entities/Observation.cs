using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Core.Domain;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Time;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// A recorded clinical fact (spec §8.1): this concept had this value for the participant of this enrollment, observed
/// at <see cref="ObservedAt"/>. It is the authoritative value; an eCRF field is a representation of it (ADR-2620).
/// <list type="bullet">
/// <item>Values are never overwritten (K-FACT-1). Each correction appends an insert-only
/// <see cref="ObservationRevision"/> with its reason; <see cref="Value"/> repeats the current revision.</item>
/// <item>A revision is recorded from its <see cref="ObservationRevision.RecordedAt"/> until the next one's, so the
/// pre-correction view is read without changing any history row.</item>
/// <item>Locks and judgements about it are <see cref="Assertion"/>s (spec §8.2, §10.5); the record and correction
/// commands read the locks in force under shared scope locks (ADR-2621).</item>
/// </list>
/// Expected failures return a <see cref="KernelError"/>.
/// </summary>
public class Observation : AggregateRoot<ObservationId>
{
    private readonly List<ObservationRevision> _revisions = [];

    protected Observation() { }

    private Observation(
        ObservationId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        ConceptRef concept,
        DateTimeOffset observedAt)
        : base(id)
    {
        TenantId = tenantId;
        EnrollmentId = enrollmentId;
        ParticipantId = participantId;
        StudyId = studyId;
        ConceptId = concept.ConceptId;
        ConceptVersion = concept.Version;
        ObservedAt = observedAt;
    }

    public TenantId TenantId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ParticipantId ParticipantId { get; private set; }

    public StudyId StudyId { get; private set; }

    public ConceptId ConceptId { get; private set; }

    public int ConceptVersion { get; private set; }

    /// <summary>What the observation means (K-SEM-1).</summary>
    public ConceptRef Concept => new(ConceptId, ConceptVersion);

    /// <summary>When the fact held in the trial world (valid time).</summary>
    public DateTimeOffset ObservedAt { get; private set; }

    /// <summary>The current value: the value of the latest revision (ADR-2623).</summary>
    public ObservationValue Value { get; private set; } = null!;

    /// <summary>The number of the latest revision.</summary>
    public int Revision { get; private set; }

    /// <summary>Every revision, oldest first.</summary>
    public IReadOnlyList<ObservationRevision> Revisions => _revisions;

    /// <summary>Records the first value, entered by <paramref name="recordedBy"/> at <paramref name="recordedAt"/>.</summary>
    public static CommandResult<Observation> Record(
        ObservationId id,
        TenantId tenantId,
        EnrollmentId enrollmentId,
        ParticipantId participantId,
        StudyId studyId,
        ConceptRef concept,
        ObservationValue? value,
        DateTimeOffset observedAt,
        string reasonForChange,
        string recordedBy,
        DateTimeOffset recordedAt)
    {
        if (ObservationValue.ProblemOf(value) is { } problem)
        {
            return ObservationErrors.ValueInvalid.WithMessage(problem);
        }

        var observed = KernelTimestamps.ToStoredPrecision(observedAt);
        var recorded = KernelTimestamps.ToStoredPrecision(recordedAt);
        if (observed > recorded)
        {
            return ObservationErrors.ObservedAtInvalid;
        }

        var observation = new Observation(id, tenantId, enrollmentId, participantId, studyId, concept, observed);
        observation.Append(value!, reasonForChange, recordedBy, recorded);
        return CommandResult<Observation>.Success(observation);
    }

    /// <summary>
    /// Supersedes the current value with <paramref name="value"/>. The caller has already checked the lock and the
    /// expected version; this refuses only an invalid or unchanged value.
    /// </summary>
    public KernelError? Correct(ObservationValue? value, string reasonForChange, string recordedBy, DateTimeOffset recordedAt)
    {
        if (string.IsNullOrWhiteSpace(reasonForChange) || reasonForChange.Trim().Length < 10)
        {
            return KernelErrors.ReasonForChangeRequired;
        }

        if (ObservationValue.ProblemOf(value) is { } problem)
        {
            return ObservationErrors.ValueInvalid.WithMessage(problem);
        }

        if (value == Value)
        {
            return ObservationErrors.ValueUnchanged;
        }

        Append(value!, reasonForChange, recordedBy, KernelTimestamps.ToStoredPrecision(recordedAt));
        return null;
    }

    private void Append(ObservationValue value, string reasonForChange, string recordedBy, DateTimeOffset recordedAt)
    {
        var number = _revisions.Count == 0 ? 1 : _revisions.Max(r => r.Number) + 1;
        _revisions.Add(new ObservationRevision(this, number, value, reasonForChange, recordedBy, recordedAt));
        Value = value;
        Revision = number;
    }
}

/// <summary>
/// One immutable value of an <see cref="Observation"/>, with who recorded it, when and why. Insert-only in the database.
/// </summary>
public class ObservationRevision
{
    protected ObservationRevision() { }

    internal ObservationRevision(Observation observation, int number, ObservationValue value, string reasonForChange, string recordedBy, DateTimeOffset recordedAt)
    {
        Id = Guid.CreateVersion7(recordedAt);
        TenantId = observation.TenantId;
        ObservationId = observation.Id;
        Number = number;
        Value = value;
        ReasonForChange = reasonForChange;
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public ObservationId ObservationId { get; private set; }

    /// <summary>1 for the recorded value, then one more per correction.</summary>
    public int Number { get; private set; }

    /// <summary>The value this revision recorded.</summary>
    public ObservationValue Value { get; private set; } = null!;

    public string ReasonForChange { get; private set; } = string.Empty;

    public string RecordedBy { get; private set; } = string.Empty;

    /// <summary>When the kernel recorded this value; it holds until the next revision's.</summary>
    public DateTimeOffset RecordedAt { get; private set; }
}
