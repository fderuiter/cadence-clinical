using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Cadence.Cli.Services;
using Cadence.Security.Rbac;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Verifies that the static RBAC role-to-permission grant inventory (<see cref="PermissionMatrix.ROLE_PERMISSIONS"/>)
/// matches the shrink-only baseline at <see cref="PermissionMatrixRatchetService.BaselineRelativePath"/>, that no
/// shadow static role-permission lookup table exists outside <see cref="PermissionMatrix"/>, and that any unbaselined
/// grant fails both the xUnit sentinel and the Native-AOT CLI gate.
/// </summary>
public class PermissionMatrixSentinelTests
{
    private const string RemediationGuidance =
        "Evaluate clinical authority and capability grants through the central kernel policy evaluator " +
        "(AuthorityEvaluator / Capability grants in the kernel command pipeline, CLINICAL_KERNEL_SPECIFICATION.md §4 " +
        "and ADR-2600) rather than adding static role-to-permission entries to PermissionMatrix.";

    private static readonly IReadOnlyList<string> BaselineHeader =
    [
        "# Static RBAC role-to-permission grant inventory baseline (PermissionMatrix.ROLE_PERMISSIONS).",
        "# One canonical grant per line: role|resource|action. Sorted ordinally. Unreviewed privilege additions are rejected.",
        $"# Regenerate with {KernelBoundaryBaseline.UpdateEnvironmentVariable}=1 (see tests/Cadence.ArchitectureTests/Architecture/PermissionMatrixSentinelTests.cs).",
    ];

    [Fact(DisplayName = "PermissionMatrix_RoleToPermissionGrants_MustMatchShrinkOnlyBaseline")]
    [Trait("Category", "Architecture")]
    public void PermissionMatrixRoleToPermissionGrantsMustMatchShrinkOnlyBaseline()
    {
        var runtimeGrants = PermissionMatrixRatchetService.GetRuntimeGrants();
        runtimeGrants.Should().NotBeEmpty();

        string repoRoot = KernelBoundaryBaseline.RepositoryRoot();
        var inventoried = PermissionMatrixRatchetService.InventoryGrants(repoRoot);
        inventoried.Should().Equal(
            runtimeGrants,
            "source Grant(...) declarations across packages/security/src/Rbac and apps/ must match compiled PermissionMatrix.ROLE_PERMISSIONS");

        var comparison = ShrinkOnlyBaseline.Verify(
            PermissionMatrixRatchetService.BaselineRelativePath,
            BaselineHeader,
            runtimeGrants);

        comparison.IsClean.Should().BeTrue(
            comparison.Describe("PermissionMatrix", nameof(PermissionMatrixSentinelTests), RemediationGuidance));

        var baseline = ShrinkOnlyBaseline.Read(PermissionMatrixRatchetService.BaselineRelativePath);
        baseline.Should().BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
        baseline.Should().OnlyContain(line => line.Split('|').Length == 3, "each line is role|resource|action");
    }

    [Fact(DisplayName = "ProductionAssemblies_MustNotDeclareShadowRolePermissionMatrices")]
    [Trait("Category", "Architecture")]
    public void ProductionAssembliesMustNotDeclareShadowRolePermissionMatrices()
    {
        var shadowFields = KernelBoundaryRules.LoadAllProductionCadenceAssemblies()
            .SelectMany(KernelBoundaryRules.GetLoadableTypes)
            .Where(t => t != typeof(PermissionMatrix))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(IsShadowRolePermissionField)
                .Select(f => $"{t.FullName}.{f.Name}"))
            .ToList();

        shadowFields.Should().BeEmpty(
            "all static role-to-permission dictionaries must live in Cadence.Security.Rbac.PermissionMatrix so the permission-matrix ratchet inventories 100% of grants");
    }

    [Fact(DisplayName = "PermissionMatrix_Bites_OnSyntheticUnbaselinedGrant")]
    [Trait("Category", "Architecture")]
    public void PermissionMatrixBitesOnSyntheticUnbaselinedGrant()
    {
        const string syntheticSource = """
            namespace Cadence.Security.Rbac;
            public static partial class PermissionMatrix
            {
                private static void AddShadowGrants()
                {
                    Grant(RoleNormalization.ROLE_CRC, "unblinded_arm_assignment", "read", "export");
                    Grant("shadow_admin", "audit_trail", "delete");
                }
            }
            """;

        var extracted = PermissionMatrixRatchetService.ExtractDirectSourceGrants(syntheticSource);
        extracted.Should().Equal(
            "crc|unblinded_arm_assignment|export",
            "crc|unblinded_arm_assignment|read",
            "shadow_admin|audit_trail|delete");

        var baseline = PermissionMatrixRatchetService.GetRuntimeGrants();
        var (added, stale) = PermissionMatrixRatchetService.Compare(baseline.Concat(extracted), baseline);
        added.Should().Equal(extracted);
        stale.Should().BeEmpty();

        var comparison = ShrinkOnlyBaseline.Compare(
            PermissionMatrixRatchetService.BaselineRelativePath,
            baseline.Concat(extracted),
            baseline);
        comparison.IsClean.Should().BeFalse();
        comparison.Describe("PermissionMatrix", nameof(PermissionMatrixSentinelTests), RemediationGuidance)
            .Should().Contain("AuthorityEvaluator");
    }

    private static bool IsShadowRolePermissionField(FieldInfo field)
    {
        var type = field.FieldType;
        if (!type.IsGenericType)
        {
            return false;
        }

        var def = type.GetGenericTypeDefinition();
        if (def != typeof(Dictionary<,>) && def != typeof(IReadOnlyDictionary<,>) && def != typeof(IDictionary<,>))
        {
            return false;
        }

        var args = type.GetGenericArguments();
        if (args[0] != typeof(string) || !args[1].IsGenericType)
        {
            return false;
        }

        var innerDef = args[1].GetGenericTypeDefinition();
        if (innerDef == typeof(Dictionary<,>) || innerDef == typeof(IReadOnlyDictionary<,>) || innerDef == typeof(IDictionary<,>))
        {
            return args[1].GetGenericArguments()[0] == typeof(string);
        }

        return args[1].GetGenericArguments().FirstOrDefault() == typeof(string) &&
               (field.Name.Contains("Permission", StringComparison.OrdinalIgnoreCase) ||
                field.Name.Contains("Grant", StringComparison.OrdinalIgnoreCase));
    }
}
