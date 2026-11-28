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

        if (services.GetService<IAgentVisibility>() is { } visibility
            && !await IsFindingVisibleAsync(principal, finding, services, visibility, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return finding.Explain();
    }

    internal static async Task<bool> IsFindingVisibleAsync(
        AgentPrincipal principal,
        ValidationFindingView finding,
        IServiceProvider? services,
        IAgentVisibility visibility,
        CancellationToken cancellationToken)
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
            && services?.GetService<ICollectionInstanceRecords>() is { } forms
            && await forms.FindAsync(principal.Tenant, CollectionInstanceId.FromGuid(formGuid), cancellationToken).ConfigureAwait(false) is { } form)
        {
            nodesToCheck.Add(NodeRef.For(form.EnrollmentId));
            var studyForms = await forms.ListForStudyAsync(principal.Tenant, finding.Study, cancellationToken).ConfigureAwait(false);
            var siblingForms = studyForms.Where(f => f.EnrollmentId == form.EnrollmentId).ToList();
            IReadOnlyList<CollectionInstanceRecord> formsToInspect = siblingForms.Count > 0 ? siblingForms : [form];
            var matchedObservations = new List<NodeRef>();
            foreach (var candidateForm in formsToInspect)
            {
                foreach (var value in candidateForm.Values.Where(v => ReferencesItem(finding, form.CollectionSpecId, candidateForm.CollectionSpecId, v.ItemId)))
                {
                    matchedObservations.Add(NodeRef.For(value.ObservationId));
                }
            }

            if (matchedObservations.Count == 0 && BlindedTreatmentClassifier.MatchesMarkers(form.CollectionSpecId))
            {
                matchedObservations.AddRange(form.Values.Select(v => NodeRef.For(v.ObservationId)));
            }

            nodesToCheck.AddRange(matchedObservations);
        }

        var masks = await visibility.MaskAsync(principal, nodesToCheck.Distinct().ToList(), cancellationToken).ConfigureAwait(false);
        return masks.Count == 0;
    }

    private static bool ReferencesItem(ValidationFindingView finding, string defaultSpecId, string candidateSpecId, string itemId)
    {
        if (MatchesVariable(finding.Target.Variable, defaultSpecId, candidateSpecId, itemId))
        {
            return true;
        }

        return finding.Observed.Any(o => MatchesVariable(o.Variable, defaultSpecId, candidateSpecId, itemId));
    }

    private static bool MatchesVariable(string? variable, string defaultSpecId, string candidateSpecId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            return false;
        }

        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            return string.Equals(variable[..dot], candidateSpecId, StringComparison.Ordinal)
                && string.Equals(variable[(dot + 1)..], itemId, StringComparison.Ordinal);
        }

        return string.Equals(variable, itemId, StringComparison.Ordinal)
            && (string.Equals(candidateSpecId, defaultSpecId, StringComparison.Ordinal) || !string.IsNullOrEmpty(candidateSpecId));
    }
}
