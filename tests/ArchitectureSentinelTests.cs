using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Cadence.Apps.Ctms.Domain;
using Cadence.Apps.Designer;
using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Fileshare.Domain;
using Cadence.Apps.Quality.Domain;
using Cadence.Core.Domain;
using Cadence.Kernel.Domain.Entities;
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
    /// Validates 21 CFR Part 11 regulatory invariant: All DDD aggregate roots across domain assemblies must implement IGxPAuditable.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "AggregateRoots_MustImplement_IGxPAuditableContract")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void AggregateRootsMustImplementIGxPAuditableContract()
    {
        // Arrange
        var domainAssemblies = new[]
        {
            typeof(AggregateRoot<>).Assembly,
            typeof(ClinicalSubject).Assembly,
            typeof(CtmsSiteMilestone).Assembly,
            typeof(TmfDocument).Assembly,
            typeof(QualityDeviation).Assembly,
            typeof(FileRecord).Assembly,
            typeof(Study).Assembly
        }.Distinct();

        var aggregateRootTypes = domainAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => typeof(AggregateRootBase).IsAssignableFrom(t) || IsAssignableToGenericType(t, typeof(AggregateRoot<>)))
            .ToList();

        aggregateRootTypes.Should().NotBeEmpty("Domain assemblies must contain concrete aggregate root types");

        // Assert
        foreach (var type in aggregateRootTypes)
        {
            typeof(IGxPAuditable).IsAssignableFrom(type).Should().BeTrue(
                $"Aggregate root type '{type.FullName}' must implement IGxPAuditable to enforce 21 CFR Part 11 audit trails");
        }
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

    /// <summary>
    /// Validates Clean Architecture boundary purity: Core domain models, value objects, domain rules, and ports
    /// across all C# execution domain namespaces must have zero references to ASP.NET Core or MassTransit.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "ExecutionDomainLayer_MustNotReference_AspNetCoreOrMassTransit")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void ExecutionDomainLayerMustNotReferenceAspNetCoreOrMassTransit()
    {
        // Arrange
        var executionAssembly = typeof(ClinicalSubject).Assembly;
        var domainTypes = executionAssembly.GetTypes()
            .Where(t => t.Namespace != null &&
                        t.Namespace.StartsWith("Cadence.Apps.Execution.Domain", StringComparison.Ordinal))
            // MIG-SUBJ-010 explicit exception: DomainErrorHttpMapping and DomainErrorExtensions serve as the
            // isolated ProblemDetails HTTP mapping bridge at the boundary.
            .Where(t => t != typeof(DomainErrorHttpMapping) && t != typeof(DomainErrorExtensions))
            .ToList();

        domainTypes.Should().NotBeEmpty("Domain layer must contain discovered types");

        // Assert
        foreach (var type in domainTypes)
        {
            var referencedTypes = new HashSet<Type>();

            if (type.BaseType != null) referencedTypes.Add(type.BaseType);
            foreach (var iface in type.GetInterfaces()) referencedTypes.Add(iface);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                referencedTypes.Add(field.FieldType);
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                referencedTypes.Add(prop.PropertyType);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                referencedTypes.Add(method.ReturnType);
                foreach (var p in method.GetParameters()) referencedTypes.Add(p.ParameterType);
            }
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var p in ctor.GetParameters()) referencedTypes.Add(p.ParameterType);
            }

            foreach (var refType in referencedTypes)
            {
                var assemblyName = refType.Assembly.GetName().Name ?? string.Empty;
                var typeNamespace = refType.Namespace ?? string.Empty;

                assemblyName.Should().NotStartWith("Microsoft.AspNetCore",
                    $"Domain type '{type.FullName}' must not reference ASP.NET Core types (found '{refType.FullName}')");
                typeNamespace.Should().NotStartWith("Microsoft.AspNetCore",
                    $"Domain type '{type.FullName}' must not reference ASP.NET Core namespaces (found '{refType.FullName}')");

                assemblyName.Should().NotStartWith("MassTransit",
                    $"Domain type '{type.FullName}' must not reference MassTransit types (found '{refType.FullName}')");
                typeNamespace.Should().NotStartWith("MassTransit",
                    $"Domain type '{type.FullName}' must not reference MassTransit namespaces (found '{refType.FullName}')");
            }
        }

        // Boundary check on ClinicalSubject aggregate root specifically
        typeof(ClinicalSubject).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Should().NotContain(t => (t.Namespace ?? "").StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                                      (t.Namespace ?? "").StartsWith("MassTransit", StringComparison.Ordinal),
                "ClinicalSubject signatures must remain completely pure of web or messaging infrastructure");
    }

    private static readonly Assembly[] ProductModuleAssemblies =
    [
        typeof(ClinicalSubject).Assembly,
        typeof(CtmsSiteMilestone).Assembly,
        typeof(Cadence.Apps.Designer.Program).Assembly,
        typeof(TmfDocument).Assembly,
        typeof(QualityDeviation).Assembly,
        typeof(FileRecord).Assembly,
        typeof(Cadence.Apps.AiGateway.Program).Assembly,
        typeof(Cadence.Apps.Econsent.Program).Assembly,
        typeof(Cadence.Apps.Eisf.Program).Assembly,
        typeof(Cadence.Apps.Gateway.Program).Assembly,
        typeof(Cadence.Apps.Imaging.Program).Assembly,
        typeof(Cadence.Apps.Interop.Program).Assembly,
        typeof(Cadence.Apps.Knowledge.Program).Assembly,
        typeof(Cadence.Apps.Notifications.Program).Assembly,
        typeof(Cadence.Apps.Org.Program).Assembly,
        typeof(Cadence.Apps.Payments.Program).Assembly,
        typeof(Cadence.Apps.Safety.Program).Assembly,
        typeof(Cadence.Apps.Tickets.Program).Assembly
    ];

    /// <summary>
    /// Validates K-XS-2 (positive half): every product module references Cadence.Kernel.Abstractions to bind against
    /// canonical clinical truth. The forbidden-redefinition and Kernel.Persistence rules (K-XS-1, K-XS-2, K-MOD-1)
    /// live in tests/Architecture/KernelBoundaryTests.cs, the single source of truth for kernel boundaries.
    /// @req:PRD-SYS-053
    /// </summary>
    [Fact(DisplayName = "K_XS_2_ProductModules_MustReference_KernelAbstractions")]
    [Trait("Category", "Architecture")]
    [GxPRequirement("PRD-SYS-053")]
    public void ProductModulesMustReferenceKernelAbstractions()
    {
        foreach (var assembly in ProductModuleAssemblies)
        {
            var hasReference = HasProjectOrAssemblyReference(assembly, "Cadence.Kernel.Abstractions");

            hasReference.Should().BeTrue(
                $"Product module '{assembly.GetName().Name}' must reference Cadence.Kernel.Abstractions to bind against canonical clinical truth (K-XS-2)");
        }
    }

    private static bool HasProjectOrAssemblyReference(Assembly assembly, string targetProjectOrAssemblyName)
    {
        if (assembly.GetReferencedAssemblies().Any(a => string.Equals(a.Name, targetProjectOrAssemblyName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var assemblyName = assembly.GetName().Name;
        if (string.IsNullOrEmpty(assemblyName))
        {
            return false;
        }

        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Cadence.sln")))
        {
            currentDir = currentDir.Parent;
        }

        if (currentDir == null)
        {
            return false;
        }

        var matchingCsproj = Directory.GetFiles(currentDir.FullName, $"{assemblyName}.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (matchingCsproj != null)
        {
            var content = File.ReadAllText(matchingCsproj);
            return content.Contains(targetProjectOrAssemblyName, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsAssignableToGenericType(Type givenType, Type genericType)
    {
        var currentType = givenType;
        while (currentType != null && currentType != typeof(object))
        {
            var cur = currentType.IsGenericType ? currentType.GetGenericTypeDefinition() : currentType;
            if (genericType == cur)
                return true;
            currentType = currentType.BaseType;
        }
        return false;
    }
}

