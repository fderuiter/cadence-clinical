using System;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Declares that a command has no FluentValidation validator, and why (ADR-2600 §4.4). The pipeline runs every
/// registered <c>IValidator&lt;TCommand&gt;</c> after resolving the actor and before authorization; a catalog test
/// fails when a command has neither a validator nor this attribute, or has both.
/// </summary>
/// <param name="reason">Why the command needs no validator beyond the pipeline-wide checks.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class NoValidatorAttribute(string reason) : Attribute
{
    /// <summary>
    /// The reason on kernel commands whose handlers check their own inputs and return registered domain errors, which
    /// predate the validation stage (#5448 moves their shape checks into validators).
    /// </summary>
    public const string HandlerChecksInputs =
        "The handler checks its inputs and returns registered domain errors (K-ERR-1); shape checks move to a validator under #5448.";

    /// <summary>Why the command needs no validator.</summary>
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason)
        ? throw new ArgumentException("A command without a validator says why.", nameof(reason))
        : reason;
}
