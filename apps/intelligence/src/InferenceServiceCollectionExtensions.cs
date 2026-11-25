using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Deid;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Inference;
using Cadence.Modules.Intelligence.Inference.Guard;
using Cadence.Modules.Intelligence.Inference.Providers;
using Cadence.Modules.Intelligence.Inference.Routing;
using Cadence.Modules.Intelligence.Infrastructure.Models;
using Cadence.Modules.Intelligence.Sandbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Cadence.Modules.Intelligence;

/// <summary>Registers the inference layer (ADR-2636): providers, supplier records, the router, the guard and the sandbox.</summary>
public static class InferenceServiceCollectionExtensions
{
    /// <summary>
    /// Adds the cloud and in-deployment providers configured under <c>Intelligence:Inference:Providers</c>, the
    /// supplier catalogue with the deployment's overlay, <see cref="IInferenceRouter"/>, and the sandbox egress
    /// handler on every factory HTTP client. Configuration errors stop the host at start, not at the first run.
    /// </summary>
    public static IServiceCollection AddIntelligenceInference(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        services.TryAddSingleton(new InferenceHost(environment.IsDevelopment()));

        var section = configuration.GetSection(InferenceOptions.Section);
        var intelligence = configuration.GetSection(IntelligenceOptions.SectionName).Get<IntelligenceOptions>() ?? new IntelligenceOptions();
        services.TryAddSingleton(new InferenceModelSettings(intelligence.Anthropic.Tiers, intelligence.Prices));
        services.AddOptions<InferenceOptions>().Bind(section);
        services.AddOptions<SandboxEgressOptions>().Bind(configuration.GetSection(SandboxEgressOptions.Section));
        var providersSection = section.GetSection("Providers");

        AddProvider<ClaudePlatformAwsOptions>(services, ClaudePlatformAwsProvider.ProviderId, providersSection.GetSection(nameof(InferenceProvidersOptions.AnthropicAws)), (sp, o) => new ClaudePlatformAwsProvider(sp.GetRequiredService<IHttpClientFactory>(), o));
        AddProvider<BedrockOptions>(services, BedrockProvider.ProviderId, providersSection.GetSection(nameof(InferenceProvidersOptions.Bedrock)), (sp, o) => new BedrockProvider(sp.GetRequiredService<IHttpClientFactory>(), o));
        AddProvider<VertexOptions>(services, VertexProvider.ProviderId, providersSection.GetSection(nameof(InferenceProvidersOptions.Vertex)), (sp, o) => new VertexProvider(sp.GetRequiredService<IHttpClientFactory>(), o));
        AddProvider<FoundryOptions>(services, FoundryProvider.ProviderId, providersSection.GetSection(nameof(InferenceProvidersOptions.Foundry)), (sp, o) => new FoundryProvider(sp.GetRequiredService<IHttpClientFactory>(), o));
        AddProvider<OpenAiCompatibleOptions>(services, OpenAiCompatibleProvider.ProviderId, providersSection.GetSection(nameof(InferenceProvidersOptions.OpenAiCompatible)), (sp, o) => new OpenAiCompatibleProvider(sp.GetRequiredService<IHttpClientFactory>(), o));

        var overlays = section.GetSection("Suppliers").Get<Dictionary<string, SupplierOverlayOptions>>()
            ?? new Dictionary<string, SupplierOverlayOptions>(StringComparer.Ordinal);
        services.TryAddSingleton<ISupplierCatalog>(_ => new SupplierCatalog(overlays));
        services.TryAddSingleton<DeidDetector>();
        services.TryAddSingleton<OutboundGuardFactory>();
        services.TryAddSingleton<ITenantInferencePolicies, DefaultTenantInferencePolicies>();
        services.TryAddSingleton<IModelProviderRegistry, ModelProviderRegistry>();
        services.TryAddSingleton<IInferenceRouter, InferenceRouter>();
        services.TryAddSingleton<IRunChatClientFactory, InferenceRunChatClientFactory>();

        services.AddHttpClient();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<HttpClientFactoryOptions>, SandboxEgressConfiguration>());
        services.AddHostedService<InferenceStartupCheck>();
        return services;
    }

    /// <summary>
    /// Registers a provider, its entry in the module's provider registry, and its named HTTP client, whose endpoint
    /// handler pins the provider's hosts.
    /// </summary>
    public static IServiceCollection AddInferenceProvider(this IServiceCollection services, string providerId, Func<IServiceProvider, IInferenceProvider> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton(factory);
        services.AddSingleton<IModelProvider>(sp => new InferenceModelProvider(
            sp.GetServices<IInferenceProvider>().Single(p => p.Id == providerId),
            sp.GetService<InferenceModelSettings>() ?? new InferenceModelSettings(new IntelligenceOptions().Anthropic.Tiers, new Dictionary<string, ModelPrice>())));
        services.AddHttpClient(InferenceHttpClients.NameFor(providerId))
            .AddHttpMessageHandler(sp => new ProviderEndpointHandler(
                providerId, sp.GetServices<IInferenceProvider>().Single(p => p.Id == providerId).EndpointHosts));
        return services;
    }

    private static void AddProvider<TOptions>(
        IServiceCollection services,
        string providerId,
        IConfigurationSection section,
        Func<IServiceProvider, TOptions, IInferenceProvider> create)
        where TOptions : class
    {
        if (!HasConfiguredValue(section))
        {
            return;
        }

        if (section.Get<TOptions>() is { } options)
        {
            services.AddInferenceProvider(providerId, sp => create(sp, options));
        }
    }

    private static bool HasConfiguredValue(IConfigurationSection section) =>
        section.AsEnumerable(makePathsRelative: true)
            .Any(entry => !string.IsNullOrWhiteSpace(entry.Value));

    /// <summary>Builds every provider and the supplier catalogue at start, so a bad endpoint or approval stops the host.</summary>
    private sealed class InferenceStartupCheck(IEnumerable<IInferenceProvider> providers, ISupplierCatalog suppliers) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var ids = providers.Select(p => p.Id).ToList();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            {
                throw new InvalidOperationException($"Inference provider ids must be unique: {string.Join(", ", ids)}.");
            }

            _ = suppliers.All;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
