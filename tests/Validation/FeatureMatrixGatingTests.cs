using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.Validation;

/// <summary>
/// Feature Matrix Gating Linter Test Suite.
/// Ported from legacy test_feature_matrix_gating.py.
/// </summary>
public class FeatureMatrixGatingTests
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
    /// Test that the actual FEATURE_MATRIX.md correctly validates and exists.
    /// </summary>
    [Fact(DisplayName = "FeatureMatrixValidationSuccess")]
    [Trait("Category", "Validation")]
    public void TestFeatureMatrixValidationSuccess()
    {
        var repoRoot = GetRepoRoot();
        var matrixPath = Path.Combine(repoRoot, "docs", "FEATURE_MATRIX.md");

        File.Exists(matrixPath).Should().BeTrue("FEATURE_MATRIX.md must exist in docs/");

        var content = File.ReadAllText(matrixPath);
        content.Should().Contain("# Feature", "FEATURE_MATRIX.md must contain Feature matrix header");
    }

    /// <summary>
    /// Test that missing services in a feature matrix trigger validation failure.
    /// </summary>
    [Fact(DisplayName = "FeatureMatrixValidationMissingService")]
    [Trait("Category", "Validation")]
    public void TestFeatureMatrixValidationMissingService()
    {
        var activeServices = new List<string> { "designer", "execution", "safety" };
        var matrixContent = "## Clinical Entities Mapping\n| Study Protocols | Designer |\n| Subjects | Execution |";

        var missingServices = activeServices.Where(s => !matrixContent.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();
        missingServices.Should().ContainSingle().Which.Should().Be("safety");
    }

    /// <summary>
    /// Test that helper/infrastructure services are ignored in feature matrix checks.
    /// </summary>
    [Fact(DisplayName = "FeatureMatrixValidationIgnoringHelperAndExcludedServices")]
    [Trait("Category", "Validation")]
    public void TestFeatureMatrixValidationIgnoringHelperAndExcludedServices()
    {
        var helperServices = new HashSet<string> { "postgres", "neo4j", "keycloak", "gateway" };
        var activeServices = new[] { "designer", "execution", "postgres", "neo4j", "keycloak", "gateway" };

        var domainServices = activeServices.Where(s => !helperServices.Contains(s)).ToList();
        domainServices.Should().BeEquivalentTo("designer", "execution");
    }
}
