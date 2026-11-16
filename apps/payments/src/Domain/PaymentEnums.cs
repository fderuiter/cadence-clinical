using System;
using System.Collections.Generic;

namespace Cadence.Apps.Payments.Domain;

/// <summary>
/// Declared domain lifecycle statuses for investigator milestone payables.
/// </summary>
public enum PayableStatus
{
    TRIGGERED,
    VERIFIED,
    INVOICED,
    CANCELLED
}

/// <summary>
/// Declared domain lifecycle statuses for self-billing invoices.
/// </summary>
public enum SelfBillingInvoiceStatus
{
    DRAFT,
    APPROVED,
    RECONCILED,
    CANCELLED
}

/// <summary>
/// Declared domain lifecycle statuses for participant expense reimbursement claims.
/// </summary>
public enum ParticipantExpenseClaimStatus
{
    PENDING_REVIEW,
    APPROVED,
    REJECTED
}

/// <summary>
/// Declared domain lifecycle statuses for participant disbursements.
/// </summary>
public enum ParticipantDisbursementStatus
{
    DISPATCHED,
    SETTLED,
    FAILED,
    CANCELLED
}

/// <summary>
/// Extension methods and helpers for domain status validation.
/// </summary>
public static class PaymentStatusExtensions
{
    public static bool IsValidPayableStatus(string status) =>
        Enum.TryParse<PayableStatus>(status, ignoreCase: true, out _);

    public static bool IsValidInvoiceStatus(string status) =>
        Enum.TryParse<SelfBillingInvoiceStatus>(status, ignoreCase: true, out _);

    public static bool IsValidExpenseClaimStatus(string status) =>
        Enum.TryParse<ParticipantExpenseClaimStatus>(status, ignoreCase: true, out _);

    public static bool IsValidDisbursementStatus(string status) =>
        Enum.TryParse<ParticipantDisbursementStatus>(status, ignoreCase: true, out _);
}
