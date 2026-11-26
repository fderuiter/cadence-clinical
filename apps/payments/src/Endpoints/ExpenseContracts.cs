using System;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Payments.Endpoints;

public record SubmitExpenseClaimRequestDto(
    string SubjectId,
    string StudyId,
    string SiteId,
    string Category,
    string MerchantName,
    decimal GrossAmount,
    bool IsMealAllowance = false,
    decimal? PerDiemCap = null,
    string? ExpenseDate = null,
    string? ReceiptImageName = null,
    object? ReceiptOcrData = null,
    double? OcrConfidence = null,
    string? OcrToken = null,
    string? ReceiptImageBase64 = null
);

public record ReviewExpenseClaimRequestDto(
    string Status,
    string? ReviewNotes = null,
    decimal? AdjustedAmount = null
);

public record OcrExtractRequestDto(
    string? RawText = null,
    string? ImageBase64 = null,
    string? CategoryHint = null,
    string? Filename = null
);

public record ReceiptOcrExtractResponseDto(
    bool OcrReadable,
    string MerchantName,
    string ExpenseDate,
    decimal GrossAmount,
    decimal TaxDeduction,
    decimal NetAmount,
    string Category,
    double ConfidenceScore,
    string RawText,
    string? Warning = null,
    string? OcrToken = null,
    string? ReceiptSha256 = null
);

public record SelfServiceSubmitExpenseRequestDto(
    string Category,
    string MerchantName,
    decimal GrossAmount,
    bool IsMealAllowance = false,
    decimal? PerDiemCap = null,
    string? ExpenseDate = null,
    string? ReceiptImageName = null,
    object? ReceiptOcrData = null,
    double? OcrConfidence = null,
    [property: JsonPropertyName("studyId")] string? StudyCode = null,
    [property: JsonPropertyName("siteId")] string? SiteCode = null,
    string? SubjectId = null,
    string? WalletId = null,
    string? IdempotencyKey = null,
    string? OcrToken = null,
    string? ReceiptImageBase64 = null
);

public record SelfServiceExpenseClaimResponseDto(
    string Id,
    string WalletId,
    string SubjectId,
    [property: JsonPropertyName("studyId")] string StudyCode,
    [property: JsonPropertyName("siteId")] string SiteCode,
    string ExpenseDate,
    string Category,
    string MerchantName,
    decimal GrossAmount,
    decimal TaxDeduction,
    decimal NetAmount,
    string Status,
    string ApprovalDecision,
    bool AutoApproved,
    string? DisbursementReference,
    string? ReviewNotes,
    string? ReceiptImageName,
    decimal WalletCurrentBalance,
    decimal LifetimeStipends,
    decimal LifetimeReimbursements,
    decimal LifetimeDisbursements,
    DateTimeOffset CreatedAt
);
