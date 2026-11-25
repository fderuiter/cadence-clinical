using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Application service owning IRS 1099 threshold monitoring and Form W-9 taxpayer identification submission and dual verification.
/// </summary>
public sealed class TaxWithholdingApplicationService(PaymentsUnitOfWork uow, IClock clock)
{
    public const decimal Annual1099Threshold = 600.00m;
    private const decimal ApproachingThreshold = 500.00m;

    public async Task<IResult> GetTax1099StatusAsync(string walletId, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var wallet = await uow.Context.ParticipantWallets.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == walletId);

        if (wallet == null || !Reaches(httpContext, wallet))
        {
            return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
        }

        decimal percentage = Math.Min(100m, (wallet.TaxableYtd / Annual1099Threshold) * 100m);
        string warningLevel;
        if (wallet.TaxableYtd >= Annual1099Threshold)
        {
            warningLevel = "EXCEEDED";
        }
        else if (wallet.TaxableYtd >= ApproachingThreshold)
        {
            warningLevel = "APPROACHING";
        }
        else
        {
            warningLevel = "NORMAL";
        }

        return Results.Ok(new Tax1099StatusResponseDto(
            WalletId: wallet.Id,
            SubjectId: wallet.SubjectId,
            TaxableYtd: wallet.TaxableYtd,
            ThresholdAmount: Annual1099Threshold,
            ThresholdPercentage: percentage,
            Is1099ThresholdExceeded: wallet.Is1099ThresholdExceeded,
            W9Status: wallet.W9Status,
            WarningLevel: warningLevel));
    }

    public async Task<IResult> SubmitW9FormAsync(string walletId, SubmitW9RequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await uow.ExecuteWalletTransactionAsync(async ct =>
        {
            var wallet = await uow.LockWalletByIdAsync(walletId, ct);

            if (wallet == null || !Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            string? trimmedLegalName = request.FullLegalName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLegalName))
            {
                return Results.UnprocessableEntity(new { detail = "Full legal name is required for Form W-9 submission." });
            }

            string? trimmedLastFour = request.TaxIdLastFour?.Trim();
            if (!IsFourAsciiDigits(trimmedLastFour))
            {
                return Results.UnprocessableEntity(new { detail = "TIN format does not conform to IRS requirements: taxIdLastFour must be four digits." });
            }

            if (!request.CertifyAccurate)
            {
                return Results.UnprocessableEntity(new { detail = "W-9 certification must be agreed and verified as accurate." });
            }

            DateTimeOffset now = clock.UtcNow;
            string priorStatus = wallet.W9Status;
            wallet.TaxIdLastFour = trimmedLastFour;
            wallet.W9Status = "SUBMITTED";
            wallet.W9SubmittedAt = now;
            wallet.W9VerifiedAt = null;
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "W9_FORM_SUBMITTED",
                UserId = userId,
                UserRoles = rolesText,
                PreviousStateJson = JsonSerializer.Serialize(new { w9_status = priorStatus }),
                NewStateJson = JsonSerializer.Serialize(new { w9_status = wallet.W9Status, tax_id_last_four = trimmedLastFour }),
                Details = JsonSerializer.Serialize(new { full_legal_name = trimmedLegalName, tax_id_last_four = trimmedLastFour, address = request.Address }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Form W-9 taxpayer identification certification submitted for the participant",
                VersionIndex = wallet.VersionIndex
            });
            await uow.CommitAsync(ct);

            return Results.Ok(MapWalletToResponse(wallet));
        });
    }

    public async Task<IResult> VerifyW9FormAsync(string walletId, VerifyW9RequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        return await uow.ExecuteWalletTransactionAsync(async ct =>
        {
            var wallet = await uow.LockWalletByIdAsync(walletId, ct);

            if (wallet == null || !Reaches(httpContext, wallet))
            {
                return Results.NotFound(new { detail = $"Participant wallet '{walletId}' not found." });
            }

            if (!string.Equals(wallet.W9Status, "SUBMITTED", StringComparison.OrdinalIgnoreCase)
                || !IsFourAsciiDigits(wallet.TaxIdLastFour?.Trim()))
            {
                return Results.Conflict(new
                {
                    detail = "Form W-9 can only be verified when a submitted Form W-9 with a four-digit taxpayer identification suffix is pending review."
                });
            }

            string? submittedBy = await ResolveLatestW9SubmitterAsync(wallet, ct);
            if (!string.IsNullOrWhiteSpace(submittedBy)
                && string.Equals(submittedBy, userId, StringComparison.Ordinal))
            {
                return Results.Conflict(new { detail = "The user who submitted this Form W-9 cannot also verify it." });
            }

            DateTimeOffset now = clock.UtcNow;
            string priorStatus = wallet.W9Status;
            wallet.W9Status = "VERIFIED";
            wallet.W9VerifiedAt = now;
            wallet.UpdatedAt = now;
            wallet.VersionIndex += 1;

            uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantWallet",
                EntityId = wallet.Id,
                Action = "W9_FORM_VERIFIED",
                UserId = userId,
                UserRoles = rolesText,
                PreviousStateJson = JsonSerializer.Serialize(new { w9_status = priorStatus }),
                NewStateJson = JsonSerializer.Serialize(new { w9_status = wallet.W9Status }),
                Details = JsonSerializer.Serialize(new { notes = request.Notes ?? "TIN matched against verification registry." }),
                CreatedAt = now,
                CreatedBy = userId,
                ReasonForChange = "Site staff validated Form W-9 taxpayer identification",
                VersionIndex = wallet.VersionIndex
            });
            await uow.CommitAsync(ct);

            return Results.Ok(MapWalletToResponse(wallet));
        });
    }

    private static bool IsFourAsciiDigits(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length == 4 && value.All(char.IsAsciiDigit);

    private async Task<string?> ResolveLatestW9SubmitterAsync(
        ParticipantWallet wallet,
        CancellationToken ct)
    {
        var submissionLogs = await uow.Context.PaymentAuditLogs.AsNoTracking()
            .Where(l => l.EntityType == "ParticipantWallet" && l.EntityId == wallet.Id && l.Action == "W9_FORM_SUBMITTED")
            .Select(l => new { l.UserId, l.CreatedBy, l.CreatedAt, l.VersionIndex })
            .ToListAsync(ct);

        var latestSubmission = submissionLogs
            .OrderByDescending(l => l.VersionIndex)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefault();

        if (latestSubmission == null)
        {
            return wallet.CreatedBy;
        }

        return !string.IsNullOrWhiteSpace(latestSubmission.UserId)
            ? latestSubmission.UserId
            : latestSubmission.CreatedBy;
    }
}
