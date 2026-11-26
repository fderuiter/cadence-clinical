using Cadence.Apps.Payments.Application;
using Cadence.Apps.Payments.Application.Services;
using Cadence.Apps.Payments.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Payments;

/// <summary>
/// Dependency injection registrations for Payments persistence seams and vertical application services.
/// </summary>
public static class PaymentsServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentsApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IPaymentsUnitOfWork, PaymentsUnitOfWork>();
        services.AddScoped<IWalletRowLockRepository, WalletRowLockRepository>();
        services.AddScoped<WalletApplicationService>();
        services.AddScoped<StipendApplicationService>();
        services.AddScoped<DiaryStipendApplicationService>();
        services.AddScoped<TaxApplicationService>();
        services.AddScoped<ExpenseApplicationService>();
        services.AddScoped<SelfServiceExpenseApplicationService>();
        services.AddScoped<InvoiceCompilationService>();
        services.AddScoped<InvoiceLifecycleService>();
        return services;
    }
}
