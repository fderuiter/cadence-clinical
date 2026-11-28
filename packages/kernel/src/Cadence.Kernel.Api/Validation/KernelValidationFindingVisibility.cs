using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Application.Commands;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Kernel.Api.Validation;

internal static class KernelValidationFindingVisibility
{
    internal static readonly Capability EnrollmentUnblindCapability = new("enrollment.unblind", VerbClass.Read);

    internal static IValidationFindings? ResolveActiveValidationFindings(IServiceProvider services)
    {
        if (services.GetService<IValidationFindings>() is not { } findings)
        {
            return null;
        }

        var isPersistenceFindings = findings.GetType().Namespace?.StartsWith("Cadence.Kernel.Persistence", StringComparison.Ordinal) ?? false;
        if (isPersistenceFindings
            && services.GetService<IAuthorityStore>() is { } authorityStore
            && !(authorityStore.GetType().Namespace?.StartsWith("Cadence.Kernel.Persistence", StringComparison.Ordinal) ?? false))
        {
            return null;
        }

        return findings;
    }

    internal static async Task<bool> IsFindingVisibleAsync(
        TenantId tenant,
        ValidationFindingView finding,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var studyNode = NodeRef.For(finding.Study);
        var studyMasks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, [studyNode], cancellationToken).ConfigureAwait(false);
        if (studyMasks.For(studyNode) is not null)
        {
            return false;
        }

