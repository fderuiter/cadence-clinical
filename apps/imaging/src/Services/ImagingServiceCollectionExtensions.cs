using Cadence.Apps.Imaging.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Apps.Imaging.Services;

public static class ImagingServiceCollectionExtensions
{
    public static IServiceCollection AddImagingApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<IDicomIngestionService, DicomIngestionService>();
        services.TryAddScoped<IImagingSegmentationService, ImagingSegmentationService>();
        services.TryAddScoped<ImagingUnitOfWork>();
        services.TryAddScoped<DicomQueryService>();
        services.TryAddScoped<ImagingAdjudicationService>();
        return services;
    }
}
