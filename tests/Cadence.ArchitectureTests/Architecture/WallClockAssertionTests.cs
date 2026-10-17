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
}
