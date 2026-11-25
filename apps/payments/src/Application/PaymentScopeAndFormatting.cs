using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Shared study/site scope guards, participant self-service wallet resolution, and masked identifier projections.
/// </summary>
internal static class PaymentScopeAndFormatting
{
    public const string SeedSystemUser = "svc:payments";
    public const string AutoApprovalSystemUser = "svc:system_auto_approval";
    public const int MaxRolesLength = 255;

    public static IResult NoSignedCaller() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "The request carries no signed caller.");

    public static ProblemHttpResult OutsideScope() =>
        ClinicalScopeGuard.Forbidden("The caller is not assigned to this study or site.");

    public static ProblemHttpResult CrossParticipantAccessDenied() =>
        ClinicalScopeGuard.Forbidden("Participants may only access or mutate their own wallet.");

    public static bool HasValidCallerSiteScope(HttpContext httpContext) =>
        ClinicalScopeGuard.HasValidSiteScopeIfRequired(ClinicalScopeGuard.ResolvePrincipal(httpContext));

    public static bool Reaches(HttpContext httpContext, string? studyCode, string? siteCode) =>
        ClinicalScopeGuard.CanAccessStudy(httpContext, studyCode)
        && ClinicalScopeGuard.CanAccessSite(httpContext, siteCode);

    public static bool Reaches(HttpContext httpContext, ParticipantWallet? wallet) =>
        wallet is not null && Reaches(httpContext, wallet.StudyId, wallet.SiteId);

    public static bool Reaches(HttpContext httpContext, SelfBillingInvoice? invoice) =>
        invoice is not null && Reaches(httpContext, invoice.StudyId, invoice.SiteId);

    public static WalletResponse MapWalletToResponse(ParticipantWallet wallet)
    {
        string routingMask = MaskRoutingIdentifier(wallet.AchRoutingMask) ?? "*****0021";
        string accountMask = MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321";
        string cardMask = MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821";
        return new WalletResponse(
            Id: wallet.Id,
            SubjectId: wallet.SubjectId,
            StudyId: wallet.StudyId,
            SiteId: wallet.SiteId,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            TaxableYtd: wallet.TaxableYtd,
            Is1099ThresholdExceeded: wallet.Is1099ThresholdExceeded,
            W9Status: wallet.W9Status,
            Currency: wallet.Currency,
            Status: wallet.Status,
            VirtualCardMask: cardMask,
            VirtualCardExpiry: wallet.VirtualCardExpiry ?? "12/29",
            AchRoutingNumber: routingMask,
            AchAccountMask: accountMask,
            AchAccountHolder: wallet.AchAccountHolder ?? $"Participant {wallet.SubjectId}",
            AntiCoercionCap: wallet.AntiCoercionCap,
            AutoDisbursementThreshold: wallet.AutoDisbursementThreshold,
            UpdatedAt: wallet.UpdatedAt,
            CardType: wallet.CardType,
            CardStatus: wallet.CardStatus,
            AchRoutingMask: routingMask
        );
    }

    public static string? MaskRoutingIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed.Length > 4 ? $"*****{trimmed[^4..]}" : $"*****{trimmed}";
    }

    public static string? MaskAccountIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed.Length > 4 ? $"ACH-***{trimmed[^4..]}" : $"ACH-***{trimmed}";
    }

    public static string? MaskCardIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed.Length > 4 ? $"**** **** **** {trimmed[^4..]}" : $"**** **** **** {trimmed}";
    }

    public static async Task<(ParticipantWallet? Wallet, IResult? ErrorResult)> ResolveOwnWalletAsync(
        PaymentsUnitOfWork uow,
        HttpContext httpContext,
        Principal caller,
        string? requestedSubjectCode,
        string? requestedWalletCode,
        string? requestedStudyCode,
        string? requestedSiteCode,
        bool createIfMissing,
        DateTimeOffset now,
        bool lockForUpdate = false,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(requestedSubjectCode)
            && !string.Equals(requestedSubjectCode.Trim(), caller.UserId, StringComparison.Ordinal))
        {
            return (null, CrossParticipantAccessDenied());
        }

        if (!ClinicalScopeGuard.HasValidSiteScopeIfRequired(caller))
        {
            return (null, OutsideScope());
        }

        if (!string.IsNullOrWhiteSpace(requestedSiteCode)
            && !ClinicalScopeGuard.CanAccessSite(httpContext, requestedSiteCode.Trim()))
        {
            return (null, OutsideScope());
        }

        if (!string.IsNullOrWhiteSpace(requestedWalletCode))
        {
            string trimmedWalletId = requestedWalletCode.Trim();
            var byId = lockForUpdate
                ? await uow.LockWalletByIdAsync(trimmedWalletId, ct)
                : await uow.Context.ParticipantWallets.FirstOrDefaultAsync(w => w.Id == trimmedWalletId, ct);
            if (byId == null)
            {
                return (null, Results.NotFound(new { detail = $"Participant wallet '{requestedWalletCode}' not found." }));
            }

            if (!string.Equals(byId.SubjectId, caller.UserId, StringComparison.Ordinal))
            {
                return (null, CrossParticipantAccessDenied());
            }

            if (!Reaches(httpContext, byId))
            {
                return (null, OutsideScope());
            }

            return (byId, null);
        }

        var loadedWallets = lockForUpdate
            ? await uow.LockWalletsBySubjectAsync(caller.UserId, ct)
            : await uow.Context.ParticipantWallets.Where(w => w.SubjectId == caller.UserId).ToListAsync(ct);
        var ownWallets = loadedWallets
            .OrderByDescending(w => w.UpdatedAt)
            .ToList();

        if (!string.IsNullOrWhiteSpace(requestedStudyCode))
        {
            if (!ClinicalScopeGuard.CanAccessStudy(httpContext, requestedStudyCode))
            {
                return (null, OutsideScope());
            }

            var studyMatch = ownWallets.FirstOrDefault(w => string.Equals(w.StudyId, requestedStudyCode, StringComparison.Ordinal));
            if (studyMatch != null)
            {
                return Reaches(httpContext, studyMatch) ? (studyMatch, null) : (null, OutsideScope());
            }
        }
        else if (ownWallets.Count > 0)
        {
            var scopedMatch = ownWallets.FirstOrDefault(w => Reaches(httpContext, w));
            if (scopedMatch == null)
            {
                return (null, OutsideScope());
            }

            return (scopedMatch, null);
        }

        if (!createIfMissing)
        {
            return (null, null);
        }

        string? resolvedStudy = !string.IsNullOrWhiteSpace(requestedStudyCode)
            ? requestedStudyCode.Trim()
            : caller.AssignedStudies.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s != "*");
        if (string.IsNullOrWhiteSpace(resolvedStudy) || !ClinicalScopeGuard.CanAccessStudy(httpContext, resolvedStudy))
        {
            return (null, OutsideScope());
        }

        string resolvedSite = !string.IsNullOrWhiteSpace(requestedSiteCode)
            ? requestedSiteCode.Trim()
            : (caller.AssignedSites.Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "SITE-DEFAULT");

        if (!Reaches(httpContext, resolvedStudy, resolvedSite))
        {
            return (null, OutsideScope());
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
        uow.Context.ParticipantWallets.Add(provisioned);

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = provisioned.Id,
            Action = "PARTICIPANT_WALLET_CREATED",
            UserId = caller.UserId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
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

    public static bool TryValidateSelfServiceCaller(
        HttpContext httpContext,
        string? idempotencyKeyHeader,
        string? bodyIdempotencyKey,
        out Principal caller,
        out string rolesText,
        out string? idempotencyKey,
        out IResult? errorResult)
    {
        if (CallerScope.Principal(httpContext) is not { } principal || string.IsNullOrWhiteSpace(principal.UserId))
        {
            caller = default!;
            rolesText = string.Empty;
            idempotencyKey = null;
            errorResult = NoSignedCaller();
            return false;
        }

        caller = principal;
        rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        idempotencyKey = PaymentTokenService.NormalizeIdempotencyKey(idempotencyKeyHeader, bodyIdempotencyKey);
        if (idempotencyKey is { Length: > 255 })
        {
            errorResult = Results.BadRequest(new { detail = "Idempotency-Key must not exceed 255 characters." });
            return false;
        }

        errorResult = null;
        return true;
    }

    public static async Task<(ParticipantWallet? Wallet, IResult? ErrorResult)> LockOrProvisionOwnWalletAsync(
        PaymentsUnitOfWork uow,
        HttpContext httpContext,
        Principal caller,
        string? requestedSubjectCode,
        string? requestedWalletCode,
        string? requestedStudyCode,
        string? requestedSiteCode,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var (wallet, error) = await ResolveOwnWalletAsync(
            uow,
            httpContext,
            caller,
            requestedSubjectCode: requestedSubjectCode,
            requestedWalletCode: requestedWalletCode,
            requestedStudyCode: requestedStudyCode,
            requestedSiteCode: requestedSiteCode,
            createIfMissing: true,
            now: now,
            lockForUpdate: true,
            ct: ct);

        if (error != null)
        {
            return (null, error);
        }

        return wallet != null ? (wallet, null) : (null, OutsideScope());
    }

    internal static IResult BuildSelfServiceExpenseClaimResponse(
        ParticipantExpenseClaim existingClaim,
        ParticipantWallet wallet,
        string subjectCode)
    {
        bool wasAutoApproved = !string.IsNullOrEmpty(existingClaim.DisbursementReference)
            && string.Equals(
                existingClaim.Status,
                ParticipantExpenseClaimStatus.APPROVED.ToString(),
                StringComparison.OrdinalIgnoreCase);
        string existingDecision = wasAutoApproved ? "AUTO_APPROVED" : existingClaim.Status;

        return Results.Ok(new SelfServiceExpenseClaimResponseDto(
            Id: existingClaim.Id,
            WalletId: wallet.Id,
            SubjectId: subjectCode,
            StudyCode: wallet.StudyId,
            SiteCode: wallet.SiteId,
            ExpenseDate: existingClaim.ExpenseDate,
            Category: existingClaim.Category,
            MerchantName: existingClaim.MerchantName,
            GrossAmount: existingClaim.GrossAmount,
            TaxDeduction: existingClaim.TaxDeduction,
            NetAmount: existingClaim.NetAmount,
            Status: existingClaim.Status,
            ApprovalDecision: existingDecision,
            AutoApproved: wasAutoApproved,
            DisbursementReference: existingClaim.DisbursementReference,
            ReviewNotes: existingClaim.ReviewNotes,
            ReceiptImageName: existingClaim.ReceiptImageName,
            WalletCurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            CreatedAt: existingClaim.CreatedAt));
    }
}
