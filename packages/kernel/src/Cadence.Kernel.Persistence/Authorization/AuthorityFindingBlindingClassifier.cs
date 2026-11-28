using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Authorization;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Authorization;

internal static class AuthorityFindingBlindingClassifier
{
    public static async Task LoadValidationFindingClassificationsAsync(
        KernelDbContext db,
        IBlindedTreatmentClassifier blinding,
        TenantId tenant,
        List<ValidationFindingClassificationRow> rows,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy,
        Dictionary<(string Kind, Guid Id), ScopePath> resolvedPaths,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence,
        CancellationToken ct)
    {
        var conceptIdsByStudy = new Dictionary<Guid, HashSet<ConceptId>>();
        var distinctConceptIds = new HashSet<ConceptId>();
        foreach (var row in rows)
        {
            if (row.StudyId is not { } studyGuid || conceptIdsByStudy.ContainsKey(studyGuid))
            {
                continue;
            }

            var studyConceptIds = new HashSet<ConceptId>();
            if (designsByStudy.TryGetValue(studyGuid, out var studyDesigns))
            {
                foreach (var design in studyDesigns)
                {
                    foreach (var conceptRef in design.ExternalReferences().Concepts)
                    {
                        if (!conceptRef.ConceptId.IsEmpty)
                        {
                            studyConceptIds.Add(conceptRef.ConceptId);
                            distinctConceptIds.Add(conceptRef.ConceptId);
                        }
                    }
                }
            }

            conceptIdsByStudy[studyGuid] = studyConceptIds;
        }

        var conceptsById = await AuthorityEvidenceClassifier.LoadConceptMetadataAsync(db, tenant, distinctConceptIds, ct)
            .ConfigureAwait(false);
        var pendingSiblingFindings = new List<PendingSiblingFinding>();
        var pendingEnrollmentIds = new HashSet<Guid>();

        foreach (var row in rows)
        {
            var scope = row.ToScopePath() ?? ScopePath.TenantWide;
            resolvedPaths[(NodeKinds.ValidationFinding, row.Id)] = scope;

            IReadOnlyList<ProtocolDesign> designs = row.StudyId is { } studyGuid
                && designsByStudy.TryGetValue(studyGuid, out var studyDesigns)
                ? studyDesigns
                : [];
            var studyConcepts = row.StudyId is { } conceptStudyGuid
                && conceptIdsByStudy.TryGetValue(conceptStudyGuid, out var referencedIds)
                ? referencedIds
                : null;
            BlindedConceptMetadata? ResolveConcept(ConceptId id) =>
                studyConcepts is not null && studyConcepts.Contains(id) && conceptsById.TryGetValue(id, out var meta)
                    ? meta
                    : null;

            var targetKind = Enum.Parse<FindingTargetKind>(row.TargetKind, ignoreCase: true);
            var target = new FindingTarget(targetKind, row.Dataset, row.TargetKey, row.Variable);
            var rawObserved = JsonSerializer.Deserialize<string?[][]>(row.ObservedJson) ?? [];
            var observedValues = new List<ObservedVariable>(rawObserved.Length);
            foreach (var pair in rawObserved)
            {
                observedValues.Add(new ObservedVariable(pair[0]!, pair[1]));
            }

            var isBlinded = blinding.IsBlindedFinding(target, observedValues, designs, ResolveConcept);
            if (!isBlinded && row.TargetObservationId is { } targetObsGuid && row.TargetObsConceptId is { } targetObsConceptGuid && row.TargetObsValueJson is not null)
            {
                if (resolvedEvidence.TryGetValue((NodeKinds.Observation, targetObsGuid), out var classifiedTargetObs))
                {
                    isBlinded = classifiedTargetObs.IsBlinded;
                }
                else
                {
                    var targetObsValue = AuthorityEvidenceClassifier.DeserializeValueForClassifier(blinding, row.TargetObsValueJson);
                    BlindedConceptMetadata? targetObsConcept = row.TargetObsConceptPreferredLabel is not null
                        ? new BlindedConceptMetadata(row.TargetObsConceptExternalCode, row.TargetObsConceptPreferredLabel, row.TargetObsConceptDefinition)
                        : null;
                    isBlinded = blinding.IsBlindedObservation(
                        ConceptId.FromGuid(targetObsConceptGuid),
                        targetObsValue,
                        targetObsConcept,
                        row.TargetObsSpecId,
                        row.TargetObsItemId,
                        designs);
                }
            }
            else if (!isBlinded
                && row.TargetFormId is { } targetFormId
                && row.TargetFormSpecId is not null
                && row.TargetFormEnrollmentId is { } targetEnrollmentId)
            {
                pendingSiblingFindings.Add(new PendingSiblingFinding(
                    row.Id,
                    scope,
                    row.TargetFormSpecId,
                    row.Variable,
                    observedValues,
                    targetFormId,
                    targetEnrollmentId));
                pendingEnrollmentIds.Add(targetEnrollmentId);
                continue;
            }

            resolvedEvidence[(NodeKinds.ValidationFinding, row.Id)] = new EvidenceNodeClassification(scope, isBlinded);
        }

        if (pendingSiblingFindings.Count == 0)
        {
            return;
        }

        var siblingsByEnrollment = await LoadClassifiedSiblingMembersByEnrollmentAsync(
                db,
                blinding,
                tenant,
                [.. pendingEnrollmentIds],
                designsByStudy,
                resolvedEvidence,
                ct)
            .ConfigureAwait(false);

        foreach (var pending in pendingSiblingFindings)
        {
            IReadOnlyList<ClassifiedSiblingMember> siblingMembers =
                siblingsByEnrollment.TryGetValue(pending.EnrollmentId, out var cachedSiblings)
                    ? cachedSiblings
                    : [];
            var isBlinded = ReferencesBlindedSiblingMember(
                pending.TargetFormSpecId,
                pending.Variable,
                pending.ObservedValues,
                pending.TargetFormId,
                siblingMembers);
            resolvedEvidence[(NodeKinds.ValidationFinding, pending.FindingId)] =
                new EvidenceNodeClassification(pending.Scope, isBlinded);
        }
    }

