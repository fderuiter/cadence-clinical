using Cadence.Kernel.Standards.Mapping;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Standards.Mapping;

/// <summary>Dependency injection registration for the mapping engine.</summary>
public static class MappingServiceCollectionExtensions
{
    /// <summary>Registers the .NET <see cref="IMappingEngine"/>. Register another implementation first to swap engines.</summary>
    public static IServiceCollection AddCadenceMapping(this IServiceCollection services)
    {
        services.TryAddSingleton<IMappingEngine, MappingEngine>();
        return services;
    }
}
