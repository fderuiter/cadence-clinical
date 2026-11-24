using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Simulation;
using Cadence.Kernel.Application.Validation;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Indicators;
using Cadence.Kernel.Persistence.Evidence;
using Cadence.Kernel.Persistence.Indicators;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Protocol;
using Cadence.Kernel.Protocol.Design;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Kernel.Persistence.Validation;

/// <summary>
/// Reads validation runs and derives findings from them (spec §10.3) through a context bound to the caller's tenant,
/// so Row-Level Security scopes every query (K-ID-2). Read-only. Derivation reads the light columns of every detection
/// in scope (run, finding, stage, pack, dataset, severity), then the full detail only for the findings it returns.
/// </summary>
internal sealed class KernelValidationStore(KernelTenantDbContextFactory contexts) : IValidationLedgerSource, IValidationFindings
{
    public async Task<ValidationLedgerData> LoadStudyAsync(TenantId tenant, StudyId study, ValidationStage stage, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await LoadStudyAsync(db, study, stage, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ValidationLedgerData?> LoadFindingAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        return await LoadFindingAsync(db, finding, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ValidationFindingView?> FindAsync(TenantId tenant, ValidationFindingId finding, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var data = await LoadFindingAsync(db, finding, cancellationToken).ConfigureAwait(false);
        if (data?.Derive().SingleOrDefault(f => f.Finding == finding) is not { } state)
        {
            return null;
        }

        var views = await DetailAsync(db, [state], cancellationToken).ConfigureAwait(false);
        return views[0];
    }

    public async Task<FindingPage> ListAsync(TenantId tenant, FindingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Skip);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, FindingQuery.MaxTake);

        await using var db = contexts.Create(tenant);
        var data = await LoadStudyAsync(db, query.Study, query.Stage, cancellationToken).ConfigureAwait(false);
        var matching = data.Derive()
            .Where(f => query.Severity is null || f.Severity == query.Severity)
            .Where(f => query.Statuses is null || query.Statuses.Contains(f.Status))
            .Where(f => query.Classification is null || f.Classification == query.Classification)
            .Where(f => query.PackKey is null || string.Equals(f.PackKey, query.PackKey, StringComparison.Ordinal))
            .Where(f => query.Dataset is null || string.Equals(f.Dataset, query.Dataset, StringComparison.Ordinal))
            .Where(f => !query.BlockingOnly || f.Blocks)
            .OrderBy(f => f.Severity).ThenBy(f => f.Stage)
            .ThenBy(f => f.PackKey, StringComparer.Ordinal).ThenBy(f => f.Dataset, StringComparer.Ordinal).ThenBy(f => f.Finding)
            .ToList();

        var page = matching.Skip(query.Skip).Take(query.Take).ToList();
        var views = page.Count == 0 ? [] : await DetailAsync(db, page, cancellationToken).ConfigureAwait(false);
        return new FindingPage(views, matching.Count);
    }

    public async Task<ValidationRunSummary?> FindRunAsync(TenantId tenant, ValidationRunId run, CancellationToken cancellationToken)
    {
        await using var db = contexts.Create(tenant);
        var found = await db.Set<ValidationRun>().AsNoTracking().AsSplitQuery()
            .Include(r => r.Packs).Include(r => r.Datasets).Include(r => r.Inputs)
            .SingleOrDefaultAsync(r => r.Id == run, cancellationToken).ConfigureAwait(false);
        return found is null
            ? null
            : new ValidationRunSummary(
                found.Id,
                found.StudyId,
                found.Stage,
                found.AsOf,
                found.Standard,
                found.StandardVersion,
                found.Agency,
                [.. found.Packs.OrderBy(p => p.PackKey, StringComparer.Ordinal)
                    .Select(p => new ValidationRunPackView(p.LibraryItemId, p.PackKey, p.PackVersion, p.ContentHash))],
                [.. found.Datasets.Select(d => d.Dataset).Order(StringComparer.Ordinal)],
                [.. found.Inputs.Select(i => i.Input).Order(StringComparer.Ordinal).Select(NodeRef.Parse)],
                found.StartedAt,
                found.CompletedAt,
                Utc(found.CreatedAt),
                found.CreatedBy,
                found.DetectionCount);
    }

    /// <summary>Loads targets of active (<see cref="FindingStatus.Open"/> or <see cref="FindingStatus.Investigating"/>) <see cref="ValidationSeverity.Error"/> findings in <paramref name="study"/>.</summary>
    internal static async Task<IReadOnlyList<(ValidationFindingId Id, FindingTarget Target)>> LoadActiveErrorFindingTargetsAsync(
        KernelDbContext db, StudyId study, CancellationToken cancellationToken)
    {
        var data = await LoadStudyAsync(db, study, stage: null, cancellationToken).ConfigureAwait(false);
        var activeErrors = data.Derive()
            .Where(f => f.Severity == ValidationSeverity.Error && f.Status is FindingStatus.Open or FindingStatus.Investigating)
            .ToList();
        if (activeErrors.Count == 0)
        {
            return [];
        }

        var ids = activeErrors.Select(s => s.Finding).ToList();
        var lastRuns = activeErrors.Select(s => s.LastDetectedRun).Distinct().ToList();
        var detections = (await db.Set<ValidationDetectionRecord>().AsNoTracking()
                .Where(d => ids.Contains(d.FindingId) && lastRuns.Contains(d.RunId))
                .Select(d => new { d.RunId, d.FindingId, d.TargetKind, d.Dataset, d.TargetKey, d.Variable })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(d => (d.RunId, d.FindingId), d => new FindingTarget(d.TargetKind, d.Dataset, d.TargetKey, d.Variable));
        return [.. activeErrors
            .Where(s => detections.ContainsKey((s.LastDetectedRun, s.Finding)))
            .Select(s => (s.Finding, detections[(s.LastDetectedRun, s.Finding)]))];
    }

    /// <summary>Loads full views of active (<see cref="FindingStatus.Open"/> or <see cref="FindingStatus.Investigating"/>) <see cref="ValidationSeverity.Error"/> findings in <paramref name="study"/>.</summary>
    internal static async Task<IReadOnlyList<ValidationFindingView>> LoadActiveErrorFindingsAsync(
        KernelDbContext db, StudyId study, CancellationToken cancellationToken)
    {
        var data = await LoadStudyAsync(db, study, stage: null, cancellationToken).ConfigureAwait(false);
        var activeErrors = data.Derive()
            .Where(f => f.Severity == ValidationSeverity.Error && f.Status is FindingStatus.Open or FindingStatus.Investigating)
            .OrderBy(f => f.Stage)
            .ThenBy(f => f.PackKey, StringComparer.Ordinal)
            .ThenBy(f => f.Dataset, StringComparer.Ordinal)
            .ThenBy(f => f.Finding)
            .ToList();
        return activeErrors.Count == 0
            ? []
            : await DetailAsync(db, activeErrors, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The runs, detections and decisions of a study, at one stage or at every stage.</summary>
    internal static async Task<ValidationLedgerData> LoadStudyAsync(
        KernelDbContext db, StudyId study, ValidationStage? stage, CancellationToken cancellationToken)
    {
        var runs = db.Set<ValidationRun>().AsNoTracking().Where(r => r.StudyId == study && (stage == null || r.Stage == stage));
        var ledgerRuns = await LoadRunsAsync(db, runs, cancellationToken).ConfigureAwait(false);

        var detections = await db.Set<ValidationDetectionRecord>().AsNoTracking()
            .Where(d => d.StudyId == study && (stage == null || d.Stage == stage))
            .Select(d => new LedgerDetection(d.RunId, d.FindingId, d.Stage, d.PackKey, d.Dataset, d.Severity))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var decisions = await db.Set<ValidationFinding>().AsNoTracking()
            .Where(f => f.StudyId == study)
            .Select(f => new LedgerDecision(f.Id, f.Decision, f.DecidedAt, f.Classification))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new ValidationLedgerData(study, ledgerRuns, detections, decisions);
    }

    /// <summary>
    /// One finding's detections and decision, and the runs that cover it (its stage, pack and dataset): the runs that
    /// did not detect it are what resolves it.
    /// </summary>
    private static async Task<ValidationLedgerData?> LoadFindingAsync(KernelDbContext db, ValidationFindingId finding, CancellationToken cancellationToken)
    {
        var detections = await db.Set<ValidationDetectionRecord>().AsNoTracking()
            .Where(d => d.FindingId == finding)
            .Select(d => new { d.StudyId, Ledger = new LedgerDetection(d.RunId, d.FindingId, d.Stage, d.PackKey, d.Dataset, d.Severity) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (detections.Count == 0)
        {
            return null;
        }

        // A finding's identity fixes its study, stage, pack and dataset, so every detection agrees on them.
        var study = detections[0].StudyId;
        var (stage, pack, dataset) = (detections[0].Ledger.Stage, detections[0].Ledger.PackKey, detections[0].Ledger.Dataset);
        var covering = db.Set<ValidationRun>().AsNoTracking()
            .Where(r => r.StudyId == study && r.Stage == stage
                && r.Packs.Any(p => p.PackKey == pack)
                && r.Datasets.Any(d => d.Dataset == dataset));
        var runs = await LoadRunsAsync(db, covering, cancellationToken).ConfigureAwait(false);

        var decisions = await db.Set<ValidationFinding>().AsNoTracking()
            .Where(f => f.Id == finding)
            .Select(f => new LedgerDecision(f.Id, f.Decision, f.DecidedAt, f.Classification))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new ValidationLedgerData(study, runs, [.. detections.Select(d => d.Ledger)], decisions);
    }

    private static async Task<List<LedgerRun>> LoadRunsAsync(KernelDbContext db, IQueryable<ValidationRun> runs, CancellationToken cancellationToken)
    {
        var heads = await runs.Select(r => new { r.Id, r.Stage, r.CreatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var packs = await db.Set<ValidationRunPack>().AsNoTracking()
            .Where(p => runs.Any(r => r.Id == p.RunId))
            .Select(p => new { p.RunId, p.PackKey })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var datasets = await db.Set<ValidationRunDataset>().AsNoTracking()
            .Where(d => runs.Any(r => r.Id == d.RunId))
            .Select(d => new { d.RunId, d.Dataset })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var packsByRun = packs.ToLookup(p => p.RunId, p => p.PackKey);
        var datasetsByRun = datasets.ToLookup(d => d.RunId, d => d.Dataset);
        return [.. heads.Select(r => new LedgerRun(
            r.Id,
            r.Stage,
            Utc(r.CreatedAt),
            packsByRun[r.Id].ToHashSet(StringComparer.Ordinal),
            datasetsByRun[r.Id].ToHashSet(StringComparer.Ordinal)))];
    }

    /// <summary>The full views of <paramref name="states"/>, in the same order, from each finding's latest detection.</summary>
    private static async Task<List<ValidationFindingView>> DetailAsync(
        KernelDbContext db, IReadOnlyList<FindingState> states, CancellationToken cancellationToken)
    {
        if (states.Count == 0)
        {
            return [];
        }

        var ids = states.Select(s => s.Finding).ToList();
        var lastRuns = states.Select(s => s.LastDetectedRun).Distinct().ToList();
        var detections = (await db.Set<ValidationDetectionRecord>().AsNoTracking()
                .Where(d => ids.Contains(d.FindingId) && lastRuns.Contains(d.RunId))
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(d => (d.RunId, d.FindingId));
        var runs = await db.Set<ValidationRun>().AsNoTracking()
            .Where(r => lastRuns.Contains(r.Id))
            .Select(r => new { r.Id, r.Standard, r.StandardVersion, r.Agency })
            .ToDictionaryAsync(r => r.Id, cancellationToken).ConfigureAwait(false);
        var decisions = await db.Set<ValidationFinding>().AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, cancellationToken).ConfigureAwait(false);

        var (enrollmentByFinding, blindedVarsByFinding, blindedObsByFinding, targetLabelByFinding, targetEvidenceByFinding) =
            await ResolveFindingContextAsync(
                db,
                states,
                detections,
                cancellationToken).ConfigureAwait(false);

        return [.. states.Select(state =>
        {
            var detection = detections[(state.LastDetectedRun, state.Finding)];
            var run = runs[state.LastDetectedRun];
            var decision = state.DecisionInForce
                && decisions.TryGetValue(state.Finding, out var d) && d is { Decision: { } status, DecidedAt: { } at, DecidedBy: { } by }
                ? new FindingDecision(status, by, at, d.DecisionText, d.DecidedOnRun, d.AttestationId)
                : null;
            enrollmentByFinding.TryGetValue(state.Finding, out var enrollment);
            blindedVarsByFinding.TryGetValue(state.Finding, out var blindedVars);
            blindedObsByFinding.TryGetValue(state.Finding, out var blindedObs);
            targetLabelByFinding.TryGetValue(state.Finding, out var targetLabel);
            targetEvidenceByFinding.TryGetValue(state.Finding, out var targetEvidence);
            return new ValidationFindingView(
                state.Finding,
                detection.StudyId,
                state.Stage,
                state.PackKey,
                detection.PackItemId,
                detection.RuleId,
                detection.RuleVersion,
                state.Severity,
                detection.Target,
                detection.Message,
                detection.Expected,
                detection.ObservedValues,
                run.Standard,
                run.StandardVersion,
                run.Agency,
                state.Status,
                state.FirstDetectedRun,
                state.LastDetectedRun,
                state.ResolvedBy,
                state.Classification,
                decision,
                enrollment,
                blindedVars,
                blindedObs,
                targetLabel,
                targetEvidence);
        })];
    }

    private static async Task<(
        Dictionary<ValidationFindingId, EnrollmentId> Enrollments,
        Dictionary<ValidationFindingId, IReadOnlySet<string>> BlindedVariables,
        Dictionary<ValidationFindingId, ObservationId> BlindedObservations,
        Dictionary<ValidationFindingId, string> TargetLabels,
        Dictionary<ValidationFindingId, IReadOnlyList<NodeRef>> TargetEvidence)> ResolveFindingContextAsync(
        KernelDbContext db,
        IReadOnlyList<FindingState> states,
        IReadOnlyDictionary<(ValidationRunId RunId, ValidationFindingId FindingId), ValidationDetectionRecord> detections,
        CancellationToken cancellationToken)
    {
        var formTargetIds = new HashSet<CollectionInstanceId>();
        var obsTargetIds = new HashSet<ObservationId>();
        var hasDesignTargets = false;

        foreach (var state in states)
        {
            var target = detections[(state.LastDetectedRun, state.Finding)].Target;
            if (target.TryGetCollectionInstanceId(out var formId))
            {
                formTargetIds.Add(formId);
            }
            else if (target.TryGetObservationId(out var obsId))
            {
                obsTargetIds.Add(obsId);
            }
            else if (target.Kind == FindingTargetKind.DesignElement && NodeRef.TryParse(target.Key, out _))
            {
                hasDesignTargets = true;
            }
        }

        if (formTargetIds.Count == 0 && obsTargetIds.Count == 0 && !hasDesignTargets)
        {
            return ([], [], [], [], []);
        }

        var formTargetList = formTargetIds.ToList();
        var targetForms = formTargetList.Count == 0
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => formTargetList.Contains(c.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var obsTargetList = obsTargetIds.ToList();
        var targetObservations = obsTargetList.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : await db.Observations.AsNoTracking()
                .Where(o => obsTargetList.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var enrollmentIds = targetForms.Select(c => c.EnrollmentId)
            .Concat(targetObservations.Values.Select(o => o.EnrollmentId))
            .Distinct()
            .ToList();

        var enrollmentForms = enrollmentIds.Count == 0
            ? []
            : await db.CollectionInstances.AsNoTracking()
                .Include(c => c.Members)
                .Where(c => enrollmentIds.Contains(c.EnrollmentId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var formsById = enrollmentForms.ToDictionary(c => c.Id);
        var formsByEnrollment = enrollmentForms
            .GroupBy(c => c.EnrollmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionInstance>)g.ToList());

        var studyIds = detections.Values.Select(d => d.StudyId).Distinct().ToList();
        var rawVersions = await db.ProtocolVersions.AsNoTracking()
            .Where(v => studyIds.Contains(v.StudyId))
            .Select(v => new { v.Id, v.StudyId, v.Number, v.Content })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var designsByVersion = rawVersions.ToDictionary(
            v => v.Id,
            v => ProtocolDesignDocument.Read(v.Content).Design);
        var versionNumbersById = rawVersions.ToDictionary(v => v.Id, v => v.Number);
        var designsByStudy = rawVersions
            .Select(v => (v.StudyId, Design: designsByVersion[v.Id]))
            .Where(x => x.Design is not null)
            .GroupBy(x => x.StudyId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ProtocolDesign>)g.Select(x => x.Design!).ToList());

        var allObsIds = enrollmentForms.SelectMany(c => c.Members.Select(m => m.ObservationId))
            .Concat(obsTargetList)
            .Distinct()
            .ToList();
        var allObservations = allObsIds.Count == 0
            ? new Dictionary<ObservationId, Observation>()
            : await db.Observations.AsNoTracking()
                .Where(o => allObsIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, cancellationToken)
                .ConfigureAwait(false);

        var conceptIds = rawVersions
            .SelectMany(v => designsByVersion[v.Id]?.CollectionSpecs ?? [])
            .SelectMany(s => s.Items)
            .Select(i => i.Concept.ConceptId)
            .Concat(allObservations.Values.Select(o => o.ConceptId))
            .Distinct()
            .ToList();
        var concepts = conceptIds.Count == 0
            ? new Dictionary<ConceptId, Concept>()
            : await db.Concepts.AsNoTracking()
                .Where(c => conceptIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken)
                .ConfigureAwait(false);

        var blindedTokensByEnrollment = new Dictionary<EnrollmentId, HashSet<string>>();
        var blindedObsByEnrollment = new Dictionary<EnrollmentId, ObservationId>();
        var blindedObsSet = new HashSet<ObservationId>();
        foreach (var instance in enrollmentForms)
        {
            var studyDesigns = designsByStudy.GetValueOrDefault(instance.StudyId) ?? [];
            var spec = designsByVersion.GetValueOrDefault(instance.ProtocolVersionId)?.CollectionSpecs
                .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal));
            var items = spec?.Items ?? [];

            if (!blindedTokensByEnrollment.TryGetValue(instance.EnrollmentId, out var tokens))
            {
                tokens = new HashSet<string>(StringComparer.Ordinal);
                blindedTokensByEnrollment[instance.EnrollmentId] = tokens;
            }

            foreach (var item in items)
            {
                concepts.TryGetValue(item.Concept.ConceptId, out var concept);
                if (BlindedObservationClassifier.IsBlindedTreatment(
                        item.Concept.ConceptId,
                        new TextValue(string.Empty),
                        concept,
                        instance.CollectionSpecId,
                        item.Id,
                        studyDesigns))
                {
                    tokens.Add(item.Id);
                    tokens.Add($"{instance.CollectionSpecId}.{item.Id}");
                }
            }

            foreach (var member in instance.Members)
            {
                if (!allObservations.TryGetValue(member.ObservationId, out var obs))
                {
                    continue;
                }

                concepts.TryGetValue(obs.ConceptId, out var concept);
                if (tokens.Contains(member.ItemId)
                    || BlindedObservationClassifier.IsBlindedTreatment(
                        obs.ConceptId,
                        obs.Value,
                        concept,
                        instance.CollectionSpecId,
                        member.ItemId,
                        studyDesigns))
                {
                    tokens.Add(member.ItemId);
                    tokens.Add($"{instance.CollectionSpecId}.{member.ItemId}");
                    blindedObsSet.Add(obs.Id);
                    blindedObsByEnrollment.TryAdd(instance.EnrollmentId, obs.Id);
                }
            }
        }

        foreach (var obs in targetObservations.Values.Where(o => !blindedObsSet.Contains(o.Id)))
        {
            var studyDesigns = designsByStudy.GetValueOrDefault(obs.StudyId) ?? [];
            concepts.TryGetValue(obs.ConceptId, out var concept);
            if (BlindedObservationClassifier.IsBlindedTreatment(
                    obs.ConceptId,
                    obs.Value,
                    concept,
                    collectionSpecId: null,
                    itemId: null,
                    studyDesigns))
            {
                blindedObsSet.Add(obs.Id);
                blindedObsByEnrollment.TryAdd(obs.EnrollmentId, obs.Id);
            }
        }

        var enrollmentMap = new Dictionary<ValidationFindingId, EnrollmentId>();
        var blindedVarsMap = new Dictionary<ValidationFindingId, IReadOnlySet<string>>();
        var blindedObsMap = new Dictionary<ValidationFindingId, ObservationId>();
        var targetLabelMap = new Dictionary<ValidationFindingId, string>();
        var targetEvidenceMap = new Dictionary<ValidationFindingId, IReadOnlyList<NodeRef>>();
        var elementLabelsByStudy = new Dictionary<StudyId, Dictionary<Guid, string>>();

        foreach (var state in states)
        {
            var detection = detections[(state.LastDetectedRun, state.Finding)];
            var target = detection.Target;
            EnrollmentId? enrollmentId = null;
            ObservationId? directObsId = null;
            var isDirectBlindedObs = false;

            if (target.TryGetCollectionInstanceId(out var targetFormId)
                && formsById.TryGetValue(targetFormId, out var targetForm))
            {
                enrollmentId = targetForm.EnrollmentId;
                var design = designsByVersion.GetValueOrDefault(targetForm.ProtocolVersionId);
                var formTitle = FormatCollectionInstanceTitle(targetForm, design);
                var siblings = formsByEnrollment.GetValueOrDefault(targetForm.EnrollmentId) ?? [targetForm];
                var itemLabel = ResolveVariableLabel(target.Variable, targetForm, siblings, designsByVersion, concepts);
                targetLabelMap[state.Finding] = string.IsNullOrWhiteSpace(itemLabel)
                    ? formTitle
                    : $"{formTitle} · {itemLabel}";
                var evidence = new List<NodeRef> { NodeRef.For(targetForm.Id) };
                evidence.AddRange(ResolveReferencedObservationRefs(detection, targetForm, siblings));
                targetEvidenceMap[state.Finding] = evidence.Distinct().ToArray();
            }
            else if (target.TryGetObservationId(out var targetObsId)
                && allObservations.TryGetValue(targetObsId, out var targetObs))
            {
                enrollmentId = targetObs.EnrollmentId;
                directObsId = targetObs.Id;
                isDirectBlindedObs = blindedObsSet.Contains(targetObs.Id);

                var parentForm = (formsByEnrollment.GetValueOrDefault(targetObs.EnrollmentId) ?? [])
                    .FirstOrDefault(c => c.Members.Any(m => m.ObservationId == targetObs.Id));
                var evidence = new List<NodeRef> { NodeRef.For(targetObs.Id) };
                string? formTitle = null;
                string? itemLabel = null;
                if (parentForm is not null)
                {
                    evidence.Add(NodeRef.For(parentForm.Id));
                    var design = designsByVersion.GetValueOrDefault(parentForm.ProtocolVersionId);
                    formTitle = FormatCollectionInstanceTitle(parentForm, design);
                    var memberItemId = parentForm.Members.FirstOrDefault(m => m.ObservationId == targetObs.Id)?.ItemId;
                    var specItem = design?.CollectionSpecs
                        .FirstOrDefault(s => string.Equals(s.Id, parentForm.CollectionSpecId, StringComparison.Ordinal))
                        ?.Items.FirstOrDefault(i => string.Equals(i.Id, memberItemId, StringComparison.Ordinal));
                    if (!string.IsNullOrWhiteSpace(specItem?.Prompt))
                    {
                        itemLabel = specItem.Prompt.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(memberItemId))
                    {
                        itemLabel = memberItemId;
                    }
                }

                if (string.IsNullOrWhiteSpace(itemLabel) && concepts.TryGetValue(targetObs.ConceptId, out var obsConcept))
                {
                    itemLabel = !string.IsNullOrWhiteSpace(obsConcept.PreferredLabel)
                        ? obsConcept.PreferredLabel.Trim()
                        : obsConcept.ExternalCode;
                }

                itemLabel ??= target.Variable;
                targetLabelMap[state.Finding] = !string.IsNullOrWhiteSpace(formTitle) && !string.IsNullOrWhiteSpace(itemLabel)
                    ? $"{formTitle} · {itemLabel}"
                    : (itemLabel ?? formTitle ?? target.Dataset);
                targetEvidenceMap[state.Finding] = evidence.Distinct().ToArray();
            }
            else if (target.Kind == FindingTargetKind.DesignElement
                && NodeRef.TryParse(target.Key, out var designNode))
            {
                var unversionedDesign = designNode.Unversioned();
                targetEvidenceMap[state.Finding] = [unversionedDesign];
                if (unversionedDesign.Kind == NodeKinds.ProtocolVersion
                    && versionNumbersById.TryGetValue(ProtocolVersionId.FromGuid(unversionedDesign.Id), out var verNumber))
                {
                    targetLabelMap[state.Finding] = $"Protocol version {verNumber}";
                }
                else if (unversionedDesign.Kind == NodeKinds.ProtocolElement)
                {
                    if (!elementLabelsByStudy.TryGetValue(detection.StudyId, out var elementMap))
                    {
                        elementMap = [];
                        foreach (var rv in rawVersions.Where(v => v.StudyId == detection.StudyId))
                        {
                            var diff = ProtocolStructuralDiff.Compare(null, rv.Content);
                            if (!diff.IsSuccess)
                            {
                                continue;
                            }

                            foreach (var change in diff.Value!)
                            {
                                if (change.ElementId != ProtocolStructuralDiff.RootId)
                                {
                                    elementMap.TryAdd(ProtocolElementId.For(detection.StudyId, change.ElementId).Value, change.ElementId);
                                }
                            }
                        }

                        elementLabelsByStudy[detection.StudyId] = elementMap;
                    }

                    targetLabelMap[state.Finding] = elementMap.TryGetValue(unversionedDesign.Id, out var elementCode)
                        ? $"Protocol element {elementCode}"
                        : $"Protocol element ({target.Dataset})";
                }
            }

            if (enrollmentId is not { } eid)
            {
                continue;
            }

            enrollmentMap[state.Finding] = eid;
            var matched = new HashSet<string>(StringComparer.Ordinal);
            if (blindedTokensByEnrollment.TryGetValue(eid, out var tokens) && tokens.Count > 0)
            {
                foreach (var variable in detection.ObservedValues.Select(o => o.Variable).Where(tokens.Contains))
                {
                    matched.Add(variable);
                }

                if (!string.IsNullOrWhiteSpace(target.Variable) && tokens.Contains(target.Variable))
                {
                    matched.Add(target.Variable);
                }
            }

            if (isDirectBlindedObs)
            {
                foreach (var variable in detection.ObservedValues.Select(o => o.Variable))
                {
                    matched.Add(variable);
                }

                matched.Add(string.IsNullOrWhiteSpace(target.Variable) ? "value" : target.Variable);
            }

            if (matched.Count > 0)
            {
                blindedVarsMap[state.Finding] = matched;
                if (isDirectBlindedObs && directObsId is { } dObs)
                {
                    blindedObsMap[state.Finding] = dObs;
                }
                else if (blindedObsByEnrollment.TryGetValue(eid, out var repObs))
                {
                    blindedObsMap[state.Finding] = repObs;
                }
            }
        }

        return (enrollmentMap, blindedVarsMap, blindedObsMap, targetLabelMap, targetEvidenceMap);
    }

    private static string FormatCollectionInstanceTitle(CollectionInstance instance, ProtocolDesign? design)
    {
        var specName = design?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, instance.CollectionSpecId, StringComparison.Ordinal))?.Name;
        var formTitle = string.IsNullOrWhiteSpace(specName) ? instance.CollectionSpecId : specName.Trim();

        if (string.IsNullOrWhiteSpace(instance.PlannedActivityId))
        {
            return formTitle;
        }

        var activityName = design?.PlannedActivities
            .FirstOrDefault(a => string.Equals(a.Id, instance.PlannedActivityId, StringComparison.Ordinal))?.Name;
        var visitTitle = string.IsNullOrWhiteSpace(activityName) ? instance.PlannedActivityId.Trim() : activityName.Trim();
        return $"{formTitle} ({visitTitle})";
    }

    private static string? ResolveVariableLabel(
        string? variable,
        CollectionInstance targetForm,
        IReadOnlyList<CollectionInstance> siblingForms,
        IReadOnlyDictionary<ProtocolVersionId, ProtocolDesign?> designsByVersion,
        IReadOnlyDictionary<ConceptId, Concept> concepts)
    {
        if (string.IsNullOrWhiteSpace(variable))
        {
            return null;
        }

        var specId = targetForm.CollectionSpecId;
        var itemId = variable.Trim();
        var dot = itemId.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0 && dot < itemId.Length - 1)
        {
            specId = itemId[..dot];
            itemId = itemId[(dot + 1)..];
        }

        var matchingForm = siblingForms.FirstOrDefault(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal))
            ?? targetForm;
        var specItem = designsByVersion.GetValueOrDefault(matchingForm.ProtocolVersionId)?.CollectionSpecs
            .FirstOrDefault(s => string.Equals(s.Id, matchingForm.CollectionSpecId, StringComparison.Ordinal))
            ?.Items.FirstOrDefault(i => string.Equals(i.Id, itemId, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(specItem?.Prompt))
        {
            return specItem.Prompt.Trim();
        }

        if (specItem is not null && concepts.TryGetValue(specItem.Concept.ConceptId, out var concept)
            && !string.IsNullOrWhiteSpace(concept.PreferredLabel))
        {
            return concept.PreferredLabel.Trim();
        }

        return itemId;
    }

    private static IEnumerable<NodeRef> ResolveReferencedObservationRefs(
        ValidationDetectionRecord detection,
        CollectionInstance targetForm,
        IReadOnlyList<CollectionInstance> siblingForms)
    {
        var candidateVariables = new List<string>();
        if (!string.IsNullOrWhiteSpace(detection.Variable))
        {
            candidateVariables.Add(detection.Variable);
        }

        candidateVariables.AddRange(detection.ObservedValues.Select(o => o.Variable));

        foreach (var rawVar in candidateVariables.Distinct(StringComparer.Ordinal))
        {
            var dot = rawVar.IndexOf('.', StringComparison.Ordinal);
            if (dot > 0 && dot < rawVar.Length - 1)
            {
                var specId = rawVar[..dot];
                var itemId = rawVar[(dot + 1)..];
                foreach (var form in siblingForms.Where(f => string.Equals(f.CollectionSpecId, specId, StringComparison.Ordinal)))
                {
                    foreach (var member in form.Members.Where(m => string.Equals(m.ItemId, itemId, StringComparison.Ordinal)))
                    {
                        yield return NodeRef.For(member.ObservationId);
                    }
                }
            }
            else
            {
                foreach (var member in targetForm.Members.Where(m => string.Equals(m.ItemId, rawVar, StringComparison.Ordinal)))
                {
                    yield return NodeRef.For(member.ObservationId);
                }

                foreach (var form in siblingForms.Where(f => f.Id != targetForm.Id))
                {
                    foreach (var member in form.Members.Where(m => string.Equals(m.ItemId, rawVar, StringComparison.Ordinal)))
                    {
                        yield return NodeRef.For(member.ObservationId);
                    }
                }
            }
        }
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>Registers kernel persistence for validation runs and findings.</summary>
public static class KernelValidationPersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IValidationFindings"/>, the ledger source the validation handlers read and the
    /// <c>validation_findings</c> indicator population (ADR-2488). Call after
    /// <c>AddKernelPersistence</c>.
    /// </summary>
    public static IServiceCollection AddKernelValidationPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<KernelValidationStore>();
        services.TryAddSingleton<IValidationLedgerSource>(sp => sp.GetRequiredService<KernelValidationStore>());
        services.TryAddSingleton<IValidationFindings>(sp => sp.GetRequiredService<KernelValidationStore>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIndicatorPopulationSource, ValidationFindingPopulationSource>());
        return services;
    }
}
