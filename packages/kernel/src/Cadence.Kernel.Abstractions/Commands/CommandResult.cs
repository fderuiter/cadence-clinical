using System;

namespace Cadence.Kernel.Commands;

/// <summary>
/// Outcome of a kernel command: either a value or a registered <see cref="KernelError"/>. Expected failures never
/// throw. <see cref="Replayed"/> is true when an idempotent replay returned the original result (K-TX-3).
/// </summary>
public readonly record struct CommandResult<TResult>
{
    private CommandResult(TResult? value, KernelError? error, bool replayed)
    {
        Value = value;
        Error = error;
        Replayed = replayed;
    }

    public TResult? Value { get; }

    public KernelError? Error { get; }

    public bool Replayed { get; }

    public bool IsSuccess => Error is null;

    public static CommandResult<TResult> Success(TResult value) => new(value, null, false);

    public static CommandResult<TResult> Failure(KernelError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error, false);
    }

    /// <summary>The stored result of an earlier execution with the same idempotency key.</summary>
    public static CommandResult<TResult> Replay(TResult value) => new(value, null, true);

    public static implicit operator CommandResult<TResult>(KernelError error) => Failure(error);
}
