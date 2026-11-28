using System;

namespace Cadence.Kernel.Events;

/// <summary>
/// Signals that a committed outbox message failed terminally (for example, an unrecoverable domain invariant or
/// malformed event payload) and should be quarantined for its aggregate stream rather than retried indefinitely.
/// </summary>
public sealed class KernelOutboxPoisonMessageException : InvalidOperationException
{
    /// <summary>Initializes a new instance of <see cref="KernelOutboxPoisonMessageException"/> with a description of the terminal failure.</summary>
    public KernelOutboxPoisonMessageException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of <see cref="KernelOutboxPoisonMessageException"/> with a description and root cause.</summary>
    public KernelOutboxPoisonMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
