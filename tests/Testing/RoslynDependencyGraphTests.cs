using System;
using System.Diagnostics;
using System.IO;

using FluentAssertions;
using Xunit;

using Cadence.Testing;
using Cadence.Testing.Graph;

namespace Cadence.UnitTests.Testing;

public class RoslynDependencyGraphTests
{
    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void TestDependencyGraph_ScansAndBuildsGraphFromRepoRoot()
    {
        string repoRoot = TestDependencyGraph.FindRepoRoot(AppContext.BaseDirectory);
        var graph = new TestDependencyGraph(repoRoot);

        var sw = Stopwatch.StartNew();
        graph.Build(force: true);
        sw.Stop();

        graph.ForwardGraph.Should().NotBeNull();
        graph.ReverseGraph.Should().NotBeNull();
        graph.FileMtimes.Should().NotBeEmpty();

        // Initial cold graph build across all repo files
        sw.ElapsedMilliseconds.Should().BeLessThan(10000);
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void TestDependencyGraph_ResolvesAffectedTestsInUnder200Milliseconds()
    {
        string repoRoot = TestDependencyGraph.FindRepoRoot(AppContext.BaseDirectory);
        var graph = new TestDependencyGraph(repoRoot);
        graph.LoadOrBuild();

        string sampleModifiedFile = "packages/engine_core/src/Rules/RuleEvaluator.cs";

        var sw = Stopwatch.StartNew();
        var affected = graph.ResolveAffectedTests(new[] { sampleModifiedFile });
        sw.Stop();

        affected.Should().NotBeNull();
        sw.ElapsedMilliseconds.Should().BeLessThan(200, "Roslyn static code analysis affected test resolution must execute in under 200 ms");
    }

    [Fact]
    [GxPRequirement("PRD-SYS-053")]
    public void TestDependencyGraph_SavesAndLoadsCacheFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "cadence_graph_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string repoRoot = TestDependencyGraph.FindRepoRoot(AppContext.BaseDirectory);
        string cacheFile = Path.Combine(tempDir, "test_graph.json");

        try
        {
            var graph = new TestDependencyGraph(repoRoot, cacheFile);
            graph.Build(force: true);

            File.Exists(cacheFile).Should().BeTrue("Cache file should be created");

            var loadedGraph = new TestDependencyGraph(repoRoot, cacheFile);
            loadedGraph.LoadOrBuild();

            loadedGraph.FileMtimes.Should().NotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
