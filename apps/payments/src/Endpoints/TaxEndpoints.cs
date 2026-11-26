using System.Threading.Tasks;

using Cadence.Apps.Payments.Application.Services;
using Cadence.Security.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cadence.Apps.Payments.Endpoints;

/// <summary>
/// IRS 1099 threshold monitoring and dual-control Form W-9 taxpayer certification endpoints (3 routes).
/// </summary>
public static class TaxEndpoints
{
    public static IEndpointRouteBuilder MapTaxEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments")
            .WithTags("Payments");

        group.MapGet("/participant/wallets/{walletId}/tax-1099-status", GetTax1099Status)
            .WithName("GetTax1099Status")
            .RequirePermission("payments_wallet:read").RequireCallerStudyScope()
            .WithSummary("Retrieve IRS 1099 tax status and threshold monitoring tracer.");

        group.MapPost("/participant/wallets/{walletId}/submit-w9", SubmitW9Form)
            .WithName("SubmitW9Form")
            .RequirePermission("payments_w9:submit").RequireCallerStudyScope()
            .WithSummary("Submit Form W-9 taxpayer identification certification.");

        group.MapPost("/participant/wallets/{walletId}/verify-w9", VerifyW9Form)
            .WithName("VerifyW9Form")
            .RequirePermission("payments_w9:verify").RequireCallerStudyScope()
            .WithSummary("Verify participant Form W-9 submission.");

        return routes;
    }

    public static Task<IResult> GetTax1099Status(
        string walletId,
        [FromServices] TaxApplicationService service,
        HttpContext httpContext) =>
        service.GetTax1099StatusAsync(walletId, httpContext);

    public static Task<IResult> SubmitW9Form(
        string walletId,
        SubmitW9RequestDto request,
        [FromServices] TaxApplicationService service,
        HttpContext httpContext) =>
        service.SubmitW9FormAsync(walletId, request, httpContext);

    public static Task<IResult> VerifyW9Form(
        string walletId,
        VerifyW9RequestDto request,
        [FromServices] TaxApplicationService service,
        HttpContext httpContext) =>
        service.VerifyW9FormAsync(walletId, request, httpContext);
}
