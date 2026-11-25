using Cadence.Apps.Econsent.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Econsent.Application;

public static class EconsentServiceCollectionExtensions
{
    public static IServiceCollection AddEconsentApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<EconsentUnitOfWork>();
        services.TryAddScoped<EconsentApplicationService>();
        return services;
    }
}
