using System.Diagnostics.CodeAnalysis;

namespace Cadence.Kernel.Commands;

/// <summary>
/// A request to change kernel state, sent through <see cref="IKernelCommandSender"/> (spec §5.1). A command changes
/// exactly one aggregate (K-TX-1) and carries an idempotency key and a reason for change (K-TX-3).
/// </summary>
public interface ICommand
{
    /// <summary>Tenant whose truth the command changes.</summary>
    TenantId TenantId { get; }

    /// <summary>
    /// Client-chosen key, unique per tenant. Replaying it with the same payload returns the original result; the same
    /// key with a different payload is a conflict (K-TX-3).
    /// </summary>
    string IdempotencyKey { get; }

    /// <summary>Why the change is being made; stored on the audit entry (spec §5.3, 21 CFR Part 11).</summary>
    string ReasonForChange { get; }
}

/// <summary>A command whose successful outcome is a <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">The result type, serialisable with <c>System.Text.Json</c> so replays can return it.</typeparam>
[SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed",
    Justification = "Marker type parameter: binds a command to its handler's result type at compile time.")]
public interface ICommand<TResult> : ICommand;
