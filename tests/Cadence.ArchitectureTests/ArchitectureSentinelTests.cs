using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.ArchitectureTests.Architecture;
using Cadence.Core.Domain;
using Cadence.Testing;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.ArchitectureTests;

public partial class ArchitectureSentinelTests
{
    private static readonly Lazy<IReadOnlyList<Assembly>> NonKernelProductionAssemblies =
        new(KernelBoundaryRules.LoadNonKernelCadenceAssemblies);

    private static readonly Lazy<IReadOnlyList<Assembly>> AllProductionAssemblies =
        new(KernelBoundaryRules.LoadAllProductionCadenceAssemblies);

    /// <summary>
    /// Enforces 100% solution coverage: every production <c>Cadence.*</c> project in <c>Cadence.sln</c>
    /// (excluding test and benchmark projects) must be referenced by <c>Cadence.ArchitectureTests.csproj</c>
    /// and deployed next to the architecture test binary so reflection, EF Core model, and Mono.Cecil IL
    /// sentinels never silently miss a new service, module, or package.
    /// </summary>
    [Fact(DisplayName = "SolutionProductionProjects_MustAllBeLoaded_ByArchitectureTests")]
    [Trait("Category", "Architecture")]
    public void SolutionProductionProjectsMustAllBeLoadedByArchitectureTests()
    {
        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();
        string slnContent = File.ReadAllText(Path.Combine(repoRoot, "Cadence.sln"));
        string archTestCsproj = File.ReadAllText(
            Path.Combine(repoRoot, "tests", "Cadence.ArchitectureTests", "Cadence.ArchitectureTests.csproj"));

        var deployedAssemblyPaths = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Cadence.Cli outputs AssemblyName=cadence (cadence.dll), while all other Cadence.* projects match project name.
        var missing = EvaluateSolutionProjectCoverage(
            slnContent,
            archTestCsproj,
            deployedAssemblyPaths);

        missing.Should().BeEmpty(
            "every production Cadence.* project in Cadence.sln must be referenced by tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj so architecture sentinels inspect 100% of production code");
    }

