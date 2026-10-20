using System;
using System.Diagnostics.CodeAnalysis;

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

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
        Justification = "Factory methods are the only way to build a CommandResult; callers write CommandResult<T>.Success(value), and the implicit conversion from KernelError covers the common failure path.")]
    public static CommandResult<TResult> Success(TResult value) => new(value, null, false);

    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
        Justification = "Factory methods are the only way to build a CommandResult; callers write CommandResult<T>.Success(value), and the implicit conversion from KernelError covers the common failure path.")]
    public static CommandResult<TResult> Failure(KernelError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error, false);
    }

    /// <summary>The stored result of an earlier execution with the same idempotency key.</summary>
    [SuppressMessage("Design", "CA1000:Do not declare static members on generic types",
        Justification = "Factory methods are the only way to build a CommandResult; callers write CommandResult<T>.Success(value), and the implicit conversion from KernelError covers the common failure path.")]
    public static CommandResult<TResult> Replay(TResult value) => new(value, null, true);

    public static implicit operator CommandResult<TResult>(KernelError error) => Failure(error);
}
