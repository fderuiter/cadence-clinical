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
using Cadence.Security.Authentication;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Payments.Application.Services;

/// <summary>
/// Queries self-billing invoices and executes single-site and study-wide batch milestone invoice compilation.
/// </summary>
public sealed class InvoiceCompilationService(
    PaymentsDbContext dbContext,
    IPaymentsUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<IResult> ListInvoicesAsync(
        HttpContext httpContext,
        string? studyId = null,
        string? siteId = null,
        string? billingCycle = null,
        string? status = null,
        string? erpSyncStatus = null,
        string? batchExecutionId = null)
    {
        if (CallerScope.VerifiedUserId(httpContext) is null)
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var query = ClinicalScopeGuard.NarrowToScope(dbContext.SelfBillingInvoices.AsNoTracking(), httpContext, i => i.StudyId, i => i.SiteId);

        if (!string.IsNullOrWhiteSpace(studyId))
        {
            query = query.Where(i => i.StudyId == studyId);
        }
        if (!string.IsNullOrWhiteSpace(siteId))
        {
            query = query.Where(i => i.SiteId == siteId);
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
            var batchAuditLogs = await dbContext.PaymentAuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "SelfBillingInvoiceBatch" && a.EntityId == batchExecutionId)
                .Select(a => a.Details)
                .ToListAsync();

            var matchingInvoiceIds = new HashSet<string>();
            foreach (var detail in batchAuditLogs)
            {
                if (!string.IsNullOrEmpty(detail) && detail.Contains("invoice_ids"))
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

            if (matchingInvoiceIds.Count > 0)
            {
                query = query.Where(i => matchingInvoiceIds.Contains(i.Id));
            }
            else
            {
                query = query.Where(i => i.ReasonForChange != null && i.ReasonForChange.Contains(batchExecutionId));
            }
        }

        var invoices = await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
        var invoiceIds = invoices.Select(i => i.Id).ToList();

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking()
            .Where(l => invoiceIds.Contains(l.InvoiceId))
            .ToListAsync();

        var remittanceAdvices = await dbContext.RemittanceAdvices.AsNoTracking()
            .Where(r => invoiceIds.Contains(r.InvoiceId))
            .ToListAsync();

        var lineItemMap = lineItems.GroupBy(l => l.InvoiceId).ToDictionary(g => g.Key, g => g.ToList());
        var remittanceMap = remittanceAdvices.ToDictionary(r => r.InvoiceId, r => r);

        var responses = invoices.Select(inv => PaymentScopeAndMappingHelpers.MapInvoiceToResponse(
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
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        var inv = await dbContext.SelfBillingInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id);

        if (inv == null || !PaymentScopeAndMappingHelpers.Reaches(httpContext, inv))
        {
            return Results.NotFound(new { detail = $"Self-billing invoice with ID '{id}' not found." });
        }

        var lineItems = await dbContext.InvoiceLineItems.AsNoTracking()
            .Where(l => l.InvoiceId == id)
            .ToListAsync();

        var remittance = await dbContext.RemittanceAdvices.AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvoiceId == id);

        return Results.Ok(PaymentScopeAndMappingHelpers.MapInvoiceToResponse(inv, lineItems, remittance));
    }

    public async Task<IResult> CompileBillingCycleAsync(BillingCycleCompilationRequest request, HttpContext httpContext)
    {
        if (CallerScope.Principal(httpContext) is not { } caller || string.IsNullOrWhiteSpace(caller.UserId))
        {
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.StudyId) || string.IsNullOrWhiteSpace(request.SiteId))
        {
            return Results.BadRequest(new { detail = "Both 'study_id' and 'site_id' are required for single site invoice compilation." });
        }

        if (!PaymentScopeAndMappingHelpers.Reaches(httpContext, request.StudyId, request.SiteId))
        {
            return PaymentScopeAndMappingHelpers.OutsideScope();
        }

        VerifiedInvoiceSignature? verifiedSignature = null;
        if (request.AutoApprove)
        {
            if (!PermissionMatrix.HasPermission(caller, "payments_invoice:approve"))
            {
                return ClinicalScopeGuard.Forbidden("Compiling with auto_approve needs payments_invoice:approve.");
            }

            if (!TryResolveVerifiedAutoApproveSignature(httpContext, caller.UserId, out var resolvedSig))
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }

            verifiedSignature = resolvedSig;
        }

        DateTimeOffset now = clock.UtcNow;
        string periodStart = request.PeriodStart ?? $"{now.Year}-{now.Month:D2}-01";
        string periodEnd = request.PeriodEnd ?? $"{now.Year}-{now.Month:D2}-{DateTime.DaysInMonth(now.Year, now.Month):D2}";
        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);

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
                rolesText,
                signature: verifiedSignature);

            await unitOfWork.CommitAsync();

            return Results.Created(
                $"/api/v1/payments/invoices/{invoice.Id}",
                PaymentScopeAndMappingHelpers.MapInvoiceToResponse(invoice, lineItems, remittance));
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
            return PaymentScopeAndMappingHelpers.NoSignedCaller();
        }

        if (string.IsNullOrWhiteSpace(request.StudyId))
        {
            return Results.BadRequest(new { detail = "Study Protocol ID ('study_id') is required for batch compilation." });
        }

        if (!ClinicalScopeGuard.CanAccessStudy(httpContext, request.StudyId)
            || (request.SiteIds is { Count: > 0 } named && named.Any(site => !PaymentScopeAndMappingHelpers.Reaches(httpContext, request.StudyId, site))))
        {
            return PaymentScopeAndMappingHelpers.OutsideScope();
        }

        VerifiedInvoiceSignature? verifiedSignature = null;
        if (request.AutoApprove)
        {
            if (!PermissionMatrix.HasPermission(caller, "payments_invoice:approve"))
            {
                return ClinicalScopeGuard.Forbidden("Compiling with auto_approve needs payments_invoice:approve.");
            }

            if (!TryResolveVerifiedAutoApproveSignature(httpContext, caller.UserId, out var resolvedSig))
            {
                return ElectronicSignatureEndpointFilter.ReauthenticationRequired();
            }

            verifiedSignature = resolvedSig;
        }

        DateTimeOffset now = clock.UtcNow;
        string batchExecutionId = $"BATCH-EXEC-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        string periodStart = request.PeriodStart ?? $"{now.Year}-{now.Month:D2}-01";
        string periodEnd = request.PeriodEnd ?? $"{now.Year}-{now.Month:D2}-{DateTime.DaysInMonth(now.Year, now.Month):D2}";
        string userId = caller.UserId;
        string rolesText = CallerScope.Roles(httpContext, PaymentScopeAndMappingHelpers.MaxRolesLength);

        List<string> targetSiteIds;
        if (request.SiteIds != null && request.SiteIds.Count > 0)
        {
            targetSiteIds = request.SiteIds.Distinct().ToList();
        }
        else
        {
            var contractSites = await dbContext.BudgetContracts.AsNoTracking()
                .Where(c => c.StudyId == request.StudyId && c.IsActive)
                .Select(c => c.SiteId)
                .ToListAsync();

            var payableSites = await dbContext.Payables.AsNoTracking()
                .Where(p => p.StudyId == request.StudyId && p.Status != "INVOICED" && p.Status != "CANCELLED" && (!p.IsSdvGated || (p.SdvVerifiedAt != null && p.SdvVerifiedBy != null)))
                .Select(p => p.SiteId)
                .ToListAsync();

            targetSiteIds = contractSites.Concat(payableSites)
                .Distinct()
                .Where(site => PaymentScopeAndMappingHelpers.Reaches(httpContext, request.StudyId, site))
                .OrderBy(s => s)
                .ToList();
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

        foreach (var siteId in targetSiteIds)
        {
            try
            {
                var (invoice, _, _, auditLog) = await CompileSiteInvoiceCoreAsync(
                    request.StudyId,
                    siteId,
                    request.BillingCycle,
                    periodStart,
                    periodEnd,
                    request.AutoApprove,
                    userId,
                    rolesText,
                    batchExecutionId,
                    verifiedSignature);

                await unitOfWork.CommitAsync();

                successfulSites++;
                totalInvoices++;
                totalPayables += invoice.PayableCount;
                totalGrossSum += invoice.TotalGrossAmount;
                totalHoldbackSum += invoice.TotalHoldbackAmount;
                totalNetSum += invoice.TotalNetAmount;
                generatedInvoiceIds.Add(invoice.Id);

                siteResults.Add(new SiteBatchCompilationResult(
                    SiteId: siteId,
                    Success: true,
                    InvoiceId: invoice.Id,
                    InvoiceNumber: invoice.InvoiceNumber,
                    PayableCount: invoice.PayableCount,
                    TotalGrossAmount: invoice.TotalGrossAmount,
                    TotalHoldbackAmount: invoice.TotalHoldbackAmount,
                    TotalNetAmount: invoice.TotalNetAmount,
                    ErrorMessage: null
                ));

                batchAuditLogs.Add(new PaymentAuditLogDto(
                    Id: auditLog.Id,
                    EntityType: auditLog.EntityType,
                    EntityId: auditLog.EntityId,
                    Action: auditLog.Action,
                    UserId: auditLog.UserId,
                    Details: auditLog.Details,
                    CreatedAt: auditLog.CreatedAt,
                    CreatedBy: auditLog.CreatedBy
                ));
            }
            catch (Exception ex)
            {
                failedSites++;
                siteResults.Add(new SiteBatchCompilationResult(
                    SiteId: siteId,
                    Success: false,
                    InvoiceId: null,
                    InvoiceNumber: null,
                    PayableCount: 0,
                    TotalGrossAmount: 0m,
                    TotalHoldbackAmount: 0m,
                    TotalNetAmount: 0m,
                    ErrorMessage: ex.Message
                ));
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

        dbContext.PaymentAuditLogs.Add(masterAudit);
        await unitOfWork.CommitAsync();

        batchAuditLogs.Insert(0, new PaymentAuditLogDto(
            Id: masterAudit.Id,
            EntityType: masterAudit.EntityType,
            EntityId: masterAudit.EntityId,
            Action: masterAudit.Action,
            UserId: masterAudit.UserId,
            Details: masterAudit.Details,
            CreatedAt: masterAudit.CreatedAt,
            CreatedBy: masterAudit.CreatedBy
        ));

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
            AuditLogs: batchAuditLogs
        );

        return Results.Created($"/api/v1/payments/invoices?batch_execution_id={batchExecutionId}", response);
    }

    private Task<(SelfBillingInvoice Invoice, List<InvoiceLineItem> LineItems, RemittanceAdvice Remittance, PaymentAuditLog AuditLog)> CompileSiteInvoiceCoreAsync(
        string studyId,
        string siteId,
        string billingCycle,
        string periodStart,
        string periodEnd,
        bool autoApprove,
        string userId,
        string rolesText,
        string? batchExecutionId = null,
        VerifiedInvoiceSignature? signature = null) =>
        SiteInvoiceCompiler.CompileSiteAsync(
            dbContext, clock, studyId, siteId, billingCycle, periodStart, periodEnd, autoApprove, userId, rolesText, batchExecutionId, signature);

    private static bool TryResolveVerifiedAutoApproveSignature(
        HttpContext httpContext,
        string callerUserId,
        out VerifiedInvoiceSignature signature)
    {
        var sigToken = httpContext.VerifiedSigToken();
        if (sigToken is null
            || string.IsNullOrWhiteSpace(sigToken.Id)
            || !string.Equals(sigToken.Subject, callerUserId, StringComparison.Ordinal))
        {
            signature = default;
            return false;
        }

        signature = new VerifiedInvoiceSignature(
            sigToken.Subject,
            sigToken.Id,
            InvoiceEndpoints.InvoiceApproveSemanticAction);
        return true;
    }
}
