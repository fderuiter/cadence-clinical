using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Evidence;
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
        if (finding is null)
        {
            return null;
        }

        if (services.GetService<IAgentVisibility>() is { } visibility)
        {
            var nodesToCheck = new List<NodeRef>
            {
                NodeRef.For(finding.Study),
                NodeRef.For(finding.Id),
            };
            if (finding.Target.Node is { } targetNode)
            {
                nodesToCheck.Add(targetNode);
            }

            if (finding.Target.Kind == FindingTargetKind.DatasetRecord
                && Guid.TryParse(finding.Target.Key, out var formGuid)
                && services.GetService<ICollectionInstanceRecords>() is { } forms
                && await forms.FindAsync(principal.Tenant, CollectionInstanceId.FromGuid(formGuid), cancellationToken).ConfigureAwait(false) is { } form)
            {
                nodesToCheck.Add(NodeRef.For(form.EnrollmentId));
                nodesToCheck.AddRange(form.Values
                    .Where(value => ReferencesItem(finding, form.CollectionSpecId, value.ItemId))
                    .Select(value => NodeRef.For(value.ObservationId)));
            }

            var masks = await visibility.MaskAsync(principal, nodesToCheck.Distinct().ToList(), cancellationToken).ConfigureAwait(false);
            if (masks.Count > 0)
            {
                return null;
            }
        }

        return finding.Explain();
    }

    private static bool ReferencesItem(ValidationFindingView finding, string specId, string itemId)
    {
        if (MatchesVariable(finding.Target.Variable, specId, itemId))
        {
            return true;
        }

        return finding.Observed.Any(o => MatchesVariable(o.Variable, specId, itemId));
    }

    private static bool MatchesVariable(string? variable, string specId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            return false;
        }

        return string.Equals(variable, itemId, StringComparison.Ordinal)
            || string.Equals(variable, $"{specId}.{itemId}", StringComparison.Ordinal);
    }
}
