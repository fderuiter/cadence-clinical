using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Ratchet baseline for the kernel boundary rules. Pre-existing violations are recorded, one
/// <c>RULE|Assembly|Type[.Member]</c> per line (ordinal sort, LF line endings), in
/// <c>tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt</c>. A rule check fails when it finds a
/// violation not in the baseline (new debt) and when a baseline line no longer occurs (debt paid down but
/// not removed), so the file can only shrink.
///
/// Regenerate: <c>CADENCE_UPDATE_BASELINE=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --filter "FullyQualifiedName~KernelBoundaryTests"</c>
/// rewrites the file from the current violations. Only do that to delete paid-down lines; adding lines is
/// accepting new kernel-boundary debt, and the <c>baseline-ratchet</c> gate (<c>pnpm cadence check baseline-ratchet</c>)
/// fails it unless a commit on the branch carries a <c>Baseline-Growth: &lt;reason&gt;</c> trailer (docs/TESTING.md).
/// </summary>
public static class KernelBoundaryBaseline
{
    public const string UpdateEnvironmentVariable = "CADENCE_UPDATE_BASELINE";

    public const string RelativePath = "tests/Cadence.ArchitectureTests/ArchitectureBaselines/kernel-boundaries.baseline.txt";

    private const string RegenerateHint =
        $"To regenerate the baseline run: {UpdateEnvironmentVariable}=1 dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj " +
        "--filter \"FullyQualifiedName~KernelBoundaryTests\" (then review the diff of " + RelativePath + ").";

    private static readonly string[] Header =
    [
        "# Kernel boundary ratchet baseline (K-XS-1, K-MOD-1) - docs/kernel/CLINICAL_KERNEL_SPECIFICATION.md §2, §9.",
        "# One pre-existing violation per line: RULE|Assembly|Type[.Member]. Sorted ordinally. The list may only shrink.",
        $"# Regenerate with {UpdateEnvironmentVariable}=1 (see tests/Cadence.ArchitectureTests/Architecture/KernelBoundaryBaseline.cs).",
    ];

    private static readonly System.Threading.Lock Gate = new();

    public static bool UpdateRequested =>
        string.Equals(Environment.GetEnvironmentVariable(UpdateEnvironmentVariable), "1", StringComparison.Ordinal);

    /// <summary>Result of comparing actual violations to the recorded baseline for one rule.</summary>
    public sealed record Comparison(IReadOnlyList<string> NewViolations, IReadOnlyList<string> StaleEntries)
    {
        public bool IsClean => NewViolations.Count == 0 && StaleEntries.Count == 0;

        public string Describe(string rule)
        {
            var lines = new List<string>();
            if (NewViolations.Count > 0)
            {
                lines.Add($"{rule}: {NewViolations.Count} NEW violation(s) not in {RelativePath}. Use kernel ID types / kernel concepts instead:");
                lines.AddRange(NewViolations.Select(v => "  + " + v));
            }

            if (StaleEntries.Count > 0)
            {
                lines.Add($"{rule}: {StaleEntries.Count} baseline entr(y/ies) no longer occur. Delete these lines from {RelativePath}:");
                lines.AddRange(StaleEntries.Select(v => "  - " + v));
            }

            lines.Add(RegenerateHint);
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Pure ratchet comparison: what is new, and what in the baseline has disappeared.</summary>
    public static Comparison Compare(IEnumerable<string> actual, IEnumerable<string> baseline)
    {
        var actualSet = new SortedSet<string>(actual, StringComparer.Ordinal);
        var baselineSet = new SortedSet<string>(baseline, StringComparer.Ordinal);

        return new Comparison(
            [.. actualSet.Except(baselineSet, StringComparer.Ordinal)],
            [.. baselineSet.Except(actualSet, StringComparer.Ordinal)]);
    }

    /// <summary>Baseline entries recorded for <paramref name="rule"/>.</summary>
    public static IReadOnlyList<string> Read(string rule)
    {
        var path = FullPath();
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Where(line => line.StartsWith(rule + "|", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Compares <paramref name="actual"/> with the baseline for <paramref name="rule"/>; when
    /// <see cref="UpdateRequested"/> is set, rewrites that rule's lines instead and reports clean.
    /// </summary>
    public static Comparison Verify(string rule, IReadOnlyList<string> actual)
    {
        if (!UpdateRequested)
        {
            return Compare(actual, Read(rule));
        }

        lock (Gate)
        {
            var path = FullPath();
            var otherRules = File.Exists(path)
                ? File.ReadAllLines(path)
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith('#'))
                    .Where(line => !line.StartsWith(rule + "|", StringComparison.Ordinal))
                : [];

            var entries = otherRules.Concat(actual).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join("\n", Header.Concat(entries)) + "\n");
        }

        return new Comparison([], []);
    }

    /// <summary>Walks up from the test output directory to the directory containing Cadence.sln.</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cadence.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate Cadence.sln above " + AppContext.BaseDirectory);
    }

    private static string FullPath() => Path.Combine(RepositoryRoot(), RelativePath);
}
