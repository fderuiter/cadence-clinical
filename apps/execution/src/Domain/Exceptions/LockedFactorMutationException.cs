namespace Cadence.Apps.Execution.Domain.Exceptions;

/// <summary>
/// Thrown when a caller attempts to mutate a <c>ClinicalSubject</c>'s stratification factors or
/// demographics after the subject has passed randomization, where those fields are permanently locked.
/// @req:PRD-SYS-001
/// @req:PRD-SYS-042
/// </summary>
public sealed class LockedFactorMutationException : Exception
{
    public LockedFactorMutationException()
        : base("Stratification factors and demographics are locked and cannot be mutated after randomization.")
    {
    }

    public LockedFactorMutationException(string message)
        : base(message)
    {
    }

    public LockedFactorMutationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
