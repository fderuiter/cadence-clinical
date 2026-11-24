using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Enforces the direct state-write ratchet: no assembly outside the central kernel command pipeline may add a new
/// <c>SaveChanges</c> or <c>SaveChangesAsync</c> call (direct, async state machine, closure lambda, or reflection
/// lookup) beyond the shrink-only baseline at <see cref="DirectStateWriteRules.BaselineRelativePath"/>.
/// </summary>
public class DirectStateWriteSentinelTests
{
    private const string RemediationGuidance =
        "Route clinical and module mutations through the central kernel command pipeline via IKernelCommandSender " +
        "and ICommandHandler<TCommand, TResult> (Cadence.Kernel.Abstractions / Cadence.Kernel.Modules, ADR-2600; " +
        "CLINICAL_KERNEL_SPECIFICATION.md §11) rather than calling SaveChanges or SaveChangesAsync directly.";

    private static readonly Lazy<IReadOnlyList<Assembly>> AllProductionAssemblies =
        new(KernelBoundaryRules.LoadAllProductionCadenceAssemblies);

    [Fact(DisplayName = "DirectStateWrites_OutsideKernelCommandPipeline_MustNotExceedShrinkOnlyBaseline")]
    [Trait("Category", "Architecture")]
    public void DirectStateWritesOutsideKernelCommandPipelineMustNotExceedShrinkOnlyBaseline()
    {
        AllProductionAssemblies.Value.Should().NotBeEmpty();
        AllProductionAssemblies.Value.Select(a => a.GetName().Name).Should().Contain(
            ["Cadence.Modules.Execution", "Cadence.Kernel.Host", "Cadence.Kernel.Persistence", "cadence"],
            "100% of production assemblies including Execution module, Kernel.Host, and CLI must be scanned");

        var actual = DirectStateWriteRules.EvaluateAssemblies(AllProductionAssemblies.Value);

        // Central kernel command pipeline types are exempted, never recorded as legacy debt.
        actual.Should().NotContain(e => e.Contains("KernelCommandTransaction", StringComparison.Ordinal));

        var comparison = ShrinkOnlyBaseline.Verify(
            DirectStateWriteRules.BaselineRelativePath,
            DirectStateWriteRules.BaselineHeader,
            actual);

        comparison.IsClean.Should().BeTrue(
            comparison.Describe("DirectStateWrites", nameof(DirectStateWriteSentinelTests), RemediationGuidance));

        var recorded = ShrinkOnlyBaseline.Read(DirectStateWriteRules.BaselineRelativePath);
        recorded.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
    }

    [Fact(DisplayName = "DirectStateWrites_Bites_OnSyntheticSyncAsyncClosureAndReflectionSaveChanges")]
    [Trait("Category", "Architecture")]
    public void DirectStateWritesBitesOnSyntheticSyncAsyncClosureAndReflectionSaveChanges()
    {
        var syntheticSites = DirectStateWriteRules.EvaluateAssemblies(
            [typeof(DirectStateWriteSentinelTests).Assembly],
            outerType => outerType.FullName == typeof(SyntheticDirectWriteBypassFixture).FullName);

        const string prefix =
            "Cadence.ArchitectureTests|Cadence.ArchitectureTests.Architecture.SyntheticDirectWriteBypassFixture.";

        syntheticSites.Should().Equal(
            prefix + "RegisterClosureEndpoints->DbContext.SaveChangesAsync",
            prefix + "RegisterClosureEndpoints->DbContext.SaveChangesAsync#2",
            prefix + "SaveSynchronously->DbContext.SaveChanges",
            prefix + "SaveViaAsyncStateMachineAsync->DbContext.SaveChangesAsync",
            prefix + "SaveViaReflectionLookup->Reflection.SaveChangesAsync");

        var ratchet = ShrinkOnlyBaseline.Compare(
            DirectStateWriteRules.BaselineRelativePath,
            syntheticSites,
            ShrinkOnlyBaseline.Read(DirectStateWriteRules.BaselineRelativePath));

        ratchet.IsClean.Should().BeFalse("unbaselined SaveChanges* calls must fail verification");
        ratchet.NewViolations.Should().Equal(syntheticSites);
        ratchet.Describe("DirectStateWrites", nameof(DirectStateWriteSentinelTests), RemediationGuidance)
            .Should().Contain("IKernelCommandSender")
            .And.Contain("ICommandHandler");
    }
}

/// <summary>
/// Synthetic unbaselined direct state-write fixture proving <see cref="DirectStateWriteRules"/> catches synchronous
/// calls, compiler-generated async state machines, Minimal-API-style closure lambdas (including multi-call counts),
/// and reflection <c>GetMethod("SaveChangesAsync")</c> lookups.
/// </summary>
internal static class SyntheticDirectWriteBypassFixture
{
    public static int SaveSynchronously(DbContext db) => db.SaveChanges();

    public static async Task<int> SaveViaAsyncStateMachineAsync(DbContext db, CancellationToken ct) =>
        await db.SaveChangesAsync(ct);

    public static Func<DbContext, CancellationToken, Task>[] RegisterClosureEndpoints() =>
    [
        async (db, ct) => await db.SaveChangesAsync(ct),
        async (db, ct) => await db.SaveChangesAsync(ct),
    ];

    public static MethodInfo? SaveViaReflectionLookup(Type contextType) =>
        contextType.GetMethod("SaveChangesAsync", [typeof(CancellationToken)]);
}
