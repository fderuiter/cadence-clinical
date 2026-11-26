using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application;

internal readonly record struct SelfWalletMutationScope(
    PaymentsDbContext DbContext,
    IWalletRowLockRepository WalletLocks,
    HttpContext HttpContext,
    string? IdempotencyKeyHeader,
    string? BodyIdempotencyKey,
    string? RequestedAccountOwnerKey,
    string? RequestedWalletId,
    string? RequestedStudyCode,
    string? RequestedSiteCode,
    bool CreateIfMissing,
    DateTimeOffset Now,
    IResult? MissingWalletResult = null);

internal readonly record struct SelfWalletMutationContext(
    ParticipantWallet Wallet,
    Principal Caller,
    string RolesText,
    string? IdempotencyKey,
    DateTimeOffset Now,
    CancellationToken CancellationToken);

/// <summary>
/// Resolves or provisions the signed-in participant's study wallet with strict cross-participant and scope isolation.
/// </summary>
public static class SelfWalletResolver
{
    internal static async Task<IResult> ExecuteLockedOwnWalletMutationAsync(
        SelfWalletMutationScope scope,
        Func<SelfWalletMutationContext, Task<IResult>> executeAsync,
        Func<ParticipantWallet, Principal, string, Task<IResult?>> resolveIdempotentRaceAsync)
    {
        if (!TryPrepareSelfServiceRequest(
            scope.HttpContext,
            scope.IdempotencyKeyHeader,
            scope.BodyIdempotencyKey,
            out var caller,
            out string rolesText,
            out string? idempotencyKey,
            out var prepError))
        {
            return prepError!;
        }

        string? resolvedWalletId = null;
        try
        {
            return await scope.WalletLocks.ExecuteWalletUnitOfWorkAsync(async ct =>
            {
                var (wallet, error) = await ResolveRequiredLockedWalletAsync(
                    scope.DbContext,
                    scope.WalletLocks,
                    scope.HttpContext,
                    caller,
                    scope.RequestedAccountOwnerKey,
                    scope.RequestedWalletId,
                    scope.RequestedStudyCode,
                    scope.RequestedSiteCode,
                    scope.CreateIfMissing,
                    scope.Now,
                    ct,
                    scope.MissingWalletResult);
                if (error != null)
                {
                    return error;
                }

                resolvedWalletId = wallet!.Id;
                return await executeAsync(new SelfWalletMutationContext(wallet, caller, rolesText, idempotencyKey, scope.Now, ct));
            });
        }
        catch (DbUpdateException) when (!string.IsNullOrEmpty(idempotencyKey))
        {
            scope.WalletLocks.ClearChangeTracker();
            var racedWallet = resolvedWalletId is not null
                ? await scope.DbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.Id == resolvedWalletId)
                : await scope.DbContext.ParticipantWallets.AsNoTracking().FirstOrDefaultAsync(w => w.SubjectId == caller.UserId);
            if (racedWallet != null)
            {
                var racedResult = await resolveIdempotentRaceAsync(racedWallet, caller, idempotencyKey);
                if (racedResult != null)
                {
                    return racedResult;
                }
            }

            throw;
        }
    }

    public static bool TryPrepareSelfServiceRequest(
        HttpContext httpContext,
        string? idempotencyKeyHeader,
        string? bodyIdempotencyKey,
        out Principal caller,
        out string rolesText,
        out string? idempotencyKey,
        out IResult? errorResult)
    {
        if (CallerScope.Principal(httpContext) is not { } resolvedCaller || string.IsNullOrWhiteSpace(resolvedCaller.UserId))
        {
            caller = null!;
            rolesText = string.Empty;
            idempotencyKey = null;
            errorResult = PaymentScopeAndMappingHelpers.NoSignedCaller();
            return false;
        }

        caller = resolvedCaller;
        rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);
        idempotencyKey = PaymentScopeAndMappingHelpers.NormalizeIdempotencyKey(idempotencyKeyHeader, bodyIdempotencyKey);
        if (idempotencyKey is { Length: > 255 })
        {
            errorResult = Results.BadRequest(new { detail = "Idempotency-Key must not exceed 255 characters." });
            return false;
        }

        errorResult = null;
        return true;
    }

    public static async Task<(ParticipantWallet? Wallet, IResult? ErrorResult)> ResolveRequiredLockedWalletAsync(
        PaymentsDbContext dbContext,
        IWalletRowLockRepository walletLocks,
        HttpContext httpContext,
        Principal caller,
        string? requestedSubjectId,
        string? requestedWalletId,
        string? requestedStudyId,
        string? requestedSiteId,
        bool createIfMissing,
        DateTimeOffset now,
        CancellationToken ct,
        IResult? missingWalletResult = null)
    {
        var (wallet, error) = await ResolveOwnWalletAsync(
            dbContext,
            walletLocks,
            httpContext,
            caller,
            requestedSubjectId: requestedSubjectId,
            requestedWalletId: requestedWalletId,
            requestedStudyId: requestedStudyId,
            requestedSiteId: requestedSiteId,
            createIfMissing: createIfMissing,
            now: now,
            lockForUpdate: true,
            ct: ct);

        if (error != null)
        {
            return (null, error);
        }

        return wallet == null
            ? (null, missingWalletResult ?? PaymentScopeAndMappingHelpers.OutsideScope())
            : (wallet, null);
    }

    public static async Task<(ParticipantWallet? Wallet, IResult? ErrorResult)> ResolveOwnWalletAsync(
        PaymentsDbContext dbContext,
        IWalletRowLockRepository walletLocks,
        HttpContext httpContext,
        Principal caller,
        string? requestedSubjectId,
        string? requestedWalletId,
        string? requestedStudyId,
        string? requestedSiteId,
        bool createIfMissing,
        DateTimeOffset now,
        bool lockForUpdate = false,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(requestedSubjectId)
            && !string.Equals(requestedSubjectId.Trim(), caller.UserId, StringComparison.Ordinal))
        {
            return (null, PaymentScopeAndMappingHelpers.CrossParticipantAccessDenied());
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(caller))
        {
            return (null, PaymentScopeAndMappingHelpers.OutsideScope());
        }

        if (!string.IsNullOrWhiteSpace(requestedSiteId) && !ClinicalScopeGuard.CanAccessSite(httpContext, requestedSiteId.Trim()))
        {
            return (null, PaymentScopeAndMappingHelpers.OutsideScope());
        }

        if (!string.IsNullOrWhiteSpace(requestedWalletId))
        {
            string trimmedWalletId = requestedWalletId.Trim();
            var byId = lockForUpdate
                ? await walletLocks.LockWalletByIdAsync(trimmedWalletId, ct)
                : await dbContext.ParticipantWallets.FirstOrDefaultAsync(w => w.Id == trimmedWalletId, ct);
            if (byId == null)
            {
                return (null, Results.NotFound(new { detail = $"Participant wallet '{requestedWalletId}' not found." }));
            }
            if (!string.Equals(byId.SubjectId, caller.UserId, StringComparison.Ordinal))
            {
                return (null, PaymentScopeAndMappingHelpers.CrossParticipantAccessDenied());
            }
            if (!PaymentScopeAndMappingHelpers.Reaches(httpContext, byId))
            {
                return (null, PaymentScopeAndMappingHelpers.OutsideScope());
            }
            return (byId, null);
        }

        var loadedWallets = lockForUpdate
            ? await walletLocks.LockWalletsBySubjectAsync(caller.UserId, ct)
            : await dbContext.ParticipantWallets.Where(w => w.SubjectId == caller.UserId).ToListAsync(ct);
        var ownWallets = loadedWallets
            .OrderByDescending(w => w.UpdatedAt)
            .ToList();

        if (!string.IsNullOrWhiteSpace(requestedStudyId))
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, requestedStudyId))
            {
                return (null, PaymentScopeAndMappingHelpers.OutsideScope());
            }
            var studyMatch = ownWallets.FirstOrDefault(w => string.Equals(w.StudyId, requestedStudyId, StringComparison.Ordinal));
            if (studyMatch != null)
            {
                return PaymentScopeAndMappingHelpers.Reaches(httpContext, studyMatch)
                    ? (studyMatch, null)
                    : (null, PaymentScopeAndMappingHelpers.OutsideScope());
            }
        }
        else if (ownWallets.Count > 0)
        {
            var scopedMatch = ownWallets.FirstOrDefault(w => PaymentScopeAndMappingHelpers.Reaches(httpContext, w));
            if (scopedMatch == null)
            {
                return (null, PaymentScopeAndMappingHelpers.OutsideScope());
            }
            return (scopedMatch, null);
        }

        if (!createIfMissing)
        {
            return (null, null);
        }

        string? resolvedStudy = !string.IsNullOrWhiteSpace(requestedStudyId)
            ? requestedStudyId.Trim()
            : caller.AssignedStudies.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "*");
        if (string.IsNullOrWhiteSpace(resolvedStudy) || !ClinicalScopeGuard.CanAccessStudy(httpContext, resolvedStudy))
        {
            return (null, PaymentScopeAndMappingHelpers.OutsideScope());
        }

        string resolvedSite = !string.IsNullOrWhiteSpace(requestedSiteId)
            ? requestedSiteId.Trim()
            : (caller.AssignedSites.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "SITE-DEFAULT");

        if (!PaymentScopeAndMappingHelpers.Reaches(httpContext, resolvedStudy, resolvedSite))
        {
            return (null, PaymentScopeAndMappingHelpers.OutsideScope());
        }

        var provisioned = new ParticipantWallet
        {
            Id = Guid.NewGuid().ToString(),
            SubjectId = caller.UserId,
            StudyId = resolvedStudy,
            SiteId = resolvedSite,
            CurrentBalance = 0m,
            LifetimeStipends = 0m,
            LifetimeReimbursements = 0m,
            LifetimeDisbursements = 0m,
            Currency = "USD",
            Status = "ACTIVE",
            W9Status = "NOT_REQUIRED",
            VirtualCardMask = "**** **** **** 4821",
            VirtualCardExpiry = "12/29",
            AchRoutingMask = "*****0021",
            AchAccountMask = "ACH-***4321",
            AchAccountHolder = $"Participant {caller.UserId}",
            CreatedAt = now,
            CreatedBy = caller.UserId,
            UpdatedAt = now,
            VersionIndex = 1
        };
        dbContext.ParticipantWallets.Add(provisioned);

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = provisioned.Id,
            Action = "PARTICIPANT_WALLET_CREATED",
            UserId = caller.UserId,
            UserRoles = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength),
            Details = JsonSerializer.Serialize(new
            {
                subject_id = caller.UserId,
                study_id = resolvedStudy,
                site_id = resolvedSite
            }),
            CreatedAt = now,
            CreatedBy = caller.UserId,
            ReasonForChange = "Participant study wallet provisioned for self-service workflow",
            VersionIndex = 1
        });

        return (provisioned, null);
    }
}
