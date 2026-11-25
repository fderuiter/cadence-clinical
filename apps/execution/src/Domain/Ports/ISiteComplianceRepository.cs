using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;

namespace Cadence.Apps.Execution.Domain.Ports;

/// <summary>
/// Persistence port for <see cref="SiteComplianceCache"/>. <see cref="GetAsync"/> backs Rule 2's
/// enrollment/activation compliance gates (<c>02_technical_design.md</c> §3.2's
/// <c>SubjectApplicationService.AdvanceStateAsync</c> is the representative caller: it looks up the
/// <c>SITE_ACTIVATION</c> milestone for the subject's own site before allowing a transition into
/// <c>Enrolled</c>).
/// <para>
/// MIG-SUBJ-013 shipped this port with <see cref="GetAsync"/> only, deliberately deferring the write side
/// to "a later ticket" (see that ticket's own <c>SiteComplianceRepositoryTests</c> doc comment) — that
/// ticket is MIG-SUBJ-020, which added <see cref="AddAsync"/> and made <see cref="GetAsync"/> tracked so its
/// <c>SiteComplianceApplicationService.ApplyEtmfWebhookAsync</c> can upsert the eTMF-webhook cache row.
/// </para>
/// Replaces <c>apps/execution/adapters/repositories.py</c> (compliance-cache lookup methods).
/// @req:PRD-SUB-001
/// </summary>
public interface ISiteComplianceRepository
{
    /// <summary>
    /// Looks up the compliance-cache row for <paramref name="studyCode"/> / <paramref name="siteCode"/> /
    /// <paramref name="milestone"/>, or <see langword="null"/> if none exists — never throws.
    /// <paramref name="siteCode"/> is nullable because a <see cref="ClinicalSubject"/> may legally have no
    /// assigned site yet; since <see cref="SiteComplianceCache.SiteCode"/> is itself never null (every cache
    /// row names a definite site), a <see langword="null"/> <paramref name="siteCode"/> can never match any
    /// row and this deliberately always resolves to <see langword="null"/> in that case, rather than treating
    /// it as "any site" — a subject with no site cannot satisfy a site-specific compliance gate.
    /// <para>
    /// Tracked (not <c>AsNoTracking()</c>), mirroring <see cref="ISubjectRepository.GetByIdAsync"/>'s own
    /// rationale: <c>SiteComplianceApplicationService.ApplyEtmfWebhookAsync</c> (MIG-SUBJ-020) loads via this
    /// method, calls <see cref="SiteComplianceCache.ApplyWebhookUpdate"/> on the result, and persists the
    /// change through a subsequent <see cref="SaveChangesAsync"/> with no intervening Attach/Update call —
    /// that flow only works if the returned entity is tracked by this repository's <c>ExecutionDbContext</c>.
    /// Read-only callers (e.g. <c>SubjectApplicationService.AdvanceStateAsync</c>'s gate check) are unaffected
    /// by tracking either way, since they never mutate the result.
    /// </para>
    /// </summary>
    Task<SiteComplianceCache?> GetAsync(StudyCode studyCode, SiteCode? siteCode, string milestone, CancellationToken cancellationToken);

    /// <summary>
    /// Batch-loads tracked <see cref="SiteComplianceCache"/> rows for <paramref name="studyCode"/>,
    /// <paramref name="milestone"/>, and <paramref name="siteCodes"/> keyed by <see cref="SiteCode"/>.
    /// </summary>
    async Task<IReadOnlyDictionary<SiteCode, SiteComplianceCache>> GetBySitesAsync(
        StudyCode studyCode,
        IReadOnlyCollection<SiteCode> siteCodes,
        string milestone,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<SiteCode, SiteComplianceCache>();
        foreach (var siteCode in siteCodes)
        {
            if (result.ContainsKey(siteCode))
            {
                continue;
            }

            var existing = await GetAsync(studyCode, siteCode, milestone, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                result[siteCode] = existing;
            }
        }

        return result;
    }

    /// <summary>Stages a new <see cref="SiteComplianceCache"/> for insertion; takes effect on the next
    /// <see cref="SaveChangesAsync"/>. Added by MIG-SUBJ-020 for the eTMF-webhook "no existing row" branch —
    /// mirrors <see cref="IConsentRepository.AddAsync"/>/<see cref="ISubjectRepository.AddAsync"/>.</summary>
    Task AddAsync(SiteComplianceCache cache, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a bitemporal <see cref="SiteActivationPeriod"/> period row for <paramref name="cache"/> and closes
    /// any previously open recorded period overlapping the new transition instant.
    /// </summary>
    Task RecordActivationPeriodAsync(
        SiteComplianceCache cache,
        DateTimeOffset recordedAt,
        string? reasonForChange,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Appends bitemporal <see cref="SiteActivationPeriod"/> rows for a batch of created or modified
    /// <see cref="SiteComplianceCache"/> rows and closes open periods for <paramref name="existingCacheIds"/> in one pass.
    /// </summary>
    async Task RecordActivationPeriodsBatchAsync(
        IReadOnlyList<SiteComplianceCache> caches,
        IReadOnlySet<Guid> existingCacheIds,
        DateTimeOffset recordedAt,
        string? reasonForChange,
        CancellationToken cancellationToken)
    {
        foreach (var cache in caches)
        {
            await RecordActivationPeriodAsync(cache, recordedAt, reasonForChange, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reconstructs the bitemporal <see cref="SiteActivationPeriod"/> row active at <paramref name="validAt"/>
    /// as known at <paramref name="knownAt"/>.
    /// </summary>
    Task<SiteActivationPeriod?> GetPeriodAsOfAsync(
        StudyCode studyCode,
        SiteCode siteCode,
        string milestone,
        DateTimeOffset validAt,
        DateTimeOffset knownAt,
        CancellationToken cancellationToken) => Task.FromResult<SiteActivationPeriod?>(null);

    /// <summary>
    /// Persists all pending changes tracked by this repository's underlying
    /// <c>Infrastructure.Persistence.ExecutionDbContext</c> (the same scoped context every repository in this
    /// slice wraps), wrapped in the same <c>DbUpdateConcurrencyException</c> retry policy shared by all three
    /// repositories (<c>Infrastructure.Persistence.Repositories.ExecutionRepositoryBase</c>).
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
