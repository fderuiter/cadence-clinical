using Cadence.Apps.Interop.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Interop.Application.Services;

public static class InteropServiceCollectionExtensions
{
    public static IServiceCollection AddInteropApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<InteropUnitOfWork>();
        services.TryAddScoped<EsourceConfigurationService>();
        services.TryAddScoped<EsourceIngestionAndQueryService>();
        services.TryAddScoped<EsourceReconciliationService>();
        services.TryAddScoped<SceProgramAndLockService>();
        services.TryAddScoped<SceExecutionService>();
        return services;
    }
}
