using System;
using System.Linq;

using Cadence.Modules.Intelligence.Grounding;
using Cadence.Modules.Intelligence.Tools;

using FluentAssertions;

using Mono.Cecil;
using Mono.Cecil.Cil;

using Xunit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchLoader = ArchUnitNET.Loader.ArchLoader;
using ArchModel = ArchUnitNET.Domain.Architecture;
using ArchRuleExtensions = ArchUnitNET.xUnit.ArchRuleExtensions;
using IArchRule = ArchUnitNET.Fluent.IArchRule;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// ADR-2633 §4.3: agent tools only read. Nothing in the Intelligence module's <c>Tools</c> or <c>Mcp</c> namespaces can
/// reach the kernel command sender or the Proposal writer's ports; only <c>Grounding</c> proposes. The module
/// references the kernel's abstractions and module SDK only (K-XS-2).
/// </summary>
public class IntelligenceToolBoundaryTests
{
    private static readonly Lazy<ArchModel> Module = new(() => new ArchLoader().LoadAssemblies(typeof(AgentToolCatalog).Assembly).Build());

    private static readonly string[] WritePaths =
    [
        "Cadence.Kernel.Commands.IKernelCommandSender",
        "Cadence.Modules.Intelligence.Grounding.IAgentProposalGateway",
        "Cadence.Modules.Intelligence.Grounding.IPlanArtifactStore",
        "Cadence.Modules.Intelligence.Grounding.AgentProposalWriter",
    ];

    /// <summary>Validates that the tools and the MCP server depend on no write path.</summary>
    [Fact(DisplayName = "ADR_2633_AgentTools_CannotReachWritePaths")]
    [Trait("Category", "Architecture")]
    public void AgentToolsCannotReachWritePaths()
    {
        ArchRuleExtensions.Check(NoWrites(@"^Cadence\.Modules\.Intelligence\.(Tools|Mcp)(\..*)?$"), Module.Value);
        WriteCalls("Cadence.Modules.Intelligence.Tools").Should().BeEmpty("agent tools only read (ADR-2633)");
        WriteCalls("Cadence.Modules.Intelligence.Mcp").Should().BeEmpty("the MCP server publishes reads only (ADR-2633)");
    }

    /// <summary>The same checks find the Proposal writer's writes, so they are not vacuous.</summary>
    [Fact(DisplayName = "ADR_2633_Rules_BiteOnCodeThatProposes")]
    [Trait("Category", "Architecture")]
    public void RulesBiteOnCodeThatProposes()
    {
        NoWrites(@"^Cadence\.Modules\.Intelligence\.Grounding$").HasNoViolations(Module.Value).Should().BeFalse("the writer proposes");
        WriteCalls("Cadence.Modules.Intelligence.Grounding").Should().NotBeEmpty("the writer calls the proposal gateway inside its async method");
    }

    /// <summary>Validates K-XS-2 for the module: no kernel Domain, Application, Persistence or Api reference.</summary>
    [Fact(DisplayName = "K_XS_2_IntelligenceModule_ReferencesOnlyTheModuleSdk")]
    [Trait("Category", "Architecture")]
    public void IntelligenceModuleReferencesOnlyTheModuleSdk()
    {
        var references = typeof(AgentProposalWriter).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        references.Should().Contain("Cadence.Kernel.Abstractions");
        references.Should().NotContain(["Cadence.Kernel.Domain", "Cadence.Kernel.Application", "Cadence.Kernel.Persistence", "Cadence.Kernel.Api"]);
    }

    private static IArchRule NoWrites(string ns) =>
        Types().That().ResideInNamespaceMatching(ns)
            .Should().NotDependOnAny(Types().That().HaveFullName(WritePaths[0])
                .Or().HaveFullName(WritePaths[1])
                .Or().HaveFullName(WritePaths[2])
                .Or().HaveFullName(WritePaths[3]));

    /// <summary>
    /// Calls into a write path from types in <paramref name="ns"/> (and its sub-namespaces), read from the IL of every
    /// method, including the compiler's nested async state machines, which ArchUnitNET does not attribute to their
    /// declaring type.
    /// </summary>
    private static string[] WriteCalls(string ns)
    {
        using var module = ModuleDefinition.ReadModule(typeof(AgentToolCatalog).Assembly.Location);
        return [.. module.GetTypes()
            .Where(t => OuterNamespace(t) == ns || OuterNamespace(t).StartsWith(ns + ".", StringComparison.Ordinal))
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions
                .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Newobj) && i.Operand is MethodReference target
                    && WritePaths.Contains(target.DeclaringType.FullName, StringComparer.Ordinal))
                .Select(i => $"{m.FullName} -> {((MethodReference)i.Operand).FullName}"))];
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
