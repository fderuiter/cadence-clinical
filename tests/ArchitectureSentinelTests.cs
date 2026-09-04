using System;
using System.Linq;
using System.Reflection;

using Cadence.Core.Domain;
using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests;

public class ArchitectureSentinelTests
{
    /// <summary>
    /// Validates Hexagonal and Clean Architecture boundary purity: Cadence.Core.Domain must not reference infrastructure or web assemblies.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "DomainLayer_MustNotReference_InfrastructureOrPresentationAssemblies")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void DomainLayerMustNotReferenceInfrastructureOrPresentationAssemblies()
    {
        // Arrange
        var domainAssembly = typeof(AggregateRoot<>).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        // Assert
        referencedAssemblies.Should().NotContain(name => name.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase),
            "Domain core must remain pure and free from ORM/infrastructure dependencies");
        referencedAssemblies.Should().NotContain(name => name.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase),
            "Domain core must remain pure and free from ASP.NET Core presentation dependencies");
        referencedAssemblies.Should().NotContain(name => name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase),
            "Domain core must not reference database provider drivers directly");
    }

    /// <summary>
    /// Validates 21 CFR Part 11 regulatory invariant: All DDD aggregate roots must implement IGxPAuditable.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "AggregateRoots_MustImplement_IGxPAuditableContract")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void AggregateRootsMustImplementIGxPAuditableContract()
    {
        // Arrange
        var aggregateType = typeof(AggregateRoot<string>);

        // Assert
        typeof(IGxPAuditable).IsAssignableFrom(aggregateType).Should().BeTrue(
            "All AggregateRoot types must implement IGxPAuditable to enforce 21 CFR Part 11 audit trails");
    }

    /// <summary>
    /// Validates that test methods in test projects carry compile-time [GxPRequirement] regulatory tags.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "RegulatedTestSuites_MustFeature_GxPRequirementAttributes")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void RegulatedTestSuitesMustFeatureGxPRequirementAttributes()
    {
        // Arrange
        var testAssembly = typeof(Cadence.UnitTests.SecurityAndDomainTests).Assembly;
        var testMethods = testAssembly.GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttributes(typeof(FactAttribute), false).Length > 0)
            .ToList();

        // Assert
        testMethods.Should().NotBeEmpty();
        foreach (var method in testMethods)
        {
            var hasGxpAttribute = method.GetCustomAttributes(typeof(GxPRequirementAttribute), false).Length > 0;
            hasGxpAttribute.Should().BeTrue($"Test method '{method.DeclaringType?.Name}.{method.Name}' must declare [GxPRequirement]");
        }
    }
}
