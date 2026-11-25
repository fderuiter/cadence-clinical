using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Cadence.ToolingCore.Services;

using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Compiled-IL scanner for direct <c>SaveChanges</c> and <c>SaveChangesAsync</c> calls outside the central kernel
/// command pipeline. Uses <see cref="Mono.Cecil"/> so calls inside async state machines (<c>&lt;Method&gt;d__N</c>),
/// closures (<c>&lt;&gt;c__DisplayClass...</c>), local functions, and reflection <c>GetMethod("SaveChanges*")</c>
/// lookups are attributed back to their logical outer type and method.
/// </summary>
public static class DirectStateWriteRules
{
    public const string BaselineDirectory = BaselineGateCatalog.DirectStateWritesGate;

    public const string LegacyBaselineRelativePath = BaselineGateCatalog.LegacyDirectStateWritesBaseline;

    private const string ShardSuffix = TextBaselineShardStore.ShardSuffix;

    public static IReadOnlyList<string> BuildShardHeader(string assemblyName) =>
    [
        $"# Direct state-write ratchet baseline shard for {assemblyName} (SaveChanges / SaveChangesAsync outside the central kernel command pipeline).",
        "# One compiled IL call site per line: Assembly|OuterType.LogicalMethod->TargetMethod[#N]. Sorted ordinally. The list may only shrink.",
        $"# Prune resolved entries with {KernelBoundaryBaseline.UpdateEnvironmentVariable}=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj.",
    ];

