using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cadence.Kernel.Commands;

/// <summary>
/// The single path for kernel writes (spec §5.1): resolve actor → authorize → idempotency → handler → state,
/// provenance, events, audit entry and outbox rows in one transaction.
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
/// <see cref="Proposal"/> names the approved proposal this command executes (K-AI-2, ADR-2632): the pipeline then
/// requires the proposal to be approved, unexpired, approved by the acting person and to request the command's
/// capability, and links the proposal to the command's provenance activity, lineage and a <c>ProposalExecuted</c> event.
/// </summary>
public sealed record CommandMetadata(Guid CorrelationId, Guid? CausationId = null, ProposalId? Proposal = null);
