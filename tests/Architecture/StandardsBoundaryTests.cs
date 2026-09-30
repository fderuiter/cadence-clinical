using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Cadence.Standards;

using FluentAssertions;

using Xunit;

namespace Cadence.ArchitectureTests.Architecture;

/// <summary>
/// Enforces the Standards Registry boundaries (spec §2.4, §6.3): the standards assembly depends on the kernel's
/// public contracts only, and standard dataset and variable names exist only as registry data, never in kernel or
/// standards code (K-PROJ-1).
/// </summary>
public partial class StandardsBoundaryTests
{
    private static readonly Assembly Standards = typeof(StandardSeeds).Assembly;

    /// <summary>
    /// Validates that Cadence.Standards references Cadence.Kernel.Abstractions and no other Cadence assembly, and no
    /// EF Core, ASP.NET Core or Npgsql assembly, so any host or application can load standards content.
    /// </summary>
    [Fact(DisplayName = "Standards_ReferencesKernelAbstractionsOnly")]
    [Trait("Category", "Architecture")]
    public void StandardsReferencesKernelAbstractionsOnly()
    {
        var references = Standards.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        references.Where(r => r.StartsWith("Cadence.", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Be("Cadence.Kernel.Abstractions");
        references.Should().NotContain(r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || r.StartsWith("Npgsql", StringComparison.Ordinal));
    }

    /// <summary>
    /// Validates K-PROJ-1: no identifier or string literal in kernel or standards source code equals the name of a
    /// dataset or variable the shipped standards define. Those names are registry data; code that needs one reads it
    /// from a registered standard version or a mapping specification.
    /// </summary>
    [Fact(DisplayName = "KernelAndStandardsSource_NamesNoStandardDatasetOrVariable")]
    [Trait("Category", "Architecture")]
    public void KernelAndStandardsSourceNamesNoStandardDatasetOrVariable()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in StandardSeeds.LoadAll())
        {
            foreach (var dataset in seed.Package.Datasets)
            {
                names.Add(dataset.Name);
                names.UnionWith(dataset.Variables.Select(v => v.Name));
            }
        }

        names.Should().Contain("USUBJID", "the check must see the shipped seeds");

        var root = Path.Combine(KernelBoundaryBaseline.RepositoryRoot(), "packages", "kernel", "src");
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            var code = Comments().Replace(File.ReadAllText(file), " ");
            violations.AddRange(Tokens().Matches(code)
                .Select(m => m.Groups["literal"].Success ? m.Groups["literal"].Value : m.Value)
                .Where(names.Contains)
                .Distinct(StringComparer.Ordinal)
                .Select(name => $"{relative}: {name}"));
        }

        violations.Should().BeEmpty("standard dataset and variable names are data, not code (K-PROJ-1)");
    }

    [GeneratedRegex(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex("\"(?<literal>[^\"\\\\\\n]*)\"|\\b[A-Za-z_][A-Za-z0-9_]*\\b")]
    private static partial Regex Tokens();
}
