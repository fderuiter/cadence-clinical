using Cadence.Apps.Ctms.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Ctms.Application;

public static class CtmsServiceCollectionExtensions
{
    public static IServiceCollection AddCtmsApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<CtmsUnitOfWork>();
        services.TryAddScoped<CtmsSiteScopeService>();
        services.TryAddScoped<DoaMutationService>();
        services.TryAddScoped<DoaQueryAndExportService>();
        services.TryAddScoped<LetterTemplateApplicationService>();
        services.TryAddScoped<MonitoringVisitApplicationService>();
        services.TryAddScoped<SiteMilestoneApplicationService>();
        services.TryAddScoped<StartupGreenlightApplicationService>();
        return services;
    }
}
