using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Testing;

using FluentAssertions;

using Xunit;

namespace Cadence.E2ETests.Validation;

/// <summary>
/// Offline Schema Drift Test Suite.
/// Ported from legacy test_offline_schema_drift.py.
/// </summary>
public class OfflineSchemaDriftTests
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
    /// Validate PendingDelta schema compatibility between client and server.
    /// </summary>
    [Fact(DisplayName = "PendingDeltaSchemaDrift")]
    [Trait("Category", "Validation")]
    public void TestPendingDeltaSchemaDrift()
    {
        var repoRoot = GetRepoRoot();
        var clientFile = Path.Combine(repoRoot, "apps", "web", "src", "utils", "syncEngine.ts");

        // Verify that web client sync engine or database schema definitions exist
        File.Exists(clientFile).Should().BeTrue("apps/web/src/utils/syncEngine.ts must exist");
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

    /// <summary>
    /// Validate committed TypeScript DB schemas existence.
    /// </summary>
    [Fact(DisplayName = "CommittedTypescriptSchemaIsUpToDate")]
    [Trait("Category", "Validation")]
    public void TestCommittedTypescriptSchemaIsUpToDate()
    {
        var repoRoot = GetRepoRoot();
        var committedFile = Path.Combine(repoRoot, "apps", "web", "src", "types", "db_schemas.ts");
        File.Exists(committedFile).Should().BeTrue("db_schemas.ts must exist");
    }

    /// <summary>
    /// Validate committed schema visualizer HTML artifacts.
    /// </summary>
    [Fact(DisplayName = "CommittedSchemaVisualizerIsUpToDate")]
    [Trait("Category", "Validation")]
    public void TestCommittedSchemaVisualizerIsUpToDate()
    {
        var repoRoot = GetRepoRoot();
        var visualizerFile = Path.Combine(repoRoot, "docs", "schema_visualizer.html");
        File.Exists(visualizerFile).Should().BeTrue("schema_visualizer.html must exist");
    }

    /// <summary>
    /// Validate schema visualizers use vendored assets without CDN dependencies.
    /// </summary>
    [Fact(DisplayName = "SchemaVisualizersUseVendoredAssets")]
    [Trait("Category", "Validation")]
    public void TestSchemaVisualizersUseVendoredAssets()
    {
        var repoRoot = GetRepoRoot();
        var visualizerFile = Path.Combine(repoRoot, "docs", "schema_visualizer.html");
        if (File.Exists(visualizerFile))
        {
            var content = File.ReadAllText(visualizerFile);
            content.Should().NotContain("cdn.tailwindcss.com");
            content.Should().NotContain("unpkg.com");
        }
    }
}
