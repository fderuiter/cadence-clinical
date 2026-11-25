using Cadence.Apps.Tickets.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Tickets.Application;

public static class TicketsServiceCollectionExtensions
{
    public static IServiceCollection AddTicketsApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<TicketsUnitOfWork>();
        services.TryAddScoped<TicketCrudApplicationService>();
        services.TryAddScoped<TicketWorkflowApplicationService>();
        return services;
    }
}
