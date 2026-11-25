using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using static Cadence.Apps.Payments.Application.PaymentScopeAndFormatting;

namespace Cadence.Apps.Payments.Application;

/// <summary>
/// Application service owning self-billing invoice compilation, batch milestone invoicing, approval,
/// 21 CFR Part 11 remittance signing, settlement reconciliation, and ERP gateway export.
/// </summary>
public sealed partial class InvoiceBillingApplicationService(PaymentsUnitOfWork uow, IClock clock)
{
    public async Task<IResult> ListInvoicesAsync(
        HttpContext httpContext,
        string? studyCode = null,
        string? siteCode = null,
        string? billingCycle = null,
        string? status = null,
        string? erpSyncStatus = null,
        string? batchExecutionId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToScope(uow.Context.SelfBillingInvoices.AsNoTracking(), httpContext, i => i.StudyId, i => i.SiteId);

        if (!string.IsNullOrWhiteSpace(studyCode))
        {
            query = query.Where(i => i.StudyId == studyCode);
        }

        if (!string.IsNullOrWhiteSpace(siteCode))
        {
            query = query.Where(i => i.SiteId == siteCode);
        }

        if (!string.IsNullOrWhiteSpace(billingCycle))
        {
            query = query.Where(i => i.BillingCycle == billingCycle);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(i => i.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(erpSyncStatus))
        {
            query = query.Where(i => i.ErpSyncStatus == erpSyncStatus);
        }

        if (!string.IsNullOrWhiteSpace(batchExecutionId))
        {
            var batchAuditLogs = await uow.Context.PaymentAuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "SelfBillingInvoiceBatch" && a.EntityId == batchExecutionId)
                .Select(a => a.Details)
                .ToListAsync();

            var matchingInvoiceIds = new HashSet<string>();
            foreach (var detail in batchAuditLogs)
            {
                if (!string.IsNullOrEmpty(detail) && detail.Contains("invoice_ids", StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(detail);
                        if (doc.RootElement.TryGetProperty("invoice_ids", out var idsElement) && idsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var idEl in idsElement.EnumerateArray())
                            {
                                if (idEl.GetString() is string idStr)
                                {
                                    matchingInvoiceIds.Add(idStr);
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _ = ex.Message;
                    }
                }
            }

            query = matchingInvoiceIds.Count > 0
                ? query.Where(i => matchingInvoiceIds.Contains(i.Id))
                : query.Where(i => i.ReasonForChange != null && i.ReasonForChange.Contains(batchExecutionId));
        }

        var invoices = await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
        var invoiceIds = invoices.Select(i => i.Id).ToList();

        var lineItems = await uow.Context.InvoiceLineItems.AsNoTracking()
            .Where(l => invoiceIds.Contains(l.InvoiceId))
            .ToListAsync();

        var remittanceAdvices = await uow.Context.RemittanceAdvices.AsNoTracking()
            .Where(r => invoiceIds.Contains(r.InvoiceId))
            .ToListAsync();

        var lineItemMap = lineItems.GroupBy(l => l.InvoiceId).ToDictionary(g => g.Key, g => g.ToList());
        var remittanceMap = remittanceAdvices.ToDictionary(r => r.InvoiceId, r => r);

        var responses = invoices.Select(inv => MapInvoiceToResponse(
            inv,
            lineItemMap.GetValueOrDefault(inv.Id) ?? [],
            remittanceMap.GetValueOrDefault(inv.Id)
        )).ToList();

        return Results.Ok(responses);
    }

    public async Task<IResult> GetInvoiceByIdAsync(string id, HttpContext httpContext)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return NoSignedCaller();
        }

        var inv = await uow.Context.SelfBillingInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null || !Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Self-billing invoice with ID '{id}' not found." });
        }

        var lineItems = await uow.Context.InvoiceLineItems.AsNoTracking()
            .Where(l => l.InvoiceId == id)
            .ToListAsync();

