using Cadence.Modules.Intelligence.Evaluation.Production;
using Cadence.Modules.Intelligence.Evaluation.Runs;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Modules.Intelligence.Evaluation;

/// <summary>Registers the evaluation harness and, once its ports exist, the production signal recorder (ADR-2634).</summary>
public static class IntelligenceEvaluationServiceCollectionExtensions
{
    /// <summary>Adds <see cref="EvaluationHarness"/>. <c>IClock</c> comes from the host.</summary>
    public static IServiceCollection AddIntelligenceEvaluation(this IServiceCollection services)
    {
        services.TryAddSingleton<EvaluationHarness>();
        return services;
    }

    /// <summary>
    /// Adds <see cref="ProductionSignalRecorder"/> with its ports: where outcomes are stored, where Quality signals go, and
    /// how a configuration is suspended. The outcome store, the Quality module and the configuration registry (ADR-2631)
    /// call this when they provide them; until then the recorder is not registered, so the host's container stays valid.
    /// </summary>
    public static IServiceCollection AddProductionSignals<TStore, TSink, TSuspension>(this IServiceCollection services)
        where TStore : class, IProposalOutcomeStore
        where TSink : class, IQualitySignalSink
        where TSuspension : class, ITaskConfigurationSuspension
    {
        services.TryAddScoped<IProposalOutcomeStore, TStore>();
        services.TryAddScoped<IQualitySignalSink, TSink>();
        services.TryAddScoped<ITaskConfigurationSuspension, TSuspension>();
        services.TryAddScoped<ProductionSignalRecorder>();
        return services;
    }
}
