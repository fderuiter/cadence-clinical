using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

internal sealed record ServerExpensePolicyEvaluation(
    string NormalizedCategory,
    string MerchantName,
    string ResolvedExpenseDate,
    decimal GrossAmount,
    decimal TaxDeduction,
    decimal NetAmount,
    double OcrConfidence,
    bool CanAutoApprove,
    string InitialStatus,
    string ApprovalDecision,
    string ReviewNotes,
    string ServerReceiptOcrJson,
    string? ConsumedScanId,
    string? NewReceiptSha256);

/// <summary>
/// Application service owning receipt OCR scanning, single-use OCR token issuance/consumption,
/// duplicate receipt digest detection, low-risk auto-approval policy evaluation, and automated ACH disbursement.
/// </summary>
public sealed class ExpenseReceiptPolicyService(
    PaymentsUnitOfWork uow,
    IClock clock,
    IOptionsMonitor<GatewayAuthenticationOptions>? gatewayAuthentication = null)
{
    public async Task<IResult> ExtractReceiptOcrAsync(OcrExtractRequestDto request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        DateTimeOffset now = clock.UtcNow;
        string? gatewaySecret = PaymentTokenService.ResolveConfiguredGatewaySecret(gatewayAuthentication);
        byte[] receiptBytes = PaymentTokenService.DecodeReceiptImageBytes(request.ImageBase64);
        var extracted = ReceiptOcrEngine.Extract(
            rawText: receiptBytes.Length > 0 ? null : request.RawText,
            imageBase64: request.ImageBase64,
            categoryHint: request.CategoryHint);

        string? receiptSha256 = receiptBytes.Length > 0 ? HmacSigner.ComputeSha256Hash(receiptBytes) : null;
        string scanId = "OCR_SCAN_" + Guid.NewGuid().ToString("N");
        string? ocrToken = receiptSha256 is not null
            && string.IsNullOrEmpty(request.RawText)
            && extracted.OcrReadable
            && !string.IsNullOrWhiteSpace(gatewaySecret)
            ? PaymentTokenService.IssueReceiptOcrToken(new ReceiptOcrTokenPayload(
                ScanId: scanId,
                CallerUserId: userId,
                ReceiptSha256: receiptSha256,
                OcrReadable: extracted.OcrReadable,
                MerchantName: extracted.MerchantName,
                ExpenseDate: extracted.ExpenseDate,
                GrossAmount: extracted.GrossAmount,
                Category: extracted.Category,
                ConfidenceScore: extracted.ConfidenceScore,
                IssuedAtUnix: now.ToUnixTimeSeconds(),
                ExpiresAtUnix: now.Add(PaymentTokenService.OcrTokenLifetime).ToUnixTimeSeconds()), gatewaySecret)
            : null;

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantExpenseClaim",
            EntityId = scanId,
            Action = "OCR_RECEIPT_EXTRACTED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new
            {
                ocr_readable = extracted.OcrReadable,
                merchant_name = extracted.MerchantName,
                expense_date = extracted.ExpenseDate,
                gross_amount = extracted.GrossAmount,
                tax_deduction = extracted.TaxDeduction,
                net_amount = extracted.NetAmount,
                category = extracted.Category,
                confidence_score = extracted.ConfidenceScore,
                receipt_sha256 = receiptSha256,
                filename = request.Filename
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Receipt OCR scan performed",
            VersionIndex = 1
        });
        await uow.CommitAsync();

        return Results.Ok(new ReceiptOcrExtractResponseDto(
            OcrReadable: extracted.OcrReadable,
            MerchantName: extracted.MerchantName,
            ExpenseDate: extracted.ExpenseDate,
            GrossAmount: extracted.GrossAmount,
            TaxDeduction: extracted.TaxDeduction,
            NetAmount: extracted.NetAmount,
            Category: extracted.Category,
            ConfidenceScore: extracted.ConfidenceScore,
            RawText: extracted.RawText,
            Warning: extracted.Warning,
            OcrToken: ocrToken,
            ReceiptSha256: receiptSha256));
    }

    internal async Task<ServerExpensePolicyEvaluation> EvaluateServerExpensePolicyAsync(
        string callerUserId,
        ParticipantWallet wallet,
        string? rawCategory,
        string? rawMerchantName,
        decimal requestedGrossAmount,
        bool isMealAllowance,
        string? requestedExpenseDate,
        string? explicitOcrToken,
        string? receiptImageBase64,
        object? rawReceiptOcrData,
        DateTimeOffset now,
        string approvedSummaryNote,
        CancellationToken ct)
    {
        string? gatewaySecret = PaymentTokenService.ResolveConfiguredGatewaySecret(gatewayAuthentication);
        string normalizedCategory = string.IsNullOrWhiteSpace(rawCategory)
            ? "TRANSIT"
            : rawCategory.Trim().ToUpperInvariant();
        string trimmedMerchant = (rawMerchantName ?? string.Empty).Trim();

        decimal gross = Math.Round(requestedGrossAmount, 2);
        bool isMeal = string.Equals(normalizedCategory, "MEAL", StringComparison.OrdinalIgnoreCase) || isMealAllowance;
        decimal perDiemCap = ParticipantExpenseClaim.ServerMealPerDiemCap;
        decimal taxDeduction = ParticipantExpenseClaim.ComputeMealTaxDeduction(gross, isMeal);
        decimal net = Math.Round(gross - taxDeduction, 2);

        string? candidateToken = !string.IsNullOrWhiteSpace(explicitOcrToken)
            ? explicitOcrToken.Trim()
            : PaymentTokenService.TryExtractOcrTokenFromObject(rawReceiptOcrData);
        byte[] receiptBytes = PaymentTokenService.DecodeReceiptImageBytes(receiptImageBase64);

        var verificationHoldReasons = new List<string>();
        bool hasVerifiedScan = false;
        bool ocrReadable = false;
        string ocrMerchant = string.Empty;
        string ocrExpenseDate = string.Empty;
        decimal ocrGrossAmount = 0m;
        string ocrCategory = normalizedCategory;
        double rawOcrConfidence = 0.0;
        string? receiptSha256 = null;
        string? consumedScanId = null;
        bool fieldMismatch = false;
        bool duplicateReceipt = false;

        ReceiptOcrTokenPayload? verifiedToken = null;
        bool hasCandidateToken = !string.IsNullOrWhiteSpace(candidateToken);
        if (hasCandidateToken && PaymentTokenService.TryVerifyReceiptOcrToken(candidateToken, gatewaySecret, callerUserId, now, out var parsedToken) && parsedToken is not null)
        {
            bool tokenAlreadyConsumed = await uow.Context.PaymentAuditLogs.AsNoTracking()
                .AnyAsync(l => l.Action == PaymentTokenService.OcrTokenConsumedAuditAction && l.EntityId == parsedToken.ScanId, ct)
                || uow.Context.ChangeTracker.Entries<PaymentAuditLog>()
                    .Any(e => e.Entity.Action == PaymentTokenService.OcrTokenConsumedAuditAction && e.Entity.EntityId == parsedToken.ScanId);
            if (tokenAlreadyConsumed)
            {
                verificationHoldReasons.Add("Receipt OCR scan token was already consumed by a prior expense submission.");
            }
            else
            {
                verifiedToken = parsedToken;
            }
        }
        else if (hasCandidateToken)
        {
            verificationHoldReasons.Add("Receipt OCR scan token could not be verified for this caller.");
        }

        if (receiptBytes.Length > 0)
        {
            string imageSha256 = HmacSigner.ComputeSha256Hash(receiptBytes);
            var extracted = ReceiptOcrEngine.Extract(
                rawText: null,
                imageBase64: receiptImageBase64,
                categoryHint: normalizedCategory);

            if (hasCandidateToken)
            {
                if (verifiedToken is null || !string.Equals(verifiedToken.ReceiptSha256, imageSha256, StringComparison.Ordinal))
                {
                    if (verifiedToken is not null)
                    {
                        verificationHoldReasons.Add("Uploaded receipt bytes do not match the server OCR scan token.");
                    }
                }
                else
                {
                    consumedScanId = verifiedToken.ScanId;
                    hasVerifiedScan = true;
                }
            }
            else
            {
                hasVerifiedScan = true;
            }

            receiptSha256 = imageSha256;
            ocrReadable = extracted.OcrReadable;
            ocrMerchant = extracted.MerchantName;
            ocrExpenseDate = extracted.ExpenseDate;
            ocrGrossAmount = extracted.GrossAmount;
            ocrCategory = extracted.Category;
            rawOcrConfidence = extracted.ConfidenceScore;
        }
        else if (verifiedToken is not null)
        {
            hasVerifiedScan = true;
            consumedScanId = verifiedToken.ScanId;
            receiptSha256 = verifiedToken.ReceiptSha256;
            ocrReadable = verifiedToken.OcrReadable;
            ocrMerchant = verifiedToken.MerchantName;
            ocrExpenseDate = verifiedToken.ExpenseDate;
            ocrGrossAmount = verifiedToken.GrossAmount;
            ocrCategory = verifiedToken.Category;
            rawOcrConfidence = verifiedToken.ConfidenceScore;
        }

        if (!string.IsNullOrEmpty(receiptSha256))
        {
            duplicateReceipt = await uow.Context.PaymentAuditLogs.AsNoTracking()
                .AnyAsync(l => l.Action == PaymentTokenService.ReceiptDigestRecordedAuditAction && l.EntityId == receiptSha256, ct)
                || uow.Context.ChangeTracker.Entries<PaymentAuditLog>()
                    .Any(e => e.Entity.Action == PaymentTokenService.ReceiptDigestRecordedAuditAction && e.Entity.EntityId == receiptSha256);
            if (duplicateReceipt)
            {
                verificationHoldReasons.Add("Duplicate receipt digest detected across expense claims.");
            }
        }

        double effectiveOcrConfidence = 0.0;
        if (hasVerifiedScan && verificationHoldReasons.Count == 0)
        {
            if (!ocrReadable)
            {
                verificationHoldReasons.Add("Receipt scan could not read legible merchant and total fields.");
            }
            else
            {
                bool amountMatches = Math.Round(gross, 2) == Math.Round(ocrGrossAmount, 2);
                bool merchantMatches = !string.IsNullOrWhiteSpace(ocrMerchant)
                    && string.Equals(trimmedMerchant, ocrMerchant.Trim(), StringComparison.OrdinalIgnoreCase);
                bool categoryMatches = string.Equals(normalizedCategory, ocrCategory, StringComparison.OrdinalIgnoreCase);
                bool dateMatches = string.IsNullOrWhiteSpace(ocrExpenseDate)
                    || string.IsNullOrWhiteSpace(requestedExpenseDate)
                    || string.Equals(requestedExpenseDate.Trim(), ocrExpenseDate.Trim(), StringComparison.Ordinal);

                if (amountMatches && merchantMatches && categoryMatches && dateMatches)
                {
                    effectiveOcrConfidence = rawOcrConfidence;
                }
                else
                {
                    fieldMismatch = true;
                    verificationHoldReasons.Add("Submitted claim fields differ from server-extracted receipt scan.");
                }
            }
        }

        bool isAmountLowRisk = gross <= 100.00m;
        bool isHighConfidence = effectiveOcrConfidence >= 0.90;
        bool isMealWithinCap = !isMeal || gross <= perDiemCap;
        bool isW9Compliant = !(wallet.TaxableYtd >= 600.00m && !string.Equals(wallet.W9Status, "VERIFIED", StringComparison.OrdinalIgnoreCase));

        bool canAutoApprove = isAmountLowRisk
            && isHighConfidence
            && isMealWithinCap
            && isW9Compliant
            && verificationHoldReasons.Count == 0;

        string initialStatus = canAutoApprove ? "APPROVED" : "PENDING_REVIEW";
        string approvalDecision = canAutoApprove ? "AUTO_APPROVED" : "PENDING_REVIEW";

        string reviewNotes;
        if (!canAutoApprove)
        {
            var holdReasons = new List<string>(verificationHoldReasons);
            if (!isAmountLowRisk)
            {
                holdReasons.Add($"Gross amount (${gross:F2}) exceeds $100.00 auto-approval threshold.");
            }

            if (!isMealWithinCap)
            {
                holdReasons.Add($"Meal expense (${gross:F2}) exceeds per-diem cap (${perDiemCap:F2}).");
            }

            if (!isW9Compliant)
            {
                holdReasons.Add("IRS $600.00 annual taxable threshold reached without verified Form W-9 certification.");
            }

            if (!isHighConfidence)
            {
                holdReasons.Add("We could not read every detail on the receipt photo clearly, so your study coordinator will check it for you.");
            }

            reviewNotes = string.Join(" ", holdReasons) + " Routed to site coordinator review queue.";
        }
        else
        {
            reviewNotes = approvedSummaryNote;
        }

        string resolvedExpenseDate = now.ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(requestedExpenseDate))
        {
            resolvedExpenseDate = requestedExpenseDate.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(ocrExpenseDate))
        {
            resolvedExpenseDate = ocrExpenseDate;
        }

        string serverOcrJson = JsonSerializer.Serialize(new
        {
            ocr_readable = ocrReadable,
            merchant_name = ocrMerchant,
            expense_date = ocrExpenseDate,
            gross_amount = ocrGrossAmount,
            category = ocrCategory,
            confidence_score = effectiveOcrConfidence,
            raw_confidence_score = rawOcrConfidence,
            is_meal_allowance = isMeal,
            per_diem_cap = perDiemCap,
            receipt_sha256 = receiptSha256,
            scan_id = consumedScanId,
            duplicate_receipt = duplicateReceipt,
            field_mismatch = fieldMismatch
        });

        string? newReceiptSha256 = !string.IsNullOrEmpty(receiptSha256) && !duplicateReceipt
            ? receiptSha256
            : null;

        return new ServerExpensePolicyEvaluation(
            NormalizedCategory: normalizedCategory,
            MerchantName: trimmedMerchant,
            ResolvedExpenseDate: resolvedExpenseDate,
            GrossAmount: gross,
            TaxDeduction: taxDeduction,
            NetAmount: net,
            OcrConfidence: effectiveOcrConfidence,
            CanAutoApprove: canAutoApprove,
            InitialStatus: initialStatus,
            ApprovalDecision: approvalDecision,
            ReviewNotes: reviewNotes,
            ServerReceiptOcrJson: serverOcrJson,
            ConsumedScanId: consumedScanId,
            NewReceiptSha256: newReceiptSha256);
    }

    internal static void RecordServerReceiptVerificationAuditLogs(
        PaymentsUnitOfWork uow,
        ParticipantExpenseClaim claim,
        string callerUserId,
        string callerRolesText,
        ServerExpensePolicyEvaluation policy,
        DateTimeOffset now)
    {
        if (!string.IsNullOrEmpty(policy.ConsumedScanId))
        {
            uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = policy.ConsumedScanId,
                Action = PaymentTokenService.OcrTokenConsumedAuditAction,
                UserId = callerUserId,
                UserRoles = callerRolesText,
                Details = JsonSerializer.Serialize(new { claim_id = claim.Id, scan_id = policy.ConsumedScanId }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Single-use receipt OCR token bound to expense claim",
                VersionIndex = 1
            });
        }

        if (!string.IsNullOrEmpty(policy.NewReceiptSha256))
        {
            uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = policy.NewReceiptSha256,
                Action = PaymentTokenService.ReceiptDigestRecordedAuditAction,
                UserId = callerUserId,
                UserRoles = callerRolesText,
                Details = JsonSerializer.Serialize(new { claim_id = claim.Id, receipt_sha256 = policy.NewReceiptSha256 }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Receipt image SHA-256 digest bound to expense claim",
                VersionIndex = 1
            });
        }
    }

    internal static void ApplyAutoApprovedExpenseDisbursement(
        PaymentsUnitOfWork uow,
        ParticipantWallet wallet,
        ParticipantExpenseClaim claim,
        string callerUserId,
        string callerRolesText,
        double ocrConfidence,
        DateTimeOffset now,
        bool auditAsSystemActor)
    {
        decimal net = claim.NetAmount;
        wallet.CurrentBalance += net;
        wallet.LifetimeReimbursements += net;
        wallet.UpdatedAt = now;
        wallet.VersionIndex += 1;

        uow.Context.ParticipantTransactions.Add(new ParticipantTransaction
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = claim.SubjectId,
            StudyId = wallet.StudyId,
            TransactionType = "REIMBURSEMENT_CREDIT",
            Amount = net,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceId = claim.Id,
            Description = $"Auto-approved reimbursement credit for {claim.Category} expense ({claim.MerchantName})",
            CreatedAt = now,
            CreatedBy = AutoApprovalSystemUser,
            VersionIndex = 1
        });

        string achRef = $"DISB-ACH-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        claim.RecordWalletCredit();
        claim.RecordDisbursement(achRef);

        wallet.CurrentBalance -= net;
        wallet.LifetimeDisbursements += net;

        var disbursement = new ParticipantDisbursement
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = claim.SubjectId,
            Amount = net,
            DisbursementType = "DIRECT_ACH",
            ReferenceNumber = achRef,
            Status = "DISPATCHED",
            CreatedAt = now,
            CreatedBy = AutoApprovalSystemUser,
            VersionIndex = 1
        };
        uow.Context.ParticipantDisbursements.Add(disbursement);

        uow.Context.ParticipantTransactions.Add(new ParticipantTransaction
        {
            Id = Guid.NewGuid().ToString(),
            WalletId = wallet.Id,
            SubjectId = claim.SubjectId,
            StudyId = wallet.StudyId,
            TransactionType = "DISBURSEMENT_DEBIT",
            Amount = -net,
            BalanceAfter = wallet.CurrentBalance,
            ReferenceId = claim.Id,
            Description = $"Automated direct ACH payout dispatch ({achRef})",
            CreatedAt = now,
            CreatedBy = AutoApprovalSystemUser,
            VersionIndex = 1
        });

        string auditUserId = auditAsSystemActor ? AutoApprovalSystemUser : callerUserId;
        string auditUserRoles = auditAsSystemActor ? string.Empty : callerRolesText;

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantExpenseClaim",
            EntityId = claim.Id,
            Action = "EXPENSE_CLAIM_AUTO_APPROVED",
            UserId = auditUserId,
            UserRoles = auditUserRoles,
            Details = JsonSerializer.Serialize(new
            {
                claim_id = claim.Id,
                submitted_by = callerUserId,
                approver = AutoApprovalSystemUser,
                gross_amount = claim.GrossAmount,
                net_amount = net,
                ocr_confidence = ocrConfidence,
                disbursement_reference = achRef
            }),
            CreatedAt = now,
            CreatedBy = auditUserId,
            ReasonForChange = "Automated OCR policy auto-approval and ACH payout dispatch",
            VersionIndex = claim.VersionIndex
        });

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "ParticipantWallet",
            EntityId = wallet.Id,
            Action = "DIGITAL_DISBURSEMENT_DISPATCHED",
            UserId = auditUserId,
            UserRoles = auditUserRoles,
            Details = JsonSerializer.Serialize(new
            {
                submitted_by = callerUserId,
                disbursement_id = disbursement.Id,
                amount = net,
                reference_number = achRef,
                disbursement_type = "DIRECT_ACH"
            }),
            CreatedAt = now,
            CreatedBy = auditUserId,
            ReasonForChange = "Automated direct ACH reimbursement dispatched to linked bank account",
            VersionIndex = wallet.VersionIndex
        });
    }
}
