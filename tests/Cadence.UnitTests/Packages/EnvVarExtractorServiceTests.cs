using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Cadence.Cli.Commands;
using Cadence.Cli.Services;
using Cadence.ToolingCore.Services;
using Cadence.Topology;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Packages;

public class EnvVarExtractorServiceTests
{
    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode0_WhenRepoIsSynchronized()
    {
        string repoRoot = QualityGateOrchestratorFindRepoRoot();
        int exitCode = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: repoRoot);
        exitCode.Should().Be(0);
    }

    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode1_WhenCodeVarIsMissingFromEnvExample()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "apps", "test"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "test", "Service.cs"),
                "var x = Environment.GetEnvironmentVariable(\"MISSING_ENV_VAR_ABC\");");

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "EXISTING_ENV_VAR=default\n");

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| `EXISTING_ENV_VAR` | description | default |");

            int exitCode = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            exitCode.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode1_WhenEnvExampleVarIsMissingFromDocs()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "apps", "test"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "test", "Service.cs"),
                "var x = Environment.GetEnvironmentVariable(\"VAR_ONE\");");

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "VAR_ONE=value1\nVAR_TWO=value2\n");

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| `VAR_ONE` | description | default |");

            int exitCode = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            exitCode.Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode1_WhenDocConcreteVarIsMissingFromEnvExample()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "apps", "test"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "test", "Service.cs"),
                "var x = Environment.GetEnvironmentVariable(\"VAR_ONE\");");

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "VAR_ONE=value1\n");

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| Key | Read by | Default |\n| --- | --- | --- |\n| `VAR_ONE` | `apps/test/Service.cs` | value1 |\n| `RANDOMIZATION_ALLOCATION_KEY` | `apps/test/Service.cs` | none |\n");

            EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir).Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAndValidateEnvVars_ReturnsExitCode1_WhenDocCitesDeletedRepositoryPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "apps", "test"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "test", "Service.cs"),
                "var x = Environment.GetEnvironmentVariable(\"GH_TOKEN\");");

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "GH_TOKEN=\n");

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| Key | Read by | Default |\n| --- | --- | --- |\n| `GH_TOKEN` | `packages/tooling_core/src/Services/SyncGithubProjectService.cs:10` | none |\n");

            EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir).Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAndValidateEnvVars_ExtractsIConfigurationKeysAndConstants()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "apps", "execution"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "execution", "AllocationProtectionOptions.cs"),
                """
                namespace Demo;
                public sealed class AllocationProtectionOptions
                {
                    public const string ConfigurationKey = "RANDOMIZATION_ALLOCATION_KEY";
                }
                """);

            File.WriteAllText(
                Path.Combine(tempDir, "apps", "execution", "Program.cs"),
                """
                var authority = builder.Configuration["OIDC_AUTHORITY"];
                var httpsMeta = builder.Configuration.GetValue("OIDC_REQUIRE_HTTPS_METADATA", true);
                var key = builder.Configuration[AllocationProtectionOptions.ConfigurationKey];
                """);

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "OIDC_AUTHORITY=http://localhost:8080/realms/cadence\nOIDC_REQUIRE_HTTPS_METADATA=false\n");

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| Key | Description |\n| --- | --- |\n| `OIDC_AUTHORITY` | a |\n| `OIDC_REQUIRE_HTTPS_METADATA` | b |\n| `RANDOMIZATION_ALLOCATION_KEY` | c |\n");

            int missingConstResult = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            missingConstResult.Should().Be(1);

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                "OIDC_AUTHORITY=http://localhost:8080/realms/cadence\nOIDC_REQUIRE_HTTPS_METADATA=false\nRANDOMIZATION_ALLOCATION_KEY=placeholder\n");

            int synchronizedResult = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            synchronizedResult.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ExtractAndValidateEnvVars_IgnoresPathAndMatchesEnvExampleKeysExactly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "packages", "tooling"));
            Directory.CreateDirectory(Path.Combine(tempDir, "docs"));

            File.WriteAllText(
                Path.Combine(tempDir, "packages", "tooling", "Launcher.cs"),
                """
                var path = Environment.GetEnvironmentVariable("PATH");
                var custom = Environment.GetEnvironmentVariable("CUSTOM_KEY");
                """);

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                """
                CADENCE_ENV=development
                MY_CUSTOM_KEY_EXTRA=1
                """);

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| Key | Description |\n| --- | --- |\n| `CADENCE_ENV` | a |\n| `MY_CUSTOM_KEY_EXTRA` | b |\n");

            int substringOnlyResult = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            substringOnlyResult.Should().Be(1);

            File.WriteAllText(
                Path.Combine(tempDir, ".env.example"),
                """
                CADENCE_ENV=development
                CUSTOM_KEY=1
                """);

            File.WriteAllText(
                Path.Combine(tempDir, "docs", "ENVIRONMENT_VARIABLES.md"),
                "| Key | Description |\n| --- | --- |\n| `CADENCE_ENV` | a |\n| `CUSTOM_KEY` | b |\n");

            int exactMatchResult = EnvVarExtractorService.ExtractAndValidateEnvVars(checkOnly: true, repoRoot: tempDir);
            exactMatchResult.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateScaffoldServiceStartInfo_PassesServiceNameAsPositionalFirstArgument()
    {
        var psi = ScaffoldCommands.CreateScaffoldServiceStartInfo("clinical_billing");

        psi.FileName.Should().Be("bash");
        psi.ArgumentList.Should().Equal("scripts/migrate_service_wizard.sh", "clinical_billing");
        psi.ArgumentList.Should().NotContain("-ServiceName");
    }

    [Fact]
    public void AgentSkills_UseValidPnpmCadenceCliSyntaxAndPostgres18()
    {
        string repoRoot = QualityGateOrchestratorFindRepoRoot();
        string agentsDir = Path.Combine(repoRoot, ".agents");
        var skillFiles = Directory.EnumerateFiles(agentsDir, "SKILL.md", SearchOption.AllDirectories).OrderBy(p => p).ToList();
        skillFiles.Should().NotBeEmpty();

        var rootCommand = Cadence.Cli.Program.BuildRootCommand();
        int parsedCommandCount = 0;

        foreach (string skillFile in skillFiles)
        {
            string relativePath = Path.GetRelativePath(repoRoot, skillFile);
            string content = File.ReadAllText(skillFile);

            content.Should().NotContain("dotnet cadence", $"skill {relativePath} must invoke the CLI via pnpm cadence");
            content.Should().NotContain("PostgreSQL 16", $"skill {relativePath} must reference PostgreSQL 18");

            foreach (string rawTail in ExtractSkillCadenceInvocations(content))
            {
                string[] args = TokenizeShellWords(rawTail);
                var parseResult = rootCommand.Parse(args);
                parseResult.Errors.Should().BeEmpty($"command 'pnpm cadence {rawTail}' in {relativePath} must parse cleanly");

                if (args.Length >= 2 && string.Equals(args[0], "check", StringComparison.Ordinal) && !args[1].StartsWith('-'))
                {
                    QualityGates.Find(args[1]).Should().NotBeNull($"gate '{args[1]}' in {relativePath} must exist in QualityGates");
                }

                parsedCommandCount++;
            }
        }

        parsedCommandCount.Should().BeGreaterThan(0);
    }

    private static IEnumerable<string> ExtractSkillCadenceInvocations(string markdown)
    {
        var snippets = new List<string>();

        foreach (Match m in Regex.Matches(markdown, @"CommandLine=""([^""\r\n]+)"""))
        {
            snippets.Add(m.Groups[1].Value);
        }

        foreach (Match m in Regex.Matches(markdown, @"`([^`\r\n]+)`"))
        {
            string span = m.Groups[1].Value;
            if (!span.Contains("CommandLine=\"", StringComparison.Ordinal))
            {
                snippets.Add(span);
            }
        }

        foreach (Match m in Regex.Matches(markdown, @"```(?:bash|sh)\r?\n([\s\S]*?)```"))
        {
            foreach (string line in m.Groups[1].Value.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                {
                    snippets.Add(trimmed);
                }
            }
        }

        foreach (string snippet in snippets)
        {
            foreach (string part in snippet.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int idx = part.IndexOf("pnpm cadence ", StringComparison.Ordinal);
                if (idx < 0)
                {
                    continue;
                }

                string tail = part[(idx + "pnpm cadence ".Length)..].Trim();
                if (string.IsNullOrWhiteSpace(tail) || tail.Contains("...", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return tail;
            }
        }
    }

    private static string[] TokenizeShellWords(string commandTail)
    {
        var tokens = new List<string>();
        var tokenRegex = new Regex(@"""([^""]*)""|'([^']*)'|(\S+)");
        foreach (Match match in tokenRegex.Matches(commandTail))
        {
            if (match.Groups[1].Success)
            {
                tokens.Add(match.Groups[1].Value);
            }
            else if (match.Groups[2].Success)
            {
                tokens.Add(match.Groups[2].Value);
            }
            else
            {
                tokens.Add(match.Groups[3].Value);
            }
        }

        return [.. tokens];
    }

    private static string QualityGateOrchestratorFindRepoRoot() => RepositoryRoot.Find(AppContext.BaseDirectory);
}
