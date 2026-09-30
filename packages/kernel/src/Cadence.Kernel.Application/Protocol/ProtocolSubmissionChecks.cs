using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Protocol.Design;

namespace Cadence.Kernel.Application.Protocol;

/// <summary>
/// A check a draft protocol version must pass before it is submitted for review, beyond its own structure and
/// references. Modules register one to enforce a rule that needs their data, for example K-STD-1 (every collected
/// Concept has a mapping in the study's declared standards, ADR-2463). A check runs inside the submit command, so it may
/// declare lineage inputs of the protocol version with <see cref="IKernelUnitOfWork.LinkInput"/>.
/// </summary>
public interface IProtocolSubmissionCheck
{
    /// <summary>Why <paramref name="design"/> of <paramref name="study"/> cannot be submitted, or null when it passes.</summary>
    Task<KernelError?> CheckAsync(StudyId study, ProtocolDesign design, ICommandContext context, CancellationToken cancellationToken);
}