    private static async Task<Dictionary<Guid, List<ClassifiedSiblingMember>>> LoadClassifiedSiblingMembersByEnrollmentAsync(
        KernelDbContext db,
        IBlindedTreatmentClassifier blinding,
        TenantId tenant,
        Guid[] enrollmentIds,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence,
        CancellationToken ct)
    {
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("enrollmentIds", enrollmentIds),
        ];
        var rows = await db.Database
            .SqlQueryRaw<SiblingMemberRow>(AuthorityClassificationSql.EnrollmentSiblingMembersSql, parameters)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byEnrollment = new Dictionary<Guid, List<ClassifiedSiblingMember>>(enrollmentIds.Length);
        foreach (var m in rows)
        {
            if (!byEnrollment.TryGetValue(m.EnrollmentId, out var members))
            {
                members = [];
                byEnrollment[m.EnrollmentId] = members;
            }

            bool isBlinded;
            if (resolvedEvidence.TryGetValue((NodeKinds.Observation, m.ObservationId), out var classifiedObs))
            {
                isBlinded = classifiedObs.IsBlinded;
            }
            else
            {
                IReadOnlyList<ProtocolDesign> designs = designsByStudy.TryGetValue(m.StudyId, out var studyDesigns)
                    ? studyDesigns
                    : [];
                var value = AuthorityEvidenceClassifier.DeserializeValueForClassifier(blinding, m.ValueJson);
                BlindedConceptMetadata? concept = m.ConceptPreferredLabel is not null
                    ? new BlindedConceptMetadata(m.ConceptExternalCode, m.ConceptPreferredLabel, m.ConceptDefinition)
                    : null;
                isBlinded = blinding.IsBlindedObservation(
                    ConceptId.FromGuid(m.ConceptId),
                    value,
                    concept,
                    m.CollectionSpecId,
                    m.ItemId,
                    designs);
            }

            members.Add(new ClassifiedSiblingMember(m.FormId, m.CollectionSpecId, m.ItemId, isBlinded));
        }

        return byEnrollment;
    }

    private static bool ReferencesBlindedSiblingMember(
        string defaultSpecId,
        string? targetVariable,
        IReadOnlyList<ObservedVariable> observedValues,
        Guid targetFormId,
        IReadOnlyList<ClassifiedSiblingMember> siblingMembers) =>
        MatchesBlindedSiblingMember(defaultSpecId, targetVariable, targetFormId, siblingMembers)
        || observedValues.Any(observed =>
            MatchesBlindedSiblingMember(defaultSpecId, observed.Variable, targetFormId, siblingMembers));

    private static bool MatchesBlindedSiblingMember(
        string defaultSpecId,
        string? variable,
        Guid targetFormId,
        IReadOnlyList<ClassifiedSiblingMember> siblingMembers)
    {
        if (string.IsNullOrWhiteSpace(variable) || siblingMembers.Count == 0)
        {
            return false;
        }

        var dot = variable.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < variable.Length - 1)
        {
            var specId = variable[..dot];
            var itemId = variable[(dot + 1)..];
            return siblingMembers.Any(m =>
                m.IsBlinded
                && string.Equals(m.CollectionSpecId, specId, StringComparison.Ordinal)
                && string.Equals(m.ItemId, itemId, StringComparison.Ordinal));
        }

        if (siblingMembers.Any(m =>
                m.IsBlinded
                && m.FormId == targetFormId
                && string.Equals(m.ItemId, variable, StringComparison.Ordinal)))
        {
            return true;
        }

        return siblingMembers.Any(m =>
            m.IsBlinded
            && !string.Equals(m.CollectionSpecId, defaultSpecId, StringComparison.Ordinal)
            && string.Equals(m.ItemId, variable, StringComparison.Ordinal));
    }
}
