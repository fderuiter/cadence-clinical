using System;
using System.Linq;
using System.Reflection;

using Cadence.Modules.Intelligence;

using FluentAssertions;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// ADR-2636 §4.4: the inference and sandbox code reaches the network only through <c>IHttpClientFactory</c> clients,
/// where the sandbox egress handler and the provider endpoint pins apply. It opens no socket, builds no
/// <c>HttpClient</c> or handler of its own, starts no process and touches no file. Read from the IL of every method,
/// including compiler-generated async state machines.
/// </summary>
public class InferenceSandboxBoundaryTests
{
    private const string GuardedNamespaces = "Cadence.Modules.Intelligence.Inference|Cadence.Modules.Intelligence.Sandbox";

    /// <summary>Validates that inference and sandbox code uses no network, process or file API outside the client factory.</summary>
    [Fact(DisplayName = "ADR_2636_InferenceAndSandbox_UseOnlyFactoryClients")]
    [Trait("Category", "Architecture")]
    public void InferenceAndSandboxUseOnlyFactoryClients() =>
        ForbiddenCalls(typeof(InferenceServiceCollectionExtensions).Assembly, GuardedNamespaces)
            .Should().BeEmpty("agent runs reach the network only through the egress-checked client factory (ADR-2636)");

    /// <summary>The same scan finds such calls in code that does make them, so it is not vacuous.</summary>
    [Fact(DisplayName = "ADR_2636_Scan_BitesOnCodeThatStartsProcessesAndWritesFiles")]
    [Trait("Category", "Architecture")]
    public void ScanBitesOnCodeThatStartsProcessesAndWritesFiles() =>
        ForbiddenCalls(typeof(Cadence.Cli.Program).Assembly, "Cadence.Cli")
            .Should().NotBeEmpty("the developer CLI starts processes and writes files");

    private static string[] ForbiddenCalls(Assembly assembly, string namespaces)
    {
        var guarded = namespaces.Split('|');
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        return [.. module.GetTypes()
            .Where(t => guarded.Any(ns => OuterNamespace(t) == ns || OuterNamespace(t).StartsWith(ns + ".", StringComparison.Ordinal)))
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions
                .Where(i => i.Operand is MethodReference target && IsForbidden(i.OpCode, target))
                .Select(i => $"{m.FullName} -> {((MethodReference)i.Operand).FullName}"))];
    }

    private static bool IsForbidden(OpCode opCode, MethodReference target)
    {
        var declaring = target.DeclaringType.FullName;
        if (declaring.StartsWith("System.Net.Sockets.", StringComparison.Ordinal)
            || declaring is "System.Diagnostics.Process" or "System.IO.File" or "System.IO.Directory" or "System.IO.FileStream" or "System.IO.FileInfo")
        {
            return true;
        }

        return opCode == OpCodes.Newobj
            && declaring is "System.Net.Http.HttpClient" or "System.Net.Http.HttpClientHandler" or "System.Net.Http.SocketsHttpHandler";
    }

    private static string OuterNamespace(TypeDefinition type)
    {
        while (type.DeclaringType is not null)
        {
            type = type.DeclaringType;
        }

        return type.Namespace;
    }
}
