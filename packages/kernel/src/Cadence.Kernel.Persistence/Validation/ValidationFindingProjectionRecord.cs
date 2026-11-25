using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Validation;

/// <summary>
/// Materialized read-model row in <c>kernel.validation_finding_projections</c> (ADR-2462 §5). Mapped as a non-aggregate
/// record so <c>KernelCommandTransaction</c> commits projection updates atomically alongside <see cref="ValidationRun"/>
/// and <see cref="ValidationFinding"/> commands without claiming a second aggregate root (K-TX-1, K-TX-2).
/// </summary>
internal sealed class ValidationFindingProjectionRecord
{
    public TenantId TenantId { get; set; }

    public ValidationFindingId FindingId { get; set; }

    public StudyId StudyId { get; set; }

    public ValidationStage Stage { get; set; }

    public string PackKey { get; set; } = string.Empty;

    public string Dataset { get; set; } = string.Empty;

    public string RuleId { get; set; } = string.Empty;

    public FindingTargetKind TargetKind { get; set; }

    public string TargetKey { get; set; } = string.Empty;

    public string? Variable { get; set; }

    public ValidationSeverity Severity { get; set; }

    public FindingStatus Status { get; set; }

    public bool Blocks { get; set; }

    public FindingClassification? Classification { get; set; }

