using System.Threading.Tasks;

using Cadence.Apps.Payments.Application;
using Cadence.Security.Authentication;
using Cadence.Security.DependencyInjection;
using Cadence.Security.Rbac;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// Self-billing invoice compilation, batch milestone invoicing, approval, 21 CFR Part 11 remittance signing,
/// reconciliation, and ERP export route slice (9 routes).
/// </summary>
public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapGet("/invoices", ListInvoices)
            .WithName("ListInvoices")
            .RequirePermission("payments_invoice:read").RequireCallerStudyScope()
            .WithSummary("Lists compiled self-billing invoices matching filter criteria.");

        group.MapGet("/invoices/{id}", GetInvoiceById)
            .WithName("GetInvoiceById")
            .RequirePermission("payments_invoice:read").RequireCallerStudyScope()
            .WithSummary("Retrieve single self-billing invoice details with line items.");

        group.MapPost("/invoices/compile", CompileBillingCycle)
            .WithName("CompileBillingCycle")
            .RequirePermission("payments_invoice:compile").RequireCallerStudyScope()
            .WithSummary("Automates single-site billing cycle compilation into a self-billing invoice.");

        group.MapPost("/invoices/batch-compile", BatchCompileBillingCycles)
            .WithName("BatchCompileBillingCycles")
            .RequirePermission("payments_invoice:compile").RequireCallerStudyScope()
            .WithSummary("Executes study-wide batch milestone invoice compilation across all active trial sites.");

        group.MapPost("/invoices/{id}/approve", ApproveInvoice)
            .WithName("ApproveInvoice")
            .RequirePermission("payments_invoice:approve").RequireCallerStudyScope()
            .WithSummary("Approve draft self-billing invoice for ERP export and settlement.")
            .RequireElectronicSignature("payments.invoice.approve", http => http.Request.Path.Value ?? string.Empty);

        group.MapPost("/invoices/{id}/remittance-advice/sign", SignRemittanceAdvice)
            .WithName("SignRemittanceAdvice")
            .RequirePermission("payments_invoice:sign_remittance").RequireCallerStudyScope()
            .WithSummary("Applies 21 CFR Part 11 electronic signature to cryptographic remittance advice.")
            .RequireElectronicSignature("payments.remittance.sign", http => http.Request.Path.Value ?? string.Empty);

        group.MapGet("/invoices/{id}/remittance-advice/pdf", DownloadRemittancePdf)
            .WithName("DownloadRemittancePdf")
            .RequirePermission("payments_invoice:read").RequireCallerStudyScope()
            .WithSummary("Downloads cryptographic PDF remittance statement.");

        group.MapPost("/invoices/{id}/reconcile", ReconcileInvoice)
            .WithName("ReconcileInvoice")
            .RequirePermission("payments_invoice:reconcile").RequireCallerStudyScope()
            .WithSummary("Reconciles invoice settlement against bank wire or ERP clearing statement.");

        group.MapPost("/invoices/{id}/erp-export", ExportInvoiceToErp)
            .WithName("ExportInvoiceToErp")
            .RequirePermission("payments_invoice:export_erp").RequireCallerStudyScope()
            .WithSummary("Exports self-billing invoice to ERP gateway (e.g. SAP).");

        return routes;
    }

    internal static Task<IResult> ListInvoices(
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext,
        string? study_id = null,
        string? site_id = null,
        string? billing_cycle = null,
        string? status = null,
        string? erp_sync_status = null,
        string? batch_execution_id = null) =>
        billingService.ListInvoicesAsync(
            httpContext,
            studyCode: study_id,
            siteCode: site_id,
            billingCycle: billing_cycle,
            status: status,
            erpSyncStatus: erp_sync_status,
            batchExecutionId: batch_execution_id);

    internal static Task<IResult> GetInvoiceById(
        string id,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext) =>
        billingService.GetInvoiceByIdAsync(id, httpContext);

    internal static Task<IResult> CompileBillingCycle(
        BillingCycleCompilationRequest request,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext) =>
        billingService.CompileBillingCycleAsync(request, httpContext);

    internal static Task<IResult> BatchCompileBillingCycles(
        BatchBillingCycleCompilationRequest request,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext) =>
        billingService.BatchCompileBillingCyclesAsync(request, httpContext);

    internal static Task<IResult> ApproveInvoice(
        string id,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext) =>
        billingService.ApproveInvoiceAsync(id, httpContext);

    internal static Task<IResult> SignRemittanceAdvice(
        string id,
        SignRemittanceRequest request,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext) =>
        billingService.SignRemittanceAdviceAsync(id, request, httpContext);

    internal static Task<IResult> DownloadRemittancePdf(
        string id,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext) =>
        billingService.DownloadRemittancePdfAsync(id, httpContext);

    internal static Task<IResult> ReconcileInvoice(
        string id,
        ReconcileInvoiceRequest request,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext) =>
        billingService.ReconcileInvoiceAsync(id, request, httpContext);

    internal static Task<IResult> ExportInvoiceToErp(
        string id,
        [FromServices] InvoiceBillingApplicationService billingService,
        HttpContext httpContext,
        string? erp_system = "SAP") =>
        billingService.ExportInvoiceToErpAsync(id, httpContext, erp_system);
}
