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

using Microsoft.EntityFrameworkCore;

using Xunit;


namespace Cadence.ArchitectureTests;

public class ArchitectureSentinelTests
{
    /// <summary>
    /// Validates Hexagonal and Clean Architecture boundary purity: Cadence.Core.Domain must not reference infrastructure or web assemblies.
    /// </summary>
    [Fact(DisplayName = "DomainLayer_MustNotReference_InfrastructureOrPresentationAssemblies")]
    [Trait("Category", "Architecture")]
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
    /// </summary>
    [Fact(DisplayName = "AggregateRoots_MustImplement_IGxPAuditableContract")]
    [Trait("Category", "Architecture")]
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
    /// Validates Clean Architecture boundary purity: Core domain models, value objects, domain rules, and ports
    /// across all C# execution domain namespaces must have zero references to ASP.NET Core or MassTransit.
    /// </summary>
    [Fact(DisplayName = "ExecutionDomainLayer_MustNotReference_AspNetCoreOrMassTransit")]
    [Trait("Category", "Architecture")]
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

    /// <summary>
    /// Validates 21 CFR Part 11 § 11.10(e) / ALCOA+ audit-trail durability (#4764): no audit log, transition or
    /// history entity in any service DbContext may be the dependent side of a cascading delete, because deleting
    /// the regulated parent would then silently erase its audit trail. Every such relationship must use
    /// <see cref="DeleteBehavior.Restrict"/> (or another non-cascading behaviour).
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "AuditTrailEntities_MustNotBe_DependentOfCascadeDelete")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void AuditTrailEntitiesMustNotBeDependentOfCascadeDelete()
    {
        // Arrange
        string[] auditTrailSuffixes = ["AuditLog", "Transition", "History"];
        var dbContextTypes = ProductModuleAssemblies
            .SelectMany(GetLoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(DbContext).IsAssignableFrom(t))
            .Distinct()
            .ToList();

        dbContextTypes.Select(t => t.Name).Should().Contain(
            ["FileshareDbContext", "EtmfDbContext", "CtmsDbContext", "QualityDbContext"],
            "the sentinel must inspect every service that persists regulated audit trails");

        var auditTrailForeignKeysInspected = 0;
        var violations = new List<string>();

        // Act
        foreach (var contextType in dbContextTypes)
        {
            using var context = CreateModelOnlyDbContext(contextType);
            if (context is null)
            {
                continue;
            }

            foreach (var entityType in context.Model.GetEntityTypes())
            {
                var clrName = entityType.ClrType.Name;
                if (!auditTrailSuffixes.Any(suffix => clrName.EndsWith(suffix, StringComparison.Ordinal)))
                {
                    continue;
                }

                foreach (var foreignKey in entityType.GetForeignKeys())
                {
                    auditTrailForeignKeysInspected++;
                    if (foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade)
                    {
                        violations.Add(
                            $"{contextType.Name}: {clrName} -> {foreignKey.PrincipalEntityType.ClrType.Name} " +
                            $"({string.Join(", ", foreignKey.Properties.Select(p => p.Name))}) uses {foreignKey.DeleteBehavior}");
                    }
                }
            }
        }

        // Assert
        auditTrailForeignKeysInspected.Should().BeGreaterThan(0,
            "the sentinel must find audit log / transition / history relationships to inspect");
        string.Join(Environment.NewLine, violations).Should().BeEmpty(
            "deleting a regulated parent must never cascade-delete its audit trail (21 CFR 11.10(e), ALCOA+); use DeleteBehavior.Restrict");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    /// <summary>
    /// Builds a service DbContext on the Npgsql provider purely to materialise its EF Core model; no connection is opened.
    /// Returns null for contexts that do not expose the conventional single <c>DbContextOptions&lt;T&gt;</c> constructor.
    /// </summary>
    private static DbContext? CreateModelOnlyDbContext(Type contextType)
    {
        var optionsType = typeof(DbContextOptions<>).MakeGenericType(contextType);
        var constructor = contextType.GetConstructor([optionsType]);
        if (constructor is null)
        {
            return null;
        }

        var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(
            typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        optionsBuilder.UseNpgsql("Host=localhost;Database=cadence_model_only");

        return (DbContext)constructor.Invoke([optionsBuilder.Options]);
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
    /// live in tests/Cadence.ArchitectureTests/Architecture/KernelBoundaryTests.cs, the single source of truth for kernel boundaries.
    /// </summary>
    [Fact(DisplayName = "K_XS_2_ProductModules_MustReference_KernelAbstractions")]
    [Trait("Category", "Architecture")]
    public void ProductModulesMustReferenceKernelAbstractions()
    {
        foreach (var assembly in ProductModuleAssemblies)
        {
            var hasReference = HasProjectOrAssemblyReference(assembly, "Cadence.Kernel.Abstractions");

            hasReference.Should().BeTrue(
                $"Product module '{assembly.GetName().Name}' must reference Cadence.Kernel.Abstractions to bind against canonical clinical truth (K-XS-2)");
        }
    }

    /// <summary>
    /// #4761: every backend service authenticates callers through the signed gateway identity and requires an
    /// authenticated caller by default, and the gateway validates OIDC tokens at the edge. A new service that
    /// skips the wiring fails here instead of shipping anonymous endpoints.
    /// </summary>
    [Fact(DisplayName = "EveryService_MustEnforceGatewayAuthentication")]
    [Trait("Category", "Architecture")]
    public void EveryServiceMustEnforceGatewayAuthentication()
    {
        var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (repoRoot != null && !File.Exists(Path.Combine(repoRoot.FullName, "Cadence.sln")))
        {
            repoRoot = repoRoot.Parent;
        }

        repoRoot.Should().NotBeNull("the tests run inside the repository");

        var programs = Directory.GetFiles(Path.Combine(repoRoot!.FullName, "apps"), "Program.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        programs.Should().NotBeEmpty();

        var violations = new List<string>();
        foreach (var program in programs)
        {
            string service = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(program))!);
            string source = File.ReadAllText(program);

            if (service == "apphost")
            {
                continue;
            }

            string[] required = service == "gateway"
                ? ["AddJwtBearer(", "FallbackPolicy", "RequireAuthenticatedUser()", "UseAuthentication()", "UseAuthorization()", "AddSignedIdentityTransform"]
                : ["AddCadenceSecurity(", "UseCadenceSecurity()"];

            violations.AddRange(required.Where(call => !source.Contains(call, StringComparison.Ordinal)).Select(call => $"{service}: missing {call}"));
        }

        violations.Should().BeEmpty();
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

