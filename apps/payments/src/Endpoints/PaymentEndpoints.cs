using System;

using Cadence.Apps.Payments.Application;

using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// Top-level route entry point for the Payments service, delegating across bounded wallet, tax, expense,
/// and self-billing invoice endpoint slices.
/// </summary>
public static class PaymentEndpoints
{
    /// <summary>
    /// The identity recorded for rows the service writes on its own, such as demonstration stipend rules seeded on first read.
    /// </summary>
    public const string SeedSystemUser = PaymentScopeAndFormatting.SeedSystemUser;

    /// <summary>
    /// The identity recorded for the expense auto-approval policy, which approves and pays a low-risk claim with no person reviewing it.
    /// </summary>
    public const string AutoApprovalSystemUser = PaymentScopeAndFormatting.AutoApprovalSystemUser;

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapWalletEndpoints();
        routes.MapTaxEndpoints();
        routes.MapExpenseEndpoints();
        routes.MapInvoiceEndpoints();
        return routes;
    }

    /// <summary>
    /// Issues a single-use HMAC verification token proving server-confirmed ePRO diary synchronization
    /// bound to the caller, study, diary, assignment, sequence number, and expiration window.
    /// </summary>
    public static string IssueDiarySyncVerificationToken(
        string callerUserId,
        string studyCode,
        string? diaryId,
        string? assignmentId,
        int? sequenceNumber,
        DateTimeOffset issuedAt,
        string? gatewaySecret,
        TimeSpan? lifetime = null,
        string? tokenId = null) =>
        PaymentTokenService.IssueDiarySyncVerificationToken(
            callerUserId,
            studyCode,
            diaryId,
            assignmentId,
            sequenceNumber,
            issuedAt,
            gatewaySecret,
            lifetime,
            tokenId);
}
