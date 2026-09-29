using System;
using System.Linq;
using System.Reflection;

using Cadence.Kernel.Api;
using Cadence.Kernel.Application;
using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Enforces the dependency direction between the five kernel assemblies (spec §2.4, #4772):
/// Abstractions → Domain → Application → Persistence → Api, with Domain and Abstractions free of EF Core,
/// ASP.NET Core, Npgsql and rendering.
/// </summary>
public class KernelLayeringTests
{
    private static readonly string[] InfrastructurePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
    ];

    private static readonly Assembly Abstractions = typeof(Cadence.Kernel.StudyId).Assembly;
    private static readonly Assembly Domain = typeof(Study).Assembly;
    private static readonly Assembly Application = typeof(KernelApplicationAssembly).Assembly;
    private static readonly Assembly Persistence = typeof(KernelPersistenceServiceCollectionExtensions).Assembly;
    private static readonly Assembly Api = typeof(KernelApiEndpointRouteBuilderExtensions).Assembly;

    /// <summary>
    /// Validates that Kernel.Domain and Kernel.Abstractions reference no EF Core, ASP.NET Core or Npgsql assembly.
    /// @req:PRD-SYS-053
    /// </summary>
    [Theory(DisplayName = "Kernel_DomainAndAbstractions_AreFreeOfInfrastructure")]
    [InlineData("Cadence.Kernel.Abstractions")]
    [InlineData("Cadence.Kernel.Domain")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void KernelDomainAndAbstractionsAreFreeOfInfrastructure(string assemblyName)
    {
        var assembly = new[] { Abstractions, Domain }.Single(a => a.GetName().Name == assemblyName);

        References(assembly)
            .Where(r => InfrastructurePrefixes.Any(p => r.StartsWith(p, StringComparison.Ordinal)))
            .Should().BeEmpty($"{assemblyName} must stay pure (spec §2.4)");
    }

    /// <summary>
    /// Validates that no kernel assembly references the rendering package (rendering is a kernel non-goal, §0.2).
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "Kernel_Assemblies_DoNotReferenceTemplating")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void KernelAssembliesDoNotReferenceTemplating()
    {
        foreach (var assembly in new[] { Abstractions, Domain, Application, Persistence, Api })
        {
            References(assembly).Should().NotContain(r => r.StartsWith("Cadence.Templating", StringComparison.Ordinal)
                    || r.StartsWith("Fluid", StringComparison.Ordinal),
                $"{assembly.GetName().Name} must not carry rendering");
        }
    }

    /// <summary>
    /// Validates the kernel dependency direction: inner layers never reference outer ones.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "Kernel_Layers_DependInwardOnly")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void KernelLayersDependInwardOnly()
    {
        References(Abstractions).Should().NotContain(r => r.StartsWith("Cadence.", StringComparison.Ordinal),
            "Abstractions is the innermost kernel layer");
        References(Domain).Should().NotContain(
            ["Cadence.Kernel.Application", "Cadence.Kernel.Persistence", "Cadence.Kernel.Api"]);
        References(Application).Should().NotContain(["Cadence.Kernel.Persistence", "Cadence.Kernel.Api"]);
        References(Persistence).Should().NotContain("Cadence.Kernel.Api");
    }

    private static string[] References(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];
}
