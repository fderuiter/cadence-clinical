using System;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Application service owning staff and participant self-service receipt OCR scanning, fail-closed caller site-scope
/// checks (ADR-2667), single-pass bounded receipt byte decoding, single-use OCR HMAC token issuance, and scan auditing.
/// </summary>
public sealed class ReceiptOcrApplicationService(
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

        if (!HasValidCallerSiteScope(httpContext))
        {
            return OutsideScope();
        }

        DateTimeOffset now = clock.UtcNow;
        string? gatewaySecret = PaymentTokenService.ResolveConfiguredGatewaySecret(gatewayAuthentication);
        byte[] receiptBytes = PaymentTokenService.DecodeReceiptImageBytes(request.ImageBase64);
        var extracted = ReceiptOcrEngine.ExtractDecodedOrFallback(
            receiptBytes,
            request.ImageBase64,
            request.RawText,
            request.CategoryHint);

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
}
