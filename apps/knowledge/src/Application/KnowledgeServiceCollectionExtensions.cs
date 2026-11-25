using Cadence.Apps.Knowledge.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Knowledge.Application;

public static class KnowledgeServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<KnowledgeUnitOfWork>();
        services.TryAddScoped<KnowledgeQueryApplicationService>();
        services.TryAddScoped<TrainingApplicationService>();
        return services;
    }
}
