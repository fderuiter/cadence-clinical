using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel;
using Cadence.Kernel.Commands;
using Cadence.Kernel.Evidence;
using Cadence.Kernel.Modules;
using Cadence.Kernel.Navigation;
using Cadence.Kernel.References;
using Cadence.Kernel.Time;
using Cadence.Modules.Execution.Domain;
using Cadence.Modules.Execution.Infrastructure;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Application;

/// <summary>Result of evaluating overdue open queries and generating daily study/site aging digests.</summary>
public sealed record FormQueryEscalationPassResult(
    int EscalatedQueries,
    IReadOnlyList<FormQueryAgingDigestState> Digests);

/// <summary>
/// Evaluates open <see cref="FormQuery"/> aggregates against <see cref="IClock"/>, escalates those open past 14 calendar
/// days to <see cref="FormQueryPriority.High"/>, and emits de-identified daily aging digests for study and site leads
/// (<c>PRD-QRY-002</c>).
/// </summary>
public interface IFormQueryEscalationService
{
    /// <summary>Runs an escalation and daily aging digest pass for <paramref name="studyId"/> in <paramref name="tenantId"/>.</summary>
    Task<CommandResult<FormQueryEscalationPassResult>> RunStudyPassAsync(
        TenantId tenantId,
        StudyId studyId,
        string? reasonForChange = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Default implementation of <see cref="IFormQueryEscalationService"/>.</summary>
internal sealed class FormQueryEscalationService(
    IClock clock,
    ICollectionInstanceRecords forms,
    IKernelNavigator navigator,
    IModuleTenantContextFactory<ExecutionModuleDbContext> contexts,
    IKernelCommandSender sender) : IFormQueryEscalationService
{
    public const string StudyLeadRole = "study_lead";
    public const string SiteInvestigatorRole = "site_investigator";

    public async Task<CommandResult<FormQueryEscalationPassResult>> RunStudyPassAsync(
        TenantId tenantId,
        StudyId studyId,
        string? reasonForChange = null,
        CancellationToken cancellationToken = default)
    {
        var now = KernelTimestamps.ToStoredPrecision(clock.UtcNow);
        var digestDate = DateOnly.FromDateTime(now.UtcDateTime);
        var studySummary = await navigator.ResolveAsync(tenantId, NodeRef.For(studyId), cancellationToken).ConfigureAwait(false);
        var studyCode = studySummary?.Code ?? studySummary?.Title ?? studyId.ToString();
        var effectiveReason = string.IsNullOrWhiteSpace(reasonForChange)
            ? $"14-day query SLA escalation pass for {studyCode} on {digestDate:yyyy-MM-dd}"
            : reasonForChange.Trim();

        // Map all enrollments in this study via navigator in one batch (falling back to children traversal for stubs).
        var enrollmentMeta = new Dictionary<EnrollmentId, (StudySiteId? SiteId, string SiteCode, string SubjectCode)>();
        var batchedEnrollments = await navigator.GetStudyEnrollmentsAsync(tenantId, studyId, cancellationToken).ConfigureAwait(false);
        if (batchedEnrollments is not null)
        {
            foreach (var member in batchedEnrollments)
            {
                var siteCode = member.StudySiteId is { } sId
                    ? (member.SiteCode ?? sId.ToString())
                    : "UNASSIGNED";
                enrollmentMeta[member.EnrollmentId] = (
                    member.StudySiteId,
                    siteCode,
                    member.SubjectCode ?? member.EnrollmentId.ToString());
            }
        }
        else
        {
            var studyChildren = await navigator.GetChildrenAsync(tenantId, NodeRef.For(studyId), cancellationToken).ConfigureAwait(false) ?? [];
            foreach (var child in studyChildren.Select(c => c.Node))
            {
                if (child.Ref.Kind == NodeKinds.StudySite)
                {
                    var siteId = StudySiteId.FromGuid(child.Ref.Id);
                    var siteCode = child.Code ?? child.Title ?? siteId.ToString();
                    var siteMembers = await navigator.GetChildrenAsync(tenantId, child.Ref, cancellationToken).ConfigureAwait(false) ?? [];
                    foreach (var member in siteMembers.Select(m => m.Node).Where(n => n.Ref.Kind == NodeKinds.Enrollment))
                    {
                        var enrollmentId = EnrollmentId.FromGuid(member.Ref.Id);
                        var subjectCode = member.Code ?? member.Title ?? enrollmentId.ToString();
                        enrollmentMeta[enrollmentId] = (siteId, siteCode, subjectCode);
                    }
                }
                else if (child.Ref.Kind == NodeKinds.Enrollment)
                {
                    var enrollmentId = EnrollmentId.FromGuid(child.Ref.Id);
                    var subjectCode = child.Code ?? child.Title ?? enrollmentId.ToString();
                    enrollmentMeta[enrollmentId] = (null, "UNASSIGNED", subjectCode);
                }
            }
        }

        List<FormQuery> openQueries;
        await using (var db = contexts.Create(tenantId))
        {
            var studyEnrollmentIds = enrollmentMeta.Keys.ToList();
            openQueries = await db.FormQueries.AsNoTracking()
                .Where(q => q.Status == FormQueryStatus.Open && studyEnrollmentIds.Contains(q.EnrollmentId))
                .OrderBy(q => q.OpenedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var newlyEscalated = 0;
        foreach (var query in openQueries)
        {
            if (query.EscalatedAt is null && query.DueAt is { } due && now >= due)
            {
                var escalateResult = await sender.SendAsync(
                    new EscalateFormQuery(
                        tenantId,
                        query.Id,
                        query.EnrollmentId,
                        $"query-escalate:{query.Id.Value:D}:{due.UtcTicks}",
                        effectiveReason),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!escalateResult.IsSuccess
                    && escalateResult.Error?.Code != KernelErrors.AlreadyExists.Code
                    && escalateResult.Error?.Code != FormQueryErrors.InvalidTransition.Code
                    && escalateResult.Error?.Code != AssertionErrors.Locked.Code)
                {
                    return escalateResult.Error!;
                }

                if (escalateResult.IsSuccess)
                {
                    newlyEscalated++;
                }
            }
        }

        // Reload all currently escalated open queries in the study to build the daily digest (zero participant PII).
        List<FormQuery> escalatedOpenQueries;
        await using (var db = contexts.Create(tenantId))
        {
            var studyEnrollmentIds = enrollmentMeta.Keys.ToList();
            escalatedOpenQueries = await db.FormQueries.AsNoTracking()
                .Where(q => q.Status == FormQueryStatus.Open
                    && q.EscalatedAt != null
                    && studyEnrollmentIds.Contains(q.EnrollmentId))
                .OrderBy(q => q.OpenedAt)
                .ThenBy(q => q.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (escalatedOpenQueries.Count == 0)
        {
            return CommandResult<FormQueryEscalationPassResult>.Success(new FormQueryEscalationPassResult(newlyEscalated, []));
        }

        // Ensure site/subject codes are resolved via form headers if any enrollment lacked a site header.
        var unresolvedFormIds = escalatedOpenQueries
            .Where(q => !enrollmentMeta.TryGetValue(q.EnrollmentId, out var existing) || existing.SiteId is null)
            .Select(q => q.FormId)
            .Distinct()
            .ToList();
        if (unresolvedFormIds.Count > 0)
        {
            var headersByForm = await forms.FindHeadersAsync(tenantId, unresolvedFormIds, cancellationToken).ConfigureAwait(false);
            var nodesToResolve = new HashSet<NodeRef>();
            foreach (var header in headersByForm.Values)
            {
                if (!enrollmentMeta.TryGetValue(header.EnrollmentId, out var existing) || existing.SiteId is null)
                {
                    if (!header.StudySiteId.IsEmpty)
                    {
                        nodesToResolve.Add(NodeRef.For(header.StudySiteId));
                    }

                    nodesToResolve.Add(NodeRef.For(header.EnrollmentId));
                }
            }

            var summaries = nodesToResolve.Count > 0
                ? await navigator.ResolveManyAsync(tenantId, nodesToResolve, cancellationToken).ConfigureAwait(false)
                : new Dictionary<NodeRef, NodeSummary>();
            foreach (var header in headersByForm.Values)
            {
                if (!enrollmentMeta.TryGetValue(header.EnrollmentId, out var existing) || existing.SiteId is null)
                {
                    var siteSummary = header.StudySiteId.IsEmpty ? null : summaries.GetValueOrDefault(NodeRef.For(header.StudySiteId));
                    var enrSummary = summaries.GetValueOrDefault(NodeRef.For(header.EnrollmentId));
                    enrollmentMeta[header.EnrollmentId] = (
                        header.StudySiteId,
                        siteSummary?.Code ?? siteSummary?.Title ?? header.StudySiteId.ToString(),
                        enrSummary?.Code ?? enrSummary?.Title ?? header.EnrollmentId.ToString());
                }
            }
        }

        var allEntries = new List<(StudySiteId? SiteId, string SiteCode, FormQueryAgingDigestEntry Entry)>();
        foreach (var query in escalatedOpenQueries)
        {
            var (siteId, siteCode, subjectCode) = enrollmentMeta.TryGetValue(query.EnrollmentId, out var meta)
                ? meta
                : (null, "UNASSIGNED", query.EnrollmentId.ToString());
            var ageDays = Math.Max(FormQuery.OpenSlaDays, (int)Math.Floor((now - query.StatusSince).TotalDays));
            var entry = new FormQueryAgingDigestEntry(
                query.Id.Value,
                query.FormId.Value,
                query.ItemId,
                siteCode,
                subjectCode,
                ToSnakeCase(query.OwnerRole),
                query.Priority.ToString().ToLowerInvariant(),
                query.OpenedAt,
                query.DueAt ?? query.StatusSince.AddDays(FormQuery.OpenSlaDays),
                ageDays);
            allEntries.Add((siteId, siteCode, entry));
        }

        var digests = new List<FormQueryAgingDigestState>();

        // 1. Study-wide lead digest.
        var studyDigestId = DeriveDigestId(tenantId, studyId, null, digestDate);
        var studyDigest = await sender.SendAsync(
            new GenerateFormQueryAgingDigest(
                tenantId,
                studyDigestId,
                studyId,
                null,
                StudyLeadRole,
                digestDate,
                studyCode,
                null,
                [.. allEntries.Select(e => e.Entry)],
                $"query-digest:{studyDigestId:D}",
                effectiveReason),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!studyDigest.IsSuccess && studyDigest.Error?.Code != KernelErrors.AlreadyExists.Code)
        {
            return studyDigest.Error!;
        }

        if (studyDigest.Value is { } recordedStudyDigest)
        {
            digests.Add(recordedStudyDigest);
        }

        // 2. Site-scoped lead digests (one per study site with escalated queries).
        foreach (var siteGroup in allEntries.Where(e => e.SiteId is not null).GroupBy(e => e.SiteId!.Value))
        {
            var siteId = siteGroup.Key;
            var siteCode = siteGroup.First().SiteCode;
            var siteDigestId = DeriveDigestId(tenantId, studyId, siteId, digestDate);
            var siteDigest = await sender.SendAsync(
                new GenerateFormQueryAgingDigest(
                    tenantId,
                    siteDigestId,
                    studyId,
                    siteId,
                    SiteInvestigatorRole,
                    digestDate,
                    studyCode,
                    siteCode,
                    [.. siteGroup.Select(e => e.Entry)],
                    $"query-digest:{siteDigestId:D}",
                    effectiveReason),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!siteDigest.IsSuccess && siteDigest.Error?.Code != KernelErrors.AlreadyExists.Code)
            {
                return siteDigest.Error!;
            }

            if (siteDigest.Value is { } recordedSiteDigest)
            {
                digests.Add(recordedSiteDigest);
            }
        }

        return CommandResult<FormQueryEscalationPassResult>.Success(new FormQueryEscalationPassResult(newlyEscalated, digests));
    }

    internal static string ToSnakeCase(FormQueryOwnerRole role) => role switch
    {
        FormQueryOwnerRole.Site => "site",
        FormQueryOwnerRole.Monitor => "monitor",
        FormQueryOwnerRole.DataManager => "data_manager",
        _ => role.ToString().ToLowerInvariant(),
    };

    private static Guid DeriveDigestId(TenantId tenant, StudyId study, StudySiteId? site, DateOnly date)
    {
        var scope = site is { } s ? $"site:{s.Value:D}" : "study";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"cadence-query-aging-digest:{tenant}:{study}:{scope}:{date:yyyy-MM-dd}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
