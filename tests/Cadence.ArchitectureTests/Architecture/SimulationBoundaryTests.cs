using System;
using System.Linq;

using Cadence.Kernel.Application;
using Cadence.Kernel.Persistence;

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
/// K-SIM-1 (ADR-2480, spec §11.1): simulation writes no clinical state. The engine cannot reach the command sender,
/// the command pipeline, a unit of work or kernel persistence, and its persistence reads never save.
/// </summary>
public class SimulationBoundaryTests
{
    private static readonly Lazy<ArchModel> Kernel = new(() => new ArchLoader()
        .LoadAssemblies(typeof(KernelApplicationAssembly).Assembly, typeof(KernelPersistenceServiceCollectionExtensions).Assembly)
        .Build());

    private static readonly string[] WritePaths =
    [
        "Cadence.Kernel.Commands.IKernelCommandSender",
        "Cadence.Kernel.Application.Commands.IKernelUnitOfWork",
        "Cadence.Kernel.Application.Commands.IKernelUnitOfWorkFactory",
        "Cadence.Kernel.Application.Commands.KernelCommandPipeline",
    ];

    /// <summary>Validates that the simulation engine depends on no write path and no persistence or EF Core type.</summary>
    [Fact(DisplayName = "K_SIM_1_SimulationEngine_CannotReachWritePaths")]
    [Trait("Category", "Architecture")]
    public void KSim1SimulationEngineCannotReachWritePaths() =>
        ArchRuleExtensions.Check(EngineRule(@"^Cadence\.Kernel\.Application\.Simulation$"), Kernel.Value);

    /// <summary>Validates that simulation's persistence reads reach no write path and call no <c>SaveChanges</c>.</summary>
    [Fact(DisplayName = "K_SIM_1_SimulationInputs_NeverSave")]
    [Trait("Category", "Architecture")]
    public void KSim1SimulationInputsNeverSave()
    {
        ArchRuleExtensions.Check(InputsRule(@"^Cadence\.Kernel\.Persistence\.Simulation$"), Kernel.Value);
        SaveCalls("Cadence.Kernel.Persistence.Simulation").Should().BeEmpty("simulation's reads never save (K-SIM-1)");
    }

    /// <summary>The same rules fail on code that does write, so they are not vacuous.</summary>
    [Fact(DisplayName = "K_SIM_1_Rules_BiteOnCodeThatWrites")]
    [Trait("Category", "Architecture")]
    public void KSim1RulesBiteOnCodeThatWrites()
    {
        EngineRule(@"^Cadence\.Kernel\.Application\.Protocol$").HasNoViolations(Kernel.Value).Should().BeFalse(
            "the protocol command handlers use the unit of work");
        InputsRule(@"^Cadence\.Kernel\.Persistence\.Commands$").HasNoViolations(Kernel.Value).Should().BeFalse(
            "the command transaction is the unit of work");
        SaveCalls("Cadence.Kernel.Persistence.Activity").Should().NotBeEmpty("the activity projector saves its entries");
    }

    private static IArchRule EngineRule(string ns) =>
        Types().That().ResideInNamespaceMatching(ns)
            .Should().NotDependOnAny(Types().That().HaveFullName(WritePaths[0])
                .Or().HaveFullName(WritePaths[1])
                .Or().HaveFullName(WritePaths[2])
                .Or().HaveFullName(WritePaths[3])
                .Or().ResideInNamespaceMatching(@"^Cadence\.Kernel\.Persistence(\..*)?$")
                .Or().ResideInNamespaceMatching(@"^Microsoft\.EntityFrameworkCore(\..*)?$"));

    private static IArchRule InputsRule(string ns) =>
        Types().That().ResideInNamespaceMatching(ns)
            .Should().NotDependOnAny(Types().That().HaveFullName(WritePaths[0])
                .Or().HaveFullName(WritePaths[1])
                .Or().HaveFullName(WritePaths[2])
                .Or().HaveFullName(WritePaths[3]));

    /// <summary>
    /// Calls to a <c>SaveChanges</c> method from types in <paramref name="ns"/>, read from the IL of every method,
    /// including the compiler's nested async state machines (structs in Release builds), which ArchUnitNET does not
    /// attribute to their declaring type.
    /// </summary>
    private static string[] SaveCalls(string ns)
    {
        using var module = ModuleDefinition.ReadModule(typeof(KernelPersistenceServiceCollectionExtensions).Assembly.Location);
        return [.. module.GetTypes()
            .Where(t => OuterNamespace(t) == ns)
            .SelectMany(t => t.Methods)
            .Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions
                .Where(i => (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) && i.Operand is MethodReference target
                    && target.Name.StartsWith("SaveChanges", StringComparison.Ordinal))
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
