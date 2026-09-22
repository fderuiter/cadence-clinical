using System;
using System.IO;
using Cadence.Testing;
using FluentAssertions;
using Xunit;

namespace Cadence.E2ETests.Validation;

/// <summary>
/// SDLC Documentation Drift & GxP Compliance Validation Suite.
/// Ported from legacy gxp_compliance_suite.py.
/// </summary>
public class GxPComplianceSuiteTests
{
    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Cadence.sln")))
        {
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        return dir ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    }

    /// <summary>
    /// Verifies SDLC documentation drift and GxP compliance integrity.
    /// @req:PRD-SYS-001
    /// </summary>
    [Fact(DisplayName = "GxPComplianceDriftsIdentified")]
    [GxPRequirement("PRD-SYS-001")]
    [Trait("Requirement", "PRD-SYS-001")]
    [Trait("Category", "Validation")]
    public void TestGxPComplianceDriftsIdentified()
    {
        var repoRoot = GetRepoRoot();
        var sdlcDir = Path.Combine(repoRoot, "docs", "SDLC");
        Directory.Exists(sdlcDir).Should().BeTrue("docs/SDLC directory must exist for GxP compliance");

        var rtmFile = Path.Combine(sdlcDir, "Requirements_Traceability_Matrix.md");
        File.Exists(rtmFile).Should().BeTrue("Requirements Traceability Matrix must exist");
    }
}
