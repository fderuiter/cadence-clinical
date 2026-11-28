using System;

using Cadence.Apps.Ctms.Application;
using Cadence.Apps.Ctms.Application.Services;
using Cadence.Apps.Ctms.Infrastructure.Kernel;
using Cadence.Apps.Ctms.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Security.Authentication;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Cadence.Apps.Ctms;

public static class CtmsServiceCollectionExtensions
{
    public const string KernelTrustHttpClientName = "CtmsKernelTrust";

    public static IServiceCollection AddCtmsApplicationServices(this IServiceCollection services)
    {
        services.AddHttpClient(KernelTrustHttpClientName);
        services.TryAddSingleton<InMemoryCtmsKernelTrustClient>();
        services.TryAddScoped<ICtmsKernelTrustClient>(sp =>
        {
            var config = sp.GetService<IConfiguration>();
            string? kernelUrl = config?["KERNEL_SERVICE_URL"] ?? config?["Services:Kernel"];
            if (!string.IsNullOrWhiteSpace(kernelUrl) && Uri.TryCreate(kernelUrl, UriKind.Absolute, out var baseUri))
            {
                var factory = sp.GetRequiredService<IHttpClientFactory>();
                var client = factory.CreateClient(KernelTrustHttpClientName);
                client.BaseAddress = baseUri;
                return new HttpCtmsKernelTrustClient(
                    client,
                    sp.GetRequiredService<IOptionsMonitor<GatewayAuthenticationOptions>>(),
                    sp.GetRequiredService<IClock>());
            }

            return sp.GetRequiredService<InMemoryCtmsKernelTrustClient>();
        });

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
