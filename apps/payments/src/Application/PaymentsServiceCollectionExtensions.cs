using Cadence.Apps.Payments.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Payments.Application;

public static class PaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentsApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<PaymentsUnitOfWork>();
        services.TryAddScoped<InvoiceBillingApplicationService>();
        services.TryAddScoped<TaxWithholdingApplicationService>();
        services.TryAddScoped<ParticipantWalletApplicationService>();
        services.TryAddScoped<ParticipantStipendApplicationService>();
        services.TryAddScoped<ReceiptOcrApplicationService>();
        services.TryAddScoped<ExpenseReceiptPolicyService>();
        services.TryAddScoped<ExpenseClaimApplicationService>();
        return services;
    }
}
