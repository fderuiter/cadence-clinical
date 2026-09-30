using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Indicators;
using Cadence.Kernel.Persistence.Tenancy;
using Cadence.Kernel.References;
using Cadence.Kernel.Validation;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Kernel.Persistence.Indicators;

/// <summary>
/// The <see cref="KernelPopulations.Enrollments"/> population (ADR-2488): a study's enrollments with the status
/// Cadence recorded for the as-of, as it knew it then (K-TIME-2). Enrollments with no status then (not yet screened)
/// are not in it.
/// </summary>
internal sealed class EnrollmentPopulationSource(KernelTenantDbContextFactory contexts) : IIndicatorPopulationSource
{
    public string Population => KernelPopulations.Enrollments;

    public async Task<PopulationRead> ReadAsync(TenantId tenant, StudyId study, DateTimeOffset asOf, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        await using var db = contexts.Create(tenant);
        var rows = await (
                from period in db.EnrollmentStatusPeriods.AsNoTracking()
                join enrollment in db.Enrollments.AsNoTracking() on period.EnrollmentId equals enrollment.Id
                where enrollment.StudyId == study && period.Period.Valid.Contains(asOf) && period.Period.Recorded.Contains(asOf)
                orderby enrollment.Id
                select new { enrollment.Id, enrollment.StudySiteId, enrollment.EnrollmentNumber, period.Status })
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var records = rows.Take(limit).Select(r => new PopulationRecord(
            NodeRef.For(r.Id),
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["STATUS"] = r.Status.ToString(),
                ["STUDY_SITE"] = r.StudySiteId.Value.ToString(),
                ["ENROLLMENT_NUMBER"] = r.EnrollmentNumber?.ToString(CultureInfo.InvariantCulture),
            }));
        return new PopulationRead([.. records.OrderBy(r => r.Node.ToString(), StringComparer.Ordinal)], rows.Count > limit);
    }
}

/// <summary>
/// The <see cref="KernelPopulations.ValidationFindings"/> population (ADR-2488): a study's findings with the status
/// derived from its runs and decisions as currently known (ADR-2462). Findings have no bitemporal history, so the
/// as-of does not change which are read; the evaluation's input hash records what was.
/// </summary>
internal sealed class ValidationFindingPopulationSource(IValidationFindings findings) : IIndicatorPopulationSource
{
    public string Population => KernelPopulations.ValidationFindings;

    public async Task<PopulationRead> ReadAsync(TenantId tenant, StudyId study, DateTimeOffset asOf, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var records = new List<PopulationRecord>();
        var skip = 0;
        while (true)
        {
            var page = await findings.ListAsync(tenant, new FindingQuery(study) { Skip = skip, Take = FindingQuery.MaxTake }, cancellationToken)
                .ConfigureAwait(false);
            records.AddRange(page.Findings.Select(Record));
            skip += page.Findings.Count;
            if (page.Findings.Count == 0 || skip >= page.Total || records.Count > limit)
            {
                break;
            }
        }

        return new PopulationRead([.. records.Take(limit).OrderBy(r => r.Node.ToString(), StringComparer.Ordinal)], records.Count > limit);
    }

    private static PopulationRecord Record(ValidationFindingView finding) => new(
        NodeRef.For(finding.Id),
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["STATUS"] = finding.Status.ToString(),
            ["SEVERITY"] = finding.Severity.ToString(),
            ["STAGE"] = finding.Stage.ToString(),
            ["PACK"] = finding.PackKey,
            ["RULE"] = finding.RuleId,
            ["DATASET"] = finding.Target.Dataset,
            ["CLASSIFICATION"] = finding.Classification?.ToString(),
            ["BLOCKS"] = finding.Blocks ? "true" : "false",
        });
}
