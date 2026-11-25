using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Domain;
using Cadence.Database;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Cadence.Apps.Payments.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL-aware row-level <c>FOR UPDATE</c> locking and retryable transactional runner for participant wallets.
/// </summary>
public sealed class WalletRowLockRepository(PaymentsDbContext dbContext) : IWalletRowLockRepository
{
    public bool IsRelational => dbContext.Database.IsRelational();

    public void ClearChangeTracker() => dbContext.ChangeTracker.Clear();

    public bool IsUniqueViolation(DbUpdateException ex, string? constraintContains = null) =>
        (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && (constraintContains is null
                || (pg.ConstraintName is not null
                    && pg.ConstraintName.Contains(constraintContains, StringComparison.OrdinalIgnoreCase))))
        || (ex.InnerException is not null
            && string.Equals(ex.InnerException.GetType().Name, "SqliteException", StringComparison.Ordinal)
            && ex.InnerException.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            && (constraintContains is null
                || ex.InnerException.Message.Contains(constraintContains, StringComparison.OrdinalIgnoreCase)));

    public Task<ParticipantWallet?> LockWalletByIdAsync(string walletId, CancellationToken ct = default) =>
        SupportsRowLevelForUpdate()
            ? dbContext.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"Id\" = {walletId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
            : dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.Id == walletId, ct);

    public Task<ParticipantWallet?> LockWalletBySubjectAndStudyAsync(
        string subjectId,
        string studyId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate()
            ? dbContext.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"SubjectId\" = {subjectId} AND \"StudyId\" = {studyId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
            : dbContext.ParticipantWallets
                .FirstOrDefaultAsync(w => w.SubjectId == subjectId && w.StudyId == studyId, ct);

    public Task<List<ParticipantWallet>> LockWalletsBySubjectAsync(
        string subjectId,
        CancellationToken ct = default) =>
        SupportsRowLevelForUpdate()
            ? dbContext.ParticipantWallets
                .FromSqlInterpolated($"SELECT * FROM payments_participant_wallets WHERE \"SubjectId\" = {subjectId} FOR UPDATE")
                .ToListAsync(ct)
            : dbContext.ParticipantWallets
                .Where(w => w.SubjectId == subjectId)
                .ToListAsync(ct);

    public async Task<IResult> ExecuteWalletUnitOfWorkAsync(
        Func<CancellationToken, Task<IResult>> unitOfWork,
        CancellationToken cancellationToken = default)
    {
        for (int walletInitAttempt = 0; walletInitAttempt < 2; walletInitAttempt++)
        {
            try
            {
                return await dbContext.ExecuteInTransactionAsync(async ct =>
                {
                    if (dbContext.Database.IsRelational())
                    {
                        dbContext.ChangeTracker.Clear();
                    }

                    return await unitOfWork(ct);
                }, cancellationToken: cancellationToken);
            }
            catch (DbUpdateException ex) when (walletInitAttempt == 0 && IsUniqueViolation(ex, "payments_participant_wallets"))
            {
                dbContext.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Unreachable wallet initialization retry fallback.");
    }

    private bool SupportsRowLevelForUpdate() =>
        dbContext.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) is true;
}
