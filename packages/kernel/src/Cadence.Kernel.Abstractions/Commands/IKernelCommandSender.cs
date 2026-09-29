using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Commands;

/// <summary>
/// The single path for kernel writes (spec §5.1): resolve actor → authorize → idempotency → handler → state,
/// provenance, events, audit entry and outbox rows in one transaction.
/// @req:PRD-SYS-053
/// </summary>
public interface IKernelCommandSender
{
    Task<CommandResult<TResult>> SendAsync<TResult>(
        ICommand<TResult> command,
        CommandMetadata? metadata = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tracing identifiers carried onto provenance, audit and event rows. <see cref="CorrelationId"/> groups everything
/// caused by one user request; <see cref="CausationId"/> is the event that triggered this command, if any.
/// </summary>
public sealed record CommandMetadata(Guid CorrelationId, Guid? CausationId = null);
