using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Safety.Application;

public static class SafetyServiceCollectionExtensions
{
    public static IServiceCollection AddSafetyApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<DsurApplicationService>();
        return services;
    }
}
