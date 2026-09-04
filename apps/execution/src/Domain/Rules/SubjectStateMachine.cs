using System.Collections.Frozen;

using Cadence.Apps.Execution.Domain.Enums;
using Cadence.Apps.Execution.Domain.Exceptions;

namespace Cadence.Apps.Execution.Domain.Rules;

/// <summary>
/// Zero-allocation, immutable rule engine for the regulated <see cref="SubjectState"/> transition graph.
/// Reverse-engineered parity port of the legacy Python <c>guard_subject_transition</c> function.
/// @req:PRD-SYS-042
/// @req:PRD-SUB-001
/// </summary>
public static class SubjectStateMachine
{
    private static readonly FrozenSet<SubjectState> LockedPostRandomizationStates = new[]
    {
        SubjectState.Randomized,
        SubjectState.Active,
        SubjectState.Completed,
        SubjectState.Unblinded,
        SubjectState.Withdrawn,
    }.ToFrozenSet();

    private static readonly FrozenDictionary<SubjectState, FrozenSet<SubjectState>> AllowedTransitions =
        new Dictionary<SubjectState, FrozenSet<SubjectState>>
        {
            [SubjectState.Screening] = new[]
            {
                SubjectState.ScreenFailed,
                SubjectState.Enrolled,
                SubjectState.Withdrawn,
                SubjectState.ReconsentRequired,
            }.ToFrozenSet(),
            [SubjectState.Enrolled] = new[]
            {
                SubjectState.Randomized,
                SubjectState.Withdrawn,
                SubjectState.ReconsentRequired,
            }.ToFrozenSet(),
            [SubjectState.Randomized] = new[]
            {
                SubjectState.Active,
                SubjectState.Withdrawn,
                SubjectState.Unblinded,
                SubjectState.ReconsentRequired,
            }.ToFrozenSet(),
            [SubjectState.Active] = new[]
            {
                SubjectState.Completed,
                SubjectState.Withdrawn,
                SubjectState.Unblinded,
                SubjectState.ReconsentRequired,
            }.ToFrozenSet(),
            [SubjectState.Unblinded] = new[]
            {
                SubjectState.Completed,
                SubjectState.Withdrawn,
                SubjectState.ReconsentRequired,
            }.ToFrozenSet(),
            [SubjectState.ReconsentRequired] = new[]
            {
                SubjectState.Screening,
                SubjectState.Enrolled,
                SubjectState.Randomized,
                SubjectState.Active,
                SubjectState.Unblinded,
                SubjectState.Completed,
                SubjectState.Withdrawn,
            }.ToFrozenSet(),
            [SubjectState.ScreenFailed] = FrozenSet<SubjectState>.Empty,
            [SubjectState.Completed] = FrozenSet<SubjectState>.Empty,
            [SubjectState.Withdrawn] = FrozenSet<SubjectState>.Empty,
        }.ToFrozenDictionary();

    /// <summary>
    /// Determines whether <paramref name="target"/> is a legal transition from <paramref name="current"/>.
    /// Self-transitions are always permitted (idempotent), including from terminal states.
    /// </summary>
    public static bool CanTransition(SubjectState current, SubjectState target) =>
        current == target || AllowedTransitions[current].Contains(target);

    /// <summary>
    /// Validates the requested transition, throwing <see cref="InvalidStateTransitionException"/> when
    /// the state machine forbids it.
    /// </summary>
    public static void EnsureValidTransition(SubjectState current, SubjectState target)
    {
        if (!CanTransition(current, target))
        {
            throw new InvalidStateTransitionException(current, target);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="state"/> is a post-randomization state in which stratification
    /// factors and demographics become permanently locked against mutation.
    /// </summary>
    public static bool IsLockedPostRandomization(SubjectState state) =>
        LockedPostRandomizationStates.Contains(state);
}
