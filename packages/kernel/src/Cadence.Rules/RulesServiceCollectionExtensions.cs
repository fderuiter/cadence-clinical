using Cadence.Kernel.Standards;
using Cadence.Kernel.Time;
using Cadence.Kernel.Validation;
using Cadence.Rules.Amendment;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Indicators;
using Cadence.Rules.Packs;
using Cadence.Rules.Validation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cadence.Rules;

/// <summary>Dependency injection registration for <c>Cadence.Rules</c>.</summary>
public static class RulesServiceCollectionExtensions
{
    /// <summary>
    /// Registers the rules subsystem: the .NET <see cref="IRuleEvaluator"/> (replace the registration to swap engines),
    /// the <see cref="RulePackLoader"/> (needs an <see cref="IStandardsRegistry"/>, from
    /// <c>AddKernelStandardsPersistence()</c>), the rule pack content validator that <c>DraftLibraryItem</c> runs, and the
    /// <see cref="ValidationRunner"/> (needs the kernel's <c>IKernelCommandSender</c> and <c>IClock</c>), and the indicator
    /// set loader, content validator, <see cref="IndicatorEvaluator"/> and the scoped <see cref="StudyIndicators"/>, which
    /// needs the host's <c>INodeVisibility</c> and the population sources kernel persistence registers (ADR-2488).
    /// </summary>
    public static IServiceCollection AddCadenceRules(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.TryAddSingleton<IRuleEvaluator, DotNetRuleEvaluator>();
        services.TryAddSingleton<RulePackLoader>();
        services.TryAddScoped<ValidationRunner>();
        services.TryAddSingleton<BatchJobStore>();
        services.TryAddScoped<ParallelBatchRuleValidator>();
        services.TryAddScoped<AmendmentRegressionEngine>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILibraryContentValidator, RulePackContentValidator>());
        services.TryAddSingleton<IndicatorSetLoader>();
        services.TryAddSingleton<IndicatorEvaluator>();
        services.TryAddScoped<StudyIndicators>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILibraryContentValidator, IndicatorSetContentValidator>());
        return services;
    }

    /// <summary>
    /// Registers <see cref="ICollectionValidationService"/> for hosts that wire kernel standards and collection persistence.
    /// </summary>
    public static IServiceCollection AddCadenceCollectionValidation(this IServiceCollection services)
    {
        services.TryAddScoped<ICollectionValidationService, CollectionValidationService>();
        return services;
    }
}
