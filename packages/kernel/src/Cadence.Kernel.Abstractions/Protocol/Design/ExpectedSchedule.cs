using System;
using System.Collections.Generic;
using System.Linq;

namespace Cadence.Kernel.Protocol.Design;

/// <summary>
/// A planned activity placed on the study-day axis: when it is due relative to the reference start, independent of
/// any participant (K-TIME-7, ADR-2487).
/// </summary>
/// <param name="ActivityId">The planned activity's element id.</param>
/// <param name="Name">The activity's name.</param>
/// <param name="EpochId">The epoch it belongs to.</param>
/// <param name="EpochOrder">The epoch's order in the design.</param>
/// <param name="StudyDay">The planned study day: day 1 is the reference start, day -1 the day before; there is no day 0.</param>
/// <param name="WindowBefore">Days allowed before the planned day.</param>
/// <param name="WindowAfter">Days allowed after the planned day.</param>
public sealed record PlannedVisit(
    string ActivityId,
    string Name,
    string EpochId,
    int EpochOrder,
    int StudyDay,
    int WindowBefore,
    int WindowAfter);

/// <summary>Where the evaluation date falls against a visit's window.</summary>
public enum VisitWindowStatus
{
    /// <summary>The window has not opened yet.</summary>
    NotYetOpen,

    /// <summary>The evaluation date is inside the window.</summary>
    Open,

    /// <summary>The window has closed.</summary>
    Closed,
}

/// <summary>A planned visit placed on the calendar for one reference start, with its window's state at the as-of date.</summary>
/// <param name="Visit">The planned visit.</param>
/// <param name="Target">The planned date.</param>
/// <param name="WindowStart">The first date allowed.</param>
/// <param name="WindowEnd">The last date allowed.</param>
/// <param name="Status">Where the as-of date falls against the window.</param>
public sealed record ExpectedVisit(PlannedVisit Visit, DateOnly Target, DateOnly WindowStart, DateOnly WindowEnd, VisitWindowStatus Status);

/// <summary>
/// The expected schedule (spec K-TIME-7, ADR-2487): a pure function of the protocol design, the anchor and the as-of
/// date. It is never stored as truth; a read model may cache it as a projection. Activities without a timing are
/// event-driven and are not on the schedule.
/// </summary>
public static class ExpectedSchedule
{
    /// <summary>
    /// The design's timed activities on the study-day axis, ordered by study day, then epoch order, then activity id.
    /// </summary>
    public static IReadOnlyList<PlannedVisit> Plan(ProtocolDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        var epochOrder = design.Epochs.ToDictionary(e => e.Id, e => e.Order, StringComparer.Ordinal);
        return
        [
            .. design.PlannedActivities
                .Where(a => a.Timing is not null)
                .Select(a => new PlannedVisit(
                    a.Id,
                    a.Name,
                    a.EpochId,
                    epochOrder.GetValueOrDefault(a.EpochId),
                    a.Timing!.Day,
                    a.Timing.WindowBefore,
                    a.Timing.WindowAfter))
                .OrderBy(v => Offset(v.StudyDay))
                .ThenBy(v => v.EpochOrder)
                .ThenBy(v => v.ActivityId, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The expected schedule for a participant whose reference start (study day 1) is <paramref name="referenceStart"/>,
    /// evaluated at <paramref name="asOf"/>. Both are dates in the site's calendar: the caller converts instants to the
    /// site's local date.
    /// </summary>
    public static IReadOnlyList<ExpectedVisit> Compute(ProtocolDesign design, DateOnly referenceStart, DateOnly asOf) =>
        [
            .. Plan(design).Select(visit =>
            {
                var target = DateOfStudyDay(referenceStart, visit.StudyDay);
                var start = target.AddDays(-visit.WindowBefore);
                var end = target.AddDays(visit.WindowAfter);
                return new ExpectedVisit(visit, target, start, end, StatusAt(asOf, start, end));
            }),
        ];

    /// <summary>
    /// The date of <paramref name="studyDay"/> for a reference start, following the SDTM <c>--DY</c> convention: day 1
    /// is the reference start and day -1 the day before. There is no day 0; a timing of day 0 is read as day 1.
    /// </summary>
    public static DateOnly DateOfStudyDay(DateOnly referenceStart, int studyDay) => referenceStart.AddDays(Offset(studyDay));

    private static VisitWindowStatus StatusAt(DateOnly asOf, DateOnly start, DateOnly end)
    {
        if (asOf < start)
        {
            return VisitWindowStatus.NotYetOpen;
        }

        return asOf > end ? VisitWindowStatus.Closed : VisitWindowStatus.Open;
    }

    private static int Offset(int studyDay) => studyDay > 0 ? studyDay - 1 : studyDay;
}
