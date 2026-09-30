using Cadence.Kernel.Standards;
using Cadence.Rules.Evaluation;
using Cadence.Rules.Indicators;
using Cadence.Rules.Packs;

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
    /// set loader, content validator and <see cref="IndicatorEvaluator"/> (ADR-2488).
    /// </summary>
    public static IServiceCollection AddCadenceRules(this IServiceCollection services)
    {
        services.TryAddSingleton<IRuleEvaluator, DotNetRuleEvaluator>();
        services.TryAddSingleton<RulePackLoader>();
        services.TryAddScoped<ValidationRunner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILibraryContentValidator, RulePackContentValidator>());
        services.TryAddSingleton<IndicatorSetLoader>();
        services.TryAddSingleton<IndicatorEvaluator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILibraryContentValidator, IndicatorSetContentValidator>());
        return services;
    }
}
