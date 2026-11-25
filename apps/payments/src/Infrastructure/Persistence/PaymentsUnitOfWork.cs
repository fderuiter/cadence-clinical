using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Database;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Apps.Payments.Infrastructure.Persistence;

/// <summary>
/// Request-scoped persistence unit of work and row-level wallet locking gateway for the Payments service.
/// Consolidates endpoint-driven <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> commits into a single seam
/// while sharing the request-scoped <see cref="PaymentsDbContext"/> for GxP audit attribution.
/// </summary>
public sealed class PaymentsUnitOfWork(PaymentsDbContext dbContext)
{
    public PaymentsDbContext Context { get; } = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public Task<int> CommitAsync(CancellationToken ct = default) =>
        Context.SaveChangesAsync(ct);

    public bool SupportsRowLevelForUpdate =>
        Context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;

    public Task<ParticipantWallet?> LockWalletByIdAsync(
        string walletId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate
            ? Context.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"Id\" = {walletId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
            : Context.ParticipantWallets
                .FirstOrDefaultAsync(w => w.Id == walletId, ct);

    public Task<ParticipantWallet?> LockWalletBySubjectAndStudyAsync(
        string subjectId,
        string studyId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate
            ? Context.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"SubjectId\" = {subjectId} AND \"StudyId\" = {studyId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
            : Context.ParticipantWallets
                .FirstOrDefaultAsync(w => w.SubjectId == subjectId && w.StudyId == studyId, ct);

    public Task<List<ParticipantWallet>> LockWalletsBySubjectAsync(
        string subjectId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate
            ? Context.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"SubjectId\" = {subjectId} FOR UPDATE")
                .ToListAsync(ct)
            : Context.ParticipantWallets
                .Where(w => w.SubjectId == subjectId)
                .ToListAsync(ct);

    public async Task<TResult> ExecuteWalletTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        for (int walletInitAttempt = 0; walletInitAttempt < 2; walletInitAttempt++)
        {
            try
            {
                return await Context.ExecuteInTransactionAsync(async ct =>
                {
                    if (Context.Database.IsRelational())
                    {
                        Context.ChangeTracker.Clear();
                    }

                    return await operation(ct);
                }, cancellationToken: cancellationToken);
            }
            catch (DbUpdateException ex) when (walletInitAttempt == 0 && IsUniqueViolation(ex, "payments_participant_wallets"))
            {
                Context.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Unreachable wallet initialization retry fallback.");
    }

    public static bool IsUniqueViolation(DbUpdateException ex, string? constraintContains = null) =>
        (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && (constraintContains is null
                || (pg.ConstraintName is not null
                    && pg.ConstraintName.Contains(constraintContains, StringComparison.OrdinalIgnoreCase))))
        || (ex.InnerException is not null
            && string.Equals(ex.InnerException.GetType().Name, "SqliteException", StringComparison.Ordinal)
            && ex.InnerException.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            && (constraintContains is null
                || ex.InnerException.Message.Contains(constraintContains, StringComparison.OrdinalIgnoreCase)));
}
