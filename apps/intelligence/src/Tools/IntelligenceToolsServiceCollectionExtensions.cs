using Cadence.Modules.Intelligence.Tools.Kernel;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Modules.Intelligence.Tools;

/// <summary>Registers the governed agent tools (ADR-2633).</summary>
public static class IntelligenceToolsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the kernel read tools, their catalogue and requester-bound visibility. The host provides the kernel reads
    /// (<c>AddKernelPersistence</c>), <c>INodeVisibility</c> and <c>IClock</c>; until it registers <see cref="IAgentDryRun"/> and <see cref="IAgentSimulation"/> (ADR-2632), those
    /// tools answer "not available". The Proposal writer is registered separately (<c>AddIntelligenceProposalWriter</c>).
    /// </summary>
    public static IServiceCollection AddIntelligenceTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IAgentVisibility, RequesterBoundVisibility>();
        services.TryAddScoped<IAgentDryRun, UnavailableAgentDryRun>();
        services.TryAddScoped<IAgentSimulation, UnavailableAgentSimulation>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IExplanationSource, ValidationFindingExplanations>());

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, GetNodeTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, ListChildrenTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, SearchKernelTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, TraceLineageTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, ExplainTool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, CanITool>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentTool, SimulateTool>());
        services.TryAddScoped<AgentToolCatalog>();
        return services;
    }
}
