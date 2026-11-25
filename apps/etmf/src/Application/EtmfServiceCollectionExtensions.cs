using Cadence.Apps.Etmf.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Etmf.Application;

public static class EtmfServiceCollectionExtensions
{
    public static IServiceCollection AddEtmfApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<EtmfUnitOfWork>();
        services.TryAddScoped<EtmfQcApplicationService>();
        services.TryAddScoped<EtmfDocumentApplicationService>();
        services.TryAddScoped<EtmfExportApplicationService>();
        return services;
    }
}
