using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application.Services;

/// <summary>
/// Compiles SDV-verified site payables into a self-billing invoice, line items, SHA-256 sealed remittance advice, and audit log.
/// </summary>
internal static class SiteInvoiceCompiler
{
    private const long InvoiceTickModulus = 1_000_000L;

    public static async Task<(SelfBillingInvoice Invoice, List<InvoiceLineItem> LineItems, RemittanceAdvice Remittance, PaymentAuditLog AuditLog)> CompileSiteAsync(
        PaymentsDbContext dbContext,
        IClock clock,
        string studyId,
        string siteId,
        string billingCycle,
        string periodStart,
        string periodEnd,
        bool autoApprove,
        string userId,
        string rolesText,
        string? batchExecutionId = null)
    {
        DateTimeOffset now = clock.UtcNow;

        var contract = await dbContext.BudgetContracts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.StudyId == studyId && c.SiteId == siteId && c.IsActive);

        string contractId = contract?.Id ?? $"CONTRACT-{studyId}-{siteId}";
        string currency = contract?.Currency ?? "USD";

        var pendingPayables = await dbContext.Payables
            .Where(p => p.StudyId == studyId && p.SiteId == siteId && p.Status != "INVOICED" && p.Status != "CANCELLED")
            .ToListAsync();

        var eligiblePayables = pendingPayables
            .Where(p => !p.IsSdvGated || (p.SdvVerifiedAt.HasValue && !string.IsNullOrWhiteSpace(p.SdvVerifiedBy)))
            .ToList();

        if (eligiblePayables.Count == 0)
        {
            throw new InvalidOperationException($"No pending achieved milestone payables found for site '{siteId}' in study '{studyId}'.");
        }

        decimal totalBase = eligiblePayables.Sum(p => p.BaseAmount);
        decimal totalOverhead = eligiblePayables.Sum(p => p.OverheadAmount);
        decimal totalStipend = eligiblePayables.Sum(p => p.PatientStipendAmount);
        decimal totalTravel = eligiblePayables.Sum(p => p.TravelReimbursementAmount);
        decimal totalGross = eligiblePayables.Sum(p => p.GrossAmount);
        decimal totalHoldback = eligiblePayables.Sum(p => p.WithholdingAmount);
        decimal totalNet = totalGross - totalHoldback;

        foreach (var p in eligiblePayables)
        {
            p.MarkInvoiced(now);
        }

        string invoiceNumber = $"INV-{studyId}-{siteId}-{now.Ticks % InvoiceTickModulus:D6}";
        var invoice = new SelfBillingInvoice
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceNumber = invoiceNumber,
            StudyId = studyId,
            SiteId = siteId,
            ContractId = contractId,
            BillingCycle = billingCycle,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Currency = currency,
            TotalBaseAmount = totalBase,
            TotalOverheadAmount = totalOverhead,
            TotalPatientStipend = totalStipend,
            TotalTravelReimbursement = totalTravel,
            TotalGrossAmount = totalGross,
            TotalHoldbackAmount = totalHoldback,
            TotalNetAmount = totalNet,
            PayableCount = eligiblePayables.Count,
            Status = autoApprove ? "APPROVED" : "DRAFT",
            ApprovedBy = autoApprove ? userId : null,
            ApprovedAt = autoApprove ? now : null,
            ErpSyncStatus = "PENDING",
            CreatedAt = now,
            CreatedBy = userId,
            UpdatedAt = now,
            ReasonForChange = batchExecutionId != null ? $"Compiled in study batch run {batchExecutionId}" : "Compiled billing cycle",
            VersionIndex = 1
        };

        var lineItems = new List<InvoiceLineItem>();
        foreach (var p in eligiblePayables)
        {
            lineItems.Add(new InvoiceLineItem
            {
                Id = Guid.NewGuid().ToString(),
                InvoiceId = invoice.Id,
                PayableId = p.Id,
                SubjectId = p.SubjectId,
                VisitCode = p.VisitCode,
                ProcedureCode = p.ProcedureCode,
                ProcedureName = p.ProcedureName,
                BaseAmount = p.BaseAmount,
                OverheadAmount = p.OverheadAmount,
                PatientStipend = p.PatientStipendAmount,
                TravelReimbursement = p.TravelReimbursementAmount,
                GrossAmount = p.GrossAmount,
                WithholdingRate = p.WithholdingRate,
                WithholdingAmount = p.WithholdingAmount,
                NetAmount = p.NetAmount,
                Currency = p.Currency,
                CreatedAt = now,
                CreatedBy = userId,
                UpdatedAt = now,
                ReasonForChange = "Invoice line item generated from milestone payable",
                VersionIndex = 1
            });
        }

        string remittanceNumber = $"REM-{invoiceNumber}";
        string pdfContentRaw = $"SELF-BILLING REMITTANCE STATEMENT\nInvoice #: {invoiceNumber}\nStudy: {studyId}\nSite: {siteId}\nNet Total: {currency} {totalNet:F2}\nDate: {now:yyyy-MM-dd HH:mm:ss} UTC";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(pdfContentRaw));
        string sha256Hash = Convert.ToHexStringLower(hashBytes);

        var remittance = new RemittanceAdvice
        {
            Id = Guid.NewGuid().ToString(),
            RemittanceNumber = remittanceNumber,
            InvoiceId = invoice.Id,
            StudyId = studyId,
            SiteId = siteId,
            Currency = currency,
            GrossAmount = totalGross,
            OverheadAmount = totalOverhead,
            HoldbackAmount = totalHoldback,
            NetAmount = totalNet,
            PdfSha256Hash = sha256Hash,
            PdfContent = pdfContentRaw,
            IsSealed = true,
            IsSigned = false,
            DispatchStatus = "PENDING",
            CreatedAt = now,
            CreatedBy = userId,
            UpdatedAt = now,
            ReasonForChange = "Remittance advice generated for self-billing invoice",
            VersionIndex = 1
        };

        var auditLog = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoice",
            EntityId = invoice.Id,
            Action = batchExecutionId != null ? "BATCH_SITE_INVOICE_COMPILED" : "INVOICE_COMPILED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new
            {
                invoice_id = invoice.Id,
                invoice_number = invoice.InvoiceNumber,
                study_id = studyId,
                site_id = siteId,
                billing_cycle = billingCycle,
                payable_count = eligiblePayables.Count,
                total_gross_amount = totalGross,
                total_holdback_amount = totalHoldback,
                total_net_amount = totalNet,
                batch_execution_id = batchExecutionId
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice {invoiceNumber} generated for site {siteId}",
            VersionIndex = 1
        };

        dbContext.SelfBillingInvoices.Add(invoice);
        dbContext.InvoiceLineItems.AddRange(lineItems);
        dbContext.RemittanceAdvices.Add(remittance);
        dbContext.PaymentAuditLogs.Add(auditLog);

        return (invoice, lineItems, remittance, auditLog);
    }
}
