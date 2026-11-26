using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application.Services;

/// <summary>
/// Manages post-compilation self-billing invoice approval, 21 CFR Part 11 remittance signing, PDF export, ERP sync, and reconciliation.
/// </summary>
public sealed class InvoiceLifecycleService(
    PaymentsDbContext dbContext,
    IPaymentsUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<IResult> ApproveInvoiceAsync(string id, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        string priorStatus = inv.Status;
        string? priorApprovedBy = inv.ApprovedBy;
        DateTimeOffset? priorApprovedAt = inv.ApprovedAt;
        try
        {
            inv.Approve(userId, now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        var sigToken = httpContext.VerifiedSigToken();
        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_APPROVED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength),
            PreviousStateJson = JsonSerializer.Serialize(new { status = priorStatus, approved_by = priorApprovedBy, approved_at = priorApprovedAt }),
            NewStateJson = JsonSerializer.Serialize(new { status = inv.Status, approved_by = inv.ApprovedBy, approved_at = inv.ApprovedAt }),
            Details = JsonSerializer.Serialize(new
            {
                invoice_id = inv.Id,
                invoice_number = inv.InvoiceNumber,
                status = inv.Status,
                signature_subject = sigToken?.Subject,
                signature_token_id = sigToken?.Id,
                signature_semantic_action = "payments.invoice.approve"
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Self-billing invoice approved for ERP sync and payment processing",
            VersionIndex = inv.VersionIndex
        });
        await unitOfWork.CommitAsync();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await dbContext.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(PaymentScopeAndMappingHelpers.MapInvoiceToResponse(inv, lineItems, remittance));
    }

    public async Task<IResult> SignRemittanceAdviceAsync(string id, SignRemittanceRequest request, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var remittance = await dbContext.RemittanceAdvices.FirstOrDefaultAsync(r => r.InvoiceId == id || r.Id == id);
        if (remittance == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, remittance.StudyId, remittance.SiteId))
        {
            return Results.NotFound(new { detail = $"Remittance advice statement for invoice '{id}' not found." });
        }

        string? approvedBy = await dbContext.SelfBillingInvoices.AsNoTracking()
            .Where(i => i.Id == remittance.InvoiceId)
            .Select(i => i.ApprovedBy)
            .FirstOrDefaultAsync();
        if (string.Equals(approvedBy, userId, StringComparison.Ordinal))
        {
            return Results.Conflict(new { detail = "The user who approved this invoice cannot also sign its remittance advice." });
        }

        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);
        DateTimeOffset now = clock.UtcNow;
        remittance.IsSigned = true;
        remittance.SignedBy = userId;
        remittance.SignedAt = now;
        remittance.SignerRole = rolesText;
        remittance.DispatchStatus = "SIGNED_SEALED";
        remittance.UpdatedAt = now;
        remittance.VersionIndex += 1;

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
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
        await unitOfWork.CommitAsync();

        return Results.Ok(PaymentScopeAndMappingHelpers.MapRemittanceAdviceResponse(remittance));
    }

    public async Task<IResult> DownloadRemittancePdfAsync(string id, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var remittance = await dbContext.RemittanceAdvices.AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvoiceId == id || r.Id == id);

        if (remittance == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, remittance.StudyId, remittance.SiteId))
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
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        string priorStatus = inv.Status;
        string? priorReconciliationReference = inv.ReconciliationReference;
        try
        {
            inv.Reconcile(userId, now, request.PaymentReference, request.Notes, request.ErpSystem);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_SETTLEMENT_RECONCILED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength),
            PreviousStateJson = JsonSerializer.Serialize(new { status = priorStatus, reconciliation_reference = priorReconciliationReference }),
            NewStateJson = JsonSerializer.Serialize(new { status = inv.Status, reconciled_by = inv.ReconciledBy, reconciliation_reference = inv.ReconciliationReference }),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, reference = request.PaymentReference, amount_paid = request.AmountPaid ?? inv.TotalNetAmount }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = "Invoice settlement reconciled against bank wire statement",
            VersionIndex = inv.VersionIndex
        });
        await unitOfWork.CommitAsync();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await dbContext.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(PaymentScopeAndMappingHelpers.MapInvoiceToResponse(inv, lineItems, remittance));
    }

    public async Task<IResult> ExportInvoiceToErpAsync(string id, HttpContext httpContext, string? erpSystem = "SAP")
    {
        if (CallerScope.VerifiedUserId(httpContext) is not { } userId)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (inv == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Invoice with ID '{id}' not found." });
        }

        DateTimeOffset now = clock.UtcNow;
        string sys = erpSystem ?? "SAP";
        string erpRef = $"ERP-{sys}-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        string priorErpSyncStatus = inv.ErpSyncStatus;
        string? priorErpReferenceId = inv.ErpReferenceId;

        try
        {
            inv.ExportToErp(sys, erpRef, now);
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }

        dbContext.ErpSyncLogs.Add(new ErpSyncLog
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

        dbContext.PaymentAuditLogs.Add(new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = inv.Id,
            Action = "INVOICE_ERP_EXPORTED",
            UserId = userId,
            UserRoles = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength),
            PreviousStateJson = JsonSerializer.Serialize(new { status = inv.Status, erp_sync_status = priorErpSyncStatus, erp_reference_id = priorErpReferenceId }),
            NewStateJson = JsonSerializer.Serialize(new { status = inv.Status, erp_sync_status = inv.ErpSyncStatus, erp_system = inv.ErpSystem, erp_reference_id = inv.ErpReferenceId }),
            Details = JsonSerializer.Serialize(new { invoice_id = inv.Id, erp_system = sys, erp_reference_id = erpRef }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice synced to {sys} ERP gateway",
            VersionIndex = inv.VersionIndex
        });
        await unitOfWork.CommitAsync();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking().Where(l => l.InvoiceId == id).ToListAsync();
        var remittance = await dbContext.RemittanceAdvices.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(PaymentScopeAndMappingHelpers.MapInvoiceToResponse(inv, lineItems, remittance));
    }
}
