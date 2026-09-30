using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>
/// A check a draft protocol version must pass before it is submitted for review, beyond its own structure and
/// references. Modules register one to enforce a rule that needs their data, for example K-STD-1 (every collected
/// Concept has a mapping in the study's declared standards, ADR-2463). A check runs inside the submit command, so it may
/// declare lineage inputs of the protocol version with <see cref="IKernelUnitOfWork.LinkInput"/>.
/// </summary>
public interface IProtocolSubmissionCheck
{
    /// <summary>Why <paramref name="submission"/> cannot be submitted, or null when it passes.</summary>
    Task<KernelError?> CheckAsync(ProtocolSubmission submission, ICommandContext context, CancellationToken cancellationToken);
}

/// <summary>A draft protocol version being submitted for review.</summary>
/// <param name="Study">The study.</param>
/// <param name="Version">The protocol version.</param>
/// <param name="Node">The version's lineage node at the content being submitted: its id and version index before submission.</param>
/// <param name="Design">The design being submitted.</param>
public sealed record ProtocolSubmission(StudyId Study, ProtocolVersionId Version, NodeRef Node, ProtocolDesign Design);
