using System;
using System.IO;
using System.Linq;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Keeps the test tree one folder per project (#4793): each test runs in exactly one project, and a test that needs
/// PostgreSQL lives in the integration project, so <c>--filter "Category!=Integration"</c> needs no database.
/// </summary>
public class TestLayoutTests
{
    private static readonly string[] ProjectFolders =
    [
        "Cadence.UnitTests",
        "Cadence.IntegrationTests",
        "Cadence.ArchitectureTests",
        "Cadence.E2ETests",
        "Cadence.TestSupport",
    ];

    [Fact]
    public void EverySourceFile_BelongsToOneTestProjectFolder()
    {
        string tests = Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), "tests");

        var strays = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(tests, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(segment => segment is "bin" or "obj"))
            .Where(path => !ProjectFolders.Contains(path.Split('/')[0]))
            .ToList();

        strays.Should().BeEmpty("every file under tests/ belongs to the project whose folder it is in (docs/TESTING.md)");
    }

    [Fact]
    public void TestProjects_CompileOnlyTheirOwnFolder()
    {
        string tests = Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), "tests");

        foreach (string folder in ProjectFolders)
        {
            string[] projects = Directory.GetFiles(Path.Combine(tests, folder), "*.csproj");
            projects.Should().ContainSingle($"tests/{folder} holds one project");
            File.ReadAllText(projects[0]).Should().NotContain("<Compile Remove", "a project owns its folder, so it carves nothing out")
                .And.NotContain("<Compile Include", "shared helpers go in Cadence.TestSupport, not linked sources");
        }
    }

    [Fact]
    public void IntegrationTraits_OnlyInTheIntegrationProject()
    {
        string tests = Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), "tests");

        var misplaced = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(tests, path).Replace('\\', '/'))
            .Where(path => !path.StartsWith("Cadence.IntegrationTests/", StringComparison.Ordinal))
            .Where(path => !path.Split('/').Any(segment => segment is "bin" or "obj"))
            .Where(path => File.ReadAllText(Path.Combine(tests, path)).Contains("\"Category\", \"Integration\"", StringComparison.Ordinal))
            .ToList();

        misplaced.Should().BeEmpty("the integration project marks all of its tests Category=Integration; other projects run without a database");
    }
}
