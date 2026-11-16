using System;

namespace Cadence.Apps.Payments.Domain;

/// <summary>
/// Base exception for domain invariant violations and lifecycle rules within the payment bounded context.
/// </summary>
public class PaymentDomainException : Exception
{
    public PaymentDomainException(string message) : base(message) { }

    public PaymentDomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an illegal state transition is attempted on a financial domain entity.
/// </summary>
public class InvalidStateTransitionException : PaymentDomainException
{
    public string EntityName { get; }
    public string CurrentState { get; }
    public string AttemptedState { get; }

    public InvalidStateTransitionException(
        string entityName,
        string currentState,
        string attemptedState,
        string? message = null)
        : base(message ?? $"Cannot transition {entityName} from state '{currentState}' to state '{attemptedState}'.")
    {
        EntityName = entityName;
        CurrentState = currentState;
        AttemptedState = attemptedState;
    }
}
