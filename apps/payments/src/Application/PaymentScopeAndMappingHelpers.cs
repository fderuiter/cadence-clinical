using System;
using System.Collections.Generic;
using System.Linq;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Shared RBAC scope checks, identifier masking, and response mapping helpers for Payments services.
/// </summary>
public static class PaymentScopeAndMappingHelpers
{
    public const string SeedSystemUser = "svc:payments";

    public const string AutoApprovalSystemUser = "svc:system_auto_approval";

    public const int MaxRolesLength = 255;

    public static IResult NoSignedCaller() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "The request carries no signed caller.");

    public static ProblemHttpResult OutsideScope() =>
        ClinicalScopeGuard.Forbidden("The caller is not assigned to this study or site.");

    public static ProblemHttpResult CrossParticipantAccessDenied() =>
        ClinicalScopeGuard.Forbidden("Participants may only access or mutate their own wallet.");

    public static bool Reaches(HttpContext httpContext, string? studyId, string? siteId) =>
        ClinicalScopeGuard.CanAccessStudy(httpContext, studyId)
        && ClinicalScopeGuard.CanAccessSite(httpContext, siteId);

    public static bool Reaches(HttpContext httpContext, ParticipantWallet? wallet) =>
        wallet is not null && Reaches(httpContext, wallet.StudyId, wallet.SiteId);

    public static bool Reaches(HttpContext httpContext, SelfBillingInvoice? invoice) =>
        invoice is not null && Reaches(httpContext, invoice.StudyId, invoice.SiteId);

    public static WalletResponse MapWalletToResponse(ParticipantWallet wallet)
    {
        string routingMask = MaskRoutingIdentifier(wallet.AchRoutingMask) ?? "*****0021";
        string accountMask = MaskAccountIdentifier(wallet.AchAccountMask) ?? "ACH-***4321";
        string cardMask = MaskCardIdentifier(wallet.VirtualCardMask) ?? "**** **** **** 4821";
        return new WalletResponse(
            Id: wallet.Id,
            SubjectId: wallet.SubjectId,
            StudyId: wallet.StudyId,
            SiteId: wallet.SiteId,
            CurrentBalance: wallet.CurrentBalance,
            LifetimeStipends: wallet.LifetimeStipends,
            LifetimeReimbursements: wallet.LifetimeReimbursements,
            LifetimeDisbursements: wallet.LifetimeDisbursements,
            TaxableYtd: wallet.TaxableYtd,
            Is1099ThresholdExceeded: wallet.Is1099ThresholdExceeded,
            W9Status: wallet.W9Status,
            Currency: wallet.Currency,
            Status: wallet.Status,
            VirtualCardMask: cardMask,
            VirtualCardExpiry: wallet.VirtualCardExpiry ?? "12/29",
            AchRoutingNumber: routingMask,
            AchAccountMask: accountMask,
            AchAccountHolder: wallet.AchAccountHolder ?? $"Participant {wallet.SubjectId}",
            AntiCoercionCap: wallet.AntiCoercionCap,
            AutoDisbursementThreshold: wallet.AutoDisbursementThreshold,
            UpdatedAt: wallet.UpdatedAt,
            CardType: wallet.CardType,
            CardStatus: wallet.CardStatus,
            AchRoutingMask: routingMask
        );
    }

    public static string? MaskRoutingIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }
        return trimmed.Length > 4 ? $"*****{trimmed[^4..]}" : $"*****{trimmed}";
    }

    public static string? MaskAccountIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }
        return trimmed.Length > 4 ? $"ACH-***{trimmed[^4..]}" : $"ACH-***{trimmed}";
    }

    public static string? MaskCardIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal))
        {
            return trimmed;
        }
        return trimmed.Length > 4 ? $"**** **** **** {trimmed[^4..]}" : $"**** **** **** {trimmed}";
    }

    public static string? NormalizeIdempotencyKey(string? headerValue, string? bodyValue)
    {
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.Trim();
        }

        if (!string.IsNullOrWhiteSpace(bodyValue))
        {
            return bodyValue.Trim();
        }

        return null;
    }

    public static SelfBillingInvoiceResponse MapInvoiceToResponse(
        SelfBillingInvoice inv,
        IEnumerable<InvoiceLineItem> lineItems,
        RemittanceAdvice? remittance) => new(
            Id: inv.Id,
            InvoiceNumber: inv.InvoiceNumber,
            StudyId: inv.StudyId,
            SiteId: inv.SiteId,
            ContractId: inv.ContractId,
            BillingCycle: inv.BillingCycle,
            PeriodStart: inv.PeriodStart,
            PeriodEnd: inv.PeriodEnd,
            Currency: inv.Currency,
            TotalBaseAmount: inv.TotalBaseAmount,
            TotalOverheadAmount: inv.TotalOverheadAmount,
            TotalPatientStipend: inv.TotalPatientStipend,
            TotalTravelReimbursement: inv.TotalTravelReimbursement,
            TotalGrossAmount: inv.TotalGrossAmount,
            TotalHoldbackAmount: inv.TotalHoldbackAmount,
            TotalNetAmount: inv.TotalNetAmount,
            PayableCount: inv.PayableCount,
            Status: inv.Status,
            ApprovedBy: inv.ApprovedBy,
            ApprovedAt: inv.ApprovedAt,
            ErpSystem: inv.ErpSystem,
            ErpReferenceId: inv.ErpReferenceId,
            ErpSyncStatus: inv.ErpSyncStatus,
            ErpSyncedAt: inv.ErpSyncedAt,
            ReconciledBy: inv.ReconciledBy,
            ReconciledAt: inv.ReconciledAt,
            ReconciliationReference: inv.ReconciliationReference,
            ReconciliationNotes: inv.ReconciliationNotes,
            CreatedAt: inv.CreatedAt,
            CreatedBy: inv.CreatedBy,
            ReasonForChange: inv.ReasonForChange,
            VersionIndex: inv.VersionIndex,
            LineItems: lineItems.Select(item => new InvoiceLineItemDto(
                Id: item.Id,
                InvoiceId: item.InvoiceId,
                PayableId: item.PayableId,
                SubjectId: item.SubjectId,
                VisitCode: item.VisitCode,
                ProcedureCode: item.ProcedureCode,
                ProcedureName: item.ProcedureName,
                BaseAmount: item.BaseAmount,
                OverheadAmount: item.OverheadAmount,
                PatientStipend: item.PatientStipend,
                TravelReimbursement: item.TravelReimbursement,
                GrossAmount: item.GrossAmount,
                WithholdingRate: item.WithholdingRate,
                WithholdingAmount: item.WithholdingAmount,
                NetAmount: item.NetAmount,
                Currency: item.Currency,
                CreatedAt: item.CreatedAt,
                VersionIndex: item.VersionIndex
            )).ToList(),
            RemittanceAdvice: remittance == null ? null : MapRemittanceAdviceResponse(remittance)
        );

    public static RemittanceAdviceDto MapRemittanceAdviceResponse(RemittanceAdvice remittance) =>
        new(
            Id: remittance.Id,
            RemittanceNumber: remittance.RemittanceNumber,
            InvoiceId: remittance.InvoiceId,
            StudyId: remittance.StudyId,
            SiteId: remittance.SiteId,
            Currency: remittance.Currency,
            GrossAmount: remittance.GrossAmount,
            OverheadAmount: remittance.OverheadAmount,
            HoldbackAmount: remittance.HoldbackAmount,
            NetAmount: remittance.NetAmount,
            PdfSha256Hash: remittance.PdfSha256Hash,
            IsSealed: remittance.IsSealed,
            IsSigned: remittance.IsSigned,
            SignedBy: remittance.SignedBy,
            SignedAt: remittance.SignedAt,
            SignerRole: remittance.SignerRole,
            DispatchStatus: remittance.DispatchStatus,
            CreatedAt: remittance.CreatedAt,
            VersionIndex: remittance.VersionIndex
        );
}
