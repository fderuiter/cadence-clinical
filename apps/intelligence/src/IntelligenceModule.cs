using System;
using System.Linq;

using Anthropic;

using Cadence.Kernel.Modules;
using Cadence.Modules.Intelligence.Application;
using Cadence.Modules.Intelligence.Domain;
using Cadence.Modules.Intelligence.Endpoints;
using Cadence.Modules.Intelligence.Evaluation;
using Cadence.Modules.Intelligence.Infrastructure.Models;
using Cadence.Modules.Intelligence.Infrastructure.Persistence;
using Cadence.Modules.Intelligence.Infrastructure.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Cadence.Modules.Intelligence;

/// <summary>Adds the Intelligence module to the composition host (ADR-2601, ADR-2630 §4.2).</summary>
public static class IntelligenceModule
{
    /// <summary>The name of the environment in which integration tests run the host.</summary>
    public const string TestingEnvironment = "Testing";

    /// <summary>
    /// Registers the module: its commands with the kernel pipeline, its schema migrations, its tenant-scoped reads, the
    /// configured model providers and the agent runtime. The Anthropic provider is configured only when it has an API key;
    /// the fake provider only when asked for, and only in Development or Testing.
    /// </summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment.</param>
    /// <param name="connectionString">The connection string the host's kernel persistence uses.</param>
    /// <exception cref="InvalidOperationException">The fake provider is asked for outside Development or Testing.</exception>
    public static IServiceCollection AddCadenceIntelligenceModule(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        var options = configuration.GetSection(IntelligenceOptions.SectionName).Get<IntelligenceOptions>() ?? new IntelligenceOptions();
        if (options.FakeProvider && !environment.IsDevelopment() && !environment.IsEnvironment(TestingEnvironment))
        {
            throw new InvalidOperationException(
                $"Intelligence:FakeProvider is set in the {environment.EnvironmentName} environment. The fake answers are not a model's; use it in Development or Testing only.");
        }

        services
            .AddCadenceModule(IntelligenceDbContext.Module, typeof(IntelligenceModule).Assembly)
            .AddModuleCommandStore<IntelligenceDbContext>(
                IntelligenceDbContext.Module,
                o => new IntelligenceDbContext(o),
                npgsql => npgsql.MigrationsHistoryTable(ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable, IntelligenceDbContext.Schema))
            .AddModuleMigrations<IntelligenceDbContext>(
                IntelligenceDbContext.Module, IntelligenceDbContext.Schema, o => new IntelligenceDbContext(o), IntelligenceDatabase.GrantAppRoleSql)
            .AddModuleTenantContext<IntelligenceDbContext>(connectionString, o => new IntelligenceDbContext(o))
            .AddCommandHandler<StartAgentRun, Guid, StartAgentRunHandler>()
            .AddCommandValidator<StartAgentRun, StartAgentRunValidator>()
            .AddCommandHandler<CompleteAgentRun, AgentRunStatus, CompleteAgentRunHandler>()
            .AddCommandValidator<CompleteAgentRun, CompleteAgentRunValidator>()
            .AddCommandHandler<SetAgentControl, AgentControlState, SetAgentControlHandler>()
            .AddCommandValidator<SetAgentControl, SetAgentControlValidator>()
            .AddCommandHandler<RecordEvaluationRun, EvaluationRunState, RecordEvaluationRunHandler>()
            .AddCommandValidator<RecordEvaluationRun, RecordEvaluationRunValidator>()
            .AddCommandHandler<QualifyTaskConfiguration, TaskQualificationState, QualifyTaskConfigurationHandler>()
            .AddCommandValidator<QualifyTaskConfiguration, QualifyTaskConfigurationValidator>()
            .AddCommandHandler<SuspendTaskConfiguration, TaskQualificationState, SuspendTaskConfigurationHandler>()
            .AddCommandValidator<SuspendTaskConfiguration, SuspendTaskConfigurationValidator>();
        services.AddModuleAttestationTarget<TaskConfigurationAttestationCheck>();
        services.TryAddScoped<ITaskConfigurationSuspension, TaskConfigurationSuspension>();
        services.ConfigureAll<OpenApiOptions>(o => o.AddSchemaTransformer<LivenessScopeCodeOpenApiTransformer>());

        var prices = options.Prices;

        // Intelligence__Anthropic__ApiKey, or the SDK's usual ANTHROPIC_API_KEY. No key, no provider: never a default.
        var apiKey = options.Anthropic.ApiKey is { Length: > 0 } configured ? configured : configuration["ANTHROPIC_API_KEY"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var client = new AnthropicClient { ApiKey = apiKey };
            services.AddSingleton<IModelProvider>(new ChatClientModelProvider(
                "anthropic", client.AsIChatClient(options.Anthropic.Tiers.GetValueOrDefault("deep")), options.Anthropic.Tiers, prices));
        }

        if (options.FakeProvider)
        {
            var tiers = options.Anthropic.Tiers.ToDictionary(t => t.Key, t => $"fake-{t.Key}", StringComparer.Ordinal);
            services.AddSingleton<IModelProvider>(new ChatClientModelProvider(FakeChatClient.ProviderName, new FakeChatClient(), tiers, prices));
        }

        // Providers beyond Anthropic, tenant policy, residency, supplier records, the outbound guard and the sandbox (ADR-2636).
        services.AddIntelligenceInference(configuration, environment);

        services.TryAddSingleton<IModelProviderRegistry, ModelProviderRegistry>();
        services.TryAddSingleton<IRunChatClientFactory, ProviderRunChatClientFactory>();
        services.TryAddSingleton<ITaskConfigurationCatalog>(sp => new RegisteredTaskConfigurations(sp.GetServices<TaskConfiguration>()));
        services.TryAddSingleton<EfAgentLedger>();
        services.TryAddSingleton<IAgentLedger>(sp => sp.GetRequiredService<EfAgentLedger>());
        services.TryAddSingleton<IEvaluationRuns>(sp => sp.GetRequiredService<EfAgentLedger>());
        services.TryAddSingleton(new AgentRunnerOptions(TimeSpan.FromSeconds(Math.Max(1, options.RequestTimeoutSeconds))));
        services.TryAddScoped<AgentRunner>();
        services.AddIntelligenceEvaluation();
        return services;
    }

    /// <summary>
    /// Registers an agent's task configuration with the runtime. Agents call this from their own registration; #5385
    /// replaces it with versioned, qualified configuration files (ADR-2631).
    /// </summary>
    public static IServiceCollection AddAgentTask(this IServiceCollection services, TaskConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSingleton(configuration);
        return services;
    }

    /// <summary>Maps the module's routes under <c>/api/v1/ai</c> in the <c>intelligence</c> OpenAPI group.</summary>
    public static IEndpointRouteBuilder MapIntelligenceModule(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup(string.Empty).WithGroupName(IntelligenceDbContext.Module);
        group.MapIntentEndpoints().MapIntelligenceEndpoints().MapBiometricEndpoints();
        return app;
    }
}
