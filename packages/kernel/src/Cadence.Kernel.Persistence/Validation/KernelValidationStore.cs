using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Application.Validation;
using Cadence.Kernel.Domain.Validation;
using Cadence.Kernel.Indicators;
using Cadence.Kernel.Persistence.Indicators;
using Cadence.Kernel.Persistence.Tenancy;
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

        var (enrollmentByFinding, blindedVarsByFinding, blindedObsByFinding) = await ValidationFindingBlindingResolver.ResolveBlindingContextAsync(
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
                blindedObs);
        })];
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