        var visible = await FilterVisibleFindingsAsync(tenant, finding.Study, [finding], services, visibility, actor, cancellationToken)
            .ConfigureAwait(false);
        return visible.Count > 0;
    }

    internal static async Task<IReadOnlyList<ValidationFindingView>> FilterVisibleFindingsAsync(
        TenantId tenant,
        StudyId studyId,
        IReadOnlyList<ValidationFindingView> candidates,
        IServiceProvider services,
        INodeVisibility visibility,
        ICurrentActor actor,
        CancellationToken cancellationToken)
    {
        var collections = services.GetService<ICollectionInstanceRecords>();
        var protocols = services.GetService<IProtocolVersionReader>();
        var concepts = services.GetService<IConceptRegistry>();
        var probe = services.GetService<IAuthorizationProbe>();
        var classifier = services.GetService<IBlindedTreatmentClassifier>() ?? BlindedTreatmentClassifier.Instance;

        var formsByKey = new Dictionary<string, CollectionInstanceRecord>(StringComparer.Ordinal);
        var formsByEnrollment = new Dictionary<EnrollmentId, List<CollectionInstanceRecord>>();
        if (collections is not null && candidates.Any(f => f.Target.Kind == FindingTargetKind.DatasetRecord))
        {
            var studyForms = await collections.ListForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
            foreach (var form in studyForms)
            {
                formsByKey[form.CollectionInstanceId.Value.ToString("D")] = form;
                if (!formsByEnrollment.TryGetValue(form.EnrollmentId, out var list))
                {
                    formsByEnrollment[form.EnrollmentId] = list = [];
                }

                list.Add(form);
            }
        }

        var perFindingRefs = new Dictionary<ValidationFindingId, List<NodeRef>>(candidates.Count);
        var allNodesToCheck = new List<NodeRef>(candidates.Count * 2);
        foreach (var finding in candidates)
        {
            var refs = new List<NodeRef> { NodeRef.For(finding.Id) };
            if (finding.Target.Node is { } targetNode)
            {
                refs.Add(targetNode);
            }

            if (finding.Target.Kind == FindingTargetKind.DatasetRecord
                && formsByKey.TryGetValue(finding.Target.Key, out var targetForm))
            {
                refs.Add(NodeRef.For(targetForm.EnrollmentId));
                var siblings = formsByEnrollment.GetValueOrDefault(targetForm.EnrollmentId) ?? [targetForm];
                foreach (var obsId in ResolveReferencedObservationIds(finding, targetForm, siblings))
                {
                    refs.Add(NodeRef.For(obsId));
                }
            }

            perFindingRefs[finding.Id] = refs;
            allNodesToCheck.AddRange(refs);
        }

        var masks = await KernelReadMasking.MaskAsync(visibility, actor, tenant, allNodesToCheck, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<ProtocolDesign>? designs = null;
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>? conceptCache = null;

        var unmaskedCandidates = new List<(ValidationFindingView Finding, NodeRef? BlindedScope)>(candidates.Count);
        var blindedScopes = new HashSet<NodeRef>();
        foreach (var finding in candidates)
        {
            var refs = perFindingRefs[finding.Id];
            if (refs.Any(r => masks.For(r) is not null))
            {
                continue;
            }

            NodeRef? blindedScope = null;
            if (probe is not null)
            {
                designs ??= await LoadStudyDesignsAsync(tenant, studyId, protocols, formsByKey.Values, cancellationToken).ConfigureAwait(false);
                conceptCache ??= await LoadStudyConceptsAsync(tenant, concepts, formsByKey.Values, designs, cancellationToken).ConfigureAwait(false);
                BlindedConceptMetadata? ResolveConcept(ConceptId id) => conceptCache.TryGetValue(id, out var m) ? m : null;

                var isBlinded = classifier.IsBlindedFinding(finding.Target, finding.Observed, designs, ResolveConcept);
                NodeRef unblindScope = NodeRef.For(studyId);
                if (finding.Target.Kind == FindingTargetKind.DatasetRecord
                    && formsByKey.TryGetValue(finding.Target.Key, out var targetForm))
                {
                    unblindScope = NodeRef.For(targetForm.EnrollmentId);
                    if (!isBlinded)
                    {
                        var siblings = formsByEnrollment.GetValueOrDefault(targetForm.EnrollmentId) ?? [targetForm];
                        isBlinded = ReferencesBlindedFormItem(finding, targetForm, siblings, classifier, designs, conceptCache);
                    }
                }

                if (isBlinded)
                {
                    blindedScope = unblindScope;
                    blindedScopes.Add(unblindScope);
                }
            }

            unmaskedCandidates.Add((finding, blindedScope));
        }

        IReadOnlyDictionary<NodeRef, AuthorizationDecision>? unblindDecisions = null;
        if (probe is not null && blindedScopes.Count > 0)
        {
            unblindDecisions = await probe.CanIManyAsync(
                tenant,
                EnrollmentUnblindCapability,
                blindedScopes,
                cancellationToken).ConfigureAwait(false);
        }

        var visible = new List<ValidationFindingView>(unmaskedCandidates.Count);
        foreach (var (finding, blindedScope) in unmaskedCandidates)
        {
            if (blindedScope is { } scope
                && (unblindDecisions is null
                    || !unblindDecisions.TryGetValue(scope, out var decision)
                    || !decision.Permitted))
            {
                continue;
            }

            visible.Add(finding);
        }

        return visible;
    }

    private static IReadOnlyList<ObservationId> ResolveReferencedObservationIds(
        ValidationFindingView finding,
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblingForms)
    {
        var matched = MatchVariableObservations(targetForm.CollectionSpecId, finding.Target.Variable, targetForm, siblingForms)
            .Concat(finding.Observed.SelectMany(observed =>
                MatchVariableObservations(targetForm.CollectionSpecId, observed.Variable, targetForm, siblingForms)))
            .Distinct()
            .ToList();

        if (matched.Count == 0 && BlindedTreatmentClassifier.MatchesMarkers(targetForm.CollectionSpecId))
        {
            return [.. targetForm.Values.Select(val => val.ObservationId).Distinct()];
        }

        return matched;
    }

    private static IEnumerable<ObservationId> MatchVariableObservations(
        string defaultSpecId,
        string? variable,
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblingForms)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            yield break;
        }

        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            var specId = variable[..dot];
            var itemId = variable[(dot + 1)..];
            foreach (var form in siblingForms.Where(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal)))
            {
                foreach (var val in form.Values.Where(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal)))
                {
                    yield return val.ObservationId;
                }
            }

            yield break;
        }

        foreach (var val in targetForm.Values.Where(v => string.Equals(v.ItemId, variable, StringComparison.Ordinal)))
        {
            yield return val.ObservationId;
        }

        foreach (var form in siblingForms.Where(f => !string.Equals(f.CollectionSpecId, defaultSpecId, StringComparison.Ordinal)))
        {
            foreach (var val in form.Values.Where(v => string.Equals(v.ItemId, variable, StringComparison.Ordinal)))
            {
                yield return val.ObservationId;
            }
        }
    }

    private static bool ReferencesBlindedFormItem(
        ValidationFindingView finding,
        CollectionInstanceRecord targetForm,
        IReadOnlyList<CollectionInstanceRecord> siblingForms,
        IBlindedTreatmentClassifier classifier,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> conceptCache)
    {
        bool IsFormBlindedItem(CollectionInstanceRecord form, string itemId)
        {
            if (ReferenceEquals(classifier, BlindedTreatmentClassifier.Instance)
                && form.BlindedItemIds is not null
                && form.BlindedItemIds.Contains(itemId))
            {
                return true;
            }

            var specItem = form.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
            var recorded = form.Values.FirstOrDefault(v => string.Equals(v.ItemId, itemId, StringComparison.Ordinal));
            BlindedConceptMetadata? meta = specItem is not null && conceptCache.TryGetValue(specItem.Concept.ConceptId, out var m)
                ? m
                : null;
            return classifier.IsBlindedCollectionItem(form.CollectionSpecId, form.CollectionTitle, specItem, recorded?.Value, meta, designs);
        }

        bool MatchesVariable(string? variable)
        {
            if (string.IsNullOrWhiteSpace(variable))
            {
                return false;
            }

            var dot = variable.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && dot < variable.Length - 1)
            {
                var specId = variable[..dot];
                var itemId = variable[(dot + 1)..];
                return siblingForms
                    .Where(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal))
                    .Any(f => IsFormBlindedItem(f, itemId));
            }

            return IsFormBlindedItem(targetForm, variable)
                || siblingForms.Any(f => IsFormBlindedItem(f, variable));
        }

        return classifier.IsBlindedCollectionSpec(targetForm.CollectionSpecId, targetForm.CollectionTitle, designs: designs)
            || MatchesVariable(finding.Target.Variable)
            || finding.Observed.Any(o => MatchesVariable(o.Variable));
    }

    private static async Task<IReadOnlyList<ProtocolDesign>> LoadStudyDesignsAsync(
        TenantId tenant,
        StudyId studyId,
        IProtocolVersionReader? protocols,
        IEnumerable<CollectionInstanceRecord> forms,
        CancellationToken cancellationToken)
    {
        if (protocols is null)
        {
            return [];
        }

        var distinctFormVersions = forms.Select(f => f.ProtocolVersionId).Distinct().ToList();
        var studyDesigns = await protocols.GetDesignsForStudyAsync(tenant, studyId, cancellationToken).ConfigureAwait(false);
        if (studyDesigns.Count > 0)
        {
            var formDesigns = new List<ProtocolDesign>();
            foreach (var versionId in distinctFormVersions)
            {
                if (studyDesigns.TryGetValue(versionId, out var design))
                {
                    formDesigns.Add(design);
                }
            }

            return formDesigns.Count > 0 ? formDesigns : [.. studyDesigns.Values];
        }

        var fallbackDesigns = new List<ProtocolDesign>();
        foreach (var versionId in distinctFormVersions.Where(id => !id.IsEmpty))
        {
            if (await protocols.GetDesignAsync(tenant, versionId, cancellationToken).ConfigureAwait(false) is { } design)
            {
                fallbackDesigns.Add(design);
            }
        }

        return fallbackDesigns;
    }

    private static Task<IReadOnlyDictionary<ConceptId, BlindedConceptMetadata>> LoadStudyConceptsAsync(
        TenantId tenant,
        IConceptRegistry? concepts,
        IEnumerable<CollectionInstanceRecord> forms,
        IReadOnlyList<ProtocolDesign> designs,
        CancellationToken cancellationToken) =>
        BlindedConceptMetadata.ResolveManyAsync(
            concepts,
            tenant,
            forms.SelectMany(f => f.Items).Select(i => i.Concept)
                .Concat(designs.SelectMany(d => d.CollectionSpecs).SelectMany(s => s.Items).Select(i => i.Concept)),
            cancellationToken);
}
