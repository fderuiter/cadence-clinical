using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Security.Authentication;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Evaluates server-side expense reimbursement policies, single-use OCR receipt tokens, duplicate digest checks,
/// and low-risk automated ACH payouts.
/// </summary>
public static class ExpensePolicyEvaluator
{
    public sealed record ServerExpensePolicyEvaluation(
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

    public static async Task<ServerExpensePolicyEvaluation> EvaluateServerExpensePolicyAsync(
        PaymentsDbContext dbContext,
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
        string? gatewaySecret,
        DateTimeOffset now,
        string approvedSummaryNote,
        CancellationToken ct)
    {
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
            : PaymentHmacTokenHelper.TryExtractOcrTokenFromObject(rawReceiptOcrData);
        byte[] receiptBytes = PaymentHmacTokenHelper.DecodeReceiptImageBytes(receiptImageBase64);

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

        PaymentHmacTokenHelper.ReceiptOcrTokenPayload? verifiedToken = null;
        bool hasCandidateToken = !string.IsNullOrWhiteSpace(candidateToken);
        if (hasCandidateToken
            && PaymentHmacTokenHelper.TryVerifyReceiptOcrToken(candidateToken, gatewaySecret, callerUserId, now, out var parsedToken)
            && parsedToken is not null)
        {
            bool tokenAlreadyConsumed = await dbContext.PaymentAuditLogs.AsNoTracking()
                .AnyAsync(l => l.Action == PaymentHmacTokenHelper.OcrTokenConsumedAuditAction && l.EntityId == parsedToken.ScanId, ct)
                || dbContext.ChangeTracker.Entries<PaymentAuditLog>()
                    .Any(e => e.Entity.Action == PaymentHmacTokenHelper.OcrTokenConsumedAuditAction && e.Entity.EntityId == parsedToken.ScanId);
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
            var extracted = ReceiptOcrEngine.ExtractFromBytes(
                receiptBytes,
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
            duplicateReceipt = await dbContext.PaymentAuditLogs.AsNoTracking()
                .AnyAsync(l => l.Action == PaymentHmacTokenHelper.ReceiptDigestRecordedAuditAction && l.EntityId == receiptSha256, ct)
                || dbContext.ChangeTracker.Entries<PaymentAuditLog>()
                    .Any(e => e.Entity.Action == PaymentHmacTokenHelper.ReceiptDigestRecordedAuditAction && e.Entity.EntityId == receiptSha256);
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

    public static void RecordServerReceiptVerificationAuditLogs(
        PaymentsDbContext dbContext,
        ParticipantExpenseClaim claim,
        string callerUserId,
        string callerRolesText,
        ServerExpensePolicyEvaluation policy,
        DateTimeOffset now)
    {
        if (!string.IsNullOrEmpty(policy.ConsumedScanId))
        {
            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = policy.ConsumedScanId,
                Action = PaymentHmacTokenHelper.OcrTokenConsumedAuditAction,
                UserId = callerUserId,
                UserRoles = callerRolesText,
                Details = JsonSerializer.Serialize(new
                {
                    claim_id = claim.Id,
                    scan_id = policy.ConsumedScanId
                }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Single-use receipt OCR token bound to expense claim",
                VersionIndex = 1
            });
        }

        if (!string.IsNullOrEmpty(policy.NewReceiptSha256))
        {
            dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
            {
                Id = Guid.NewGuid().ToString(),
                EntityType = "ParticipantExpenseClaim",
                EntityId = policy.NewReceiptSha256,
                Action = PaymentHmacTokenHelper.ReceiptDigestRecordedAuditAction,
                UserId = callerUserId,
                UserRoles = callerRolesText,
                Details = JsonSerializer.Serialize(new
                {
                    claim_id = claim.Id,
                    receipt_sha256 = policy.NewReceiptSha256
                }),
                CreatedAt = now,
                CreatedBy = callerUserId,
                ReasonForChange = "Receipt image SHA-256 digest bound to expense claim",
                VersionIndex = 1
            });
        }
    }

    public static void ApplyAutoApprovedExpenseDisbursement(
        PaymentsDbContext dbContext,
        ParticipantWallet wallet,
        ParticipantExpenseClaim claim,
        string callerUserId,
        string callerRolesText,
        double ocrConfidence,
        DateTimeOffset now,
        bool auditAsSystemActor)
    {
        string autoApprover = PaymentScopeAndMappingHelpers.AutoApprovalSystemUser;
        decimal net = claim.NetAmount;
        wallet.CurrentBalance += net;
        wallet.LifetimeReimbursements += net;
        wallet.UpdatedAt = now;
        wallet.VersionIndex += 1;

        dbContext.ParticipantTransactions.Add(new ParticipantTransaction
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
            CreatedBy = autoApprover,
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
            CreatedBy = autoApprover,
            VersionIndex = 1
        };
        dbContext.ParticipantDisbursements.Add(disbursement);

        dbContext.ParticipantTransactions.Add(new ParticipantTransaction
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
            CreatedBy = autoApprover,
            VersionIndex = 1
        });

        string auditUserId = auditAsSystemActor ? autoApprover : callerUserId;
        string auditUserRoles = auditAsSystemActor ? string.Empty : callerRolesText;

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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
                approver = autoApprover,
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

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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
