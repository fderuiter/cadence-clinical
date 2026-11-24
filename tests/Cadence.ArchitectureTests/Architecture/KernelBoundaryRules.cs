using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Single source of truth for the Clinical Kernel boundary rules of
/// docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md §2 and §9 (K-XS-1, K-XS-2, K-MOD-1).
/// Every rule is a pure function from its inputs (assemblies, types, csproj contents) to a sorted list of
/// violation keys of the form <c>RULE|Assembly|Type[.Member]</c>, so the same function evaluates both
/// the real product assemblies and the synthetic fixtures that prove the rules bite (spec §14 scenario 7).
/// </summary>
public static partial class KernelBoundaryRules
{
    public const string KXs1 = "K-XS-1";
    public const string KXs2 = "K-XS-2";
    public const string KMod1 = "K-MOD-1";

    public const string KernelPersistenceAssemblyName = "Cadence.Kernel.Persistence";
    public const string KernelHostAssemblyName = "Cadence.Kernel.Host";

    /// <summary>
    /// The kernel assemblies a product module may not reference (K-XS-2, ADR-2600). Modules bind to
    /// <c>Cadence.Kernel.Abstractions</c> and the module SDK <c>Cadence.Kernel.Modules</c> only.
    /// </summary>
    public static readonly IReadOnlyList<string> KernelInternalAssemblyNames =
    [
        "Cadence.Kernel.Api",
        "Cadence.Kernel.Application",
        "Cadence.Kernel.Domain",
        KernelPersistenceAssemblyName,
    ];