        var remittance = await uow.Context.RemittanceAdvices.AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(MapInvoiceToResponse(inv, lineItems, remittance));
    }

    public async Task<IResult> CompileBillingCycleAsync(BillingCycleCompilationRequest request, HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SiteId))
        {
            return Results.BadRequest(new { detail = "Both 'study_id' and 'site_id' are required for single site invoice compilation." });
        }

        if (!Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return OutsideScope();
        }

        if (request.AutoApprove && !PermissionMatrix.HasPermission(caller, "payments_invoice:approve"))
        {
            return ClinicalScopeGuard.Forbidden("Compiling with auto_approve needs payments_invoice:approve.");
        }

        DateTimeOffset now = clock.UtcNow;
        string periodStart = request.PeriodStart ?? $"{now.Year}-{now.Month:D2}-01";
        string periodEnd = request.PeriodEnd ?? $"{now.Year}-{now.Month:D2}-{DateTime.DaysInMonth(now.Year, now.Month):D2}";
        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);
        try
        {
            var (invoice, lineItems, remittance, _) = await CompileSiteInvoiceCoreAsync(
                request.StudyId,
                request.SiteId,
                request.BillingCycle,
                periodStart,
                periodEnd,
                request.AutoApprove,
                caller.UserId,
                rolesText);

            await uow.CommitAsync();

            return Results.Created($"/api/v1/payments/invoices/{invoice.Id}", MapInvoiceToResponse(invoice, lineItems, remittance));
        }
        catch (PaymentDomainException ex)
        {
            return Results.Conflict(new { detail = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { detail = ex.Message });
        }
    }

    public async Task<IResult> BatchCompileBillingCyclesAsync(BatchBillingCycleCompilationRequest request, HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return Results.BadRequest(new { detail = "Study Protocol ID ('study_id') is required for batch compilation." });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId)
            || (request.SiteIds is { Count: > 0 } named && named.Any(site => !Reaches(httpContext, request.StudyId, site))))
        {
            return OutsideScope();
        }

        if (request.AutoApprove && !PermissionMatrix.HasPermission(caller, "payments_invoice:approve"))
        {
            return ClinicalScopeGuard.Forbidden("Compiling with auto_approve needs payments_invoice:approve.");
        }

        DateTimeOffset now = clock.UtcNow;
        string batchExecutionId = $"BATCH-EXEC-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        string periodStart = request.PeriodStart ?? $"{now.Year}-{now.Month:D2}-01";
        string periodEnd = request.PeriodEnd ?? $"{now.Year}-{now.Month:D2}-{DateTime.DaysInMonth(now.Year, now.Month):D2}";
        string userId = caller.UserId;
        string rolesText = CallerScope.Roles(httpContext, MaxRolesLength);

        List<string> targetSiteIds;
        if (request.SiteIds is { Count: > 0 })
        {
            targetSiteIds = request.SiteIds.Distinct().ToList();
        }
        else
        {
            var contractSites = await uow.Context.BudgetContracts.AsNoTracking()
                .Where(c => c.StudyId == request.StudyId && c.IsActive)
                .Select(c => c.SiteId)
                .ToListAsync();

            var payableSites = await uow.Context.Payables.AsNoTracking()
                .Where(p => p.StudyId == request.StudyId && p.Status != "INVOICED" && p.Status != "CANCELLED" && (!p.IsSdvGated || (p.SdvVerifiedAt != null && p.SdvVerifiedBy != null)))
                .Select(p => p.SiteId)
                .ToListAsync();

            targetSiteIds = contractSites.Concat(payableSites).Distinct().Where(site => Reaches(httpContext, request.StudyId, site)).OrderBy(s => s).ToList();
        }

        var siteResults = new List<SiteBatchCompilationResult>();
        var batchAuditLogs = new List<PaymentAuditLogDto>();
        var generatedInvoiceIds = new List<string>();

        int successfulSites = 0;
        int failedSites = 0;
        int totalInvoices = 0;
        int totalPayables = 0;
        decimal totalGrossSum = 0m;
        decimal totalHoldbackSum = 0m;
        decimal totalNetSum = 0m;

        foreach (var siteCode in targetSiteIds)
        {
            try
            {
                var (invoice, _, _, auditLog) = await CompileSiteInvoiceCoreAsync(
                    request.StudyId,
                    siteCode,
                    request.BillingCycle,
                    periodStart,
                    periodEnd,
                    request.AutoApprove,
                    userId,
                    rolesText,
                    batchExecutionId);

                await uow.CommitAsync();

                successfulSites++;
                totalInvoices++;
                totalPayables += invoice.PayableCount;
                totalGrossSum += invoice.TotalGrossAmount;
                totalHoldbackSum += invoice.TotalHoldbackAmount;
                totalNetSum += invoice.TotalNetAmount;
                generatedInvoiceIds.Add(invoice.Id);

                siteResults.Add(new SiteBatchCompilationResult(
                    SiteId: siteCode,
                    Success: true,
                    InvoiceId: invoice.Id,
                    InvoiceNumber: invoice.InvoiceNumber,
                    PayableCount: invoice.PayableCount,
                    TotalGrossAmount: invoice.TotalGrossAmount,
                    TotalHoldbackAmount: invoice.TotalHoldbackAmount,
                    TotalNetAmount: invoice.TotalNetAmount,
                    ErrorMessage: null));

                batchAuditLogs.Add(new PaymentAuditLogDto(
                    Id: auditLog.Id,
                    EntityType: auditLog.EntityType,
                    EntityId: auditLog.EntityId,
                    Action: auditLog.Action,
                    UserId: auditLog.UserId,
                    Details: auditLog.Details,
                    CreatedAt: auditLog.CreatedAt,
                    CreatedBy: auditLog.CreatedBy));
            }
            catch (Exception ex)
            {
                failedSites++;
                siteResults.Add(new SiteBatchCompilationResult(
                    SiteId: siteCode,
                    Success: false,
                    InvoiceId: null,
                    InvoiceNumber: null,
                    PayableCount: 0,
                    TotalGrossAmount: 0m,
                    TotalHoldbackAmount: 0m,
                    TotalNetAmount: 0m,
                    ErrorMessage: ex.Message));
            }
        }

        var masterAudit = new PaymentAuditLog
        {
            Id = Guid.NewGuid().ToString(),
            EntityType = "SelfBillingInvoiceBatch",
            EntityId = batchExecutionId,
            Action = "BATCH_MILESTONE_INVOICE_COMPILED",
            UserId = userId,
            UserRoles = rolesText,
            Details = JsonSerializer.Serialize(new
            {
                batch_execution_id = batchExecutionId,
                study_id = request.StudyId,
                billing_cycle = request.BillingCycle,
                period_start = periodStart,
                period_end = periodEnd,
                total_sites_processed = targetSiteIds.Count,
                successful_sites_count = successfulSites,
                failed_sites_count = failedSites,
                total_invoices_generated = totalInvoices,
                total_payables_compiled = totalPayables,
                total_gross_amount = totalGrossSum,
                total_holdback_amount = totalHoldbackSum,
                total_net_amount = totalNetSum,
                invoice_ids = generatedInvoiceIds
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Executed multi-site batch milestone compilation for study {request.StudyId}",
            VersionIndex = 1
        };

        uow.Context.PaymentAuditLogs.Add(masterAudit);
        await uow.CommitAsync();

        batchAuditLogs.Insert(0, new PaymentAuditLogDto(
            Id: masterAudit.Id,
            EntityType: masterAudit.EntityType,
            EntityId: masterAudit.EntityId,
            Action: masterAudit.Action,
            UserId: masterAudit.UserId,
            Details: masterAudit.Details,
            CreatedAt: masterAudit.CreatedAt,
            CreatedBy: masterAudit.CreatedBy));

        var response = new BatchBillingCompilationResponse(
            BatchExecutionId: batchExecutionId,
            StudyId: request.StudyId,
            BillingCycle: request.BillingCycle,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            TotalSitesProcessed: targetSiteIds.Count,
            SuccessfulSitesCount: successfulSites,
            FailedSitesCount: failedSites,
            TotalInvoicesGenerated: totalInvoices,
            TotalPayablesCompiled: totalPayables,
            TotalGrossAmount: totalGrossSum,
            TotalHoldbackAmount: totalHoldbackSum,
            TotalNetAmount: totalNetSum,
            SiteResults: siteResults,
            AuditLogs: batchAuditLogs);

        return Results.Created($"/api/v1/payments/invoices?batch_execution_id={batchExecutionId}", response);
    }

    private async Task<(SelfBillingInvoice Invoice, List<InvoiceLineItem> LineItems, RemittanceAdvice Remittance, PaymentAuditLog AuditLog)> CompileSiteInvoiceCoreAsync(
        string studyCode,
        string siteCode,
        string billingCycle,
        string periodStart,
        string periodEnd,
        bool autoApprove,
        string userId,
        string rolesText,
        string? batchExecutionId = null)
    {
        DateTimeOffset now = clock.UtcNow;

        var contract = await uow.Context.BudgetContracts.AsNoTracking()
            .FirstOrDefaultAsync(c => c.StudyId == studyCode && c.SiteId == siteCode && c.IsActive);

        string contractId = contract?.Id ?? $"CONTRACT-{studyCode}-{siteCode}";
        string currency = contract?.Currency ?? "USD";

        var pendingPayables = await uow.Context.Payables
            .Where(p => p.StudyId == studyCode && p.SiteId == siteCode && p.Status != "INVOICED" && p.Status != "CANCELLED")
            .ToListAsync();

        var eligiblePayables = pendingPayables
            .Where(p => !p.IsSdvGated || (p.SdvVerifiedAt.HasValue && !string.IsNullOrWhiteSpace(p.SdvVerifiedBy)))
            .ToList();

        if (eligiblePayables.Count == 0)
        {
            throw new InvalidOperationException($"No pending achieved milestone payables found for site '{siteCode}' in study '{studyCode}'.");
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

        string invoiceNumber = $"INV-{studyCode}-{siteCode}-{now.Ticks % 1_000_000:D6}";
        var invoice = new SelfBillingInvoice
        {
            Id = Guid.NewGuid().ToString(),
            InvoiceNumber = invoiceNumber,
            StudyId = studyCode,
            SiteId = siteCode,
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

        var lineItems = eligiblePayables.Select(p => new InvoiceLineItem
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
        }).ToList();

        string remittanceNumber = $"REM-{invoiceNumber}";
        string pdfContentRaw = $"SELF-BILLING REMITTANCE STATEMENT\nInvoice #: {invoiceNumber}\nStudy: {studyCode}\nSite: {siteCode}\nNet Total: {currency} {totalNet:F2}\nDate: {now:yyyy-MM-dd HH:mm:ss} UTC";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(pdfContentRaw));
        string sha256Hash = Convert.ToHexStringLower(hashBytes);

        var remittance = new RemittanceAdvice
        {
            Id = Guid.NewGuid().ToString(),
            RemittanceNumber = remittanceNumber,
            InvoiceId = invoice.Id,
            StudyId = studyCode,
            SiteId = siteCode,
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
                study_id = studyCode,
                site_id = siteCode,
                billing_cycle = billingCycle,
                payable_count = eligiblePayables.Count,
                total_gross_amount = totalGross,
                total_holdback_amount = totalHoldback,
                total_net_amount = totalNet,
                batch_execution_id = batchExecutionId
            }),
            CreatedAt = now,
            CreatedBy = userId,
            ReasonForChange = $"Self-billing invoice {invoiceNumber} generated for site {siteCode}",
            VersionIndex = 1
        };

        uow.Context.SelfBillingInvoices.Add(invoice);
        uow.Context.InvoiceLineItems.AddRange(lineItems);
        uow.Context.RemittanceAdvices.Add(remittance);
        uow.Context.PaymentAuditLogs.Add(auditLog);

        return (invoice, lineItems, remittance, auditLog);
    }
}
