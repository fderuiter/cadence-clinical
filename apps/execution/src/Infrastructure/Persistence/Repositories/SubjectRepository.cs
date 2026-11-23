using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Execution.Domain.Ports;
using Cadence.Apps.Execution.Domain.Subjects;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core adapter for <see cref="ISubjectRepository"/>. <see cref="EnrollNextIndexAsync"/> implements
/// <c>02_technical_design.md</c> §4.2: inside the caller's <see cref="ExecutionRepositoryBase.ExecuteInTransactionAsync{TResult}"/>
/// unit of work it takes <c>pg_advisory_xact_lock(hashtext(studyCode))</c> (see <see cref="AdvisoryLockSql"/>),
/// which PostgreSQL holds until that transaction commits, so the read of the highest index and the caller's insert
/// are serialized per study. The unit of work runs inside the context's retrying execution strategy, and a
/// transient failure re-runs it whole, lock included (#5383). The advisory-lock statement is PostgreSQL-specific
/// and is skipped when <c>ExecutionDbContext.Database</c> is not Npgsql (SQLite/InMemory in tests).
/// Replaces <c>apps/execution/adapters/repositories.py</c> /
/// <c>apps/execution/infrastructure/repositories/execution_repositories.py</c>.
/// @req:PRD-SUB-001
/// </summary>
public sealed class SubjectRepository(ExecutionDbContext context)
    : ExecutionRepositoryBase(context), ISubjectRepository
{
    public async Task<int> EnrollNextIndexAsync(StudyCode studyCode, CancellationToken cancellationToken)
    {
        if (Context.Database.IsRelational() && Context.Database.CurrentTransaction is null)
        {
            // Outside a transaction the advisory lock would be released as soon as its statement ended.
            throw new InvalidOperationException(
                $"{nameof(EnrollNextIndexAsync)} must run inside {nameof(ExecuteInTransactionAsync)}, which holds the per-study lock until the insert commits.");
        }

        if (Context.Database.IsNpgsql())
        {
            await Context.Database
                .ExecuteSqlInterpolatedAsync(AdvisoryLockSql.ForStudy(studyCode), cancellationToken)
                .ConfigureAwait(false);
        }

        int? maxIndex = await Context.ClinicalSubjects.AsNoTracking()
            .Where(s => s.StudyCode == studyCode)
            .Select(s => (int?)s.EnrollmentIndex)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);

        return maxIndex is null ? 0 : maxIndex.Value + 1;
    }

    public async Task<ClinicalSubject?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            return null;
        }

        var subjectId = new SubjectId(guid);

        // Tracked (not AsNoTracking): callers such as SubjectApplicationService.AdvanceStateAsync load via
        // this method, call a mutating domain method on the result, and persist the change through a
        // subsequent SaveChangesAsync with no intervening Attach/Update call (02_technical_design.md §3.2) --
        // that flow only works if the returned entity is tracked by this repository's ExecutionDbContext.
        return await Context.ClinicalSubjects
            .SingleOrDefaultAsync(s => s.Id == subjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ClinicalSubject?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var subjectCode = new SubjectCode(code);

        // Tracked -- see GetByIdAsync's comment; the same load-then-mutate-then-save flow applies here.
        return await Context.ClinicalSubjects
            .SingleOrDefaultAsync(s => s.SubjectCode == subjectCode, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SubjectPage> ListAsync(SubjectListCriteria criteria, CancellationToken cancellationToken)
    {
        var query = Context.ClinicalSubjects.AsNoTracking()
            .Where(s => s.StudyCode == criteria.StudyCode);

        if (criteria.SiteCode is { } siteCode)
        {
            query = query.Where(s => s.SiteCode == siteCode);
        }
        else if (criteria.AllowedSites is { } allowedSites)
        {
            List<SiteCode?> siteValues = allowedSites.Select(s => (SiteCode?)s).ToList();
            query = query.Where(s => siteValues.Contains(s.SiteCode));
        }

        if (criteria.Status is { } status)
        {
            query = query.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Query))
        {
            string trimmed = criteria.Query.Trim();
            if (Context.Database.IsRelational())
            {
                string pattern = $"%{EscapeLikePattern(trimmed.ToLowerInvariant())}%";
                query = query.Where(s => EF.Functions.Like(((string)(object)s.SubjectCode).ToLower(), pattern, "\\"));
            }
            else
            {
                query = query.Where(s => s.SubjectCode.Value.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
            }
        }

        int total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderBy(s => s.EnrollmentIndex)
            .ThenBy(s => s.SubjectCode)
            .Skip(criteria.Offset)
            .Take(criteria.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SubjectPage(items, total);
    }

    public async Task<IReadOnlyDictionary<SubjectState, int>> CountByStatusAsync(
        SubjectCountCriteria criteria,
        CancellationToken cancellationToken)
    {
        var query = Context.ClinicalSubjects.AsNoTracking()
            .Where(s => s.StudyCode == criteria.StudyCode);

        if (criteria.SiteCode is { } siteCode)
        {
            query = query.Where(s => s.SiteCode == siteCode);
        }
        else if (criteria.AllowedSites is { } allowedSites)
        {
            List<SiteCode?> siteValues = allowedSites.Select(s => (SiteCode?)s).ToList();
            query = query.Where(s => siteValues.Contains(s.SiteCode));
        }

        var rows = await query
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(r => r.Status, r => r.Count);
    }

    private static string EscapeLikePattern(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    public async Task AddAsync(ClinicalSubject subject, CancellationToken cancellationToken) =>
        await Context.ClinicalSubjects.AddAsync(subject, cancellationToken).ConfigureAwait(false);

    public async Task RevokePendingActivationTokensAsync(SubjectCode subjectCode, CancellationToken cancellationToken)
    {
        var pending = await Context.SubjectActivationTokens
            .Where(t => t.SubjectCode == subjectCode && !t.IsRevoked && t.RedeemedAt == null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var token in pending)
        {
            token.Revoke();
        }
    }

    public async Task AddActivationTokenAsync(SubjectActivationToken token, CancellationToken cancellationToken) =>
        await Context.SubjectActivationTokens.AddAsync(token, cancellationToken).ConfigureAwait(false);

    public async Task<SubjectActivationToken?> GetActivationTokenByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return null;
        }

        return await Context.SubjectActivationTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<SubjectActivationToken?> GetRedeemedActivationByPrincipalHashAsync(string principalHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(principalHash))
        {
            return null;
        }

        return await Context.SubjectActivationTokens.AsNoTracking()
            .Where(t => t.RedeemedByPrincipalHash == principalHash && t.RedeemedAt != null && !t.IsRevoked)
            .OrderByDescending(t => t.RedeemedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
