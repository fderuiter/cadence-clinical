using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Core.Domain;
using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.Modules.Execution.Domain;

/// <summary>
/// Raised when an opened scheduled eCRF form falls outside its planned visit window (<c>Early</c> or <c>Late</c>)
/// under <see cref="ExpectedSchedule.Assess"/> (ADR-2507, PRD-QLT-001).
/// </summary>
/// <param name="TenantId">The tenant.</param>
/// <param name="FormId">The opened form instance identifier.</param>
/// <param name="EnrollmentId">The participant enrollment identifier.</param>
/// <param name="StudyId">The study identifier.</param>
/// <param name="StudySiteId">The study-site identifier.</param>
/// <param name="ProtocolVersionId">The governing protocol version identifier.</param>
/// <param name="CollectionSpecId">The collection specification identifier.</param>
/// <param name="PlannedActivityId">The scheduled planned activity identifier.</param>
/// <param name="Category">Deviation category (<c>VISIT_WINDOW</c>).</param>
/// <param name="Timing">Assessed timing (<c>Early</c> or <c>Late</c>).</param>
/// <param name="TargetDate">Planned target date in site calendar time.</param>
/// <param name="WindowStart">First allowed date of the visit window.</param>
/// <param name="WindowEnd">Last allowed date of the visit window.</param>
/// <param name="ActualDate">Actual collection date in site calendar time.</param>
/// <param name="DaysFromTarget">Calendar days from target date (negative when early, positive when late).</param>
/// <param name="DaysOutsideWindow">Calendar days outside the allowed visit window.</param>
/// <param name="CollectedAt">Recorded collection timestamp.</param>
[KernelEvent("execution.VisitWindowDeviationDetected", 1)]
public sealed record VisitWindowDeviationDetected(
    TenantId TenantId,
    CollectionInstanceId FormId,
    EnrollmentId EnrollmentId,
    StudyId StudyId,
    StudySiteId StudySiteId,
    ProtocolVersionId ProtocolVersionId,
    string CollectionSpecId,
    string PlannedActivityId,
    string Category,
    string Timing,
    DateOnly TargetDate,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    DateOnly ActualDate,
    int DaysFromTarget,
    int DaysOutsideWindow,
    DateTimeOffset CollectedAt);

/// <summary>
/// Sink notified when an opened scheduled eCRF form is assessed as out of window (<c>VISIT_WINDOW</c>).
/// </summary>
public interface IVisitWindowDeviationSink
{
    /// <summary>Records or forwards a detected <c>VISIT_WINDOW</c> deviation event.</summary>
    ValueTask NotifyAsync(VisitWindowDeviationDetected deviation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Evaluates <see cref="ExpectedSchedule.Compute"/> and <see cref="ExpectedSchedule.Assess"/> for opened scheduled forms
/// in site local calendar time (ADR-2487, ADR-2507).
/// </summary>
internal static class VisitTimingEvaluator
{
    /// <summary>Canonical quality deviation category emitted for out-of-window scheduled forms.</summary>
    public const string VisitWindowCategory = "VISIT_WINDOW";

    public static VisitTimingAssessment? Assess(
        ProtocolDesign? design,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        DateTimeOffset? enrollmentReferenceAt)
    {
        if (design is null || string.IsNullOrWhiteSpace(plannedActivityId) || enrollmentReferenceAt is not { } anchor)
        {
            return null;
        }

        var referenceStart = DateOnly.FromDateTime(anchor.DateTime);
        var actualDate = DateOnly.FromDateTime(collectedAt.ToOffset(anchor.Offset).DateTime);
        var expected = ExpectedSchedule.Compute(design, referenceStart, actualDate)
            .FirstOrDefault(v => string.Equals(v.Visit.ActivityId, plannedActivityId, StringComparison.Ordinal));
        return expected is null ? null : ExpectedSchedule.Assess(expected, actualDate);
    }

    public static async Task<VisitTimingAssessment?> AssessAsync(
        TenantId tenant,
        ProtocolVersionId protocolVersionId,
        string? plannedActivityId,
        DateTimeOffset collectedAt,
        DateTimeOffset? enrollmentReferenceAt,
        IProtocolVersionReader? versions,
        CancellationToken cancellationToken)
    {
        if (versions is null || string.IsNullOrWhiteSpace(plannedActivityId) || enrollmentReferenceAt is null)
        {
            return null;
        }

        var design = await versions.GetDesignAsync(tenant, protocolVersionId, cancellationToken).ConfigureAwait(false);
        return Assess(design, plannedActivityId, collectedAt, enrollmentReferenceAt);
    }

    public static async Task<Dictionary<ProtocolVersionId, ProtocolDesign?>> LoadDesignsAsync(
        TenantId tenant,
        IEnumerable<ProtocolVersionId> versionIds,
        IProtocolVersionReader? versions,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<ProtocolVersionId, ProtocolDesign?>();
        if (versions is null)
        {
            return map;
        }

        foreach (var versionId in versionIds.Distinct())
        {
            map[versionId] = await versions.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false);
        }

        return map;
    }

    public static string? DeviationCategoryOf(VisitTimingAssessment? assessment) =>
        assessment is { Timing: VisitTiming.Early or VisitTiming.Late }
            ? VisitWindowCategory
            : null;
}
