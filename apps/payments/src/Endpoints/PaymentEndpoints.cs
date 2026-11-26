using System;

using Cadence.Apps.Payments.Application;

using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// Top-level route delegator for `/api/v1/payments`, composing bounded invoice, wallet, W-9 tax, and expense endpoint slices.
/// </summary>
public static class PaymentEndpoints
{
    public const string SeedSystemUser = PaymentScopeAndMappingHelpers.SeedSystemUser;

    public const string AutoApprovalSystemUser = PaymentScopeAndMappingHelpers.AutoApprovalSystemUser;

    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapWalletEndpoints();
        routes.MapTaxEndpoints();
        routes.MapExpenseEndpoints();
        routes.MapInvoiceEndpoints();
        return routes;
    }

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
        PaymentHmacTokenHelper.IssueDiarySyncVerificationToken(
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
