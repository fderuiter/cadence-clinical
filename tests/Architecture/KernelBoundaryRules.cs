using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

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
public static class KernelBoundaryRules
{
    public const string KXs1 = "K-XS-1";
    public const string KXs2 = "K-XS-2";
    public const string KMod1 = "K-MOD-1";

    public const string KernelPersistenceAssemblyName = "Cadence.Kernel.Persistence";

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

    /// <summary>K-XS-1 / K-MOD-1: member names that carry a kernel identity.</summary>
    public static readonly IReadOnlyList<string> KernelIdMemberSuffixes = ["StudyId", "SiteId", "ParticipantId"];

    private static readonly string[] TestAssemblyNames =
    [
        "Cadence.ArchitectureTests", "Cadence.UnitTests", "Cadence.E2ETests",
    ];

    /// <summary>True for any assembly that belongs to the kernel (<c>Cadence.Kernel.*</c>).</summary>
    public static bool IsKernelAssembly(string? assemblyName) =>
        assemblyName is not null &&
        (assemblyName.Equals("Cadence.Kernel", StringComparison.Ordinal) ||
         assemblyName.StartsWith("Cadence.Kernel.", StringComparison.Ordinal));

    /// <summary>
    /// Every <c>Cadence.*</c> production assembly deployed next to the test binaries, excluding the kernel
    /// and the test assemblies themselves. Coverage is therefore bounded by the ProjectReferences of
    /// Cadence.ArchitectureTests.csproj.
    /// </summary>
    public static IReadOnlyList<Assembly> LoadNonKernelCadenceAssemblies()
    {
        return Directory.GetFiles(AppContext.BaseDirectory, "Cadence.*.dll")
            .Select(path => AssemblyName.GetAssemblyName(path))
            .Where(name => !IsKernelAssembly(name.Name) && !TestAssemblyNames.Contains(name.Name, StringComparer.Ordinal))
            .OrderBy(name => name.Name, StringComparer.Ordinal)
            .Select(Assembly.Load)
            .ToList();
    }

    // ---------------------------------------------------------------- K-XS-1

    /// <summary>
    /// K-XS-1 (spec §2): no assembly outside <c>Cadence.Kernel.*</c> declares a type named
    /// Study|Site|Participant|Subject|Observation|Organization|Person|Role|Delegation or <c>*AuditLog</c>,
    /// or a public property/field of type <c>string</c> named <c>*StudyId|*SiteId|*ParticipantId</c>.
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

                var stringIdMembers = type.GetProperties(publicDeclared)
                    .Where(p => p.PropertyType == typeof(string))
                    .Select(p => p.Name)
                    .Concat(type.GetFields(publicDeclared)
                        // A const string is a name (header, claim type, route key), never an ID value.
                        .Where(f => f.FieldType == typeof(string) && !f.IsLiteral)
                        .Select(f => f.Name))
                    .Where(IsKernelIdMemberName);

                foreach (var member in stringIdMembers)
                {
                    violations.Add(Key(KXs1, assemblyName, $"{TypeName(type)}.{member}"));
                }
            }
        }

        return [.. violations];
    }

    // ---------------------------------------------------------------- K-XS-2

    /// <summary>
    /// K-XS-2 applies to product modules and packages. The kernel itself and the test projects (which verify the
    /// kernel model and migrations) may reference Kernel.Persistence.
    /// </summary>
    private static bool IsExemptFromKXs2(string? name) =>
        IsKernelAssembly(name) || TestAssemblyNames.Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// K-XS-2 (spec §2/§9), compiled form: no non-kernel assembly references <c>Cadence.Kernel.Persistence</c>.
    /// </summary>
    public static IReadOnlyList<string> EvaluateKXs2(IEnumerable<Assembly> assemblies)
    {
        return assemblies
            .Where(a => !IsExemptFromKXs2(a.GetName().Name))
            .Where(a => a.GetReferencedAssemblies().Any(r =>
                string.Equals(r.Name, KernelPersistenceAssemblyName, StringComparison.OrdinalIgnoreCase)))
            .Select(a => Key(KXs2, a.GetName().Name!, KernelPersistenceAssemblyName))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// K-XS-2 (spec §2/§9), source form: no project outside the kernel declares a Project/Package/Reference
    /// item pointing at <c>Cadence.Kernel.Persistence</c>. Takes (project name, csproj XML) pairs so it can be
    /// fed both the repository's real project files and synthetic ones.
    /// </summary>
    public static IReadOnlyList<string> EvaluateKXs2Projects(IEnumerable<(string ProjectName, string CsprojContent)> projects)
    {
        var reference = new Regex(
            @"<(ProjectReference|PackageReference|Reference)\b[^>]*Include\s*=\s*""[^""]*Cadence\.Kernel\.Persistence\b[^""]*""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        return projects
            .Where(p => !IsExemptFromKXs2(p.ProjectName) && reference.IsMatch(p.CsprojContent))
            .Select(p => Key(KXs2, p.ProjectName, KernelPersistenceAssemblyName))
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
    /// K-MOD-1 (spec §9), initial form: entity types mapped by module DbContexts have no <c>string</c> or
    /// <c>Guid</c> column named <c>*StudyId|*SiteId|*ParticipantId</c>; those columns must use kernel ID types.
    /// Columns are read from the built EF Core model (so shadow properties count), with each DbContext
    /// instantiated over the InMemory provider.
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
            using var context = CreateInMemoryContext(contextType);

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

    private static DbContext CreateInMemoryContext(Type contextType)
    {
        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseInMemoryDatabase($"k-mod-1-{contextType.FullName}");

        return (DbContext)(Activator.CreateInstance(contextType, builder.Options)
            ?? throw new InvalidOperationException(
                $"K-MOD-1 cannot instantiate {contextType.FullName}; it must expose a (DbContextOptions<T>) constructor."));
    }

    // ---------------------------------------------------------------- helpers

    public static bool IsKernelIdMemberName(string memberName) =>
        KernelIdMemberSuffixes.Any(suffix => memberName.EndsWith(suffix, StringComparison.Ordinal));

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

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
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
}
