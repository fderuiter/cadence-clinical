using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Authorization;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Knowledge;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Evidence;

internal static class CollectionMemberBlindingClassifier
{
    internal static readonly string ObservationProjectionSql = $"""
        SELECT DISTINCT ON (o.id)
            o.id AS "Id",
            o.revision AS "Revision",
            {BlindedObservationSql.Predicate(includeMaskedRevisions: false)} AS "IsBlinded"
        FROM kernel.observations AS o
        LEFT JOIN kernel.collection_instance_members AS cim
            ON cim.tenant_id = o.tenant_id AND cim.observation_id = o.id
        LEFT JOIN kernel.collection_instances AS ci
            ON ci.tenant_id = cim.tenant_id AND ci.id = cim.collection_instance_id
        LEFT JOIN kernel.concepts AS c
            ON c.tenant_id = o.tenant_id AND c.id = o.concept_id
        WHERE o.tenant_id = @tenant AND o.id = ANY(@ids)
        ORDER BY o.id, 3 DESC
        """;

    internal static async Task<IReadOnlyDictionary<ObservationId, CollectionObservationProjection>> FindObservationProjectionsAsync(
        KernelDbContext db,
        TenantId tenant,
        IReadOnlyCollection<ObservationId> distinctIds,
        IBlindedTreatmentClassifier blinding,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational() && ReferenceEquals(blinding, BlindedTreatmentClassifier.Instance))
        {
            object[] parameters =
            [
                new NpgsqlParameter<Guid>("tenant", tenant.Value),
                new NpgsqlParameter<Guid[]>("ids", [.. distinctIds.Select(id => id.Value)]),
            ];
            var rows = await db.Database
                .SqlQueryRaw<ObservationProjectionRow>(ObservationProjectionSql, parameters)
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            return rows.ToDictionary(
                r => ObservationId.FromGuid(r.Id),
                r => new CollectionObservationProjection(r.Revision, r.IsBlinded));
        }

