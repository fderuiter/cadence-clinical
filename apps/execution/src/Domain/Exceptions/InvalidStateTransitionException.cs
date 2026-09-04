using Cadence.Apps.Execution.Domain.Enums;

namespace Cadence.Apps.Execution.Domain.Exceptions;

/// <summary>
/// Thrown when a caller requests a <c>ClinicalSubject</c> state transition that the regulated
/// state machine does not permit from the subject's current state.
/// @req:PRD-SYS-042
/// @req:PRD-SUB-001
/// </summary>
public sealed class InvalidStateTransitionException : Exception
{
    public SubjectState CurrentState { get; }
    public SubjectState TargetState { get; }

    public InvalidStateTransitionException(SubjectState currentState, SubjectState targetState)
        : base($"Transition from '{currentState}' to '{targetState}' is not permitted by the subject state machine.")
    {
        CurrentState = currentState;
        TargetState = targetState;
    }

    public InvalidStateTransitionException(SubjectState currentState, SubjectState targetState, string message)
        : base(message)
    {
        CurrentState = currentState;
        TargetState = targetState;
    }

    public InvalidStateTransitionException(SubjectState currentState, SubjectState targetState, string message, Exception innerException)
        : base(message, innerException)
    {
        CurrentState = currentState;
        TargetState = targetState;
    }
}
