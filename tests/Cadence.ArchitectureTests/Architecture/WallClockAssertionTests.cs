using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Unit tests assert behaviour, never how long the machine took (#5415). A latency ceiling measured with a stopwatch
/// passes or fails with the load on the machine: one failed local CI while a build ran beside it, and passed alone.
/// </summary>
public partial class WallClockAssertionTests
{
    [Fact]
    public void UnitTests_AssertNoWallClockLatency()
    {
        string root = KernelBoundaryBaseline.RepositoryRoot();
        string unitTests = Path.Combine(root, "tests", "Cadence.UnitTests");

        var offenders = Directory.EnumerateFiles(unitTests, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => WallClockAssertions(File.ReadAllText(path))
                .Select(line => $"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{line}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a unit test asserts what the code does; elapsed time belongs to timeouts and polling deadlines (docs/TESTING.md)");
    }

    /// <summary>The same rule for the frontend's Vitest suites (#5478): a duration measured with <c>performance.now()</c>
    /// or <c>Date.now()</c> is never compared against a ceiling.</summary>
    [Fact]
    public void FrontendTests_AssertNoWallClockLatency()
    {
        string root = KernelBoundaryBaseline.RepositoryRoot();
        string[] suites = ["apps/web/tests", "apps/web/src", "apps/subject-portal/tests", "apps/subject-portal/src", "packages/ui/src"];
        string[] extensions = [".js", ".ts", ".mjs", ".vue"];

        var offenders = suites
            .Select(suite => Path.Combine(root, suite))
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.Ordinal) && !IsNodeModules(path))
            .SelectMany(path => FrontendWallClockAssertions(File.ReadAllText(path))
                .Select(line => $"{Path.GetRelativePath(root, path).Replace('\\', '/')}:{line}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a frontend test asserts what the code does; elapsed time belongs to test timeouts (docs/TESTING.md)");
    }

    [Theory]
    [InlineData("expect(duration).toBeLessThan(100);", 1)]
    [InlineData("expect(elapsedMs).toBeLessThanOrEqual(500);", 1)]
    [InlineData("expect(performance.now() - start).toBeLessThan(50);", 1)]
    [InlineData("expect(latency).toBeLessThan(20)", 1)]
    [InlineData("expect(result.length).toBeLessThan(3);", 0)]
    [InlineData("expect(queued.length).toBe(0);", 0)]
    public void FrontendWallClockAssertions_FindsLatencyCeilings(string source, int expected)
    {
        FrontendWallClockAssertions(source).Should().HaveCount(expected);
    }

    internal static IEnumerable<int> FrontendWallClockAssertions(string source)
    {
        string[] lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (FrontendWallClockAssertionRegex().IsMatch(lines[i]))
            {
                yield return i + 1;
            }
        }
    }

    private static bool IsNodeModules(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [Theory]
    [InlineData("sw.ElapsedMilliseconds.Should().BeLessThan(100);", 1)]
    [InlineData("stopwatch.Elapsed.TotalSeconds.Should().BeLessThan(5.0);", 1)]
    [InlineData("elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));", 1)]
    [InlineData("averageLatencyMs.Should().BeLessThan(20.0);", 1)]
    [InlineData("result.Count.Should().BeLessThan(3);", 0)]
    [InlineData("while (stopwatch.Elapsed < deadline) { }", 0)]
    public void WallClockAssertions_FindsLatencyCeilings(string source, int expected)
    {
        WallClockAssertions(source).Should().HaveCount(expected);
    }

    internal static IEnumerable<int> WallClockAssertions(string source)
    {
        string[] lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (WallClockAssertionRegex().IsMatch(lines[i]))
            {
                yield return i + 1;
            }
        }
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    [GeneratedRegex(@"(?i)(elapsed|latency)\w*(\.\w+)*\.Should\(\)\.BeLess")]
    private static partial Regex WallClockAssertionRegex();

    [GeneratedRegex(@"(?i)expect\(.*?(duration|elapsed|latency|performance\.now|Date\.now).*?\)\.toBeLess")]
    private static partial Regex FrontendWallClockAssertionRegex();
}
