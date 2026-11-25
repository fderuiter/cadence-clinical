using System;
using System.Collections.Generic;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Canonical directory prefixes and retired legacy file paths for all seven sharded ratchet baseline gates.
/// Shared across <c>Cadence.ToolingCore</c>, <c>Cadence.Cli</c>, and <c>Cadence.ArchitectureTests</c> so gate
/// paths and legacy-to-shard multiset mappings stay synchronized in a single place.
/// </summary>
public static class BaselineGateCatalog
{
    public const string BaselinesRootPrefix = "baselines/";

    public const string KernelBoundariesGate =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries";

    public const string KernelRuleCoverageGate =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage";

    public const string PermissionMatrixGate = "baselines/permission-matrix";

    public const string DirectStateWritesGate = "baselines/direct-state-writes";

    public const string CrossServiceIsolationGate = "baselines/cross-service-isolation";

    public const string DeidGate = "baselines/deid";
    public const string DeidFilesDirectory = "baselines/deid/files";

    public const string SecretsGate = "baselines/secrets"; // pragma: allowlist secret
    public const string SecretsFilesDirectory = "baselines/secrets/files"; // pragma: allowlist secret

    public const string LegacyKernelBoundariesBaseline =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt";

    public const string LegacyKernelRuleCoverageBaseline =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-rule-coverage.baseline.txt";

    public const string LegacyPermissionMatrixBaseline =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/permission-matrix.baseline.txt";

    public const string LegacyDirectStateWritesBaseline =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/direct-state-writes.baseline.txt";

    public const string LegacyCrossServiceIsolationBaseline =
        "tests/Cadence.ArchitectureTests/ArchitectureBaselines/cross-service-isolation.baseline.txt";

    public const string LegacyDeidBaseline = ".deid-baseline.txt";

    public const string LegacySecretsBaseline = ".secrets.baseline"; // pragma: allowlist secret

    public static IReadOnlyList<string> AllShardedGateKeys { get; } =
    [
        KernelBoundariesGate,
        KernelRuleCoverageGate,
        PermissionMatrixGate,
        DirectStateWritesGate,
        CrossServiceIsolationGate,
        DeidGate,
        SecretsGate,
    ];

    /// <summary>
    /// Maps a normalized repository-relative baseline file or gate prefix to its canonical sharded gate key,
    /// or returns <see langword="null"/> when <paramref name="normalizedPath"/> is a standalone baseline file.
    /// </summary>
    public static string? TryResolveShardedGateKey(string normalizedPath)
    {
        if (MatchesGate(normalizedPath, KernelBoundariesGate, LegacyKernelBoundariesBaseline))
        {
            return KernelBoundariesGate;
        }

        if (MatchesGate(normalizedPath, KernelRuleCoverageGate, LegacyKernelRuleCoverageBaseline))
        {
            return KernelRuleCoverageGate;
        }

        if (MatchesGate(normalizedPath, PermissionMatrixGate, LegacyPermissionMatrixBaseline))
        {
            return PermissionMatrixGate;
        }

        if (MatchesGate(normalizedPath, DirectStateWritesGate, LegacyDirectStateWritesBaseline))
        {
            return DirectStateWritesGate;
        }

        if (MatchesGate(normalizedPath, CrossServiceIsolationGate, LegacyCrossServiceIsolationBaseline))
        {
            return CrossServiceIsolationGate;
        }

        if (MatchesGate(normalizedPath, DeidGate, LegacyDeidBaseline))
        {
            return DeidGate;
        }

        if (MatchesGate(normalizedPath, SecretsGate, LegacySecretsBaseline))
        {
            return SecretsGate;
        }

        return null;
    }

    public static bool IsShardedGateKey(string normalizedPath) =>
        normalizedPath is
            KernelBoundariesGate or
            KernelRuleCoverageGate or
            PermissionMatrixGate or
            DirectStateWritesGate or
            CrossServiceIsolationGate or
            DeidGate or
            SecretsGate;

    private static bool MatchesGate(string normalizedPath, string gateDirectory, string legacyBaselinePath) =>
        normalizedPath.Equals(legacyBaselinePath, StringComparison.Ordinal) ||
        normalizedPath.Equals(gateDirectory, StringComparison.Ordinal) ||
        normalizedPath.StartsWith(gateDirectory + "/", StringComparison.Ordinal);
}
