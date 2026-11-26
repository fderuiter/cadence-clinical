using Cadence.Apps.Ctms.Application;
using Cadence.Apps.Ctms.Application.Services;
using Cadence.Apps.Ctms.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Apps.Ctms;

public static class CtmsServiceCollectionExtensions
{
    public static IServiceCollection AddCtmsApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICtmsUnitOfWork, CtmsUnitOfWork>();
        services.AddScoped<ICtmsSiteScopeService, CtmsSiteScopeService>();
        services.AddScoped<IDoaMutationService, DoaMutationService>();
        services.AddScoped<IDoaQueryAndExportService, DoaQueryAndExportService>();
        services.AddScoped<ILetterTemplateApplicationService, LetterTemplateApplicationService>();
        services.AddScoped<IMonitoringVisitApplicationService, MonitoringVisitApplicationService>();
        services.AddScoped<ISiteMilestoneApplicationService, SiteMilestoneApplicationService>();
        services.AddScoped<IStartupGreenlightApplicationService, StartupGreenlightApplicationService>();
        return services;
    }
}
