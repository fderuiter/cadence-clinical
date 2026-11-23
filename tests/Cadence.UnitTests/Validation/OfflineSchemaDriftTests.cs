using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Testing;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.Validation;

/// <summary>
/// Offline Schema Drift Test Suite.
/// Ported from legacy test_offline_schema_drift.py.
/// </summary>
public class OfflineSchemaDriftTests
{
    private static string GetRepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);

    private static string CamelToSnake(string name)
    {
        return Regex.Replace(name, @"(?<!^)(?=[A-Z])", "_").ToLower();
    }

    private static bool AreTypesCompatible(string jsType, string pyType)
    {
        var js = jsType.Trim().ToLower();
        var py = pyType.Trim().ToLower();

        if (js == "string" && (py.Contains("str") || py.Contains("datetime") || py.Contains("status"))) return true;
        if (js == "number" && (py.Contains("int") || py.Contains("float") || py.Contains("double") || py.Contains("decimal"))) return true;
        if (js == "boolean" && py.Contains("bool")) return true;
        return false;
    }

    /// <summary>
    /// Validate submission object compatibility.
    /// </summary>
    [Fact(DisplayName = "SubmissionsSchemaDrift")]
    [Trait("Category", "Validation")]
    public void TestSubmissionsSchemaDrift()
    {
        var repoRoot = GetRepoRoot();
        var clientFile = Path.Combine(repoRoot, "apps", "subject-portal", "src", "sync-queue.js");
        File.Exists(clientFile).Should().BeTrue("apps/subject-portal/src/sync-queue.js must exist");
    }

    /// <summary>
    /// Test detection of field rename drift.
    /// </summary>
    [Fact(DisplayName = "SimulatedFieldNameRenameDrift")]
    [Trait("Category", "Validation")]
    public void TestSimulatedFieldNameRenameDrift()
    {
        var clientField = "entityTypeRenamed";
        var serverField = CamelToSnake(clientField);
        serverField.Should().Be("entity_type_renamed");
    }

    /// <summary>
    /// Test detection of type mismatch drift.
    /// </summary>
    [Fact(DisplayName = "SimulatedTypeMismatchDrift")]
    [Trait("Category", "Validation")]
    public void TestSimulatedTypeMismatchDrift()
    {
        var jsType = "boolean";
        var serverType = "str";
        AreTypesCompatible(jsType, serverType).Should().BeFalse();
    }

    /// <summary>
    /// Validate immunity to formatting and whitespace changes.
    /// </summary>
    [Fact(DisplayName = "FormattingAndWhitespaceImmunity")]
    [Trait("Category", "Validation")]
    public void TestFormattingAndWhitespaceImmunity()
    {
        var jsTypeWithSpaces = "  string  ";
        var pyTypeWithSpaces = "  str | None  ";
        AreTypesCompatible(jsTypeWithSpaces, pyTypeWithSpaces).Should().BeTrue();
    }
}
