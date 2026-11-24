using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Cadence.ToolingCore.Services;

using FluentAssertions;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Enforces cross-service isolation across <c>Cadence.Apps.*</c> microservices: blocks both direct compile-time
/// imports/references and indirect runtime reflection string lookups (<c>OpCodes.Ldstr</c> operands naming sibling
/// <c>Cadence.Apps.&lt;Sibling&gt;</c> types/namespaces such as <c>AppDomain.CurrentDomain.GetAssemblies()</c> lookups),
/// ratcheted against the shrink-only baseline at <see cref="ImportValidatorService.BaselineRelativePath"/>.
/// </summary>
public partial class CrossServiceIsolationTests
{
    private const string RemediationGuidance =
        "Route cross-module clinical mutations through IKernelCommandSender in the central kernel command pipeline " +
        "(ADR-2600) and asynchronous cross-service notifications through Cadence.Contracts events (ADR-2524) rather " +
        "than importing or reflecting over sibling Cadence.Apps.* types.";

    private static readonly IReadOnlyList<string> BaselineHeader =
    [
        "# Cross-service isolation ratchet baseline (ImportValidatorService source scan + Mono.Cecil Ldstr / reference IL scan).",
        "# Records pre-existing sibling-service debt (such as LetterTemplateEndpoints.cs reflection lookups). The list may only shrink.",
        $"# Regenerate with {KernelBoundaryBaseline.UpdateEnvironmentVariable}=1 (see tests/Cadence.ArchitectureTests/Architecture/CrossServiceIsolationTests.cs).",
    ];

    private static readonly Lazy<IReadOnlyList<Assembly>> NonKernelAssemblies =
        new(KernelBoundaryRules.LoadNonKernelCadenceAssemblies);

    [Fact(DisplayName = "CrossServiceIsolation_SourceAndCompiledIl_MustNotExceedShrinkOnlyBaseline")]
    [Trait("Category", "Architecture")]
    public void CrossServiceIsolationSourceAndCompiledIlMustNotExceedShrinkOnlyBaseline()
    {
        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();

        var sourceViolations = ImportValidatorService.ScanSourceViolations(repoRoot);
        var ilViolations = EvaluateCompiledAppAssemblies(NonKernelAssemblies.Value);
        var combined = sourceViolations.Concat(ilViolations).ToList();

        var comparison = ShrinkOnlyBaseline.Verify(
            ImportValidatorService.BaselineRelativePath,
            BaselineHeader,
            combined);

        comparison.IsClean.Should().BeTrue(
            comparison.Describe("CrossServiceIsolation", nameof(CrossServiceIsolationTests), RemediationGuidance));

        var recorded = ShrinkOnlyBaseline.Read(ImportValidatorService.BaselineRelativePath);
        recorded.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();

        // Also confirm the CLI's ImportValidatorService agrees cleanly with the shared baseline.
        ImportValidatorService.ValidateImports(repoRoot).Should().Be(0);
    }

    [Fact(DisplayName = "CrossServiceIsolation_Bites_OnSyntheticDirectImportsAndReflectionStrings")]
    [Trait("Category", "Architecture")]
    public void CrossServiceIsolationBitesOnSyntheticDirectImportsAndReflectionStrings()
    {
        const string syntheticSource = """
            global using Cadence.Apps.Payments.Domain;
            using static Cadence.Apps.Safety.Endpoints.SafetyRoutes;
            using EtmfDb = Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext;

            namespace Cadence.Apps.Ctms.Endpoints;

            public static class BypassEndpoint
            {
                public static Type? ResolveSibling()
                {
                    // Comment mentioning Cadence.Apps.Tickets.Domain must be ignored.
                    _ = typeof(Cadence.Apps.Quality.Domain.QualityDeviation);
                    return Type.GetType("Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext");
                }
            }
            """;

        var sourceHits = ImportValidatorService.ScanFileSource(
            "ctms",
            "apps/ctms/src/Endpoints/BypassEndpoint.cs",
            syntheticSource);

        sourceHits.Should().Equal(
            "SOURCE|ctms|apps/ctms/src/Endpoints/BypassEndpoint.cs|code:Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext",
            "SOURCE|ctms|apps/ctms/src/Endpoints/BypassEndpoint.cs|code:Cadence.Apps.Payments.Domain",
            "SOURCE|ctms|apps/ctms/src/Endpoints/BypassEndpoint.cs|code:Cadence.Apps.Quality.Domain.QualityDeviation",
            "SOURCE|ctms|apps/ctms/src/Endpoints/BypassEndpoint.cs|code:Cadence.Apps.Safety.Endpoints.SafetyRoutes",
            "SOURCE|ctms|apps/ctms/src/Endpoints/BypassEndpoint.cs|string:Cadence.Apps.Notifications.Infrastructure.Persistence.NotificationsDbContext");

        var ilHits = EvaluateSingleTypeIl(
            typeof(CrossServiceIsolationTests).Assembly,
            "Cadence.Apps.Ctms",
            "Ctms",
            typeof(SyntheticCrossServiceReflectionFixture).FullName!);

        ilHits.Should().Equal(
            "IL|Cadence.Apps.Ctms|Cadence.ArchitectureTests.Architecture.SyntheticCrossServiceReflectionFixture.LookupSiblingAsync->Cadence.Apps.Payments.Infrastructure.PaymentsDbContext",
            "IL|Cadence.Apps.Ctms|Cadence.ArchitectureTests.Architecture.SyntheticCrossServiceReflectionFixture.LookupSiblingDbContext->Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext");

        var ratchet = ShrinkOnlyBaseline.Compare(
            ImportValidatorService.BaselineRelativePath,
            ilHits,
            ShrinkOnlyBaseline.Read(ImportValidatorService.BaselineRelativePath));
        ratchet.IsClean.Should().BeFalse("unbaselined cross-service reflection strings must fail verification");
    }