    /// <summary>
    /// Validates that adding an unreferenced production <c>Cadence.*</c> project to <c>Cadence.sln</c> fails the
    /// solution coverage sentinel, while test and benchmark projects are ignored.
    /// </summary>
    [Fact(DisplayName = "SolutionProjectCoverage_Bites_WhenProductionProjectIsMissingReference")]
    [Trait("Category", "Architecture")]
    public void SolutionProjectCoverageBitesWhenProductionProjectIsMissingReference()
    {
        const string syntheticSln = """
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Cadence.Modules.Execution", "apps\execution\module\Cadence.Modules.Execution.csproj", "{11111111-1111-1111-1111-111111111111}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Cadence.Apps.NewService", "apps\new-service\src\Cadence.Apps.NewService.csproj", "{22222222-2222-2222-2222-222222222222}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Cadence.UnitTests", "tests\Cadence.UnitTests\Cadence.UnitTests.csproj", "{33333333-3333-3333-3333-333333333333}"
            EndProject
            """;

        const string syntheticArchCsproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\..\apps\execution\module\Cadence.Modules.Execution.csproj" />
              </ItemGroup>
            </Project>
            """;

        var missing = EvaluateSolutionProjectCoverage(
            syntheticSln,
            syntheticArchCsproj,
            new HashSet<string>(["Cadence.Modules.Execution"], StringComparer.OrdinalIgnoreCase));

        missing.Should().Equal("Cadence.Apps.NewService (not referenced in Cadence.ArchitectureTests.csproj)");
    }

    internal static IReadOnlyList<string> EvaluateSolutionProjectCoverage(
        string slnContent,
        string architectureTestsCsprojContent,
        IReadOnlySet<string> deployedBinaryNames)
    {
        var violations = new List<string>();

        foreach (Match match in SolutionProjectRegex().Matches(slnContent))
        {
            string projectName = match.Groups["name"].Value;
            if (!projectName.StartsWith("Cadence.", StringComparison.Ordinal) ||
                KernelBoundaryRules.NonProductionProjectNames.Contains(projectName))
            {
                continue;
            }

            if (!architectureTestsCsprojContent.Contains($"{projectName}.csproj", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{projectName} (not referenced in Cadence.ArchitectureTests.csproj)");
                continue;
            }

            string expectedBinaryName = string.Equals(projectName, "Cadence.Cli", StringComparison.Ordinal)
                ? "cadence"
                : projectName;
            if (!deployedBinaryNames.Contains(expectedBinaryName))
            {
                violations.Add($"{projectName} (binary '{expectedBinaryName}.dll' missing from AppContext.BaseDirectory)");
            }
        }

        violations.Sort(StringComparer.Ordinal);
        return violations;
    }

    /// <summary>
    /// Validates Hexagonal and Clean Architecture boundary purity: Cadence.Core.Domain must not reference infrastructure or web assemblies.
    /// </summary>
    [Fact(DisplayName = "DomainLayer_MustNotReference_InfrastructureOrPresentationAssemblies")]
    [Trait("Category", "Architecture")]
    public void DomainLayerMustNotReferenceInfrastructureOrPresentationAssemblies()
    {
        var domainAssembly = typeof(AggregateRoot<>).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        referencedAssemblies.Should().NotContain(name => name.Contains("EntityFrameworkCore", StringComparison.OrdinalIgnoreCase),
            "Domain core must remain pure and free from ORM/infrastructure dependencies");
        referencedAssemblies.Should().NotContain(name => name.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase),
            "Domain core must remain pure and free from ASP.NET Core presentation dependencies");
        referencedAssemblies.Should().NotContain(name => name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase),
            "Domain core must not reference database provider drivers directly");
    }

    /// <summary>
    /// Validates 21 CFR Part 11 regulatory invariant: All DDD aggregate roots across every production assembly
    /// (dynamically discovered across non-kernel and kernel assemblies) must implement IGxPAuditable.
    /// </summary>
    [Fact(DisplayName = "AggregateRoots_MustImplement_IGxPAuditableContract")]
    [Trait("Category", "Architecture")]
    public void AggregateRootsMustImplementIGxPAuditableContract()
    {
        AllProductionAssemblies.Value.Select(a => a.GetName().Name).Should().Contain(
            ["Cadence.Modules.Execution", "Cadence.Kernel.Host", "Cadence.Kernel.Domain", "cadence"],
            "100% of production assemblies must be loaded before scanning aggregate roots");

        var aggregateRootTypes = AllProductionAssemblies.Value
            .SelectMany(KernelBoundaryRules.GetLoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(AggregateRootBase).IsAssignableFrom(t) || IsAssignableToGenericType(t, typeof(AggregateRoot<>)))
            .Distinct()
            .ToList();

        aggregateRootTypes.Should().NotBeEmpty("Production assemblies must contain concrete aggregate root types");

        foreach (var type in aggregateRootTypes)
        {
            typeof(IGxPAuditable).IsAssignableFrom(type).Should().BeTrue(
                $"Aggregate root type '{type.FullName}' in '{type.Assembly.GetName().Name}' must implement IGxPAuditable to enforce 21 CFR Part 11 audit trails");
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
        var executionAssembly = typeof(ClinicalSubject).Assembly;
        var domainTypes = executionAssembly.GetTypes()
            .Where(t => t.Namespace != null &&
                        t.Namespace.StartsWith("Cadence.Apps.Execution.Domain", StringComparison.Ordinal))
            // MIG-SUBJ-010 explicit exception: DomainErrorHttpMapping and DomainErrorExtensions serve as the
            // isolated ProblemDetails HTTP mapping bridge at the boundary.
            .Where(t => t != typeof(DomainErrorHttpMapping) && t != typeof(DomainErrorExtensions))
            .ToList();

        domainTypes.Should().NotBeEmpty("Domain layer must contain discovered types");

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

        typeof(ClinicalSubject).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Should().NotContain(t => (t.Namespace ?? "").StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                                      (t.Namespace ?? "").StartsWith("MassTransit", StringComparison.Ordinal),
                "ClinicalSubject signatures must remain completely pure of web or messaging infrastructure");
    }

    /// <summary>
    /// Validates 21 CFR Part 11 § 11.10(e) / ALCOA+ audit-trail durability (#4764): no audit log, transition, history,
    /// revision, or period entity in any dynamically discovered non-kernel DbContext (including Cadence.Modules.Execution
    /// and Cadence.Kernel.Host) may be the dependent side of a cascading delete.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "AuditTrailEntities_MustNotBe_DependentOfCascadeDelete")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void AuditTrailEntitiesMustNotBeDependentOfCascadeDelete()
    {
        string[] auditTrailSuffixes = ["AuditLog", "Transition", "History", "Revision", "Period"];
        var dbContextTypes = NonKernelProductionAssemblies.Value
            .SelectMany(KernelBoundaryRules.GetLoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(DbContext).IsAssignableFrom(t))
            .Distinct()
            .ToList();

        dbContextTypes.Select(t => t.Name).Should().Contain(
            ["FileshareDbContext", "EtmfDbContext", "CtmsDbContext", "QualityDbContext", "ExecutionModuleDbContext"],
            "the sentinel must dynamically inspect every non-kernel persistence module including ExecutionModuleDbContext");

        var auditTrailForeignKeysInspected = 0;
        var violations = new List<string>();

        foreach (var contextType in dbContextTypes)
        {
            using var context = CreateModelOnlyDbContext(contextType);
            context.Should().NotBeNull(
                $"{contextType.FullName} must expose a (DbContextOptions<T>) or (DbContextOptions<T>, IFieldEncryptor) constructor so cascade-delete audit sentinels inspect 100% of persistence models");

            foreach (var entityType in context!.Model.GetEntityTypes())
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

        auditTrailForeignKeysInspected.Should().BeGreaterThan(0,
            "the sentinel must find audit log / transition / history / revision / period relationships to inspect");
        string.Join(Environment.NewLine, violations).Should().BeEmpty(
            "deleting a regulated parent must never cascade-delete its audit trail (21 CFR 11.10(e), ALCOA+); use DeleteBehavior.Restrict");
    }

    /// <summary>
    /// Verifies that all standalone tenant-scoped domain tables across the 15 standalone DbContext schemas enforce
    /// optimistic concurrency tokens (<c>VersionIndex</c> or shadow <c>_version</c>) and that contestable clinical,
    /// delegation, safety, document, and operational ticket entities map PostgreSQL <c>tstzrange</c> bitemporal
    /// period tables or numbered immutable revision tables with <see cref="DeleteBehavior.Restrict"/>.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "StandaloneDomainTables_MustEnforce_ConcurrencyTokens_And_ContestableHistory")]
    [Trait("Category", "GxP_OQ")]
    [GxPRequirement("PRD-SYS-001")]
    public void StandaloneDomainTablesMustEnforceConcurrencyTokensAndContestableHistory()
    {
        string[] expectedStandaloneContextNames =
        [
            "CtmsDbContext",
            "EconsentDbContext",
            "EisfDbContext",
            "EtmfDbContext",
            "ExecutionDbContext",
            "FileshareDbContext",
            "ImagingDbContext",
            "InteropDbContext",
            "KnowledgeDbContext",
            "NotificationsDbContext",
            "OrgDbContext",
            "PaymentsDbContext",
            "QualityDbContext",
            "SafetyDbContext",
            "TicketsDbContext",
        ];

        var standaloneContexts = NonKernelProductionAssemblies.Value
            .SelectMany(KernelBoundaryRules.GetLoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && expectedStandaloneContextNames.Contains(t.Name))
            .Distinct()
            .ToList();

        standaloneContexts.Select(t => t.Name).Should().BeEquivalentTo(expectedStandaloneContextNames);

        int standaloneTableCount = 0;
        var missingConcurrencyTokens = new List<string>();
        var discoveredPeriodTables = new Dictionary<string, (string? ValidType, string? RecordedType)>(StringComparer.Ordinal);
        var discoveredRevisionTables = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contextType in standaloneContexts)
        {
            using var context = CreateModelOnlyDbContext(contextType);
            context.Should().NotBeNull($"{contextType.Name} must expose a single DbContextOptions<T> constructor");

            foreach (var entityType in context!.Model.GetEntityTypes())
            {
                if (entityType.IsOwned()
                    || entityType.BaseType is not null
                    || entityType.HasSharedClrType
                    || entityType.FindPrimaryKey() is null
                    || entityType.GetViewName() is not null
                    || entityType.ClrType == typeof(Dictionary<string, object>))
                {
                    continue;
                }

                string? tableName = entityType.GetTableName();
                if (tableName is null || Cadence.Database.Tenancy.StandaloneTenantModelExtensions.NonTenantTableNames.Contains(tableName))
                {
                    continue;
                }

                standaloneTableCount++;
                bool hasToken = entityType.GetProperties().Any(p => p.IsConcurrencyToken);
                if (!hasToken)
                {
                    missingConcurrencyTokens.Add($"{contextType.Name}.{entityType.ClrType.Name} ({tableName})");
                }

                var validProp = entityType.FindProperty("ValidPeriod");
                var recordedProp = entityType.FindProperty("RecordedPeriod");
                if (validProp is not null || recordedProp is not null)
                {
                    discoveredPeriodTables[tableName] = (validProp?.GetColumnType(), recordedProp?.GetColumnType());
                }

                if (tableName.EndsWith("_revisions", StringComparison.Ordinal) || tableName == "knowledge_article_versions")
                {
                    discoveredRevisionTables.Add(tableName);
                }
            }
        }

        standaloneTableCount.Should().BeGreaterThanOrEqualTo(134,
            "all 134 baseline standalone domain tables plus new bitemporal/revision tables must be inspected");
        string.Join(Environment.NewLine, missingConcurrencyTokens).Should().BeEmpty(
            "every standalone domain table must enforce an optimistic concurrency token (VersionIndex or _version)");

        string[] expectedBitemporalTables =
        [
            "ctms_delegation_periods",
            "doa_delegation_periods",
            "site_activation_periods",
            "delegation_of_authority_periods",
            "delegation_record_periods",
        ];
        discoveredPeriodTables.Keys.Should().Contain(expectedBitemporalTables);
        foreach (string periodTable in expectedBitemporalTables)
        {
            discoveredPeriodTables[periodTable].ValidType.Should().Be("tstzrange");
            discoveredPeriodTables[periodTable].RecordedType.Should().Be("tstzrange");
        }

        discoveredRevisionTables.Should().Contain(
        [
            "ticket_revisions",
            "periodic_safety_report_revisions",
            "safety_narrative_revisions",
            "tmf_document_revisions",
            "consent_document_revisions",
            "eisf_document_revisions",
            "knowledge_article_versions",
        ]);
    }

    /// <summary>
    /// Builds a service DbContext on the Npgsql provider purely to materialise its EF Core model; no connection is opened.
    /// Supports both <c>(DbContextOptions&lt;T&gt;)</c> and <c>(DbContextOptions&lt;T&gt;, IFieldEncryptor)</c> constructors.
    /// </summary>
    private static DbContext? CreateModelOnlyDbContext(Type contextType)
    {
        var optionsType = typeof(DbContextOptions<>).MakeGenericType(contextType);
        var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(
            typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        optionsBuilder.UseNpgsql("Host=localhost;Database=cadence_model_only");

        var encryptedCtor = contextType.GetConstructor([optionsType, typeof(Cadence.Security.FieldProtection.IFieldEncryptor)]);
        if (encryptedCtor is not null)
        {
            return (DbContext)encryptedCtor.Invoke([optionsBuilder.Options, Cadence.Security.FieldProtection.DesignTimeFieldEncryptor.Instance]);
        }

        var constructor = contextType.GetConstructor([optionsType]);
        return constructor is null
            ? null
            : (DbContext)constructor.Invoke([optionsBuilder.Options]);
    }

    /// <summary>
    /// Dynamically discovers every product module and host service assembly (<c>Cadence.Apps.*</c>,
    /// <c>Cadence.Modules.*</c>, and <c>Cadence.Kernel.Host</c>).
    /// </summary>
    private static IEnumerable<Assembly> DiscoverProductModuleAssemblies() =>
        NonKernelProductionAssemblies.Value.Where(assembly =>
        {
            string name = assembly.GetName().Name ?? string.Empty;
            return name.StartsWith("Cadence.Apps.", StringComparison.Ordinal)
                   || name.StartsWith("Cadence.Modules.", StringComparison.Ordinal)
                   || name.Equals(KernelBoundaryRules.KernelHostAssemblyName, StringComparison.Ordinal);
        });

    /// <summary>
    /// Validates K-XS-2 (positive half): every product module and service references Cadence.Kernel.Abstractions to bind
    /// against canonical clinical truth.
    /// </summary>
    [Fact(DisplayName = "K_XS_2_ProductModules_MustReference_KernelAbstractions")]
    [Trait("Category", "Architecture")]
    public void ProductModulesMustReferenceKernelAbstractions()
    {
        var productAssemblies = DiscoverProductModuleAssemblies().ToList();
        productAssemblies.Should().NotBeEmpty();

        foreach (var assembly in productAssemblies)
        {
            var hasReference = HasProjectOrAssemblyReference(assembly, "Cadence.Kernel.Abstractions");

            hasReference.Should().BeTrue(
                $"Product module '{assembly.GetName().Name}' must reference Cadence.Kernel.Abstractions to bind against canonical clinical truth (K-XS-2)");
        }
    }

    /// <summary>
    /// #4761: every backend service authenticates callers through the signed gateway identity and requires an
    /// authenticated caller by default, and the gateway validates OIDC tokens at the edge.
    /// </summary>
    [Fact(DisplayName = "EveryService_MustEnforceGatewayAuthentication")]
    [Trait("Category", "Architecture")]
    public void EveryServiceMustEnforceGatewayAuthentication()
    {
        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();

        var programs = Directory.GetFiles(Path.Combine(repoRoot, "apps"), "Program.cs", SearchOption.AllDirectories)
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

        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();
        var matchingCsproj = Directory.GetFiles(repoRoot, $"{assemblyName}.csproj", SearchOption.AllDirectories)
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

    [GeneratedRegex(@"Project\(""\{[^""]+\}""\)\s*=\s*""(?<name>[^""]+)""\s*,\s*""(?<path>[^""]+\.csproj)""", RegexOptions.CultureInvariant)]
    private static partial Regex SolutionProjectRegex();
}
