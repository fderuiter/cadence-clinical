using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

public sealed partial class InvoiceBillingApplicationService
{
    public async Task<IResult> ApproveInvoiceAsync(string id, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var inv = await uow.Context.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        try
        {
            inv.Approve(userId, now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_APPROVED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, invoice_number = inv.InvoiceNumber, status = inv.Status }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Self-billing invoice approved for ERP sync and payment processing",
            VersionIndex = inv.VersionIndex
        });
        await uow.CommitAsync();

        var lineItems = await uow.Context.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await uow.Context.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }

    public async Task<IResult> SignRemittanceAdviceAsync(string id, SignRemittanceRequest request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var remittance = await uow.Context.RemittanceAdvices.FirstOrDefaultAsync(r => r.InvoiceId == id || r.Id == id);
        if (remittance == null || !Reaches(httpContext, remittance.StudyId, remittance.SiteId))
        {
            return Results.NotFound(new { detail = $"Remittance advice statement for invoice '{id}' not found." });
        }

        string? approvedBy = await uow.Context.SelfBillingInvoices.AsNoTracking()
            .Where(i => i.Id == remittance.InvoiceId)
            .Select(i => i.ApprovedBy)
            .FirstOrDefaultAsync();
        if (string.Equals(approvedBy, userId, StringComparison.Ordinal))
        {
            return Results.Conflict(new { detail = "The user who approved this invoice cannot also sign its remittance advice." });
        }

        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        DateTimeOffset now = clock.UtcNow;
        remittance.IsSigned = true;
        remittance.SignedBy = userId;
        remittance.SignedAt = now;
        remittance.SignerRole = rolesText;
        remittance.DispatchStatus = "SIGNED_SEALED";
        remittance.UpdatedAt = now;
        remittance.VersionIndex += 1;

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "RemittanceAdvice",
            EntityId = remittance.Id,
            Action = "REMITTANCE_ADVICE_SIGNED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new { remittance_id = remittance.Id, invoice_id = remittance.InvoiceId, signed_by = userId, reason = request.SignatureReason }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = request.SignatureReason ?? "21 CFR Part 11 electronic signature applied to remittance statement",
            VersionIndex = remittance.VersionIndex
        });
        await uow.CommitAsync();

        return Results.Ok(MapRemittanceToResponse(remittance));
    }

    public async Task<IResult> DownloadRemittancePdfAsync(string id, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var remittance = await uow.Context.RemittanceAdvices.AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvoiceId == id || r.Id == id);

        if (remittance == null || !Reaches(httpContext, remittance.StudyId, remittance.SiteId))
        {
            return Results.NotFound(new { detail = $"Remittance statement for invoice '{id}' not found." });
        }

        string text = remittance.PdfContent ?? $"REMITTANCE STATEMENT {remittance.RemittanceNumber}\nGross: {remittance.GrossAmount:F2}\nNet: {remittance.NetAmount:F2}";
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return Results.Bytes(bytes, contentType: "application/pdf", fileDownloadName: $"{remittance.RemittanceNumber}.pdf");
    }

    public async Task<IResult> ReconcileInvoiceAsync(string id, ReconcileInvoiceRequest request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var inv = await uow.Context.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        try
        {
            inv.Reconcile(userId, now, request.PaymentReference, request.Notes, request.ErpSystem);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_SETTLEMENT_RECONCILED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, reference = request.PaymentReference, amount_paid = request.AmountPaid ?? inv.TotalNetAmount }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Invoice settlement reconciled against bank wire statement",
            VersionIndex = inv.VersionIndex
        });
        await uow.CommitAsync();

        var lineItems = await uow.Context.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await uow.Context.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }

    public async Task<IResult> ExportInvoiceToErpAsync(string id, HttpContext httpContext, string? erpSystem = "SAP")
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return NoSignedCaller();
        }

        var inv = await uow.Context.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        string sys = erpSystem ?? "SAP";
        string erpRef = $"ERP-{sys}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();

        try
        {
            inv.ExportToErp(sys, erpRef, now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        uow.Context.ErpSyncLogs.Add(new ErpSyncLog
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceId = inv.Id,
            ErpSystem = sys,
            SyncDirection = "OUTBOUND_EXPORT",
            Status = "EXPORTED",
            ExternalReference = erpRef,
            Reconciled = false,
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice exported to {sys}",
            VersionIndex = 1
        });

        uow.Context.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_ERP_EXPORTED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, MaxRolesLength),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, erp_system = sys, erp_reference_id = erpRef }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice synced to {sys} ERP gateway",
            VersionIndex = inv.VersionIndex
        });
        await uow.CommitAsync();

        var lineItems = await uow.Context.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await uow.Context.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }

    private static RemittanceAdviceDto MapRemittanceToResponse(RemittanceAdvice remittance) => new(
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
        VersionIndex: remittance.VersionIndex);

    private static SelfBillingInvoiceResponse MapInvoiceToResponse(
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
                VersionIndex: item.VersionIndex)).ToList(),
            RemittanceAdvice: remittance == null ? null : MapRemittanceToResponse(remittance));
}