        return await LoadObservationProjectionsFallbackAsync(db, distinctIds, blinding, cancellationToken).ConfigureAwait(false);
    }

    internal static (IReadOnlySet<string> BlindedItems, IReadOnlySet<ObservationId> BlindedObservations) ClassifyBlindedMembers(
        IBlindedTreatmentClassifier blinding,
        CollectionInstance instance,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, Observation> observations,
        IReadOnlyDictionary<ConceptId, Concept> concepts,
        IReadOnlyList<ProtocolDesign> designs,
        IReadOnlyList<FormAttestationView> attestations)
    {
        BlindedConceptMetadata? ResolveConcept(ConceptId id) =>
            concepts.TryGetValue(id, out var concept) ? BlindedObservationClassifier.ToMetadata(concept) : null;

        var specById = items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var (specBlinded, blindedItems) = ClassifySpecBlindedItems(
            blinding,
            instance.CollectionSpecId,
            collectionTitle,
            items,
            ResolveConcept,
            designs);

        var blindedObservations = new HashSet<ObservationId>();
        foreach (var member in instance.Members)
        {
            if (!observations.TryGetValue(member.ObservationId, out var obs))
            {
                continue;
            }

            specById.TryGetValue(member.ItemId, out var specItem);
            var conceptMeta = ResolveConcept(obs.ConceptId);
            var currentOrHistoricalBlinded = IsCurrentOrHistoricalMemberBlinded(
                blinding,
                instance.CollectionSpecId,
                collectionTitle,
                member.ItemId,
                specItem,
                obs,
                conceptMeta,
                designs);
            if (specBlinded || blindedItems.Contains(member.ItemId) || currentOrHistoricalBlinded)
            {
                blindedItems.Add(member.ItemId);
                blindedObservations.Add(obs.Id);
            }
        }

        foreach (var signedValue in attestations
            .Where(signature => signature.SignedValues is not null)
            .SelectMany(signature => signature.SignedValues!))
        {
            specById.TryGetValue(signedValue.ItemId, out var specItem);
            var conceptMeta = specItem is not null ? ResolveConcept(specItem.Concept.ConceptId) : null;
            if (specBlinded
                || blindedItems.Contains(signedValue.ItemId)
                || blinding.IsBlindedCollectionItem(
                    instance.CollectionSpecId,
                    collectionTitle,
                    specItem,
                    signedValue.Value,
                    conceptMeta,
                    designs))
            {
                blindedItems.Add(signedValue.ItemId);
                blindedObservations.Add(signedValue.ObservationId);
            }
        }

        return (
            blindedItems.Count == 0 ? FrozenSet<string>.Empty : blindedItems.ToFrozenSet(StringComparer.Ordinal),
            blindedObservations.Count == 0 ? FrozenSet<ObservationId>.Empty : blindedObservations.ToFrozenSet());
    }

    internal static (IReadOnlySet<string> BlindedItems, HashSet<ObservationId> BlindedObservations) ClassifyQueryTargetBlinding(
        IBlindedTreatmentClassifier blinding,
        QueryTargetFormRow form,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        IReadOnlyDictionary<ObservationId, QueryTargetObservationRow> observations,
        IReadOnlyDictionary<ConceptId, BlindedConceptMetadata> concepts,
        IReadOnlyList<ProtocolDesign> designs)
    {
        BlindedConceptMetadata? ResolveConcept(ConceptId id) =>
            concepts.TryGetValue(id, out var foundMeta) ? foundMeta : null;

        var (specBlinded, blindedItems) = ClassifySpecBlindedItems(
            blinding,
            form.CollectionSpecId,
            collectionTitle,
            items,
            ResolveConcept,
            designs);

        var blindedObservations = new HashSet<ObservationId>();
        foreach (var member in form.Members)
        {
            if (!observations.TryGetValue(member.ObservationId, out var obs))
            {
                continue;
            }

            if (specBlinded
                || blindedItems.Contains(member.ItemId)
                || blinding.IsBlindedObservation(
                    obs.ConceptId,
                    obs.Value,
                    ResolveConcept(obs.ConceptId),
                    form.CollectionSpecId,
                    member.ItemId,
                    designs))
            {
                blindedItems.Add(member.ItemId);
                blindedObservations.Add(obs.Id);
            }
        }

        return (
            blindedItems.Count == 0 ? FrozenSet<string>.Empty : blindedItems.ToFrozenSet(StringComparer.Ordinal),
            blindedObservations);
    }

    internal static bool IsCurrentOrHistoricalMemberBlinded(
        IBlindedTreatmentClassifier blinding,
        string collectionSpecId,
        string? collectionTitle,
        string itemId,
        CollectionItem? specItem,
        Observation obs,
        BlindedConceptMetadata? conceptMeta,
        IReadOnlyList<ProtocolDesign> designs) =>
        blinding.IsBlindedCollectionItem(
                collectionSpecId,
                collectionTitle,
                specItem,
                obs.Value,
                conceptMeta,
                designs)
            || blinding.IsBlindedObservation(
                obs.ConceptId,
                obs.Value,
                conceptMeta,
                collectionSpecId,
                itemId,
                designs)
            || obs.Revisions.Any(r => blinding.IsBlindedObservation(
                obs.ConceptId,
                r.Value,
                conceptMeta,
                collectionSpecId,
                itemId,
                designs));

    private static (bool SpecBlinded, HashSet<string> BlindedItems) ClassifySpecBlindedItems(
        IBlindedTreatmentClassifier blinding,
        string collectionSpecId,
        string? collectionTitle,
        IReadOnlyList<CollectionItem> items,
        Func<ConceptId, BlindedConceptMetadata?> resolveConcept,
        IReadOnlyList<ProtocolDesign> designs)
    {
        var specBlinded = blinding.IsBlindedCollectionSpec(collectionSpecId, collectionTitle, designs: designs);
        var blindedItems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (specBlinded
                || blinding.IsBlindedCollectionItem(
                    collectionSpecId,
                    collectionTitle,
                    item,
                    value: null,
                    resolveConcept(item.Concept.ConceptId),
                    designs))
            {
                blindedItems.Add(item.Id);
            }
        }

        return (specBlinded, blindedItems);
    }

    private static async Task<IReadOnlyDictionary<ObservationId, CollectionObservationProjection>> LoadObservationProjectionsFallbackAsync(
        KernelDbContext db,
        IReadOnlyCollection<ObservationId> distinctIds,
        IBlindedTreatmentClassifier blinding,
        CancellationToken cancellationToken)
    {
        var obsRows = await (
                from o in db.Observations.AsNoTracking()
                join cim in db.Set<CollectionInstanceMember>().AsNoTracking()
                    on new { o.TenantId, ObservationId = o.Id } equals new { cim.TenantId, cim.ObservationId }
                    into cimGroup
                from cim in cimGroup.DefaultIfEmpty()
                join ci in db.CollectionInstances.AsNoTracking()
                    on new { TenantId = cim != null ? cim.TenantId : default, Id = cim != null ? cim.CollectionInstanceId : default }
                    equals new { ci.TenantId, ci.Id }
                    into ciGroup
                from ci in ciGroup.DefaultIfEmpty()
                join c in db.Concepts.AsNoTracking()
                    on new { o.TenantId, Id = o.ConceptId } equals new { c.TenantId, c.Id }
                    into cGroup
                from c in cGroup.DefaultIfEmpty()
                where distinctIds.Contains(o.Id)
                select new
                {
                    o.Id,
                    o.Revision,
                    o.StudyId,
                    o.ConceptId,
                    o.Value,
                    CollectionSpecId = ci != null ? ci.CollectionSpecId : null,
                    ItemId = cim != null ? cim.ItemId : null,
                    ConceptMeta = c != null ? (BlindedConceptMetadata?)new BlindedConceptMetadata(c.ExternalCode, c.PreferredLabel, c.Definition) : null,
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (obsRows.Count == 0)
        {
            return new Dictionary<ObservationId, CollectionObservationProjection>();
        }

        var studyIds = obsRows.Select(r => r.StudyId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => studyIds.Contains(v.StudyId))
            .Select(v => new { v.StudyId, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByStudy = rawVersions
            .Select(v => (v.StudyId, Design: ProtocolDesignDocument.Read(v.Content).Design))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var result = new Dictionary<ObservationId, CollectionObservationProjection>(obsRows.Count);
        var structuralByStudy = new Dictionary<StudyId, Dictionary<(ConceptId ConceptId, string? SpecId, string? ItemId, BlindedConceptMetadata? Meta), bool>>(studyIds.Count);
        foreach (var row in obsRows)
        {
            var studyDesigns = designsByStudy.GetValueOrDefault(row.StudyId) ?? [];
            bool isBlinded;
            if (row.Value is MissingValue { Reason: NullFlavor.Masked })
            {
                isBlinded = blinding.IsBlindedObservation(
                    row.ConceptId,
                    row.Value,
                    row.ConceptMeta,
                    row.CollectionSpecId,
                    row.ItemId,
                    studyDesigns);
            }
            else
            {
                if (!structuralByStudy.TryGetValue(row.StudyId, out var studyCache))
                {
                    studyCache = [];
                    structuralByStudy[row.StudyId] = studyCache;
                }

                var key = (row.ConceptId, row.CollectionSpecId, row.ItemId, row.ConceptMeta);
                if (!studyCache.TryGetValue(key, out isBlinded))
                {
                    isBlinded = blinding.IsBlindedObservation(
                        row.ConceptId,
                        value: null,
                        row.ConceptMeta,
                        row.CollectionSpecId,
                        row.ItemId,
                        studyDesigns);
                    studyCache[key] = isBlinded;
                }
            }

            result[row.Id] = new CollectionObservationProjection(row.Revision, isBlinded);
        }

        return result;
    }
}
