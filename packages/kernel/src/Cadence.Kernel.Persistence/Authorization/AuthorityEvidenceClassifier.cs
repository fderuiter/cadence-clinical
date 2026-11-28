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
using Cadence.Kernel.Lineage;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Kernel.Persistence.Authorization;

internal static class AuthorityEvidenceClassifier
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
    };

    public static async Task ClassifyEvidenceInOpenConnectionAsync(
        KernelDbContext db,
        IBlindedTreatmentClassifier blinding,
        TenantId tenant,
        Guid[] requestedObsIds,
        Guid[] requestedFindingIds,
        List<AssertionId> requestedAssertionIds,
        List<Guid> requestedFormQueryIds,
        Dictionary<(string Kind, Guid Id), ScopePath> resolvedPaths,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence,
        CancellationToken cancellationToken)
    {
        var extraObsGuids = new HashSet<Guid>(requestedObsIds);
        var assertionTargetObs = new Dictionary<AssertionId, Guid>();
        var assertionScopeHints = new Dictionary<AssertionId, (StudyId StudyId, EnrollmentId? EnrollmentId, StudySiteId? DirectStudySiteId)>();
        var formQueryTargetObs = new Dictionary<Guid, Guid>();
        var formQueryEnrollments = new Dictionary<Guid, EnrollmentId>();

        if (requestedAssertionIds.Count > 0)
        {
            var assertions = await db.Assertions.AsNoTracking()
                .Where(a => a.TenantId == tenant && requestedAssertionIds.Contains(a.Id))
                .Select(a => new { a.Id, a.StudyId, a.EnrollmentId, a.TargetKind, a.TargetId, a.RetractsId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var parentAssertionIds = assertions
                .Select(a => a.RetractsId ?? (a.TargetKind == NodeKinds.Assertion ? AssertionId.FromGuid(a.TargetId) : (AssertionId?)null))
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var parentById = parentAssertionIds.Count == 0
                ? new Dictionary<AssertionId, (EnrollmentId? EnrollmentId, string TargetKind, Guid TargetId)>()
                : (await db.Assertions.AsNoTracking()
                        .Where(a => a.TenantId == tenant && parentAssertionIds.Contains(a.Id))
                        .Select(a => new { a.Id, a.EnrollmentId, a.TargetKind, a.TargetId })
                        .ToListAsync(cancellationToken)
                        .ConfigureAwait(false))
                    .ToDictionary(a => a.Id, a => (a.EnrollmentId, a.TargetKind, a.TargetId));

            foreach (var a in assertions)
            {
                var parentId = a.RetractsId ?? (a.TargetKind == NodeKinds.Assertion ? AssertionId.FromGuid(a.TargetId) : (AssertionId?)null);
                var hasParent = parentId is { } pid && parentById.TryGetValue(pid, out _);
                var parent = hasParent ? parentById[parentId!.Value] : default;

                var enrollmentId = a.EnrollmentId ?? (hasParent ? parent.EnrollmentId : null);
                StudySiteId? directSite = null;
                if (a.TargetKind == NodeKinds.StudySite)
                {
                    directSite = StudySiteId.FromGuid(a.TargetId);
                }
                else if (hasParent && parent.TargetKind == NodeKinds.StudySite)
                {
                    directSite = StudySiteId.FromGuid(parent.TargetId);
                }

                assertionScopeHints[a.Id] = (a.StudyId, enrollmentId, directSite);

                if (a.TargetKind == NodeKinds.Observation)
                {
                    assertionTargetObs[a.Id] = a.TargetId;
                    extraObsGuids.Add(a.TargetId);
                }
                else if (hasParent && parent.TargetKind == NodeKinds.Observation)
                {
                    assertionTargetObs[a.Id] = parent.TargetId;
                    extraObsGuids.Add(parent.TargetId);
                }
            }
        }

        if (requestedFormQueryIds.Count > 0)
        {
            var queryEdges = await db.LineageEdges.AsNoTracking()
                .Where(e => e.TenantId == tenant
                    && e.ToKind == NodeKinds.FormQuery
                    && requestedFormQueryIds.Contains(e.ToId)
                    && ((e.FromKind == NodeKinds.Observation && e.Kind == LineageEdgeKind.DerivedFrom)
                        || (e.FromKind == NodeKinds.Enrollment && e.Kind == LineageEdgeKind.GovernedBy)))
                .Select(e => new { e.ToId, e.FromKind, e.FromId, e.Kind })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var edge in queryEdges)
            {
                if (edge.FromKind == NodeKinds.Observation && edge.Kind == LineageEdgeKind.DerivedFrom)
                {
                    formQueryTargetObs[edge.ToId] = edge.FromId;
                    extraObsGuids.Add(edge.FromId);
                }
                else if (edge.FromKind == NodeKinds.Enrollment && edge.Kind == LineageEdgeKind.GovernedBy)
                {
                    formQueryEnrollments[edge.ToId] = EnrollmentId.FromGuid(edge.FromId);
                }
            }
        }

        var obsRows = extraObsGuids.Count > 0
            ? await QueryObservationClassificationRowsAsync(db, blinding, tenant, [.. extraObsGuids], cancellationToken).ConfigureAwait(false)
            : [];
        var findingRows = requestedFindingIds.Length > 0
            ? await QueryValidationFindingClassificationRowsAsync(db, tenant, requestedFindingIds, cancellationToken).ConfigureAwait(false)
            : [];

        var distinctStudyIds = new HashSet<StudyId>();
        foreach (var row in obsRows)
        {
            if (row.StudyId is { } studyGuid)
            {
                distinctStudyIds.Add(StudyId.FromGuid(studyGuid));
            }
        }

        foreach (var row in findingRows)
        {
            if (row.StudyId is { } studyGuid)
            {
                distinctStudyIds.Add(StudyId.FromGuid(studyGuid));
            }
        }

        var designsByStudy = await LoadStudyDesignsAsync(db, tenant, distinctStudyIds, cancellationToken)
            .ConfigureAwait(false);

        if (obsRows.Count > 0)
        {
            ClassifyObservationRows(blinding, obsRows, designsByStudy, resolvedPaths, resolvedEvidence);
        }

        if (findingRows.Count > 0)
        {
            await AuthorityFindingBlindingClassifier.LoadValidationFindingClassificationsAsync(
                    db,
                    blinding,
                    tenant,
                    findingRows,
                    designsByStudy,
                    resolvedPaths,
                    resolvedEvidence,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (assertionScopeHints.Count == 0 && requestedFormQueryIds.Count == 0)
        {
            return;
        }

        var neededStudyIds = assertionScopeHints.Values.Select(h => h.StudyId).Distinct().ToList();
        var neededEnrollmentIds = assertionScopeHints.Values
            .Where(h => h.EnrollmentId is not null)
            .Select(h => h.EnrollmentId!.Value)
            .Concat(formQueryEnrollments.Values)
            .Distinct()
            .ToList();
        var neededStudySiteIds = assertionScopeHints.Values
            .Where(h => h.DirectStudySiteId is not null)
            .Select(h => h.DirectStudySiteId!.Value)
            .Distinct()
            .ToList();

        var studyScopes = new Dictionary<StudyId, ScopePath>();
        if (neededStudyIds.Count > 0)
        {
            var studyRows = await db.Studies.AsNoTracking()
                .Where(s => s.TenantId == tenant && neededStudyIds.Contains(s.Id))
                .Select(s => new { s.Id, s.SponsorId })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in studyRows)
            {
                studyScopes[row.Id] = new ScopePath(row.SponsorId, row.Id);
            }
        }

        var enrollmentScopes = new Dictionary<EnrollmentId, ScopePath>();
        if (neededEnrollmentIds.Count > 0)
        {
            var enrollmentRows = await (
                    from e in db.Enrollments.AsNoTracking()
                    join ss in db.StudySites.AsNoTracking() on new { e.TenantId, Id = e.StudySiteId } equals new { ss.TenantId, ss.Id }
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where e.TenantId == tenant && neededEnrollmentIds.Contains(e.Id)
                    select new { e.Id, study.SponsorId, ss.StudyId, site.Country, StudySiteId = ss.Id })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in enrollmentRows)
            {
                enrollmentScopes[row.Id] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.StudySiteId);
            }
        }

        var studySiteScopes = new Dictionary<StudySiteId, ScopePath>();
        if (neededStudySiteIds.Count > 0)
        {
            var siteRows = await (
                    from ss in db.StudySites.AsNoTracking()
                    join study in db.Studies.AsNoTracking() on new { ss.TenantId, Id = ss.StudyId } equals new { study.TenantId, study.Id }
                    join site in db.Sites.AsNoTracking() on new { ss.TenantId, Id = ss.SiteId } equals new { site.TenantId, site.Id }
                    where ss.TenantId == tenant && neededStudySiteIds.Contains(ss.Id)
                    select new { ss.Id, study.SponsorId, ss.StudyId, site.Country })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in siteRows)
            {
                studySiteScopes[row.Id] = new ScopePath(row.SponsorId, row.StudyId, row.Country, row.Id);
            }
        }

        foreach (var (assertionId, hint) in assertionScopeHints)
        {
            var scope = studyScopes.GetValueOrDefault(hint.StudyId, ScopePath.TenantWide);
            if (hint.EnrollmentId is { } eid && enrollmentScopes.TryGetValue(eid, out var enrScope))
            {
                scope = enrScope;
            }
            else if (hint.DirectStudySiteId is { } sid && studySiteScopes.TryGetValue(sid, out var siteScope))
            {
                scope = siteScope;
            }

            var isBlinded = false;
            if (assertionTargetObs.TryGetValue(assertionId, out var targetObsGuid)
                && resolvedEvidence.TryGetValue((NodeKinds.Observation, targetObsGuid), out var obsInfo))
            {
                scope = obsInfo.Scope;
                isBlinded = obsInfo.IsBlinded;
            }

            resolvedPaths[(NodeKinds.Assertion, assertionId.Value)] = scope;
            resolvedEvidence[(NodeKinds.Assertion, assertionId.Value)] = new EvidenceNodeClassification(scope, isBlinded);
        }

        foreach (var queryId in requestedFormQueryIds)
        {
            EvidenceNodeClassification? targetObsInfo = null;
            if (formQueryTargetObs.TryGetValue(queryId, out var targetObsGuid)
                && resolvedEvidence.TryGetValue((NodeKinds.Observation, targetObsGuid), out var foundObsInfo))
            {
                targetObsInfo = foundObsInfo;
            }

            ScopePath? scope = null;
            if (formQueryEnrollments.TryGetValue(queryId, out var enrId)
                && enrollmentScopes.TryGetValue(enrId, out var enrScope))
            {
                scope = enrScope;
            }
            else if (targetObsInfo is not null)
            {
                scope = targetObsInfo.Scope;
            }

            if (scope is null)
            {
                continue;
            }

            var isBlinded = targetObsInfo?.IsBlinded ?? false;
            resolvedPaths[(NodeKinds.FormQuery, queryId)] = scope;
            resolvedEvidence[(NodeKinds.FormQuery, queryId)] = new EvidenceNodeClassification(scope, isBlinded);
        }
    }

    public static async Task<Dictionary<ConceptId, BlindedConceptMetadata>> LoadConceptMetadataAsync(
        KernelDbContext db,
        TenantId tenant,
        HashSet<ConceptId> conceptIds,
        CancellationToken ct)
    {
        if (conceptIds.Count == 0)
        {
            return [];
        }

        var ids = conceptIds.ToList();
        var rows = await db.Concepts.AsNoTracking()
            .Where(c => c.TenantId == tenant && ids.Contains(c.Id))
            .Select(c => new { c.Id, c.ExternalCode, c.PreferredLabel, c.Definition })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var map = new Dictionary<ConceptId, BlindedConceptMetadata>(rows.Count);
        foreach (var row in rows)
        {
            map[row.Id] = new BlindedConceptMetadata(row.ExternalCode, row.PreferredLabel, row.Definition);
        }

        return map;
    }

    public static ObservationValue? DeserializeValueForClassifier(
        IBlindedTreatmentClassifier blinding,
        string? valueJson)
    {
        if (string.IsNullOrEmpty(valueJson))
        {
            return null;
        }

        if (ReferenceEquals(blinding, BlindedTreatmentClassifier.Instance)
            && !valueJson.Contains("missing", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return JsonSerializer.Deserialize<ObservationValue>(valueJson, WebJsonOptions);
    }

    private static Task<List<ObservationClassificationRow>> QueryObservationClassificationRowsAsync(
        KernelDbContext db,
        IBlindedTreatmentClassifier blinding,
        TenantId tenant,
        Guid[] rawIds,
        CancellationToken ct)
    {
        var includeAllValues = !ReferenceEquals(blinding, BlindedTreatmentClassifier.Instance);
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("ids", rawIds),
            new NpgsqlParameter<bool>("includeAllValues", includeAllValues),
        ];
        return db.Database
            .SqlQueryRaw<ObservationClassificationRow>(AuthorityClassificationSql.ObservationClassificationSql, parameters)
            .ToListAsync(ct);
    }

    private static Task<List<ValidationFindingClassificationRow>> QueryValidationFindingClassificationRowsAsync(
        KernelDbContext db,
        TenantId tenant,
        Guid[] rawIds,
        CancellationToken ct)
    {
        object[] parameters =
        [
            new NpgsqlParameter<Guid>("tenant", tenant.Value),
            new NpgsqlParameter<Guid[]>("ids", rawIds),
        ];
        return db.Database
            .SqlQueryRaw<ValidationFindingClassificationRow>(AuthorityClassificationSql.ValidationFindingClassificationSql, parameters)
            .ToListAsync(ct);
    }

    private static async Task<Dictionary<Guid, List<ProtocolDesign>>> LoadStudyDesignsAsync(
        KernelDbContext db,
        TenantId tenant,
        HashSet<StudyId> studyIds,
        CancellationToken ct)
    {
        if (studyIds.Count == 0)
        {
            return [];
        }

        var ids = studyIds.ToList();
        var rows = await db.ProtocolVersions.AsNoTracking()
            .Where(pv => pv.TenantId == tenant && ids.Contains(pv.StudyId))
            .OrderBy(pv => pv.Number)
            .Select(pv => new { pv.StudyId, pv.Content })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var designsByStudy = new Dictionary<Guid, List<ProtocolDesign>>(ids.Count);
        foreach (var row in rows)
        {
            if (!designsByStudy.TryGetValue(row.StudyId.Value, out var designs))
            {
                designs = [];
                designsByStudy[row.StudyId.Value] = designs;
            }

            if (ProtocolDesignDocument.Read(row.Content).Design is { } design)
            {
                designs.Add(design);
            }
        }

        return designsByStudy;
    }

    private static void ClassifyObservationRows(
        IBlindedTreatmentClassifier blinding,
        List<ObservationClassificationRow> rows,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy,
        Dictionary<(string Kind, Guid Id), ScopePath> resolvedPaths,
        Dictionary<(string Kind, Guid Id), EvidenceNodeClassification> resolvedEvidence)
    {
        var isDefaultClassifier = ReferenceEquals(blinding, BlindedTreatmentClassifier.Instance);
        Dictionary<(Guid? StudyId, Guid ConceptId, string? SpecId, string? ItemId), bool>? batchMemo =
            isDefaultClassifier ? new() : null;

        foreach (var row in rows)
        {
            var scope = row.ToScopePath() ?? ScopePath.TenantWide;
            resolvedPaths[(NodeKinds.Observation, row.Id)] = scope;

            var value = DeserializeValueForClassifier(blinding, row.ValueJson);
            if (batchMemo is not null && value is null)
            {
                var key = (row.StudyId, row.ConceptId, row.CollectionSpecId, row.ItemId);
                if (!batchMemo.TryGetValue(key, out var cachedBlinded))
                {
                    cachedBlinded = ClassifyObservationRow(blinding, row, value, designsByStudy);
                    batchMemo[key] = cachedBlinded;
                }

                resolvedEvidence[(NodeKinds.Observation, row.Id)] = new EvidenceNodeClassification(scope, cachedBlinded);
                continue;
            }

            var isBlinded = ClassifyObservationRow(blinding, row, value, designsByStudy);
            resolvedEvidence[(NodeKinds.Observation, row.Id)] = new EvidenceNodeClassification(scope, isBlinded);
        }
    }

    private static bool ClassifyObservationRow(
        IBlindedTreatmentClassifier blinding,
        ObservationClassificationRow row,
        ObservationValue? value,
        Dictionary<Guid, List<ProtocolDesign>> designsByStudy)
    {
        IReadOnlyList<ProtocolDesign> designs = row.StudyId is { } studyGuid
            && designsByStudy.TryGetValue(studyGuid, out var studyDesigns)
            ? studyDesigns
            : [];
        BlindedConceptMetadata? concept = row.ConceptPreferredLabel is not null
            ? new BlindedConceptMetadata(row.ConceptExternalCode, row.ConceptPreferredLabel, row.ConceptDefinition)
            : null;
        return blinding.IsBlindedObservation(
            ConceptId.FromGuid(row.ConceptId),
            value,
            concept,
            row.CollectionSpecId,
            row.ItemId,
            designs);
    }
}
