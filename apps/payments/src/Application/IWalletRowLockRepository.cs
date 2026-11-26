using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Abstracts row-level <c>FOR UPDATE</c> wallet locking and transactional wallet units of work.
/// </summary>
public interface IWalletRowLockRepository
{
    bool IsRelational { get; }

    void ClearChangeTracker();

    bool IsUniqueViolation(DbUpdateException ex, string? constraintContains = null);

    Task<ParticipantWallet?> LockWalletByIdAsync(string walletId, CancellationToken ct = default);

    Task<ParticipantWallet?> LockWalletBySubjectAndStudyAsync(
        string subjectId,
        string studyId,
        CancellationToken ct = default);

    Task<List<ParticipantWallet>> LockWalletsBySubjectAsync(
        string subjectId,
        CancellationToken ct = default);

    Task<IResult> ExecuteWalletUnitOfWorkAsync(
        Func<CancellationToken, Task<IResult>> unitOfWork,
        CancellationToken cancellationToken = default);
}
