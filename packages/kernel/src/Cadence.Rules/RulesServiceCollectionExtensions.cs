using Cadence.Kernel.Standards;
using Cadence.Rules.Evaluation;
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
    /// <c>AddKernelStandardsPersistence()</c>) and the rule pack content validator that <c>DraftLibraryItem</c> runs.
    /// </summary>
    public static IServiceCollection AddCadenceRules(this IServiceCollection services)
    {
        services.TryAddSingleton<IRuleEvaluator, DotNetRuleEvaluator>();
        services.TryAddSingleton<RulePackLoader>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILibraryContentValidator, RulePackContentValidator>());
        return services;
    }
}
