using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Shared shrink-only ratchet baseline evaluator for architecture sentinels
/// (<c>direct-state-writes.baseline.txt</c>, <c>permission-matrix.baseline.txt</c>,
/// <c>cross-service-isolation.baseline.txt</c>). Uses <see cref="KernelBoundaryBaseline.UpdateEnvironmentVariable"/>
/// (<c>CADENCE_UPDATE_BASELINE=1</c>) to regenerate baseline files when paying down debt.
/// </summary>
public static class ShrinkOnlyBaseline
{
    private static readonly object Gate = new();

    /// <summary>Result of comparing actual entries against a recorded shrink-only baseline.</summary>
    public sealed record Comparison(
        string RelativePath,
        IReadOnlyList<string> NewViolations,
        IReadOnlyList<string> StaleEntries)
    {
        public bool IsClean => NewViolations.Count == 0 && StaleEntries.Count == 0;

        public string Describe(string ruleName, string filterHint, string? remediationHint = null)
        {
            var lines = new List<string>();
            if (NewViolations.Count > 0)
            {
                lines.Add($"{ruleName}: {NewViolations.Count} NEW violation(s) not in {RelativePath}:");
                lines.AddRange(NewViolations.Select(v => "  + " + v));
                if (!string.IsNullOrWhiteSpace(remediationHint))
                {
                    lines.Add($"Remediation: {remediationHint}");
                }
            }

            if (StaleEntries.Count > 0)
            {
                lines.Add($"{ruleName}: {StaleEntries.Count} baseline entr(y/ies) no longer occur. Delete these lines from {RelativePath}:");
                lines.AddRange(StaleEntries.Select(v => "  - " + v));
            }

            lines.Add(
                $"To drop paid-down baseline entries run: {KernelBoundaryBaseline.UpdateEnvironmentVariable}=1 " +
                $"dotnet test tests/Cadence.ArchitectureTests/Cadence.ArchitectureTests.csproj --filter \"FullyQualifiedName~{filterHint}\" " +
                $"(then review the diff of {RelativePath}).");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Pure comparison between actual entries and recorded baseline entries.</summary>
    public static Comparison Compare(string relativePath, IEnumerable<string> actual, IEnumerable<string> baseline)
    {
        var actualSet = new SortedSet<string>(actual, StringComparer.Ordinal);
        var baselineSet = new SortedSet<string>(baseline, StringComparer.Ordinal);

        return new Comparison(
            relativePath,
            [.. actualSet.Except(baselineSet, StringComparer.Ordinal)],
            [.. baselineSet.Except(actualSet, StringComparer.Ordinal)]);
    }

    /// <summary>Reads non-comment, non-empty lines from <paramref name="relativePath"/> under the repository root.</summary>
    public static IReadOnlyList<string> Read(string relativePath)
    {
        string path = Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), relativePath);
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
    }

    /// <summary>
    /// Verifies <paramref name="actual"/> against <paramref name="relativePath"/>, or rewrites the file when
    /// <see cref="KernelBoundaryBaseline.UpdateRequested"/> is enabled.
    /// </summary>
    public static Comparison Verify(
        string relativePath,
        IReadOnlyList<string> headerLines,
        IEnumerable<string> actual)
    {
        var sortedActual = actual
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (!KernelBoundaryBaseline.UpdateRequested)
        {
            return Compare(relativePath, sortedActual, Read(relativePath));
        }

        lock (Gate)
        {
            string fullPath = Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            List<string> nextEntries;
            if (File.Exists(fullPath))
            {
                var existing = Read(relativePath);
                var actualSet = sortedActual.ToHashSet(StringComparer.Ordinal);
                nextEntries = existing
                    .Where(actualSet.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();
            }
            else
            {
                nextEntries = sortedActual;
            }

            File.WriteAllText(fullPath, string.Join("\n", headerLines.Concat(nextEntries)) + "\n");
            return Compare(relativePath, sortedActual, Read(relativePath));
        }
    }
}