    /// <summary>
    /// K-XS-1: type names only the kernel may declare (spec §2), plus the CTMS-specific duplicates
    /// (<c>CtmsStudy</c>, <c>SiteStaffMember</c>) already banned by the #4723 sentinel this rule replaces.
    /// </summary>
    public static readonly IReadOnlySet<string> ForbiddenTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Study", "Site", "Participant", "Subject", "Observation", "Organization", "Person", "Role", "Delegation",
        "CtmsStudy", "SiteStaffMember",
    };

    /// <summary>K-XS-1: module-local audit tables (<c>CtmsAuditLog</c>, <c>OrgAuditLog</c>, …) are forbidden.</summary>
    public const string ForbiddenTypeNameSuffix = "AuditLog";

    /// <summary>K-XS-1 / K-MOD-1: member names that carry a central kernel identity.</summary>
    public static readonly IReadOnlyList<string> KernelIdMemberSuffixes =
    [
        "StudyId", "SiteId", "ParticipantId", "SubjectId", "TenantId",
    ];

    /// <summary>Test and benchmark projects in Cadence.sln that are not production runtime/tooling assemblies.</summary>
    public static readonly IReadOnlySet<string> NonProductionProjectNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Cadence.ArchitectureTests",
        "Cadence.UnitTests",
        "Cadence.IntegrationTests",
        "Cadence.E2ETests",
        "Cadence.TestSupport",
        "Cadence.Kernel.ConsumerTests",
        "Cadence.Rules.Benchmarks",
    };

    /// <summary>
    /// Build-time developer tools that read or migrate the kernel's EF model out of process. They are never deployed
    /// and no application references them, so they may reference Kernel.Persistence like the tests do.
    /// </summary>
    private static readonly string[] ToolingAssemblyNames = ["Cadence.DatabaseMigrator", "Cadence.SchemaDocExporter"];

    /// <summary>
    /// True for any library assembly that belongs to the kernel (<c>Cadence.Kernel.*</c>, excluding the
    /// <c>Cadence.Kernel.Host</c> composition host app so host types remain subject to cascade-delete, audit, and
    /// boundary sentinels while receiving an explicit <see cref="IsExemptFromKXs2"/> exemption).
    /// </summary>
    public static bool IsKernelAssembly(string? assemblyName) =>
        assemblyName is not null &&
        !assemblyName.Equals(KernelHostAssemblyName, StringComparison.Ordinal) &&
        (assemblyName.Equals("Cadence.Kernel", StringComparison.Ordinal) ||
         assemblyName.StartsWith("Cadence.Kernel.", StringComparison.Ordinal));

    /// <summary>
    /// Every <c>Cadence.*</c> production assembly (including <c>cadence.dll</c> from <c>Cadence.Cli</c>) deployed next
    /// to the test binaries, excluding the kernel libraries and test/benchmark assemblies. Coverage is enforced across
    /// 100% of <c>Cadence.sln</c> by
    /// <c>ArchitectureSentinelTests.SolutionProductionProjectsMustAllBeLoadedByArchitectureTests</c>.
    /// </summary>
    public static IReadOnlyList<Assembly> LoadNonKernelCadenceAssemblies()
    {
        return EnumerateDeployedCadenceAssemblyPaths()
            .Select(AssemblyName.GetAssemblyName)
            .Where(name => name.Name is not null
                           && !IsKernelAssembly(name.Name)
                           && !NonProductionProjectNames.Contains(name.Name))
            .OrderBy(name => name.Name, StringComparer.Ordinal)
            .Select(Assembly.Load)
            .ToList();
    }

    /// <summary>
    /// Every production <c>Cadence.*</c> assembly (both kernel and non-kernel, including <c>cadence.dll</c>) deployed
    /// next to the test binaries.
    /// </summary>
    public static IReadOnlyList<Assembly> LoadAllProductionCadenceAssemblies()
    {
        return EnumerateDeployedCadenceAssemblyPaths()
            .Select(AssemblyName.GetAssemblyName)
            .Where(name => name.Name is not null && !NonProductionProjectNames.Contains(name.Name))
            .OrderBy(name => name.Name, StringComparer.Ordinal)
            .Select(Assembly.Load)
            .ToList();
    }

    private static IEnumerable<string> EnumerateDeployedCadenceAssemblyPaths()
    {
        foreach (string path in Directory.GetFiles(AppContext.BaseDirectory, "Cadence.*.dll"))
        {
            yield return path;
        }

        string cliDllPath = Path.Combine(AppContext.BaseDirectory, "cadence.dll");
        if (File.Exists(cliDllPath))
        {
            yield return cliDllPath;
        }
    }

    // ---------------------------------------------------------------- K-XS-1

    /// <summary>
    /// K-XS-1 (spec §2): no assembly outside <c>Cadence.Kernel.*</c> declares a type named
    /// Study|Site|Participant|Subject|Observation|Organization|Person|Role|Delegation or <c>*AuditLog</c>,
    /// a public property/field of type <c>string</c> named <c>*StudyId|*SiteId|*ParticipantId|*SubjectId|*TenantId</c>,
    /// or a <see cref="JsonPropertyNameAttribute"/> wire alias for those central identities on a non-kernel member type.
    /// </summary>
    public static IReadOnlyList<string> EvaluateKXs1(IEnumerable<Assembly> assemblies)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in assemblies.Where(a => !IsKernelAssembly(a.GetName().Name)))
        {
            var assemblyName = assembly.GetName().Name!;

            foreach (var type in GetLoadableTypes(assembly).Where(t => !IsCompilerGenerated(t)))
            {
                var simpleName = StripGenericArity(type.Name);
                if (ForbiddenTypeNames.Contains(simpleName) ||
                    simpleName.EndsWith(ForbiddenTypeNameSuffix, StringComparison.Ordinal))
                {
                    violations.Add(Key(KXs1, assemblyName, TypeName(type)));
                }

                const BindingFlags publicDeclared =
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

                var properties = type.GetProperties(publicDeclared);
                var fields = type.GetFields(publicDeclared).Where(f => !f.IsLiteral).ToArray();

                var stringIdMembers = properties
                    .Where(p => p.PropertyType == typeof(string))
                    .Select(p => p.Name)
                    .Concat(fields
                        // A const string is a name (header, claim type, route key), never an ID value.
                        .Where(f => f.FieldType == typeof(string))
                        .Select(f => f.Name))
                    .Where(IsKernelIdMemberName);

                foreach (var member in stringIdMembers)
                {
                    violations.Add(Key(KXs1, assemblyName, $"{TypeName(type)}.{member}"));
                }

                foreach (var property in properties)
                {
                    if (!IsKernelDefinedType(property.PropertyType) &&
                        HasForbiddenWireAlias(property.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: false)))
                    {
                        violations.Add(Key(KXs1, assemblyName, $"{TypeName(type)}.{property.Name}"));
                    }
                }

                foreach (var field in fields)
                {
                    if (!IsKernelDefinedType(field.FieldType) &&
                        HasForbiddenWireAlias(field.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: false)))
                    {
                        violations.Add(Key(KXs1, assemblyName, $"{TypeName(type)}.{field.Name}"));
                    }
                }

                foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    foreach (var parameter in ctor.GetParameters())
                    {
                        if (parameter.Name is null ||
                            IsKernelDefinedType(parameter.ParameterType) ||
                            !HasForbiddenWireAlias(parameter.GetCustomAttribute<JsonPropertyNameAttribute>(inherit: false)))
                        {
                            continue;
                        }

                        var matchingProperty = properties.FirstOrDefault(
                            p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase));
                        string memberName = matchingProperty?.Name ?? parameter.Name;
                        violations.Add(Key(KXs1, assemblyName, $"{TypeName(type)}.{memberName}"));
                    }
                }
            }
        }

        return [.. violations];
    }

    // ---------------------------------------------------------------- K-XS-2

    /// <summary>
    /// K-XS-2 applies to product modules, standalone services, and shared packages. The kernel libraries themselves,
    /// the composition host <c>Cadence.Kernel.Host</c> (ADR-2601), the test/benchmark projects, and the build-time
    /// schema/migration tools may reference kernel internals.
    /// </summary>
    public static bool IsExemptFromKXs2(string? name) =>
        name is not null &&
        (IsKernelAssembly(name)
         || name.Equals(KernelHostAssemblyName, StringComparison.Ordinal)
         || NonProductionProjectNames.Contains(name)
         || ToolingAssemblyNames.Contains(name, StringComparer.Ordinal));

    /// <summary>
    /// K-XS-2 (spec §2/§9, ADR-2600), compiled form: no non-exempt assembly references a kernel-internal assembly
    /// (<see cref="KernelInternalAssemblyNames"/>).
    /// </summary>
    public static IReadOnlyList<string> EvaluateKXs2(IEnumerable<Assembly> assemblies)
    {
        return assemblies
            .Where(a => !IsExemptFromKXs2(a.GetName().Name))
            .SelectMany(a => a.GetReferencedAssemblies()
                .Select(r => r.Name)
                .Where(r => KernelInternalAssemblyNames.Contains(r, StringComparer.OrdinalIgnoreCase))
                .Select(r => Key(KXs2, a.GetName().Name!, r!)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// K-XS-2 (spec §2/§9, ADR-2600), source form: no project outside the kernel (except <c>Cadence.Kernel.Host</c>
    /// and exempted tooling/tests) declares a Project/Package/Reference item pointing at a kernel-internal assembly
    /// (<see cref="KernelInternalAssemblyNames"/>).
    /// </summary>
    public static IReadOnlyList<string> EvaluateKXs2Projects(IEnumerable<(string ProjectName, string CsprojContent)> projects)
    {
        var reference = new Regex(
            @"<(ProjectReference|PackageReference|Reference)\b[^>]*Include\s*=\s*""[^""]*(?<assembly>Cadence\.Kernel\.(Persistence|Application|Domain|Api))\b[^""]*""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        return projects
            .Where(p => !IsExemptFromKXs2(p.ProjectName))
            .SelectMany(p => reference.Matches(p.CsprojContent)
                .Select(m => KernelInternalAssemblyNames.Single(n => n.Equals(m.Groups["assembly"].Value, StringComparison.OrdinalIgnoreCase)))
                .Select(n => Key(KXs2, p.ProjectName, n)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>All *.csproj files in the repository (excluding build output), as (project name, content).</summary>
    public static IEnumerable<(string ProjectName, string CsprojContent)> LoadRepositoryProjects(string repositoryRoot)
    {
        var pending = new Stack<string>([repositoryRoot]);
        var projectFiles = new List<string>();
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            projectFiles.AddRange(Directory.EnumerateFiles(directory, "*.csproj"));
            foreach (var child in Directory.EnumerateDirectories(directory)
                         .Where(child => !ExcludedScanDirectories.Contains(Path.GetFileName(child))))
            {
                pending.Push(child);
            }
        }

        return projectFiles
            .Order(StringComparer.Ordinal)
            .Select(path => (Path.GetFileNameWithoutExtension(path), File.ReadAllText(path)));
    }

    private static readonly HashSet<string> ExcludedScanDirectories = new(StringComparer.Ordinal)
    {
        "bin", "obj", "node_modules", ".claude", ".git", ".venv",
    };

    // ---------------------------------------------------------------- K-MOD-1

    /// <summary>
    /// K-MOD-1 (spec §9): entity types mapped by module DbContexts have no <c>string</c> or <c>Guid</c> column named
    /// <c>*StudyId|*SiteId|*ParticipantId|*SubjectId|*TenantId</c>; those columns must use kernel ID types. Columns are
    /// read from the built EF Core model (so shadow properties count), instantiated over Npgsql without opening any
    /// network connection so PostgreSQL-specific column types (such as <c>tstzrange</c> on bitemporal module entities)
    /// materialize cleanly.
    /// </summary>
    public static IReadOnlyList<string> EvaluateKMod1(IEnumerable<Assembly> assemblies)
    {
        var contextTypes = assemblies
            .Where(a => !IsKernelAssembly(a.GetName().Name))
            .SelectMany(GetLoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(DbContext).IsAssignableFrom(t));

        return EvaluateKMod1(contextTypes);
    }

    /// <inheritdoc cref="EvaluateKMod1(IEnumerable{Assembly})"/>
    public static IReadOnlyList<string> EvaluateKMod1(IEnumerable<Type> dbContextTypes)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var contextType in dbContextTypes)
        {
            var assemblyName = contextType.Assembly.GetName().Name!;
            using var context = CreateModelOnlyContext(contextType);

            foreach (var entityType in context.Model.GetEntityTypes())
            {
                var entityName = entityType.HasSharedClrType ? entityType.Name : TypeName(entityType.ClrType);

                foreach (IProperty property in entityType.GetDeclaredProperties())
                {
                    var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                    if ((clrType == typeof(string) || clrType == typeof(Guid)) && IsKernelIdMemberName(property.Name))
                    {
                        violations.Add(Key(KMod1, assemblyName, $"{entityName}.{property.Name}"));
                    }
                }
            }
        }

        return [.. violations];
    }

    private static DbContext CreateModelOnlyContext(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseNpgsql("Host=localhost;Database=k_mod_1_model_only");

        // Contexts with encrypted columns (#4770) also take an IFieldEncryptor; only the model is read here, so the
        // design-time encryptor (which never converts values) is enough.
        object[] arguments = contextType.GetConstructor([builder.Options.GetType(), typeof(IFieldEncryptor)]) is not null
            ? [builder.Options, DesignTimeFieldEncryptor.Instance]
            : [builder.Options];

        return (DbContext)(Activator.CreateInstance(contextType, arguments)
            ?? throw new InvalidOperationException(
                $"K-MOD-1 cannot instantiate {contextType.FullName}; it must expose a (DbContextOptions<T>) or (DbContextOptions<T>, IFieldEncryptor) constructor."));
    }

    // ---------------------------------------------------------------- helpers

    public static bool IsKernelIdMemberName(string memberName) =>
        KernelIdMemberSuffixes.Any(suffix => memberName.EndsWith(suffix, StringComparison.Ordinal));

    /// <summary>
    /// True when a serialized JSON property name aliases a central kernel identity (<c>Study</c>, <c>Site</c>,
    /// <c>Participant</c>, <c>Subject</c>, or <c>Tenant</c> ID in camelCase, PascalCase, snake_case, or kebab-case)
    /// without false-matching unrelated English words such as <c>compositeId</c> or <c>websiteId</c>.
    /// </summary>
    public static bool IsKernelWireIdAlias(string? wireName) =>
        !string.IsNullOrWhiteSpace(wireName) && KernelWireIdAliasRegex().IsMatch(wireName.Trim());

    private static bool HasForbiddenWireAlias(JsonPropertyNameAttribute? attribute) =>
        attribute is not null && IsKernelWireIdAlias(attribute.Name);

    private static bool IsKernelDefinedType(Type type)
    {
        var unwrapped = Nullable.GetUnderlyingType(type) ?? type;
        return IsKernelAssembly(unwrapped.Assembly.GetName().Name);
    }

    private static string Key(string rule, string assemblyName, string target) => $"{rule}|{assemblyName}|{target}";

    private static string TypeName(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string StripGenericArity(string name)
    {
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal) ||
        type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false);

    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }

    [GeneratedRegex(
        @"^(?:(?:^|.*[_-])(?i:study|site|participant|subject|tenant)|.*[a-z0-9](?:Study|Site|Participant|Subject|Tenant))[_-]?(?i:id)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex KernelWireIdAliasRegex();
}
