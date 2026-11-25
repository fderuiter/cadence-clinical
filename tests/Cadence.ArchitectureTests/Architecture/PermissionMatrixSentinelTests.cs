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
/// matches the shrink-only per-resource shards under <see cref="PermissionMatrixRatchetService.BaselineDirectory"/>,
/// that no shadow static role-permission lookup table exists outside <see cref="PermissionMatrix"/>, and that any
/// unbaselined grant fails both the xUnit sentinel and the Native-AOT CLI gate.
/// </summary>
public class PermissionMatrixSentinelTests
{
    private const string RemediationGuidance =
        "Evaluate clinical authority and capability grants through the central kernel policy evaluator " +
        "(AuthorityEvaluator / Capability grants in the kernel command pipeline, CLINICAL_KERNEL_SPECIFICATION.md §4 " +
        "and ADR-2600) rather than adding static role-to-permission entries to PermissionMatrix.";

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

        var comparison = ShrinkOnlyBaseline.VerifySharded(
            PermissionMatrixRatchetService.BaselineDirectory,
            PermissionMatrixRatchetService.LegacyBaselineRelativePath,
            runtimeGrants,
            PermissionMatrixRatchetService.GetShardRelativePathForGrant,
            shardRelPath =>
            {
                _ = PermissionMatrixRatchetService.TryExtractResourceFromShardPath(shardRelPath, out string resource);
                return PermissionMatrixRatchetService.BuildShardHeader(resource);
            });

        comparison.IsClean.Should().BeTrue(
            comparison.Describe("PermissionMatrix", nameof(PermissionMatrixSentinelTests), RemediationGuidance));

        var baseline = ShrinkOnlyBaseline.ReadSharded(PermissionMatrixRatchetService.BaselineDirectory);
        baseline.Should().OnlyHaveUniqueItems();
        baseline.Order(StringComparer.Ordinal).Should().Equal(runtimeGrants);
        baseline.Should().OnlyContain(line => line.Split('|').Length == 3, "each line is role|resource|action");

        PermissionMatrixRatchetService.Check(repoRoot).Should().Be(0);
    }

    [Fact(DisplayName = "ShrinkOnlyBaseline_RejectsEmptyUnsortedAndLegacyShards_AndPrunesOnlyStaleEntries")]
    [Trait("Category", "Architecture")]
    public void ShrinkOnlyBaselineRejectsEmptyUnsortedAndLegacyShardsAndPrunesOnlyStaleEntries()
    {
        string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cadence-shrink-test-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempRoot);
        try
        {
            string shardDir = System.IO.Path.Combine(tempRoot, PermissionMatrixRatchetService.BaselineDirectory);
            System.IO.Directory.CreateDirectory(shardDir);

            // 1. Empty shard and unsorted shard must be rejected.
            System.IO.File.WriteAllText(System.IO.Path.Combine(shardDir, "empty_res.baseline.txt"), "# header\n");
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(shardDir, "tickets.baseline.txt"),
                "# header\nsysadmin|tickets|read\nadmin|tickets|read\n");
            string legacyPath = System.IO.Path.Combine(tempRoot, PermissionMatrixRatchetService.LegacyBaselineRelativePath);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(legacyPath)!);
            System.IO.File.WriteAllText(legacyPath, "admin|tickets|read\n");

            var invalidCheck = ShrinkOnlyBaseline.VerifySharded(
                PermissionMatrixRatchetService.BaselineDirectory,
                PermissionMatrixRatchetService.LegacyBaselineRelativePath,
                ["admin|tickets|read", "sysadmin|tickets|read"],
                PermissionMatrixRatchetService.GetShardRelativePathForGrant,
                shardRelPath =>
                {
                    _ = PermissionMatrixRatchetService.TryExtractResourceFromShardPath(shardRelPath, out string res);
                    return PermissionMatrixRatchetService.BuildShardHeader(res);
                },
                repoRoot: tempRoot,
                updateOverride: false);

            invalidCheck.IsClean.Should().BeFalse();
            invalidCheck.ValidationErrors.Should().HaveCount(3);

            // 2. Clean shards with stale entries + a new violation: updateOverride=true prunes stale, deletes empty shard, never writes new violation.
            System.IO.File.Delete(legacyPath);
            System.IO.File.Delete(System.IO.Path.Combine(shardDir, "empty_res.baseline.txt"));
            PermissionMatrixRatchetService.WriteBaselineShards(
                tempRoot,
                ["admin|obsolete_res|read", "admin|tickets|legacy", "admin|tickets|read"]);

            var prunedCheck = ShrinkOnlyBaseline.VerifySharded(
                PermissionMatrixRatchetService.BaselineDirectory,
                PermissionMatrixRatchetService.LegacyBaselineRelativePath,
                ["admin|tickets|read", "admin|unapproved_res|write"],
                PermissionMatrixRatchetService.GetShardRelativePathForGrant,
                shardRelPath =>
                {
                    _ = PermissionMatrixRatchetService.TryExtractResourceFromShardPath(shardRelPath, out string res);
                    return PermissionMatrixRatchetService.BuildShardHeader(res);
                },
                repoRoot: tempRoot,
                updateOverride: true);

            System.IO.File.Exists(System.IO.Path.Combine(shardDir, "obsolete_res.baseline.txt")).Should().BeFalse("empty shards must be deleted");
            System.IO.File.Exists(System.IO.Path.Combine(shardDir, "unapproved_res.baseline.txt")).Should().BeFalse("update mode must never add new shards");
            ShrinkOnlyBaseline.ReadSharded(PermissionMatrixRatchetService.BaselineDirectory, tempRoot)
                .Should().Equal("admin|tickets|read");
            prunedCheck.StaleEntries.Should().BeEmpty();
            prunedCheck.NewViolations.Should().Equal("admin|unapproved_res|write");
        }
        finally
        {
            if (System.IO.Directory.Exists(tempRoot))
            {
                System.IO.Directory.Delete(tempRoot, recursive: true);
            }
        }
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
            PermissionMatrixRatchetService.BaselineDirectory,
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