    public FindingStatus? Decision { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public bool DecisionInForce { get; set; }

    public ValidationRunId FirstDetectedRunId { get; set; }

    public DateTimeOffset FirstDetectedAt { get; set; }

    public ValidationRunId LastDetectedRunId { get; set; }

    public DateTimeOffset LastDetectedAt { get; set; }

    public ValidationRunId? ResolvedByRunId { get; set; }

    public DateTimeOffset? LatestCleanRunAt { get; set; }

    public long ProjectionVersion { get; set; }

    public FindingTarget Target => new(TargetKind, Dataset, TargetKey, Variable);

    public FindingProjectionState ToProjectionState() => new(
        FindingId,
        Stage,
        PackKey,
        Dataset,
        RuleId,
        Target,
        Severity,
        Status,
        Classification,
        Decision,
        DecidedAt,
        DecisionInForce,
        FirstDetectedRunId,
        FirstDetectedAt,
        LastDetectedRunId,
        LastDetectedAt,
        ResolvedByRunId,
        LatestCleanRunAt);

    public FindingState ToFindingState() => new(
        FindingId,
        Stage,
        PackKey,
        Dataset,
        Severity,
        Status,
        FirstDetectedRunId,
        LastDetectedRunId,
        ResolvedByRunId,
        Classification,
        DecisionInForce);

    public static ValidationFindingProjectionRecord From(TenantId tenant, StudyId study, FindingProjectionState state) => new()
    {
        TenantId = tenant,
        FindingId = state.Finding,
        StudyId = study,
        Stage = state.Stage,
        PackKey = state.PackKey,
        Dataset = state.Dataset,
        RuleId = state.RuleId,
        TargetKind = state.Target.Kind,
        TargetKey = state.Target.Key,
        Variable = state.Target.Variable,
        Severity = state.Severity,
        Status = state.Status,
        Blocks = state.Blocks,
        Classification = state.Classification,
        Decision = state.Decision,
        DecidedAt = state.DecidedAt,
        DecisionInForce = state.DecisionInForce,
        FirstDetectedRunId = state.FirstDetectedRun,
        FirstDetectedAt = state.FirstDetectedAt,
        LastDetectedRunId = state.LastDetectedRun,
        LastDetectedAt = state.LastDetectedAt,
        ResolvedByRunId = state.ResolvedBy,
        LatestCleanRunAt = state.LatestCleanRunAt,
        ProjectionVersion = 1,
    };

    public void Apply(FindingProjectionState state)
    {
        if (Severity == state.Severity
            && Status == state.Status
            && Blocks == state.Blocks
            && Classification == state.Classification
            && Decision == state.Decision
            && DecidedAt == state.DecidedAt
            && DecisionInForce == state.DecisionInForce
            && FirstDetectedRunId == state.FirstDetectedRun
            && FirstDetectedAt == state.FirstDetectedAt
            && LastDetectedRunId == state.LastDetectedRun
            && LastDetectedAt == state.LastDetectedAt
            && ResolvedByRunId == state.ResolvedBy
            && LatestCleanRunAt == state.LatestCleanRunAt)
        {
            return;
        }

        Severity = state.Severity;
        Status = state.Status;
        Blocks = state.Blocks;
        Classification = state.Classification;
        Decision = state.Decision;
        DecidedAt = state.DecidedAt;
        DecisionInForce = state.DecisionInForce;
        FirstDetectedRunId = state.FirstDetectedRun;
        FirstDetectedAt = state.FirstDetectedAt;
        LastDetectedRunId = state.LastDetectedRun;
        LastDetectedAt = state.LastDetectedAt;
        ResolvedByRunId = state.ResolvedBy;
        LatestCleanRunAt = state.LatestCleanRunAt;
        ProjectionVersion++;
    }
}

/// <summary>
/// Stages transactional validation finding projection updates inside <c>KernelCommandTransaction</c>.
/// </summary>
internal static class ValidationProjectionStagingHelper
{
    public static async Task<ValidationRunOutcome> StageValidationRunAsync(
        KernelDbContext db,
        TenantId tenant,
        ValidationRun run,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        var recordedAtUtc = recordedAt.ToUniversalTime();
        var packKeys = run.Packs.Select(p => p.PackKey).ToHashSet(StringComparer.Ordinal);
        var datasets = run.Datasets.Select(d => d.Dataset).ToHashSet(StringComparer.Ordinal);
        if (packKeys.Count == 0 || datasets.Count == 0)
        {
            return new ValidationRunOutcome(run.Id, 0, 0, 0);
        }

        var packList = packKeys.ToList();
        var datasetList = datasets.ToList();
        var recordedUtcDateTime = recordedAtUtc.UtcDateTime;
        var sameOrLaterRuns = await db.Set<ValidationRun>().AsNoTracking()
            .Where(r => r.StudyId == run.StudyId && r.Stage == run.Stage && r.CreatedAt >= recordedUtcDateTime)
            .Select(r => new { r.Id, r.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var outOfOrder = sameOrLaterRuns.Any(r => r.CreatedAt > recordedUtcDateTime || r.Id > run.Id);

        var coveredRows = await db.ValidationFindingProjections
            .Where(p => p.StudyId == run.StudyId
                && p.Stage == run.Stage
                && packList.Contains(p.PackKey)
                && datasetList.Contains(p.Dataset))
            .ToDictionaryAsync(p => p.FindingId, cancellationToken)
            .ConfigureAwait(false);

        var ledgerRun = new LedgerRun(run.Id, run.Stage, recordedAtUtc, packKeys, datasets);
        if (outOfOrder)
        {
            return await StageOutOfOrderCoveredRunAsync(
                db,
                tenant,
                run,
                ledgerRun,
                packList,
                datasetList,
                coveredRows,
                cancellationToken).ConfigureAwait(false);
        }

        var detectionsByFinding = run.Detections.ToDictionary(d => d.FindingId);
        var newCount = 0;
        var resolvedCount = 0;

        foreach (var (findingId, existingRow) in coveredRows)
        {
            var beforeStatus = existingRow.Status;
            detectionsByFinding.TryGetValue(findingId, out var detection);
            var projectedDetection = detection is null ? null : ToLedgerProjectedDetection(detection);
            var nextState = FindingLedger.ApplyCoveringRun(existingRow.ToProjectionState(), ledgerRun, projectedDetection);
            if (beforeStatus != FindingStatus.Resolved && nextState.Status == FindingStatus.Resolved)
            {
                resolvedCount++;
            }

            existingRow.Apply(nextState);
        }

        foreach (var detection in run.Detections)
        {
            if (coveredRows.ContainsKey(detection.FindingId))
            {
                continue;
            }

            newCount++;
            var nextState = FindingLedger.ApplyCoveringRun(
                current: null,
                ledgerRun,
                ToLedgerProjectedDetection(detection));
            db.ValidationFindingProjections.Add(ValidationFindingProjectionRecord.From(tenant, run.StudyId, nextState));
        }

        return new ValidationRunOutcome(run.Id, run.Detections.Count, newCount, resolvedCount);
    }

    public static async Task<(StudyId Study, FindingProjectionState State)?> FindProjectedFindingAsync(
        KernelDbContext db,
        ValidationFindingId finding,
        CancellationToken cancellationToken)
    {
        var row = db.ValidationFindingProjections.Local.FirstOrDefault(p => p.FindingId == finding)
            ?? await db.ValidationFindingProjections
                .SingleOrDefaultAsync(p => p.FindingId == finding, cancellationToken)
                .ConfigureAwait(false);
        return row is null ? null : (row.StudyId, row.ToProjectionState());
    }

    public static async Task StageFindingDecisionAsync(
        KernelDbContext db,
        ValidationFindingId finding,
        LedgerDecision decision,
        CancellationToken cancellationToken)
    {
        var row = db.ValidationFindingProjections.Local.FirstOrDefault(p => p.FindingId == finding)
            ?? await db.ValidationFindingProjections
                .SingleAsync(p => p.FindingId == finding, cancellationToken)
                .ConfigureAwait(false);
        var nextState = FindingLedger.ApplyDecision(row.ToProjectionState(), decision);
        row.Apply(nextState);
    }

    public static async Task<int> StageRebuildStudyAsync(
        KernelDbContext db,
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        var projected = await LoadProjectedStudyStatesAsync(db, study, cancellationToken).ConfigureAwait(false);
        var existing = await db.ValidationFindingProjections
            .Where(p => p.StudyId == study)
            .ToDictionaryAsync(p => p.FindingId, cancellationToken)
            .ConfigureAwait(false);

        var kept = new HashSet<ValidationFindingId>(projected.Count);
        foreach (var state in projected)
        {
            kept.Add(state.Finding);
            if (existing.TryGetValue(state.Finding, out var row))
            {
                row.RuleId = state.RuleId;
                row.TargetKind = state.Target.Kind;
                row.TargetKey = state.Target.Key;
                row.Variable = state.Target.Variable;
                row.Apply(state);
            }
            else
            {
                db.ValidationFindingProjections.Add(ValidationFindingProjectionRecord.From(tenant, study, state));
            }
        }

        foreach (var (findingId, stale) in existing)
        {
            if (!kept.Contains(findingId))
            {
                db.ValidationFindingProjections.Remove(stale);
            }
        }

        return projected.Count;
    }

    internal static async Task<IReadOnlyList<FindingProjectionState>> LoadProjectedStudyStatesAsync(
        KernelDbContext db,
        StudyId study,
        CancellationToken cancellationToken)
    {
        var runsQuery = db.Set<ValidationRun>().AsNoTracking().Where(r => r.StudyId == study);
        var runs = await KernelValidationStore.LoadRunsAsync(db, runsQuery, cancellationToken).ConfigureAwait(false);

        var rawDetections = await db.Set<ValidationDetectionRecord>().AsNoTracking()
            .Where(d => d.StudyId == study)
            .Select(d => new
            {
                d.RunId,
                d.FindingId,
                d.Stage,
                d.PackKey,
                d.RuleId,
                d.TargetKind,
                d.Dataset,
                d.TargetKey,
                d.Variable,
                d.Severity,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var detections = rawDetections.Select(d => new LedgerProjectedDetection(
            d.RunId,
            d.FindingId,
            d.Stage,
            d.PackKey,
            d.RuleId,
            new FindingTarget(d.TargetKind, d.Dataset, d.TargetKey, d.Variable),
            d.Severity)).ToList();

        var decisions = await db.Set<ValidationFinding>().AsNoTracking()
            .Where(f => f.StudyId == study)
            .Select(f => new LedgerDecision(f.Id, f.Decision, f.DecidedAt, f.Classification))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return FindingLedger.Project(runs, detections, decisions);
    }

    internal static LedgerProjectedDetection ToLedgerProjectedDetection(ValidationDetectionRecord d) => new(
        d.RunId,
        d.FindingId,
        d.Stage,
        d.PackKey,
        d.RuleId,
        new FindingTarget(d.TargetKind, d.Dataset, d.TargetKey, d.Variable),
        d.Severity);

    private static async Task<ValidationRunOutcome> StageOutOfOrderCoveredRunAsync(
        KernelDbContext db,
        TenantId tenant,
        ValidationRun run,
        LedgerRun thisRun,
        List<string> packList,
        List<string> datasetList,
        Dictionary<ValidationFindingId, ValidationFindingProjectionRecord> coveredRows,
        CancellationToken cancellationToken)
    {
        var runsQuery = db.Set<ValidationRun>().AsNoTracking()
            .Where(r => r.StudyId == run.StudyId
                && r.Stage == run.Stage
                && r.Packs.Any(p => packList.Contains(p.PackKey))
                && r.Datasets.Any(d => datasetList.Contains(d.Dataset)));
        var existingRuns = await KernelValidationStore.LoadRunsAsync(db, runsQuery, cancellationToken).ConfigureAwait(false);

        var rawDetections = await db.Set<ValidationDetectionRecord>().AsNoTracking()
            .Where(d => d.StudyId == run.StudyId
                && d.Stage == run.Stage
                && packList.Contains(d.PackKey)
                && datasetList.Contains(d.Dataset))
            .Select(d => new
            {
                d.RunId,
                d.FindingId,
                d.Stage,
                d.PackKey,
                d.RuleId,
                d.TargetKind,
                d.Dataset,
                d.TargetKey,
                d.Variable,
                d.Severity,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingDetections = rawDetections.Select(d => new LedgerProjectedDetection(
            d.RunId,
            d.FindingId,
            d.Stage,
            d.PackKey,
            d.RuleId,
            new FindingTarget(d.TargetKind, d.Dataset, d.TargetKey, d.Variable),
            d.Severity)).ToList();

        var findingIds = existingDetections.Select(d => d.Finding)
            .Concat(run.Detections.Select(d => d.FindingId))
            .Distinct()
            .ToList();
        var decisions = findingIds.Count == 0
            ? []
            : await db.Set<ValidationFinding>().AsNoTracking()
                .Where(f => findingIds.Contains(f.Id))
                .Select(f => new LedgerDecision(f.Id, f.Decision, f.DecidedAt, f.Classification))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        var thisDetections = run.Detections.Select(ToLedgerProjectedDetection).ToList();
        var afterStates = FindingLedger.Project(
            [.. existingRuns, thisRun],
            [.. existingDetections, .. thisDetections],
            decisions);

        var newCount = run.Detections.Count(d => !coveredRows.ContainsKey(d.FindingId));
        var resolvedCount = 0;
        foreach (var state in afterStates)
        {
            if (coveredRows.TryGetValue(state.Finding, out var existing))
            {
                if (existing.Status != FindingStatus.Resolved && state.Status == FindingStatus.Resolved)
                {
                    resolvedCount++;
                }

                existing.Apply(state);
            }
            else
            {
                db.ValidationFindingProjections.Add(ValidationFindingProjectionRecord.From(tenant, run.StudyId, state));
            }
        }

        return new ValidationRunOutcome(run.Id, run.Detections.Count, newCount, resolvedCount);
    }
}

/// <summary>
/// Deterministically rebuilds <c>kernel.validation_finding_projections</c> from the immutable validation run, detection,
/// and decision tables (Requirement 7, ADR-2462 §5).
/// </summary>
internal sealed class ValidationFindingProjector(KernelTenantDbContextFactory contexts) : IValidationFindingProjector
{
    public async Task<int> RebuildStudyAsync(TenantId tenant, StudyId study, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030

        var count = await RebuildStudyCoreAsync(db, tenant, study, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    public async Task<int> RebuildTenantAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        await using var db = contexts.Create(tenant);
#pragma warning disable RS0030 // Kernel contexts register no retrying execution strategy (KernelDatabase.UseKernelNpgsql).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore RS0030

        var studyIds = await db.Studies.AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var total = 0;
        foreach (var study in studyIds)
        {
            total += await RebuildStudyCoreAsync(db, tenant, study, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return total;
    }

    private static async Task<int> RebuildStudyCoreAsync(
        KernelDbContext db,
        TenantId tenant,
        StudyId study,
        CancellationToken cancellationToken)
    {
        var projected = await ValidationProjectionStagingHelper
            .LoadProjectedStudyStatesAsync(db, study, cancellationToken)
            .ConfigureAwait(false);

        if (projected.Count == 0)
        {
            await db.ValidationFindingProjections
                .Where(p => p.StudyId == study)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
            return 0;
        }

        var keptIds = projected.Select(s => s.Finding).ToList();
        await db.ValidationFindingProjections
            .Where(p => p.StudyId == study && !keptIds.Contains(p.FindingId))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var state in projected)
        {
            var tenantGuid = tenant.Value;
            var findingGuid = state.Finding.Value;
            var studyGuid = study.Value;
            var stage = state.Stage.ToString();
            var packKey = state.PackKey;
            var dataset = state.Dataset;
            var ruleId = state.RuleId;
            var targetKind = state.Target.Kind.ToString();
            var targetKey = state.Target.Key;
            var variable = state.Target.Variable;
            var severity = state.Severity.ToString();
            var status = state.Status.ToString();
            var blocks = state.Blocks;
            var classification = state.Classification?.ToString();
            var decision = state.Decision?.ToString();
            var decidedAt = state.DecidedAt;
            var decisionInForce = state.DecisionInForce;
            var firstRunGuid = state.FirstDetectedRun.Value;
            var firstAt = state.FirstDetectedAt;
            var lastRunGuid = state.LastDetectedRun.Value;
            var lastAt = state.LastDetectedAt;
            var resolvedByGuid = state.ResolvedBy?.Value;
            var latestCleanAt = state.LatestCleanRunAt;

            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO kernel.validation_finding_projections (
                    tenant_id, finding_id, study_id, stage, pack_key, dataset, rule_id,
                    target_kind, target_key, variable, severity, status, blocks,
                    classification, decision, decided_at, decision_in_force,
                    first_detected_run_id, first_detected_at, last_detected_run_id, last_detected_at,
                    resolved_by_run_id, latest_clean_run_at, projection_version
                )
                VALUES (
                    {tenantGuid}, {findingGuid}, {studyGuid}, {stage}, {packKey}, {dataset}, {ruleId},
                    {targetKind}, {targetKey}, {variable}, {severity}, {status}, {blocks},
                    {classification}, {decision}, {decidedAt}, {decisionInForce},
                    {firstRunGuid}, {firstAt}, {lastRunGuid}, {lastAt},
                    {resolvedByGuid}, {latestCleanAt}, 1
                )
                ON CONFLICT (tenant_id, finding_id) DO UPDATE SET
                    study_id = EXCLUDED.study_id,
                    stage = EXCLUDED.stage,
                    pack_key = EXCLUDED.pack_key,
                    dataset = EXCLUDED.dataset,
                    rule_id = EXCLUDED.rule_id,
                    target_kind = EXCLUDED.target_kind,
                    target_key = EXCLUDED.target_key,
                    variable = EXCLUDED.variable,
                    severity = EXCLUDED.severity,
                    status = EXCLUDED.status,
                    blocks = EXCLUDED.blocks,
                    classification = EXCLUDED.classification,
                    decision = EXCLUDED.decision,
                    decided_at = EXCLUDED.decided_at,
                    decision_in_force = EXCLUDED.decision_in_force,
                    first_detected_run_id = EXCLUDED.first_detected_run_id,
                    first_detected_at = EXCLUDED.first_detected_at,
                    last_detected_run_id = EXCLUDED.last_detected_run_id,
                    last_detected_at = EXCLUDED.last_detected_at,
                    resolved_by_run_id = EXCLUDED.resolved_by_run_id,
                    latest_clean_run_at = EXCLUDED.latest_clean_run_at,
                    projection_version = kernel.validation_finding_projections.projection_version + 1
                """,
                cancellationToken).ConfigureAwait(false);
        }

        return projected.Count;
    }
}
