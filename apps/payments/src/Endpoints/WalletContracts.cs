using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Cadence.Apps.Payments.Domain;

namespace Cadence.Apps.Payments.Endpoints;

public record DisburseStipendRequest(
    string SubjectId,
    string? StudyId,
    string? SiteId,
    int? SequenceNumber,
    string? AssignmentId,
    string? DiaryId,
    decimal? Amount,
    string? IdempotencyKey,
    string? DisbursementType
);

public record DisburseStipendResponse(
    bool Success,
    string Id,
    string WalletId,
    string SubjectId,
    decimal Amount,
    decimal NewBalance,
    string Currency,
    string Status,
    string ReferenceNumber,
    string IdempotencyKey,
    DateTimeOffset DispatchedAt
);

public record WalletResponse(
    string Id,
    string SubjectId,
    string StudyId,
    string SiteId,
    decimal CurrentBalance,
    decimal LifetimeStipends,
    decimal LifetimeReimbursements,
    decimal LifetimeDisbursements,
    decimal TaxableYtd,
    bool Is1099ThresholdExceeded,
    string W9Status,
    string Currency,
    string Status,
    string? VirtualCardMask,
    string? VirtualCardExpiry,
    string? AchRoutingNumber,
    string? AchAccountMask,
    string? AchAccountHolder,
    decimal? AntiCoercionCap,
    decimal? AutoDisbursementThreshold,
    DateTimeOffset UpdatedAt,
    string CardType = "RELOADABLE_DEBIT",
    string CardStatus = "ACTIVE",
    string? AchRoutingMask = null
);

public record IssueCardRequestDto(
    string? CardType = "RELOADABLE_DEBIT"
);

public record DisburseFundsRequestDto(
    string DisbursementType,
    decimal Amount
);

public record WalletBalanceInquiryResponseDto(
    [property: JsonPropertyName("wallet_id")] string WalletId,
    [property: JsonPropertyName("subject_id")] string SubjectId,
    [property: JsonPropertyName("available_balance")] decimal AvailableBalance,
    [property: JsonPropertyName("pending_claims_total")] decimal PendingClaimsTotal,
    [property: JsonPropertyName("lifetime_stipends")] decimal LifetimeStipends,
    [property: JsonPropertyName("lifetime_reimbursements")] decimal LifetimeReimbursements,
    [property: JsonPropertyName("lifetime_disbursements")] decimal LifetimeDisbursements,
    [property: JsonPropertyName("taxable_ytd")] decimal TaxableYtd
);

public record SelfServiceWalletSummaryResponseDto(
    WalletResponse Wallet,
    List<ParticipantTransaction> RecentTransactions,
    List<ParticipantExpenseClaim> ExpenseClaims
);

public record SelfServiceDisburseRequestDto(
    string? DisbursementType = "DIRECT_ACH",
    decimal? Amount = null,
    string? WalletId = null,
    string? SubjectId = null,
    string? IdempotencyKey = null
);

public record SelfServiceDisburseResponseDto(
    string Id,
    string WalletId,
    string SubjectId,
    decimal Amount,
    decimal CurrentBalance,
    decimal LifetimeDisbursements,
    string Currency,
    string DisbursementType,
    string DestinationMask,
    string ReferenceNumber,
    string Status,
    DateTimeOffset CreatedAt
);
