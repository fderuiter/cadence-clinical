using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence.Apps.Payments.Endpoints;

public record VisitVerificationEventDto(
    string StudyId,
    string SiteId,
    string SubjectId,
    string VisitCode,
    string EventId
);

public record VisitVerificationResponseDto(
    string Status,
    decimal Amount,
    decimal CurrentBalance,
    string WalletId,
    string? TransactionId,
    string EventId,
    string SubjectId,
    string? Reason = null,
    bool RequiresReview = false,
    string? W9Status = null
);

public record ParticipantStipendRuleCreateDto(
    string StudyId,
    string VisitCode,
    string VisitName,
    string? ProcedureCode,
    decimal Amount,
    string? Currency = "USD",
    bool RequiresVerification = true,
    bool Taxable = false,
    decimal? AntiCoercionCap = null,
    decimal? AutoDisbursementThreshold = null,
    bool IsMilestone = false,
    string? MilestoneCode = null
);

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

public sealed class SelfServiceDiaryStipendRequestDto
{
    public string? DiaryId { get; set; }
    public string? AssignmentId { get; set; }
    public int? SequenceNumber { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SyncVerificationToken { get; set; }
    [JsonPropertyName("studyId")]
    public string? StudyCode { get; set; }
    [JsonPropertyName("siteId")]
    public string? SiteCode { get; set; }
    public string? SubjectId { get; set; }
    public string? WalletId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public record SelfServiceDiaryStipendResponseDto(
    bool Success,
    string Status,
    decimal CreditedAmount,
    decimal CurrentBalance,
    decimal LifetimeStipends,
    string WalletId,
    string SubjectId,
    string Currency,
    string? TransactionId,
    string? ReferenceNumber,
    string IdempotencyKey,
    string? Reason = null,
    bool RequiresReview = false,
    string? W9Status = null
);
