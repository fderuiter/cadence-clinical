using System.Collections.Generic;

using Cadence.Core.Domain;

namespace Cadence.Apps.Execution.Domain.Subjects;

/// <summary>
/// Rule 9's allowed-transition graph (`guard_subject_transition`).
/// @req:PRD-SUB-001
/// </summary>
public static class SubjectTransitionGraph
{
    private static readonly IReadOnlyDictionary<SubjectState, IReadOnlySet<SubjectState>> Allowed =
        new Dictionary<SubjectState, IReadOnlySet<SubjectState>>
        {
            [SubjectState.Screening] = new HashSet<SubjectState> { SubjectState.ScreenFailed, SubjectState.Enrolled, SubjectState.Withdrawn, SubjectState.ReconsentRequired },
            [SubjectState.Enrolled] = new HashSet<SubjectState> { SubjectState.Randomized, SubjectState.Active, SubjectState.Withdrawn, SubjectState.ReconsentRequired },
            [SubjectState.Randomized] = new HashSet<SubjectState> { SubjectState.Active, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired },
            [SubjectState.Active] = new HashSet<SubjectState> { SubjectState.Completed, SubjectState.Withdrawn, SubjectState.Unblinded, SubjectState.ReconsentRequired },
            [SubjectState.Unblinded] = new HashSet<SubjectState> { SubjectState.Withdrawn, SubjectState.Completed, SubjectState.ReconsentRequired },
            [SubjectState.ReconsentRequired] = new HashSet<SubjectState> { SubjectState.Screening, SubjectState.Enrolled, SubjectState.Randomized, SubjectState.Active, SubjectState.Unblinded, SubjectState.Completed, SubjectState.Withdrawn },
            [SubjectState.ScreenFailed] = new HashSet<SubjectState>(),
            [SubjectState.Completed] = new HashSet<SubjectState>(),
            [SubjectState.Withdrawn] = new HashSet<SubjectState>(),
        };

    /// <summary>
    /// Rule 9 order of checks, exactly: (1) same-state is always a silent no-op — including terminal states;
    /// (2) current == null legally targets only Screening; (3) unknown/malformed state strings, checked identically
    /// to (4) legal-but-forbidden known-state transitions. Returns a <see cref="DomainError"/> rather than throwing
    /// (raise sites #4/#5/#6 collapse onto one error family in this port).
    /// </summary>
    public static Result<SubjectState> Guard(SubjectState? current, SubjectState target)
    {
        if (current == target)
            return target; // same-state no-op, including e.g. WITHDRAWN -> WITHDRAWN

        if (current is null)
            return target == SubjectState.Screening
                ? target
                : SubjectErrors.InvalidStateTransition("None", SubjectStateCodec.ToCode(target));

        return Allowed[current.Value].Contains(target)
            ? target
            : SubjectErrors.InvalidStateTransition(SubjectStateCodec.ToCode(current.Value), SubjectStateCodec.ToCode(target));
    }
}