    internal static IReadOnlyList<string> EvaluateCompiledAppAssemblies(IEnumerable<Assembly> assemblies)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            string assemblyName = assembly.GetName().Name ?? string.Empty;
            if (!assemblyName.StartsWith("Cadence.Apps.", StringComparison.Ordinal) ||
                assemblyName.Equals("Cadence.Apps.Gateway", StringComparison.Ordinal))
            {
                continue;
            }

            string selfApp = assemblyName["Cadence.Apps.".Length..];
            foreach (string hit in ScanModuleIl(assembly.Location, assemblyName, selfApp, typeFilter: null))
            {
                violations.Add(hit);
            }
        }

        return [.. violations];
    }

    private static IReadOnlyList<string> EvaluateSingleTypeIl(
        Assembly assembly,
        string simulatedAssemblyName,
        string simulatedAppName,
        string outerTypeFullName) =>
        ScanModuleIl(
            assembly.Location,
            simulatedAssemblyName,
            simulatedAppName,
            outerType => outerType.FullName == outerTypeFullName);

    private static IReadOnlyList<string> ScanModuleIl(
        string modulePath,
        string reportedAssemblyName,
        string selfApp,
        Func<TypeDefinition, bool>? typeFilter)
    {
        string normalizedSelf = ImportValidatorService.NormalizeAppName(selfApp);
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        using var module = ModuleDefinition.ReadModule(modulePath);

        if (typeFilter is null)
        {
            foreach (var asmRef in module.AssemblyReferences)
            {
                if (asmRef.Name.StartsWith("Cadence.Apps.", StringComparison.Ordinal))
                {
                    string targetApp = asmRef.Name["Cadence.Apps.".Length..];
                    if (!ImportValidatorService.NormalizeAppName(targetApp).Equals(normalizedSelf, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"IL|{reportedAssemblyName}|AssemblyRef->{asmRef.Name}");
                    }
                }
            }
        }

        foreach (var type in module.GetTypes())
        {
            var outerType = DirectStateWriteRules.UnwrapOuterType(type);
            if (typeFilter is not null && !typeFilter(outerType))
            {
                continue;
            }

            string outerTypeName = outerType.FullName.Replace('/', '.');
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                string logicalMethod = DirectStateWriteRules.ResolveLogicalMethodName(type, method);
                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand is not string literal)
                    {
                        continue;
                    }

                    foreach (Match match in SiblingAppLiteralRegex().Matches(literal))
                    {
                        string targetApp = match.Groups["app"].Value;
                        if (!ImportValidatorService.NormalizeAppName(targetApp).Equals(normalizedSelf, StringComparison.OrdinalIgnoreCase))
                        {
                            violations.Add(
                                $"IL|{reportedAssemblyName}|{outerTypeName}.{logicalMethod}->{match.Groups["full"].Value}");
                        }
                    }
                }
            }
        }

        return [.. violations];
    }

    [GeneratedRegex(
        @"(?<full>Cadence\.Apps\.(?<app>[A-Za-z0-9_]+)(?:\.[A-Za-z0-9_]+)*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SiblingAppLiteralRegex();
}

/// <summary>
/// Synthetic cross-service reflection bypass fixture proving <see cref="CrossServiceIsolationTests"/> catches
/// compiled <c>Ldstr</c> operands in both synchronous methods and async state machines.
/// </summary>
internal static class SyntheticCrossServiceReflectionFixture
{
    public static Type? LookupSiblingDbContext() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.FullName == "Cadence.Apps.Etmf.Infrastructure.Persistence.EtmfDbContext");

    public static async System.Threading.Tasks.Task<string> LookupSiblingAsync()
    {
        await System.Threading.Tasks.Task.Yield();
        return "Cadence.Apps.Payments.Infrastructure.PaymentsDbContext";
    }
}
