using Cadence.Kernel.Standards;
using Cadence.Standards.Blocks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Standards;

/// <summary>Dependency injection registration for <c>Cadence.Standards</c>.</summary>
public static class StandardsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the collection specification content validator that <c>DraftLibraryItem</c> runs, so a
    /// <c>CollectionSpecification</c> library item is refused unless its content can be instantiated (ADR-2503).
    /// </summary>
    public static IServiceCollection AddCadenceStandards(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILibraryContentValidator, CollectionSpecificationContentValidator>());
        return services;
    }
}
