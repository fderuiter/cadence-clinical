using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Modules.Intelligence.Grounding;

/// <summary>Registers the Proposal writer (ADR-2633 §4.7).</summary>
public static class IntelligenceGroundingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Proposal writer. Call it together with the host's <see cref="IPlanArtifactStore"/> and
    /// <see cref="IAgentProposalGateway"/> (ADR-2632); without them the writer cannot be resolved, so it is not part of
    /// <c>AddIntelligenceTools</c> and a host without them still starts.
    /// </summary>
    public static IServiceCollection AddIntelligenceProposalWriter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<AgentProposalWriter>();
        return services;
    }
}