    public static string GetShardRelativePathForEntry(string entry)
    {
        int pipeIdx = entry.IndexOf('|', StringComparison.Ordinal);
        if (pipeIdx <= 0)
        {
            throw new FormatException($"Direct state-write entry '{entry}' is missing its 'Assembly|' prefix.");
        }

        string assemblyName = entry[..pipeIdx];
        if (!assemblyName.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
        {
            throw new FormatException($"Direct state-write entry '{entry}' has invalid assembly token '{assemblyName}'.");
        }

        return $"{BaselineDirectory}/{assemblyName}{ShardSuffix}";
    }

    public static IReadOnlyList<string> BuildShardHeaderFromShardPath(string shardRelativePath)
    {
        string fileName = Path.GetFileName(shardRelativePath);
        string assemblyName = fileName.EndsWith(ShardSuffix, StringComparison.Ordinal)
            ? fileName[..^ShardSuffix.Length]
            : fileName;
        return BuildShardHeader(assemblyName);
    }

    /// <summary>
    /// Exact kernel persistence types permitted to invoke <c>DbContext.SaveChanges*</c> under
    /// <c>KernelDbContext.PermitPipelineWrites()</c> (K-TX-2: the command transaction, outbox relay, and derived
    /// activity projection).
    /// </summary>
    private static readonly HashSet<string> PermittedKernelPipelineTypes = new(StringComparer.Ordinal)
    {
        "Cadence.Kernel.Persistence.Commands.KernelCommandTransaction",
        "Cadence.Kernel.Persistence.Outbox.KernelOutboxRelay",
        "Cadence.Kernel.Persistence.Activity.ActivityProjection",
    };

    /// <summary>
    /// Base-call forwarding overrides on kernel/module DbContext base types that guard <c>SaveChanges*</c> and
    /// delegate to <c>base.SaveChanges*</c>.
    /// </summary>
    private static readonly HashSet<string> PermittedBaseOverrideTypes = new(StringComparer.Ordinal)
    {
        "Cadence.Kernel.Persistence.KernelDbContext",
        "Cadence.Kernel.Modules.ModuleDbContext",
    };

    /// <summary>Scans compiled IL across <paramref name="assemblies"/> for unexempted <c>SaveChanges*</c> writes.</summary>
    public static IReadOnlyList<string> EvaluateAssemblies(
        IEnumerable<Assembly> assemblies,
        Func<TypeDefinition, bool>? typeFilter = null)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var assembly in assemblies.OrderBy(a => a.GetName().Name, StringComparer.Ordinal))
        {
            string assemblyName = assembly.GetName().Name!;
            using var module = ModuleDefinition.ReadModule(assembly.Location);

            foreach (var type in module.GetTypes())
            {
                var outerType = UnwrapOuterType(type);
                if (typeFilter is not null && !typeFilter(outerType))
                {
                    continue;
                }

                string outerTypeName = outerType.FullName.Replace('/', '.');
                if (PermittedKernelPipelineTypes.Contains(outerTypeName))
                {
                    continue;
                }

                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    string logicalMethod = ResolveLogicalMethodName(type, method);

                    foreach (var instruction in method.Body.Instructions)
                    {
                        if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
                            instruction.Operand is MethodReference target &&
                            target.Name is "SaveChanges" or "SaveChangesAsync")
                        {
                            if (IsPermittedBaseForwardingCall(type, method, instruction, target))
                            {
                                continue;
                            }

                            string targetDeclaring = NormalizeDeclaringTypeName(target.DeclaringType);
                            string siteKey = $"{assemblyName}|{outerTypeName}.{logicalMethod}->{targetDeclaring}.{target.Name}";
                            counts[siteKey] = counts.GetValueOrDefault(siteKey) + 1;
                        }
                        else if (instruction.OpCode == OpCodes.Ldstr &&
                                 instruction.Operand is string literal &&
                                 literal is "SaveChanges" or "SaveChangesAsync")
                        {
                            string siteKey = $"{assemblyName}|{outerTypeName}.{logicalMethod}->Reflection.{literal}";
                            counts[siteKey] = counts.GetValueOrDefault(siteKey) + 1;
                        }
                    }
                }
            }
        }

        var entries = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (siteKey, count) in counts)
        {
            entries.Add(siteKey);
            for (int occurrence = 2; occurrence <= count; occurrence++)
            {
                entries.Add($"{siteKey}#{occurrence}");
            }
        }

        return [.. entries];
    }

    public static TypeDefinition UnwrapOuterType(TypeDefinition type)
    {
        var current = type;
        while (current.DeclaringType is not null && IsCompilerGeneratedType(current))
        {
            current = current.DeclaringType;
        }

        return current;
    }

    public static string ResolveLogicalMethodName(TypeDefinition declaringType, MethodDefinition method)
    {
        // Walk from innermost compiler-generated type/method outward to find the user-authored enclosing method name.
        string? candidate = TryExtractGeneratedOwnerMethodName(method.Name);
        var current = declaringType;
        while (current is not null && IsCompilerGeneratedType(current))
        {
            string? fromType = TryExtractGeneratedOwnerMethodName(current.Name);
            if (!string.IsNullOrEmpty(fromType))
            {
                candidate = fromType;
            }

            current = current.DeclaringType;
        }

        string resolved = candidate ?? method.Name;
        return resolved == "<Main>$" ? "Main" : resolved;
    }

    private static string? TryExtractGeneratedOwnerMethodName(string rawName)
    {
        if (rawName.StartsWith("<<Main>$>", StringComparison.Ordinal) || rawName == "<Main>$")
        {
            return "Main";
        }

        int start = 0;
        while (start < rawName.Length && rawName[start] == '<')
        {
            start++;
        }

        if (start == 0 || start >= rawName.Length)
        {
            return null;
        }

        int end = rawName.IndexOf('>', start);
        if (end <= start)
        {
            return null;
        }

        return rawName[start..end];
    }

    private static bool IsCompilerGeneratedType(TypeDefinition type) =>
        type.Name.Contains('<', StringComparison.Ordinal) ||
        type.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    private static bool IsPermittedBaseForwardingCall(
        TypeDefinition type,
        MethodDefinition method,
        Instruction instruction,
        MethodReference target)
    {
        string typeFullName = type.FullName.Replace('/', '.');
        return PermittedBaseOverrideTypes.Contains(typeFullName)
               && instruction.OpCode == OpCodes.Call
               && string.Equals(method.Name, target.Name, StringComparison.Ordinal);
    }

    private static string NormalizeDeclaringTypeName(TypeReference declaringType)
    {
        string name = declaringType.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }
}
