using System;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Explanations;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Modules.Intelligence.Tools.Kernel;

/// <summary>
/// Explains validation findings with the finding's own <see cref="ValidationFindingView.Explain"/> (K-EXP-1). A host
/// without the kernel's validation store has no findings to explain, so the store is optional.
/// </summary>
internal sealed class ValidationFindingExplanations(IServiceProvider services) : IExplanationSource
{
    public string Kind => NodeKinds.ValidationFinding;

    public async Task<Explanation?> ExplainAsync(AgentPrincipal principal, NodeRef node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(node);
        if (services.GetService<IValidationFindings>() is not { } findings)
        {
            return null;
        }

        var finding = await findings.FindAsync(principal.Tenant, new ValidationFindingId(node.Id), cancellationToken).ConfigureAwait(false);
        return finding?.Explain();
    }
}
