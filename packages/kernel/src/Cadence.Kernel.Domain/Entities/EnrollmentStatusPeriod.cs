using System;

using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Execution;

namespace Cadence.Kernel.Domain.Entities;

/// <summary>
/// One bitemporal fact about an enrollment: it had <see cref="Status"/> during <see cref="TemporalPeriod.Valid"/>, as
/// Cadence knew it during <see cref="TemporalPeriod.Recorded"/> (spec §4.1). Rows are never changed except to close
/// <c>recorded_period</c> when a later command supersedes them (K-TIME-1), and two rows of one enrollment never overlap
/// in both times (K-TIME-2). The participant and study site are repeated so the database can keep a participant to one
/// active enrollment per study site at any valid instant.
/// </summary>
public class EnrollmentStatusPeriod : IBitemporalEntity
{
    protected EnrollmentStatusPeriod() { }

    internal EnrollmentStatusPeriod(Enrollment enrollment, EnrollmentStatus status, TemporalPeriod period)
    {
        Id = Guid.CreateVersion7(period.Recorded.Start);
        TenantId = enrollment.TenantId;
        EnrollmentId = enrollment.Id;
        ParticipantId = enrollment.ParticipantId;
        StudySiteId = enrollment.StudySiteId;
        Status = status;
        Period = period;
    }

    public Guid Id { get; private set; }

    public TenantId TenantId { get; private set; }

    public EnrollmentId EnrollmentId { get; private set; }

    public ParticipantId ParticipantId { get; private set; }

    public StudySiteId StudySiteId { get; private set; }

    public EnrollmentStatus Status { get; private set; }

    public TemporalPeriod Period { get; private set; }

    /// <summary>True while this row is both current knowledge and open-ended in valid time: the status now.</summary>
    internal bool IsCurrent => Period.Recorded.IsOpen && Period.Valid.IsOpen;

    /// <summary>Closes this row's recorded period at <paramref name="supersededAt"/>: the only change K-TIME-1 allows.</summary>
    internal void Supersede(DateTimeOffset supersededAt) => Period = Period.Supersede(supersededAt);
}
